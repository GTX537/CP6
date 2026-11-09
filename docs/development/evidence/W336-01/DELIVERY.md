# W336-01 真实 V2 创建调整的普通租户事务

[原任务 WP02-02](../../task-plans/WP02.md#wp02-02) · [关联 WP02-10](../../task-plans/WP02.md#wp02-10) · [本窗开工](../../dispatch/20261109-wave336/README.md)

Main 基线 `5ab1d647e78cbae7135cfdabd2870f71c9246df5`。COORD 在固定 DEV-1 单人执行 `codex/w336-01`。显式启用 `StockReadTenancy:EnableV2Adjust` 后，原创建上下文用普通只读事务，原调整用既有普通库存写者。两个入口有各自可信用途，继续沿原 Owner 的 JWT、当前部门版本、库位、字段及原结果合同。既有 `operations/v1` 使用 `EnableV1Adjust` 的普通只读路径披露 V2 原结果。 [运行说明](../../../../CP6.Core/Persistence/Tenancy/README.md#stock-v2-creation-writer)

## 实际验证

Windows、PostgreSQL 18.6、Npgsql 8.0.8、net8.0。真实 Kestrel、JWT Cookie 和 CSRF。用隔离的普通租户 LOGIN、模拟库存、部门和业务用户。

- `red-v2-adjust.trx` 保留 W335 宿主因新配置项退出的实际失败，宿主日志为 `STOCK_READ_TENANCY_CONFIGURATION_REJECTED`。最终 `v2-adjust-http.trx` 通过 4 项。主用例包含 W335 原语、原 HTTP 上下文和写入，另有旧 HTTP 与两种合法无归属回归。
- 撤销普通角色部门列读权限时上下文返回 503。恢复权限后只给出当前子部门。实际新增原部门映射使旧授权版本失效，过期请求返回 412，目标越权部门返回 403。
- 撤销普通角色的创建审计函数执行权后，实际 PostgreSQL 拒绝调用。库存、流水、审计、原操作及通知保持事务前值，原键查询返回 `UNKNOWN`。恢复后同键成功写入 `9007199254740.12345678`，使用实际用户，库存和流水均归属原部门。重放和 GET 返回同一个原结果，只提交一条创建审计、原操作和 Outbox。
- `v1-stock-compat.trx` 在独立夹具通过实际 V1 事务回归 1 项，含 V1 移库、原结果和提交确认中断。两批合计 5 项，零跳过。原始 TRX、PostgreSQL 和宿主日志位于 `D:/CP6-local-test/CP6-72H-20261109/W336-01`，实例均已停止。

集中审查覆盖原权限版本、租户绑定、实际普通读写、失败原子性、旧请求及回执重放。复用 W335 安装和迁移证据。普通 Actions 为手动入口，R2 自动入口匹配版本标签。Actions 触发、取消及生产部署为零。

## 工时和接续

起始估算 18–32 分钟。证据齐备时主机单调实测 529 秒，包含 W335 基线、真正宿主验证及独立 V1 回归。复用已有原入口、配置和定向夹具减少了开工估算中的接线成本。完整窗以正常远端采用和席位释放回读计时。

下一步为 V2 移库上下文和目标创建，实测两端归属、原货权和第二腿失败的整体回滚。原 375 项为 37 DONE、2 IN_PROGRESS、336 PLANNED。原 59 项为 9 闭环、50 余项，125 完整交付为 0、247 正式运行场景为 NOT_RUN。
