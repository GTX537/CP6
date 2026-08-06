using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CP6.Space.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SpaceE13S09ProposalDecisions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "GenerationProposalId",
                table: "Space_ModelIssue",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "GenerationRunId",
                table: "Space_ModelIssue",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ResolutionDecisionId",
                table: "Space_ModelIssue",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<short>(
                name: "ResolutionKind",
                table: "Space_ModelIssue",
                type: "smallint",
                nullable: false,
                defaultValue: (short)0);

            migrationBuilder.Sql(
                "UPDATE [Space_ModelIssue] SET [ResolutionKind] = 1 " +
                "WHERE [Status] = 1 AND [ResolutionCommandBatchId] IS NOT NULL;");

            migrationBuilder.CreateTable(
                name: "Space_GenerationLockedFact",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RunId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BasedOnRunId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SourceProposalId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SourceDecisionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SourceHash = table.Column<string>(type: "char(64)", unicode: false, fixedLength: true, maxLength: 64, nullable: false),
                    SourceKey = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    ProposalType = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    FieldPath = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    ValueJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    MatchMethod = table.Column<short>(type: "smallint", nullable: false),
                    MatchScore = table.Column<decimal>(type: "decimal(6,5)", nullable: false),
                    IsConfirmed = table.Column<bool>(type: "bit", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ModifiedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Space_GenerationLockedFact", x => x.Id);
                    table.UniqueConstraint("AK_Space_GenerationLockedFact_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.CheckConstraint("CK_Space_GenerationLockedFact_Match", "[MatchScore] >= 0 AND [MatchScore] <= 1 AND [RunId] <> [BasedOnRunId]");
                    table.ForeignKey(
                        name: "FK_Space_GenerationLockedFact_BasedOnRun_Tenant",
                        columns: x => new { x.TenantId, x.BasedOnRunId },
                        principalTable: "Space_GenerationRun",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Space_GenerationLockedFact_Decision_Tenant",
                        columns: x => new { x.TenantId, x.SourceDecisionId },
                        principalTable: "Space_ProposalDecision",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Space_GenerationLockedFact_Proposal_Tenant_Run",
                        columns: x => new { x.TenantId, x.BasedOnRunId, x.SourceProposalId },
                        principalTable: "Space_GenerationProposal",
                        principalColumns: new[] { "TenantId", "RunId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Space_GenerationLockedFact_Run_Tenant",
                        columns: x => new { x.TenantId, x.RunId },
                        principalTable: "Space_GenerationRun",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Space_ModelIssue_Tenant_Run_Proposal_Status",
                table: "Space_ModelIssue",
                columns: new[] { "TenantId", "GenerationRunId", "GenerationProposalId", "Status", "Severity" },
                filter: "[GenerationRunId] IS NOT NULL AND [IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_Space_ModelIssue_TenantId_ResolutionDecisionId",
                table: "Space_ModelIssue",
                columns: new[] { "TenantId", "ResolutionDecisionId" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_Space_ModelIssue_GenerationScope",
                table: "Space_ModelIssue",
                sql: "([GenerationProposalId] IS NULL OR [GenerationRunId] IS NOT NULL) AND ([ResolutionDecisionId] IS NULL OR [GenerationProposalId] IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Space_ModelIssue_Resolution",
                table: "Space_ModelIssue",
                sql: "([Status] <> 1 AND [ResolutionKind] = 0 AND [ResolutionCommandBatchId] IS NULL AND [ResolutionDecisionId] IS NULL) OR ([Status] = 1 AND (([ResolutionKind] = 1 AND [ResolutionCommandBatchId] IS NOT NULL AND [ResolutionDecisionId] IS NULL) OR ([ResolutionKind] IN (2, 3) AND [ResolutionCommandBatchId] IS NULL AND [ResolutionDecisionId] IS NOT NULL)))");

            migrationBuilder.CreateIndex(
                name: "IX_GenerationLockedFact_Tenant_Decision_Run",
                table: "Space_GenerationLockedFact",
                columns: new[] { "TenantId", "SourceDecisionId", "RunId" });

            migrationBuilder.CreateIndex(
                name: "IX_Space_GenerationLockedFact_TenantId_BasedOnRunId_SourceProposalId",
                table: "Space_GenerationLockedFact",
                columns: new[] { "TenantId", "BasedOnRunId", "SourceProposalId" });

            migrationBuilder.CreateIndex(
                name: "UX_GenerationLockedFact_Tenant_Run_Source_Type_Field",
                table: "Space_GenerationLockedFact",
                columns: new[] { "TenantId", "RunId", "SourceKey", "ProposalType", "FieldPath" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.AddForeignKey(
                name: "FK_Space_ModelIssue_GenerationRun_Tenant",
                table: "Space_ModelIssue",
                columns: new[] { "TenantId", "GenerationRunId" },
                principalTable: "Space_GenerationRun",
                principalColumns: new[] { "TenantId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Space_ModelIssue_Proposal_Tenant_Run",
                table: "Space_ModelIssue",
                columns: new[] { "TenantId", "GenerationRunId", "GenerationProposalId" },
                principalTable: "Space_GenerationProposal",
                principalColumns: new[] { "TenantId", "RunId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Space_ModelIssue_ResolutionDecision_Tenant",
                table: "Space_ModelIssue",
                columns: new[] { "TenantId", "ResolutionDecisionId" },
                principalTable: "Space_ProposalDecision",
                principalColumns: new[] { "TenantId", "Id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Space_ModelIssue_GenerationRun_Tenant",
                table: "Space_ModelIssue");

            migrationBuilder.DropForeignKey(
                name: "FK_Space_ModelIssue_Proposal_Tenant_Run",
                table: "Space_ModelIssue");

            migrationBuilder.DropForeignKey(
                name: "FK_Space_ModelIssue_ResolutionDecision_Tenant",
                table: "Space_ModelIssue");

            migrationBuilder.DropTable(
                name: "Space_GenerationLockedFact");

            migrationBuilder.DropIndex(
                name: "IX_Space_ModelIssue_Tenant_Run_Proposal_Status",
                table: "Space_ModelIssue");

            migrationBuilder.DropIndex(
                name: "IX_Space_ModelIssue_TenantId_ResolutionDecisionId",
                table: "Space_ModelIssue");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Space_ModelIssue_GenerationScope",
                table: "Space_ModelIssue");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Space_ModelIssue_Resolution",
                table: "Space_ModelIssue");

            migrationBuilder.DropColumn(
                name: "GenerationProposalId",
                table: "Space_ModelIssue");

            migrationBuilder.DropColumn(
                name: "GenerationRunId",
                table: "Space_ModelIssue");

            migrationBuilder.DropColumn(
                name: "ResolutionDecisionId",
                table: "Space_ModelIssue");

            migrationBuilder.DropColumn(
                name: "ResolutionKind",
                table: "Space_ModelIssue");
        }
    }
}
