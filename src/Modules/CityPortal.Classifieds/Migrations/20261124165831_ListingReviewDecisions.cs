using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CityPortal.Classifieds.Migrations;

/// <inheritdoc />
public partial class ListingReviewDecisions : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "ReviewOutcome",
            schema: "classifieds",
            table: "Drafts",
            type: "character varying(8)",
            maxLength: 8,
            nullable: true);

        migrationBuilder.CreateTable(
            name: "ReviewDecisions",
            schema: "classifieds",
            columns: table => new
            {
                ListingId = table.Column<Guid>(type: "uuid", nullable: false),
                SubmittedVersion = table.Column<int>(type: "integer", nullable: false),
                ActorId = table.Column<Guid>(type: "uuid", nullable: false),
                DecidedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                Outcome = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ReviewDecisions", x => new { x.ListingId, x.SubmittedVersion });
            });

        migrationBuilder.CreateIndex(
            name: "IX_Drafts_SubmittedAtUtc_Id",
            schema: "classifieds",
            table: "Drafts",
            columns: new[] { "SubmittedAtUtc", "Id" },
            filter: "\"SubmittedAtUtc\" IS NOT NULL");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "ReviewDecisions",
            schema: "classifieds");

        migrationBuilder.DropIndex(
            name: "IX_Drafts_SubmittedAtUtc_Id",
            schema: "classifieds",
            table: "Drafts");

        migrationBuilder.DropColumn(
            name: "ReviewOutcome",
            schema: "classifieds",
            table: "Drafts");
    }
}
