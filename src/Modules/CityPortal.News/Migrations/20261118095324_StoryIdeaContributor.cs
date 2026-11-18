using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CityPortal.News.Migrations;

/// <inheritdoc />
public partial class StoryIdeaContributor : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<Guid>(
            name: "IdeaContributorId",
            schema: "news",
            table: "Articles",
            type: "uuid",
            nullable: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "IdeaContributorId",
            schema: "news",
            table: "Articles");
    }
}
