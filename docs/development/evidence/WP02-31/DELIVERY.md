# WP02-31 联系人历史导出与受控下载

Main PRIV/FILE 提供方完整 DONE。经真实 Main TLS 和隔离 PostgreSQL 固定 `CONTACT-HISTORY-EXPORT/1.1` 原请求、Job/Artifact 签名结果、当前下载许可及可验证制品。此卡是 CRM-CON-01 的 FOUNDATION。Main 受保护 PR 的合并提交固定实际采用树，原链 DELIVERY 记录同树冒烟与 DEV-1 正式释放。 [Main PR](https://github.com/GTX537/CP6/pull/1310) · [原派工 #402](https://github.com/GTX537/CP6.AI-Handoff/blob/36f758a041cc5b7702b22f3472ed093bdcf5f3a9/handoffs/CP6-DEV-20261012/tasks/COORD/replies/20261122T152454Z-exec-wp02-14b-wp02-31/MESSAGE.md) · [原 START](https://github.com/GTX537/CP6.AI-Handoff/blob/e68bcf017aa53c99d01f40bb3353ef61df95edd0/handoffs/CP6-DEV-20261012/tasks/WP02-31/messages/20261122T153141Z-exec-wp02-14b-start/MESSAGE.md) · [原交付链](https://github.com/GTX537/CP6.AI-Handoff/tree/codex/dev-handoff-20261117-exec-wp02-14b/handoffs/CP6-DEV-20261012/tasks/WP02-31/messages)

## 入口和依赖

提供方 `Main.PRIV.FILE.ContactHistory`，证明权威 `Main.PRIV.FILE.ContactHistory/1`。数据集为 `CONTACT_HISTORY`、`CONTACT-HISTORY-SCHEMA/1`、`CSV_VIEW`。TLS 路径前缀 `/internal/main/v1/contact-history-exports`。使用租户登记的 Main C01 服务身份、当前同库凭据、Main 当前主体和 Contact 的可读投影。签名公钥经登记固定，结果绑定 producer、租户、epoch、原请求、事件、流、修订和原字节 SHA-256。 [固定代码和传输合同](../../contracts/contact-history-export-v11.md) · [公钥样例](download-authority.json)

Main 固定席位基线 `18a5b2420b75fe15d868e962973d9c0cf4efd3f5`，产品受验提交 `c1d00fc82b14c1975db0592afb0768f1abc60d0e`。直接前置 WP02-14、WP02-14b、WP02-15 及展开依赖完整 DONE。三项的实际远端采用分别为 `81f90499f5c5ee039872601a59e0f641106edabc`、`4ecad67f4a5a092c3e415d2c9516b084adc9e89d`、`3ea9d2ba2ab4e1a644369432460a5251e2f1bcda`，均是领取基线的祖先。 [WP02-14 #1242](https://github.com/GTX537/CP6/pull/1242) · [WP02-14b #1254](https://github.com/GTX537/CP6/pull/1254) · [WP02-15 #1255](https://github.com/GTX537/CP6/pull/1255)

CRM 接口来自采用提交 `512303e9f6c530dbd398991d96a69ffe9990cf23`。将同一已采用代码的纯解码器用于本卡 13 份 Main 实际结果，通过事件键、精确流摘要、前驱及版本核对。实际 CRM 的 TLS 适配、Inbox 应用与最终验收归 WP03-11b。 [已采用六项接口](https://github.com/GTX537/CP6.CRM/blob/512303e9f6c530dbd398991d96a69ffe9990cf23/src/CP6.CRM.Api/Contacts/ContactHistoryExportV11.cs#L38) · [解码对账](crm-decoder-final.json)

## 合同验收及实际证据

| 原消费操作 | Main 实际运行与观察 | 固定结果 |
|---|---|---|
| `Describe` | 真实 TLS 验服务 JWT、租户和 Main 服务登记。返回四个支持字段、schema、格式及 2 MiB 上限。匿名 TLS 拒绝。 | [验证清单](validation.json) |
| `SubmitOrReadSameAsync` | 完整意图在隔离 PostgreSQL 产生唯一原决定。按确认列序与 UTF-8/NFC、UTC 毫秒、Ordinal、显式 null 验摘要。登记截点、发生区间及两个视图独立。首次响应体未读，退出并重启进程后回同一原 Job。提交前并发和提交后重放各只形成一个 Job 与一个制品。撤新申请权而保留读权可回原件。同键异意图、错源事件、错摘要、错租户及错主体无新增。已登记的第二租户凭据查原键不可见，租户下请求为零。 | [原请求](allocation-request.json) · [受理原件](allocation-job-accepted.json) · [分配观察](allocation-observations.json) |
| 分配前终拒 | 无权用途用 NFC 归一后的摘要得到已签 `BEFORE_JOB_ALLOCATION`，撤去该用途限制后原键仍回同一决定。拒绝无 Job 和制品。 | [终拒请求](decline-request.json) · [终拒原件](decline-original.json) · [签名](decline-proof.json) · [签名公钥](decline-authority.json) |
| `AuthenticateResultAsync` | 以实际 Main 原件 SHA-256 及事件键查询，逐报文校验 ES256。以 Artifact 先于 Job 的逆序查回各原件，跨流以精确 `requiredJobRevision` 标识依赖。不同租户或摘要不能认证。 | [Job 受理](download-job-accepted.json) · [运行](download-job-running.json) · [完成](download-job-completed.json) · [Artifact READY](download-artifact-ready.json) · [签名包](download-results.json) |
| `VerifyReplayAsync` | 对同已收原件验证重复证据。改 digest 失败。发布序游标回读新增原件，历史字节一致。 | [验证清单](validation.json) · [两条流原件](checkpoint-results.json) |
| `VerifyCheckpointAsync` | 实际 Main 保存两条独立前沿。错前驱、错流或用另一流冲突 ID 尝试闭合被拒。只解决指名冲突，遗漏冲突继续阻止下载。完成两条流核对后原版本及 SHA 保持一致，并可重新签发许可。 | [Job checkpoint](job-checkpoint-original.json) · [Artifact checkpoint](artifact-checkpoint-final-original.json) · [错流证明](artifact-checkpoint-negative.json) · [错冲突归属](checkpoint-invalid-resolution.json) · [签名公钥](checkpoint-authority.json) |
| `AuthorizeReadAsync` | 按原 Request、Job、Artifact、版本、意图、摘要、大小、格式及当前读权与 DL/180 颁发有限许可。错误 Job、错租户、错主体、错版本、摘要、大小、格式、水位、字段或用途均拒绝。独立脱敏件使用新 opaque version 和新摘要。过期、撤 DL、匿名化、恢复 ACL 后的旧许可和旧 READY 均返回零字节。流中撤 DL 后连接中断，收到 16384 / 1298768 字节，仍在原许可期限内，旧许可再请求零字节。 | [许可](download-permit.json) · [文件撤销](download-artifact-revoked.json) · [脱敏状态](download-artifact-redacted.json) · [首字节前观察](download-observations.json) · [流中观察](stream-revocation-observation.json) |

四字段既有意图摘要为 `d2bb0c2ca661031e987b9890ccb8b8249cabc58e94c5cbaa1b936b81189ebc14`。Main 复算意图摘要与制品 SHA-256，CRM 命令 HMAC `RequestDigest` 由 CRM 验证。读取文件时对原始字节直接计算 SHA-256，按 Git 原字节核对完整清单。 [既有四字段合同向量](https://github.com/GTX537/CP6.CRM/blob/512303e9f6c530dbd398991d96a69ffe9990cf23/docs/development/evidence/WP03-11/DELIVERY.md#L15) · [SHA-256 清单](sha256.json)

| 实际下载文件 | 字节 | SHA-256 |
|---|---:|---|
| [联系人历史原件](contact-history.csv) | 128 | `0bced2f97833de5765ecf4822f3c9a3a390967816713f59d8b446ad81aac4d78` |
| [独立脱敏原件](contact-history-redacted.csv) | 91 | `ea26ecf53193fe920f6eb863248e383d3400151c10ad87c59772403bbf66dcd1` |
| [原件 SHA-256 清单](sha256.json) | 按清单 | `f8fe4fbe86105780b6878b6da9e89cbea879e55084aad1a35e96c035af8adc09` |

## 运行边界与核销

Windows、.NET 8 Kestrel 回环 TLS、隔离 PostgreSQL 18.6 和生产控制器、前向迁移及真实字节链均实际运行。TLS 证书、Main C01 服务凭据、联系人、源历史事实与当前字段、用途、隐私投影是同合同的合成夹具。以隔离库提交更新模拟外部权限及匿名化事实，真实 Main 读取并裁决。CRM 仅复用已采用解码器。分叉用已登记恢复调用者对真实原件登记冲突。随 PR 提交的固定原件只含合成数据与公钥。

最终定向 6/6，相邻 PrivacyExport、FILE 与 C01 服务令牌 101/101，零跳过。游标与归一增强后重跑全部定向，相邻复用同版本公共入口和未改动的相邻业务代码。脚本输出和原始 TRX 保存在本卡本机制品目录。工作流、远端采用、同树冒烟结果及干净释放见正常 PR 和原执行链。 [运行命令及测试清单](validation.json) · [Main PR](https://github.com/GTX537/CP6/pull/1310) · [原链 DELIVERY](https://github.com/GTX537/CP6.AI-Handoff/tree/codex/dev-handoff-20261117-exec-wp02-14b/handoffs/CP6-DEV-20261012/tasks/WP02-31/messages)

CRM-CON-01 维持原 AC 42/49、必需补充 24/26、必需运行修订 2/2。C16、C36、C37、REV1-18 的实际双向采用及 C35、C34、REV1-13 归 WP03-11b。C35 按裁定由 CRM Lead 通过授权写入显式登记租户、Contact、ActivityId 与 LeadId，保留来源、版本和更正。C22 归 WP03-20，C47 归 WP03-18。 [目标原验收](https://github.com/GTX537/CP6.CRM/blob/512303e9f6c530dbd398991d96a69ffe9990cf23/docs/development/evidence/WP03-11/DELIVERY.md#L119) · [C35 裁定](https://github.com/GTX537/CP6.AI-Handoff/blob/027eab421b934bbd6833d39d6551026d9eb62418/handoffs/CP6-DEV-20261012/tasks/WP03-11/replies/20261122T152246Z-coord-c35-lead-owner-decision/MESSAGE.md)

WP02-15 拨入 13102.913 秒，WP02-23 拨入 1297.087 秒，原额 14400 秒，21600 秒强制拆卡。WP03-11b 保留原父 WP03-11，增加 WP02-31 直接前置，额度 10600.674 秒。按 #401 与原 DELIVERY 完整 SHA 加路径去重，单次补记 WP03-07 收尾 187.323 秒至 3197.740 秒。原目标核对 2.820 秒单列。本卡快照 `2026-11-22T16:05:38.360019+00:00`，卡内 2125.703 秒，目标核对 0.000 秒，计划累计 166964.957 秒。下一候选为 WP03-11b，须由 COORD 单独派工。 [G05 授权](https://github.com/GTX537/CP6.AI-Handoff/blob/027eab421b934bbd6833d39d6551026d9eb62418/handoffs/CP6-DEV-20261012/tasks/WP03-11/replies/20261122T152246Z-coord-g05-main-owner-authorization/MESSAGE.md) · [#401 签收](https://github.com/GTX537/CP6.AI-Handoff/blob/0b5590dab6d2550abdbf05d0e078ec83d015b93c/handoffs/CP6-DEV-20261012/tasks/WP03-07/replies/20261122T152012Z-coord-acceptance-ack/MESSAGE.md) · [原 DELIVERY](https://github.com/GTX537/CP6.AI-Handoff/blob/b6219fa3d08ec085b77aad6bc985d38430a08770/handoffs/CP6-DEV-20261012/tasks/WP03-07/messages/20261122T151309Z-exec-wp02-14b-delivery/MESSAGE.md) · [单卡计划](../../planning/20261117-card-execution.md)
