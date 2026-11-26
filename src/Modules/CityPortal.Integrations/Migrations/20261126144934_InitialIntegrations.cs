using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace CityPortal.Integrations.Migrations;

/// <inheritdoc />
public partial class InitialIntegrations : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.EnsureSchema(
            name: "integrations");

        migrationBuilder.CreateTable(
            name: "settings",
            schema: "integrations",
            columns: table => new
            {
                Id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                Version = table.Column<int>(type: "integer", nullable: false),
                RssEnabled = table.Column<bool>(type: "boolean", nullable: false),
                WeatherEnabled = table.Column<bool>(type: "boolean", nullable: false),
                Latitude = table.Column<double>(type: "double precision", nullable: true),
                Longitude = table.Column<double>(type: "double precision", nullable: true),
                TelegramEnabled = table.Column<bool>(type: "boolean", nullable: false),
                TelegramChannel = table.Column<string>(type: "character varying(33)", maxLength: 33, nullable: true),
                AiEnabled = table.Column<bool>(type: "boolean", nullable: false),
                McpEnabled = table.Column<bool>(type: "boolean", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_settings", x => x.Id);
                table.CheckConstraint("CK_settings_singleton", "\"Id\" = 1");
            });

        migrationBuilder.CreateTable(
            name: "settings_changes",
            schema: "integrations",
            columns: table => new
            {
                Version = table.Column<int>(type: "integer", nullable: false),
                ActorId = table.Column<Guid>(type: "uuid", nullable: false),
                ChangedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                Settings = table.Column<string>(type: "jsonb", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_settings_changes", x => x.Version);
            });

        migrationBuilder.CreateTable(
            name: "TelegramDeliveries",
            schema: "integrations",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                ArticleId = table.Column<Guid>(type: "uuid", nullable: false),
                RevisionId = table.Column<Guid>(type: "uuid", nullable: false),
                Channel = table.Column<string>(type: "character varying(33)", maxLength: 33, nullable: false),
                Text = table.Column<string>(type: "character varying(3000)", maxLength: 3000, nullable: false),
                State = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                Version = table.Column<long>(type: "bigint", nullable: false),
                Attempt = table.Column<int>(type: "integer", nullable: false),
                RateLimitRetries = table.Column<int>(type: "integer", nullable: false),
                MessageId = table.Column<int>(type: "integer", nullable: true),
                ErrorCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_TelegramDeliveries", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "weather_snapshots",
            schema: "integrations",
            columns: table => new
            {
                Id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                Latitude = table.Column<double>(type: "double precision", nullable: false),
                Longitude = table.Column<double>(type: "double precision", nullable: false),
                Celsius = table.Column<double>(type: "double precision", nullable: false),
                WeatherCode = table.Column<int>(type: "integer", nullable: false),
                ObservedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                RefreshedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_weather_snapshots", x => x.Id);
                table.CheckConstraint("CK_weather_snapshots_singleton", "\"Id\" = 1");
            });

        migrationBuilder.CreateTable(
            name: "TelegramDeliveryEvents",
            schema: "integrations",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                DeliveryId = table.Column<Guid>(type: "uuid", nullable: false),
                ActorId = table.Column<Guid>(type: "uuid", nullable: true),
                RevisionId = table.Column<Guid>(type: "uuid", nullable: false),
                Action = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                Attempt = table.Column<int>(type: "integer", nullable: false),
                AtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_TelegramDeliveryEvents", x => x.Id);
                table.ForeignKey(
                    name: "FK_TelegramDeliveryEvents_TelegramDeliveries_DeliveryId",
                    column: x => x.DeliveryId,
                    principalSchema: "integrations",
                    principalTable: "TelegramDeliveries",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.InsertData(
            schema: "integrations",
            table: "settings",
            columns: new[] { "Id", "AiEnabled", "Latitude", "Longitude", "McpEnabled", "RssEnabled", "TelegramChannel", "TelegramEnabled", "Version", "WeatherEnabled" },
            values: new object[] { 1, false, null, null, false, false, null, false, 0, false });

        migrationBuilder.CreateIndex(
            name: "IX_TelegramDeliveries_ArticleId_Channel",
            schema: "integrations",
            table: "TelegramDeliveries",
            columns: new[] { "ArticleId", "Channel" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_TelegramDeliveries_State_UpdatedAtUtc",
            schema: "integrations",
            table: "TelegramDeliveries",
            columns: new[] { "State", "UpdatedAtUtc" });

        migrationBuilder.CreateIndex(
            name: "IX_TelegramDeliveryEvents_DeliveryId",
            schema: "integrations",
            table: "TelegramDeliveryEvents",
            column: "DeliveryId");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "settings",
            schema: "integrations");

        migrationBuilder.DropTable(
            name: "settings_changes",
            schema: "integrations");

        migrationBuilder.DropTable(
            name: "TelegramDeliveryEvents",
            schema: "integrations");

        migrationBuilder.DropTable(
            name: "weather_snapshots",
            schema: "integrations");

        migrationBuilder.DropTable(
            name: "TelegramDeliveries",
            schema: "integrations");
    }
}
