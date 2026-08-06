# E13-S09 决策与人工锁定修正开发切片

- 状态：Ready for controlled integration
- 日期：2026-08-05
- 起始集成基线：`e469c6ca`
- 功能分支：`codex/space-e13-s09-proposal-decisions`
- 功能提交：`c87289f2`
- 目标分支：`integration/space-v1-20260730`

## 1. 交付结论

E13-S09 已把 E13-S08 的只读审核工作台接到权威服务端决策链：内部审核人员可以分页读取
Run、Proposal、Issue 和追加式 Decision 历史，并对单条 Proposal 执行 Accept、Reject 或
Modify，对最多 1,000 条 Proposal 执行批量 Reject。每次写入都重新核验租户、Site、Run
状态、Draft ContentRevision、Proposal rowversion、ReviewEtag、服务端批量资格和幂等键。

Decision、人工终值、锁定字段、理由、评论、操作者、批次和时间均持久化；Decision 和跨 Run
锁定事实均由 `SpaceContext` 拒绝更新或删除。审核完成时间只由服务端在“所有非 Obsolete
Proposal 已决策且无 Open Blocking Issue”时写入一次。整个切片仍不写 Draft、Published、WMS
或设备控制数据；E13-S10 才能执行 Staging 和原子 Apply。

## 2. 决策、一致性与幂等

- 单条决策要求当前 Proposal rowversion；并发变化返回稳定 409，客户端刷新权威审核状态。
- ReviewEtag 绑定 Run rowversion、全部状态计数、Open Blocking 计数和最后 Decision 身份；
  游标同时绑定 Tenant、Run、ReviewEtag 和完整筛选哈希，不能跨审核状态或筛选重放。
- 写入使用 Serializable 事务；Proposal 状态、Decision、Issue 关闭、ReviewCompletedAtUtc 和
  24 小时重放/90 天保留的幂等记录原子提交。同一键不同请求或过期请求返回 409。
- Accept/Reject 不允许携带 Patch 或锁定字段；Modify 必须携带 1～32 个唯一 Patch，并使锁定
  字段集合与实际修改路径完全一致。
- 显式 ID 与筛选批量严格二选一；空、重复、未知、陈旧或超过 1,000 项的选择失败关闭。
  批量 Accept 默认关闭；即使配置开启也只允许 High 且无 Blocking 的 Proposal。本轮前端只
  开放批量 Reject，并明确展示质量黄金集/Wilson 下界门禁仍未满足。

## 3. Patch、问题关闭与敏感内容边界

权威策略为 `space-ai-proposal-patch-v1`。只接受 RFC 6902 `replace`，并按 Floor、Zone、Aisle、
Rack、Wall、Column、Door、Dock、StaticEquipment 定义精确字段白名单。目标字段必须已存在，
所有值必须是有界字符串；业务枚举逐值校验。关系只能指向同一 Run 内类型匹配、非 Obsolete、
非自身的 SourceKey。

Blocking Proposal 不能普通 Accept。只有 `AI_RELATION_AMBIGUOUS`、`AI_PROPOSAL_TYPE_INVALID`、
`AI_BUSINESS_ENUM_INVALID` 可由合规 Modify 修复并关闭；Reject 可用追加式拒绝 Decision 关闭该
Proposal 的 Blocking Issue。Run 级 Blocking 始终需要独立解决。ReasonCode 只接受大写稳定令牌；
评论拒绝控制字符和 Bearer、API key、password、secret 等凭据样式内容。

前端 Modify 对话框可在一次 Decision 中编辑并锁定多个不重复字段，最多 32 个；提交后不允许
再用第二条 Decision 覆盖同一 Proposal，避免“逐字段提交”造成半完成状态。

## 4. 跨 Run 人工锁定

新增不可变 `Space_GenerationLockedFact`：目标 Run、BasedOnRun、原 Proposal/Decision、SourceHash、
SourceKey、ProposalType、FieldPath、标量终值、匹配方法/分数和确认状态全部可追溯。目标 Run 与
来源 Run 必须属于相同 Site/ModelVersion。

`ISpaceAiLockedFactService` 从 BasedOnRun 的 Modified/Applied Proposal 及最终 Modify Decision
投影锁定事实。相同 SourceHash 只按 `SourceKey + ProposalType + FieldPath` 自动继承，匹配方法为
`SameSourceIdentity`、分数为 1、确认状态为 true；唯一索引和 Serializable 事务使重复加载只产生
一份不可变事实。SourceHash 不同则返回空，不进行猜测式自动锁定。

