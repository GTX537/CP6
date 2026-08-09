using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CP6.Space.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SpaceE13S17AiRetention : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "PayloadPurgedAtUtc",
                table: "Space_ModelIssue",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "PayloadPurgedAtUtc",
                table: "Space_GenerationRun",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "RetentionHoldUntilUtc",
                table: "Space_GenerationRun",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "PayloadPurgedAtUtc",
                table: "Space_GenerationProposal",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ArchivedAtUtc",
                table: "Space_AiUsageRecord",
                type: "datetime2",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ModelIssue_Tenant_Purge_Run",
                table: "Space_ModelIssue",
                columns: new[] { "TenantId", "PayloadPurgedAtUtc", "GenerationRunId", "Id" },
                filter: "[GenerationRunId] IS NOT NULL AND [IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_GenerationRun_Tenant_Retention",
                table: "Space_GenerationRun",
                columns: new[] { "TenantId", "PayloadPurgedAtUtc", "IsCurrent", "Status", "CreatedAtUtc", "Id" },
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_Proposal_Tenant_Purge_Run",
                table: "Space_GenerationProposal",
                columns: new[] { "TenantId", "PayloadPurgedAtUtc", "RunId", "Id" },
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_AiUsage_Tenant_Retention",
                table: "Space_AiUsageRecord",
                columns: new[] { "TenantId", "ArchivedAtUtc", "RecordedAtUtc", "Id" },
                filter: "[IsDeleted] = 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                ;THROW 51017,
                    'E13-S17 is forward-only. Apply a reviewed forward-fix migration; do not remove AI retention or audit columns.',
                    1;
                """);
        }
    }
}
