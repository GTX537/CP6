using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CP6.Core.Migrations
{
    /// <inheritdoc />
    public partial class SysRoleMenuUniqueIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Sys_RoleMenu_Tenant_Role",
                table: "Sys_RoleMenus");

            // P0 终审 #3：升唯一索引前先清存量重复 (TenantId,RoleId,MenuId) 行（原仅非唯一索引，
            // 兜底网/重跑种子可能已插重复）——每组保留最小 Id，否则 CreateIndex(unique) 会失败。
            migrationBuilder.Sql(@"
DELETE rm FROM dbo.Sys_RoleMenus rm
WHERE rm.Id > (
    SELECT MIN(x.Id) FROM dbo.Sys_RoleMenus x
    WHERE x.TenantId = rm.TenantId AND x.RoleId = rm.RoleId AND x.MenuId = rm.MenuId
);");

            migrationBuilder.CreateIndex(
                name: "IX_Sys_RoleMenu_Tenant_Role",
                table: "Sys_RoleMenus",
                columns: new[] { "TenantId", "RoleId", "MenuId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Sys_RoleMenu_Tenant_Role",
                table: "Sys_RoleMenus");

            migrationBuilder.CreateIndex(
                name: "IX_Sys_RoleMenu_Tenant_Role",
                table: "Sys_RoleMenus",
                columns: new[] { "TenantId", "RoleId" });
        }
    }
}
