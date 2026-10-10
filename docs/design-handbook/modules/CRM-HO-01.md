# CRM-HO-01 商业交接、投递恢复与工程受理

整理状态：`core_semantics_consolidated`。当前已接受 v1.0 原件411969字节、7204行，SHA-256 `affc1caf67bbf423989dbdf184ca7308a56fb313c9ffec95959b7379940becca`。后继 UA 接受同一原字节及全部12项D-HO设计选择；主文保留的DRAFT/FROZEN/PROPOSED标签属于接受前状态。真实Owner采用、数据库并发和运行未证，64项业务AC全部NOT_RUN。[精确接受][U11]

## 1. 目的、操作者与唯一事实 Owner

销售在商机420查看商业请求各版、传输状态、Main受理、正式报价/订单引用，按明确意图补件；技术操作员只能对原事件重投或查询原业务键。工程发起人还可把已保存早期需求送PLM，查看准确源的受理与进度。Main/PLM实际业务与CRM本地请求/投影始终有各自责任和回执。[职责][S39]

商业申请唯一入口仍为 OPP `opportunities/{id}/handoffs/preview`、`apply`。OPP拥有需求/预测/Stakeholder、CommercialIntent、CurrentPending、商业归属/贡献/Won；HO作为同CRM数据库事务参与者拥有唯一RequestVersion和完整Outbox。Main拥有正式BP、商业受理、Quote/Order；PLM拥有工程需求/修订/迭代/基线/发布。HO没有第二POST商业申请，没有第二商业结果消费者，也没有HoWon/HoContribution账。[唯一提交与消费][S43]

H39早期工程可从已保存Requirement开始，DISCOVERY/QUALIFYING、预测未齐、材料/工艺未知都不必挡住；仍须实质需求、真实已有Account、准确Product或null、源可读可传、新用途和PLM范围。商业H03继续要求OPP适用PASS、COMMERCIAL_REVIEW等商业门。工程unknowns保持未知，不补假BOM/0；纯外购/标准复购按实际适用范围。[早期工程][S63]

## 2. 当前组合、接受与评审范围

有效输入是7204行主文、UA87行、FINAL独审69行、CURRENT196行；INDEX只导航。主文附录A逐字导入当前OPP v1.1.2相关合同，附录B导入当前ACT v1.1.1工程来源合同，两份上游原件未被改写。UA明确接受全部六原SPEC、H39、最终同Case问题门、初始空Case路径、精确Intake纠错、M07首次Main证据读取和原重复回执快照语义。[接受的边界][U32]

FINAL独审在已读完整主体后限定复审，结论没有所审范围内的剩余实质阻塞；重点确认新Source不能把适用Case问题转为无关历史、直接Intake纠错不能混入别Case/Progress问题、M07不偷偷扩大OPP Context，以及DUPLICATE回原应用CaseRevision而非当前版。[限定复审结论][R42]

11个O-HO Gate保持UNPROVEN_PRODUCTION_GATE；112段JSON解析、105具名对象/103封闭顶层DTO、摘要复算仅是历史文档一致性证据，不等真实签名/运行。UA内“INQ/LEAD deferred unfinished”是2026-09-30当时全局状态；本手册对它们采用后继已选INQ/LEAD当前稿，不能把这一旧状态重新当今天未接受结论。[执行边界][U51]

## 3. 身份、状态轴、实体与字段

七状态轴分开：本地Request COMMITTED；Delivery PENDING/IN_FLIGHT/BROKER_ACKED/PAUSED/DEAD；OPP Main Intake UNKNOWN/NEEDS_INFO/ACCEPTED/REJECTED_FINAL；Quote DRAFT/ISSUED/ACCEPTED/REJECTED/WITHDRAWN；OPP CommercialView；工程申请PENDING/NEEDS_INFO/ACCEPTED/REJECTED及可用性；PLM Progress CLARIFICATION/ENGINEERING/READY_FOR_REFERENCE/STOPPED及各用途门。任何上层ACK不推导下一层完成，lastProven不替代当前CONFLICT/UNKNOWN。[七轴][S49]

| 身份/实体 | 精确键与业务字段 | 不变量 |
|---|---|---|
| 商业H/V | H=(Tenant,HandoffId)，V=(H,RequestVersion)；冻结目标MainOwner/LegalEntity/Opp/Account/processingType | INITIAL1，SUPPLEMENT同H连续+1；每V一原Event/Intent/body/O；不因标题相同合并真实新意图 |
| HoHandoffHeader/RequestVersion/RouteBinding | 原OPP operation/key指纹；完整Envelope密文/KeyVersion/BodyHash；源用途证明；固定Intake三元组及Quote/Commercial策略 | UQ原O防重复参与；Header无HO可写CurrentVersion；当前/待回指针只OPP裁决 |
| HoOutbox/Delivery/Attempt | TargetKind/Id/Version唯一；Event原body；DeliveryRevision初1、state/attempts/lease token/version/time；追加AttemptObservation | 只一次生成发送原字节；技术租约不代业务幂等；旧失败不能降级已证ACK |
| HoCommand / ReconcileJob | Tenant/OperationKind/Key唯一，Actor/目标/指纹/预期版/安全回执；job唯一原O/完整QuerySelection/路由/returned/applied/waiting | 无先commit永久PROCESSING；query job完成不等远端业务完成 |
| 工程EC/Case | EC=(Tenant,OpportunityId,TargetPlmAuthority,TargetLegalEntity,ENGINEERING_INTAKE)；CaseId、CaseRevision、CurrentSubmissionVersion | 每自然槽永远一个稳定Case；拒绝/关闭/源升级不另建Case |
| 工程ES/SourceSnapshot | ES=(CaseId,SubmissionVersion)；每版独立RequestKey/EventId、完整Source密文、SourceDigest/KeyVersion、NextAuthorization、固定Intake/Progress三元组 | Submission连续+1；RequestKey准确源不可改绑；证据变而Requirement版不变也可明确新ES/new RequestKey |
| IntakeStream/Result | 每Tenant/Case/ES一规范流；CurrentOrdinal/Terminal/Correction/Slot；receiptId、原完整source/结果/证明 | 初ordinal/slot0；结果ordinal≥1；receipt是Owner opaque业务键，不是本地UUID |
| ProgressStream/Result | 每Case/ES一规范流；准确已接受Intake receipt、结果ID、完整Iteration/Baseline/readiness状态 | Progress不是Intake，不能凭工程号补造接受；不跨Source取最大版 |
| EngineeringInbox/Alias/Conflict/ResolutionLedger | 完整原body/Trust/BodyHash；运输五元键；业务/冲突摘要；ApplicableToCurrent；准确处置集合及原回执 | 同业务别名不加修订；适用问题不得由新ES擅自清掉；Ledger不是工程主账 |
| Audit/ReadGeneration | 安全原O/事件/结果/版本与前后引用；ScopePartition水位 | 原文/敏感原因按策略加密遮罩；内部计数不直接公开 |

