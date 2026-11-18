using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CityPortal.CitizenSubmissions.Migrations;

/// <inheritdoc />
public partial class PausedSuggestionExpiry : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // The preceding schema allowed assignments without articles. Make those
        // suggestions available again under their existing queue deadlines.
        migrationBuilder.Sql("""
            UPDATE citizen_submissions."Suggestions"
            SET "AssigneeId" = NULL, "Version" = "Version" + 1
            WHERE "AssigneeId" IS NOT NULL
            """);

        migrationBuilder.AddColumn<Guid>(
            name: "ArticleId",
            schema: "citizen_submissions",
            table: "Suggestions",
            type: "uuid",
            nullable: true);

        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "PausedAtUtc",
            schema: "citizen_submissions",
            table: "Suggestions",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.CreateIndex(
            name: "IX_Suggestions_ArticleId",
            schema: "citizen_submissions",
            table: "Suggestions",
            column: "ArticleId",
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_Suggestions_ArticleId",
            schema: "citizen_submissions",
            table: "Suggestions");

        migrationBuilder.DropColumn(
            name: "ArticleId",
            schema: "citizen_submissions",
            table: "Suggestions");

        migrationBuilder.DropColumn(
            name: "PausedAtUtc",
            schema: "citizen_submissions",
            table: "Suggestions");
    }
}
