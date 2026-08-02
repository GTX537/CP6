using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CP6.Space.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SpaceE10S01PersonnelEvents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Space_PersonnelEvent",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SiteId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SourceId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    SourceKind = table.Column<short>(type: "smallint", nullable: false),
                    SourceEventId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    PersonExternalId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    EventKind = table.Column<short>(type: "smallint", nullable: false),
                    WorkState = table.Column<short>(type: "smallint", nullable: true),
                    FloorLogicalId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    LocationLogicalId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    XMillimeters = table.Column<decimal>(type: "decimal(18,3)", nullable: true),
                    YMillimeters = table.Column<decimal>(type: "decimal(18,3)", nullable: true),
                    ZMillimeters = table.Column<decimal>(type: "decimal(18,3)", nullable: true),
                    AccuracyMillimeters = table.Column<decimal>(type: "decimal(18,3)", nullable: true),
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
                    table.PrimaryKey("PK_Space_PersonnelEvent", x => x.Id);
                    table.UniqueConstraint("AK_Space_PersonnelEvent_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.CheckConstraint("CK_Space_PersonnelEvent_Accuracy", "[AccuracyMillimeters] IS NULL OR ([AccuracyMillimeters] >= 0 AND [XMillimeters] IS NOT NULL AND [YMillimeters] IS NOT NULL AND [ZMillimeters] IS NOT NULL)");
                    table.CheckConstraint("CK_Space_PersonnelEvent_Kind", "[EventKind] IN (0, 1)");
                    table.CheckConstraint("CK_Space_PersonnelEvent_Shape", "([EventKind] = 0 AND [WorkState] IS NULL AND ([LocationLogicalId] IS NOT NULL OR ([FloorLogicalId] IS NOT NULL AND [XMillimeters] IS NOT NULL AND [YMillimeters] IS NOT NULL AND [ZMillimeters] IS NOT NULL))) OR ([EventKind] = 1 AND [WorkState] IS NOT NULL AND [FloorLogicalId] IS NULL AND [LocationLogicalId] IS NULL AND [XMillimeters] IS NULL AND [YMillimeters] IS NULL AND [ZMillimeters] IS NULL AND [AccuracyMillimeters] IS NULL)");
                    table.CheckConstraint("CK_Space_PersonnelEvent_SourceKind", "[SourceKind] IN (0, 1)");
                    table.CheckConstraint("CK_Space_PersonnelEvent_SourceSequence", "[SourceSequence] IS NULL OR [SourceSequence] >= 0");
                    table.CheckConstraint("CK_Space_PersonnelEvent_WorkState", "[WorkState] IS NULL OR [WorkState] BETWEEN 0 AND 4");
                });

            migrationBuilder.CreateTable(
                name: "Space_PersonnelState",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SiteId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SourceId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    SourceKind = table.Column<short>(type: "smallint", nullable: false),
                    PersonExternalId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
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
                    WorkState = table.Column<short>(type: "smallint", nullable: false),
                    WorkStateOccurredAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    WorkStateReceivedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    WorkStateSourceSequence = table.Column<long>(type: "bigint", nullable: true),
                    WorkStateSourceEventId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    WorkStateEventId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
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
                    table.PrimaryKey("PK_Space_PersonnelState", x => x.Id);
                    table.UniqueConstraint("AK_Space_PersonnelState_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.CheckConstraint("CK_Space_PersonnelState_SourceKind", "[SourceKind] IN (0, 1)");
                    table.CheckConstraint("CK_Space_PersonnelState_WorkState", "[WorkState] BETWEEN 0 AND 4");
                });

            migrationBuilder.CreateIndex(
                name: "IX_Space_PersonnelEvent_Tenant_Site_Source_Person_Time",
                table: "Space_PersonnelEvent",
                columns: new[] { "TenantId", "SiteId", "SourceId", "PersonExternalId", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "UX_Space_PersonnelEvent_Tenant_Site_Source_Event",
                table: "Space_PersonnelEvent",
                columns: new[] { "TenantId", "SiteId", "SourceId", "SourceEventId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Space_PersonnelState_Tenant_Site_WorkState_Time",
                table: "Space_PersonnelState",
                columns: new[] { "TenantId", "SiteId", "WorkState", "WorkStateOccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "UX_Space_PersonnelState_Tenant_Site_Source_Person",
                table: "Space_PersonnelState",
                columns: new[] { "TenantId", "SiteId", "SourceId", "PersonExternalId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Space_PersonnelEvent");

            migrationBuilder.DropTable(
                name: "Space_PersonnelState");
        }
    }
}
