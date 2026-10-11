# CRM-OPP-01 商机、销售阶段与成交投影

整理状态：`core_semantics_consolidated`。当前完整设计为 v1.1.2，准确 SHA-256 `bdc91e2093023cf77ff76fc77339f215eb3ffe9ec8c6510a04ca6f605de5530f`、350401字节、3845行。主文仍保留 RECOVERY_REVIEW_CANDIDATE、DRAFT、PROPOSED 和“未经接受”的历史标签，后继 UA 已接受同一原字节及 D-OPP-01～14；这些标签不能用于撤销当前接受。54项业务验收仍 NOT_RUN。[接受范围][U9]

## 1. 目的、操作者与 Owner 边界

销售从 Lead 原转换或显式新建得到商机，保存需求、独立预测和资格评估，推进销售阶段，向原 CRM-HO 交付准确需求，再消费 Main 的正式报价与订单商业确认。CRM 负责销售判断和归属投影；Main 负责报价、订单、确认、变更和取消。一次发布、QuoteAccepted、OrderCreated 或最后一条取消消息，都不能替代完整成交判定。[业务链][S32]

本域拥有 Opportunity 身份、需求/预测版本、Stage、Assessment、跟进/失单/目标决定，以及对已登记商业责任组的当前贡献投影。Lead 转换协调器拥有原转换槽和清单；Account/Contact 拥有主数据与资格；Activity 只供420消费明确 OBJECT 引用；CRM-HO 拥有唯一 Request/Outbox；Main Quote/Sales 拥有正式业务结果。库存、履约、收入与回款不属于商机成交额，CRM 不反向改订单或凭证。[权威边界][S40]

普通销售可在自己的授权范围维护需求/预测和跟进；资格/阶段、失单、失单纠错、目标修订、归属纠错、归档各有独立域能力。工程/商业消息由受信服务身份消费，不能用销售 Bearer 作为 Producer。某个 Account/Contact 可见不会自动赋予其商机读写权。[权限入口][S283]

## 2. 当前原件、评审与接受覆盖

有效组合：主文 v1.1.2、CURRENT104行、UA48行、最终限定独审34行。FINAL 独审结论 `REVIEWED_FOR_DELEGATED_ACCEPTANCE`；UA 随后以 `USER_AUTHORIZED_DELEGATE` 接受准确原件。四组已闭合：D01 Stage目标门/可空Assessment、D02同Handoff补件原子pending替代、D03错误Intake拒绝后继及第二责任组首次恢复、D04完整订单声明范围的确认覆盖。[最终独审][R17]

旧 `SR-20260930-A-CRM-OPP-MD01` 在 a3622fa8 的评审已由 a57cfadc 撤回，拟加分未应用；新评审不复活旧事件。v1.1.1 主稿/INDEX 的接受时状态仍 MISSING_SOURCE/UNVERIFIED，本接受不证明历史原件找回。Payload/STATE 当时 null 也不改变准确设计已接受。PB01历史分数是文档成熟度事件，不是业务执行。[历史效力][R27] [保留门槛][U37]

D-OPP-01～14已接受；真实工程、Lead转换、HO/Main、IAM、Quote导航、安全、跨Owner纠错、字典策略、证据与运行 O-OPP-01～11仍未验证。跨客户重归属、零额另一套Won判据、FX汇总、跳链/换代、Opportunity原生Activity写均不在当前默认范围。[接受决定][U20] [范围外能力][S3777]

## 3. 数据实体、字段、金额和身份

| 实体/版本 | 必要字段及持久责任 | 不变量 |
|---|---|---|
| Opportunity / OpportunityRevision | Tenant/P；AccountId、DepartmentId、OwnerId/TeamId可null；Title、DisplayNumber可null；SalesRevision、Need/Forecast/Stakeholder版本；Stage/Pursuit/Lifecycle、原创建与审计 | 标题非唯一，首版不自建业务号；完整销售快照逐版保留 |
| Requirement / Line | summary；decisionPath/noDecisionDateReason；lineId、ordinal、description、customerItemCode、productReference；quantity/unit、needByDate | 原Handoff引用准确旧版本；新行服务端ID；销售单位标签不冒Main批准UOM |
| Forecast | estimatedNet Money可null、probabilityPercent可null、expectedDecisionDate；ForecastVersion | 预测与Main报价/订单分离；不由Stage或Won写100% |
| StakeholderSet / Stakeholder | SetVersion、Account、摘要；ContactId/RoleCode/ContactRevision、可空RelationId/Revision | 零Contact合法；角色不是互动参与、外联许可或Contact主要关系 |
| Qualification / StageHistory | Assessment身份/版本、五答案与证据、Need/Forecast/Stakeholder/Account资格/Policy绑定；目标Stage、QualificationRequirement、AssessmentBindingState | PASS还要CURRENT；目标不要求Assessment时存成对NULL，不伪造PASS |
| LossDecision / LossResolution | lossAt、原因、证据、pending知情集合、ObservedCommercialRevision；VOID/REPLACED/RESUMED/WIN_AFTER_LOSS后继 | 原Loss不覆盖；迟到确认不被Lost丢弃 |
| TargetBaseline | baselineId/version、Need/ForecastVersion、正/零目标Money、reason、sourceHandoff、createdAt | 首次交接冻结；预测后改不追改分母，显式REVISE_TARGET追加版本 |
| OpportunityOrigin | Tenant/LeadAuthority/LeadId/PRIMARY_OPPORTUNITY唯一槽，ConversionId、原版本/选择摘要/Manifest/Opp | 转换清单与商机同CRM事务，不按消息Key防第二商机 |
| CommercialIntent / HandoffLink | 完整冻结意图密文、原版本/IntentDigest；HandoffId/RequestVersion/IntentId/目标经营主体/路由信任 | HO持请求主账；每版意图不可覆盖，补件仍同Handoff |
| HandoffCurrent / PendingVersion / Supersession | 当前RequestVersion/CurrentPendingVersion/SlotVersion；CURRENT_PENDING/SUPERSEDED/RESOLVED；完整新旧前驱和HO证明 | 同Handoff最多一个current pending；旧NEEDS_INFO保留；旧结果不能复活旧pending |
| Intake/Quote投影与OwnerResult | 准确自然流、Producer/Contract/Epoch、ordinal/前驱业务摘要、当前指针、不可变结果密文；Quote版本净额/源 | 不以最高字符串版本或到达顺序选头；报价不增加商业贡献 |
| CommercialGroup / Result / OrderRef / Member / Retirement | Group自然键、经济范围、原Source、币种策略、规范流；全体订单/份额/确认/取消/退休；准确ChangeProof | 完整当前组快照替换；一个经济份额至多一当前组；退休键不复用 |
| EconomicClaim / GroupContribution | 精确经济份额自然键；商机/组贡献C/X/E与可用性 | Claim防跨组双计；Opp根下重算所有摘要，不反向锁其它Group |
| CommercialProjection / History / WinRecognition / Attribution | CommercialRevision、当前/已知/lastProven金额、Outcome、首次识别、目标覆盖；原/后继归属证据 | 当前未知不补0；首次Win历史保留；同责任重归属不成为新销售 |
| Command/Audit/Outbox/Inbox/Conflict/ResolutionLedger | 原O与HMAC、完整收件密文/Trust、业务/运输/冲突身份、处置原结果 | 本地一次效果不等跨库exactly-once；安全摘要不足以恢复原消息 |

完整表/列/类型在1264～1372、约束/索引1374～1414。SQL Server是建议物理适配，不是实际库存在性声明；所有内域FK带Tenant，历史NO ACTION，无跨Main FK。Hash索引命中后还要逐字段核完整自然身份；外部Key采用Ordinal/BIN2，不能不区分大小写合并。[物理模型][S1264]

