# WP02-13b：审计证据、安全读取及原计划处置

**卡 DONE；SYS-AUD-01 目标 DONE（原 21/21 AC）**。本卡关闭 WP02-13 的三组余项；完整原卡由已合并的 WP02-13 部分和本卡组成。准入 Main `6c013650e0cb92cd86868073ef5d8c93104b8512`；审计产品及测试源码 `1979923c4e9d4de6ffe10fe2c590a29df06f42e3`，合并见 [PR #1248](https://github.com/GTX537/CP6/pull/1248)。

## 卡出口与边界

- 三类审计使用明确事件来源/时间可靠性。HTTP 旁路只接收路由、状态和受信任引用；实际身份、有效身份、服务身份分别存放。登录完成、IAM、平台管理和模拟采用原事务的授权及操作引用；旧记录缺来源时明确标为旧本地时间或证据不足。字段历史保持在原 EF 业务事务内，Machine 保存包括 PostgreSQL `xmin` 与现有并发版本在内的原生摘要，以及真实事务引用。现有三类原证据及旧原回执不回写。
- 原操作、安全、字段入口和平台跨租户入口当前授权后才返回安全摘要、计数及分页。详情、导出、下载分别再核当前操作点及原对象/字段范围。导出以冻结的原生行键及安全结果摘要登记，短期使用，每次下载重新授权；查询和下载不把原字段 JSON、原请求体或原回执当作安全视图。
- 三类审计原生存储可按有效的逐租户政策删除或匿名化。七类保留类别及其授权边界均明确：原 Owner 结果、重放、技术内容和隐私执行证据仍走各自原 Owner，不由通用日志清理改写。兼容 Clear 与后台走同一检查和事务；删除及回执同时提交，发生不确定结果只按原键查询。保留限制和隐私保留与执行在同一 PostgreSQL 授权门下排序；法务规则使用明确的模拟政策，系统没有给真实租户自动设置保留期限、发布政策或给角色扩权。

## SYS-AUD-01 原验收逐条核对

原条目见 [Foundation 原验收目录](../../assessments/20261011/catalogs/foundation.json) 的 `/targets/3/cases/0` 至 `/targets/3/cases/20`；[已接受设计](../../../design-handbook/modules/SYS-AUD-01.md) 不改。原候选文档 `S5_SYS-AUD-01_完整设计候选_v0.1.md` SHA-256 `c9943c2bdead200996e9ce7cf4e9d6de67c62ab28f435086d8d7fd303f72f623`。覆盖卡为 WP02-13 与 WP02-13b；沿用 [父卡已验证部分](../WP02-13/card-closure/DELIVERY.md)，当前台账没有该目标所需的设计修订场景。测试名为下方 TRX 中的原名或唯一前缀；正反场景均用模拟业务数据。

| 原 AC | 适用证据与结果 | 结果 |
|---|---|---|
| AC-AU01-01 | 请求仅保存路由元数据；SSO、Space 实际参数及安全诊断中的敏感样本不进入新技术日志。 `Request_observation_publishes_only_routed_metadata`、`Sso_configuration_never_serializes_client_secret`、`Space_mutation_never_serializes_action_arguments` | PASS |
| AC-AU01-02 | 技术侧保留 UNKNOWN 和原引用，HTTP 状态单独记录；提交应答丢失后凭 PostgreSQL 原键取得原结果。 `Unknown_owner_commit_is_distinct_from_an_http_error`、`AuditV1_worker_lost_commit_acknowledgement` | PASS |
| AC-AU01-03 | 受限搜索、计数、稳定原键分页不泄露其他租户或未授权字段。 `OperationLog_normal_keyword_page_and_clear_preserve_the_other_tenant`、`AuditV1_export_freezes_native_ids`、`AuditList_filters_summary_cap_and_tie_order` | PASS |
| AC-AU01-04 | 旧 Clear 经过当前授权和保留门；原日志延迟故障保存原行，技术日志旁路失败不改主业务结果。 `OperationLog_clear_without_a_retention_policy`、`OperationLog_late_delete_fault`、`Space_db_fallback_failure_console_output` | PASS |
| AC-AU02-01 | 可信平台动作原命令摘要等于数据库原命令表值；真实模拟区分两身份，后台回执保留服务身份。 `Tenant_management_v1_configuration_lifecycle_concurrency_and_original_results`、`Http_round_trip_preserves_identity_cookie_audit_and_effective_role_boundaries`、`AuditV1_worker_lost_commit_acknowledgement` | PASS |
| AC-AU02-02 | 技术遥测和强制原事务证据分级；强制安全和字段审计失败回滚，调用方原先工作可独立提交。 `Unknown_owner_commit_is_distinct_from_an_http_error`、`Create_rolls_back_when_required_management_audit_is_rejected`、`Caller_transaction_keeps_prior_work_after_late_field_audit_failure`、`Http_required_audit_rejection_never_outputs_a_switch_cookie` | PASS |
| AC-AU02-03 | 平台跨租户名单筛选仅依可信目标并重核平台权限；租户安全视图和导出均不返回请求 IP/UA/原上下文。 `AuditV1_actual_platform_summary_filters_only_trusted_targets`、`AuditV1_tenant_security_content_and_export_apply_current_field_policy` | PASS |
| AC-AU02-04 | 新记录用明确 UTC，旧日期右开口径保留；未知时间不伪造，向前迁移前后比较三类旧原行。 `AuditV1_forward_migration_preserves_all_three_legacy_sources`、`AuditList_filters_summary_cap_and_tie_order`、`AuditV1_export_freezes_native_ids` | PASS |
| AC-AU03-01 | 同一对象范围限制摘要、时间线和通用详情；无原业务查询权或无内容权不能看字段历史。 `Field_audit_actual_HTTP_log_query_cannot_bypass_missing_business_query`、`Field_audit_actual_HTTP_only_projects_current_Machine_status`、`AuditList_current_identity_and_action_decide_before_summary_read` | PASS |
| AC-AU03-02 | AuditIgnore 与敏感字段规则覆盖原采集；坏 JSON、合法零变化与无 Owner 映射分开。 `Password_excluded_by_AuditIgnore`、`Secret_field_excluded_by_denylist`、`Field_audit_malformed_and_unmapped_history_is_explicit`、`Broken_or_ambiguous_history_is_unreadable_never_zero` | PASS |
| AC-AU03-03 | 原生 JSON 摘要、投影版本和返回字段分离；实际 HTTP 查询后原历史逐字相同。 `Field_audit_actual_HTTP_only_projects_current_Machine_status`、`AuditList_filters_summary_cap_and_tie_order`、`AuditV1_export_freezes_native_ids` | PASS |
| AC-AU03-04 | 真实 HTTP 修改产生 PostgreSQL 原生版本及已提交事务号；非 EF 写入没有冒充字段审计。 `Machine_owner_actual_HTTP_rechecks_grants_and_never_caches_protected_rows`、`Tenant_management_v1_configuration_lifecycle_concurrency_and_original_results` | PASS |
| AC-AU04-01 | 查询、内容、导出、保留管理分开；受限或缺少明确导出目的/版本不得注册或下载。 `AuditV1_export_freezes_native_ids`、`Field_audit_actual_HTTP_rechecks_both_current_actions`、`OperationLog_existing_entries_decide_current_user_and_action` | PASS |
| AC-AU04-02 | 撤权后重新读取详情、搜索和下载即受阻；原命令的读取只按原键及当前观察范围，无新业务写入。 `AuditV1_export_freezes_native_ids`、`Field_audit_actual_HTTP_rechecks_both_current_actions`、`OperationLog_query_first_completes_before_the_current_permission_revocation`、`Tenant_management_v1_configuration_lifecycle_concurrency_and_original_results` | PASS |
| AC-AU04-03 | 未知投影版本、无持有政策和不受通用清理管理的源均拒绝，无跨类默认政策。 `AuditV1_export_freezes_native_ids`、`AuditV1_policy_change_blocks_old_plan`、`OperationLog_clear_without_a_retention_policy` | PASS |
| AC-AU04-04 | 平台会话和模拟边界实时检查，另一租户和对象行为不可区分；当前字段策略限制随最新权限生效。 `AuditV1_actual_platform_summary_filters_only_trusted_targets`、`Actual_Program_accepts_simulation_round_trip`、`Field_audit_current_object_scope_foreign_deleted_and_missing_are_indistinguishable` | PASS |
| AC-AU05-01 | 7 天只作为模拟的 TECH_TELEMETRY 授权示例，无其他源默认；同计划内只处置授权记录。 `OperationLog_clear_without_a_retention_policy`、`AuditV1_legacy_and_explicit_cleanup_exclude_indeterminate`、`AuditV1_policy_change_blocks_old_plan` | PASS |
| AC-AU05-02 | 保留限制、未决、待重排、争议、原回执引用、警报和外租户原行不被通用清理。 `AuditV1_legacy_and_explicit_cleanup_exclude_indeterminate`、`PrivacyDisposition_cross_operator_tenant_hold`、`PGT017_bank_voucher_preserves_per_line_commits_and_failed_number` | PASS |
| AC-AU05-03 | 两连接实证清理在现有授权门排队，当前 hold 或政策变化使旧计划终止，不修改原证据。 `AuditV1_legacy_and_explicit_cleanup_exclude_indeterminate`、`AuditV1_policy_change_blocks_old_plan`、`PrivacyDisposition_approval_and_global_hold_use_same_actual_authority_gate` | PASS |
| AC-AU05-04 | 最小收据包含处置范围、执行身份、政策和原计划；安全匿名化只删已准字段。 `AuditV1_legacy_and_explicit_cleanup_exclude_indeterminate`、`AuditV1_policy_change_blocks_old_plan`、`AuditV1_worker_lost_commit_acknowledgement` | PASS |
| AC-AU05-05 | 后台真实提交后应用注入 ACK 丢失，重启和旧 Clear 均取回原收据，新增到期行不会借原键重删。 `AuditV1_worker_lost_commit_acknowledgement`、`AuditV1_legacy_and_explicit_cleanup_exclude_indeterminate`、`OperationLog_late_delete_fault` | PASS |

## 验证与任务级审查

- 后端定向回归 **410 项唯一真实通过**，前端 `vue-tsc` 通过，Vitest **8 项通过**。后端运行 `dotnet build CP6.Tests/CP6.Tests.csproj` 和 `dotnet vstest …/CP6.Tests.dll /TestCaseFilter:<记录的过滤器>`。本卡证据目录 `D:/CP6-test-artifacts/WP02-13b/`：`build-final.log`、`build-recheck.log`、`build-migration-fix.log`、`build-security.log`、对应 `*-test-filter.txt`、`final/final.trx`、`recheck/recheck.trx`、`migration-fix/migration-fix.trx`、`security/security.trx`、`simulation/simulation.trx`、`privacy/privacy.trx`；`web-type-acceptance.log`、`web-test-acceptance.log`。预检查失败原件与后续结果均保留，最终统计按测试名用后来通过的具体结果替换，未把重复运行计两次。
- PostgreSQL 18.6，候选和定向修正分别用本卡隔离端口 55618、55620、55621、55622、55623、55624。实际 Main `Program`、JWT、两独立 PostgreSQL 连接、原事务及实迁移。合成业务数据与策略，没有真实个人或企业数据。`Machine` 另核对了字段审计摘要中的实际 `xmin` 和并发版本、真正提交的事务以及非 EF 写入不静默生成历史。保留计划回执明确区分应用注入的 ACK 丢失与实际事务，保留执行顺序与原回执；未模拟生产网络断线。
- 用应用迁移前的真实 schema 填充三类原审计行，迁移后整行原数据、旧时间缺口和目录无自动授权均复核。pgsql `jsonb` 不用文本排序规则；保留完整 EF 模型一致性断言，按已在 Main 的 `20261030010000_LocationDraftV1` 和 `20261103051000_NativeTaskFormToIdentityV2` 补齐快照，未改两条历史迁移、实表或业务行为（`model-diagnostic` 对照 `baseline.log` / `current.log`）。旧迁移夹具使用当时可用列，字段分页样本唯一键不共享。旧的「注销在等数据库门前总是黑名单」断言在未改本卡代码的 WP02-05b 已验证构建也失败（`baseline-logout/baseline-logout.trx`）；现有注册 Web 会话与租户写入共门。新实证分别确认先有退出后拒绝写入，以及写入先持门时原回执持久、退出完成后原会话无法发新命令。原流程未改变。
- 关联回归覆盖租户管理、会话/模拟、IAM、字段审计、死信诊断、隐私保留和银行原生凭证读回。宽过滤器发现 32 项需 CRM 双宿主的既有条件跳过，另有 3 项因缺少独立 CRM 宿主在夹具启动前报错（`Inquiry_export_service_v2_actual_TLS_requires_independent_privacy_authority`、`Inquiry_retention_actual_TLS_controller_to_CRM_current_service_transaction`、`Inquiry_service_context_actual_TLS_controller_to_separate_CRM_submit_transaction`）；两者均未计入。本卡独立 Main 原验收无例外，双宿主采用按对应卡验证。更宽的早期 PGT 预检查使用旧候选，保留原失败 `test-acceptance.log` 并停下剩余非本卡项目；Machine 原失败已定向通过，预检查未计入验收。没有实际生产保留授权、发布、数据清理或外部消费者的新采用。其他定义和已接受设计未改。

## 远端采用与工作区

候选与受保护 Main 的工作流树 `3355235636fd45116f5445fca32fc65e49f13003` 均复用已核对的普通手动触发/生产标记入口；本卡无触发与依赖调整、无远端 Actions 启动或重跑。合并后核对同一候选提交在远端 Main 可达，才核销 [Issue #1240](https://github.com/GTX537/CP6/issues/1240)。台账记录实际准入远端 SHA；新内容的远端采用以该 PR 正常合并和官方 `D:/CP6-test-artifacts/WP02-13b/finish.json` 证明为准。仅对该任务处置本地/远端分支，固定 DEV-1 清洁并释放，保留根工作区的既有文件。

## 预算和下一卡

本窗关闭一张余卡、一项目标，新增拆卡 0。原号 DONE / 完整 DONE **47 / 45**；登记卡 **49/379**、目标 **3/125**（含一个 TEST_ONLY 支撑）。WP02-13b 自官方开工 `2026-11-17T23:22:23.001731+00:00` 到技术核销与目标核对分界 `2026-11-18T01:25:37.5484118Z` 计 7394.547 秒（2.054h），低于剩余卡上限 3.864642h。接续核销上卡提交/合并/席位收尾 150.215 秒，前后用户等待不计。SYS-AUD-01 单独从 125h 目标池扣 388.733 秒，目标累计 0.588h。卡上限仍为 2,052h，不消耗缓冲；计划累计 6.581h，未到 24h 有效时间检查点。本候选后的合并和席位收尾差额仅在后续卡交付时准确结转。

现阶段严格前置完整后最长下游链选 **WP02-06**（43 条边）。在其 DONE PR 重核 SYS-TENANT-01 原验收；本卡不实现其业务。
