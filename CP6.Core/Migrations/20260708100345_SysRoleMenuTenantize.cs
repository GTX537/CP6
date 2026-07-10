using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CP6.Core.Migrations
{
    /// <inheritdoc />
    public partial class SysRoleMenuTenantize : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "Sys_RoleMenus",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.CreateIndex(
                name: "IX_Sys_RoleMenu_Tenant_Role",
                table: "Sys_RoleMenus",
                columns: new[] { "TenantId", "RoleId" });

            // ───────────────────────────────────────────────────────────────────────────
            //  P0-T3 补口（评审 Important）存量归户回填：Sys_RoleMenu 随 Sys_Role 一并租户化。
            //  与 SysRoleTenantize 同策略：①存量行归户 A1 ②对每个非默认租户复制映射副本（同 RoleId/MenuId，
            //  且仅复制该租户已拥有对应角色副本的行——不扩散孤儿映射）③校验：任一映射行的 (TenantId,RoleId)
            //  若 RoleId 是已知角色号则必须有本租户角色副本，否则 THROW 中止事务整体回滚。
            //  幂等/健壮性：NOT EXISTS 防重复复制；无非默认租户时 CROSS JOIN 空集安全。
            // ───────────────────────────────────────────────────────────────────────────
            migrationBuilder.Sql(@"
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;

DECLARE @A1 uniqueidentifier = '00000000-0000-0000-0000-0000000000A1';
DECLARE @Empty uniqueidentifier = '00000000-0000-0000-0000-000000000000';

-- Step 1：存量全部映射行归户默认租户 A1。
UPDATE dbo.Sys_RoleMenus SET TenantId = @A1 WHERE TenantId = @Empty;

-- Step 2：对 A1 之外每个租户复制映射副本（同 RoleId/MenuId）。仅复制该租户拥有对应角色副本的行
--（SysRoleTenantize 已把角色复制到每个租户，故正常等价全集；孤儿映射不扩散）。NOT EXISTS 保幂等。
INSERT INTO dbo.Sys_RoleMenus (TenantId, RoleId, MenuId)
SELECT t.Id, rm.RoleId, rm.MenuId
FROM dbo.Sys_RoleMenus AS rm
CROSS JOIN dbo.Sys_Tenants AS t
WHERE rm.TenantId = @A1
  AND t.Id <> @A1
  AND EXISTS (SELECT 1 FROM dbo.Sys_Roles ro WHERE ro.TenantId = t.Id AND ro.RoleId = rm.RoleId)
  AND NOT EXISTS (SELECT 1 FROM dbo.Sys_RoleMenus x
                  WHERE x.TenantId = t.Id AND x.RoleId = rm.RoleId AND x.MenuId = rm.MenuId);

-- Step 3：一致性校验 —— 任一映射行引用的已知 (TenantId,RoleId) 必有对应角色副本，缺失即回填不完整。
IF EXISTS (
    SELECT 1
    FROM dbo.Sys_RoleMenus c
    WHERE EXISTS (SELECT 1 FROM dbo.Sys_Roles a WHERE a.RoleId = c.RoleId)
      AND NOT EXISTS (SELECT 1 FROM dbo.Sys_Roles r WHERE r.TenantId = c.TenantId AND r.RoleId = c.RoleId)
)
BEGIN
    THROW 50002, N'SysRoleMenuTenantize backfill incomplete: a Sys_RoleMenus row references a RoleId without a matching per-tenant Sys_Role row.', 1;
END;
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Sys_RoleMenu_Tenant_Role",
                table: "Sys_RoleMenus");

            // P0 终审 #4（对称清理）：Up() 把 A1 的映射逐租户复制。回滚须先删非 A1 副本还原单命名空间状态，
            // 否则残留重复行会在重新应用迁移时叠加（虽无唯一约束不撞键，但污染数据）。与角色迁移 Down 对称。
            migrationBuilder.Sql(
                "DELETE FROM dbo.Sys_RoleMenus WHERE TenantId <> '00000000-0000-0000-0000-0000000000A1';");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "Sys_RoleMenus");
        }
    }
}
