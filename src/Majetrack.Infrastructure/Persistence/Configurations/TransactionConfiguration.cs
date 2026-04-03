using Majetrack.Domain.Entities;
using Majetrack.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Majetrack.Infrastructure.Persistence.Configurations;

/// <summary>
/// Configures the EF Core entity mapping for <see cref="Transaction"/>,
/// including foreign keys, indexes, decimal precision, enum-to-string conversions,
/// and column constraints.
/// </summary>
public class TransactionConfiguration : IEntityTypeConfiguration<Transaction>
{
    /// <summary>
    /// Applies the Transaction entity configuration to the model builder.
    /// </summary>
    /// <param name="builder">The builder used to configure the Transaction entity.</param>
    public void Configure(EntityTypeBuilder<Transaction> builder)
    {
        builder.HasKey(t => t.Id);

        // Foreign keys — no navigation properties on domain entities
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(t => t.UserId)
            .OnDelete(DeleteBehavior.Cascade)
            .IsRequired();

        builder.HasOne<Asset>()
            .WithMany()
            .HasForeignKey(t => t.AssetId)
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired(false);

        // Indexes
        builder.HasIndex(t => new { t.UserId, t.Platform, t.TransactionDate });

        builder.HasIndex(t => new { t.AssetId, t.TransactionType, t.TransactionDate });

        builder.HasIndex(t => new { t.UserId, t.Platform, t.ExternalId })
            .IsUnique()
            .HasFilter("external_id IS NOT NULL");

        // Enum → string conversions
        builder.Property(t => t.TransactionType)
            .IsRequired()
            .HasConversion<string>();

        builder.Property(t => t.Currency)
            .IsRequired()
            .HasConversion<string>();

        builder.Property(t => t.Platform)
            .IsRequired()
            .HasConversion<string>();

        // Decimal precision
        builder.Property(t => t.Quantity)
            .HasPrecision(18, 8);

        builder.Property(t => t.PricePerUnit)
            .HasPrecision(18, 6);

        builder.Property(t => t.TotalAmount)
            .HasPrecision(18, 2);

        builder.Property(t => t.Fee)
            .HasPrecision(18, 2);

        // Max lengths
        builder.Property(t => t.Note)
            .HasMaxLength(1000);

        builder.Property(t => t.ExternalId)
            .HasMaxLength(100);

        builder.Property(t => t.TransactionDate)
            .IsRequired();

        builder.Property(t => t.CreatedAt)
            .IsRequired();
    }
}
