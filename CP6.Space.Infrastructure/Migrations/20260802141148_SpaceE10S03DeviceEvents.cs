using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CP6.Space.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SpaceE10S03DeviceEvents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Space_DeviceMapping",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SiteId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SourceId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    SourceKind = table.Column<short>(type: "smallint", nullable: false),
                    DeviceExternalId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    DeviceKind = table.Column<short>(type: "smallint", nullable: false),
                    ElementLogicalId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ElementType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    ValidatedModelVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ValidatedFloorLogicalId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
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
                    table.PrimaryKey("PK_Space_DeviceMapping", x => x.Id);
                    table.UniqueConstraint("AK_Space_DeviceMapping_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.CheckConstraint("CK_Space_DeviceMapping_DeviceKind", "[DeviceKind] BETWEEN 0 AND 7");
                    table.CheckConstraint("CK_Space_DeviceMapping_SourceKind", "[SourceKind] IN (0, 1)");
                    table.ForeignKey(
                        name: "FK_Space_DeviceMapping_Element_Tenant_Version_Logical",
                        columns: x => new { x.TenantId, x.ValidatedModelVersionId, x.ElementLogicalId },
                        principalTable: "Space_ElementRevision",
                        principalColumns: new[] { "TenantId", "ModelVersionId", "LogicalId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Space_DeviceMapping_ModelVersion_Tenant",
                        columns: x => new { x.TenantId, x.ValidatedModelVersionId },
                        principalTable: "Space_ModelVersion",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Space_DeviceEvent",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SiteId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SourceId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    SourceKind = table.Column<short>(type: "smallint", nullable: false),
                    SourceEventId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    DeviceMappingId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DeviceExternalId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    DeviceKind = table.Column<short>(type: "smallint", nullable: false),
                    ElementLogicalId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EventKind = table.Column<short>(type: "smallint", nullable: false),
                    OperatingState = table.Column<short>(type: "smallint", nullable: true),
                    FloorLogicalId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    LocationLogicalId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    XMillimeters = table.Column<decimal>(type: "decimal(18,3)", nullable: true),
                    YMillimeters = table.Column<decimal>(type: "decimal(18,3)", nullable: true),
                    ZMillimeters = table.Column<decimal>(type: "decimal(18,3)", nullable: true),
                    AccuracyMillimeters = table.Column<decimal>(type: "decimal(18,3)", nullable: true),
                    AlarmExternalId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    AlarmCode = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    AlarmSeverity = table.Column<short>(type: "smallint", nullable: true),
                    AlarmMessage = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    SourceSequence = table.Column<long>(type: "bigint", nullable: true),
                    OccurredAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ReceivedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
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
                    table.PrimaryKey("PK_Space_DeviceEvent", x => x.Id);
                    table.UniqueConstraint("AK_Space_DeviceEvent_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.CheckConstraint("CK_Space_DeviceEvent_Accuracy", "[AccuracyMillimeters] IS NULL OR ([AccuracyMillimeters] >= 0 AND [XMillimeters] IS NOT NULL)");
                    table.CheckConstraint("CK_Space_DeviceEvent_AlarmSeverity", "[AlarmSeverity] IS NULL OR [AlarmSeverity] BETWEEN 0 AND 2");
                    table.CheckConstraint("CK_Space_DeviceEvent_CoordinateTriple", "([XMillimeters] IS NULL AND [YMillimeters] IS NULL AND [ZMillimeters] IS NULL) OR ([XMillimeters] IS NOT NULL AND [YMillimeters] IS NOT NULL AND [ZMillimeters] IS NOT NULL)");
                    table.CheckConstraint("CK_Space_DeviceEvent_DeviceKind", "[DeviceKind] BETWEEN 0 AND 7");
                    table.CheckConstraint("CK_Space_DeviceEvent_Kind", "[EventKind] BETWEEN 0 AND 3");
                    table.CheckConstraint("CK_Space_DeviceEvent_OperatingState", "[OperatingState] IS NULL OR [OperatingState] BETWEEN 0 AND 6");
                    table.CheckConstraint("CK_Space_DeviceEvent_Shape", "([EventKind] = 0 AND [OperatingState] IS NULL AND [AlarmExternalId] IS NULL AND [AlarmCode] IS NULL AND [AlarmSeverity] IS NULL AND [AlarmMessage] IS NULL AND ([LocationLogicalId] IS NOT NULL OR ([FloorLogicalId] IS NOT NULL AND [XMillimeters] IS NOT NULL))) OR ([EventKind] = 1 AND [OperatingState] IS NOT NULL AND [FloorLogicalId] IS NULL AND [LocationLogicalId] IS NULL AND [XMillimeters] IS NULL AND [YMillimeters] IS NULL AND [ZMillimeters] IS NULL AND [AccuracyMillimeters] IS NULL AND [AlarmExternalId] IS NULL AND [AlarmCode] IS NULL AND [AlarmSeverity] IS NULL AND [AlarmMessage] IS NULL) OR ([EventKind] = 2 AND [OperatingState] IS NULL AND [FloorLogicalId] IS NULL AND [LocationLogicalId] IS NULL AND [XMillimeters] IS NULL AND [YMillimeters] IS NULL AND [ZMillimeters] IS NULL AND [AccuracyMillimeters] IS NULL AND [AlarmExternalId] IS NOT NULL AND [AlarmCode] IS NOT NULL AND [AlarmSeverity] IS NOT NULL) OR ([EventKind] = 3 AND [OperatingState] IS NULL AND [FloorLogicalId] IS NULL AND [LocationLogicalId] IS NULL AND [XMillimeters] IS NULL AND [YMillimeters] IS NULL AND [ZMillimeters] IS NULL AND [AccuracyMillimeters] IS NULL AND [AlarmExternalId] IS NOT NULL AND [AlarmCode] IS NULL AND [AlarmSeverity] IS NULL AND [AlarmMessage] IS NULL)");
                    table.CheckConstraint("CK_Space_DeviceEvent_SourceKind", "[SourceKind] IN (0, 1)");
                    table.CheckConstraint("CK_Space_DeviceEvent_SourceSequence", "[SourceSequence] IS NULL OR [SourceSequence] >= 0");
                    table.ForeignKey(
                        name: "FK_Space_DeviceEvent_Mapping_Tenant",
                        columns: x => new { x.TenantId, x.DeviceMappingId },
                        principalTable: "Space_DeviceMapping",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Space_DeviceEvent_Tenant_Site_Alarm_Time",
                table: "Space_DeviceEvent",
                columns: new[] { "TenantId", "SiteId", "AlarmExternalId", "OccurredAtUtc" },
                filter: "[AlarmExternalId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Space_DeviceEvent_Tenant_Site_Source_Device_Time",
                table: "Space_DeviceEvent",
                columns: new[] { "TenantId", "SiteId", "SourceId", "DeviceExternalId", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Space_DeviceEvent_TenantId_DeviceMappingId",
                table: "Space_DeviceEvent",
                columns: new[] { "TenantId", "DeviceMappingId" });

            migrationBuilder.CreateIndex(
                name: "UX_Space_DeviceEvent_Tenant_Site_Source_Event",
                table: "Space_DeviceEvent",
                columns: new[] { "TenantId", "SiteId", "SourceId", "SourceEventId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Space_DeviceMapping_TenantId_ValidatedModelVersionId_ElementLogicalId",
                table: "Space_DeviceMapping",
                columns: new[] { "TenantId", "ValidatedModelVersionId", "ElementLogicalId" });

            migrationBuilder.CreateIndex(
                name: "UX_Space_DeviceMapping_Tenant_Site_Source_Device",
                table: "Space_DeviceMapping",
                columns: new[] { "TenantId", "SiteId", "SourceId", "DeviceExternalId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_Space_DeviceMapping_Tenant_Site_Source_Element",
                table: "Space_DeviceMapping",
                columns: new[] { "TenantId", "SiteId", "SourceId", "ElementLogicalId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Space_DeviceEvent");

            migrationBuilder.DropTable(
                name: "Space_DeviceMapping");
        }
    }
}
