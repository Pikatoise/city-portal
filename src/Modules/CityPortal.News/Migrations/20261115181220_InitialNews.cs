using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CityPortal.News.Migrations;

/// <inheritdoc />
public partial class InitialNews : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.EnsureSchema(
            name: "news");

        migrationBuilder.CreateTable(
            name: "Articles",
            schema: "news",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                AuthorId = table.Column<Guid>(type: "uuid", nullable: false),
                Version = table.Column<long>(type: "bigint", nullable: false),
                PublicationState = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                WorkingRevisionId = table.Column<Guid>(type: "uuid", nullable: false),
                PublishedRevisionId = table.Column<Guid>(type: "uuid", nullable: true),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                DeletedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Articles", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "Taxonomy",
            schema: "news",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                Kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                NormalizedName = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                Archived = table.Column<bool>(type: "boolean", nullable: false),
                Version = table.Column<long>(type: "bigint", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Taxonomy", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "Revisions",
            schema: "news",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                ArticleId = table.Column<Guid>(type: "uuid", nullable: false),
                State = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                ContentJson = table.Column<string>(type: "jsonb", nullable: false),
                Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                PlainText = table.Column<string>(type: "text", nullable: false),
                PreviewHtml = table.Column<string>(type: "text", nullable: false),
                Retained = table.Column<bool>(type: "boolean", nullable: false),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Revisions", x => x.Id);
                table.ForeignKey(
                    name: "FK_Revisions_Articles_ArticleId",
                    column: x => x.ArticleId,
                    principalSchema: "news",
                    principalTable: "Articles",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "History",
            schema: "news",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                ArticleId = table.Column<Guid>(type: "uuid", nullable: false),
                RevisionId = table.Column<Guid>(type: "uuid", nullable: false),
                ActorId = table.Column<Guid>(type: "uuid", nullable: false),
                Action = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_History", x => x.Id);
                table.ForeignKey(
                    name: "FK_History_Articles_ArticleId",
                    column: x => x.ArticleId,
                    principalSchema: "news",
                    principalTable: "Articles",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_History_Revisions_RevisionId",
                    column: x => x.RevisionId,
                    principalSchema: "news",
                    principalTable: "Revisions",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_Articles_AuthorId_UpdatedAtUtc",
            schema: "news",
            table: "Articles",
            columns: new[] { "AuthorId", "UpdatedAtUtc" });

        migrationBuilder.CreateIndex(
            name: "IX_History_ArticleId_CreatedAtUtc",
            schema: "news",
            table: "History",
            columns: new[] { "ArticleId", "CreatedAtUtc" });

        migrationBuilder.CreateIndex(
            name: "IX_History_RevisionId",
            schema: "news",
            table: "History",
            column: "RevisionId");

        migrationBuilder.CreateIndex(
            name: "IX_Revisions_ArticleId",
            schema: "news",
            table: "Revisions",
            column: "ArticleId");

        migrationBuilder.CreateIndex(
            name: "IX_Taxonomy_Kind_NormalizedName",
            schema: "news",
            table: "Taxonomy",
            columns: new[] { "Kind", "NormalizedName" },
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "History",
            schema: "news");

        migrationBuilder.DropTable(
            name: "Taxonomy",
            schema: "news");

        migrationBuilder.DropTable(
            name: "Revisions",
            schema: "news");

        migrationBuilder.DropTable(
            name: "Articles",
            schema: "news");
    }
}
