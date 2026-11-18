# WP02-14b 文件草稿正式化与受控处置

WP02-14b 交付 Main FILE 提供者。正式化以当前操作者的草稿全集和目标工程版本为依据，按原键返回原回执。处置先持久保存解绑或物理计划，在最终动作前重查授权、保留和真实引用。CRM ACT 及技术发布接收方沿各自原卡采用。 [原任务卡](../../task-plans/WP02.md#wp02-14b) · [原父卡证据](../WP02-14/card-closure/DELIVERY.md)

## 业务结果

| 出口 | 本次交付 |
|---|---|
| 草稿与正式引用 | 实际 API 固定当前 actor、完整有序集合和草稿版本。逐项核原件身份、当前安全检查和字节。工程修订入口以当前对象编辑能力表明绑定用途。在同一个 FILE 事务核对源和目标权限并封存草稿、保存正式引用、原回执及审计。同键同义返回原结果。异义、过版和冻结目标拒绝。历史倒查使用原目标版本，仅返回在当前对象权限内的文件。 |
| 原结果与当前披露 | 复用 Main 原操作账及权限栅栏。提交确认丢失后按原键恢复；旧写权撤销后，仍具当前读权的原操作者可查原件。草稿内容、正式引用、回执、审计和原字节身份可对账。 |
| 保留与完整引用 | FILE 删除权限及源 Owner 当前版本仍须通过。采用已发布的技术内容证据保留策略、数据库时间、FILE 保留、PRIV hold 和原生 AUD 未知、处理中、争议等保护登记。在单一本地 PostgreSQL 下检查当前草稿/正式引用、DOC 原件与衍生、RMA 原证据、FORM 存储路径和旧附件全租户共享路径。读到部分图、旧授权、冻结或保留不足均阻断。 |
| 物理效果与墓碑 | 物理适配使用部署侧登记的独占本地目录及持久存储身份。先保存计划，再持久登记开始；随后复核引用、实时授权、期限和原件摘要，处理准确文件并保存完成回执。开始前遇到原件缺失保留未确认状态。处理中的未知按同一原操作及同一存储身份复核。原文件 ID 与最小摘要留在不可变墓碑，原件不可再下载或绑定。 |
| 现有入口 | 工程、DOC、RMA 与 FORM 继续使用既有原身份。现有文件页明确显示已解绑、等待物理确认与已处置，并按当前状态禁用下载。 |

API 前缀为 `/api/file-content/owner-v1`。`POST /drafts` 保存准确草稿版本，`POST /drafts/bind` 提交完整集合，`GET /draft-operations/{id}` 使用原操作号恢复。`GET /bindings` 使用 `targetId`、`targetVersion`、`targetDigest` 读取准确版本。`POST /dispositions` 接受 `UNLINK_ONLY` 或 `PHYSICAL`，`POST /dispositions/{id}/complete` 持久登记开始并执行已经批准的物理计划，`GET /dispositions/{id}` 查询同一操作的最新确认状态。重复请求处置计划返回原计划收据。

前向迁移为 `20261117220000_FileDraftBindingV1` 与 `20261117223000_FileDispositionV1`。FILE 真实服务需具备新增表的适用读写和 readiness 函数执行权。处置须由现行授权管理员授予 `file-content/delete`，并有已发布且到期的 `TECHNICAL_CONTENT_EVIDENCE` 策略。只有确认归属和引用目录的独占本地存储才配置 `FileContent:Disposition:ExclusiveLocalRoot`，普通共享 S3 与其他未经采用的物理范围保持关闭。新迁移、策略和本地策略样本仅在可丢弃测试数据库执行。 [文件原设计](../../../design-handbook/modules/SYS-FILE-01.md) · [ACT 共同门修订](../../../design-handbook/remediation/07-shared-consumer-mappings.md)

## 本地验证与采用

从 Main `ac52760b9420b221488168d718ae24ae00a4ced2` 接续，产品受测源码 `9c63f7e7c50075fd280869acabeb6ee9d3ee8876`。固定 DEV-2 的 `codex/wp02-14b` 起始基线为 `db7488b69d58657cdfcf25250de72fa5ece3ddff`。并入上游客户端工作流修复后按最新两侧引用核对触发与变更路径。前置 WP02-14、WP02-10 和既有 W189、W190、W285 的远端采用已经核对。 [父卡 PR](https://github.com/GTX537/CP6/pull/1243) · [W189](../W189-01/DELIVERY.md) · [W190](../W190-01/DELIVERY.md) · [W285](../W285-01/DELIVERY.md) · [上游 PR #1253](https://github.com/GTX537/CP6/pull/1253)

| 本地运行 | 结果与适用范围 |
|---|---|
| PostgreSQL 18.6、.NET SDK 8.0.424 与实际 Kestrel | **26 项通过，零跳过**。采用模拟租户、业务、扫描和期限，用真实文件目录确认物理结果。提交响应丢失使用提交后故障注入。集中审查后的受影响用例定向复验，按测试名合并结果。验证全量草稿校验、并发同键、旧目标、未知回执加撤销写权、晚到保护登记、无效存储身份、未开始原件意外缺失、已登记 DOC/RMA 引用、原生 FILE 及 legacy 附件回归。 |
| Vue 文件页现有组件 | **12 项通过**。核对权限拒绝、迟到响应、待检、保留及原操作恢复。依赖严格使用现有 lockfile。 |
| 原始证据 | `D:/CP6-test-artifacts/WP02-14b` 内保留 `native-final.trx`、`review-final.trx`、`visibility-final.trx`、`file-web-final.log` 及精确运行命令。红测确认草稿和处置入口缺失。早期测试编译、临时触发器、复用测试授权记录及 Node 缓存安装错误均保留原日志，最终使用独立夹具和锁定依赖复测。 |

受审候选的工作流树为 `fefb806d1cc0c5b8d5c7169bb015fa7c7c41183a`。普通验证入口保持手动，生产候选仅受保护的发行标签触发。本分支及 PR 无匹配事件。远端采用由正常合并记录和官方 `finish.json` 核对。 [PR #1254](https://github.com/GTX537/CP6/pull/1254)

## SYS-FILE-01 目标核对

逐项核对原 30 条验收、两条 ACT 必需运行修订及采用证据。台账记录 9 条全断言通过。`PE-ACT-FILE-COMMIT-MODE-POS` 要求实际 CRM 本地共同门和同事务提交，`PE-ACT-FILE-COMMIT-MODE-NEG` 要求阻断仅持远端 reserve-confirm 的新提交。两条均登记为 `NOT_RUN`，由 WP03-19 核对实际采用。 [原验收目录](../../assessments/20261011/catalogs/foundation.json) · [修订验收目录](../../../design-handbook/remediation/08-acceptance-catalog.json)

| 原验收 | 实际证据和剩余范围 | 状态 |
|---|---|---|
| AC-FI01-01 | 各真实消费者的声明媒体类型/跨边界流式拒绝仍要按对应合同联验。W189 及本轮已测大小、标签、摘要和无半成品。 | 目标待验 · WP14-08 |
| AC-FI01-02 | W66 的旧 MD5 共用路径与本轮原生 SHA-256/目标当前字段门各自保持。同字节不扩共享访问。 `Upload_SameHash_ReusesStorePath_SaveOnce`、`FileContent_current_parent_field_and_tenant_permissions_protect_history_and_receipts`。 | PASS |
| AC-FI01-03 | 实际上传重复及原键 UNKNOWN、关闭、事务失败保持零正式引用。复用 W285 的实际 FORM 恢复。 `FileContent_concurrent_upload_replay_and_state_compare_exchange`、`FileContent_unknown_close_final_authority_rollback_and_trigger_readiness`。 | PASS |
| AC-FI01-04 | 实际待检、保留和隔离拒绝下载与新入选，页面报告对应当前状态。 `FileContent_pending_and_rejected_scans_never_download_release_does_not_qualify`、`FileDraft_checks_whole_set_and_keeps_exact_prior_target_version`、文件页 Vitest。 | PASS |
| AC-FI02-01 | 原生对象、附件字段、租户及父级实时授权。原件/列表输出验证未泄存储路径。 `FileContent_actual_HTTP_scan_exact_download_and_current_revocation`、`FileContent_current_parent_field_and_tenant_permissions_protect_history_and_receipts`。 | PASS |
| AC-FI02-02 | Main 同角色不同 actor 的草稿无法互读，且范围外成员从绑定列表剔除。ACT 协作者集合及真实消费者仍待证。 | 目标待验 · WP03-19 |
| AC-FI02-03 | Main 按原工程版本稳定读取绑定全集。ACT 受限分页和冻结引用的真实组合仍待核对。 | 目标待验 · WP03-19 |
| AC-FI02-04 | 文件页定向回归验证授权空列表、拒绝/未知读和身份切换后忽略迟到响应。 文件页 Vitest。 | PASS |
| AC-FI03-01 | 本轮再次核对原生新下载的当前限制。ACT 逐请求及续传的当前权和用途组合仍待实证。 | 目标待验 · WP03-19 |
| AC-FI03-02 | W190 与 DOC 回归验证衍生件另身份及精确父引用。原件下载使用附件响应，未授内联预览。 `ControlledDocument_original_derived_bindings_replay_history_and_diff`、`FileContent_actual_HTTP_scan_exact_download_and_current_revocation`。 | PASS |
| AC-FI03-03 | 存储字节不符的实际结果为持久隔离，旧历史保留，下载未转读其他版本。 `FileContent_store_tampering_quarantines_and_never_falls_back`。 | PASS |
| AC-FI03-04 | 实际缩略、OCR、搜索或走发布接收方的入口仍须证明按当前权限过滤。 | 目标待验 · WP03-19 |
| AC-FI04-01 | Main 当前源/目标权限、准确版本及 actor 全集已采用。实际 ACT 目标持共同门时仍需正反验证。 | 目标待验 · WP03-19 |
| AC-FI04-02 | 实际并发请求只产生唯一结果。HTTP 同键异义拒绝，已正式化草稿无法被旧命令覆盖。 `FileDraft_actual_http_binds_exact_set_and_recovers_original_receipt`、`FileDraft_checks_whole_set_and_keeps_exact_prior_target_version`。 | PASS |
| AC-FI04-03 | Main 新目标版本与旧绑定历史保持分开。真实发布后对版本、用途和新批准的组合仍待核对。 | 目标待验 · WP05-34 |
| AC-FI04-04 | 文件意图与原 PostgreSQL 回执区分实际状态。跨仓副作用、真实业务部分接受及原键恢复仍需联验。 | 目标待验 · WP14-08 |
| AC-FI05-01 | Main 实际 AUD 未知保护、真实 DOC/RMA 和活动草稿引用、当前期限参与拒绝。跨域在途义务与冻结恢复仍待组合。 | 目标待验 · WP14-08 |
| AC-FI05-02 | Main 受控本地提供者检查已登记源及全租户路径。实际隐私清单接入外部共享、归档及独立 Owner 的保留决定还待消费。 | 目标待验 · WP02-15 |
| AC-FI05-03 | 实际本地物理效果与 PostgreSQL 解绑、计划、开始及完成回执分离。缺原件保持未确认，原键恢复没有重复效果。 `FileDisposition_actual_http_requires_approved_policy_and_confirms_isolated_physical_result`、`Delete_OnlyUnlinks_AndRetainsPhysicalEvenAfterLastVisibleReference`。 | PASS |
| AC-FI05-04 | Main 保留原身份和最小摘要。隐私擦除后真实恢复路径须按原墓碑及授权证明未复活。 | 目标待验 · WP02-15 |
| AC-FI06-01 | Main 原生门使用对象与字段，旧附件仍可区分。ACT 的实际能力和独立用途绑定仍待实证。 | 目标待验 · WP03-19 |
| AC-FI06-02 | Main 按提供者当前范围过滤绑定列表，无越范围成员和数量；ACT 隐藏成员与只读角色的列表、计数及预览仍待实证。 | 目标待验 · WP03-19 |
| AC-FI06-03 | 已采用 Main 当前安全门。真实发布目标的安全、用途授权与人类/机器执行角色交集仍需逐 Target 核验。 | 目标待验 · WP05-36 |
| AC-FI06-04 | 本地 FILE 事务缺失时无法新绑定。CRM 实际历史读及外域 reserve-confirm 拒绝须按同一 ACT 修订验收。 | 目标待验 · WP03-19 |
| AC-FI07-01 | Main 已核原件与 DOC 具体版本/摘要。完整技术发布目标接纳准确 manifest 的范围仍待验。 | 目标待验 · WP05-34 |
| AC-FI07-02 | DOC 原件与派生保持独立。跨域实际表示、原件与 OCR/转换消费展开仍须沿正式恢复链核实。 | 目标待验 · WP14-08 |
| AC-FI07-03 | Main 同事务当前权限已实证。真实 ReleaseRevision 精确用途与安全共同门须在发布端完成。 | 目标待验 · WP05-34 |
| AC-FI07-04 | FILE 记录原绑定回执。实际 Target 业务采用的用途、scope、对象版本和原操作回执须由接收 Owner 实证。 | 目标待验 · WP05-36 |
| AC-FI07-05 | 新原件及目标版本保留原绑定。发布后变更技术版本或用途须走原 Owner 后继及新批准。 | 目标待验 · WP05-34 |
| AC-FI07-06 | 当前技术文件的 UNKNOWN 与保留证据已实证。跨源必要证据及真实隐私执行与恢复仍待组合。 | 目标待验 · WP02-15 |

### 缺口归属

| 归属卡 | 待验范围 |
|---|---|
| WP03-19 | CRM ACT 在真实业务 Owner 使用 LOCAL_AUTHORITATIVE_GUARD，验证正反两条必需修订、完整集合、目标权限、分页和新请求持续当前授权。 对应 AC-FI02-02、AC-FI02-03、AC-FI03-01、AC-FI03-04、AC-FI04-01、AC-FI06-01、AC-FI06-02、AC-FI06-04。 |
| WP02-15 | 核实已接隐私 Owner 的真实引用清单和文件保留责任，以及共享路径、擦除原收据及恢复墓碑组合。其他业务适配仍按各自原卡实施。 对应 AC-FI05-02、AC-FI05-04、AC-FI07-06。 |
| WP05-34 | 真实 ReleaseRevision 冻结准确技术文件 manifest、版本、用途及目标范围，后继版本和新用途沿原批准。 对应 AC-FI04-03、AC-FI07-01、AC-FI07-03、AC-FI07-05。 |
| WP05-36 | 实际接收 Owner 核当下安全与用途，采用准确目标、scope、版本、原操作并查回原收据。 对应 AC-FI06-03、AC-FI07-04。 |
| WP14-08 | 跨 Owner 的真实部分失败、未知提交、迟到保护及从保留状态恢复，核对实际媒体和原件/派生结果。 对应 AC-FI01-01、AC-FI04-04、AC-FI05-01、AC-FI07-02。 |

## 工作区处置

正常 PR 采用与固定 DEV-2 的结果以 `D:/CP6-test-artifacts/WP02-14b/finish.json` 为准。试验文件、PostgreSQL 与日志保留在本卡制品目录，测试进程停止。固定工作区的锁定前端依赖保留作可复用缓存。GitHub Actions、生产部署和既有数据库迁移均为零。

## 工时与接续

WP02-14b 卡 DONE，WP02-14 全部拆分余项闭环。SYS-FILE-01 逐项核对原 30 条和两条必需修订，9 条具备完整通过证据，21 条及两条修订的剩余出口归入 5 张现有卡。原号 DONE / 完整 DONE 为 **49 / 47**，登记卡 **52/380**，目标 **4/125**。

从约 `2026-11-18T03:08:30+00:00` 的只读准备到 `2026-11-18T04:09:38.1192853+00:00` 计 3668.119 秒，卡预算 14305.753 秒。结转 WP02-09 官方收尾 543.526 秒，WP02-09b 余额 12831.351 秒。目标复核计 124.794 秒，125 小时目标池累计使用 0.663 小时。计划累计 8.567 小时，未到 24 小时检查点。卡池 2,052 小时，缓冲耗用为零。本候选之后的合并和席位收尾按官方 finish 在下张卡结转。

严格前置和最长下游链重排后，下一张可开工卡为 **WP02-15**，全图最长下游链 40 条边。
