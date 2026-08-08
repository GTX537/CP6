using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CP6.Core.Migrations
{
    /// <inheritdoc />
    public partial class ProductionMoveSerialLpnV2 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "SerialTrackingLockedAt",
                table: "T_ProductMaster",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TrackingMode",
                table: "T_ProductMaster",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "AreaCd",
                table: "T_MobileTask",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ContractVersion",
                table: "T_MobileTask",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "DueAt",
                table: "T_MobileTask",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExceptionDescription",
                table: "T_MobileTask",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExceptionReasonCd",
                table: "T_MobileTask",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ExecutionId",
                table: "T_MobileTask",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ExecutionVersion",
                table: "T_MobileTask",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "LastDeviceId",
                table: "T_MobileTask",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ParentTaskNo",
                table: "T_MobileTask",
                type: "nvarchar(25)",
                maxLength: 25,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PartialReason",
                table: "T_MobileTask",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PauseReason",
                table: "T_MobileTask",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "PlannedStartAt",
                table: "T_MobileTask",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RemainderTaskNo",
                table: "T_MobileTask",
                type: "nvarchar(25)",
                maxLength: 25,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ReservedSourceQty",
                table: "T_MobileTask",
                type: "decimal(21,8)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "ReservedTargetCapacityQty",
                table: "T_MobileTask",
                type: "decimal(21,8)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "AreaCd",
                table: "T_Location",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ReservedCapacityQty",
                table: "T_Location",
                type: "decimal(21,8)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "BadgeNo",
                table: "Sys_Users",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "QuickPinHash",
                table: "Sys_Users",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "T_BarcodeAlias",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Barcode = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    BarcodeType = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    TargetKey = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    ProductCd = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    LotNo = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    LocationCd = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    PackageUnitCd = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    ConversionRate = table.Column<decimal>(type: "decimal(21,8)", nullable: false),
                    ValidFrom = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ValidUntil = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsEnabled = table.Column<bool>(type: "bit", nullable: false),
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
                    table.PrimaryKey("PK_T_BarcodeAlias", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "T_BarcodeProfile",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProfileName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Format = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Pattern = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    MappingJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Priority = table.Column<int>(type: "int", nullable: false),
                    IsEnabled = table.Column<bool>(type: "bit", nullable: false),
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
                    table.PrimaryKey("PK_T_BarcodeProfile", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "T_ClientDevice",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DeviceId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    DeviceMode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Platform = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    PublicKey = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: false),
                    WarehouseCd = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    AreaCd = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    AppVersion = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    PlatformVersion = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    ActivatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ActivatedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    LastSeenAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    BatteryPercent = table.Column<int>(type: "int", nullable: true),
                    NetworkType = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    CurrentUser = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CurrentTaskNo = table.Column<string>(type: "nvarchar(25)", maxLength: 25, nullable: true),
                    FullAuthExpiresAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    QuickSwitchFailureCount = table.Column<int>(type: "int", nullable: false),
                    DisabledAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DisabledBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
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
                    table.PrimaryKey("PK_T_ClientDevice", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "T_DeviceActivation",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TokenHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Platform = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    DeviceMode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    WarehouseCd = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    AreaCd = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    ExpiresAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ConsumedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ConsumedByDeviceId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
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
                    table.PrimaryKey("PK_T_DeviceActivation", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "T_LabelJob",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    JobNo = table.Column<string>(type: "nvarchar(25)", maxLength: 25, nullable: false),
                    OperationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TemplateName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    WarehouseCd = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    PrinterName = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    PayloadJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    RequestedDeviceId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    RequestedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    RequestedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    AttemptCount = table.Column<int>(type: "int", nullable: false),
                    ResultMessage = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
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
                    table.PrimaryKey("PK_T_LabelJob", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "T_LabelTemplate",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TemplateName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Format = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    TemplateBody = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Language = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    IsEnabled = table.Column<bool>(type: "bit", nullable: false),
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
                    table.PrimaryKey("PK_T_LabelTemplate", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "T_LogisticsUnit",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LpnNo = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ContainerType = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    WarehouseCd = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    LocationCd = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    ParentLpnNo = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
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
                    table.PrimaryKey("PK_T_LogisticsUnit", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "T_LpnClosure",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AncestorLpnNo = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    DescendantLpnNo = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Depth = table.Column<int>(type: "int", nullable: false),
                    Creator = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CreateDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Modifier = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ModifyDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_T_LpnClosure", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "T_LpnContent",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LpnNo = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ProductCd = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    LotNo = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    SerialNo = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    Qty = table.Column<decimal>(type: "decimal(21,8)", nullable: false),
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
                    table.PrimaryKey("PK_T_LpnContent", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "T_LpnEvent",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LpnNo = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    OperationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EventType = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    UserName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    DeviceId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    OccurredAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DataJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Creator = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CreateDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Modifier = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ModifyDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_T_LpnEvent", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "T_LpnPolicy",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WarehouseCd = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    ContainerType = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    AllowMixedProducts = table.Column<bool>(type: "bit", nullable: false),
                    AllowMixedLots = table.Column<bool>(type: "bit", nullable: false),
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
                    table.PrimaryKey("PK_T_LpnPolicy", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "T_MobileTaskEvent",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TaskNo = table.Column<string>(type: "nvarchar(25)", maxLength: 25, nullable: false),
                    EventType = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    OperationId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ExecutionVersion = table.Column<int>(type: "int", nullable: false),
                    UserName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    DeviceId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    OccurredAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DataJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Creator = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CreateDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Modifier = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ModifyDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_T_MobileTaskEvent", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "T_MobileTaskReservation",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TaskNo = table.Column<string>(type: "nvarchar(25)", maxLength: 25, nullable: false),
                    WarehouseCd = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    FromLocationCd = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    ToLocationCd = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    ProductCd = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    LotNo = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    ReservedQty = table.Column<decimal>(type: "decimal(21,8)", nullable: false),
                    ConsumedQty = table.Column<decimal>(type: "decimal(21,8)", nullable: false),
                    ReleasedQty = table.Column<decimal>(type: "decimal(21,8)", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
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
                    table.PrimaryKey("PK_T_MobileTaskReservation", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "T_MobileTaskScanLog",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TaskNo = table.Column<string>(type: "nvarchar(25)", maxLength: 25, nullable: false),
                    ClientScanNo = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ExecutionVersion = table.Column<int>(type: "int", nullable: false),
                    Step = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    RawBarcode = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                    DeviceId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    UserName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ScannedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ParsedKind = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    ParsedValue = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    Matched = table.Column<bool>(type: "bit", nullable: false),
                    FailureCode = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    RetainUntil = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Creator = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CreateDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Modifier = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ModifyDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_T_MobileTaskScanLog", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "T_StockSerial",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProductCd = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    SerialNo = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    WarehouseCd = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    LocationCd = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    LotNo = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    LpnNo = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    LastTxnNo = table.Column<string>(type: "nvarchar(25)", maxLength: 25, nullable: true),
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
                    table.PrimaryKey("PK_T_StockSerial", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "T_StockSerialTransaction",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TxnNo = table.Column<string>(type: "nvarchar(25)", maxLength: 25, nullable: false),
                    OperationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TxnType = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ProductCd = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    SerialNo = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    WarehouseCd = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    LotNo = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    FromLocationCd = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    ToLocationCd = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    LpnNo = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    OperatorCd = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    DeviceId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    OccurredAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Creator = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CreateDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Modifier = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ModifyDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_T_StockSerialTransaction", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "T_TaskCommandReceipt",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OperationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TaskNo = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    CommandName = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    ResultJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Creator = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CreateDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Modifier = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ModifyDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_T_TaskCommandReceipt", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "T_WmsFeatureFlag",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WarehouseCd = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    ProductionMoveEnabled = table.Column<bool>(type: "bit", nullable: false),
                    SerialLpnEnabled = table.Column<bool>(type: "bit", nullable: false),
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
                    table.PrimaryKey("PK_T_WmsFeatureFlag", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_T_MobileTask_ContractVersion_WarehouseCd_AreaCd_Status",
                table: "T_MobileTask",
                columns: new[] { "ContractVersion", "WarehouseCd", "AreaCd", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_T_MobileTask_DueAt",
                table: "T_MobileTask",
                column: "DueAt");

            migrationBuilder.CreateIndex(
                name: "IX_T_MobileTask_ParentTaskNo",
                table: "T_MobileTask",
                column: "ParentTaskNo");

            migrationBuilder.CreateIndex(
                name: "IX_Sys_Users_BadgeNo",
                table: "Sys_Users",
                columns: new[] { "TenantId", "BadgeNo" },
                unique: true,
                filter: "[BadgeNo] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_T_BarcodeAlias_Barcode",
                table: "T_BarcodeAlias",
                columns: new[] { "TenantId", "Barcode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_T_BarcodeAlias_BarcodeType_TargetKey",
                table: "T_BarcodeAlias",
                columns: new[] { "BarcodeType", "TargetKey" });

            migrationBuilder.CreateIndex(
                name: "IX_T_BarcodeAlias_ProductCd_LotNo",
                table: "T_BarcodeAlias",
                columns: new[] { "ProductCd", "LotNo" });

            migrationBuilder.CreateIndex(
                name: "IX_T_BarcodeProfile_IsEnabled_Priority",
                table: "T_BarcodeProfile",
                columns: new[] { "IsEnabled", "Priority" });

            migrationBuilder.CreateIndex(
                name: "IX_T_BarcodeProfile_ProfileName",
                table: "T_BarcodeProfile",
                columns: new[] { "TenantId", "ProfileName" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_T_ClientDevice_DeviceId",
                table: "T_ClientDevice",
                columns: new[] { "TenantId", "DeviceId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_T_ClientDevice_LastSeenAt",
                table: "T_ClientDevice",
                column: "LastSeenAt");

            migrationBuilder.CreateIndex(
                name: "IX_T_ClientDevice_Status_WarehouseCd_AreaCd",
                table: "T_ClientDevice",
                columns: new[] { "Status", "WarehouseCd", "AreaCd" });

            migrationBuilder.CreateIndex(
                name: "IX_T_DeviceActivation_ExpiresAt_ConsumedAt",
                table: "T_DeviceActivation",
                columns: new[] { "ExpiresAt", "ConsumedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_T_DeviceActivation_TokenHash",
                table: "T_DeviceActivation",
                columns: new[] { "TenantId", "TokenHash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_T_LabelJob_JobNo",
                table: "T_LabelJob",
                columns: new[] { "TenantId", "JobNo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_T_LabelJob_OperationId",
                table: "T_LabelJob",
                columns: new[] { "TenantId", "OperationId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_T_LabelJob_WarehouseCd_Status_RequestedAt",
                table: "T_LabelJob",
                columns: new[] { "WarehouseCd", "Status", "RequestedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_T_LabelTemplate_TemplateName",
                table: "T_LabelTemplate",
                columns: new[] { "TenantId", "TemplateName" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_T_LogisticsUnit_LpnNo",
                table: "T_LogisticsUnit",
                columns: new[] { "TenantId", "LpnNo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_T_LogisticsUnit_ParentLpnNo",
                table: "T_LogisticsUnit",
                column: "ParentLpnNo");

            migrationBuilder.CreateIndex(
                name: "IX_T_LogisticsUnit_WarehouseCd_LocationCd_Status",
                table: "T_LogisticsUnit",
                columns: new[] { "WarehouseCd", "LocationCd", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_T_LpnClosure_AncestorLpnNo_DescendantLpnNo",
                table: "T_LpnClosure",
                columns: new[] { "TenantId", "AncestorLpnNo", "DescendantLpnNo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_T_LpnClosure_DescendantLpnNo_Depth",
                table: "T_LpnClosure",
                columns: new[] { "DescendantLpnNo", "Depth" });

            migrationBuilder.CreateIndex(
                name: "IX_T_LpnContent_LpnNo_ProductCd_LotNo_SerialNo",
                table: "T_LpnContent",
                columns: new[] { "TenantId", "LpnNo", "ProductCd", "LotNo", "SerialNo" },
                unique: true,
                filter: "[SerialNo] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_T_LpnContent_SerialNo",
                table: "T_LpnContent",
                columns: new[] { "TenantId", "SerialNo" },
                unique: true,
                filter: "[SerialNo] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_T_LpnEvent_LpnNo_OccurredAt",
                table: "T_LpnEvent",
                columns: new[] { "LpnNo", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_T_LpnEvent_OperationId",
                table: "T_LpnEvent",
                column: "OperationId");

            migrationBuilder.CreateIndex(
                name: "IX_T_LpnPolicy_WarehouseCd_ContainerType",
                table: "T_LpnPolicy",
                columns: new[] { "TenantId", "WarehouseCd", "ContainerType" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_T_MobileTaskEvent_OperationId",
                table: "T_MobileTaskEvent",
                column: "OperationId");

            migrationBuilder.CreateIndex(
                name: "IX_T_MobileTaskEvent_TaskNo_OccurredAt",
                table: "T_MobileTaskEvent",
                columns: new[] { "TaskNo", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_T_MobileTaskReservation_TaskNo",
                table: "T_MobileTaskReservation",
                columns: new[] { "TenantId", "TaskNo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_T_MobileTaskReservation_WarehouseCd_FromLocationCd_ProductCd_LotNo_IsActive",
                table: "T_MobileTaskReservation",
                columns: new[] { "WarehouseCd", "FromLocationCd", "ProductCd", "LotNo", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_T_MobileTaskScanLog_ClientScanNo",
                table: "T_MobileTaskScanLog",
                columns: new[] { "TenantId", "ClientScanNo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_T_MobileTaskScanLog_RetainUntil",
                table: "T_MobileTaskScanLog",
                column: "RetainUntil");

            migrationBuilder.CreateIndex(
                name: "IX_T_MobileTaskScanLog_TaskNo_ExecutionVersion_ScannedAt",
                table: "T_MobileTaskScanLog",
                columns: new[] { "TaskNo", "ExecutionVersion", "ScannedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_T_StockSerial_LpnNo",
                table: "T_StockSerial",
                column: "LpnNo");

            migrationBuilder.CreateIndex(
                name: "IX_T_StockSerial_ProductCd_SerialNo",
                table: "T_StockSerial",
                columns: new[] { "TenantId", "ProductCd", "SerialNo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_T_StockSerial_WarehouseCd_LocationCd_ProductCd_LotNo",
                table: "T_StockSerial",
                columns: new[] { "WarehouseCd", "LocationCd", "ProductCd", "LotNo" });

            migrationBuilder.CreateIndex(
                name: "IX_T_StockSerialTransaction_OperationId",
                table: "T_StockSerialTransaction",
                column: "OperationId");

            migrationBuilder.CreateIndex(
                name: "IX_T_StockSerialTransaction_ProductCd_SerialNo_OccurredAt",
                table: "T_StockSerialTransaction",
                columns: new[] { "ProductCd", "SerialNo", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_T_StockSerialTransaction_TxnNo",
                table: "T_StockSerialTransaction",
                columns: new[] { "TenantId", "TxnNo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_T_TaskCommandReceipt_OperationId",
                table: "T_TaskCommandReceipt",
                columns: new[] { "TenantId", "OperationId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_T_TaskCommandReceipt_TaskNo_CommandName",
                table: "T_TaskCommandReceipt",
                columns: new[] { "TaskNo", "CommandName" });

            migrationBuilder.CreateIndex(
                name: "IX_T_WmsFeatureFlag_WarehouseCd",
                table: "T_WmsFeatureFlag",
                columns: new[] { "TenantId", "WarehouseCd" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "T_BarcodeAlias");

            migrationBuilder.DropTable(
                name: "T_BarcodeProfile");

            migrationBuilder.DropTable(
                name: "T_ClientDevice");

            migrationBuilder.DropTable(
                name: "T_DeviceActivation");

            migrationBuilder.DropTable(
                name: "T_LabelJob");

            migrationBuilder.DropTable(
                name: "T_LabelTemplate");

            migrationBuilder.DropTable(
                name: "T_LogisticsUnit");

            migrationBuilder.DropTable(
                name: "T_LpnClosure");

            migrationBuilder.DropTable(
                name: "T_LpnContent");

            migrationBuilder.DropTable(
                name: "T_LpnEvent");

            migrationBuilder.DropTable(
                name: "T_LpnPolicy");

            migrationBuilder.DropTable(
                name: "T_MobileTaskEvent");

            migrationBuilder.DropTable(
                name: "T_MobileTaskReservation");

            migrationBuilder.DropTable(
                name: "T_MobileTaskScanLog");

            migrationBuilder.DropTable(
                name: "T_StockSerial");

            migrationBuilder.DropTable(
                name: "T_StockSerialTransaction");

            migrationBuilder.DropTable(
                name: "T_TaskCommandReceipt");

            migrationBuilder.DropTable(
                name: "T_WmsFeatureFlag");

            migrationBuilder.DropIndex(
                name: "IX_T_MobileTask_ContractVersion_WarehouseCd_AreaCd_Status",
                table: "T_MobileTask");

            migrationBuilder.DropIndex(
                name: "IX_T_MobileTask_DueAt",
                table: "T_MobileTask");

            migrationBuilder.DropIndex(
                name: "IX_T_MobileTask_ParentTaskNo",
                table: "T_MobileTask");

            migrationBuilder.DropIndex(
                name: "IX_Sys_Users_BadgeNo",
                table: "Sys_Users");

            migrationBuilder.DropColumn(
                name: "SerialTrackingLockedAt",
                table: "T_ProductMaster");

            migrationBuilder.DropColumn(
                name: "TrackingMode",
                table: "T_ProductMaster");

            migrationBuilder.DropColumn(
                name: "AreaCd",
                table: "T_MobileTask");

            migrationBuilder.DropColumn(
                name: "ContractVersion",
                table: "T_MobileTask");

            migrationBuilder.DropColumn(
                name: "DueAt",
                table: "T_MobileTask");

            migrationBuilder.DropColumn(
                name: "ExceptionDescription",
                table: "T_MobileTask");

            migrationBuilder.DropColumn(
                name: "ExceptionReasonCd",
                table: "T_MobileTask");

            migrationBuilder.DropColumn(
                name: "ExecutionId",
                table: "T_MobileTask");

            migrationBuilder.DropColumn(
                name: "ExecutionVersion",
                table: "T_MobileTask");

            migrationBuilder.DropColumn(
                name: "LastDeviceId",
                table: "T_MobileTask");

            migrationBuilder.DropColumn(
                name: "ParentTaskNo",
                table: "T_MobileTask");

            migrationBuilder.DropColumn(
                name: "PartialReason",
                table: "T_MobileTask");

            migrationBuilder.DropColumn(
                name: "PauseReason",
                table: "T_MobileTask");

            migrationBuilder.DropColumn(
                name: "PlannedStartAt",
                table: "T_MobileTask");

            migrationBuilder.DropColumn(
                name: "RemainderTaskNo",
                table: "T_MobileTask");

            migrationBuilder.DropColumn(
                name: "ReservedSourceQty",
                table: "T_MobileTask");

            migrationBuilder.DropColumn(
                name: "ReservedTargetCapacityQty",
                table: "T_MobileTask");

            migrationBuilder.DropColumn(
                name: "AreaCd",
                table: "T_Location");

            migrationBuilder.DropColumn(
                name: "ReservedCapacityQty",
                table: "T_Location");

            migrationBuilder.DropColumn(
                name: "BadgeNo",
                table: "Sys_Users");

            migrationBuilder.DropColumn(
                name: "QuickPinHash",
                table: "Sys_Users");
        }
    }
}
