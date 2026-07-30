using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CP6.Space.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SpaceE01S04PublishedClone : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "CloneOperationId",
                table: "Space_ModelVersion",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Space_FloorRevision",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SiteLogicalId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Level = table.Column<int>(type: "int", nullable: false),
                    FloorCode = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Elevation = table.Column<int>(type: "int", nullable: false),
                    Height = table.Column<int>(type: "int", nullable: false),
                    BoundaryJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CoordinateSystem = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    UnderlaySourceId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UnderlayScale = table.Column<decimal>(type: "decimal(18,8)", nullable: true),
                    UnderlayOffsetX = table.Column<int>(type: "int", nullable: false),
                    UnderlayOffsetY = table.Column<int>(type: "int", nullable: false),
                    UnderlayRotationZ = table.Column<decimal>(type: "decimal(9,4)", nullable: false),
                    Revision = table.Column<long>(type: "bigint", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ModifiedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    ModelVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LogicalId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SourceId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SourceRef = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    LifecycleState = table.Column<short>(type: "smallint", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Space_FloorRevision", x => x.Id);
                    table.UniqueConstraint("AK_Space_FloorRevision_TenantId_ModelVersionId_Id", x => new { x.TenantId, x.ModelVersionId, x.Id });
                    table.UniqueConstraint("AK_Space_FloorRevision_TenantId_ModelVersionId_LogicalId", x => new { x.TenantId, x.ModelVersionId, x.LogicalId });
                    table.ForeignKey(
                        name: "FK_Space_FloorRevision_Space_ModelSource_TenantId_ModelVersionId_SourceId",
                        columns: x => new { x.TenantId, x.ModelVersionId, x.SourceId },
                        principalTable: "Space_ModelSource",
                        principalColumns: new[] { "TenantId", "ModelVersionId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Space_FloorRevision_Space_ModelVersion_TenantId_ModelVersionId",
                        columns: x => new { x.TenantId, x.ModelVersionId },
                        principalTable: "Space_ModelVersion",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Space_FloorRevision_UnderlaySource_Tenant_Version",
                        columns: x => new { x.TenantId, x.ModelVersionId, x.UnderlaySourceId },
                        principalTable: "Space_ModelSource",
                        principalColumns: new[] { "TenantId", "ModelVersionId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Space_ElementRevision",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FloorLogicalId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ParentLogicalId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ElementType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    GeometryJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ModelAssetId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    X = table.Column<int>(type: "int", nullable: false),
                    Y = table.Column<int>(type: "int", nullable: false),
                    Z = table.Column<int>(type: "int", nullable: false),
                    RotationZ = table.Column<decimal>(type: "decimal(9,4)", nullable: false),
                    Width = table.Column<int>(type: "int", nullable: false),
                    Height = table.Column<int>(type: "int", nullable: false),
                    Depth = table.Column<int>(type: "int", nullable: false),
                    BusinessCode = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    LinkedEntityType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    LinkedLogicalId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ModifiedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    ModelVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LogicalId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SourceId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SourceRef = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    LifecycleState = table.Column<short>(type: "smallint", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Space_ElementRevision", x => x.Id);
                    table.UniqueConstraint("AK_Space_ElementRevision_TenantId_ModelVersionId_Id", x => new { x.TenantId, x.ModelVersionId, x.Id });
                    table.UniqueConstraint("AK_Space_ElementRevision_TenantId_ModelVersionId_LogicalId", x => new { x.TenantId, x.ModelVersionId, x.LogicalId });
                    table.CheckConstraint("CK_Space_ElementRevision_Geometry", "[RotationZ] >= 0 AND [RotationZ] < 360 AND [Width] >= 0 AND [Height] >= 0 AND [Depth] >= 0");
                    table.ForeignKey(
                        name: "FK_Space_ElementRevision_Floor_Tenant_Version_Logical",
                        columns: x => new { x.TenantId, x.ModelVersionId, x.FloorLogicalId },
                        principalTable: "Space_FloorRevision",
                        principalColumns: new[] { "TenantId", "ModelVersionId", "LogicalId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Space_ElementRevision_Parent_Tenant_Version_Logical",
                        columns: x => new { x.TenantId, x.ModelVersionId, x.ParentLogicalId },
                        principalTable: "Space_ElementRevision",
                        principalColumns: new[] { "TenantId", "ModelVersionId", "LogicalId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Space_ElementRevision_Space_ModelSource_TenantId_ModelVersionId_SourceId",
                        columns: x => new { x.TenantId, x.ModelVersionId, x.SourceId },
                        principalTable: "Space_ModelSource",
                        principalColumns: new[] { "TenantId", "ModelVersionId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Space_ElementRevision_Space_ModelVersion_TenantId_ModelVersionId",
                        columns: x => new { x.TenantId, x.ModelVersionId },
                        principalTable: "Space_ModelVersion",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Space_ZoneRevision",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FloorLogicalId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ZoneCode = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ZoneType = table.Column<short>(type: "smallint", nullable: false),
                    PolygonJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Color = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    CapabilityFlags = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ModifiedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    ModelVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LogicalId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SourceId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SourceRef = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    LifecycleState = table.Column<short>(type: "smallint", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Space_ZoneRevision", x => x.Id);
                    table.UniqueConstraint("AK_Space_ZoneRevision_TenantId_ModelVersionId_Id", x => new { x.TenantId, x.ModelVersionId, x.Id });
                    table.UniqueConstraint("AK_Space_ZoneRevision_TenantId_ModelVersionId_LogicalId", x => new { x.TenantId, x.ModelVersionId, x.LogicalId });
                    table.ForeignKey(
                        name: "FK_Space_ZoneRevision_Floor_Tenant_Version_Logical",
                        columns: x => new { x.TenantId, x.ModelVersionId, x.FloorLogicalId },
                        principalTable: "Space_FloorRevision",
                        principalColumns: new[] { "TenantId", "ModelVersionId", "LogicalId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Space_ZoneRevision_Space_ModelSource_TenantId_ModelVersionId_SourceId",
                        columns: x => new { x.TenantId, x.ModelVersionId, x.SourceId },
                        principalTable: "Space_ModelSource",
                        principalColumns: new[] { "TenantId", "ModelVersionId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Space_ZoneRevision_Space_ModelVersion_TenantId_ModelVersionId",
                        columns: x => new { x.TenantId, x.ModelVersionId },
                        principalTable: "Space_ModelVersion",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Space_ElementAttribute",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ModelVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ElementRevisionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Namespace = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Key = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ValueType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Value = table.Column<string>(type: "nvarchar(max)", maxLength: 8000, nullable: true),
                    Unit = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ModifiedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Space_ElementAttribute", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Space_ElementAttribute_Element_Tenant_Version",
                        columns: x => new { x.TenantId, x.ModelVersionId, x.ElementRevisionId },
                        principalTable: "Space_ElementRevision",
                        principalColumns: new[] { "TenantId", "ModelVersionId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Space_AisleRevision",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ZoneLogicalId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AisleCode = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    PolygonJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CenterlineJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Direction = table.Column<short>(type: "smallint", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ModifiedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    ModelVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LogicalId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SourceId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SourceRef = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    LifecycleState = table.Column<short>(type: "smallint", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Space_AisleRevision", x => x.Id);
                    table.UniqueConstraint("AK_Space_AisleRevision_TenantId_ModelVersionId_Id", x => new { x.TenantId, x.ModelVersionId, x.Id });
                    table.UniqueConstraint("AK_Space_AisleRevision_TenantId_ModelVersionId_LogicalId", x => new { x.TenantId, x.ModelVersionId, x.LogicalId });
                    table.ForeignKey(
                        name: "FK_Space_AisleRevision_Space_ModelSource_TenantId_ModelVersionId_SourceId",
                        columns: x => new { x.TenantId, x.ModelVersionId, x.SourceId },
                        principalTable: "Space_ModelSource",
                        principalColumns: new[] { "TenantId", "ModelVersionId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Space_AisleRevision_Space_ModelVersion_TenantId_ModelVersionId",
                        columns: x => new { x.TenantId, x.ModelVersionId },
                        principalTable: "Space_ModelVersion",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Space_AisleRevision_Zone_Tenant_Version_Logical",
                        columns: x => new { x.TenantId, x.ModelVersionId, x.ZoneLogicalId },
                        principalTable: "Space_ZoneRevision",
                        principalColumns: new[] { "TenantId", "ModelVersionId", "LogicalId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Space_RackRevision",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FloorLogicalId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ZoneLogicalId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AisleLogicalId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RackCode = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    TemplateVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    X = table.Column<int>(type: "int", nullable: false),
                    Y = table.Column<int>(type: "int", nullable: false),
                    Z = table.Column<int>(type: "int", nullable: false),
                    RotationZ = table.Column<decimal>(type: "decimal(9,4)", nullable: false),
                    Width = table.Column<int>(type: "int", nullable: false),
                    Depth = table.Column<int>(type: "int", nullable: false),
                    Height = table.Column<int>(type: "int", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ModifiedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    ModelVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LogicalId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SourceId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SourceRef = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    LifecycleState = table.Column<short>(type: "smallint", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Space_RackRevision", x => x.Id);
                    table.UniqueConstraint("AK_Space_RackRevision_TenantId_ModelVersionId_Id", x => new { x.TenantId, x.ModelVersionId, x.Id });
                    table.UniqueConstraint("AK_Space_RackRevision_TenantId_ModelVersionId_LogicalId", x => new { x.TenantId, x.ModelVersionId, x.LogicalId });
                    table.CheckConstraint("CK_Space_RackRevision_Geometry", "[RotationZ] >= 0 AND [RotationZ] < 360 AND [Width] >= 0 AND [Depth] >= 0 AND [Height] >= 0");
                    table.ForeignKey(
                        name: "FK_Space_RackRevision_Aisle_Tenant_Version_Logical",
                        columns: x => new { x.TenantId, x.ModelVersionId, x.AisleLogicalId },
                        principalTable: "Space_AisleRevision",
                        principalColumns: new[] { "TenantId", "ModelVersionId", "LogicalId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Space_RackRevision_Floor_Tenant_Version_Logical",
                        columns: x => new { x.TenantId, x.ModelVersionId, x.FloorLogicalId },
                        principalTable: "Space_FloorRevision",
                        principalColumns: new[] { "TenantId", "ModelVersionId", "LogicalId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Space_RackRevision_Space_ModelSource_TenantId_ModelVersionId_SourceId",
                        columns: x => new { x.TenantId, x.ModelVersionId, x.SourceId },
                        principalTable: "Space_ModelSource",
                        principalColumns: new[] { "TenantId", "ModelVersionId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Space_RackRevision_Space_ModelVersion_TenantId_ModelVersionId",
                        columns: x => new { x.TenantId, x.ModelVersionId },
                        principalTable: "Space_ModelVersion",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Space_RackRevision_Zone_Tenant_Version_Logical",
                        columns: x => new { x.TenantId, x.ModelVersionId, x.ZoneLogicalId },
                        principalTable: "Space_ZoneRevision",
                        principalColumns: new[] { "TenantId", "ModelVersionId", "LogicalId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Space_LocationRevision",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FloorLogicalId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RackLogicalId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    LocationCode = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ColumnNo = table.Column<int>(type: "int", nullable: false),
                    LevelNo = table.Column<int>(type: "int", nullable: false),
                    DepthNo = table.Column<int>(type: "int", nullable: false),
                    Width = table.Column<int>(type: "int", nullable: false),
                    Height = table.Column<int>(type: "int", nullable: false),
                    Depth = table.Column<int>(type: "int", nullable: false),
                    MaxLoad = table.Column<decimal>(type: "decimal(18,4)", nullable: true),
                    CodeOrigin = table.Column<short>(type: "smallint", nullable: false),
                    ExternalBindingState = table.Column<short>(type: "smallint", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ModifiedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    ModelVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LogicalId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SourceId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SourceRef = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    LifecycleState = table.Column<short>(type: "smallint", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Space_LocationRevision", x => x.Id);
                    table.UniqueConstraint("AK_Space_LocationRevision_TenantId_ModelVersionId_Id", x => new { x.TenantId, x.ModelVersionId, x.Id });
                    table.UniqueConstraint("AK_Space_LocationRevision_TenantId_ModelVersionId_LogicalId", x => new { x.TenantId, x.ModelVersionId, x.LogicalId });
                    table.CheckConstraint("CK_Space_LocationRevision_Dimensions", "[ColumnNo] > 0 AND [LevelNo] > 0 AND [DepthNo] > 0 AND [Width] > 0 AND [Height] > 0 AND [Depth] > 0 AND ([MaxLoad] IS NULL OR [MaxLoad] >= 0)");
                    table.ForeignKey(
                        name: "FK_Space_LocationRevision_Floor_Tenant_Version_Logical",
                        columns: x => new { x.TenantId, x.ModelVersionId, x.FloorLogicalId },
                        principalTable: "Space_FloorRevision",
                        principalColumns: new[] { "TenantId", "ModelVersionId", "LogicalId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Space_LocationRevision_Rack_Tenant_Version_Logical",
                        columns: x => new { x.TenantId, x.ModelVersionId, x.RackLogicalId },
                        principalTable: "Space_RackRevision",
                        principalColumns: new[] { "TenantId", "ModelVersionId", "LogicalId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Space_LocationRevision_Space_ModelSource_TenantId_ModelVersionId_SourceId",
                        columns: x => new { x.TenantId, x.ModelVersionId, x.SourceId },
                        principalTable: "Space_ModelSource",
                        principalColumns: new[] { "TenantId", "ModelVersionId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Space_LocationRevision_Space_ModelVersion_TenantId_ModelVersionId",
                        columns: x => new { x.TenantId, x.ModelVersionId },
                        principalTable: "Space_ModelVersion",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Space_RackLevelRevision",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RackLogicalId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LevelNo = table.Column<int>(type: "int", nullable: false),
                    BottomZ = table.Column<int>(type: "int", nullable: false),
                    ClearHeight = table.Column<int>(type: "int", nullable: false),
                    BinCount = table.Column<int>(type: "int", nullable: false),
                    DepthCount = table.Column<int>(type: "int", nullable: false),
                    CellWidth = table.Column<int>(type: "int", nullable: false),
                    CellDepth = table.Column<int>(type: "int", nullable: false),
                    MaxLoad = table.Column<decimal>(type: "decimal(18,4)", nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ModifiedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    ModelVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LogicalId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SourceId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SourceRef = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    LifecycleState = table.Column<short>(type: "smallint", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Space_RackLevelRevision", x => x.Id);
                    table.UniqueConstraint("AK_Space_RackLevelRevision_TenantId_ModelVersionId_Id", x => new { x.TenantId, x.ModelVersionId, x.Id });
                    table.UniqueConstraint("AK_Space_RackLevelRevision_TenantId_ModelVersionId_LogicalId", x => new { x.TenantId, x.ModelVersionId, x.LogicalId });
                    table.CheckConstraint("CK_Space_RackLevelRevision_Dimensions", "[LevelNo] > 0 AND [ClearHeight] > 0 AND [BinCount] > 0 AND [DepthCount] > 0 AND [CellWidth] > 0 AND [CellDepth] > 0 AND ([MaxLoad] IS NULL OR [MaxLoad] >= 0)");
                    table.ForeignKey(
                        name: "FK_Space_RackLevelRevision_Rack_Tenant_Version_Logical",
                        columns: x => new { x.TenantId, x.ModelVersionId, x.RackLogicalId },
                        principalTable: "Space_RackRevision",
                        principalColumns: new[] { "TenantId", "ModelVersionId", "LogicalId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Space_RackLevelRevision_Space_ModelSource_TenantId_ModelVersionId_SourceId",
                        columns: x => new { x.TenantId, x.ModelVersionId, x.SourceId },
                        principalTable: "Space_ModelSource",
                        principalColumns: new[] { "TenantId", "ModelVersionId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Space_RackLevelRevision_Space_ModelVersion_TenantId_ModelVersionId",
                        columns: x => new { x.TenantId, x.ModelVersionId },
                        principalTable: "Space_ModelVersion",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "UX_Space_ModelVersion_Tenant_Model_CloneOperation",
                table: "Space_ModelVersion",
                columns: new[] { "TenantId", "ModelId", "CloneOperationId" },
                unique: true,
                filter: "[CloneOperationId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Space_AisleRevision_TenantId_ModelVersionId_SourceId",
                table: "Space_AisleRevision",
                columns: new[] { "TenantId", "ModelVersionId", "SourceId" });

            migrationBuilder.CreateIndex(
                name: "UX_Space_AisleRevision_Zone_Code_Active",
                table: "Space_AisleRevision",
                columns: new[] { "TenantId", "ModelVersionId", "ZoneLogicalId", "AisleCode" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "UX_Space_ElementAttribute_Element_Key_Active",
                table: "Space_ElementAttribute",
                columns: new[] { "TenantId", "ModelVersionId", "ElementRevisionId", "Namespace", "Key" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_Space_ElementRevision_Floor_Type",
                table: "Space_ElementRevision",
                columns: new[] { "TenantId", "ModelVersionId", "FloorLogicalId", "ElementType" });

            migrationBuilder.CreateIndex(
                name: "IX_Space_ElementRevision_TenantId_ModelVersionId_ParentLogicalId",
                table: "Space_ElementRevision",
                columns: new[] { "TenantId", "ModelVersionId", "ParentLogicalId" });

            migrationBuilder.CreateIndex(
                name: "IX_Space_ElementRevision_TenantId_ModelVersionId_SourceId",
                table: "Space_ElementRevision",
                columns: new[] { "TenantId", "ModelVersionId", "SourceId" });

            migrationBuilder.CreateIndex(
                name: "IX_Space_FloorRevision_TenantId_ModelVersionId_SourceId",
                table: "Space_FloorRevision",
                columns: new[] { "TenantId", "ModelVersionId", "SourceId" });

            migrationBuilder.CreateIndex(
                name: "IX_Space_FloorRevision_TenantId_ModelVersionId_UnderlaySourceId",
                table: "Space_FloorRevision",
                columns: new[] { "TenantId", "ModelVersionId", "UnderlaySourceId" });

            migrationBuilder.CreateIndex(
                name: "IX_Space_FloorRevision_Version_Level",
                table: "Space_FloorRevision",
                columns: new[] { "TenantId", "ModelVersionId", "Level" });

            migrationBuilder.CreateIndex(
                name: "UX_Space_FloorRevision_Version_Code_Active",
                table: "Space_FloorRevision",
                columns: new[] { "TenantId", "ModelVersionId", "FloorCode" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_Space_LocationRevision_Rack_Position_Active",
                table: "Space_LocationRevision",
                columns: new[] { "TenantId", "ModelVersionId", "RackLogicalId", "LevelNo", "ColumnNo", "DepthNo" },
                filter: "[RackLogicalId] IS NOT NULL AND [IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_Space_LocationRevision_TenantId_ModelVersionId_FloorLogicalId",
                table: "Space_LocationRevision",
                columns: new[] { "TenantId", "ModelVersionId", "FloorLogicalId" });

            migrationBuilder.CreateIndex(
                name: "IX_Space_LocationRevision_TenantId_ModelVersionId_SourceId",
                table: "Space_LocationRevision",
                columns: new[] { "TenantId", "ModelVersionId", "SourceId" });

            migrationBuilder.CreateIndex(
                name: "UX_Space_LocationRevision_Version_Code_Active",
                table: "Space_LocationRevision",
                columns: new[] { "TenantId", "ModelVersionId", "LocationCode" },
                unique: true,
                filter: "[LocationCode] IS NOT NULL AND [IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_Space_RackLevelRevision_TenantId_ModelVersionId_SourceId",
                table: "Space_RackLevelRevision",
                columns: new[] { "TenantId", "ModelVersionId", "SourceId" });

            migrationBuilder.CreateIndex(
                name: "UX_Space_RackLevelRevision_Rack_Level_Active",
                table: "Space_RackLevelRevision",
                columns: new[] { "TenantId", "ModelVersionId", "RackLogicalId", "LevelNo" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_Space_RackRevision_TenantId_ModelVersionId_AisleLogicalId",
                table: "Space_RackRevision",
                columns: new[] { "TenantId", "ModelVersionId", "AisleLogicalId" });

            migrationBuilder.CreateIndex(
                name: "IX_Space_RackRevision_TenantId_ModelVersionId_FloorLogicalId",
                table: "Space_RackRevision",
                columns: new[] { "TenantId", "ModelVersionId", "FloorLogicalId" });

            migrationBuilder.CreateIndex(
                name: "IX_Space_RackRevision_TenantId_ModelVersionId_SourceId",
                table: "Space_RackRevision",
                columns: new[] { "TenantId", "ModelVersionId", "SourceId" });

            migrationBuilder.CreateIndex(
                name: "UX_Space_RackRevision_Zone_Code_Active",
                table: "Space_RackRevision",
                columns: new[] { "TenantId", "ModelVersionId", "ZoneLogicalId", "RackCode" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_Space_ZoneRevision_TenantId_ModelVersionId_SourceId",
                table: "Space_ZoneRevision",
                columns: new[] { "TenantId", "ModelVersionId", "SourceId" });

            migrationBuilder.CreateIndex(
                name: "UX_Space_ZoneRevision_Floor_Code_Active",
                table: "Space_ZoneRevision",
                columns: new[] { "TenantId", "ModelVersionId", "FloorLogicalId", "ZoneCode" },
                unique: true,
                filter: "[IsDeleted] = 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Space_ElementAttribute");

            migrationBuilder.DropTable(
                name: "Space_LocationRevision");

            migrationBuilder.DropTable(
                name: "Space_RackLevelRevision");

            migrationBuilder.DropTable(
                name: "Space_ElementRevision");

            migrationBuilder.DropTable(
                name: "Space_RackRevision");

            migrationBuilder.DropTable(
                name: "Space_AisleRevision");

            migrationBuilder.DropTable(
                name: "Space_ZoneRevision");

            migrationBuilder.DropTable(
                name: "Space_FloorRevision");

            migrationBuilder.DropIndex(
                name: "UX_Space_ModelVersion_Tenant_Model_CloneOperation",
                table: "Space_ModelVersion");

            migrationBuilder.DropColumn(
                name: "CloneOperationId",
                table: "Space_ModelVersion");
        }
    }
}
