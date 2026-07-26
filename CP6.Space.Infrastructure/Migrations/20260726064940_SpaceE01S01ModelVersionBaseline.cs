using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CP6.Space.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SpaceE01S01ModelVersionBaseline : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Space_Model",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SiteId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Mode = table.Column<short>(type: "smallint", nullable: false),
                    CutoverState = table.Column<short>(type: "smallint", nullable: false),
                    CutoverOperationId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ActiveDraftVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CurrentPublishedVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    LastMaterializedHash = table.Column<string>(type: "char(64)", unicode: false, fixedLength: true, maxLength: 64, nullable: true),
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
                    table.PrimaryKey("PK_Space_Model", x => x.Id);
                    table.UniqueConstraint("AK_Space_Model_TenantId_Id", x => new { x.TenantId, x.Id });
                });

            migrationBuilder.CreateTable(
                name: "Space_ModelVersion",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ModelId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VersionNo = table.Column<long>(type: "bigint", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Status = table.Column<short>(type: "smallint", nullable: false),
                    BasedOnVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ContentRevision = table.Column<long>(type: "bigint", nullable: false),
                    ContentHash = table.Column<string>(type: "char(64)", unicode: false, fixedLength: true, maxLength: 64, nullable: true),
                    RuleSetVersion = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    ValidatedHash = table.Column<string>(type: "char(64)", unicode: false, fixedLength: true, maxLength: 64, nullable: true),
                    WmsCapabilityHash = table.Column<string>(type: "char(64)", unicode: false, fixedLength: true, maxLength: 64, nullable: true),
                    PublishedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    PublishedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
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
                    table.PrimaryKey("PK_Space_ModelVersion", x => x.Id);
                    table.UniqueConstraint("AK_Space_ModelVersion_TenantId_ModelId_Id", x => new { x.TenantId, x.ModelId, x.Id });
                    table.ForeignKey(
                        name: "FK_Space_ModelVersion_BasedOn_Tenant_Model_Version",
                        columns: x => new { x.TenantId, x.ModelId, x.BasedOnVersionId },
                        principalTable: "Space_ModelVersion",
                        principalColumns: new[] { "TenantId", "ModelId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Space_ModelVersion_Space_Model_Tenant_Model",
                        columns: x => new { x.TenantId, x.ModelId },
                        principalTable: "Space_Model",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Space_Model_TenantId_Id_ActiveDraftVersionId",
                table: "Space_Model",
                columns: new[] { "TenantId", "Id", "ActiveDraftVersionId" });

            migrationBuilder.CreateIndex(
                name: "IX_Space_Model_TenantId_Id_CurrentPublishedVersionId",
                table: "Space_Model",
                columns: new[] { "TenantId", "Id", "CurrentPublishedVersionId" });

            migrationBuilder.CreateIndex(
                name: "UX_Space_Model_Tenant_ActiveDraft",
                table: "Space_Model",
                columns: new[] { "TenantId", "ActiveDraftVersionId" },
                unique: true,
                filter: "[ActiveDraftVersionId] IS NOT NULL AND [IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "UX_Space_Model_Tenant_CurrentPublished",
                table: "Space_Model",
                columns: new[] { "TenantId", "CurrentPublishedVersionId" },
                unique: true,
                filter: "[CurrentPublishedVersionId] IS NOT NULL AND [IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "UX_Space_Model_Tenant_Site_Active",
                table: "Space_Model",
                columns: new[] { "TenantId", "SiteId" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_Space_ModelVersion_Tenant_BasedOn",
                table: "Space_ModelVersion",
                columns: new[] { "TenantId", "BasedOnVersionId" },
                filter: "[BasedOnVersionId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Space_ModelVersion_Tenant_Model_Status",
                table: "Space_ModelVersion",
                columns: new[] { "TenantId", "ModelId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_Space_ModelVersion_TenantId_ModelId_BasedOnVersionId",
                table: "Space_ModelVersion",
                columns: new[] { "TenantId", "ModelId", "BasedOnVersionId" });

            migrationBuilder.CreateIndex(
                name: "UX_Space_ModelVersion_Tenant_Model_VersionNo",
                table: "Space_ModelVersion",
                columns: new[] { "TenantId", "ModelId", "VersionNo" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Space_Model_ActiveDraft_Tenant_Model_Version",
                table: "Space_Model",
                columns: new[] { "TenantId", "Id", "ActiveDraftVersionId" },
                principalTable: "Space_ModelVersion",
                principalColumns: new[] { "TenantId", "ModelId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Space_Model_CurrentPublished_Tenant_Model_Version",
                table: "Space_Model",
                columns: new[] { "TenantId", "Id", "CurrentPublishedVersionId" },
                principalTable: "Space_ModelVersion",
                principalColumns: new[] { "TenantId", "ModelId", "Id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Space_Model_ActiveDraft_Tenant_Model_Version",
                table: "Space_Model");

            migrationBuilder.DropForeignKey(
                name: "FK_Space_Model_CurrentPublished_Tenant_Model_Version",
                table: "Space_Model");

            migrationBuilder.DropTable(
                name: "Space_ModelVersion");

            migrationBuilder.DropTable(
                name: "Space_Model");
        }
    }
}
