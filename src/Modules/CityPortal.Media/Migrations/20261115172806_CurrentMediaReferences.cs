using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CityPortal.Media.Migrations;

/// <inheritdoc />
public partial class CurrentMediaReferences : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<bool>(
            name: "Current",
            schema: "media",
            table: "References",
            type: "boolean",
            nullable: false,
            defaultValue: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "Current",
            schema: "media",
            table: "References");
    }
}
