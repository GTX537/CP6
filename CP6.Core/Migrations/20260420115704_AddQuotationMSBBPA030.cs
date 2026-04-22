using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CP6.Core.Migrations
{
    /// <inheritdoc />
    public partial class AddQuotationMSBBPA030 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "T_Quotation",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    QtnNo = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    QtnNoMain = table.Column<int>(type: "int", nullable: false),
                    QtnNoBranch = table.Column<int>(type: "int", nullable: false),
                    RefQtnNo = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    BaseCd = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    StaffCd = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    CustomerCd = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    CustomerName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ProjectNoParent = table.Column<string>(type: "nvarchar(15)", maxLength: 15, nullable: true),
                    ProjectNoChild = table.Column<string>(type: "nvarchar(15)", maxLength: 15, nullable: true),
                    ProjectNoMaterial = table.Column<string>(type: "nvarchar(15)", maxLength: 15, nullable: true),
                    FscMgmtNo = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    FscChecklistDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ContactPerson = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    DeliveryLocation = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    DeliveryDeadline = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Freight = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    PaymentCondition = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ValidityPeriod = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    QtnNote01 = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    QtnNote02 = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    QtnNote03 = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    QtnNote04 = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    QtnNote05 = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    QtnNote06 = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    QtnNote07 = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    QtnNote08 = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    QtnNote09 = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    QtnNote10 = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    QtnNote11 = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    QtnNote12 = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    QtnNote13 = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    QtnNote14 = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    QtnNote15 = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    DimensionPrint = table.Column<string>(type: "nvarchar(4)", maxLength: 4, nullable: true),
                    CalcNote01 = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    CalcNote02 = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    CalcNote03 = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    CalcNote04 = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    CalcNote05 = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    CalcNote06 = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    CalcNote07 = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    CalcNote08 = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    QtnIssueDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CalcIssueDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    TotalAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    PrintTotalFlg = table.Column<bool>(type: "bit", nullable: false),
                    EstimateCheckFlg = table.Column<int>(type: "int", nullable: false),
                    EstimateCheckDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    MasterConfirmFlg = table.Column<int>(type: "int", nullable: false),
                    MasterConfirmDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Memo1 = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Memo2 = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Memo3 = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Creator = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CreateDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Modifier = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ModifyDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_T_Quotation", x => x.Id);
                    table.UniqueConstraint("AK_T_Quotation_QtnNo", x => x.QtnNo);
                });

            migrationBuilder.CreateTable(
                name: "T_QuotationCalc",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    QtnNo = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    QtnCalcNo = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    EstimateCheckFlg = table.Column<int>(type: "int", nullable: false),
                    EstimateCheckDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    MasterConfirmFlg = table.Column<int>(type: "int", nullable: false),
                    MasterConfirmDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Creator = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CreateDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Modifier = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ModifyDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_T_QuotationCalc", x => x.Id);
                    table.ForeignKey(
                        name: "FK_T_QuotationCalc_T_Quotation_QtnNo",
                        column: x => x.QtnNo,
                        principalTable: "T_Quotation",
                        principalColumn: "QtnNo",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "T_QuotationDetail",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    QtnNo = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    DetailNo = table.Column<int>(type: "int", nullable: false),
                    ItemName1 = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ItemName2 = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Quantity = table.Column<decimal>(type: "decimal(12,2)", nullable: true),
                    UnitPrice = table.Column<decimal>(type: "decimal(15,4)", nullable: true),
                    Unit = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    PrintTotalFlg = table.Column<bool>(type: "bit", nullable: false),
                    QtnCalcNo = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    Creator = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CreateDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Modifier = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ModifyDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_T_QuotationDetail", x => x.Id);
                    table.ForeignKey(
                        name: "FK_T_QuotationDetail_T_Quotation_QtnNo",
                        column: x => x.QtnNo,
                        principalTable: "T_Quotation",
                        principalColumn: "QtnNo",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_T_Quotation_BaseCd_StaffCd",
                table: "T_Quotation",
                columns: new[] { "BaseCd", "StaffCd" });

            migrationBuilder.CreateIndex(
                name: "IX_T_Quotation_CustomerCd_IsDeleted",
                table: "T_Quotation",
                columns: new[] { "CustomerCd", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_T_Quotation_QtnIssueDate_IsDeleted",
                table: "T_Quotation",
                columns: new[] { "QtnIssueDate", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_T_Quotation_QtnNo",
                table: "T_Quotation",
                column: "QtnNo",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_T_QuotationCalc_QtnCalcNo",
                table: "T_QuotationCalc",
                column: "QtnCalcNo");

            migrationBuilder.CreateIndex(
                name: "IX_T_QuotationCalc_QtnNo_QtnCalcNo",
                table: "T_QuotationCalc",
                columns: new[] { "QtnNo", "QtnCalcNo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_T_QuotationDetail_QtnNo_DetailNo",
                table: "T_QuotationDetail",
                columns: new[] { "QtnNo", "DetailNo" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "T_QuotationCalc");

            migrationBuilder.DropTable(
                name: "T_QuotationDetail");

            migrationBuilder.DropTable(
                name: "T_Quotation");
        }
    }
}
