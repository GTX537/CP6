# W408 v10 Stock 当前权威对账

W408 于 2026-11-09T15:45:16.514769Z 在 DEV-2 从 #846 开工，估完整 29–48 分钟。本地原生验证及集中复核齐备用 1,273.6 秒。W407 随 #846 采用并释放固定席位，完整 1,914.212 秒。 [W408](../../evidence/W408-01/DELIVERY.md)

同一只读快照核原消息键、租户、操作、摘要和当前路由。v10 原件核当前 Pending，W352 与 W403 的独立原件核历史 Pending 对当前 Published，包含原票据、完成回执和发送尝试。 [原 S02 §9](../../../design-handbook/architecture/02-postgresql-tenancy.md#9-单租户导出恢复和全局恢复)

Platform #80 的精确源码已合入，Platform 任务分支两端清理、原席位释放。旧 0.10.3 实际重现 LF 拒绝，新源码的本地 PostgreSQL 定向回归 9 项通过。正式包和 Main 采用待验。 [Platform #80](https://github.com/GTX537/CP6.Platform/pull/80) · [Issue #844](https://github.com/GTX537/CP6/issues/844)

## W409

W409 于 2026-11-09T16:02:57.480891Z 在 DEV-1 从同一 #846 开工。三个既有入口采用同源 v9/v10，估完整 12–20 分钟，资源与合并排队另计。两个任务分开编辑，验证复用专属副本并串行进行，共享台账按任务串行合并。W406 续验完整 Core 以及 W401 普通读回保留各自条件。 [W406 保存候选](https://github.com/GTX537/CP6/blob/fe52f94eaa8d93e7c560b518a229d2ccbd75abdb/docs/development/evidence/W406-01/DELIVERY.md)
