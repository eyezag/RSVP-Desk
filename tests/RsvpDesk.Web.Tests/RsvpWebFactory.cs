using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Npgsql;

namespace RsvpDesk.Web.Tests;

/// <summary>
/// Runs the app against a throwaway database per factory instance: SQLite by default, or
/// PostgreSQL when RSVP_TEST_DB=Postgres and RSVP_TEST_POSTGRES holds a connection string (CI runs both).
/// </summary>
public sealed class RsvpWebFactory : WebApplicationFactory<Program>
{
    public const string OrganiserEmail = "organiser@test.example";
    public const string Password = "correct-horse-battery";

    /// <summary>The app's clock. Starts at the real time and only moves when a test advances it.</summary>
    public FakeTimeProvider Clock { get; } = new(DateTimeOffset.UtcNow);

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
        builder.ConfigureTestServices(services => services.AddSingleton<TimeProvider>(Clock));
        builder.UseSetting("Database:MigrateOnStartup", "true");
        builder.UseSetting("Demo:SeedSampleEvent", "true");
        builder.UseSetting("Auth:InitialOrganiser:Email", OrganiserEmail);
        builder.UseSetting("Auth:InitialOrganiser:Password", Password);
        builder.UseSetting("Auth:LoginRateLimitPerMinute", "10000");
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
