using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CityPortal.News.Migrations;

/// <inheritdoc />
public partial class TaxonomyAudit : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "TaxonomyChanges",
            schema: "news",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                TaxonomyId = table.Column<Guid>(type: "uuid", nullable: false),
                ActorId = table.Column<Guid>(type: "uuid", nullable: false),
                Action = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                Archived = table.Column<bool>(type: "boolean", nullable: false),
                Version = table.Column<long>(type: "bigint", nullable: false),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_TaxonomyChanges", x => x.Id);
                table.ForeignKey(
                    name: "FK_TaxonomyChanges_Taxonomy_TaxonomyId",
                    column: x => x.TaxonomyId,
                    principalSchema: "news",
                    principalTable: "Taxonomy",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_TaxonomyChanges_TaxonomyId_Version",
            schema: "news",
            table: "TaxonomyChanges",
            columns: new[] { "TaxonomyId", "Version" },
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "TaxonomyChanges",
            schema: "news");
    }
}
