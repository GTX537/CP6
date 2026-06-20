using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CP6.Core.Migrations
{
    /// <inheritdoc />
    public partial class A3FixedAsset : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Fin_AssetCard",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AssetNo = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    SpecModel = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CategoryId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OriginalValue = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    SalvageRate = table.Column<decimal>(type: "decimal(7,4)", nullable: false),
                    SalvageValue = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Method = table.Column<int>(type: "int", nullable: false),
                    UsefulLifeMonths = table.Column<int>(type: "int", nullable: false),
                    TotalWorkload = table.Column<decimal>(type: "decimal(18,4)", nullable: true),
                    WorkloadUnit = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    AcquisitionDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DepreciationStartPeriod = table.Column<string>(type: "nvarchar(7)", maxLength: 7, nullable: true),
                    AccumulatedDepreciation = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    DepreciatedPeriods = table.Column<int>(type: "int", nullable: false),
                    DeprecExpenseAccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CostCenterId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    MachineId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DeptId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    Location = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Custodian = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    IsOpeningImport = table.Column<bool>(type: "bit", nullable: false),
                    Remarks = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    Creator = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CreateDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Modifier = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ModifyDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Fin_AssetCard", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Fin_AssetCategory",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ParentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Level = table.Column<int>(type: "int", nullable: false),
                    DefaultMethod = table.Column<int>(type: "int", nullable: false),
                    DefaultUsefulLifeMonths = table.Column<int>(type: "int", nullable: false),
                    DefaultSalvageRate = table.Column<decimal>(type: "decimal(7,4)", nullable: false),
                    AssetAccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AccumDeprecAccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DeprecExpenseAccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
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
                    table.PrimaryKey("PK_Fin_AssetCategory", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Fin_AssetDisposal",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    No = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    AssetCardId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DisposalType = table.Column<int>(type: "int", nullable: false),
                    DisposalDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    FiscalPeriodId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OriginalValue = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    AccumulatedDepreciation = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    NetBookValue = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Proceeds = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    TaxAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    DisposalExpense = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    NetGainLoss = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    ClearingAccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    GainLossAccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReceiptBankAccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    PriorStatus = table.Column<int>(type: "int", nullable: true),
                    JournalEntryId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    FinalDeprecEntryId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ConfirmedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ConfirmedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ReversedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ReversedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    Creator = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CreateDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Modifier = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ModifyDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Fin_AssetDisposal", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Fin_DepreciationEntry",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RunId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AssetCardId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FiscalPeriodId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Method = table.Column<int>(type: "int", nullable: false),
                    DepreciationAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    OpeningAccumulated = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    ClosingAccumulated = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    OpeningNetValue = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    ClosingNetValue = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    DeprecExpenseAccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AccumDeprecAccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CostCenterId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    WorkloadThisPeriod = table.Column<decimal>(type: "decimal(18,4)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    Creator = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CreateDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Modifier = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ModifyDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Fin_DepreciationEntry", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Fin_DepreciationRun",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    No = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    FiscalPeriodId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PeriodYearMonth = table.Column<string>(type: "nvarchar(7)", maxLength: 7, nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    RunMode = table.Column<int>(type: "int", nullable: false),
                    TotalAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    AssetCount = table.Column<int>(type: "int", nullable: false),
                    JournalEntryId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RunAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RunBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    PostedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    PostedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ReversedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ReversedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    Creator = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CreateDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Modifier = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ModifyDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Fin_DepreciationRun", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Fin_AssetCard_CategoryId",
                table: "Fin_AssetCard",
                column: "CategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_Fin_AssetCard_MachineId",
                table: "Fin_AssetCard",
                column: "MachineId");

            migrationBuilder.CreateIndex(
                name: "IX_Fin_AssetCard_Status",
                table: "Fin_AssetCard",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "UX_Fin_AssetCard_AssetNo",
                table: "Fin_AssetCard",
                columns: new[] { "TenantId", "AssetNo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Fin_AssetCategory_ParentId",
                table: "Fin_AssetCategory",
                column: "ParentId");

            migrationBuilder.CreateIndex(
                name: "UX_Fin_AssetCategory_Code",
                table: "Fin_AssetCategory",
                columns: new[] { "TenantId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Fin_AssetDisposal_AssetCardId",
                table: "Fin_AssetDisposal",
                column: "AssetCardId");

            migrationBuilder.CreateIndex(
                name: "IX_Fin_AssetDisposal_Status",
                table: "Fin_AssetDisposal",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "UX_Fin_AssetDisposal_No",
                table: "Fin_AssetDisposal",
                columns: new[] { "TenantId", "No" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Fin_DepreciationEntry_AssetCardId",
                table: "Fin_DepreciationEntry",
                column: "AssetCardId");

            migrationBuilder.CreateIndex(
                name: "IX_Fin_DepreciationEntry_AssetCardId_FiscalPeriodId",
                table: "Fin_DepreciationEntry",
                columns: new[] { "AssetCardId", "FiscalPeriodId" });

            migrationBuilder.CreateIndex(
                name: "UX_Fin_DepreciationEntry_RunAsset",
                table: "Fin_DepreciationEntry",
                columns: new[] { "TenantId", "RunId", "AssetCardId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Fin_DepreciationRun_FiscalPeriodId_Status",
                table: "Fin_DepreciationRun",
                columns: new[] { "FiscalPeriodId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_Fin_DepreciationRun_No",
                table: "Fin_DepreciationRun",
                column: "No");

            migrationBuilder.CreateIndex(
                name: "UX_Fin_DepreciationRun_PeriodSingleBatch",
                table: "Fin_DepreciationRun",
                columns: new[] { "TenantId", "FiscalPeriodId" },
                unique: true,
                filter: "[RunMode] IN (1,2,3) AND [Status] <> 2");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Fin_AssetCard");

            migrationBuilder.DropTable(
                name: "Fin_AssetCategory");

            migrationBuilder.DropTable(
                name: "Fin_AssetDisposal");

            migrationBuilder.DropTable(
                name: "Fin_DepreciationEntry");

            migrationBuilder.DropTable(
                name: "Fin_DepreciationRun");
        }
    }
}
