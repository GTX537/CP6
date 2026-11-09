# W356 原 RMA HTTP 与动态 EF 采用

2026-11-09 08:24:33 UTC，RMA-HTTP-READ 领取 DEV-1，codex/w356-01，基线 8c72ed72784d973d3fb66818c8e01a245e77d1cf。W353 已随 [#812](https://github.com/GTX537/CP6/pull/812) 采用、准确分支删除并释放。隔离 PostgreSQL 55566，起始估 35–65 分钟。

经原真实登录和正常 RMA 单条/历史 GET 走当前共享会话、出库可见性和字段决定。实际普通 LOGIN 在同一物理库、准确 schema 和 Ready 上使用最小 EF 模型，核 A/B 同 ID、当前撤权、错目标、模型缓存隔离和原 DTO/ETag。原共享默认与历史迁移保留，启用环境中尚未接续的旧数据路径拒绝。普通新写与原 journal 另窗。

W355 在 DEV-2 测真实 Stock 普通登录，10/100 冷暖、池和双进程有限并发已通过，待采用。 [交付报告](../../evidence/W355-01/DELIVERY.md)

2026-11-09 08:25:15 UTC，W357-VERIFY 领取 INTEGRATION-1，RESTORE-INCOMPLETE-VERIFY，固定 detached 同基线，私有 PostgreSQL 55565，起始估 18–32 分钟。用真实仅租户 schema 备份核两个现有预览/准入边界及完整备份对照。保留原运行，形式 PGT 与完整单租户恢复另验。COORD 串行维护台账与合并。
