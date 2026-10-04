using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Requests.Application.Requests.Entities;

namespace Requests.Infrastructure.Persistence.Configurations;

public class ServiceRequestConfiguration : IEntityTypeConfiguration<ServiceRequest>
{
    public void Configure(EntityTypeBuilder<ServiceRequest> builder)
    {
        builder.ToTable("Requests");
        builder.HasKey(r => r.Id);

        builder.Property(r => r.Title).HasMaxLength(RequestFieldLimits.Title).IsRequired();
        builder.Property(r => r.OrganizationName).HasMaxLength(RequestFieldLimits.OrganizationName).IsRequired();
        builder.Property(r => r.AssignedTo).HasMaxLength(RequestFieldLimits.AssignedTo);
        builder.Property(r => r.Status).HasConversion<byte>();
        builder.Property(r => r.Priority).HasConversion<byte>();
        builder.Property(r => r.CreatedAt).HasColumnType("datetime2(3)");
        builder.Property(r => r.UpdatedAt).HasColumnType("datetime2(3)");

        // Optimistic concurrency token: EF adds "WHERE RowVersion = @original" to every UPDATE.
        builder.Property(r => r.RowVersion).IsRowVersion();

        // Indexes are derived from the main query patterns – see docs/PERFORMANCE.md.
        // Default list view: ORDER BY CreatedAt DESC (+ date range filter).
        builder.HasIndex(r => r.CreatedAt)
            .HasDatabaseName("IX_Requests_CreatedAt");

        // Most common filter (e.g. "all New requests") with the default sort. Priority is included
        // so the status/priority aggregation is answered from this narrow index only.
        builder.HasIndex(r => new { r.Status, r.CreatedAt })
            .IncludeProperties(r => r.Priority)
            .HasDatabaseName("IX_Requests_Status_CreatedAt");

        // "My requests" / workload per handler and the top-assignees aggregation.
        builder.HasIndex(r => new { r.AssignedTo, r.Status })
            .HasDatabaseName("IX_Requests_AssignedTo_Status");

        // Organization filter is a prefix match (LIKE 'abc%') which can seek on this index.
        builder.HasIndex(r => r.OrganizationName)
            .HasDatabaseName("IX_Requests_OrganizationName");
    }
}
