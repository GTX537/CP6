-- ═══════════════════════════════════════════════════════════════════════════
-- MES 菜单 MenuKey 锚定 — SQL 对照（M-MES Task 2）
-- ─────────────────────────────────────────────────────────────────────────────
-- ★正本 = CP6.WebApi/Seed/MesMenuSeed.cs（启动幂等 C# 种子）。本 SQL 仅为审计/手工核对对照，
--   不参与运行时播种。以 C# 为准；若二者不一致，以 MesMenuSeed.cs + docs/seeds/mes-permission-keys.md §二 为真相源。
-- 前置：300–315 菜单行由 Program.cs 播种；本脚本仅显式设 10 锚定行 MenuKey=mes-*，并矫正历史回填错配。
-- 作用域严限 10 锚定行（MenuId in 301,302,304,306,308,310,311,313,314,315）；一覧/看板/父行 MenuKey 留 null。
-- 幂等：仅当 MenuKey <> 目标 或 为 null 时更新。
-- ═══════════════════════════════════════════════════════════════════════════

UPDATE Sys_Menus SET MenuKey = 'mes-planning-board'     WHERE MenuId = 301 AND (MenuKey IS NULL OR MenuKey <> 'mes-planning-board');
UPDATE Sys_Menus SET MenuKey = 'mes-work-order'         WHERE MenuId = 302 AND (MenuKey IS NULL OR MenuKey <> 'mes-work-order');
UPDATE Sys_Menus SET MenuKey = 'mes-production-result'  WHERE MenuId = 304 AND (MenuKey IS NULL OR MenuKey <> 'mes-production-result');
UPDATE Sys_Menus SET MenuKey = 'mes-quality-inspection' WHERE MenuId = 306 AND (MenuKey IS NULL OR MenuKey <> 'mes-quality-inspection');
UPDATE Sys_Menus SET MenuKey = 'mes-defect'             WHERE MenuId = 308 AND (MenuKey IS NULL OR MenuKey <> 'mes-defect');
-- ★命门2：310 RoutePath=/mes/machine-list 回填得 mes-machine-list，此处强制纠回 mes-machine。
UPDATE Sys_Menus SET MenuKey = 'mes-machine'            WHERE MenuId = 310 AND (MenuKey IS NULL OR MenuKey <> 'mes-machine');
UPDATE Sys_Menus SET MenuKey = 'mes-oee'                WHERE MenuId = 311 AND (MenuKey IS NULL OR MenuKey <> 'mes-oee');
UPDATE Sys_Menus SET MenuKey = 'mes-plan-achievement'   WHERE MenuId = 313 AND (MenuKey IS NULL OR MenuKey <> 'mes-plan-achievement');
UPDATE Sys_Menus SET MenuKey = 'mes-work-center'        WHERE MenuId = 314 AND (MenuKey IS NULL OR MenuKey <> 'mes-work-center');
UPDATE Sys_Menus SET MenuKey = 'mes-process-cost-rate'  WHERE MenuId = 315 AND (MenuKey IS NULL OR MenuKey <> 'mes-process-cost-rate');

-- 核对：应恰好 10 行非空 mes-* 键，且各占唯一 MenuId。
-- SELECT MenuId, MenuKey, RoutePath FROM Sys_Menus WHERE MenuId BETWEEN 300 AND 315 ORDER BY MenuId;
