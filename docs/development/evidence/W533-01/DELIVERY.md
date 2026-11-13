# W533-01 · 旧单任务审批与 IAM 撤权

2026-11-13 UTC，Main 基线 `e8ca7b6246a814351dc98f22fec881bc6fa33103`，实测源码 `959cbc574f3c7f20ebbfd26b0ecbf86245ddc62d`。正式 PostgreSQL **26 PASS / 1 NOT_RUN**，原询盘 **60/60**。 [正式卡](../../acceptance/postgresql-tenancy.json#L646) · [Issue #1004](https://github.com/GTX537/CP6/issues/1004)

`POST /api/oa/tasks/{taskId}/decision` 的旧流程分支在原有 PostgreSQL 事务开始后获取共同认证根，随后以原签名会话重验 `oa-inbox:approve`。在根内重新读取实际任务及代理关系，持锁到现有业务、历史和通知记录一起提交。原并发重试重新进入同一授权边界，提交阶段的异常返回明确未知结果。保留旧审批表单字段和响应协议。新版 APP 及个人表单仍由各自 Owner 处理；其原结果读取继续采用原读取权限。 [写事务](../../../../CP6.Core/Services/Oa/TaskDecisionService.cs) · [宿主路由](../../../../CP6.WebApi/Controllers/Oa/TaskDecisionController.cs)

## 实际验证

独立 Main Kestrel、真实登录 Cookie、CSRF 和 PostgreSQL 18.6。模拟一条实际历史流程定义及在途任务，真实版本化 IAM 撤回同租户动作权限。通过不同数据库连接持锁和 `pg_blocking_pids` 观测请求先后。

- 审批先持共同根并等待任务行，IAM 等审批事务。审批 200，任务和流程状态正常推进，历史含真实 Actor，新增一条通知 Outbox。后续旧会话写入 403，已提交写集保持。
- IAM 先持共同根并等待授权表，审批连接在原生认证根等待。IAM 提交后审批及旧会话新写均 403，任务、流程、历史和通知原行逐字节相等。旧版同一竞争返回 200，另一个顺序原本已能提交。
- `baseline.trx` 1 通过、1 失败。首次编译夹具参数错误保留在 `build-baseline.log`。最终 `final.trx` 两项真实竞态及四项原组件回归均通过，涉及表单字段、旧 RowVersion 和重试。`native-http.trx` 新版 APP 审批实际登录/CSRF、版本和原结果恢复通过。合计 **7 通过、0 失败、0 跳过**。 [并发测试](../../../../CP6.Tests/Erp/Pgt017LegacyDecisionPostgreSqlTests.cs)
- `.NET 8.0.424` 构建零错误，原依赖警告保留。发布程序集与实际业务测试宿主一致，WebApi SHA-256 `85d13ccbe99669558e036bc6e65627ca701219dab8a428a8b7a38d7203f9e8ce`。本地制品在隔离实例启动，存活 200、匿名访问受保护路由 401。原始日志、TRX、发布时间和文件摘要在 `D:/CP6-test-artifacts/W533-01`，本任务所有宿主及数据库已停止。

一次集中审查核对签名会话、持锁读权限、实际写集、原重试及独立的新版 Owner 路径。旧批办、转交、退回、撤回和其他业务写者仍按各自提交单元处理。PGT-017 尚未核销全写者覆盖。GitHub Actions 启动与取消为零，本轮执行本地验证和本地发布检查。远端采用与席位以任务 PR 和 `D:/CP6-test-artifacts/W533-01/main-finish.json` 回读。 [剩余写者](../W531-01/DELIVERY.md#仍待核销的正式前置)
