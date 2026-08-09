using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CP6.Space.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SpaceE06S03PublishOrchestration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Space_PublishPlan",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SiteId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TargetVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BaseVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ValidationRunId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ContentHash = table.Column<string>(type: "char(64)", unicode: false, fixedLength: true, maxLength: 64, nullable: false),
                    AdapterId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    CapabilityHash = table.Column<string>(type: "char(64)", unicode: false, fixedLength: true, maxLength: 64, nullable: false),
                    PlanHash = table.Column<string>(type: "char(64)", unicode: false, fixedLength: true, maxLength: 64, nullable: false),
                    ItemCount = table.Column<int>(type: "int", nullable: false),
                    PlanJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ModifiedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Space_PublishPlan", x => x.Id);
                    table.UniqueConstraint("AK_Space_PublishPlan_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_Space_PublishPlan_TargetVersion_Tenant",
                        columns: x => new { x.TenantId, x.TargetVersionId },
                        principalTable: "Space_ModelVersion",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Space_PublishPlan_ValidationRun_Tenant",
                        columns: x => new { x.TenantId, x.ValidationRunId },
                        principalTable: "Space_ValidationRun",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Space_RuntimeElement",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SiteId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ModelVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LogicalId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FloorLogicalId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    PayloadJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    PayloadHash = table.Column<string>(type: "char(64)", unicode: false, fixedLength: true, maxLength: 64, nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ModifiedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Space_RuntimeElement", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Space_PublishAttempt",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SiteId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PublishPlanId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TargetVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BaseVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    AdapterId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Status = table.Column<short>(type: "smallint", nullable: false),
                    CurrentStep = table.Column<short>(type: "smallint", nullable: false),
                    BusinessIdempotencyKey = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    RequestHash = table.Column<string>(type: "char(64)", unicode: false, fixedLength: true, maxLength: 64, nullable: false),
                    OwnsPublishSlot = table.Column<bool>(type: "bit", nullable: false),
                    StartedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    FinishedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RequestedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ApprovedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ApprovalReference = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    WmsCommittedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RuntimeActivatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastErrorCode = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Summary = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    CorrelationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
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
                    table.PrimaryKey("PK_Space_PublishAttempt", x => x.Id);
                    table.UniqueConstraint("AK_Space_PublishAttempt_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.CheckConstraint("CK_Space_PublishAttempt_Slot", "([OwnsPublishSlot] = 1 AND [FinishedAtUtc] IS NULL) OR ([OwnsPublishSlot] = 0)");
                    table.ForeignKey(
                        name: "FK_Space_PublishAttempt_Plan_Tenant",
                        columns: x => new { x.TenantId, x.PublishPlanId },
                        principalTable: "Space_PublishPlan",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Space_PublishBatch",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AttemptId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BatchNo = table.Column<int>(type: "int", nullable: false),
                    OperationKey = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    PayloadHash = table.Column<string>(type: "char(64)", unicode: false, fixedLength: true, maxLength: 64, nullable: false),
                    Status = table.Column<short>(type: "smallint", nullable: false),
                    AttemptCount = table.Column<int>(type: "int", nullable: false),
                    ExternalOperationId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ResultJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ObservedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ModifiedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Space_PublishBatch", x => x.Id);
                    table.UniqueConstraint("AK_Space_PublishBatch_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_Space_PublishBatch_Attempt_Tenant",
                        columns: x => new { x.TenantId, x.AttemptId },
                        principalTable: "Space_PublishAttempt",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Space_ReconciliationIssue",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AttemptId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LogicalId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ExpectedStateHash = table.Column<string>(type: "char(64)", unicode: false, fixedLength: true, maxLength: 64, nullable: true),
                    WmsStateHash = table.Column<string>(type: "char(64)", unicode: false, fixedLength: true, maxLength: 64, nullable: true),
                    RuntimeStateHash = table.Column<string>(type: "char(64)", unicode: false, fixedLength: true, maxLength: 64, nullable: true),
                    Classification = table.Column<short>(type: "smallint", nullable: false),
                    Status = table.Column<short>(type: "smallint", nullable: false),
                    Summary = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    Resolution = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ModifiedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Space_ReconciliationIssue", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Space_ReconciliationIssue_Attempt_Tenant",
                        columns: x => new { x.TenantId, x.AttemptId },
                        principalTable: "Space_PublishAttempt",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Space_WmsReceipt",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BatchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LogicalId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LocationCode = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    Action = table.Column<short>(type: "smallint", nullable: false),
                    Outcome = table.Column<short>(type: "smallint", nullable: false),
                    ExternalLocationId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ExternalVersion = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ResponseHash = table.Column<string>(type: "char(64)", unicode: false, fixedLength: true, maxLength: 64, nullable: true),
                    ErrorCode = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ReceivedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ModifiedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Space_WmsReceipt", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Space_WmsReceipt_Batch_Tenant",
                        columns: x => new { x.TenantId, x.BatchId },
                        principalTable: "Space_PublishBatch",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Space_PublishAttempt_Tenant_Site_Started",
                table: "Space_PublishAttempt",
                columns: new[] { "TenantId", "SiteId", "StartedAtUtc", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_Space_PublishAttempt_TenantId_PublishPlanId",
                table: "Space_PublishAttempt",
                columns: new[] { "TenantId", "PublishPlanId" });

            migrationBuilder.CreateIndex(
                name: "UX_Space_PublishAttempt_Tenant_Idempotency",
                table: "Space_PublishAttempt",
                columns: new[] { "TenantId", "BusinessIdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_Space_PublishAttempt_Tenant_Site_Active",
                table: "Space_PublishAttempt",
                columns: new[] { "TenantId", "SiteId" },
                unique: true,
                filter: "[OwnsPublishSlot] = 1 AND [IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "UX_Space_PublishBatch_Tenant_Attempt_BatchNo",
                table: "Space_PublishBatch",
                columns: new[] { "TenantId", "AttemptId", "BatchNo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_Space_PublishBatch_Tenant_OperationKey",
                table: "Space_PublishBatch",
                columns: new[] { "TenantId", "OperationKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Space_PublishPlan_Tenant_Site_Target_Created",
                table: "Space_PublishPlan",
                columns: new[] { "TenantId", "SiteId", "TargetVersionId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Space_PublishPlan_TenantId_TargetVersionId",
                table: "Space_PublishPlan",
                columns: new[] { "TenantId", "TargetVersionId" });

            migrationBuilder.CreateIndex(
                name: "IX_Space_PublishPlan_TenantId_ValidationRunId",
                table: "Space_PublishPlan",
                columns: new[] { "TenantId", "ValidationRunId" });

            migrationBuilder.CreateIndex(
                name: "UX_Space_PublishPlan_Tenant_PlanHash",
                table: "Space_PublishPlan",
                columns: new[] { "TenantId", "PlanHash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Space_ReconciliationIssue_Tenant_Attempt_Status",
                table: "Space_ReconciliationIssue",
                columns: new[] { "TenantId", "AttemptId", "Status", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_Space_RuntimeElement_Tenant_Site_Version_Active",
                table: "Space_RuntimeElement",
                columns: new[] { "TenantId", "SiteId", "ModelVersionId", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "UX_Space_RuntimeElement_Tenant_Site_LogicalId",
                table: "Space_RuntimeElement",
                columns: new[] { "TenantId", "SiteId", "LogicalId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_Space_WmsReceipt_Tenant_Batch_LogicalId",
                table: "Space_WmsReceipt",
                columns: new[] { "TenantId", "BatchId", "LogicalId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                ;THROW 51019,
                    'E06-S03 is forward-only. Apply a reviewed forward-fix migration; do not remove publish or reconciliation evidence.',
                    1;
                """);
        }
    }
}
