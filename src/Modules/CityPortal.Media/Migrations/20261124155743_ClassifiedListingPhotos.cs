using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CityPortal.Media.Migrations;

/// <inheritdoc />
public partial class ClassifiedListingPhotos : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<bool>(
            name: "ForListing",
            schema: "media",
            table: "Images",
            type: "boolean",
            nullable: false,
            defaultValue: false);

        migrationBuilder.CreateTable(
            name: "ListingImages",
            schema: "media",
            columns: table => new
            {
                ImageId = table.Column<Guid>(type: "uuid", nullable: false),
                DraftId = table.Column<Guid>(type: "uuid", nullable: false),
                AlternativeText = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                Position = table.Column<int>(type: "integer", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ListingImages", x => x.ImageId);
                table.ForeignKey(
                    name: "FK_ListingImages_Images_ImageId",
                    column: x => x.ImageId,
                    principalSchema: "media",
                    principalTable: "Images",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_ListingImages_DraftId",
            schema: "media",
            table: "ListingImages",
            column: "DraftId");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "ListingImages",
            schema: "media");

        migrationBuilder.DropColumn(
            name: "ForListing",
            schema: "media",
            table: "Images");
    }
}
