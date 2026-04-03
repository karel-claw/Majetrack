using Majetrack.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Majetrack.Infrastructure.Persistence;

/// <summary>
/// The primary database context for the Majetrack application.
/// Manages all entity sets and applies entity configurations, snake-case naming,
/// and enum-to-string conversions for PostgreSQL compatibility.
/// </summary>
public class MajetrackDbContext : DbContext
{
    /// <summary>
    /// Initializes a new instance of <see cref="MajetrackDbContext"/> with the specified options.
    /// </summary>
    /// <param name="options">The database context options, typically injected by the DI container.</param>
    public MajetrackDbContext(DbContextOptions<MajetrackDbContext> options) : base(options)
    {
    }

    /// <summary>
    /// The set of registered users in the system.
    /// </summary>
    public DbSet<User> Users => Set<User>();

    /// <summary>
    /// The set of financial assets (stocks, ETFs, P2P loans) available for trading.
    /// </summary>
    public DbSet<Asset> Assets => Set<Asset>();

    /// <summary>
    /// The set of financial transactions recorded by users.
    /// </summary>
    public DbSet<Transaction> Transactions => Set<Transaction>();

    /// <summary>
    /// The set of historical exchange rates used for currency conversion.
    /// </summary>
    public DbSet<ExchangeRate> ExchangeRates => Set<ExchangeRate>();

    /// <summary>
    /// Configures the model by applying all <see cref="IEntityTypeConfiguration{TEntity}"/>
    /// implementations found in the Infrastructure assembly.
    /// </summary>
    /// <param name="modelBuilder">The builder used to construct the model for this context.</param>
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(MajetrackDbContext).Assembly);
    }

    /// <summary>
    /// Configures context-level conventions including snake-case column naming
    /// for PostgreSQL compatibility.
    /// </summary>
    /// <param name="configurationBuilder">The builder used to configure model conventions.</param>
    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        base.ConfigureConventions(configurationBuilder);
    }

    /// <summary>
    /// Configures the database provider options, including snake-case naming conventions.
    /// This method is only used when options are not externally provided (e.g., design-time).
    /// </summary>
    /// <param name="optionsBuilder">The builder used to configure context options.</param>
    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        base.OnConfiguring(optionsBuilder);
        optionsBuilder.UseSnakeCaseNamingConvention();
    }
}