完整物理类型/每列/PK/FK/UQ在590～620。每表带Tenant，FK带Tenant；外部Key大小写敏感，不强转UUID。UTC datetime2(3)，Revision bigint并在JSON用字符串；正文/证据varbinary密文+KeyVersion+BodyHash，安全索引不代原文。Snapshot/Submission/Case当前指针完整关系必须同事务，实际DDL需证明可插入顺序及双边一致，不以“延迟FK”空话当SQL Server现成能力。[物理模型][S590]

本地公开body≤256KiB、批量30/比较2，工程入站≤1MiB；reason1～2000，约束/未知每项1～1000且各0～30；activitySources/evidenceRefs各0～20、按完整自然身份唯一。digest固定64小写hex；来源需求行已有lineId、clientRef=null，不可在HO输入框独立改Requirement；候选Product先由原Owner登记。[值约束][S124] [工程源字段][S516]

SourceKind固定CRM_OPPORTUNITY_REQUIREMENT，SourceId=OppId，SourceVersion=RequirementVersion；另保存HO sourceRevisionKey/快照身份，不能冒OPP版本。SourceDigest对**完整**EngineeringSource用目的隔离HMAC `HO-ENG-SOURCE-1`，包含Account/准确需求/Product/constraints/unknowns/ACT/附件证据；不含现权限/token/未来RequestKey。ACT证据保持自己的ActivityRevision/ReferenceSet/DigestKeyVersion，不和主Source混为一体。[源身份][S77]

## 4. 商业与工程流程、状态推进和写集合

| 事务 | 最终守卫与锁 | 一次提交的业务效果 |
|---|---|---|
| T-COMMERCIAL | OPP原Guard/O→Account/Contact→H身份槽→Intake→Opp；准确源/7项prior/路由/新用途 | HO Header/Version/Route/唯一Outbox/Delivery＋原OPP Intent/Link/Supersession/Current/Pending/历史/回执/水位；无同步Main写 |
| T-RETRY | 当前HO_RETRY/传输用途→O→H或EC→Delivery；准确event/body/revision、无有效租约 | 同Delivery置PENDING、NextAttemptAt、DeliveryRevision+1、Audit/Command；原body/event/业务状态不改 |
| T-QUERY | 当前独立结果查询权/O→目标→Delivery；原locator/路由版本 | 完整QueryJob、Command/Audit；后续只读Main/PLM原链 |
| T-ESUB | IAM/SourceUse→O→必要Account/ACT/File Guards→EC/Case→Opp保持锁→HO子记录 | 首Case、SourceSnapshot、新ES/new RequestKey/Event、完整Envelope/Stream初行/Outbox/Delivery、CurrentES/CaseRevision+1、Command/Audit/水位 |
| T-EIN | 服务/Owner Guard→Case→ES→Intake流/Result/Conflict→Inbox | 不可变IntakeResult、流头/Terminal/Correction、精确冲突、适用CaseRevision、Inbox/Alias/原Receipt同提交；工程写0 |
| T-EPROG | Case→ES→Intake接受槽→Progress流→Inbox | 完整ProgressResult/Current、适用CaseRevision/水位/Inbox；工程写0 |
| T-ERES | 原Owner处置→Case→ES→准确流→Conflict→Ledger/Inbox | 只精确所列Conflict处置及可用性、Ledger/Receipt/Audit；不应用未知候选、不造工程 |

[统一事务表][S628]

商业INITIAL/SUPPLEMENT完全沿OPP准确规则；`HoEnlistInput`传完整CommercialIntent/prior、可信origin O指纹、固定route/stream和SourcePermissionEvidence；`sameTransaction:true`只能由实际数据库参与机制证明，不是客户端布尔值。HO无独立commit，任一步失败全回滚。原Body一次规范序列化并固定BodyHash，以后发原字节，不更新时间/重新序列化。v1未发Outbox不能因为v2出现就删除以假造撤销；Main最终版本槽必须防旧worker晚执行。[Enlist闭合][S313]

T-RETRY只允许PAUSED/DEAD、无有效lease；PENDING→RETRY_NOT_NEEDED，IN_FLIGHT→LEASE_ACTIVE，BROKER_ACKED→ALREADY_ACKED并导向原键查询。人工重排不清总attempt历史，不释放OPP pending、不改Intake/Won。更换文件、目标或正文是新明确业务意图，不能通过retry编辑原body。[重投规则][S351]

工程H15先从准确OPP版本重建源并读Main M02 NextContext；初始EC缺行返回CaseRevision0/CurrentSubmission0/caseId=null，但必须有Main自然槽首次CREATE证明，不拿404/空数组代无执行。已有Case由Main明确CREATE/REVISE与真实EngineeringRef；UNKNOWN/PENDING不能后继，NEEDS_INFO/REJECTED/ACCEPTED也都需nextAllowed及旧执行关闭证据。[准备与最终点][S518]

H16只读精确源/用途/Main上下文并签token，不预分配RequestKey。H17同Case锁重核：当前Requirement完整内容/Account、所有版本与用途、Case/CurrentES、MainNext对应当前ES，且无适用OPEN Case/Intake/Progress冲突、无已认证但待Source/前驱/接受的当前依赖、无明确影响当前同工程的待核纠错。即使Main nextAllowed=true，本地问题门仍可拒；旧token不能绕过。新Source不能把未解决旧问题改成非适用历史。[最终Case门][S522]

确实初始空Case路径不要求不存在的Intake为KNOWN；已有sourceChanged本身不算冲突，合法源后继可提交。问题适用范围登记后持续保留至精确Owner处置，不允许H17重分类。成功切新ES时当前OwnerOutcome=UNKNOWN，旧Accepted仅lastProven，不将旧Ready借给新Source；旧ES和原工程继续保存，不自动撤销。[空Case与后继][S530]

工程Intake流先核冻结Producer/Contract/Epoch及完整EC/Case/ES/RequestKey/Source。PENDING可继续或一次转NEEDS_INFO/ACCEPTED/REJECTED，三者均终结准确RequestKey；普通高序不能换终态、receipt或工程号。CORRECTION另需ENGINEERING_INTAKE_CORRECT、准确前驱/BusinessDigest、CorrectionRevision+1和真实工程业务证据：FIX_REJECTION仅REJECTED→ACCEPTED、同Source真实非null工程；INVALIDATE_ACCEPTANCE仅ACCEPTED→REJECTED，保同EngineeringRef为历史身份，不能推断实体删除/副作用全回滚。[受理状态][S546]

直接IntakeCorrection只准精确同Case/ES/INTAKE规范流、同前驱、OPEN的INTAKE_TERMINAL_IDENTITY_CONFLICT或INTAKE_FACT_CONFLICT；每个id/hash/incomingDigest与原完整候选匹配，Owner决策明确覆盖事实映射。Progress、别Case/ES/source、未批准epoch、别前驱混入则整笔回滚，不能先清合法子集。只自动更正当前ES；旧ES已有后继的不同更正进入LATE_PREDECESSOR_CORRECTION，可能阻断整个Case，需Main处理真实后继影响。[限定直接纠错][S554]

