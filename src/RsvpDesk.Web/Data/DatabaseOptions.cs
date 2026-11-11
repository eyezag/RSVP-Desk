namespace RsvpDesk.Web.Data;

public enum DatabaseProvider
{
    Sqlite,
    Postgres,
}

public sealed class DatabaseOptions
{
    public const string SectionName = "Database";

    public DatabaseProvider Provider { get; set; } = DatabaseProvider.Sqlite;

    /// <summary>Apply pending migrations when the app starts. On by default for the demo.</summary>
    public bool MigrateOnStartup { get; set; } = true;
}
