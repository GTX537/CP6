using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CP6.Space.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SpaceE13S10AtomicApply : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Name",
                table: "Space_ZoneRevision",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Name",
                table: "Space_RackRevision",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RackType",
                table: "Space_RackRevision",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AppliedCountsJson",
                table: "Space_GenerationRun",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ApplyCommandBatchId",
                table: "Space_GenerationRun",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ApplyExpectedRunRowVersion",
                table: "Space_GenerationRun",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ApplyJobId",
                table: "Space_GenerationRun",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ApplyPlanHash",
                table: "Space_GenerationRun",
                type: "char(64)",
                unicode: false,
                fixedLength: true,
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ApplyPreparedAtUtc",
                table: "Space_GenerationRun",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ApplyReviewEtag",
                table: "Space_GenerationRun",
                type: "char(64)",
                unicode: false,
                fixedLength: true,
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "TargetFloorLogicalId",
                table: "Space_GenerationRun",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Name",
                table: "Space_AisleRevision",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.Sql(
                "UPDATE [Space_ZoneRevision] SET [Name] = [ZoneCode] " +
                "WHERE [Name] IS NULL;");
            migrationBuilder.Sql(
                "UPDATE [Space_AisleRevision] SET [Name] = [AisleCode] " +
                "WHERE [Name] IS NULL;");
            migrationBuilder.Sql(
                "UPDATE [Space_RackRevision] SET [Name] = [RackCode] " +
                "WHERE [Name] IS NULL;");

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "Space_ZoneRevision",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(200)",
                oldMaxLength: 200,
                oldNullable: true);
            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "Space_AisleRevision",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(200)",
                oldMaxLength: 200,
                oldNullable: true);
            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "Space_RackRevision",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(200)",
                oldMaxLength: 200,
                oldNullable: true);

            migrationBuilder.CreateTable(
                name: "Space_GenerationStagingElement",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RunId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProposalId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ModelVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SequenceNo = table.Column<int>(type: "int", nullable: false),
                    LogicalId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FloorLogicalId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ElementType = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    NormalizedPayloadJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ValidationStatus = table.Column<short>(type: "smallint", nullable: false),
                    ValidationHash = table.Column<string>(type: "char(64)", unicode: false, fixedLength: true, maxLength: 64, nullable: true),
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
                    table.PrimaryKey("PK_Space_GenerationStagingElement", x => x.Id);
                    table.UniqueConstraint("AK_Space_GenerationStagingElement_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.CheckConstraint("CK_Space_GenerationStagingElement_Validation", "([ValidationStatus] = 0 AND [ValidationHash] IS NULL) OR ([ValidationStatus] = 1 AND [ValidationHash] IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_Space_GenerationStaging_Floor_Tenant_Version",
                        columns: x => new { x.TenantId, x.ModelVersionId, x.FloorLogicalId },
                        principalTable: "Space_FloorRevision",
                        principalColumns: new[] { "TenantId", "ModelVersionId", "LogicalId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Space_GenerationStaging_Proposal_Tenant_Run",
                        columns: x => new { x.TenantId, x.RunId, x.ProposalId },
                        principalTable: "Space_GenerationProposal",
                        principalColumns: new[] { "TenantId", "RunId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Space_GenerationStaging_Run_Tenant",
                        columns: x => new { x.TenantId, x.RunId },
                        principalTable: "Space_GenerationRun",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Space_GenerationRun_TenantId_ApplyJobId",
                table: "Space_GenerationRun",
                columns: new[] { "TenantId", "ApplyJobId" });

            migrationBuilder.CreateIndex(
                name: "IX_Space_GenerationRun_TenantId_ModelVersionId_TargetFloorLogicalId",
                table: "Space_GenerationRun",
                columns: new[] { "TenantId", "ModelVersionId", "TargetFloorLogicalId" });

            migrationBuilder.CreateIndex(
                name: "IX_Space_GenerationStagingElement_TenantId_ModelVersionId_FloorLogicalId",
                table: "Space_GenerationStagingElement",
                columns: new[] { "TenantId", "ModelVersionId", "FloorLogicalId" });

            migrationBuilder.CreateIndex(
                name: "UX_GenerationStaging_Tenant_Run_Logical",
                table: "Space_GenerationStagingElement",
                columns: new[] { "TenantId", "RunId", "LogicalId" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "UX_GenerationStaging_Tenant_Run_Proposal",
                table: "Space_GenerationStagingElement",
                columns: new[] { "TenantId", "RunId", "ProposalId" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "UX_GenerationStaging_Tenant_Run_Sequence",
                table: "Space_GenerationStagingElement",
                columns: new[] { "TenantId", "RunId", "SequenceNo" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.AddForeignKey(
                name: "FK_Space_GenerationRun_ApplyJob_Tenant",
                table: "Space_GenerationRun",
                columns: new[] { "TenantId", "ApplyJobId" },
                principalTable: "Space_Job",
                principalColumns: new[] { "TenantId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Space_GenerationRun_TargetFloor_Tenant_Version",
                table: "Space_GenerationRun",
                columns: new[] { "TenantId", "ModelVersionId", "TargetFloorLogicalId" },
                principalTable: "Space_FloorRevision",
                principalColumns: new[] { "TenantId", "ModelVersionId", "LogicalId" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Space_GenerationRun_ApplyJob_Tenant",
                table: "Space_GenerationRun");

            migrationBuilder.DropForeignKey(
                name: "FK_Space_GenerationRun_TargetFloor_Tenant_Version",
                table: "Space_GenerationRun");

            migrationBuilder.DropTable(
                name: "Space_GenerationStagingElement");

            migrationBuilder.DropIndex(
                name: "IX_Space_GenerationRun_TenantId_ApplyJobId",
                table: "Space_GenerationRun");

            migrationBuilder.DropIndex(
                name: "IX_Space_GenerationRun_TenantId_ModelVersionId_TargetFloorLogicalId",
                table: "Space_GenerationRun");

            migrationBuilder.DropColumn(
                name: "Name",
                table: "Space_ZoneRevision");

            migrationBuilder.DropColumn(
                name: "Name",
                table: "Space_RackRevision");

            migrationBuilder.DropColumn(
                name: "RackType",
                table: "Space_RackRevision");

            migrationBuilder.DropColumn(
                name: "AppliedCountsJson",
                table: "Space_GenerationRun");

            migrationBuilder.DropColumn(
                name: "ApplyCommandBatchId",
                table: "Space_GenerationRun");

            migrationBuilder.DropColumn(
                name: "ApplyExpectedRunRowVersion",
                table: "Space_GenerationRun");

            migrationBuilder.DropColumn(
                name: "ApplyJobId",
                table: "Space_GenerationRun");

            migrationBuilder.DropColumn(
                name: "ApplyPlanHash",
                table: "Space_GenerationRun");

            migrationBuilder.DropColumn(
                name: "ApplyPreparedAtUtc",
                table: "Space_GenerationRun");

            migrationBuilder.DropColumn(
                name: "ApplyReviewEtag",
                table: "Space_GenerationRun");

            migrationBuilder.DropColumn(
                name: "TargetFloorLogicalId",
                table: "Space_GenerationRun");

            migrationBuilder.DropColumn(
                name: "Name",
                table: "Space_AisleRevision");
        }
    }
}
