# W578-01 · 两种提交顺序下的 Main C02 与 CRM 原导出

2026-11-14。Main 基线 `a1b1b3087874ef4198d1b014f244b89aa8e51574`，受测 `cd6c8c56255fa0a679769b9fb0701c426599c75a`。CRM 受测 `1ca973ea2fb033b957f3544e827ea580e17b4774`，远端已采用 `6ff25d1d5a590d637ea3a0577568e02783a03374`。在独立 PostgreSQL 18.6、Dapr 1.18.2、Kafka 4.3.1 上使用模拟租户、公开询盘和隐私导出。 [CRM #226](https://github.com/GTX537/CP6.CRM/pull/226) · [CRM 正式记录](https://github.com/GTX537/CP6.CRM/blob/6ff25d1d5a590d637ea3a0577568e02783a03374/docs/development/evidence/W578-01/DELIVERY.md)

| 实际提交顺序 | PostgreSQL 读回 |
| --- | --- |
| CRM 先完成导出 | Main 的撤权等待 CRM 事务。原导出先提交，封存后、撤权投影更新后、重授投影更新及轮询后，原回执、制品、审计和效果整体摘要相同。已完成作业的轮询为空。 |
| Main 先提交撤权 | CRM 已获得的旧服务授权在封存后被拒，业务和 Outbox 写集保持原样。撤权投影更新后继续拒绝。重授后原导出完成一次，再次轮询为空。 |

两种顺序均通过 Main 原 dispatcher、Dapr/Kafka 和 CRM 原生 Inbox，独立读回版本 6 与 7，分区 0 offsets 13 与 14，终点 15。两条 C02 Inbox 原字节分别与对应 Main Outbox 和 Kafka 记录相同。Main、CRM 每个顺序各通过一项，共四项，零失败、零跳过。

限制为本地联验。CRM 投影发布仍由既有测试基线驱动。文件用于阶段同步、源字节比对及原导出摘要。原执行和停机证据、完整 broker 读回及程序集哈希位于 `D:/CP6-test-artifacts/W578-01`。测试和证据有变动，产品代码未修改。复用 Main W577 及 CRM 原产品冒烟。Actions 启动、重跑和取消为零。 [Main 联验入口](../../../../CP6.Tests/Platform/Pgt017CrmExportScopeRevocationPostgreSqlTests.cs)

正式 PostgreSQL **26 PASS / 1 NOT_RUN**，原询盘 **60/60**。CRM 正式宿主的投影更新、缺失写集的差量恢复、切换与安全重开，以及 Main 余下写入边界待验。 [正式矩阵](../../acceptance/postgresql-tenancy.json#L646) · [PGT-017 #1012](https://github.com/GTX537/CP6/issues/1012)
