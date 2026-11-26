using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CityPortal.Comments.Migrations;

/// <inheritdoc />
public partial class ArticleComments : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.EnsureSchema(
            name: "comments");

        migrationBuilder.CreateTable(
            name: "Comments",
            schema: "comments",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                ArticleId = table.Column<Guid>(type: "uuid", nullable: false),
                OwnerId = table.Column<Guid>(type: "uuid", nullable: true),
                Body = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                DeletedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                HiddenAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Comments", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_Comments_ArticleId_CreatedAtUtc_Id",
            schema: "comments",
            table: "Comments",
            columns: new[] { "ArticleId", "CreatedAtUtc", "Id" },
            descending: new[] { false, true, true },
            filter: "\"DeletedAtUtc\" IS NULL AND \"HiddenAtUtc\" IS NULL");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "Comments",
            schema: "comments");
    }
}
