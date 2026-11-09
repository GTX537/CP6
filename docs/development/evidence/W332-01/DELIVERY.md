# W332-01 库存原命令的可信只读恢复

[原任务 WP02-02](../../task-plans/WP02.md#wp02-02) · [关联 WP02-10](../../task-plans/WP02.md#wp02-10) · [本窗开工](../../dispatch/20261109-wave332/README.md)

Main 基线 `2c437de327b501a3165a4f6d5a7c36ce3f1deaff`。COORD 在固定 DEV-1 单人执行 `codex/w332-01`。启用普通库存调整的宿主，原 `GET /api/wms/stock/operations/v1/{operation}` 由已获准 LOGIN 在只读事务中查原 journal。原 Owner 继续持有共享认证和业务锁，核对原操作者与现有库存对象范围。主库身份确认纳入 journal 行隔离状态。已有 `UNKNOWN`、`CLOSED`、`COMMITTED` 和错误合同随原入口。 [运行说明](../../../../CP6.Core/Persistence/Tenancy/README.md#stock-v1-adjustment-owner)

## 实际验证

Windows、PostgreSQL 18.6、Npgsql 8.0.8、net8.0。实际 Kestrel、JWT Cookie、CSRF 及 PostgreSQL 事务，业务采用模拟租户、用户、库位和库存。

- 使用 loopback TCP 转发真实 HTTP 调整，在上游已经发送成功状态后关闭调用方连接。调用方收到传输异常。独立连接证实数据库已提交；原 GET 查回真实流水，现有 POST 重放返回相同的原结果。缺失键返回 `UNKNOWN`。读回期间原认证根版本一致，库存调整、原流水和 Outbox 引用各只发生一次。
- `red-readback.trx` 证实撤销普通 journal 结果列的读权限后，旧路径仍可披露原结果。`readback-http.trx` 对相同场景返回 503，关闭 journal 行隔离也返回 503。提交后的库位权限撤回返回 `OBJECT_FORBIDDEN`，响应不披露原流水号。
- 原库存读面与原未启用宿主的 HTTP 恢复/原子 Outbox 两项回归通过。主用例 1 项，兼容用例 2 项，零跳过。两组安装场景使用各自的自有 PostgreSQL 根目录。`readback-http.trx`、`readback-compat.trx`、运行和停止证据见 `D:/CP6-local-test/CP6-72H-20261109/W332-01`。

集中审查覆盖原只读锁、普通连接身份、权限错误及状态区分、原对象约束和接口兼容。本窗复用已有安装和数据库迁移。普通 Actions 为手动入口，R2 自动入口匹配版本标签。Actions 触发、取消及生产部署为零。

## 工时和接续

起始估算 20–40 分钟。证据齐备时主机单调实测 555 秒。恢复沿现成只读事务、原 journal 和实际宿主夹具完成，TCP 故障注入直接用 loopback。完整窗以正常远端采用和固定席位释放回读计时。

下一窗接显式关闭和数据库提交确认中断，区分本窗已经执行的 HTTP 回包截断。原 375 项为 37 DONE、2 IN_PROGRESS、336 PLANNED，原 59 项为 9 闭环、50 余项，125 完整交付为 0、247 正式运行场景为 NOT_RUN。
