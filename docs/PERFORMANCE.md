# ביצועים – מדידה וניתוח על 100,000 רשומות

כל המספרים כאן נמדדו בפועל על הנתונים שנוצרים ע"י `seed` (100,000 פניות), על SQL Server LocalDB 2019 במחשב פיתוח.
סקריפט המדידה נמצא ב-[`performance/measure.sql`](performance/measure.sql) וניתן להריץ אותו מחדש:

```bash
sqlcmd -S "(localdb)\MSSQLLocalDB" -d RequestsManagement -E -i docs/performance/measure.sql
```

**שיטה**

* ה-SQL נלקח מלוג הפקודות של EF Core (`Microsoft.EntityFrameworkCore.Database.Command=Information`) ורץ דרך `sp_executesql` עם פרמטרים – בדיוק כמו באפליקציה, כך שה-Plan זהה.
* המדד העיקרי: **Logical reads** (`SET STATISTICS IO`) – יציב ולא תלוי בעומס המחשב. בנוסף CPU/Elapsed (`SET STATISTICS TIME`) ו-Execution Plan בפועל (`SET STATISTICS PROFILE`).
* כל שאילתה רצה פעמיים: **עם האינדקסים** ו-**בלי** (רמז `WITH (INDEX(1))` שמכריח Clustered Index Scan) – כך רואים מה כל אינדקס חוסך בפועל.
* בנוסף נמדד זמן תגובה מקצה לקצה של ה-API (‏`curl`, ממוצע של 20 קריאות, Cache חם של SQL).

## שתי הפעולות המרכזיות שנבחנו

### 1. `GET /api/requests` – חיפוש/סינון/מיון/דפדוף

כל קריאה מריצה שתי שאילתות: `COUNT(*)` עם אותם סינונים (לצורך מספר העמודים) ו-`ORDER BY … OFFSET … FETCH` לעמוד אחד. שום דבר לא נטען לזיכרון מעבר ל-20–100 שורות העמוד.

| תרחיש | שאילתה | Plan בפועל (עם אינדקסים) | Logical reads עם אינדקס | Logical reads בלי (Scan) | זמן |
|---|---|---|---|---|---|
| Q1 עמוד ראשון, מיון ברירת מחדל `CreatedAt DESC` | עמוד | Index Scan **ORDERED BACKWARD** על `IX_Requests_CreatedAt` → נעצר אחרי 20 שורות + 20 Key Lookups | **66** | 2,405 (+Sort של 100K שורות) | ‎~0ms מול 162ms |
| | COUNT | Index Scan על האינדקס הצר ביותר | 614 | 2,405 | ‎<11ms |
| Q2 `status IN (New, Waiting)` | COUNT | **Index Seek** על `IX_Requests_Status_CreatedAt` (שני טווחים) | **213** | 2,405 | 3ms מול 13ms |
| | עמוד | Scan אחורה על `IX_Requests_CreatedAt` עם סינון – נקראו 78 שורות כדי למצוא 20 | 242 | 2,405 + Sort | ‎~0ms מול 57ms |
| Q5 `assignedTo=agent07 AND status=InProgress` | COUNT | **Index Seek** על `IX_Requests_AssignedTo_Status` | **10** | 2,405 | ‎~0ms מול 14ms |
| | עמוד (מיון לפי Priority) | Seek (601 שורות) + Lookups + Sort קטן | 1,851 | – | 2ms |
| Q6 ארגון מתחיל ב-`Negev` + טווח תאריכים | COUNT | **Index Intersection**: Seek על `IX_Requests_OrganizationName` + Seek על `IX_Requests_CreatedAt` + Hash Join | **142** | 2,405 | 6ms מול 13ms |
| Q4 חיפוש טקסט `%permit%` | COUNT | **Clustered Index Scan** – אין אינדקס שעוזר ל-`LIKE '%x%'` | 2,405 | 2,405 | **~400ms CPU** |
| | עמוד | Scan אחורה על `IX_Requests_CreatedAt`, נעצר אחרי 315 שורות | 969 | – | 2ms |
| Q3 עמוד 500 (`OFFSET 9980`) של `status=New` | עמוד | האופטימייזר מוותר על האינדקס: Clustered Scan (20,091 שורות) + Sort | 2,405 | – | **~50ms** |

**זמן תגובה מקצה לקצה (API, ממוצע 20 קריאות):**

| קריאה | ממוצע | מקסימום |
|---|---|---|
| עמוד 1, ללא סינון | 22ms | 30ms |
| סינון לפי סטטוס | 14ms | 23ms |
| סינון לפי מטפל + סטטוס | 10ms | 12ms |
| עמוד 500 | 50ms | 55ms |
| חיפוש טקסט `permit` | **405ms** | 463ms |

### 2. `GET /api/requests/summary` – Aggregations

| שאילתה | Plan | Logical reads עם אינדקס | בלי | זמן |
|---|---|---|---|---|
| Q7 `GROUP BY Status, Priority` | Index Scan על `IX_Requests_Status_CreatedAt` (Priority ב-INCLUDE) + Hash Aggregate → 12 שורות | **670** | 2,405 | ~33ms |
| Q8 Top 5 מטפלים לפי פניות פתוחות | **Index Seek** `AssignedTo IS NOT NULL` על `IX_Requests_AssignedTo_Status` + Stream Aggregate (ממוין כבר לפי AssignedTo) | **664** | 2,405 | ~18ms |

| קריאה | ממוצע |
|---|---|
| `summary` מה-Cache | **5ms** |
| `summary` אחרי Invalidation (חישוב מחדש) | 64ms |

שתי השאילתות סורקות את כל הטבלה מטבען (Aggregation על כל הנתונים) – האינדקס הצר מקטין את ה-IO פי 3.6, וה-Cache חוסך את החישוב לגמרי ברוב הקריאות.