Title NFC trim1～200；summary1～4000；line.description1～1000；单次最多100行；quantity decimal(19,6)字符串且与unit成对可空；reason1～1000；证据≤20。金额单值decimal(19,4)、合计decimal(28,4)，wire十进制字符串，不经JS Number；币种由版本化CurrencyPolicy给minorDigits0～4，拒绝负数/指数/NaN/超精度/空白，不自动修好。公开请求≤256KiB；内部完整组≤1MiB且订单/成员/退休每类≤200，不能静默分片应用半组。[字段][S228] [大小/完整组][S1030]

比较口径 `NET_EX_TAX_AFTER_DISCOUNT`：Main已作为合同对价确认的净额，扣折扣不含税；运费仅在Main明确计入成员一次时计入。每成员 C≥0、0≤X≤C、E=C−X；每币种对**当前选中成员**求和，新快照替换本组后重算，禁止总额+=消息金额。不同币种不换算，UNKNOWN不是0。销售预测加权仅显示 `ROUND_HALF_EVEN(estimatedNet×probability/100,minorDigits)`；不重算Main已冻结价格/税额。[金额规则][S149]

## 4. 四轴状态、资格门和本域操作

生命周期 ACTIVE/ARCHIVED、跟进 WORKING/STOPPED、五级SalesStage、商业Outcome独立。首次已验证/准确归属/当前有效正Main净贡献即WON，不要求目标100%；零额确认只显示 zeroValueConfirmation。取消至零成为NO_ACTIVE_WIN并保留历史Win，不自动Lost/恢复跟进。[四轴决定][S95]

当前Outcome优先级固定：适用冲突→CONFLICT/isWon=null；缺链/关键证据不可证→UNKNOWN/null；已登记可证范围任一币种正E→WON；否则有效人工Loss→LOST；否则历史曾Win→NO_ACTIVE_WIN；否则已提交待回或Draft/Created→PENDING_CONFIRMATION；其余OPEN。scope=`REGISTERED_RESPONSIBILITIES_AS_OBSERVED`；独立未回Handoff增加pending，不抹掉已证正贡献，也不证明全部范围完成。lastProven是历史，不能显示为当前总额。[判定顺序][S129]

| Stage | 目标门 | Assessment记录规则 |
|---|---|---|
| DISCOVERY | 可见、ACTIVE、合法身份 | NOT_REQUIRED，AssessmentId/Revision成对NULL |
| QUALIFYING | summary非空、Account当前新业务资格 | NOT_REQUIRED，不因未评估阻止合法进入 |
| SOLUTION | 当前适用PASS、至少一个交付描述 | REQUIRED，准确Assessment及Revision、BindingState=VALID |
| NEGOTIATION | 累计SOLUTION门＋金额/货币/决策路径齐 | REQUIRED，不要求已有Quote |
| COMMERCIAL_REVIEW | 累计NEGOTIATION门＋交接准备可证、WORKING | REQUIRED，阶段本身不发Handoff |

前进可跨级但核目标累计门；退回也核目标门并需原因，不撤外域事实。STOPPED先显式RESUME；旧Qualification变STALE后Stage保留并标CURRENTLY_BLOCKED，后续前进/交接重新核。目标Policy决定NOT_REQUIRED/REQUIRED，不能由客户端上传；历史评估不因低阶段不要求而被删除。[阶段及D01][S327]

`OPP-QUAL/1`五项：NEED_DEFINED、CUSTOMER_IDENTIFIED、CONTACT_PATH_KNOWN、BUDGET_RANGE_KNOWN、DECISION_PATH_KNOWN。企业联系路径说明可替至少一个Contact角色，但不是外联授权。后端先判结构缺失MISSING，再明确NO/否决FAIL，再必要证据UNKNOWN，最后全有据YES→PASS。preview只读；明确Record可保存FAIL/UNKNOWN历史，但不准推进。Requirement/Forecast/Stakeholder/Account资格/Policy变化使旧Assessment失效；普通Title/Owner变更不重写评估内容，当前主体权限仍核。[资格算法][S319]

| 操作 | 写集和效果 | 关键拒绝/保留 |
|---|---|---|
| 原生Create | Head、Revision1、Requirement1、Forecast1、StakeholderSet0/1、Audit、最小事件、原O一次事务 | 已有可引用Account必需；Contact可0、预测可null；任何明示子项失败整意图0；无Lead/客户/Main/Activity/Handoff写 |
| Profile | 仅title/department/owner/team，真实变动SalesRevision+1 | Department变化须用户明确合法Owner或null；无变化只命令结果 |
| Requirements/Forecast | 对应新子版本＋SalesRevision；旧外部Source仍指旧版 | 需求完整行集，无法看全不可把遮罩写回；Forecast不改Target/Quote |
| Stakeholders | 完整授权集合token，精确旧版，新Set和SalesRevision | token5分钟且绑定完整Scope/Account/成员；无法完整授权不给替换token；已交接/商业/Quote绑定后普通换Account拒绝 |
| Qualification/Stage | 预览无永久ID；Apply锁内重核依赖后Assessment或StageHistory＋SalesRevision/Audit/事件/回执 | 加密自含token，不能apply内部重评救旧token；相同Stage no-op |
| Lost | 明确原因/lossAt/证据/全部可知pending精确知情；LossDecision＋SalesRevision/Audit，重算解释，Pursuit=STOPPED | 当前正额/UNKNOWN/CONFLICT拒绝；无法披露必需pending不能省略放行；不发Main cancel |
| LossCorrection | 指定旧Decision、VOID或REPLACE；替换与旧决定后继同事务 | 不删旧行；新Loss必须仍通过当前零额门，不制造现正贡献的Lost |
| STOP/RESUME、ARCHIVE/RESTORE | 独立决定和销售修订 | STOP不等Lost；RESUME明确解除旧Loss当前适用性；ARCHIVE需STOPPED；RESTORE不自动RESUME/恢复资格；后台仍收Main事实 |
| REVISE_TARGET | 双前驱＋当前需求/预测＋原因，新Target版本、SalesRevision和CommercialHistory | 只改变比较分母，不能制造Won；商业UNKNOWN时覆盖仍UNKNOWN |

[本域写][S743]、[预览/确认][S756]、[Loss与跟进][S767]、[目标修订][S781]。

首次Handoff以明确非null Forecast冻结TargetBaseline1，同事务保存；目标0可交接但NOT_COMPARABLE。只有当前E可证、目标>0、同币同basis且无其它币种贡献才比：E=0 NONE，0<E<target PARTIAL，E≥target TARGET_REACHED；字段金额不可见则不返ratio。TARGET_REACHED不是需求数量/技术/履约全部完成。[冻结目标][S162]

Lost之后合法确认追加WIN_AFTER_LOSS、使原Loss当前解释被商业事实后继，原行不删，Pursuit仍STOPPED；再取消至零不会复活旧Loss，需新明确决定。firstWin.recognizedAt为首次CRM裁决时刻，mainEffectiveAt另存Main事实时间；晚到更早事实不覆盖首次识别。[失单后继][S775] [首次识别][S1059]

## 5. API、H02、交接、报价和商业合同

公开根 `/api/crm/v1`；内部路径已写完整，不能再叠公开根。Tenant/Subject来自会话/服务映射；Cookie按平台CSRF/Origin。所有人工写Idempotency-Key；既有商机单个强If-Match，缺少428，weak/多tag/`*`拒绝；Create无虚构ETag。完整DTO入口373～511，严格未知/重复字段、遗漏/null/[]区分；普通读取遮罩结构不能直接回写。[wire约定][S362]

