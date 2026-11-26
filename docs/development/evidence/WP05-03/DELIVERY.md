# WP05-03 工程修订、工作快照与组成候选

## 准入

Main 线以 `CP6-EXEC` 在 DEV-1 领取 `CP6:WP05-03`，分支 `codex/wp05-03`，派发起点 `09a21c0b066c995bbdc164d4eb30abb0f220fb86`。共享互斥资源为 `Main:engineering-baseline`。六项直接前置 S01-02、S01-07、S02-00、WP02-10、WP02-13、WP05-02 递归展开至 14 项，含拆分余卡 WP02-13b，均为 DONE。各卡交付记录的采用提交是核实过的对应远端主线祖先。WP05-02 的 #1338 合并提交就是派发起点，当时远端同名分支和同卡 PR 均为空。本地候选随后接到合入 #1339 的 Main `54b9375e12a15d56e5885c3dc24f65da2d7d780a`，最终验证包含该主线的共同授权与 DOC 来源依赖。 [派工卡](<D:/CP6-test-artifacts/WP05-03/WP05-03-CARD-20261126.md>) · [前置核对](<D:/CP6-test-artifacts/WP05-03/exec-20261126T155310Z/prerequisites.json>) · [WP05-02 交付](../WP05-02/DELIVERY.md)

总控复核 R1 后在原 DEV-1 和原任务分支续作。复核提交 `4373f7e94be5eaf0c0ee3c575a2717ad65d24f9f` 是 R1 本地父提交及本轮接续时的远端任务分支头。 [总控复核](<D:/CP6-test-artifacts/WP05-03/coord-verify-20261126T174324790Z/coord-review.md>) · [R1 席位接续](<D:/CP6-test-artifacts/WP05-03/exec-20261126T182106Z/resume-r1.json>)

## 原范围与实际交付

- 工程修订、工作与配置快照、组成候选及准确成员引用沿用 W151-01、W152-01、W192-01、W201-01、W202-01 采用的 Main Owner。普通 patch 追加工作版。现有修订页面增加三方差异、来源状态、逐条决定及历史回查。
- 三方比较读取已保存的准确基准、当前和传入候选或冻结基线。比较键为 occurrence 与字段，新增或删除位置按整项显示。逐项保存 `KEEP_BASE`、`ACCEPT_SOURCE` 或 `MANUAL` 及理由。三种值分别来自基准原件、传入原件和用户填写的明确值。服务器复核完整来源、当前授权和 CAS 后，候选快照、谱系记录、回执及审计同事务提交。
- R1 处理工作版本或 ETag 失配。对已保存基准上的有效 `PATCH` 或 `REBASE`，先以原操作键持久化拒绝时的冲突清单，再返回 HTTP 412。逐项保存发生位置和字段、请求所依据的值、先保存值、尝试写入值、准确引用和适用理由。版本失配或 ETag 失配可在字段值一致时留下空清单及两端准确引用。同键同输入重试仍返回 412，异体返回 409，原操作及结束操作接口回读 `CONFLICT`。拒绝分支只写冲突原件，候选版、重基记录和 Main 原回执数量保持不变。
- `GET /api/engineering-revisions/owner-v1/operations/{id}/conflict` 根据原租户及原发起人查询，并复核修订和来源的当前读取权限。页面按原操作键回读，刷新时只恢复最近的操作键，清单内容重新通过服务获取。重基预览提示工程 BOM、制造 BOM、工艺和 DOC 的目标修订归属限制。
- 已有父基线以原生后继回执或改型创建的原引用固定。重基后的组成基准沿工作版本继承，历史重基记录保留准确的三端身份。改型消费核对同产品父基线、准确变更及批准范围，使用合成的同事务源验证正向合同。真实 PLM-CHG 批准与接续由 WP05-26 交付，当前主线在提供方缺失时拒绝改型。冻结后继继续使用现有工程基线入口。制造用途由 WP05-32/42 负责。
- 原在审轮次锁定其准确工作版，冻结原件由既有受控后继门保护。重基即使保留相同内容摘要，也会追加不同候选版。旧 CheckRun 的原件仍可按当前权限回查，当前性变为 `STALE`，`HoldExact` 拒绝继续消费。新工作版需单独检查。迭代候选展示已确认的来源漂移，仍可回查旧检查中的未知项。
- 历史资料由专门动作登记，记录产品原引用、档案提交、原记录键、原件摘要、路径和证据缺项。保存、展示及恢复均核对当前权限。以已登记的准确历史来源创建的新工作稿继承历史谱系，证据状态为 `EVIDENCE_INCOMPLETE`，批准、冻结及用途从新时点按原业务门处理。

