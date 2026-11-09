# W350 原 RMA 租户业务 schema 首版

2026-11-09，2026-11-09T07:25:50.263747+00:00，RMA-MIGRATION 独占 CP6 DEV-1，分支 codex/w350-01，最新主线 22bf06da325c099c6d2b894c7f9e7c748f85d622。原 WP02-02。W346 正常采用及 W348 核验收尾已核，W349 独占 DEV-2 的本地工具和证据。 [原 WP02-02](../../task-plans/WP02.md#wp02-02) · [W346](../../evidence/W346-01/DELIVERY.md) · [上一登记](../20261109-wave346/README.md)

本窗估 30–55 分钟，使用新隔离 PostgreSQL 18.6，端口 55558。复用可信普通 LOGIN 目录和实际物理目标核验。真实 RMA heads、versions 表保留原复合键、延迟外键、JSONB 和版本不可变，验证租户固定约束和最小授予。每租户及 lineage 使用独立控制行。DDL、权限、本地历史和共享 Ready 同事务。

通过真实独立 CLI 观察 A/A 竞争与 B 成功提交，在实际 DDL 和历史后中断准确 A 客户端并核原子回滚，复测同键和原结果。W350 范围为新隔离库原 RMA 业务对象首版迁移。已有共享默认、历史迁移、现有数据切换及后续原 HTTP 入口采用按各自边界保留。 [原 RMA 迁移](../../../../CP6.Persistence.PostgreSql/Migrations/Core/20261030022000_RmaApplicationV1.cs) · [原 PGT](../../acceptance/postgresql-tenancy.json)

COORD 串行回收共享台账、正常合并与席位，原正式验收状态按实证范围分别登记。
