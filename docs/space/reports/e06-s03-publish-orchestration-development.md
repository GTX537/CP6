# E06-S03 仓库级发布编排开发报告

日期：2026-08-07
状态：功能分支完成，待 no-ff 集成
集成基线：`0bde7bc9005f73a4cd1ff4e633d73d0c92f39046`
功能分支：`codex/space-e06-s03-publish-orchestration`
功能提交：`48082680ffa157e1d774eb504b53b657a82a1749`
no-ff 集成提交：待生成

## 1. 本卡边界

本切片只交付 E06-S03：把 E06-S02 的确定性发布预览重新在服务端构建、校验并持久化为不可变
PublishPlan，以同一仓库槽位内的可恢复 Saga 执行 WMS 预检、分批写入、状态查询、回执保存、回读验证和
CP6 运行态激活。只有全部外部写入已确认且回读一致时，Published 指针才会切换。

本卡没有实现 E06-S04 的后台队列、超时调度、自动/人工重试与独立发布审计事件，没有实现 E06-S05 的历史版本
再发布回退，也没有实现 E06-S06 管理 UI。当前执行为请求内同步编排；部分成功、不确定结果或运行态激活失败会
保存证据并进入 `ReconciliationRequired`，不会伪装成完整成功。

## 2. 不可变计划、幂等与仓库槽位

- `POST /api/space/design/v1/versions/{versionId}/publish-attempts` 要求 `space:model:publish`、
  `Idempotency-Key`、期望 Published 版本、ValidationRunId、PlanHash 和可选审批引用。
- 服务端重新读取权威版本、ContentRevision、ValidationRun、WMS 能力和当前 Published 指针，并重建 E06-S02
  计划；客户端提交的计划内容或状态不会被信任。
- 同一幂等键与同一请求稳定重放；同键不同请求返回冲突。Serializable 事务与过滤唯一索引保证一个
  Tenant/Site 同时只有一个活动发布槽位。
- 持久化的计划 JSON、PlanHash、WMS 回执和批次身份字段均禁止修改或删除。目标版本只在槽位事务成功后从
  Ready 进入 Publishing。

## 3. WMS Saga 与失败边界

- 计划项以稳定 OperationKey 和 PayloadHash 分批；执行顺序为 WMS preflight、apply、必要时查询 operation
  status、保存 receipt/evidence、逐项回读验证。
- 一旦外部写入可能开始，后续证据保存使用独立于 HTTP 取消信号的 token，避免客户端断线造成“WMS 已写、
  CP6 无记录”。
- 预检或首个零影响 WMS 失败会将目标退回 Ready 并释放槽位。
- 部分成功、状态未知、回执矛盾、回读不一致或运行态激活失败都会保留旧 Published 指针，将目标置为
  ReconciliationRequired，保存 ReconciliationIssue，并继续占用槽位，等待 E06-S04 恢复流程。

## 4. 运行态激活与指针切换

- WMS 全部确认后，SpaceContext 与 CP6Context 共用同一物理 SQL 事务，物化 Floor、Zone、Aisle、Rack、
  Location 和 `Space_RuntimeElement`。
- 事务内重新读取 CP6 运行态投影并比较哈希；只有回读哈希一致，旧 Published 才进入 Superseded，目标才从
  Publishing 进入 Published，并更新模型的 CurrentPublishedVersionId。
- 任一运行态写入或回读失败都回滚本地事务，旧生产指针保持不变，外部写入证据进入对账状态。

## 5. API、权限与数据模型

- 新增创建发布尝试和读取发布尝试两个 API；创建要求 `space:model:publish`，读取要求 `space:model:read`，并分别
  配置写审计和读审计元数据。
- OpenAPI operation 数从 105 增至 107，C# 与 TypeScript SDK 已重新生成并通过漂移检查。
- 新增六张表：`Space_PublishPlan`、`Space_PublishAttempt`、`Space_PublishBatch`、`Space_WmsReceipt`、
  `Space_ReconciliationIssue`、`Space_RuntimeElement`。
- Migration `20260807135544_SpaceE06S03PublishOrchestration` 的 Down 使用 `THROW 51019` 失败关闭；修复必须走更高
  版本 forward-fix，避免破坏不可变发布证据。

## 6. 验证证据

| 门禁 | 结果 |
|---|---|
| E06-S03 领域状态聚焦 | 4/4 passed |
| Controller、权限、审计、OpenAPI 聚焦 | 58/58 passed |
| E06-S03 真实 SQL：成功激活与部分 WMS 对账 | 1/1 fact passed |
| Space Unit 全量 | 452/452 passed |
| CP6.Tests 全量 | 2798 passed / 17 environment-gated skipped / 0 failed |
| 默认 Space Integration 全量 | 259 passed / 90 SQL-gated skipped / 0 failed |
| Infrastructure 与 WebApi Release build | 0 warning / 0 error |
| 完整 `CP6.slnx` Release 双架构 AOT 构建 | 0 error / 10 条既有 warning |
| EF pending model changes | none |
| OpenAPI/C#/TypeScript SDK drift | passed |
| Migration 幂等 SQL | 从 E06-S01 基线连续执行两次，6 张表 / 1 条 migration history |
| `git diff --check` | passed |

完整 solution 使用单线程并保持 Android 双架构 AOT，耗时约 4 分 39 秒。10 条 warning 均来自既有 Core/Test
nullable 或 analyzer 告警，本卡没有新增构建错误。真实 SQL 测试覆盖一次完整成功激活，以及 WMS 部分写入后旧
Published 指针不变、目标进入对账的路径。

幂等 SQL 用 `sqlcmd -I` 启用 SQL Server 要求的 `QUOTED_IDENTIFIER` 会话设置，从 E06-S01 基线库连续执行两次
均通过；临时数据库和容器内测试脚本已经删除。首次未带 `-I` 的执行被过滤索引会话设置拒绝，不计为脚本失败，
也没有残留测试数据库。

## 7. 尚未完成与下一步

1. E06-S04：后台发布队列、超时、稳定重试、人工干预、对账恢复和追加式发布审计；恢复前必须继续保留旧生产版本。
2. E06-S05：以历史版本创建新的发布动作实现可审计回退，不修改或删除历史证据。
3. E06-S06：预检、差异、审批、执行进度、失败原因、对账和回退入口 UI。
4. 生产 Hosted Space Worker、正式 CAD Provider 与有权使用的真实黄金集、E03-S04/S05 权威匹配写入链，
   以及 Beta/GA 跨职能证据仍是独立缺口。

本卡是 E06-S03 开发闭环，不是完整 E06、Beta 或 GA 发布签收。