| 操作族 | 正确接口入口 |
|---|---|
| options/选择器/查询 | GET `opportunity-options`；POST `opportunity-options/accounts/query`、`contacts/query`、`owners/query`；POST `opportunities/query` |
| 建立/编辑 | POST `opportunities`；GET/PATCH `opportunities/{id}`；GET `edit-context`、`stakeholder-edit-context`；POST `stakeholders`、`requirements`、`forecasts` |
| 资格/阶段 | `opportunities/{id}/qualification/preview`、`apply`及GET qualification；`stage/preview`、`apply` |
| 交接/报价/商业 | `handoffs/preview`、`apply`及GET handoffs；GET quotes/commercial；POST commercial/groups/query |
| Loss/跟进/目标 | `loss/preview`、`apply`；`loss-corrections/preview`、`apply`；`pursuit-decisions`；`lifecycle`；`target/preview`、`apply` |
| 归属纠错 | POST `commercial-attribution/preview`、`apply`，不在单商机路径暗改另商机 |
| 历史/RQ/导航 | POST `opportunities/{id}/history/query`、`commands/query`、`owner-navigation`；GET `opportunity-commands/{operationKind}/{key}` |
| 服务消费 | POST `/internal/crm/v1/opportunity-intake-results`、`opportunity-quote-results`、`opportunity-commercial-results` |
| 精确冲突处置 | POST `/internal/crm/v1/opportunity-conflict-context/query`、`opportunity-conflict-resolutions` |

同一族带`{id}`的后续相对名继续在`opportunities/{id}/`下；准确方法/DTO/能力逐项见515～598及1134，表中未增加原文之外新路由。[完整端点][S515] [冲突端点][S1134]

**H02。** 原Lead协调器＋OPP事务参与者，同真实CRM DbConnection/DbTransaction创建。业务槽 `(Tenant,LeadAuthority,LeadId,PRIMARY_OPPORTUNITY)` 独立于人工Key/MessageId；同冻结选择返回原Manifest，不同选择冲突。OPP只写自己的Head/子版本/Origin，原Lead清单/关联/审计/回执一并commit，不另HTTP创建、异步补建或自己commit。需要新Account/Contact时原Owner用例同事务参与，不能复制DTO绕规则。现有自带事务ExecuteLeadAsync需要明确Enlist边界，不能嵌套后称已满足。原Facade的Prepare/Convert是应用合同，本稿未定义新的生产BFF路径。[H02完整类型与算法][S699]

H02本稿源自较早Lead消费背景；当前Lead已选CP22，落地前需逐字段对齐其已接受转换manifest/schema/真实Enlist证明，不能因两边同名H02便复制旧Facade当当前公共wire。[跨模块入口](<D:/CP6/docs/CP6_开发设计文档_20261010/modules/CRM-LEAD-01.md>)。这是本次整合待核项，尚未判定存在不可调和冲突。

**H03/HO。** HandoffPlan固定 `INITIAL|SUPPLEMENT`。INITIAL prior=null；SUPPLEMENT prior必须7项：handoffId、requestVersion、mainReceiptId、intentDigest、intakeOrdinal、intakeBusinessDigest、handoffSlotVersion，来自受权GET handoffs当前行。intakeBusinessDigest是保存的业务摘要，不能用运输BodyHash或客户端重算隐藏结果替代。服务从准确Requirement/Forecast/Stakeholder版本重建完整`OPP-INTENT/1`，冻结目标Owner/经营主体/Account、processingType=ESTIMATE_AND_QUOTE、请求人/原因/证据新用途；SHA256 `OPP-INTENT-CANON-1\n+Canonical(完整意图)`只标身份不授权限。[交接DTO][S793]

初次交接要求适用PASS、COMMERCIAL_REVIEW、ACTIVE/WORKING、非null预测、实际HO合同及交接能力；不要求CRM先主动建BP，Main按H04解析伙伴。证据必须准确版本和COMMERCIAL_HANDOFF新用途，旧Retain/Download不等可传Main。无附件路径可以独立，明确证据项失败不能删项后成功。[准备门][S860]

补件必须同targetOwner/经营主体/Tenant/Opp/Account/processingType；当前版仍NEEDS_INFO且current pending、无冲突、未登记下游Group/Quote。原HO `StageWithinTransaction` 同CRM事务分配同Handoff下一连续RequestVersion、新IntentId/EventId并签完整Supersession。它若独立部署/无法同DbTx参与，本版COMMIT_GUARD_UNPROVEN且所有写0，不能以“已有可靠Outbox”替代。[HO原子边界][S919]

补件提交原子写新Intent/Link、原HO Request/唯一完整Envelope Outbox/授权回执、Supersession；v1 pending→SUPERSEDED，新v2→CURRENT_PENDING，Current头/Slot+1，唯一HANDOFF pending索引原位切v2，SalesRevision/Audit/Changed/读取水位和原O；商业视图确有变化才商业修订。旧Intake Outcome及原O回执不改；Target不追改。202仅LOCAL_REQUEST/UNKNOWN；旧v1晚回只入旧历史/冲突，不能清v2或复活v1。两客户端旧prior最多一个继任，不生成v3。[完整写集][S924]

**Intake。** `OPP-INTAKE/1`规范流键为Tenant/目标MainAuthority/Handoff/RequestVersion；Producer/合同/Epoch来自发送时Owner路由批准，初始ordinal1，连续previousOrdinal/previousBusinessDigest。ACCEPTED、NEEDS_INFO、REJECTED_FINAL各有真实证据；NEEDS_INFO新SUPPLEMENT，已ACCEPTED/最终拒绝后改变输入的新申请是新INITIAL意图；纠正错误最终拒绝必须专门有据后继，不能普通ACCEPTED覆盖。[Intake类型][S872]

**Quote。** `OPP-QUOTE/1`按Tenant/MainQuoteAuthority/LegalEntity/QuoteKey规范流。每QuoteVersion的净额/币种/口径/源不可变，新版明确supersedes且前驱已保存，不按v10/v2字符串排序；不同Quote不互相覆盖。当前版本只支持net，tax/gross必须null。Quote状态后继不改该版本金额，QuoteAccepted不Won；接受报价/建单只安全Main导航并由Main再次授权。Quote可先于Intake到达而有据保存，不凭此制造Intake回执。[报价合同][S934]

**H08商业快照。** `OPP-COMMERCIAL/1`包含 CommercialBinding、Sequence、resultKey/effectiveAt/changeKind/reason、完整orders/members/retirements、COMPLETE_CURRENT_GROUP coverage、ChangeProof及Owner时刻。GroupNaturalKey=(Tenant,authority,legalEntity,groupKey)，绑定原完整SourceHandoffRef、客户商业意图/经济scope、BP opaque string、一币种净额策略和规范流；本地归属后继不改Main originalSource。[完整wire][S973]

经济份额自然键 `(authority,legalEntity,order,line,schedule,economicSlice)` 全租户至多一当前组；同完整customerIntent/economicScope也不能换groupKey重复注册。Order/members/coveredOrderKeys严格一致，需求line来自原source准确NeedVersion，不从当前同名行猜。没有ConfirmationRef必须C=X=0，DRAFT/CREATED不能有正贡献；缺成员不默认取消。[商业校验][S1032]

OBSERVE仅未确认观察；CONFIRM不减少C/增加X/退休；CANCEL只增准确X；AMEND允许有据商业改价/变更；CORRECT指实际历史result纠错；REPLACE仅同经济责任搬迁。REPLACE新成员每个share指本次SUPERSEDED旧成员，新share按每旧成员C/X分别守恒、每新成员也等自身share总和；旧键永久退休。普通变更不得CORRECTED_OUT；任何一项非法整组0，不先加合法订单。[变更类型与守恒][S1042]

