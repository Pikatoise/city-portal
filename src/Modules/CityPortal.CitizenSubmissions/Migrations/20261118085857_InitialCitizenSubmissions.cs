using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CityPortal.CitizenSubmissions.Migrations;

/// <inheritdoc />
public partial class InitialCitizenSubmissions : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.EnsureSchema(
            name: "citizen_submissions");

        migrationBuilder.CreateTable(
            name: "Suggestions",
            schema: "citizen_submissions",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                ResidentId = table.Column<Guid>(type: "uuid", nullable: false),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                ExpiresAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                FirstClaimedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                Version = table.Column<int>(type: "integer", nullable: false),
                Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                Description = table.Column<string>(type: "character varying(10000)", maxLength: 10000, nullable: false),
                EventDate = table.Column<DateOnly>(type: "date", nullable: true),
                Location = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                Contact = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Suggestions", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_Suggestions_ExpiresAtUtc",
            schema: "citizen_submissions",
            table: "Suggestions",
            column: "ExpiresAtUtc");

        migrationBuilder.CreateIndex(
            name: "IX_Suggestions_ResidentId_CreatedAtUtc_Id",
            schema: "citizen_submissions",
            table: "Suggestions",
            columns: new[] { "ResidentId", "CreatedAtUtc", "Id" });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "Suggestions",
            schema: "citizen_submissions");
    }
}