Progress必须绑定已应用且未INVALIDATE的ACCEPTED receipt、同EngineeringRequestId；缺接受先WAITING_ACCEPTANCE，不能从Progress工程号补造Intake。Intake/Progress各自严格连续ordinal/digest、各自slot，不能和CaseRevision比较。完整状态iterations/baselines各≤50；消失需Owner说明，readiness恰三用途INTERNAL_ESTIMATE/FORMAL_QUOTE/MANUFACTURING_RELEASE各一次。Ready引用不直接授Main报价/制造权。[进度准入][S564]

STOP必须stage=STOPPED并把此前READY用途改NOT_READY/UNKNOWN（NOT_APPLICABLE保持）；普通PROGRESS不能从STOPPED复活，恢复要有据CORRECT且指准确停止结果。Intake INVALIDATE与Progress共享Case/ES锁：先invalidate则后progress隔离；先progress再invalidate则保历史，当前Ready不能用。旧源普通进度不自动全局停止，真实影响当前工程的旧源纠错必须按Owner影响范围形成适用问题。[停止与竞争][S570]

## 5. HTTP、跨域 wire 与结果边界

公开根 `/api/crm/v1`；人工HO operationKind仅 `ho.delivery.retry`、`ho.reconcile`、`ho.engineering.apply`，各写用当前context强If-Match和Idempotency-Key。商业仍OPP命令域。原文H01～H26是本模块端点定位号，不是正式FunctionId，也不能与蓝图H02等跨域编号混用。完整DTO在144～151、242～304、315～327、428～514、576～584、667～683；上游OPP/ACT别名见附录精确原形。[通用协议][S120]

| 方法/路径 | 意图与回包 |
|---|---|
| GET `handoff-options`，POST `handoffs/query` | 允许动作/真实路由、独立传输和Main状态筛选；HoOptions/HoPage |
| GET `handoffs/{H}`、`versions`、`deliveries/{V}`、`issues` | 选定准确版/当前OPP指针、传输、原结果和精确问题；版本页最多30 |
| POST `handoffs/{H}/compare` | 两个已存版本的当前受权差异，不能回写或推测遮罩内容 |
| POST `delivery-retries/preview`、`apply` | RetryPlan绑定target/event/DeliveryRevision/reason，仅REQUEUE_SAME_EVENT |
| POST `handoff-reconciliations/context`、`handoff-reconciliations`；GET `handoff-reconciliations/{job}` | 从本地稳定原locator准备，创建只读查询job，不要未来MainReceiptId |
| GET `handoff-commands/{operationKind}/{key}` | 独立RQ的安全历史原结果；NOT_OBSERVED不等未执行 |
| POST `handoff-navigation` | 注册Owner目标类型/key/version→短时受权URI，不接前端host/path |
| POST `engineering-handoffs/context`、`preview`、`apply` | EngineeringSelection→精确源/Case/MainNext→原子本地ES |
| GET `engineering-handoffs/{case}`、`submissions`、`submissions/{V}/source`、`progress`、`delivery` | 准确当前/历史ES/源/进度/投递；原应用回执与当前Case分读 |
| POST `/internal/crm/v1/engineering-intake-results`、`engineering-progress-results` | 封闭HO-ENG-INTAKE/1、HO-ENG-PROGRESS/1→EngineeringIngestReceipt，scope=PROJECTION/engineeringWrites=0 |
| POST `/internal/crm/v1/engineering-conflict-context`、`engineering-conflict-resolutions` | 实际原PLM处置服务准备/处置准确Case/ES/stream冲突 |

最后两行是完整内部路径，不叠公开根。完整26端点及L01～L04上游入口以172～207为准。[端点清单][S172]

| Required Contract | 设计路径与要求 |
|---|---|
| M01 Main原键查询 | POST `/internal/main/v1/crm-handoff-originals/query`；准确H/V/IntentDigest→原结果链 |
| M02 PLM后继准备 | POST `/internal/plm/v1/engineering-handoff-next-context/query`；自然Case/当前ES→MainCaseRevision、nextAllowed、CREATE/REVISE/target、priorExecutionFence/decision、route/两规范流、有效期 |
| M03 PLM受理 | POST `/internal/plm/v1/engineering-handoffs`；完整HO-ENG-REQUEST/1→202 DURABLY_RECEIVED/ALREADY_OBSERVED，scope=RECEIPT_ONLY/businessOutcome=UNKNOWN；业务结果另H22 |
| M04 PLM原键查询 | POST `/internal/plm/v1/engineering-handoff-originals/query`；准确Case/ES/RequestKey/SourceDigest原消息目录 |
| M05 双源关联 | POST `/internal/plm/v1/engineering-source-associations/query`；完整HO源＋ACT原requestOwner/requestKey/source＋工程→ASSOCIATED/NOT_ASSOCIATED/UNPROVEN |
| M06 Owner导航 | POST Main或PLM `/internal/{main|plm}/v1/crm-owner-navigation`；按已登记authority唯一服务及目标当前权 |
| M07 Main首次有效商业证据 | POST `/internal/main/v1/commercial-confirmation-evidence/query`；准确authority/legalEntity/group/stream/ordinal/result/source→MainConfirmationEvidenceRead |

这些是已接受设计所要求Owner采用的接口，不是已部署路由证据。M03只持久接收回执，重复也不直接给业务终态；原结果仍原键查。[Owner端点及回执][S209]

Main商业受理键=(Tenant,CRM_HO,H,V)，相同完整Intent/body回原，异文冲突。Main最终事务持H版本槽和V键，核当前接收用途、源/目标/BP/成熟度及supersession；后继n+1只能基于同H准确NEEDS_INFO n，不用到达顺序取新版。真实受理/需求关系/审计/请求执行结果/唯一OPP-INTAKE Outbox同Main事务。晚旧worker进相同槽不能重复建BP/Quote/Order，CRM成功提交不承诺Main成功。[Main商业合同][S339]

M02只读不能成为永久执行许可。M03在Main EC自然槽→原RequestKey→准确EngineeringRequest（REVISE）下重核当前SourceUse/IAM、完整source HMAC证明、NextContext/mainCaseRevision/执行Fence/目标版本。CREATE必须Main证无该EC已有工程，REVISE只原工程准确expectedRevision；过时STALE_NEXT_CONTEXT、零半创建/修订，不能退回CREATE另建。Main工程需求或修订/SourceRelation/Audit/原请求终态/Intake Outbox同事务；NEEDS_INFO工程号可null或真实存在，ACCEPTED必须真实非null工程。[PLM最终执行合同][S536]

M07只给当前Main Intake纠错Owner服务，Main Sales真实提供原CommercialResult完整base64字节/BodyHash/ReceivedTrust，及自然键/规范流/ordinal/result/source/BindingDigest/BusinessDigest完全一致的reference。未证时reference/candidate均null，不填伪值。第一次IntakeCorrection还不存在时，OPP普通Context的intakeCandidates必须[]，首次证据来自M07；后续ACK_INTAKE_CORRECTION仍用OPP专属Context，不能用M07换掉它的精确冲突集合。[M07闭合][S238]

