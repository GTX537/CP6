# W516-01 · 正式 PGT-026 认证目录与共享公开路由

采用 [PGT-026](../../acceptance/postgresql-tenancy.json) 的当前宿主范围。CRM #206 采用测试源 `615744ebf71184885ff9649563722c521a1cacb1`，远端 `eb35232c1ae819efaacac318781c78e4cf9d0d2b` 与验证树一致。Main 实际验证入口源 `45f79d41a57c5f44f94c9da64a4ff0d1019741b2`。使用模拟租户、用户和表单资料，运行真实 Kestrel、.NET 8 和 PostgreSQL 18.6。 [CRM #206](https://github.com/GTX537/CP6.CRM/pull/206)

- CRM 原生宿主使用一份已登记的 `PublicForms` 共享目录。实际签名匿名 GET 使用同一宿主和普通 PostgreSQL 角色。A/B 两个已发布路径返回各自的租户、站点、表单及当前配置，源表单版本分别 11、22，每次实际只打开一条业务连接。A/B/C 签发数由 `[0,0,0]` 经 `[1,0,0]` 到 `[1,1,0]`。真实 C 的表单在 PostgreSQL 已激活。C 未登记公开路由，和两条交叉路径及未知路径均返回同形 `404 crm.form_unavailable`，无 Cookie、目录回显或业务库连接，四次查询均未签发。
- Main W511 的实际同名租户与用户经受控 OIDC 目录获取各自令牌，未知 slug 或 client 返回安全 404，A 凭据不能授权 B。实际数据库验证原授权、access session 与全局 refresh 键相符。普通角色读取租户目录、用户目录及密钥列分别复用 W368/W427 的真实 `42501`。 [同名认证与全局凭据](../W511-01/DELIVERY.md) · [普通角色目录与密钥](../W427-01/DELIVERY.md)
- Main W515 实测同库 RMA 启动切换，从共享空布局查询进入已启用的租户 schema。真实登出持久撤销原令牌，独立复核其签名和有效期，切换后按原撤销状态返回 401，普通租户连接和 RMA SQL 均为零。另一份当前令牌以普通角色在 `rma-schema-activation-v2` 读取该租户 schema。正式证据采用现有启动切换，既有历史迁移按独立任务执行。 [切换的原生实证](../W515-01/DELIVERY.md)
- W515 原测试用例和现有读取用例使用同一 opt-in 夹具，原宽筛选实际将两者同时选中。分开两个入口及数据库夹具，默认精确运行原读取和 EF 模型，`--schema-activation` 精确运行切换用例并核真实 SQL。两个入口分别在实际 PostgreSQL 上运行。 [Main #983](https://github.com/GTX537/CP6/issues/983) · [可复现入口](../../../../eng/wp02-02/rma-tenant-migration/verify_read.py)

CRM `public-route-01.trx` 3 通过、0 跳过，含共享路由新证据及原发布/签发控制。实际路由记录位于 `D:/CP6-test-artifacts/W516-01`。Main 原入口 `rma-read.trx` 2 通过、0 跳过，原生普通查询验证通过。独立 `--schema-activation` 入口 `rma-schema-activation.trx` 1 通过、0 跳过，真实共享查询、旧令牌零普通访问与当前令牌租户查询均通过。两套独立数据库的原始结果在 `D:/CP6-test-artifacts/W516-01/main-rma`。 CRM 与 Main 均复用相同产品输入，仅重编受影响测试。自有 PG、连接探针及宿主均已停止。集中审查覆盖受控目录、普通角色、实际绑定、未登记路由和原撤销状态。

[存储设计](../../../design-handbook/architecture/02-postgresql-tenancy.md#3-数据分类与物理边界) 将 PublicRoute 或宿主公共路由目录定义为共享控制数据。本次实际采用当前宿主目录和 PostgreSQL 表单绑定，Main 使用受控身份权威和真实租户 schema。正式 PostgreSQL 由 20 PASS / 7 NOT_RUN 更新为 21 PASS / 6 NOT_RUN。原询盘业务验收 60/60。完整 WP02-02/WP02-06 其他消费者与迁移继续各自任务。CRM 本地及远端任务分支已清理，DEV-2 席位已释放。Actions 触发和取消为零，部署为零。