这满足 E13-S09 的“同一来源重跑保留锁定值”开发验收，但当前 BuildScene 生产步骤执行器仍按
E13-S03 的既定边界默认失败关闭；本服务尚未被真实 Worker 的 `LoadLockedFacts` 步骤自动调用。
不同 SourceHash 的规范几何指纹“建议继承 + 用户确认”也尚未实现，不能把本轮的失败关闭行为
描述为该异源建议流程已完成。

## 5. API、权限、审计与前端

新增六个 Design V1 操作：

- `GET /generation-runs/{runId}/review`
- `GET /generation-runs/{runId}/proposals`
- `GET /generation-runs/{runId}/issues`
- `GET /generation-runs/{runId}/decisions`
- `POST /generation-runs/{runId}/decisions`
- `POST /generation-runs/{runId}/decisions:batch`

四个读取要求 `space:model:review-ai` 并记录受控读取审计；两个写操作同时要求
`space:model:review-ai` 和 `space:model:edit`，记录稳定 mutation 审计动作。服务层在任何查询前
拒绝 external principal，并复核 Tenant 与 Site。所有操作提供统一 400/401/403/404/409/422/500
Problem Details；操作名与既有 Planning Decision API 保持全局唯一。OpenAPI、C# SDK 和 TypeScript
SDK 均由同一生成脚本产出。

Design 编辑器可通过 `generationRunId` 打开实时决策面板，筛选 Proposal、显示阻断摘要、逐项
Accept/Reject/Modify、批量 Reject，并在 409 时刷新；原 E13-S08 本地只读工件面板继续保留。

## 6. 数据库与迁移

Migration：`20260806054950_SpaceE13S09ProposalDecisions`。

- 新增 `Space_GenerationLockedFact`、租户复合外键、唯一索引、RowVersion 和匹配约束。
- `Space_ModelIssue` 新增 GenerationRun/Proposal、ResolutionKind、ResolutionDecision；历史已由
  command batch 关闭的记录在加约束前前向回填为 `CommandBatch`。
- Resolution 约束保证 Open/Acknowledged 不携带解决来源，Resolved 必须且只能由 command batch、
  proposal decision 或 proposal rejection 之一关闭。
- 幂等 SQL 由 Migration 重新生成后规范化内容完全一致；`has-pending-model-changes` 返回无漂移。

完整 SQL 首轮 312 项中，310 项通过；两个既有历史升级夹具分别因一次性插入互相引用的
Model/Version、以及在旧迁移阶段用当前实体写入当时不存在的 `Purpose` 列而失败。夹具已改为
真实分阶段保存和按历史物理列写入，聚焦 2/2 后完整套件重跑 312/312、0 skipped。没有修改生产
迁移来迎合测试。

## 7. 验证证据

| 门禁 | 结果 |
|---|---|
| E13-S09 真实 SQL 聚焦 | 3 passed / 0 failed / 0 skipped |
| Space Unit 全量 | 413 passed / 0 failed / 0 skipped |
| Space Integration + KOUSQLSERVER | 312 passed / 0 failed / 0 skipped |
| CP6.Tests 全量 | 2779 passed / 0 failed / 17 environment-gated skipped |
| CAD 实验工具 | 25 passed / 0 failed / 0 skipped |
| 前端 E13 聚焦 | 2 files / 7 tests passed |
| 前端全量 | 128 files / 692 tests passed |
| 前端 type-check / production build | passed；仅既有大 chunk 提示 |
| OpenAPI/C#/TypeScript SDK drift | passed |
| EF 模型/迁移及幂等 SQL drift | passed |
| 完整 solution Release 非增量单线程构建 | 0 errors / 10 existing warnings；Desktop/Android AOT 保持 |
| 手写 C# whitespace 与 `git diff --check` | passed；`SpaceContext` 历史全文件缩进债务未扩散 |

## 8. 回滚与下一步

运行回滚面为关闭审核写入口和保持批量 Accept Disabled；Decision、锁定事实和审计不做破坏性
回滚。数据库变更使用前向修复 Migration，不删除已产生历史。由于本切片从未写 Draft，停用后
Published、WMS 与设备状态不受影响。

下一张独立卡是 E13-S10：在锁定 Model/Version/Run 后构造 Staging，重新验证 Proposal、Issue、
Revision、唯一/引用/碰撞/边界，再以单事务只增加一个 ContentRevision；任何 409 或校验失败都
不得产生部分 Draft。E13-S11 继续负责 Stale/取消/重试恢复；异源几何建议继承、真实 BuildScene
执行器、外部 Provider、授权真实 CAD 和正式黄金集仍是后续独立缺口。
