using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CP6.Space.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SpaceE03S05ExcelDesignMetadata : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "LocationType",
                table: "Space_LocationRevision",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Space_DesignAttribute",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ModelVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ObjectType = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ObjectLogicalId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Namespace = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Key = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Value = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    Unit = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    SourceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SourceRef = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ModifiedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Space_DesignAttribute", x => x.Id);
                    table.UniqueConstraint("AK_Space_DesignAttribute_TenantId_ModelVersionId_Id", x => new { x.TenantId, x.ModelVersionId, x.Id });
                    table.CheckConstraint("CK_Space_DesignAttribute_ObjectType", "[ObjectType] IN ('Rack', 'RackLevel', 'Location')");
                    table.ForeignKey(
                        name: "FK_Space_DesignAttribute_Source_Tenant_Version",
                        columns: x => new { x.TenantId, x.ModelVersionId, x.SourceId },
                        principalTable: "Space_ModelSource",
                        principalColumns: new[] { "TenantId", "ModelVersionId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Space_DesignAttribute_Version_Tenant",
                        columns: x => new { x.TenantId, x.ModelVersionId },
                        principalTable: "Space_ModelVersion",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Space_LocationExternalBinding",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ModelVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LocationLogicalId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AdapterId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    WarehouseCode = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ExternalLocationId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    BindingMode = table.Column<short>(type: "smallint", nullable: false),
                    SourceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SourceRef = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ModifiedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Space_LocationExternalBinding", x => x.Id);
                    table.UniqueConstraint("AK_Space_LocationExternalBinding_TenantId_ModelVersionId_Id", x => new { x.TenantId, x.ModelVersionId, x.Id });
                    table.CheckConstraint("CK_Space_LocationExternalBinding_Mode", "[BindingMode] IN (0, 1)");
                    table.ForeignKey(
                        name: "FK_Space_LocationExternalBinding_Location_Tenant_Version_Logical",
                        columns: x => new { x.TenantId, x.ModelVersionId, x.LocationLogicalId },
                        principalTable: "Space_LocationRevision",
                        principalColumns: new[] { "TenantId", "ModelVersionId", "LogicalId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Space_LocationExternalBinding_Source_Tenant_Version",
                        columns: x => new { x.TenantId, x.ModelVersionId, x.SourceId },
                        principalTable: "Space_ModelSource",
                        principalColumns: new[] { "TenantId", "ModelVersionId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Space_LocationExternalBinding_Version_Tenant",
                        columns: x => new { x.TenantId, x.ModelVersionId },
                        principalTable: "Space_ModelVersion",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Space_DesignAttribute_TenantId_ModelVersionId_SourceId",
                table: "Space_DesignAttribute",
                columns: new[] { "TenantId", "ModelVersionId", "SourceId" });

            migrationBuilder.CreateIndex(
                name: "UX_Space_DesignAttribute_Target_Key_Active",
                table: "Space_DesignAttribute",
                columns: new[] { "TenantId", "ModelVersionId", "ObjectType", "ObjectLogicalId", "Namespace", "Key" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_Space_LocationExternalBinding_TenantId_ModelVersionId_SourceId",
                table: "Space_LocationExternalBinding",
                columns: new[] { "TenantId", "ModelVersionId", "SourceId" });

            migrationBuilder.CreateIndex(
                name: "UX_Space_LocationExternalBinding_External_Active",
                table: "Space_LocationExternalBinding",
                columns: new[] { "TenantId", "ModelVersionId", "AdapterId", "WarehouseCode", "ExternalLocationId" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "UX_Space_LocationExternalBinding_Primary_Active",
                table: "Space_LocationExternalBinding",
                columns: new[] { "TenantId", "ModelVersionId", "LocationLogicalId" },
                unique: true,
                filter: "[BindingMode] = 0 AND [IsDeleted] = 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Space_DesignAttribute");

            migrationBuilder.DropTable(
                name: "Space_LocationExternalBinding");

            migrationBuilder.DropColumn(
                name: "LocationType",
                table: "Space_LocationRevision");
        }
    }
}
