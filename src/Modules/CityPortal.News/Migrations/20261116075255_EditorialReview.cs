using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CityPortal.News.Migrations;

/// <inheritdoc />
public partial class EditorialReview : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_Revisions_ArticleId",
            schema: "news",
            table: "Revisions");

        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "SubmittedAtUtc",
            schema: "news",
            table: "Revisions",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "Reason",
            schema: "news",
            table: "History",
            type: "character varying(2000)",
            maxLength: 2000,
            nullable: false,
            defaultValue: "");

        migrationBuilder.AddColumn<Guid>(
            name: "ReviewerId",
            schema: "news",
            table: "History",
            type: "uuid",
            nullable: true);

        migrationBuilder.AddColumn<long>(
            name: "Version",
            schema: "news",
            table: "History",
            type: "bigint",
            nullable: false,
            defaultValue: 0L);

        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "FirstPublishedAtUtc",
            schema: "news",
            table: "Articles",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.AddColumn<Guid>(
            name: "LastSubmittedRevisionId",
            schema: "news",
            table: "Articles",
            type: "uuid",
            nullable: true);

        migrationBuilder.AddColumn<Guid>(
            name: "ReviewerId",
            schema: "news",
            table: "Articles",
            type: "uuid",
            nullable: true);

        migrationBuilder.CreateIndex(
            name: "IX_Revisions_ArticleId_SubmittedAtUtc",
            schema: "news",
            table: "Revisions",
            columns: new[] { "ArticleId", "SubmittedAtUtc" });

        migrationBuilder.CreateIndex(
            name: "IX_Articles_ReviewerId",
            schema: "news",
            table: "Articles",
            column: "ReviewerId");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_Revisions_ArticleId_SubmittedAtUtc",
            schema: "news",
            table: "Revisions");

        migrationBuilder.DropIndex(
            name: "IX_Articles_ReviewerId",
            schema: "news",
            table: "Articles");

        migrationBuilder.DropColumn(
            name: "SubmittedAtUtc",
            schema: "news",
            table: "Revisions");

        migrationBuilder.DropColumn(
            name: "Reason",
            schema: "news",
            table: "History");

        migrationBuilder.DropColumn(
            name: "ReviewerId",
            schema: "news",
            table: "History");

        migrationBuilder.DropColumn(
            name: "Version",
            schema: "news",
            table: "History");

        migrationBuilder.DropColumn(
            name: "FirstPublishedAtUtc",
            schema: "news",
            table: "Articles");

        migrationBuilder.DropColumn(
            name: "LastSubmittedRevisionId",
            schema: "news",
            table: "Articles");

        migrationBuilder.DropColumn(
            name: "ReviewerId",
            schema: "news",
            table: "Articles");

        migrationBuilder.CreateIndex(
            name: "IX_Revisions_ArticleId",
            schema: "news",
            table: "Revisions",
            column: "ArticleId");
    }
}
