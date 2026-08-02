# E11-S05 执行状态、幂等回执与安全补偿交付报告

- 状态：功能分支全量门禁、远端备份、Space 受控集成与合并态复验完成，待临时资源清理
- 起始基线：`b0231e241e881046d75c938e2de4c88527ec8725`
- 合同提交：`139c76b5bd7eafc62bbd4468603f1dc7baa31294`
- 功能提交：`e8df8288`
- 文档提交：`a0b247ab`
- no-ff 集成提交：`cf35849c`
- 功能分支：`codex/space-e11-s05-execution-receipts-compensation`
- Migration：`20260802192420_SpaceE11S05ExecutionReceiptsCompensation`

## 1. 交付结果

E11-S05 在 E11-S04 的 OA 审批与真实 `MobileTask` 整批分派之上，新增实时执行状态、持久化动作回执、受限人工重试和安全补偿。执行读取直接聚合当前 `MobileTask`、任务事件、原始命令回执和执行动作账本，不复制或伪造另一套任务状态。

新增三个内部运营接口：读取审批批次执行状态、以调用方 UUID 发起分派重试、以调用方 UUID 发起未开始分派补偿。动作 UUID 与规范化 payload hash 共同构成幂等身份；相同身份和内容返回原结果，不同内容复用同一 UUID 返回冲突。OA 审批/拒绝回调与底层任务适配器回执也支持精确重放，部分或冲突回执一律失败关闭。

## 2. 重试与补偿边界

- 只有 `FailedNoEffect` 批次可以人工重试，每次都重新验证 Published 版本、不可变建议证据、人员实时性/空闲状态、内部用户映射、WMS 范围及任务并发事实。
- 每个审批批次最多接受 3 次不同的人工重试；同一动作的精确重放不重复计数，也不会重复写任务事件或命令回执。
- 补偿只允许整批任务仍为 Pending、仍分配给原用户、执行版本未变、从未开始或完成，且原始分派回执完整一致时执行。
- 补偿只撤销 `AssignedTo`，并追加任务事件、补偿命令回执和执行动作账本；它不修改任务执行版本、任务执行结果、库存、订单或 WCS/PDA 事实。
- 任一任务已开始、暂停、异常、完成、部分完成、取消、被释放、被接管、版本偏离、缺失或回执不一致时，整批补偿无效果并留下失败回执。

## 3. 权限、审计与 Viewer

新增 `space:operations:dispatch:retry` 与 `space:operations:dispatch:compensate` 权限；执行读取继续使用 `space:operations:dispatch:read`。审计动作固定为 `space.operations.dispatch-execution.read/retry/compensate`。

Viewer 调度面板现在展示实时聚合状态、批次计数、每项 WMS 状态/执行版本/开始与完成时点、最近事件、动作历史、重试余额和补偿阻断码。重试和补偿必须填写显式原因；关闭面板、生成新建议或切换审批后，旧异步响应不能覆盖当前界面。

新增 28 行五语言种子，其中 26 个键加入生成式快照，快照从 4,561 增至 4,587 个唯一键。i18n 静态门禁仍报告 908 个既有缺失项，本卡净新增缺失为 0。

## 4. 验证证据

| 门禁 | 结果 |
|---|---|
| 服务、适配器与幂等/补偿聚焦 | 14 passed |
| 权限、合同、种子与基础设施聚焦 | 43 passed |
| 前端 API 与执行面板聚焦 | 2 files / 21 tests passed |
| Space Unit Release 全量 | 249 passed / 0 failed |
| Space Integration Release 默认全集 | 230 passed / 0 failed / 62 SQL 环境门禁 skipped |
| CP6.Tests Release 全量 | 2,757 passed / 0 failed / 17 环境门禁 skipped |
| 前端全量 | 118 files / 658 tests passed |
| 前端严格类型检查与生产构建 | passed；仅既有大 chunk 提示 |
| 完整 solution Release 非增量构建 | exit 0；10 条既有 warning |
| EF pending model | 无待迁移模型变化 |
| Design V1 SDK drift | passed |
| TypeScript SDK strict no-emit | passed（项目锁定编译器） |
| 原生客户端 OpenAPI surface | 新增执行 DTO 后哈希已审阅更新并复验同步 |
| i18n 静态门禁 | 908 项既有欠账；本卡无新增 |
| Git 差异检查 | passed |

受控集成提交 `cf35849c` 上的合并态复验再次通过：服务/适配器 14/14、权限/合同/种子 35/35、前端 2 files / 21 tests、前端类型检查、Design V1 SDK drift、TypeScript SDK strict no-emit、EF pending model 与 Git 差异检查。

全量回归曾发现新增 SQL Server `LEN()` 检查约束会阻断 SQLite 测试建库；该约束已移除，哈希仍由固定长度列、应用生成与 payload 比较保护，随后 2,757 项 CP6.Tests 全量通过。

默认测试集未连接 SQL Server，因此 62 项 SQL 集成门禁按既有约定跳过；这不是通过结果。部署前仍需在具备 SQL Server 的发布环境执行迁移、真实 SQL 集成门禁和备份/回滚演练。

## 5. 明确未做与下一步

本卡没有自动重试 Worker，没有认领、启动、暂停或完成任务，没有修改库存/订单，没有发出 WCS 命令，也没有把 Space 变成 WMS 执行事实源。E11-S06 的效果评估与闭环度量仍是下一张独立卡。

CAD/E02 的正式授权黄金集、格式/版本/语义覆盖、供应商 SDK/凭据及冻结 Worker 证据仍未满足；E03-S04、E04-S05 和后续依赖卡继续等待这些外部门禁。本卡没有绕过它们，也没有扩展冻结的 Design v1 HTTP/SDK。
