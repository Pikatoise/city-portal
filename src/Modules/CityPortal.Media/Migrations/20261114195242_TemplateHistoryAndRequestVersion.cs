using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CityPortal.Media.Migrations;
/// <inheritdoc />
public partial class TemplateHistoryAndRequestVersion : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<long>(
            name: "Version",
            schema: "media",
            table: "RemovalRequests",
            type: "bigint",
            nullable: false,
            defaultValue: 1L);

        migrationBuilder.AddColumn<string>(
            name: "Approval",
            schema: "media",
            table: "Events",
            type: "character varying(20)",
            maxLength: 20,
            nullable: false,
            defaultValue: "");

        migrationBuilder.AddColumn<bool>(
            name: "Archived",
            schema: "media",
            table: "Events",
            type: "boolean",
            nullable: false,
            defaultValue: false);

        migrationBuilder.AddColumn<bool>(
            name: "Blocked",
            schema: "media",
            table: "Events",
            type: "boolean",
            nullable: false,
            defaultValue: false);

        migrationBuilder.AddColumn<string>(
            name: "Name",
            schema: "media",
            table: "Events",
            type: "character varying(200)",
            maxLength: 200,
            nullable: false,
            defaultValue: "");

        migrationBuilder.AddColumn<long>(
            name: "Version",
            schema: "media",
            table: "Events",
            type: "bigint",
            nullable: false,
            defaultValue: 0L);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "Version",
            schema: "media",
            table: "RemovalRequests");

        migrationBuilder.DropColumn(
            name: "Approval",
            schema: "media",
            table: "Events");

        migrationBuilder.DropColumn(
            name: "Archived",
            schema: "media",
            table: "Events");

        migrationBuilder.DropColumn(
            name: "Blocked",
            schema: "media",
            table: "Events");

        migrationBuilder.DropColumn(
            name: "Name",
            schema: "media",
            table: "Events");

        migrationBuilder.DropColumn(
            name: "Version",
            schema: "media",
            table: "Events");
    }
}
