-- ═══════════════════════════════════════════════════════════════════════════
-- OA/WF 菜单 MenuKey 锚定 — SQL 对照（M-OA/WF Task 2）
-- ─────────────────────────────────────────────────────────────────────────────
-- ★正本 = CP6.WebApi/Seed/OawfMenuSeed.cs（启动幂等 C# 种子）。本 SQL 仅为审计/手工核对对照，
--   不参与运行时播种。以 C# 为准；若二者不一致，以 OawfMenuSeed.cs + docs/seeds/oawf-permission-keys.md §二 为真相源。
-- 前置：733–740 菜单行由 Program.cs（:1446–1496）播种；本脚本仅显式设 7 锚定行 MenuKey=oa-*，并矫正历史/异常写坏。
-- 作用域严限 7 锚定行（MenuId in 733,734,735,736,737,738,739）；740 父行 MenuKey 留 null。
-- 幂等：仅当 MenuKey <> 目标 或 为 null 时更新。
-- OA 派生键与真相源逐字一致（零错配，不同于 MES machine-list）；命门纯为回填时序，故须先于回填块执行。
-- ═══════════════════════════════════════════════════════════════════════════

UPDATE Sys_Menus SET MenuKey = 'oa-inbox'        WHERE MenuId = 733 AND (MenuKey IS NULL OR MenuKey <> 'oa-inbox');
UPDATE Sys_Menus SET MenuKey = 'oa-flow-admin'   WHERE MenuId = 734 AND (MenuKey IS NULL OR MenuKey <> 'oa-flow-admin');
UPDATE Sys_Menus SET MenuKey = 'oa-form-catalog' WHERE MenuId = 735 AND (MenuKey IS NULL OR MenuKey <> 'oa-form-catalog');
UPDATE Sys_Menus SET MenuKey = 'oa-form-search'  WHERE MenuId = 736 AND (MenuKey IS NULL OR MenuKey <> 'oa-form-search');
UPDATE Sys_Menus SET MenuKey = 'oa-settings'     WHERE MenuId = 737 AND (MenuKey IS NULL OR MenuKey <> 'oa-settings');
UPDATE Sys_Menus SET MenuKey = 'oa-designer'     WHERE MenuId = 738 AND (MenuKey IS NULL OR MenuKey <> 'oa-designer');
UPDATE Sys_Menus SET MenuKey = 'oa-approver-map' WHERE MenuId = 739 AND (MenuKey IS NULL OR MenuKey <> 'oa-approver-map');

-- 核对：应恰好 7 行非空 oa-* 键，且各占唯一 MenuId；740 父行 MenuKey 留 null。
-- SELECT MenuId, MenuKey, RoutePath FROM Sys_Menus WHERE MenuId BETWEEN 733 AND 740 ORDER BY MenuId;

-- ═══════════════════════════════════════════════════════════════════════════
-- T2 追补：双栈孤儿路由收编（用户裁决 2026-07-12，照 ErpMenuSeed 五孤儿先例）
-- 权限锚在 738（oa-designer:edit/form-save），741/742 故意不赋 MenuKey（留 null，避免撞 738 的唯一索引）。
-- ═══════════════════════════════════════════════════════════════════════════

INSERT INTO Sys_Menus (MenuId, MenuName, RoutePath, Icon, ParentId, OrderNo, MenuKey, Enable)
SELECT 741, N'フォームデザイナー(旧)', '/wf/form-designer', 'Edit', 740, 741, NULL, 1
WHERE NOT EXISTS (SELECT 1 FROM Sys_Menus WHERE MenuId = 741);

INSERT INTO Sys_Menus (MenuId, MenuName, RoutePath, Icon, ParentId, OrderNo, MenuKey, Enable)
SELECT 742, N'フローデザイナー(旧)', '/wf/flow-designer', 'Edit', 740, 742, NULL, 1
WHERE NOT EXISTS (SELECT 1 FROM Sys_Menus WHERE MenuId = 742);

INSERT INTO Sys_RoleMenus (RoleId, MenuId)
SELECT 1, 741 WHERE NOT EXISTS (SELECT 1 FROM Sys_RoleMenus WHERE RoleId = 1 AND MenuId = 741);

INSERT INTO Sys_RoleMenus (RoleId, MenuId)
SELECT 1, 742 WHERE NOT EXISTS (SELECT 1 FROM Sys_RoleMenus WHERE RoleId = 1 AND MenuId = 742);

-- 核对：741/742 RoutePath 须与 cp6.web/src/router/index.ts:46-47 viewModules 键逐字一致，MenuKey 恒 null。
-- SELECT MenuId, MenuKey, RoutePath FROM Sys_Menus WHERE MenuId IN (741, 742) ORDER BY MenuId;
