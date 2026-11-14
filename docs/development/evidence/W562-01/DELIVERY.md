# W562-01 · 采购申请和订单建单及订单取消

2026-11-14 UTC。Main 基线 `eface8879c0383408d345ab8248c18475755e211`，受测源码 `0c733d4e2756df94e02bf25c0cca89294e962f20`。隔离 PostgreSQL 18.6、实际 Main Kestrel、真实登录与 IAM 角色撤权、模拟采购申请和订单。 [Issue #1050](https://github.com/GTX537/CP6/issues/1050)

采购申请直接建单、采购订单直接建单和取消登记现有 `CurrentWriteAuthority`。这三个路径的实际写入使用请求的 Core Context。采番、建单头和明细随同一个 PostgreSQL 事务提交。取消沿用已有状态和软删除判定。 [申请](../../../../CP6.WebApi/Controllers/Pur/PurchaseRequestController.cs) · [订单](../../../../CP6.WebApi/Controllers/Pur/PurchaseOrderController.cs)

- 最终基线四项竞争失败。旧入口在撤权持有根时能成功建 PR、建 PO 和取消 PO。PO 写者先写且等待表锁时，旧撤权仍能先提交。早期两次测试含夹具全局菜单 ID 碰撞，采用不同固定 ID 后在原产品重新验证四项基线失败，保留原 TRX。
- 修复后三个入口撤权先提交均返回 403，业务行、状态、编号前后相等。PO 先取得根时撤权等待实际业务事务。订单头、明细、数量 `3.12345678`、单价 `12.1234` 和编号保留，迟到请求拒绝。重授后两类建单及取消正常完成。两种时序核对角色版本、身份代次、快照、Outbox、强审计和原回执。 [PostgreSQL 回归](../../../../CP6.Tests/Erp/Pgt017PurchasingWritesPostgreSqlTests.cs)
- 七项 PostgreSQL、26 项采购组件通过，零失败、零跳过。包括四项新增竞争和 W561 的三项申请分组回归。编译零错误，既有告警保留。本地发布 310 个文件，WebApi SHA-256 `0e3adddd7f60223c0a6089db3d1da676817f1ae264fbfba5bca11e48b7b64a1a`，与实测宿主一致。隔离建库退出 0、存活 200、匿名管理与询价 401。原 TRX、日志及摘要存于 `D:/CP6-test-artifacts/W562-01`，隔离实例已停止。Actions 启动、重跑和取消为零。

正式 PostgreSQL **26 PASS / 1 NOT_RUN**，原询盘 **60/60**。采购申请和订单送审进入旧 OA 通知与回调链，采购收货走独立 WMS 事务。它们与其他采购写者、独立 Space Design、旧 OA、财务、Stock 普通身份及两库恢复与运输仍待核销。分支、工作区及席位处置见远端 PR 和 `main-finish.json`。 [正式矩阵](../../acceptance/postgresql-tenancy.json#L646) · [PGT-017 跟踪](https://github.com/GTX537/CP6/issues/1012)
