using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RsvpDesk.Web.Data.Migrations.Postgres
{
    /// <inheritdoc />
    public partial class AddRetention : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "GuestDataDeletedAt",
                table: "Events",
                type: "timestamp with time zone",
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
