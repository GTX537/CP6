using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CP6.Core.Migrations
{
    /// <inheritdoc />
    public partial class AddWmsConnectivity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "T_CarrierShipment",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ShipmentNo = table.Column<string>(type: "nvarchar(25)", maxLength: 25, nullable: false),
                    PackageNo = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    CarrierCd = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    TrackingNo = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    ServiceType = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    CustomerCd = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    ShipToAddress = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ShipToName = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: true),
                    ShipToTel = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    WeightKg = table.Column<decimal>(type: "decimal(10,3)", nullable: true),
                    CarrierFee = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    PickedUpAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DeliveredAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    EventsJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ApiRefId = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Remarks = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Creator = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CreateDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Modifier = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ModifyDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_T_CarrierShipment", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "T_IotSensor",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SensorId = table.Column<string>(type: "nvarchar(25)", maxLength: 25, nullable: false),
                    SensorType = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    SensorName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    WarehouseCd = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    LocationCd = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    Unit = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    MinThreshold = table.Column<decimal>(type: "decimal(18,4)", nullable: true),
                    MaxThreshold = table.Column<decimal>(type: "decimal(18,4)", nullable: true),
                    IsEnabled = table.Column<bool>(type: "bit", nullable: false),
                    LastValue = table.Column<decimal>(type: "decimal(18,4)", nullable: true),
                    LastReadAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Remarks = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Creator = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CreateDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Modifier = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ModifyDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_T_IotSensor", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "T_IotSensorReading",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SensorId = table.Column<string>(type: "nvarchar(25)", maxLength: 25, nullable: false),
                    ReadAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Value = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    IsAlert = table.Column<bool>(type: "bit", nullable: false),
                    AlertMessage = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Creator = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CreateDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Modifier = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ModifyDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_T_IotSensorReading", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "T_WcsTask",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TaskNo = table.Column<string>(type: "nvarchar(25)", maxLength: 25, nullable: false),
                    TaskType = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    Priority = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    DeviceCd = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    RelatedNo = table.Column<string>(type: "nvarchar(25)", maxLength: 25, nullable: true),
                    RelatedType = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    FromWarehouseCd = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    FromLocationCd = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    ToWarehouseCd = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    ToLocationCd = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    ProductCd = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    LotNo = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    Qty = table.Column<decimal>(type: "decimal(21,8)", nullable: true),
                    UnitCd = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DispatchedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    StartedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ErrorMessage = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Remarks = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Creator = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CreateDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Modifier = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ModifyDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_T_WcsTask", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_T_CarrierShipment_CarrierCd_Status_IsDeleted",
                table: "T_CarrierShipment",
                columns: new[] { "CarrierCd", "Status", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_T_CarrierShipment_CustomerCd",
                table: "T_CarrierShipment",
                column: "CustomerCd");

            migrationBuilder.CreateIndex(
                name: "IX_T_CarrierShipment_PackageNo",
                table: "T_CarrierShipment",
                column: "PackageNo");

            migrationBuilder.CreateIndex(
                name: "IX_T_CarrierShipment_ShipmentNo",
                table: "T_CarrierShipment",
                column: "ShipmentNo",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_T_CarrierShipment_TrackingNo",
                table: "T_CarrierShipment",
                column: "TrackingNo");

            migrationBuilder.CreateIndex(
                name: "IX_T_IotSensor_SensorId",
                table: "T_IotSensor",
                column: "SensorId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_T_IotSensor_SensorType_IsEnabled",
                table: "T_IotSensor",
                columns: new[] { "SensorType", "IsEnabled" });

            migrationBuilder.CreateIndex(
                name: "IX_T_IotSensor_WarehouseCd_LocationCd",
                table: "T_IotSensor",
                columns: new[] { "WarehouseCd", "LocationCd" });

            migrationBuilder.CreateIndex(
                name: "IX_T_IotSensorReading_IsAlert",
                table: "T_IotSensorReading",
                column: "IsAlert");

            migrationBuilder.CreateIndex(
                name: "IX_T_IotSensorReading_SensorId_ReadAt",
                table: "T_IotSensorReading",
                columns: new[] { "SensorId", "ReadAt" });

            migrationBuilder.CreateIndex(
                name: "IX_T_WcsTask_CreatedAt",
                table: "T_WcsTask",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_T_WcsTask_DeviceCd",
                table: "T_WcsTask",
                column: "DeviceCd");

            migrationBuilder.CreateIndex(
                name: "IX_T_WcsTask_RelatedNo_RelatedType",
                table: "T_WcsTask",
                columns: new[] { "RelatedNo", "RelatedType" });

            migrationBuilder.CreateIndex(
                name: "IX_T_WcsTask_Status_Priority_IsDeleted",
                table: "T_WcsTask",
                columns: new[] { "Status", "Priority", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_T_WcsTask_TaskNo",
                table: "T_WcsTask",
                column: "TaskNo",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "T_CarrierShipment");

            migrationBuilder.DropTable(
                name: "T_IotSensor");

            migrationBuilder.DropTable(
                name: "T_IotSensorReading");

            migrationBuilder.DropTable(
                name: "T_WcsTask");
        }
    }
}
