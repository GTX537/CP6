# Space E11-S04 建议审批与任务适配实现计划

状态：合同冻结，进入实现  
起始基线：`bf1bf4ca5f5cdcd4c8bc4f4a15939e694353eec4`  
功能分支：`codex/space-e11-s04-dispatch-approval-adapter`

## 1. 目标与依赖

E11-S03 已保存不可变的人员/任务调度建议，并明确要求任何执行前重新验证任务、人员和 Published 空间事实。E11-S04 在该证据上增加受控审批，并把审批通过的选择转换为 CP6 `MobileTask` 的分派；任务由 WMS 持有，Space 不接管库存、订单、认领或执行状态。

CAD 授权黄金集、供应商 SDK/凭据和冻结 Worker 仍阻塞 E02/E03/E13/E06 主链，本卡不绕过这些门禁，也不扩展冻结的 Design v1 HTTP/SDK。

## 2. HTTP、权限、审计与幂等

- 新增内部资源：
  - `PUT /api/space/operations/v1/sites/{siteId}/dispatch-recommendations/{recommendationId}/approval-requests/{approvalRequestId}`
  - `GET /api/space/operations/v1/sites/{siteId}/dispatch-recommendations/{recommendationId}/approval-requests/{approvalRequestId}`
  - `POST /api/space/operations/v1/sites/{siteId}/dispatch-recommendations/{recommendationId}/approval-requests/{approvalRequestId}/cancel`
- PUT 需要 `space:operations:dispatch:submit`，GET 需要 `space:operations:dispatch:read`，取消需要 `space:operations:dispatch:cancel`；审批动作继续由 OA 待办及其既有授权控制。
- 审计动作固定为 `space.operations.dispatch-approval.submit/read/cancel`。所有路径均使用 Problem Details，GET 显式读审计。
- 调用方提供非空 UUID 作为审批请求身份。相同规范化负载重放返回 `Duplicate`；同一 ID 不同负载返回 409。每个建议同时最多一个 `PendingApproval` 请求，终态后可用新 ID 重新提交。
- 外部 Portal 主体、空租户/操作者、越站点访问均失败关闭。提交人与最终审批人必须分离。

## 3. 选择与不可变审批快照

- 请求只接受 1～100 个唯一 `selectedRanks` 和必填、最多 500 字符的 `reason`。Rank 必须存在于指定 E11-S03 建议，选择内任务和人员仍各自唯一。
- 只允许 `AssignmentsGenerated` 建议；模拟人员、无内部 `UserId`、停用用户、超过 WMS `AssignedTo` 长度的用户名均不可提交。
- 提交时把建议 ID/哈希、Published 版本、仓库、定义版本、选中 rank，以及每条任务并发证据、人员来源身份、内部用户映射和双时点保存到 CP6 审批业务行。对外 DTO 不返回 `UserId`。
- 业务类型固定为 `SPACE_DISPATCH_ASSIGNMENT`，使用 OA 已发布流程和稳定 `FlowInstanceId`。业务行、OA 实例及表单快照由同一 `CP6Context` 保存。

## 4. 审批通过前的失败关闭复验

最终通过回调必须重新验证全部选择，任何一项不满足即整批 `Stale` 或 `FailedNoEffect`，不修改任何任务：

- 当前 Published 版本仍等于建议版本，仓库和租户仍一致；
- 人员来源仍为 Real，当前位置与工作状态双时点均存在且在 E10 阈值内，工作状态仍为 `Idle`；
- 人员当前态仍映射到快照中的同一内部用户，该用户仍启用且用户名未变化；
- 任务仍为同仓库、`Pending`、未分派，TaskType、ContractVersion、ExecutionVersion 和 RowVersion 与建议证据完全一致；
- 审批操作者的 WMS 仓库/库区作用域覆盖全部任务；适配器和数据源必须为真实 CP6 MobileTask。

人员位置或任务在建议生成后变化是正常并发，不做猜测性修补，也不自动换人或换任务。

## 5. 原子任务适配

- 新增内部批量适配口 `ISpaceDispatchTaskAdapter`；首个实现固定为 `cp6-mobile-task-assignment-v1`。
- 适配器先完整预检，再在同一个 `CP6Context` 工作单元中设置 `MobileTask.AssignedTo`，并为每条任务追加 `Assigned` 事件和幂等 `TaskCommandReceipt`。EF RowVersion 在最终保存时再次保护并发。
- 多条任务与审批业务状态一次保存：全部生效后请求进入 `Applied`；任一预检失败则进入 `Stale`/`FailedNoEffect` 且零任务变化。
- 本卡只做 Assign。不得 Claim、Start、Takeover、Complete，不修改库存、预留、订单或人员状态；PDA 通过既有 MobileTask 读取获得分派。没有已认证 WCS 写适配器时明确失败关闭，不伪装为 WCS 已派发。

## 6. 状态与回读

状态固定为 `PendingApproval`、`Applied`、`Rejected`、`Cancelled`、`Stale`、`FailedNoEffect`。回读提供请求身份、建议身份、选择摘要、理由、审批实例、请求/决定/应用时间、适配器 ID、公开安全的失败码和每条任务的应用回执；不返回内部用户 ID、OA 私有变量或异常文本。

申请人仅能取消自己的 `PendingApproval` 请求；取消同步撤回 OA 待办。已决定或已应用请求不可取消。

## 7. Viewer 与本地化

- DSP 面板允许逐条选择建议、填写理由并提交审批；默认不自动选择、不自动提交。
- 明确显示“审批通过后只分派任务，不认领、不启动”，显示当前审批状态、OA 实例、适配器、回执和安全失败码。
- 建议刷新、面板关闭、站点切换或组件卸载会清除未提交选择；已提交请求可按其 ID 回读。
- 新增简中、繁中、英语、日语、韩语完整词条并同步快照；既有 i18n 静态欠账不得净增加。

## 8. 自动化门禁与交付

- 单元/集成覆盖请求规范化、幂等冲突、单活动请求、外部/租户/站点隔离、审批人分离、取消、OA 回调、Published/人员/用户/任务并发复验和模拟来源拒绝。
- 适配器覆盖批量全有或全无、WMS 作用域、RowVersion、事件/回执和重放。
- API/权限/审计/流程种子、前端 API/面板/Viewer 交互、本地化均自动化。
- 运行 Space Unit、默认 Integration、环境可用时 SQL 集成、CP6.Tests、前端全量、TypeScript strict、生产构建、solution Release、EF pending、SDK drift、i18n 差异和 `git diff --check`。
- 功能分支先推远端备份；门禁通过后 no-ff 合入 `integration/space-v1-20260730`，合并态冒烟通过再推集成并删除已合并临时分支/工作树。`main` 在 Space 整体发布边界批准前不改动。

## 9. 明确不做

- E11-S05 执行状态、回执轮询、重试、异常补偿或跨系统最终一致性状态机。
- E11-S06 效果评估、基线对照或收益看板。
- 自动认领、启动、完成、改派已分配任务，或任何库存/订单事实写入。
- 未认证 WCS 命令、真实外部 PDA 推送、外部 Portal 审批、技能/资质/班次/工时/设备资格推断。
- 在审批 DTO 中暴露姓名、邮箱或内部 `UserId`，以及扩展冻结的 Design v1 HTTP/SDK。
