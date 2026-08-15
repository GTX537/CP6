using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CP6.Space.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SpaceCadManualCorrectionLock : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsManualCorrectionLocked",
                table: "Space_ElementRevision",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "ManualCorrectionUpdatedAtUtc",
                table: "Space_ElementRevision",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ManualCorrectionUpdatedBy",
                table: "Space_ElementRevision",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "UserCorrectionVersion",
                table: "Space_ElementRevision",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddCheckConstraint(
                name: "CK_Space_ElementRevision_ManualCorrection",
                table: "Space_ElementRevision",
                sql: "[UserCorrectionVersion] >= 0 AND ([IsManualCorrectionLocked] = 0 OR ([SourceId] IS NOT NULL AND [SourceRef] IS NOT NULL AND [UserCorrectionVersion] > 0 AND [ManualCorrectionUpdatedBy] IS NOT NULL AND [ManualCorrectionUpdatedAtUtc] IS NOT NULL))");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Space_ElementRevision_ManualCorrection",
                table: "Space_ElementRevision");

            migrationBuilder.DropColumn(
                name: "IsManualCorrectionLocked",
                table: "Space_ElementRevision");

            migrationBuilder.DropColumn(
                name: "ManualCorrectionUpdatedAtUtc",
                table: "Space_ElementRevision");

            migrationBuilder.DropColumn(
                name: "ManualCorrectionUpdatedBy",
                table: "Space_ElementRevision");

            migrationBuilder.DropColumn(
                name: "UserCorrectionVersion",
                table: "Space_ElementRevision");
        }
    }
}
