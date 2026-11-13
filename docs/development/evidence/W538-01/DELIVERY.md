# W538-01 · 财务手工凭证提交栅栏

2026-11-13 UTC。Main 基线 `a0582b0d7e230ea1282c56f531b21cd75d2b69bf`，实测源码 `544f72f3349fc9e02860016f266be4393b938064`。正式 PostgreSQL **26 PASS / 1 NOT_RUN**，原询盘 **60/60**。 [原验收](../../acceptance/postgresql-tenancy.json#L646) · [Issue #1015](https://github.com/GTX537/CP6/issues/1015)

手工凭证创建、提交复核、过账、驳回、红冲共 5 个 HTTP 写入口采用现有 `CurrentWriteAuthority` 和请求 Core 事务。租户归属使用已认证租户；草稿头及各分录归属在入库前绑定该租户。会计期间、GL 采番、凭证头与分录的多次保存处于同一事务。保留原动作键、输入、制单与复核、金额精度和红冲语义。 [实际入口](../../../../CP6.WebApi/Controllers/Fin/JournalEntryController.cs) · [凭证服务](../../../../CP6.Core/Services/Fin/JournalEntryService.cs)

## 实际验证

隔离 PostgreSQL 18.6 和 Main Kestrel，模拟租户、真实签名会话和 IAM 角色策略撤权。由独立连接控制 `Fin_JournalEntry` 或 `Sys_RoleAction` 等待并查询 `pg_blocking_pids`。旧基线六例失败，撤权先持根时五个写入口仍成功，过账先阻塞时撤权先返回。修复后五个撤权先提交的请求都等在原生认证根，随后 403，凭证、分录、期间和采番逐行快照不变。过账先持根时 IAM 在根等待，合法过账提交后保留，旧会话再尝试得到 403。六例的 IAM 身份快照、代次、优先或普通 Outbox、审计与原回执核对一致。 [实际并发回归](../../../../CP6.Tests/Erp/Pgt017JournalPostgreSqlTests.cs)

重新授权后各原动作成功，金额 `17.23`、汇率 `1.123456` 及原红冲方向实测保持。草稿遇到重复 ID 时，已暂存的新会计期间和新月份计数也全部回滚。最终 **32 通过、0 失败、0 跳过**，含 15 项实际 PostgreSQL 与 17 项凭证组件验证。构建零错误，原警告保留。实际测试宿主与本地发布的 WebApi SHA-256 均为 `7da135c9388bc2c6998595a79138f455ca35a9f0f574cc59057f275d0d8ed8b7`，完整清单及原始证据在 `D:/CP6-test-artifacts/W538-01`。本地隔离发布存活 200，匿名访问凭证入口 401。宿主和 PostgreSQL 已停止。

OA 回调、自动过账、银行按行提交、会计期间状态与预算写者仍需按其实际业务事务核对。主线还需其他写者及 Main 到 CRM 的跨库协议，PGT-017 保持 NOT_RUN。OIDC 会话族 W537 独立已测试，PR 暂缓，源码保存在该任务远端分支。Actions 启动与取消为零。实际采用、分支及席位见本任务 PR 与 `D:/CP6-test-artifacts/W538-01/main-finish.json`。 [OA 财务回调](../../../../CP6.Core/Services/Fin/JournalApprovalCallback.cs) · [W537 独立 PR](https://github.com/GTX537/CP6/pull/1014) · [跨库 #1012](https://github.com/GTX537/CP6/issues/1012)