ACT只可作精确证据和有据只读关联。HO主Source仍OPP Requirement；相同EngineeringRequestId不证明两个来源同一。必须M05 SourceAssociationProof保留两个独立原身份，且双方当前字段权成立；HO不写ACT Binding/CanonicalStream/CurrentSourceAcceptance，不用新HO源重绑ACT旧RequestKey。ACT变化只sourceChanged，不自动EngineeringSubmit；G-ACT-07仍不启用。[双源边界][S87] [关联DTO][S574]

## 6. 原键、RequiredFenced、并发与恢复

HO的登录态原命令查询与匿名public receipt是不同入口：H12要求当前HO_COMMAND_QUERY/原主体或明确管理范围；本版未定义INQ式匿名proof/browser binding。刷新只可保存无PII的O/Target安全locator，敏感body保受控会话内存，原成功但当前详情无权可COMMITTED/canOpen=false。不能以安全locator存在宣称已支持所有跨刷新冻结意图恢复。[RQ与前端恢复][S116]

MainOriginalPage的noEffectProven**恒false**；NOT_OBSERVED可让查询job COMPLETE，但原请求仍UNKNOWN，绝不是RequiredFenced防晚执行证明。删除队列、STOP/Lost/Archive、原O本地墓碑也不构成远端执行fence。本版不提供cancel-unobserved；M02 priorExecutionFence只在PLM实际自然槽/所有worker/别名共同最终裁决已被证明时成立，不把字段名或GET bool当已实现RequiredFenced。[原键查询语义][S361] [禁止假撤销][S359]

原键返回完整 `OriginalResultItem`：contract/businessKey/ordinal/originalBodyBase64/bodyHash/receivedTrust，允许多个独立stream。Owner目录游标pageToken独立于任何业务ordinal，稳定目录覆盖和缺失区间必须真实；FOUND也不直接改投影，只把每个原Envelope/Trust交唯一OPP或HO工程消费器。缺前驱列waiting，全部本次返回链处理才job COMPLETE；Owner无真实目录则自动job Gate关闭，保原消息重投入口。[原链 DTO][S367]

所有查无再插用UPDLOCK/HOLDLOCK缺行范围或已验证Serializable等价保护；CAS同时核CurrentOrdinal/SlotVersion/RowVersion、影响行1才成功；失败全部Result/Inbox写回滚。各公开CaseRevision/DeliveryRevision/OPP SalesRevision/ordinal与数据库RV分开。事务外准备只发现依赖，若锁内发现更上游依赖，退回重准备不逆锁。[唯一性与CAS][S622]

当前Guard必须有可证明最终生效点：同库Owner保持权威撤权/版本槽，或Owner提供覆盖本操作/字段/Source/body且在撤回/消费间串行裁决的有界执行许可。普通GET/过期token不满足。HO这一通用设计允许经证明的Owner许可，不能反向放宽ACT附件当前只支持LOCAL_AUTHORITATIVE_GUARD的更窄限定；每个消费者按自身合同接线。真正远端PLM业务准入仍M03重核，CRM本地202只是请求。[HO Guard][S641]

Outbox固定原密文/bodyHash，worker租约30秒、CAS、退避1/5/30/120/600秒、当前周期20次dead；重排保总attempts。旧租约完成只追加AttemptObservation，仅当前LeaseToken可改状态；迟到真实ACK可在Delivery锁内有据合并，迟到失败不能BROKER_ACKED→DEAD。Broker证明需绑定event/body/route，不接受worker字符串。[租约与迟到观察][S357]

工程TransportKey=(Tenant,Producer,Contract,Epoch,EventId)核原字节BodyHash；业务键准确ES流/ordinal；ConflictKeyHash前缀HO-ENG-CONFLICT-1并排除运输ID/到达/重试次数。Intake/Progress/Request摘要分别封闭实际业务对象；只排除schema明示EvidenceRef/ACT attestation的proofRef载体，不递归删除任意同名字段，业务时间/source/状态/冲突引用都保留。[三重身份][S649]

完整密文原载荷/KeyVersion/BodyHash/ReceivedTrust持久后才ACK。WAITING_SOURCE/PREDECESSOR/ACCEPTANCE内部续办要重读原文、核当前服务/Owner信任并重走全部绑定/规则；不能因为Event已存在duplicate早退。两worker同槽/CAS仅一效果；APPLIED重放返回**原应用**CaseRevision，当前Case由H18另读。缺密钥/原证据保UNPROVEN，篡改隔离，不能用现Main GET/摘要拼原Envelope。[完整恢复][S657]

H25 `HO-ENG-RESOLUTION/1`只有REJECT_CANDIDATE或ACK_CANONICAL_CORRECTION。精确1～20同Case/ES/stream冲突、source.conflictIds与conflicts集合恰等、当前Slot/原头/OwnerDecision全部匹配；后者必须所指CORRECTION/CORRECT已APPLIED，不能凭Resolution首次应用无序候选。Case后继影响没有权威处置时继续CONFLICT，无工程合并/删除按钮。H24/H25 currentResultId等是Owner业务键（Intake receiptId或Progress resultId），不是本地表UUID。[工程处置][S665] [业务键澄清][S753]

## 7. 租户、权限、PII与模拟隔离

HO_READ/HISTORY沿原420 query＋独立商机DataScope；HO_RETRY核指定版本传输权/用途；HO_RECONCILE不依赖仍可编辑销售；HO_ENGINEERING_SUBMIT仅工程发起源/用途/目标权，不授PLM写；RQ与导航各自独立。能力名是域映射需求，不证明已存在角色/正式Function。服务身份与专业Owner纠错能力分开，普通进度发布不能处理冲突。[能力表][S130]

所有表、FK、服务映射含Tenant，正文不能覆盖Tenant/Actor。读字段、历史差异、附件新用途、导航分别校验，版本比较不泄隐藏值/摘要/数量。金额遮罩连ratio/排序都保护，source/附件URL不进普通日志。隐私擦除后墓碑和Owner证据不足即UNPROVEN，不从回传payload恢复已擦PII。[隐私保留][S693]

CONTRACT_FIXTURE必须在身份、存储、队列、Command/Request/Inbox/Outbox/CanonicalHead/Conflict/Guard/ReadGeneration/额度水位全隔离。仅“不写Main表”不足；fixture不能占生产O/ordinal。已接受OPP DTO没有environment字段，不偷加字段，依受信部署/入口识别，生产拒fixtureIssuer/route。[模拟隔离][S695]

## 8. 页面、操作反馈与连续链

原400列表和420详情内“交接与结果”“工程受理”tab承担工作面，U01～U16为内部控件。商业预览只选目标/证据/原因，展示准确需求/预测；确认后202显示LOCAL_REQUEST，断线固定原O；补件先通过原OPP保存需求/预测并重新评估，再从受权当前Handoff读7项prior。传输/Intake/Quote/商业/工程状态分栏，各带sourceVersion、owner ordinal、localObservedAt、coverage。[具体控件][S93]

技术重投只能选准确Delivery和原因，不编辑原body；原键查询不要求未来receipt。工程准备允许unknowns，展示Main规定CREATE/REVISE及真实目标前驱；前驱未知不能提供“新建一个试试”。READY_FOR_REFERENCE仅各用途现状，正式报价/投产由Main自身Guard。主加载/空/无权/未配置/失败/部分历史/冲突独立显示，旧lastProven不能伪装最新成功。[控件状态][S105]

