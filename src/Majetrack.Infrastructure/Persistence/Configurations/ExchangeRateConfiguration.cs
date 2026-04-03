using Majetrack.Domain.Entities;
using Majetrack.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Majetrack.Infrastructure.Persistence.Configurations;

/// <summary>
/// Configures the EF Core entity mapping for <see cref="ExchangeRate"/>,
/// including primary key, unique composite index, enum-to-string conversions,
/// and decimal precision for the rate column.
/// </summary>
public class ExchangeRateConfiguration : IEntityTypeConfiguration<ExchangeRate>
{
    /// <summary>
    /// Applies the ExchangeRate entity configuration to the model builder.
    /// </summary>
    /// <param name="builder">The builder used to configure the ExchangeRate entity.</param>
    public void Configure(EntityTypeBuilder<ExchangeRate> builder)
    {
        builder.HasKey(e => e.Id);

        builder.HasIndex(e => new { e.Date, e.SourceCurrency, e.TargetCurrency })
            .IsUnique();

        builder.Property(e => e.SourceCurrency)
            .IsRequired()
            .HasConversion<string>();

        builder.Property(e => e.TargetCurrency)
            .IsRequired()
            .HasConversion<string>();

        builder.Property(e => e.Rate)
            .HasPrecision(18, 6);

        builder.Property(e => e.Date)
            .IsRequired();

        builder.Property(e => e.FetchedAt)
            .IsRequired();
    }
}
