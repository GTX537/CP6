# W668-01 2FA 凭据原件与 CRM 写许可

## 源码与范围

DEV-2 领取基线为 `9f8e964ea8766506e36d9749a27d65f7605c5513`，候选夹具独立 checkpoint 为 `1115ce892009809412a9f7494ee8e8bb069a60d7`。串行接入 W667 #1189 Main `479f4e9b975b049d81e4ff3b27ade232900c60e2` 后，业务候选收敛到 `5cfdd940e496e625155d4437eb3b8015b21469d3`。最终提交 `31951c98418d211834dacf0c3e13c2eb2a2029e9` 追加一条专属夹具，产品文件与业务候选逐字节相同。

覆盖自助 setup、enroll、disable 三个入口，以及 Web 和原生两对 setup/enroll。自助凭据沿用 W666 的 Main 授权根、每用户唯一待确认记录、物理 Store 和精确 C02 推进。待确认记录绑定原准备 revision 和已接受的因子请求。验证码的验证材料只封存在本地受保护原件，原验证码或新密钥没有写进计划。邮件码在初次合法接受时消费，恢复同一请求用原证明。W667 的审计 savepoint 只包实际已完成的审计，凭据保存发生在它之前。保留原 enroll 一条审计和 disable 两条不同来源的审计。

