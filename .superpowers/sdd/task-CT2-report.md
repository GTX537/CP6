# Task C-T2 报告：CancelTokenSubtree 子树清场（WFS kernel hardening 波②）

## Status: DONE（红→绿→commit→push 全闭环）

## 落码
- `CP6.Core/Services/Wf/FlowEngine.Tokens.cs`：新增 `internal void CancelTokenSubtree(Guid instanceId, Guid rootTokenId)` + `internal void CancelPendingServiceJobsOfToken(Guid instanceId, Guid tokenId)`（照计划逐字）。
- `CP6.Tests/Wf/TokenSubtreeCancelTests.cs`：新建（1 Fact，照计划逐字）。
- 依赖前置已就位：`SnapshotTokens`（A-T1）、`CancelPendingTasksOfToken`（已落）、`VoidPendingFormTos(instanceId, tokenId:)` 重载（ReadModel.cs:163）。未碰 Prune.cs（未复用错位置）。

## 红绿证据
- 红：`--filter TokenSubtreeCancel` → CS1061 `FlowEngine does not contain a definition for 'CancelTokenSubtree'`（编译失败，方法不存在）。
- 绿（单测）：`Passed 1 / Failed 0`。
- 绿（Wf 闸）：`--filter Wf` → **243 passed / 0 failed**。
- 绿（全量）：`dotnet test CP6.Tests` → **1881 passed / 5 skipped**（基线 1880→+1；27 不变量零改）。
- EF clean：`has-pending-model-changes` → "No changes have been made to the model"（零迁移）。
- 零跨模块污染：`git show --stat` = 仅 FlowEngine.Tokens.cs + TokenSubtreeCancelTests.cs 两文件。

## 签名（C-T3 要用，精确）
```csharp
internal void CancelTokenSubtree(Guid instanceId, Guid rootTokenId);
internal void CancelPendingServiceJobsOfToken(Guid instanceId, Guid tokenId);
```
- 子树闭包算法：`HashSet{rootTokenId}` 起，按 `ParentTokenId ∈ subtree` 定点迭代直至不增长（含 root 自身）。
- 不改 `inst.Status`（清场级操作，非驳回/撤回）。

## 四清面各自过滤谓词（子树内每个 tokenId 逐一施加）
1. **Token**（本方法内直改）：`subtree.Contains(t.Id) && t.Status == Active` → `Cancelled`（快照来自 SnapshotTokens=Local∪DB 去重，直改追踪实体）。
2. **任务** `CancelPendingTasksOfToken(instanceId, id)`：`InstanceId==inst && TokenId==id && Status∈{Pending,Suspended}` → `Cancelled`（Local + localIds-exclusion 双扫）。
3. **履历** `VoidPendingFormTos(instanceId, tokenId: id)`：`InstanceId==inst && TokenId==id && Status==Pending` → `Voided`（既有重载 Match 谓词）。
4. **服务作业** `CancelPendingServiceJobsOfToken(instanceId, id)`：`InstanceId==inst && TokenId==id && Status==Pending` → `Cancelled` + `CompletedAtUtc=now`（镜像 CancelAllActiveTokens 的 B-T3 job 清场，Local + localIds-exclusion 双扫；Running job 不强杀，由 worker 侧状态闸处理）。

## 疑虑 / 记档
- 无 BLOCKED / 无 NEEDS_CONTEXT。实现照计划逐字，零偏差。
- 提交仅含我的 2 文件：`.superpowers/sdd/task-CT2-brief.md` 工作区有 262 行 pre-existing 无关改动（非本 Task 产物），未纳入本 commit。本报告文件此前为旧波「样例 dataWriteback executor」的陈旧同名报告，已覆盖。
- commit=1a30ed2，已 push（0abe6ae..1a30ed2，feat/wfs-kernel-hardening）。