未发送草稿可放弃；已发网络错误先UNKNOWN并保原body/token/header。新意图只在原操作已确定拒绝且用户明确修改后换Key；412不自动合并。当前无权详情但RQ合法，显示安全成功/canOpen=false。每页来源与Owner时点分列，不能将三个时点合成UpdatedAt业务因果。[前端恢复][S116]

SC02链：已存合格商机→OPP提交HO V1→Broker ACK但MainUNKNOWN→原键查询NOT_OBSERVED仍未知→NEEDS_INFO→原OPP保存需求/重评→同H V2→Main受理→Quote→CREATED组仍0→真实Confirm才OPP一次贡献。工程链：早期需求ES1→NEEDS_INFO→保存新需求/M02证前驱闭合→同Case ES2→Progress先到WAITING_ACCEPTANCE→真实Intake后续办→STOP→INVALIDATE保工程历史→未批准epoch隔离→精确否定候选，原STOP/否定仍在。连续例均fixture，未执行。[SC02入口][S754] [工程轨迹][S5482]

## 9. 当前代码差异与整合次序

本轮未新审源码。主文在固定CRM `c778a3052a4416b82facb07b1244fc98fb6ad8a1` 与Main `157630594e3371fe181955d2f6227ff3b6962c84` 的切片记录：旧C03是Created/BookedAmount/ResultVersion，不能证明Confirm/经济覆盖；旧ErpReadController普通GET不能提供原历史链或无执行证明。[源码证据范围][S699]

原文明确旧Main `ErpQuotationOrderFactory.cs` 第36～37行按同CrmOpportunityId已有任意订单即C03_ORDER_ALREADY_EXISTS，不兼容已接受OPP多订单责任组。编码前必须对实际目标源码核这一去重边界，不能直接复用旧工厂为新受理Owner，更不能在CRM插Main订单绕限制。这里引用的是原Spec固定切片结论，不是本轮今日全仓审计。[固定冲突][S408] [准确旧源码身份][S711]

部署前要证明真实PrimaryWriter唯一、旧C03仅隔离观察、新旧队列/积压/每原业务键效果覆盖，不因设计出现就更名Topic/停止旧消费者/发Demo。本次整理建议先完成同库Enlist＋Main H/V最终槽，再工程NextContext/PLM实际执行Fence，再完整原键链/密钥/恢复，再UI；这是整合顺序，不是已经获准切换/部署。[唯一写者][S404]

## 10. 验收与未运行范围

64项AC-HO-001～064全部NOT_RUN，必须核响应、全部持久集合、版本/历史、禁止副作用和恢复。112 JSON静态一致性不是SQL/签名/服务/并发/性能通过。[AC口径][S6067]

| AC族 | 实施必须证明 |
|---|---|
| 001～018 | OPP/HO同事务原子申请/补件；同O/双O竞争/断点；用途撤权零写；NOT_OBSERVED不改未知；旧v1不能复活pending；历史差异遮罩 |
| 019～035 | Quote准确版且税/含税null；Created不Won；完整范围40/100、取消保有效40、零价、多币；经济Claim不双占；错误拒绝/第二组首次恢复完全OPP一次效果 |
| 036～045 | 只原event重投；有效lease不可抢；旧失败不降ACK；撤用途仅阻新传输；完整WAITING续办、同transport异文/同冲突别名、密钥缺失、独立RQ、fixture不占生产控制水位 |
| 046～060 | 早期工程不放宽商业资格；空Case并发唯一；M02 Fence缺证拒新ES；REVISE旧版本Main最终拒；先Progress不造Intake；另epoch隔离；STOP/CORRECT/INVALIDATE/双源绑定与版本分域 |
| 061 | Main允许新源但本Case有适用OPEN冲突或待核依赖：H15/H16不签可执行token，H17同Case最终拒绝，新ES/Key/Outbox0 |
| 062～063 | 直接IntakeCorrection混别Case Progress冲突整笔拒绝；准确有限集合与结果同事务，未列STOP/问题保持；过期集合不可继续清 |
| 064 | DUPLICATE回原CaseRevision2，当前5另读；首次商业纠错普通OPP Context候选空，证据从M07原body取得，不改上游合同 |

001/013/033/040/052要在每表写前、CAS后、commit发送前/响应丢失后断点；权限竞争包括准备后、最终Guard前、最终点后撤权，后者保历史但下一传输/导航再核。当前所有这些运行均未执行。[故障观察][S6138]

连续wire补充下列调用与恢复断言。所有fixture proof均为设计样例，不代表真实Owner签发或验收运行。

