# W627-01 CRM 目标 C02 消费和后继 Outbox 采用

2026-11-15 UTC。CRM 本项受测源码 `2cbe688316b4df17b93229d654288dfb6d699f78`，远端主线 `e5a8157f20e5817f58b33242a4bd6188f1c58461`。两次联验各使用两个实际登记的模拟租户、隔离 PostgreSQL 18.6、Dapr 1.18.2 和认证 Kafka 4.3.1。Main 使用 W625 已验证的宿主 `30a4b08faeeb8ab5efd191c6c6f4c41e27f4f45e` 执行既有 IAM 命令。 [CRM PR #237](https://github.com/GTX537/CP6.CRM/pull/237) · [CRM 实际交付](https://github.com/GTX537/CP6.CRM/blob/e5a8157f20e5817f58b33242a4bd6188f1c58461/docs/development/evidence/W627-01/DELIVERY.md)

- 同一 C02 消费组的组位点和末端位点实测从 `30/30` 到 `30/31`。物理目标仍关闭时，真实 Dapr 回调得到 503/RETRY，两租户写集保持。已批准重绑后新主体接续到 `31/31`，原业务及原回执保持。旧 Topic、Group 和 APP token 由实际 broker 及目标宿主拒绝。
- 两条新事件由实际 Main 签名 IAM 请求写入原生 Outbox，验收中继读取原生字节，经独立认证的 Main Dapr 发布，目标 Dapr 实际消费。两个租户的 Main 权限与目标 CRM 封存版本分别到 8，核对原 Inbox、Source、Current、原回执和实际 Kafka 组位点 `33/33`。
- 物理后继 `00000003` 使用正确前身和目标专属数据库角色、Dapr 及 broker 主体完成第二次签名采用。实际两个目标 Worker 分别发布一条已存在的原待处理提交消息，保留全部 12 个 Root、原配置主题 6 条记录、历史已知回执和未知派送票据。直接前任的数据库登录、在线 broker 写权限和目标 token 实际拒绝。

实际联验 **8 项通过、零跳过**。首轮封存凭据长度、Windows 启动参数、核对查询 collation 和额外草稿的夹具失败已记录于 CRM 报告。Main 本 PR 同步验收和项目状态，CRM 已合入并经远端核对。Actions 与部署均为零，全部本地任务进程已停止。实际环境的批准与运行身份另按采用流程记录。正式 PGT-017 仍有 Main 写者范围待验。 [正式矩阵](../../acceptance/postgresql-tenancy.json#L646)
