using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CityPortal.Classifieds.Migrations;

/// <inheritdoc />
public partial class ListingSubmission : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "SubmittedAtUtc",
            schema: "classifieds",
            table: "Drafts",
            type: "timestamp with time zone",
            nullable: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "SubmittedAtUtc",
            schema: "classifieds",
            table: "Drafts");
    }
}
