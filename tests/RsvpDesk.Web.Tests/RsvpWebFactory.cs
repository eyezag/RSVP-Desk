using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Npgsql;

namespace RsvpDesk.Web.Tests;

/// <summary>
/// Runs the app against a throwaway database per factory instance: SQLite by default, or
/// PostgreSQL when RSVP_TEST_DB=Postgres and RSVP_TEST_POSTGRES holds a connection string (CI runs both).
/// </summary>
public sealed class RsvpWebFactory : WebApplicationFactory<Program>
{
    private readonly string? _sqlitePath;
    private readonly string? _postgresConnection;

    public RsvpWebFactory()
    {
        var usePostgres = string.Equals(Environment.GetEnvironmentVariable("RSVP_TEST_DB"), "Postgres", StringComparison.OrdinalIgnoreCase);
        if (usePostgres)
        {
            var baseConnection = Environment.GetEnvironmentVariable(PostgresFactAttribute.EnvVar)
                ?? throw new InvalidOperationException($"RSVP_TEST_DB=Postgres needs {PostgresFactAttribute.EnvVar}.");
            _postgresConnection = new NpgsqlConnectionStringBuilder(baseConnection) { Database = $"rsvpdesk_it_{Guid.NewGuid():N}" }.ConnectionString;
        }
        else
        {
            _sqlitePath = Path.Combine(Path.GetTempPath(), $"rsvpdesk-test-{Guid.NewGuid():N}.db");
        }
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("Database:MigrateOnStartup", "true");
        builder.UseSetting("Demo:SeedSampleEvent", "true");
        if (_postgresConnection is not null)
        {
            builder.UseSetting("Database:Provider", "Postgres");
            builder.UseSetting("ConnectionStrings:Rsvp", _postgresConnection);
        }
        else
        {
            builder.UseSetting("Database:Provider", "Sqlite");
            builder.UseSetting("ConnectionStrings:Rsvp", $"Data Source={_sqlitePath};Pooling=False");
        }
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (_sqlitePath is not null && File.Exists(_sqlitePath))
        {
            File.Delete(_sqlitePath);
        }

        if (_postgresConnection is not null)
        {
            NpgsqlConnection.ClearAllPools();
            var builder = new NpgsqlConnectionStringBuilder(_postgresConnection);
            var database = builder.Database;
            builder.Database = "postgres";
            using var connection = new NpgsqlConnection(builder.ConnectionString);
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = $"DROP DATABASE IF EXISTS \"{database}\" WITH (FORCE)";
            command.ExecuteNonQuery();
        }
    }
}
