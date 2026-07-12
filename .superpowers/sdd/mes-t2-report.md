# M-MES T2 报告：菜单 MenuKey 锚定(MesMenuSeed)

生成于 2026-07-12。真相源 `docs/seeds/mes-permission-keys.md` §二/§六；同型先例 ErpMenuSeed。

## 10 键锚定表（逐字取自真相源 §二）

| # | menu-key | 锚定 MenuId | RoutePath | 裁决 |
|---|---|---|---|---|
| 1 | `mes-planning-board` | 301 | `/mes/planning-board` | 单页 |
| 2 | `mes-work-order` | 302 | `/mes/work-order` | 入力锚定；303 一覧 null |
| 3 | `mes-production-result` | 304 | `/mes/production-result` | 入力锚定；305 一覧 null |
| 4 | `mes-quality-inspection` | 306 | `/mes/quality-inspection` | 入力锚定；307 一覧 null。键取菜单域名非 `inspections` |
| 5 | `mes-defect` | 308 | `/mes/defect` | 单页 |
| 6 | `mes-machine` | 310 | `/mes/machine-list` | ★命门2：显式赋 `mes-machine`（非回填 `mes-machine-list`）|
| 7 | `mes-oee` | 311 | `/mes/oee` | 单页 |
| 8 | `mes-plan-achievement` | 313 | `/mes/plan-achievement` | 单页，仅 view |
| 9 | `mes-work-center` | 314 | `/mes/work-center` | 主数据 |
| 10 | `mes-process-cost-rate` | 315 | `/mes/process-cost-rate` | 主数据 |

非锚定行（MenuKey 留 null）：300 父 / 303,305,307 一覧 / 309 dashboard,312 control-tower（GET-only）。共 16 行 = 10 锚 + 6 null。

## 矫正块作用域说明

防御矫正块 `foreach (r in Rows) { if (r.Key == null) continue; ... }` — **作用域严限 10 个 `r.Key != null` 锚定行**。
一覧页/看板/父行（6 行 null 键）不进入矫正，交由 Program.cs 回填派生 `*-list` 后缀键（无 action 引用、无害）。
关键场景：既有库中 310 若被历史回填成 `mes-machine-list`，矫正块就地纠回 `mes-machine`（对应测试 `EnsureSeeded_CorrectsHistoricalBackfilledBareKey_ToMesPrefixed`）。

## 回填时序落实

`MesMenuSeed.EnsureSeeded(db)` 接入 Program.cs（WmsPermissionSeed 之后，约 :844），
位于「无 MenuKey 菜单 RoutePath 自动回填」块（:894-901）**之前**，且在启动种子块无条件路径上。
洁净首启即赋 10 锚定行 MenuKey，无「首启 null → 全 403 → 二次重启才生效」窗口（硬前置①解除）。
现有 Program.cs MES 菜单 Add 块（:1511+）位于回填之后、Add 时不设 MenuKey——因 MesMenuSeed 已先插入这些 MenuId，
其 `if (!Any(MenuId==...))` 守卫全部跳过，无重复插入（幂等）。

## TDD Evidence

- **RED**：先写 `MesMenuSeedTests.cs`（6 测试），`dotnet test --filter MesMenuSeedTests` →
  编译失败 `CS0103: The name 'MesMenuSeed' does not exist`（实现未存在）。
- **GREEN**：实现 `MesMenuSeed.cs` + Program.cs 接线后 → `Passed! Failed: 0, Passed: 6`。
- **全量基线**：`dotnet test CP6.Tests` → `Passed: 1722, Skipped: 5`（基线 1716 + 新增 6，无回归下跌）。

6 个测试：锚定 10 键→MenuId / machine 显式 mes-machine 非 mes-machine-list / 无两行共非空键(=10) /
幂等(16 菜单+16 RoleMenu 二次不变) / 历史回填错配就地矫正 / 父行+一覧+GET-only 留 null。

## Files changed

- `CP6.WebApi/Seed/MesMenuSeed.cs`（新增，实现正本）
- `CP6.Tests/MesMenuSeedTests.cs`（新增，6 测试）
- `CP6.WebApi/Program.cs`（接线 1 处：WmsPermissionSeed 之后、回填块之前）
- `docs/seeds/mes-key-menu-anchor.md`（新增，T3b 输入映射）
- `docs/seeds/mes-menu-seed.sql`（新增，SQL 对照，头声明 C# 正本）

## Self-review findings

- 10 键与真相源 §二逐字一致 ✅；锚定 MenuId 与锚定表 md/SQL 三者一致 ✅。
- 接线在回填块(:894)之前、启动无条件路径 ✅。
- 矫正块严限 10 锚定行（`r.Key == null` 跳过）✅；machine-list 错配可被纠回 ✅。
- 唯一索引安全（10 distinct 键，测试断言）✅；6 非锚定行留 null ✅。
- 测试真实（删实现 → 编译红，已实证）；幂等二次调用 16/16 不变 ✅。
- 无发现须修问题。

## Concerns

- 逐租户 RoleAction 传播（mes-* 键→admin 放行）不在本任务范围，属 T3b；沿用平台既有「首次补建管理员角色」机制。
- ProcessCostRate 高危提级(§五归并5)为真相源待 T2 审计拍板项——本任务只锚定 menu-key，不涉及 action 分级，无影响。
