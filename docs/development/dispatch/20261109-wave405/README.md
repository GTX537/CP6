# W405 RMA 和 Main Stock 同快照归档

W405 于 2026-11-09 14:08:16 UTC 在 DEV-1 从 #842 开工，原估 22–38 分钟。本地原生验证和集中复核齐备用 2,254.0 秒，含内存和固定端口等待、真实派送守卫诊断。W404 随 #843 采用，实际 858 秒，两端分支清理且 DEV-2 干净释放。 [W405](../../evidence/W405-01/DELIVERY.md) · [PR #843](https://github.com/GTX537/CP6/pull/843)

统一 v10 从一份 PostgreSQL 快照保留 RMA、既有依赖与 14 个 Stock 成员。真实晚到 Stock 和隐私写留在快照之后。实际 Stock 消息为 Pending，派送兼容独立跟踪。 [派送问题](https://github.com/GTX537/CP6/issues/844)

W406 于 2026-11-09T14:17:33.891016Z 从 #843 在 DEV-2 开工，估 24–42 分钟，内存排队单列。W401 继续使用原隔离候选验证普通读回。 [原 S02 §9](../../../design-handbook/architecture/02-postgresql-tenancy.md#9-单租户导出恢复和全局恢复)