订单头CANCELLED不推出E=0或confirmationCoverage=NONE。Verifier对准确OrderVersion完整声明范围提供互斥且穷尽的 liveConfirmed、extinguished、neverConfirmed 三集合：live为空→NONE；live非空且其它非空→PARTIAL；仅live且覆盖完整→COMPLETE；缺证→UNKNOWN/UNPROVEN，相关组当前金额不可证。首次40/100是PARTIAL而无需虚构取消；确认100已履行40取消60仍E40、PARTIAL/WON；零价存活范围不因E0自动NONE。订单确认覆盖与CRM目标覆盖是两件事。[范围验证][S1502]

**有据纠错。** 当前Intake REJECTED_FINAL与真实Main Confirm矛盾，必须保留两份原结果并使相关商机CONFLICT。`IntakeCorrectionSuccessor`是同OPP-INTAKE/1封闭联合分支：kind=INTAKE_CORRECTION、outcome=ACCEPTED、replacesOutcome=REJECTED_FINAL；准确拒绝前驱/MainReceiptId、连续ordinal、CurrentCorrectionRevision0→1、未用过新receipt、完整source、真实MainConfirmationEvidenceRef、精确resolvedConflicts及原MainOwner的INTAKE_CORRECT_REJECTED_FINAL授权。本版只允许首次纠错，不普通反复重写。Main证据必须定位完整有据Confirm/CORRECT原消息，不可Quote/Draft/Broker ACK。[纠错wire和守卫][S886]

纠错与缓存真实Group的首次应用/复用、经济Claim、Contribution、精确冲突、匹配当前版本pending、CommercialHistory/FirstWin/Loss后继、两份Inbox同事务；原拒绝原字节保留，SalesRevision/Stage/Pursuit/Lifecycle不变。另一GB只在CONFLICT Inbox尚无Group时，必须用 `OPP-RESOLUTION/1 ACK_INTAKE_CORRECTION` 显式派发1～20准确候选，完整回读原Main payload/Trust，首次建真实Group/贡献；不能只清冲突等普通WAITING扫描，也不能填伪0组。[整笔纠错][S1143] [第二组恢复][S1157]

普通Resolution仅处理明确Conflict id/hash/incomingDigest和source/slot/保留结果。REJECT_CANDIDATE否定异常候选不应用；ACK_CANONICAL_CORRECTION只确认已合法应用的CORRECT，不再改金额。普通高序合法消息不自动消OPEN冲突，未列问题仍OPEN；服务可信不等有处置权限。[精确处置][S1106]

## 6. 事务、幂等、并发与故障恢复

人工O=(Tenant,OperationKind,IdempotencyKey)，绑定Actor、目标路由、规范body、IfMatch/token/前驱。`OPP-CMD-CANON-1`保留遗漏/null、数组意图顺序，UUID小写/UTC毫秒、文本NFC/trim、decimal不经浮点、opaque字节不改；HMAC目的隔离并存算法/密钥版本。当前身份＋独立RQ先回原成功/终态拒绝，不重做当前Active/用途检查。NOT_OBSERVED/IN_PROGRESS/EXPIRED_UNPROVEN均不证明最终未执行。[命令裁决][S1174]

新执行在唯一键/缺行范围锁下放未提交占位，最终守卫通过才实体/历史/事件/COMMITTED同commit，没有先commit的永久PROCESSING。领域拒绝在业务写前仅保存REJECTED_FINAL，FinalityDomain=OPPORTUNITY_LOCAL，不证明Main未执行。异常回滚并弃context；commit断开新连接查同O，不自动新Key/新预览或412合并。[新执行与未知][S1183]

后台锁序：原Handoff身份槽→必要Intake源槽→准确Group/Quote/经济Claim→受影响Opp根按ID→投影/Inbox→ReadGeneration。各Group写者在Opp根下维护Contribution摘要并从所有摘要重算，不在持Opp根时反向锁其它Group或远程Main，避免两个组互等。GroupHead/Result/Claim/贡献/历史/FirstWin/Loss后继/Inbox一次提交，不先ACK后尽力补金额。[商业锁序][S1051] [共同事务][S1416]

三身份分开：TransportKey=Tenant+Producer+Contract+Epoch+EventId，BodyHash核原字节；业务身份=自然流＋ordinal，另一Event同业务只别名、同序异义隔离；ConflictKeyHash排除运输ID/到达时刻，同业务冲突不重复推进CommercialRevision。BindingDigest、CandidateStateDigest、BusinessDigest各有 `OPP-GROUP-BINDING/STATE/BUSINESS-1` 规范前缀与明确成员；业务时间/金额/source不因去重被排除，proof载体与运输ID才按规范排除。所有摘要不是授权。[摘要规范][S1189]

完整RawPayloadCipher/KeyVersion/BodyHash/ReceivedTrust必须与分类持久后才ACK。WAITING_SOURCE/WAITING_PREDECESSOR重启/前驱到达/5秒每批20建议扫描时解密原消息，核当前信任，按同锁序内部续办；不能因Inbox已有EventId就duplicate早退。缺密钥/原载荷/认证保持UNPROVEN，篡改隔离，不用当前Main GET或摘要构造原消息，不再Confirm Main。[收件恢复][S1204]

补件commit前全回滚仍v1；commit后原O回同v2/Supersession、同EventId继续投递；链有缺环报HANDOFF_CHAIN_INTEGRITY_UNPROVEN不猜身份。Intake纠错和ACK_INTAKE_CORRECTION分别进入专门内部续办，完整Resolution与Main候选都要可恢复；任一候选失败不能先清冲突。相同处置历史优先幂等，不同旧前驱不覆盖新槽。[专项恢复][S1252]

出站 `crm.opportunity.changed.v1` 只安全ID/版本/字段代码；HO才有唯一完整HandoffEnvelope。商业投影变更不发Main Confirm/Cancel，避免反馈循环。Outbox建议30秒lease、1/5/30/120/600秒退避、20次dead，只同event继续；Broker ACK不是业务受理。Lost/STOP不构成远端防晚执行fence，本版没有cancel-unobserved；RequiredFenced不能由本地O墓碑推断。[投递边界][S1242]

## 7. 租户、权限、隐私与金额遮罩

沿既有 `crm-opportunity:query/add/edit/accept-quote/create-order/view-pii` 与400/410/420，内部域能力映射由真实IAM确认，不新增全局万能ActionCode。OWN、明确DEPARTMENTS、授权TENANT；Team有独立成员证据，部门不默认子树。Owner=null只允许明确部门/Team范围，不公开。原结果RQ默认本人且独立于实体query/edit/Active；身份过期连RQ关闭。[权限模型][S283]

金额权与Outcome权独立；策略允许可只显示WON而遮金额，但不得用排序、ratio、sum、错误或精确组数旁路。无法看全相关组时currentAmounts=null、knownAmounts=[]、lastProven.amounts=[]并标redactedFields，不把可见部分加成“全额”。系统可信服务仍记录真实Main事实，不受销售用户当时PII权阻止。[金额投影][S1061]

跨Tenant/不可知对象404，可见无动作403；公开body不接Tenant/Actor/Won/Main金额/后台认证。敏感详情、预览、回执与错误no-store；草稿/冻结敏感body仅原tab内存，切Tenant/退出/失权清缓存/预览/金额/旧响应，不写URL/localStorage/普通日志。历史理由/旧预测/Contact标签仍按当前权限，原结果不能复原已擦PII。[页面安全][S182] [历史隐私][S1221]

## 8. 页面与连续业务链

正式路由：MSBBCR400 `/crm/opportunities`、410 `/crm/opportunities/new`、420 `/crm/opportunities/:opportunityId`。420 overview/requirements/qualification/commercial/activities/history只是tab；编辑/Stage/Lost/纠错用抽屉，没有新增edit路由或一级菜单。400列表/看板同查询，五Stage列默认ACTIVE/WORKING，WON/PARTIAL是独立标签，不成为列。默认最窄scope、ACTIVE、30行、updatedAtDesc。[页面定义][S172]