| 入口与原场景 | 开发必须保留的身份／分支 |
|---|---|
| S01–S15 初次交接与回查 | edit-context→preview→apply从OPP准确Sales/Requirement/Forecast/Stakeholder版本产生本地HandoffReceipt，202只是LOCAL_REQUEST。Outbox持久唯一Envelope；Broker ACK后Main仍UNKNOWN。H09/H10按Handoff/version+intentDigest建立LOCAL_QUERY_JOB，不需先有MainReceiptId；NOT_OBSERVED/noEffectProven=false不准换新键重申请。[原文 L762](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/af/affc1caf67bbf423__CRM-HO-01_商业交接与结果跟踪_前后端开发Spec_v1.0_DRAFT.md:762>)、[原文 L958](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/af/affc1caf67bbf423__CRM-HO-01_商业交接与结果跟踪_前后端开发Spec_v1.0_DRAFT.md:958>)、[原文 L990](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/af/affc1caf67bbf423__CRM-HO-01_商业交接与结果跟踪_前后端开发Spec_v1.0_DRAFT.md:990>)、[原文 L1135](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/af/affc1caf67bbf423__CRM-HO-01_商业交接与结果跟踪_前后端开发Spec_v1.0_DRAFT.md:1135>)、[原文 L1213](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/af/affc1caf67bbf423__CRM-HO-01_商业交接与结果跟踪_前后端开发Spec_v1.0_DRAFT.md:1213>)、[原文 L1246](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/af/affc1caf67bbf423__CRM-HO-01_商业交接与结果跟踪_前后端开发Spec_v1.0_DRAFT.md:1246>) |
| S18–S35 同案补件 | 原需求由OPP明确改2→3并重做资格，SalesRevision8后才能SUPPLEMENT。prior七字段取当前回查；一事务保同Handoff，requestVersion1→2、Slot1→2、pending1SUPERSEDED/2CURRENT_PENDING，Sales9/Commercial2。晚到v1别名返回原Commercial1回执，不倒退当前；Main接受v2也不产生Won。[原文 L1325](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/af/affc1caf67bbf423__CRM-HO-01_商业交接与结果跟踪_前后端开发Spec_v1.0_DRAFT.md:1325>)、[原文 L1800](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/af/affc1caf67bbf423__CRM-HO-01_商业交接与结果跟踪_前后端开发Spec_v1.0_DRAFT.md:1800>)、[原文 L1973](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/af/affc1caf67bbf423__CRM-HO-01_商业交接与结果跟踪_前后端开发Spec_v1.0_DRAFT.md:1973>)、[原文 L2140](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/af/affc1caf67bbf423__CRM-HO-01_商业交接与结果跟踪_前后端开发Spec_v1.0_DRAFT.md:2140>)、[原文 L2225](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/af/affc1caf67bbf423__CRM-HO-01_商业交接与结果跟踪_前后端开发Spec_v1.0_DRAFT.md:2225>)、[原文 L2280](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/af/affc1caf67bbf423__CRM-HO-01_商业交接与结果跟踪_前后端开发Spec_v1.0_DRAFT.md:2280>) |
| S36–S42 报价／订单／成交 | 报价105只是Main引用，预测与首目标100独立。CREATED组的confirmation=null、confirmedNet0及pending=true只ORDER_OBSERVED；完整有据确认100后才由OPP唯一投影WON/TARGET_REACHED，覆盖范围仍为REGISTERED_RESPONSIBILITIES_AS_OBSERVED。[原文 L2295](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/af/affc1caf67bbf423__CRM-HO-01_商业交接与结果跟踪_前后端开发Spec_v1.0_DRAFT.md:2295>)、[原文 L2395](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/af/affc1caf67bbf423__CRM-HO-01_商业交接与结果跟踪_前后端开发Spec_v1.0_DRAFT.md:2395>)、[原文 L2523](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/af/affc1caf67bbf423__CRM-HO-01_商业交接与结果跟踪_前后端开发Spec_v1.0_DRAFT.md:2523>)、[原文 L2660](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/af/affc1caf67bbf423__CRM-HO-01_商业交接与结果跟踪_前后端开发Spec_v1.0_DRAFT.md:2660>) |
| 原拒绝纠错与GB首次恢复 | IntakeCorrection绑定原REJECTED_FINAL结果、前驱摘要、CorrectionRevision、准确Main候选及冲突hash。GB不在第一次列表就仍隔离；其ACK_INTAKE_CORRECTION必须由准确Context取得Slot2及候选，一次首次建GB贡献40，GA100不重加，Commercial10→11、Slot2→3、Pursuit仍STOPPED。M07候选原body解码须与bodyHash及同一结果一致。[原文 L2918](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/af/affc1caf67bbf423__CRM-HO-01_商业交接与结果跟踪_前后端开发Spec_v1.0_DRAFT.md:2918>)、[原文 L3029](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/af/affc1caf67bbf423__CRM-HO-01_商业交接与结果跟踪_前后端开发Spec_v1.0_DRAFT.md:3029>)、[原文 L3150](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/af/affc1caf67bbf423__CRM-HO-01_商业交接与结果跟踪_前后端开发Spec_v1.0_DRAFT.md:3150>)、[原文 L3224](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/af/affc1caf67bbf423__CRM-HO-01_商业交接与结果跟踪_前后端开发Spec_v1.0_DRAFT.md:3224>)、[原文 L3325](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/af/affc1caf67bbf423__CRM-HO-01_商业交接与结果跟踪_前后端开发Spec_v1.0_DRAFT.md:3325>)、[原文 L3396](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/af/affc1caf67bbf423__CRM-HO-01_商业交接与结果跟踪_前后端开发Spec_v1.0_DRAFT.md:3396>) |
| E01–E23 工程澄清 | 材料未知可candidateProduct=null、unknowns具名。CaseNaturalKey稳定；首次CREATE须M02明确空槽执行Fence，不用404猜无工程。需求2→3先由OPP保存；ES2保同Case、另RequestKey，并携ES1/source/receipt和Main明确闭合证明。[原文 L3465](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/af/affc1caf67bbf423__CRM-HO-01_商业交接与结果跟踪_前后端开发Spec_v1.0_DRAFT.md:3465>)、[原文 L3506](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/af/affc1caf67bbf423__CRM-HO-01_商业交接与结果跟踪_前后端开发Spec_v1.0_DRAFT.md:3506>)、[原文 L4047](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/af/affc1caf67bbf423__CRM-HO-01_商业交接与结果跟踪_前后端开发Spec_v1.0_DRAFT.md:4047>)、[原文 L4142](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/af/affc1caf67bbf423__CRM-HO-01_商业交接与结果跟踪_前后端开发Spec_v1.0_DRAFT.md:4142>)、[原文 L4453](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/af/affc1caf67bbf423__CRM-HO-01_商业交接与结果跟踪_前后端开发Spec_v1.0_DRAFT.md:4453>) |
| E24–E32 乱序与历史回执 | Progress先到Intake只WAITING_ACCEPTANCE，不用工程号补造受理。受理精确到达后内部Worker重入原Inbox推进Case4→5；外部重复不能早退而丢待处理链。旧ES1运输别名只回历史CaseRevision2，当前ES2仍5。INTERNAL_ESTIMATE READY不授权FORMAL_QUOTE或MANUFACTURING_RELEASE。[原文 L4595](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/af/affc1caf67bbf423__CRM-HO-01_商业交接与结果跟踪_前后端开发Spec_v1.0_DRAFT.md:4595>)、[原文 L4704](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/af/affc1caf67bbf423__CRM-HO-01_商业交接与结果跟踪_前后端开发Spec_v1.0_DRAFT.md:4704>)、[原文 L4792](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/af/affc1caf67bbf423__CRM-HO-01_商业交接与结果跟踪_前后端开发Spec_v1.0_DRAFT.md:4792>)、[原文 L4809](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/af/affc1caf67bbf423__CRM-HO-01_商业交接与结果跟踪_前后端开发Spec_v1.0_DRAFT.md:4809>)、[原文 L4994](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/af/affc1caf67bbf423__CRM-HO-01_商业交接与结果跟踪_前后端开发Spec_v1.0_DRAFT.md:4994>) |
| E33–E43 STOP、受理否定、流冲突 | STOP是Progress后继；INVALIDATE_ACCEPTANCE是Intake有据纠错，二者分轴，工程ER-HO-900身份保留。未获准epoch e2置CONFLICT且不移动e1业务头；REJECT_CANDIDATE仅关闭明确e2冲突，受理否定与STOP继续有效，不能自动CREATE。[原文 L5011](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/af/affc1caf67bbf423__CRM-HO-01_商业交接与结果跟踪_前后端开发Spec_v1.0_DRAFT.md:5011>)、[原文 L5137](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/af/affc1caf67bbf423__CRM-HO-01_商业交接与结果跟踪_前后端开发Spec_v1.0_DRAFT.md:5137>)、[原文 L5226](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/af/affc1caf67bbf423__CRM-HO-01_商业交接与结果跟踪_前后端开发Spec_v1.0_DRAFT.md:5226>)、[原文 L5282](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/af/affc1caf67bbf423__CRM-HO-01_商业交接与结果跟踪_前后端开发Spec_v1.0_DRAFT.md:5282>)、[原文 L5420](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/af/affc1caf67bbf423__CRM-HO-01_商业交接与结果跟踪_前后端开发Spec_v1.0_DRAFT.md:5420>) |
| R01–R05、F01–F09 重传与冲突范围 | DEAD后preview/apply只能REQUEUE_SAME_EVENT，保持原RequestKey/Event/body；回执LOCAL_DELIVERY_CONTROL不是工程成功。Main nextAllowed=true但本地CASE_CONFLICT_OPEN时allowed=false/token=null。纠错掺入另一Case的PROGRESS冲突拒绝；仅精确同Case/ES/INTAKE列表可在同事务推进Intake并关闭所列冲突。[原文 L5496](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/af/affc1caf67bbf423__CRM-HO-01_商业交接与结果跟踪_前后端开发Spec_v1.0_DRAFT.md:5496>)、[原文 L5578](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/af/affc1caf67bbf423__CRM-HO-01_商业交接与结果跟踪_前后端开发Spec_v1.0_DRAFT.md:5578>)、[原文 L5626](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/af/affc1caf67bbf423__CRM-HO-01_商业交接与结果跟踪_前后端开发Spec_v1.0_DRAFT.md:5626>)、[原文 L5871](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/af/affc1caf67bbf423__CRM-HO-01_商业交接与结果跟踪_前后端开发Spec_v1.0_DRAFT.md:5871>)、[原文 L5954](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/af/affc1caf67bbf423__CRM-HO-01_商业交接与结果跟踪_前后端开发Spec_v1.0_DRAFT.md:5954>)、[原文 L5973](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/af/affc1caf67bbf423__CRM-HO-01_商业交接与结果跟踪_前后端开发Spec_v1.0_DRAFT.md:5973>)、[原文 L6051](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/af/affc1caf67bbf423__CRM-HO-01_商业交接与结果跟踪_前后端开发Spec_v1.0_DRAFT.md:6051>) |

