-- ============================================================
-- OA Phase C′ 流程设计器 QA Seed — 可选辅助脚本
-- DB: CP6DB_OA (隔离库)
-- 说明: Program.cs 启动时已自动 seed 菜单 738 及 i18n 词条。
--        本脚本仅供手动校验 seed 落库情况使用，无需手动执行。
-- ============================================================

-- 1. 验证菜单 738 已插入
SELECT MenuId, MenuName, RoutePath, ParentId, OrderNo, Enable
FROM   Sys_Menus
WHERE  MenuId = 738;
-- Expected: 1 row, MenuName='流程设计器', RoutePath='/oa/designer', ParentId=740

-- 2. 验证 RoleMenu 授权
SELECT RoleId, MenuId
FROM   Sys_RoleMenus
WHERE  MenuId = 738 AND RoleId = 1;
-- Expected: 1 row

-- 3. 验证 nav.738 i18n 词条落库
SELECT LangKey, ZhCN, ZhTW, En, Ja, Ko
FROM   Sys_Langs
WHERE  LangKey = 'nav.738';
-- Expected: 1 row, En='Flow Designer'

-- 4. 验证 oa.designer.* 词条总数
SELECT COUNT(*) AS DesignerKeyCount
FROM   Sys_Langs
WHERE  LangKey LIKE 'oa.designer.%';
-- Expected: 69 rows (nav.738 单独计 1 行，共 70 行)

-- 5. 验证校验消息词条
SELECT LangKey, ZhCN, En
FROM   Sys_Langs
WHERE  LangKey IN (
    'oa.designer.errNoStart',
    'oa.designer.errNoEnd',
    'oa.designer.errDanglingEdge',
    'oa.designer.errNoStrategy'
);
-- Expected: 4 rows

-- 6. 剧本执行后验证设计器落库（运行剧本1后使用）
SELECT FlowKey, FlowName, FormKey, FunctionId, FlowCode, IsActive
FROM   wf.Wf_FlowDef
WHERE  FlowKey = 'qa-designer-001';

SELECT NodeId, NodeType, NodeName, PositionX, PositionY
FROM   wf.Wf_FlowNode
WHERE  FlowKey = 'qa-designer-001'
ORDER  BY NodeType;

SELECT EdgeId, [From], [To], ConditionType, ConditionExpr
FROM   wf.Wf_FlowEdge
WHERE  FlowKey = 'qa-designer-001';
