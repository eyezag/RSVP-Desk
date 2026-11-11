using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace RsvpDesk.Web.Data;

// Used only by `dotnet ef` when generating migrations. The connection strings
// here are placeholders; no database is contacted while adding a migration.

internal sealed class SqliteDesignTimeFactory : IDesignTimeDbContextFactory<SqliteRsvpDbContext>
{
    public SqliteRsvpDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<SqliteRsvpDbContext>().UseSqlite("Data Source=design.db").Options);
}

internal sealed class PostgresDesignTimeFactory : IDesignTimeDbContextFactory<PostgresRsvpDbContext>
{
    public PostgresRsvpDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<PostgresRsvpDbContext>().UseNpgsql("Host=localhost;Database=design").Options);
}
