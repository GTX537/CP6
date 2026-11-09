# W359 原 RMA 当前授权列表

2026-11-09 08:50:03 UTC，RMA-HTTP-LIST 领取 DEV-1，codex/w359-01，基线 421fb54497913f0998506455715d90e05bec7605。W356 随 [#814](https://github.com/GTX537/CP6/pull/814) 远端采用，准确分支清理并释放。沿原专有 PostgreSQL 55566，起始估 20–35 分钟。

原列表使用现行原 RMA Owner 和真实普通租户 EF。按当前共享出库可见及 self 筛选后，以 PostgreSQL ID 先后分页。实际 A/B 同 ID、多于一页、第二页跨候选批次、字段遮蔽和撤权。普通写与原 journal 另窗。

W358 的实际并发索引、A 两次中断、B 独立和原键恢复通过。 [交付报告](../../evidence/W358-01/DELIVERY.md)

2026-11-09 08:51:03 UTC，W360-VERIFY 领取 INTEGRATION-1，SCHEMA-IDENTIFIER-VERIFY，固定 detached 同基线，私有 PostgreSQL 55568，估 15–27 分钟。真实全 Core 的 GUID 命名、Unicode/长输入、错误所有者冲突、原键恢复与普通角色。COORD 保留原验证状态和独占写入。

W358 随 [#815](https://github.com/GTX537/CP6/pull/815) 正常采用并释放，完整 1,711 秒。W360 的实际命名及冲突验收在 581 秒后正式收尾，INTEGRATION-1 干净。

W359 真实 A/B 先授权再分页，第二页跨候选批次。正式验证 2 项通过、0 跳过。 [交付报告](../../evidence/W359-01/DELIVERY.md)

2026-11-09 09:04:56 UTC，W361-01 领取 DEV-2，RMA-NATIVE-JOURNAL，codex/w361-01，基线 baff5fc20dceee932c805fbf309e6542b960cbeb。私有 PostgreSQL 55567，估 26–45 分钟。沿原 RMA Owner 的原共享 journal 和旧写栅栏，验证受管普通角色的原生最小权、原件与原子性。

2026-11-09 09:05:15 UTC，W362-VERIFY 领取 INTEGRATION-1，RMA-COMMIT-REPLY-VERIFY，固定 detached 同基线，专用 PostgreSQL 55568，估 14–25 分钟。原控制方法接实际 COMMIT 截断，独立直连及真实 CLI 同键核原件。
