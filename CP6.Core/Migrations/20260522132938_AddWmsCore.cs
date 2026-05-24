using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CP6.Core.Migrations
{
    /// <inheritdoc />
    public partial class AddWmsCore : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "T_Location",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LocationCd = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    WarehouseCd = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    ParentLocationCd = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    LocationLevel = table.Column<int>(type: "int", nullable: false),
                    LocationName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    XCoord = table.Column<int>(type: "int", nullable: true),
                    YCoord = table.Column<int>(type: "int", nullable: true),
                    ZCoord = table.Column<int>(type: "int", nullable: true),
                    CapacityQty = table.Column<decimal>(type: "decimal(21,8)", nullable: false),
                    AllowedProductType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    IsPickable = table.Column<bool>(type: "bit", nullable: false),
                    IsBlocked = table.Column<bool>(type: "bit", nullable: false),
                    Barcode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Creator = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CreateDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Modifier = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ModifyDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_T_Location", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "T_Stock",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WarehouseCd = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    LocationCd = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    ProductCd = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    LotNo = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    PhysicalQty = table.Column<decimal>(type: "decimal(21,8)", nullable: false),
                    AllocatedQty = table.Column<decimal>(type: "decimal(21,8)", nullable: false),
                    AvailableQty = table.Column<decimal>(type: "decimal(21,8)", nullable: false),
                    UnitCd = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    ReceiveDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ExpiryDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UnitPrice = table.Column<decimal>(type: "decimal(18,4)", nullable: true),
                    RecallFlag = table.Column<bool>(type: "bit", nullable: false),
                    OwnerType = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    OwnerCd = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    PaperRollNo = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    Creator = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CreateDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Modifier = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ModifyDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_T_Stock", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "T_StockTransaction",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TxnNo = table.Column<string>(type: "nvarchar(25)", maxLength: 25, nullable: false),
                    TxnType = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    TxnDateTime = table.Column<DateTime>(type: "datetime2", nullable: false),
                    WarehouseCd = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    LocationCd = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    ProductCd = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    LotNo = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Qty = table.Column<decimal>(type: "decimal(21,8)", nullable: false),
                    UnitCd = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    UnitPrice = table.Column<decimal>(type: "decimal(18,4)", nullable: true),
                    RelatedNo = table.Column<string>(type: "nvarchar(25)", maxLength: 25, nullable: true),
                    RelatedType = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    CounterLocationCd = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    OperatorCd = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    Remark = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ReceiptInspectionNo = table.Column<string>(type: "nvarchar(25)", maxLength: 25, nullable: true),
                    KitOrderNo = table.Column<string>(type: "nvarchar(25)", maxLength: 25, nullable: true),
                    RmaNo = table.Column<string>(type: "nvarchar(25)", maxLength: 25, nullable: true),
                    CrossDockFlag = table.Column<bool>(type: "bit", nullable: true),
                    OwnerType = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    OwnerCd = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    PaperRollNo = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    ConsumedLengthM = table.Column<decimal>(type: "decimal(21,8)", nullable: true),
                    Creator = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CreateDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Modifier = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ModifyDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_T_StockTransaction", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "T_Warehouse",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WarehouseCd = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    WarehouseName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    WarehouseType = table.Column<int>(type: "int", nullable: false),
                    BaseCd = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    AddressText = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ManagerCd = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    AllowNegative = table.Column<bool>(type: "bit", nullable: false),
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
                    table.PrimaryKey("PK_T_Warehouse", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "T_WmsSequence",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Prefix = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    DateKey = table.Column<string>(type: "nvarchar(8)", maxLength: 8, nullable: false),
                    NextNo = table.Column<int>(type: "int", nullable: false),
                    Creator = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CreateDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Modifier = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ModifyDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_T_WmsSequence", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_T_Location_Barcode",
                table: "T_Location",
                column: "Barcode");

            migrationBuilder.CreateIndex(
                name: "IX_T_Location_LocationCd",
                table: "T_Location",
                column: "LocationCd",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_T_Location_WarehouseCd_IsBlocked_IsPickable",
                table: "T_Location",
                columns: new[] { "WarehouseCd", "IsBlocked", "IsPickable" });

            migrationBuilder.CreateIndex(
                name: "IX_T_Location_WarehouseCd_ParentLocationCd",
                table: "T_Location",
                columns: new[] { "WarehouseCd", "ParentLocationCd" });

            migrationBuilder.CreateIndex(
                name: "IX_T_Stock_PaperRollNo",
                table: "T_Stock",
                column: "PaperRollNo");

            migrationBuilder.CreateIndex(
                name: "IX_T_Stock_ProductCd_ExpiryDate",
                table: "T_Stock",
                columns: new[] { "ProductCd", "ExpiryDate" });

            migrationBuilder.CreateIndex(
                name: "IX_T_Stock_ProductCd_OwnerType_OwnerCd",
                table: "T_Stock",
                columns: new[] { "ProductCd", "OwnerType", "OwnerCd" });

            migrationBuilder.CreateIndex(
                name: "IX_T_Stock_WarehouseCd_IsDeleted",
                table: "T_Stock",
                columns: new[] { "WarehouseCd", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "UX_Stock_WLPL",
                table: "T_Stock",
                columns: new[] { "WarehouseCd", "LocationCd", "ProductCd", "LotNo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_T_StockTransaction_ProductCd_LotNo_TxnDateTime",
                table: "T_StockTransaction",
                columns: new[] { "ProductCd", "LotNo", "TxnDateTime" });

            migrationBuilder.CreateIndex(
                name: "IX_T_StockTransaction_RelatedType_RelatedNo",
                table: "T_StockTransaction",
                columns: new[] { "RelatedType", "RelatedNo" });

            migrationBuilder.CreateIndex(
                name: "IX_T_StockTransaction_TxnDateTime",
                table: "T_StockTransaction",
                column: "TxnDateTime");

            migrationBuilder.CreateIndex(
                name: "IX_T_StockTransaction_TxnNo",
                table: "T_StockTransaction",
                column: "TxnNo",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_T_StockTransaction_TxnType_TxnDateTime",
                table: "T_StockTransaction",
                columns: new[] { "TxnType", "TxnDateTime" });

            migrationBuilder.CreateIndex(
                name: "IX_T_StockTransaction_WarehouseCd_LocationCd_TxnDateTime",
                table: "T_StockTransaction",
                columns: new[] { "WarehouseCd", "LocationCd", "TxnDateTime" });

            migrationBuilder.CreateIndex(
                name: "IX_T_Warehouse_BaseCd_IsDeleted",
                table: "T_Warehouse",
                columns: new[] { "BaseCd", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_T_Warehouse_WarehouseCd",
                table: "T_Warehouse",
                column: "WarehouseCd",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_T_Warehouse_WarehouseType_IsDeleted",
                table: "T_Warehouse",
                columns: new[] { "WarehouseType", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_T_WmsSequence_Prefix_DateKey",
                table: "T_WmsSequence",
                columns: new[] { "Prefix", "DateKey" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "T_Location");

            migrationBuilder.DropTable(
                name: "T_Stock");

            migrationBuilder.DropTable(
                name: "T_StockTransaction");

            migrationBuilder.DropTable(
                name: "T_Warehouse");

            migrationBuilder.DropTable(
                name: "T_WmsSequence");
        }
    }
}
