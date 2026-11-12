using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CityPortal.Identity.Migrations;

/// <inheritdoc />
public partial class AdministrationHistory : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "RoleChanges",
            schema: "identity",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                ActorId = table.Column<Guid>(type: "uuid", nullable: true),
                ActorKind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                TargetAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                OldRoles = table.Column<string[]>(type: "text[]", nullable: false),
                NewRoles = table.Column<string[]>(type: "text[]", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_RoleChanges", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_RoleChanges_TargetAccountId_CreatedAtUtc",
            schema: "identity",
            table: "RoleChanges",
            columns: new[] { "TargetAccountId", "CreatedAtUtc" });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "RoleChanges",
            schema: "identity");
    }
}
