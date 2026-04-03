using Majetrack.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Majetrack.Infrastructure.Persistence.Configurations;

/// <summary>
/// Configures the EF Core entity mapping for <see cref="User"/>,
/// including primary key, indexes, column constraints, and max lengths.
/// </summary>
public class UserConfiguration : IEntityTypeConfiguration<User>
{
    /// <summary>
    /// Applies the User entity configuration to the model builder.
    /// </summary>
    /// <param name="builder">The builder used to configure the User entity.</param>
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.HasKey(u => u.Id);

        builder.HasIndex(u => u.EntraObjectId)
            .IsUnique();

        builder.HasIndex(u => u.Email);

        builder.Property(u => u.EntraObjectId)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(u => u.Email)
            .IsRequired()
            .HasMaxLength(320);

        builder.Property(u => u.DisplayName)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(u => u.CreatedAt)
            .IsRequired();
    }
}
