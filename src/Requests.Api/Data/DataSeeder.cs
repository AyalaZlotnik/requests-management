using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Requests.Api.Domain;

namespace Requests.Api.Data;

/// <summary>
/// Re-creates deterministic test data (same seed → same data) using SqlBulkCopy.
/// Run with: dotnet run --project Requests.Api -- seed [count]
/// </summary>
public partial class DataSeeder(RequestsDbContext db, TimeProvider timeProvider, ILogger<DataSeeder> logger)
{
    public const int DefaultCount = 100_000;
    private const int RandomSeed = 20261004;

    private static readonly string[] OrgPrefixes =
        ["Global", "Northern", "Southern", "Central", "United", "Blue", "Green", "Prime", "Smart", "Delta",
         "Alpha", "Omega", "Galil", "Negev", "Carmel", "Sharon", "Jordan", "Eilat", "Golan", "Kinneret"];

    private static readonly string[] OrgCores =
        ["Logistics", "Foods", "Chemicals", "Construction", "Energy", "Plastics", "Metals", "Textiles",
         "Recycling", "Pharma", "Electronics", "Transport", "Agro", "Water", "Paper"];

    private static readonly string[] OrgSuffixes = ["Ltd", "Industries", "Group", "Holdings", "Co"];

    private static readonly string[] TitleSubjects =
        ["Permit renewal", "Emission report", "Waste disposal approval", "Inspection request", "Payment issue",
         "Address change", "Document upload failure", "License transfer", "Complaint", "Data correction",
         "Monitoring system calibration", "Pipeline report", "Contact details update", "Deadline extension",
         "Account access"];

    private static readonly string[] TitleQualifiers =
        ["urgent", "follow-up", "annual", "for site A", "for site B", "second request", "clarification needed",
         "Q1", "Q2", "Q3", "Q4", "new branch"];

    private static readonly string[] Agents = Enumerable.Range(1, 40).Select(i => $"agent{i:00}").ToArray();

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

        // Fresh statistics so the first execution plans are based on the real data distribution.
        await db.Database.ExecuteSqlRawAsync("UPDATE STATISTICS Requests WITH FULLSCAN;", ct);
        LogSeeded(logger, count);
    }

    private static DataTable BuildTable(int count, DateTime nowUtc)
    {
        var random = new Random(RandomSeed);
        var organizations = OrgPrefixes
            .SelectMany(p => OrgCores.Select(c => $"{p} {c} {OrgSuffixes[(p.Length + c.Length) % OrgSuffixes.Length]}"))
            .ToArray();

        var table = new DataTable();
        table.Columns.Add("Title", typeof(string));
        table.Columns.Add("OrganizationName", typeof(string));
        table.Columns.Add("Status", typeof(byte));
        table.Columns.Add("Priority", typeof(byte));
        table.Columns.Add("AssignedTo", typeof(string));
        table.Columns.Add("CreatedAt", typeof(DateTime));
        table.Columns.Add("UpdatedAt", typeof(DateTime));

        var rangeSeconds = (int)TimeSpan.FromDays(730).TotalSeconds;
        for (var i = 0; i < count; i++)
        {
            var createdAt = nowUtc.AddSeconds(-random.Next(rangeSeconds));
            var status = (RequestStatus)PickWeighted(random, 20, 25, 10, 45);
            var priority = (RequestPriority)PickWeighted(random, 50, 35, 15);

            // New requests are often not assigned yet.
            string? assignedTo = status == RequestStatus.New && random.Next(100) < 60
                ? null
                : Agents[random.Next(Agents.Length)];
            var updatedAt = status == RequestStatus.New
                ? createdAt
                : Min(createdAt.AddMinutes(random.Next(1, 60 * 24 * 30)), nowUtc);

            var title = $"{TitleSubjects[random.Next(TitleSubjects.Length)]} - {TitleQualifiers[random.Next(TitleQualifiers.Length)]} #{i + 1}";

            table.Rows.Add(title, organizations[random.Next(organizations.Length)], (byte)status, (byte)priority,
                (object?)assignedTo ?? DBNull.Value, createdAt, updatedAt);
        }

        return table;
    }

    private static int PickWeighted(Random random, params int[] weights)
    {
        var roll = random.Next(weights.Sum());
        for (var i = 0; i < weights.Length; i++)
        {
            if (roll < weights[i]) return i;
            roll -= weights[i];
        }
        return weights.Length - 1;
    }

    private static DateTime Min(DateTime a, DateTime b) => a < b ? a : b;

    [LoggerMessage(Level = LogLevel.Information, Message = "Deleting existing data")]
    private static partial void LogDeleting(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Seeded {Count} requests")]
    private static partial void LogSeeded(ILogger logger, int count);
}
