using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CP6.Core.Migrations
{
    /// <inheritdoc />
    public partial class AddWmsKitting : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "T_KitMaster",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    KitSku = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    KitName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    DefaultWarehouseCd = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    Remarks = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ActiveFlg = table.Column<bool>(type: "bit", nullable: false),
                    Creator = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CreateDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Modifier = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ModifyDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_T_KitMaster", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "T_KitMasterComponent",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    KitSku = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    LineNo = table.Column<int>(type: "int", nullable: false),
                    ComponentProductCd = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ComponentName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    RequiredQty = table.Column<decimal>(type: "decimal(21,8)", nullable: false),
                    UnitCd = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
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
                    table.PrimaryKey("PK_T_KitMasterComponent", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "T_KitOrder",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    KitOrderNo = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    KitSku = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    KitName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Qty = table.Column<decimal>(type: "decimal(21,8)", nullable: false),
                    Direction = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    WarehouseCd = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    KitLocationCd = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    KitLotNo = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    OperatorCd = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    Remarks = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ExecutedTxnNos = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    ExecutedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Creator = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CreateDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Modifier = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ModifyDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_T_KitOrder", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_T_KitMaster_ActiveFlg_IsDeleted",
                table: "T_KitMaster",
                columns: new[] { "ActiveFlg", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_T_KitMaster_KitSku",
                table: "T_KitMaster",
                column: "KitSku",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_T_KitMasterComponent_ComponentProductCd",
                table: "T_KitMasterComponent",
                column: "ComponentProductCd");

            migrationBuilder.CreateIndex(
                name: "IX_T_KitMasterComponent_KitSku_LineNo",
                table: "T_KitMasterComponent",
                columns: new[] { "KitSku", "LineNo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_T_KitOrder_Direction_Status",
                table: "T_KitOrder",
                columns: new[] { "Direction", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_T_KitOrder_ExecutedAt_IsDeleted",
                table: "T_KitOrder",
                columns: new[] { "ExecutedAt", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_T_KitOrder_KitOrderNo",
                table: "T_KitOrder",
                column: "KitOrderNo",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_T_KitOrder_KitSku_IsDeleted",
                table: "T_KitOrder",
                columns: new[] { "KitSku", "IsDeleted" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "T_KitMaster");

            migrationBuilder.DropTable(
                name: "T_KitMasterComponent");

            migrationBuilder.DropTable(
                name: "T_KitOrder");
        }
    }
}
