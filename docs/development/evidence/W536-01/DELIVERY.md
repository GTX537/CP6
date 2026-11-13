# W536-01 · 仅访问令牌注销

2026-11-13 UTC，Main 基线 `05906d98dbf2b68484545cf94df0440607ff24fc`，实测源码 `a1725295743647ac177b05fbf6a5f2383ebc1790`。正式 PostgreSQL **26 PASS / 1 NOT_RUN**，原询盘 **60/60**。 [原验收](../../acceptance/postgresql-tenancy.json#L646) · [Issue #1010](https://github.com/GTX537/CP6/issues/1010)

Web 无刷新 Cookie、原生无刷新令牌时，原 HTTP 注销在 PostgreSQL 共同认证根内持久化当次签名会话的 access alias 撤销。仅首个修改提交同时写一条 Logout 审计。普通登录、受限 Web 登录和模拟会话仍按原认证入口判断，保留原刷新谱系和其他 alias。浏览器恢复、Main 认证和 Main 的 CRM 来源检查均消费撤销记录。Main 的 CRM 检查拒绝明确撤销的记录，保留历史未注册 alias 的原判定。携带刷新令牌的注销仍按其已验收行为先发布本地撤销信号，再持久撤销刷新谱系。 [精确撤销](../../../../CP6.Core/Services/Sys/AuthenticationInteractionStoreV1.cs) · [Web 和原生 HTTP 公用入口](../../../../CP6.WebApi/Services/AccessLogoutHttpV1.cs) · [Main 的 CRM 来源检查](../../../../CP6.WebApi/Services/CrmOidcDirectory.cs) · [单列前向迁移](../../../../CP6.Persistence.PostgreSql/Migrations/Core/20261113133000_AccessSessionRevocationV1.cs)

## 实际验证

隔离 PostgreSQL 18.6、Main Kestrel、模拟租户与真实 Web Cookie / 原生 Bearer。以不同连接持住计数行或访问签发记录，用 `pg_blocking_pids` 实测两条等待链。基线 5 项均失败。Web 和原生注销先到时旧入口在未取根的情况下返回；业务先到时旧注销没有等在共同根；CRM 扩展用例的持久撤销缺失。 [四条并发顺序](../../../../CP6.Tests/Erp/Pgt017AccessLogoutPostgreSqlTests.cs) · [CRM 实际签发与来源读取](../../../../CP6.Tests/Platform/IamInquiryPermissionPostgreSqlTests.cs)

| 提交顺序 | Web | 原生 |
| --- | --- | --- |
| 业务先 | 200，计数 1 → 2 | 200，计数 0 → 1 |
| 仅访问令牌注销先 | 401，计数 0 → 0 | 401，计数 1 → 1 |

四例核对等待前写集未变化，提交后当次签发标记与 Logout 审计同在，原令牌再次新写 401，原 refresh 可轮转，另一正常会话继续有效。Main 的实际 OIDC 用户读取拒绝该来源，另一租户的有效来源继续成功。旧有携带 refresh 的注销竞争回归仍通过。 `final.trx` 29 项实际 PostgreSQL 通过。独立 `upgrade.trx` 实际从前版已存发行证据升级，保留原 alias、证据与绑定，确认无待执行迁移且禁止逆向删除撤销记录，1 项通过。合计 **30 通过、0 失败、0 跳过**。

构建零错误，保留原警告。源码和 310 个发布文件摘要在 `D:/CP6-test-artifacts/W536-01`。WebApi `19eb8761088f3efb358e5624997c0a54571dd2abf0681a66b3b21ff7a68673f1`、Core `8554ebabb2e490941b4c3de010a526e1341aabbe0252c14c9043735ca93ae1a3`、迁移程序集 `154266f5fd723b2be6ab6fea07541a92c6de3176c1a0b7d38921291fd6a47093` 与本地实测程序集一致。隔离发布存活 200，匿名订单取号 401。宿主及 PostgreSQL 均已停止。

本窗覆盖仅访问令牌注销与普通业务写入。全量验收仍有携带 refresh 的即时缓存信号、OIDC 族锁序、其他 Main 写者和 Main 至 CRM 的跨库共同协议待核对。Main 发行证据与 CRM 本地事务的单独验证范围沿用 W535。Actions 启动与取消为零。实际采用、分支与席位处置见 PR 及 `D:/CP6-test-artifacts/W536-01/main-finish.json`。 [此前核对](../W535-01/DELIVERY.md) · [验收要求](../../../design-handbook/architecture/02-postgresql-tenancy.md#7-owner消息和撤权的共同提交边界)
