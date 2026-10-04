# ניהול פניות – מבדק Full Stack

מערכת לניהול פניות שמגיעות מארגונים ומעסיקים, לעבודה של כמה משתמשים במקביל על 100,000 פניות ויותר: איתור, סינון, מיון ודפדוף בצד השרת, עדכון סטטוס עם הגנה מפני דריסה, עדכון מרוכז, היסטוריית שינויים ונתונים מסכמים.

| מסמך | מה יש בו |
|---|---|
| README (כאן) | הרצה, טכנולוגיות, מבנה, בסיס נתונים, Concurrency, Bulk, Cache, החלטות, מגבלות, שימוש ב-AI |
| [docs/WORK_PLAN.md](docs/WORK_PLAN.md) | פירוק האפיון למשימות: תוצרים, תלויות, סדר ביצוע, הערכת מאמץ, ההחלטות שהתקבלו |
| [docs/PERFORMANCE.md](docs/PERFORMANCE.md) | מדידות על 100,000 פניות: תוכניות ביצוע, אינדקסים, צווארי בקבוק ושיפורים |
| [docs/performance/measure.sql](docs/performance/measure.sql) | סקריפט המדידה |
| [docs/performance/plans](docs/performance/plans) | תוכניות ביצוע בפועל (`.sqlplan`), כולל לפני/אחרי לכל תיקון |

## מדריך לבודק – 5 דקות

### הרצה (דקה וחצי)

**Windows עם LocalDB:**

```bash
dotnet run --project src/Requests.Api            # טרמינל 1 – בהרצה הראשונה: בסיס נתונים + 100,000 פניות (~15 שניות)
cd src/requests-ui && npm ci && npm start         # טרמינל 2 – http://localhost:4200
```

**Docker (Windows / Mac / Linux)** – SQL Server 2022 בקונטיינר, בלי LocalDB:

```bash
docker compose up -d --wait                       # טרמינל 1 – ממתין עד ש-SQL Server מוכן
export ConnectionStrings__RequestsDb='Server=localhost,1433;Database=RequestsManagement;User Id=sa;Password=Requests_Dev_123!;TrustServerCertificate=True'
dotnet run --project src/Requests.Api
cd src/requests-ui && npm ci && npm start         # טרמינל 2 – http://localhost:4200
```

