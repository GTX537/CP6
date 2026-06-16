using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CP6.Core.Migrations
{
    /// <inheritdoc />
    public partial class PurGoodsReceipt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Pur_GoodsReceipt",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    GrNo = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    PoNo = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    SupplierId = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ReceiptDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    WmsInboundNo = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    PostingBasis = table.Column<string>(type: "nvarchar(4)", maxLength: 4, nullable: false),
                    WarehouseCd = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    Remarks = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
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
                    table.PrimaryKey("PK_Pur_GoodsReceipt", x => x.Id);
                    table.UniqueConstraint("AK_Pur_GoodsReceipt_GrNo", x => x.GrNo);
                });

            migrationBuilder.CreateTable(
                name: "Pur_GoodsReceiptLine",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    GrNo = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    LineNo = table.Column<int>(type: "int", nullable: false),
                    PoLineNo = table.Column<int>(type: "int", nullable: false),
                    ItemId = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    ReceivedQty = table.Column<decimal>(type: "decimal(21,8)", nullable: false),
                    AcceptedQty = table.Column<decimal>(type: "decimal(21,8)", nullable: false),
                    RejectedQty = table.Column<decimal>(type: "decimal(21,8)", nullable: false),
                    QcStatus = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    WmsReceiptDetailRef = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
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
                    table.PrimaryKey("PK_Pur_GoodsReceiptLine", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Pur_GoodsReceiptLine_Pur_GoodsReceipt_GrNo",
                        column: x => x.GrNo,
                        principalTable: "Pur_GoodsReceipt",
                        principalColumn: "GrNo",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Pur_GoodsReceipt_GrNo",
                table: "Pur_GoodsReceipt",
                column: "GrNo",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Pur_GoodsReceipt_PoNo",
                table: "Pur_GoodsReceipt",
                column: "PoNo");

            migrationBuilder.CreateIndex(
                name: "IX_Pur_GoodsReceipt_SupplierId_Status",
                table: "Pur_GoodsReceipt",
                columns: new[] { "SupplierId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_Pur_GoodsReceiptLine_GrNo",
                table: "Pur_GoodsReceiptLine",
                column: "GrNo");

            migrationBuilder.CreateIndex(
                name: "IX_Pur_GoodsReceiptLine_PoLineNo",
                table: "Pur_GoodsReceiptLine",
                column: "PoLineNo");

            migrationBuilder.CreateIndex(
                name: "UX_Pur_GrLine_No",
                table: "Pur_GoodsReceiptLine",
                columns: new[] { "TenantId", "GrNo", "LineNo" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Pur_GoodsReceiptLine");

            migrationBuilder.DropTable(
                name: "Pur_GoodsReceipt");
        }
    }
}
