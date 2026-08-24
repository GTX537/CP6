# Space Studio WP6 发布恢复可观测性报告

日期：2026-08-14

任务分支：`codex/space-publish-observability`

范围：仓库侧发布恢复指标、告警规则、运行手册与自动化；不包含生产告警部署或真实 WMS 恢复签字。

## 1. 交付结果

- `/metrics` 新增四组固定低基数 Gauge，按 `waiting_retry`、`manual_intervention`、`reconciliation_required` 汇总活动 Publish Attempt。
- 指标覆盖活动数量、最老等待时长、超过冻结 SLO 的数量和固定目标秒数；不输出 Tenant、Site、Version、Attempt 或其他业务标识。
- 状态进入时间优先取不可变 Publish Audit 事件，旧记录回退到 Attempt `StartedAtUtc`；未来时间被归零，避免产生负等待时长。
- Prometheus 规则覆盖自动恢复超过 15 分钟、人工恢复/对账超过 4 小时及指标连续缺失 10 分钟。
- 运行手册冻结旧 Published 持续服务、同一 PublishPlan 幂等、Retry/Reconcile 正式入口、历史重发 Warning 重新认领和证据清单。
- 既有真实 SQL WMS 超时场景增加恢复指标断言：失败进入 `WaitingRetry` 时计数为 1，完成后回到 0。

## 2. 自动化证据

- 聚焦指标/告警合同测试：6/6 通过。
- `CP6.Tests`：2,883 passed / 19 environment-gated skipped。
- `CP6.Space.UnitTests`：506/506 通过。
- `CP6.Client.Tests`：71/71 通过。
- `CP6.Space.IntegrationTests`：305 passed / 104 SQL/environment-gated skipped。
- 完整 Release solution：0 warning / 0 error。
- 真实 SQL WMS 超时指标用例：本机未配置 `CP6_TEST_SQLSERVER`，因此 skipped；不计为真实环境通过。

## 3. GA 尚未关闭的门禁

- 在生产等价 Prometheus/Alertmanager（或等价平台）加载规则并验证真实通知路由、确认和恢复通知。
- 在真实 SQL Server 与 CP6 WMS 执行成功发布、超时自动恢复、部分/不确定写入对账、同 PublishPlan 重试无重复及旧 Published 连续服务演练。
- 保存自动恢复不超过 15 分钟、人工恢复/对账不超过 4 小时的计时证据。
- 完成备份恢复、安全矩阵、双仓 Pilot 和五方 GA 签字。

结论：本卡完成 WP6 的仓库侧可观测性基础，不代表 WP6 或 Space Studio 核心 GA 完成。
