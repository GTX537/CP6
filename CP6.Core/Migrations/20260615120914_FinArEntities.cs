using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CP6.Core.Migrations
{
    /// <inheritdoc />
    public partial class FinArEntities : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "CreditLimit",
                table: "T_WebBusinessPartner",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Fin_ArInvoice",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    No = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    CustomerId = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    InvoiceDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DueDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CurrencyCd = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: true),
                    FxRate = table.Column<decimal>(type: "decimal(18,6)", nullable: false),
                    NetAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    TaxAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    GrossAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    SettledAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    CostAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    JournalEntryId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CostJournalEntryId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ShipmentId = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    OrderId = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    IsCreditMemo = table.Column<bool>(type: "bit", nullable: false),
                    OriginInvoiceId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreditNoteId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Creator = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CreateDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Modifier = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ModifyDate = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Fin_ArInvoice", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Fin_ArSettlement",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReceiptId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ArInvoiceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SettledAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    DiffAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    DiffType = table.Column<int>(type: "int", nullable: false),
                    DiffAccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DiffJournalEntryId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Creator = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CreateDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Modifier = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ModifyDate = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Fin_ArSettlement", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Fin_Receipt",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    No = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    CustomerId = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ReceiptDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CurrencyCd = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: true),
                    FxRate = table.Column<decimal>(type: "decimal(18,6)", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    SettledAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Method = table.Column<int>(type: "int", nullable: false),
                    BankAccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IsAdvance = table.Column<bool>(type: "bit", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    JournalEntryId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Creator = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CreateDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Modifier = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ModifyDate = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Fin_Receipt", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Fin_ArInvoiceLine",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    InvoiceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LineNo = table.Column<int>(type: "int", nullable: false),
                    ItemId = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    Qty = table.Column<decimal>(type: "decimal(21,8)", nullable: false),
                    UnitPrice = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    TaxCodeId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    TaxAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    RevenueAccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CostCenterId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Creator = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CreateDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Modifier = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ModifyDate = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Fin_ArInvoiceLine", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Fin_ArInvoiceLine_Fin_ArInvoice_InvoiceId",
                        column: x => x.InvoiceId,
                        principalTable: "Fin_ArInvoice",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Fin_ArInvoice_CustomerId",
                table: "Fin_ArInvoice",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_Fin_ArInvoice_DueDate",
                table: "Fin_ArInvoice",
                column: "DueDate");

            migrationBuilder.CreateIndex(
                name: "IX_Fin_ArInvoice_No",
                table: "Fin_ArInvoice",
                column: "No",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Fin_ArInvoice_Status",
                table: "Fin_ArInvoice",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "UX_Fin_ArInvoice_ShipmentDupGuard",
                table: "Fin_ArInvoice",
                column: "ShipmentId",
                unique: true,
                filter: "[ShipmentId] IS NOT NULL AND [IsCreditMemo] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_Fin_ArInvoiceLine_InvoiceId",
                table: "Fin_ArInvoiceLine",
                column: "InvoiceId");

            migrationBuilder.CreateIndex(
                name: "IX_Fin_ArSettlement_ArInvoiceId",
                table: "Fin_ArSettlement",
                column: "ArInvoiceId");

            migrationBuilder.CreateIndex(
                name: "IX_Fin_ArSettlement_ReceiptId",
                table: "Fin_ArSettlement",
                column: "ReceiptId");

            migrationBuilder.CreateIndex(
                name: "IX_Fin_Receipt_CustomerId",
                table: "Fin_Receipt",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_Fin_Receipt_No",
                table: "Fin_Receipt",
                column: "No",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Fin_Receipt_Status",
                table: "Fin_Receipt",
                column: "Status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Fin_ArInvoiceLine");

            migrationBuilder.DropTable(
                name: "Fin_ArSettlement");

            migrationBuilder.DropTable(
                name: "Fin_Receipt");

            migrationBuilder.DropTable(
                name: "Fin_ArInvoice");

            migrationBuilder.DropColumn(
                name: "CreditLimit",
                table: "T_WebBusinessPartner");
        }
    }
}
