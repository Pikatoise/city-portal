using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CityPortal.Notifications.Migrations;

/// <inheritdoc />
public partial class AccountEmailDelivery : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        using Stream stream = typeof(AccountEmailDelivery).Assembly.GetManifestResourceStream("CityPortal.Notifications.Storage.quartz-postgresql.sql")
            ?? throw new InvalidOperationException("Quartz migration resource is missing.");
        using var reader = new StreamReader(stream);
        migrationBuilder.Sql(reader.ReadToEnd());
        migrationBuilder.EnsureSchema(
            name: "notifications");

        migrationBuilder.CreateTable(
            name: "EmailDeliveries",
            schema: "notifications",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                LinkId = table.Column<Guid>(type: "uuid", nullable: false),
                Purpose = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                ProtectedPayload = table.Column<string>(type: "text", nullable: false),
                State = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                AttemptCount = table.Column<int>(type: "integer", nullable: false),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                ExpiresAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                LastAttemptAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                NextAttemptAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                CompletedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                ErrorCode = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                Version = table.Column<long>(type: "bigint", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_EmailDeliveries", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "EmailAttempts",
            schema: "notifications",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                DeliveryId = table.Column<Guid>(type: "uuid", nullable: false),
                Number = table.Column<int>(type: "integer", nullable: false),
                StartedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                FinishedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                Result = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                ErrorCode = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_EmailAttempts", x => x.Id);
                table.ForeignKey(
                    name: "FK_EmailAttempts_EmailDeliveries_DeliveryId",
                    column: x => x.DeliveryId,
                    principalSchema: "notifications",
                    principalTable: "EmailDeliveries",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_EmailAttempts_DeliveryId_Number",
            schema: "notifications",
            table: "EmailAttempts",
            columns: new[] { "DeliveryId", "Number" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_EmailDeliveries_AccountId",
            schema: "notifications",
            table: "EmailDeliveries",
            column: "AccountId");

        migrationBuilder.CreateIndex(
            name: "IX_EmailDeliveries_CompletedAtUtc",
            schema: "notifications",
            table: "EmailDeliveries",
            column: "CompletedAtUtc");

        migrationBuilder.CreateIndex(
            name: "IX_EmailDeliveries_State_NextAttemptAtUtc",
            schema: "notifications",
            table: "EmailDeliveries",
            columns: new[] { "State", "NextAttemptAtUtc" });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DROP TABLE notifications.qrtz_fired_triggers, notifications.qrtz_paused_trigger_grps,
                notifications.qrtz_paused_job_grps, notifications.qrtz_scheduler_state, notifications.qrtz_locks,
                notifications.qrtz_simprop_triggers, notifications.qrtz_simple_triggers, notifications.qrtz_cron_triggers,
                notifications.qrtz_blob_triggers, notifications.qrtz_triggers, notifications.qrtz_job_details,
                notifications.qrtz_calendars, notifications.qrtz_execution_history, notifications.qrtz_misfire_history,
                notifications.qrtz_job_status;
            """);
        migrationBuilder.DropTable(
            name: "EmailAttempts",
            schema: "notifications");

        migrationBuilder.DropTable(
            name: "EmailDeliveries",
            schema: "notifications");
    }
}
