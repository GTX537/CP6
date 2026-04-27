using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CP6.Core.Migrations
{
    /// <inheritdoc />
    public partial class AddProductMasterMSBBPA050 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "T_ProductMaster",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProductCd = table.Column<string>(type: "nvarchar(15)", maxLength: 15, nullable: false),
                    ItemCd = table.Column<string>(type: "nvarchar(15)", maxLength: 15, nullable: false),
                    Branch1 = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    Branch2 = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    Branch3 = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    ProjectNoParent = table.Column<string>(type: "nvarchar(15)", maxLength: 15, nullable: true),
                    ProjectNoChild = table.Column<string>(type: "nvarchar(15)", maxLength: 15, nullable: true),
                    ProjectNoMaterial = table.Column<string>(type: "nvarchar(15)", maxLength: 15, nullable: true),
                    QuotationNo = table.Column<string>(type: "nvarchar(11)", maxLength: 11, nullable: true),
                    EstimateCalcNo = table.Column<string>(type: "nvarchar(11)", maxLength: 11, nullable: true),
                    RefEstimateCalcNo = table.Column<string>(type: "nvarchar(11)", maxLength: 11, nullable: true),
                    CustomerCd = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    SetProductCd = table.Column<string>(type: "nvarchar(15)", maxLength: 15, nullable: false),
                    SetProductName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ParentChildDiv = table.Column<string>(type: "nvarchar(1)", maxLength: 1, nullable: false),
                    CustomerItemName1 = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CustomerItemName2 = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CustomerPartNo = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    CpItemName1 = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CpItemName2 = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    JanCode = table.Column<string>(type: "nvarchar(13)", maxLength: 13, nullable: true),
                    SetRatio = table.Column<decimal>(type: "decimal(18,8)", nullable: false),
                    ProductUsage = table.Column<string>(type: "nvarchar(4)", maxLength: 4, nullable: true),
                    DistributionDiv = table.Column<string>(type: "nvarchar(4)", maxLength: 4, nullable: true),
                    ConfidentialInfo = table.Column<string>(type: "nvarchar(4)", maxLength: 4, nullable: true),
                    SeizureDiv = table.Column<string>(type: "nvarchar(4)", maxLength: 4, nullable: true),
                    ImportanceDiv = table.Column<string>(type: "nvarchar(4)", maxLength: 4, nullable: true),
                    MChange = table.Column<string>(type: "nvarchar(4)", maxLength: 4, nullable: true),
                    QualityDiv = table.Column<string>(type: "nvarchar(4)", maxLength: 4, nullable: true),
                    ShipInspection = table.Column<string>(type: "nvarchar(4)", maxLength: 4, nullable: true),
                    ProductShape = table.Column<string>(type: "nvarchar(4)", maxLength: 4, nullable: true),
                    UnescoMark = table.Column<string>(type: "nvarchar(4)", maxLength: 4, nullable: true),
                    OrigamiMark = table.Column<string>(type: "nvarchar(4)", maxLength: 4, nullable: true),
                    FourMContract = table.Column<string>(type: "nvarchar(4)", maxLength: 4, nullable: true),
                    TkpWrinkleStd = table.Column<string>(type: "nvarchar(4)", maxLength: 4, nullable: true),
                    FoodSafety = table.Column<string>(type: "nvarchar(4)", maxLength: 4, nullable: true),
                    AdShape = table.Column<string>(type: "nvarchar(4)", maxLength: 4, nullable: true),
                    StrategicDiv01 = table.Column<string>(type: "nvarchar(1)", maxLength: 1, nullable: true),
                    StrategicDiv02 = table.Column<string>(type: "nvarchar(1)", maxLength: 1, nullable: true),
                    StrategicDiv03 = table.Column<string>(type: "nvarchar(1)", maxLength: 1, nullable: true),
                    StrategicDiv04 = table.Column<string>(type: "nvarchar(1)", maxLength: 1, nullable: true),
                    StrategicDiv05 = table.Column<string>(type: "nvarchar(1)", maxLength: 1, nullable: true),
                    StrategicDiv06 = table.Column<string>(type: "nvarchar(1)", maxLength: 1, nullable: true),
                    StrategicDiv07 = table.Column<string>(type: "nvarchar(1)", maxLength: 1, nullable: true),
                    StrategicDiv08 = table.Column<string>(type: "nvarchar(1)", maxLength: 1, nullable: true),
                    StrategicDiv09 = table.Column<string>(type: "nvarchar(1)", maxLength: 1, nullable: true),
                    StrategicDiv10 = table.Column<string>(type: "nvarchar(1)", maxLength: 1, nullable: true),
                    FscProductDiv = table.Column<string>(type: "nvarchar(4)", maxLength: 4, nullable: true),
                    FscMaterialDiv = table.Column<string>(type: "nvarchar(4)", maxLength: 4, nullable: true),
                    FscManagementNo = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    RecyclingPayment = table.Column<string>(type: "nvarchar(2)", maxLength: 2, nullable: true),
                    IdMark = table.Column<string>(type: "nvarchar(2)", maxLength: 2, nullable: true),
                    PaperUsageG = table.Column<decimal>(type: "decimal(21,8)", nullable: true),
                    PlasticUsageG = table.Column<decimal>(type: "decimal(21,8)", nullable: true),
                    GlassUsageG = table.Column<decimal>(type: "decimal(21,8)", nullable: true),
                    PetUsageG = table.Column<decimal>(type: "decimal(21,8)", nullable: true),
                    PackPaperUsageG = table.Column<decimal>(type: "decimal(21,8)", nullable: true),
                    PackPlasticUsageG = table.Column<decimal>(type: "decimal(21,8)", nullable: true),
                    DesignProposalNo = table.Column<string>(type: "nvarchar(11)", maxLength: 11, nullable: true),
                    OrderType = table.Column<string>(type: "nvarchar(4)", maxLength: 4, nullable: true),
                    SheetFlute = table.Column<string>(type: "nvarchar(4)", maxLength: 4, nullable: true),
                    PaperCdF = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    PrintCdF = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    EmbossCdF = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    MakerCdF = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    PaperCdC = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    PrintCdC = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    EmbossCdC = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    PaperCdB = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    PrintCdB = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    EmbossCdB = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    MakerCdB = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    SheetPrint = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    BladeWidth = table.Column<decimal>(type: "decimal(21,8)", nullable: true),
                    BladeFlow = table.Column<decimal>(type: "decimal(21,8)", nullable: true),
                    GutterFb = table.Column<decimal>(type: "decimal(21,8)", nullable: true),
                    GutterLr = table.Column<decimal>(type: "decimal(21,8)", nullable: true),
                    SheetDimW = table.Column<decimal>(type: "decimal(21,8)", nullable: true),
                    SheetDimF = table.Column<decimal>(type: "decimal(21,8)", nullable: true),
                    FinalMachineProcess = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    PrintNote = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    MfgNote = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    SlipNote = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    DeliveryNote = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ShipNote1 = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ShipNote2 = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ProductCatBig = table.Column<string>(type: "nvarchar(4)", maxLength: 4, nullable: true),
                    ProductCatMid = table.Column<string>(type: "nvarchar(4)", maxLength: 4, nullable: true),
                    ProductCatSml = table.Column<string>(type: "nvarchar(4)", maxLength: 4, nullable: true),
                    SalesPriceDiv = table.Column<string>(type: "nvarchar(1)", maxLength: 1, nullable: false),
                    QtyUnit = table.Column<string>(type: "nvarchar(4)", maxLength: 4, nullable: true),
                    UnitPriceUnit = table.Column<string>(type: "nvarchar(4)", maxLength: 4, nullable: true),
                    PurchaseVendor = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    FreightBilling = table.Column<string>(type: "nvarchar(1)", maxLength: 1, nullable: false),
                    FixedShipment = table.Column<string>(type: "nvarchar(4)", maxLength: 4, nullable: true),
                    DeliveryReserve = table.Column<decimal>(type: "decimal(21,8)", nullable: true),
                    SalesSample = table.Column<decimal>(type: "decimal(21,8)", nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    WfApprovalFlg = table.Column<bool>(type: "bit", nullable: false),
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
                    table.PrimaryKey("PK_T_ProductMaster", x => x.Id);
                    table.UniqueConstraint("AK_T_ProductMaster_ProductCd", x => x.ProductCd);
                });

            migrationBuilder.CreateTable(
                name: "T_ProductCoProduct",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProductCd = table.Column<string>(type: "nvarchar(15)", maxLength: 15, nullable: false),
                    ProcessCd = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    RowNo = table.Column<int>(type: "int", nullable: false),
                    CoProductName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    QtyRatio = table.Column<decimal>(type: "decimal(21,8)", nullable: false),
                    NextProcessCd = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    Creator = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CreateDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Modifier = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ModifyDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_T_ProductCoProduct", x => x.Id);
                    table.ForeignKey(
                        name: "FK_T_ProductCoProduct_T_ProductMaster_ProductCd",
                        column: x => x.ProductCd,
                        principalTable: "T_ProductMaster",
                        principalColumn: "ProductCd",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "T_ProductLotPrice",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProductCd = table.Column<string>(type: "nvarchar(15)", maxLength: 15, nullable: false),
                    DetailNo = table.Column<int>(type: "int", nullable: false),
                    ItemCd = table.Column<string>(type: "nvarchar(15)", maxLength: 15, nullable: true),
                    Branch1 = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    Branch2 = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    Branch3 = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    CurrentPriceDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    NewPriceDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CurrentBranchDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    NewBranchDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LotQty = table.Column<decimal>(type: "decimal(21,8)", nullable: false),
                    CurrentSetPrice = table.Column<decimal>(type: "decimal(21,8)", nullable: true),
                    NewSetPrice = table.Column<decimal>(type: "decimal(21,8)", nullable: true),
                    CurrentUnitPrice = table.Column<decimal>(type: "decimal(21,8)", nullable: true),
                    NewUnitPrice = table.Column<decimal>(type: "decimal(21,8)", nullable: true),
                    CurrentBranchPrice = table.Column<decimal>(type: "decimal(21,8)", nullable: true),
                    NewBranchPrice = table.Column<decimal>(type: "decimal(21,8)", nullable: true),
                    PurchasePrice = table.Column<decimal>(type: "decimal(21,8)", nullable: true),
                    PriceApplyBasis = table.Column<string>(type: "nvarchar(1)", maxLength: 1, nullable: true),
                    Creator = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CreateDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Modifier = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ModifyDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_T_ProductLotPrice", x => x.Id);
                    table.ForeignKey(
                        name: "FK_T_ProductLotPrice_T_ProductMaster_ProductCd",
                        column: x => x.ProductCd,
                        principalTable: "T_ProductMaster",
                        principalColumn: "ProductCd",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "T_ProductMaterial",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProductCd = table.Column<string>(type: "nvarchar(15)", maxLength: 15, nullable: false),
                    ProcessCd = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    MaterialCd = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    MaterialTypeDiv = table.Column<string>(type: "nvarchar(1)", maxLength: 1, nullable: false),
                    ItemCd = table.Column<string>(type: "nvarchar(15)", maxLength: 15, nullable: true),
                    Branch1 = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    Branch2 = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    Branch3 = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    SupplyDiv = table.Column<string>(type: "nvarchar(4)", maxLength: 4, nullable: true),
                    SupplyPrice = table.Column<decimal>(type: "decimal(21,8)", nullable: true),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    Creator = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CreateDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Modifier = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ModifyDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_T_ProductMaterial", x => x.Id);
                    table.ForeignKey(
                        name: "FK_T_ProductMaterial_T_ProductMaster_ProductCd",
                        column: x => x.ProductCd,
                        principalTable: "T_ProductMaster",
                        principalColumn: "ProductCd",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "T_ProductProcess",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProductCd = table.Column<string>(type: "nvarchar(15)", maxLength: 15, nullable: false),
                    TaskCd = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    ProcessCd = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    TopItemCd = table.Column<string>(type: "nvarchar(15)", maxLength: 15, nullable: true),
                    TopBranch1 = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    TopBranch2 = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    TopBranch3 = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    ItemCd = table.Column<string>(type: "nvarchar(15)", maxLength: 15, nullable: true),
                    Branch1 = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    Branch2 = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    Branch3 = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    WgCd = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    MachineOrVendor = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    MachineFixedFlg = table.Column<bool>(type: "bit", nullable: false),
                    CpDeliveryDiv = table.Column<string>(type: "nvarchar(4)", maxLength: 4, nullable: true),
                    Spec01 = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    Spec02 = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    Spec03 = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    Spec04 = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    Spec05 = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    Spec06 = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    Spec07 = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    Spec08 = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    Spec09 = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    Spec10 = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    PlateNo1 = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    PlateNo2 = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    PlateNo3 = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    Consumable1 = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    Consumable2 = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    Consumable3 = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    PurchasePrice = table.Column<decimal>(type: "decimal(21,8)", nullable: true),
                    FixedPrice = table.Column<decimal>(type: "decimal(21,8)", nullable: true),
                    LossRate = table.Column<decimal>(type: "decimal(21,8)", nullable: true),
                    MachineCount = table.Column<decimal>(type: "decimal(21,8)", nullable: true),
                    LeadTime = table.Column<decimal>(type: "decimal(21,8)", nullable: true),
                    ProcessNote1 = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ProcessNote2 = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    StorageDest = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    ManufOrderPrio1 = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    ManufOrderPrio2 = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    ManufOrderPrio3 = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    ManufOrderPrio4 = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    ManufOrderPrio5 = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    ManufOrderPrio6 = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    ManufOrderPrio7 = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    ManufOrderPrio8 = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    Creator = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CreateDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Modifier = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ModifyDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_T_ProductProcess", x => x.Id);
                    table.ForeignKey(
                        name: "FK_T_ProductProcess_T_ProductMaster_ProductCd",
                        column: x => x.ProductCd,
                        principalTable: "T_ProductMaster",
                        principalColumn: "ProductCd",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_T_ProductCoProduct_ProductCd_ProcessCd_RowNo",
                table: "T_ProductCoProduct",
                columns: new[] { "ProductCd", "ProcessCd", "RowNo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_T_ProductLotPrice_ProductCd_DetailNo",
                table: "T_ProductLotPrice",
                columns: new[] { "ProductCd", "DetailNo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_T_ProductMaster_CustomerCd_IsDeleted",
                table: "T_ProductMaster",
                columns: new[] { "CustomerCd", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_T_ProductMaster_EstimateCalcNo_IsDeleted",
                table: "T_ProductMaster",
                columns: new[] { "EstimateCalcNo", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_T_ProductMaster_ItemCd",
                table: "T_ProductMaster",
                column: "ItemCd");

            migrationBuilder.CreateIndex(
                name: "IX_T_ProductMaster_ProductCd",
                table: "T_ProductMaster",
                column: "ProductCd",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_T_ProductMaster_ProjectNoParent_ProjectNoChild",
                table: "T_ProductMaster",
                columns: new[] { "ProjectNoParent", "ProjectNoChild" });

            migrationBuilder.CreateIndex(
                name: "IX_T_ProductMaster_QuotationNo_IsDeleted",
                table: "T_ProductMaster",
                columns: new[] { "QuotationNo", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_T_ProductMaster_SetProductCd_IsDeleted",
                table: "T_ProductMaster",
                columns: new[] { "SetProductCd", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_T_ProductMaster_Status_IsDeleted",
                table: "T_ProductMaster",
                columns: new[] { "Status", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_T_ProductMaterial_ProductCd_ProcessCd_MaterialCd",
                table: "T_ProductMaterial",
                columns: new[] { "ProductCd", "ProcessCd", "MaterialCd" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_T_ProductMaterial_ProductCd_SortOrder",
                table: "T_ProductMaterial",
                columns: new[] { "ProductCd", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_T_ProductProcess_ProcessCd",
                table: "T_ProductProcess",
                column: "ProcessCd");

            migrationBuilder.CreateIndex(
                name: "IX_T_ProductProcess_ProductCd_SortOrder",
                table: "T_ProductProcess",
                columns: new[] { "ProductCd", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_T_ProductProcess_ProductCd_TaskCd",
                table: "T_ProductProcess",
                columns: new[] { "ProductCd", "TaskCd" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "T_ProductCoProduct");

            migrationBuilder.DropTable(
                name: "T_ProductLotPrice");

            migrationBuilder.DropTable(
                name: "T_ProductMaterial");

            migrationBuilder.DropTable(
                name: "T_ProductProcess");

            migrationBuilder.DropTable(
                name: "T_ProductMaster");
        }
    }
}
