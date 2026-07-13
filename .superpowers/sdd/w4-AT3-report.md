# Task A-T3 报告：PersistentWfNotifier 接矩阵偏好（逐收件人 × 逐通道）

**STATUS: DONE** ✅
**Commit:** `185b6a5`（已 push 到 `feat/wfs-inbox-ux`）
**测试小结:** 全量 `dotnet test CP6.slnx` = 2007 passed / 5 skipped（基线 1996 + 新增 11）；migrations 无待决模型变更。

---

## 交付内容

- Modify: `CP6.WebApi/Services/PersistentWfNotifier.cs` — 四方法接矩阵偏好
- Test: `CP6.Tests/Oa/PersistentWfNotifierTests.cs`（新建，11 用例）

## TDD 证据

### RED（Step 2）
`dotnet test --filter PersistentWfNotifierTests` → **Failed: 6, Passed: 5**。失败用例（现状缺陷暴露）：
- `InAppOff_SkipsPersistAndPush_EmailStillSent` — 现状 inApp 无通道门控 → 仍持久化（Type=1）
- `EmailOff_PersistsAndPushes_NoEmail` — 现状 email 走全局 legacy `prefs.Email` → 矩阵 JSON 无全局键 → 仍发邮件
- `BothOff_SkipsEverything`（flowRejected 双关）— 矩阵 JSON legacy 解析看不到 `flowRejected` 对象 → `prefs.Rejected` 保持 true → 仍持久化
- `BranchPruned_InAppOff_*` / `BranchPruned_EmailOff_*` / `BranchPruned_BothOff_*` — 现状 BranchPruned 完全无门控（v1 无偏好）

绿的 5 个是回归保护（Default / TypesIndependent / LegacyFlat×2 / BranchPruned_Default）。

### GREEN（Step 4）
实现后 `--filter PersistentWfNotifierTests` → **Passed: 11, Failed: 0**。

### 回归闸
- `--filter "Oa|Wf"` → 409 passed（含 NotificationEngineHookTests / TimeoutScanTests 照绿）
- 全量 `CP6.slnx` → 2007 passed / 5 skipped

## 第 4 方法适配细节（binding pre-flight）

brief 的 R1 摘录称「3 个方法」已过时——`BranchPrunedAsync` 已由内核 hardening 波合入本文件（`WfNotificationType.BranchPruned = 5`，`NotifyMatrix.Support["branchPruned"] = (true, true)` 均已就位）。按控制器授权的适配：
- 将 brief 给出的 `TodoCreatedAsync` 矩阵接线模式**逐字镜像**到 `BranchPrunedAsync`（typeKey = `"branchPruned"`，camelCase 契约）。
- 补充 4 个 branchPruned 测试（Default / InAppOff / EmailOff / BothOff），与前三方法用例结构同构。
- 未触碰通知文案（i18n 键 hardening 波已存），只改门控接线。

## 双重门控消除证据

改造前 `BranchPrunedAsync` 尾部残留 `var prefs = await _pref.GetNotifyPrefsAsync(starterId); if (prefs.Email) ...`（legacy 扁平路径）。四方法全部改为 `IsEnabledAsync` 双通道查询后，`grep GetNotifyPrefsAsync CP6.WebApi/Services/PersistentWfNotifier.cs` = **0 匹配**——通知器内不再有任何 legacy 扁平门控，矩阵（含 A-T1 的遗留回落语义）为唯一门控路径，无双重过滤。

铁律③已更新为「偏好按 收件人×类型×通道 独立生效（矩阵）」。

## 门禁核对
1. ✅ 新测试 11 绿；全量 2007 绿
2. ✅ `dotnet ef migrations has-pending-model-changes` = No changes（零迁移）
3. ✅ `git show --stat HEAD` = 仅 brief 两文件（PersistentWfNotifier.cs + PersistentWfNotifierTests.cs）

## concerns
- 无。`GetNotifyPrefsAsync`/`ParseNotifyPrefs` 保留于 PrefService（其他消费者/测试仍用），仅 notifier 停用——符合 brief Step 5 口径。
- `NotifyMatrix.Support["timeout"] = (false, false)`，但 timeout 无生产者（超时以 TodoCreated 发出），本任务不涉及，符合 R1。
