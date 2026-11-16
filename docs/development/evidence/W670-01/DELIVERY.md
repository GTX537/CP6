# W670-01 · 已批准身份资料处置的 CRM 事务栅栏

Main 已采用基线为 W668 的 a831e1df97c6052dc5dc2ba435fa4f1f67ec900f。本地候选为 9fec8a9095135d1b2fcfc6159d55d2a509e2df21。专属测试检查点 c0ed466b7ead3a2420b85ba486589462f57ac671。 [#1012](https://github.com/GTX537/CP6/issues/1012) · [原 W314 处置](../W314-01/DELIVERY.md)

## 产品变更

- 原 POST /api/platform/privacy-dispositions-v1/plans/{id}/execute 在 Main 当前授权根内核对执行人、独立审批、政策与保留状态和准确版本。已登记 CRM Store 的处置在首次连接前持久接受原操作。私有记录绑定公开操作、原输入摘要、目标用户与 Store、准确 C02 后继、稳定认证 epoch 与 PostgreSQL 时间。受影响用户在待确认期间停止取得新 CRM 写许可。
- 首次 CRM 连接在 Main 根外建立。Main 先持久记录开始封存，再以原用户及其当前 grant 的准确版本提交已经连接的 CRM 原生批次。Main 处置、会话撤销、C02、Outbox、墓碑、审计及原操作回执在同一本地事务确认。原读权可查询已提交原件，尚未开始封存的原件可以关闭，已有封存开始记录的原件保留待确认。
- 前向迁移 20261116013000_PrivacyDispositionCrmSealIntentV1 仅扩展待确认表的 kind。现有 completion_id 唯一约束和对应挑战类型检查保留。W315 离线重施调用原接口，继续使用原本地时间合同。 [处置事务](https://github.com/GTX537/CP6/blob/9fec8a9095135d1b2fcfc6159d55d2a509e2df21/CP6.Core/Services/Platform/PrivacyDispositionOwnerV1.CrmSeal.cs) · [前向迁移](https://github.com/GTX537/CP6/blob/9fec8a9095135d1b2fcfc6159d55d2a509e2df21/CP6.Persistence.PostgreSql/Migrations/Core/20261116013000_PrivacyDispositionCrmSealIntentV1.cs)

## 实际 PostgreSQL 验收

使用隔离 PostgreSQL 18.6、真实签名 Main Kestrel 和已采用 W649 的冻结 CRM 宿主。账户、组织、批准计划和询盘均为模拟数据。顺序夹具只控制原请求到达时点。实际受理、停发许可和封存由产品执行。

| 场景 | 实际结果 | 证据 |
|---|---|---|
| 首次专用 CRM 登录拒接 | PostgreSQL 返回 28000，产品 HTTP 503。原批准与 Main 用户、会话、C02、Outbox 和回执保留，CRM 最低版本原值。未发原请求关闭后按同键返回 CLOSED。另一个已接受原请求跨实际 Main PID 保持待确认，两个目标 grant 停发写 fence，合法 Main/CRM 签名读与独立用户保持。变更确认同键 409，恢复后原确认一次应用。 | [初始候选 TRX](D:/CP6-test-artifacts/W670-01/native-cold-1/main.trx) · [写集](D:/CP6-test-artifacts/W670-01/native-cold-1/coordination/privacy-cold.json) |
| CRM 受理先提交 | 原生 Main 封存等待者 PID 21424。已获 CRM 门的受理决定和一条对应原 Outbox 保留，处置随后提交。Main 与 CRM 各 1/1。 | [结果](D:/CP6-test-artifacts/W670-01/native-owner-first-1/result.json) · [Main 锁与写集](D:/CP6-test-artifacts/W670-01/native-owner-first-1/permission-consumption-main/coordination/privacy-order.json) · [CRM TRX](D:/CP6-test-artifacts/W670-01/native-owner-first-1/crm.trx) |
| Main 处置先提交 | 已取得旧签名且尚未进入最终门的 CRM 受理在真实最终写入点返回 403，CRM 业务和 Outbox 写集保持。Main 与 CRM 各 1/1。 | [结果](D:/CP6-test-artifacts/W670-01/native-disposition-first-1/result.json) · [Main 写集](D:/CP6-test-artifacts/W670-01/native-disposition-first-1/permission-consumption-main/coordination/privacy-order.json) · [CRM TRX](D:/CP6-test-artifacts/W670-01/native-disposition-first-1/crm.trx) |
| Main 原生最终 COMMIT 应答丢失 | 精确观察 Main UPDATE 的等待者 30480 与持有者 35828，随后丢失原 PostgreSQL COMMIT 应答。HTTP 503 PRIVACY_DISPOSITION_OUTCOME_UNKNOWN。独立原库读到执行、墓碑和原 journal 各一份。故障宿主 9552 停止后，直连宿主 29988 的同键 POST 和 GET 恢复精确回执及已持久的审计字节。 | [TRX](D:/CP6-test-artifacts/W670-01/native-main-ack-1/main.trx) · [原生事实](D:/CP6-test-artifacts/W670-01/native-main-ack-1/coordination/privacy-main-ack.json) · [直连恢复](D:/CP6-test-artifacts/W670-01/native-main-ack-1/coordination/privacy-main-ack-readback.json) |

实际成功样本保留原用户 ID，C02 代次 20 → 23，相关 Outbox 4 → 7，CRM 用户及两个 grant 最低版本为 [5,1,1]。真实两人审批、同人审批拒绝、活动保留及原 CAS 都经过原 HTTP。W670 的应答故障只注入 Main 最终事务。CRM 批次宿主的源 blob c319a5eb529178e4e5bfa309e1689a5e91c1292f 与 W666 相同，复用 W666 已实测 CRM 应答遗失的证据。 [W666 CRM 回包与原版本](D:/CP6-test-artifacts/W666-01/final-ack-password/coordination/credential-crm-ack.json)

原处置 **7/7** 通过，包括原签名 HTTP、真实保留竞争、原跨租户范围及审计。原 W315 物理归档重施另在新库 **1/1** 通过。同批初跑留下 7 通过、1 失败。共享测试环境已有其他用例提交后保留的活动 hold，恢复的环境级检查返回 PRIVACY_RESTORE_POLICY_BLOCKED。独立复验保持原恢复规则。静态准备第二次编译的测试局部名冲突已修复，保留原输出。 [原处置混合 TRX](D:/CP6-test-artifacts/W670-01/related-native-1/main.trx) · [独立原恢复 TRX](D:/CP6-test-artifacts/W670-01/related-restore-2/main.trx) · [编译诊断](D:/CP6-test-artifacts/W670-01/product-compile-2/build.log)

## 本地发布与交接

将三个最终原生运行的源文件 SHA-256 与已提交候选逐一核对后重新发布。WebApi、Core 与 PostgreSQL 迁移程序集的产品版本均含精确候选 SHA。310 文件清单 SHA-256 为 B7AD82632068EF2772A0E4F8DC71785DE3E12497405620BE9523EE36DB1D2E5B。发布程序在 57258 的新隔离 PostgreSQL 原生初始化退出 0，读回最新 kind 与原 completion 约束。在 57259 实际启动，存活 200，匿名执行和原操作查询均 401。 [发布与启动](D:/CP6-test-artifacts/W670-01/publish/result.json) · [逐文件哈希](D:/CP6-test-artifacts/W670-01/publish/package-sha256.json) · [运行入口](D:/CP6-test-artifacts/W670-01/run-privacy-disposition.ps1)

codex/w670-01 保留本地提交，DEV-1 固定工作区干净。任务进程和 57240–57259 监听均为零，席位由协调方在集成后释放。Actions 运行、远端变更和部署均为零。 [停机与工作区](D:/CP6-test-artifacts/W670-01/process-verification.json)
