# W384 原队列当前只读对账

W384 于 2026-11-09 11:30:40 UTC 领取 DEV-1，IDENTITY-PRIORITY-RECONCILE，codex/w384-01，基线 b21d364e2eec480847521be0a3a5e8ad61406f38，专属 PostgreSQL 55566，估 24–40 分钟，实库及集中复核证据齐备 1,394 秒。W385 随 #829 正常采用，完整 638 秒，两端分支删除，DEV-2 干净释放。 [W384](../../evidence/W384-01/DELIVERY.md) · [#829](https://github.com/GTX537/CP6/pull/829)

原 Inbox/Outbox 全局键按实际 PostgreSQL 比较。对账保留当前 132、冲突 5、原件缺失 2 UNKNOWN。真实低权 RLS 与修复后关闭 Windows 句柄通过。源租户两边无持久改动。

W387 于 2026-11-09T11:50:20.530142+00:00 在 DEV-2 开工，基线 #829，专属 PostgreSQL 55567，估 22–38 分钟。W388 继续按原场景核现有证据。当前恢复候选、IAM 原引用与外部资产依赖继续。
