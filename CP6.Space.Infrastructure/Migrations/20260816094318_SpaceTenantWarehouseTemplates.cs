using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CP6.Space.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SpaceTenantWarehouseTemplates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Space_WarehouseTemplate",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TemplateCode = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    NormalizedTemplateCode = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CurrentVersion = table.Column<int>(type: "int", nullable: false),
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
                    table.PrimaryKey("PK_Space_WarehouseTemplate", x => x.Id);
                    table.UniqueConstraint("AK_Space_WarehouseTemplate_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.CheckConstraint("CK_Space_WarehouseTemplate_CurrentVersion", "[CurrentVersion] > 0");
                });

            migrationBuilder.CreateTable(
                name: "Space_WarehouseTemplateVersion",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TemplateId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VersionNo = table.Column<int>(type: "int", nullable: false),
                    SchemaVersion = table.Column<int>(type: "int", nullable: false),
                    ContentJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ContentHash = table.Column<string>(type: "char(64)", unicode: false, fixedLength: true, maxLength: 64, nullable: false),
                    FloorCount = table.Column<int>(type: "int", nullable: false),
                    ZoneCount = table.Column<int>(type: "int", nullable: false),
                    AisleCount = table.Column<int>(type: "int", nullable: false),
                    RackCount = table.Column<int>(type: "int", nullable: false),
                    LocationCount = table.Column<int>(type: "int", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ModifiedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Space_WarehouseTemplateVersion", x => x.Id);
                    table.UniqueConstraint("AK_Space_WarehouseTemplateVersion_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.CheckConstraint("CK_Space_WarehouseTemplateVersion_Counts", "[FloorCount] > 0 AND [ZoneCount] >= 0 AND [AisleCount] >= 0 AND [RackCount] >= 0 AND [LocationCount] >= 0");
                    table.CheckConstraint("CK_Space_WarehouseTemplateVersion_SchemaVersion", "[SchemaVersion] > 0");
                    table.CheckConstraint("CK_Space_WarehouseTemplateVersion_VersionNo", "[VersionNo] > 0");
                    table.ForeignKey(
                        name: "FK_Space_WarehouseTemplateVersion_Template_Tenant",
                        columns: x => new { x.TenantId, x.TemplateId },
                        principalTable: "Space_WarehouseTemplate",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "UX_Space_WarehouseTemplate_Tenant_Code_Active",
                table: "Space_WarehouseTemplate",
                columns: new[] { "TenantId", "NormalizedTemplateCode" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_Space_WarehouseTemplateVersion_ContentHash",
                table: "Space_WarehouseTemplateVersion",
                columns: new[] { "TenantId", "ContentHash" });

            migrationBuilder.CreateIndex(
                name: "UX_Space_WarehouseTemplateVersion_Template_Version",
                table: "Space_WarehouseTemplateVersion",
                columns: new[] { "TenantId", "TemplateId", "VersionNo" },
                unique: true,
                filter: "[IsDeleted] = 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Space_WarehouseTemplateVersion");

            migrationBuilder.DropTable(
                name: "Space_WarehouseTemplate");
        }
    }
}
