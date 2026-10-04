# ביצועים – מדידה וניתוח על 100,000 פניות

כל המספרים במסמך נמדדו בפועל על 100,000 הפניות שנוצרות בהרצה הראשונה (או ב-`dotnet run -- seed`),
על SQL Server LocalDB 2019 במחשב פיתוח, עם EF Core 8.

סקריפט המדידה: [`performance/measure.sql`](performance/measure.sql) – אפשר להריץ אותו שוב:

```bash
sqlcmd -S "(localdb)\MSSQLLocalDB" -d RequestsManagement -E -f 65001 -i docs/performance/measure.sql
```

## שיטה

* **ה-SQL הוא בדיוק מה ש-EF Core 8 שולח.** הוא נלקח מלוג הפקודות (`Microsoft.EntityFrameworkCore.Database.Command=Information`) ורץ דרך `sp_executesql` עם פרמטרים, כך שתוכנית הביצוע זהה לאפליקציה.
* **מדד ראשי – Logical reads** (`SET STATISTICS IO`): לא תלוי בעומס על המחשב. בנוסף CPU/Elapsed (`SET STATISTICS TIME`) ותוכנית ביצוע בפועל (Actual Execution Plan, `SET STATISTICS XML`) – כולל מספר השורות וה-reads בכל אופרטור. התוכניות עצמן שמורות ב-[`performance/plans`](performance/plans), וראו [תוכניות ביצוע בפועל](#תוכניות-ביצוע-בפועל) למטה.
* **עם אינדקס מול בלי:** כל שאילתה רצה גם עם `WITH (INDEX(1))`, שמכריח סריקה של כל הטבלה – כך רואים מה כל אינדקס חוסך.
* **מקצה לקצה:** זמן תגובה של ה-API (ממוצע ו-p95 של 20 קריאות, אחרי קריאת חימום).

## הפעולה הראשונה: `GET /api/requests` – רשימה עם סינון, מיון ודפדוף

כל קריאה מריצה שתי שאילתות: `COUNT(*)` עם אותם סינונים (בשביל מספר העמודים) ו-`ORDER BY … OFFSET … FETCH` לעמוד אחד.
הטבלה כולה היא 1,999 דפים; כל מספר קטן מזה הוא קריאה חלקית.

| # | תרחיש | שאילתה | תוכנית בפועל | Reads | בלי אינדקס | זמן |
|---|---|---|---|---|---|---|
| Q1 | ללא סינון, מיון לפי תאריך | עמוד | סריקה **לאחור** של `IX_Requests_CreatedAt` – נעצרת אחרי 20 שורות, 20 Key Lookups | 46 | 1,999 + מיון 100K שורות | ‎3ms מול 162ms |
| | | COUNT | סריקת האינדקס הצר ביותר | 213 | | 15ms |
| Q2 | סטטוס חדשה או ממתינה | COUNT | **Seek** על `IX_Requests_Status_CreatedAt` – שני טווחים, 30,156 שורות | **104** | 1,999 | ‎8ms |
| | | עמוד | סריקה לאחור על `IX_Requests_CreatedAt`, 105 שורות עד שנמצאו 20 | 288 | | ‎2ms |
| Q5 | מטפל מכיל "לוי" + בטיפול, מיון לפי עדיפות | COUNT | סריקת `IX_Requests_AssignedTo_Status` (אינדקס צר) | 370 | 1,999 | 30ms |
| | | עמוד | סריקה מלאה + מיון 606 שורות | 1,999 | | 40ms |
| Q6 | ארגון מתחיל ב"נגב" + רבעון ראשון 2026 | COUNT | **חיתוך אינדקסים**: Seek על `IX_Requests_OrganizationName` + Seek על `IX_Requests_CreatedAt` + Hash Join | **63** | 1,999 | 13ms |
| | | עמוד | Seek על טווח התאריכים, 320 שורות עד שנמצאו 20 | 991 | | ‎1ms |
| Q3 | עמוד 500 של "חדשה" | עמוד | האופטימייזר מוותר על האינדקס: סריקה מלאה + מיון 20,025 שורות | 2,004 | | **40ms** |
| Q4 | חיפוש טקסט "היתר" | COUNT | סריקה מלאה – אין אינדקס שעוזר ל-`LIKE '%x%'` | 2,004 | 1,999 | **~350ms CPU** |
| | | עמוד | סריקה לאחור על `IX_Requests_CreatedAt`, 337 שורות עד שנמצאו 20 | 954 | | ‎2ms |

**זמני תגובה של ה-API** (20 קריאות):

| קריאה | ממוצע | p95 |
|---|---|---|
| עמוד 1 ללא סינון | 22ms | 35ms |
| סטטוס חדשה + ממתינה | 13ms | 16ms |
| ארגון + טווח תאריכים | 18ms | 21ms |
| מטפל (מכיל) + סטטוס, מיון לפי עדיפות | 76ms | 85ms |
| עמוד 500 | 48ms | 57ms |
| חיפוש טקסט | **363ms** | 391ms |

## הפעולה השנייה: `GET /api/requests/summary` – הנתונים המסכמים

שתי שאילתות: `GROUP BY (Status, Priority)` עם ספירת הפניות הוותיקות והעדכון האחרון (עד 12 שורות), ו-Top 5 מטפלים.

| # | שאילתה | תוכנית בפועל | Reads | בלי אינדקס | זמן |
|---|---|---|---|---|---|
| Q7 | קבוצות סטטוס × עדיפות | סריקה של `IX_Requests_Status_CreatedAt` בלבד (Priority ו-UpdatedAt ב-INCLUDE) + Hash Aggregate | **328** | 1,999 | 46ms |
| Q8 | Top 5 מטפלים | Seek על `IX_Requests_AssignedTo_Status` (‏AssignedTo IS NOT NULL), 43,086 שורות, Stream Aggregate בלי מיון | **392** | 1,999 | 17ms |

| קריאה | ממוצע | p95 |
|---|---|---|
| ללא סינון – **מה-Cache** | **2ms** | 3ms |
| עדיפות = גבוהה (מחושב) | 89ms | 105ms |
| חיפוש טקסט (מחושב) | 524ms | 546ms |

השאילתות האלה עוברות על כל הפניות התואמות מטבען (זו אגרגציה). האינדקסים מקטינים את ה-IO פי 5–6, וה-Cache חוסך את החישוב לגמרי בתצוגה הנפוצה ביותר.

## עדכון סטטוס

`UPDATE … WHERE Id = @id AND RowVersion = @expected` + הוספת שורת היסטוריה: **3 + 7 + 4 logical reads**, ‏~1ms. בדיקת הגרסה מתבצעת בתוך ה-UPDATE עצמו: בתוכנית רואים Seek על `Id` ובדיקת `RowVersion` על אותה שורה (Q9 למטה).

## תוכניות ביצוע בפועל

התוכניות נשמרו עם `SET STATISTICS XML ON` (Actual Execution Plan) על 100,000 הפניות, אחרי ריצת חימום, עם ה-SQL והפרמטרים של EF Core 8 מ-[`measure.sql`](performance/measure.sql).
קובצי `.sqlplan` נפתחים ב-SSMS או ב-Azure Data Studio (גרירה לחלון), ומציגים לכל אופרטור את השורות בפועל מול ההערכה ואת ה-logical reads.

| # | שאילתה | קובץ |
|---|---|---|
| Q1 | עמוד ראשון ללא סינון | [`q1-list-page.sqlplan`](performance/plans/q1-list-page.sqlplan) |
| Q1 | `COUNT(*)` ללא סינון | [`q1-count.sqlplan`](performance/plans/q1-count.sqlplan) |
| Q2 | סינון סטטוס – COUNT | [`q2-status-count.sqlplan`](performance/plans/q2-status-count.sqlplan) |
| Q2 | סינון סטטוס – עמוד | [`q2-status-page.sqlplan`](performance/plans/q2-status-page.sqlplan) |
| Q3 | עמוד 500 (דפדוף עמוק) | [`q3-deep-page.sqlplan`](performance/plans/q3-deep-page.sqlplan) |
| Q4 | חיפוש טקסט – COUNT | [`q4-search-count.sqlplan`](performance/plans/q4-search-count.sqlplan) |
| Q4 | חיפוש טקסט – עמוד | [`q4-search-page.sqlplan`](performance/plans/q4-search-page.sqlplan) |
| Q7 | Summary – קבוצות סטטוס × עדיפות | [`q7-summary-buckets.sqlplan`](performance/plans/q7-summary-buckets.sqlplan) |
| Q8 | Summary – Top 5 מטפלים | [`q8-top-assignees.sqlplan`](performance/plans/q8-top-assignees.sqlplan) |
| Q9 | UPDATE מותנה בגרסה | [`q9-conditional-update.sqlplan`](performance/plans/q9-conditional-update.sqlplan) |
| Q9 | הוספת שורת היסטוריה | [`q9-audit-insert.sqlplan`](performance/plans/q9-audit-insert.sqlplan) |

תקציר העץ של כל תוכנית (אופרטור | שורות בפועל (הערכה) | reads):

```text
Q1 עמוד ראשון – 0ms. נעצר אחרי 20 שורות; אין מיון.
Top                                                    20
  Nested Loops                                         20
    Index Scan  IX_Requests_CreatedAt  BACKWARD        20 (est 20)     reads 2
    Clustered Index Seek  PK_Requests  (Key Lookup)    20              reads 44

Q1 COUNT – 14ms. סורק את האינדקס הצר ביותר, לא את הטבלה.
Stream Aggregate                                       1
  Index Scan  IX_Requests_CreatedAt                    100,000         reads 213

Q2 COUNT סטטוס חדשה או ממתינה – 8ms. שני טווחי Seek (Merge Interval על שני הפרמטרים).
Stream Aggregate                                       1
  Nested Loops
    Merge Interval ← Concatenation ← 2 × Constant Scan 2
    Index Seek  IX_Requests_Status_CreatedAt           30,156 (est 30,173)  reads 104

Q2 עמוד – 0ms. סריקה לאחור לפי תאריך ובדיקת הסטטוס בכל שורה.
Top                                                    20
  Nested Loops
    Index Scan  IX_Requests_CreatedAt  BACKWARD        105 (est 66)    reads 3
    Clustered Index Seek  PK_Requests                  20              reads 285

Q3 עמוד 500 – 46ms. ל-OFFSET 9,980 האופטימייזר מעדיף סריקה + מיון על פני 10,000 Key Lookups.
Top                                                    20
  Sort                                                 10,000
    Clustered Index Scan  PK_Requests                  20,025 (est 20,044)  reads 2,004

Q4 COUNT חיפוש "היתר" – 343ms CPU. ‏LIKE '%x%' לא יכול להשתמש ב-Seek: כל הטבלה.
Stream Aggregate                                       1
  Clustered Index Scan  PK_Requests                    6,798 (est 13,192)   reads 2,004

Q4 עמוד – 3ms. עוצר אחרי 337 שורות, כשנמצאו 20 התאמות.
Top                                                    20
  Nested Loops
    Index Scan  IX_Requests_CreatedAt  BACKWARD        337 (est 152)   reads 3
    Clustered Index Seek  PK_Requests                  20              reads 951

Q7 Summary – קבוצות – 56ms. כל העמודות באינדקס; הטבלה לא נקראת.
Hash Match (Aggregate)                                 12
  Index Scan  IX_Requests_Status_CreatedAt             100,000         reads 328

Q8 Summary – Top 5 מטפלים – 16ms. Seek על AssignedTo IS NOT NULL; הנתונים כבר ממוינים לפי מטפל, אין מיון לפני הקיבוץ.
Top ← Sort (5)
  Stream Aggregate                                     40
    Index Seek  IX_Requests_AssignedTo_Status          43,086 (est 51,645)  reads 392

Q9 UPDATE ... WHERE Id = @p2 AND RowVersion = @p3 – 0ms.
Clustered Index Update  PK_Requests (+ שני האינדקסים שמכילים Status)    1
  Top ← Compute Scalar
    Clustered Index Seek  PK_Requests  Seek: Id, Predicate: RowVersion   1   reads 3

Q9 INSERT היסטוריה – 0ms. כולל בדיקת ה-FK לפנייה.
Assert
  Nested Loops
    Clustered Index Insert  PK_RequestStatusHistory    1               reads 2
    Clustered Index Seek  PK_Requests  (FK)            1               reads 3
```

### לפני ואחרי שלושת התיקונים

שלושת התיקונים שבסעיף הבא, כל אחד עם שתי תוכניות על אותם נתונים. התוכניות של OPENJSON נשמרו על בסיס הנתונים של האפליקציה.
לפרגמנטציה ולאינדקס המכסה נוצר עותק זמני של הטבלה עם 100,000 השורות, נטען בדיוק כמו ב-Seeder (‏`SqlBulkCopy`, ‏TableLock, מנות של 10,000, האינדקסים קיימים מראש) ועם האינדקס כפי שהיה במיגרציה הראשונה.

| תיקון | לפני | אחרי |
|---|---|---|
| 1. OPENJSON → שרשרת OR | [`status-openjson-count`](performance/plans/before-after/status-openjson-count.sqlplan): ‏Index Scan על כל 100,000 השורות + Merge Join מול ה-JSON – **328 reads**, ‏16ms | [`status-or-chain-count`](performance/plans/before-after/status-or-chain-count.sqlplan): ‏Index Seek לשני הטווחים, 30,156 שורות – **104 reads**, ‏6ms |
| 2. פרגמנטציה → REBUILD | [`fragmented-q1-count`](performance/plans/before-after/fragmented-q1-count.sqlplan): ‏**364 reads**; ‏[`fragmented-q2-status-count`](performance/plans/before-after/fragmented-q2-status-count.sqlplan): ‏**128** | [`rebuilt-q1-count`](performance/plans/before-after/rebuilt-q1-count.sqlplan): ‏**213 reads**; ‏[`rebuilt-q2-status-count`](performance/plans/before-after/rebuilt-q2-status-count.sqlplan): ‏**77** |
| 3. ‏`UpdatedAt` ב-INCLUDE | [`summary-index-without-updatedat`](performance/plans/before-after/summary-index-without-updatedat.sqlplan): ‏**Clustered Index Scan** של כל הטבלה – **1,999 reads** | [`summary-index-covering`](performance/plans/before-after/summary-index-covering.sqlplan): ‏Index Scan של האינדקס בלבד – **324 reads** |

**הערות:**
* **תיקון 2.** צורת התוכנית זהה לפני ואחרי; רק מספר הדפים משתנה. מדידת הפרגמנטציה באותו עותק:

  | אינדקס | אחרי הטעינה | אחרי REBUILD |
  |---|---|---|
  | `IX_Requests_CreatedAt` | 97.2% פרגמנטציה, 58% מילוי, 362 דפים | 0%, 99.5%, 211 דפים |
  | `IX_Requests_Status_CreatedAt` | 98.7%, 60%, 389 דפים | 0%, 99.9%, 235 דפים |
  | `IX_Requests_OrganizationName` | 99.1%, 66%, 895 דפים | 0%, 99.6%, 591 דפים |
  | `IX_Requests_AssignedTo_Status` | 97.5%, 72%, 527 דפים | 0%, 99.8%, 382 דפים |

  ה-reads למעלה נמדדו עם Cache חם. מקריאה מהדיסק המחיר גבוה יותר, כי דפים מפוצלים לא יושבים ברצף ו-Read-ahead פחות יעיל. את זה לא מדדתי.
* **תיקון 3.** זמן ה-CPU כמעט זהה (50–62ms), כי בשני המקרים מסכמים 100,000 שורות. החיסכון הוא ב-IO: פי 6 פחות דפים, ופחות לחץ על ה-Buffer Pool.

## מה נמצא ותוקן במהלך המדידות

1. **EF Core 8 ו-`OPENJSON` (תוקן).** `statuses.Contains(r.Status)` מתורגם ב-EF Core 8 ל-`IN (SELECT … FROM OPENJSON(@json))`. ‏SQL Server לא יכול להעריך כמה ערכים יש ב-JSON, ולכן סרק את כל אינדקס הסטטוס (100,000 שורות) במקום Seek:

   | COUNT של סטטוס חדשה + ממתינה | Reads | זמן |
   |---|---|---|
   | `OPENJSON` (ברירת המחדל של EF Core 8) | 328 | 16ms |
   | `Status = @p0 OR Status = @p1` (התיקון) | **104** | 6ms |

   התיקון: ב-Repository נבנה ביטוי `שדה = @p0 OR שדה = @p1` לסינוני הסטטוס והעדיפות. כל ערך נשאר פרמטר, כך שהתוכנית נשמרת ב-Cache של SQL Server. ב-Bulk, ‏`ids.Contains` נשאר עם `OPENJSON` – עד 100 מזהים, Join על המפתח הראשי, וזה המבנה הנכון שם.

2. **פרגמנטציה אחרי טעינה מרוכזת (תוקן).** ה-Seeder מכניס את השורות לפי סדר ה-Id, אבל האינדקסים המשניים ממוינים לפי עמודות אחרות, ולכן נבנו בפיצולי דפים: **97–99% פרגמנטציה ו-58–72% מילוי**. אחרי `ALTER INDEX ALL … REBUILD` בסוף ה-Seed: ‏0% פרגמנטציה, 99% מילוי, והאינדקסים קטנו (למשל `IX_Requests_CreatedAt`: ‏362 → 211 דפים). המספרים משחזור של אותה טעינה – ראו [לפני ואחרי](#לפני-ואחרי-שלושת-התיקונים).

3. **אינדקס מכסה ל-Summary (תוקן).** ה-Summary החדש סוכם גם את `CreatedAt` ואת `UpdatedAt`. בלי `UpdatedAt` ב-INCLUDE של `IX_Requests_Status_CreatedAt`, ‏SQL Server סרק את כל הטבלה. אחרי ההוספה: **1,999 → 324 reads** (סריקת כל הטבלה → סריקת האינדקס בלבד). אין עלות כתיבה נוספת: ‏`UpdatedAt` משתנה באותו UPDATE כמו `Status`, שכבר מעדכן את האינדקס הזה.

## האינדקסים ולמה

| אינדקס | עמודות | בשביל מה | מה נמדד |
|---|---|---|---|
| `PK_Requests` (Clustered) | `Id` | שליפה ועדכון לפי מזהה, Key Lookup | עדכון: 3 reads |
| `IX_Requests_CreatedAt` | `CreatedAt` | מיון ברירת המחדל וטווח תאריכים: "20 האחרונות" בלי למיין 100K שורות | Q1: ‏3ms מול 162ms |
| `IX_Requests_Status_CreatedAt` | `Status, CreatedAt` INCLUDE `Priority, UpdatedAt` | הסינון הנפוץ ביותר, ומכסה את כל שאילתת הקבוצות של ה-Summary | Q2 COUNT: ‏104 מול 1,999; ‏Q7: ‏328 מול 1,999 |
| `IX_Requests_AssignedTo_Status` | `AssignedTo, Status` | Top מטפלים; סינון לפי מטפל סורק אותו במקום את הטבלה | Q8: ‏392 מול 1,999 |
| `IX_Requests_OrganizationName` | `OrganizationName` | סינון ארגון לפי תחילית (`LIKE 'abc%'`) – Seek | Q6: ‏63 מול 1,999 |
| `IX_RequestStatusHistory_RequestId_ChangedAt` | `RequestId, ChangedAt` | היסטוריה של פנייה, מהחדש לישן; משמש גם את ה-FK | – |

**מה לא נוסף ולמה:** לא נוסף אינדקס על `Priority` לבד (3 ערכים – סלקטיביות נמוכה מדי) ולא על `Title` (החיפוש הוא "מכיל", ו-B-Tree לא עוזר לו).
**מחיר בכתיבה:** עדכון סטטוס משנה את שני האינדקסים שמכילים `Status`, בנוסף לטבלה – 7 reads לכל ה-UPDATE. מקובל: עדכונים הם פעולה בודדת לפי מפתח, וקריאות הן הרוב.

## איך נמנעת טעינת כל הנתונים לזיכרון

* ה-Repository בונה `IQueryable` אחד: סינונים → מיון + `ThenBy(Id)` → `Skip/Take` → הטלה ל-DTO. ‏EF מתרגם הכול לשאילתה אחת עם `OFFSET/FETCH`, ורק העמוד המבוקש יוצא מבסיס הנתונים.
* ההטלה ל-DTO קוראת רק את העמודות הנחוצות, עם `AsNoTracking`.
* `PageSize` מוגבל ל-100 בוולידציה – אי אפשר לבקש "הכול". עמוד שאחרי הסוף מחזיר ריק בלי לשאול את בסיס הנתונים.
* ה-Summary רץ כ-`GROUP BY` בבסיס הנתונים ומחזיר עד 12 שורות + 5 מטפלים.
* בצד Angular אין סינון, מיון או דפדוף בכלל. כל שינוי שולח בקשה חדשה לשרת, ו-`switchMap` מבטל את הקודמת.

## צווארי בקבוק והצעות לשיפור

### 1. חיפוש טקסט "מכיל" – צוואר הבקבוק העיקרי (~360ms, ‏Summary עם חיפוש ~520ms)

`LIKE '%היתר%'` לא יכול להשתמש באינדקס, ולכן ה-`COUNT` סורק את כל 100,000 השורות. העמוד עצמו מהיר (2ms) כי הוא נעצר אחרי 20 תוצאות – **ה-COUNT הוא שיקר**. רוב הזמן הוא **CPU** ולא IO: השוואת מחרוזות Unicode עם Collation לשוני.

ניסוי (אותן 6,798 תוצאות):

| גרסה | CPU / Elapsed |
|---|---|
| נוכחי – `nvarchar` עם ה-Collation של בסיס הנתונים | 313ms / 363ms |
| `COLLATE Latin1_General_100_BIN2` | **46ms / 45ms** (פי 8) |
| `UPPER(...) COLLATE Latin1_General_100_BIN2` | 79ms / 85ms |

לעברית אין אותיות גדולות וקטנות, כך שהשוואה בינארית מספיקה לה. `UPPER` נדרש רק כדי שגם אותיות לטיניות יימצאו בלי תלות ב-Case.

**הצעות, לפי סדר:**
1. **זול ומהיר:** עמודה מחושבת `PERSISTED` – ‏`SearchText = UPPER(Title + N' ' + OrganizationName) COLLATE Latin1_General_100_BIN2` – והחיפוש עליה. שיפור צפוי של פי 4–8 בלי תשתית חדשה.
2. **Full-Text Search** של SQL Server (`CONTAINS`) – אינדקס הפוך לפי מילים. לא זמין ב-LocalDB, ולכן לא מומש.
3. כבר קיים בממשק: המתנה של 350ms אחרי ההקלדה, וביטול בקשות קודמות עם `switchMap`.

### 2. דפדוף עמוק (OFFSET)

בעמוד 500, ‏SQL Server צריך לעבור על 10,000 שורות ולזרוק אותן, ובוחר בסריקה + מיון (‏~40ms). העלות גדלה עם מספר העמוד.
**שיפור:** Keyset pagination – ‏`WHERE (CreatedAt, Id) < (@lastCreatedAt, @lastId)` – מחיר קבוע לכל עמוד. החיסרון: אי אפשר לקפוץ ישירות לעמוד N, ולכן הוא לא נבחר.

### 3. Summary עם סינון סטטוס או עדיפות בלבד

שאילתת הקבוצות מתעלמת בכוונה מסינוני הסטטוס והעדיפות (הם מוחלים אחר כך, כדי שכל פילוח יוכל להתעלם מהסינון שלו). לכן כשהמשתמש מסנן **רק** לפי סטטוס או עדיפות, השאילתה זהה לזו של התצוגה ללא סינון – אבל מחושבת מחדש בכל פעם (89ms).
**שיפור:** לשמור ב-Cache את הקבוצות לפי "הסינונים המשותפים" (כל השאר), ולא את התוצאה הסופית. בחירת צ'יפים של סטטוס/עדיפות תהיה אז מה-Cache.

### 4. סינון לפי מטפל ("מכיל")

נבחר חיפוש "מכיל" כדי שאפשר יהיה לחפש לפי שם משפחה. המחיר: סריקה של אינדקס המטפלים הצר (370 reads) במקום Seek. עם מיון לפי עדיפות, SQL Server סורק את הטבלה וממיין 606 שורות – 76ms בסך הכול. מקובל בהיקף הזה. אם יידרש: רשימת מטפלים לבחירה (התאמה מדויקת → Seek).

### 5. `COUNT(*)` בכל בקשה

גם ללא סינון, ה-COUNT סורק אינדקס שלם (213 reads, 15ms). זה זול כאן, אבל גדל ליניארית עם הטבלה. אפשרויות: ספירה משוערת מ-`sys.dm_db_partition_stats` כשאין סינון, או Cache קצר לספירה.
