# W566-01 · 采购送审与当前授权事务

2026-11-14 UTC。Main 基线 `b738ae07eb6b6547a9548a4e033f927ca5f8fc70`，受测源码 `3df55876ddad659a4817bec340ed0b48eb189852`。隔离 PostgreSQL 18.6、真实 Main Kestrel、模拟采购申请、采购订单、审批流程和本地 HTTP 连接器。 [Issue #1057](https://github.com/GTX537/CP6/issues/1057)

旧采购送审只在入口检查权限，首次工作流 INSERT 较晚取得共同根。真实 IAM 撤权先持根时，旧会话请求等待后仍返回 200。另一路流程在首次持久化前同步调用外部连接器，外发没有稳定的作业 ID。旧产品的撤权回归 2 通过、2 失败，外发回归 0 通过、2 失败。保留失败原件。 [撤权测试](../../../../CP6.Tests/Erp/Pgt017PurchasingApprovalPostgreSqlTests.cs) · [外发测试](../../../../CP6.Tests/Erp/Pgt017PurchasingApprovalDeliveryPostgreSqlTests.cs)

- 采购申请和采购订单送审从 Main 共同认证根内读取当前会话、租户和动作权限。送审单据、工作流、历史、任务及通知 Outbox 随同一个 PostgreSQL 事务提交。真实 IAM 角色操作先获根时，写入口等待后返回 403，单据仍为草稿。业务先获根时，撤权等待，原顺序、工作流和单据关联保留，原会话的新请求返回 403。回读 IAM 代次、角色版本、审计、快照、唯一优先 Outbox 及原回执。两种单据的 start→end 回调验证即时终态。 [当前授权](../../../../CP6.WebApi/Services/IamAdministrationHttpV1.cs) · [采购订单入口](../../../../CP6.WebApi/Controllers/Pur/PurchaseOrderController.cs) · [采购申请入口](../../../../CP6.WebApi/Controllers/Pur/PurchaseRequestController.cs)
- 采购启动流程遇到配置为同步的 WebAPI 节点，提交后才由现有 worker 外发。HTTP 送审可以先返回已提交或待审批状态。两种单据分别验证了数据库提交被拒时没有单据、流程、队列或外发效果，数据库已提交而回执丢失时返回未知结果、直接回读原单据与流程、重提不产生第二个作业。模拟连接器接受请求后丢失第一次回包，worker 使用同一作业键重试，模拟端按该键只产生一次业务效果，单据最终正确批准。接收方仍需支持该键的幂等契约。 [工作流启动](../../../../CP6.Core/Services/Wf/FlowEngine.cs) · [作业执行](../../../../CP6.Core/Services/Wf/WfServiceJobService.cs)
- 最终构建零错误，最终定向 8 项通过、零跳过。候选 41 项通过、零跳过；其中复用未受最终修改影响的 35 项既有组件及采购检收覆盖。本地隔离发布 310 文件，WebApi SHA-256 为 `5935e623a99a2e2ff6ad645722aaff68585f2d2d36ebb4e9745a9eee2ea4ad9d`。实际建库退出 0，存活 200，匿名管理及询盘 401。原 TRX、日志、产物摘要和冒烟结果存于 `D:/CP6-test-artifacts/W566-01`。未触发 Actions、未部署。

正式 PostgreSQL 26 PASS / 1 NOT_RUN，原询盘 60/60。 剩余范围包括采购确认收货的 WMS 自有事务和采购累计、旧 OA 其余写者、财务、独立 Space Design、原生数据库角色，以及两库未知结果恢复、重开对账、已登记宿主传输和旧实例排空。 工作区和分支最终处置由 PR 与本任务 `main-finish.json` 记录。 [正式矩阵](../../acceptance/postgresql-tenancy.json#L646) · [PGT-017 #1012](https://github.com/GTX537/CP6/issues/1012)
