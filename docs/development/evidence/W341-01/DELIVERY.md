# W341-01 实际 A/B 同业务键和原操作键

[原任务 WP02-02](../../task-plans/WP02.md#wp02-02) · [关联 WP02-10](../../task-plans/WP02.md#wp02-10) · [本窗开工](../../dispatch/20261109-wave341/README.md) · [库存对象表](../W340-01/DELIVERY.md)

Main 基线 `b1ed3502b37a0c5c892973871f2c7b16430c8c9c`。COORD 在固定 DEV-1 单人执行 `codex/w341-01`。复用已经采用的 Stock 宿主及普通写者，在一个隔离 PostgreSQL 18.6 库安装两个真实普通 LOGIN。实际 Kestrel、JWT Cookie 和 CSRF 分别使用模拟租户 A、B 的当前用户。两租户使用相同 `W01/L1/P1/LOT` 和相同原操作 GUID。

## 实际验证

- A 通过原 V1 HTTP 调整提交 `0.33333333`。B 同键库存保持事务前值，B 对同一个原操作 GUID 查询返回 `UNKNOWN`。B 随后提交 `0.55555555`，库存 ID、数量和数据库流水各落本租户。
- 两个租户的原操作分别绑定自己的实际 actor，原键 GET 及相同输入重放分别返回各自原结果。重放后库存只累计各自一次增量。各有一个与本租户源操作、消息、租约绑定、路由和启用 Authority 一致的 Outbox 引用，`AssignedEpoch` 与当前路由一致。消息身份分别为各自的 `stock-hint:<tenant>:<operation>`。
- `pg_stat_activity` 在初始读取及三轮顺序 A/B/A 后显示同一对普通连接。模拟 `stock_creation_a` 为 PID 52328，`stock_creation_b` 为 PID 42340。各次读取只返回当前租户的原库存 ID 和准确数量。
- `tenant-pair-http.trx` 实际通过 1 项、零跳过，含现有 V2 创建、调整、移库与新增的 A/B 验收。目录补读核实 Outbox 租约、Authority 和 Route 的强制行安全、主键、唯一键及只允许锁定的 UPDATE 策略，已补入库存对象表。原始 TRX、物理连接观测和目录输出位于 `D:/CP6-local-test/CP6-72H-20261109/W341-01`，实例均已停止。 [真实 A/B 用例](../../../../CP6.Tests/Erp/StockTenantPairPostgreSqlTests.cs)

集中审查覆盖实际用户和 PostgreSQL LOGIN 区分、同键两租户、原键重放、物理池复用、全表消息身份和当前租约。采用范围为现有 Stock 的三轮顺序 A/B/A。同一原 PGT 还包含并发、更多轮次及选定布局的完整场景，接续按原条件核定。普通 Actions 为手动入口，R2 自动入口匹配版本标签。Actions 触发、取消及生产部署为零。 [原 PGT 场景](../../acceptance/postgresql-tenancy.json)

## 工时和接续

起始估算 20–34 分钟，证据齐备时主机单调实测 362 秒。复用现有写者、原操作、真实宿主及隔离夹具，增加了第二租户和真实连接观测。完整窗按正常远端采用及席位释放回读计时。

下一步在真实库存写入口验证错 LOGIN 绑定，补普通角色原生越权及零业务效果。继续凭据轮换和版本验证。原 375 项为 37 DONE、2 IN_PROGRESS、336 PLANNED，原 59 项为 9 闭环、50 余项，125 完整交付为 0、247 正式运行场景为 NOT_RUN。
