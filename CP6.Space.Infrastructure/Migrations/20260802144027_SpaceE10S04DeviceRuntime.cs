using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CP6.Space.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SpaceE10S04DeviceRuntime : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Space_DeviceAlarmState",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SiteId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SourceId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    SourceKind = table.Column<short>(type: "smallint", nullable: false),
                    DeviceExternalId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    DeviceMappingId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AlarmExternalId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    AlarmCode = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    AlarmSeverity = table.Column<short>(type: "smallint", nullable: true),
                    AlarmMessage = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    OccurredAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ReceivedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    SourceSequence = table.Column<long>(type: "bigint", nullable: true),
                    SourceEventId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    EventId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
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
                    table.PrimaryKey("PK_Space_DeviceAlarmState", x => x.Id);
                    table.UniqueConstraint("AK_Space_DeviceAlarmState_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.CheckConstraint("CK_Space_DeviceAlarmState_ActiveShape", "[IsActive] = 0 OR ([AlarmCode] IS NOT NULL AND [AlarmSeverity] IS NOT NULL)");
                    table.CheckConstraint("CK_Space_DeviceAlarmState_Severity", "[AlarmSeverity] IS NULL OR [AlarmSeverity] BETWEEN 0 AND 2");
                    table.CheckConstraint("CK_Space_DeviceAlarmState_SourceKind", "[SourceKind] IN (0, 1)");
                    table.CheckConstraint("CK_Space_DeviceAlarmState_SourceSequence", "[SourceSequence] IS NULL OR [SourceSequence] >= 0");
                    table.ForeignKey(
                        name: "FK_Space_DeviceAlarmState_Mapping_Tenant",
                        columns: x => new { x.TenantId, x.DeviceMappingId },
                        principalTable: "Space_DeviceMapping",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Space_DeviceState",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SiteId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SourceId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    SourceKind = table.Column<short>(type: "smallint", nullable: false),
                    DeviceExternalId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    DeviceMappingId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FloorLogicalId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    LocationLogicalId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    XMillimeters = table.Column<decimal>(type: "decimal(18,3)", nullable: true),
                    YMillimeters = table.Column<decimal>(type: "decimal(18,3)", nullable: true),
                    ZMillimeters = table.Column<decimal>(type: "decimal(18,3)", nullable: true),
                    AccuracyMillimeters = table.Column<decimal>(type: "decimal(18,3)", nullable: true),
                    PositionOccurredAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    PositionReceivedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    PositionSourceSequence = table.Column<long>(type: "bigint", nullable: true),
                    PositionSourceEventId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    PositionEventId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    OperatingState = table.Column<short>(type: "smallint", nullable: false),
                    OperatingStateOccurredAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    OperatingStateReceivedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    OperatingStateSourceSequence = table.Column<long>(type: "bigint", nullable: true),
                    OperatingStateSourceEventId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    OperatingStateEventId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
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
                    table.PrimaryKey("PK_Space_DeviceState", x => x.Id);
                    table.UniqueConstraint("AK_Space_DeviceState_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.CheckConstraint("CK_Space_DeviceState_Accuracy", "[AccuracyMillimeters] IS NULL OR ([AccuracyMillimeters] >= 0 AND [XMillimeters] IS NOT NULL)");
                    table.CheckConstraint("CK_Space_DeviceState_CoordinateTriple", "([XMillimeters] IS NULL AND [YMillimeters] IS NULL AND [ZMillimeters] IS NULL) OR ([XMillimeters] IS NOT NULL AND [YMillimeters] IS NOT NULL AND [ZMillimeters] IS NOT NULL)");
                    table.CheckConstraint("CK_Space_DeviceState_OperatingState", "[OperatingState] BETWEEN 0 AND 6");
                    table.CheckConstraint("CK_Space_DeviceState_SourceKind", "[SourceKind] IN (0, 1)");
                    table.ForeignKey(
                        name: "FK_Space_DeviceState_Mapping_Tenant",
                        columns: x => new { x.TenantId, x.DeviceMappingId },
                        principalTable: "Space_DeviceMapping",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Space_DeviceAlarmState_Tenant_Site_Active_Severity_Time",
                table: "Space_DeviceAlarmState",
                columns: new[] { "TenantId", "SiteId", "IsActive", "AlarmSeverity", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Space_DeviceAlarmState_TenantId_DeviceMappingId",
                table: "Space_DeviceAlarmState",
                columns: new[] { "TenantId", "DeviceMappingId" });

            migrationBuilder.CreateIndex(
                name: "UX_Space_DeviceAlarmState_Tenant_Site_Source_Device_Alarm",
                table: "Space_DeviceAlarmState",
                columns: new[] { "TenantId", "SiteId", "SourceId", "DeviceExternalId", "AlarmExternalId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Space_DeviceState_Tenant_Site_State_Time",
                table: "Space_DeviceState",
                columns: new[] { "TenantId", "SiteId", "OperatingState", "OperatingStateOccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Space_DeviceState_TenantId_DeviceMappingId",
                table: "Space_DeviceState",
                columns: new[] { "TenantId", "DeviceMappingId" });

            migrationBuilder.CreateIndex(
                name: "UX_Space_DeviceState_Tenant_Site_Source_Device",
                table: "Space_DeviceState",
                columns: new[] { "TenantId", "SiteId", "SourceId", "DeviceExternalId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Space_DeviceAlarmState");

            migrationBuilder.DropTable(
                name: "Space_DeviceState");
        }
    }
}
