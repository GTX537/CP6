# E13-S10 Staging 与原子 AI Apply 开发报告

- 状态：Ready for integration
- 日期：2026-08-06
- 起始集成基线：`b663c4ae`
- 功能分支：`codex/space-e13-s10-atomic-apply`
- 功能提交：`43dc5534`
- 基线更新纠偏提交：`fbc59fb3`
- 目标分支：`integration/space-v1-20260730`

## 1. 交付结论

E13-S10 已把 E13-S09 的已审查 Proposal 接入可重试 Staging 与短原子提交链。内部审核人员在
Run 已完成审核且目标仍为精确 Draft revision 时，可以提交 Apply；服务先同步校验
ContentRevision、Run rowversion、ReviewEtag、租户、Site、权限与幂等键，再创建唯一
`ApplyGeneration` Job。Worker 把最终 Decision 快照规范化为 Staging、生成不可变 ApplyPlanHash，
随后在一个 SQL 事务内重验并写入 Draft。

成功 Apply 只创建一个 CommandBatch、只推进一次 Floor Revision 和一次 ModelVersion
ContentRevision，并把 Proposal 标记为 Applied、Run 标记为 Succeeded。任一陈旧版本、唯一性、
引用、边界、碰撞、数量、身份或故障注入失败均不会留下部分 Draft；Published 指针、WMS 和设备
控制状态不被修改。

## 2. 原子性、并发与幂等

- POST 接收前要求精确 `expectedContentRevision`、`expectedRunRowVersion`、`reviewEtag` 和
  `Idempotency-Key`；同键同请求重放同一 Job，同键异请求失败关闭。
- Queue 使用 Serializable 事务，并以租户 + Run 的 SQL Server transaction-scoped
  `sp_getapplock` 串行化同一 Run 的并发请求；真实双连接测试证明只创建一个 Job 和一条幂等记录。
- Worker 分为 `PrepareStaging`、`ValidateStaging`、`CommitDraft`。准备结果和 ApplyPlanHash 可安全
  复用；最终事务按 Run → Version → Model 固定顺序加锁，并再次读取当前数据库状态。
- 最终事务开始后不调用 Provider、对象存储、CAD Worker 或 WMS。提交前故障会完整回滚；重试复用
  已验证计划并最终只增加一次 ContentRevision。
- POST 后发生的 revision 变化把 Run/Job 标记为 Stale，并返回稳定 `SPACE_AI_RUN_STALE`；不会补写
  Draft 或修改原 202 响应。

## 3. 新增与既有基线更新语义

审核工作台的差异语义包含 Added、Modified 和 Unchanged，因此 Apply 同时支持新增对象与更新同一
逻辑身份的当前 Draft 基线：

- Zone、Aisle、Rack 和通用 Element 在同类型、同 ModelVersion、同目标 Floor 下按 LogicalId
  更新；跨类型、跨楼层或资产库绑定 Element 的身份碰撞失败关闭。
- RackLevel 和 Location 使用 Rack 派生确定性 ID 对齐。仍存在的项原位更新并恢复 Active；新增项
  补齐；不再属于新派生方案的未绑定项转为 Disabled。
- WMS 已绑定 Location 不允许因 Rack 派生缩减而被移除；匹配身份的绑定库位保留既有业务编码和
  绑定状态。
- 通用 Element 的 Design namespace 语义属性按 key 不区分大小写进行新增、更新和软删除；其他
  namespace 不受影响。
- 修改命令的 `BeforeJson` 保存权威修改前快照，`AfterJson` 保存已验证 Staging；新增对象的
  `BeforeJson` 为 JSON `null`。

## 4. 校验与安全边界

Staging 与最终事务均重新验证：

- Proposal 必须为 Accepted/Modified，且每项都有权威最终 Decision 快照；Run 级 Blocking 和未决
  Proposal 均禁止 Apply。
- 顶层逻辑 ID、派生层/库位 ID、Sequence 和 Proposal 绑定必须唯一；跨对象类型、跨 Rack owner、
  跨 Floor 的碰撞失败关闭。
- Zone/Aisle/Rack 引用、Aisle 与 Zone 归属、Floor 边界、Rack 矩形碰撞、Zone/Aisle/Rack code
  唯一性在准备阶段和最终事务内重验。
- 单次最多 100,000 个 Proposal、1,000,000 个派生 Location；规范化 payload 和 ApplyPlan 使用
  SHA-256 绑定，任何部分 Staging 状态都不允许提交。
