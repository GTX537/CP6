# Space E11-S05 执行状态、幂等回执与异常补偿实现计划

状态：合同冻结，进入实现
起始基线：`b0231e241e881046d75c938e2de4c88527ec8725`
功能分支：`codex/space-e11-s05-execution-receipts-compensation`

## 1. 目标与事实边界

E11-S04 已把获批建议一次性分派到真实 CP6 `MobileTask`，并保存每个任务的稳定 OperationId 和命令回执。E11-S05 在此基础上提供可审计的执行状态、三层幂等重放、显式失败重试，以及只针对尚未开始任务的安全整批补偿。

`MobileTask`、`MobileTaskEvent` 和 `TaskCommandReceipt` 继续是执行事实源。Space 不复制任务状态，不自动认领、启动、暂停、完成或解决任务异常，不修改库存、预留、订单或来源单据，也不伪造 WCS/PDA 回执。

## 2. HTTP、权限与审计

在 E11-S04 审批资源下新增：

- `GET .../approval-requests/{approvalRequestId}/execution`
- `PUT .../approval-requests/{approvalRequestId}/retry-requests/{actionId}`
- `PUT .../approval-requests/{approvalRequestId}/compensation-requests/{actionId}`

GET 沿用 `space:operations:dispatch:read`，两个动作分别要求 `space:operations:dispatch:retry` 与 `space:operations:dispatch:compensate`。审计动作固定为：

- `space.operations.dispatch-execution.read`
- `space.operations.dispatch-execution.retry`
- `space.operations.dispatch-execution.compensate`

外部 Portal 主体、空租户/操作者、越站点访问均失败关闭。动作请求理由必填且最多 500 字符；`actionId` 必须为非空 UUID。

## 3. 实时执行状态

GET 每次从当前 CP6 WMS 行和事件读取事实，并返回统一观察时间、批次状态、计数和逐任务证据。逐任务保留 rank、TaskId、人员外部身份、原始分派 OperationId、WMS 数字状态、映射状态、ExecutionVersion、StartedAt/DoneAt、最近事件类型与时间；不返回 `AssignedTo`、内部用户 ID、姓名或邮箱。

逐任务状态固定为：

- `Assigned`：仍为 Pending，且分派人和执行版本与审批快照一致；
- `InProgress`、`Paused`、`Exception`、`Completed`、`PartiallyCompleted`、`Cancelled`：WMS 当前真实状态且所有者仍一致；
- `Compensated`：审批已补偿，任务仍为 Pending、未分派且执行版本一致；
- `Released`：未补偿却变回未分派；
- `Diverged`：被改派/接管或身份、执行版本不一致；
- `Missing`：任务事实缺失。

批次状态固定为 `PendingApproval`、`Rejected`、`Cancelled`、`Stale`、`AssignmentFailed`、`Assigned`、`Executing`、`Completed`、`Compensated` 或 `AttentionRequired`。任何 Missing/Diverged/Exception/Cancelled/PartiallyCompleted 均进入 `AttentionRequired`，不得被汇总成成功。

## 4. 三层幂等回执

1. OA 批准/拒绝回调：相同 FlowInstance、相同决定人与相同终态的重复回调无副作用返回；不同决定人或相反终态仍冲突。
2. 任务适配器：相同 OperationId 的完整、同命令、同任务、同载荷回执可重放；部分回执、命令/任务/结果不一致均以不确定结果失败关闭，禁止重复写。
3. 重试/补偿动作：`SpaceDispatchExecutionAction` 以调用方 UUID 为身份并保存规范化载荷 SHA-256、动作、操作者、时间、状态、适配器、回执和失败码。相同 ID/相同载荷返回 `Duplicate`，相同 ID/不同载荷返回 409。

## 5. 失败重试

- 只允许对 `FailedNoEffect` 审批执行人工重试；每个审批最多三个不同的重试动作，相同 actionId 重放不计数。
- 每次重试重新验证当前 Published、建议哈希、真实且新鲜的 Idle 人员、用户映射、任务 Pending/未分派/并发证据和 WMS 作用域。
- 验证漂移时审批转为 `Stale`，动作保存 `RejectedNoEffect`；适配器明确无效果失败时保持 `FailedNoEffect`，动作保存 `FailedNoEffect`；成功时审批转为 `Applied` 并保存原始稳定 OperationId 回执。
- 未知提交结果不得伪装为 `FailedNoEffect`；数据库/事务异常继续抛出并整体回滚，由同一 actionId 安全重放。

## 6. 安全补偿

- 只允许补偿 `Applied` 审批，且全部任务仍为 Pending、仍分派给原人员、ExecutionVersion 未变化、StartedAt/DoneAt 为空、原始分派回执完整匹配。
- 全部预检通过后，在同一 CP6 工作单元中整批清空 `AssignedTo`，追加 `AssignmentCompensated` 事件和 `space-dispatch-unassign` 命令回执，并把审批置为 `Compensated`。
- 每个补偿任务使用由 actionId 和 rank 确定性派生的 OperationId；动作重放只读取完整匹配的回执，不重复撤销。
- 任一任务已开始、暂停、异常、完成、取消、被释放、被接管、缺失或证据损坏时，整批 0 写入并返回稳定失败码。此时必须转交现有 WMS V2 异常/接管/取消流程人工处理，Space 不越权补偿库存或执行状态。

## 7. 持久化、Viewer 与本地化

- 新增租户隔离的执行动作表，并为审批增加补偿时间/操作者/理由和重试次数；动作表使用复合租户外键、查询索引、动作/状态/哈希检查约束。
- DSP 面板在审批终态后显示实时批次/逐任务状态、时间证据和动作回执；失败无效果时允许填写理由并重试，全部仍可安全补偿时才显示补偿入口。
- 关闭面板、刷新建议、站点切换或组件卸载会使旧执行响应失效。动作不自动触发，界面明确提示补偿只撤销未开始分派。
- 新增简中、繁中、英语、日语、韩语完整词条并同步生成快照；既有 i18n 静态欠账不得净增加。

## 8. 自动化门禁与交付

- 服务/适配器覆盖执行状态映射、批次聚合、隐私、OA 回调重放、适配器完整/部分/冲突回执、动作幂等冲突、三次重试、重试成功/漂移/失败、补偿成功及每类阻断条件。
- API/权限/审计、实体/Migration/租户过滤、前端 API/面板/旧响应、本地化均自动化。
- 运行 Space Unit、默认 Integration、环境可用时 SQL 集成、CP6.Tests、前端全量、TypeScript strict、生产构建、solution Release、EF pending、SDK drift、i18n 差异和 `git diff --check`。
- 功能分支先推远端备份；通过后 no-ff 合入 `integration/space-v1-20260730`，合并态复验并推送，再验证祖先关系后删除临时分支/工作树。`main` 在 Space 整体发布边界批准前不改动。

## 9. 明确不做

- E11-S06 优化前后效果评估、收益基线或看板。
- 自动重试后台 Worker、自动补偿、跨外部 WMS/WCS 的 Saga 或不确定结果猜测。
- 自动认领、启动、暂停、完成、改派、接管、取消或解决任务异常，以及库存/预留/订单/来源单据写入。
- 未认证 WCS 命令、外部 PDA 推送、外部 Portal 操作、技能/资质/班次推断，或扩展冻结的 Design v1 HTTP/SDK。
