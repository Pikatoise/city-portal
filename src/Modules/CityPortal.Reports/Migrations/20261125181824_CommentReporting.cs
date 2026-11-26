using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CityPortal.Reports.Migrations;

/// <inheritdoc />
public partial class CommentReporting : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AlterColumn<Guid>(
            name: "OwnerId",
            schema: "reports",
            table: "Targets",
            type: "uuid",
            nullable: true,
            oldClrType: typeof(Guid),
            oldType: "uuid");

        migrationBuilder.AddColumn<Guid>(
            name: "DirectDecisionId",
            schema: "reports",
            table: "Targets",
            type: "uuid",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "SnapshotText",
            schema: "reports",
            table: "Targets",
            type: "character varying(500)",
            maxLength: 500,
            nullable: true);

        migrationBuilder.CreateIndex(
            name: "IX_Targets_DirectDecisionId",
            schema: "reports",
            table: "Targets",
            column: "DirectDecisionId");

        migrationBuilder.AddForeignKey(
            name: "FK_Targets_Decisions_DirectDecisionId",
            schema: "reports",
            table: "Targets",
            column: "DirectDecisionId",
            principalSchema: "reports",
            principalTable: "Decisions",
            principalColumn: "Id",
            onDelete: ReferentialAction.Restrict);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey(
            name: "FK_Targets_Decisions_DirectDecisionId",
            schema: "reports",
            table: "Targets");

        migrationBuilder.DropIndex(
            name: "IX_Targets_DirectDecisionId",
            schema: "reports",
            table: "Targets");

        migrationBuilder.DropColumn(
            name: "DirectDecisionId",
            schema: "reports",
            table: "Targets");

        migrationBuilder.DropColumn(
            name: "SnapshotText",
            schema: "reports",
            table: "Targets");

        migrationBuilder.AlterColumn<Guid>(
            name: "OwnerId",
            schema: "reports",
            table: "Targets",
            type: "uuid",
            nullable: false,
            defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
            oldClrType: typeof(Guid),
            oldType: "uuid",
            oldNullable: true);
    }
}
