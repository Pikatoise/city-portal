using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CityPortal.News.Migrations;

/// <inheritdoc />
public partial class PublicArticleAddresses : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<DateOnly>(
            name: "FirstPublishedLocalDate",
            schema: "news",
            table: "Articles",
            type: "date",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "PublicAddress",
            schema: "news",
            table: "Articles",
            type: "character varying(144)",
            maxLength: 144,
            nullable: true);

        migrationBuilder.CreateIndex(
            name: "IX_Articles_PublicAddress",
            schema: "news",
            table: "Articles",
            column: "PublicAddress",
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_Articles_PublicAddress",
            schema: "news",
            table: "Articles");

        migrationBuilder.DropColumn(
            name: "FirstPublishedLocalDate",
            schema: "news",
            table: "Articles");

        migrationBuilder.DropColumn(
            name: "PublicAddress",
            schema: "news",
            table: "Articles");
    }
}