410明确已有Account、Owner/Department、可选Contact角色、需求、预测；无Account导航原Owner建立后重读，保存里不隐建。420将预测/正式Quote/确认C/取消X/有效E分栏，外部未知只影响相应区块。拖拽先回弹/影子卡→edit-context→预览→确认，412/403/UNKNOWN不让卡片留目标Stage；UNKNOWN冻结原O先查询。[交互状态][S176]

列表cursor绑定Tenant/ActorScope、ReadGeneration、AccessStamp、筛选/排序/pageSize/末位键；UpdatedAt＋UUID BIN2补序，预计日期null最后。相关事实/权限变更→CURSOR_STALE；公开generation为主体范围opaque摘要不泄隐藏变化次数。看板先Opp去重再分Stage，不能Order JOIN出重复卡片；Group分页仅查看，不用可见页计算全额。[分页][S1236]

420活动只调用ACT Timeline host=Opportunity/本ID，命中明确OBJECT引用；添加Stakeholder不会带入该人全部互动。更正活动转原Account/Contact宿主。Main Quote/Order/Handoff导航通过实际注册路由解析准确key/version，NOT_CONFIGURED不能猜URL或按钮伪成功。[Activity边界][S351] [导航][S598]

连续链A是H02唯一商机→需求/预测分别版本→记录资格→阶段累计门→HO本地202→Main Intake→Quote v1/v2→真实商业Confirm，最终SalesRevision和CommercialRevision分别演進；各次Main结果不生成新的人工编辑。链B的当前C/X/E为40/0/40→100/0/100→100/10/90→100/40/60→95/40/55→95/95/0→人工Lost→95/85/10，最后WIN_AFTER_LOSS但Pursuit仍STOPPED。此序列表为规范算例，不是运行结果。[连续链A断言][S1880] [完整金额序列][S3136]

## 9. 源码映射与整合差异

本次未重新审计当前业务源码。原Spec在固定 `CP6.CRM@c778a3052a4416b82facb07b1244fc98fb6ad8a1` 有界读取C03、MapCrmEndpoints和LeadService：C03 ErpOrderCreated仅有ResultVersion/OrderKey/QuotationKey/BusinessPartnerKey/BookedAmount/Currency，缺有效Confirm、取消份额、OrderVersion、经济覆盖。此证据范围只支持“旧Created不能直接Won”，不支持“所有分支都无OPP实现”。当前整合工作树基线另为 `90c871fe571fd6b390f53e8678376d7ce60bcb60`，未以SHA存在冒功能实现。[固定源码来源][S3805] [旧代码差异][S3821]

原文S08将83331字节Account v1.0作为已接受依赖引用；当前根选择的CRM-ACC有效输入是R01/R02/R03及UA/CR组合。本整理继续以根选择组合为ACC权威，不凭OPP来源表给那个独立草稿另授当前接受；需要逐项核Account引用/Guard语义是否完整落在当前组合。[OPP源表][S3809] [当前Account](<D:/CP6/docs/CP6_开发设计文档_20261010/modules/CRM-ACC-01.md>)。

适配顺序建议先核真实Lead Enlist、HO同事务参与和Main商业范围/签名三条关键合同，再映射现表/统一执行器/锁序，最后接UI与真实恢复。复用ACT运输键/冲突去重/完整Inbox，但**工程终态冻结不能变成订单永远不能取消**；OPP明确有据AMEND/CANCEL/CORRECT/REPLACE，二者在各自Owner范围成立。[消费差异][S1508]

## 10. 验收、异常矩阵与当前执行状态

原40项AC＋REV1 AC041～054共54项全部NOT_RUN；80段JSON解析/受影响digest重算是历史文档静态证据，未运行数据库、消息、HTTP、UI或生产签名。本轮只读取与整理，没有执行源脚本、编译、测试、数据库或消息命令。[独审执行边界][R25]

| AC范围 | 需要实际观察的结果 |
|---|---|
| 001～008 | 多实例H02同槽一Manifest/Opp；创建完整意图0/1；同O响应丢失不重建；需求新版本不改旧Handoff；已有外部绑定不得换Account |
| 009～014、041～042 | MISSING/FAIL/UNKNOWN/PASS；过期/撤权零Apply；目标NOT_REQUIRED成对NULL合法；REQUIRED准确已保存PASS；看板/按钮同门 |
| 015～019、043～046 | 202仅本地请求；同Handoff NEEDS_INFO→补件v2唯一pending；两O争旧prior最多一个；旧结果不清v2；Quote版本不相加/Created不Won；每写点与commit故障验证 |
| 020～029、052～053 | 正40/目标100＝WON＋PARTIAL；取消只减当前责任；全部归0保历史；REPLACE份额守恒；CANCELLED仍有效40；零额与存活范围分开 |
| 030～040 | 完整原链缺版恢复；重复冲突/epoch隔离；金额遮罩；多币无FX；同客户跨Opp归属原子；归档后服务仍消费；Target修订只改比较 |
| 047～051 | 错误REJECTED_FINAL有据后继0→1；权限/源/前驱错误不越拒绝；未知/两worker同纠错一次；未列问题仍冲突，旧版纠错不清v2 |
| 054 | GA已恢复100、GB只有Conflict候选，无Group：ACK_INTAKE_CORRECTION完整首次建GB40，全部写集原子；重放不重复40，缺链不先清冲突 |

每项同时核API/UI、持久身份/历史/版本与禁止副作用，不以HTTP200代替。准确Given/When/Then在3620～3734；完整连续JSON样例尚有本轮逐字段未审范围，见阅读证据。[完整AC][S3620]

连续wire补充下列可直接用于实现与断言的检查点，合成证明和摘要不是真实Owner签发。

