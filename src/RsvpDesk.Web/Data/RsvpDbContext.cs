using Microsoft.EntityFrameworkCore;

namespace RsvpDesk.Web.Data;

/// <summary>
/// The application's EF Core model. The model is defined once here; each
/// supported database provider has a thin subclass that owns its own
/// migration history (see docs/decisions.md, D-004).
/// </summary>
public abstract class RsvpDbContext(DbContextOptions options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(RsvpDbContext).Assembly);
    }
}

/// <summary>SQLite: local development and the public demo.</summary>
public sealed class SqliteRsvpDbContext(DbContextOptions<SqliteRsvpDbContext> options) : RsvpDbContext(options);

/// <summary>PostgreSQL: real deployments.</summary>
public sealed class PostgresRsvpDbContext(DbContextOptions<PostgresRsvpDbContext> options) : RsvpDbContext(options);
