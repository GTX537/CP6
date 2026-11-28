# WP05-60 Main 工程自然 Case、NextContext 与受理门

## 范围与严格前置

Main 线 `DEV-1` 由 `CP6-EXEC` 领取，基线为 `40defafc4a36d1aa4dae553a2f6ed5102d78e3fb`（#1342）。四项直接前置 WP02-10、WP02-13、WP02-22、WP05-13 均完整 `DONE`。递归展开去重 37 项，含 WP02-13b、WP02-14b、WP02-09b、WP02-05b。领取时远端同卡分支和 PR 均为零。三项锁 `Main:plm-req-01-provider`、`ProductService.cs`、`ApprovalDispatcher.cs` 与 CRM 在途 WP03-19 的资源无交集。 [原卡](../../task-plans/WP05.md#wp05-60) · [开工核对](</D:/CP6-test-artifacts/WP05-60/exec-20261127T183833396Z/prerequisites.json>)

原卡要求真实 Main 自然 Case 与原 `RequestKey` 目录、M02 首次 `ABSENT` 证明与当前 ES、执行代次，以及和真实 EngineeringRequirement 一起提交的 M03 最终门。`CREATE` 固定首个工程身份，`REVISE` 在同一工程身份形成准确后继。按原键取回受理结果，旧源、旧工程版本及已有效果继续保留。原卡验收为旧上下文拒绝、丢失确认仍只有一个工程身份、真实无 Case 的自然槽证明、CRM 本地冲突门，以及 202 受理边界。 [设计模块](../../../design-handbook/modules/PLM-REQ-01.md) · [CRM HO 合同](../../../design-handbook/modules/CRM-HO-01.md) · [共享消费者映射](../../../design-handbook/remediation/07-shared-consumer-mappings.md)

基线的 WP05-13 已交付 REQ 来源、工程分解和判据。其旧 `scope_reconciliation.remaining` 仍记商业、文档澄清与方法覆盖缺口。后续 `remaining_scope` 已补充部分销售、DOC/FILE 原件、澄清及逐项覆盖证据。真实 CRM 活动、商机源权限、正式裁决与分配、工程采用和专业许可继续由各接续卡完成。WP05-60 的覆盖对应原件回查、保留前版、受理与实际完成分界、制造许可独立四类原 AC 出口，下表逐项登记全链剩余。 [WP05-13 原报告](../WP05-13/DELIVERY.md) · [原 AC](../../assessments/20261011/catalogs/engineering-oa.json)

## 实现与接入

- 在既有 C02 服务凭据路线上添加 M02、M03、M04。Main 从当前服务凭据确定租户和注册的原生 REQ 主体。入口校验签发者、服务主体和原令牌登记，最终事务持有令牌锁及当前 IAM 门。收到的原始字节以租户、原键、合同为密文保护用途，并保存 SHA-256。
- M02 在已确认完整写入方覆盖的自然槽锁内观察当前 ES、前驱终态、执行代次和真实 REQ 当前版本。覆盖由实际源参加者和已登记的 PostgreSQL 目录共同校验，迁移后需要登记才可签发。首个无 Case 上下文使用 `priorExecutionFence.key=ABSENT`，已有终态使用前驱原键，证明版本绑定写入方目录、接收代次和执行代次。两份签名采用 P-256/SHA-256，签名输入分别是 `HO-ENG-MAIN-FENCE-1`、`HO-ENG-MAIN-DECISION-1`、换行和两处 `proofRef` 为空的规范上下文。消费验证使用 `decision.version` 对应的登记公钥。
- M03 在当前源共同门下按自然槽、原键、真实 REQ 的顺序保持 PostgreSQL 锁。入站正文不超过 1 MiB。新效果复核原源全文 HMAC、原前驱、模式、目标版、两份签名及有效期，临近提交再次复核权限与时间。原 REQ 用例在同一事务写来源包和草稿，连同 REQ 原操作、审计、受理结果、原请求及待发送记录一起提交。后继 `REVISE` 保留历史澄清的原源、问题与答复，过期上下文返回 `STALE_NEXT_CONTEXT`。
- M03 的 202 只有 `RECEIPT_ONLY` 和 `UNKNOWN`。当前有权时，同一原始字节恢复首次接收时刻，异文为 `REQUEST_KEY_CONFLICT`。M04 的 `FOUND` 携带准确原结果字节、哈希及签名信任记录。未观察到原请求时，有目录覆盖才返回 `NOT_OBSERVED`，否则为 `UNPROVEN`，两者均为 `noEffectProven=false`。历史原件继续按当前服务、Main 主体及准确坐标验权，包含写入覆盖停用后的有权回读。补件结果继续引用既有工程时，原工程采用的旧源另行验权。受理落到 REQ 的 `DRAFT_ONLY`，业务验证、制造用途独立审批。
- 新增 PostgreSQL 前滚迁移 `20261127190000_EngineeringIntakeV1`，只增加本受理 schema、写入方目录、自然槽、原键、固定终态和待发送记录。迁移回归以基线旧库先写入 REQ 原件及原回执，再前滚并比对旧字节和权限。文件和审批 Owner 的既有入口沿用。
- 真实 OPP、ACT、File、Product 源及其同事务用途门由 `IEngineeringIntakeAuthorityV1` 接入。当前 Program 在缺少该参加者、具体覆盖或可核原件时保持 `AUTHORITY_FENCE_UNAVAILABLE` / `ENGINEERING_INTAKE_SOURCE_UNPROVEN`。本地采用真实 Main REQ 与 PostgreSQL，CRM 源、目录授权与调用数据均标记 `SIMULATED`。实际 CRM 消费和本地冲突门归 WP03-26，当前进展流、原终态纠错和实际投递归 WP05-61 与 WP03-27。 [路由及提供者](../../../../CP6.WebApi/Controllers/Internal/EngineeringIntakeControllerV1.cs) · [事务服务](../../../../CP6.Core/Services/Plm/EngineeringIntakeContextService.cs) · [参加者接口](../../../../CP6.Core/Services/Plm/EngineeringIntakeContractV1.cs) · [迁移](../../../../CP6.Persistence.PostgreSql/Migrations/Core/20261127190000_EngineeringIntakeV1.cs)

