using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CP6.Core.Migrations
{
    /// <inheritdoc />
    public partial class AddOrderShipmentTracking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "LastOutboundNo",
                table: "T_OrderDetail",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastShipDate",
                table: "T_OrderDetail",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ShipStatus",
                table: "T_OrderDetail",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<decimal>(
                name: "ShippedQty",
                table: "T_OrderDetail",
                type: "decimal(21,8)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ActualShipDate",
                table: "T_Order",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ShipStatus",
                table: "T_Order",
                type: "int",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LastOutboundNo",
                table: "T_OrderDetail");

            migrationBuilder.DropColumn(
                name: "LastShipDate",
                table: "T_OrderDetail");

            migrationBuilder.DropColumn(
                name: "ShipStatus",
                table: "T_OrderDetail");

            migrationBuilder.DropColumn(
                name: "ShippedQty",
                table: "T_OrderDetail");

            migrationBuilder.DropColumn(
                name: "ActualShipDate",
                table: "T_Order");

            migrationBuilder.DropColumn(
                name: "ShipStatus",
                table: "T_Order");
        }
    }
}