## האינדקסים שנוספו ולמה

| אינדקס | עמודות | למה | הוכחה מהמדידה |
|---|---|---|---|
| `PK_Requests` (Clustered) | `Id` | שליפה/עדכון לפי מזהה, Key Lookup | – |
| `IX_Requests_CreatedAt` | `CreatedAt` | מיון ברירת המחדל + טווח תאריכים. מאפשר "Top N" בלי למיין 100K שורות | Q1: 66 מול 2,405 reads, 0 מול 162ms |
| `IX_Requests_Status_CreatedAt` | `Status, CreatedAt` INCLUDE `Priority` | הסינון הנפוץ ביותר (תור "חדשות"), ו-Covering ל-Aggregation של סטטוס×עדיפות | Q2 COUNT: 213 מול 2,405; Q7: 670 מול 2,405 |
| `IX_Requests_AssignedTo_Status` | `AssignedTo, Status` | "הפניות שלי" + עומס לפי מטפל | Q5 COUNT: 10 מול 2,405; Q8: Seek במקום Scan |
| `IX_Requests_OrganizationName` | `OrganizationName` | סינון ארגון כ-**prefix** (`LIKE 'abc%'`) שמאפשר Seek | Q6: 142 מול 2,405 |
| `IX_RequestStatusHistory_RequestId_ChangedAt` | `RequestId, ChangedAt` | היסטוריה של פנייה ממוינת לפי זמן; גם תומך ב-FK | – |

**מחיר בכתיבה:** עדכון סטטוס משנה את `Status`, ולכן מעדכן גם את שני האינדקסים שמכילים אותו (בנוסף ל-Clustered). זה מחיר מקובל: עדכונים הם פעולה בודדת לפי מפתח, וקריאות הן הרוב. לא נוסף אינדקס על `Priority` לבד (סלקטיביות נמוכה – 3 ערכים) ולא על `Title` (חיפוש הוא `contains`, ש-B-Tree לא עוזר לו).

## איך נמנעת טעינה של כל הנתונים לזיכרון

* `RequestQueryService` בונה `IQueryable` אחד: `Where` (כל הסינונים) → `OrderBy` + `ThenBy(Id)` → `Skip/Take` → `Select` ל-DTO. EF מתרגם הכול לשאילתה אחת עם `OFFSET/FETCH` – רק העמוד חוזר מה-DB.
* `Select` ל-DTO מביא רק את העמודות הנחוצות, ו-`AsNoTracking` – אין Change Tracking לשורות קריאה.
* `PageSize` מוגבל ל-100 (Validation), כך שאי אפשר לבקש "הכול".
* ה-Aggregations רצים כ-`GROUP BY` ב-DB ומחזירים 12 + 5 שורות בלבד.
* בצד Angular אין סינון/מיון/דפדוף כלל – כל שינוי שולח בקשה חדשה לשרת.

## Bottlenecks שזוהו והצעות לשיפור

### 1. חיפוש טקסט `contains` – ה-Bottleneck העיקרי (~400ms)

`LIKE '%permit%'` לא יכול להשתמש באינדקס B-Tree, ולכן ה-`COUNT` סורק את כל 100K השורות. העמוד עצמו מהיר (2ms) כי הוא נעצר אחרי 20 תוצאות – **ה-COUNT הוא שעולה**. רוב הזמן הוא **CPU**, לא IO: השוואת מחרוזות Unicode עם Collation לא-בינארי יקרה.

ניסוי שנערך (אותן 6,710 תוצאות):

| גרסה | CPU / Elapsed |
|---|---|
| נוכחי – `nvarchar` עם Collation של ה-DB | 391ms / 422ms |
| `UPPER(col) COLLATE Latin1_General_100_BIN2 LIKE '%PERMIT%'` | **62ms / 65ms** (פי 6.5) |
| `varchar` עם SQL Collation | 94ms / 80ms |

**הצעות, לפי סדר עדיפות:**
1. **מהיר וזול:** עמודה מחושבת `PERSISTED` – `SearchText = UPPER(Title + ' ' + OrganizationName) COLLATE Latin1_General_100_BIN2`, והחיפוש עליה. שיפור של פי ~6 בלי תשתית חדשה.
2. **Full-Text Search** של SQL Server (`CONTAINS`) – חיפוש לפי מילים עם אינדקס הפוך. לא זמין ב-LocalDB ולכן לא מומש.
3. בממשק: Debounce (‏350ms) + ביטול בקשות קודמות (`switchMap`) כבר מונעים עומס מיותר בזמן הקלדה.
4. לשקול **ספירה משוערת** או "יש עוד עמוד" במקום `COUNT` מדויק לחיפושי טקסט.

### 2. דפדוף עמוק (OFFSET)

בעמוד 500, SQL Server צריך לעבור על 10,000 שורות ולזרוק אותן. האופטימייזר מעדיף אז Scan + Sort (‏50ms). בממשק אנשים כמעט לא מגיעים לשם, אבל זה גדל ליניארית עם מספר העמוד.
**שיפור:** Keyset pagination (‏"seek method") – `WHERE (CreatedAt, Id) < (@lastCreatedAt, @lastId) ORDER BY CreatedAt DESC, Id DESC` – מחיר קבוע לכל עמוד. החיסרון: אי אפשר לקפוץ ישירות לעמוד N, ולכן לא נבחר לגרסה הזו.

### 3. `COUNT(*)` בכל בקשה

גם ללא סינון, `COUNT` סורק אינדקס שלם (614 reads). זה זול כאן, אבל ב-10M שורות יהיה מורגש. אפשרויות: Cache קצר לספירה לפי פילטר, או ספירה משוערת מ-`sys.dm_db_partition_stats` כשאין פילטר.
