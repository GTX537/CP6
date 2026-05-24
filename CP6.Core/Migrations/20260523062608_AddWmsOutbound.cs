using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CP6.Core.Migrations
{
    /// <inheritdoc />
    public partial class AddWmsOutbound : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "T_OutboundOrder",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OutboundNo = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    OutboundType = table.Column<int>(type: "int", nullable: false),
                    WorkOrderNo = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    WebOrderNo = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    CustomerCd = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    CustomerName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    WarehouseCd = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    PlannedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    Priority = table.Column<int>(type: "int", nullable: false),
                    ShipToAddress = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CarrierCd = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
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
                    table.PrimaryKey("PK_T_OutboundOrder", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "T_OutboundOrderDetail",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OutboundNo = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    LineNo = table.Column<int>(type: "int", nullable: false),
                    ProductCd = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ProductName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    RequiredQty = table.Column<decimal>(type: "decimal(21,8)", nullable: false),
                    AllocatedQty = table.Column<decimal>(type: "decimal(21,8)", nullable: false),
                    ShippedQty = table.Column<decimal>(type: "decimal(21,8)", nullable: false),
                    LotNo = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    LocationCd = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    UnitCd = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    UnitPrice = table.Column<decimal>(type: "decimal(18,4)", nullable: true),
                    AllocateTxnNo = table.Column<string>(type: "nvarchar(25)", maxLength: 25, nullable: true),
                    ShipTxnNo = table.Column<string>(type: "nvarchar(25)", maxLength: 25, nullable: true),
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
                    table.PrimaryKey("PK_T_OutboundOrderDetail", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "T_ShippingPackage",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PackageNo = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    OutboundNo = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    CaseQty = table.Column<int>(type: "int", nullable: false),
                    TotalWeightKg = table.Column<decimal>(type: "decimal(10,3)", nullable: true),
                    TotalVolumeM3 = table.Column<decimal>(type: "decimal(10,4)", nullable: true),
                    CarrierCd = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    TrackingNo = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    DepartureTime = table.Column<DateTime>(type: "datetime2", nullable: true),
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
                    table.PrimaryKey("PK_T_ShippingPackage", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_T_OutboundOrder_CustomerCd_IsDeleted",
                table: "T_OutboundOrder",
                columns: new[] { "CustomerCd", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_T_OutboundOrder_OutboundNo",
                table: "T_OutboundOrder",
                column: "OutboundNo",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_T_OutboundOrder_OutboundType_Status",
                table: "T_OutboundOrder",
                columns: new[] { "OutboundType", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_T_OutboundOrder_PlannedDate_IsDeleted",
                table: "T_OutboundOrder",
                columns: new[] { "PlannedDate", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_T_OutboundOrder_Status_IsDeleted",
                table: "T_OutboundOrder",
                columns: new[] { "Status", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_T_OutboundOrder_WebOrderNo_IsDeleted",
                table: "T_OutboundOrder",
                columns: new[] { "WebOrderNo", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_T_OutboundOrder_WorkOrderNo_IsDeleted",
                table: "T_OutboundOrder",
                columns: new[] { "WorkOrderNo", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_T_OutboundOrderDetail_AllocateTxnNo",
                table: "T_OutboundOrderDetail",
                column: "AllocateTxnNo");

            migrationBuilder.CreateIndex(
                name: "IX_T_OutboundOrderDetail_OutboundNo_LineNo",
                table: "T_OutboundOrderDetail",
                columns: new[] { "OutboundNo", "LineNo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_T_OutboundOrderDetail_ProductCd_LotNo",
                table: "T_OutboundOrderDetail",
                columns: new[] { "ProductCd", "LotNo" });

            migrationBuilder.CreateIndex(
                name: "IX_T_OutboundOrderDetail_ShipTxnNo",
                table: "T_OutboundOrderDetail",
                column: "ShipTxnNo");

            migrationBuilder.CreateIndex(
                name: "IX_T_ShippingPackage_CarrierCd_DepartureTime",
                table: "T_ShippingPackage",
                columns: new[] { "CarrierCd", "DepartureTime" });

            migrationBuilder.CreateIndex(
                name: "IX_T_ShippingPackage_OutboundNo",
                table: "T_ShippingPackage",
                column: "OutboundNo");

            migrationBuilder.CreateIndex(
                name: "IX_T_ShippingPackage_PackageNo",
                table: "T_ShippingPackage",
                column: "PackageNo",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_T_ShippingPackage_TrackingNo",
                table: "T_ShippingPackage",
                column: "TrackingNo");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "T_OutboundOrder");

            migrationBuilder.DropTable(
                name: "T_OutboundOrderDetail");

            migrationBuilder.DropTable(
                name: "T_ShippingPackage");
        }
    }
}
