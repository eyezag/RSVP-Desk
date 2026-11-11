using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace RsvpDesk.Web.Data;

/// <summary>
/// The application's EF Core model. The model is defined once here; each
/// supported database provider has a thin subclass that owns its own
/// migration history (see docs/decisions.md, D-004).
/// </summary>
public abstract class RsvpDbContext(DbContextOptions options) : DbContext(options)
{
    public DbSet<Event> Events => Set<Event>();
    public DbSet<Guest> Guests => Set<Guest>();
    public DbSet<Conversation> Conversations => Set<Conversation>();
    public DbSet<Rsvp> Rsvps => Set<Rsvp>();
    public DbSet<RsvpPerson> RsvpPersons => Set<RsvpPerson>();
    public DbSet<Message> Messages => Set<Message>();
    public DbSet<WebhookEvent> WebhookEvents => Set<WebhookEvent>();
    public DbSet<User> Users => Set<User>();
    public DbSet<AuditEntry> AuditLog => Set<AuditEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(RsvpDbContext).Assembly);
    }
}

/// <summary>SQLite: local development and the public demo.</summary>
public sealed class SqliteRsvpDbContext(DbContextOptions<SqliteRsvpDbContext> options) : RsvpDbContext(options)
{
    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // SQLite has no date type; EF stores DateTimeOffset as text, which cannot be ordered or
        // compared in SQL. Store UTC ticks instead (decisions D-202).
        configurationBuilder.Properties<DateTimeOffset>().HaveConversion<UtcTicksConverter>();
        configurationBuilder.Properties<DateTimeOffset?>().HaveConversion<UtcTicksConverter>();
    }

    private sealed class UtcTicksConverter() : ValueConverter<DateTimeOffset, long>(
        v => v.UtcTicks,
        v => new DateTimeOffset(v, TimeSpan.Zero));
}

/// <summary>PostgreSQL: real deployments.</summary>
public sealed class PostgresRsvpDbContext(DbContextOptions<PostgresRsvpDbContext> options) : RsvpDbContext(options)
{
    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // timestamptz only accepts UTC values from Npgsql.
        configurationBuilder.Properties<DateTimeOffset>().HaveConversion<ToUtcConverter>();
        configurationBuilder.Properties<DateTimeOffset?>().HaveConversion<ToUtcConverter>();
    }

    private sealed class ToUtcConverter() : ValueConverter<DateTimeOffset, DateTimeOffset>(
        v => v.ToUniversalTime(),
        v => v.ToUniversalTime());
}
