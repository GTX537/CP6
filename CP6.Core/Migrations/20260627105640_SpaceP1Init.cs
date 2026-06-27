using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CP6.Core.Migrations
{
    /// <inheritdoc />
    public partial class SpaceP1Init : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Space_Aisle",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ZoneId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AisleCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Polygon = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Centerline = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Creator = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CreateDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Modifier = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ModifyDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Space_Aisle", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Space_CodeRule",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RuleName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ScopeType = table.Column<int>(type: "int", nullable: false),
                    ScopeId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Segments = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    IsDefault = table.Column<bool>(type: "bit", nullable: false),
                    Creator = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CreateDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Modifier = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ModifyDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Space_CodeRule", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Space_Floor",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SiteId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Level = table.Column<int>(type: "int", nullable: false),
                    FloorCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    FloorName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Height = table.Column<int>(type: "int", nullable: false),
                    UnderlayImage = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    UnderlayScale = table.Column<double>(type: "float", nullable: true),
                    UnderlayOffsetX = table.Column<int>(type: "int", nullable: false),
                    UnderlayOffsetY = table.Column<int>(type: "int", nullable: false),
                    OriginX = table.Column<int>(type: "int", nullable: false),
                    OriginY = table.Column<int>(type: "int", nullable: false),
                    Creator = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CreateDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Modifier = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ModifyDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Space_Floor", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Space_Location",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RackId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    FloorId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    LocationCode = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CodeOrigin = table.Column<int>(type: "int", nullable: false),
                    Col = table.Column<int>(type: "int", nullable: true),
                    Level = table.Column<int>(type: "int", nullable: true),
                    Depth = table.Column<int>(type: "int", nullable: true),
                    AbsX = table.Column<int>(type: "int", nullable: true),
                    AbsY = table.Column<int>(type: "int", nullable: true),
                    AbsZ = table.Column<int>(type: "int", nullable: true),
                    SizeW = table.Column<int>(type: "int", nullable: true),
                    SizeH = table.Column<int>(type: "int", nullable: true),
                    SizeD = table.Column<int>(type: "int", nullable: true),
                    LoadLimit = table.Column<int>(type: "int", nullable: true),
                    Capacity = table.Column<int>(type: "int", nullable: true),
                    CapacityUom = table.Column<int>(type: "int", nullable: true),
                    Placed = table.Column<bool>(type: "bit", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    Creator = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CreateDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Modifier = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ModifyDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Space_Location", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Space_Marker",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FloorId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    X = table.Column<int>(type: "int", nullable: false),
                    Y = table.Column<int>(type: "int", nullable: false),
                    Z = table.Column<int>(type: "int", nullable: false),
                    MarkerType = table.Column<int>(type: "int", nullable: false),
                    Text = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    RefRackId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Creator = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CreateDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Modifier = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ModifyDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Space_Marker", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Space_Rack",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ZoneId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AisleId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    FloorId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TemplateId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RackCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    X = table.Column<int>(type: "int", nullable: false),
                    Y = table.Column<int>(type: "int", nullable: false),
                    Z = table.Column<int>(type: "int", nullable: false),
                    RotationZ = table.Column<double>(type: "float", nullable: false),
                    Cols = table.Column<int>(type: "int", nullable: false),
                    Levels = table.Column<int>(type: "int", nullable: false),
                    DepthCount = table.Column<int>(type: "int", nullable: false),
                    CellW = table.Column<int>(type: "int", nullable: false),
                    CellH = table.Column<int>(type: "int", nullable: false),
                    CellD = table.Column<int>(type: "int", nullable: false),
                    Enable = table.Column<bool>(type: "bit", nullable: false),
                    Creator = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CreateDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Modifier = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ModifyDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Space_Rack", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Space_Site",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SiteCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    SiteName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Address = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Lng = table.Column<double>(type: "float", nullable: true),
                    Lat = table.Column<double>(type: "float", nullable: true),
                    Enable = table.Column<bool>(type: "bit", nullable: false),
                    Creator = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CreateDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Modifier = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ModifyDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Space_Site", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Space_Template",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TemplateCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    TemplateName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    TemplateType = table.Column<int>(type: "int", nullable: false),
                    Params = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Creator = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CreateDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Modifier = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ModifyDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Space_Template", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Space_Zone",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FloorId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ZoneCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    ZoneName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ZoneType = table.Column<int>(type: "int", nullable: false),
                    Polygon = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Color = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    Enable = table.Column<bool>(type: "bit", nullable: false),
                    Creator = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CreateDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Modifier = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ModifyDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Space_Zone", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Space_Aisle_TenantId_ZoneId",
                table: "Space_Aisle",
                columns: new[] { "TenantId", "ZoneId" });

            migrationBuilder.CreateIndex(
                name: "IX_Space_Aisle_TenantId_ZoneId_AisleCode",
                table: "Space_Aisle",
                columns: new[] { "TenantId", "ZoneId", "AisleCode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Space_CodeRule_TenantId_ScopeType_ScopeId",
                table: "Space_CodeRule",
                columns: new[] { "TenantId", "ScopeType", "ScopeId" });

            migrationBuilder.CreateIndex(
                name: "IX_Space_Floor_TenantId_SiteId",
                table: "Space_Floor",
                columns: new[] { "TenantId", "SiteId" });

            migrationBuilder.CreateIndex(
                name: "IX_Space_Floor_TenantId_SiteId_FloorCode",
                table: "Space_Floor",
                columns: new[] { "TenantId", "SiteId", "FloorCode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Space_Location_TenantId_FloorId",
                table: "Space_Location",
                columns: new[] { "TenantId", "FloorId" });

            migrationBuilder.CreateIndex(
                name: "IX_Space_Location_TenantId_LocationCode",
                table: "Space_Location",
                columns: new[] { "TenantId", "LocationCode" },
                unique: true,
                filter: "[LocationCode] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Space_Location_TenantId_RackId",
                table: "Space_Location",
                columns: new[] { "TenantId", "RackId" });

            migrationBuilder.CreateIndex(
                name: "IX_Space_Location_TenantId_Status",
                table: "Space_Location",
                columns: new[] { "TenantId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_Space_Marker_TenantId_FloorId",
                table: "Space_Marker",
                columns: new[] { "TenantId", "FloorId" });

            migrationBuilder.CreateIndex(
                name: "IX_Space_Rack_TenantId_AisleId",
                table: "Space_Rack",
                columns: new[] { "TenantId", "AisleId" });

            migrationBuilder.CreateIndex(
                name: "IX_Space_Rack_TenantId_FloorId",
                table: "Space_Rack",
                columns: new[] { "TenantId", "FloorId" });

            migrationBuilder.CreateIndex(
                name: "IX_Space_Rack_TenantId_ZoneId",
                table: "Space_Rack",
                columns: new[] { "TenantId", "ZoneId" });

            migrationBuilder.CreateIndex(
                name: "IX_Space_Rack_TenantId_ZoneId_RackCode",
                table: "Space_Rack",
                columns: new[] { "TenantId", "ZoneId", "RackCode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Space_Site_TenantId_SiteCode",
                table: "Space_Site",
                columns: new[] { "TenantId", "SiteCode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Space_Template_TenantId_TemplateCode",
                table: "Space_Template",
                columns: new[] { "TenantId", "TemplateCode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Space_Zone_TenantId_FloorId",
                table: "Space_Zone",
                columns: new[] { "TenantId", "FloorId" });

            migrationBuilder.CreateIndex(
                name: "IX_Space_Zone_TenantId_FloorId_ZoneCode",
                table: "Space_Zone",
                columns: new[] { "TenantId", "FloorId", "ZoneCode" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Space_Aisle");

            migrationBuilder.DropTable(
                name: "Space_CodeRule");

            migrationBuilder.DropTable(
                name: "Space_Floor");

            migrationBuilder.DropTable(
                name: "Space_Location");

            migrationBuilder.DropTable(
                name: "Space_Marker");

            migrationBuilder.DropTable(
                name: "Space_Rack");

            migrationBuilder.DropTable(
                name: "Space_Site");

            migrationBuilder.DropTable(
                name: "Space_Template");

            migrationBuilder.DropTable(
                name: "Space_Zone");
        }
    }
}
