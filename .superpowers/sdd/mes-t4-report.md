# M-MES T4 执行报告：fail-closed 权限反射测试

## 概要
- 新增文件（零生产改动）：`CP6.Tests/MesPermissionAttributeTests.cs`（4 个 [Fact]）。
- 真相源：`docs/seeds/mes-permission-keys.md`（§七 计数收口 + §四 豁免 + §三 高危/状态键）。
- 同型先例：`CP6.Tests/ErpPermissionAttributeTests.cs`（含 fable 终审后的 DeclaredOnly 注释纠偏），已 MES 化。

## 计数对账（与真相源 §七逐项吻合）
| 维度 | 真相源 | 测试断言 | 实扫 |
|---|---|---|---|
| 扫描控制器（Mes 命名空间，非抽象继承 ControllerBase） | 11 | `Assert.Equal(11,…)` | 11 ✅ |
| 非 GET 端点（POST/PUT/DELETE）总数 | 30 | 28 + 2 = 30 | 30 ✅ |
| 贴点（真写，taggedCount） | 28 | `Assert.Equal(28, taggedCount)` | 28 ✅ |
| 只读 POST 豁免命中（exemptHit） | 2 | `Assert.Equal(2, exemptHit.Count)` | 2 ✅ |
| menu-key 去重 | 10 | 正则 `^mes-[a-z0-9-]+$` 校验 | — |

- 28 贴点实证（grep RequirePermission）：WorkOrder 5、ProductionResult 5、Machine 6、Defect 3、QualityInspection 2、WorkCenter 2、ProcessCostRate 2、Oee 1、PlanningBoard 2 = 28。
- 30 非 GET 端点（grep `[HttpPost|Put|Delete]`）：上述 28 + PlanAchievement 2（全豁免）= 30。

## 豁免清单对账（§四，共 2 条，逐条带真相源编号理由注释）
| 键 | 真相源编号 | 理由（注释内） |
|---|---|---|
| `PlanAchievementController.Summary` | §四#1 | `GetSummaryAsync` 仅 `WorkOrders.AsNoTracking` 读→内存 GroupBy 达成率 DTO，全类无 Add/Update/Remove/SaveChanges；POST 仅为传复杂查询体 |
| `PlanAchievementController.ExportCsv` | §四#2 | 调 GetSummaryAsync 后拼 CSV bytes，纯读导出，无写 |

- 已复核 `PlanAchievementController.cs`：两方法均 `[HttpPost(...)]`、无 `[RequirePermission]`、类级 `[Authorize]`（登录即可用），与豁免语义一致。
- 「既贴键又在豁免」冲突场景已显式捕获（照 ERP 版：`offenders.Add(…二者互斥)`）。
- 豁免防腐用例 `ReadOnlyPostExemptions_AreAllStillUntaggedMutatingEndpoints`：每条豁免必须实存、仍是变更端点、且未贴键。

## action 词汇集（逐词相等 HashSet，13 词，均从已贴 28 键读出，不含 view）
`add, edit, del, issue, start, suspend, complete, report, status, downtime, recalculate, reschedule, arrange`
- `view` 不入集（豁免归 view 但未贴属性）。
- `complete`（mes-production-result:complete）+ `edit`（mes-process-cost-rate:edit）为 §三 两高危键，其余 9 状态键落在集内。
- 多一词/少一词或任何 typo 即红。

## 基类继承链自查（DeclaredOnly 安全前提）
- 逐类核对 11 个 MES 控制器类头：**全部直接 `: ControllerBase`**（WorkOrder/ProductionResult/DefectRecord/Machine/Oee/PlanningBoard/QualityInspection/PlanAchievement/WorkCenter/ProcessCostRate/MesDashboard）。
- **无 LocalizedControllerBase、无任何中间抽象基类**（比 ERP 更简单——ERP 有 9 个经 LocalizedControllerBase）。
- `ControllerBase` 自身不声明任何 `[HttpXxx]` action，所有写端点均为子类手写方法 → `BindingFlags.DeclaredOnly` 不漏扫。
- 注释已按 MES 实际形态准确书写，并保留「未来若引入声明 action 的共享基类须改扫描策略」的前瞻告警。

## 反向验证证据（临时删贴点 → 红 → 恢复 → 绿 → 工作树干净）
1. 临时删除 `WorkOrderController.Create` 的 `[RequirePermission("mes-work-order","add")]`。
2. `dotnet test --filter MesPermissionAttributeTests` → **Failed! 1 failed / 3 passed**，报错精确：
   ```
   变更端点权限点缺失/键不合约定/豁免冲突:
   WorkOrderController.Create：变更端点缺 [RequirePermission] 且不在只读 POST 豁免清单
   ```
   （同时 taggedCount 28→27 触发 `Assert.Equal(28,…)`，双闸命中。）
3. 恢复贴点 → `dotnet test --filter MesPermissionAttributeTests` → **Passed! 4/4**。
4. `git status --short` 仅列 `?? CP6.Tests/MesPermissionAttributeTests.cs`（生产文件无残留改动，工作树干净）。

## 验证结果
- `dotnet test --filter MesPermissionAttributeTests`：**Passed! 4/4**（338ms）。
- 全量：**Passed! 1731 / Failed 0 / Skipped 5**（基线 1727 + 4 新用例 = 1731，未跌）。

## Concerns
- 无。零生产改动，纯结构性测试，计数与真相源三处硬编码（11/28/2）精确锚定，任何漂移即红。
