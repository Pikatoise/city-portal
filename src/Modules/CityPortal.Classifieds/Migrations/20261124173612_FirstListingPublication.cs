using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CityPortal.Classifieds.Migrations;

/// <inheritdoc />
public partial class FirstListingPublication : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AlterColumn<string>(
            name: "Reason",
            schema: "classifieds",
            table: "ReviewDecisions",
            type: "character varying(500)",
            maxLength: 500,
            nullable: true,
            oldClrType: typeof(string),
            oldType: "character varying(500)",
            oldMaxLength: 500);

        migrationBuilder.AlterColumn<string>(
            name: "ReviewOutcome",
            schema: "classifieds",
            table: "Drafts",
            type: "character varying(12)",
            maxLength: 12,
            nullable: true,
            oldClrType: typeof(string),
            oldType: "character varying(8)",
            oldMaxLength: 8,
            oldNullable: true);

        migrationBuilder.CreateTable(
            name: "ListingBlockDecisions",
            schema: "classifieds",
            columns: table => new
            {
                ListingId = table.Column<Guid>(type: "uuid", nullable: false),
                Version = table.Column<int>(type: "integer", nullable: false),
                ActorId = table.Column<Guid>(type: "uuid", nullable: false),
                DecidedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ListingBlockDecisions", x => new { x.ListingId, x.Version });
            });

        migrationBuilder.CreateTable(
            name: "Publications",
            schema: "classifieds",
            columns: table => new
            {
                ListingId = table.Column<Guid>(type: "uuid", nullable: false),
                RevisionId = table.Column<Guid>(type: "uuid", nullable: false),
                OwnerId = table.Column<Guid>(type: "uuid", nullable: false),
                Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                Description = table.Column<string>(type: "character varying(10000)", maxLength: 10000, nullable: false),
                CategoryId = table.Column<Guid>(type: "uuid", nullable: false),
                DistrictId = table.Column<Guid>(type: "uuid", nullable: false),
                Price = table.Column<long>(type: "bigint", nullable: true),
                Phone = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                Email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: true),
                PublishedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                ExpiresAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                HiddenByAccountBlock = table.Column<bool>(type: "boolean", nullable: false),
                HiddenByListingBlock = table.Column<bool>(type: "boolean", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Publications", x => x.ListingId);
                table.ForeignKey(
                    name: "FK_Publications_Drafts_ListingId",
                    column: x => x.ListingId,
                    principalSchema: "classifieds",
                    principalTable: "Drafts",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_Publications_OwnerId",
            schema: "classifieds",
            table: "Publications",
            column: "OwnerId");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "ListingBlockDecisions",
            schema: "classifieds");

        migrationBuilder.DropTable(
            name: "Publications",
            schema: "classifieds");

        migrationBuilder.AlterColumn<string>(
            name: "Reason",
            schema: "classifieds",
            table: "ReviewDecisions",
            type: "character varying(500)",
            maxLength: 500,
            nullable: false,
            defaultValue: "",
            oldClrType: typeof(string),
            oldType: "character varying(500)",
            oldMaxLength: 500,
            oldNullable: true);

        migrationBuilder.AlterColumn<string>(
            name: "ReviewOutcome",
            schema: "classifieds",
            table: "Drafts",
            type: "character varying(8)",
            maxLength: 8,
            nullable: true,
            oldClrType: typeof(string),
            oldType: "character varying(12)",
            oldMaxLength: 12,
            oldNullable: true);
    }
}
