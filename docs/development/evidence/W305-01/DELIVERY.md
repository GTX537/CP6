# W305-01 原委派授权与历史职责映射范围核销

W305按原WP12-10三项范围及正反验收核销委派修订、准确实际身份和历史缺证边界，原卡正式DONE；历史转换按原TASK R2 §5.1仍为独立迁移设计，不新增执行入口。

基线 `591ab07d6bd44f68219c69cd40a0bcb06f6be56c`，本对话COORD单人，固定DEV1/Main与DEV2/Handoff。[原卡](../../task-plans/WP12.md#wp12-10)的三项实施范围和四项正反验收逐项核对；原TASK R2 §5/5.1明确“本轮不执行迁移”。计划的NativeTaskHistoryV2占位路径不是当前源码，不能据此新增映射层或把原设计排除的历史转换执行算作当前缺口。

## 原卡验收映射

| 范围／验收 | 已采用的实际行为与来源 |
|---|---|
| grant/revision、范围、UTC半开窗、撤销epoch、原命令审计 | [W277](../W277-01/DELIVERY.md)实际TaskDelegationOwnerV2/Reader：不可变修订、当前指针CAS、准确UTC、单一完整grant的Ordinal选择；flowKeys/bizTypes维度AND、维内OR、至少一非空维及TASK_READ前置。封闭输入、自己/跨租户/失效用户及重叠转授链拒绝；旧无scope/本地时间记录不升级。原操作与授权同事务，失败无虚假权限。 |
| 本人actual=principal、grant/onBehalf为空；显式代理保actual及准确grant版 | W277的App_assignment_keeps_original_principal_with_active_delegate_and_freezes_real_rule_candidates、App_decision_delegated_vote_retains_original_principal_and_rejects_expired_grant、TaskDelegation_native_read_and_ordinary_vote_require_separate_current_actions_and_preserve_both_actors。源任务职责不随代理配送改变；本人/显式代理/SOD与双方当前权限分别裁决。 |
| 普通、串签及后续新轮沿同一职责语义 | 当前FlowEngine.ManagedRuntime下ResolveActualAssigneeAsync直接保路由uid；普通/Serial入口均消费该结果，准确Task/FormTo及stage/round保留。既有APP和FORM实际内核/页面采用见W277与[W278](../W278-01/DELIVERY.md)。这里是当前代码适用性核对，不新增声称一个未运行的串签专属浏览器场景。 |
| 撤销与新办理同门互斥、过期列表与动作一致 | W277 App_assignment_authority_is_same_transaction_and_grant_revocation_cannot_commit_old_proof实际双连接：撤销等待当前事务，之后新资格被拒；同事务用户撤权使旧证明失效。TaskDelegation_current_single_grant_selection_is_ordinal_action_specific_and_expires_at_utc_end及认证HTTP撤销后列表拒绝。既有票/原命令保历史身份，不靠当前grant补旧事实。 |
| 委派不授本人草稿编辑、不扩大业务scope | 当前PersonalFormOwned按实际actor，TASK代理仅进入任务Owner；W277业务类型从冻结APP来源，和FLOW范围求交，当前双方字段/资源资格继续核。授权Owner拒绝act-as转授。任务授权不作为业务撤回/应用授权；真实业务Owner另有独立授权。 |
| 旧自动替换assignee缺证不猜实际人、不改原历史 | [W292](../W292-01/DELIVERY.md)实际旧引擎建任务→Main认证HTTP：准确FormToId、原expected/actual/onBehalf及LEGACY_ACTOR_INCOMPLETE；停用今天grant不改变旧缺证结论，旧链办理覆盖onBehalf后仍不冒充核实职责。旧任务新版context404、非参与人403，无v2票、无读取改写历史。 |
| 历史映射及切换的设计边界 | 原TASK R2 §5.1和W292已明确准确原路由/pin/task/token/stage/round/FormTo/原grant revision、原件hash及同事务switch fence；多个候选或缺原件拒转换。原合同本轮不执行迁移，保旧链或只读。不能因当前角色或管理员身份自动补原principal，也不能声称已有实际历史转换产品能力。 |
| 实际页面采用 | W277授权管理/UTC原精度/未知意图/身份清理组件与真实HTTP；W278真实Chrome登录委托人创建TASK授权、受托人独立登录显式选择身份、读/标读/同意，PG核原职责与实际人及唯一票；W292原历史字段与缺证提示组件。各自模拟业务与实际运行范围保持原报告口径。 |

## 证据适用性与直接前置

当前TaskDelegationOwnerV2、TaskDelegationV2、TaskDelegationPanel.vue相对W277交付内容未变。共享任务运行入口后来增加退回/转交、字段patch等行为，仍由当前ManagedRuntime职责创建、TaskAccess/ApprovalTaskAuthority和最终WF_AUTH核验负责；本次检查上述具体调用链，没有把文件SHA变化当作重跑理由。W292历史读面准确原FormTo与缺证标识仍在InboxService；不把LegacyPrincipalMapping设计当已执行迁移。

S01-02、S02-00及原定义发布WP12-02已DONE，W295准确远端采用已在当前基线中。WP02-10采用已CLOSED的W171 Main当前身份/共同fence基础；WP02-13采用W277实际授权命令、不可变修订、同事务结果/审计、原结果及失败恢复。本次登记该限定采用，不声称整个审计保留/清理卡完成，不删除原前置。

完整准确来源直接引用 `C:/Users/tt/CP6-planning-reports/20261017-foundation/wave305-reconciliation.json`。W277原33项PG、35项Vue，W278实际Chrome和W292实际旧链缺证结果均保原时间与范围；本窗只使用其中对应行为，不把全部业务应用证据算本卡，也不把旧执行改称本轮新运行。早期失败不删；没有新增编译、产品回归、哈希包或重复回执。

## 状态与下一步

原375现32DONE/2IN_PROGRESS/341PLANNED；原59局部8闭环/51剩余。125完整目标0关闭、247正式验收NOT_RUN保持，其他卡不连带关闭。

原卡当前范围无剩余。历史数据实际迁移/转换、其他业务Owner、TASK日历/超时/通知和完整OA-TASK目标仍独立。个人FORM旧历史内容的披露/采用问题属于WP12-03/04，不能通过TASK职责映射解决；后续先回到原FORM合同核定是否真正要求新的历史采用，再处理W306前置或补真实缺口。

一次集中文档/源码适用性与证据审查；新验证为 原WP12-10范围、当前源码适用性、既有真实执行及远端采用祖先、限定前置、文档diff/链接/台账/生成页核对通过；无新增产品测试。 当前有效27162秒，开工26793秒；finish后记录实际耗时。无Actions、工作流变更、生产部署或既有环境迁移。

两仓采用、准确分支处置、工作区干净及官方finish释放以 `C:/Users/tt/CP6-planning-reports/20261017-foundation/wave305-final-verification.json` 最终PASS核销。24h目标持续；下一28800秒校准，本短窗不代替三天。
