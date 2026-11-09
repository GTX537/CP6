# W344-01 Stock 实际取消、坏连接和原键恢复

[原任务 WP02-02](../../task-plans/WP02.md#wp02-02) · [关联 WP02-10](../../task-plans/WP02.md#wp02-10) · [本窗开工](../../dispatch/20261109-wave344/README.md) · [原 PGT-010](../../acceptance/postgresql-tenancy.json)

Main 基线 `b10b819dcab7ed11ea195145ac14befba8404528`。COORD 在 DEV-1 执行 `codex/w344-01`。沿已有普通 Stock 写者、真实 Kestrel、两模拟租户及独占 PostgreSQL 18.6。运行项目输入与 W342 一致，复用其本地构建并专编测试项目。

## 实际验证

- 两次都让 A 的真实普通调整进入 `StockOutboxEnqueueV1` 的实际路由锁等待。此时库存写入和原操作已在调用方事务内完成。控制连接持同一原路由键，`pg_stat_activity`、`pg_blocking_pids` 和实际 SQL 确认阻塞。事务外读取仍见原库存、流水、原操作和消息计数。
- 首次运行在等待期间要求 B 的 HTTP 原结果提前返回，B 的限时请求实际取消。源码复核发现原查询需要共享授权根的 `FOR SHARE`。复测两次均以实际数据库阻塞关系确认 B 正在等该授权根。A 退出事务后，B 同一请求准确得到自己的原结果，并继续用普通后端 7872。
- HTTP 取消到达数据库。原生日志记录取消语句，A 60784 结束事务，之后复用 60784。另一次用隔离库控制连接终止同一准确后端，原生日志记录管理员终止，原 HTTP 返回 503 `OPERATIONS_OUTCOME_UNKNOWN`，后端退出，A 后续换为 55220。
- 两次故障后库存、流水、原操作和 Outbox 均保持前值。A 先查询同键得到 `UNKNOWN`，再提交同输入，分别只累计 `0.12345671` 和 `0.12345673`，各自产生一条流水、原操作与通知。原键随后读取为 `COMMITTED`，B 同键均为 `UNKNOWN` 且业务计数不变。
- `connection-failures-http2.trx` 实际通过 1 项、零跳过，包含现有 Stock V2、A/B、错绑定和轮换回归。TRX 在 06:08:32 UTC 完成。首跑失败、复测 TRX、PostgreSQL 日志和 `stock-connection-failures.json` 留在 `D:/CP6-local-test/CP6-72H-20261109/W344-01`，本窗进程均已停止。 [实际请求和数据库故障](../../../../CP6.Tests/Erp/StockConnectionFailurePostgreSqlTests.cs)

集中审查覆盖现有事务顺序、精确故障目标、原结果查询先于同键提交以及共享授权根对其他租户的影响。本窗覆盖 PGT-010 的真实请求取消、事务清理和失效连接替换。真实并行读吞吐、其他超时与恢复组合按原范围继续核对。普通 Actions 为手动入口，R2 自动入口匹配版本标签。本窗 Actions 触发和取消均为零。

## 工时和接续

开工估算 13–24 分钟。测试结束距本机开工约 722 秒，检查原始证据时实测 761 秒。完整工时按远端采用及释放回读计时。DEV-2 同时执行 WP02-28 独立实际制品，两窗重叠区间在三天目标内计一次。

接续收尾 WP02-28 两制品。发现的全新 PostgreSQL 初始化阻断按 BUG 规则独立登记和优先处理。库存首装回执恢复的原范围已准备。原 375 项为 37 DONE、2 IN_PROGRESS、336 PLANNED，原 59 项为 9 闭环、50 余项，125 完整交付为 0、247 正式运行场景为 NOT_RUN。
