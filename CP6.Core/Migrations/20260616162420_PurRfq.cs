using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CP6.Core.Migrations
{
    /// <inheritdoc />
    public partial class PurRfq : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Pur_Rfq",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RfqNo = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    RfqDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DueDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    Buyer = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    SourcePrNo = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
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
                    table.PrimaryKey("PK_Pur_Rfq", x => x.Id);
                    table.UniqueConstraint("AK_Pur_Rfq_RfqNo", x => x.RfqNo);
                });

            migrationBuilder.CreateTable(
                name: "Pur_RfqLine",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RfqNo = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    LineNo = table.Column<int>(type: "int", nullable: false),
                    ItemId = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Qty = table.Column<decimal>(type: "decimal(21,8)", nullable: false),
                    UnitCd = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    RequiredDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    SourcePrNo = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    SourcePrLineNo = table.Column<int>(type: "int", nullable: true),
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
                    table.PrimaryKey("PK_Pur_RfqLine", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Pur_RfqLine_Pur_Rfq_RfqNo",
                        column: x => x.RfqNo,
                        principalTable: "Pur_Rfq",
                        principalColumn: "RfqNo",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Pur_RfqQuote",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RfqNo = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    SupplierId = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    LineNo = table.Column<int>(type: "int", nullable: false),
                    QuotedPrice = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    CurrencyCd = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: true),
                    LeadDays = table.Column<int>(type: "int", nullable: true),
                    ValidUntil = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsSelected = table.Column<bool>(type: "bit", nullable: false),
                    Rank = table.Column<int>(type: "int", nullable: false),
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
                    table.PrimaryKey("PK_Pur_RfqQuote", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Pur_RfqQuote_Pur_Rfq_RfqNo",
                        column: x => x.RfqNo,
                        principalTable: "Pur_Rfq",
                        principalColumn: "RfqNo",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Pur_RfqSupplier",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RfqNo = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    SupplierId = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    SupplierName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    InviteStatus = table.Column<int>(type: "int", nullable: false),
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
                    table.PrimaryKey("PK_Pur_RfqSupplier", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Pur_RfqSupplier_Pur_Rfq_RfqNo",
                        column: x => x.RfqNo,
                        principalTable: "Pur_Rfq",
                        principalColumn: "RfqNo",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Pur_Rfq_RfqNo",
                table: "Pur_Rfq",
                column: "RfqNo",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Pur_Rfq_SourcePrNo",
                table: "Pur_Rfq",
                column: "SourcePrNo");

            migrationBuilder.CreateIndex(
                name: "IX_Pur_Rfq_Status",
                table: "Pur_Rfq",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_Pur_RfqLine_ItemId",
                table: "Pur_RfqLine",
                column: "ItemId");

            migrationBuilder.CreateIndex(
                name: "IX_Pur_RfqLine_RfqNo",
                table: "Pur_RfqLine",
                column: "RfqNo");

            migrationBuilder.CreateIndex(
                name: "IX_Pur_RfqLine_SourcePrNo_SourcePrLineNo",
                table: "Pur_RfqLine",
                columns: new[] { "SourcePrNo", "SourcePrLineNo" });

            migrationBuilder.CreateIndex(
                name: "UX_Pur_RfqLine_No",
                table: "Pur_RfqLine",
                columns: new[] { "TenantId", "RfqNo", "LineNo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Pur_RfqQuote_RfqNo",
                table: "Pur_RfqQuote",
                column: "RfqNo");

            migrationBuilder.CreateIndex(
                name: "UX_Pur_RfqQuote",
                table: "Pur_RfqQuote",
                columns: new[] { "TenantId", "RfqNo", "SupplierId", "LineNo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Pur_RfqSupplier_RfqNo",
                table: "Pur_RfqSupplier",
                column: "RfqNo");

            migrationBuilder.CreateIndex(
                name: "UX_Pur_RfqSupplier",
                table: "Pur_RfqSupplier",
                columns: new[] { "TenantId", "RfqNo", "SupplierId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Pur_RfqLine");

            migrationBuilder.DropTable(
                name: "Pur_RfqQuote");

            migrationBuilder.DropTable(
                name: "Pur_RfqSupplier");

            migrationBuilder.DropTable(
                name: "Pur_Rfq");
        }
    }
}
