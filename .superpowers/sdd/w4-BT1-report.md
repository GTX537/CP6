# Task B-T1 报告：InboxService.BatchTransferAsync + Preview（逐条独立事务 + 汇总报告）

**STATUS: DONE**
**Commit SHA:** `f6b9f80685262ce6ac07c5d279246233210d94f8`（已 push 至 `feat/wfs-inbox-ux`）
**分支:** feat/wfs-inbox-ux

## 一行测试摘要
新增 BatchTransferTests 9/9 全绿；`dotnet test CP6.slnx` 2016 passed / 5 skipped（基线 2007 + 9 新）；`Oa|Wf` 过滤 418 全绿；`ef migrations has-pending-model-changes` 无变更。

## 实现要点（严格 TDD：RED→GREEN）
- **RED 验证**：先写 `CP6.Tests/Oa/BatchTransferTests.cs`（brief 逐字），跑测确认编译失败（record/方法不存在）。
- **契约 record 族**（`InboxModels.cs` 末尾追加）：`BatchTransferFilter` / `BatchTransferItemResult` / `BatchTransferReport` / `BatchTransferPreview`——B-T2 端点与 B-T3 UI 依赖。
- **接口**（`IInboxService.cs`）：`BatchTransferAsync(actorId, fromUserId, toUserId, comment, filter?)` + `BatchTransferPreviewAsync(fromUserId, filter?)`。
- **实现**（`InboxService.cs`，仿 `ActBatchAsAsync` 循环口径）：
  - 前置校验（400 口径，不占 E-WF 码）：`from==to → oa.bt.errSameUser`；`to 不存在/停用/跨租户 → oa.bt.errTargetInvalid`（全局租户过滤器令跨租户查不到 = 与不存在同路径）。
  - 候选查询 `QueryTransferCandidatesAsync`：常规路径取 from 的全部 Pending 待办（Running 实例）按 filter（FlowKey / BeforeUtc 比对 CreateDate）收窄；`TaskIds` 显式点名走重试口径——不预筛状态、让引擎裁决，仅保留 `AssigneeId==from` 归属过滤。
  - `MaxBatchTransfer=500`：候选超上限在循环前抛 `oa.bt.errTooMany`（一条都不转）。
  - 逐条 `await _engine.TransferAsync(taskId, actorId, toUserId, comment)`（引擎内部单次 SaveChanges = 单条独立事务，R3/D3），`catch InvalidOperationException` 收集失败明细行、不中断后续。
  - Preview 只读：复用 `PendingAsync(fromUserId)` 现签名（现状即逐任务行 = expanded 语义，R5），取候选前 10 条为 Sample。

## 关键决策
- **PendingAsync 签名**：D-T1（rowMode 扩展）尚未落地，接口仍是单参 `PendingAsync(Guid userId)`。按 brief 注记以现签名调用——两种执行顺序都编译，语义相同。D-T1 改签名时默认参不破本调用。

## 引擎零改动确认
`git show --stat HEAD` 仅 4 个 brief 文件（IInboxService/InboxModels/InboxService/BatchTransferTests），共 +288 行；`AdvancedFlow.cs`、`IFlowEngine.cs`、`FlowEngine` 等引擎文件零 diff。

## 关注点 / 交接
- **i18n 键落地在 E-T1**：`oa.bt.errSameUser` / `oa.bt.errTargetInvalid` / `oa.bt.errTooMany`（message=键、前端 t(raw)）。B-T1 只产错误键字符串，前端翻译词条不在本任务范围。
- **B-T2 端点**依赖本任务的 record 族与两个方法签名（已就位）。
- 无迁移、无种子变更。
