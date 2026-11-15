using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CityPortal.Notifications.Migrations;

/// <inheritdoc />
public partial class WorkflowNotifications : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "NotificationType",
            schema: "notifications",
            table: "EmailDeliveries",
            type: "character varying(40)",
            maxLength: 40,
            nullable: true);

        migrationBuilder.CreateTable(
            name: "AccountNotifications",
            schema: "notifications",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                Type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                ResourceId = table.Column<Guid>(type: "uuid", nullable: false),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                Read = table.Column<bool>(type: "boolean", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_AccountNotifications", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "Preferences",
            schema: "notifications",
            columns: table => new
            {
                AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                Type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                Account = table.Column<bool>(type: "boolean", nullable: false),
                Email = table.Column<bool>(type: "boolean", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Preferences", x => new { x.AccountId, x.Type });
            });

        migrationBuilder.CreateTable(
            name: "WorkflowReceipts",
            schema: "notifications",
            columns: table => new
            {
                EventId = table.Column<Guid>(type: "uuid", nullable: false),
                AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                Type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                NotificationId = table.Column<Guid>(type: "uuid", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_WorkflowReceipts", x => new { x.EventId, x.AccountId, x.Type });
            });

        migrationBuilder.CreateIndex(
            name: "IX_AccountNotifications_AccountId_CreatedAtUtc",
            schema: "notifications",
            table: "AccountNotifications",
            columns: new[] { "AccountId", "CreatedAtUtc" });

        migrationBuilder.CreateIndex(
            name: "IX_AccountNotifications_CreatedAtUtc",
            schema: "notifications",
            table: "AccountNotifications",
            column: "CreatedAtUtc");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "AccountNotifications",
            schema: "notifications");

        migrationBuilder.DropTable(
            name: "Preferences",
            schema: "notifications");

        migrationBuilder.DropTable(
            name: "WorkflowReceipts",
            schema: "notifications");

        migrationBuilder.DropColumn(
            name: "NotificationType",
            schema: "notifications",
            table: "EmailDeliveries");
    }
}
