using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CP6.Core.Migrations
{
    /// <inheritdoc />
    public partial class PurPurchaseOrder : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Pur_PurchaseOrder",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PoNo = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    SupplierId = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    SupplierName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Type = table.Column<int>(type: "int", nullable: false),
                    CurrencyCd = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: true),
                    FxRate = table.Column<decimal>(type: "decimal(18,6)", nullable: false),
                    PostingBasis = table.Column<string>(type: "nvarchar(4)", maxLength: 4, nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    OrderDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    NetAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    TaxAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    GrossAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    SourceRfqNo = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    ApprovalRef = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
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
                    table.PrimaryKey("PK_Pur_PurchaseOrder", x => x.Id);
                    table.UniqueConstraint("AK_Pur_PurchaseOrder_PoNo", x => x.PoNo);
                });

            migrationBuilder.CreateTable(
                name: "Pur_PurchaseOrderLine",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PoNo = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    LineNo = table.Column<int>(type: "int", nullable: false),
                    ItemId = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Qty = table.Column<decimal>(type: "decimal(21,8)", nullable: false),
                    UnitPrice = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    TaxCodeId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    TaxRate = table.Column<decimal>(type: "decimal(9,6)", nullable: false),
                    NetAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    TaxAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    RequiredDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ReceivedQty = table.Column<decimal>(type: "decimal(21,8)", nullable: false),
                    AcceptedQty = table.Column<decimal>(type: "decimal(21,8)", nullable: false),
                    InvoicedQty = table.Column<decimal>(type: "decimal(21,8)", nullable: false),
                    MatchStatus = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
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
                    table.PrimaryKey("PK_Pur_PurchaseOrderLine", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Pur_PurchaseOrderLine_Pur_PurchaseOrder_PoNo",
                        column: x => x.PoNo,
                        principalTable: "Pur_PurchaseOrder",
                        principalColumn: "PoNo",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Pur_PurchaseOrder_OrderDate",
                table: "Pur_PurchaseOrder",
                column: "OrderDate");

            migrationBuilder.CreateIndex(
                name: "IX_Pur_PurchaseOrder_PoNo",
                table: "Pur_PurchaseOrder",
                column: "PoNo",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Pur_PurchaseOrder_Status",
                table: "Pur_PurchaseOrder",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_Pur_PurchaseOrder_SupplierId_Status",
                table: "Pur_PurchaseOrder",
                columns: new[] { "SupplierId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_Pur_PurchaseOrderLine_ItemId",
                table: "Pur_PurchaseOrderLine",
                column: "ItemId");

            migrationBuilder.CreateIndex(
                name: "IX_Pur_PurchaseOrderLine_PoNo",
                table: "Pur_PurchaseOrderLine",
                column: "PoNo");

            migrationBuilder.CreateIndex(
                name: "UX_Pur_PoLine_No",
                table: "Pur_PurchaseOrderLine",
                columns: new[] { "TenantId", "PoNo", "LineNo" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Pur_PurchaseOrderLine");

            migrationBuilder.DropTable(
                name: "Pur_PurchaseOrder");
        }
    }
}
