using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CP6.Space.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SpaceE06S05HistoricalRepublish : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Space_HistoricalRepublish",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SiteId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ModelId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    HistoricalVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ExpectedPublishedVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TargetVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    JobId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ValidationRunId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    PublishAttemptId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    BusinessIdempotencyKey = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    RequestHash = table.Column<string>(type: "char(64)", unicode: false, fixedLength: true, maxLength: 64, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    ApprovalReference = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    RequestedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RequestedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CorrelationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Status = table.Column<short>(type: "smallint", nullable: false),
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
                    table.PrimaryKey("PK_Space_HistoricalRepublish", x => x.Id);
                    table.UniqueConstraint("AK_Space_HistoricalRepublish_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.CheckConstraint("CK_Space_HistoricalRepublish_Status", "[Status] IN (0, 1, 2, 3, 4)");
                    table.ForeignKey(
                        name: "FK_Space_HistoricalRepublish_ExpectedVersion_Tenant",
                        columns: x => new { x.TenantId, x.ModelId, x.ExpectedPublishedVersionId },
                        principalTable: "Space_ModelVersion",
                        principalColumns: new[] { "TenantId", "ModelId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Space_HistoricalRepublish_HistoricalVersion_Tenant",
                        columns: x => new { x.TenantId, x.ModelId, x.HistoricalVersionId },
                        principalTable: "Space_ModelVersion",
                        principalColumns: new[] { "TenantId", "ModelId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Space_HistoricalRepublish_Job_Tenant",
                        columns: x => new { x.TenantId, x.JobId },
                        principalTable: "Space_Job",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Space_HistoricalRepublish_Model_Tenant",
                        columns: x => new { x.TenantId, x.ModelId },
                        principalTable: "Space_Model",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Space_HistoricalRepublish_PublishAttempt_Tenant",
                        columns: x => new { x.TenantId, x.PublishAttemptId },
                        principalTable: "Space_PublishAttempt",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Space_HistoricalRepublish_TargetVersion_Tenant",
                        columns: x => new { x.TenantId, x.ModelId, x.TargetVersionId },
                        principalTable: "Space_ModelVersion",
                        principalColumns: new[] { "TenantId", "ModelId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Space_HistoricalRepublish_Validation_Tenant",
                        columns: x => new { x.TenantId, x.ValidationRunId },
                        principalTable: "Space_ValidationRun",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Space_HistoricalRepublish_Tenant_Site_Requested",
                table: "Space_HistoricalRepublish",
                columns: new[] { "TenantId", "SiteId", "RequestedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Space_HistoricalRepublish_TenantId_JobId",
                table: "Space_HistoricalRepublish",
                columns: new[] { "TenantId", "JobId" });

            migrationBuilder.CreateIndex(
                name: "IX_Space_HistoricalRepublish_TenantId_ModelId_ExpectedPublishedVersionId",
                table: "Space_HistoricalRepublish",
                columns: new[] { "TenantId", "ModelId", "ExpectedPublishedVersionId" });

            migrationBuilder.CreateIndex(
                name: "IX_Space_HistoricalRepublish_TenantId_ModelId_HistoricalVersionId",
                table: "Space_HistoricalRepublish",
                columns: new[] { "TenantId", "ModelId", "HistoricalVersionId" });

            migrationBuilder.CreateIndex(
                name: "IX_Space_HistoricalRepublish_TenantId_ModelId_TargetVersionId",
                table: "Space_HistoricalRepublish",
                columns: new[] { "TenantId", "ModelId", "TargetVersionId" });

            migrationBuilder.CreateIndex(
                name: "IX_Space_HistoricalRepublish_TenantId_ValidationRunId",
                table: "Space_HistoricalRepublish",
                columns: new[] { "TenantId", "ValidationRunId" });

            migrationBuilder.CreateIndex(
                name: "UX_Space_HistoricalRepublish_Tenant_Idempotency",
                table: "Space_HistoricalRepublish",
                columns: new[] { "TenantId", "BusinessIdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_Space_HistoricalRepublish_Tenant_PublishAttempt",
                table: "Space_HistoricalRepublish",
                columns: new[] { "TenantId", "PublishAttemptId" },
                unique: true,
                filter: "[PublishAttemptId] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "THROW 51022, 'E06-S05 historical republish evidence is forward-only; apply a higher forward-fix migration.', 1;");
        }
    }
}
