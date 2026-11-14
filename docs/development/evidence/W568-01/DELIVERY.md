# W568-01 · Main 撤权提交后回执丢失

2026-11-14 UTC。Main 基线 `e1c1e7d04af870e5573e5fd8e14a17597e9c93df`，定向夹具 `b7ae0e7891265e8988e829d8794086fa6730985f`。CRM `f8c9a453164a1af8f0ade70ed240ac86df63ca16`。两个隔离 PostgreSQL 18.6 实例、实际 Main IAM HTTP、实际 TLS 服务权限及 CRM Worker，采用模拟租户、角色、表单与待处理导出。 [PGT-017 #1012](https://github.com/GTX537/CP6/issues/1012)

- CRM 先取得仍合法的导出许可并暂缓。真实 Main IAM 请求先在 CRM 栅栏提交角色门槛，再提交 Main 撤权。本机测试中继只在 PostgreSQL 确实返回 COMMIT 成功后丢弃该回包。Main HTTP 返回 503 `IAM_OPERATION_UNKNOWN`。独立连接确认 CRM 门槛及 Main 角色、快照、权限代次、唯一优先 Outbox、审计和原回执均实际提交。在 C02 新事件未发布时，迟到许可仍被 CRM 拒绝，导出原状态、制品和审计、Outbox 没有新增。
- 使用原操作 ID 查询返回 `COMMITTED`，重复相同 Main 命令返回同一回执，核对两库写集保持稳定。CRM 先提交的对照通过锁等待确认 Main 等待，原导出保存，撤权以后新签发返回 403。 [主机夹具](../../../../CP6.Tests/Platform/IamInquiryServiceContextHttpTests.cs) · [回执及写集](../../../../CP6.Tests/Platform/Pgt017CrmExportRoleRevocationPostgreSqlTests.cs)
- 两个场景各在 Main 与 CRM 通过一项，共 4 项，零失败、零跳过。隔离进程及数据库已停止。Build 零错误。原 TRX、日志、停止记录和两仓受测程序集摘要位于 `D:/CP6-test-artifacts/W568-01`。产品合同、实现及发布工作流均沿用已采用验证，Main [W567](../W567-01/DELIVERY.md) 与 CRM [W557](../W557-01/DELIVERY.md) 的产品冒烟供复用。Actions 启动、重跑及取消均为零。 [CRM 提交回执丢失的前序 W552](../W552-01/DELIVERY.md) · [Main 回滚的前序 W551](../W551-01/DELIVERY.md)

正式 PostgreSQL 26 PASS / 1 NOT_RUN，原询盘 60/60。 已核对 CRM 原活库正常重新激活和物理恢复后继续拒绝的既有证据。物理恢复目标的时间线状态与普通租户重开是两个边界，既有 C02 投影对账只覆盖对应身份事实。仍需完成物理恢复后的 Main 权威与 CRM 封存版本、C02 已发布身份、原业务回执和消息水位对账，目标库重新开放，实际宿主运输与旧实例排空，以及 Main 的旧 OA、财务、独立 Space Design 和其余普通数据库写者。 分支和席位处置见 PR 与本任务 `main-finish.json`。 [物理恢复 W557](../W557-01/DELIVERY.md) · [PostgreSQL 验收矩阵](../../acceptance/postgresql-tenancy.json#L646)
