# Task A-T2 报告：PrefService 矩阵读取 + 服务端合并写 + PrefController 端点

**STATUS: DONE** — 严格 TDD（RED→GREEN），全量回归绿，零迁移，已推送。

## Commit
- `4015d30` feat(wfs-inbox): A-T2 PrefService 矩阵读取/合并写/rowMode + save Merge 分流 + notify-matrix 端点
- 分支 `feat/wfs-inbox-ux`，已 push（`46b1213..4015d30`）。

## 交付内容
- **IPrefService.cs**：追加 `IsEnabledAsync` / `SaveMergeAsync` / `GetRowModeAsync` 三接口成员（保留既有三方法与注释不动）。
- **PrefService.cs**：
  - `_prefsCache`（Scoped 实例内 `Dictionary<Guid,string>`，= per-request 缓存）+ `GetCachedAsync`。
  - `IsEnabledAsync` → 消费 A-T1 `NotifyMatrix.IsEnabled(缓存json, type, channel)`。
  - `SaveMergeAsync` → 顶层键合并写：patch 非法 JSON 抛 `InvalidOperationException("oa.pref.errBadJson")`；库内畸形 → 以 patch 重建；`null` 值删键；单次 read-modify-write + 单次 `SaveChanges`（复用 `SaveAsync`）；写后 `_prefsCache.Remove` 使自身缓存失效。
  - `GetRowModeAsync` → `"merged"`（默认）| `"expanded"`。
- **PrefController.cs**：
  - `SavePrefReq` 换签名加 `bool Merge = false`（既有前端只传 `{ prefsJson }`，绑定缺字段取默认 → 既有调用方零变化）。
  - `Save` 分流：`Merge` 真走 `SaveMergeAsync`，否则 `SaveAsync`。
  - 新增 `GET notify-matrix` → `Ok2(NotifyMatrix.Rows())`。
- **PrefMergeTests.cs**：brief 逐字 7 测试（合并保留他键 / 整体替换+null 删键 / 无行建行 / 坏 JSON i18n 键 / 查库矩阵 / 实例内缓存不跨实例 / 保存失效自身缓存）。

## Gates
1. 新测试 7/7 绿。全量 `dotnet test CP6.slnx`：**Passed 1996 / Skipped 5 / Failed 0**（基线 1989 + 7 新 = 1996）。守卫 `OawfPermissionAttributeTests`（含 `NoReadOnlyGetAction`）随全量绿。
2. `dotnet ef migrations has-pending-model-changes`：**No changes**（零迁移）。
3. `git show --stat HEAD`：仅 brief 4 文件（IPrefService/PrefService/PrefController/PrefMergeTests），205+ / 2-。

## 关键决策 / 适配
- **守卫合规**：新 `notify-matrix` 为只读 GET，**不贴** `[RequirePermission]`（满足 `NoReadOnlyGetAction_HasRequirePermission`）。既有 `Save` 的 `[RequirePermission("oa-settings","edit")]` **保留**（brief 上下文明确「既有端点标记不动」——brief 代码片段省略了它，按上下文口径保留）。
- **BranchPruned 无需适配**：A-T1 的 `NotifyMatrix` 已含 5 行（`branchPruned` 波②合入），本任务测试未断言矩阵行数，无 4-vs-5 冲突。
- **DI 事实核对**：`PrefService` 于 `Program.cs:175` 注册为 Scoped（brief 写「151」为陈旧行号，Scoped 事实成立，per-request 缓存语义有效）。

## Concerns
- 无。R2 并发口径按 brief 文档化为 last-write-wins per top-level key（`Wf_InboxPref` 无 RowVersion，零迁移约束），合并窗口收敛至单请求单 SaveChanges 毫秒级。
- 下游：A-T3（notifier 消费 `IsEnabledAsync`）、A-T4/D-T2（前端消费 `notify-matrix` / rowMode）依赖本任务契约，均已按共享契约精确名交付。
