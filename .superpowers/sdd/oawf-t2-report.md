# M-OA/WF T2 执行报告：菜单 MenuKey 锚定(OawfMenuSeed)

生成于 2026-07-12。执行者：subagent（Opus 4.8）。分支 `feat/m-oawf-crosscutting`。

## 交付物

| 文件 | 类型 | 说明 |
|---|---|---|
| `CP6.WebApi/Seed/OawfMenuSeed.cs` | 正本 | 启动幂等种子（缺行补建 + 7 键显式锚定 + 防御矫正块） |
| `CP6.Tests/OawfMenuSeedTests.cs` | 测试 | 6 用例（锚定/RoutePath/唯一键/幂等/矫正/父行 null） |
| `CP6.WebApi/Program.cs` | 接线 | `OawfMenuSeed.EnsureSeeded(db)` 置于 MesPermissionSeed 之后（~:857）、全局回填块（:908）之前 |
| `docs/seeds/oawf-key-menu-anchor.md` | 交付文档 | 7 键锚定表 + 硬前置落实 |
| `docs/seeds/oawf-menu-seed.sql` | SQL 对照 | 头声明 C# 正本；7 行 UPDATE 严限 733–739 |
| `docs/seeds/oawf-permission-keys.md` | 真相源更新 | 委派双键合一裁决（§一#26/§二/§三/§六注4/§七 全同步） |

## 7 键锚定表（真相源 §二 逐字一致）

| menu-key | MenuId | RoutePath | 菜单名 |
|---|---|---|---|
| `oa-inbox` | 733 | `/oa/inbox` | 电子表单信箱 |
| `oa-flow-admin` | 734 | `/oa/flow-admin` | 流程管理 |
| `oa-form-catalog` | 735 | `/oa/form-catalog` | 填單 |
| `oa-form-search` | 736 | `/oa/form-search` | 表单查询 |
| `oa-settings` | 737 | `/oa/settings` | 设定 |
| `oa-designer` | 738 | `/oa/designer` | 流程设计器 |
| `oa-approver-map` | 739 | `/oa/approver-map` | approverMap |

非锚定：**740 OA工作流父组行**（无 RoutePath，MenuKey 留 null，回填因 RoutePath==null 亦跳过）。

## 矫正块作用域

- **按 MenuId 定位**（照 MesMenuSeed 正解，非按 Key——按 Key 找会漏被写坏行）。
- **严限 7 锚定行**（`r.Key != null` → 733–739）；740 父行不动。
- 幂等：`menu.MenuKey != r.Key` 时才写，已正确即跳过。
- **OA 特性（与 MES 差异）**：OA 733–739 RoutePath 派生键与真相源逐字一致（`/oa/inbox`→`oa-inbox` …），**零错配**。命门纯为**回填时序**（非 MES machine-list 键值错配）。故矫正块正常恒 no-op，仅为结构对齐 + 防御历史/异常写坏保留。硬前置由「先于 :908 回填执行」这一接线位置解决。

## 三主控拍板落实

1. **委派双键合一** → `oa-settings:delegate`：真相源 §一#26 改锚 oa-settings、§三删 `oa-inbox:delegate` 行并合并、§六注4 记 T1 建议→T2 裁决全过程、§七 高危 9→8 / 资源键 23→22 同步。**menu-key 集不变（仍 7 键）**——合一在 action 层，oa-inbox/oa-settings 两菜单键均照旧锚定，故本 seed 无需改动。
2. **`oa-flow-admin:enable` 维持状态级**：734 菜单照锚，未提级、未拆键。
3. **双栈未裁决**：不动 /wf/*-designer 路由；旧栈 `oa-designer:*` 键照真相源锚定到 738 流程设计器菜单行。

## TDD Evidence

- **先红**（mutation 验证测试有牙）：将 733 锚定键临时改为 `oa-inbox-MUTATED` → `dotnet test --filter OawfMenuSeedTests` = **Failed: 2, Passed: 4**（`AnchorsAll7KeysToExpectedMenuIds` + `AnchoredRowsMatchTruthSourceRoutePaths` 双红），随即还原。
- **后绿**：`dotnet test --filter OawfMenuSeedTests` = **Passed: 6, Failed: 0**（4s）。
- **全量**：`dotnet test CP6.Tests` = **Passed: 1764, Failed: 0, Skipped: 5, Total: 1769**（1m3s）。基线 1758 + 新增 6 = 1764，**零回归、无下跌**。

## Self-Review 核对

| 检查项 | 结论 |
|---|---|
| 7 键与真相源（合一后）逐字一致 | ✅ 7 键 = §二 表；合一不影响 menu-key 集 |
| 接线在全局回填块（:908）前无条件路径 | ✅ 置于 :857（MesPermissionSeed 后），无条件调用，先于 :908 |
| 矫正块按 MenuId 严限 7 行 | ✅ `if (r.Key == null) continue` + `FirstOrDefault(MenuId==r.Id)`，740 跳过 |
| 唯一索引安全 | ✅ 7 锚定键互异，740 留 null；不撞 `MenuKey IS NOT NULL` 过滤唯一索引（CP6Context.cs:602） |
| 测试删实现会红 | ✅ mutation 验证 2 红（且删 class 会编译失败红） |

## 遗留（不属本任务范围）

- 双栈孤儿路由 `/wf/form-designer`、`/wf/flow-designer` 退役/收编待用户裁决（真相源 §六头号裁决点）——T3 贴权限前须裁定，否则旧栈 def 端点键概念上锚不可达路由。
- **环境事故复现**：执行中 C 盘 0GB（WU Download 缓存 2.7GB 撑满，即 7/12 已复盘的磁盘满事故）。已清 WU 缓存 + 用户 temp 释放至 1.59GB 完成构建；**扩容仍是治本建议**（记忆已录）。
