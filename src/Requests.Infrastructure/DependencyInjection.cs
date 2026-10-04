using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Requests.Application.Requests.Abstractions;
using Requests.Infrastructure.Caching;
using Requests.Infrastructure.Persistence;

namespace Requests.Infrastructure;

public static class DependencyInjection
{
    /// <summary>The only entry point the API uses to wire up data access and caching.</summary>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<RequestsDbContext>(o => o.UseSqlServer(configuration.GetConnectionString("RequestsDb")));
        services.AddScoped<IRequestRepository, RequestRepository>();
        services.AddScoped<DataSeeder>();

        services.AddMemoryCache();
        services.Configure<CacheOptions>(configuration.GetSection("Cache"));
        // Singleton: it owns the invalidation token shared by all requests.
        services.AddSingleton<ISummaryCache, MemorySummaryCache>();

        return services;
    }

    /// <summary>Applies pending EF Core migrations and, if requested, seeds test data when the table is empty.</summary>
    public static async Task InitializeDatabaseAsync(this IServiceProvider services, bool seedIfEmpty, CancellationToken ct = default)
    {
        await using var scope = services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<RequestsDbContext>().Database.MigrateAsync(ct);
        if (seedIfEmpty)
        {
            await scope.ServiceProvider.GetRequiredService<DataSeeder>().SeedIfEmptyAsync(DataSeeder.DefaultCount, ct);
        }
    }

    /// <summary>Deletes all data and inserts <paramref name="count"/> generated requests (default 100,000).</summary>
    public static async Task ReseedDatabaseAsync(this IServiceProvider services, int? count = null, CancellationToken ct = default)
    {
        await using var scope = services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<DataSeeder>().SeedAsync(count ?? DataSeeder.DefaultCount, ct);
        services.GetRequiredService<ISummaryCache>().Invalidate();
    }
}
