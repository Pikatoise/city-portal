using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CityPortal.Identity.Migrations;

/// <inheritdoc />
public partial class AccountContentBlocking : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "BlockReason",
            schema: "identity",
            table: "AspNetUsers",
            type: "character varying(500)",
            maxLength: 500,
            nullable: true);

        migrationBuilder.CreateTable(
            name: "BlockDecisions",
            schema: "identity",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                TargetId = table.Column<Guid>(type: "uuid", nullable: false),
                ActorId = table.Column<Guid>(type: "uuid", nullable: false),
                DecidedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_BlockDecisions", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_BlockDecisions_TargetId_DecidedAtUtc",
            schema: "identity",
            table: "BlockDecisions",
            columns: new[] { "TargetId", "DecidedAtUtc" });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "BlockDecisions",
            schema: "identity");

        migrationBuilder.DropColumn(
            name: "BlockReason",
            schema: "identity",
            table: "AspNetUsers");
    }
}
