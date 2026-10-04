using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Requests.Application.Requests.Entities;

namespace Requests.Infrastructure.Persistence.Configurations;

public class RequestStatusHistoryConfiguration : IEntityTypeConfiguration<RequestStatusHistory>
{
    public void Configure(EntityTypeBuilder<RequestStatusHistory> builder)
    {
        builder.ToTable("RequestStatusHistory");
        builder.HasKey(h => h.Id);

        builder.Property(h => h.PreviousStatus).HasConversion<byte>();
        builder.Property(h => h.NewStatus).HasConversion<byte>();
        builder.Property(h => h.ChangedBy).HasMaxLength(RequestFieldLimits.ChangedBy).IsRequired();
        builder.Property(h => h.ChangedAt).HasColumnType("datetime2(3)");

        builder.HasOne<ServiceRequest>()
            .WithMany()
            .HasForeignKey(h => h.RequestId)
            .OnDelete(DeleteBehavior.Cascade);

        // History endpoint: WHERE RequestId = @id ORDER BY ChangedAt DESC.
        builder.HasIndex(h => new { h.RequestId, h.ChangedAt })
            .HasDatabaseName("IX_RequestStatusHistory_RequestId_ChangedAt");
    }
}
