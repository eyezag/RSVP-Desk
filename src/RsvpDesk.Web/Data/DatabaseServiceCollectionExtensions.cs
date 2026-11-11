using Microsoft.EntityFrameworkCore;

namespace RsvpDesk.Web.Data;

public static class DatabaseServiceCollectionExtensions
{
    /// <summary>
    /// Registers the provider-specific DbContext selected by <c>Database:Provider</c>
    /// and exposes it as <see cref="RsvpDbContext"/> so the rest of the app never
    /// needs to know which database it is talking to.
    /// </summary>
    public static IServiceCollection AddRsvpDatabase(this IServiceCollection services, IConfiguration configuration)
    {
        var options = configuration.GetSection(DatabaseOptions.SectionName).Get<DatabaseOptions>() ?? new DatabaseOptions();
        services.Configure<DatabaseOptions>(configuration.GetSection(DatabaseOptions.SectionName));

        var connectionString = configuration.GetConnectionString("Rsvp")
            ?? throw new InvalidOperationException("Connection string 'ConnectionStrings:Rsvp' is not configured.");

        switch (options.Provider)
        {
            case DatabaseProvider.Sqlite:
                services.AddDbContext<SqliteRsvpDbContext>(o => o.UseSqlite(connectionString));
                services.AddScoped<RsvpDbContext>(sp => sp.GetRequiredService<SqliteRsvpDbContext>());
                break;
            case DatabaseProvider.Postgres:
                services.AddDbContext<PostgresRsvpDbContext>(o => o.UseNpgsql(connectionString));
                services.AddScoped<RsvpDbContext>(sp => sp.GetRequiredService<PostgresRsvpDbContext>());
                break;
            default:
                throw new InvalidOperationException($"Unsupported database provider '{options.Provider}'.");
        }

        return services;
    }

    public static async Task MigrateRsvpDatabaseAsync(this WebApplication app)
    {
        var options = app.Configuration.GetSection(DatabaseOptions.SectionName).Get<DatabaseOptions>() ?? new DatabaseOptions();
        if (!options.MigrateOnStartup)
        {
            return;
        }

        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<RsvpDbContext>();
        await db.Database.MigrateAsync();
    }

    public static async Task SeedDemoDataAsync(this WebApplication app)
    {
        var options = app.Configuration.GetSection(DemoOptions.SectionName).Get<DemoOptions>() ?? new DemoOptions();
        if (!options.SeedSampleEvent)
        {
            return;
        }

        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<RsvpDbContext>();
        await DemoSeeder.SeedAsync(db, scope.ServiceProvider.GetRequiredService<TimeProvider>());
    }

    /// <summary>Creates the first organiser from configuration when there are no users yet.</summary>
    public static async Task SeedInitialOrganiserAsync(this WebApplication app)
    {
        var initial = app.Configuration.GetSection(Auth.AuthOptions.SectionName).Get<Auth.AuthOptions>()?.InitialOrganiser;
        if (string.IsNullOrWhiteSpace(initial?.Email) || string.IsNullOrEmpty(initial.Password))
        {
            return;
        }

        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<RsvpDbContext>();
        if (await db.Users.AnyAsync())
        {
            return;
        }

        await scope.ServiceProvider.GetRequiredService<Auth.UserService>().CreateAsync(initial.Email, initial.Password, UserRole.Organiser, CancellationToken.None);
    }
}
