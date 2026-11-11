using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace CityPortal.Identity.Migrations;
/// <inheritdoc />
public partial class RegistrationAndConfirmation : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "CreatedAtUtc",
            schema: "identity",
            table: "AspNetUsers",
            type: "timestamp with time zone",
            nullable: false,
            defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

        migrationBuilder.AddColumn<Guid>(
            name: "LatestConfirmationMessageId",
            schema: "identity",
            table: "AspNetUsers",
            type: "uuid",
            nullable: true);

        migrationBuilder.AddColumn<long>(
            name: "Version",
            schema: "identity",
            table: "AspNetUsers",
            type: "bigint",
            nullable: false,
            defaultValue: 0L);

        migrationBuilder.CreateTable(
            name: "Links",
            schema: "identity",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                TokenHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                ExpiresAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                State = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Links", x => x.Id);
                table.ForeignKey(
                    name: "FK_Links_AspNetUsers_AccountId",
                    column: x => x.AccountId,
                    principalSchema: "identity",
                    principalTable: "AspNetUsers",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "RequestAttempts",
            schema: "identity",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                Key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_RequestAttempts", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "Sessions",
            schema: "identity",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                ExpiresAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                Revoked = table.Column<bool>(type: "boolean", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Sessions", x => x.Id);
                table.ForeignKey(
                    name: "FK_Sessions_AspNetUsers_AccountId",
                    column: x => x.AccountId,
                    principalSchema: "identity",
                    principalTable: "AspNetUsers",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_Links_AccountId_CreatedAtUtc",
            schema: "identity",
            table: "Links",
            columns: new[] { "AccountId", "CreatedAtUtc" });

        migrationBuilder.CreateIndex(
            name: "IX_RequestAttempts_Key_CreatedAtUtc",
            schema: "identity",
            table: "RequestAttempts",
            columns: new[] { "Key", "CreatedAtUtc" });

        migrationBuilder.CreateIndex(
            name: "IX_Sessions_AccountId",
            schema: "identity",
            table: "Sessions",
            column: "AccountId");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "Links",
            schema: "identity");

        migrationBuilder.DropTable(
            name: "RequestAttempts",
            schema: "identity");

        migrationBuilder.DropTable(
            name: "Sessions",
            schema: "identity");

        migrationBuilder.DropColumn(
            name: "CreatedAtUtc",
            schema: "identity",
            table: "AspNetUsers");

        migrationBuilder.DropColumn(
            name: "LatestConfirmationMessageId",
            schema: "identity",
            table: "AspNetUsers");

        migrationBuilder.DropColumn(
            name: "Version",
            schema: "identity",
            table: "AspNetUsers");
    }
}
