# מערכת לניהול פניות – מבדק Full Stack

מערכת לניהול פניות מארגונים ומעסיקים: איתור, סינון, צפייה, עדכון סטטוס (כולל Bulk), היסטוריית שינויים ונתונים מסכמים – לעבודה של מספר משתמשים במקביל על 100,000+ פניות.

| מסמך | תוכן |
|---|---|
| README (כאן) | הרצה, טכנולוגיות, מבנה, DB, Concurrency, Bulk, Cache, החלטות, מגבלות, שימוש ב-AI |
| [docs/WORK_PLAN.md](docs/WORK_PLAN.md) | פירוק האפיון למשימות, תלויות, סדר ביצוע והערכת מאמץ |
| [docs/PERFORMANCE.md](docs/PERFORMANCE.md) | מדידות על 100K רשומות, Execution Plans, אינדקסים, Bottlenecks |
| [docs/performance/measure.sql](docs/performance/measure.sql) | סקריפט המדידה (ניתן להרצה חוזרת) |

## טכנולוגיות וגרסאות

| שכבה | טכנולוגיה |
|---|---|
| Backend | .NET 10 (LTS), ASP.NET Core Web API, Entity Framework Core 10.0.12 |
| Database | SQL Server (נבדק על LocalDB 2019 ו-SQL Server 2025) |
| Frontend | Angular 21 (Standalone, Signals, Zoneless), RxJS 7.8, TypeScript 5.9 |
| בדיקות | xUnit 2.9, `Microsoft.AspNetCore.Mvc.Testing` (Integration), Vitest (Angular) |
| תיעוד API | Swagger (Swashbuckle 10) |

## הרצה

### דרישות מקדימות

* .NET SDK 10
* Node.js ‏22.12 ומעלה (או 20.19+ / 24+)
* SQL Server – ברירת המחדל היא **LocalDB** (מגיע עם Visual Studio). לשרת אחר יש לעדכן Connection String (ראו למטה).

### 1. בסיס נתונים + נתוני בדיקה (100,000 פניות)

```bash
cd backend
dotnet run --project Requests.Api -- seed
```

הפקודה יוצרת את בסיס הנתונים (Migrations), **מוחקת את הנתונים הקיימים** ומכניסה 100,000 פניות דטרמיניסטיות (Seed קבוע → אותם נתונים בכל הרצה) ב-SqlBulkCopy, תוך כ-10 שניות. אפשר להריץ שוב בכל זמן כדי לאפס. כמות אחרת: `-- seed 250000`.

**Connection String אחר** – ב-`backend/Requests.Api/appsettings.json` או במשתנה סביבה:

```bash
# PowerShell
$env:ConnectionStrings__RequestsDb = "Server=localhost;Database=RequestsManagement;Trusted_Connection=True;TrustServerCertificate=True"
```

### 2. API

```bash
cd backend
dotnet run --project Requests.Api
```

* API: `http://localhost:5080`
* Swagger: `http://localhost:5080/swagger`
* בסביבת Development ה-Migrations מוחלים אוטומטית בעלייה.

### 3. Angular

```bash
cd frontend/requests-ui
npm ci
npm start
```

* ממשק: `http://localhost:4200` (אם הפורט תפוס: `npm start -- --port 4300`)
* `proxy.conf.json` מפנה את `/api` ל-`http://localhost:5080`, כך שאין צורך ב-CORS.

### 4. בדיקות

```bash
cd backend
dotnet test                       # 33 בדיקות: Domain + Integration מול SQL Server

cd frontend/requests-ui
npx ng test --watch=false         # 4 בדיקות של ה-Store
```

בדיקות ה-Integration יוצרות בסיס נתונים נפרד `RequestsManagement_Tests` (לא נוגעות בנתוני הפיתוח). שרת אחר: משתנה סביבה `REQUESTS_TEST_DB`.

## מבנה הפתרון

