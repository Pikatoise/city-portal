using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CityPortal.Media.Migrations;
/// <inheritdoc />
public partial class InitialMedia : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.EnsureSchema(
            name: "media");

        migrationBuilder.CreateTable(
            name: "Images",
            schema: "media",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                OwnerId = table.Column<Guid>(type: "uuid", nullable: false),
                Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                State = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                Width = table.Column<int>(type: "integer", nullable: false),
                Height = table.Column<int>(type: "integer", nullable: false),
                Version = table.Column<long>(type: "bigint", nullable: false),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                NextAttemptAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                Attempts = table.Column<int>(type: "integer", nullable: false),
                EverAttached = table.Column<bool>(type: "boolean", nullable: false),
                ErrorCode = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Images", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "Outputs",
            schema: "media",
            columns: table => new
            {
                ImageId = table.Column<Guid>(type: "uuid", nullable: false),
                Variant = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                Width = table.Column<int>(type: "integer", nullable: false),
                Height = table.Column<int>(type: "integer", nullable: false),
                PendingBytes = table.Column<byte[]>(type: "bytea", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Outputs", x => new { x.ImageId, x.Variant });
                table.ForeignKey(
                    name: "FK_Outputs_Images_ImageId",
                    column: x => x.ImageId,
                    principalSchema: "media",
                    principalTable: "Images",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "References",
            schema: "media",
            columns: table => new
            {
                RevisionId = table.Column<Guid>(type: "uuid", nullable: false),
                ImageId = table.Column<Guid>(type: "uuid", nullable: false),
                AuthorId = table.Column<Guid>(type: "uuid", nullable: false),
                ReviewerId = table.Column<Guid>(type: "uuid", nullable: true),
                Submitted = table.Column<bool>(type: "boolean", nullable: false),
                Public = table.Column<bool>(type: "boolean", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_References", x => new { x.RevisionId, x.ImageId });
                table.ForeignKey(
                    name: "FK_References_Images_ImageId",
                    column: x => x.ImageId,
                    principalSchema: "media",
                    principalTable: "Images",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "Templates",
            schema: "media",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                OwnerId = table.Column<Guid>(type: "uuid", nullable: false),
                ImageId = table.Column<Guid>(type: "uuid", nullable: false),
                Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                Approval = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                Submitted = table.Column<bool>(type: "boolean", nullable: false),
                Archived = table.Column<bool>(type: "boolean", nullable: false),
                Blocked = table.Column<bool>(type: "boolean", nullable: false),
                Reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                Version = table.Column<long>(type: "bigint", nullable: false),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Templates", x => x.Id);
                table.ForeignKey(
                    name: "FK_Templates_Images_ImageId",
                    column: x => x.ImageId,
                    principalSchema: "media",
                    principalTable: "Images",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "Events",
            schema: "media",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                TemplateId = table.Column<Guid>(type: "uuid", nullable: false),
                ImageId = table.Column<Guid>(type: "uuid", nullable: false),
                OwnerId = table.Column<Guid>(type: "uuid", nullable: false),
                ActorId = table.Column<Guid>(type: "uuid", nullable: false),
                Action = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                Reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                NotificationDispatchedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Events", x => x.Id);
                table.ForeignKey(
                    name: "FK_Events_Images_ImageId",
                    column: x => x.ImageId,
                    principalSchema: "media",
                    principalTable: "Images",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_Events_Templates_TemplateId",
                    column: x => x.TemplateId,
                    principalSchema: "media",
                    principalTable: "Templates",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "RemovalRequests",
            schema: "media",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                TemplateId = table.Column<Guid>(type: "uuid", nullable: false),
                OwnerId = table.Column<Guid>(type: "uuid", nullable: false),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                ResolvedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                ResolvedBy = table.Column<Guid>(type: "uuid", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_RemovalRequests", x => x.Id);
                table.ForeignKey(
                    name: "FK_RemovalRequests_Templates_TemplateId",
                    column: x => x.TemplateId,
                    principalSchema: "media",
                    principalTable: "Templates",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_Events_ImageId",
            schema: "media",
            table: "Events",
            column: "ImageId");

        migrationBuilder.CreateIndex(
            name: "IX_Events_TemplateId_CreatedAtUtc",
            schema: "media",
            table: "Events",
            columns: new[] { "TemplateId", "CreatedAtUtc" });

        migrationBuilder.CreateIndex(
            name: "IX_Images_OwnerId_CreatedAtUtc",
            schema: "media",
            table: "Images",
            columns: new[] { "OwnerId", "CreatedAtUtc" });

        migrationBuilder.CreateIndex(
            name: "IX_Images_State_NextAttemptAtUtc",
            schema: "media",
            table: "Images",
            columns: new[] { "State", "NextAttemptAtUtc" });

        migrationBuilder.CreateIndex(
            name: "IX_References_ImageId_Public",
            schema: "media",
            table: "References",
            columns: new[] { "ImageId", "Public" });

        migrationBuilder.CreateIndex(
            name: "IX_RemovalRequests_TemplateId",
            schema: "media",
            table: "RemovalRequests",
            column: "TemplateId",
            unique: true,
            filter: "\"ResolvedAtUtc\" IS NULL");

        migrationBuilder.CreateIndex(
            name: "IX_Templates_Approval_Submitted_CreatedAtUtc",
            schema: "media",
            table: "Templates",
            columns: new[] { "Approval", "Submitted", "CreatedAtUtc" });

        migrationBuilder.CreateIndex(
            name: "IX_Templates_ImageId",
            schema: "media",
            table: "Templates",
            column: "ImageId");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "Events",
            schema: "media");

        migrationBuilder.DropTable(
            name: "Outputs",
            schema: "media");

        migrationBuilder.DropTable(
            name: "References",
            schema: "media");

        migrationBuilder.DropTable(
            name: "RemovalRequests",
            schema: "media");

        migrationBuilder.DropTable(
            name: "Templates",
            schema: "media");

        migrationBuilder.DropTable(
            name: "Images",
            schema: "media");
    }
}
