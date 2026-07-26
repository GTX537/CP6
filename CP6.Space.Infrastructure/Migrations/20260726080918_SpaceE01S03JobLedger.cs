using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CP6.Space.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SpaceE01S03JobLedger : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "JobId",
                table: "Space_Artifact",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddUniqueConstraint(
                name: "AK_Space_Artifact_TenantId_Id",
                table: "Space_Artifact",
                columns: new[] { "TenantId", "Id" });

            migrationBuilder.CreateTable(
                name: "Space_Job",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    JobType = table.Column<short>(type: "smallint", nullable: false),
                    SubjectType = table.Column<short>(type: "smallint", nullable: false),
                    SubjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BusinessKey = table.Column<string>(type: "char(64)", unicode: false, fixedLength: true, maxLength: 64, nullable: false),
                    InputHash = table.Column<string>(type: "char(64)", unicode: false, fixedLength: true, maxLength: 64, nullable: false),
                    Status = table.Column<short>(type: "smallint", nullable: false),
                    Priority = table.Column<short>(type: "smallint", nullable: false),
                    AttemptCount = table.Column<int>(type: "int", nullable: false),
                    MaxAttempts = table.Column<int>(type: "int", nullable: false),
                    NextAttemptAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LockedBy = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    LockedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LockExpiresAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ActiveAttemptId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    LeaseRevision = table.Column<long>(type: "bigint", nullable: false),
                    ProgressDone = table.Column<long>(type: "bigint", nullable: false),
                    ProgressTotal = table.Column<long>(type: "bigint", nullable: false),
                    ProgressStage = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    RequestedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RequestedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    StartedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    FinishedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CorrelationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PayloadJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ResultSummaryJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    LastFailureKind = table.Column<short>(type: "smallint", nullable: true),
                    LastErrorCode = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    LastErrorSummary = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    RetryOfJobId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CancellationRequestedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CancellationRequestedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
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
                    table.PrimaryKey("PK_Space_Job", x => x.Id);
                    table.UniqueConstraint("AK_Space_Job_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.CheckConstraint("CK_Space_Job_Attempts", "[AttemptCount] >= 0 AND [MaxAttempts] BETWEEN 1 AND 20 AND [AttemptCount] <= [MaxAttempts]");
                    table.CheckConstraint("CK_Space_Job_Lease", "([Status] = 1 AND [LockedBy] IS NOT NULL AND [LockedAtUtc] IS NOT NULL AND [LockExpiresAtUtc] IS NOT NULL AND [ActiveAttemptId] IS NOT NULL) OR ([Status] <> 1 AND [LockedBy] IS NULL AND [LockedAtUtc] IS NULL AND [LockExpiresAtUtc] IS NULL AND [ActiveAttemptId] IS NULL)");
                    table.CheckConstraint("CK_Space_Job_Progress", "[ProgressDone] >= 0 AND [ProgressTotal] >= 0 AND ([ProgressTotal] = 0 OR [ProgressDone] <= [ProgressTotal])");
                    table.ForeignKey(
                        name: "FK_Space_Job_RetryOf_Tenant",
                        columns: x => new { x.TenantId, x.RetryOfJobId },
                        principalTable: "Space_Job",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Space_JobAttempt",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    JobId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AttemptNo = table.Column<int>(type: "int", nullable: false),
                    WorkerId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    StartedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    FinishedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Outcome = table.Column<short>(type: "smallint", nullable: false),
                    InputHash = table.Column<string>(type: "char(64)", unicode: false, fixedLength: true, maxLength: 64, nullable: false),
                    ProcessorVersion = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ResourceUsageJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    FailureKind = table.Column<short>(type: "smallint", nullable: true),
                    ErrorCode = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    SanitizedError = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    DiagnosticArtifactId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ModifiedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Space_JobAttempt", x => x.Id);
                    table.UniqueConstraint("AK_Space_JobAttempt_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.CheckConstraint("CK_Space_JobAttempt_OutcomeTime", "([Outcome] = 0 AND [FinishedAtUtc] IS NULL) OR ([Outcome] <> 0 AND [FinishedAtUtc] IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_Space_JobAttempt_DiagnosticArtifact_Tenant",
                        columns: x => new { x.TenantId, x.DiagnosticArtifactId },
                        principalTable: "Space_Artifact",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Space_JobAttempt_Job_Tenant",
                        columns: x => new { x.TenantId, x.JobId },
                        principalTable: "Space_Job",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Space_ModelIssue",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ModelVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SourceId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    JobId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Severity = table.Column<short>(type: "smallint", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    SourceRef = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    TargetLogicalId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    MessageArgsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SuggestedActionCode = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Status = table.Column<short>(type: "smallint", nullable: false),
                    ResolutionCommandBatchId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    AcknowledgedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    AcknowledgedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    AcknowledgementReason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ModifiedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Space_ModelIssue", x => x.Id);
                    table.CheckConstraint("CK_Space_ModelIssue_Context", "[ModelVersionId] IS NOT NULL OR [SourceId] IS NOT NULL OR [JobId] IS NOT NULL");
                    table.CheckConstraint("CK_Space_ModelIssue_SourceVersion", "[SourceId] IS NULL OR [ModelVersionId] IS NOT NULL");
                    table.ForeignKey(
                        name: "FK_Space_ModelIssue_Job_Tenant",
                        columns: x => new { x.TenantId, x.JobId },
                        principalTable: "Space_Job",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Space_ModelIssue_Source_Tenant_Version",
                        columns: x => new { x.TenantId, x.ModelVersionId, x.SourceId },
                        principalTable: "Space_ModelSource",
                        principalColumns: new[] { "TenantId", "ModelVersionId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Space_ModelIssue_Version_Tenant",
                        columns: x => new { x.TenantId, x.ModelVersionId },
                        principalTable: "Space_ModelVersion",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Space_JobStep",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AttemptId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StepNo = table.Column<int>(type: "int", nullable: false),
                    StepCode = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Status = table.Column<short>(type: "smallint", nullable: false),
                    StartedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    FinishedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CheckpointJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    OutputHash = table.Column<string>(type: "char(64)", unicode: false, fixedLength: true, maxLength: 64, nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ModifiedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Space_JobStep", x => x.Id);
                    table.CheckConstraint("CK_Space_JobStep_StatusTime", "([Status] = 0 AND [FinishedAtUtc] IS NULL) OR ([Status] <> 0 AND [FinishedAtUtc] IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_Space_JobStep_Attempt_Tenant",
                        columns: x => new { x.TenantId, x.AttemptId },
                        principalTable: "Space_JobAttempt",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Space_Artifact_Tenant_Job_Active",
                table: "Space_Artifact",
                columns: new[] { "TenantId", "JobId" },
                filter: "[JobId] IS NOT NULL AND [IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_Space_Job_Tenant_Claim",
                table: "Space_Job",
                columns: new[] { "TenantId", "Status", "NextAttemptAtUtc", "LockExpiresAtUtc", "Priority", "RequestedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Space_Job_Tenant_Correlation",
                table: "Space_Job",
                columns: new[] { "TenantId", "CorrelationId" });

            migrationBuilder.CreateIndex(
                name: "IX_Space_Job_Tenant_Subject",
                table: "Space_Job",
                columns: new[] { "TenantId", "SubjectType", "SubjectId" });

            migrationBuilder.CreateIndex(
                name: "IX_Space_Job_TenantId_RetryOfJobId",
                table: "Space_Job",
                columns: new[] { "TenantId", "RetryOfJobId" });

            migrationBuilder.CreateIndex(
                name: "UX_Space_Job_Tenant_Type_BusinessKey_Active",
                table: "Space_Job",
                columns: new[] { "TenantId", "JobType", "BusinessKey" },
                unique: true,
                filter: "[Status] IN (0, 1) AND [IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_Space_JobAttempt_Tenant_Job_Started",
                table: "Space_JobAttempt",
                columns: new[] { "TenantId", "JobId", "StartedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Space_JobAttempt_TenantId_DiagnosticArtifactId",
                table: "Space_JobAttempt",
                columns: new[] { "TenantId", "DiagnosticArtifactId" });

            migrationBuilder.CreateIndex(
                name: "UX_Space_JobAttempt_Tenant_Job_AttemptNo",
                table: "Space_JobAttempt",
                columns: new[] { "TenantId", "JobId", "AttemptNo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_Space_JobStep_Tenant_Attempt_StepCode",
                table: "Space_JobStep",
                columns: new[] { "TenantId", "AttemptId", "StepCode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_Space_JobStep_Tenant_Attempt_StepNo",
                table: "Space_JobStep",
                columns: new[] { "TenantId", "AttemptId", "StepNo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Space_ModelIssue_Tenant_Job_Status",
                table: "Space_ModelIssue",
                columns: new[] { "TenantId", "JobId", "Status" },
                filter: "[JobId] IS NOT NULL AND [IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_Space_ModelIssue_Tenant_Version_Status",
                table: "Space_ModelIssue",
                columns: new[] { "TenantId", "ModelVersionId", "Status", "Severity", "Code" });

            migrationBuilder.CreateIndex(
                name: "IX_Space_ModelIssue_TenantId_ModelVersionId_SourceId",
                table: "Space_ModelIssue",
                columns: new[] { "TenantId", "ModelVersionId", "SourceId" });

            migrationBuilder.AddForeignKey(
                name: "FK_Space_Artifact_Job_Tenant",
                table: "Space_Artifact",
                columns: new[] { "TenantId", "JobId" },
                principalTable: "Space_Job",
                principalColumns: new[] { "TenantId", "Id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Space_Artifact_Job_Tenant",
                table: "Space_Artifact");

            migrationBuilder.DropTable(
                name: "Space_JobStep");

            migrationBuilder.DropTable(
                name: "Space_ModelIssue");

            migrationBuilder.DropTable(
                name: "Space_JobAttempt");

            migrationBuilder.DropTable(
                name: "Space_Job");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_Space_Artifact_TenantId_Id",
                table: "Space_Artifact");

            migrationBuilder.DropIndex(
                name: "IX_Space_Artifact_Tenant_Job_Active",
                table: "Space_Artifact");

            migrationBuilder.DropColumn(
                name: "JobId",
                table: "Space_Artifact");
        }
    }
}