| 原流程入口 | 准确身份／状态／恢复行为 |
|---|---|
| H02转换与主机会 | Convert输入保留leadVersion、已选账户／联系人和机会种子；新OppId只从共同commit的ConversionManifest取得。摘要移除短期leadReadToken及proofRef载体，保留业务证据身份；同转换槽同选择返回原Manifest，不生成ERP客户。[原文 L1561](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/bd/bdc91e2093023cf7__CRM-OPP-01_商机与销售管道_前后端开发Spec_v1.1.2_RECOVERY_REVIEW_CANDIDATE.md:1561>)、[原文 L1567](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/bd/bdc91e2093023cf7__CRM-OPP-01_商机与销售管道_前后端开发Spec_v1.1.2_RECOVERY_REVIEW_CANDIDATE.md:1567>)、[原文 L1570](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/bd/bdc91e2093023cf7__CRM-OPP-01_商机与销售管道_前后端开发Spec_v1.1.2_RECOVERY_REVIEW_CANDIDATE.md:1570>) |
| 需求→预测→评估→阶段 | requirementVersion、forecastVersion分别到2；销售修订逐步推进，商业修订不随预测变化。QUALIFYING可按目标阶段明确NOT_REQUIRED且assessmentId=null；COMMERCIAL_REVIEW依累计门及当前Assessment，不自动交接。[原文 L1604](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/bd/bdc91e2093023cf7__CRM-OPP-01_商机与销售管道_前后端开发Spec_v1.1.2_RECOVERY_REVIEW_CANDIDATE.md:1604>)、[原文 L1669](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/bd/bdc91e2093023cf7__CRM-OPP-01_商机与销售管道_前后端开发Spec_v1.1.2_RECOVERY_REVIEW_CANDIDATE.md:1669>)、[原文 L1909](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/bd/bdc91e2093023cf7__CRM-OPP-01_商机与销售管道_前后端开发Spec_v1.1.2_RECOVERY_REVIEW_CANDIDATE.md:1909>) |
| 同Handoff补件 | prior完整绑定旧MainReceipt／IntakeOrdinal／BusinessDigest／IntentDigest／SlotVersion；新requestVersion2在同一Handoff下替换pending1→2。保存SupersessionProof与CurrentPendingVersion，旧NEEDS_INFO别名只DUPLICATE，新版本初始MainOutcome=UNKNOWN。[原文 L2041](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/bd/bdc91e2093023cf7__CRM-OPP-01_商机与销售管道_前后端开发Spec_v1.1.2_RECOVERY_REVIEW_CANDIDATE.md:2041>)、[原文 L2094](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/bd/bdc91e2093023cf7__CRM-OPP-01_商机与销售管道_前后端开发Spec_v1.1.2_RECOVERY_REVIEW_CANDIDATE.md:2094>)、[原文 L2259](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/bd/bdc91e2093023cf7__CRM-OPP-01_商机与销售管道_前后端开发Spec_v1.1.2_RECOVERY_REVIEW_CANDIDATE.md:2259>)、[原文 L2301](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/bd/bdc91e2093023cf7__CRM-OPP-01_商机与销售管道_前后端开发Spec_v1.1.2_RECOVERY_REVIEW_CANDIDATE.md:2301>) |
| 错误终态拒绝恢复 | INTAKE_CORRECTION明确replacesOutcome=REJECTED_FINAL、predecessorResultId、correctionRevision及完整同源MainEvidenceRef；resolvedConflicts绑定准确conflictKeyHash/incomingDigest。第二责任组首次恢复需后继ACK_INTAKE_CORRECTION准确指定组、候选及当前SlotVersion，不借另一组的纠错直接放行。[原文 L2532](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/bd/bdc91e2093023cf7__CRM-OPP-01_商机与销售管道_前后端开发Spec_v1.1.2_RECOVERY_REVIEW_CANDIDATE.md:2532>)、[原文 L2764](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/bd/bdc91e2093023cf7__CRM-OPP-01_商机与销售管道_前后端开发Spec_v1.1.2_RECOVERY_REVIEW_CANDIDATE.md:2764>) |
| 报价与成交分轴 | Quote v1=105、v2=100只改Main报价引用。Order草稿／CREATED确认身份为空，不能判零价成交；真实确认后保留已注册责任范围，firstWin独立，销售概率仍60。两个有据不相交组A40+B60达目标100。[原文 L1843](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/bd/bdc91e2093023cf7__CRM-OPP-01_商机与销售管道_前后端开发Spec_v1.1.2_RECOVERY_REVIEW_CANDIDATE.md:1843>)、[原文 L1849](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/bd/bdc91e2093023cf7__CRM-OPP-01_商机与销售管道_前后端开发Spec_v1.1.2_RECOVERY_REVIEW_CANDIDATE.md:1849>)、[原文 L2905](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/bd/bdc91e2093023cf7__CRM-OPP-01_商机与销售管道_前后端开发Spec_v1.1.2_RECOVERY_REVIEW_CANDIDATE.md:2905>)、[原文 L2917](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/bd/bdc91e2093023cf7__CRM-OPP-01_商机与销售管道_前后端开发Spec_v1.1.2_RECOVERY_REVIEW_CANDIDATE.md:2917>)、[原文 L2923](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/bd/bdc91e2093023cf7__CRM-OPP-01_商机与销售管道_前后端开发Spec_v1.1.2_RECOVERY_REVIEW_CANDIDATE.md:2923>) |
| 取消、Lost及有据恢复 | A取消10后有效90，A取消40后60，B纠正60→55后55，B全取消55后NO_ACTIVE_WIN。显式Loss核Sales6/Commercial9后才LOST／pursuit STOPPED；有据纠正取消55→45恢复有效10，商业回到WON而pursuit仍STOPPED，firstWin不重造。[原文 L2951](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/bd/bdc91e2093023cf7__CRM-OPP-01_商机与销售管道_前后端开发Spec_v1.1.2_RECOVERY_REVIEW_CANDIDATE.md:2951>)、[原文 L2957](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/bd/bdc91e2093023cf7__CRM-OPP-01_商机与销售管道_前后端开发Spec_v1.1.2_RECOVERY_REVIEW_CANDIDATE.md:2957>)、[原文 L2963](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/bd/bdc91e2093023cf7__CRM-OPP-01_商机与销售管道_前后端开发Spec_v1.1.2_RECOVERY_REVIEW_CANDIDATE.md:2963>)、[原文 L2973](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/bd/bdc91e2093023cf7__CRM-OPP-01_商机与销售管道_前后端开发Spec_v1.1.2_RECOVERY_REVIEW_CANDIDATE.md:2973>)、[原文 L2982](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/bd/bdc91e2093023cf7__CRM-OPP-01_商机与销售管道_前后端开发Spec_v1.1.2_RECOVERY_REVIEW_CANDIDATE.md:2982>)、[原文 L3131](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/bd/bdc91e2093023cf7__CRM-OPP-01_商机与销售管道_前后端开发Spec_v1.1.2_RECOVERY_REVIEW_CANDIDATE.md:3131>) |
| CANCELLED订单头两支 | confirmed100/cancelled60、当前范围保留40可为PARTIAL且pendingCommercialDecision=false；cancelled100且全确认范围灭失才NONE/0。必须核declaredOrderScope、liveConfirmed／extinguished／neverConfirmed集合和candidateDigest，不能仅按订单头CANCELLED归零。[原文 L3322](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/bd/bdc91e2093023cf7__CRM-OPP-01_商机与销售管道_前后端开发Spec_v1.1.2_RECOVERY_REVIEW_CANDIDATE.md:3322>)、[原文 L3443](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/bd/bdc91e2093023cf7__CRM-OPP-01_商机与销售管道_前后端开发Spec_v1.1.2_RECOVERY_REVIEW_CANDIDATE.md:3443>)、[原文 L3472](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/bd/bdc91e2093023cf7__CRM-OPP-01_商机与销售管道_前后端开发Spec_v1.1.2_RECOVERY_REVIEW_CANDIDATE.md:3472>)、[原文 L3593](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/bd/bdc91e2093023cf7__CRM-OPP-01_商机与销售管道_前后端开发Spec_v1.1.2_RECOVERY_REVIEW_CANDIDATE.md:3593>) |

## 11. 未确认项与开发前整合动作

| Gate | 准确未验证内容 | 对实施的约束 |
|---|---|---|
| O01 | 实际框架/DbContext/Provider/等价表/事务/读快照/索引 | 不依旧React/新Vue范例切栈；规范表不是迁移结果 |
| O02 | 当前Lead入口/CP22转换槽Manifest/Enlist/共同锁序 | 同事务真实参与不足不先建半清单 |
| O03 | HO参与者/租户经营主体映射/Producer/Epoch/补件与Intake实例 | LOCAL_REQUEST不冒Main受理；跨部署缺协议全0 |
| O04 | Main商业责任/不重叠范围/真实Confirm等决策/连续流/Claim | 不用C03 BookedAmount代成交，不猜订单coverage |
| O05 | Function/Action→域能力/Scope/金额/Team及撤权窗口 | 无admin/ALL默认；所有writer共同最终Guard |
| O06 | Quote准确净额/版本/前驱/安全导航 | 本域不制造QuoteAccepted/Order |
| O07 | Inbox加密/可信issuer/HMAC/原键保留/PII/重启信任 | 缺原字节/密钥不恢复成功，跨刷新能力不自动成立 |
| O08 | CRM归属决定＋Main确认、Intake/商业/冲突处置签署范围 | 当前只同Tenant/同Account/同经营主体归属纠错；跨客户另治理 |
| O09 | 已接受设计对应真实Stage/Qualification/Currency/角色/原因版本 | 设计接受已定，生产字典发布/授权仍需证据 |
| O10 | 文件/Activity准确版本/新商业用途/最小化 | 旧下载/保留权不能授向Main传播；无附件路径独立 |
| O11 | 原签名缺链读取、派发/收件密钥保留、告警/失败演练 | 无跳链latest替身，无生产吞吐恢复声明 |

