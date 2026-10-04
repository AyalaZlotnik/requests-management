using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Requests.Api.Common.Caching;
using Requests.Api.Data;
using Requests.Api.Domain;

namespace Requests.Tests.Infrastructure;

/// <summary>
/// Hosts the real API in memory against a real SQL Server test database (separate from the dev database),
/// so the tests exercise HTTP, validation, EF Core, rowversion concurrency and the indexes together.
/// Connection string: env var REQUESTS_TEST_DB, default LocalDB.
/// </summary>
public sealed class RequestsApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly string _connectionString =
        Environment.GetEnvironmentVariable("REQUESTS_TEST_DB")
        ?? @"Server=(localdb)\MSSQLLocalDB;Database=RequestsManagement_Tests;Trusted_Connection=True;TrustServerCertificate=True";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:RequestsDb", _connectionString);
        builder.UseSetting("Database:MigrateOnStartup", "false");
    }

    public async Task InitializeAsync()
    {
        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<RequestsDbContext>();
        await db.Database.EnsureDeletedAsync();
        await db.Database.MigrateAsync();
    }

    public new async Task DisposeAsync() => await base.DisposeAsync();

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
        Services.GetRequiredService<SummaryCache>().Invalidate();
        return requests.ToList();
    }

    public async Task<T> QueryDbAsync<T>(Func<RequestsDbContext, Task<T>> query)
    {
        await using var scope = Services.CreateAsyncScope();
        return await query(scope.ServiceProvider.GetRequiredService<RequestsDbContext>());
    }

    public static ServiceRequest NewRequest(
        string title = "Permit renewal",
        string organization = "Acme Ltd",
        RequestStatus status = RequestStatus.New,
        RequestPriority priority = RequestPriority.Medium,
        string? assignedTo = "agent01",
        DateTime? createdAt = null) =>
        new(title, organization, priority, assignedTo, createdAt ?? new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), status);
}

[CollectionDefinition(Name)]
public class ApiTestsDefinition : ICollectionFixture<RequestsApiFactory>
{
    public const string Name = "api";
}
