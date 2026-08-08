using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CP6.Space.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SpaceE06S01ValidationEngine : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Category",
                table: "Space_ModelIssue",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EvidenceJson",
                table: "Space_ModelIssue",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "{}");

            migrationBuilder.AddColumn<string>(
                name: "FieldPath",
                table: "Space_ModelIssue",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ValidationRunId",
                table: "Space_ModelIssue",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Space_ValidationRun",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ModelVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ContentRevision = table.Column<long>(type: "bigint", nullable: false),
                    ContentHash = table.Column<string>(type: "char(64)", unicode: false, fixedLength: true, maxLength: 64, nullable: false),
                    RuleSetVersion = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    AdapterId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    CapabilityHash = table.Column<string>(type: "char(64)", unicode: false, fixedLength: true, maxLength: 64, nullable: false),
                    Status = table.Column<short>(type: "smallint", nullable: false),
                    BlockingCount = table.Column<int>(type: "int", nullable: false),
                    WarningCount = table.Column<int>(type: "int", nullable: false),
                    InfoCount = table.Column<int>(type: "int", nullable: false),
                    RequestedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RequestedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StartedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    FinishedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    JobId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CorrelationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FailureCode = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    FailureSummary = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
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
                    table.PrimaryKey("PK_Space_ValidationRun", x => x.Id);
                    table.UniqueConstraint("AK_Space_ValidationRun_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.CheckConstraint("CK_Space_ValidationRun_Counts", "[BlockingCount] >= 0 AND [WarningCount] >= 0 AND [InfoCount] >= 0 AND ([Status] <> 2 OR [BlockingCount] = 0) AND ([Status] <> 3 OR [BlockingCount] > 0)");
                    table.CheckConstraint("CK_Space_ValidationRun_StatusTime", "([Status] = 0 AND [StartedAtUtc] IS NULL AND [FinishedAtUtc] IS NULL) OR ([Status] = 1 AND [StartedAtUtc] IS NOT NULL AND [FinishedAtUtc] IS NULL) OR ([Status] IN (2, 3, 4) AND [FinishedAtUtc] IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_Space_ValidationRun_Job_Tenant",
                        columns: x => new { x.TenantId, x.JobId },
                        principalTable: "Space_Job",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Space_ValidationRun_Version_Tenant",
                        columns: x => new { x.TenantId, x.ModelVersionId },
                        principalTable: "Space_ModelVersion",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Space_ModelIssue_Tenant_Validation_Severity_Code",
                table: "Space_ModelIssue",
                columns: new[] { "TenantId", "ValidationRunId", "Severity", "Code", "Id" },
                filter: "[ValidationRunId] IS NOT NULL AND [IsDeleted] = 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Space_ModelIssue_ValidationScope",
                table: "Space_ModelIssue",
                sql: "[ValidationRunId] IS NULL OR ([ModelVersionId] IS NOT NULL AND [JobId] IS NOT NULL)");

            migrationBuilder.CreateIndex(
                name: "IX_Space_ValidationRun_Tenant_Version_Requested",
                table: "Space_ValidationRun",
                columns: new[] { "TenantId", "ModelVersionId", "RequestedAtUtc", "Id" });

            migrationBuilder.CreateIndex(
                name: "UX_Space_ValidationRun_Tenant_Input_ActiveOrReusable",
                table: "Space_ValidationRun",
                columns: new[] { "TenantId", "ModelVersionId", "ContentHash", "RuleSetVersion", "AdapterId", "CapabilityHash" },
                unique: true,
                filter: "[Status] <> 4 AND [IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "UX_Space_ValidationRun_Tenant_Job",
                table: "Space_ValidationRun",
                columns: new[] { "TenantId", "JobId" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Space_ModelIssue_ValidationRun_Tenant",
                table: "Space_ModelIssue",
                columns: new[] { "TenantId", "ValidationRunId" },
                principalTable: "Space_ValidationRun",
                principalColumns: new[] { "TenantId", "Id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                ;THROW 51018,
                    'E06-S01 is forward-only. Apply a reviewed forward-fix migration; do not remove validation evidence.',
                    1;
                """);
        }
    }
}
