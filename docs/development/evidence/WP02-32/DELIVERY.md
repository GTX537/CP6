# WP02-32 ACT 文件权威门与守护流式下载

本补充卡正式编号为 `WP02-32`。Handoff #439 R1 授权中的暂名为 `WP02-14c`。分支 `codex/wp02-14c` 与 DEV-2 工作区租约 `CP6:WP02-14c` 沿用原派工标识。 [Draft PR #1339](https://github.com/GTX537/CP6/pull/1339)

总控经 Handoff #439 转交 File/Security Owner 的 R1 裁决「选甲，委托本地门」。唯一授权消息提交为 `c49a2ed2ff23eba263ff969bae6f5322f3616483`。预算沿 WP02-29 的 11599.8 秒和 WP03-18 的 2800.2 秒，合计 14400 秒。原 AC 归属沿用 #438。 [R1 原消息](https://github.com/GTX537/CP6.AI-Handoff/blob/c49a2ed2ff23eba263ff969bae6f5322f3616483/handoffs/CP6-DEV-20261012/tasks/WP03-19/replies/20261126T132500Z-coord-file-guard-delegation-r1/MESSAGE.md)

Main 远端基线为 `80113f4f751617d73d1b1e989838a2f6ee0b27da`，树为 `f64614f369db41db7a69d4aa6de7c4fa38364fd8`。独立复核的起点为 `5d6c6e1fcfe19a6a1c45fbd95b5fc3c66075b14a`。打回后的受验产品候选为 `f0a16a1184844ecf27fc4bcc752572529f21f5b2`，树为 `85f52d3e62918d8d96c82105ccc21a1e4926c188`。最终候选的 SHA、树和全量复验证据固定在 `D:/CP6-test-artifacts/WP02-14c/exec-20261126T140339Z/final-verification.json`。直接前置 WP02-14、WP02-14b 已采用，合并提交分别为 `81f90499f5c5ee039872601a59e0f641106edabc`、`4ecad67f4a5a092c3e415d2c9516b084adc9e89d`，均在基线祖先中。连同前置和拆分余卡，共 11 项 DONE。DEV-2 独占本卡，FILE 文件与 WP05-02 的 ERP/PLM 资源无交集。 [WP02-14 #1242](https://github.com/GTX537/CP6/pull/1242) · [WP02-14b #1254](https://github.com/GTX537/CP6/pull/1254)

## Main 提供者

- 对 CRM ACT 的裁决设在 CRM 库内。Main 每个登记租户使用一条 guard 行覆盖该租户所有 ACT 文件。角色、租户授权和保留规则可影响多个文件，用同一租户行串行裁决。精确文件、用途及动作始终由证明与具体授权记录限定。
- Main 前向迁移 `20261126132500_ActFileGuardProviderV1` 将 FILE 原件及状态、草稿正式绑定、隔离、处置、技术内容保留、隐私 hold、相关 IAM 和服务撤权写入接到 PostgreSQL 触发器。同一 Main 事务对每租户只推进一次。Main 从受控连接调用 CRM 的 `advance_v1` 并先收到独立远端提交回执，随后本地事实、代次和既有业务回执一起提交。普通 SQL、批量修改与应用写者经过同一门。Main 尚未提交且 CRM 已先推进时，两个代次不一致，封闭新证明。用旧 `epoch/receipt` 原键取得同一推进回执，Main 业务恢复使用原 API 操作键。
- ACT 文件政策按登记租户、主体现行有效性、精确文件及 `ACTIVITY_EVIDENCE` 绑定。Main 用途授权写者由每租户的 `grant_login` 固定。CRM 引用全集接入前，已登记 ACT 文件的物理处置在 Main 拒绝。`FileContentController.Download` 仍使用既有 FILE 路由和授权。
- Main 内部 TLS API 前缀为 `/internal/main/v1/act-file-references`。 `POST /proofs` 核对已登记的 CRM 服务令牌、租户、现行主体、精确意图和每个成员，保存整份签名原件。同键异输入拒绝。引用原证明可按原键回读，下载证明中的元数据只在 guard 仍为当前时返回。普通 GET、推测 TTL 及 reserve/hold/confirm 不能取得提交保证。 `POST /downloads` 消费单个已签发的 `OWNER_GUARDED_STREAM` 下载决定。签名绑定精确长度、媒体类型和安全文件名。先验证精确原件并保存 STARTED 安全审计，再按 16 KiB 分块持锁复核当前授权和 scan/guard。响应发送和刷新在每块的共享锁内完成，块间释放。撤权后旧证明终止流，审计记录已确认刷新的字节和结论。证据不能断言客户端已读走所有已刷新字节。
- 常规 Main 迁移可独立应用。未登记的既有 FILE 调用无需安装 `dblink` 扩展。生产登记条件与角色权限见下文。 [Main 迁移](../../../../CP6.Persistence.PostgreSql/Migrations/Core/20261126132500_ActFileGuardProviderV1.cs)

## 打回修订

- 签发、下载证明原键恢复及每次流分块均在 Main 的 IAM、FILE 和来源锁内复核当前主体。FILE Owner 沿用租户、对象范围、固定工程版本、可选工程产品父级和授权目录。Add 与 Download 检查现行 FILE `query`、`Content` 和 `FileName` 读取权限。Retain 使用 FILE `edit` 对象范围及独立留存许可。Remove 使用 FILE `delete` 对象范围及独立解绑许可。后两项只处理引用权限，允许无 FILE 内容读取或下载权，Remove 不删除原件。工程版本和产品的部门绑定写入也参加同一 guard。 [权限补充迁移](../../../../CP6.Persistence.PostgreSql/Migrations/Core/20261126135000_ActFileNativeAuthorityV1.cs)
- 守护流在 Main 先持 FILE 和来源排他锁，并在 CRM 共享锁内核对精确原件。完整性失败后先释放本次 CRM 共享锁，再走两个下载入口共用的原有 FILE 写路径。隔离事件 `INTEGRITY_FAILURE`、`NOT_CLEAN` 状态、FILE 原回执与零字节流审计随 Main 事务提交。原件、状态版本和 ETag 对照后才隔离；原有写者触发器排他推进同一个 CRM guard，有其他 CRM 事务持锁时等待。提交后返回 `FILE_CONTENT_INTEGRITY_UNAVAILABLE`，响应无内容和存储路径。同租户其他文件的状态保持有效，后续调用使用新代次证明。

## 生产启用

启用状态为 `NOT_RUN`。按 WP03-19 采用实际 CRM schema、部署身份和事务顺序后执行环境登记。

1. 在 Main 应用前向迁移。由受控数据库管理员在专用 schema 执行 `CREATE EXTENSION dblink WITH SCHEMA cp6_act_file_transport`，核对扩展实际安装位置。收回 `PUBLIC` 的 schema 与扩展函数权限。只向 `advance_sources_v1` 定义者开放该 schema、`dblink(text,text)` 与对应外部服务器的使用权。每个登记使用 `dblink_fdw` 外部服务器与定义者专属的 `CREATE USER MAPPING`，通过密码管理系统提供窄推进账号的认证材料。 [PostgreSQL dblink](https://www.postgresql.org/docs/18/contrib-dblink-connect.html)
2. CRM 分开登记推进、Main 守护读和 Activity 登录。推进登录仅有 CRM `CONNECT`、专用 schema `USAGE` 与 `advance_v1` 的 `EXECUTE`，Main 守护读账号仅执行 `hold_v1`，Activity 登录另按自己的业务职责取得权限。Main 运行角色使用既有 FILE Owner 数据读写、原回执和 IAM 锁权限，同时只需对 ACT 表读取用途授权、读取和追加证明原回执、追加流审计，以及执行 `hold_source_v1` 和 `hold_source_for_integrity_v1`。独立 `grant_login` 登记为用途授权写者。部署时确认角色未继承迁移或跨库传输权限。
3. 使用与 CRM 证书匹配的 DNS 主机名配置外部服务器 `host`、`port`、`dbname`、`sslmode=verify-full`、受信 CA 与 `connect_timeout=5`。Main 守护读连接使用独立账号和 `SSL Mode=VerifyFull`。在 CRM 登记实际数据库身份、租户、`store_id` 和初始 `epoch/receipt`，在 Main `cp6_act_file_v1.sources` 核对同一元组及对应外部服务器。通过 `ActFileReference` 的 `Clients` 登记 C01 服务 `ClientId`、租户、存储和密管读取连接。注入 ES256 签名密钥，CRM 固定 `KeyId` 和公钥，双向登记核对完成后再启用 HTTP 入口。 [PostgreSQL TLS 和 libpq 连接参数](https://www.postgresql.org/docs/18/libpq-connect.html)
4. 在 CRM 为每个推进登录设置单库执行上限 `ALTER ROLE <seal_login> IN DATABASE <crm_db> SET statement_timeout = '12s'`。这是部署默认值，PostgreSQL 出厂值 `0` 会关闭执行超时。新连接应以推进登录确认 `SHOW statement_timeout` 为 `12s`。CRM 门函数行锁上限为 8 秒，Main 守护读连接为 5 秒和命令 9 秒，Main ACT 本地事务的锁和语句上限分别为 8 秒和 15 秒。远端执行上限从 CRM 收到语句开始计时。网络或提交结果不明时封闭新裁决，恢复时以已登记的旧代次和回执找回该次推进。隔离未取得确认时，用 FILE 原入口重新检查原件并走同一推进恢复。 [PostgreSQL 会话超时](https://www.postgresql.org/docs/18/runtime-config-client.html#GUC-STATEMENT-TIMEOUT) · [登录角色设置](https://www.postgresql.org/docs/18/sql-alterrole.html)
5. CRM 先完成 Main 预检，再开启 Activity 事务取 CRM guard 的共享锁，按实际持有的代次验签并全量核对意图，再取 Activity 业务锁并持 FILE guard 到提交。代次变化时退出当前事务，释放锁后再预检。Main 写者持本地授权、FILE 或来源锁时等待 CRM 排他锁。如果 CRM 持共享锁再调用 Main 预检，预检会等 Main 本地锁，形成跨库循环等待。按先预检后持锁可消除该环。

## WP03-19 接口与 schema 合同

IAM 封存的实际 DDL 位于 CRM 仓，所以本卡在隔离夹具上验证 CRM 等价结构，生产 schema、真实 Activity 事务及 Add/Retain/Remove 消费由 WP03-19 实现。Main 不安装 CRM schema。 `runtime_status` 保持 `UNPROVEN_G03`。 [CRM IAM 封存先例](https://github.com/GTX537/CP6.CRM/blob/9c071f20d044f9fb26dbf8f91e777a204326adee/src/CP6.CRM.Infrastructure/Identity/PostgreSql/InquiryTenantPauseSchemaV1.sql) · [等价 DDL 与授权](../../../../CP6.Tests/Erp/ActFileGuardCrmFixtureV1.cs)

| CRM 结构 | 精确约定 |
|---|---|
| `crm_act_file_guard_v1.guards` | `tenant_id uuid` 主键，`store_id uuid` 唯一。绑定实际 PostgreSQL 的 `cluster_id bigint` 和 `database_oid oid`。专用推进、Main 证明读取、CRM Activity 三种登录名分别列为 `seal_login name`、`read_login name`、`activity_login name`。当前 `epoch bigint` 为正数，`receipt uuid` 非空。登记双方初始值完全相同。 |
| `crm_act_file_guard_v1.advances` | 不可变推进原回执和审计。原键 `(tenant_id, store_id, previous_epoch)`，连同 `previous_receipt uuid` 核重放。保存新 `epoch bigint`、唯一 `receipt uuid`、数据库时刻及实际 `session_user`。 |
| `advance_v1(uuid,uuid,bigint,uuid)` | 入参依次是租户、存储、原代次和原回执。窄 `SECURITY DEFINER` 函数只准登记的 `seal_login` 调用，核当前数据库身份并取 `FOR UPDATE`。同一原键只推进一次，确认后返回单行 `(epoch bigint,receipt uuid)`。写者角色只可执行该函数，无 guard 表或 CRM 业务表写权。 |
| `hold_v1(uuid,uuid)` | 只准登记的读取与 Activity 登录调用。核租户、存储、实际数据库身份，在传入事务取得 `FOR SHARE`，返回单行 `(epoch bigint,receipt uuid)`。库恢复后按准入流程重新登记存储身份及代次，旧登记证据不继续放行。 |
| 证明与消费 | 锁序按 CRM 原合同先持 FILE guard，后取 Activity 意图锁与业务锁。固定 Main ES256 公钥和 `keyId`，使用原始 body 字节验签。 `body` 与 64 字节 P1363 `signature` 使用 Base64；正文含 `schemaVersion`、`authority`、`mode`、`attestationRef`、`consumerClientId`、`guard`、有序 `decisions`。 `guard.epoch` 以精确十进制字符串传输。附件键和版本保留为不透明字符串。Main 仅在解析自己发行的原生 FILE 键时使用 UUID。完整意图走同一签名，覆盖原合同 100 项显式窗口和最多 50 项新增。CRM 核对整组 Tenant/Actor/O/intentDigest、primary/Activity/Revision/Set、ReferenceId、AttachmentId/Version、用途、SHA-256、动作、Decision、扫描和策略/保留证明。请求摘要若提供则必须与 Main 原件匹配。签名结果始终包含 Main 核定的精确 SHA-256。签名中相同租户、`storeId/epoch/receipt` 及全向量匹配后才提交。下载调用还需由 CRM 当前权限与真实 Reference 成员关系生成 `currentAccessStamp`。 |

## 原 AC 对照

| 原出口 | Main 提供者侧证据 | CRM 采用侧 |
|---|---|---|
| AC-FI02-02 | 当前主体与 FILE 专用授权精确绑定，跨租户、错主体拒绝。 | WP03-19 同角色协作者和可见范围。 |
| AC-FI02-03 | 证明固定 Activity/Revision/Set 和精确文件版本，错版本拒绝。 | WP03-19 历史页、冻结集合与分页。 |
| AC-FI03-01 | 当前 DL、用途、精确版本和扫描门逐块复核，旧证明撤权后中断或拒绝。提供者路由拒绝 Range。 | WP03-19 真实宿主、历史入口与新读取组合。 |
| AC-FI03-04 | 受控原件没有授予其他字节、用途或预览。 | WP03-19 缩略、OCR、搜索等实际披露，`NOT_RUN`。 |
| AC-FI04-01 | 同一意图全组判定，签名固定锚点、原键、原件和动作，拒绝后无新证明。 | WP03-19 目标现态、集合写权和整图同一事务。 |
| AC-FI06-01 | 新用途有独立 ACT 授权，既有 FILE 下载回归通过。 | WP03-19 真实菜单、对象、字段和用途能力接线。 |
| AC-FI06-02 | 证明和字节接口按指定主体、引用和用途裁决。 | WP03-19 隐藏成员的列表、计数、预览及只读限制。 |
| AC-FI06-04 | guard 缺证、远端只读或 TTL/预占输入关闭新效果，Retain、Remove 与 Download 权限独立。 | WP03-19 实际最终门和原历史读取。 |
| PE-ACT-FILE-COMMIT-MODE-POS / NEG | 隔离双库证明实际共享锁、互斥顺序、原回执、旧证明拒绝和远端提交方式拒绝。 | WP03-19 真实采用仍为 `NOT_RUN`。 |

## 本地运行

.NET SDK 10.0.404 Release 与 PostgreSQL 18.6。已提交产品候选一次运行 **35 项通过，0 失败，0 跳过**，含原 32 项及新增的 3 项。ACT 共 12 项，FILE 内容／草稿／处置回归 17 项，WP02-31 受控下载及 G05 回归 6 项。产品提交 `f0a16a1184844ecf27fc4bcc752572529f21f5b2` 的树为 `85f52d3e62918d8d96c82105ccc21a1e4926c188`，其 TRX 为 `D:/CP6-test-artifacts/WP02-14c/exec-20261126T134709Z-rework1/rework-product-20261126T135852111Z/results.trx`，SHA-256 为 `874404e052c563fc6327ec344a749191ff752a695dd37f40fcfccf993f743020`。最终复验用 `D:/CP6-test-artifacts/WP02-14c/exec-20261126T140339Z/verify.ps1`，同目录 `final-verification.json` 固定实际提交、树、TRX 路径和 SHA-256。每轮启动环境和完整命令均保存于对应的 `command.json`。

| R1 验收 | PostgreSQL 或真实 TLS 用例 | 结果 |
|---|---|---|
| Main HOLD 等待 CRM 事务提交，随后旧代次失效，推进角色无业务表或 guard 表直接写权 | `ActFile_Main_native_hold_waits_for_CRM_commit_then_invalidates_the_original_guard` | PASS |
| 整意图 Add/Retain/Remove 判定、身份签名、持锁确认、原键恢复及撤权后零新增 | `ActFile_proof_is_atomic_binds_exact_identity_and_replays_original_after_revocation` | PASS |
| 完整 100 项既有窗口加 50 项新增，不透明键原样传输 | `ActFile_intent_accepts_complete_window_and_opaque_external_keys` | PASS |
| 远端 GET、TTL、reserve/hold/confirm、跨租户、错主体、错用途、错版本或摘要、待检与隔离 | `ActFile_rejects_remote_modes_mismatched_identity_pending_and_quarantine` | PASS |
| 远端不可达、已推进但未得到确定回执、Main 无已生效结果、原键恢复同一推进 | `ActFile_writers_fail_closed_and_recover_original_CRM_advance_after_unknown_reply` | PASS |
| 授权写者重复同状态不推进，保留写入推进，直接绕过和已登记原件物理处置拒绝 | `ActFile_retention_unlink_and_disposition_writers_use_the_same_guard_with_narrow_privileges` | PASS |
| 当前 DL 与精确 SHA-256，分块撤权后无后续输出，审计确认 16384／62000 字节，旧下载证明拒绝重放 | `ActFile_stream_returns_exact_hash_and_stops_after_first_slice_when_DL_is_revoked` | PASS |
| 实际 C01 TLS、跨租户拒绝、守护 HTTP 流撤权截断和旧 `FileContentController.Download` 成功兼容 | `ActFile_actual_TLS_requires_registered_service_and_streams_exact_file` | PASS |
| 前滚迁移前后旧原件、原回执和旧入口稳定 | `ActFile_forward_migration_preserves_an_existing_file_and_receipt_without_CRM_registration` | PASS |
| 仅撤销现行 FILE 动作、附件字段或父级查询时，原用途授权保留，新 Add 和 Download 拒绝且零新回执。恢复后可签发，Retain 与 Remove 独立判定 | `ActFile_native_FILE_action_fields_and_parent_revocation_refuse_new_proofs_until_restored` | PASS |
| 撤销 FILE 对象的部门范围会推进 CRM guard，拒绝新 Add，恢复后同键可签发 | `ActFile_native_FILE_object_scope_writer_invalidates_guard_and_rejects_new_reference` | PASS |
| 篡改精确原件后零字节，隔离写等待 CRM 共享持有结束，日志和状态持久化，原代次失效，新 Add 拒绝，健康原件可下载 | `ActFile_guarded_stream_integrity_failure_quarantines_exact_file_and_advances_guard` | PASS |

原候选的三条打回用例分别复现撤权后仍签发、部门范围撤销未推进 guard、异常原件仍可用。复现 TRX 保存于 `D:/CP6-test-artifacts/WP02-14c/exec-20261126T134709Z-rework1/rework-red-20261126T135027258Z/results.trx`。本轮两侧工作流静态检查 26 条记录，工作流无差异，普通分支与 PR 无匹配自动触发。使用此前按锁文件校验的 `yaml`。

两库使用同一隔离 PostgreSQL 18 试验集群，分别使用不同数据库和真实锁。身份、文件、授权和 CRM 活动占位写为合成，服务传输为回环真实 TLS。远端回执未知使用远端实际提交、丢弃返回结果的测试注入。真实 CRM 生产 schema 和业务采用、生产数据库间 TLS 登记、真实迁移与运行采用保持 `UNPROVEN`／`NOT_RUN`。资料归档后未保留监听或测试进程。

## 总控独立复核

首轮对候选 `5d6c6e1fcfe19a6a1c45fbd95b5fc3c66075b14a` 的结论为 **FAIL**。32 项自动化全通过，集中复核发现 FILE 当前权限复核和摘要异常隔离两处阻塞。首轮 TRX SHA-256 为 `387f76a0bfc58ffaf88a299f92c0cb0f55ea953480e177470fcd48431126a04f`，原件位于 `D:/CP6-test-artifacts/WP02-14c/coord-verify-20261126T133250174Z/results.trx`。

打回修复后，总控对 `84bed4b2d9e9427efb77727cd0c9f2e414e892b2`（树 `2ef15eed0928f278223529ce6b36b938ab072fd2`）独立构建并复跑，**35 项通过，0 失败，0 跳过**，测试名单和结果与执行侧一致。复核结论 **PASS_WITH_NITS**。独立 TRX 位于 `D:/CP6-test-artifacts/WP02-14c/coord-verify-20261126T142630678Z/results.trx`，SHA-256 为 `e8d8d423dc1eb95780031d03b3a1693b0c3295074899c0834ca821d35d0ba9e1`。集中审查记录位于 `D:/CP6-test-artifacts/WP02-14c/coord-verify-20261126T142630678Z/review.json`。

## 已知项

守护流首读在读取整件原件期间持有 FILE 表级写锁。上线前需量化慢存储下并发下载等待，以及对旧 FILE 下载入口延迟的影响。生产启用保持 `NOT_RUN`。 [守护流首读](../../../../CP6.Core/Services/Pub/ActFileReferenceOwnerV1.Stream.cs#L38)

## 有效时间与生效

执行侧累计有效时间台账取最终交付原件的 `4912.0` 秒。两个产品复跑分别绑定执行侧与总控记录。 [执行侧计时与席位原件](/D:/CP6-test-artifacts/WP02-14c/exec-20261126T140339Z/workspace-readback.json)

WP02-32 的卡级 DONE 随 #1339 登记，在 PR 正常合并至受保护主线并核对实际远端采用后生效。SYS-FILE-01 保持开放，生产 guard schema、八条原 AC 与两条提交方式场景的 CRM 采用归 WP03-19，`runtime_status` 保持 `UNPROVEN_G03`。 [PR #1339](https://github.com/GTX537/CP6/pull/1339)
