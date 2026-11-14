# W560-01 · 询价按供应商转采购单

2026-11-14 UTC。Main 基线 `b018f0892ab6675e3733fdb9e9c32d5a28129b31`，受测源码 `03abc87f4e2deba633054187d83e056c6f581a9b`。隔离 PostgreSQL 18.6、原 Main Kestrel、真实登录与 IAM 撤权、模拟询价、供应商和采购。 [Issue #1046](https://github.com/GTX537/CP6/issues/1046)

现行询价转采购单沿用每组独立事务。每组入库、明细与编号使用同一 Core Context 和同一 Main IAM 根。进入每组时读取当前选中报价、数量与要求日期，核对原成交依据和有效期。询价抬头最后在同一授权根下单独完成，当前选中组合变化返回 409。 [服务](../../../../CP6.Core/Services/Pur/RfqService.ConvertPostgreSql.cs) · [HTTP](../../../../CP6.WebApi/Controllers/Pur/RfqController.cs)

- 基线最初三项实测中，两项竞争失败，普通转单通过。旧请求在 IAM 持有根时返回两张采购单，另一顺序下撤权无需等待写者。
- 修复后撤权先持根时，转单等待后返回 403，采购头、明细、编号与询价持久状态均保持原值。首组先持根时，撤权等待实际买方事务，再拒绝第二组和抬头更新。首组订单、明细、编号、询价来源、`3.12345678` 数量、`12.1234` 成交价、下单日与原交期保留。
- 最终抬头已持根时，两组订单已独立提交，撤权等待抬头提交，抬头终态为 Converted。两种先后均检查 IAM 角色版本、代次、快照、优先 Outbox、审计及撤权原回执。所有竞争中迟到旧授权再次写入均拒绝。通过 `pg_blocking_pids` 与两条独立 PostgreSQL 连接观察实际等待。 [PostgreSQL 回归](../../../../CP6.Tests/Erp/Pgt017RfqConversionPostgreSqlTests.cs)
- 14 项 PostgreSQL 与 42 项 RFQ、价表和采购路由组件通过，零失败、零跳过。包括原 W556 回写、W559 询价五入口和本轮四个场景。编译零错误，既有告警保留。隔离发布建库退出 0、存活 200、匿名租户管理及询价 401。
- 本地发布 310 文件，WebApi SHA-256 `d499086ddcd3fc496ac9240dad1993d0ce41c7dc0c2e3e984ba8f33f3c1d390f` 与受测程序集一致。原始 TRX、日志和文件摘要在 `D:/CP6-test-artifacts/W560-01`，独占实例和应用已停止。Actions 启动、重跑和取消均为零。

现行入口没有跨组原操作键。中途被撤权后旧会话继续拒绝，原授权恢复后重复调用可能重新生成已提交组，不能将失败响应作为无效果依据。正式商业分组原回执及查复按 PUR-RFQ-01 保留。本轮沿用旧订单的价格、汇率及供应商结算合同，并未采用正式商业组。 [PUR-RFQ-01](../../../design-handbook/modules/PUR-RFQ-01.md)

正式 PostgreSQL **26 PASS / 1 NOT_RUN**，原询盘 **60/60**。其余采购、独立 Space Design、旧 OA、财务、Stock 普通数据库身份及两库恢复对账仍待。Main 分支、工作区及席位处置见远端 PR 和 `main-finish.json`。 [正式矩阵](../../acceptance/postgresql-tenancy.json#L646) · [PGT-017 跟踪](https://github.com/GTX537/CP6/issues/1012)
