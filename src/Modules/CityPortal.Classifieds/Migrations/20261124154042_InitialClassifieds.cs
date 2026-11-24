using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CityPortal.Classifieds.Migrations;

/// <inheritdoc />
public partial class InitialClassifieds : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.EnsureSchema(
            name: "classifieds");

        migrationBuilder.CreateTable(
            name: "Catalog",
            schema: "classifieds",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                Kind = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                Name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                NormalizedName = table.Column<string>(type: "text", nullable: false),
                RequiresPrice = table.Column<bool>(type: "boolean", nullable: false),
                Archived = table.Column<bool>(type: "boolean", nullable: false),
                Version = table.Column<int>(type: "integer", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Catalog", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "Drafts",
            schema: "classifieds",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                OwnerId = table.Column<Guid>(type: "uuid", nullable: false),
                Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                Description = table.Column<string>(type: "character varying(10000)", maxLength: 10000, nullable: true),
                CategoryId = table.Column<Guid>(type: "uuid", nullable: true),
                DistrictId = table.Column<Guid>(type: "uuid", nullable: true),
                Price = table.Column<long>(type: "bigint", nullable: true),
                Phone = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                Email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: true),
                UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                Version = table.Column<int>(type: "integer", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Drafts", x => x.Id);
                table.ForeignKey(
                    name: "FK_Drafts_Catalog_CategoryId",
                    column: x => x.CategoryId,
                    principalSchema: "classifieds",
                    principalTable: "Catalog",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_Drafts_Catalog_DistrictId",
                    column: x => x.DistrictId,
                    principalSchema: "classifieds",
                    principalTable: "Catalog",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_Catalog_Kind_NormalizedName",
            schema: "classifieds",
            table: "Catalog",
            columns: new[] { "Kind", "NormalizedName" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_Drafts_CategoryId",
            schema: "classifieds",
            table: "Drafts",
            column: "CategoryId");

        migrationBuilder.CreateIndex(
            name: "IX_Drafts_DistrictId",
            schema: "classifieds",
            table: "Drafts",
            column: "DistrictId");

        migrationBuilder.CreateIndex(
            name: "IX_Drafts_OwnerId_UpdatedAtUtc_Id",
            schema: "classifieds",
            table: "Drafts",
            columns: new[] { "OwnerId", "UpdatedAtUtc", "Id" },
            descending: new[] { false, true, true });

        migrationBuilder.InsertData(
            schema: "classifieds",
            table: "Catalog",
            columns: new[] { "Id", "Kind", "Name", "NormalizedName", "RequiresPrice", "Archived", "Version" },
            values: new object[,]
            {
                { new Guid("fd15be26-7273-4068-bb75-870ea6866211"), "categories", "Электроника и вещи", "ЭЛЕКТРОНИКА И ВЕЩИ", true, false, 1 },
                { new Guid("fd15be26-7273-4068-bb75-870ea6866212"), "categories", "Недвижимость", "НЕДВИЖИМОСТЬ", true, false, 1 },
                { new Guid("fd15be26-7273-4068-bb75-870ea6866213"), "categories", "Транспорт", "ТРАНСПОРТ", true, false, 1 },
                { new Guid("fd15be26-7273-4068-bb75-870ea6866214"), "categories", "Услуги", "УСЛУГИ", false, false, 1 },
                { new Guid("fd15be26-7273-4068-bb75-870ea6866215"), "categories", "Работа", "РАБОТА", false, false, 1 },
                { new Guid("fd15be26-7273-4068-bb75-870ea6866216"), "categories", "Животные и потерявшиеся питомцы", "ЖИВОТНЫЕ И ПОТЕРЯВШИЕСЯ ПИТОМЦЫ", false, false, 1 },
                { new Guid("fd15be26-7273-4068-bb75-870ea6866217"), "categories", "Разное", "РАЗНОЕ", false, false, 1 }
            });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "Drafts",
            schema: "classifieds");

        migrationBuilder.DropTable(
            name: "Catalog",
            schema: "classifieds");
    }
}
