# WP02-14c ACT 文件权威门与守护流式下载

总控经 Handoff #439 转交 File/Security Owner 的 R1 裁决「选甲，委托本地门」。唯一授权消息提交为 `c49a2ed2ff23eba263ff969bae6f5322f3616483`。预算沿 WP02-29 的 11599.8 秒和 WP03-18 的 2800.2 秒，合计 14400 秒。原 AC 归属沿用 #438。 [R1 原消息](https://github.com/GTX537/CP6.AI-Handoff/blob/c49a2ed2ff23eba263ff969bae6f5322f3616483/handoffs/CP6-DEV-20261012/tasks/WP03-19/replies/20261126T132500Z-coord-file-guard-delegation-r1/MESSAGE.md)

Main 远端基线为 `80113f4f751617d73d1b1e989838a2f6ee0b27da`，树为 `f64614f369db41db7a69d4aa6de7c4fa38364fd8`。受验产品候选为 `e06419b3951e25b16d5dcae7d039b0be545e9f54`，树为 `df1fc35eb6135f897aba7ec8064260c9d44a4802`。本报告随同一次交付另提交，报告提交的复验记录由外部执行目录保存。直接前置 WP02-14、WP02-14b 已采用，合并提交分别为 `81f90499f5c5ee039872601a59e0f641106edabc`、`4ecad67f4a5a092c3e415d2c9516b084adc9e89d`，均在基线祖先中。连同前置和拆分余卡，共 11 项 DONE。DEV-2 独占本卡，FILE 文件与 WP05-02 的 ERP/PLM 资源无交集。 [WP02-14 #1242](https://github.com/GTX537/CP6/pull/1242) · [WP02-14b #1254](https://github.com/GTX537/CP6/pull/1254)

## Main 提供者

- 对 CRM ACT 的裁决设在 CRM 库内。Main 每个登记租户使用一条 guard 行覆盖该租户所有 ACT 文件。角色、租户授权和保留规则可影响多个文件，用同一租户行串行裁决。精确文件、用途及动作始终由证明与具体授权记录限定。
- Main 前向迁移 `20261126132500_ActFileGuardProviderV1` 将 FILE 原件及状态、草稿正式绑定、隔离、处置、技术内容保留、隐私 hold、相关 IAM 和服务撤权写入接到 PostgreSQL 触发器。同一 Main 事务对每租户只推进一次。Main 从受控连接调用 CRM 的 `advance_v1` 并先收到独立远端提交回执，随后本地事实、代次和既有业务回执一起提交。普通 SQL、批量修改与应用写者经过同一门。Main 尚未提交且 CRM 已先推进时，两个代次不一致，封闭新证明。用旧 `epoch/receipt` 原键取得同一推进回执，Main 业务恢复使用原 API 操作键。
- Main 运行角色只取得证明读取、原回执与流审计所需的本库授权。Main 的 ACT 授权写者由每租户的 grant_login 固定。ACT 文件政策按登记租户、主体现行有效性、精确文件及 `ACTIVITY_EVIDENCE` 绑定。Add、Retain、Remove 和 Download 分别判定，Retain 需要可用保留许可，Remove 不依赖下载权且不会删除原件。CRM 引用全集未接入前，已登记 ACT 文件的物理处置在 Main 拒绝。旧 `FileContentController.Download` 沿原路由与授权工作。
- Main 内部 TLS API 前缀为 `/internal/main/v1/act-file-references`。 `POST /proofs` 核对已登记的 CRM 服务令牌、租户、现行主体、精确意图和每个成员，保存整份签名原件。同键异输入拒绝。引用原证明可按原键回读，下载证明中的元数据只在 guard 仍为当前时返回。普通 GET、推测 TTL 及 reserve/hold/confirm 不能取得提交保证。 `POST /downloads` 消费单个已签发的 `OWNER_GUARDED_STREAM` 下载决定。签名绑定精确长度、媒体类型和安全文件名。先验证精确原件并保存 STARTED 安全审计，再按 16 KiB 分块持锁复核当前授权和 scan/guard。响应发送和刷新在每块的共享锁内完成，块间释放。撤权后旧证明终止流，审计记录已确认刷新的字节和结论。证据不能断言客户端已读走所有已刷新字节。
- 仅在登记时启用受控跨库通道。Main 迁移为已登记外部服务器提供专用的 `cp6_act_file_transport` schema，登记角色配置 `dblink_fdw`、受控用户映射、5 秒连接超时及验证服务端身份的 TLS。本地专用的 CRM 读取账号来自 `ActFileReference:Clients` 的绑定。远端推进账号与证明读取账号分开。常规 Main 迁移可独立应用，未登记的旧 FILE 行为无需 `dblink` 扩展。 [PostgreSQL dblink](https://www.postgresql.org/docs/18/contrib-dblink-connect.html) · [Main 迁移](../../../../CP6.Persistence.PostgreSql/Migrations/Core/20261126132500_ActFileGuardProviderV1.cs)

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

.NET SDK 10.0.404 Release 与 PostgreSQL 18.6。已提交产品候选运行 **32 项通过，0 失败，0 跳过**，其中本卡 9 项、原 FILE 内容／草稿／处置 17 项、WP02-31 受控下载及相应 G05 回归 6 项。带报告的最终提交另跑同一组，用该次 `verification.json` 固定最终 HEAD 和 TRX。产品提交的 TRX 为 `D:/CP6-test-artifacts/WP02-14c/exec-20261126T123129Z/product-r1-20261126T130008534Z/results.trx`，SHA-256 为 `46201ec1b651c463a426bd0c161800fbc9a5b09b460a8ec761d3a171dc0686f8`。复现入口为 `D:/CP6-test-artifacts/WP02-14c/exec-20261126T123129Z/verify.ps1`。启动环境和完整参数记录在各轮 `command.json`。

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

首次全组缺少夹具要求的 `DOTNET_ROOT`，修正复现脚本后复跑。TLS 的匿名 POST 测试按现有 CSRF 前置更新期望，并单独实证无效 Bearer 在服务认证处返回 401。失败原件保留在同一执行目录。受验产品的两侧工作流静态复核 26 条记录，工作流无差异，普通分支／PR 无匹配自动触发。复核用的 `yaml` 与现有锁文件摘要一致，未安装 Vue 工程依赖。

两库使用同一隔离 PostgreSQL 18 试验集群，分别使用不同数据库和真实锁。身份、文件、授权和 CRM 活动占位写为合成，服务传输为回环真实 TLS。远端回执未知使用远端实际提交、丢弃返回结果的测试注入。真实 CRM 生产 schema 和业务采用、生产数据库间 TLS 登记、真实迁移与运行采用保持 `UNPROVEN`／`NOT_RUN`。资料归档后未保留监听或测试进程。

本机 UTC 自 R1 执行开始到产品证据快照 2026-11-26T13:06:41.7938016Z，连同原派工约 270 秒的只读核对，计约 2689 有效秒。最终含报告的候选核对时间见交付回执。任务分支 `codex/wp02-14c` 留在 DEV-2，席位保持登记给 CP6-EXEC，等待总控复核。第一阶段未更新台账，也未远端推送、建 PR、合并、触发 Actions、部署或迁移真实数据库。
