# Task B-T1 报告：剪枝通知契约（WfNotificationType.BranchPruned + IWfNotifier.BranchPrunedAsync ×4 实现）

**Status:** ✅ 完成并推送
**Commit:** `cccfea0`（分支 `feat/wfs-kernel-hardening`，push 成功 b0f280d..cccfea0）

## 红→绿

- **红（Step 2）**：`dotnet build CP6.Tests` → `CS0117: 'WfNotificationType' does not contain a definition for 'BranchPruned'`（新常量测试落锚，编译失败）。
- **绿（Step 4）**：
  - `--filter NotificationEngineHookTests` → Passed 4 / Failed 0（既有 3 断言零改 + 新 `WfNotificationType_BranchPruned_Is5`）。
  - `--filter Wf` → Passed 227 / Failed 0。
  - 全量 `dotnet test CP6.Tests` → **Passed 1865 / Skipped 5 / Failed 0**（基线 1864 + 新增 1，5 skip=SQLite 既知）。
  - `dotnet build CP6.WebApi` → Build succeeded, 0 Error。
  - ef `has-pending-model-changes` → **clean**（"No changes ... since the last migration"，零迁移）。

## 契约签名（B-T2 复用，前后一致不许漂移）

```csharp
// WfNotificationType.cs
public const int BranchPruned = 5;

// IWfNotifier.cs（接口 + NullWfNotifier 空实现）
Task BranchPrunedAsync(Guid starterId, Guid instanceId, string flowKey, string nodeId, string? comment);
```

四实现：
1. `NullWfNotifier`（IWfNotifier.cs）— `=> Task.CompletedTask` 空实现。
2. `SignalRWfNotifier`（WebApi）— no-op（注释指明由 PersistentWfNotifier 承载）。
3. `PersistentWfNotifier`（WebApi）— 三渠道（持久化 CreateAsync + SignalR best-effort try/catch + 邮件 prefs.Email）。**v1 不查偏好门控**（BranchPruned 无 NotificationPrefs 字段 → 缺键默认 true，等价信箱 spec §2.1 三态坍缩）。
4. 测试 `CountingNotifier`（NotificationEngineHookTests.cs）— 新增 `PrunedCount` / `LastPrunedNodeId` + `BranchPrunedAsync` 计数（供 B-T2 引擎测试复用）。

## 变更范围（git diff --stat，5 文件 +61）

- `CP6.Entity/DomainModels/Wf/WfNotificationType.cs`（+3）
- `CP6.Core/Services/Wf/IWfNotifier.cs`（+4）
- `CP6.WebApi/Services/SignalRWfNotifier.cs`（+2）
- `CP6.WebApi/Services/PersistentWfNotifier.cs`（+38）
- `CP6.Tests/Oa/NotificationEngineHookTests.cs`（+14）

零跨模块污染（无 Space 触碰），27 Wf 不变量零改，既有通知断言零改。

## 疑虑

无。计划代码给全，照落。偏好门控缺席为计划明示口径（信箱 spec 落地时统一接管），非遗漏。
