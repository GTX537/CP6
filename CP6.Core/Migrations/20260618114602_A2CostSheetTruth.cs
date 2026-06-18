using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CP6.Core.Migrations
{
    /// <inheritdoc />
    public partial class A2CostSheetTruth : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ── (1) AddColumn：CostSheet 工/费 实际+标准 四字段（先加列，再回填，最后删旧列）──
            migrationBuilder.AddColumn<decimal>(
                name: "LaborActual",
                table: "Fin_CostSheet",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "LaborStandard",
                table: "Fin_CostSheet",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "OverheadActual",
                table: "Fin_CostSheet",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "OverheadStandard",
                table: "Fin_CostSheet",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            // ── (1) AddColumn：CostSheetLine 8 个工时追溯字段 ──
            migrationBuilder.AddColumn<string>(
                name: "CalcNote",
                table: "Fin_CostSheetLine",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "HourSource",
                table: "Fin_CostSheetLine",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "Hours",
                table: "Fin_CostSheetLine",
                type: "decimal(21,8)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "RateValidFrom",
                table: "Fin_CostSheetLine",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "StandardHours",
                table: "Fin_CostSheetLine",
                type: "decimal(21,8)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TaskCd",
                table: "Fin_CostSheetLine",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WarningCode",
                table: "Fin_CostSheetLine",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WgCd",
                table: "Fin_CostSheetLine",
                type: "nvarchar(10)",
                maxLength: 10,
                nullable: true);

            // ── (2) A2 历史回填：旧估算额 → 标准额且实际额=标准额（历史成本单总额不变）──
            migrationBuilder.Sql(@"UPDATE [Fin_CostSheet]
  SET [LaborActual]=[LaborStd],[LaborStandard]=[LaborStd],
      [OverheadActual]=[OverheadStd],[OverheadStandard]=[OverheadStd]
  WHERE 1=1;");

            // ── (3) DropColumn：删旧估算单字段 ──
            migrationBuilder.DropColumn(
                name: "LaborStd",
                table: "Fin_CostSheet");

            migrationBuilder.DropColumn(
                name: "OverheadStd",
                table: "Fin_CostSheet");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // 反向：重建旧估算单字段，回填实际额，再删新字段
            migrationBuilder.AddColumn<decimal>(
                name: "LaborStd",
                table: "Fin_CostSheet",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "OverheadStd",
                table: "Fin_CostSheet",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.Sql(@"UPDATE [Fin_CostSheet]
  SET [LaborStd]=[LaborActual],[OverheadStd]=[OverheadActual]
  WHERE 1=1;");

            migrationBuilder.DropColumn(
                name: "LaborActual",
                table: "Fin_CostSheet");

            migrationBuilder.DropColumn(
                name: "LaborStandard",
                table: "Fin_CostSheet");

            migrationBuilder.DropColumn(
                name: "OverheadActual",
                table: "Fin_CostSheet");

            migrationBuilder.DropColumn(
                name: "OverheadStandard",
                table: "Fin_CostSheet");

            migrationBuilder.DropColumn(
                name: "CalcNote",
                table: "Fin_CostSheetLine");

            migrationBuilder.DropColumn(
                name: "HourSource",
                table: "Fin_CostSheetLine");

            migrationBuilder.DropColumn(
                name: "Hours",
                table: "Fin_CostSheetLine");

            migrationBuilder.DropColumn(
                name: "RateValidFrom",
                table: "Fin_CostSheetLine");

            migrationBuilder.DropColumn(
                name: "StandardHours",
                table: "Fin_CostSheetLine");

            migrationBuilder.DropColumn(
                name: "TaskCd",
                table: "Fin_CostSheetLine");

            migrationBuilder.DropColumn(
                name: "WarningCode",
                table: "Fin_CostSheetLine");

            migrationBuilder.DropColumn(
                name: "WgCd",
                table: "Fin_CostSheetLine");
        }
    }
}
