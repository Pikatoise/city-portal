using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CityPortal.Media.Migrations;

/// <inheritdoc />
public partial class PublishedListingPhotos : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "PublishedListingImages",
            schema: "media",
            columns: table => new
            {
                RevisionId = table.Column<Guid>(type: "uuid", nullable: false),
                ImageId = table.Column<Guid>(type: "uuid", nullable: false),
                AlternativeText = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                Position = table.Column<int>(type: "integer", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_PublishedListingImages", x => new { x.RevisionId, x.ImageId });
                table.ForeignKey(
                    name: "FK_PublishedListingImages_Images_ImageId",
                    column: x => x.ImageId,
                    principalSchema: "media",
                    principalTable: "Images",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_PublishedListingImages_ImageId",
            schema: "media",
            table: "PublishedListingImages",
            column: "ImageId");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "PublishedListingImages",
            schema: "media");
    }
}
