# E13-S12 并发、预算、用量与费用完成报告

- 状态：**Integrated**
- 日期：2026-07-30
- 功能分支：`codex/space-e13-s12-budget-concurrency`
- 功能提交：`54456946`
- no-ff 集成提交：`b33929fb`
- 集成分支：`integration/space-v1-20260730`

## 1. 交付结论

E13-S12 已按冻结边界交付并进入唯一 Space 集成基线。新增租户 AI 工作槽和
预算预留账本，使用 SQL Server 行锁/范围锁保护单租户最多三并发、日/月
预算、Provider 请求幂等、用量和实际费用审计；不使用单机 `Semaphore`。

应用层新增 `ISpaceAiCapacityLedger` 和
`SpaceAiCapacityCoordinator`。Coordinator 在工作槽或预算不足时稳定返回
429 / `SPACE_AI_QUOTA_EXCEEDED`。租户 AI 策略现在可固定并发上限、日预算、
月预算和币种；Disabled 策略继续保持无定价、无预算的失败关闭默认值。

## 2. 并发槽

`Space_TenantAiWorkSlot` 使用 `(TenantId, SlotNo)` 复合主键，SlotNo 固定为
1～3，并保存 Run、LeaseOwner、LeaseExpiresAtUtc 和 RowVersion。

- 首次使用在短事务内以 `UPDLOCK, HOLDLOCK` 幂等建立三个槽位。
- 认领前锁定并统计租户全部未过期活动槽，再用
  `UPDLOCK, READPAST, ROWLOCK` 获取最低可用槽。
- 即使租户策略从三并发收紧为一并发，仍按全部活动槽计数，不会因为 1 号
  槽空闲而忽略仍占用的 2、3 号槽。
- `(TenantId, RunId)` 过滤唯一索引保证一个 Run 最多持有一个槽。
- 默认租约 60 秒；续租和释放同时围栏 Tenant、Run、LeaseOwner 和
  RowVersion。
- Worker 异常后过期槽可被其他 Run 回收；过期 Worker 不能再续租。

## 3. 预算、用量和费用

`Space_AiBudgetReservation` 保存：

- RunId 和 SHA-256 `ProviderRequestKey`；
- PeriodDay / PeriodMonth；
- ReservedCostMinor / ActualCostMinor；
- Currency；
- Reserved / Submitted / Reported / Released / Reconciled 状态；
- ExpiresAtUtc 和 RowVersion。

预算预留使用 `UPDLOCK, HOLDLOCK` 锁定请求键和租户预算窗口：

- 日预算与月预算均按同一币种原子检查；
- 同一 ProviderRequestKey 只能以完全相同的 Run、期间、估算金额和币种
  重放；
- 未发送的 Reserved 默认 15 分钟后可释放；
- Submitted 表示请求已发送但结果未知，不能因超时释放，必须等待报告或
  对账；
- Reported 的实际费用可高于估算值，但必须如实入账；
- Reconciled 与 `Space_AiUsageRecord` 在同一事务提交；
- Usage 的既有 `(TenantId, ProviderRequestIdHash)` 唯一索引和预算请求键
  唯一索引共同阻止重复计费。

没有 Provider 定价时使用无预算/无币种策略，不虚构货币金额；有非零估算
或实际费用时必须提供合法 ISO 4217 三字母币种。

## 4. 数据库变更

Migration：

`20260730183757_SpaceE13S12AiCapacityLedger`

新增表：

- `Space_TenantAiWorkSlot`
- `Space_AiBudgetReservation`

关键约束和索引：

- `PK_Space_TenantAiWorkSlot`
- `UX_TenantAiWorkSlot_Tenant_Run`
- `UX_AiBudgetReservation_Tenant_Request`
- `IX_AiBudgetReservation_Tenant_Day`
- `IX_AiBudgetReservation_Tenant_Month`
- 两表到 `Space_GenerationRun` 的 Tenant 复合外键
- 槽号、租约原子空值、预算金额、期间和币种 Check Constraint

已生成幂等 SQL 脚本：

`CP6.Space.Infrastructure/Migrations/Scripts/20260730183757_SpaceE13S12AiCapacityLedger.sql`

`dotnet ef migrations has-pending-model-changes` 返回：

```text
No changes have been made to the model since the last migration.
```

## 5. 验证证据

| 检查 | 结果 |
|---|---|
| Space 受影响项目 Release 编译 | 0 errors；Infrastructure 首轮 3 existing warnings |
| `dotnet build CP6.slnx -c Release --no-restore` | 最终重跑 0 errors，10 existing warnings；包含 Android D8 打包 |
| Space UnitTests | 136 passed；其中 E13-S12 新增 10 |
| Space IntegrationTests（默认门禁） | 46 passed，36 SQL-gated skipped |
| E13-S12 SQL Server 聚焦测试 | 4 passed，0 skipped |
| Space SQL 全量启用 | 82 项中 77 首轮通过；5 项在并行建库/Migration/删库压力下超时，逐项串行复跑全部通过 |
| EF Migration 一致性 | 通过，无待迁移模型变更 |
| CP6.Tests | 2680 passed，17 environment-gated skipped |
| CP6.Client.Tests | 71 passed |
| 精确 C# whitespace/style 与 staged diff | 通过 |

E13-S12 SQL 测试真实连接本机 `KOUSQLSERVER`，使用 Windows 集成认证和每
测试唯一临时数据库，证明：

- 四个并发 Run 中三个获得不同槽，第四个稳定收到 429；
- 策略由三并发收紧为一并发后不会超配；
- 两笔 60 单位费用在 100 单位预算中并发竞争时只有一笔成功；
- ProviderRequestKey 重放只产生一条 Usage；
- 未发送预留过期释放后额度可复用；
- 跨日后日预算重置，但同月实际费用继续占用月预算。

全量 SQL 首轮的 5 个失败分别发生在本卡槽初始化、WMS Migration、文件
保留建库、RowVersion 删库和 Job Processor 删库；逐项串行复跑全部通过。
因此不声称 82 项首轮一次性全绿。

整解 Release 首轮的唯一错误来自 Android D8：
`java.io.IOException: There is not enough space on the disk`。失败发生在
移动端打包阶段，Space 受影响项目和测试产物此前均已编译成功。随后只清理
本轮已完成 S02/S03 工作树中可重建的 Android Release `bin/obj`，D 盘空闲
从约 0.06GB 恢复到约 0.61GB；同一完整构建最终重跑通过，结果为
0 errors、10 existing warnings。

## 6. 明确未实现

- `Space_AiProviderConfig` 的持久化管理和 HTTP API；
- Provider 定价目录、汇率换算和财务发票对账；
- BuildScene 实际步骤执行器和外部 Provider 调用；
- AI usage 查询 API、管理工作台和告警；
- 跨租户 Worker 宿主循环。

S01 的低层 `SpaceAiGenerationGateway` 仍保留失败关闭配额管理器，因为该
旧入口没有稳定 RunId、LeaseOwner 和预算请求上下文；本卡没有用匿名或
随机 RunId 绕过审计。后续 BuildScene 执行器必须使用本卡的
`SpaceAiCapacityCoordinator`。

## 7. 后续依赖

- E13-S04 等待 E02-S03。
- E13-S05 等待 S04 和正式 Provider 证据。
- E13-S13 同时依赖 S04、S12，因此仍被 S04 阻塞。
- E13-S11、S18 分别等待 Apply/恢复链和试点链。

因此 S12 完成后，当前 E13 依赖图没有新的可独立启动卡；下一步需要外部
证据或前置 Epic 解锁，不能通过提前实现后续能力规避门禁。
