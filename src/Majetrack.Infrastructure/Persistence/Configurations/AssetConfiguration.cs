using Majetrack.Domain.Entities;
using Majetrack.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Majetrack.Infrastructure.Persistence.Configurations;

/// <summary>
/// Configures the EF Core entity mapping for <see cref="Asset"/>,
/// including primary key, composite indexes, enum-to-string conversions, and column constraints.
/// </summary>
public class AssetConfiguration : IEntityTypeConfiguration<Asset>
{
    /// <summary>
    /// Applies the Asset entity configuration to the model builder.
    /// </summary>
    /// <param name="builder">The builder used to configure the Asset entity.</param>
    public void Configure(EntityTypeBuilder<Asset> builder)
    {
        builder.HasKey(a => a.Id);

        builder.HasIndex(a => new { a.Ticker, a.Platform })
            .IsUnique()
            .HasFilter("ticker IS NOT NULL");

        builder.HasIndex(a => new { a.Name, a.Platform, a.AssetType })
            .IsUnique();

        builder.Property(a => a.AssetType)
            .IsRequired()
            .HasConversion<string>();

        builder.Property(a => a.Currency)
            .IsRequired()
            .HasConversion<string>();

        builder.Property(a => a.Platform)
            .IsRequired()
            .HasConversion<string>();

        builder.Property(a => a.Ticker)
            .HasMaxLength(20);

        builder.Property(a => a.Name)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(a => a.Exchange)
            .HasMaxLength(50);

        builder.Property(a => a.CreatedAt)
            .IsRequired();
    }
}
