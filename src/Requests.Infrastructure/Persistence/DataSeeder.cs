using System.Data;
using Bogus;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Requests.Application.Requests.Entities;

namespace Requests.Infrastructure.Persistence;

/// <summary>
/// Generates fictional Hebrew test data. A fixed random seed makes every run produce the same
/// requests; dates are relative to the time of seeding, so "open for more than 7 days" stays meaningful.
/// Rows are written with SqlBulkCopy – 100,000 rows take a few seconds instead of minutes through EF.
/// </summary>
public partial class DataSeeder(RequestsDbContext db, TimeProvider timeProvider, ILogger<DataSeeder> logger)
{
    public const int DefaultCount = 100_000;
    private const int RandomSeed = 4_10_2026;

    // Organization names are combinations of these parts – fictional, not real bodies.
    private static readonly string[] OrgNames =
        ["אלון", "גליל", "שקד", "רימון", "נחשון", "תמר", "ארז", "דקל", "יובל", "שחר",
         "צפון", "מרום", "נגב", "עמק", "חוף", "גבעה", "אופק", "קשת", "להב", "ניר"];

    private static readonly string[] OrgFields =
        ["מערכות", "תעשיות", "לוגיסטיקה", "מזון", "כימיקלים", "אנרגיה", "מים", "בנייה",
         "פלסטיק", "מתכות", "טקסטיל", "מחזור", "הובלות", "אלקטרוניקה", "חקלאות"];

    private static readonly string[] OrgSuffixes = ["בע\"מ", "ושות'", "אחזקות", "קבוצה", "שותפות"];

    private static readonly string[] TitleSubjects =
        ["חידוש היתר", "דיווח פליטות", "אישור פינוי פסולת", "בקשה לביקורת", "בעיה בתשלום",
         "עדכון כתובת", "תקלה בהעלאת מסמכים", "העברת רישיון", "תלונה", "תיקון נתונים",
         "כיול מערכת ניטור", "דיווח קווי צנרת", "עדכון פרטי קשר", "בקשה להארכת מועד", "בעיית גישה לחשבון"];

    private static readonly string[] TitleDetails =
        ["דחוף", "המשך טיפול", "שנתי", "אתר מרכזי", "אתר משני", "פנייה חוזרת", "נדרשת הבהרה",
         "רבעון ראשון", "רבעון שני", "רבעון שלישי", "רבעון רביעי", "סניף חדש"];

    private static readonly string[] FirstNames =
        ["דנה", "יוסי", "מיכל", "אבי", "רונית", "עומר", "שירה", "איתי", "נועה", "גיל",
         "תמר", "אלון", "הדס", "ערן", "ליאת", "משה", "יעל", "רועי", "אורית", "עידו"];

    private static readonly string[] LastNames =
        ["לוי", "כהן", "מזרחי", "פרץ", "ביטון", "אברהם", "פרידמן", "שפירא", "אזולאי", "גולן",
         "בן דוד", "רוזן", "חדד", "נחום", "אשכנזי", "דהן", "קפלן", "סגל", "וקנין", "שלום"];

    private const int HandlerCount = 40;

    /// <summary>Seeds only when there are no requests yet (first run).</summary>
    public async Task SeedIfEmptyAsync(int count, CancellationToken ct = default)
    {
        if (await db.Requests.AnyAsync(ct))
        {
            return;
        }

        await SeedAsync(count, ct);
    }