[原卡](../../task-plans/WP05.md#wp05-03) · [W310-01 余项](../W310-01/DELIVERY.md#wp05-03) · [设计](../../../design-handbook/modules/ENG-REV-01.md) · [原验收](../../assessments/20261011/catalogs/engineering-oa.json) · [共享消费者修订](../../../design-handbook/remediation/07-shared-consumer-mappings.md)

## 验收与原验收出口

| 原卡验收 | 本卡场景与用例 |
| --- | --- |
| 正向：同产品准确父基线和来源，同命令仅生成一版 | `EngineeringRebase_frozen_parent_and_review_are_exact_and_old_check_loses_current_use` 在真实冻结、后继与原检查链上验证，并通过明确标记的合成变更源验证连续修订、撤销及原键。`EngineeringRebase_HTTP_previews_exact_saved_candidates_and_recovers_the_same_operation` 验证真实 HTTP 原命令和查回。 |
| 正向：同料各 occurrence 分列，逐项保留决定与理由 | `EngineeringRebase_occurrence_fields_keep_accept_manual_and_retain_original_refs` 验证同料两个位置、三个处置、原 Ref、理由、历史工作稿及冲突异体。 |
| 反向：`latest`、缺来源或未知 Owner 不能成为完整成员 | `EngineeringRebase_manual_member_requires_an_exact_known_source_or_reports_a_draft_gap` 和 `EngineeringRebase_rejects_unresolved_exact_refs_stale_frontiers_and_cross_tenant_disclosure` 验证成员和候选引用、缺源投影及权限。原 CheckRun、BOM、工艺、DOC、QUALITY 用例复核实际源采用边界。 |
| 反向：在审或冻结拒绝改写，过期 CAS 不增版 | `EngineeringRebase_frozen_parent_and_review_are_exact_and_old_check_loses_current_use` 和 `EngineeringRebase_concurrent_CAS_writes_create_one_snapshot_and_one_receipt` 验证审批、冻结、旧检查失效及两连接竞争的唯一候选与回执。 |
| 反向：AC-009／TC-018，CAS 拒绝后保存并回读逐项冲突 | `EngineeringRebase_stale_http_recovers_immutable_occurrence_conflicts_by_original_key` 用两名工程人员、真实 HTTP 和 PostgreSQL 原件核对同位置两侧值、原 Ref、理由、同键幂等、不增版本或回执、两次后继写入后字节一致、租户隔离及撤权。`EngineeringRebase_stale_patch_and_etag_only_preserve_typed_values_and_the_actual_head` 验证普通修改、数值类型与仅 ETag 失配。冻结父基线用例附加真实冻结父基线、较新已存候选和 DOC 发生位置冲突。`EngineeringRevisionsView.spec.ts` 验证刷新后回读、拒绝披露及清除页面内容。 |
| 历史接续、权限和迁移 | `EngineeringLegacy_HTTP_records_original_evidence_and_derives_only_a_new_draft`、`EngineeringLegacy_requires_an_exact_same_product_and_authorized_registration`、`EngineeringLegacy_forward_migration_preserves_existing_revision_original_journal_and_grants`。 |

原 AC-001/002/003/004/005 本卡范围内对应身份、父基线／变更谱系、工作与配置历史、准确来源和 occurrence。AC-008 对应旧检查失效，最终冻结的专业条件沿 WP05-11/12。AC-009（TC-017/018）对应实际候选的三方处置、来源漂移、CAS 冲突保存和原键回读。AC-017 对应原冻结和在审保护。AC-024（TC-047/048）对应变更引用、合并决定及漂移拒绝，真实独立 ECO 交接沿 WP05-25/26。AC-027（TC-053/054）对应准确历史证据与明确的新草稿接续，存量分类、真实资料批准与历史采用待实际业务核定。AC-028 验证目标及来源当前权限、原键撤权与租户隔离。整目标状态仍开放，FB01-025/026/027 与多输出 POS/NEG 沿 WP05-02 的核对记录和下列责任卡。

- WP05-10 承接 `ENG-OUTPUT-RELATIONS/1`，核销多输出逐 occurrence 的输入、产出工序、配置与关系证据，提供多输出差异及准确回读。 [WP05-10](../../task-plans/WP05.md#wp05-10)
- WP05-12 承接技术评审和双候选双摘要复核，以及冻结时对 `RelationSet`、`OutputBinding`、原技术清单与原回执的同事务绑定。 [WP05-12](../../task-plans/WP05.md#wp05-12)

## 迁移与验证原件

PostgreSQL 前向迁移 `20261126160000_EngineeringRevisionLineageV1` 追加重基、历史来源和 `revision_conflicts` 表、不可变保护、操作引用和历史登记动作定义。R1 冲突表以租户和操作键唯一，外键绑定两份工作原件，输入与清单字节各自核验摘要。该迁移在 R1 接续时尚未进入 Main。既有工作原件和原操作通过原字节回查，迁移前后的角色授权集合逐项核对。测试以隔离 PostgreSQL 18、回环网络与合成业务数据执行。变更批准提供方也是合成服务。现网采用、生产部署和真实库迁移为 `NOT_RUN`。

首轮工程回归发现迭代候选的 `UNKNOWN` 遮盖已确认的漂移。派发基线的等价构建复现 `IterationCandidate_native_source_drift_requires_explicit_choice_and_preserves_occurrences` 和 `IterationCandidate_stage_hold_and_input_change_do_not_retarget_original` 的原失败。修复后统一按新提交从头复跑。 [基线复现及摘要](<D:/CP6-test-artifacts/WP05-03/exec-20261126T155310Z/validation-baseline-drift.json>)

R1 在改动实现前复现 412 后的回读 404 和浏览器未发起回读。在最终候选统一运行 Release 还原与构建、WP05-02 原 95 项、工程、DOC／QUALITY 及前端验证。最终文件记录提交和树、各组 TRX 用例名与 SHA-256、Vitest、`vue-tsc --build`、Vite 及本轮回环实例停机。 [HTTP 初跑复现](<D:/CP6-test-artifacts/WP05-03/exec-20261126T182106Z/validation-red-tc018.json>) · [前端初跑复现](<D:/CP6-test-artifacts/WP05-03/exec-20261126T182106Z/vitest-red-tc018-node.log>) · [最终原件与摘要](<D:/CP6-test-artifacts/WP05-03/exec-20261126T182106Z/final-readback.json>) · [本地运行脚本](<D:/CP6-test-artifacts/WP05-03/exec-20261126T182106Z/run-final.ps1>) · [工作流核对](<D:/CP6-test-artifacts/WP05-03/exec-20261126T182106Z/workflow-final.json>)

## 交接状态

候选保存在 DEV-1 的 `codex/wp05-03`。席位仍由 `CP6:WP05-03` 占用，候选、工作区清洁性与登记状态以最终读回为准。总控负责独立复核、远端处置和席位释放。 [席位记录与回执](<D:/CP6-test-artifacts/WP05-03/exec-20261126T182106Z/final-readback.json>)
