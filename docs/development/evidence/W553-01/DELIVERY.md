# W553-01 · 定时 ABC 快照与租户暂停

2026-11-13 UTC。Main 基线 `366d32ae8667309e0fe409637f5d93e15420b8e0`，受测源码 `1de58fd27de89ac38e767c50506c75c6ef6ab600`。使用隔离 PostgreSQL 18.6、模拟租户和站点、真实 Main 登录及租户管理 HTTP、实际 `SpaceAbcSnapshotWorker` 和 WMS 聚合。测试替换租户枚举以保留暂停前的清单，独立连接持锁和观察。 [Issue #1034](https://github.com/GTX537/CP6/issues/1034) · [PGT-017](../../acceptance/postgresql-tenancy.json#L646)

旧版本的两项回归均失败。租户暂停持锁等待租户行时，定时任务提前完成并写了一份快照。任务的快照 INSERT 被拦住时，租户暂停提前返回 200，释放后仍新增快照。根因是定时入口只从事务外枚举租户。 [定时入口](../../../../CP6.WebApi/BackgroundServices/SpaceAbcSnapshotWorker.cs)

修复后，每站点在 PostgreSQL Read Committed 事务先取得 Main 授权根，再读取租户当前启用状态、到期时间、分析配置、站点和当日快照。持锁完成写入，提交前复核到期时间。站点及租户本地日期唯一约束保持适用。空间人工重建继续走既有 HTTP 事务入口。 [写入实现](../../../../CP6.Core/Services/Space/SpaceAnalyticsService.cs) · [并发测试](../../../../CP6.Tests/Erp/Pgt017SpaceScheduledPostgreSqlTests.cs)

| 顺序 | PostgreSQL 等待与提交结果 |
| --- | --- |
| 业务先持根 | worker 的 INSERT 等测试表锁，暂停连接通过 `pg_blocking_pids` 被该 worker 的根事务阻塞。快照提交后暂停完成。重复处理旧清单保留原快照 ID 和一份记录。 |
| 暂停先持根 | 暂停的 UPDATE 等测试租户行锁，worker 在共同根等待暂停。暂停完成后无新增快照，重复处理旧清单仍为零。 |

两种顺序均确认释放锁前无快照和未提交的暂停效果，最终复核租户禁用、认证政策、管理版本、身份代次及快照版本、优先 Outbox、审计、回执。集中审查覆盖实际租户 scope、事务根与时钟、重复调度、回滚后的实体跟踪和既有人工入口。

两项新 PostgreSQL 回归、14 项相关 PostgreSQL 栅栏、34 项相关组件检查均通过，零跳过。本地发布 310 文件，`CP6.WebApi.dll` SHA-256 `056c54207ed60b31bb2c6c338f64097b28be68ea597386e4e1b71c3679b8769b`，与受测程序集一致。发布宿主建库退出 0，存活 200，匿名租户管理与空间配置 401。TRX、文件清单、源码绑定与停止记录在 `D:/CP6-test-artifacts/W553-01`。Actions 启动、重跑及取消为零。隔离实例已停止。

正式 PostgreSQL **26 PASS / 1 NOT_RUN**，原询盘 **60/60**。仍需核对独立 Space Design、旧 OA、财务、采购、MRP、普通数据库角色、Main 原生控制及 Main/CRM 恢复后的存储身份和权限水位。正式 PGT-017 按完整写者和提交证据核销。 [W531 原余项](../W531-01/DELIVERY.md#仍待核销的正式前置) · [跨库余项 #1012](https://github.com/GTX537/CP6/issues/1012)

远端采用、分支和固定席位处置见本次 PR 及本地 `main-finish.json`。