    /// <summary>Deletes all requests and history and inserts <paramref name="count"/> new requests.</summary>
    public async Task SeedAsync(int count, CancellationToken ct = default)
    {
        await db.Database.MigrateAsync(ct);

        LogDeleting(logger);
        await db.Database.ExecuteSqlRawAsync(
            """
            TRUNCATE TABLE RequestStatusHistory;
            DELETE FROM Requests;
            -- Reseed only if rows were ever inserted; on a brand-new table RESEED 0 would make the first Id 0.
            IF EXISTS (SELECT 1 FROM sys.identity_columns
                WHERE object_id = OBJECT_ID('Requests') AND last_value IS NOT NULL)
                DBCC CHECKIDENT ('Requests', RESEED, 0);
            """, ct);

        var table = BuildTable(count, timeProvider.GetUtcNow().UtcDateTime);

        var connection = (SqlConnection)db.Database.GetDbConnection();
        await connection.OpenAsync(ct);
        try
        {
            using var bulkCopy = new SqlBulkCopy(connection, SqlBulkCopyOptions.TableLock, null)
            {
                DestinationTableName = "Requests",
                BatchSize = 10_000,
                BulkCopyTimeout = 300
            };
            foreach (DataColumn column in table.Columns)
            {
                bulkCopy.ColumnMappings.Add(column.ColumnName, column.ColumnName);
            }

            await bulkCopy.WriteToServerAsync(table, ct);
        }
        finally
        {
            await connection.CloseAsync();
        }

        // Rows arrive in Id order, so the secondary indexes (sorted by other columns) fill up through page
        // splits – measured 95-99% fragmentation and 56-76% page fill. Rebuilding after a bulk load is the
        // standard fix; it also refreshes the statistics with a full scan.
        await db.Database.ExecuteSqlRawAsync("ALTER INDEX ALL ON Requests REBUILD;", ct);
        LogSeeded(logger, count);
    }

    private static DataTable BuildTable(int count, DateTime nowUtc)
    {
        var faker = new Faker { Random = new Randomizer(RandomSeed) };

        var organizations = OrgNames
            .SelectMany(name => OrgFields.Select(field => $"{name} {field} {faker.PickRandom(OrgSuffixes)}"))
            .ToArray();

        var handlers = FirstNames
            .SelectMany(first => LastNames.Select(last => $"{first} {last}"))
            .OrderBy(_ => faker.Random.Int())
            .Take(HandlerCount)
            .ToArray();

        var table = new DataTable();
        table.Columns.Add("Title", typeof(string));
        table.Columns.Add("OrganizationName", typeof(string));
        table.Columns.Add("Status", typeof(byte));
        table.Columns.Add("Priority", typeof(byte));
        table.Columns.Add("AssignedTo", typeof(string));
        table.Columns.Add("CreatedAt", typeof(DateTime));
        table.Columns.Add("UpdatedAt", typeof(DateTime));

        RequestStatus[] statuses = [RequestStatus.New, RequestStatus.InProgress, RequestStatus.Waiting, RequestStatus.Completed];
        float[] statusWeights = [0.20f, 0.25f, 0.10f, 0.45f];
        RequestPriority[] priorities = [RequestPriority.Low, RequestPriority.Medium, RequestPriority.High];
        float[] priorityWeights = [0.50f, 0.35f, 0.15f];

        for (var i = 0; i < count; i++)
        {
            var createdAt = Truncate(faker.Date.Between(nowUtc.AddYears(-2), nowUtc));
            var status = faker.Random.WeightedRandom(statuses, statusWeights);
            var priority = faker.Random.WeightedRandom(priorities, priorityWeights);

            // New requests are often not assigned yet.
            string? assignedTo = status == RequestStatus.New && faker.Random.Bool(0.6f) ? null : faker.PickRandom(handlers);
            var updatedAt = status == RequestStatus.New
                ? createdAt
                : Truncate(Min(createdAt.AddMinutes(faker.Random.Int(1, 60 * 24 * 30)), nowUtc));

            var title = $"{faker.PickRandom(TitleSubjects)} - {faker.PickRandom(TitleDetails)} #{i + 1}";

            table.Rows.Add(title, faker.PickRandom(organizations), (byte)status, (byte)priority,
                (object?)assignedTo ?? DBNull.Value, createdAt, updatedAt);
        }

        return table;
    }

    // Match the column precision (datetime2(3)).
    private static DateTime Truncate(DateTime value) => value.AddTicks(-(value.Ticks % TimeSpan.TicksPerMillisecond));

    private static DateTime Min(DateTime a, DateTime b) => a < b ? a : b;

    [LoggerMessage(Level = LogLevel.Information, Message = "Deleting existing data")]
    private static partial void LogDeleting(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Seeded {Count} requests")]
    private static partial void LogSeeded(ILogger logger, int count);
}
