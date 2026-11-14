# W557-01 · CRM 时间点恢复后的询盘写入

Main 基线 `78a3312029abe26c8a90c4f0feaa563a62afefe5`，CRM 受测提交 `d51c8d201e63e6b0ccbd74477fe44f242b216712`，已由 [CRM #218](https://github.com/GTX537/CP6.CRM/pull/218) 合入远端 `ffeadb7e70fe0d8c03c0bdf45c4c797f08fd0506`。使用隔离 PostgreSQL 18.6、真实基础备份、WAL 和模拟租户。原库提交角色撤权后拒绝版本 1，恢复至撤权前的数据库保留原集群标识与 OID。基线仍返回开放版本 1。 [修复 #1040](https://github.com/GTX537/CP6/issues/1040) · [CRM 实测与采用](https://github.com/GTX537/CP6.CRM/blob/ffeadb7e70fe0d8c03c0bdf45c4c797f08fd0506/docs/development/evidence/W557-01/DELIVERY.md)

- 将原 WAL 时间线纳入原栅栏。实际物理恢复验证备份前安装、以及旧副本恢复后安装两种情况。恢复库拒绝开放版本读取和 Main 的暂停、重开、角色、身份、期限、部门封存，原恢复状态保持不变。已有登记在原库升级后需验证来源，登记可信原时间线保留已提交撤权。
- 同样的栅栏不可用状态下，CRM 原生公开签发与提交返回 503，无新 cookie、尝试行、配额、业务或 Outbox。此 HTTP 用例在原库模拟登记缺失，实际物理恢复检查直接在数据库执行。CRM 原生回归 10 通过、0 跳过。
- 实际 Main TLS 权威与 CRM 导出联验两种提交顺序，各 2 个仓库测试通过。CRM 先提交保留已落库的导出，Main 撤权先提交拒绝旧授权，新授权发布后可按原导出继续。首次 CRM 先提交误选仅适用于另一顺序的重授用例，修正选择后通过，失败保留。
- CRM 本地发布 255 文件，Infrastructure SHA-256 `68b0bda24dd4ac57ea6e15876619c7693767353fda3eb70badf243c310f1d010` 与受测程序集一致，隔离原生宿主存活 200、匿名配置 403。CRM 工作树已干净分离，分支本地及远端已删除，DEV-2 已释放。原始结果在 `D:/CP6-test-artifacts/W557-01`。

正式 PostgreSQL **26 PASS / 1 NOT_RUN**，原询盘 **60/60**。物理恢复后重新开放、两库差量和原结果及消息对账、旧宿主排空及实际传输仍待验证。独立 Space Design、旧 OA、财务、采购收货和计划、Stock 普通 PostgreSQL 写者继续按原提交边界核对。Actions 启动、重跑、取消为零。本轮本地测试和发布使用隔离环境。 [正式矩阵](../../acceptance/postgresql-tenancy.json#L646) · [剩余写入口](../W531-01/DELIVERY.md#仍待核销的正式前置) · [跨库余项 #1012](https://github.com/GTX537/CP6/issues/1012)
