# W569-01 · CRM 恢复库拒绝重复投递

2026-11-14 UTC，Main 基线 `6511f246537f824ccb883149333c7bf5ec01e757`。CRM 修复源码 `9aa9c8127f7fdd84cec79cf73227533824b03c5e` 已合入远端 `main`，采用提交 `5baf07e5df5d93278adce5c9dec6f76b5b482470` 与受测源码树一致。 [CRM #221](https://github.com/GTX537/CP6.CRM/pull/221) · [CRM #220](https://github.com/GTX537/CP6.CRM/issues/220)

- 实际 PostgreSQL 18.6 基础备份加 WAL 恢复，基线原库和恢复库分别发布同一条已接受的询盘消息。修复后，配置了存储绑定的 CRM 发布器在新建派发前核对原时间线。恢复库连 HTTP 接收端前即被拒绝，原回执、消息和派发记录保持原样。原库仍能按原权限投递已经提交的消息。 [源码、权限与实测](https://github.com/GTX537/CP6.CRM/blob/5baf07e5df5d93278adce5c9dec6f76b5b482470/docs/development/evidence/W569-01/DELIVERY.md)
- 一项物理恢复回归、两个既有栅栏物理恢复场景及 20 项定向回归通过。最终执行零失败、零跳过。第一次宽泛筛选的历史过期夹具缺失和跨库未运行结果一并保留。发布宿主健康 200、匿名配置 403。原始日志、发布文件摘要和数据库关闭记录位于 `D:/CP6-test-artifacts/W569-01`。本地 HTTP 接收端模拟发布协议。当前 GitHub Actions 启动、重跑和取消为零。 [CRM 交付报告](https://github.com/GTX537/CP6.CRM/blob/5baf07e5df5d93278adce5c9dec6f76b5b482470/docs/development/evidence/W569-01/DELIVERY.md)

正式 PostgreSQL **26 PASS / 1 NOT_RUN**，原询盘 **60/60**。PGT-017 仍需在封闭恢复目标上核对 Main 权威、CRM 封存版本、C02 已发布身份、原业务回执与真实消息水位，对齐差量后验证重新开放和旧宿主排空；Main 旧 OA、财务、独立 Space Design 和其余普通数据库写者仍待逐个核销。两仓分支和席位处置记录分别为 `crm-finish.json`、`main-finish.json`。 [正式矩阵](../../acceptance/postgresql-tenancy.json#L646) · [PGT-017](https://github.com/GTX537/CP6/issues/1012)
