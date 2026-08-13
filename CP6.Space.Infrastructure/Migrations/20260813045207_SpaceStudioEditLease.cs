using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CP6.Space.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SpaceStudioEditLease : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "LeaseId",
                table: "Space_ElementCommandBatch",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Space_EditLease",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ModelVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FloorLogicalId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LeaseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OwnerUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    HolderDisplayName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ClientInstanceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AcquiredAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LastRenewedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
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
                    table.PrimaryKey("PK_Space_EditLease", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Space_EditLease_Floor_Tenant_Version_Logical",
                        columns: x => new { x.TenantId, x.ModelVersionId, x.FloorLogicalId },
                        principalTable: "Space_FloorRevision",
                        principalColumns: new[] { "TenantId", "ModelVersionId", "LogicalId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Space_EditLease_Version_Tenant",
                        columns: x => new { x.TenantId, x.ModelVersionId },
                        principalTable: "Space_ModelVersion",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Space_EditLeaseTakeoverAudit",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ModelVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FloorLogicalId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PreviousLeaseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PreviousOwnerUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    NewLeaseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TakenOverByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClientInstanceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    CorrelationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RequestSource = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    TakenOverAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ModifiedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Space_EditLeaseTakeoverAudit", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Space_EditLeaseTakeoverAudit_Version_Tenant",
                        columns: x => new { x.TenantId, x.ModelVersionId },
                        principalTable: "Space_ModelVersion",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "UX_Space_EditLease_Version_Floor",
                table: "Space_EditLease",
                columns: new[] { "TenantId", "ModelVersionId", "FloorLogicalId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Space_EditLeaseTakeoverAudit_Floor_TakenOver",
                table: "Space_EditLeaseTakeoverAudit",
                columns: new[] { "TenantId", "ModelVersionId", "FloorLogicalId", "TakenOverAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Space_EditLease");

            migrationBuilder.DropTable(
                name: "Space_EditLeaseTakeoverAudit");

            migrationBuilder.DropColumn(
                name: "LeaseId",
                table: "Space_ElementCommandBatch");
        }
    }
}
