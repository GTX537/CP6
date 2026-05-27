using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CP6.Core.Migrations
{
    /// <inheritdoc />
    public partial class AddWmsPaperIndustry2 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "T_PlateMoldStock",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PlateNo = table.Column<string>(type: "nvarchar(25)", maxLength: 25, nullable: false),
                    PlateType = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    CustomerCd = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    CustomerName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ProductCd = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    ProductName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ColorCount = table.Column<int>(type: "int", nullable: true),
                    SizeNote = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    MadeDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    MadeCost = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    MaxShots = table.Column<int>(type: "int", nullable: true),
                    UsedShots = table.Column<int>(type: "int", nullable: false),
                    LastUsedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    NextMaintenanceDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    WarehouseCd = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    LocationCd = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
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
                    table.PrimaryKey("PK_T_PlateMoldStock", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "T_RemnantMaterial",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RemnantNo = table.Column<string>(type: "nvarchar(25)", maxLength: 25, nullable: false),
                    MaterialType = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    MaterialGrade = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    WidthMm = table.Column<int>(type: "int", nullable: false),
                    LengthMm = table.Column<int>(type: "int", nullable: false),
                    ThicknessUm = table.Column<int>(type: "int", nullable: true),
                    Quantity = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    UnitCd = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    SourceWorkOrderNo = table.Column<string>(type: "nvarchar(25)", maxLength: 25, nullable: true),
                    SourceRollNo = table.Column<string>(type: "nvarchar(25)", maxLength: 25, nullable: true),
                    WarehouseCd = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    LocationCd = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    ReservedFor = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    RegisteredAt = table.Column<DateTime>(type: "datetime2", nullable: false),
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
                    table.PrimaryKey("PK_T_RemnantMaterial", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "T_SampleStock",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SampleNo = table.Column<string>(type: "nvarchar(25)", maxLength: 25, nullable: false),
                    SampleType = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    CustomerCd = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    CustomerName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ProductCd = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    ProductName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Quantity = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    UnitCd = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    WarehouseCd = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    LocationCd = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    LentTo = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: true),
                    LentAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ExpectedReturnDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ReturnedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RegisteredAt = table.Column<DateTime>(type: "datetime2", nullable: false),
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
                    table.PrimaryKey("PK_T_SampleStock", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_T_PlateMoldStock_CustomerCd_ProductCd",
                table: "T_PlateMoldStock",
                columns: new[] { "CustomerCd", "ProductCd" });

            migrationBuilder.CreateIndex(
                name: "IX_T_PlateMoldStock_NextMaintenanceDate",
                table: "T_PlateMoldStock",
                column: "NextMaintenanceDate");

            migrationBuilder.CreateIndex(
                name: "IX_T_PlateMoldStock_PlateNo",
                table: "T_PlateMoldStock",
                column: "PlateNo",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_T_PlateMoldStock_PlateType_Status_IsDeleted",
                table: "T_PlateMoldStock",
                columns: new[] { "PlateType", "Status", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_T_PlateMoldStock_WarehouseCd_LocationCd",
                table: "T_PlateMoldStock",
                columns: new[] { "WarehouseCd", "LocationCd" });

            migrationBuilder.CreateIndex(
                name: "IX_T_RemnantMaterial_MaterialType_Status_IsDeleted",
                table: "T_RemnantMaterial",
                columns: new[] { "MaterialType", "Status", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_T_RemnantMaterial_RemnantNo",
                table: "T_RemnantMaterial",
                column: "RemnantNo",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_T_RemnantMaterial_SourceRollNo",
                table: "T_RemnantMaterial",
                column: "SourceRollNo");

            migrationBuilder.CreateIndex(
                name: "IX_T_RemnantMaterial_SourceWorkOrderNo",
                table: "T_RemnantMaterial",
                column: "SourceWorkOrderNo");

            migrationBuilder.CreateIndex(
                name: "IX_T_RemnantMaterial_WarehouseCd_LocationCd",
                table: "T_RemnantMaterial",
                columns: new[] { "WarehouseCd", "LocationCd" });

            migrationBuilder.CreateIndex(
                name: "IX_T_RemnantMaterial_WidthMm_LengthMm",
                table: "T_RemnantMaterial",
                columns: new[] { "WidthMm", "LengthMm" });

            migrationBuilder.CreateIndex(
                name: "IX_T_SampleStock_CustomerCd_SampleType",
                table: "T_SampleStock",
                columns: new[] { "CustomerCd", "SampleType" });

            migrationBuilder.CreateIndex(
                name: "IX_T_SampleStock_ExpectedReturnDate",
                table: "T_SampleStock",
                column: "ExpectedReturnDate");

            migrationBuilder.CreateIndex(
                name: "IX_T_SampleStock_SampleNo",
                table: "T_SampleStock",
                column: "SampleNo",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_T_SampleStock_Status_IsDeleted",
                table: "T_SampleStock",
                columns: new[] { "Status", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_T_SampleStock_WarehouseCd_LocationCd",
                table: "T_SampleStock",
                columns: new[] { "WarehouseCd", "LocationCd" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "T_PlateMoldStock");

            migrationBuilder.DropTable(
                name: "T_RemnantMaterial");

            migrationBuilder.DropTable(
                name: "T_SampleStock");
        }
    }
}
