using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CityPortal.Media.Migrations;

/// <inheritdoc />
public partial class CitizenSuggestionImages : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<bool>(
            name: "AuthorVisible",
            schema: "media",
            table: "References",
            type: "boolean",
            nullable: false,
            defaultValue: true);

        migrationBuilder.AddColumn<bool>(
            name: "ForSuggestion",
            schema: "media",
            table: "Images",
            type: "boolean",
            nullable: false,
            defaultValue: false);

        migrationBuilder.CreateTable(
            name: "SuggestionImages",
            schema: "media",
            columns: table => new
            {
                ImageId = table.Column<Guid>(type: "uuid", nullable: false),
                SuggestionId = table.Column<Guid>(type: "uuid", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_SuggestionImages", x => x.ImageId);
                table.ForeignKey(
                    name: "FK_SuggestionImages_Images_ImageId",
                    column: x => x.ImageId,
                    principalSchema: "media",
                    principalTable: "Images",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_SuggestionImages_SuggestionId",
            schema: "media",
            table: "SuggestionImages",
            column: "SuggestionId");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "AuthorVisible",
            schema: "media",
            table: "References");

        migrationBuilder.DropTable(
            name: "SuggestionImages",
            schema: "media");

        migrationBuilder.DropColumn(
            name: "ForSuggestion",
            schema: "media",
            table: "Images");
    }
}
