using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CityPortal.Identity.Migrations;

/// <inheritdoc />
public partial class PasswordRecoveryPurpose : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "Purpose",
            schema: "identity",
            table: "Links",
            type: "character varying(20)",
            maxLength: 20,
            nullable: false,
            defaultValue: "confirmation");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "Purpose",
            schema: "identity",
            table: "Links");
    }
}
