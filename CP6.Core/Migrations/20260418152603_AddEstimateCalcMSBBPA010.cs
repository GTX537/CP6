using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CP6.Core.Migrations
{
    /// <inheritdoc />
    public partial class AddEstimateCalcMSBBPA010 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "M_Base",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BaseCd = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    BaseName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    IsAdminBase = table.Column<bool>(type: "bit", nullable: false),
                    DiscountThreshold = table.Column<decimal>(type: "decimal(10,4)", nullable: true),
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
                    table.PrimaryKey("PK_M_Base", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "M_GenericCode",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    GroupCode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Code = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Attr1 = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Attr2 = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Num1 = table.Column<decimal>(type: "decimal(18,4)", nullable: true),
                    Num2 = table.Column<decimal>(type: "decimal(18,4)", nullable: true),
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
                    table.PrimaryKey("PK_M_GenericCode", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "M_Staff",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StaffCd = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    StaffName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    BaseCd = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    SysUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
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
                    table.PrimaryKey("PK_M_Staff", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "T_EstimateCalc",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    QtnCalcNo = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    QtnCalcNoMain = table.Column<int>(type: "int", nullable: false),
                    QtnCalcNoBranch = table.Column<int>(type: "int", nullable: false),
                    RefQtnCalcNo = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    ProCd = table.Column<string>(type: "nvarchar(15)", maxLength: 15, nullable: true),
                    QtnDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    QtnBaseCd = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    OrderBaseCd = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    StaffCd = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    CustomerCd = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    ProjectNoParent = table.Column<string>(type: "nvarchar(15)", maxLength: 15, nullable: true),
                    ProjectNoChild = table.Column<string>(type: "nvarchar(15)", maxLength: 15, nullable: true),
                    ProjectNoMaterial = table.Column<string>(type: "nvarchar(15)", maxLength: 15, nullable: true),
                    OrderType = table.Column<string>(type: "nvarchar(4)", maxLength: 4, nullable: true),
                    ProductCategoryBig = table.Column<string>(type: "nvarchar(4)", maxLength: 4, nullable: true),
                    ProductCategoryMid = table.Column<string>(type: "nvarchar(4)", maxLength: 4, nullable: true),
                    ProductCategorySml = table.Column<string>(type: "nvarchar(4)", maxLength: 4, nullable: true),
                    CustomerProductName1 = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CustomerProductName2 = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    OrderQty = table.Column<decimal>(type: "decimal(10,2)", nullable: true),
                    OrderYm = table.Column<string>(type: "nvarchar(6)", maxLength: 6, nullable: true),
                    ParentChildDiv = table.Column<string>(type: "nvarchar(2)", maxLength: 2, nullable: true),
                    FscProductDiv = table.Column<string>(type: "nvarchar(4)", maxLength: 4, nullable: true),
                    FscMaterialDiv = table.Column<string>(type: "nvarchar(4)", maxLength: 4, nullable: true),
                    SheetFlute = table.Column<string>(type: "nvarchar(4)", maxLength: 4, nullable: true),
                    PaperCdF = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    PrintCdF = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    EmbossCdF = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    PatternCntF = table.Column<decimal>(type: "decimal(10,2)", nullable: true),
                    PaperCdC = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    PrintCdC = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    EmbossCdC = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    PaperCdB = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    PrintCdB = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    EmbossCdB = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    PatternCntB = table.Column<decimal>(type: "decimal(10,2)", nullable: true),
                    SheetPrint = table.Column<string>(type: "nvarchar(4)", maxLength: 4, nullable: true),
                    BladeWidth = table.Column<decimal>(type: "decimal(10,2)", nullable: true),
                    BladeFlow = table.Column<decimal>(type: "decimal(10,2)", nullable: true),
                    GutterFb = table.Column<decimal>(type: "decimal(10,2)", nullable: true),
                    GutterLr = table.Column<decimal>(type: "decimal(10,2)", nullable: true),
                    SheetDimW = table.Column<decimal>(type: "decimal(10,2)", nullable: true),
                    SheetDimF = table.Column<decimal>(type: "decimal(10,2)", nullable: true),
                    FinalMachineProc = table.Column<string>(type: "nvarchar(4)", maxLength: 4, nullable: true),
                    ProductShape1 = table.Column<string>(type: "nvarchar(4)", maxLength: 4, nullable: true),
                    ProductShape2 = table.Column<string>(type: "nvarchar(4)", maxLength: 4, nullable: true),
                    DistDiv = table.Column<string>(type: "nvarchar(4)", maxLength: 4, nullable: true),
                    RecyclePayment = table.Column<string>(type: "nvarchar(1)", maxLength: 1, nullable: true),
                    IdMark = table.Column<string>(type: "nvarchar(4)", maxLength: 4, nullable: true),
                    AdShape = table.Column<string>(type: "nvarchar(4)", maxLength: 4, nullable: true),
                    StrategicDiv01 = table.Column<bool>(type: "bit", nullable: false),
                    StrategicDiv02 = table.Column<bool>(type: "bit", nullable: false),
                    StrategicDiv03 = table.Column<bool>(type: "bit", nullable: false),
                    StrategicDiv04 = table.Column<bool>(type: "bit", nullable: false),
                    StrategicDiv05 = table.Column<bool>(type: "bit", nullable: false),
                    StrategicDiv06 = table.Column<bool>(type: "bit", nullable: false),
                    StrategicDiv07 = table.Column<bool>(type: "bit", nullable: false),
                    StrategicDiv08 = table.Column<bool>(type: "bit", nullable: false),
                    StrategicDiv09 = table.Column<bool>(type: "bit", nullable: false),
                    StrategicDiv10 = table.Column<bool>(type: "bit", nullable: false),
                    PrintNote = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    MfgNote = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    SlipNote = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    DeliveryNote = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    ShipNote1 = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    ShipNote2 = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    EstimateQty01 = table.Column<decimal>(type: "decimal(10,2)", nullable: true),
                    EstimateQty02 = table.Column<decimal>(type: "decimal(10,2)", nullable: true),
                    EstimateQty03 = table.Column<decimal>(type: "decimal(10,2)", nullable: true),
                    EstimateQty04 = table.Column<decimal>(type: "decimal(10,2)", nullable: true),
                    EstimateQty05 = table.Column<decimal>(type: "decimal(10,2)", nullable: true),
                    EstimateQty06 = table.Column<decimal>(type: "decimal(10,2)", nullable: true),
                    EstimateQty07 = table.Column<decimal>(type: "decimal(10,2)", nullable: true),
                    EstimateQty08 = table.Column<decimal>(type: "decimal(10,2)", nullable: true),
                    ProposalLot1 = table.Column<decimal>(type: "decimal(10,2)", nullable: true),
                    ProposalLot2 = table.Column<decimal>(type: "decimal(10,2)", nullable: true),
                    Unit = table.Column<string>(type: "nvarchar(4)", maxLength: 4, nullable: true),
                    DecidedQty = table.Column<decimal>(type: "decimal(10,2)", nullable: true),
                    PalletCnt01 = table.Column<decimal>(type: "decimal(10,2)", nullable: true),
                    PalletCnt02 = table.Column<decimal>(type: "decimal(10,2)", nullable: true),
                    PalletCnt03 = table.Column<decimal>(type: "decimal(10,2)", nullable: true),
                    PalletCnt04 = table.Column<decimal>(type: "decimal(10,2)", nullable: true),
                    PalletCnt05 = table.Column<decimal>(type: "decimal(10,2)", nullable: true),
                    PalletCnt06 = table.Column<decimal>(type: "decimal(10,2)", nullable: true),
                    PalletCnt07 = table.Column<decimal>(type: "decimal(10,2)", nullable: true),
                    PalletCnt08 = table.Column<decimal>(type: "decimal(10,2)", nullable: true),
                    QtnDiv = table.Column<string>(type: "nvarchar(2)", maxLength: 2, nullable: true),
                    EstimateSqm = table.Column<decimal>(type: "decimal(12,4)", nullable: true),
                    StandardUnitPrice = table.Column<decimal>(type: "decimal(15,4)", nullable: true),
                    EstimateUnitPrice = table.Column<decimal>(type: "decimal(15,4)", nullable: true),
                    ConfirmedUnitPrice = table.Column<decimal>(type: "decimal(15,4)", nullable: true),
                    Creator = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CreateDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Modifier = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ModifyDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_T_EstimateCalc", x => x.Id);
                    table.UniqueConstraint("AK_T_EstimateCalc_QtnCalcNo", x => x.QtnCalcNo);
                });

            migrationBuilder.CreateTable(
                name: "T_EstimateCalcProcess",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    QtnCalcNo = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    SeqNo = table.Column<int>(type: "int", nullable: false),
                    ProcessCd = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    ProcessName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    TaskCd = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    TaskName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    WgCd = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    MfgLocation = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Spec1Label = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Spec1Val = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Spec2Label = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Spec2Val = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Spec3Label = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Spec3Val = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Spec4Label = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Spec4Val = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Spec5Label = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Spec5Val = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Spec6Label = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Spec6Val = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Spec7Label = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Spec7Val = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    PlateNo = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    ProcNote1 = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ProcNote2 = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Creator = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CreateDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Modifier = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ModifyDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_T_EstimateCalcProcess", x => x.Id);
                    table.ForeignKey(
                        name: "FK_T_EstimateCalcProcess_T_EstimateCalc_QtnCalcNo",
                        column: x => x.QtnCalcNo,
                        principalTable: "T_EstimateCalc",
                        principalColumn: "QtnCalcNo",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_M_Base_BaseCd",
                table: "M_Base",
                column: "BaseCd",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_M_GenericCode_GroupCode_Code",
                table: "M_GenericCode",
                columns: new[] { "GroupCode", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_M_Staff_BaseCd",
                table: "M_Staff",
                column: "BaseCd");

            migrationBuilder.CreateIndex(
                name: "IX_M_Staff_StaffCd",
                table: "M_Staff",
                column: "StaffCd",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_T_EstimateCalc_CustomerCd_IsDeleted",
                table: "T_EstimateCalc",
                columns: new[] { "CustomerCd", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_T_EstimateCalc_QtnCalcNo",
                table: "T_EstimateCalc",
                column: "QtnCalcNo",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_T_EstimateCalc_QtnDate_IsDeleted",
                table: "T_EstimateCalc",
                columns: new[] { "QtnDate", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_T_EstimateCalcProcess_QtnCalcNo_SeqNo",
                table: "T_EstimateCalcProcess",
                columns: new[] { "QtnCalcNo", "SeqNo" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "M_Base");

            migrationBuilder.DropTable(
                name: "M_GenericCode");

            migrationBuilder.DropTable(
                name: "M_Staff");

            migrationBuilder.DropTable(
                name: "T_EstimateCalcProcess");

            migrationBuilder.DropTable(
                name: "T_EstimateCalc");
        }
    }
}
