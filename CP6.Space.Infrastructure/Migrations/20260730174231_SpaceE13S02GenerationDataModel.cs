using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CP6.Space.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SpaceE13S02GenerationDataModel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Space_GenerationRun",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SiteId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ModelVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SourceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SourceHash = table.Column<string>(type: "char(64)", unicode: false, fixedLength: true, maxLength: 64, nullable: false),
                    BaseContentRevision = table.Column<long>(type: "bigint", nullable: false),
                    Status = table.Column<short>(type: "smallint", nullable: false),
                    Progress = table.Column<int>(type: "int", nullable: false),
                    IdempotencyKeyHash = table.Column<string>(type: "char(64)", unicode: false, fixedLength: true, maxLength: 64, nullable: false),
                    BusinessKeyHash = table.Column<string>(type: "char(64)", unicode: false, fixedLength: true, maxLength: 64, nullable: false),
                    BasedOnRunId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    IsCurrent = table.Column<bool>(type: "bit", nullable: false),
                    MappingProfileVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RackGenerationProfileVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RuleVersion = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    PolicySnapshot = table.Column<short>(type: "smallint", nullable: false),
                    ProviderConfigVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ProviderCode = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    ProviderModel = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    InputSchemaVersion = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    OutputSchemaVersion = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    JobId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FailureCode = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    FailureSummary = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: true),
                    DegradedReason = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    CancelRequestedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CancelPending = table.Column<bool>(type: "bit", nullable: false),
                    CancelledAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ReviewCompletedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    AppliedContentRevision = table.Column<long>(type: "bigint", nullable: true),
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
                    table.PrimaryKey("PK_Space_GenerationRun", x => x.Id);
                    table.UniqueConstraint("AK_Space_GenerationRun_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.CheckConstraint("CK_Space_GenerationRun_Progress", "[Progress] >= 0 AND [Progress] <= 100");
                    table.ForeignKey(
                        name: "FK_Space_GenerationRun_BasedOn_Tenant",
                        columns: x => new { x.TenantId, x.BasedOnRunId },
                        principalTable: "Space_GenerationRun",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Space_GenerationRun_Job_Tenant",
                        columns: x => new { x.TenantId, x.JobId },
                        principalTable: "Space_Job",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Space_GenerationRun_Source_Tenant_Version",
                        columns: x => new { x.TenantId, x.ModelVersionId, x.SourceId },
                        principalTable: "Space_ModelSource",
                        principalColumns: new[] { "TenantId", "ModelVersionId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Space_GenerationRun_Version_Tenant",
                        columns: x => new { x.TenantId, x.ModelVersionId },
                        principalTable: "Space_ModelVersion",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Space_AiUsageRecord",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RunId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProviderCode = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ProviderModel = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    ProviderRequestIdHash = table.Column<string>(type: "char(64)", unicode: false, fixedLength: true, maxLength: 64, nullable: false),
                    InputUnits = table.Column<long>(type: "bigint", nullable: false),
                    OutputUnits = table.Column<long>(type: "bigint", nullable: false),
                    EstimatedCostMinor = table.Column<long>(type: "bigint", nullable: false),
                    ActualCostMinor = table.Column<long>(type: "bigint", nullable: true),
                    Currency = table.Column<string>(type: "char(3)", unicode: false, fixedLength: true, maxLength: 3, nullable: true),
                    LatencyMs = table.Column<long>(type: "bigint", nullable: false),
                    Outcome = table.Column<short>(type: "smallint", nullable: false),
                    RecordedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
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
                    table.PrimaryKey("PK_Space_AiUsageRecord", x => x.Id);
                    table.UniqueConstraint("AK_Space_AiUsageRecord_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.CheckConstraint("CK_Space_AiUsageRecord_Cost", "[EstimatedCostMinor] >= 0 AND ([ActualCostMinor] IS NULL OR [ActualCostMinor] >= 0)");
                    table.CheckConstraint("CK_Space_AiUsageRecord_Latency", "[LatencyMs] >= 0");
                    table.CheckConstraint("CK_Space_AiUsageRecord_Units", "[InputUnits] >= 0 AND [OutputUnits] >= 0");
                    table.ForeignKey(
                        name: "FK_Space_AiUsageRecord_Run_Tenant",
                        columns: x => new { x.TenantId, x.RunId },
                        principalTable: "Space_GenerationRun",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Space_GenerationProposal",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RunId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ModelVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BaseContentRevision = table.Column<long>(type: "bigint", nullable: false),
                    SourceHash = table.Column<string>(type: "char(64)", unicode: false, fixedLength: true, maxLength: 64, nullable: false),
                    SourceKey = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    ProposalType = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    SuggestedGeometryJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SuggestedAttributesJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SuggestedRelationsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SourceRefsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    EvidenceJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    FieldProvenanceJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ConfidenceScore = table.Column<decimal>(type: "decimal(6,5)", nullable: false),
                    ConfidenceBand = table.Column<short>(type: "smallint", nullable: false),
                    Status = table.Column<short>(type: "smallint", nullable: false),
                    HasBlockingIssue = table.Column<bool>(type: "bit", nullable: false),
                    HumanPatchJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    LockedFieldsJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    AppliedLogicalId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
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
                    table.PrimaryKey("PK_Space_GenerationProposal", x => x.Id);
                    table.UniqueConstraint("AK_Space_GenerationProposal_Tenant_Run_Id", x => new { x.TenantId, x.RunId, x.Id });
                    table.UniqueConstraint("AK_Space_GenerationProposal_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.CheckConstraint("CK_Space_GenerationProposal_Confidence", "[ConfidenceScore] >= 0 AND [ConfidenceScore] <= 1");
                    table.ForeignKey(
                        name: "FK_Space_GenerationProposal_Run_Tenant",
                        columns: x => new { x.TenantId, x.RunId },
                        principalTable: "Space_GenerationRun",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Space_GenerationProposal_Version_Tenant",
                        columns: x => new { x.TenantId, x.ModelVersionId },
                        principalTable: "Space_ModelVersion",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Space_ProposalDecision",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RunId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProposalId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DecisionType = table.Column<short>(type: "smallint", nullable: false),
                    BeforeJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    AfterJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    LockedFieldsJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ReasonCode = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    Comment = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                    DecisionBatchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
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
                    table.PrimaryKey("PK_Space_ProposalDecision", x => x.Id);
                    table.UniqueConstraint("AK_Space_ProposalDecision_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_Space_ProposalDecision_Proposal_Tenant_Run",
                        columns: x => new { x.TenantId, x.RunId, x.ProposalId },
                        principalTable: "Space_GenerationProposal",
                        principalColumns: new[] { "TenantId", "RunId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Space_ProposalDecision_Run_Tenant",
                        columns: x => new { x.TenantId, x.RunId },
                        principalTable: "Space_GenerationRun",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AiUsage_Tenant_Run_Recorded",
                table: "Space_AiUsageRecord",
                columns: new[] { "TenantId", "RunId", "RecordedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "UX_AiUsage_Tenant_ProviderRequest",
                table: "Space_AiUsageRecord",
                columns: new[] { "TenantId", "ProviderRequestIdHash" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_Proposal_Tenant_Run_Status_Band_Type",
                table: "Space_GenerationProposal",
                columns: new[] { "TenantId", "RunId", "Status", "ConfidenceBand", "ProposalType", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_Space_GenerationProposal_TenantId_ModelVersionId",
                table: "Space_GenerationProposal",
                columns: new[] { "TenantId", "ModelVersionId" });

            migrationBuilder.CreateIndex(
                name: "UX_Proposal_Tenant_Run_Source_Type",
                table: "Space_GenerationProposal",
                columns: new[] { "TenantId", "RunId", "SourceKey", "ProposalType" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_GenerationRun_Tenant_Job",
                table: "Space_GenerationRun",
                columns: new[] { "TenantId", "JobId" });

            migrationBuilder.CreateIndex(
                name: "IX_GenerationRun_Tenant_Site_Status_Created",
                table: "Space_GenerationRun",
                columns: new[] { "TenantId", "SiteId", "Status", "CreatedAtUtc" },
                descending: new[] { false, false, false, true });

            migrationBuilder.CreateIndex(
                name: "IX_GenerationRun_Tenant_Version_Current",
                table: "Space_GenerationRun",
                columns: new[] { "TenantId", "ModelVersionId", "IsCurrent" });

            migrationBuilder.CreateIndex(
                name: "IX_Space_GenerationRun_TenantId_BasedOnRunId",
                table: "Space_GenerationRun",
                columns: new[] { "TenantId", "BasedOnRunId" });

            migrationBuilder.CreateIndex(
                name: "IX_Space_GenerationRun_TenantId_ModelVersionId_SourceId",
                table: "Space_GenerationRun",
                columns: new[] { "TenantId", "ModelVersionId", "SourceId" });

            migrationBuilder.CreateIndex(
                name: "UX_GenerationRun_Tenant_Business_Current",
                table: "Space_GenerationRun",
                columns: new[] { "TenantId", "BusinessKeyHash" },
                unique: true,
                filter: "[IsCurrent] = 1 AND [IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_ProposalDecision_Tenant_Batch",
                table: "Space_ProposalDecision",
                columns: new[] { "TenantId", "DecisionBatchId", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_ProposalDecision_Tenant_Run_Proposal_Created",
                table: "Space_ProposalDecision",
                columns: new[] { "TenantId", "RunId", "ProposalId", "CreatedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Space_AiUsageRecord");

            migrationBuilder.DropTable(
                name: "Space_ProposalDecision");

            migrationBuilder.DropTable(
                name: "Space_GenerationProposal");

            migrationBuilder.DropTable(
                name: "Space_GenerationRun");
        }
    }
}
