using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CP6.Core.Migrations
{
    /// <inheritdoc />
    public partial class AddWmsPaperIndustry : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "T_InkColorMatchHistory",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MatchNo = table.Column<string>(type: "nvarchar(25)", maxLength: 25, nullable: false),
                    CustomerCd = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    CustomerName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ColorCode = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    FormulaJson = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    ConsumedQty = table.Column<decimal>(type: "decimal(21,8)", nullable: false),
                    MatchedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    OperatorCd = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    Remarks = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Creator = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CreateDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Modifier = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ModifyDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_T_InkColorMatchHistory", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "T_InkLot",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    InkLotNo = table.Column<string>(type: "nvarchar(25)", maxLength: 25, nullable: false),
                    ColorCode = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    InkType = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    OpenStatus = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    ExpiryDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Quantity = table.Column<decimal>(type: "decimal(21,8)", nullable: false),
                    UnitCd = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    ViscosityCp = table.Column<decimal>(type: "decimal(10,2)", nullable: true),
                    SolidContent = table.Column<decimal>(type: "decimal(5,2)", nullable: true),
                    ParentLotNoA = table.Column<string>(type: "nvarchar(25)", maxLength: 25, nullable: true),
                    ParentLotNoB = table.Column<string>(type: "nvarchar(25)", maxLength: 25, nullable: true),
                    LocationCd = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    SupplierCd = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    Remarks = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Creator = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CreateDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Modifier = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ModifyDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_T_InkLot", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "T_Pallet",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PalletNo = table.Column<string>(type: "nvarchar(25)", maxLength: 25, nullable: false),
                    ProductCd = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ProductName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    LotNo = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    CartonQty = table.Column<int>(type: "int", nullable: false),
                    WeightKg = table.Column<decimal>(type: "decimal(10,3)", nullable: true),
                    HeightMm = table.Column<int>(type: "int", nullable: true),
                    MaxStackLayers = table.Column<int>(type: "int", nullable: true),
                    WarehouseCd = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    LocationCd = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    ShippedOutboundNo = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    Remarks = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Creator = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CreateDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Modifier = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ModifyDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_T_Pallet", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "T_PaperRoll",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RollNo = table.Column<string>(type: "nvarchar(25)", maxLength: 25, nullable: false),
                    PaperGrade = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    WidthMm = table.Column<int>(type: "int", nullable: false),
                    BasisWeight = table.Column<decimal>(type: "decimal(10,2)", nullable: false),
                    GrainDirection = table.Column<string>(type: "nvarchar(2)", maxLength: 2, nullable: false),
                    OriginalLengthM = table.Column<decimal>(type: "decimal(21,8)", nullable: false),
                    RemainingLengthM = table.Column<decimal>(type: "decimal(21,8)", nullable: false),
                    CoreDiameterInch = table.Column<decimal>(type: "decimal(5,2)", nullable: false),
                    MfgDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    MfgLotNo = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    SupplierRollNo = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    LocationCd = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    WarehouseCd = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    ParentRollNo = table.Column<string>(type: "nvarchar(25)", maxLength: 25, nullable: true),
                    DisposeThresholdM = table.Column<decimal>(type: "decimal(21,8)", nullable: true),
                    Remarks = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Creator = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CreateDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Modifier = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ModifyDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_T_PaperRoll", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "T_VmiBilling",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BillingNo = table.Column<string>(type: "nvarchar(25)", maxLength: 25, nullable: false),
                    CustomerCd = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    CustomerName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    YearMonth = table.Column<string>(type: "nvarchar(6)", maxLength: 6, nullable: false),
                    SkuCount = table.Column<int>(type: "int", nullable: false),
                    BeginQty = table.Column<decimal>(type: "decimal(21,8)", nullable: false),
                    EndQty = table.Column<decimal>(type: "decimal(21,8)", nullable: false),
                    AvgQty = table.Column<decimal>(type: "decimal(21,8)", nullable: false),
                    DailyStorageRate = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    BillingAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    CalculatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Confirmed = table.Column<bool>(type: "bit", nullable: false),
                    Remarks = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Creator = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CreateDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Modifier = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ModifyDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_T_VmiBilling", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_T_InkColorMatchHistory_CustomerCd_ColorCode",
                table: "T_InkColorMatchHistory",
                columns: new[] { "CustomerCd", "ColorCode" });

            migrationBuilder.CreateIndex(
                name: "IX_T_InkColorMatchHistory_MatchedAt",
                table: "T_InkColorMatchHistory",
                column: "MatchedAt");

            migrationBuilder.CreateIndex(
                name: "IX_T_InkColorMatchHistory_MatchNo",
                table: "T_InkColorMatchHistory",
                column: "MatchNo",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_T_InkLot_ColorCode",
                table: "T_InkLot",
                column: "ColorCode");

            migrationBuilder.CreateIndex(
                name: "IX_T_InkLot_ExpiryDate_IsDeleted",
                table: "T_InkLot",
                columns: new[] { "ExpiryDate", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_T_InkLot_InkLotNo",
                table: "T_InkLot",
                column: "InkLotNo",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_T_InkLot_InkType_IsDeleted",
                table: "T_InkLot",
                columns: new[] { "InkType", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_T_InkLot_OpenStatus_IsDeleted",
                table: "T_InkLot",
                columns: new[] { "OpenStatus", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_T_Pallet_PalletNo",
                table: "T_Pallet",
                column: "PalletNo",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_T_Pallet_ProductCd_LotNo",
                table: "T_Pallet",
                columns: new[] { "ProductCd", "LotNo" });

            migrationBuilder.CreateIndex(
                name: "IX_T_Pallet_ShippedOutboundNo",
                table: "T_Pallet",
                column: "ShippedOutboundNo");

            migrationBuilder.CreateIndex(
                name: "IX_T_Pallet_Status_IsDeleted",
                table: "T_Pallet",
                columns: new[] { "Status", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_T_Pallet_WarehouseCd_LocationCd",
                table: "T_Pallet",
                columns: new[] { "WarehouseCd", "LocationCd" });

            migrationBuilder.CreateIndex(
                name: "IX_T_PaperRoll_PaperGrade_WidthMm_GrainDirection_Status",
                table: "T_PaperRoll",
                columns: new[] { "PaperGrade", "WidthMm", "GrainDirection", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_T_PaperRoll_RemainingLengthM",
                table: "T_PaperRoll",
                column: "RemainingLengthM");

            migrationBuilder.CreateIndex(
                name: "IX_T_PaperRoll_RollNo",
                table: "T_PaperRoll",
                column: "RollNo",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_T_PaperRoll_Status_IsDeleted",
                table: "T_PaperRoll",
                columns: new[] { "Status", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_T_PaperRoll_WarehouseCd_LocationCd",
                table: "T_PaperRoll",
                columns: new[] { "WarehouseCd", "LocationCd" });

            migrationBuilder.CreateIndex(
                name: "IX_T_VmiBilling_BillingNo",
                table: "T_VmiBilling",
                column: "BillingNo",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_T_VmiBilling_Confirmed",
                table: "T_VmiBilling",
                column: "Confirmed");

            migrationBuilder.CreateIndex(
                name: "IX_T_VmiBilling_CustomerCd_YearMonth",
                table: "T_VmiBilling",
                columns: new[] { "CustomerCd", "YearMonth" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_T_VmiBilling_YearMonth",
                table: "T_VmiBilling",
                column: "YearMonth");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "T_InkColorMatchHistory");

            migrationBuilder.DropTable(
                name: "T_InkLot");

            migrationBuilder.DropTable(
                name: "T_Pallet");

            migrationBuilder.DropTable(
                name: "T_PaperRoll");

            migrationBuilder.DropTable(
                name: "T_VmiBilling");
        }
    }
}
