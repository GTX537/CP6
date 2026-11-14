# W555-01 · Main 范围重授与 CRM 原导出恢复

2026-11-13 UTC。Main 基线 `6830f343486af75f61805481b9f25f9119dfe790`，实测夹具 `ad45a494a5004db9741063e23c514a8c16d7b9a7`。CRM 实测夹具 `1625fab01acdaaa4d3bb03060ab4aeb0fb97ad85`，远端采用 `27ce9d891dc252440186b2343dae0a7013b50d59`。两套隔离 PostgreSQL 18.6，使用模拟租户、角色、站点表单、询盘和导出。 [CRM #217](https://github.com/GTX537/CP6.CRM/pull/217) · [CRM 证据](https://github.com/GTX537/CP6.CRM/blob/27ce9d891dc252440186b2343dae0a7013b50d59/docs/development/evidence/W555-01/DELIVERY.md)

1. Main 真实版本化 IAM 撤销目标站点和表单组合。CRM 迟到的先前 TLS 授权被拒，待处理导出、制品、审计、Outbox 无变化。
2. 将 Main 原 PostgreSQL Outbox 的 C02 V3 原始事件交给 CRM 原生 Inbox。使用实际恢复和投影发布，保留原测试租户其他快照。被撤范围继续拒绝。
3. Main 使用新操作重授同一组合，版本、身份代次、Outbox、审计、回执各推进，重复命令读取原回执。CRM 收到并发布下一版本后，用新 TLS 权限完成同一待处理导出，仅生成一份制品。再次运行无新增效果。

跨库主场景和 CRM 先提交的兼容场景各在 Main 与 CRM 通过一项，零失败、零跳过。测试桥传输原始 Outbox 事件，实际传输调度仍待核对。仅改联验夹具和记录，产品发布复用 [Main W553](../W553-01/DELIVERY.md) 与 [CRM W550](../W550-01/DELIVERY.md)。本地源码、二进制摘要、TRX 和停止记录在 `D:/CP6-test-artifacts/W555-01`。Actions 启动、重跑及取消为零。

正式 PostgreSQL **26 PASS / 1 NOT_RUN**，原询盘 **60/60**。剩余全写者登记、独立 Space Design、OA、财务、采购、MRP、普通数据库角色，以及两库数据库时间点恢复、映射变更、实际传输和滚动采用。 [正式矩阵](../../acceptance/postgresql-tenancy.json#L646) · [跨库余项 #1012](https://github.com/GTX537/CP6/issues/1012) · [Main 原清单](../W531-01/DELIVERY.md#仍待核销的正式前置)

两仓分支、工作区和席位处置见远端 PR 和 `main-finish.json`、`crm-finish.json`。
