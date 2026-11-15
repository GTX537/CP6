# W658-01 · CRM 角色撤权首次封存连接失败恢复

2026-11-15 UTC。Main 基线 `39923b3e5ce0f328591609e7af266c5c48aee1e9`，源码 `3d8500b93159fce224bfd58b7e7008b1acbe42ca`。隔离 PostgreSQL 18.6、实际 Main Kestrel 和签发、CRM W649 原生询盘宿主和原生导出 Owner。CRM 产品源码沿用 `8f5064db86bee2f0660fcb163a94d3496d7a39c2`，已由 `5f09533b9168ee614a6df1f75629b39635585f86` 采用。本轮使用模拟租户、同租户独立人类及服务角色、站点表单、配置和导出资料。 [PGT-017](https://github.com/GTX537/CP6/issues/1012) · [W649](https://github.com/GTX537/CP6/blob/6f6b75ebf836427a2b274e8a0238afe767a33969/docs/development/evidence/W649-01/DELIVERY.md)

## 产品范围

- `PUT /api/iam/roles/{id}` 角色启用状态变化、`PUT /api/iam/role-policies/{actions|scopes|fields}/{roleId}` 和 `PUT /api/iam/inquiry-scopes/{roleId}` 走原 IAM 根、角色/引用/目录 CAS、原受保护请求摘要和原回执。使用现有 C02 生成和原生业务验证决定是否需要实际封存，先回滚预备写集，再耐久保存原 actor、操作键、目标角色、StoreId、精确 C02 前驱和后继摘要。已登记站点表单撤权限定至被移除的来源对。
- Main 在根事务之外进行首次专用 CRM 连接。首次拒接维持未尝试状态，受影响的新写许可停止签发。需要回读 CRM 原件的路径保留经验证的原签名许可，省略新写 fence。保留当前合法读、独立角色和独立服务登记。已在先前合法持有 CRM 业务门的事务由原 CRM 封存锁决定顺序。
- 首次连接成功后再检查原 actor、角色/引用/目录 CAS 和绑定，先耐久记录可能封存，再在原 Main 根内用已打开连接完成有时限的精确封存。有效业务、C02、Outbox、审计和原回执在同一 Main 事务提交并清除待确认。已完成的原键读回原回执，已确认未尝试可由原 actor 明确关闭。可能已封存仍未知时保留意图，拒绝关闭、换键和假定无效果。确认已装配的 W649 租户暂停保持原表与协议。

## 实测

**48 项通过，Main 42、CRM 6，零失败、零跳过。**

| 实际用例 | 结果及证据 |
| --- | --- |
| 角色动作冷连接、持久状态和重启 | 实际 PG 专用登录拒绝 `28000`，原两库作用量未改。原角色的实际 TLS 许可停发写 fence。另一个同租户独立角色和独立发布登记继续签发。实际 Main 换 PID 后继续读取原意图，原 CAS/角色/目录一致，同键完成一次。原已签发的角色许可在实际 CRM 封存完成后无法生成新制品。 [Main 与 CRM TRX](D:/CP6-test-artifacts/W658-01/final-cold-role) |
| 角色启用状态、数据与字段策略、询盘范围冷连接 | 前三种真实原 HTTP 入口分别证明首次拒绝、对应待确认键及 C02 原前驱、当前新准入停止、未尝试可关闭且原封存不变。范围用例另验证真实换 PID、竞争 actor 403、改输入和另键 409、当前原件及同键完成一次。被保留的角色 71 站点表单对和独立角色 72 对被撤目标仍可获 fence。真实 BFF 已提交草稿按原键恢复 `201`、JSON、ETag，新操作 `503` 无写，原读 `200`，封存后受撤目标 `403`。 [Main 与 CRM TRX](D:/CP6-test-artifacts/W658-01/final-cold-config-fixed) |
| 两种正常提交顺序 | 角色和询盘范围分别覆盖 CRM Owner 先持门及 Main 封存先持门，独立读回原制品、任务、C02 原生代次、Outbox、审计、回执。角色封存先见冷角色恢复用例，顺序采用真实 CRM SQL 锁和已签发权限的控制夹具。 [角色业务先](D:/CP6-test-artifacts/W658-01/final-role-owner-first) · [范围封存先](D:/CP6-test-artifacts/W658-01/final-scope-seal-first) · [范围业务先](D:/CP6-test-artifacts/W658-01/final-scope-owner-first) |
| CRM 确认丢失 | 实际 PostgreSQL `SyncRep` 中断时已持久化 CRM 角色门槛，Main 原写集尚未提交。GET 为 `UNKNOWN`，Close 为 `503 IAM_OPERATION_UNKNOWN`。恢复同键时原门槛保持，CRM 技术 fence 行 revision 再加一。Main C02、业务、Outbox、审计和原回执各提交一次。原终态回读未再触碰 CRM revision。 [Main 与 CRM TRX](D:/CP6-test-artifacts/W658-01/final-role-crm-ack) |
| 定向回归 | 原角色、旧入口、版本化策略、询盘范围、校验、CAS、原回执、故障和事务回归 36/36。 [Main TRX](D:/CP6-test-artifacts/W658-01/main-regression/main.trx) |

真实基线在首次拒接后仍签发旧角色的新写 fence，行为断言按预期失败。早期候选在重启后重新登录的跟踪用例返回原 `412`。最终续用原有效签名会话并逐项核对角色、引用、目录 CAS 均相等。扩展夹具先选了数据范围键 `crm-lead`，字段模式改用已登记的 `crm-intake` 后通过。保留每次真实失败。 [基线 TRX](D:/CP6-test-artifacts/W658-01/red-role/main.trx) · [候选 412](D:/CP6-test-artifacts/W658-01/candidate-role/main.trx) · [字段夹具失败](D:/CP6-test-artifacts/W658-01/final-cold-config/main.trx) · [执行脚本](D:/CP6-test-artifacts/W658-01/run-role.ps1)

## 本地发布与交接

从干净源码 `3d8500b93159fce224bfd58b7e7008b1acbe42ca` 发布，Main WebApi、Core 与 PostgreSQL 迁移程序集均报告精确提交。310 个文件的清单 SHA-256 为 `ED3819548D98BEFE2334AE46FE10F59C231D359AA7C5E3D44FC827A78B09EC28`。独立 PostgreSQL 的 `DatabaseInit` 退出 0，W649 待确认暂停表与 W658 原认证门触发器存在。实际 `/health/live` 返回 200，匿名访问 IAM 角色编辑器返回 401。源码绑定发布和测试证据用于本地开发验收，环境推广独立登记。 [发布结果](D:/CP6-test-artifacts/W658-01/main-publish-smoke/result.json) · [文件哈希](D:/CP6-test-artifacts/W658-01/main-publish-smoke/publish-sha256.json)

范围限于上述已登记角色写者和实际 CRM 消费者。向用户/会话、部门、服务登记变更、显式有效期以及全局菜单扇出扩展仍各有独立权威。W659 共享 `CP6.Tests/Platform/IamInquiryPermissionPostgreSqlTests.cs`，由协调方串行采用及核对受影响原生用例。

`codex/w658-01` 保留一份本地源码提交，DEV-1 干净，任务进程和 57240–57259 监听为零。PR、远端采用与 DEV-1 释放交由协调方，当前席位仍登记给 W658。工作流树与基线同为 `3355235636fd45116f5445fca32fc65e49f13003`。Actions 触发、重跑、取消及部署均为零。

采用 Main `382328801d062080426c97b5f9fdf1d87ed957c2` 时，对比全部 17 个受测文件与实测源码无差异。W654 后续采用只涉及 Space 目录和相关回归，未修改本次 IAM/C02 或 CRM 受测输入。上面的实测与本地发布按原范围引用。