## 11. 尚需证明的 Gate

| Gate | 具体未证能力 | 未证时边界 |
|---|---|---|
| O-HO-OPP | 同真实DbTx/锁序、唯一Current/Pending和原消费者 | 关闭商业新申请/补件，保安全历史 |
| O-HO-IAM | 现Action/字段/DataScope/RQ及撤权最终点 | 相应读写失败关闭，不造角色 |
| O-HO-MAIN | Main H/V去重、受理/Outbox同事务、真实Intake流/最终Guard | 商业新出站关闭，旧C03只观察 |
| O-HO-COMMERCIAL | Quote/Group/覆盖/Claim/Correction、M07真实原证据和完整链 | 当前商业结果UNPROVEN，不强Won |
| O-HO-PLM | M02自然Case/NextContext/Fence、M03工程修订同事务、规范结果流 | H17关闭，已有工程历史可受权引用 |
| O-HO-SOURCE | 精确OPP源、ACT attestation、File/Activity/Product新用途 | 使用缺证据的整计划拒绝；用户可另明确合法无该证据意图 |
| O-HO-QUERY | 真原键目录/原消息/当前查询权 | 自动job关闭，显示安全locator并保原消息重投 |
| O-HO-SECURITY | 完整密文/Key/BodyHash/Trust、当前信任/保留 | 不ACK不能持久消息，保未证 |
| O-HO-SINGLE-WRITER | 旧C03兼容、唯一效果、积压处理证据 | 新生产Writer不开启 |
| O-HO-NAV | 注册安全导航和目标当前权限 | 链接关闭，不猜URL |
| O-HO-RUNTIME | 数据库并发、权限竞争、worker恢复、真实Owner联调/容量/隐私 | 64AC保持NOT_RUN |

这些是真实实现能力门，已接受D-HO不需再次投票。没有M02/M03闭合证明时，不能用“先消息投递以后补Fence”的方式声称H39已可生产用。[原Gate表][S734]

## 12. 实际阅读、复用与剩余范围

7204行主文的当前必要语义已完整归并。核心、DTO／物理模型／事务／恢复、64AC及连续链所有非fenced文字保留原全文实读范围；本轮另分八组阅读112JSON全部字段和值，93个完全相同子对象按原行／JSON pointer复用，112对象重建0差异。M07候选原body Base64解码3100B，SHA等于bodyHash且JSON对象与同册已读原Main候选准确相等。没有把这些结构核对写成业务运行或每行排版人工重读。

附录复用准确同文本的OPP369～513→HO6156～6300、OPP793～1539→HO6301～7047、ACT722～874→HO7052～7204；分隔说明7048～7051也已读。UA87／FINAL69／CURRENT196／INDEX24全文；五项当前必要输入状态 `required_materials_consolidated`。完整SHA、直接阅读／结构复用映射见[commercial-reading.json](<D:/CP6/docs/CP6_开发设计文档_20261010/evidence/commercial-reading.json>)。

历史载体和固定源码没有因此全文审计；实际Main／PLM／IAM原件查询、共同事务／单writer及64AC运行仍未证。关联：[OPP](<D:/CP6/docs/CP6_开发设计文档_20261010/modules/CRM-OPP-01.md>)、[ACT](<D:/CP6/docs/CP6_开发设计文档_20261010/modules/CRM-ACT-01.md>)、[INT-COM](<D:/CP6/docs/CP6_开发设计文档_20261010/modules/INT-COM-01.md>)。

