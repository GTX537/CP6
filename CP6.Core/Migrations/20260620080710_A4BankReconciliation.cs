using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CP6.Core.Migrations
{
    /// <inheritdoc />
    public partial class A4BankReconciliation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Fin_BankImportProfile",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    BankAccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    FileFormat = table.Column<int>(type: "int", nullable: false),
                    Encoding = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Delimiter = table.Column<string>(type: "nvarchar(4)", maxLength: 4, nullable: false),
                    SkipHeaderRows = table.Column<int>(type: "int", nullable: false),
                    DateField = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    DateFormat = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    AmountMode = table.Column<int>(type: "int", nullable: false),
                    AmountField = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    DepositAmountField = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    WithdrawalAmountField = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    SignRule = table.Column<int>(type: "int", nullable: false),
                    DescriptionField = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    CounterpartyField = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    RefNoField = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    BalanceField = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    DecimalSeparator = table.Column<string>(type: "nvarchar(2)", maxLength: 2, nullable: false),
                    ThousandsSeparator = table.Column<string>(type: "nvarchar(2)", maxLength: 2, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    Creator = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CreateDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Modifier = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ModifyDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Fin_BankImportProfile", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Fin_BankReconJournalLink",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MatchGroupId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    JournalLineId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    JournalEntryId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BankSignedAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Creator = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CreateDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Modifier = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ModifyDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Fin_BankReconJournalLink", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Fin_BankReconMatch",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StatementId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MatchType = table.Column<int>(type: "int", nullable: false),
                    StmtSignedSum = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    MatchedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    MatchedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Note = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    Creator = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CreateDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Modifier = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ModifyDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Fin_BankReconMatch", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Fin_BankStatement",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    No = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    BankAccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FiscalPeriodId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PeriodStart = table.Column<DateTime>(type: "datetime2", nullable: false),
                    PeriodEnd = table.Column<DateTime>(type: "datetime2", nullable: false),
                    StatementDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CurrencyCd = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: true),
                    OpeningBalance = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    ClosingBalance = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    ImportFileName = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    LockedStatementInternalDiff = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    LockedReconciledDiff = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    LockedBankAdjustedBalance = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    LockedBookAdjustedBalance = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    LockSnapshotJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    LockedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LockedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    Creator = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CreateDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Modifier = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ModifyDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Fin_BankStatement", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Fin_BankStatementLine",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StatementId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LineNo = table.Column<int>(type: "int", nullable: false),
                    TxnDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Direction = table.Column<int>(type: "int", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    SignedAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    CurrencyCd = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: true),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CounterpartyName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    RefNo = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    BalanceAfter = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    Source = table.Column<int>(type: "int", nullable: false),
                    MatchStatus = table.Column<int>(type: "int", nullable: false),
                    Category = table.Column<int>(type: "int", nullable: false),
                    MatchGroupId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    GeneratedJournalEntryId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    GeneratedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    GeneratedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ImportBatchNo = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    RawRowJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RawRowHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    Fingerprint = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    Creator = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CreateDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Modifier = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ModifyDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Fin_BankStatementLine", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Fin_BankReconJournalLink_Group",
                table: "Fin_BankReconJournalLink",
                column: "MatchGroupId");

            migrationBuilder.CreateIndex(
                name: "UX_Fin_BankReconJournalLink_JL",
                table: "Fin_BankReconJournalLink",
                columns: new[] { "TenantId", "JournalLineId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Fin_BankReconMatch_StatementId",
                table: "Fin_BankReconMatch",
                column: "StatementId");

            migrationBuilder.CreateIndex(
                name: "IX_Fin_BankStatement_No",
                table: "Fin_BankStatement",
                column: "No");

            migrationBuilder.CreateIndex(
                name: "UX_Fin_BankStatement_AcctPeriod",
                table: "Fin_BankStatement",
                columns: new[] { "TenantId", "BankAccountId", "FiscalPeriodId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Fin_BankStatementLine_Fingerprint",
                table: "Fin_BankStatementLine",
                columns: new[] { "StatementId", "Fingerprint" });

            migrationBuilder.CreateIndex(
                name: "IX_Fin_BankStatementLine_Stmt",
                table: "Fin_BankStatementLine",
                column: "StatementId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Fin_BankImportProfile");

            migrationBuilder.DropTable(
                name: "Fin_BankReconJournalLink");

            migrationBuilder.DropTable(
                name: "Fin_BankReconMatch");

            migrationBuilder.DropTable(
                name: "Fin_BankStatement");

            migrationBuilder.DropTable(
                name: "Fin_BankStatementLine");
        }
    }
}
