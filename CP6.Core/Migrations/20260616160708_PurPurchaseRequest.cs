using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CP6.Core.Migrations
{
    /// <inheritdoc />
    public partial class PurPurchaseRequest : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Pur_PurchaseRequest",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PrNo = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    RequesterId = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    DeptId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RequestDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    Source = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    SourceRefNo = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: true),
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
                    table.PrimaryKey("PK_Pur_PurchaseRequest", x => x.Id);
                    table.UniqueConstraint("AK_Pur_PurchaseRequest_PrNo", x => x.PrNo);
                });

            migrationBuilder.CreateTable(
                name: "Pur_PurchaseRequestLine",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PrNo = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    LineNo = table.Column<int>(type: "int", nullable: false),
                    ItemId = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Qty = table.Column<decimal>(type: "decimal(21,8)", nullable: false),
                    UnitCd = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    RequiredDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    EstPrice = table.Column<decimal>(type: "decimal(18,4)", nullable: true),
                    SuggestSupplierId = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    ConvertedPoNo = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
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
                    table.PrimaryKey("PK_Pur_PurchaseRequestLine", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Pur_PurchaseRequestLine_Pur_PurchaseRequest_PrNo",
                        column: x => x.PrNo,
                        principalTable: "Pur_PurchaseRequest",
                        principalColumn: "PrNo",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Pur_PurchaseRequest_PrNo",
                table: "Pur_PurchaseRequest",
                column: "PrNo",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Pur_PurchaseRequest_Source_SourceRefNo",
                table: "Pur_PurchaseRequest",
                columns: new[] { "Source", "SourceRefNo" });

            migrationBuilder.CreateIndex(
                name: "IX_Pur_PurchaseRequest_Status",
                table: "Pur_PurchaseRequest",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_Pur_PurchaseRequestLine_ConvertedPoNo",
                table: "Pur_PurchaseRequestLine",
                column: "ConvertedPoNo");

            migrationBuilder.CreateIndex(
                name: "IX_Pur_PurchaseRequestLine_ItemId",
                table: "Pur_PurchaseRequestLine",
                column: "ItemId");

            migrationBuilder.CreateIndex(
                name: "IX_Pur_PurchaseRequestLine_PrNo",
                table: "Pur_PurchaseRequestLine",
                column: "PrNo");

            migrationBuilder.CreateIndex(
                name: "UX_Pur_PrLine_No",
                table: "Pur_PurchaseRequestLine",
                columns: new[] { "TenantId", "PrNo", "LineNo" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Pur_PurchaseRequestLine");

            migrationBuilder.DropTable(
                name: "Pur_PurchaseRequest");
        }
    }
}
