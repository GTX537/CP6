# E11-S04 调度审批与任务适配器交付报告

- 状态：功能分支验证完成，等待进入 Space 受控集成分支
- 起始基线：`bf1bf4ca5f5cdcd4c8bc4f4a15939e694353eec4`
- 合同提交：`098fb54bb316f69d2c0a513a68ca0f9290a062d7`
- 功能提交：`a7298e28`
- 功能分支：`codex/space-e11-s04-dispatch-approval-adapter`
- Migration：`20260802184419_SpaceE11S04DispatchApproval`

## 1. 交付结果

E11-S04 将 E11-S03 的不可变调度建议接入 CP6 OA 审批与真实 `MobileTask` 分配链路。新增提交、读取和取消审批请求接口，使用调用方提供的 UUID 保证提交幂等；审批通过后重新验证 Published 版本、建议行与哈希、人员实时性和空闲状态、内部用户映射、任务待处理/未分配状态及并发证据，然后一次性执行整批分配。

审批请求保存原始建议快照、选中 rank、任务和人员事实、来源时间、并发版本、内部映射与执行回执。对外响应不会暴露人员姓名、邮箱或内部人员 `UserId`。任何一项事实过期或不一致都会关闭写入，结果进入 `Stale` 或 `FailedNoEffect`，不会产生部分分配。

## 2. 安全与业务边界

- OA BizType 固定为 `SPACE_DISPATCH_ASSIGNMENT`，提交人与审批人必须分离。
- 默认种子把管理员配置为指定审批人；生产环境必须为提交者委派提交权限，或按组织治理要求调整 OA 审批人，不能依赖管理员自批。
- 权限拆分为 `space:operations:dispatch:submit/read/cancel`，审计拆分为 `space.operations.dispatch-approval.submit/read/cancel`。
- 执行适配器标识为 `cp6-mobile-task-assignment-v1`；它只分配现有任务，不认领、不启动、不修改库存/订单、不伪造 WCS 命令。
- 批次在同一工作单元中完成完整预检、任务分配、事件与回执落库；任一项失败则整批无效果。
- PDA 继续读取现有 `MobileTask`，本卡没有另建执行事实源。

## 3. Viewer 交互

调度建议面板新增显式勾选、审批理由、提交、刷新、取消、状态与回执展示。只有真实人员建议可以提交；关闭面板、切换结果或发起新请求都会使旧异步响应失效，避免旧审批状态覆盖当前界面。

新增 21 行五语言种子，生成式 i18n 快照从 4,542 增至 4,561 个唯一键。静态门禁仍报告 908 个历史缺失项，相比原基线减少 1 个，本卡没有新增缺失键。

## 4. 验证证据

| 门禁 | 结果 |
|---|---|
| 审批服务与任务适配器聚焦 | 8 passed |
| 权限、合同、种子与基础设施聚焦 | 44 passed |
| Space Unit Release 全量 | 249 passed / 0 failed |
| Space Integration Release 默认全集 | 224 passed / 0 failed / 62 SQL 环境门禁 skipped |
| CP6.Tests Release 全量 | 2,757 passed / 0 failed / 17 环境门禁 skipped |
| 前端聚焦 | 2 files / 19 tests passed |
| 前端全量 | 118 files / 656 tests passed |
| 前端严格类型检查 | passed |
| 前端生产构建 | passed；仅既有大 chunk 提示 |
| 完整 solution Release 非增量构建 | exit 0 |
| EF pending model | 无待迁移模型变化 |
| Design V1 SDK drift | passed |
| TypeScript SDK strict no-emit | passed |
| Git 差异检查 | passed |

默认测试集未连接 SQL Server，因此 62 项 SQL 集成门禁按既有约定跳过；这不是通过结果，部署前仍需在具备 SQL Server 的发布环境执行迁移和 SQL 集成门禁。

## 5. 明确未做与下一步

本卡不包含 E11-S05 的执行状态、幂等回执、失败重试与补偿治理，也不包含 E11-S06 的效果评估。CAD/E02 的正式授权黄金集、格式/版本/语义覆盖、供应商 SDK/凭据及冻结 Worker 证据仍未满足，本卡没有绕过这些外部门禁，也没有扩展冻结的 Design v1 HTTP/SDK。

下一步是在受控集成分支复验合并态，更新项目状态并推送远端。确认远端集成 tip 精确包含功能 tip 后，才删除本地/远端临时功能分支及功能工作树；`main` 继续保持不变。
