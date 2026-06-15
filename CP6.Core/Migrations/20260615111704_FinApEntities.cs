using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CP6.Core.Migrations
{
    /// <inheritdoc />
    public partial class FinApEntities : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Fin_ApInvoice",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    No = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    SupplierInvoiceNo = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    SupplierId = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    InvoiceDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DueDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CurrencyCd = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: true),
                    FxRate = table.Column<decimal>(type: "decimal(18,6)", nullable: false),
                    NetAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    TaxAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    GrossAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    SettledAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    JournalEntryId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    PurchaseOrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    IsCreditMemo = table.Column<bool>(type: "bit", nullable: false),
                    OriginInvoiceId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RmaId = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    Creator = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CreateDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Modifier = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ModifyDate = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Fin_ApInvoice", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Fin_ApSettlement",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PaymentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ApInvoiceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
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
                    table.PrimaryKey("PK_Fin_ApSettlement", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Fin_BankAccount",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    BankName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    AccountNo = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    CurrencyCd = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: true),
                    GlAccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    Creator = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CreateDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Modifier = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ModifyDate = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Fin_BankAccount", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Fin_Payment",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    No = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    SupplierId = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    PayDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CurrencyCd = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: true),
                    FxRate = table.Column<decimal>(type: "decimal(18,6)", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    SettledAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Method = table.Column<int>(type: "int", nullable: false),
                    BankAccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IsPrepayment = table.Column<bool>(type: "bit", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    JournalEntryId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Creator = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CreateDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Modifier = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ModifyDate = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Fin_Payment", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Fin_TaxCode",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Rate = table.Column<decimal>(type: "decimal(9,6)", nullable: false),
                    Direction = table.Column<int>(type: "int", nullable: false),
                    Recoverable = table.Column<bool>(type: "bit", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    Creator = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CreateDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Modifier = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ModifyDate = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Fin_TaxCode", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Fin_ApInvoiceLine",
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
                    ExpenseAccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CostCenterId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Creator = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CreateDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Modifier = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ModifyDate = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Fin_ApInvoiceLine", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Fin_ApInvoiceLine_Fin_ApInvoice_InvoiceId",
                        column: x => x.InvoiceId,
                        principalTable: "Fin_ApInvoice",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Fin_ApInvoice_DueDate",
                table: "Fin_ApInvoice",
                column: "DueDate");

            migrationBuilder.CreateIndex(
                name: "IX_Fin_ApInvoice_No",
                table: "Fin_ApInvoice",
                column: "No",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Fin_ApInvoice_Status",
                table: "Fin_ApInvoice",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_Fin_ApInvoice_SupplierId",
                table: "Fin_ApInvoice",
                column: "SupplierId");

            migrationBuilder.CreateIndex(
                name: "UX_Fin_ApInvoice_SupplierDupGuard",
                table: "Fin_ApInvoice",
                columns: new[] { "SupplierId", "SupplierInvoiceNo" },
                unique: true,
                filter: "[SupplierInvoiceNo] IS NOT NULL AND [IsCreditMemo] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_Fin_ApInvoiceLine_InvoiceId",
                table: "Fin_ApInvoiceLine",
                column: "InvoiceId");

            migrationBuilder.CreateIndex(
                name: "IX_Fin_ApSettlement_ApInvoiceId",
                table: "Fin_ApSettlement",
                column: "ApInvoiceId");

            migrationBuilder.CreateIndex(
                name: "IX_Fin_ApSettlement_PaymentId",
                table: "Fin_ApSettlement",
                column: "PaymentId");

            migrationBuilder.CreateIndex(
                name: "IX_Fin_BankAccount_Code",
                table: "Fin_BankAccount",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Fin_Payment_No",
                table: "Fin_Payment",
                column: "No",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Fin_Payment_Status",
                table: "Fin_Payment",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_Fin_Payment_SupplierId",
                table: "Fin_Payment",
                column: "SupplierId");

            migrationBuilder.CreateIndex(
                name: "IX_Fin_TaxCode_Code",
                table: "Fin_TaxCode",
                column: "Code",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Fin_ApInvoiceLine");

            migrationBuilder.DropTable(
                name: "Fin_ApSettlement");

            migrationBuilder.DropTable(
                name: "Fin_BankAccount");

            migrationBuilder.DropTable(
                name: "Fin_Payment");

            migrationBuilder.DropTable(
                name: "Fin_TaxCode");

            migrationBuilder.DropTable(
                name: "Fin_ApInvoice");
        }
    }
}