- API 同时要求 `space:model:review-ai` 与 `space:model:edit`，服务层在读取 Generation 数据前拒绝
  external principal，并再次检查租户和 Site。审计记录稳定 action、资源、幂等键和执行结果。

## 5. API、SDK 与前端

新增 Design V1 操作：

- `GET /api/space/design/v1/generation-runs/{runId}`
- `POST /api/space/design/v1/generation-runs/{runId}/apply`

POST 成功返回 `202 Accepted`、Apply Job、Run 状态和 `Idempotent-Replay`。GET 返回终态、稳定失败码、
recovery、appliedContentRevision 和各对象计数。OpenAPI required 字段、C# SDK 与 TypeScript SDK 已由
统一脚本生成并通过漂移检查。

前端审核面板只在服务端已完成 Review 且 Run 为 AwaitingReview 时展示 Apply；提交时携带当前冻结
revision/rowversion/etag，收到 202 后轮询 Run 至终态。成功后 Design 编辑器重新加载权威 Scene；
Stale 或异步失败展示服务端 failureCode，不在客户端猜测成功。

## 6. 数据库与迁移

Migration：`20260806110504_SpaceE13S10AtomicApply`。

- 新增 `Space_GenerationStagingElement`，包含租户复合外键、Proposal/Run/Floor 绑定、RowVersion、
  ValidationStatus/ValidationHash，以及 Run 内 LogicalId、ProposalId、SequenceNo 三个活动唯一索引。
- `Space_GenerationRun` 新增 TargetFloor、Apply Job/CommandBatch、冻结 rowversion/review etag、
  ApplyPlanHash、准备时间和 AppliedCounts 字段，并以租户复合外键绑定 Job 与 Floor。
- Zone/Aisle/Rack 增加非空 Name；Rack 增加 RackType。迁移先按 code 前向回填历史 Name，再收紧
  非空约束。
- 已生成幂等 SQL；`has-pending-model-changes` 返回无模型漂移。

## 7. 验证证据

| 门禁 | 结果 |
|---|---|
| E13-S10 真实 SQL 聚焦 | 7 passed / 0 failed / 0 skipped |
| Space Unit 全量 | 413 passed / 0 failed / 0 skipped |
| Space Integration 默认门禁 | 248 passed / 0 failed / 71 SQL-gated skipped |
| CP6.Client.Tests | 71 passed / 0 failed / 0 skipped |
| CP6.Tests 全量 | 2783 passed / 0 failed / 17 environment-gated skipped |
| 完整 solution Debug 测试 | exit 0；上述四套测试零失败 |
| OpenAPI/权限聚焦 | 52 passed / 0 failed |
| 前端全量 | 129 files / 694 tests passed |
| 前端 type-check / production build | passed；仅既有大 chunk 提示 |
| OpenAPI/C#/TypeScript SDK drift | passed |
| EF 模型/迁移漂移 | passed；无 pending model changes |
| `git diff --check` | passed |

真实 SQL 7 项覆盖：同键并发幂等、单 revision 原子新增、Draft stale 零写入、故障回滚与计划复用、
完整 Zone/Aisle/Rack/RackLevel/Location/Element 物化、既有审核基线原位更新与 Rack 派生协调，以及
external principal 读取前拒绝。完整 solution 首次在受限沙箱运行时因本机 SDK/DataProtection 目录
不可读产生环境假失败；在获准本机通道复跑后零失败。

## 8. 清理、回滚与下一步

验证期间恢复的前端 `node_modules` 已删除，回收约 0.31 GB；依赖可由 `npm ci` 重建，源码、锁文件、
构建缓存和 Git 历史保留。

运行回滚面为关闭 AI Apply 入口/权限并停止领取新的 ApplyGeneration Job；已提交的 Draft revision
继续作为普通可编辑 Draft，不自动发布。数据库变更使用前向修复 Migration，不破坏 Staging、命令、
Proposal 或审计历史。

下一独立切片为 E13-S11：完善取消、重试、降级和 Stale 恢复的产品化流程。真实 Worker
`LoadLockedFacts` 自动接线、不同 SourceHash 的几何建议继承与人工确认、外部 Provider、授权真实
DWG/DXF、正式黄金集和发布晋级证据仍是独立缺口；本切片不把合成测试数据描述为正式 CAD 验收。
