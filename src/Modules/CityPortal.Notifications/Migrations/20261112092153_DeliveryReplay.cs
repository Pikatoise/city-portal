using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CityPortal.Notifications.Migrations;

/// <inheritdoc />
public partial class DeliveryReplay : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<int>(
            name: "RetryCount",
            schema: "notifications",
            table: "EmailDeliveries",
            type: "integer",
            nullable: false,
            defaultValue: 0);

        // Existing deliveries have not been replayed; preserve their already-used retry budget.
        migrationBuilder.Sql("UPDATE notifications.\"EmailDeliveries\" SET \"RetryCount\" = \"AttemptCount\"");

        migrationBuilder.CreateTable(
            name: "Replays",
            schema: "notifications",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                ActorId = table.Column<Guid>(type: "uuid", nullable: false),
                DeliveryId = table.Column<Guid>(type: "uuid", nullable: false),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                PreviousVersion = table.Column<long>(type: "bigint", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Replays", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_Replays_DeliveryId_CreatedAtUtc",
            schema: "notifications",
            table: "Replays",
            columns: new[] { "DeliveryId", "CreatedAtUtc" });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "Replays",
            schema: "notifications");

        migrationBuilder.DropColumn(
            name: "RetryCount",
            schema: "notifications",
            table: "EmailDeliveries");
    }
}
