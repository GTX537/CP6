# W353 全 Core 原 RMA 权威接入

2026-11-09 07:58:01 UTC，RMA-COMMISSION 独占 DEV-1，codex/w353-01，最新主线 4631874fdcfbf9fac6db5dc7c1e5397dd75f0542。原 WP02-02，W350 真实业务表、正常远端采用和正式释放已核。原估 25–45 分钟，使用隔离 PostgreSQL 55562。 [W350 交付](../../evidence/W350-01/DELIVERY.md)

在全新隔离 Core 已初始化的物理库，锁定原共享 RMA 两表和准确 RMA Owner journal，核现存范围为空。原子禁用旧 RMA 写入，包括只写原操作的关闭路径。保留其他 Owner 权威、历史迁移与旧环境默认。继续复用 W350 租户 DDL、历史与原回执。原正常 HTTP、当前共享授权和普通业务写入另窗。

#808 在 DEV-2 独占 Stock 派送和 WebApi 注入，INTEGRATION-1 独立验证受控旧消息的当前原接收回执。COORD 回收共享状态及保护合并。 [Issue #808](https://github.com/GTX537/CP6/issues/808)

W353 的真实 Core 三次初始化、旧事务等待、第二租户故障整次回滚和同键恢复均通过。W350 原字节兼容通过。 [交付报告](../../evidence/W353-01/DELIVERY.md)

#808 随 [#811](https://github.com/GTX537/CP6/pull/811) 合入，Issue CLOSED。准确分支删除，DEV-2 干净并正式释放。

2026-11-09 08:11:56 UTC，W355-01 已由 STOCK-CAPACITY 领取 DEV-2，codex/w355-01，基线 310b99df8c41ca33e846a706c1025bab63f85404。估 24–38 分钟。使用独立 PostgreSQL 55564，测真实 Stock 普通 LOGIN 10/100 冷暖、模型、池峰值及边界。原 WP02-02 的业务写入口和总台账仍由对应窗口及 COORD 独占。
