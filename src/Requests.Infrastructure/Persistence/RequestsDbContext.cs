using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Requests.Application.Requests.Entities;

namespace Requests.Infrastructure.Persistence;

public class RequestsDbContext(DbContextOptions<RequestsDbContext> options) : DbContext(options)
{
    public DbSet<ServiceRequest> Requests => Set<ServiceRequest>();
    public DbSet<RequestStatusHistory> StatusHistory => Set<RequestStatusHistory>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(RequestsDbContext).Assembly);

    // All dates are stored as UTC; mark them as UTC when read so the API serializes them with "Z".
    // Values without a kind (e.g. "?createdFrom=2026-01-01") are treated as UTC.
    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) =>
        configurationBuilder.Properties<DateTime>().HaveConversion<UtcDateTimeConverter>();

    private sealed class UtcDateTimeConverter() : ValueConverter<DateTime, DateTime>(
        v => v.Kind == DateTimeKind.Local ? v.ToUniversalTime() : v,
        v => DateTime.SpecifyKind(v, DateTimeKind.Utc));
}
