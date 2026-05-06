using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CP6.Core.Migrations
{
    /// <inheritdoc />
    public partial class AddBpAndFscPA110 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "FscManagementNo",
                table: "T_QuotationCalc",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FscManagementNo",
                table: "T_EstimateCalc",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Status",
                table: "T_EstimateCalc",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<decimal>(
                name: "TotalAmount",
                table: "T_EstimateCalc",
                type: "decimal(15,4)",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "T_FscChecklist",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FscManagementNo = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    QtnNo = table.Column<string>(type: "nvarchar(11)", maxLength: 11, nullable: false),
                    QtnCalcNo = table.Column<string>(type: "nvarchar(11)", maxLength: 11, nullable: false),
                    CustomerCd = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    StaffCd = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    BaseCd = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    IssueDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    FormatName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    TemplatePath = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ExcelFileName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    FscProductDiv = table.Column<string>(type: "nvarchar(1)", maxLength: 1, nullable: true),
                    Creator = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CreateDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Modifier = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ModifyDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_T_FscChecklist", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "T_WebBusinessPartner",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BpCd = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    BpName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    BpAbbrev = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    BaseCd = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    StdCoCd = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    Ein = table.Column<string>(type: "nvarchar(13)", maxLength: 13, nullable: true),
                    EinType = table.Column<string>(type: "nvarchar(2)", maxLength: 2, nullable: true),
                    LocalPublicCd = table.Column<string>(type: "nvarchar(5)", maxLength: 5, nullable: true),
                    DenzaiNo = table.Column<string>(type: "nvarchar(9)", maxLength: 9, nullable: true),
                    ZipCd = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    Addr1 = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    Addr2 = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    Addr3 = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    Addr4 = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    Tel = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    Fax = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    AreaCd = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: true),
                    SalesStaffCd = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    BusinessStaffCd = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    CustomerFlg = table.Column<bool>(type: "bit", nullable: false),
                    AccountsReceivableFlg = table.Column<bool>(type: "bit", nullable: false),
                    BillingFlg = table.Column<bool>(type: "bit", nullable: false),
                    ReceiptFlg = table.Column<bool>(type: "bit", nullable: false),
                    DeliveryFlg = table.Column<bool>(type: "bit", nullable: false),
                    SupplierFlg = table.Column<bool>(type: "bit", nullable: false),
                    AccountsPayableFlg = table.Column<bool>(type: "bit", nullable: false),
                    PaymentScheduleFlg = table.Column<bool>(type: "bit", nullable: false),
                    PaymentFlg = table.Column<bool>(type: "bit", nullable: false),
                    CreditMgmtFlg = table.Column<bool>(type: "bit", nullable: false),
                    MakerFlg = table.Column<bool>(type: "bit", nullable: false),
                    PaidSupplyFlg = table.Column<bool>(type: "bit", nullable: false),
                    RebuyObligationFlg = table.Column<bool>(type: "bit", nullable: false),
                    BpClass01 = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    BpClass02 = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    BpClass03 = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    BpClass04 = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    BpClass05 = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    BpClass06 = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    BpClass07 = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    BpClass08 = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    BpClass09 = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    BpClass10 = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    SalesAnalysis1 = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    SalesAnalysis2 = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    SalesAnalysis3 = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    ParentCustomerCd = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    UserConverterDiv = table.Column<string>(type: "nvarchar(4)", maxLength: 4, nullable: true),
                    AccountsReceivableCd = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    CreditMgmtCd = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    CustomerDept = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    CustomerContact = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    CustomerContactTitle = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    RecyclingTarget = table.Column<string>(type: "nvarchar(4)", maxLength: 4, nullable: true),
                    SalesPostingDiv = table.Column<string>(type: "nvarchar(4)", maxLength: 4, nullable: true),
                    SheetSalesCalcMethod = table.Column<string>(type: "nvarchar(4)", maxLength: 4, nullable: true),
                    SalesCalcDivM2 = table.Column<string>(type: "nvarchar(4)", maxLength: 4, nullable: true),
                    SalesCalcDivPiece = table.Column<string>(type: "nvarchar(4)", maxLength: 4, nullable: true),
                    FractionCalcDiv = table.Column<string>(type: "nvarchar(4)", maxLength: 4, nullable: true),
                    FullSheetSalesDiv = table.Column<string>(type: "nvarchar(4)", maxLength: 4, nullable: true),
                    SlitterBillingDiv = table.Column<string>(type: "nvarchar(4)", maxLength: 4, nullable: true),
                    SlitterBillingUnitPrice = table.Column<decimal>(type: "decimal(18,4)", nullable: true),
                    SlitterMaxFlow = table.Column<decimal>(type: "decimal(18,4)", nullable: true),
                    PrintMinBilling = table.Column<string>(type: "nvarchar(4)", maxLength: 4, nullable: true),
                    PrintMinBillingBelow = table.Column<decimal>(type: "decimal(18,4)", nullable: true),
                    PrintMinBillingUnit = table.Column<string>(type: "nvarchar(4)", maxLength: 4, nullable: true),
                    LaminateMinBilling = table.Column<string>(type: "nvarchar(4)", maxLength: 4, nullable: true),
                    LaminateMinBillingBelow = table.Column<decimal>(type: "decimal(18,4)", nullable: true),
                    LaminateMinBillingUnit = table.Column<string>(type: "nvarchar(4)", maxLength: 4, nullable: true),
                    LaminateAddRate = table.Column<decimal>(type: "decimal(18,4)", nullable: true),
                    LaminateAddDisplay = table.Column<string>(type: "nvarchar(4)", maxLength: 4, nullable: true),
                    ProcessingMinEstimate = table.Column<string>(type: "nvarchar(4)", maxLength: 4, nullable: true),
                    NewSheetUnitPriceBase = table.Column<string>(type: "nvarchar(4)", maxLength: 4, nullable: true),
                    DeliverySlipOutDiv = table.Column<string>(type: "nvarchar(4)", maxLength: 4, nullable: true),
                    DeliverySlipIssueDiv = table.Column<string>(type: "nvarchar(4)", maxLength: 4, nullable: true),
                    SpecialSlipDiv = table.Column<string>(type: "nvarchar(4)", maxLength: 4, nullable: true),
                    NightLoadDiv = table.Column<string>(type: "nvarchar(4)", maxLength: 4, nullable: true),
                    SizePrintDiv = table.Column<string>(type: "nvarchar(4)", maxLength: 4, nullable: true),
                    DeliveryCalcOutDiv = table.Column<string>(type: "nvarchar(4)", maxLength: 4, nullable: true),
                    DeliveryCalcIssueDiv = table.Column<string>(type: "nvarchar(4)", maxLength: 4, nullable: true),
                    DeliveryCalcOutDiv2 = table.Column<string>(type: "nvarchar(4)", maxLength: 4, nullable: true),
                    DeliveryCalcAddressee = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    DeliveryCalcSender = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    DeliveryCalcZipCd = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    DeliveryCalcAddr1 = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    DeliveryCalcAddr2 = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    DeliveryCalcAddr3 = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    DeliveryCalcAddr4 = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    BillingCd = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    BillingName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ReceiptCd = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    ReceiptName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CreditMgmtArCd = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    BillingClosingDay1 = table.Column<int>(type: "int", nullable: true),
                    BillingPrintDiv = table.Column<string>(type: "nvarchar(4)", maxLength: 4, nullable: true),
                    BillingSealDiv = table.Column<string>(type: "nvarchar(4)", maxLength: 4, nullable: true),
                    ElectronicBilling = table.Column<string>(type: "nvarchar(4)", maxLength: 4, nullable: true),
                    BillingAddressee = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    BillingSender = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    BillingZipCd = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    BillingAddr1 = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    BillingAddr2 = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    BillingAddr3 = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    BillingAddr4 = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    RemittanceName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    BankCd = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    BankBranchCd = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    RemittanceAccount = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    TempAccountDiv = table.Column<string>(type: "nvarchar(4)", maxLength: 4, nullable: true),
                    MainAccountRegDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Drawer = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    CollectionDiv = table.Column<string>(type: "nvarchar(4)", maxLength: 4, nullable: true),
                    CollectionPlannedDay = table.Column<int>(type: "int", nullable: true),
                    BillRotationDiv = table.Column<string>(type: "nvarchar(4)", maxLength: 4, nullable: true),
                    ReceiptAddressee = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ReceiptSender = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    ReceiptZipCd = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    ReceiptAddr1 = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    ReceiptAddr2 = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    ReceiptAddr3 = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    ReceiptAddr4 = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    CollectionNote = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    LogisticsGroupCd = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    LogisticsGroupName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    DeliveryDept = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    DeliveryContact = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    DeliveryContactTitle = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    TruckLengthLimit = table.Column<decimal>(type: "decimal(10,2)", nullable: true),
                    DeliveryTimeFrom = table.Column<string>(type: "nvarchar(8)", maxLength: 8, nullable: true),
                    DeliveryTimeTo = table.Column<string>(type: "nvarchar(8)", maxLength: 8, nullable: true),
                    PlannedShipTime = table.Column<string>(type: "nvarchar(8)", maxLength: 8, nullable: true),
                    SupplierPattern = table.Column<string>(type: "nvarchar(4)", maxLength: 4, nullable: true),
                    SubcontractTargetFlg = table.Column<bool>(type: "bit", nullable: false),
                    SubcontractPriceDiv = table.Column<string>(type: "nvarchar(4)", maxLength: 4, nullable: true),
                    SupplyPostingDiv = table.Column<string>(type: "nvarchar(4)", maxLength: 4, nullable: true),
                    SupplyPriceChangeAllowFlg = table.Column<bool>(type: "bit", nullable: false),
                    DeliveryConfirmFlg = table.Column<bool>(type: "bit", nullable: false),
                    PurchaseFractionDiv = table.Column<string>(type: "nvarchar(4)", maxLength: 4, nullable: true),
                    PurchaseTaxFractionDiv = table.Column<string>(type: "nvarchar(4)", maxLength: 4, nullable: true),
                    PurchaseTaxCd = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    PurchaseTaxPriorityFlg = table.Column<bool>(type: "bit", nullable: false),
                    SupplierCalendarCd = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    SupplyConsignDiv = table.Column<string>(type: "nvarchar(4)", maxLength: 4, nullable: true),
                    PurchaseLotSplitDiv = table.Column<string>(type: "nvarchar(4)", maxLength: 4, nullable: true),
                    PurchasePostingDiv = table.Column<string>(type: "nvarchar(4)", maxLength: 4, nullable: true),
                    PaidSupplyFractionDiv = table.Column<string>(type: "nvarchar(4)", maxLength: 4, nullable: true),
                    PaidSupplyTaxCd = table.Column<string>(type: "nvarchar(4)", maxLength: 4, nullable: true),
                    PaidSupplyTaxPriorityFlg = table.Column<bool>(type: "bit", nullable: false),
                    PaidSupplyAmountFractionDiv = table.Column<string>(type: "nvarchar(4)", maxLength: 4, nullable: true),
                    PaidSupplyTaxFractionDiv = table.Column<string>(type: "nvarchar(4)", maxLength: 4, nullable: true),
                    PaidSupplyTaxCalcDiv = table.Column<string>(type: "nvarchar(4)", maxLength: 4, nullable: true),
                    FscCertificationDiv = table.Column<string>(type: "nvarchar(4)", maxLength: 4, nullable: true),
                    PaymentScheduleCd = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    PaymentCd = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    PaymentScheduleDeptCd = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    PaidEachTimeFlg = table.Column<bool>(type: "bit", nullable: false),
                    PaymentClosingDay1 = table.Column<int>(type: "int", nullable: true),
                    PaymentTaxCalcFlg = table.Column<bool>(type: "bit", nullable: false),
                    PaymentTaxFractionDiv = table.Column<string>(type: "nvarchar(4)", maxLength: 4, nullable: true),
                    BatchPaymentScheduleFlg = table.Column<bool>(type: "bit", nullable: false),
                    PaymentScheduleDelayDays = table.Column<int>(type: "int", nullable: true),
                    GifuInterfaceFlg = table.Column<bool>(type: "bit", nullable: false),
                    McTransferFlg = table.Column<bool>(type: "bit", nullable: false),
                    Creator = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CreateDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Modifier = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ModifyDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_T_WebBusinessPartner", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_T_FscChecklist_FscManagementNo",
                table: "T_FscChecklist",
                column: "FscManagementNo",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_T_FscChecklist_IssueDate_IsDeleted",
                table: "T_FscChecklist",
                columns: new[] { "IssueDate", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_T_FscChecklist_QtnNo_QtnCalcNo",
                table: "T_FscChecklist",
                columns: new[] { "QtnNo", "QtnCalcNo" });

            migrationBuilder.CreateIndex(
                name: "IX_T_WebBusinessPartner_AreaCd",
                table: "T_WebBusinessPartner",
                column: "AreaCd");

            migrationBuilder.CreateIndex(
                name: "IX_T_WebBusinessPartner_BaseCd_IsDeleted",
                table: "T_WebBusinessPartner",
                columns: new[] { "BaseCd", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_T_WebBusinessPartner_BpCd",
                table: "T_WebBusinessPartner",
                column: "BpCd",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_T_WebBusinessPartner_BusinessStaffCd",
                table: "T_WebBusinessPartner",
                column: "BusinessStaffCd");

            migrationBuilder.CreateIndex(
                name: "IX_T_WebBusinessPartner_CreateDate_IsDeleted",
                table: "T_WebBusinessPartner",
                columns: new[] { "CreateDate", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_T_WebBusinessPartner_CustomerFlg",
                table: "T_WebBusinessPartner",
                column: "CustomerFlg");

            migrationBuilder.CreateIndex(
                name: "IX_T_WebBusinessPartner_SalesStaffCd",
                table: "T_WebBusinessPartner",
                column: "SalesStaffCd");

            migrationBuilder.CreateIndex(
                name: "IX_T_WebBusinessPartner_Status_IsDeleted",
                table: "T_WebBusinessPartner",
                columns: new[] { "Status", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_T_WebBusinessPartner_SupplierFlg",
                table: "T_WebBusinessPartner",
                column: "SupplierFlg");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "T_FscChecklist");

            migrationBuilder.DropTable(
                name: "T_WebBusinessPartner");

            migrationBuilder.DropColumn(
                name: "FscManagementNo",
                table: "T_QuotationCalc");

            migrationBuilder.DropColumn(
                name: "FscManagementNo",
                table: "T_EstimateCalc");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "T_EstimateCalc");

            migrationBuilder.DropColumn(
                name: "TotalAmount",
                table: "T_EstimateCalc");
        }
    }
}
