SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET NOCOUNT ON;

-- OA 阶段4 章09 自研设计器菜单（表单设计器 503 + 流程设计器 504，挂在 OA审批 500 组下），幂等。
-- 前置：OA审批顶级组 500 已由 tmp/seed_oa_menu.sql 种入（待办501/我的申请502）。
IF NOT EXISTS (SELECT 1 FROM Sys_Menus WHERE MenuId = 503)
  INSERT INTO Sys_Menus (MenuId, MenuName, RoutePath, MenuKey, Icon, ParentId, OrderNo, Enable, CreateDate)
  VALUES (503, N'表单设计器', N'/wf/form-designer', N'wf-form-designer', N'Edit', 500, 3, 1, GETDATE());

IF NOT EXISTS (SELECT 1 FROM Sys_Menus WHERE MenuId = 504)
  INSERT INTO Sys_Menus (MenuId, MenuName, RoutePath, MenuKey, Icon, ParentId, OrderNo, Enable, CreateDate)
  VALUES (504, N'流程设计器', N'/wf/flow-designer', N'wf-flow-designer', N'Share', 500, 4, 1, GETDATE());

-- 授权 角色 1/2/3（幂等）
INSERT INTO Sys_RoleMenus (RoleId, MenuId)
SELECT r.RoleId, m.MenuId
FROM (VALUES (1),(2),(3)) AS r(RoleId)
CROSS JOIN (VALUES (503),(504)) AS m(MenuId)
WHERE NOT EXISTS (SELECT 1 FROM Sys_RoleMenus x WHERE x.RoleId = r.RoleId AND x.MenuId = m.MenuId);

SELECT MenuId, MenuName, RoutePath, ParentId, OrderNo FROM Sys_Menus WHERE MenuId IN (503, 504) ORDER BY MenuId;
