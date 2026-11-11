using Microsoft.EntityFrameworkCore;
using Npgsql;
using RsvpDesk.Web.Data;

namespace RsvpDesk.Web.Tests;

/// <summary>Runs only when RSVP_TEST_POSTGRES holds a connection string (CI sets it).</summary>
public sealed class PostgresFactAttribute : FactAttribute
{
    public const string EnvVar = "RSVP_TEST_POSTGRES";

    public PostgresFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(EnvVar)))
        {
            Skip = $"Set {EnvVar} to run PostgreSQL tests.";
        }
    }
}

public sealed class PostgresMigrationTests
{
    [PostgresFact]
    public async Task Postgres_migrations_apply_to_an_empty_database()
    {
        var baseConnection = new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable(PostgresFactAttribute.EnvVar)!);
        var dbName = $"rsvpdesk_test_{Guid.NewGuid():N}";
        var connection = new NpgsqlConnectionStringBuilder(baseConnection.ConnectionString) { Database = dbName };

        var options = new DbContextOptionsBuilder<PostgresRsvpDbContext>().UseNpgsql(connection.ConnectionString).Options;
        await using var db = new PostgresRsvpDbContext(options);
        try
        {
            await db.Database.MigrateAsync();
            Assert.Empty(await db.Database.GetPendingMigrationsAsync());
        }
        finally
        {
            await db.Database.EnsureDeletedAsync();
        }
    }
}
