# W565-01 · 采购检收结果与 IAM 撤权

2026-11-14 UTC。Main 基线 `6e97a005208dc06e7c0f7dfe243d2cf73778c769`，受测源码 `b84fa740fdd849bba82abf88140b93d1a9ad6371`。隔离 PostgreSQL 18.6、真实 Main Kestrel、真实 WMS 检收读取适配器、合成采购单和判定。 [Issue #1055](https://github.com/GTX537/CP6/issues/1055)

旧 `POST /api/pur/gr/{grNo}/apply-qc` 只在动作入口检查权限。旧会话可以在 IAM 撤权持有认证根时回 200。业务先更新收货行与验收累计时，撤权也能在 PO 派生状态提交前完成。现在使用同一 Core Context 的现有当前授权过滤器，保持原动作键和检收规则。从查询判定、更新收货结果到刷新 PO 状态都位于一个 PostgreSQL 事务并持认证根。 [入口](../../../../CP6.WebApi/Controllers/Pur/GoodsReceiptController.cs) · [检收查询](../../../../CP6.Core/Services/Pur/Contracts/WmsQcQueryAdapter.cs)

- 两种顺序均用 PostgreSQL 阻塞后端确定。IAM 先持根时，新写等待后返回 403，收货单与验收累计不变。检收先持根时，IAM 等待，同一事务保留 3.12345676 合格、0.00000002 不良、收货完成、PO 状态和累计。撤权后原会话的后续写返回 403。两种顺序均核对角色版本、代次、快照、唯一优先 Outbox、审计和原回执。 [原生回归](../../../../CP6.Tests/Erp/Pgt017GoodsReceiptQualityPostgreSqlTests.cs)
- 修复前 2 项回归均失败。候选中 13 项通过、1 项因夹具期望其他 Owner 的拒绝码失败。按本入口既有 `IAM_FORBIDDEN` 回读修正断言后，2 项竞态全部通过。其余候选 13 项通过包含 1 项已复跑的竞态、4 项原采购 PostgreSQL 和 8 项收货及适配器组件检查。无跳过。最终构建零错误，保留既有告警。
- 本地发布 310 文件，实测 WebApi 与发布 SHA-256 均为 `3db2fa4f665ed5e3d6e0b8e1b9b7d260ce087aaedd57590336f2867162084afe`。隔离发布建库退出 0、存活 200、匿名管理及询价 401。原 TRX、日志、发布摘要和停止记录在 `D:/CP6-test-artifacts/W565-01`。Actions 启动、重跑和取消均为零。

源码核对确认 CRM 到期保留与复核的实际批次在新租约和新单元初始化时持 C02 栅栏，原公共回执路径只读取原件。已有实测使用预置单元触发后续业务写入，批次初始化为本轮源码核对。租约完成或技术失败记账、过期桶清理按原职责提交。 [原询盘后台验收](../W544-01/DELIVERY.md) · [CRM 保留 Owner](https://github.com/GTX537/CP6.CRM/blob/f8c9a453164a1af8f0ade70ed240ac86df63ca16/src/CP6.CRM.Api/Inquiry/CrmInquiryRetentionOwnerV1.cs) · [CRM 复核过期 Owner](https://github.com/GTX537/CP6.CRM/blob/f8c9a453164a1af8f0ade70ed240ac86df63ca16/src/CP6.CRM.Api/Inquiry/CrmInquiryReviewExpiryOwnerV1.cs)

采购确认收货包含 WMS 自有提交边界，仍需核对。采购送审和 WMS 自管事务的收货确认、旧 OA 其余写者、财务、独立 Space Design、数据库角色及两库恢复对账和已登记宿主的传输与旧实例排空待验。正式 PostgreSQL **26 PASS / 1 NOT_RUN**，原询盘 **60/60**。分支、工作区和席位最终处置见 PR 与 `main-finish.json`。 [正式矩阵](../../acceptance/postgresql-tenancy.json#L646) · [PGT-017 #1012](https://github.com/GTX537/CP6/issues/1012)
