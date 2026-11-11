using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RsvpDesk.Web.Data.Migrations.Sqlite
{
    /// <inheritdoc />
    public partial class AddRsvpModel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Events",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    StartsAt = table.Column<long>(type: "INTEGER", nullable: false),
                    TimeZoneId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Venue = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    Capacity = table.Column<int>(type: "INTEGER", nullable: false),
                    PartyMax = table.Column<int>(type: "INTEGER", nullable: false),
                    DeleteDataAfterDays = table.Column<int>(type: "INTEGER", nullable: false),
                    IsSample = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Events", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "WebhookEvents",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Source = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    ExternalId = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    ReceivedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    RawJson = table.Column<string>(type: "TEXT", nullable: false),
                    ProcessedAt = table.Column<long>(type: "INTEGER", nullable: true),
                    Error = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WebhookEvents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Guests",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    EventId = table.Column<int>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    PhoneE164 = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    ConsentAt = table.Column<long>(type: "INTEGER", nullable: true),
                    ConsentSource = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    OptedOutAt = table.Column<long>(type: "INTEGER", nullable: true),
                    InviteSentAt = table.Column<long>(type: "INTEGER", nullable: true),
                    NeedsAttention = table.Column<bool>(type: "INTEGER", nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    LastActivityAt = table.Column<long>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Guests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Guests_Events_EventId",
                        column: x => x.EventId,
                        principalTable: "Events",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Conversations",
                columns: table => new
                {
                    GuestId = table.Column<int>(type: "INTEGER", nullable: false),
                    State = table.Column<string>(type: "TEXT", maxLength: 24, nullable: false),
                    StateData = table.Column<string>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    Version = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Conversations", x => x.GuestId);
                    table.ForeignKey(
                        name: "FK_Conversations_Guests_GuestId",
                        column: x => x.GuestId,
                        principalTable: "Guests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Messages",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    GuestId = table.Column<int>(type: "INTEGER", nullable: false),
                    Direction = table.Column<string>(type: "TEXT", maxLength: 8, nullable: false),
                    Body = table.Column<string>(type: "TEXT", maxLength: 4096, nullable: false),
                    ReplyId = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    ChoicesJson = table.Column<string>(type: "TEXT", nullable: true),
                    ProviderMessageId = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    SentAt = table.Column<long>(type: "INTEGER", nullable: false),
                    DeliveryStatus = table.Column<string>(type: "TEXT", maxLength: 16, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Messages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Messages_Guests_GuestId",
                        column: x => x.GuestId,
                        principalTable: "Guests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Rsvps",
                columns: table => new
                {
                    GuestId = table.Column<int>(type: "INTEGER", nullable: false),
                    Attending = table.Column<bool>(type: "INTEGER", nullable: true),
                    PartySize = table.Column<int>(type: "INTEGER", nullable: true),
                    WheelchairUsers = table.Column<int>(type: "INTEGER", nullable: true),
                    AccessNote = table.Column<string>(type: "TEXT", maxLength: 600, nullable: true),
                    ConfirmedAt = table.Column<long>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Rsvps", x => x.GuestId);
                    table.ForeignKey(
                        name: "FK_Rsvps_Guests_GuestId",
                        column: x => x.GuestId,
                        principalTable: "Guests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RsvpPersons",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    RsvpId = table.Column<int>(type: "INTEGER", nullable: false),
                    PersonIndex = table.Column<int>(type: "INTEGER", nullable: false),
                    Dietary = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    DietaryNote = table.Column<string>(type: "TEXT", maxLength: 300, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RsvpPersons", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RsvpPersons_Rsvps_RsvpId",
                        column: x => x.RsvpId,
                        principalTable: "Rsvps",
                        principalColumn: "GuestId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Guests_EventId_PhoneE164",
                table: "Guests",
                columns: new[] { "EventId", "PhoneE164" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Guests_EventId_Status",
                table: "Guests",
                columns: new[] { "EventId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_Messages_GuestId_Id",
                table: "Messages",
                columns: new[] { "GuestId", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_Messages_ProviderMessageId",
                table: "Messages",
                column: "ProviderMessageId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RsvpPersons_RsvpId_PersonIndex",
                table: "RsvpPersons",
                columns: new[] { "RsvpId", "PersonIndex" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WebhookEvents_ExternalId",
                table: "WebhookEvents",
                column: "ExternalId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WebhookEvents_ProcessedAt",
                table: "WebhookEvents",
                column: "ProcessedAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Conversations");

            migrationBuilder.DropTable(
                name: "Messages");

            migrationBuilder.DropTable(
                name: "RsvpPersons");

            migrationBuilder.DropTable(
                name: "WebhookEvents");

            migrationBuilder.DropTable(
                name: "Rsvps");

            migrationBuilder.DropTable(
                name: "Guests");

            migrationBuilder.DropTable(
                name: "Events");
        }
    }
}
