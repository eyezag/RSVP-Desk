using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RsvpDesk.Web.Data.Migrations.Postgres
{
    /// <inheritdoc />
    public partial class AddWhatsAppFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DeliveryError",
                table: "Messages",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "DeliveryStatusAt",
                table: "Messages",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WhatsAppUserId",
                table: "Guests",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Guests_WhatsAppUserId",
                table: "Guests",
                column: "WhatsAppUserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Guests_WhatsAppUserId",
                table: "Guests");

            migrationBuilder.DropColumn(
                name: "DeliveryError",
                table: "Messages");

            migrationBuilder.DropColumn(
                name: "DeliveryStatusAt",
                table: "Messages");

            migrationBuilder.DropColumn(
                name: "WhatsAppUserId",
                table: "Guests");
        }
    }
}
