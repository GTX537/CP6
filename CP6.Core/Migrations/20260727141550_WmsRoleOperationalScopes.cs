using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CP6.Core.Migrations
{
    /// <inheritdoc />
    public partial class WmsRoleOperationalScopes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "T_WmsRoleScope",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RoleId = table.Column<int>(type: "int", nullable: false),
                    WarehouseCd = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    AreaCd = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Creator = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CreateDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Modifier = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ModifyDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_T_WmsRoleScope", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_T_WmsRoleScope_RoleId_WarehouseCd_AreaCd",
                table: "T_WmsRoleScope",
                columns: new[] { "RoleId", "WarehouseCd", "AreaCd" });

            migrationBuilder.CreateIndex(
                name: "IX_T_WmsRoleScope_TenantId_RoleId_WarehouseCd_AreaCd",
                table: "T_WmsRoleScope",
                columns: new[] { "TenantId", "RoleId", "WarehouseCd", "AreaCd" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "T_WmsRoleScope");
        }
    }
}