## 验证与验收对照

正式验证记录按最终提交 SHA 去重在此报告所指的目录下归档。 `final-<候选前12位>/final-verification.json` 记录完整 SHA、树、工具版本、脱敏环境、实际命令、每组测试名、结果和 TRX 的 SHA-256。四个回归组沿 WP05-08 独立 R3 的精确筛选器在最终候选上运行，审批组包含 PostgreSQL 审批路径和既有工作流用例。首轮执行范围为 Main 服务与数据库合同，R1 的前端复验见下节。 [正式原件根目录](</D:/CP6-test-artifacts/WP05-60/exec-20261127T183833396Z/>) · [正式运行脚本](</D:/CP6-test-artifacts/WP05-60/exec-20261127T183833396Z/run-final.ps1>)

| 原卡验收及专项 | 用例 |
| --- | --- |
| 正向，CRM 准备后 Main 发生变化，M03 拒旧签名 | `Wp0560_changes_to_writer_generation_or_Main_target_invalidate_Prepared_context_and_keep_history` |
| 正向，同一原请求在确认丢失后仍只有一个工程身份 | `Wp0560_lost_commit_recovers_same_key_and_intake_bytes_one_engineering_identity_only` |
| 正向，有写入方覆盖的自然槽首次证明 | `Wp0560_absence_is_signed_only_with_registered_writer_coverage_and_M04_proves_no_absence` |
| 反向，Main NextContext 允许时 CRM 本地冲突可拒 | `Wp0560_Main_context_does_not_clear_simulated_consumer_conflict_and_202_grants_no_manufacturing`，限定模拟消费 |
| 反向，实际 HTTP 202 只返回受理结果，真实 REQ 保留草稿及独立制造门 | `Wp0560_HTTP_holds_current_service_credential_and_returns_only_receipt_then_exact_original`、`Wp0560_Main_context_does_not_clear_simulated_consumer_conflict_and_202_grants_no_manufacturing` |
| 合法后继、并发和原结果一致性 | `Wp0560_needs_info_preserves_closed_selection_and_allows_exact_same_Case_CREATE_successor`、`Wp0560_two_connections_share_the_final_natural_slot_and_original_key_fences` |
| 当前授权、精确全文、旧工程采用源和最后期限 | `Wp0560_latest_source_grant_Main_actions_and_own_tenant_apply_to_write_and_historical_disclosure`、`Wp0560_source_HMAC_and_signed_context_bind_exact_content_and_final_source_deadline`、`Wp0560_needs_info_with_existing_engineering_requires_read_of_the_adopted_old_source` |
| 部分写入回滚、密钥缺失、前滚后原件与授权 | `Wp0560_failure_staging_outbox_rolls_back_native_journal_source_and_original_request`、`Wp0560_forward_migration_preserves_preexisting_native_originals_and_permissions_without_seeding_coverage` |

## PLM-REQ-01 原 AC 剩余出口

基线台账列出的 70 项原 AC 全链均保持 `NOT_RUN`。本卡提供者证据和全链待运行场景逐项列出。 [原 AC 目录](../../assessments/20261011/catalogs/engineering-oa.json) · [原任务与接续卡](../../task-plans/WP05.md)

