# W545-01 · Main 角色撤权与 CRM 导出提交

2026-11-13 UTC。Main 基线 `477041d69d3591db212d5769a98be869a643d7af`，实测差异 `a205a0fc8312f8f178d7033db6a8f5cccba61e86`。CRM 基线 `a26a851352dcbc665768de64cae5202764baa7f3`，实测差异 `91f088f58420c44eca3f5bbe36486518bab144c8`，远端采用 `4490ec11043db2fef1500db5c818f47fddd821a3`。 [CRM #212](https://github.com/GTX537/CP6.CRM/pull/212) · [正式 PGT-017](../../acceptance/postgresql-tenancy.json#L646)

Main 在原权限事务提交已存在的 C02 角色快照前，先取得 CRM 原 C02 锁，持久化该角色的最低版本。CRM 配置、人类和服务授权的新效果在各自原事务中核对已持有的角色快照版本。CRM 同事务更新身份行，保证 Serializable 等锁后的旧快照不能通过。无关角色沿用各自权限。历史读取、原结果回放、公共表单签发仍沿用原合同。采用时需两仓配套、前向安装和补齐限定函数权限。 [CRM 采用步骤](https://github.com/GTX537/CP6.CRM/blob/4490ec11043db2fef1500db5c818f47fddd821a3/docs/development/evidence/W545-01/DELIVERY.md)

## PostgreSQL 18.6 实测

两套隔离 PostgreSQL、Main 实际登录、版本化 IAM 和 V2 TLS 权限、CRM 实际隐私导出 Owner。租户、角色、询盘和制品使用模拟数据，导出请求准备沿用 W544 夹具。联验期间没有将 Main 新身份事实投递给 CRM，直接检查先签发的授权和本地封存。

- `baseline-role-first` 核对 Main 实际撤销 `crm-privacy:export` 后，CRM 仍用旧授权生成制品。
- `candidate-role-first` 核对撤权先提交时 CRM 拒绝旧授权。待处理任务、制品、审计、历史和 Outbox 均未变化。
- `candidate-owner-first` 通过 `pg_blocking_pids` 确认 Main 在 CRM 等待，CRM 原事务先提交导出制品与审计。Main 撤权后保留原业务结果。
- 两种顺序都核对 Main 租户仍启用、角色权限版本、身份代次和角色快照各自推进、同版本 Outbox、审计、操作回执和同键重试。4 项跨库通过。原暂停公开提交、人工受理和配置中断恢复三组兼容共 6 项通过。Main 定向 14 项及 CRM 定向 8 项通过。
- CRM PostgreSQL 门槛验证核对相同版本重试、禁止倒退、拒绝错存储和普通运行角色、无关角色仍可通过以及目标角色到达最低版本后通过。后者为数据库门槛，实际 C02 角色恢复和权限重授另验。

本地证据在 `D:/CP6-test-artifacts/W545-01`。Main 发布 310 个文件，WebApi SHA-256 `c55be058f489f7e58f780600b31694a8756a415dff44889336480081f1b11437`，Core `91f5abf4e23674438cefc24b9cc02948346e6545290078fe7c01b613cbd988a7`，建库退出 0、存活 200、匿名管理 401。CRM 发布 255 个文件，API `cc049481d28e32d75c6fecb832e39bda5aa83bbe76e163850b98497224e79ac8`，Infrastructure `805effb018e847036f84232f7fc4bfb52fb0f6c2e555a753b3e23e3c029667dc`，存活 200、匿名配置 403。均匹配受测程序集。宿主和数据库已停止。Actions 启动、重跑和取消为零。

## 仍需验收

正式 PostgreSQL **26 PASS / 1 NOT_RUN**，原询盘 **60/60**。此次两库撤权实测覆盖角色的实际导出权限变更，其他角色写法、用户、会话、部门、服务主体、租户自然到期和直接 SQL 仍需按各自入口验收。跨库提交确认丢失、不可达、恢复实例及其他 Main 原生业务事务仍需核对。CRM 封存成功后 Main 若中断，受影响角色的新效果等待 Main 权限写入及快照发布，当前记录验证了底层门槛与幂等。 [跨库余项 #1012](https://github.com/GTX537/CP6/issues/1012)

两仓分支和席位处置见远端 PR 和本地 `main-finish.json`、`crm-finish.json`。
