# W667-01 CRM 精确 access 与真实 refresh 重用封存

Main 基线为 `39b52d7b906beb4ab191849af015906bd8827513`，测试专属提交为 `291effa15f8eeb3f734f45f12b402d451d38dce1`。实际 Main Kestrel 与两个隔离 PostgreSQL 18.6 库使用模拟租户和真实 Web Cookie、原生 Windows Bearer、原 OIDC 兑换及签名 `intake:reject` 许可。CRM 使用 W649 冻结源码 `8f5064db86bee2f0660fcb163a94d3496d7a39c2` 的受限原生只读身份门。 [测试源码](https://github.com/GTX537/CP6/blob/1303c7f486918019da7eb7830d6350aa3672703e/CP6.Tests/Platform/Pgt017AccessAndRefreshRevocationPostgreSqlTests.cs) · [基线源码与程序集](D:/CP6-test-artifacts/W667-01/baseline-source.json) · [本地 runner](D:/CP6-test-artifacts/W667-01/run-native-sources.ps1)

## 实际基线

每个场景用实际 Main OIDC 取得四个 CRM grant。两枚绑定同一个原 access JTI，一枚绑定同家族轮转后的不同 JTI，一枚绑定该用户的独立家族。原生会话没有相连的 CRM grant。Main 读回将每个 `CrmOidcGrant.SourceJti` 与签名 Web JWT 的 JTI 及原 `access_sessions` 相互核对。四项故障在新版预期的 CRM 原生判定处触发，六项原合同对照通过，零跳过。十次中封存专用登录均由直接 PostgreSQL 见证返回 `28000`，完整停机日志各仅一条见证拒绝，动作增加的封存连接数为零。

| 场景 | 真实观察 |
| --- | --- |
| 缺 refresh 的 Web raw 与在 native URL 使用真实 Web bearer | 两条均 HTTP 200，原 access JTI 撤销记录增加 1，当前三个 refresh 仍有效。同家族轮转后 JTI、独立族和无关联 native 的 access 仍可用。目标两枚原签许可的 CRM 判定仍为 true。四个 grant、C02 快照与优先 Outbox 无新写。Web 清原四 Cookie，native URL 返回原无 Cookie 响应。 [Web](D:/CP6-test-artifacts/W667-01/baseline-web-access/main.trx) · [native URL](D:/CP6-test-artifacts/W667-01/baseline-client-web-alias/main.trx) |
| Web 与 Windows 的实际已交付 refresh 重用 | 各先通过产品入口轮转并核对实际完成记录，再提交原 refresh。两条均 HTTP 400，原错误 `E-SEC-008`，未写 Cookie。主体活跃 refresh 3→0，分别拒绝 4 和 5 份原 session evidence，所有原 access 401。四项 CRM 许可在冻结原生判定仍可用，grant/C02/优先 Outbox 无新写。 [Web](D:/CP6-test-artifacts/W667-01/verified-web-reuse/main.trx) · [Windows](D:/CP6-test-artifacts/W667-01/verified-native-reuse/main.trx) |
| 仅本地退出 | 未知非空 Web Cookie 保留 200 及原清 Cookie，原 JTI 由旧本地路径失效，持久 alias 和四份 CRM 许可保持。无关联原生 access 退出保留 HTTP 200，仅撤销真实 native alias。正常 native 带 refresh 退出后再刷新返回 401，原 Web 会话与 CRM 许可仍有效。 [未知 Cookie](D:/CP6-test-artifacts/W667-01/baseline-unknown-cookie/main.trx) · [无关联 native access](D:/CP6-test-artifacts/W667-01/verified-native-access/main.trx) · [正常退出](D:/CP6-test-artifacts/W667-01/verified-ordinary-close/main.trx) |
| 原 refresh 的普通拒绝 | ClientKind 不符为 400/`E-SEC-024`。隔离 PostgreSQL 共同身份锁见证两个实际请求已等根，赢家完成，竞争输家 400/`E-SEC-007`，其余 refresh 保持。真实 native 轮转保留未送达状态时 400/`E-SEC-007`，执行原拒绝补偿后再次返回同效果，仅关闭该 native 输出。三个场景的四份 CRM 许可均保持。 [ClientKind](D:/CP6-test-artifacts/W667-01/verified-wrong-kind/main.trx) · [并发输家](D:/CP6-test-artifacts/W667-01/verified-simultaneous-loser/main.trx) · [未送达与拒绝](D:/CP6-test-artifacts/W667-01/verified-rejected-completion/main.trx) |

初轮夹具在动作前遇到 Windows 活动 PostgreSQL 日志共享限制，已移至已验证停机之后统计日志。随后按旧 JSON envelope 核对数值状态和本地化错误键。原尝试均已保留。 [首轮日志](D:/CP6-test-artifacts/W667-01/red-web-reuse/main.trx) · [原 envelope 断言日志](D:/CP6-test-artifacts/W667-01/baseline-web-reuse/main.trx) · [最终编译](D:/CP6-test-artifacts/W667-01/build-checkpoint.log)

## 产品改动

最终 Main 候选为 `1303c7f486918019da7eb7830d6350aa3672703e`，基线为已采用的 `9f8e964ea8766506e36d9749a27d65f7605c5513`。范围为 13 个产品路径及一个测试源。现行源与编译产物摘要均已读回。 [源码和程序集摘要](D:/CP6-test-artifacts/W667-01/integrated-source.json)

- 单 access 退出在共同 Main 根下从已认证的签名 JTI 查回原 Web refresh 哈希，再以租户、主体和 `CrmOidcGrant.SourceJti` 固定相关 grant。缺 refresh 的 Web 与 native URL、以及有 grant 的未知 Cookie 都复用原 native access 许可撤销。其他 JTI、同族后续 refresh 和独立家族保持可用。未知非空 Web Cookie 仅在原 JTI 关联 grant 时进入同一封存，保留原清 Cookie 响应及尽力写审计。无关联 Web 与 native 继续走原本地合同。
- 已送达 refresh 的真实重用只在 Web 与 native 共用的 `RotateAuthenticatedAsync` 原风险分支接受。持久原证据为已验证原 refresh 与已送达后继哈希、租户和主体。该用户原 scope 的新 CRM grant 创建和写 fence 在待确认期间停止。Main 最终交易仍退役该主体的全部 refresh 和原 native session evidence。另一主体不受此 scope 影响。
- 前向迁移为两种 scope 保存精确原受理记录。受理记录保存源哈希和确定的 C02 计划。真拒接用例核对了原 JWT、Cookie、refresh 明文和密码均没有进入持久记录。专用 CRM 封存连接在 Main 根外首次打开。旧 W661 worker 发现原记录后依同一 scoped 计划恢复。CRM 封存确认后，同一 Main 交易写原本地退出、精确授予撤销、C02、优先 Outbox 和原完成状态。Main 真 COMMIT 确认丢失时，已受理的请求读回自己的完整原记录并恢复原 HTTP 结果。

## 候选的实际验证

| 场景 | 观察 |
| --- | --- |
| 有效来源与真重用 | 只退原 JTI 的两枚许可，保持同族 R1 的另一 JTI 和独立族。Web 及 Windows 已送达后的原 refresh 重放保持 400／`E-SEC-008`，主体四枚许可退役。未知非空 Cookie 的关联来源同样封存两枚，返回原 200 与四个清除 Cookie。 [Web JTI](D:/CP6-test-artifacts/W667-01/green-web-access/main.trx) · [Web reuse](D:/CP6-test-artifacts/W667-01/green-web-reuse/main.trx) · [Windows reuse](D:/CP6-test-artifacts/W667-01/green-native-reuse/main.trx) · [未知 Cookie](D:/CP6-test-artifacts/W667-01/green-unknown-cookie/main.trx) |
| 两种 scope 各自的实际 PostgreSQL 写入两序 | 真实冻结 CRM `intake:reject` 业务先提交原制品，Main 等 CRM 原生锁，随后封存。封存先时冻结 CRM 拒绝迟到的原许可，原业务写集保持。两种 scope 两序的 Main 与 CRM 共 8 PASS。 [JTI 业务先 Main](D:/CP6-test-artifacts/W667-01/order-access-owner-first/main.trx) · [CRM](D:/CP6-test-artifacts/W667-01/order-access-owner-first/crm.trx) · [JTI 封存先 Main](D:/CP6-test-artifacts/W667-01/order-access-pause-first/main.trx) · [CRM](D:/CP6-test-artifacts/W667-01/order-access-pause-first/crm.trx) · [reuse 业务先 Main](D:/CP6-test-artifacts/W667-01/order-reuse-owner-first/main.trx) · [CRM](D:/CP6-test-artifacts/W667-01/order-reuse-owner-first/crm.trx) · [reuse 封存先 Main](D:/CP6-test-artifacts/W667-01/order-reuse-pause-first/main.trx) · [CRM](D:/CP6-test-artifacts/W667-01/order-reuse-pause-first/crm.trx) |
| 首次实际拒接及重启 | 仅拒绝受控 CRM seal login，实际 PostgreSQL `28000`。原请求 503，Main 原写集保持，原精确记录留存。原 scope 的 Main signer 保留权限证据并停发新写 fence，停止原来源的新授权码。JTI 模式同族后续 JTI 继续签发，两个模式的同租户另一主体继续签发。重启前后 PID 不同。恢复专用登录后由 Main 实际 hosted worker 完成。Native scope 用另一条已实际交付的同主体 Web 复用竞争，原 native 计划保持。 [JTI worker](D:/CP6-test-artifacts/W667-01/worker-web-access/main.trx) · [native worker](D:/CP6-test-artifacts/W667-01/final-worker-native-reuse/main.trx) |
| 两种 scope 的真双库 COMMIT ACK | CRM 真 COMMIT 后只丢回包。原最低版本已生效，Main 原计划与业务写集保持，返回 503。精确重申后 Main 真 COMMIT 回包丢失，已受理请求读回原记录，恢复 Web alias 的 200 或 native 真复用的 400／`E-SEC-008`。原 grant、快照、优先 Outbox 各一次。接收端技术 Revision 实测累计增加 4 和 8。 [Native URL 的 Web alias](D:/CP6-test-artifacts/W667-01/ack-client-web-alias/main.trx) · [Windows reuse](D:/CP6-test-artifacts/W667-01/final-ack-native-reuse/main.trx) |
| 审计与兼容 | 两个在共同根等待的真实同 JTI Web 退出均保留原 200，一份原审计和持久结果。未知 Cookie 关联真实 grant 时，独立 PostgreSQL 审计触发器拒绝写入，仍完成原封存和清 Cookie。未知无关 Cookie、无关联 native access、错 ClientKind、实际并发输家、未送达与拒绝、正常退出对照继续通过。 [并发同 JTI](D:/CP6-test-artifacts/W667-01/final-web-access-duplicate/main.trx) · [审计故障](D:/CP6-test-artifacts/W667-01/final-unknown-cookie-audit/main.trx) · [未知无关联 Cookie](D:/CP6-test-artifacts/W667-01/final-unknown-cookie-local/main.trx) |
| 采用后的 Main 相关回归 | W536 四项真实 Main 业务与仅 access 退出双序，加上原迁移已存数据和 OIDC bridge 合同，合计 34 PASS。 [回归 TRX](D:/CP6-test-artifacts/W667-01/final-regressions/main.trx) |

初次产品 warm 在新 SQL 条件拼接缺行分隔时返回 503，实际 PostgreSQL 错误已记录。修正后重新跑通业务。四组物理顺序和 JTI ACK 使用最终候选同字节的封存、grant 与 C02 路径，后续小改动仅检查 Main 事务洁净及已接受的同 JTI 并发回读。 [初次 warm](D:/CP6-test-artifacts/W667-01/warm-web-access/main.trx) · [整合源码差异](D:/CP6-test-artifacts/W667-01/integrated-source.json)

## 发布及交接

候选共 58 PASS，零失败与零跳过。4 项原故障与 6 项基线对照另存。 [最终编译](D:/CP6-test-artifacts/W667-01/build-source-exact.log) · [范围与进程核对](D:/CP6-test-artifacts/W667-01/process-verification.json)

独立 W667 本地制品包含 310 个文件，清单 SHA-256 为 `3C477BF7F7F6A46EBD0180711C32D5845573887AAA108D9725D3D0E28E06F052`。WebApi、Core 和 PostgreSQL 迁移程序集与最终实测字节相同。实际 DatabaseInit 正常完成，核验新表、三索引和原 Web 家族方案列。实际存活 HTTP 200，两条匿名 logout HTTP 401。 [本地制品清单](D:/CP6-test-artifacts/W667-01/main-publish/package-sha256.json) · [制品烟测](D:/CP6-test-artifacts/W667-01/main-publish/result.json)

`codex/w667-01` 保留于 Main DEV-1 供协调方采用，工作区干净。所有自有进程已停，57240–57259 无监听，临时桥接凭据已清除。席位待采用后由协调方释放。验收覆盖两个本机 PostgreSQL 和实际 HTTP，环境推广属于独立窗口。本任务 GitHub Actions 与部署执行均为 0。 [工作区状态](D:/CP6-test-artifacts/W667-01/process-verification.json)
