using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CP6.Core.Migrations
{
    /// <inheritdoc />
    public partial class AddWmsStockTake : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "T_StockTake",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StockTakeNo = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    StockTakeType = table.Column<int>(type: "int", nullable: false),
                    PlannedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ActualDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CompletedDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    TargetWarehouseCd = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    TargetLocationPrefix = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    TargetProductCd = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    ApprovalThresholdAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    ApproverCd = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
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
                    table.PrimaryKey("PK_T_StockTake", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "T_StockTakeDetail",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StockTakeNo = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    LineNo = table.Column<int>(type: "int", nullable: false),
                    StockId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WarehouseCd = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    LocationCd = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    ProductCd = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    LotNo = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    BookQty = table.Column<decimal>(type: "decimal(21,8)", nullable: false),
                    CountedQty = table.Column<decimal>(type: "decimal(21,8)", nullable: true),
                    DiffQty = table.Column<decimal>(type: "decimal(21,8)", nullable: true),
                    DiffAmount = table.Column<decimal>(type: "decimal(18,4)", nullable: true),
                    UnitPrice = table.Column<decimal>(type: "decimal(18,4)", nullable: true),
                    DiffReasonCd = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    DiffReasonText = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ApprovalStatus = table.Column<int>(type: "int", nullable: false),
                    AdjustTxnNo = table.Column<string>(type: "nvarchar(25)", maxLength: 25, nullable: true),
                    CountedByCd = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
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
                    table.PrimaryKey("PK_T_StockTakeDetail", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_T_StockTake_PlannedDate_IsDeleted",
                table: "T_StockTake",
                columns: new[] { "PlannedDate", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_T_StockTake_Status_IsDeleted",
                table: "T_StockTake",
                columns: new[] { "Status", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_T_StockTake_StockTakeNo",
                table: "T_StockTake",
                column: "StockTakeNo",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_T_StockTake_TargetWarehouseCd_IsDeleted",
                table: "T_StockTake",
                columns: new[] { "TargetWarehouseCd", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_T_StockTakeDetail_ApprovalStatus",
                table: "T_StockTakeDetail",
                column: "ApprovalStatus");

            migrationBuilder.CreateIndex(
                name: "IX_T_StockTakeDetail_ProductCd_LotNo",
                table: "T_StockTakeDetail",
                columns: new[] { "ProductCd", "LotNo" });

            migrationBuilder.CreateIndex(
                name: "IX_T_StockTakeDetail_StockId",
                table: "T_StockTakeDetail",
                column: "StockId");

            migrationBuilder.CreateIndex(
                name: "IX_T_StockTakeDetail_StockTakeNo_LineNo",
                table: "T_StockTakeDetail",
                columns: new[] { "StockTakeNo", "LineNo" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "T_StockTake");

            migrationBuilder.DropTable(
                name: "T_StockTakeDetail");
        }
    }
}
