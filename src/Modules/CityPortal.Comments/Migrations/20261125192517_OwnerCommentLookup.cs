using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CityPortal.Comments.Migrations;

/// <inheritdoc />
public partial class OwnerCommentLookup : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateIndex(
            name: "IX_Comments_OwnerId_CreatedAtUtc_Id",
            schema: "comments",
            table: "Comments",
            columns: new[] { "OwnerId", "CreatedAtUtc", "Id" },
            descending: new[] { false, true, true },
            filter: "\"OwnerId\" IS NOT NULL AND \"DeletedAtUtc\" IS NULL");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_Comments_OwnerId_CreatedAtUtc_Id",
            schema: "comments",
            table: "Comments");
    }
}