Web/原生原挑战保留绑定、过期、准备、lease 及原 TOTP 消费证据。最终入会、原会话、审计、C02、回执和待确认清理共用 Main 实际事务。没有 C02 推进的准备仍可正常执行。原 challenge 状态能关闭已证实未开始封接的原件。远端已开始而无法确认时状态为 `COMPLETION_UNKNOWN`，原挑战的过期仍适用。 [候选代码](https://github.com/GTX537/CP6/blob/31951c98418d211834dacf0c3e13c2eb2a2029e9/CP6.WebApi/Services/AuthenticationEntryServiceV1.CrmTwoFactor.cs) · [原状态与关闭](https://github.com/GTX537/CP6/blob/31951c98418d211834dacf0c3e13c2eb2a2029e9/CP6.Core/Services/Sys/AuthenticationCompletionRecoveryV1.cs)

## 实际候选验证

本轮使用真实 Main Kestrel、两套隔离 PostgreSQL 18.6、实际登录及 C02 控制，业务身份和邮件接收端为模拟。候选运行器先核对源码 SHA、夹具 blob 和测试程序集哈希。旧验证任务的自动安全审查记录保留，以下验收使用已安装的修复候选及合法当前授权。 [候选运行器](D:/CP6-test-artifacts/W668-01/run-2fa.ps1)

| 路径 | 观察 | 实际证据 |
| --- | --- | --- |
| 自助 setup | 专用 CRM 登录实际拒绝一次，503。原 Main/C02/审计等值，独立签发正常。同原件恢复 200，准备 revision 推进，原凭据 revision 和 epoch 保持，返回与原凭据字段相符，只有 token floor 为 1。 | [原件恢复](D:/CP6-test-artifacts/W668-01/candidate-self-setup-recovery/coordination/two-factor-recovered.json) |
| 自助 enroll | 503 后同原件 200，用户和 token floors 为 `[5,1]`，新增两个 Outbox 项和一条原入会审计。 | [原件恢复](D:/CP6-test-artifacts/W668-01/candidate-self-enroll-recovery/coordination/two-factor-recovered.json) |
| 自助 disable，TOTP 与邮件 | 实际各一次依赖拒绝后同原件 200，各一原回执、两条原来源审计。邮件通过隔离真实 SMTP 接收一个模拟验证码，恢复使用已接受证据。 | [TOTP](D:/CP6-test-artifacts/W668-01/candidate-self-totp-recovery/coordination/two-factor-recovered.json) · [邮件](D:/CP6-test-artifacts/W668-01/candidate-self-email-recovery/coordination/two-factor-recovered.json) |
| Web 与原生 setup | 两条真实合法原挑战各返回 200，更新准备。当前无受影响 grant 且用户 C02 投影相同，实际 CRM 连接尝试 0，消费 0，会话 0。 | [Web](D:/CP6-test-artifacts/W668-01/candidate-web-setup-no-effect/coordination/two-factor-candidate.json) · [原生](D:/CP6-test-artifacts/W668-01/candidate-native-setup-no-effect/coordination/two-factor-candidate.json) |
| Web setup 与先前登记的 grant | 目标在可选模式正式取得原 grant，独立管理员实际切换强制策略后，原 grant 仅独立读库。新合法原挑战遇实际拒绝一次，503、原写集相等、独立新登录签发正常。连接恢复后同挑战 200，原 grant 标记撤销，只有 token floor 为 1，增加一个 Outbox 项，会话和因子消费均为 0。 | [拒接](D:/CP6-test-artifacts/W668-01/final-web-setup-original-grant/coordination/two-factor-candidate.json) · [恢复](D:/CP6-test-artifacts/W668-01/final-web-setup-original-grant/coordination/two-factor-recovered.json) |
| Web enroll | 专用 CRM 登录拒接一次，503，原 status 为 `COMPLETION_UNKNOWN`，消费 1、会话 0。恢复原挑战后返回 200，原 lease 所属完成记录只有一个会话。另一条原挑战关闭后消费仍 1、会话 0。 | [恢复](D:/CP6-test-artifacts/W668-01/final-web-enroll-recovery/coordination/two-factor-recovered.json) · [关闭](D:/CP6-test-artifacts/W668-01/final-web-enroll-close/coordination/two-factor-closed.json) |
| 原生 enroll | 真拒接后 503，原 status 为 `COMPLETION_UNKNOWN`，同一 lease，消费 1，会话 0。恢复原件后完成且实际会话 1。另一个合法原件执行 close 后消费保持 1、会话 0、原件清除。 | [拒接](D:/CP6-test-artifacts/W668-01/candidate-native-enroll-recovery/coordination/two-factor-candidate.json) · [恢复](D:/CP6-test-artifacts/W668-01/candidate-native-enroll-recovery/coordination/two-factor-recovered.json) · [关闭](D:/CP6-test-artifacts/W668-01/candidate-native-enroll-close/coordination/two-factor-closed.json) |
| Main 两序 | 真实 `setup-self` 与原有签名 `PUT /api/sys/two-factor-policy`。独立连接观察真实用户或租户 UPDATE、第一个 writer 及第二个等待相同 Main 根。策略先为 401 且凭据及 CRM floors 等值。凭据先原操作 200、回执落库，独立策略随后提交。 | [策略先等待](D:/CP6-test-artifacts/W668-01/candidate-main-authority-first/coordination/two-factor-main-wait.json) · [策略先效果](D:/CP6-test-artifacts/W668-01/candidate-main-authority-first/coordination/two-factor-main-authority-first.json) · [凭据先等待](D:/CP6-test-artifacts/W668-01/candidate-main-credential-first/coordination/two-factor-main-wait.json) · [凭据先回执](D:/CP6-test-artifacts/W668-01/candidate-main-credential-first/coordination/two-factor-recovered.json) |

十三个原生候选用例及十六个原 2FA 路由组件用例均通过，零跳过。原 Web 候选在状态与一次性证据都正确后，恢复请求因 Web 控制器只读 pending 返回 400。后续修改只让 Web enroll 按原签名、身份和挑战命中已持久接受的 `completing` 记录。 [组件 TRX](D:/CP6-test-artifacts/W668-01/candidate-related-unit/related.trx) · [首轮 Web 候选 TRX](D:/CP6-test-artifacts/W668-01/candidate-web-enroll-recovery/main.trx)

两种 Main 提交顺序为本任务直接执行。跨库商业 Owner 顺序采用 W666 #1187 与 W667 #1189 的同一物理事务证据。W668 自助和共享登录路径均调用原 `CrmInquiryTenantPauseV1.AdvanceConnectedIdentitiesAsync` 到原注册 Store。该实现相对 W666 逐字节一致。W667 的 token-only 路径也调用该函数，事务获取物理 CRM 版本行、原生 COMMIT 后才返回。W668 直接验证 token-only 自助准备、用户与 token 启停及用户-only 登录入会的实际批次与失败恢复。 [W666 交付](https://github.com/GTX537/CP6/blob/31951c98418d211834dacf0c3e13c2eb2a2029e9/docs/development/evidence/W666-01/DELIVERY.md) · [W667 交付](https://github.com/GTX537/CP6/blob/31951c98418d211834dacf0c3e13c2eb2a2029e9/docs/development/evidence/W667-01/DELIVERY.md) · [共同原生 CRM 调用](https://github.com/GTX537/CP6/blob/31951c98418d211834dacf0c3e13c2eb2a2029e9/CP6.Core/Services/Platform/CrmInquiryTenantPauseV1.cs#L170)

## 本地发布与工作区

最终源码 `31951c98418d211834dacf0c3e13c2eb2a2029e9` 的本地发布包包含 310 项，清单 SHA-256 为 `6F4F124308F0056C601A359D36E8BB247D204E4919959D786141E2EC160E8775`。发布制品实际初始化 PostgreSQL 18.6，确认 W667 和 W668 的迁移对象。发布 Main Kestrel 健康 200，三个匿名自助改动各 401。 [运行结果](D:/CP6-test-artifacts/W668-01/publish/result.json) · [逐文件核对](D:/CP6-test-artifacts/W668-01/publish/package-sha256.json)

所有任务进程已停止，57260–57279 无剩余监听。分支 `codex/w668-01` 保留，DEV-2 干净，席位等待协调方完成远端采用及释放。 [发布停机](D:/CP6-test-artifacts/W668-01/publish/pg-stop.log)
