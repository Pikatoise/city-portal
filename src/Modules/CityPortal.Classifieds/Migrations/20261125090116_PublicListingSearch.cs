using Microsoft.EntityFrameworkCore.Migrations;
using NpgsqlTypes;

#nullable disable

namespace CityPortal.Classifieds.Migrations;

/// <inheritdoc />
public partial class PublicListingSearch : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<NpgsqlTsVector>(
            name: "SearchVector",
            schema: "classifieds",
            table: "Publications",
            type: "tsvector",
            nullable: false,
            computedColumnSql: "setweight(to_tsvector('russian', \"Title\"), 'A') || setweight(to_tsvector('russian', \"Description\"), 'B')",
            stored: true);

        migrationBuilder.CreateIndex(
            name: "IX_Publications_PublishedAtUtc_ListingId",
            schema: "classifieds",
            table: "Publications",
            columns: new[] { "PublishedAtUtc", "ListingId" },
            descending: new[] { true, false });

        migrationBuilder.CreateIndex(
            name: "IX_Publications_SearchVector",
            schema: "classifieds",
            table: "Publications",
            column: "SearchVector")
            .Annotation("Npgsql:IndexMethod", "GIN");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_Publications_PublishedAtUtc_ListingId",
            schema: "classifieds",
            table: "Publications");

        migrationBuilder.DropIndex(
            name: "IX_Publications_SearchVector",
            schema: "classifieds",
            table: "Publications");

        migrationBuilder.DropColumn(
            name: "SearchVector",
            schema: "classifieds",
            table: "Publications");
    }
}