```text
backend/
  Requests.Api/
    Domain/                 ישויות, סטטוסים, Workflow המעברים (StatusTransitions) – ללא תלות ב-EF/HTTP
    Data/                   DbContext, Configurations (אינדקסים), Migrations, DataSeeder
    Features/Requests/      Controller, שירותי קריאה/כתיבה/סיכום, DTOs, Mapping
    Common/Errors/          חריגות אפליקטיביות + GlobalExceptionHandler (Problem Details)
    Common/Caching/         SummaryCache
    Program.cs              DI, JSON, Swagger, פקודת seed
  Requests.Api.Tests/
    Domain/                 בדיקות יחידה למעברי סטטוס
    Api/                    Integration: חיפוש, Validation, עדכון, Concurrency, Bulk, Cache
    Infrastructure/         WebApplicationFactory מול SQL Server
frontend/requests-ui/src/app/
  core/                     מודלים, RequestsApi (HTTP בלבד), טיפול בשגיאות, CurrentUser
  features/requests/        Store (מצב + RxJS) וקומפוננטות: Filters, Table, Pagination, Summary, Details, Bulk
docs/                       תוכנית עבודה, ביצועים
```

**הפרדת אחריות ב-Backend:**
* **Controller** – רק HTTP: Binding, Validation אוטומטי (`[ApiController]`), החזרת DTO.
* **Services** – `RequestQueryService` (קריאה, `AsNoTracking` + Projection), `RequestCommandService` (כתיבה, Concurrency, Audit), `RequestSummaryService` (Aggregations + Cache).
* **Domain** – כללי עסק: מי מותר לעבור לאיזה סטטוס, ו-`ChangeStatus` שמחזיר את רשומת ה-Audit כך שאי אפשר לשנות סטטוס בלי היסטוריה.
* **Lifetimes ב-DI** – `DbContext` ושירותים: Scoped (לבקשה). `SummaryCache` ו-`TimeProvider`: Singleton (‏`SummaryCache` מחזיק את טוקן ה-Invalidation המשותף).

**הפרדת אחריות ב-Angular:**
* `RequestsApi` – HTTP בלבד, ללא מצב.
* `RequestsListStore` – מצב הרשימה (Signals) וה-Pipeline של RxJS; מסופק ברמת הדף.
* `RequestsPageComponent` – Container שמחבר בין ה-Store לקומפוננטות.
* `RequestFilters`, `RequestsTable`, `Pagination`, `BulkStatusBar` – Presentational: מקבלים `input()` ומדווחים `output()`.
* `RequestDetails`, `SummaryPanel` – טוענים את הנתונים שלהם עצמאית (`switchMap` לפי מזהה / מפתח רענון).

## API

| Method | Endpoint | תיאור | קודים |
|---|---|---|---|
| GET | `/api/requests` | חיפוש עם סינון/מיון/דפדוף בצד השרת | 200, 400 |
| GET | `/api/requests/summary` | Aggregations (מ-Cache) | 200 |
| GET | `/api/requests/{id}` | פנייה + `allowedNextStatuses` | 200, 404 |
| GET | `/api/requests/{id}/history` | היסטוריית שינויי סטטוס, מהחדש לישן | 200, 404 |
| PATCH | `/api/requests/{id}/status` | עדכון סטטוס `{ status, rowVersion, changedBy }` | 200, 400, 404, **409**, 422 |
| POST | `/api/requests/bulk/status` | עדכון עד 100 פניות `{ status, changedBy, items: [{ id, rowVersion }] }` | 200, 400 |