准确Gate原文3739～3767；上表区分缺真实实例与已接受算法，不把所有页面笼统写成不可设计。[Gate清单][S3739]

## 12. 来源、实读覆盖与剩余队列

主文3845行当前必要语义已完整归并：核心、DTO／物理约束／恢复、54AC／Gate／来源和连续链非fenced文字按原全文范围保留；本轮另读全部80JSON块，六组结构表达保留字段／值／原行／JSON pointer，重复子对象只在精确相同时复用已读对象。80对象还原后序列化0差异，不把它说成重复排版逐字重读，也未重算业务摘要／执行示例。UA48／CURRENT104／最终限定复审34行全文。精确SHA和覆盖见[阅读账本](<D:/CP6/docs/CP6_开发设计文档_20261010/evidence/commercial-reading.json>)。

状态 `required_materials_consolidated` 指当前必要设计语义归并。历史v1.0/v1.1、Library早期v1.1.2及旧撤回Review不替代当前选定原件；历史源码和全部关联Owner实现未因此全文审计。实际H02／HO共同事务参与、Main准确wire和归因、真实Producer及54AC运行仍保持未证，源内远端链接没有在本轮访问。

关联：[Lead](<D:/CP6/docs/CP6_开发设计文档_20261010/modules/CRM-LEAD-01.md>)、[Account](<D:/CP6/docs/CP6_开发设计文档_20261010/modules/CRM-ACC-01.md>)、[Contact](<D:/CP6/docs/CP6_开发设计文档_20261010/modules/CRM-CON-01.md>)、[Activity](<D:/CP6/docs/CP6_开发设计文档_20261010/modules/CRM-ACT-01.md>)、[HO](<D:/CP6/docs/CP6_开发设计文档_20261010/modules/CRM-HO-01.md>)。后继模块链接是整合入口，不表示该文件已整理完成。

