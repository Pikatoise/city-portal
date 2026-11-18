using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CityPortal.CitizenSubmissions.Migrations;

/// <inheritdoc />
public partial class SuggestionAssignmentAndDeletion : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<Guid>(
            name: "AssigneeId",
            schema: "citizen_submissions",
            table: "Suggestions",
            type: "uuid",
            nullable: true);

        migrationBuilder.CreateTable(
            name: "Deletions",
            schema: "citizen_submissions",
            columns: table => new
            {
                SuggestionId = table.Column<Guid>(type: "uuid", nullable: false),
                ActorId = table.Column<Guid>(type: "uuid", nullable: false),
                DeletedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                Reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Deletions", x => x.SuggestionId);
            });

        migrationBuilder.CreateIndex(
            name: "IX_Suggestions_AssigneeId_ExpiresAtUtc_Id",
            schema: "citizen_submissions",
            table: "Suggestions",
            columns: new[] { "AssigneeId", "ExpiresAtUtc", "Id" });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "Deletions",
            schema: "citizen_submissions");

        migrationBuilder.DropIndex(
            name: "IX_Suggestions_AssigneeId_ExpiresAtUtc_Id",
            schema: "citizen_submissions",
            table: "Suggestions");

        migrationBuilder.DropColumn(
            name: "AssigneeId",
            schema: "citizen_submissions",
            table: "Suggestions");
    }
}