ב-PowerShell, אותו דבר עם `$env:` במקום `export` – ראו [הרצה עם Docker](#הרצה-עם-docker).

פרטים ודרישות מוקדמות ב[הרצה](#הרצה); אם משהו לא עולה – [פתרון תקלות](#פתרון-תקלות).

### מסלול בדיקה בממשק (3 דקות)

| # | מה עושים | מה אמור לקרות | דרישה |
|---|---|---|---|
| 1 | פותחים את http://localhost:4200 | 100,000 פניות, 20 בעמוד; רצועת סיכום עם סה"כ, "פתוחות מעל 7 ימים" וספירה לכל סטטוס | R1, D |
| 2 | מקלידים "היתר" בחיפוש, עם DevTools → Network פתוח | בקשה אחת אחרי שמפסיקים להקליד (350ms); בקשה קודמת שעוד רצה מסומנת `(canceled)`. הרשימה והסיכום מתעדכנים יחד | R1, R5 |
| 3 | לוחצים על "ממתינה" ברצועת הסיכום, ממיינים לפי עמודה, עוברים לעמוד 3 ולוחצים F5 | הסינון, המיון והעמוד נשמרים ב-URL, והרענון מציג אותה תצוגה | R1, R5 |
| 4 | פותחים פנייה ומעדכנים סטטוס | הודעת הצלחה, שורה חדשה בהיסטוריה, והרשימה והסיכום מתעדכנים | R2, R3 |
| 5 | **409:** מעתיקים את ה-URL של הפנייה הפתוחה (כולל `?id=`) ללשונית שנייה. מעדכנים בלשונית אחת, ואז בשנייה (בלי לרענן) | בלשונית השנייה נפתח חלון: מה המצב עכשיו, מי שינה ומתי, ו"להעביר בכל זאת" / "הצגת המצב העדכני". שום דבר לא נדרס | R2, R5 |
| 6 | **Bulk:** בוחרים כמה פניות. בלשונית אחרת משנים אחת מהן. חוזרים ומפעילים "עדכון כל הנבחרות" | "עודכנו N פניות; לא עודכנה פנייה אחת", עם קישור לפנייה שלא עודכנה ("עודכנה בינתיים על ידי משתמש אחר") | R4 |
| 7 | עוצרים את השרת (Ctrl+C) ומשנים סינון; מפעילים שוב ולוחצים "ניסיון חוזר" | באנר "אין חיבור לשרת" אחד, ואחרי החזרה – הכול נטען מחדש | R5 |
| 8 | http://localhost:5080/swagger | כל ה-Endpoints, כולל קודי השגיאה | R11 |

### לראות 409 משתי פקודות

שני "משתמשים" שולחים עדכון עם אותו `ETag` (Bash / Git Bash):

```bash
ETAG=$(curl -si http://localhost:5080/api/requests/1 | grep -i '^etag:' | cut -d' ' -f2 | tr -d '\r')

# משתמש א' – מצליח (200)
curl -s -o /dev/null -w "A: %{http_code}\n" -X PATCH http://localhost:5080/api/requests/1/status \
  -H "If-Match: $ETAG" -H "Content-Type: application/json" -d '{"status":"InProgress","changedBy":"Dana"}'

# משתמש ב' – אותו ETag, שכבר לא עדכני (409)
curl -s -w "\nB: %{http_code}\n" -X PATCH http://localhost:5080/api/requests/1/status \
  -H "If-Match: $ETAG" -H "Content-Type: application/json" -d '{"status":"Waiting","changedBy":"Yossi"}'
```

תשובה אמיתית (מקוצרת):

```text
A: 200
{"title":"Concurrency conflict","status":409,"detail":"Request 1 was modified by another user. Reload it and try again.",
 "currentState":{"id":1,"status":"InProgress","rowVersion":"AAAAAAAJy9E=","allowedNextStatuses":["Waiting","Completed"],…},
 "lastChange":{"previousStatus":"Completed","newStatus":"InProgress","changedAt":"2026-10-04T18:37:15.906Z","changedBy":"Dana"}}
B: 409
```

פנייה 1 מתחילה ב"הושלמה" בנתוני ה-Seed, ולכן "בטיפול" מותר לה. אם היא כבר שונתה, בחרו סטטוס מתוך `allowedNextStatuses` שב-GET. בלי `If-Match` מתקבל 428, ועם `If-Match: *` מתקבל 400.

### בדיקות אוטומטיות

```bash
dotnet test                                         # 75 בדיקות שרת – Integration מול SQL Server (LocalDB)
cd src/requests-ui && npx ng test --watch=false     # 43 בדיקות לקוח
```

### איפה כל דרישה

| דרישה | קוד | בדיקות | תיעוד |
|---|---|---|---|
| R1 שליפה: דפדוף, סינון, חיפוש, מיון, Aggregations, ולידציה, Cancellation | [`RequestRepository`](src/Requests.Infrastructure/Persistence/RequestRepository.cs), [`RequestSearchQuery`](src/Requests.Application/Requests/Contracts/RequestSearchQuery.cs), [`RequestSummaryService`](src/Requests.Application/Requests/Services/RequestSummaryService.cs) | `SearchRequestsTests`, ‏`SummaryTests` | [API](#api) |
| R2 עדכון סטטוס, מעברים, Optimistic Concurrency | [`RequestCommandService`](src/Requests.Application/Requests/Services/RequestCommandService.cs), [`StatusTransitions`](src/Requests.Application/Requests/Entities/StatusTransitions.cs), [`EntityTags`](src/Requests.Api/Http/EntityTags.cs), [`GlobalExceptionHandler`](src/Requests.Api/ErrorHandling/GlobalExceptionHandler.cs) | `UpdateStatusTests`, ‏`DatabaseConflictTests`, ‏`StatusTransitionTests` | [Concurrency](#עדכון-סטטוס-ו-concurrency) |
| R3 היסטוריה (Audit) | [`RequestStatusHistory`](src/Requests.Application/Requests/Entities/RequestStatusHistory.cs), ‏`GET /api/requests/{id}/history` | `HistoryTests`, וכל בדיקת עדכון בודקת את שורת ההיסטוריה | [API](#api) |
| R4 Bulk עד 100, הצלחה חלקית | [`RequestCommandService`](src/Requests.Application/Requests/Services/RequestCommandService.cs) (`BulkUpdateStatusAsync`), [`UpdateStatusDtos`](src/Requests.Application/Requests/Contracts/UpdateStatusDtos.cs) | `BulkUpdateStatusTests`, ‏`BulkUpdateServiceTests`, ‏`ConcurrencyTests`, ‏`DatabaseConflictTests` | [Bulk](#עדכון-מרוכז-bulk--הצלחה-חלקית) |
| R5 Angular | [`requests-list.store.ts`](src/requests-ui/src/app/features/requests/requests-list.store.ts) (‏URL, ‏`switchMap`), [`request-filters`](src/requests-ui/src/app/features/requests/request-filters.component.ts) (Debounce), [`request-details`](src/requests-ui/src/app/features/requests/request-details.component.ts) + [`conflict-dialog`](src/requests-ui/src/app/features/requests/conflict-dialog.component.ts) (409) | `requests-list.store.spec`, ‏`request-filters.component.spec`, ‏`request-details.component.spec`, ‏`requests-page.component.spec` | [מבנה – הלקוח](#מבנה-הפתרון) |
| R6 ביצועים על 100K | [`ServiceRequestConfiguration`](src/Requests.Infrastructure/Persistence/Configurations/ServiceRequestConfiguration.cs) (אינדקסים) | [`measure.sql`](docs/performance/measure.sql), [`plans/`](docs/performance/plans) | [PERFORMANCE.md](docs/PERFORMANCE.md) |
| R7 Cache | [`MemorySummaryCache`](src/Requests.Infrastructure/Caching/MemorySummaryCache.cs), [`RequestSummaryService`](src/Requests.Application/Requests/Services/RequestSummaryService.cs) | `SummaryCacheTests` | [Cache](#cache) |
| R8 בדיקות, כולל Integration ו-Concurrency | [`tests/Requests.Tests`](tests/Requests.Tests), ‏`*.spec.ts` | 75 + 43 | [בדיקות אוטומטיות](#בדיקות-אוטומטיות) |
| R9 תכנון | – | – | [WORK_PLAN.md](docs/WORK_PLAN.md) |
| R10 איכות קוד, שגיאות, Logging | [`Program.cs`](src/Requests.Api/Program.cs), [`RequestRejectionLog`](src/Requests.Api/ErrorHandling/RequestRejectionLog.cs), [`Directory.Build.props`](Directory.Build.props) (Analyzers כשגיאות) | `Invalid_body_returns_400`, ‏`Unusable_if_match_returns_400_and_changes_nothing` | [Logging](#logging) |
| R11 תיעוד | – | – | README, ‏PERFORMANCE.md, ‏WORK_PLAN.md |
| D ‏100K רשומות, יצירה חוזרת | [`DataSeeder`](src/Requests.Infrastructure/Persistence/DataSeeder.cs), ‏`dotnet run -- seed` | – | [הרצה](#הרצה) |

### פתרון תקלות

| תקלה | פתרון |
|---|---|
| `A network-related or instance-specific error` / ‏LocalDB לא מותקן | `sqllocaldb info` אמור להציג `MSSQLLocalDB`. אם לא – להתקין [SQL Server Express LocalDB](https://learn.microsoft.com/sql/database-engine/configure-windows/sql-server-express-localdb), או להשתמש ב-SQL Server אחר (שורה אחרונה). |
| LocalDB קיים אבל לא עולה | `sqllocaldb stop MSSQLLocalDB` ואז `sqllocaldb start MSSQLLocalDB`. |
| פורט 4200 תפוס | `npm start -- --port 4300` ולפתוח http://localhost:4300. ה-Proxy ל-API עובד מכל פורט. |
| פורט 5080 תפוס | לעצור את התהליך שתופס אותו, או להריץ `dotnet run --project src/Requests.Api -- --urls http://localhost:5081` ולעדכן את `target` ב-[`proxy.conf.json`](src/requests-ui/proxy.conf.json). |
| SQL Server אחר (Express, Developer, Docker) | `ConnectionStrings__RequestsDb="Server=.\SQLEXPRESS;Database=RequestsManagement;Trusted_Connection=True;TrustServerCertificate=True"` לפני `dotnet run`. לבדיקות: ‏`REQUESTS_TEST_SERVER=.\SQLEXPRESS` (שם השרת בלבד). |
| Docker: ‏`Login failed for user 'sa'` או שהשרת לא עולה | `docker compose ps` צריך להציג `healthy` – ב-`up -d --wait` הפקודה ממתינה לזה. פורט 1433 תפוס (SQL Server מקומי): לשנות ב-`docker-compose.yml` ל-`"1434:1433"` ובמחרוזת ל-`Server=localhost,1434`. ‏Mac עם Apple Silicon: התמונה היא amd64 – להפעיל ב-Docker Desktop את "Use Rosetta for x86_64/amd64 emulation". |
| הממשק מציג "אין חיבור לשרת" | השרת לא רץ או עדיין יוצר את הנתונים בהרצה הראשונה – לחכות לשורה `Now listening on` וללחוץ "ניסיון חוזר". |

## טכנולוגיות וגרסאות

| שכבה | טכנולוגיה |
|---|---|
| שרת | .NET 8, ASP.NET Core Web API (Controllers), EF Core 8.0.31 |
| בסיס נתונים | SQL Server – ברירת מחדל LocalDB (נבדק על LocalDB 2019) |
| לקוח | Angular 21 (Standalone, Signals, Zoneless), Angular Material 21, RxJS 7.8 |
| בדיקות שרת | xUnit, FluentAssertions 7.2.2, `WebApplicationFactory` מול SQL Server אמיתי |
| בדיקות לקוח | Vitest (דרך `ng test`) |
| נתוני בדיקה | Bogus (Seed קבוע) + SqlBulkCopy |
| תיעוד API | Swagger (Swashbuckle) |

## הרצה

**דרישות:** ‏.NET SDK 8 · ‏Node.js ‏20.19+ או 22.12+ · ‏SQL Server LocalDB (מותקן עם Visual Studio).

```bash
# 1. השרת – בהרצה הראשונה נוצרים בסיס הנתונים ו-100,000 פניות (כ-15 שניות)
dotnet run --project src/Requests.Api
#    API:     http://localhost:5080
#    Swagger: http://localhost:5080/swagger

# 2. הלקוח – בטרמינל נוסף
cd src/requests-ui
npm ci
npm start
#    http://localhost:4200   (פורט תפוס? npm start -- --port 4300)
```

`proxy.conf.json` מעביר את `/api` לשרת, כך שאין צורך ב-CORS.

**יצירה מחדש של נתוני הבדיקה** (מוחק הכול ויוצר מחדש, עם אותו Seed – אותם נתונים):

```bash
dotnet run --project src/Requests.Api -- seed          # 100,000
dotnet run --project src/Requests.Api -- seed 250000   # כמות אחרת
```

**בדיקות:**

```bash
dotnet test                                    # 75 בדיקות שרת
cd src/requests-ui && npx ng test --watch=false   # 43 בדיקות לקוח
```

### הרצה עם Docker

ל-Mac, ל-Linux, או ל-Windows בלי LocalDB. דרוש Docker עם Compose (Docker Desktop, או Docker Engine עם התוסף `compose`). הקובץ [`docker-compose.yml`](docker-compose.yml) מריץ SQL Server 2022 בפורט 1433.

**Bash (Mac / Linux / Git Bash):**

```bash
docker compose up -d --wait
export ConnectionStrings__RequestsDb='Server=localhost,1433;Database=RequestsManagement;User Id=sa;Password=Requests_Dev_123!;TrustServerCertificate=True'
dotnet run --project src/Requests.Api              # בהרצה הראשונה: בסיס נתונים + 100,000 פניות

# בדיקות השרת מול הקונטיינר (כל מחלקת בדיקות יוצרת ומוחקת בסיס נתונים משלה)
export REQUESTS_TEST_CONNECTION='Server=localhost,1433;User Id=sa;Password=Requests_Dev_123!;TrustServerCertificate=True'
dotnet test

docker compose down -v                             # עצירה ומחיקת הנתונים
```

**PowerShell (Windows):**

```powershell
docker compose up -d --wait
$env:ConnectionStrings__RequestsDb = 'Server=localhost,1433;Database=RequestsManagement;User Id=sa;Password=Requests_Dev_123!;TrustServerCertificate=True'
dotnet run --project src/Requests.Api

$env:REQUESTS_TEST_CONNECTION = 'Server=localhost,1433;User Id=sa;Password=Requests_Dev_123!;TrustServerCertificate=True'
dotnet test

docker compose down -v
```

הלקוח זהה בשני המסלולים (`npm ci`, `npm start`). הסיסמה ב-`docker-compose.yml` מיועדת לפיתוח מקומי בלבד.

**SQL Server אחר:** ב-`src/Requests.Api/appsettings.json` (‏`ConnectionStrings:RequestsDb`) או במשתנה סביבה `ConnectionStrings__RequestsDb`. לבדיקות: `REQUESTS_TEST_SERVER` (שם שרת, Windows Authentication) או `REQUESTS_TEST_CONNECTION` (מחרוזת התחברות מלאה). בשני המקרים כל מחלקת בדיקות יוצרת ומוחקת בסיס נתונים משלה.

## מבנה הפתרון

```text
src/
  Requests.Api/              HTTP בלבד: Controller, טיפול בשגיאות (Problem Details), ETag/If-Match, הרכבת DI
  Requests.Application/      הלוגיקה – בלי EF Core ובלי ASP.NET Core
    Requests/Entities/         ServiceRequest, StatusTransitions (מעברים מותרים), RequestStatusHistory
    Requests/Contracts/        DTOs, פרמטרי סינון וּולידציה
    Requests/Services/         קריאה, עדכון (כולל Bulk), נתונים מסכמים
    Requests/Abstractions/     IRequestRepository, ISummaryCache – ממומשים ב-Infrastructure
  Requests.Infrastructure/   EF Core: DbContext, קונפיגורציה ואינדקסים, Migrations, Repository, Cache, Seeder
  requests-ui/               Angular
tests/
  Requests.Tests/            בדיקות Domain, Service (עם Repository מדומה) ו-Integration מול SQL Server
docs/                        תוכנית עבודה, ביצועים
```

**כיוון התלויות:** ‏`Api → Application ← Infrastructure`. ‏Application לא מכיר אף שכבה אחרת. הוא מגדיר את הממשקים, ו-Infrastructure מממש אותם. ה-API מכיר את Infrastructure רק דרך `AddInfrastructure()`, ‏`InitializeDatabaseAsync()` ו-`ReseedDatabaseAsync()`. כך הקומפיילר עצמו אוכף את ההפרדה – למשל, אי אפשר להשתמש ב-`DbContext` מתוך שירות ב-Application.

**Lifetimes:** ‏`DbContext`, ה-Repository והשירותים – Scoped (לבקשה). ‏`ISummaryCache` ו-`TimeProvider` – Singleton (ה-Cache מחזיק טוקן Invalidation משותף לכל הבקשות).

**הלקוח:**
* `RequestsApi` – HTTP בלבד, בלי מצב.
* `RequestsListStore` – מצב הרשימה. **ה-URL הוא מקור האמת**: כל פעולה מעדכנת את ה-URL, והרשימה נטענת לפיו (רענון, קישור וכפתור Back מציגים את אותה תצוגה). כל שינוי עובר דרך `switchMap`, שמבטל בקשה קודמת שעוד לא חזרה.
* `RequestsPageComponent` מחבר בין ה-Store לרכיבים; `RequestFilters`, `RequestsTable`, `BulkStatusBar` – רכיבי תצוגה (`input()`/`output()`); ‏`RequestDetails` ו-`SummaryPanel` טוענים את הנתונים שלהם לפי מזהה/סינון.
* ממשק בעברית מימין לשמאל, Angular Material:
  * **רצועת סיכום** אחת – סה"כ, פתוחות מעל 7 ימים וספירה לכל סטטוס (לחיצה מסננת לפיו); פילוחי עדיפות ומטפלים נפתחים לפי בקשה.
  * **חיפוש** בולט אחד; סטטוס ועדיפות כצ'יפים; ארגון, מטפל וטווח תאריכים (לוח שנה בעברית, הקלדה יום/חודש/שנה) תחת "סינון מתקדם"; הסינונים הפעילים מוצגים כצ'יפים שאפשר להסיר.
  * **פרטי פנייה** במגירה שנפתחת מעל הרשימה, כך שהטבלה שומרת על רוחבה.
  * **הודעות** שנבנות ממה שהשרת החזיר: איזה שדה לא תקין, אילו מעברים מותרים, איזו פנייה לא נמצאה, וקוד לפנייה לתמיכה בתקלת שרת. כשאין חיבור לשרת – באנר אחד עם "ניסיון חוזר" במקום שגיאה בכל חלק במסך.

## API

| Method | Endpoint | תיאור | קודים |
|---|---|---|---|
| GET | `/api/requests` | רשימה – סינון, חיפוש, מיון ודפדוף בצד השרת | 200, 400 |
| GET | `/api/requests/summary` | נתונים מסכמים לאותם סינונים | 200, 400 |
| GET | `/api/requests/{id}` | פנייה + `allowedNextStatuses`; כותרת `ETag` | 200, 404 |
| GET | `/api/requests/{id}/history` | היסטוריית שינויי סטטוס, מהחדש לישן | 200, 404 |
| PATCH | `/api/requests/{id}/status` | עדכון סטטוס `{ status, changedBy }` + כותרת `If-Match` | 200, 400, 404, **409**, 422, 428 |
| POST | `/api/requests/bulk/status` | עדכון עד 100 פניות `{ status, changedBy, items: [{ id, rowVersion }] }` | 200, 400 |

**פרמטרי הסינון** (ברשימה וב-Summary, כולם אופציונליים, משולבים ב-AND):
`search` – מכיל, בכותרת או בשם הארגון · `status` / `priority` – אחד או יותר (`?status=New&status=Waiting`) · `organizationName` – מתחיל ב- · `assignedTo` – חלק משם המטפל · `createdFrom` / `createdTo` – טווח כולל (UTC).
ברשימה בלבד: `page` (1–100,000), `pageSize` (1–100, ברירת מחדל 20), `sortBy` (`createdAt` · `updatedAt` · `priority` · `status` · `title` · `organizationName`), `sortDirection` (`asc` · `desc`).

**ולידציה ושגיאות:** פרמטר לא חוקי (`pageSize=500`, `status=99`, `sortBy=password`, טווח תאריכים הפוך, ערך enum לא מוכר בגוף הבקשה) מחזיר **400** עם השדה הבעייתי. כל שגיאה מוחזרת כ-**Problem Details** (RFC 7807) מ-`GlobalExceptionHandler`. שגיאה לא צפויה נרשמת בלוג ומוחזרת כ-500 בלי פרטים פנימיים. Enums מוחזרים כשמות (`"InProgress"`) בכל תשובה, כולל גוף של שגיאה.

**CancellationToken** עובר מה-Controller ועד שאילתת EF. כשהלקוח מבטל (`switchMap`), השאילתה ב-SQL Server מבוטלת, ונרשמת שורת Information ולא Error.

## בסיס הנתונים

```text
Requests                                  RequestStatusHistory
  Id               int identity PK          Id              bigint identity PK
  Title            nvarchar(200)            RequestId       int FK → Requests
  OrganizationName nvarchar(200)            PreviousStatus  tinyint
  Status           tinyint                  NewStatus       tinyint
  Priority         tinyint                  ChangedAt       datetime2(3)  UTC
  AssignedTo       nvarchar(100) null       ChangedBy       nvarchar(100)
  CreatedAt        datetime2(3)  UTC
  UpdatedAt        datetime2(3)  UTC
  RowVersion       rowversion
```

* `Status` ו-`Priority` כ-`tinyint` עם ערכים מפורשים: אינדקסים צרים, ושינוי סדר ב-enum לא משנה נתונים.
* אורכי השדות מוגדרים פעם אחת (`RequestFieldLimits`) ומשמשים גם את הוולידציה וגם את הסכמה.
* כל התאריכים ב-UTC.

**אינדקסים** (הנימוקים והמדידות המלאות ב-[PERFORMANCE.md](docs/PERFORMANCE.md)):

| אינדקס | בשביל מה |
|---|---|
| `IX_Requests_CreatedAt` | מיון ברירת המחדל וטווח תאריכים – "20 האחרונות" בלי למיין את כל הטבלה (3ms מול 162ms) |
| `IX_Requests_Status_CreatedAt` INCLUDE `Priority, UpdatedAt` | סינון לפי סטטוס, ומכסה את כל שאילתת ה-Summary (328 מול 1,999 reads) |
| `IX_Requests_AssignedTo_Status` | Top מטפלים וסינון לפי מטפל |
| `IX_Requests_OrganizationName` | ארגון לפי תחילית – Seek |
| `IX_RequestStatusHistory_RequestId_ChangedAt` | היסטוריה של פנייה |

## ביצועים ב-API

המדידות המלאות – תוכניות ביצוע, השוואה עם ובלי כל אינדקס, והסקריפט להרצה חוזרת – ב-[docs/PERFORMANCE.md](docs/PERFORMANCE.md). כל המספרים נמדדו על 100,000 פניות.

**איך נמנעת טעינת כל הנתונים לזיכרון:**
* ה-Repository בונה שאילתה אחת – סינון, מיון, `OFFSET/FETCH` והטלה ל-DTO – שרצה כולה ב-SQL Server. רק העמוד המבוקש (עד 100 שורות) יוצא מבסיס הנתונים, ורק העמודות שבתשובה.
* מספר העמודים מגיע מ-`COUNT(*)` נפרד עם אותם סינונים. עמוד שאחרי הסוף מוחזר ריק בלי לשאול את בסיס הנתונים.
* הנתונים המסכמים הם `GROUP BY` בבסיס הנתונים, שמחזיר עד 12 שורות.
* הלקוח לא מסנן, לא ממיין ולא מדפדף בעצמו. כל שינוי שולח בקשה חדשה, ובקשה קודמת שעוד לא חזרה מבוטלת – גם בשרת, דרך ה-`CancellationToken`.

**זמני תגובה של ה-API** (ממוצע של 20 קריאות):

| קריאה | זמן |
|---|---|
| עמוד ראשון, ללא סינון | 22ms |
| סינון לפי סטטוס | 13ms |
| ארגון + טווח תאריכים | 18ms |
| עמוד 500 | 48ms |
| חיפוש טקסט חופשי | **363ms** – צוואר הבקבוק, ראו למטה |
| נתונים מסכמים ללא סינון (מה-Cache) | 2ms |
| נתונים מסכמים עם סינון (מחושבים) | ~90ms |
| עדכון סטטוס (UPDATE מותנה + היסטוריה) | ~1ms בבסיס הנתונים |

**מה האינדקסים חוסכים:** בלי אינדקס, כל שאילתה סורקת את כל הטבלה (1,999 דפים). עם האינדקסים: עמוד ראשון – 3ms במקום 162ms; ספירה לפי סטטוס – 104 דפים; ארגון + תאריכים – 63 דפים; הנתונים המסכמים – 328 דפים. תוכניות הביצוע בפועל שמורות ב-[`docs/performance/plans`](docs/performance/plans).

**שלושה דברים שהמדידות גילו ותוקנו:**
* EF Core 8 תרגם את סינון הסטטוס ל-`IN (SELECT … FROM OPENJSON(…))`, ו-SQL Server סרק את האינדקס במקום לחפש בו (Seek). הסינון נבנה עכשיו כ-`Status = @p0 OR Status = @p1` – פי 3 פחות קריאות.
* אחרי טעינת 100,000 הפניות האינדקסים היו מפוצלים ב-97–99%. ה-Seeder בונה אותם מחדש בסוף הטעינה.
* לשאילתת הנתונים המסכמים חסרה עמודה באינדקס, והיא סרקה את כל הטבלה: 1,999 → 324 דפים אחרי התיקון.

**צוואר הבקבוק:** חיפוש "מכיל" (`LIKE '%x%'`) לא יכול להשתמש באינדקס, וה-COUNT שלו סורק את כל הטבלה. רוב הזמן הוא CPU של השוואת מחרוזות. ניסוי עם Collation בינארי הוריד את אותה שאילתה מ-363ms ל-45ms (פי 8) – זה השיפור המוצע (פירוט ב[מגבלות](#מגבלות-ידועות)).

## עדכון סטטוס ו-Concurrency

**הבעיה:** שני משתמשים פותחים את אותה פנייה. הראשון מעביר אותה ל"בטיפול", השני – שעדיין רואה את המצב הקודם – ל"ממתינה". בלי הגנה, העדכון השני דורס את הראשון ואף אחד לא יודע.

**הפתרון – עדכון מותנה בגרסה:**
* לכל פנייה עמודת `rowversion`, ש-SQL Server מעדכן בעצמו בכל שינוי.
* `GET /api/requests/{id}` מחזיר אותה ככותרת `ETag`. ‏`PATCH` חייב לשלוח אותה ב-`If-Match`.
* ה-Repository מגדיר אותה כגרסה הצפויה, ו-EF שולח: `UPDATE … WHERE Id = @id AND RowVersion = @expected`.
* אם מישהו אחר עדכן בינתיים, אף שורה לא מתעדכנת, וחוזר **409**. הבדיקה מתבצעת **בתוך ה-UPDATE**, כך שאין חלון בין הבדיקה לכתיבה (בדיקה עם 5 עדכונים מקבילים: בדיוק אחד מצליח, ונכתבת בדיוק שורת היסטוריה אחת).
* העדכון ושורת ההיסטוריה נשמרים באותה טרנזקציה. `UpdatedAt` מתעדכן יחד עם הסטטוס, מעוגל לדיוק העמודה, כדי שהערך שחוזר ללקוח יהיה בדיוק הערך השמור.

| מצב | קוד |
|---|---|
| עודכן | 200 + `ETag` חדש |
| גוף לא תקין (סטטוס לא מוכר, `changedBy` ריק) | 400 |
| `If-Match` חסר | 428 |
| `If-Match: *`, תג חלש, כמה תגים או ערך שאינו גרסה | 400 – `*` פירושו "דרוס מה שיש", בדיוק מה שהמנגנון נועד למנוע |
| הפנייה לא קיימת | 404 |
| הגרסה לא עדכנית | **409** – עם `currentState` (הפנייה כפי שהיא עכשיו) ו-`lastChange` (מי שינה ומתי) |
| מעבר סטטוס אסור | 422 – עם `currentStatus` ו-`allowedStatuses` |

**למה 409 ולא 412:** ‏412 (Precondition Failed) הוא הקוד המדויק לכישלון של `If-Match`, אבל הוא בלי גוף, והלקוח היה צריך בקשה נוספת כדי להבין מה קרה. ב-409 השרת מחזיר את המצב הנוכחי ואת מי ששינה אותו, וזה מה שהממשק צריך כדי להסביר למשתמש.

**בממשק:** ב-409 נפתח חלון: מה המצב עכשיו, מי שינה ומתי, ומה המשתמש ניסה לשמור (ושזה לא נשמר). שתי אפשרויות: **"הצגת המצב העדכני"**, או **"להעביר בכל זאת"** – שליחה חוזרת מול הגרסה העדכנית, שמוצעת רק אם המעבר עדיין מותר מהמצב החדש. המערכת לא מחליטה מי צודק; המשתמש מחליט.

**מעברי סטטוס מותרים** (האפיון דורש לחסום מעברים לא מותרים אך לא מגדיר אותם – זו ההנחה):

| מ- | אל |
|---|---|
| חדשה | בטיפול, ממתינה |
| בטיפול | ממתינה, הושלמה |
| ממתינה | בטיפול, הושלמה |
| הושלמה | בטיפול (פתיחה מחדש) |

הכללים נמצאים במקום אחד (`StatusTransitions`), והשרת מחזיר `allowedNextStatuses` כדי שהממשק יציג רק אפשרויות חוקיות.

## עדכון מרוכז (Bulk) – הצלחה חלקית

`POST /api/requests/bulk/status` מקבל עד 100 פניות, ולכל אחת הגרסה שהמשתמש קרא. כותרת `If-Match` אחת לא יכולה לשאת 100 גרסאות, ולכן כאן הגרסה נמצאת בגוף הבקשה.

1. ולידציה של הבקשה כולה (1–100 פריטים, בלי כפילויות, סטטוס חוקי). אם היא נכשלת – 400, ושום דבר לא מתעדכן.
2. כל הפניות נטענות בשאילתה אחת.
3. כל פריט נבדק לחוד: לא קיים → `NotFound`; גרסה לא עדכנית → `Conflict`; מעבר אסור → `InvalidTransition`.
4. כל הפריטים התקינים נשמרים **יחד, בטרנזקציה אחת**, כל אחד עם שורת היסטוריה.
5. אם בזמן השמירה מתברר שפנייה שונתה (בין הקריאה לכתיבה), בסיס הנתונים מבטל את כל הטרנזקציה. השירות מסמן רק את הפנייה הזו כ-`Conflict`, מוציא אותה ושומר שוב את השאר. כל סבב מוציא לפחות פנייה אחת, כך שהלולאה מסתיימת תמיד.
6. התשובה: **200**, עם סיכום ותוצאה לכל פריט, באותו סדר כמו בבקשה.

תשובה אמיתית – שלוש פניות, ואחת מהן שונתה על ידי משתמש אחר אחרי שנבחרה:

```json
{ "requested": 3, "succeeded": 2, "failed": 1,
  "results": [
    { "id": 59749, "outcome": "Updated",  "error": null, "rowVersion": "AAAAAAAHO5M=" },
    { "id": 75773, "outcome": "Conflict", "error": "The request was modified by another user.", "rowVersion": null },
    { "id": 50312, "outcome": "Updated",  "error": null, "rowVersion": "AAAAAAAHO5I=" } ] }
```

**למה הצלחה חלקית ולא "הכול או כלום":** כשמעדכנים עשרות פניות במערכת עם הרבה משתמשים, סביר שאחת מהן שונתה בינתיים. ב"הכול או כלום" פנייה אחת הייתה חוסמת את כל הפעולה, והמשתמש היה צריך לרענן ולנסות שוב בלי לדעת מה השתנה. כאן מתעדכן מה שאפשר, וכל מה שלא עודכן מופיע עם הסיבה – בממשק, בעברית, עם קישור שפותח את הפנייה. כל פריט עדיין מוגן בבדיקת גרסה, כך שגם כאן אין דריסה.
**למה 200 ולא 207:** ‏207 Multi-Status שייך ל-WebDAV, ולקוחות רבים לא מטפלים בו. הבקשה עצמה עובדה; הפירוט בגוף.

## Cache

**מה נשמר:** הנתונים המסכמים של **התצוגה ללא סינון** – המסך שכל משתמש מגיע אליו, והחישוב היקר ביותר (כל הטבלה). תצוגות מסוננות מחושבות בכל בקשה: הן מצומצמות יותר, משתמשות באינדקסים, ויש להן אינסוף צירופים.

**למה לא את הרשימה:** לפני עדכון, המשתמש צריך לראות את המצב האמיתי. רשימה ישנה הייתה מובילה בדיוק להתנגשויות.

**המימוש (`MemorySummaryCache`, ‏`IMemoryCache`):**
* **תפוגה:** 60 שניות (`Cache:SummaryTtlSeconds`) – רשת ביטחון, גם לשינויים שלא עברו דרך ה-API.
* **Invalidation:** כל עדכון סטטוס שנשמר (בודד או מרוכז) מנקה את ה-Cache אחרי ה-Commit.
* **מרוץ בין חישוב לעדכון:** כל ערך נשמר עם טוקן שנלקח **לפני** תחילת החישוב. אם עדכון נשמר בזמן שהחישוב רץ, הטוקן כבר מבוטל, והערך שחושב נזרק מיד – במקום להישמר כמידע ישן ל-60 שניות. יש לזה בדיקה, ווידאתי שהיא נכשלת כשמסירים את המנגנון.
* תוצאה: 2ms מה-Cache, מול 50–90ms לחישוב.

**כמה מופעי שרת:** ‏`IMemoryCache` שייך למופע אחד. ניקוי במופע A לא מנקה את מופע B, שימשיך להציג נתונים ישנים עד שיפוג התוקף. ההתאמה הנדרשת:
1. **Cache משותף (Redis)** דרך `IDistributedCache`, עם מפתח אחד. ה-Invalidation מוחק את המפתח ב-Redis, וכל המופעים רואים את זה. ה-`ISummaryCache` כבר מבודד את זה – מחליפים רק את המימוש ב-Infrastructure.
2. אם רוצים להשאיר גם Cache מקומי מהיר בכל מופע: הודעת Invalidation דרך **Redis Pub/Sub**, שכל מופע מאזין לה ומנקה את העותק המקומי.
3. בשני המקרים התפוגה נשארת כרשת ביטחון.

## שתי החלטות טכנולוגיות משמעותיות

### 1. Optimistic Concurrency עם `rowversion` ו-`ETag`/`If-Match`

| חלופה | למה לא |
|---|---|
| נעילה פסימית (נעילת השורה בזמן עריכה) | HTTP חסר מצב: אין דרך אמינה לדעת שמשתמש סגר את הדפדפן, ונשארות נעילות יתומות. ההתנגשויות כאן נדירות – אין סיבה לשלם על נעילה בכל עדכון. |
| עמודת `Version int` שמעדכנים בקוד | עובד, אבל כל עדכון עתידי (גם סקריפט) חייב לזכור להעלות אותה. `rowversion` מתעדכן על ידי בסיס הנתונים תמיד. |
| השוואת `UpdatedAt` | שני עדכונים באותו מילי-שנייה, או שעונים שונים בשרתים, יכולים לעבור את הבדיקה. |
| גרסה בגוף הבקשה | עובד, אבל `If-Match` הוא המנגנון התקני של HTTP לעדכון מותנה, וכלים ולקוחות מכירים אותו. ב-Bulk הגרסה בגוף, כי כותרת אחת לא מכילה 100 גרסאות. |

### 2. Cache רק לנתונים המסכמים של התצוגה ללא סינון – בזיכרון השרת, עם Invalidation בכל עדכון

**ההחלטה:** נשמרת ב-Cache תוצאה אחת בלבד – הנתונים המסכמים כשאין סינון. היא נשמרת ב-`IMemoryCache` של השרת עם תפוגה של 60 שניות, ומתנקה מיד בכל עדכון סטטוס (בודד או מרוכז). כל השאר – הרשימה, ונתונים מסכמים עם סינון – מחושב בכל בקשה.

**למה דווקא זה:** זה המסך שכל משתמש מגיע אליו, התשובה זהה לכולם, והחישוב הוא היקר ביותר במערכת (אגרגציה על כל 100,000 הפניות, 50–90ms). מה-Cache הוא חוזר ב-2ms.

| חלופה | למה לא |
|---|---|
| בלי Cache – להסתמך על האינדקסים | האינדקס כבר מוריד את השאילתה ל-328 דפים, אבל היא עדיין סוכמת 100,000 שורות בכל טעינת מסך, אצל כל משתמש. |
| Cache לכל צירוף סינון | יש אינסוף צירופים (חיפוש חופשי, טווחי תאריכים), ולכן מעט פגיעות, זיכרון שצריך להגביל, וכל עדכון צריך לנקות את כולם. התצוגות המסוננות ממילא מצומצמות ומשתמשות באינדקסים. |
| Cache גם לרשימת הפניות | המשתמש בוחר פנייה ומעדכן לפי מה שהוא רואה. רשימה ישנה הייתה מייצרת יותר התנגשויות (409) – בדיוק הבעיה שה-Concurrency פותר. |
| תפוגה בלבד, בלי Invalidation | אחרי שהמשתמש עדכן פנייה, הספירות לא היו מתאימות לרשימה עד 60 שניות. |
| Cache מבוזר (Redis) | המנגנון הנכון כשיש כמה מופעי שרת, אבל מוסיף תשתית שהבודק צריך להריץ – ויש כאן מופע אחד. ה-Cache מאחורי `ISummaryCache`, כך שהמעבר ל-Redis הוא החלפת מימוש ב-Infrastructure בלבד (פירוט בסעיף [Cache](#cache)). |
| Indexed View ב-SQL Server (אגרגציה שבסיס הנתונים מעדכן בעצמו) | תמיד מעודכן בלי Invalidation, אבל כל עדכון סטטוס משלם על תחזוקת ה-View, ו"פתוחות יותר מ-7 ימים" תלוי בשעת השאילתה – אי אפשר לחשב אותו מראש. |
| Output Cache של ASP.NET Core (שמירת תשובת ה-HTTP כולה) | עובד, אבל מעביר לשכבת ה-HTTP החלטות של הלוגיקה – מתי לשמור (רק בלי סינון) ומתי לנקות (אחרי עדכון) – ולא מטפל במרוץ שבין חישוב לעדכון. |

**המרוץ שצריך לטפל בו:** אם עדכון נשמר בזמן שהנתונים המסכמים מחושבים, החישוב עלול להסתיים אחרי הניקוי ולשמור תוצאה ישנה ל-60 שניות. לכן כל ערך נשמר עם טוקן שנלקח לפני תחילת החישוב. עדכון מבטל את הטוקן, וערך שנשמר עם טוקן מבוטל נזרק מיד. יש לכך בדיקה, ווידאתי שהיא נכשלת כשמסירים את הטוקן.

### החלטות נוספות בקצרה

* **ה-URL כמקור האמת של מצב הרשימה** – רענון, קישור ו-Back שומרים את התצוגה, כולל הפנייה הפתוחה.
* **Summary כ-Facets** – הפילוח לפי סטטוס מתעלם רק מסינון הסטטוס (ולפי עדיפות – רק מסינון העדיפות), כדי שיראה כמה פניות יש בקטגוריות האחרות. כל השאר מכבד את כל הסינונים, והסה"כ תמיד שווה לסה"כ ברשימה.
* **.NET 8** – הגרסה הנפוצה בסביבות ארגוניות, כדי שהפתרון ירוץ אצל הבודקים בלי להתקין SDK חדש.
* **בדיקות מול SQL Server אמיתי** – ספק ה-InMemory של EF לא אוכף `rowversion`. בדיקת Concurrency מולו הייתה עוברת גם אם המנגנון שבור.

## מגבלות ידועות

| מגבלה | השפעה (נמדד) | תיקון | הערכה |
|---|---|---|---|
| חיפוש "מכיל" (`LIKE '%x%'`) לא משתמש באינדקס | ה-COUNT סורק את כל הטבלה: ‏343ms CPU; ‏API ‏363ms; ‏Summary עם חיפוש 524ms | עמודה מחושבת `PERSISTED` עם Collation בינארי (בניסוי: 45ms, פי 8); בייצור – Full-Text Search | 3 שעות |
| אין אימות – `changedBy` מגיע מהלקוח | ההיסטוריה מתעדת את מה שהלקוח שלח, לא את מי שבאמת עדכן | שם המשתמש מתוך ה-Token (JWT / Windows Auth), לא מגוף הבקשה | 4 שעות |
| Cache בזיכרון של מופע אחד | בכמה מופעים, מופע אחר מציג סיכום ישן עד 60 שניות | `IDistributedCache` (Redis) מאחורי `ISummaryCache` | 3 שעות |
| Summary מסונן רק לפי סטטוס/עדיפות מחושב בכל פעם | 89ms במקום 2ms, אף שהשאילתה זהה לתצוגה ללא סינון | Cache לקבוצות לפי "שאר הסינונים" | 2 שעות |
| דפדוף עמוק עם `OFFSET` | עמוד 500: סריקה + מיון, 46ms ב-SQL, ‏48ms ב-API; גדל עם מספר העמוד | Keyset pagination (ויתור על קפיצה לעמוד N) | 4 שעות |
| `COUNT(*)` בכל בקשה | 213 reads ‏(14ms) גם בלי סינון; גדל ליניארית | ספירה משוערת מ-`sys.dm_db_partition_stats` כשאין סינון | שעה |

**שיפורים לפי עדיפות:** 1. אימות ל-`changedBy` (נכונות ההיסטוריה) · 2. חיפוש טקסט (צוואר הבקבוק הגדול ביותר שנמדד) · 3. Cache לסיכום לפי שאר הסינונים (זול, 89ms → 2ms) · 4. Cache מבוזר (כשעוברים לכמה מופעים) · 5. Keyset pagination.

## Logging

* עדכון סטטוס – Information: מזהה, סטטוס קודם וחדש. Bulk – Information: כמה הצליחו וכמה לא.
* 404/409/422 – Warning; שגיאה לא צפויה – Error עם ה-Exception, פעם אחת (הרישום הכפול של ה-Middleware כבוי).
* 400 של ולידציה ו-`If-Match` לא שמיש (428/400) – Warning עם שמות השדות והנתיב בלבד, בלי הערכים שנשלחו.
* כל הודעות הלוג מוגדרות עם `[LoggerMessage]` (Source Generator): בלי עלות כשהרמה כבויה, ונבדקות בקומפילציה.
* לא נרשמים גופי בקשות, מחרוזות התחברות או פרטי משתמש מעבר למזהה הפנייה. פקודות SQL לא נרשמות כברירת מחדל.

## שימוש בכלי AI

הפתרון פותח עם **Claude Code** (Anthropic), כלי AI שעובד בטרמינל.

| שלב | מה עשה הכלי | מה החלטתי | מה בדקתי |
|---|---|---|---|
| תכנון | ניתוח האפיון, פירוק למשימות והערכות, הצגת חלופות לכל החלטה | כל החלטה, אחת-אחת: SQL Server, ‏.NET 8 ו-Angular 21, מבנה השכבות, הצלחה חלקית ב-Bulk, חיפוש מטפל "מכיל", פרטים במגירה עם מזהה ב-URL, נתוני בדיקה בדיוניים בעברית | אישרתי את תוכנית העבודה וההערכות לפני הביצוע |
| קוד | כתיבת רוב הקוד – שרת, לקוח, בדיקות, Seeder | Source-generated logging, ‏DTOs מוטלים בבסיס הנתונים, שרשרת OR במקום OPENJSON; הפכתי בעצמי את עמודות הסטטוס והעדיפות ללא-ממוינות | [TO FILL: what I personally reviewed] |
| ממשק | הצעות שיפור ומימושן; בדיקות בדפדפן (Playwright) | אילו שיפורים לבצע: פריסה, שדה חיפוש, הודעות שגיאה מפורטות, תפריט משתמש, צבעים רכים, סרגל Bulk צף | הרצתי את המערכת בדפדפן ומצאתי: מסך עמוס, שדה חיפוש לא פרופורציונלי, הודעות כלליות מדי, שדה שם משתמש, כותרות ומרווחים בהיסטוריה |
| ביצועים | מדידות, תוכניות ביצוע, איתור ותיקון שלוש בעיות | בחירת ה-Cache כהחלטה הטכנולוגית המרכזית | ביקשתי הסבר על ביצועי ה-API ב-README |
| סקירה | סקירת קוד (Logging, ולידציה); בדיקות Concurrency אמיתיות; תוכניות ביצוע; מדריך לבודק | בחרתי מתוך ממצאי הסקירה מה לתקן ומה לא (404 נשאר Warning; אין אורך מינימלי לחיפוש) | העברתי את הפתרון לסוקר חיצוני, ובחנתי איתו את ששת ההערות |
| תיעוד | ניסוח המסמכים | – | עברתי על ה-README לפני ההגשה |

**מה הכלי אימת, ולא רק כתב:**
* 75 בדיקות שרת ו-43 בדיקות לקוח רצות ועוברות. בבדיקות הקריטיות הוסר המנגנון בכוונה, כדי לוודא שהבדיקה נכשלת בלעדיו: מרוץ ב-Cache, ‏`If-Match` בלקוח, באנר החיבור, בדיקת הגרסה של SQL Server בין קריאה לשמירה, ורענון הרשימה אחרי 409 ו-Bulk.
* כל מספר ב-[PERFORMANCE.md](docs/PERFORMANCE.md) נמדד, וה-SQL נלקח מהלוג של EF. תוכניות הביצוע בפועל שמורות ב-[`docs/performance/plans`](docs/performance/plans).
* פקודות ה-409 ב[מדריך לבודק](#לראות-409-משתי-פקודות) הורצו מול ה-API, והפלט שבמדריך אמיתי.

**בעיות שהמדידות והבדיקות מצאו, ותוקנו:**
* EF Core 8 תרגם את סינון הסטטוס ל-`OPENJSON`, ו-SQL Server סרק במקום Seek (פי 3 קריאות).
* אחרי טעינה מרוכזת האינדקסים היו מפוצלים ב-97–99%.
* שאילתת ה-Summary החדשה סרקה את כל הטבלה – חסרה עמודה באינדקס.
* גוף ה-409 החזיר סטטוס כמספר (`1`) במקום כשם, כי לשגיאות יש Serializer נפרד.
* ערכת הנושא של Material מגדירה Roboto בלי חלופה, והטקסט הוצג בגופן Serif.
* בדיקה של תווים מיוחדים בחיפוש (`%`, `_`, `[`) הייתה חלשה מדי ותוקנה.
* Esc לא סגר את מגירת הפרטים כשהפוקוס נשאר ברשימה.
* כשה-API כבוי, שרת הפיתוח מחזיר 500 ריק ולא "אין תשובה", ולכן במקום באנר החיבור הופיעה "תקלה בשרת".
* תיבת הסינון הייתה גבוהה כמעט פי 2 מהנדרש (חישוב גובה של Flexbox ברוחב מינימלי).
* פריט ריק (`null`) ב-Bulk החזיר 500 במקום 400.
* Esc בתוך רשימה נפתחת סגר גם את מגירת הפרטים.
* שגיאה לא צפויה נרשמה בלוג פעמיים.

האחריות על כל הקוד המוגש היא שלי, ואשמח להסביר כל חלק בו ואת השיקולים שמאחוריו.
