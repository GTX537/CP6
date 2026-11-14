# W570-01 · CRM 物理恢复后的 C02 差量

2026-11-14 UTC，Main `f3be6dda29dc18989a97b787a49919992ed69f3a`。CRM 受测夹具 `c820163668f4a95db7bbff4bc61597520c916cb1` 已由 [CRM #222](https://github.com/GTX537/CP6.CRM/pull/222) 合入远端 `82a4383ab21b4313a6c469fc015fa95d98fd5784`。两套独立 PostgreSQL 18.6 及一个 CRM 物理恢复实例，使用真实 Main 撤权及重授 HTTP、CRM Owner 和合成业务数据。 [CRM 交付记录](https://github.com/GTX537/CP6.CRM/blob/82a4383ab21b4313a6c469fc015fa95d98fd5784/docs/development/evidence/W570-01/DELIVERY.md)

- 关闭的恢复目标补收 Main Outbox 的两个原始 C02 事件，沿原生 Inbox 和恢复发布路径把角色从版本 5 补齐至 7。实际原库的封存和已发布角色为版本 7。目标与原库角色字节摘要、两个入站事件、原导出回执及出站消息原文相同。
- 目标物理绑定持续关闭。它缺少原库已提交的角色封存版本和一份导出制品，两条已投递消息仍是 Pending，无原发送尝试。外部接收使用本地 HTTP 协议，Main 原 C02 字节通过试验桥传给 CRM。
- 物理恢复场景和 CRM 先提交对照各由 Main、CRM 通过一项，共 4 项，零跳过。初次夹具错误地假定恢复点角色版本为 1，调整为核对实际版本差量。原始 TRX、日志、摘要及停止证据在 `D:/CP6-test-artifacts/W570-01`。此轮只改测试和文档，复用 CRM 产品冒烟。Actions 启动、重跑、取消为零。 [W569-01](../W569-01/DELIVERY.md)

正式 PostgreSQL **26 PASS / 1 NOT_RUN**，原询盘 **60/60**。下一段恢复需要在关闭窗口对齐已提交的封存、业务效果和真实 broker 水位，完成旧实例排空并验证重开；仍需覆盖 Main 的旧 OA、财务、独立 Space Design 等普通写者。 [正式矩阵](../../acceptance/postgresql-tenancy.json#L646) · [PGT-017 #1012](https://github.com/GTX537/CP6/issues/1012)
