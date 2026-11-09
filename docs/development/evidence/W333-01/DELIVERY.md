# W333-01 库存原操作关闭与数据库提交确认中断

[原任务 WP02-02](../../task-plans/WP02.md#wp02-02) · [关联 WP02-10](../../task-plans/WP02.md#wp02-10) · [本窗开工](../../dispatch/20261109-wave333/README.md)

Main 基线 `ddc73e51fa4b2ade79d68734b6c13b72efd58ed2`。COORD 在固定 DEV-1 单人执行 `codex/w333-01`。启用普通库存调整时，原 `POST /api/wms/stock/operations/v1/{operation}/close` 使用独立可信入口目的、原查询授权和现有普通写入事务。原认证根、图锁和 journal 约束在同一事务裁决已有结果或空键关闭。普通写入在同主库身份核对中检查 journal 行隔离。 [运行说明](../../../../CP6.Core/Persistence/Tenancy/README.md#stock-v1-adjustment-owner)

## 实际验证

Windows、PostgreSQL 18.6、Npgsql 8.0.8、net8.0。测试使用本地 Kestrel、JWT Cookie、CSRF 和模拟租户、部门、库位及库存。使用独立 loopback PostgreSQL 协议转发，仅对目标普通登录截断一次已到达的 `COMMIT` 完成消息。 [PostgreSQL 协议消息](https://www.postgresql.org/docs/18/protocol-message-formats.html)

- 实际 V1 调整返回 `OPERATIONS_OUTCOME_UNKNOWN`。独立 PostgreSQL 连接和原 GET 证实同键已提交，存在唯一流水和 Outbox 引用。随后显式关闭该键仍返回 `COMMITTED`。本窗注入发生在数据库确认链路。W332 的实际 HTTP 回包截断证据仍随该窗。 [W332 验证](../W332-01/DELIVERY.md)
- `red-close.trx` 证实撤销普通角色的图锁执行权后，旧关闭入口仍返回 `CLOSED`。`close-commit-http.trx` 对相同场景返回 503，原键读回 `UNKNOWN`。实际 `pg_blocking_pids` 确认关闭取得原根后等待库存图锁，迟到调整再等关闭。释放图锁后关闭返回 `CLOSED`，迟到调整返回 `ORIGINAL_OPERATION_CLOSED`，该键没有库存流水或通知引用。
- 真实宿主主用例 1 项，旧 V2 原键关闭与库存库位原 HTTP 恢复回归 2 项，零跳过。原始 TRX、运行和停止证据见 `D:/CP6-local-test/CP6-72H-20261109/W333-01`。核对本窗自有 PostgreSQL、宿主和测试进程均已停止。

集中审查覆盖原命令状态、当前查询权限、固定图锁和 journal RLS、未知提交语义、无附加业务效果及旧入口兼容。本窗复用现有安装和迁移。普通 Actions 为手动入口，R2 自动入口匹配版本标签。Actions 触发、取消及生产部署为零。

## 工时和接续

起始估算 35–70 分钟。证据齐备时主机单调实测 581 秒。直接复用已交付事务和恢复，协议转发首次捕获真实确认中断，成本主要为两次本地宿主验证和定向回归。下一窗按真实 V1 两腿和第二腿故障估 18–35 分钟。完整窗以正常远端采用和固定席位释放回读计时。

原 375 项为 37 DONE、2 IN_PROGRESS、336 PLANNED，原 59 项为 9 闭环、50 余项，125 完整交付为 0、247 正式运行场景为 NOT_RUN。V1 移库、V2 创建、角色/连接与逐租户版本生命周期按各自出口交付。
