# E06-S04 发布队列与恢复开发报告

日期：2026-08-07
状态：已 no-ff 集成
集成基线：`0c416d441109e86b481f12dea7e575bbdf1dcf21`
功能分支：`codex/space-e06-s04-publish-recovery`
功能提交：`0f1ee6a9cc6bf14642763921239fda39fb40ebbc`
no-ff 集成提交：`0c1e75ffa62b446f0b309d4916c87b7886f000d8`

## 1. 本卡边界

本切片交付 E06-S04：把 E06-S03 的请求内发布 Saga 改为持久化后台作业，增加超时、自动重试、人工重试、
对账恢复和哈希链式追加审计。发布入口只冻结权威请求并排队；后台 Worker 执行 WMS 预检、批次写入、回读验证
和运行态激活。任何超时、进程退出、部分成功或不确定结果都不得提前切换 Published 指针。

本卡没有实现 E06-S05 历史版本再发布回退，也没有实现 E06-S06 管理 UI。生产 WMS 端到端演练、正式 CAD
Provider/授权黄金集及 E03-S05 权威匹配写入链仍是独立缺口。

## 2. 持久化队列与租约执行

- 创建发布尝试与创建 Publish Job 在同一数据库事务内完成；HTTP 返回后由 Hosted Worker 按 Tenant 扫描并执行
  Publish/Reconcile 两类作业。
- PublishAttempt 持久化 JobId、冻结请求、当前步骤、排队时间、下一次尝试时间和批次尝试号；PublishBatch 保存
  冻结请求 JSON，使进程重启后可从已确认批次继续，而不是重新猜测外部状态。
- 发布处理器采用 30 分钟步骤超时。租约心跳使用独立 SpaceContext，避免与业务处理器并发使用同一 EF Context。
- 原有 Job Ledger 负责指数退避、尝试次数、终态失败和人工干预；PublishAttempt 状态与 Job 状态同步为
  WaitingRetry、ManualIntervention 或 ReconciliationRequired。

## 3. 自动恢复与人工重试

- WMS 健康检查、能力读取、位置发现、批次 Apply、回读和运行态激活的可恢复故障统一进入 Transient 重试；
  失败证据使用独立于请求取消信号的保存路径，防止外部可能已写入而本地没有记录。
- 已确认批次和回执会被复用；未知或矛盾结果进入对账，不会重复盲写。运行态提交结果不确定时，恢复流程先读取
  当前 Published 指针；若事务实际已提交，则补齐完成与对账解决审计。
- `POST /api/space/design/v1/publish-attempts/{attemptId}/retry` 提供显式人工恢复入口。同一 Idempotency-Key 与
  同一冻结请求稳定重放；存在未解决 ReconciliationIssue 时只创建 Reconcile Job，并要求人工给出解决说明。
- WMS 超时真实 SQL 回归证明：第一次失败后旧生产版本保持 Published；退避到期后第二次执行完成，目标版本才
  切换为 Published。

## 4. 追加式发布审计

- 新增 Space_PublishAuditEvent，事件按 AttemptId/EventNo 单调追加，PrevHash/EventHash 形成 SHA-256 链，数据库
  禁止更新和删除。
- 审计覆盖排队、开始、预检、批次提交/确认、WMS 回读、运行态激活、可重试失败观察、重试调度、人工介入、
  对账请求/解决和最终完成。
- “发现可重试失败”与“作业账本已安排重试”使用不同事件，避免把故障事实与调度决定混为一谈；幂等 EventKey
  防止租约重放产生重复证据。

## 5. 数据库、API 与兼容性

- Migration `20260807144532_SpaceE06S04PublishRecovery` 扩展发布尝试/批次字段并新增审计表；发现仍有 E06-S03
  活动发布时以 `THROW 51020` 失败关闭，避免在无法可靠恢复的旧状态上升级。
- 幂等部署脚本从 E06-S03 基线连续执行两次通过；Down 继续采用前向修复策略并失败关闭。
- 新增人工 retry API，读取发布尝试的 DTO 增加 Job、重试和审计信息。OpenAPI path 数从 107 增至 108，C# 与
  TypeScript SDK 已重新生成并通过漂移检查。

## 6. 验证证据

| 门禁 | 结果 |
|---|---|
| Space Unit 全量 | 458/458 passed |
| Controller、权限、审计、OpenAPI 聚焦 | 59/59 passed |
| CP6.Tests 全量 | 2799 passed / 17 environment-gated skipped / 0 failed |
| 默认 Space Integration 全量 | 259 passed / 93 SQL-gated skipped / 0 failed |
| E06-S04 真实 SQL | 4/4 passed |
| 迁移活动发布失败关闭与幂等双执行 | 2/2 passed |
| 完整 `CP6.slnx` Release 构建（含 Desktop/Android AOT） | 0 error / 7 条既有 warning |
| EF pending model changes | none |
| OpenAPI/C#/TypeScript SDK drift | passed |
| `git diff --check` | passed |

真实 SQL 的 4 条用例覆盖：WMS 超时后自动重试、成功发布与部分结果对账、迁移幂等双执行，以及存在旧活动发布时
拒绝升级。完整 solution 还执行过一次无增量双架构 AOT 构建，结果为 0 error / 10 条既有 warning；最终代码上的
增量复核为 0 error / 7 条既有 warning，均未新增构建错误。

## 7. 尚未完成与下一步

1. E06-S05：以历史版本创建新的、可审计的发布动作，实现安全回退；不得修改或删除历史发布证据。
2. E06-S06：发布预检、差异、审批、队列进度、失败原因、人工对账与回退入口 UI。
3. 在生产等价 WMS 环境完成真实外部写入、超时、断点恢复、告警和运维演练。
4. E03-S05 仍依赖权威 Match Artifact/CAD 链；正式 CAD Provider、组织有权使用的 DWG/DXF 黄金集和性能证据
   尚未签收。
5. Beta/GA 的跨职能验收、容量、SLO、灾备和发布证据仍需后续切片完成。

本卡是 E06-S04 开发闭环，不是完整 E06、Beta 或 GA 发布签收。下一张可独立推进 E06-S05。
