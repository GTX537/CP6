# W665-01 普通 Web 退出的 CRM 家族封存

Main 基线为 W664 正常合入的 `a3a4e3ca179cfc0430e5f65e4b5e7ed400ed12c1`，最终候选为 `9d96c80d5121ab3567184e4da767b47787a4cc3f`。测试使用 PostgreSQL 18.6、隔离的实际 Main Kestrel 和 W649 冻结 CRM 源码 `8f5064db86bee2f0660fcb163a94d3496d7a39c2`。业务数据为一个登记 CRM 租户、同一登录主体的两个独立 Web 家族和代登目标租户。权限、原 refresh 轮转、OIDC 授予和 CRM 本地写入均按真实产品入口验证。 [源与测试程序集](D:/CP6-test-artifacts/W665-01/tested-source.json)

## 源码

- `POST /api/auth/logout` 用真实签名 access alias、原 `session_evidence` 与所带 Web refresh 行确定同一租户、用户和浏览器家族。两个 Web completion close 继续用原 refresh Cookie + CSRF、原交互或浏览器能力确定目标。有效但不同家族的 Cookie 会被拒绝。未知 refresh Cookie 继续执行旧 raw 本地退出。
- 已受保护且关闭后没有独立存活 refresh 的家族，使用共同 Main 身份根保存原家族、原关闭方案、准确的 StoreId、授予 C02 前驱和后继。由 W661 已有 worker 及原能力恢复。持久接受后立即停签该家族新 CRM 写 fence 并拒绝新授权码创建。第一次真实封存连接在 Main 根外打开。最终交易重新核对原方案和 scope，把原本地关闭、授予撤销、C02 快照、优先 Outbox 以及原完成状态一并提交。
- 继续按有效主体记录 raw 审计。代登的原家族绑定到 `impersonator_session` 的原主人。raw 审计语句失败可回滚到保存点再提交原退出。Browser capability 的原关闭及必需审计一起提交。同一个能力已闭合时恢复 `CLOSED`，保持其原审计一次。raw 之后为此前未闭合的原能力执行的原审计单独保留。
- PostgreSQL 迁移 `20261115233000_CrmWebFamilyLogoutV1` 扩展 W661 同一张持久操作表。原 OIDC grant/client/nonce 与新的 Web 方案以表约束互斥。原 OIDC 收据、索引、表权限和一分钟浏览器续接交由既有路径。 [迁移](https://github.com/GTX537/CP6/blob/9d96c80d5121ab3567184e4da767b47787a4cc3f/CP6.Persistence.PostgreSql/Migrations/Core/20261115233000_CrmWebFamilyLogoutV1.cs)

## 实际验证

| 范围 | 观察 |
| --- | --- |
| 三条原入口 RED | 测试专属提交 `c6a4796074e86c2a36cee1d9d03755db406d723c`。三条实际 HTTP 退出均保有旧响应，原 Main 会话 401，独立同用户家族 200。两份退出前签出的原许可仍通过冻结 CRM 原生身份门，Main 的对应 grant、C02 和优先 Outbox 没有新写。三次预期失败各一。 [raw](D:/CP6-test-artifacts/W665-01/red-raw/main.trx) · [Cookie completion](D:/CP6-test-artifacts/W665-01/red-completion/main.trx) · [浏览器能力](D:/CP6-test-artifacts/W665-01/red-browser/main.trx) |
| 三个入口正常提交 | 各 1 PASS。旧响应与原本地审计数 1/0/1 保留。每次原家族两份 grant、C02 快照和优先 Outbox 各 2，原 CRM 门拒绝两份旧写许可，独立家族一直有效。 [raw](D:/CP6-test-artifacts/W665-01/candidate-warm-raw/main.trx) · [Cookie completion](D:/CP6-test-artifacts/W665-01/candidate-warm-completion/main.trx) · [浏览器能力](D:/CP6-test-artifacts/W665-01/candidate-warm-browser/main.trx) |
| 首连真拒绝、Main 重启、原能力恢复 | 三个入口各 1 PASS。专用 PostgreSQL login 实际返回 `28000`，HTTP 503 保留原持久行与原业务写集，原家族停签新效果，另一家族继续签发。重启前后 PID 不同，原指纹及计划不变。raw 的两个原续接入口在待确认阶段都返回 503，恢复专用 login 后实际 hosted worker 完成原家族。之后原 cookie completion 及真实浏览器能力返回 `CLOSED`。同一浏览器能力重复返回原响应，其审计只增加一次。 [raw 与 worker](D:/CP6-test-artifacts/W665-01/candidate-cold-raw/main.trx) · [Cookie completion](D:/CP6-test-artifacts/W665-01/candidate-cold-completion/main.trx) · [浏览器能力](D:/CP6-test-artifacts/W665-01/candidate-cold-browser/main.trx) |
| 真双库 COMMIT ACK 丢失 | raw 冷接收后经已知真实 capability 完成。第一轮 CRM 真 COMMIT 回包丢失，两个最低版本已生效，Main 原意图、授予与审计均未最终提交。精确重申遇 Main 真 COMMIT 回包丢失，Main 已持久完成原两授予/C02/Outbox 及 raw 审计，随后原能力准确关闭且没有重复授予撤销、快照或 Outbox。CRM 两次执行同一最低版本，技术 Revision 累计增加 4。 [双 ACK](D:/CP6-test-artifacts/W665-01/candidate-double-ack/main.trx) |
| PostgreSQL 原业务双序 | 真 Main raw 与真 CRM `intake:reject`。业务先保留已提交原制品，raw 等待 CRM 写事务。封存先使迟到原业务被拒绝。两个顺序 Main/CRM 各 1 PASS。 [业务先 Main](D:/CP6-test-artifacts/W665-01/candidate-order-owner-first/main.trx) · [业务先 CRM](D:/CP6-test-artifacts/W665-01/candidate-order-owner-first/crm.trx) · [封存先 Main](D:/CP6-test-artifacts/W665-01/candidate-order-pause-first/main.trx) · [封存先 CRM](D:/CP6-test-artifacts/W665-01/candidate-order-pause-first/crm.trx) |
| 原审计和无跨库效果的本地分支 | 独立触发器拒绝 raw 审计时仍完成原跨库退出。Browser 必需审计拒绝时保留待确认，Main 写集和 CRM 最低版本均未动，移除故障后按原能力完成一次。真实未交付的能力和含仍存活 refresh 的历史行样本继续走原本地支路。未知 refresh Cookie 的原 200/清 Cookie、本地 JTI 退役、CRM gate 保持已验证。代登仍关闭原主家族，原有效目标的 raw 审计一次。 [raw 审计](D:/CP6-test-artifacts/W665-01/candidate-raw-audit-fault/main.trx) · [Browser 审计](D:/CP6-test-artifacts/W665-01/final-browser-audit-fault/main.trx) · [存活 refresh](D:/CP6-test-artifacts/W665-01/final-browser-surviving-refresh/main.trx) · [未知 Cookie](D:/CP6-test-artifacts/W665-01/final-raw-unknown-cookie/main.trx) · [代登](D:/CP6-test-artifacts/W665-01/final-raw-impersonation/main.trx) |
| 现有连续性与 OIDC | 8 个原连续性场景 PASS，包括真实 CSRF、能力与原审计故障的历史语义。旧 OIDC `/connect/end-session` 的冻结 CRM 封存先链路 Main/CRM 各 1 PASS。 [连续性](D:/CP6-test-artifacts/W665-01/final-continuity/main.trx) · [OIDC Main](D:/CP6-test-artifacts/W665-01/final-oidc-regression/main.trx) · [OIDC CRM](D:/CP6-test-artifacts/W665-01/final-oidc-regression/crm.trx) |

集中验证共 26 PASS，零失败和零跳过。三条 RED 保留在独立基线。最终增量验证覆盖新的异常恢复与本地支路。正常双序及 ACK 由相同的封存和提交实现执行，最终审核增量没有改动其业务事务、CRM 封存或接收协议。 [最终编译](D:/CP6-test-artifacts/W665-01/build-final.log)

## 本地发布与交接

精确提交生成的 375 个发布文件已记录 SHA-256，清单为 `AA85A3CA5C51370B18D32CD392CCB3013E811841986F920ABE77B4CA89E015E2`。实际从制品执行 PostgreSQL DatabaseInit，并查到新 JSONB 列与原 OIDC 回执列。相同字节重新启动，`/health/live` 与 OIDC discovery 均为 200。注销缺 CSRF 为 403，提供匹配 CSRF 双提交但缺登录为 401。第一轮脚本把缺 CSRF 的正确 403 当作错值，原日志已保留。复核沿用相同制品和已迁移的同一隔离库，退出码 0。 [制品清单](D:/CP6-test-artifacts/W665-01/publish-smoke/package-sha256.json) · [初次结果](D:/CP6-test-artifacts/W665-01/publish-smoke/result.json) · [HTTP 复核](D:/CP6-test-artifacts/W665-01/publish-http-recheck/result.json)

`codex/w665-01` 保留在 Main DEV-1 供协调方采用，工作区干净。本任务宿主及验证进程已停止，57240–57259 全部无监听，桥接临时凭据已清除。席位由协调方在 PR 采用后释放。 [进程和端口](D:/CP6-test-artifacts/W665-01/process-verification.json)

验收范围为携已知 Web refresh 的普通退出及两个原 Web close。无 refresh 的单 access alias、全用户 refresh reuse、实际 Kafka/Dapr 投递和环境推广仍属各自独立工作。本窗口 GitHub Actions 与部署操作数均为 0。
