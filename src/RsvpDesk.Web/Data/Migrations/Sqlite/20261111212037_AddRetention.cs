using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RsvpDesk.Web.Data.Migrations.Sqlite
{
    /// <inheritdoc />
    public partial class AddRetention : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "GuestDataDeletedAt",
                table: "Events",
                type: "INTEGER",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "GuestDataDeletedAt",
                table: "Events");
        }
    }
}
