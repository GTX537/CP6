using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CP6.Core.Migrations
{
    /// <inheritdoc />
    public partial class Gap42AddOutboundRouting : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "OutboundPriority",
                table: "T_Warehouse",
                type: "int",
                nullable: false,
                defaultValue: 100);

            migrationBuilder.AddColumn<string>(
                name: "WarehouseCd",
                table: "T_OutboundOrderDetail",
                type: "nvarchar(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "T_OutboundRoutingRule",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RuleName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    CustomerCd = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    ProductCdPrefix = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    OutboundType = table.Column<int>(type: "int", nullable: true),
                    TargetWarehouseCd = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    Enabled = table.Column<bool>(type: "bit", nullable: false),
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
                    table.PrimaryKey("PK_T_OutboundRoutingRule", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_T_Warehouse_OutboundPriority",
                table: "T_Warehouse",
                column: "OutboundPriority");

            migrationBuilder.CreateIndex(
                name: "IX_T_OutboundRoutingRule_CustomerCd_IsDeleted",
                table: "T_OutboundRoutingRule",
                columns: new[] { "CustomerCd", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_T_OutboundRoutingRule_Enabled_SortOrder",
                table: "T_OutboundRoutingRule",
                columns: new[] { "Enabled", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_T_OutboundRoutingRule_TargetWarehouseCd",
                table: "T_OutboundRoutingRule",
                column: "TargetWarehouseCd");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "T_OutboundRoutingRule");

            migrationBuilder.DropIndex(
                name: "IX_T_Warehouse_OutboundPriority",
                table: "T_Warehouse");

            migrationBuilder.DropColumn(
                name: "OutboundPriority",
                table: "T_Warehouse");

            migrationBuilder.DropColumn(
                name: "WarehouseCd",
                table: "T_OutboundOrderDetail");
        }
    }
}
