using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CityPortal.News.Migrations;

/// <inheritdoc />
public partial class DiscoveryReadPerformance : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateIndex(
            name: "IX_Articles_PublishedRevisionId",
            schema: "news",
            table: "Articles",
            column: "PublishedRevisionId",
            filter: "\"PublicationState\" = 'Published' AND \"DeletedAtUtc\" IS NULL");

        // SET STORAGE affects future writes; recompute existing generated vectors too.
        migrationBuilder.Sql("""
            ALTER TABLE news."Revisions" ALTER COLUMN "SearchVector" SET STORAGE MAIN;
            UPDATE news."Revisions" SET "Title" = "Title";
            """);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_Articles_PublishedRevisionId",
            schema: "news",
            table: "Articles");
        migrationBuilder.Sql("""ALTER TABLE news."Revisions" ALTER COLUMN "SearchVector" SET STORAGE EXTENDED;""");
    }
}
