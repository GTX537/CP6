# W563-01 · CRM 隐私制品到期与 Main 暂停

2026-11-14 UTC。Main 产品基线 `c3fb0ca08070afe3a9eb0ec41f3f53565408b355`，受测夹具 `2ab27367e505b2a3afe970a6588194d8527991d4`。CRM 产品基线 `ffeadb7e70fe0d8c03c0bdf45c4c797f08fd0506`，受测夹具 `2979a3d6ec4c83f72e4c2296ea78e5eccd330947`，远端采用 `f8c9a453164a1af8f0ade70ed240ac86df63ca16`。两套隔离 PostgreSQL 18.6、模拟租户、站点、表单和询盘。Main 使用原版本化管理 HTTP 和真实 TLS 签发，CRM 运行原配置、公开询盘和隐私导出 Owner 以及普通会话连接。 [CRM #219](https://github.com/GTX537/CP6.CRM/pull/219) · [原生成和维护场景](../W544-01/DELIVERY.md)

- 通过实际 CRM 请求生成制品和下载凭证，使用配置允许的五分钟最短 TTL。等待 PostgreSQL 记录的真实截止点，数据库时间和原到期规则保持正常。
- CRM 先提交时，在原询盘行的数据库等待处保持业务事务与 C02 共同锁，Main 封存通过 `pg_blocking_pids` 被确认等待。释放测试锁后清除制品和凭证，状态 30→99、版本 2→3，审计新增一条。询盘内容、历史及原 Outbox 保持，Main 随后完成暂停。再次调用 worker 没有待清除项。
- Main 先提交时，先收到实际 TLS 许可，再等 Main 真实暂停与本地封存完成后交付许可。CRM 拒绝此次清除，制品、业务状态、版本、审计及 Outbox 前后相等。两种顺序均独立读回原建立请求的字节和 ETag，核对 Main 状态、权威代次、快照、优先 Outbox、审计、原命令及同键回读。
- 两种顺序各运行 Main 与 CRM 一项，共 4 项通过，零失败、零跳过。用例与构建可复现记录位于 `D:/CP6-test-artifacts/W563-01`，含两组原 TRX、触发顺序、期限和进程停止回执。两仓 build 均零错误。只修改测试夹具，公开产品源码、依赖和工作流 diff 为空；发布覆盖按已有 [Main W562](../W562-01/DELIVERY.md) 和 [CRM W557](../W557-01/DELIVERY.md) 的各自受测制品复用，本次实际联验程序集摘要单独记录。Actions 启动、重跑和取消均为零。 [Main 联验](../../../../CP6.Tests/Platform/IamInquiryServiceContextHttpTests.cs) · [CRM 联验](https://github.com/GTX537/CP6.CRM/blob/f8c9a453164a1af8f0ade70ed240ac86df63ca16/tests/CP6.CRM.IdentityPostgreSqlTests/Pgt017ServiceTenantPauseTests.cs)

正式 PostgreSQL **26 PASS / 1 NOT_RUN**，原询盘 **60/60**。Main 还有采购送审、收货、旧 OA、财务、独立 Space Design、Stock 与其他普通数据库写者。两库恢复对账、重新开放、实际传输和旧宿主排空按其原有边界待验；本轮通过范围为已登记的询盘宿主。两仓分支、工作区及席位处置见各自 PR、`main-finish.json` 和 `crm-finish.json`。 [正式矩阵](../../acceptance/postgresql-tenancy.json#L646) · [PGT-017 跟踪](https://github.com/GTX537/CP6/issues/1012)
