# W400 同一 v8 原包的 IAM 与 Identity Priority 对账

W400 于 2026-11-09 13:29:58 UTC 从 #839 在 DEV-1 开工，估 10–16 分钟，本地验证候选 243.388 秒。W399 随 #839 采用，实际 1,871 秒，两端分支已清理，DEV-2 干净释放。 [W400](../../evidence/W400-01/DELIVERY.md) · [PR #839](https://github.com/GTX537/CP6/pull/839)

A/B 两套各自独立的 IAM 和 Identity Priority 只读快照均以原 v8 pin 通过。各自 17/12 IAM 事实、各一条已发布 Identity Outbox。私有篡改 v8 ERP 成员后，两入口均在连接前拒绝。

W401 在 2026-11-09T13:28:16.732007+00:00 从 #839 开始验证真实两版原件在隔离 Core 的普通读回，估 24–42 分钟。W402 在 2026-11-09T13:34:29.461970+00:00 从 #839 启动同切面安全审计和主体保留历史，估 22–34 分钟。 [原 S02 §9](../../../design-handbook/architecture/02-postgresql-tenancy.md#9-单租户导出恢复和全局恢复)
