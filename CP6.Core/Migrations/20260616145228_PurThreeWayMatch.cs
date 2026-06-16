using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CP6.Core.Migrations
{
    /// <inheritdoc />
    public partial class PurThreeWayMatch : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Pur_MatchTolerance",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SupplierId = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    QtyTolPct = table.Column<decimal>(type: "decimal(9,6)", nullable: false),
                    PriceTolPct = table.Column<decimal>(type: "decimal(9,6)", nullable: false),
                    AmountTolAbs = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
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
                    table.PrimaryKey("PK_Pur_MatchTolerance", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Pur_ThreeWayMatch",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MatchNo = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    PoNo = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    SupplierInvoiceNo = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    MatchDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    MaxQtyVarPct = table.Column<decimal>(type: "decimal(9,6)", nullable: false),
                    MaxPriceVarPct = table.Column<decimal>(type: "decimal(9,6)", nullable: false),
                    ApInvoiceNo = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    ApInvoiceId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Note = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    HandledBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
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
                    table.PrimaryKey("PK_Pur_ThreeWayMatch", x => x.Id);
                    table.UniqueConstraint("AK_Pur_ThreeWayMatch_MatchNo", x => x.MatchNo);
                });

            migrationBuilder.CreateTable(
                name: "Pur_ThreeWayMatchLine",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MatchNo = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    LineNo = table.Column<int>(type: "int", nullable: false),
                    PoLineNo = table.Column<int>(type: "int", nullable: false),
                    ItemId = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Qty = table.Column<decimal>(type: "decimal(21,8)", nullable: false),
                    UnitPrice = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    TaxCodeId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    PriceVarPct = table.Column<decimal>(type: "decimal(9,6)", nullable: false),
                    RemainAccepted = table.Column<decimal>(type: "decimal(21,8)", nullable: false),
                    WithinTolerance = table.Column<bool>(type: "bit", nullable: false),
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
                    table.PrimaryKey("PK_Pur_ThreeWayMatchLine", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Pur_ThreeWayMatchLine_Pur_ThreeWayMatch_MatchNo",
                        column: x => x.MatchNo,
                        principalTable: "Pur_ThreeWayMatch",
                        principalColumn: "MatchNo",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Pur_MatchTolerance_SupplierId",
                table: "Pur_MatchTolerance",
                column: "SupplierId");

            migrationBuilder.CreateIndex(
                name: "IX_Pur_ThreeWayMatch_MatchNo",
                table: "Pur_ThreeWayMatch",
                column: "MatchNo",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Pur_ThreeWayMatch_PoNo_Status",
                table: "Pur_ThreeWayMatch",
                columns: new[] { "PoNo", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_Pur_ThreeWayMatch_SupplierInvoiceNo",
                table: "Pur_ThreeWayMatch",
                column: "SupplierInvoiceNo");

            migrationBuilder.CreateIndex(
                name: "IX_Pur_ThreeWayMatchLine_MatchNo",
                table: "Pur_ThreeWayMatchLine",
                column: "MatchNo");

            migrationBuilder.CreateIndex(
                name: "UX_Pur_TwmLine_No",
                table: "Pur_ThreeWayMatchLine",
                columns: new[] { "TenantId", "MatchNo", "LineNo" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Pur_MatchTolerance");

            migrationBuilder.DropTable(
                name: "Pur_ThreeWayMatchLine");

            migrationBuilder.DropTable(
                name: "Pur_ThreeWayMatch");
        }
    }
}
