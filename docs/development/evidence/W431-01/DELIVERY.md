# W431-01 · 原 PGT-015 消息键并发

DEV-1 于 2026-11-09 22:23:10 UTC 从 Main #862 领取。原 PGT-015 的普通角色并发、完整回滚与独立键域已在 PostgreSQL 18.6 实测，结合已执行的受控接收与实际 CRM Owner 原结果采用该场景。[原验收项](../../acceptance/postgresql-tenancy.json)

沿用 W348 原 CLI 安装的 `w348_provision_verify`。源 system identifier `7706064738685665676`，库 OID `16386`，端口 `55557`。A/B 普通登录角色 OID 为 16384/16385，当前绑定和原 Aggregate 与 W348 原值相同。Main #862 的 ERP 和 Identity 迁移及 RLS 与该基线一致。只建立一个 51,466,579 字节的隔离副本，启动前验证实际数据目录和源分离。[W348 原结果](/D:/CP6-local-test/CP6-72H-20261109/W348-VERIFY/DELIVERY.md) · [复制凭据](/D:/CP6-local-test/CP6-72H-20261109/W431-01/message-clone/copy.json) · [四张原表索引和 RLS](/D:/CP6-local-test/CP6-72H-20261109/W431-01/native-02/unique-rls-catalog.stdout)

## 实际并发与写集

临时维护认证规则撤除后，维护角色及普通 A/B 的无密码连接实际拒绝。测试通过原 SCRAM 登录，两个普通角色均具有原表 `SELECT`、`INSERT` 和 `UPDATE`，仍受实际 FORCE RLS 与目录绑定约束。B 暂存同键，A 暂存本租户伴随行并争用该键。每次先由控制连接确认真实 `transactionid` 阻塞，再让 B 提交。[普通绑定 A](/D:/CP6-local-test/CP6-72H-20261109/W431-01/native-02/ordinary-binding-a.stdout) · [普通绑定 B](/D:/CP6-local-test/CP6-72H-20261109/W431-01/native-02/ordinary-binding-b.stdout) · [首个实际阻塞](/D:/CP6-local-test/CP6-72H-20261109/W431-01/native-02/identity-inbox-same-lock.json)

| 原表和键域 | opaque raw | B 提交 xid | A 回滚 xid |
|---|---|---:|---:|
| Identity Inbox，同 consumer 与 MessageId | 相同 | 768 | 769 |
| Identity Inbox，同 consumer 与 MessageId | 不同 | 772 | 773 |
| Identity Outbox，MessageId | 不同 | 776 | 777 |
| ERP CommandInbox，MessageId | 相同 | 780 | 781 |
| ERP Outbox，MessageId | 不同 | 784 | 785 |

五次 A 均在同一个失败事务收到 `23505`、`25P02`。独立事务状态证明 B 提交、A 回滚和伴随行清零。A 重新通过普通登录读写原键均为 0 行，B 独立读取的完整原行与提交前相同。Inbox 记录原摘要，CommandInbox 和两张 Outbox 同时比对原 `bytea` 与摘要。不同原始内容的错误流未披露 B 内容。A 用同一 Inbox 键的不同 consumer 成功提交 xid 788，又用原 Identity Outbox 的同一个 MessageId 在独立 ERP 队列成功提交 xid 789。四表完整写集只包含预期七条记录，W348 原 Aggregate 与安装回执逐字节保持，SHA-256 `c78be24947a8a6e98afb94a5ed7038911b5abde18cffaad8f5004c3a75a74c5f`。[实际原件](/D:/CP6-local-test/CP6-72H-20261109/W431-01/native-02/actual.json) · [原生 SQLSTATE](/D:/CP6-local-test/CP6-72H-20261109/W431-01/native-02/identity-inbox-same-loser.stderr) · [全写集](/D:/CP6-local-test/CP6-72H-20261109/W431-01/native-02/queue-after.stdout)

## 停机与剩余范围

首轮 `native-01` 的 24 个样本为 933–998 MiB，36.421 秒后退出 75。第二轮使用同一停机副本，启动前 1,039 MiB，启动后 1,019 MiB，实际 PostgreSQL 窗口 6.828 秒。postmaster 49592 已退出，原件控制文件未变，副本正常停机并恢复原 HBA。最终驱动 SHA-256 `1921fb0ce8c67cf28aa22d018db53f09471ff6cd1dd9e70a26c48b15248d4c3c`。[第一轮门槛](/D:/CP6-local-test/CP6-72H-20261109/W431-01/native-01/prelaunch.json) · [第二轮停止](/D:/CP6-local-test/CP6-72H-20261109/W431-01/native-02/stopped.json) · [私有驱动](/D:/CP6-local-test/CP6-72H-20261109/W431-01/run-w431.py)

集中审查覆盖原 DDL、真实连接与阻塞、失败事务、当前读权、原行和全写集。本窗 `actual.json` SHA-256 `7778f3bc9ec8fbb74dcfd2ae39bb9397a3685d37c8d1bc7e8db9fc25be8a6c09`。Platform WP02-07 的实际 PostgreSQL TRX 20/20 复用原内容判定、技术冲突 DROP、无 Owner 改写、原字节及整事务重试，SHA-256 `ded4b853f9e0d38a222694ebe9615cc9f93d3f7bc3d34cf5712ff22d1381d072`。原测试 `ed6ee2972d16ea747e706be3dce258dc299cd1b8` 到正式 0.10.3 来源 `1277da5e0a20c6d96f0a95d0e9b4f20e0008332a` 的 Inbox 实现一致。[受控接收原结果](/D:/CP6/CP6.Platform/docs/development/evidence/WP02-07/test-results/inbox-postgresql-reviewed.trx) · [正式包原凭据](/D:/CP6-local-test/wave54/foundation/formal-package-cache-check.json)

CRM W57 的实际 PostgreSQL TRX 64/64 中，原 `PostgreSqlIdentityIngressV2` 使用正式 0.10.3。原有效同租户冲突只作用于可信原 aggregate。跨租撞键实际返回 `TransportIdentityConflict`，目标租户的 Sources、Conflicts、Operations 均为 0，未发生成功重复处理。该测试及受控接收相关实现从 W57 采用提交 `6ff205740ac5290154588acd7840ad9f70cb7c96` 至核实的 CRM 远端 `0cd83eb09826552fc1dc8d631a7c82484ec40363` 一致，正式依赖仍固定 0.10.3。TRX SHA-256 `c3652280b1187b00bef1b38ba4c81790682da89ea2c3be48a3e16a09223e3959`。[CRM W57 原结果](/D:/CP6-local-test/wave57/foundation/results/publication-related-regression-20261023T061845Z.trx) · [当前受控入口](https://github.com/GTX537/CP6.CRM/blob/0cd83eb09826552fc1dc8d631a7c82484ec40363/src/CP6.CRM.Infrastructure/Identity/PostgreSql/IdentityIngressInboxV2.cs)

采用范围限原 PGT-015 的全局键、并发和租户边界。完整 DEL02-A1/A2、broker 与宿主采用、独立业务运行矩阵和发布状态按各自原项登记。
