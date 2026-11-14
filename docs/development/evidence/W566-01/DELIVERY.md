# W566-01 · 采购送审撤权复现

2026-11-14 UTC。Main 基线 `b738ae07eb6b6547a9548a4e033f927ca5f8fc70`。隔离 PostgreSQL 18.6、真实 Main Kestrel、模拟采购申请、采购订单和历史审批流程。 [问题 #1057](https://github.com/GTX537/CP6/issues/1057)

现有 PR/PO 送审在入口验权限，`Wf_FlowInstance` INSERT 的语句级触发器较晚取得共同根。用真实 IAM 角色操作在持根后阻塞其授权写入，两个入口在流程 INSERT 等待该根。放开撤权后，旧授权在两种单据上均返回 200，回归预期 403。业务先取得流程事务锁时，原流程实例、关联的单据状态、待办、提交历史与通知 Outbox 均完成，撤权后三段查询复核原结果保留、旧会话新请求拒绝。 [回归夹具](../../../../CP6.Tests/Erp/Pgt017PurchasingApprovalPostgreSqlTests.cs) · [数据库触发器](../../../../CP6.Persistence.PostgreSql/Migrations/Core/20261102183000_PersonalFormV2.cs)

本任务只保留复现。真实审批通知通过提交后分发的 Outbox；另一条合法的旧流程路径允许开始时同步执行外部 WebAPI，发生在首次保存前。需要按既有流程分出获准的持久外发意图、提交后调用和原结果恢复，核对绑定与权限后再接入完成门，同时保留原采购终态行为。优先以模拟外部服务验证提交失败、超时和回放。 [节点执行](../../../../CP6.Core/Services/Wf/NodeHandlers/ServiceTaskNodeHandler.cs) · [工作流开始](../../../../CP6.Core/Services/Wf/FlowEngine.cs)

本地构建零错误。最终旧产品用例 2 通过、2 失败、零跳过，两项失败均为预期 403、实际 200。第一次测试仅用显式根 SQL 匹配等待，漏识别触发器的等待，原结果保留。证据目录 `D:/CP6-test-artifacts/W566-01/baseline-confirmed`。正式 PostgreSQL 26 PASS / 1 NOT_RUN，原询盘 60/60。产品、迁移、工作流及发布配置未修改。分支留存用于继续修复，远端采用、工作区和席位信息以本任务 finish 记录为准。 [正式矩阵](../../acceptance/postgresql-tenancy.json#L646)
