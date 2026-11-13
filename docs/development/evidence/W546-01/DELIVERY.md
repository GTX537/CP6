# W546-01 · Main 用户、会话撤权与 CRM 受理提交

2026-11-13 UTC。Main 基线 `aa69b485f59fbf8ab5e667aef45e5a01535a04e7`，实测差异 `50fae15d308a3a157cf69929d4d44ee5885581d5`。CRM 基线 `4490ec11043db2fef1500db5c818f47fddd821a3`，实测差异 `c8c6f0f5bdcba32e60a19283cc7559eb60afeedd`，远端采用 `a13588e8120f3af40cbc8023ad7924953e96b2e4`。 [CRM #213](https://github.com/GTX537/CP6.CRM/pull/213) · [正式 PGT-017](../../acceptance/postgresql-tenancy.json#L646)

Main 的原 C02 写者在提交用户或已有服务主体版本、浏览器 grant 撤销事实前，先在 CRM 的 C02 锁下持久封存相关来源。CRM 在原 Owner 事务中验证已持有的用户或服务来源版本、角色来源版本及浏览器已验证的 issuer/JTI。租户暂停和角色机制沿用 W540/W545。用户版本等待新权威快照，已撤浏览器 grant 仅匹配原会话。原回读及已提交技术工作沿用各自合同。 [CRM 向前采用](https://github.com/GTX537/CP6.CRM/blob/a13588e8120f3af40cbc8023ad7924953e96b2e4/docs/development/evidence/W546-01/DELIVERY.md)

## PostgreSQL 18.6 实测

使用模拟租户、用户、角色、会话和询盘。两套独立 PostgreSQL，实际 Main 登录/OIDC、版本化 IAM 用户禁用、OIDC end-session 和实际 CRM 拒绝受理。Main 权威的后续 C02 消息未投递给 CRM，直接核对已签发的迟到许可与原业务提交。

- `baseline-user-first` 和 `baseline-session-first` 各复现 Main 撤权已提交，CRM 仍完成拒绝受理。两个候选撤权先提交时都拒绝迟到授权，业务版本、历史、决定原件、审计和 Outbox 不变。
- 两个业务先持锁用例通过 `pg_blocking_pids` 证明 Main 等待。CRM 原拒绝受理、审计及 Outbox 提交保留。撤权后旧会话的新请求返回 401。核对用户、会话族、刷新令牌、grant、源版本、代次、优先 Outbox，及 IAM 审计与同键回执。四组两库共 8 项通过。
- 原角色撤权和配置封存后中断恢复共两组兼容通过。Main 定向 7 项、CRM 定向 8 项通过。实际服务登记变更接入同一来源核对，单仓原发布回归通过，其跨库竞争另验。

证据在 `D:/CP6-test-artifacts/W546-01`。Main 发布 310 个文件，WebApi `4ff16d6debcd022fdd0228ddf5c309b39bbcca25d17c118ccbb592487acfd114`、Core `a018d7b32585160d888da35e8db98175d13141f8a19e4de0f972c65dc7683c51`。CRM 发布 255 个文件，API `dac9791176dbb86157b87a61a8f866b9e6e21613aa292896cf2071d2348177a7`、Infrastructure `5d765a2934aaa0a83b2f862c503dec4bbf0242443706eb73f1dd9c0ca46a12b8`。均与实测程序集一致。首次并行冒烟在 HTTP 等待超时，使用相同发布制品延长探测后建库退出 0，Main 存活 200、匿名管理 401，CRM 存活 200、匿名配置 403。独占宿主与数据库已停止。Actions 启动、重跑和取消为零。

## 仍需验收

正式 PostgreSQL **26 PASS / 1 NOT_RUN**，原询盘 **60/60**。其他用户权限变更、实际服务登记撤销、部门、自然到期、直接 SQL、跨库确认丢失和恢复，以及 Main 余下独立业务事务继续核对。 [跨库余项 #1012](https://github.com/GTX537/CP6/issues/1012)

两仓分支和席位以远端 PR 和本地 `main-finish.json`、`crm-finish.json` 为准。
