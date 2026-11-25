using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace CityPortal.Reports.Migrations;

/// <inheritdoc />
public partial class ContentReports : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.EnsureSchema(
            name: "reports");

        migrationBuilder.CreateTable(
            name: "Decisions",
            schema: "reports",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                ActorId = table.Column<Guid>(type: "uuid", nullable: false),
                Outcome = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                DecidedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Decisions", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "Targets",
            schema: "reports",
            columns: table => new
            {
                Kind = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                OwnerId = table.Column<Guid>(type: "uuid", nullable: false),
                Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Targets", x => new { x.Kind, x.Id });
            });

        migrationBuilder.CreateTable(
            name: "Reports",
            schema: "reports",
            columns: table => new
            {
                Sequence = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                Kind = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
                TargetId = table.Column<Guid>(type: "uuid", nullable: false),
                Category = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                Text = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                ReceivedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                DecisionId = table.Column<Guid>(type: "uuid", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Reports", x => x.Sequence);
                table.ForeignKey(
                    name: "FK_Reports_Decisions_DecisionId",
                    column: x => x.DecisionId,
                    principalSchema: "reports",
                    principalTable: "Decisions",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_Reports_Targets_Kind_TargetId",
                    columns: x => new { x.Kind, x.TargetId },
                    principalSchema: "reports",
                    principalTable: "Targets",
                    principalColumns: new[] { "Kind", "Id" },
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_Reports_DecisionId",
            schema: "reports",
            table: "Reports",
            column: "DecisionId");

        migrationBuilder.CreateIndex(
            name: "IX_Reports_Kind_TargetId_Sequence",
            schema: "reports",
            table: "Reports",
            columns: new[] { "Kind", "TargetId", "Sequence" });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "Reports",
            schema: "reports");

        migrationBuilder.DropTable(
            name: "Decisions",
            schema: "reports");

        migrationBuilder.DropTable(
            name: "Targets",
            schema: "reports");
    }
}
