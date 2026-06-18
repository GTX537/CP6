using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CP6.Core.Migrations
{
    /// <inheritdoc />
    public partial class A2WorkOrderProcessHours : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ActualHourSource",
                table: "T_WorkOrderProcess",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ActualLaborHour",
                table: "T_WorkOrderProcess",
                type: "decimal(21,8)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ActualMachineHour",
                table: "T_WorkOrderProcess",
                type: "decimal(21,8)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "HourCalculatedTime",
                table: "T_WorkOrderProcess",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "HourRemark",
                table: "T_WorkOrderProcess",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsHourOverridden",
                table: "T_WorkOrderProcess",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<decimal>(
                name: "LaborHour",
                table: "T_ProductionResult",
                type: "decimal(21,8)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "MachineHour",
                table: "T_ProductionResult",
                type: "decimal(21,8)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ActualHourSource",
                table: "T_WorkOrderProcess");

            migrationBuilder.DropColumn(
                name: "ActualLaborHour",
                table: "T_WorkOrderProcess");

            migrationBuilder.DropColumn(
                name: "ActualMachineHour",
                table: "T_WorkOrderProcess");

            migrationBuilder.DropColumn(
                name: "HourCalculatedTime",
                table: "T_WorkOrderProcess");

            migrationBuilder.DropColumn(
                name: "HourRemark",
                table: "T_WorkOrderProcess");

            migrationBuilder.DropColumn(
                name: "IsHourOverridden",
                table: "T_WorkOrderProcess");

            migrationBuilder.DropColumn(
                name: "LaborHour",
                table: "T_ProductionResult");

            migrationBuilder.DropColumn(
                name: "MachineHour",
                table: "T_ProductionResult");
        }
    }
}