**פרמטרי חיפוש** (כולם אופציונליים, משולבים ב-AND):
`page` (‏≥1), `pageSize` (‏1–100, ברירת מחדל 20), `search` (‏contains ב-Title או OrganizationName), `status` (אפשר כמה: `?status=New&status=Waiting`), `priority` (כנ"ל), `organizationName` (‏prefix), `assignedTo` (התאמה מדויקת), `createdFrom`/`createdTo` (‏UTC, כולל), `sortBy` (‏`createdAt|updatedAt|priority|status|title|organizationName`), `sortDirection` (‏`asc|desc`).

**Validation** – פרמטר לא חוקי (‏`pageSize=500`, `status=99`, `sortBy=password`, טווח תאריכים הפוך, `rowVersion` שאינו Base64, ערך enum לא מוכר ב-JSON וכו') מחזיר **400** בפורמט `ValidationProblemDetails` עם השדה הבעייתי. כל השגיאות האחרות מוחזרות כ-**Problem Details** (RFC 7807) דרך `GlobalExceptionHandler`; שגיאה לא צפויה נרשמת ללוג ומוחזרת כ-500 ללא פרטים פנימיים.

**CancellationToken** מועבר מה-Controller לכל קריאת EF. כשהלקוח מבטל (למשל `switchMap` באנגולר), השאילתה ב-SQL מבוטלת וה-Handler רושם Information ולא Error.

## בסיס הנתונים

### מודל

```text
Requests                                 RequestStatusHistory
  Id               int identity PK         Id              bigint identity PK
  Title            nvarchar(200)           RequestId       int FK → Requests (cascade)
  OrganizationName nvarchar(200)           PreviousStatus  tinyint
  Status           tinyint                 NewStatus       tinyint
  Priority         tinyint                 ChangedAt       datetime2(3)  UTC
  AssignedTo       nvarchar(100) null      ChangedBy       nvarchar(100)
  CreatedAt        datetime2(3)  UTC
  UpdatedAt        datetime2(3)  UTC
  RowVersion       rowversion
```

* `Status`/`Priority` נשמרים כ-`tinyint` עם ערכי enum מפורשים (אינדקסים צרים; שינוי סדר ב-enum לא משנה נתונים).
* כל התאריכים ב-UTC; Value Converter מסמן אותם כ-UTC בקריאה כדי שה-JSON יכיל `Z`.

### אינדקסים

| אינדקס | תרחיש |
|---|---|
| `IX_Requests_CreatedAt` | מיון ברירת מחדל + טווח תאריכים (Top-N בלי Sort) |
| `IX_Requests_Status_CreatedAt` INCLUDE `Priority` | סינון לפי סטטוס + Covering ל-Aggregation |
| `IX_Requests_AssignedTo_Status` | "הפניות שלי", עומס לפי מטפל |
| `IX_Requests_OrganizationName` | סינון ארגון כ-prefix (Seek) |
| `IX_RequestStatusHistory_RequestId_ChangedAt` | היסטוריה של פנייה |

המדידות, ה-Plans וההשוואה עם/בלי כל אינדקס – ב-[docs/PERFORMANCE.md](docs/PERFORMANCE.md). לדוגמה: עמוד ראשון ממוין לפי תאריך – 66 logical reads מול 2,405 בלי האינדקס.

## Concurrency – איך נמנע Lost Update

* לכל פנייה עמודת `rowversion` שמשתנה **ע"י SQL Server** בכל עדכון. היא מוחזרת ללקוח (Base64) בכל DTO.
* הלקוח שולח את ה-`rowVersion` שקרא. השרת מגדיר אותו כ-`OriginalValue`, ולכן EF מייצר:
  `UPDATE Requests SET ... WHERE Id = @id AND RowVersion = @clientVersion`
* אם משתמש אחר עדכן בינתיים – 0 שורות מתעדכנות → `DbUpdateConcurrencyException` → **409 Conflict**. זה נאכף **ב-DB**, גם אם שתי הבקשות מגיעות באותו רגע בין הקריאה לכתיבה (נבדק בבדיקה עם 5 עדכונים מקבילים – בדיוק אחד מצליח, ונוצרת בדיוק רשומת Audit אחת).
* בדיקה מוקדמת: אם הגרסה של הלקוח כבר ישנה, מוחזר 409 עוד לפני בדיקת חוקיות המעבר (הנתונים שהמשתמש ראה כבר לא נכונים).
* `UpdatedAt` מתעדכן בתוך `ChangeStatus` באותה פעולה; העדכון ורשומת ה-Audit נשמרים ב-`SaveChanges` אחד = טרנזקציה אחת. הזמן מעוגל לדיוק העמודה כדי שהערך שמוחזר ללקוח זהה לזה שנשמר.
* **סדר הבדיקות בעדכון:** גוף לא חוקי → 400; פנייה לא קיימת → 404; גרסה ישנה → 409; מעבר אסור → 422 (עם `currentStatus` ו-`allowedStatuses` בתשובה).
* **ב-UI:** ב-409 מוצגת הודעה ("שונתה ע"י משתמש אחר"), הפנייה וההיסטוריה נטענות מחדש עם הגרסה העדכנית, והמשתמש מחליט אם לנסות שוב. ה-UI לא "פותר" את הקונפליקט בעצמו.

**מעברי סטטוס מותרים:**

| מ- | אל |
|---|---|
| New | InProgress, Waiting |
| InProgress | Waiting, Completed |
| Waiting | InProgress, Completed |
| Completed | InProgress (פתיחה מחדש) |

## Bulk Update – Partial Success

`POST /api/requests/bulk/status` מקבל עד 100 פריטים, כל אחד עם `id` ו-`rowVersion` משלו.

**ההתנהגות:**
1. Validation של הבקשה כולה (1–100 פריטים, ללא כפילויות, סטטוס חוקי, `rowVersion` תקין) – אם נכשל, **400** ושום דבר לא מתעדכן.
2. כל הפניות נטענות בשאילתה אחת (`WHERE Id IN (...)`).
3. כל פריט נבדק בנפרד: לא קיים → `NotFound`; גרסה ישנה → `Conflict`; מעבר אסור → `InvalidTransition`.
4. כל הפריטים התקינים נשמרים **יחד בטרנזקציה אחת** (עם Audit לכל אחד).
5. אם בזמן השמירה פנייה שונתה ע"י מישהו אחר, SQL Server מגלגל אחורה את הטרנזקציה; השירות מסמן רק את הפריט המתנגש כ-`Conflict`, מוציא אותו, ושומר שוב את השאר. כל סבב מוציא לפחות פריט אחד, כך שהלולאה חסומה.
6. התשובה – **200** עם סיכום ותוצאה לכל פריט, באותו סדר כמו בבקשה:

```json
{
  "requested": 4, "succeeded": 2, "failed": 2,
  "results": [
    { "id": 15814, "outcome": "Updated", "error": null, "rowVersion": "AAAAAAABvVE=" },
    { "id": 999999, "outcome": "NotFound", "error": "Request not found.", "rowVersion": null },
    { "id": 1, "outcome": "Conflict", "error": "The request was modified by another user.", "rowVersion": null }
  ]
}
```

**למה Partial Success ולא All-or-Nothing:** ב-Bulk על עשרות פניות במערכת עם הרבה משתמשים, הסיכוי שאחת מהן שונתה ע"י מישהו אחר גבוה. All-or-Nothing היה גורם לכך שפנייה אחת "חוסמת" את כל הפעולה, והמשתמש היה צריך לרענן ולנסות שוב בלי לדעת מה השתנה. Partial Success מעדכן את מה שאפשר, ומחזיר רשימה מפורשת של מה לא עודכן ולמה – כל פריט עדיין מוגן ב-Optimistic Concurrency, כך שאין Lost Update. החיסרון: הלקוח חייב לקרוא את התוצאה לכל פריט (ה-UI מציג אותה).
**למה 200 ולא 207:** קוד 207 Multi-Status שייך ל-WebDAV ולקוחות רבים לא מטפלים בו; הבקשה עצמה עובדה בהצלחה, והתוצאה המפורטת בגוף.

## Cache ו-Invalidation

**מה נשמר:** תוצאת `GET /api/requests/summary` (ספירות לפי סטטוס, פתוחות לפי עדיפות, Top מטפלים).

**למה דווקא זה:**
* כל Aggregation סורק את כל הטבלה (‏~64ms, ‏1,300+ logical reads) – זה החישוב היקר ביותר שאינו תלוי בפרמטרים.
* התוצאה **זהה לכל המשתמשים** ונטענת בכל פתיחת מסך – יחס פגיעה גבוה.
* סטייה קטנה בספירות סבירה לתצוגת Dashboard. לעומת זאת, רשימת הפניות **לא** נשמרת ב-Cache: יש אינסוף צירופי פילטרים, והמשתמשים צריכים לראות את הסטטוס האמיתי לפני עדכון.

**מימוש (`SummaryCache`, ‏`IMemoryCache`):**
* **Expiration:** ‏Absolute TTL של 60 שניות (`Cache:SummaryTtlSeconds`) – רשת ביטחון גם לשינויים שלא עברו דרך ה-API.
* **Invalidation:** כל עדכון סטטוס מוצלח (בודד או Bulk) קורא ל-`Invalidate()` אחרי ה-Commit.
* **Race בין חישוב לעדכון:** הרשומה ב-Cache נקשרת ל-`CancellationChangeToken` שנלקח **לפני** תחילת החישוב. אם עדכון קרה בזמן שהחישוב רץ, הטוקן כבר מבוטל והערך שנוצר נזרק מיד במקום להישמר כמידע מיושן ל-60 שניות.
* תוצאה: 5ms מה-Cache מול 64ms חישוב. בדיקת Integration מוודאת שהסיכום מתעדכן מיד אחרי שינוי סטטוס.

**בסביבה עם מספר מופעי שרת:** ‏`IMemoryCache` הוא לכל מופע, ולכן Invalidation במופע A לא מנקה את מופע B (שיראה מידע מיושן עד ה-TTL). ההתאמה הנדרשת:
1. **Distributed cache (Redis)** – ‏`IDistributedCache`/`HybridCache` עם מפתח משותף; Invalidation = מחיקת המפתח ב-Redis, וכל המופעים רואים אותו.
2. אם רוצים לשמור L1 מקומי (מהיר) – לפרסם הודעת Invalidation ב-**Redis Pub/Sub** (או Backplane של HybridCache) שכל מופע מאזין לה ומנקה את ה-L1 שלו.
3. חלופה פשוטה: מפתח עם **מספר גרסה** שנשמר ב-Redis (`summary:v{n}`) – עדכון מגדיל את `n` אטומית (`INCR`), והמופעים מחשבים מחדש כשהגרסה משתנה.
בכל המקרים ה-TTL נשאר כרשת ביטחון.

## החלטות טכנולוגיות וחלופות שנשקלו

### 1. Optimistic Concurrency עם `rowversion` של SQL Server

| חלופה | למה לא |
|---|---|
| **Pessimistic locking** (`UPDLOCK`/נעילה בזמן עריכה) | מחזיק נעילות בזמן שהמשתמש חושב, פוגע בתפוקה, ודורש ניהול שחרור נעילות. בפניות, התנגשויות נדירות – אין סיבה לשלם על כל עדכון. |
| **עמודת `Version int` ידנית** | עובד, אבל חייבים לזכור להעלות אותה בכל עדכון (גם בעדכונים עתידיים/סקריפטים). `rowversion` מתעדכן ע"י ה-DB תמיד, ו-EF תומך בו מובנית. |
| **השוואת `UpdatedAt`** | רזולוציית זמן ושעונים שונים יכולים לאפשר שני עדכונים עם אותו ערך. |
| **ETag + `If-Match` header** | נכון סמנטית ל-HTTP ושקול לפתרון; נבחר שדה בגוף כי ב-Bulk לכל פריט גרסה משלו, וחוזה אחיד פשוט יותר ללקוח. |

### 2. פרויקט API אחד עם תיקיות לפי Feature, במקום Clean Architecture עם 4 פרויקטים

ההפרדה הלוגית קיימת (Domain / Data / Features / Common, ממשקים לשירותים, Domain בלי תלות ב-EF), אבל בתוך פרויקט אחד.
* **חלופה:** ‏Domain / Application / Infrastructure / Api כפרויקטים נפרדים, Repository + Unit of Work מעל EF.
* **למה לא:** עבור Bounded Context אחד עם ישות אחת זה מוסיף הרבה קבצים ו-Mapping בלי ערך – האפיון מבקש במפורש להימנע מ-Over Engineering. `DbContext` הוא כבר Unit of Work, ו-`IQueryable` מאפשר לבנות שאילתות יעילות בלי שכבת Repository שמסתירה אותן. אם המערכת תגדל, קל לפצל את התיקיות לפרויקטים.

### החלטות נוספות בקצרה

* **Offset pagination ולא Keyset** – מאפשר קפיצה לעמוד N ומספר עמודים כולל, כנדרש בממשק. המחיר בעמודים עמוקים נמדד ומתועד, עם Keyset כשיפור.
* **Angular עם Signals + RxJS** – Signals למצב תצוגה (פשוט, Zoneless), RxJS רק איפה שיש זרם אסינכרוני: Debounce, `switchMap` לביטול בקשות.
* **SQL Server ולא MongoDB** – הנתונים טבלאיים עם סינונים משולבים ו-Aggregations, ו-`rowversion` ו-Execution Plans נותנים Concurrency ומדידה מובנים.
* **Integration tests מול SQL Server אמיתי ולא InMemory/SQLite** – ‏`rowversion`, תרגום `LIKE` והתנהגות טרנזקציות שונים בספקים אחרים; בדיקת Concurrency על InMemory לא הייתה מוכיחה כלום.

## מגבלות ידועות ושיפורים אפשריים

* **מגבלה – חיפוש טקסט:** ‏`contains` מתורגם ל-`LIKE '%x%'` שלא יכול להשתמש באינדקס; ה-`COUNT` של חיפוש טקסט לוקח ~400ms על 100K רשומות (רובו CPU של Collation). **שיפור:** עמודה מחושבת `PERSISTED` עם Collation בינארי – נמדד שיפור של פי 6.5 (‏65ms) – או Full-Text Search. פירוט ב-[PERFORMANCE.md](docs/PERFORMANCE.md).
* **מגבלה – זהות המשתמש:** אין אימות; `changedBy` מגיע מהלקוח (שדה "Acting as" בממשק). בייצור – לקחת מה-Token (‏`User.Identity`) ולא מהגוף.
* **שיפור – דפדוף עמוק:** Keyset pagination לעמודים רחוקים (עמוד 500 = ‏50ms היום).
* **נתוני Seed** – לפניות שנוצרו ב-Seed אין היסטוריה; היסטוריה נוצרת מעדכונים בפועל.

## Logging

* עדכון סטטוס מוצלח – Information עם מזהה, סטטוס קודם וחדש.
* Bulk – Information עם כמות הצלחות/כשלונות.
* 404/409/422 – Warning עם ההודעה; שגיאה לא צפויה – Error עם ה-Exception.
* לא נרשמים גופי בקשות, Connection Strings או נתוני משתמש מעבר למזהה הפנייה. פקודות SQL לא נרשמות ברירת מחדל (‏`Microsoft.EntityFrameworkCore.Database.Command: Warning`).

## שימוש בכלי AI

הפתרון פותח בעזרת **Claude Code** (סוכן AI בטרמינל), בהנחיה ובקבלת החלטות של המועמד/ת.

**במה נעשה שימוש:**
* כתיבת רוב הקוד (Backend, Angular, בדיקות), ה-Seeder וסקריפט המדידה.
* טיוטת המסמכים (README, תוכנית עבודה, ביצועים).

**החלטות שהתקבלו ע"י המועמד/ת:** בחירת SQL Server, Partial Success ב-Bulk, פתרון חדש ולא הרחבה של פתרון קיים, אופן ההגשה.

**מה נבדק ואומת בפועל (ולא רק נכתב):**
* כל הבדיקות האוטומטיות הורצו ועוברות (33 Backend, 4 Angular). בדיקת ה-Concurrency מריצה 5 עדכונים מקבילים מול SQL Server אמיתי.
* כל מספר ב-[PERFORMANCE.md](docs/PERFORMANCE.md) נמדד בפועל על 100K רשומות – ה-SQL נלקח מלוג EF, ה-Plans מ-`STATISTICS PROFILE`.
* הממשק נבדק בדפדפן אמיתי: מספר הבקשות בזמן הקלדה (Debounce – בקשה אחת למילה), סינון/מיון/דפדוף, מצב ריק, עדכון סטטוס, 409 כשמשתמש אחר עדכן באמצע, Bulk.

**באגים שהבדיקות והאימות תפסו בקוד שנוצר ותוקנו:**
* `UpdatedAt` שהוחזר ללקוח היה בדיוק גבוה מזה שנשמר ב-`datetime2(3)` (התגלה ב-Integration test).
* תאריכים הוחזרו ב-JSON בלי `Z` – הדפדפן היה מפרש אותם כשעון מקומי.
* `DBCC CHECKIDENT RESEED 0` על טבלה חדשה יצר פנייה עם `Id = 0`.
* הודעת שגיאה של JSON חשפה שם טיפוס פנימי של .NET – כובתה (`AllowInputFormatterExceptionMessages = false`).
* צבעי הגרפים בסיכום נדרסו ע"י CSS כללי (נראה רק בצילום מסך).
* בדיקת Bulk שגויה (הניחה ש-Completed→InProgress אסור, בעוד שזו פתיחה מחדש מותרת) – תוקנה הבדיקה, לא הקוד.
