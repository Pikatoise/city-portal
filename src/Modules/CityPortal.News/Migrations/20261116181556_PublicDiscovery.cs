using System.Text.Json;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;
using NpgsqlTypes;

#nullable disable

namespace CityPortal.News.Migrations;

/// <inheritdoc />
public partial class PublicDiscovery : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<bool>(
            name: "Featured",
            schema: "news",
            table: "Articles",
            type: "boolean",
            nullable: false,
            defaultValue: false);

        migrationBuilder.AddColumn<Guid>(
            name: "CategoryId",
            schema: "news",
            table: "Revisions",
            type: "uuid",
            nullable: true,
            computedColumnSql: "(\"ContentJson\" ->> 'CategoryId')::uuid",
            stored: true);

        migrationBuilder.AddColumn<string>(
            name: "CoverAlt",
            schema: "news",
            table: "Revisions",
            type: "text",
            nullable: true,
            computedColumnSql: "coalesce(\"ContentJson\" -> 'Cover' ->> 'Alt', '')",
            stored: true);

        migrationBuilder.AddColumn<Guid>(
            name: "CoverMediaId",
            schema: "news",
            table: "Revisions",
            type: "uuid",
            nullable: true,
            computedColumnSql: "(\"ContentJson\" -> 'Cover' ->> 'MediaId')::uuid",
            stored: true);

        migrationBuilder.AddColumn<NpgsqlTsVector>(
            name: "SearchVector",
            schema: "news",
            table: "Revisions",
            type: "tsvector",
            nullable: false,
            computedColumnSql: "setweight(to_tsvector('russian', coalesce(\"Title\", '')), 'A') || setweight(to_tsvector('russian', coalesce(\"ContentJson\" ->> 'Summary', '')), 'B') || setweight(to_tsvector('russian', coalesce(\"PlainText\", '')), 'C')",
            stored: true);

        migrationBuilder.AddColumn<string>(
            name: "Summary",
            schema: "news",
            table: "Revisions",
            type: "text",
            nullable: false,
            computedColumnSql: "coalesce(\"ContentJson\" ->> 'Summary', '')",
            stored: true);

        migrationBuilder.AddColumn<JsonDocument>(
            name: "Tags",
            schema: "news",
            table: "Revisions",
            type: "jsonb",
            nullable: false,
            computedColumnSql: "coalesce(\"ContentJson\" -> 'TagIds', '[]')",
            stored: true);

        migrationBuilder.CreateTable(
            name: "CurationChanges",
            schema: "news",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                ActorId = table.Column<Guid>(type: "uuid", nullable: false),
                ArticleId = table.Column<Guid>(type: "uuid", nullable: true),
                Action = table.Column<string>(type: "text", nullable: false),
                SelectionJson = table.Column<string>(type: "jsonb", nullable: false),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_CurationChanges", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "HomepageSelections",
            schema: "news",
            columns: table => new
            {
                Id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                Version = table.Column<long>(type: "bigint", nullable: false),
                FirstCategoryId = table.Column<Guid>(type: "uuid", nullable: true),
                SecondCategoryId = table.Column<Guid>(type: "uuid", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_HomepageSelections", x => x.Id);
            });

        migrationBuilder.InsertData(
            schema: "news",
            table: "HomepageSelections",
            columns: new[] { "Id", "FirstCategoryId", "SecondCategoryId", "Version" },
            values: new object[] { 1, null, null, 1L });

        migrationBuilder.CreateIndex(
            name: "IX_Revisions_CategoryId",
            schema: "news",
            table: "Revisions",
            column: "CategoryId");

        migrationBuilder.CreateIndex(
            name: "IX_Revisions_SearchVector",
            schema: "news",
            table: "Revisions",
            column: "SearchVector")
            .Annotation("Npgsql:IndexMethod", "GIN");

        migrationBuilder.CreateIndex(
            name: "IX_Revisions_Tags",
            schema: "news",
            table: "Revisions",
            column: "Tags")
            .Annotation("Npgsql:IndexMethod", "GIN");

        migrationBuilder.CreateIndex(
            name: "IX_Articles_FirstPublishedAtUtc_Id",
            schema: "news",
            table: "Articles",
            columns: new[] { "FirstPublishedAtUtc", "Id" },
            descending: new[] { true, false },
            filter: "\"PublicationState\" = 'Published' AND \"DeletedAtUtc\" IS NULL");

        migrationBuilder.CreateIndex(
            name: "IX_CurationChanges_CreatedAtUtc",
            schema: "news",
            table: "CurationChanges",
            column: "CreatedAtUtc");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "CurationChanges",
            schema: "news");

        migrationBuilder.DropTable(
            name: "HomepageSelections",
            schema: "news");

        migrationBuilder.DropIndex(
            name: "IX_Revisions_CategoryId",
            schema: "news",
            table: "Revisions");

        migrationBuilder.DropIndex(
            name: "IX_Revisions_SearchVector",
            schema: "news",
            table: "Revisions");

        migrationBuilder.DropIndex(
            name: "IX_Revisions_Tags",
            schema: "news",
            table: "Revisions");

        migrationBuilder.DropIndex(
            name: "IX_Articles_FirstPublishedAtUtc_Id",
            schema: "news",
            table: "Articles");

        migrationBuilder.DropColumn(
            name: "CategoryId",
            schema: "news",
            table: "Revisions");

        migrationBuilder.DropColumn(
            name: "CoverAlt",
            schema: "news",
            table: "Revisions");

        migrationBuilder.DropColumn(
            name: "CoverMediaId",
            schema: "news",
            table: "Revisions");

        migrationBuilder.DropColumn(
            name: "SearchVector",
            schema: "news",
            table: "Revisions");

        migrationBuilder.DropColumn(
            name: "Summary",
            schema: "news",
            table: "Revisions");

        migrationBuilder.DropColumn(
            name: "Tags",
            schema: "news",
            table: "Revisions");

        migrationBuilder.DropColumn(
            name: "Featured",
            schema: "news",
            table: "Articles");
    }
}
