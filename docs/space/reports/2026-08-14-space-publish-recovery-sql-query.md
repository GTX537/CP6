# 发布恢复指标 SQL Server 查询修复

日期：2026-08-14

范围：Space Studio V1 核心 GA / WP6 仓库实现

结论：发布恢复指标已在真实 SQL Server LocalDB 上恢复可执行；生产等价 SQL、CP6 WMS、告警通知与恢复时限接受仍为 Pending，核心 GA 保持 72% / No-Go。

## 发现方式

在远端 `main` 提交 `d4ff86fe` 上设置 `CP6_TEST_SQLSERVER` 指向 `MSSQLLocalDB`，执行完整 `CP6.Space.IntegrationTests`。原先被环境 skip 隐藏的 `Wms_timeout_keeps_production_and_automatic_retry_completes` 在读取 WaitingRetry 指标时失败；SQL Server EF Provider 无法翻译按 TenantId + AttemptId GroupJoin 后、再引用外层 AttemptStatus 的聚合表达式。

首次全量结果为 424 passed / 2 failed / 0 skipped。另一项失败属于 Published Viewer 真库数据准备，按独立任务处理。

## 修复

- 删除不可翻译的复合键 GroupJoin。
- 对每个受跟踪 Publish Attempt 使用显式 `TenantId ==`、`AttemptId ==`、`AttemptStatus ==` 的相关子查询，取最新不可变 Audit `OccurredAtUtc`。
- 保持 `IgnoreQueryFilters`、无 Tenant/Site/Version/Attempt 指标标签、历史记录回退到 `StartedAtUtc` 和未来时间钳制为零的既有语义。

## 验证

- `SpacePublishRecoveryMetricsSnapshotProviderTests`：6/6 passed。
- `SpacePublishOrchestratorSqlServerTests`：3/3 passed，0 skipped。
- 关键失败场景单独回归：1/1 passed，0 skipped。

真库覆盖 WMS 首次超时、WaitingRetry 指标、旧 Published 指针保持、同一正式 Job 重试及最终 Published 切换。测试数据库使用随机名称并在场景结束后删除。

## 接受边界

LocalDB 使用真实 SQL Server 引擎，足以证明查询可翻译和事务场景可执行，但不是生产等价拓扑，也没有真实 CP6 WMS、Prometheus 告警通知或人工对账。故本报告是仓库真库自动化证据，不将 WP6 acceptanceStatus 改为 Accepted。
