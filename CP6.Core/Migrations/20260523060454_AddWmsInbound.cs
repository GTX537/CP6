using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CP6.Core.Migrations
{
    /// <inheritdoc />
    public partial class AddWmsInbound : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "T_InboundOrder",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    InboundNo = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    InboundType = table.Column<int>(type: "int", nullable: false),
                    SupplierCd = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    SupplierName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    PoNo = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    ExpectedArrivalDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    WarehouseCd = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
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
                    table.PrimaryKey("PK_T_InboundOrder", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "T_InboundOrderDetail",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    InboundNo = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    LineNo = table.Column<int>(type: "int", nullable: false),
                    ProductCd = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ProductName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    LotNo = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    ExpectedQty = table.Column<decimal>(type: "decimal(21,8)", nullable: false),
                    ReceivedQty = table.Column<decimal>(type: "decimal(21,8)", nullable: false),
                    UnitCd = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    ExpectedLocationCd = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    UnitPrice = table.Column<decimal>(type: "decimal(18,4)", nullable: true),
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
                    table.PrimaryKey("PK_T_InboundOrderDetail", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "T_InboundReceipt",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReceiptNo = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    InboundNo = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    SourceType = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    WorkOrderNo = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    ReceiveDateTime = table.Column<DateTime>(type: "datetime2", nullable: false),
                    OperatorCd = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    WarehouseCd = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
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
                    table.PrimaryKey("PK_T_InboundReceipt", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "T_InboundReceiptDetail",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReceiptNo = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    LineNo = table.Column<int>(type: "int", nullable: false),
                    RefOrderLineNo = table.Column<int>(type: "int", nullable: true),
                    ProductCd = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ProductName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    LotNo = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    ReceivedQty = table.Column<decimal>(type: "decimal(21,8)", nullable: false),
                    UnitCd = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    LocationCd = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    UnitPrice = table.Column<decimal>(type: "decimal(18,4)", nullable: true),
                    ExpiryDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    PaperRollNo = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    StockTxnNo = table.Column<string>(type: "nvarchar(25)", maxLength: 25, nullable: true),
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
                    table.PrimaryKey("PK_T_InboundReceiptDetail", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_T_InboundOrder_ExpectedArrivalDate_IsDeleted",
                table: "T_InboundOrder",
                columns: new[] { "ExpectedArrivalDate", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_T_InboundOrder_InboundNo",
                table: "T_InboundOrder",
                column: "InboundNo",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_T_InboundOrder_Status_IsDeleted",
                table: "T_InboundOrder",
                columns: new[] { "Status", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_T_InboundOrder_SupplierCd_IsDeleted",
                table: "T_InboundOrder",
                columns: new[] { "SupplierCd", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_T_InboundOrder_WarehouseCd_IsDeleted",
                table: "T_InboundOrder",
                columns: new[] { "WarehouseCd", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_T_InboundOrderDetail_InboundNo_LineNo",
                table: "T_InboundOrderDetail",
                columns: new[] { "InboundNo", "LineNo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_T_InboundOrderDetail_ProductCd",
                table: "T_InboundOrderDetail",
                column: "ProductCd");

            migrationBuilder.CreateIndex(
                name: "IX_T_InboundReceipt_InboundNo_IsDeleted",
                table: "T_InboundReceipt",
                columns: new[] { "InboundNo", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_T_InboundReceipt_ReceiptNo",
                table: "T_InboundReceipt",
                column: "ReceiptNo",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_T_InboundReceipt_ReceiveDateTime_IsDeleted",
                table: "T_InboundReceipt",
                columns: new[] { "ReceiveDateTime", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_T_InboundReceipt_Status_IsDeleted",
                table: "T_InboundReceipt",
                columns: new[] { "Status", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_T_InboundReceipt_WorkOrderNo_IsDeleted",
                table: "T_InboundReceipt",
                columns: new[] { "WorkOrderNo", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_T_InboundReceiptDetail_ProductCd_LotNo",
                table: "T_InboundReceiptDetail",
                columns: new[] { "ProductCd", "LotNo" });

            migrationBuilder.CreateIndex(
                name: "IX_T_InboundReceiptDetail_ReceiptNo_LineNo",
                table: "T_InboundReceiptDetail",
                columns: new[] { "ReceiptNo", "LineNo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_T_InboundReceiptDetail_StockTxnNo",
                table: "T_InboundReceiptDetail",
                column: "StockTxnNo");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "T_InboundOrder");

            migrationBuilder.DropTable(
                name: "T_InboundOrderDetail");

            migrationBuilder.DropTable(
                name: "T_InboundReceipt");

            migrationBuilder.DropTable(
                name: "T_InboundReceiptDetail");
        }
    }
}
