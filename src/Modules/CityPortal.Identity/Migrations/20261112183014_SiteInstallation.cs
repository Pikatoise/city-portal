using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace CityPortal.Identity.Migrations;

/// <inheritdoc />
public partial class SiteInstallation : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "SiteConfiguration",
            schema: "identity",
            columns: table => new
            {
                Id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                Name = table.Column<string>(type: "text", nullable: true),
                City = table.Column<string>(type: "text", nullable: true),
                Region = table.Column<string>(type: "text", nullable: true),
                TimeZone = table.Column<string>(type: "text", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_SiteConfiguration", x => x.Id);
            });
        migrationBuilder.Sql("INSERT INTO identity.\"SiteConfiguration\" (\"Id\") SELECT 1 WHERE EXISTS (SELECT 1 FROM identity.\"AspNetUsers\")");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "SiteConfiguration",
            schema: "identity");
    }
}
