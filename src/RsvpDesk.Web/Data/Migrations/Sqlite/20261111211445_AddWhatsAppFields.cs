using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RsvpDesk.Web.Data.Migrations.Sqlite
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
                type: "TEXT",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "DeliveryStatusAt",
                table: "Messages",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WhatsAppUserId",
                table: "Guests",
                type: "TEXT",
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