[U9]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/ab/aba3cf1f1013b667__HISTORY__handoffs__PLAN-REUSE-01B__documentation__MD02__acceptances__A-CRM__CRM-OPP-01__UA-20260930-A-CRM-OPP-MD01.md.txt:9>
[S32]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/bd/bdc91e2093023cf7__CRM-OPP-01_商机与销售管道_前后端开发Spec_v1.1.2_RECOVERY_REVIEW_CANDIDATE.md:32>
[S40]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/bd/bdc91e2093023cf7__CRM-OPP-01_商机与销售管道_前后端开发Spec_v1.1.2_RECOVERY_REVIEW_CANDIDATE.md:40>
[S283]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/bd/bdc91e2093023cf7__CRM-OPP-01_商机与销售管道_前后端开发Spec_v1.1.2_RECOVERY_REVIEW_CANDIDATE.md:283>
[R17]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/git-objects/8b/8b58701fdba38ba2__SR-20260930-A-CRM-OPP-MD02-FINAL.md:17>
[R27]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/git-objects/8b/8b58701fdba38ba2__SR-20260930-A-CRM-OPP-MD02-FINAL.md:27>
[U37]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/ab/aba3cf1f1013b667__HISTORY__handoffs__PLAN-REUSE-01B__documentation__MD02__acceptances__A-CRM__CRM-OPP-01__UA-20260930-A-CRM-OPP-MD01.md.txt:37>
[U20]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/ab/aba3cf1f1013b667__HISTORY__handoffs__PLAN-REUSE-01B__documentation__MD02__acceptances__A-CRM__CRM-OPP-01__UA-20260930-A-CRM-OPP-MD01.md.txt:20>
[S3777]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/bd/bdc91e2093023cf7__CRM-OPP-01_商机与销售管道_前后端开发Spec_v1.1.2_RECOVERY_REVIEW_CANDIDATE.md:3777>
[S1264]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/bd/bdc91e2093023cf7__CRM-OPP-01_商机与销售管道_前后端开发Spec_v1.1.2_RECOVERY_REVIEW_CANDIDATE.md:1264>
[S228]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/bd/bdc91e2093023cf7__CRM-OPP-01_商机与销售管道_前后端开发Spec_v1.1.2_RECOVERY_REVIEW_CANDIDATE.md:228>
[S1030]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/bd/bdc91e2093023cf7__CRM-OPP-01_商机与销售管道_前后端开发Spec_v1.1.2_RECOVERY_REVIEW_CANDIDATE.md:1030>
[S149]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/bd/bdc91e2093023cf7__CRM-OPP-01_商机与销售管道_前后端开发Spec_v1.1.2_RECOVERY_REVIEW_CANDIDATE.md:149>
[S95]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/bd/bdc91e2093023cf7__CRM-OPP-01_商机与销售管道_前后端开发Spec_v1.1.2_RECOVERY_REVIEW_CANDIDATE.md:95>
[S129]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/bd/bdc91e2093023cf7__CRM-OPP-01_商机与销售管道_前后端开发Spec_v1.1.2_RECOVERY_REVIEW_CANDIDATE.md:129>
[S327]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/bd/bdc91e2093023cf7__CRM-OPP-01_商机与销售管道_前后端开发Spec_v1.1.2_RECOVERY_REVIEW_CANDIDATE.md:327>
[S319]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/bd/bdc91e2093023cf7__CRM-OPP-01_商机与销售管道_前后端开发Spec_v1.1.2_RECOVERY_REVIEW_CANDIDATE.md:319>
[S743]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/bd/bdc91e2093023cf7__CRM-OPP-01_商机与销售管道_前后端开发Spec_v1.1.2_RECOVERY_REVIEW_CANDIDATE.md:743>
[S756]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/bd/bdc91e2093023cf7__CRM-OPP-01_商机与销售管道_前后端开发Spec_v1.1.2_RECOVERY_REVIEW_CANDIDATE.md:756>
[S767]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/bd/bdc91e2093023cf7__CRM-OPP-01_商机与销售管道_前后端开发Spec_v1.1.2_RECOVERY_REVIEW_CANDIDATE.md:767>
[S781]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/bd/bdc91e2093023cf7__CRM-OPP-01_商机与销售管道_前后端开发Spec_v1.1.2_RECOVERY_REVIEW_CANDIDATE.md:781>
[S162]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/bd/bdc91e2093023cf7__CRM-OPP-01_商机与销售管道_前后端开发Spec_v1.1.2_RECOVERY_REVIEW_CANDIDATE.md:162>
[S775]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/bd/bdc91e2093023cf7__CRM-OPP-01_商机与销售管道_前后端开发Spec_v1.1.2_RECOVERY_REVIEW_CANDIDATE.md:775>
[S1059]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/bd/bdc91e2093023cf7__CRM-OPP-01_商机与销售管道_前后端开发Spec_v1.1.2_RECOVERY_REVIEW_CANDIDATE.md:1059>
[S362]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/bd/bdc91e2093023cf7__CRM-OPP-01_商机与销售管道_前后端开发Spec_v1.1.2_RECOVERY_REVIEW_CANDIDATE.md:362>
[S515]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/bd/bdc91e2093023cf7__CRM-OPP-01_商机与销售管道_前后端开发Spec_v1.1.2_RECOVERY_REVIEW_CANDIDATE.md:515>
[S1134]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/bd/bdc91e2093023cf7__CRM-OPP-01_商机与销售管道_前后端开发Spec_v1.1.2_RECOVERY_REVIEW_CANDIDATE.md:1134>
[S699]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/bd/bdc91e2093023cf7__CRM-OPP-01_商机与销售管道_前后端开发Spec_v1.1.2_RECOVERY_REVIEW_CANDIDATE.md:699>
[S793]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/bd/bdc91e2093023cf7__CRM-OPP-01_商机与销售管道_前后端开发Spec_v1.1.2_RECOVERY_REVIEW_CANDIDATE.md:793>
[S860]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/bd/bdc91e2093023cf7__CRM-OPP-01_商机与销售管道_前后端开发Spec_v1.1.2_RECOVERY_REVIEW_CANDIDATE.md:860>
[S919]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/bd/bdc91e2093023cf7__CRM-OPP-01_商机与销售管道_前后端开发Spec_v1.1.2_RECOVERY_REVIEW_CANDIDATE.md:919>
[S924]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/bd/bdc91e2093023cf7__CRM-OPP-01_商机与销售管道_前后端开发Spec_v1.1.2_RECOVERY_REVIEW_CANDIDATE.md:924>
[S872]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/bd/bdc91e2093023cf7__CRM-OPP-01_商机与销售管道_前后端开发Spec_v1.1.2_RECOVERY_REVIEW_CANDIDATE.md:872>
[S934]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/bd/bdc91e2093023cf7__CRM-OPP-01_商机与销售管道_前后端开发Spec_v1.1.2_RECOVERY_REVIEW_CANDIDATE.md:934>
[S973]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/bd/bdc91e2093023cf7__CRM-OPP-01_商机与销售管道_前后端开发Spec_v1.1.2_RECOVERY_REVIEW_CANDIDATE.md:973>
[S1032]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/bd/bdc91e2093023cf7__CRM-OPP-01_商机与销售管道_前后端开发Spec_v1.1.2_RECOVERY_REVIEW_CANDIDATE.md:1032>
[S1042]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/bd/bdc91e2093023cf7__CRM-OPP-01_商机与销售管道_前后端开发Spec_v1.1.2_RECOVERY_REVIEW_CANDIDATE.md:1042>
[S1502]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/bd/bdc91e2093023cf7__CRM-OPP-01_商机与销售管道_前后端开发Spec_v1.1.2_RECOVERY_REVIEW_CANDIDATE.md:1502>
[S886]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/bd/bdc91e2093023cf7__CRM-OPP-01_商机与销售管道_前后端开发Spec_v1.1.2_RECOVERY_REVIEW_CANDIDATE.md:886>
[S1143]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/bd/bdc91e2093023cf7__CRM-OPP-01_商机与销售管道_前后端开发Spec_v1.1.2_RECOVERY_REVIEW_CANDIDATE.md:1143>
[S1157]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/bd/bdc91e2093023cf7__CRM-OPP-01_商机与销售管道_前后端开发Spec_v1.1.2_RECOVERY_REVIEW_CANDIDATE.md:1157>
[S1106]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/bd/bdc91e2093023cf7__CRM-OPP-01_商机与销售管道_前后端开发Spec_v1.1.2_RECOVERY_REVIEW_CANDIDATE.md:1106>
[S1174]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/bd/bdc91e2093023cf7__CRM-OPP-01_商机与销售管道_前后端开发Spec_v1.1.2_RECOVERY_REVIEW_CANDIDATE.md:1174>
[S1183]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/bd/bdc91e2093023cf7__CRM-OPP-01_商机与销售管道_前后端开发Spec_v1.1.2_RECOVERY_REVIEW_CANDIDATE.md:1183>
[S1051]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/bd/bdc91e2093023cf7__CRM-OPP-01_商机与销售管道_前后端开发Spec_v1.1.2_RECOVERY_REVIEW_CANDIDATE.md:1051>
[S1416]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/bd/bdc91e2093023cf7__CRM-OPP-01_商机与销售管道_前后端开发Spec_v1.1.2_RECOVERY_REVIEW_CANDIDATE.md:1416>
[S1189]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/bd/bdc91e2093023cf7__CRM-OPP-01_商机与销售管道_前后端开发Spec_v1.1.2_RECOVERY_REVIEW_CANDIDATE.md:1189>
[S1204]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/bd/bdc91e2093023cf7__CRM-OPP-01_商机与销售管道_前后端开发Spec_v1.1.2_RECOVERY_REVIEW_CANDIDATE.md:1204>
[S1252]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/bd/bdc91e2093023cf7__CRM-OPP-01_商机与销售管道_前后端开发Spec_v1.1.2_RECOVERY_REVIEW_CANDIDATE.md:1252>
[S1242]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/bd/bdc91e2093023cf7__CRM-OPP-01_商机与销售管道_前后端开发Spec_v1.1.2_RECOVERY_REVIEW_CANDIDATE.md:1242>
[S1061]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/bd/bdc91e2093023cf7__CRM-OPP-01_商机与销售管道_前后端开发Spec_v1.1.2_RECOVERY_REVIEW_CANDIDATE.md:1061>
[S182]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/bd/bdc91e2093023cf7__CRM-OPP-01_商机与销售管道_前后端开发Spec_v1.1.2_RECOVERY_REVIEW_CANDIDATE.md:182>
[S1221]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/bd/bdc91e2093023cf7__CRM-OPP-01_商机与销售管道_前后端开发Spec_v1.1.2_RECOVERY_REVIEW_CANDIDATE.md:1221>
[S172]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/bd/bdc91e2093023cf7__CRM-OPP-01_商机与销售管道_前后端开发Spec_v1.1.2_RECOVERY_REVIEW_CANDIDATE.md:172>
[S176]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/bd/bdc91e2093023cf7__CRM-OPP-01_商机与销售管道_前后端开发Spec_v1.1.2_RECOVERY_REVIEW_CANDIDATE.md:176>
[S1236]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/bd/bdc91e2093023cf7__CRM-OPP-01_商机与销售管道_前后端开发Spec_v1.1.2_RECOVERY_REVIEW_CANDIDATE.md:1236>
[S351]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/bd/bdc91e2093023cf7__CRM-OPP-01_商机与销售管道_前后端开发Spec_v1.1.2_RECOVERY_REVIEW_CANDIDATE.md:351>
[S598]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/bd/bdc91e2093023cf7__CRM-OPP-01_商机与销售管道_前后端开发Spec_v1.1.2_RECOVERY_REVIEW_CANDIDATE.md:598>
[S1880]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/bd/bdc91e2093023cf7__CRM-OPP-01_商机与销售管道_前后端开发Spec_v1.1.2_RECOVERY_REVIEW_CANDIDATE.md:1880>
[S3136]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/bd/bdc91e2093023cf7__CRM-OPP-01_商机与销售管道_前后端开发Spec_v1.1.2_RECOVERY_REVIEW_CANDIDATE.md:3136>
[S3805]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/bd/bdc91e2093023cf7__CRM-OPP-01_商机与销售管道_前后端开发Spec_v1.1.2_RECOVERY_REVIEW_CANDIDATE.md:3805>
[S3821]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/bd/bdc91e2093023cf7__CRM-OPP-01_商机与销售管道_前后端开发Spec_v1.1.2_RECOVERY_REVIEW_CANDIDATE.md:3821>
[S3809]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/bd/bdc91e2093023cf7__CRM-OPP-01_商机与销售管道_前后端开发Spec_v1.1.2_RECOVERY_REVIEW_CANDIDATE.md:3809>
[S1508]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/bd/bdc91e2093023cf7__CRM-OPP-01_商机与销售管道_前后端开发Spec_v1.1.2_RECOVERY_REVIEW_CANDIDATE.md:1508>
[R25]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/git-objects/8b/8b58701fdba38ba2__SR-20260930-A-CRM-OPP-MD02-FINAL.md:25>
[S3620]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/bd/bdc91e2093023cf7__CRM-OPP-01_商机与销售管道_前后端开发Spec_v1.1.2_RECOVERY_REVIEW_CANDIDATE.md:3620>
[S3739]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/bd/bdc91e2093023cf7__CRM-OPP-01_商机与销售管道_前后端开发Spec_v1.1.2_RECOVERY_REVIEW_CANDIDATE.md:3739>
