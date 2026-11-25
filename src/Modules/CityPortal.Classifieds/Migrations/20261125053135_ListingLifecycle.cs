using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CityPortal.Classifieds.Migrations;

/// <inheritdoc />
public partial class ListingLifecycle : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "OriginalDetails",
            schema: "classifieds",
            table: "Publications",
            type: "text",
            nullable: true);

        migrationBuilder.AddColumn<Guid>(
            name: "OriginalRevisionId",
            schema: "classifieds",
            table: "Publications",
            type: "uuid",
            nullable: true);

        migrationBuilder.AddColumn<bool>(
            name: "WithdrawnByOwner",
            schema: "classifieds",
            table: "Publications",
            type: "boolean",
            nullable: false,
            defaultValue: false);

        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "EditReviewQueuedAtUtc",
            schema: "classifieds",
            table: "Drafts",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.CreateIndex(
            name: "IX_Drafts_EditReviewQueuedAtUtc_Id",
            schema: "classifieds",
            table: "Drafts",
            columns: new[] { "EditReviewQueuedAtUtc", "Id" },
            filter: "\"EditReviewQueuedAtUtc\" IS NOT NULL");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_Drafts_EditReviewQueuedAtUtc_Id",
            schema: "classifieds",
            table: "Drafts");

        migrationBuilder.DropColumn(
            name: "OriginalDetails",
            schema: "classifieds",
            table: "Publications");

        migrationBuilder.DropColumn(
            name: "OriginalRevisionId",
            schema: "classifieds",
            table: "Publications");

        migrationBuilder.DropColumn(
            name: "WithdrawnByOwner",
            schema: "classifieds",
            table: "Publications");

        migrationBuilder.DropColumn(
            name: "EditReviewQueuedAtUtc",
            schema: "classifieds",
            table: "Drafts");
    }
}