| 原 AC | 接续卡 | 仍需运行的原场景 | 本卡证据 |
| --- | --- | --- | --- |
| AC-PLM-PG03-A001 | WP05-14、WP03-26 | 真实原渠道在原规格版发问并返回可核答复。 | 后继保留按条款登记的澄清及准确旧源，覆盖 Main 保存和读取。 [定向用例](#验证与验收对照) |
| AC-PLM-PG03-A002 | WP05-14 | 沿真实来源等待答复后尝试确认，保留待澄清与承诺阻断。 | 无直接覆盖。 |
| AC-PLM-PG03-A003 | WP05-15 | 逐条核父项实际采纳的全部子项与准确覆盖依据。 | 无直接覆盖。 |
| AC-PLM-PG03-A004 | WP05-15 | 重叠子项无法补齐另一必需条款，保留关系与缺项。 | 无直接覆盖。 |
| AC-PLM-PG03-A005 | WP05-14 | 批准准确需求集、固定成员和签署范围。冻结与制造授权仍保留各自原结果。 | 无直接覆盖。 |
| AC-PLM-PG03-A006 | WP05-14 | 批准门复核原集合的必需单位歧义，筛选后仍按原成员阻断并可退回。 | 无直接覆盖。 |
| AC-PLM-PG03-A007 | WP05-15 | 对指定需求集和目标汇总准确专业证据，固定满足性签署范围与证据版。 | 无直接覆盖。 |
| AC-PLM-PG03-A008 | WP05-15 | 签署门在报告齐全时仍保留专业 FAIL、未满足及提交任务完成的分立状态。 | 无直接覆盖。 |
| AC-PLM-FB01-001 | WP05-23，许可事实取 WP05-32 | H42 分列计划、试制申请和 REL 许可。只有计划时，WP05-23 的最终准入不发出 MES 根工单请求。 | 无直接覆盖。 |
| AC-PLM-FB01-002 | WP05-24 | H53 的逐要求适用性由 VER 给出 REUSE、RETEST 或 N/A，只有基线变化通知时保留待评。 | 无直接覆盖。 |
| AC-PLM-FB01-003 | WP05-23，联验 WP05-22、WP05-24、WP05-32 | 试制 Owner 贯通 H42/H43/H44/H46 的原申请、许可和根子执行关联。真实投产取执行 Owner，受测对象及专业结果分别取 VER。 | 无直接覆盖。 |
| AC-PLM-FB01-004 | WP05-12，输入取 WP05-18、WP05-19 | 冻结清单核对方案和双摘要，联查源包、原要求、需求集及 ITER 精确送审输入的独立引用。 | 无直接覆盖。 |
| AC-PLM-FB01-005 | WP05-19 | 在审包固定原来源和输入版，新的来源变化可查，采用后继走显式修订。 | 同一工程身份的旧正文、旧源和后继原件均可回读，覆盖 REQ 来源版本。 [R1 后继原件](#r1-修复与验证) |
| AC-PLM-FB01-006 | WP05-12 | 冻结门核双候选双摘要的声明语义，旧通用摘要保留待核而无冻结资格。 | 无直接覆盖。 |
| AC-PLM-FB01-007 | WP05-15 | REQ 汇总分开显示任务提交、证据充分、VER 专业 FAIL 与需求未满足。 | 无直接覆盖。 |
| AC-PLM-FB01-008 | WP05-15，联验 WP05-20 | 整体满足性保留强制失败和复测责任。迭代采用在本 Owner 门复核相同硬约束。 | 无直接覆盖。 |
| AC-PLM-FB01-009 | WP05-15 | 逐目标区分有依据的 N/A 与报告缺证，汇总保留两类原状态。 | 无直接覆盖。 |
| AC-PLM-PG04-A001 | WP03-26、WP03-19 | 用现实来源原件或授权模拟的真实 Owner 链核登记和客户确认分界。 | 模拟 CRM 正文按准确接收键回读，Main REQ 解释与来源条款分别存储。 [定向用例](#验证与验收对照) |
| AC-PLM-PG04-A002 | WP03-26、WP03-19 | 同名后继原件和待补证来源从实际来源入口分别检查。 | 模拟来源的历史版本及后继分别回读，来源缺证时拒新增效果。 [定向用例](#验证与验收对照) [R1 后继原件](#r1-修复与验证) |
| AC-PLM-PG04-A003 | WP05-15 | 提交父项版、子项版和原条款覆盖关系，保留父项原文。 | 无直接覆盖。 |
| AC-PLM-PG04-A004 | WP05-15 | 在正式覆盖评审入口拒绝关系自证循环并保持待评项。 | 无直接覆盖。 |
| AC-PLM-PG04-A005 | WP05-22 | 专业方法和准确来源条件参与判据草案，实测与目标各自读取。 | 无直接覆盖。 |
| AC-PLM-PG04-A006 | WP05-22 | 按专业验证入口重验缺单位时保留待澄清和禁止默认。 | 无直接覆盖。 |
| AC-PLM-PG04-A007 | WP05-22、WP05-24 | 专业 Owner 评审指定版本、单位及工况，固定正式可评价性。 | 无直接覆盖。 |
| AC-PLM-PG04-A008 | WP05-22、WP05-24 | 准确设备参数、原精度依据和评审版本同时核验。 | 无直接覆盖。 |
| AC-PLM-PG04-A009 | WP05-14 | 在真实冲突裁决入口记录重叠范围及两条原必要性。 | `OPEN1` 保留原对端、重叠范围和双方必要性，摘要变化后记待重评。 [R1 后继原件](#r1-修复与验证) |
| AC-PLM-PG04-A010 | WP05-14 | 正式裁决按客户适用范围保留不同原条款和版本。 | 无直接覆盖。 |
| AC-PLM-PG04-A011 | WP05-14 | 来源权威处理正式放宽、隔离或拒绝并保留原证据。 | 无直接覆盖。 |
| AC-PLM-PG04-A012 | WP05-14 | 真实必须条件的正式裁决拒绝优先级或偏离旁路。 | 无直接覆盖。 |
| AC-PLM-PG04-A013 | WP05-14 | 送审固定准确清单、判据和成员缺项，候选后续编辑另形成版本。 | 无直接覆盖。 |
| AC-PLM-PG04-A014 | WP05-14 | 送审及批准门复核成员来源，拒绝通过页面标签伪造批准，保留缺项或退回事实。 | 无直接覆盖。 |
| AC-PLM-PG04-A015 | WP05-14、WP05-18 | 本卡保留原后继；联验已采用的真实工程输入、VER 计划与影响清单。 | 同一 REQ 身份分别读取旧版和后继版，显示需重评的条款、判据、范围和必要性。 [R1 后继原件](#r1-修复与验证) |
| AC-PLM-PG04-A016 | WP05-14 | 请求撤销时记录影响与处置范围，保留既有工单引用，实际停工取执行 Owner。 | 无直接覆盖。 |
| AC-PLM-PG04-A017 | WP05-14 | 受权批准后继要求并向 ITER、VER 记录准确影响交接和实际接收，保留原工单、库存和 VER 事实。 | 无直接覆盖。 |
| AC-PLM-PG04-A018 | WP05-14 | 签署前复核变化后的来源版，旧就绪视图转影响复核并保留送审原件。 | 旧采纳原件继续可读，后继显示原源差异及受影响事实。 [R1 后继原件](#r1-修复与验证) |
| AC-PLM-PG04-A019 | WP05-15 | 分配分别保留 ITER、VER 原请求、真实接收及完成，由各 Owner 提供后续事实。 | 无直接覆盖。 |
| AC-PLM-PG04-A020 | WP05-15 | 传递 ACK 仅证明已发，等待 VER 实际接收或计划，保留另一工程分配的已知结果。 | 无直接覆盖。 |
| AC-PLM-PG04-A021 | WP05-15 | 逐必需条件核对同一 Baseline 的准确计划和结果，独立保留覆盖与专业结论。 | 无直接覆盖。 |
| AC-PLM-PG04-A022 | WP05-15 | 显示错目标或未覆盖条件，保留原关联以待适用性复核，任务完成不足以证明覆盖。 | 无直接覆盖。 |
| AC-PLM-PG04-A023 | WP05-15 | 汇总适用性、证据完整性、专业结果和满足性四维，完整 FAIL 继续显示。 | 无直接覆盖。 |
| AC-PLM-PG04-A024 | WP05-15，适用性事实取 WP05-24 | 后继 Baseline 缺专业适用证据时保持待评，保留原目标证据及已知失败。 | 无直接覆盖。 |
| AC-REQ-001 | WP03-26、WP03-19 | 本卡回读准确原请求；真实原渠道、三条提炼位置与确认分列待双仓联验。 | 按原请求恢复完整接收结果，按原需求读取源原件和所引条款。 [定向用例](#验证与验收对照) [R1 后继原件](#r1-修复与验证) |
| AC-REQ-002 | WP05-14 | 原客户渠道无答复时，从正式需求集入口核承诺门。 | 无直接覆盖。 |
| AC-REQ-003 | WP05-15 | 在正式覆盖链逐条采用完整、可追到组合要求原件的分解。 | 无直接覆盖。 |
| AC-REQ-004 | WP05-15 | 按原目标签署或拒绝时逐项核未分解的父项条件。 | 无直接覆盖。 |
| AC-REQ-005 | WP05-22 | 实际 VER Owner 接到准确可评条件，设计验证方法并返回原计划。 | 无直接覆盖。 |
| AC-REQ-006 | WP05-22、WP05-24 | 在专业验证入口按明确单位准入，保留空单位草案和缺项。 | 无直接覆盖。 |
| AC-REQ-009 | WP05-14 | 在正式来源裁决与集合采用时核不同客户/范围和准确版本。 | 无直接覆盖。 |
| AC-REQ-010 | WP05-14 | 重验正式裁决和采用门不让高优先级覆盖原必须条件。 | 无直接覆盖。 |
| AC-REQ-007 | WP05-18，需求集取 WP05-14 | 从工程候选输入回查所采用的准确 RBS 及每条原成员版本。 | 无直接覆盖。 |
| AC-REQ-008 | WP05-14 | 新版需求集沿后继影响链评估，已有工程 Baseline 保留原集合版本。 | 后继的源变化单独记录，旧 REQ 及旧源原文保持可追。 [R1 后继原件](#r1-修复与验证) |
| AC-REQ-015 | WP05-24，后继要求取 WP05-14 | 受控放宽后重新评价适用性，现行结论按新依据形成，旧 FAIL 仍可回查。 | 本卡验证来源变化后判据与必要性待重评，旧工程原版继续可回读。 [R1 后继原件](#r1-修复与验证) |
| AC-REQ-016 | WP05-14 | 撤销来源生成处置与真实下游义务，活动工单原引用和执行事实继续可追。 | 无直接覆盖。 |
| AC-REQ-011 | WP05-15 | 两个必需条件分别连接同目标的两个准确 VER 结果，再按完整覆盖汇总。 | 无直接覆盖。 |
| AC-REQ-012 | WP05-15 | 单项 PASS 仅覆盖对应条件，另一缺项保留，整体保持待核。 | 无直接覆盖。 |
| AC-REQ-013 | WP05-15，许可事实取 WP05-32、WP05-42 | 签署准确覆盖范围，生产用途仍由 REL 按其条件独立决定。 | HTTP 202 只确认受理，REQ 为 `DRAFT_ONLY`，独立制造门无授权、实际工单零新增。 [定向用例](#验证与验收对照) |
| AC-REQ-014 | WP05-15 | 强制 FAIL 时拒绝整体满足签署，显式保留失败和缺项。 | 无直接覆盖。 |
| AC-PLM-FB02-025 | WP05-22，REL 用途取 WP05-32 | VER 计划独立声明主次验证目标和范围，另引用 REL 的 TRIAL 用途身份。 | 无直接覆盖。 |
| AC-PLM-FB02-026 | WP05-22 | 核计划层级按实际受测对象登记，风险和评审由各自政策裁决。 | 无直接覆盖。 |
| AC-PLM-FB02-027 | WP05-15，专业结果取 WP05-24 | REQ 分开汇总 PROCESS 证据与产品必需性能缺项，保留各自适用对象。 | 无直接覆盖。 |
| AC-PLM-FB02-028 | WP05-15，方法判断取 WP05-24 | 复核提交满足性时的原物理试验要求，演示录像保留为原证据，所缺试验仍阻断。 | 无直接覆盖。 |
| AC-PLM-FB02-029 | WP05-22 | 按总控裁定由验证计划接入准确语义版、显示修订记录和历史回读，语言包可复用 WP02-17。 | 无直接覆盖。 |
| AC-PLM-FB02-030 | WP05-22 | 按总控裁定由验证计划保留旧词条版本引用，并按当前可用性策略选择新计划词条。 | 无直接覆盖。 |
| AC-PLM-FB02-049 | WP05-24，试制许可取 WP05-32 | 设计确认前检验必需试验齐备，独立获准试制仅可用于取得所缺证据。 | 无直接覆盖。 |
| AC-PLM-FB02-050 | WP05-24，正式用途联验 WP05-42 | 固定有证的 E1 确认范围和独立性证据，E2 保持未确认，REL 复核范围上限。 | 无直接覆盖。 |
| AC-PLM-FB02-051 | WP05-24 | 条件确认时核验同一范围的原必需项和不可豁免 FAIL，合法变更须有准确后继依据。 | 无直接覆盖。 |
| AC-PLM-FB02-052 | WP05-24，执行联验 WP05-23、WP05-33 | 确认签署可记录有责任人和验收条件的独立工具实例前置，实际准入仍核当前实例。 | 无直接覆盖。 |
| AC-PLM-FB02-053 | WP05-24，原证据取 WP05-22 | 原试验仪器有效性属于当前证据充分性，未知时阻断设计确认，等待原证据核验。 | 无直接覆盖。 |
| AC-PLM-FB02-054 | WP05-28，设计确认联验 WP05-24 | 关闭水位核对明确要求的真实暴露与现场观察；在当前确认中要求该结果时由 VER 核实。 | 无直接覆盖。 |
| AC-PLM-PG05-X009 | WP05-24，正式用途联验 WP05-42 | 签署前重核 Quality/VER 新事实和原送审快照，关键要求转重评，REL 依据当前准确范围授权。 | 无直接覆盖。 |

## CRM HO 工程链联验

以下为对应原 HO 场景的接续责任。跨仓完整业务链状态均为 `NOT_RUN`。 [CRM 接续卡](../../task-plans/WP03.md) · [Main 接续卡](../../task-plans/WP05.md)

| 原 AC | 接续卡 | 仍需运行的原场景 | 本卡证据 |
| --- | --- | --- | --- |
| AC-HO-046 | WP03-26 | 用真实商机版本从 H15 至 H17 核早期需求和原 unknowns，独立验证商业发起门。 | 模拟 OPP 的早期条款和 unknowns 落到 Main 草稿，原请求可查。 [定向用例](#验证与验收对照) [R1 后继原件](#r1-修复与验证) |
| AC-HO-047 | WP03-26、WP05-32 | 用实际标准品引用和适用范围核供货与制造资料要求。 | 无直接覆盖。 |
| AC-HO-048 | WP03-26 | 两个 CRM 原操作争自然槽，从 H17 到 Main 首个 ES，逐仓核精确写入。 | 两个 PostgreSQL 会话竞争同一自然槽和原请求键，Main 保留唯一工程身份。 [定向用例](#验证与验收对照) |
| AC-HO-049 | WP03-26、WP05-61 | 对 PENDING 或未证执行闭合的真实进展阻后继，再按原键恢复。 | Main 有完整写入方覆盖且当前代次核对一致时才签发证明，拒绝旧代次。 [定向用例](#验证与验收对照) |
| AC-HO-050 | WP03-26 | 本卡验证 NEEDS_INFO 后同 Case 形成 CREATE，接续从真实 CRM 新 Requirement 发起同一条链。 | `NEEDS_INFO` 保留 Case 和前驱，Main 按同 Case 受理后续 `CREATE`。 [定向用例](#验证与验收对照) |
| AC-HO-051 | WP03-26 | 本卡验证 Main 改版拒旧目标，接续核 CRM 准备、发送、收到拒绝及原计划恢复。 | REQ 当前版变化时 Main 拒旧准备结果，重新签发后在同一工程身份建后继。 [定向用例](#验证与验收对照) [R1 后继原件](#r1-修复与验证) |
| AC-HO-052 | WP03-27、WP05-61 | 真实 Progress 先到，等原已接受 Intake 后续办一次。 | 无直接覆盖。 |
| AC-HO-053 | WP03-27、WP05-61 | 真实旧 ES 同义运输只收录原历史，当前 ES 保持。 | 新 ES 接收后仍可按前驱原键回读原 Intake，覆盖 Main 原结果保全。 [定向用例](#验证与验收对照) |
| AC-HO-054 | WP03-27、WP05-61 | 实际规范代次冲突隔离及准确候选否决。 | 无直接覆盖。 |
| AC-HO-055 | WP05-61、WP03-27、WP05-42 | 三用途 readiness 的真实原件、展示和 Main 最终许可逐项读取。 | Main 受理 HTTP 202 保持 `RECEIPT_ONLY`，制造许可由原门独立裁决。 [定向用例](#验证与验收对照) |
| AC-HO-056 | WP05-61、WP03-27 | 真实 STOP 后只接受受权且指向准确前驱的 CORRECT。 | 无直接覆盖。 |
| AC-HO-057 | WP05-61、WP03-27 | INVALIDATE 保留已接受及原工程历史，当前用途从实际授权复核。 | 无直接覆盖。 |
| AC-HO-058 | WP05-61、WP03-27 | 旧请求的晚纠错通过实际影响判断阻当前 Case。 | 无直接覆盖。 |
| AC-HO-059 | WP03-20、WP03-26 | 真实 ACT 来源与 HO 来源保持独立，M05 关联需 Main 原 Owner 证据。现有卡未单列 M05 提供者，建议归 WP05-61 的 Main 原源联验出口。 | 无直接覆盖。 |
| AC-HO-060 | WP03-26、WP03-27、WP05-61 | 实际 worker、人工后继和各域修订在各自锁定入口竞争。 | Main 实测自然槽和原键竞争，REQ 本地修订后旧目标失效。 [定向用例](#验证与验收对照) [R1 后继原件](#r1-修复与验证) |
| AC-HO-061 | WP03-26 | H15/H16 和 H17 同 Case 锁内重核适用冲突，Main 允许后本地仍零新增。 | 模拟 CRM 消费门在 Main NextContext 允许时仍因适用冲突拒 H17，零新增。 [定向用例](#验证与验收对照) |
| AC-HO-062 | WP05-61、WP03-27 | 真实混入另一 Case 的冲突处置整笔回滚。 | 无直接覆盖。 |
| AC-HO-063 | WP05-61、WP03-27 | 真实有限冲突集合由当前 Owner 精确处置，证据和原状态同事务。 | 无直接覆盖。 |
| AC-HO-064 | WP03-27、WP04-13 | CRM 历史应用回执与当前 Case 分读，M07 另走原商业对照证据。 | M04 按 Main 原结果键回读精确字节，覆盖 Main 原件查询。 [定向用例](#验证与验收对照) |

## R1 修复与验证

总控复核要求跨来源版本承继已登记的工程事实，并按 CRM 工程引用原合同解析。R1 从同一 Main 工程身份的锁内目标续写。源版本仅前移时，工程解释、适用对象、必要性、判据、未决关系和已登记澄清继续原样随附。出现补件终态时，内容比较用最近实际采纳的 CRM 原件。 [总控复核](</D:/CP6-test-artifacts/WP05-60/coord-verify-20261127T213111284Z/COORD-VERIFY.md>) · [R1 返工卡](</D:/CP6-test-artifacts/WP05-60/WP05-60-REWORK-R1-CARD-20261127.md>)

- 来源正文变化时，后继在 `sourceReviews` 固定前版 REQ、前后源的准确引用、变化条款及来源字段、受影响的工程字段和判据及关系的标识，处置状态为 `REVIEW_REQUIRED`。新源删去条款时，原判据及关系内容仍随附，已失效的条款绑定和原版分解覆盖以旧工程版为复核依据。现行草稿和版本差异可查看待重评事项。普通草稿保存继续携带这些记录。
- M03 在请求目标和签发上下文的目标中按 1–128 字符不透明 Key 校验工程 ID 和修订。实际写入前以 Main 当前锁内目标和签发上下文逐项核对。合同有效但 Main 未认识的目标返回 `409 STALE_NEXT_CONTEXT`，无新增效果。
- `sourceReviews` 为原生需求 JSON 的可选字段。本轮无新增迁移，历史 REQ 与原请求继续按原坐标读取。正式裁决与真实 CRM 来源参加者仍由对应接续卡交付。

| R1 新用例 | 核验点 |
| --- | --- |
| `Wp0560_successor_preserves_native_facts_and_marks_only_affected_basis_against_adopted_original` | PostgreSQL 前版写入 C1、C2、OPEN1，来源仅前移时原样承继。补件后按真实采纳原件识别摘要变化，C1 与 OPEN1 待重评。数量变化和条款移除影响 C2，原判据、旧源与准确旧绑定可回查。普通草稿入口拒绝伪造处置并保留待重评记录。 |
| `Wp0560_opaque_reference_keys_parse_verbatim_and_unknown_Targets_never_write` | UUID／数字与不透明 Key 的四种组合，128／129 字符及控制字符，请求目标和上下文目标分别验证。Main 最终门拒绝未知引用，核对库中效果计数。 |

R1 正式候选的六组 .NET 使用 .NET 10.0.404 Release 和隔离 PostgreSQL 18。前端从最终候选精确文件运行完整 Vitest、`vue-tsc --build --force` 和 Vite 构建。各组原始结果、前端基线对照及停机回读归档至 [R1 验证原件](<D:/CP6-test-artifacts/WP05-60/exec-20261127T232158434Z-r1/>)。

## R2 后继判据重评范围

条款删除时，现行判据只保留仍有效的绑定。重评记录固定准确工程前版及前后来源，旧工程原件保留被删条款的绑定。后继在同一租户和工程对象的版本链上回读这些原件。只有该判据在来源重评中确实失去的条款才计入历史依据。来源仅前移或交换行顺序时，已有未决记录原样保留。

工程人员重新绑定判据后，后续评估以新绑定为准。建档即无绑定（`null` 或 `[]`）的判据沿用原合同，需求范围变化会触发重评，并保留 `CRITERION_SOURCE_BASIS_REQUIRED`。因删除条款才变空的判据则按其历史依据判断影响，原重评保留 `REVIEW_REQUIRED`。该条款再次出现会引起新的重评，当前草稿的绑定仍由工程人员决定。

PostgreSQL 用例 `Wp0560_successive_clause_changes_keep_pending_reviews_and_scope_each_new_criterion_review` 逐版核对工程 v1–v9 的新增名单、原未决项、旧需求和来源原文。v7 删除 A 后，v8 只改 B 的数量，仅新增 C_B、C_AB，v9 仅前移。v10–v11 另外核对工程改绑、真正无绑定及 A 再出现。 [用例](../../../../CP6.Tests/Erp/EngineeringIntakeCriterionScopePostgreSqlTests.cs) · [总控独立复核](</D:/CP6-test-artifacts/WP05-60/coord-verify-20261128T011246811Z-r2/COORD-VERIFY.md>)

本轮证据目录收录父提交复现、修复后定向结果和最终候选的六组 TRX。前端完整 Vitest、`vue-tsc --build --force`、Vite 构建和主线失败项对照均在 .NET 结束后运行。正式结果见候选子目录内的 `verified-index.json`，本地回收记录见 `workspace-readback.json`。 [R2 验证原件](</D:/CP6-test-artifacts/WP05-60/exec-20261128T030616577Z-r2/>)

## 总控独立复核与同 PR 生效

- R1 `FAIL`。工程后继遗漏已登记的事实，M03 将 CRM 工程编号和修订限制为 UUID 和正整数。R1 返工保留工程事实并按合同解析不透明 Key。 [R1 独立复核](<D:/CP6-test-artifacts/WP05-60/coord-verify-20261127T213111284Z/COORD-VERIFY.md>)
- R2 `FAIL`。P2 已通过。v7 删除 A 后，v8 只改 B 时仍重复列入只绑定 A 的 C_A。R2 返工按当前及历史条款确定受影响的判据。 [R2 独立复核](<D:/CP6-test-artifacts/WP05-60/coord-verify-20261128T011246811Z-r2/COORD-VERIFY.md>)
- R3 `PASS`，阻塞 0。总控在产品提交 `22069080c9bd9aa182cdba6e3737cec82a486d73`、树 `aeba5ba36f1623ef40245c1b6d346c8663d2bb2f` 上原样复跑 v1–v9，并通过额外 14 版组合、P2 字段检查、前滚及两项并发验证。执行侧和独立复核各通过 .NET 399/399，零跳过。两侧 Vitest 均为 3568/3571，三个失败文件在主线复现，`vue-tsc --build --force` 和 Vite 构建均通过。 [R3 独立复核](<D:/CP6-test-artifacts/WP05-60/coord-verify-20261128T043009771Z-r3/COORD-VERIFY.md>) · [R3 验证索引](<D:/CP6-test-artifacts/WP05-60/coord-verify-20261128T043009771Z-r3/results.json>) · [执行侧验证索引](<D:/CP6-test-artifacts/WP05-60/exec-20261128T030616577Z-r2/final-22069080c9bd/verified-index.json>)

| R3 分组 | 通过/执行 | TRX SHA-256 |
| --- | ---: | --- |
| WP05-60 定向 | 16/16 | `e2851ce60294c0f68645cad034d7a806fa009c1e13c77ba44447d6c3408ebfec` [TRX](<D:/CP6-test-artifacts/WP05-60/coord-verify-20261128T043009771Z-r3/intake/results.trx>) |
| WP05-13 需求 | 103/103 | `bfeafce357d14274361c2e074e14edb1998fce64809663cbc51c5daa891fcfde` [TRX](<D:/CP6-test-artifacts/WP05-60/coord-verify-20261128T043009771Z-r3/requirements/results.trx>) |
| WP05-02 产品 | 95/95 | `700a2bc859d61529f06a2221dfe874b916b373aa88190fb37e6edf6d2cb960f6` [TRX](<D:/CP6-test-artifacts/WP05-60/coord-verify-20261128T043009771Z-r3/wp0502/results.trx>) |
| WP05-03 工程 | 44/44 | `fd756ea3849b6f48c351aa803484de3339ac6732e89635ec8627653d489090d9` [TRX](<D:/CP6-test-artifacts/WP05-60/coord-verify-20261128T043009771Z-r3/engineering/results.trx>) |
| WP05-08 工艺 | 69/69 | `2a9a946d17a102cf4f96af350d9e5b3f1fade3be9feb38e91952dd726f2df5f0` [TRX](<D:/CP6-test-artifacts/WP05-60/coord-verify-20261128T043009771Z-r3/routing/results.trx>) |
| 审批与工作流 | 72/72 | `7f4bee25a3ff4f33e8b23115ce48fc51fde576c1caacc827151196c279d00280` [TRX](<D:/CP6-test-artifacts/WP05-60/coord-verify-20261128T043009771Z-r3/workflow/results.trx>) |

总控已推送产品候选并登记 Draft PR #1343。WP05-60 的卡级 `DONE` 在该 PR 合入受保护的 `main` 并核对远端包含已验收产品提交和本次台账后生效。`delivery_evidence.remote_main_sha` 固定待核的产品祖先，实际合并 SHA 以 PR 回执为准。 [PR #1343](https://github.com/GTX537/CP6/pull/1343) · [台账](../../roadmap.json)

`PLM-REQ-01` 保持 `PLANNED`，完成审计为 `OPEN_GAPS`。70 项 PLM 原 AC 和 19 项 HO 全链均为 `NOT_RUN`。WP03-26 承接真实 CRM H15–H17 发起及本地冲突门，WP03-27 承接进展和纠错消费，WP05-61 承接 Main 实际来源、进展与授权纠错。WP05-14 承接正式裁决和需求集，WP03-19 承接真实 ACT/FILE 来源，覆盖签署、VER 和用途确认由前文表格所列责任卡完成。 [PLM 原 AC 剩余出口](#plm-req-01-原-ac-剩余出口) · [CRM HO 工程链联验](#crm-ho-工程链联验)

## 有效时间与预算

| 执行轮次 | 有效秒 | 原件 |
| --- | ---: | --- |
| 初轮 | 9,000.746 | [回执](<D:/CP6-test-artifacts/WP05-60/dispatch-20261127/codex-last.txt>) · [工作区回读](<D:/CP6-test-artifacts/WP05-60/exec-20261127T183833396Z/final-9b66b7487c21/workspace-readback.json>) |
| R1 | 6,608.320 | [回执](<D:/CP6-test-artifacts/WP05-60/rework1-dispatch-20261127/codex-last.txt>) · [工作区回读](<D:/CP6-test-artifacts/WP05-60/exec-20261127T232158434Z-r1/final-c014eb49b48f/workspace-readback.json>) |
| R2 | 5,127.121 | [回执](<D:/CP6-test-artifacts/WP05-60/rework2-dispatch-20261128/codex-last.txt>) · [工作区回读](<D:/CP6-test-artifacts/WP05-60/exec-20261128T030616577Z-r2/final-22069080c9bd/workspace-readback.json>) |

执行侧累计 **20,736.187 秒**。初轮后的独立复核要求补齐工程后继事实与不透明 Key，后续复核要求收窄重评范围。返工与验证时间已计入各轮。M 预算 14,400 秒，超出预算 **6,336.187 秒**计入缓冲，距 21,600 秒拆分线 863.813 秒。本卡新增拆分 0，计划累计 341,742.310 秒，缓冲累计消耗 26,034.515 秒。四项直接前置递归展开并去重后共 37 项（含拆分余卡），均已核对为 `DONE`。 [工时与前置核对](<D:/CP6-test-artifacts/WP05-60/ledger-20261128T063653387Z/input-proof.json>)

## 交接

DEV-1 继续登记 `CP6:WP05-60`，`codex/wp05-60` 关联 Draft PR #1343。执行侧 R2 的精确候选、完整差异、工作流检查、隔离库停机与工作区回读已归档。 [产品候选回读](<D:/CP6-test-artifacts/WP05-60/exec-20261128T030616577Z-r2/final-22069080c9bd/workspace-readback.json>) · [PR #1343](https://github.com/GTX537/CP6/pull/1343)
