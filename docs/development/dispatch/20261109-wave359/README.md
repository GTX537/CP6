# W359 原 RMA 当前授权列表

2026-11-09 08:50:03 UTC，RMA-HTTP-LIST 领取 DEV-1，codex/w359-01，基线 421fb54497913f0998506455715d90e05bec7605。W356 随 [#814](https://github.com/GTX537/CP6/pull/814) 远端采用，准确分支清理并释放。沿原专有 PostgreSQL 55566，起始估 20–35 分钟。

原列表使用现行原 RMA Owner 和真实普通租户 EF。按当前共享出库可见及 self 筛选后，以 PostgreSQL ID 先后分页。实际 A/B 同 ID、多于一页、第二页跨候选批次、字段遮蔽和撤权。普通写与原 journal 另窗。

W358 的实际并发索引、A 两次中断、B 独立和原键恢复通过。 [交付报告](../../evidence/W358-01/DELIVERY.md)

2026-11-09 08:51:03 UTC，W360-VERIFY 领取 INTEGRATION-1，SCHEMA-IDENTIFIER-VERIFY，固定 detached 同基线，私有 PostgreSQL 55568，估 15–27 分钟。真实全 Core 的 GUID 命名、Unicode/长输入、错误所有者冲突、原键恢复与普通角色。COORD 保留原验证状态和独占写入。
