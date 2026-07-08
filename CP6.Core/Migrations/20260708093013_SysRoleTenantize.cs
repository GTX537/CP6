using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CP6.Core.Migrations
{
    /// <inheritdoc />
    public partial class SysRoleTenantize : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_Sys_Roles",
                table: "Sys_Roles");

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "Sys_Roles",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddPrimaryKey(
                name: "PK_Sys_Roles",
                table: "Sys_Roles",
                columns: new[] { "TenantId", "RoleId" });

            // ───────────────────────────────────────────────────────────────────────────
            //  P0-T3 存量归户回填（手工数据段，须在复合主键建立之后执行）。
            //  设计（因 RoleId 是 int 用户自定义主键、且子表已各自携 TenantId，故采「保持 RoleId 稳定、
            //  逐租户复制副本、子表零重指」策略，而非按新 Id 重指——后者对 int 键会改动用户可见编号，
            //  且 Sys_RoleMenu 无 TenantId 无法逐租户重指，故不可行）：
            //   1. 存量全部角色行 → 默认租户 A1（新列默认空 Guid → 改指 A1，A1 的子表引用零迁移）。
            //   2. 对 A1 之外每个租户复制一份角色副本（同 RoleId）；子表按值 + 各自 TenantId 在租户作用域内解析。
            //   3.（无独立步骤）子表 UserRole/RoleAction/RoleDataScope/RoleFieldPerm/User.RoleId 零重指。
            //   4. 校验：任一子表所引用的已知 (TenantId,RoleId) 必须有对应角色副本，缺失 → THROW 中止事务（整体回滚）。
            //  幂等/健壮性：NOT EXISTS 防重复复制；无非默认租户时 CROSS JOIN 空集安全；迁移在单事务内运行，THROW 触发回滚。
            // ───────────────────────────────────────────────────────────────────────────
            migrationBuilder.Sql(@"
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;

DECLARE @A1 uniqueidentifier = '00000000-0000-0000-0000-0000000000A1';
DECLARE @Empty uniqueidentifier = '00000000-0000-0000-0000-000000000000';

-- Step 1：存量全部角色行归户默认租户 A1。
UPDATE dbo.Sys_Roles SET TenantId = @A1 WHERE TenantId = @Empty;

-- Step 2：对 A1 之外每个租户复制角色副本（保持同 RoleId）。NOT EXISTS 保幂等；无非默认租户时空集安全。
INSERT INTO dbo.Sys_Roles (TenantId, RoleId, RoleName, Description, Enable, OrderNo, CreateDate)
SELECT t.Id, r.RoleId, r.RoleName, r.Description, r.Enable, r.OrderNo, r.CreateDate
FROM dbo.Sys_Roles AS r
CROSS JOIN dbo.Sys_Tenants AS t
WHERE r.TenantId = @A1
  AND t.Id <> @A1
  AND NOT EXISTS (SELECT 1 FROM dbo.Sys_Roles x WHERE x.TenantId = t.Id AND x.RoleId = r.RoleId);

-- Step 4：一致性校验 —— 任一子表引用的已知 (TenantId,RoleId) 必有对应角色副本，缺失即回填不完整。
IF EXISTS (
    SELECT 1
    FROM (
        SELECT TenantId, RoleId FROM dbo.Sys_UserRole
        UNION SELECT TenantId, RoleId FROM dbo.Sys_RoleAction
        UNION SELECT TenantId, RoleId FROM dbo.Sys_RoleDataScope
        UNION SELECT TenantId, RoleId FROM dbo.Sys_RoleFieldPerm
        UNION SELECT TenantId, RoleId FROM dbo.Sys_Users WHERE RoleId IS NOT NULL
    ) AS c
    WHERE EXISTS (SELECT 1 FROM dbo.Sys_Roles a WHERE a.RoleId = c.RoleId)
      AND NOT EXISTS (SELECT 1 FROM dbo.Sys_Roles r WHERE r.TenantId = c.TenantId AND r.RoleId = c.RoleId)
)
BEGIN
    THROW 50001, N'SysRoleTenantize backfill incomplete: a tenant references a RoleId without a matching per-tenant Sys_Role row.', 1;
END;
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_Sys_Roles",
                table: "Sys_Roles");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "Sys_Roles");

            migrationBuilder.AddPrimaryKey(
                name: "PK_Sys_Roles",
                table: "Sys_Roles",
                column: "RoleId");
        }
    }
}