[U11]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d3/d35f1741e1c6f641__HISTORY__handoffs__PLAN-REUSE-01B__documentation__MD02__acceptances__A-CRM__CRM-HO-01__UA-20260930-A-CRM-HO-MD01.md.txt:11>
[S39]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/af/affc1caf67bbf423__CRM-HO-01_商业交接与结果跟踪_前后端开发Spec_v1.0_DRAFT.md:39>
[S43]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/af/affc1caf67bbf423__CRM-HO-01_商业交接与结果跟踪_前后端开发Spec_v1.0_DRAFT.md:43>
[S63]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/af/affc1caf67bbf423__CRM-HO-01_商业交接与结果跟踪_前后端开发Spec_v1.0_DRAFT.md:63>
[U32]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d3/d35f1741e1c6f641__HISTORY__handoffs__PLAN-REUSE-01B__documentation__MD02__acceptances__A-CRM__CRM-HO-01__UA-20260930-A-CRM-HO-MD01.md.txt:32>
[R42]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/10/101e48b852c09667__SR-20260930-A-CRM-HO-MD01-FINAL.md:42>
[U51]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d3/d35f1741e1c6f641__HISTORY__handoffs__PLAN-REUSE-01B__documentation__MD02__acceptances__A-CRM__CRM-HO-01__UA-20260930-A-CRM-HO-MD01.md.txt:51>
[S49]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/af/affc1caf67bbf423__CRM-HO-01_商业交接与结果跟踪_前后端开发Spec_v1.0_DRAFT.md:49>
[S590]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/af/affc1caf67bbf423__CRM-HO-01_商业交接与结果跟踪_前后端开发Spec_v1.0_DRAFT.md:590>
[S124]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/af/affc1caf67bbf423__CRM-HO-01_商业交接与结果跟踪_前后端开发Spec_v1.0_DRAFT.md:124>
[S516]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/af/affc1caf67bbf423__CRM-HO-01_商业交接与结果跟踪_前后端开发Spec_v1.0_DRAFT.md:516>
[S77]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/af/affc1caf67bbf423__CRM-HO-01_商业交接与结果跟踪_前后端开发Spec_v1.0_DRAFT.md:77>
[S628]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/af/affc1caf67bbf423__CRM-HO-01_商业交接与结果跟踪_前后端开发Spec_v1.0_DRAFT.md:628>
[S313]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/af/affc1caf67bbf423__CRM-HO-01_商业交接与结果跟踪_前后端开发Spec_v1.0_DRAFT.md:313>
[S351]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/af/affc1caf67bbf423__CRM-HO-01_商业交接与结果跟踪_前后端开发Spec_v1.0_DRAFT.md:351>
[S518]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/af/affc1caf67bbf423__CRM-HO-01_商业交接与结果跟踪_前后端开发Spec_v1.0_DRAFT.md:518>
[S522]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/af/affc1caf67bbf423__CRM-HO-01_商业交接与结果跟踪_前后端开发Spec_v1.0_DRAFT.md:522>
[S530]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/af/affc1caf67bbf423__CRM-HO-01_商业交接与结果跟踪_前后端开发Spec_v1.0_DRAFT.md:530>
[S546]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/af/affc1caf67bbf423__CRM-HO-01_商业交接与结果跟踪_前后端开发Spec_v1.0_DRAFT.md:546>
[S554]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/af/affc1caf67bbf423__CRM-HO-01_商业交接与结果跟踪_前后端开发Spec_v1.0_DRAFT.md:554>
[S564]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/af/affc1caf67bbf423__CRM-HO-01_商业交接与结果跟踪_前后端开发Spec_v1.0_DRAFT.md:564>
[S570]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/af/affc1caf67bbf423__CRM-HO-01_商业交接与结果跟踪_前后端开发Spec_v1.0_DRAFT.md:570>
[S120]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/af/affc1caf67bbf423__CRM-HO-01_商业交接与结果跟踪_前后端开发Spec_v1.0_DRAFT.md:120>
[S172]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/af/affc1caf67bbf423__CRM-HO-01_商业交接与结果跟踪_前后端开发Spec_v1.0_DRAFT.md:172>
[S209]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/af/affc1caf67bbf423__CRM-HO-01_商业交接与结果跟踪_前后端开发Spec_v1.0_DRAFT.md:209>
[S339]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/af/affc1caf67bbf423__CRM-HO-01_商业交接与结果跟踪_前后端开发Spec_v1.0_DRAFT.md:339>
[S536]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/af/affc1caf67bbf423__CRM-HO-01_商业交接与结果跟踪_前后端开发Spec_v1.0_DRAFT.md:536>
[S238]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/af/affc1caf67bbf423__CRM-HO-01_商业交接与结果跟踪_前后端开发Spec_v1.0_DRAFT.md:238>
[S87]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/af/affc1caf67bbf423__CRM-HO-01_商业交接与结果跟踪_前后端开发Spec_v1.0_DRAFT.md:87>
[S574]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/af/affc1caf67bbf423__CRM-HO-01_商业交接与结果跟踪_前后端开发Spec_v1.0_DRAFT.md:574>
[S116]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/af/affc1caf67bbf423__CRM-HO-01_商业交接与结果跟踪_前后端开发Spec_v1.0_DRAFT.md:116>
[S361]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/af/affc1caf67bbf423__CRM-HO-01_商业交接与结果跟踪_前后端开发Spec_v1.0_DRAFT.md:361>
[S359]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/af/affc1caf67bbf423__CRM-HO-01_商业交接与结果跟踪_前后端开发Spec_v1.0_DRAFT.md:359>
[S367]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/af/affc1caf67bbf423__CRM-HO-01_商业交接与结果跟踪_前后端开发Spec_v1.0_DRAFT.md:367>
[S622]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/af/affc1caf67bbf423__CRM-HO-01_商业交接与结果跟踪_前后端开发Spec_v1.0_DRAFT.md:622>
[S641]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/af/affc1caf67bbf423__CRM-HO-01_商业交接与结果跟踪_前后端开发Spec_v1.0_DRAFT.md:641>
[S357]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/af/affc1caf67bbf423__CRM-HO-01_商业交接与结果跟踪_前后端开发Spec_v1.0_DRAFT.md:357>
[S649]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/af/affc1caf67bbf423__CRM-HO-01_商业交接与结果跟踪_前后端开发Spec_v1.0_DRAFT.md:649>
[S657]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/af/affc1caf67bbf423__CRM-HO-01_商业交接与结果跟踪_前后端开发Spec_v1.0_DRAFT.md:657>
[S665]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/af/affc1caf67bbf423__CRM-HO-01_商业交接与结果跟踪_前后端开发Spec_v1.0_DRAFT.md:665>
[S753]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/af/affc1caf67bbf423__CRM-HO-01_商业交接与结果跟踪_前后端开发Spec_v1.0_DRAFT.md:753>
[S130]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/af/affc1caf67bbf423__CRM-HO-01_商业交接与结果跟踪_前后端开发Spec_v1.0_DRAFT.md:130>
[S693]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/af/affc1caf67bbf423__CRM-HO-01_商业交接与结果跟踪_前后端开发Spec_v1.0_DRAFT.md:693>
[S695]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/af/affc1caf67bbf423__CRM-HO-01_商业交接与结果跟踪_前后端开发Spec_v1.0_DRAFT.md:695>
[S93]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/af/affc1caf67bbf423__CRM-HO-01_商业交接与结果跟踪_前后端开发Spec_v1.0_DRAFT.md:93>
[S105]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/af/affc1caf67bbf423__CRM-HO-01_商业交接与结果跟踪_前后端开发Spec_v1.0_DRAFT.md:105>
[S754]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/af/affc1caf67bbf423__CRM-HO-01_商业交接与结果跟踪_前后端开发Spec_v1.0_DRAFT.md:754>
[S5482]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/af/affc1caf67bbf423__CRM-HO-01_商业交接与结果跟踪_前后端开发Spec_v1.0_DRAFT.md:5482>
[S699]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/af/affc1caf67bbf423__CRM-HO-01_商业交接与结果跟踪_前后端开发Spec_v1.0_DRAFT.md:699>
[S408]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/af/affc1caf67bbf423__CRM-HO-01_商业交接与结果跟踪_前后端开发Spec_v1.0_DRAFT.md:408>
[S711]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/af/affc1caf67bbf423__CRM-HO-01_商业交接与结果跟踪_前后端开发Spec_v1.0_DRAFT.md:711>
[S404]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/af/affc1caf67bbf423__CRM-HO-01_商业交接与结果跟踪_前后端开发Spec_v1.0_DRAFT.md:404>
[S6067]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/af/affc1caf67bbf423__CRM-HO-01_商业交接与结果跟踪_前后端开发Spec_v1.0_DRAFT.md:6067>
[S6138]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/af/affc1caf67bbf423__CRM-HO-01_商业交接与结果跟踪_前后端开发Spec_v1.0_DRAFT.md:6138>
[S734]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/af/affc1caf67bbf423__CRM-HO-01_商业交接与结果跟踪_前后端开发Spec_v1.0_DRAFT.md:734>
