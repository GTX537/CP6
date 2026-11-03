# W291-01 原WP12-08任务办理范围与真实前置盘点

W291按原卡逐项核销WP12-08任务办理：SFS三版本/APP双版本、服务器实际分型、当前双身份与SOD、轮次计票、原命令/TxD、稳定TaskId及CC拒绝审批均有实际证据；正式整卡前置WP12-04/10尚未核销，保持PLANNED。

基线`27ec7cbeebd639486e8f647ffbfd45640816c62a`。本窗按[原卡WP12-08](../../task-plans/WP12.md#wp12-08)、原OA-TASK R2 §3/5与S3 Common R1核对当前代码、已采用来源和实际结果。本轮新增两条缺失的CC HTTP权限边界测试；没有把旧结果改称新执行。[24小时持续计划](../../planning/20261102-codex-24h-oa.md)继续。

## 原卡逐项对应

| 要求 | 当前实际实现、验收与来源 |
|---|---|
| action-context真实版本，服务端SFS/APP封闭输入 | [W290](../W290-01/DELIVERY.md)统一真实上下文，服务端读取实例实际种类。APP接受原五字段、原Guid身份/canonical/result，禁止patch与第三版本；SFS按原TASK R2补齐task/instance/formData三版本。缺版本428、旧版本412、客户端伪造种类/actor/多余字段拒绝，真实HTTP已执行。|
| actual/principal/effective、原grant与SOD | [W277](../W277-01/DELIVERY.md)封闭TASK授权、UTC半开区间与当前双身份交集，保原职责/实际Actor/grant revision；实际APP assignment SOD及两连接撤权最终fence证据保留。W289真实SFS提交时授权过期整体回滚，同意图恢复保实际/原身份；W290 APP授权过期拒绝及撤权后原件读取。|
| token/node/stage/round计票，新轮不复用旧Approved | [W282](../W282-01/DELIVERY.md)真实原生退回/转交轮次；W289 `NativeTask_node_return_preserves_old_votes_and_prevstage_reenters_a_new_round`定向再采用通过。W290串行/并行会签真实kernel保留每个原职责，并验证全部实际票据、终态事实、outbox和原命令同TxD。|
| SFS可写字段重算，APP冻结输入/终态原子 | [W289](../W289-01/DELIVERY.md)节点/IAM/代理字段交集、精确数字/文件/字典、只读隐藏保留、三版本补丁和决定同事务，故障全部回滚。W290 APP禁止dataPatch，WFC alias仍在原AppCommand行/TxD中，无第二套完成记录。|
| 并发只有一个合法决定，原命令返原receipt | W289 SFS两连接竞争修改和投票/转交，仅一胜；W290 APP任务及实例版本竞争一胜一412，刷新后其余职责才可投票。双方未知提交均原查询/原意图恢复，无新命令伪装原结果；实际页面链与PG事实核实。|
| 合并列表保稳定TaskId | [W276](../W276-01/DELIVERY.md) `NativeInbox_merged_expanded_CC_and_current_hidden_search_share_the_exact_authorized_set`：展开/合并返回相同真实TaskIds、分页无同项，CC计数独立，授权过滤先于搜索分页。当前身份与显示分组不产生新任务身份。|
| CC不得审批 | 本轮新增 `TaskCard_HTTP_SFS_copy_recipient_can_read_but_cannot_decide_even_with_menu_approve_permission`、`TaskCard_HTTP_APP_copy_recipient_can_read_frozen_business_but_cannot_borrow_another_duty`。实际登录仅CC用户，保其菜单approve；CC列表/业务详情可读，但全部任务无办理资格，上下文和携合法原任务body的决定均404，pending及投票/命令保持。两项真实PG/HTTP通过，无mock Owner。|
| 缺双token/客户端contextKind不能绕旧单票入口 | W290新统一入口分别核真实版本与封闭合同。W289旧SFS二版本入口对新写410，原结果恢复保留；APP原接受五字段入口继续强制双版本。当前旧TaskDecisionService在写入前调用FlowEngine.RequireLegacyRuntime，真实APP/FORM分别拒绝APP_DECISION_OWNER_REQUIRED/FORM_DECISION_OWNER_REQUIRED，不能走旧单票。|
| 既有页面适配和正反验证 | W290实际Chrome的APP办理与原件恢复、SFS三版本编辑、目录/代理运行三链，以及相关Vue和应用类型检查。W289冲突保编辑、手动重载、未知结果锁定原意图及精确字段链已通过。[BUG730](../BUG-730/DELIVERY.md)随后修复SFS评论1000 Unicode码点的页面及三张PG投影容量，实际Chrome/PG回归通过；本窗不重跑有效结果。|

## 直接前置与证据适用范围

S01-02、S02-00、WP12-07已DONE且准确远端来源在当前主线。WP12-04的真实个人FORM同事务启动及历史/原件已在[W275](../W275-01/DELIVERY.md)交付并由W289实际采用；WP12-10的封闭TASK grant与当前撤权写者已在W277交付。只引用任务办理实际需要的已交付子范围，不将这两张提供者整卡自动标完成。WP02-10采用W171的可信租户/当前授权/全部writer共同fence；WP02-13限定条件是实际Owner原结果和审计与投票共同事务，采用W275及W290的实际记录和回滚证据，不等待无关全域审计保留功能。

本窗核对准确来源提交/远端主线祖先关系、原卡各项与当前调用链；旧结果索引保留真实运行时间/失败记录。W276/W277后的相关变化分别由W289/W290覆盖；BUG730仅评论码点容量与界面已定向验证。测试文件本轮只新增两个CC负向行为，不改产品/配置/依赖。一次任务级集中审查覆盖全部分支diff、入口权限、真实身份、无效果断言、报告与台账。新执行与复用索引：`D:/CP6-local-test/wave291/foundation/final-validation.json`，来源/整卡核对：`C:/Users/tt/CP6-planning-reports/20261017-foundation/wave291-reconciliation.json`。

## 真实剩余和交付状态

原375仍28DONE/2IN_PROGRESS/345PLANNED；原59局部4闭环/55余项。125完整业务目标0关闭、247正式运行验收NOT_RUN保持；其他OA卡与部署状态不连带完成。

本卡实施/验收条目已有证据，但正式DONE仍需原定义中的WP12-04与WP12-10整卡前置核销。首次台账检查明确拒绝用未定义的局部采用替代这两个整卡依赖，本窗保留原定义/校验器和PLANNED状态；该失败是状态准入检查，不是产品测试失败。接续逐项核实提供者整卡，再决定是否闭环WP12-08。原OA-TASK目标的退回/批量/期限等卡仍分别核证，审批完成、引擎终态、运输接收与业务Owner应用回执分轴。模拟用户/业务输入，实际隔离PG18和Main HTTP；未部署生产、未迁移既有环境、未运行Actions。原59局部清单本窗不减少，375任务和125/247清单保持。

总台账/生成进度/四份状态同步；Main/Handoff准确远端采用、任务分支处置、干净工作区与官方finish见`C:/Users/tt/CP6-planning-reports/20261017-foundation/wave291-final-verification.json`最终核销。提交前不提前声称席位释放；24小时目标继续，接续原卡真实余项。
