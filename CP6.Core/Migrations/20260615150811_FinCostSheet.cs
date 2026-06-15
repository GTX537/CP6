using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CP6.Core.Migrations
{
    /// <inheritdoc />
    public partial class FinCostSheet : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Fin_CostSheet",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    No = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    WorkOrderNo = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ProductCd = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    CostCenterId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CompletedQty = table.Column<decimal>(type: "decimal(21,8)", nullable: false),
                    MaterialActual = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    MaterialStandard = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    LaborStd = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    OverheadStd = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    JournalEntryId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Creator = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CreateDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Modifier = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ModifyDate = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Fin_CostSheet", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Fin_CostSheetLine",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CostSheetId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LineNo = table.Column<int>(type: "int", nullable: false),
                    Element = table.Column<int>(type: "int", nullable: false),
                    ProcessCd = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    MaterialCd = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    MaterialName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    PlanQty = table.Column<decimal>(type: "decimal(21,8)", nullable: false),
                    ActualQty = table.Column<decimal>(type: "decimal(21,8)", nullable: false),
                    UnitPrice = table.Column<decimal>(type: "decimal(21,8)", nullable: false),
                    ActualAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    StandardAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Creator = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CreateDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Modifier = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ModifyDate = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Fin_CostSheetLine", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Fin_CostSheetLine_Fin_CostSheet_CostSheetId",
                        column: x => x.CostSheetId,
                        principalTable: "Fin_CostSheet",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Fin_CostSheetLine_CostSheetId",
                table: "Fin_CostSheetLine",
                column: "CostSheetId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Fin_CostSheetLine");

            migrationBuilder.DropTable(
                name: "Fin_CostSheet");
        }
    }
}
