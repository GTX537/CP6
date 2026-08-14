# Space 发布恢复与对账运行手册

状态：仓库运行基线；生产等价演练待完成

适用范围：Design V1 `Publish Preview → Publish Attempt → CP6 WMS → Published Runtime`。本手册不授权直接修改 WMS、`Space_PublishAttempt`、`Space_PublishBatch`、Published 指针或审计表。

## 1. 恢复目标与不可破坏条件

| 分类 | 状态 | 目标 | 不可破坏条件 |
|---|---|---:|---|
| 自动恢复 | `WaitingRetry` | ≤15 分钟 | 旧 Published 持续服务；复用同一 PublishPlan、Attempt 和批次幂等身份 |
| 人工恢复 | `ManualIntervention` | ≤4 小时 | 只通过正式 Retry；不得重复裸 WMS 写入 |
| 人工对账 | `ReconciliationRequired` | ≤4 小时 | 先核对 WMS 回执和读回结果，再通过 Reconcile；不得猜测外部结果 |

任何恢复期间：

- 当前 Published 指针不得提前切换。
- 同一 PublishPlan 重试不得产生重复库位、事件或外部写入。
- Blocking 继续禁止发布；Warning 必须在当前 Publish Preview 中重新确认绑定哈希。
- 审计事件、批次、回执和对账问题只追加，不更新或删除。

## 2. 监控信号

`/metrics` 暴露固定低基数指标，不包含 Tenant、Site、Version 或 Attempt 标签：

| 指标 | 含义 |
|---|---|
| `cp6_space_publish_recovery_attempts{state}` | 当前恢复状态中的 Attempt 数量 |
| `cp6_space_publish_recovery_oldest_age_seconds{state}` | 该状态最老等待时长 |
| `cp6_space_publish_recovery_slo_breaches{state}` | 超过该状态恢复目标的 Attempt 数量 |
| `cp6_space_publish_recovery_target_seconds{state}` | 固定目标：`waiting_retry=900`，其余两类 `14400` |

状态标签只允许：`waiting_retry`、`manual_intervention`、`reconciliation_required`。

Prometheus 规则位于 [`deploy/monitoring/prometheus/space-publish-alerts.yml`](../../../deploy/monitoring/prometheus/space-publish-alerts.yml)，包含：

- `CP6SpacePublishAutomaticRecoverySloBreach`：自动恢复超过 15 分钟，Critical。
- `CP6SpacePublishManualRecoverySloBreach`：人工恢复/对账超过 4 小时，Critical。
- `CP6SpacePublishRecoveryMetricsAbsent`：指标连续缺失 10 分钟，Warning。

部署环境必须把规则加载到当前 Prometheus/Alertmanager 等价链并验证实际通知路由；仓库文件存在不等于生产告警已生效。

## 3. 首次响应

1. 记录告警开始时间、环境、指标样本和当前 UTC 时间。
2. 确认 API `/health/live`、`/health/ready`、`/health/release` 与 `/metrics` 可用；指标缺失时先恢复观测链，不能据此假定发布正常。
3. 在 Space 发布控制面按 Site 和状态定位 Attempt，记录：Attempt ID、Correlation ID、PublishPlan Hash、Target/Base Version、当前步骤、Job 状态、尝试次数、LastErrorCode、开放对账问题数。
4. 确认当前线上 Published Version 仍是故障前版本。若指针已提前变化，立即按 S1/S2 事件升级，不继续普通重试。
5. 通过审计时间线核对最近的 `RetryableFailureObserved`、`RetryScheduled`、`ManualInterventionRequired` 或 `ReconciliationRequired`，不得依赖原始异常正文。

## 4. `WaitingRetry` 自动恢复

1. 核对 WMS 健康、Adapter ID/Capability Hash 和 Job 的 `NextAttemptAtUtc`。
2. 目标时间内由持久 Job Ledger 自动重试；不要并行点击人工重试。
3. 若超过 15 分钟：
   - 确认没有仍在运行或持有有效租约的 Publish/Reconcile Job。
   - 核对已确认批次和回执；任何 `Partial`、`Uncertain` 或读回不一致必须进入对账，不能当作零效果重试。
   - 只有状态允许且没有未解决对账问题时，使用发布控制面的“人工重试”，填写故障恢复依据。
4. 恢复后确认 Attempt 为 `Completed`、目标版本成为 Published、旧版本成为 Superseded，且重复库位/事件/外部写入计数为零。

## 5. `ManualIntervention` 人工恢复

1. 查明 Job 达到最大尝试次数、输入/资源故障或运维限制的稳定错误码。
2. 修复外部依赖或配置；不要修改冻结的 RequestJson、PlanJson 或批次请求。
3. 在控制面提交带原因的正式 Retry，保持原 Attempt/PublishPlan 和幂等链。
4. 从 Retry 请求时间开始记录人工恢复耗时；4 小时内必须完成或升级事件。

## 6. `ReconciliationRequired` 人工对账

1. 按批次核对冻结请求、WMS Receipt、外部幂等键、WMS 实际库位状态和 CP6 Runtime 状态。
2. 将每个开放问题标记为 Investigating，并保存脱敏证据引用；不得把 WMS 响应体、Token 或 Secret 写入备注。
3. 判断结果：
   - WMS 已完整应用：通过 Reconcile 读回确认后继续 Runtime 激活。
   - WMS 明确未应用：由正式 Reconcile/Retry 复用同一幂等身份恢复。
   - WMS 部分或未知：保持旧 Published，继续人工核查，禁止盲重放。
4. 全部问题有解决记录后才提交 Reconcile Job；4 小时内未关闭即升级 Critical 事件。

## 7. 历史版本重发

- 历史重发创建新版本和新发布 Attempt，不修改历史 Published 证据。
- 新 ValidationRun 有 Blocking 时停止；有 Warning 时停在生成的 Ready 版本，由操作者打开 Publish Preview 并确认当前 Warning 哈希。
- 历史重发失败期间当前 Published 持续服务。

## 8. 关闭与证据

每次演练或事故至少保存：

- 告警触发/确认/恢复 UTC 时间和恢复分类。
- `/health/release` 的版本、Git SHA、镜像 digest 与最新迁移。
- 告警前后四个恢复指标的原始样本或受控截图。
- Site、Attempt ID、Correlation ID、PublishPlan Hash、Target/Base/Published Version。
- WMS 健康、批次、回执、读回与重复写入核验结果。
- 自动恢复耗时或人工恢复/对账耗时。
- 旧 Published 连续服务证据、最终状态和遗留问题。

GA 证据必须来自真实 SQL Server、CP6 WMS 和生产等价告警路由。Mock、未加载的规则文件或 skipped 测试不能关闭恢复门禁。
