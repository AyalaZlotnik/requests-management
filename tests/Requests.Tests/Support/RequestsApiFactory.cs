using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Requests.Application.Requests.Abstractions;
using Requests.Application.Requests.Entities;
using Requests.Infrastructure.Persistence;

namespace Requests.Tests.Support;

/// <summary>
/// Hosts the real API in memory against a real SQL Server database, so the tests exercise HTTP, validation,
/// EF Core, rowversion concurrency and the schema together (the InMemory provider would not enforce concurrency).
/// Every test class gets its own database, created by the real migration and deleted afterwards –
/// classes run in parallel without seeing each other's data.
/// Server: env var REQUESTS_TEST_SERVER, default LocalDB.
/// </summary>
public sealed class RequestsApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly string _connectionString =
        $"Server={Environment.GetEnvironmentVariable("REQUESTS_TEST_SERVER") ?? @"(localdb)\MSSQLLocalDB"};" +
        $"Database=RequestsTests_{Guid.NewGuid():N};Trusted_Connection=True;TrustServerCertificate=True";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:RequestsDb", _connectionString);
        builder.UseSetting("Database:MigrateOnStartup", "false");
    }

    public async Task InitializeAsync()
    {
        await using var scope = Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<RequestsDbContext>().Database.MigrateAsync();
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await using (var scope = Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<RequestsDbContext>().Database.EnsureDeletedAsync();
        }

        await DisposeAsync();
    }

    /// <summary>Clears all data and inserts the given requests. Returns them with their generated Ids.</summary>
    public async Task<List<ServiceRequest>> ResetAndSeedAsync(params ServiceRequest[] requests)
    {
        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<RequestsDbContext>();
        await db.StatusHistory.ExecuteDeleteAsync();
        await db.Requests.ExecuteDeleteAsync();
        db.Requests.AddRange(requests);
        await db.SaveChangesAsync();

        // The data was changed behind the API's back, so drop the cached summary.
        Services.GetRequiredService<ISummaryCache>().Invalidate();
        return [.. requests];
    }

    public async Task<T> QueryDbAsync<T>(Func<RequestsDbContext, Task<T>> query)
    {
        await using var scope = Services.CreateAsyncScope();
        return await query(scope.ServiceProvider.GetRequiredService<RequestsDbContext>());
    }

    public static ServiceRequest NewRequest(
        string title = "חידוש היתר",
        string organization = "ארגון בדיקה בע\"מ",
        RequestStatus status = RequestStatus.New,
        RequestPriority priority = RequestPriority.Medium,
        string? assignedTo = "דנה לוי",
        DateTime? createdAt = null) =>
        new(title, organization, priority, assignedTo, createdAt ?? new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), status);
}
