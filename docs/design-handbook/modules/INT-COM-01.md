# INT-COM-01 · CRM 与 Main 商业交接合同

整理状态：`core_semantics_consolidated`。有效正文为 **v1.0.2 R2**，SHA `5282d5545f8c9b1db11f649541ca35af8dd1d9df1264af8df6a4ed34f2fd0c63`，706,603 bytes / 10,758 行。2026-10-01 授权接受覆盖正文旧候选标签；六 SPEC 与原 DP04/08/10/12/13 的设计已接受。106 AC 均 NOT_RUN，12 个实际生产门均 UNPROVEN。本册写到的是已接受的设计合同，不能据此宣称已接线或业务验收通过。[A3][C83][S551]

## 1. 业务目的、操作者和 Owner 边界

销售在 CRM 保存真实需求、预测与依据，提交一次准确商业意图；Main 决定受理、请求补件或最终拒绝，再由各专业 Owner 产生映射、报价或订单商业事实；CRM 原消费者显示这些事实及当前知识。设计闭环由三个独立提交边界组成，页面分别显示运输、Main 业务、CRM 投影。一次 HTTP 202、Broker ACK、接收 SUCCESS、查询任务 COMPLETE 都不能替代后两项。[S54][S56][S158][S308]

|操作者/Owner|职责与真实业务事实|不能越界的工作|
|---|---|---|
|CRM 销售 / OPP|保存需求与资格；确认 INITIAL/SUPPLEMENT；OPP 与 HO 共同提交原意图|不直接产生 Main Intake、BP、Quote、Order 或客户接受|
|CRM HO|不可变 H/V、路由、原体、投递和原键查询协调|不做第二个商业结果消费者，不重新计算 Won|
|Main Intake 操作员或明确登记的规则服务|读完整原需求、当前字段政策与用途；决定 ACCEPTED/NEEDS_INFO/REJECTED_FINAL|技术故障不能当客户业务拒绝；受理不自动创订单|
|Main BP|真正候选、人工决定、Mapping/Claim/原事件|ACC 仅消费七字段关系，不自行根据同名创建客户|
|Main QTN|真实 QuoteVersion/Control、H06 原结果与原链恢复|报价接受不等 Order Confirm；不向 H06 加 AcceptanceFact 字段|
|Main Sales|责任组、准确订单范围、确认/取消/替换及 H08|C03 OrderCreated 不升级成 Confirm；不让两组重复同一经济份额|
|ACC / OPP 原消费者|各自事务保存原 Inbox、结果历史、当前投影和真实回执|消息接收成功不先标 APPLIED 再异步补业务|
|运维恢复人员|受权原体重排、原键对账、有界投影重建|不能 forceWon、force 释放未知保护、重新执行 Main 主账|

需求未知 BOM 不自动阻断询价/内部估算；订单贡献依原币种向量及明确确认，不用 FX 猜算；技术身份由真实 Owner 原件支持，不是业务事实。[S66][S196]

## 2. 当前规范组合与接受边界

|材料|当前作用|准确边界|
|---|---|---|
|R2 主文 + INDEX|六 SPEC DG02 REWIRE，H03/H04/H06/H08；R1 F01–F05 和 R2 传播均在正文|标题仍 REVIEW_R2_CANDIDATE 是原冻结字节，不能选旧稿|
|INT-COM-acceptance.md / CURRENT.json|2026-10-01T05:04:49Z 用户授权代理接受；Stage80→100，唯一增量一次|100 是设计接受，不是 runtime100；保持五原决定，没有虚构 D01|
|找回独审 R2_BOUNDED_REVIEW，57 行|PASS_DESIGN_DOCUMENT；只复核 R1-F03-01/02 和直接传播|沿用首轮/R1已关闭 F01/F02/F04/F05，不声称新完整手审10,758行|
|SR FINAL，374行|同一 CURRENT 的接受证据及独审内容|静态159 JSON、52关系、15源hash/13窗口结果是原评审文书计算，不是本轮执行|
|CURRENT 所列 2,189 行关键窗口包|准确 SHA `182abcab40dcb70bd79f689c1ba77b62080396a26c9142e2a9b050842efb6772`|本轮尚未逐段闭合，不能用已知目录宣称全文已读|

独审已关闭的 R2 问题是联合样例中 B 的完整新身份与引用传播、以及 Sales/Commercial 修订因果。四来源七结果，切点前160、恢复候选180；旧 OPP 独立 B 样例不加入这条联合谱系。这里只复用原结论，未执行其断言。[R5][R17][R23][R36]

CURRENT 撤回过宽的 O1 概述：“撤销请求先于 commit 就阻止所有旧 grant”。当前正确规则是立即阻新 grant，已有 grant 按 Execution 终态收口；原文没有因此修改，不是新设计问题。[C83][S634]

## 3. 实体、字段、身份与不变量

Handoff 根键 **(T, SourceSystem=CRM_HO, H)** 不含 L。V 是该根下连续正 Int64 字符串；Authority/L/Account/Opportunity/ProcessingType 在首次准确 V1 冻结，不能换 L 另建一个根。首次收到 V>1 只保受信 WAITING_PREDECESSOR，不抢占永久 V1 绑定。不同 T 可合法拥有同 H；同 T 跨 L 同 H 是绑定冲突。[S695][S697][S701]

Id 小写规范 UUID；Key 是1–128字符 opaque、无控制/首尾空白且 BIN2 精确相等；Rev 1..Int64max 十进制字符串，只有明确未应用头可0；Hash 64小写 hex；Utc 毫秒 Z。封闭 JSON 拒未知/重复键、NaN、注释、尾逗号、孤立 surrogate；nullable 必须显式 null。内部商业 body≤1MiB，公共请求≤256KiB；完整组≤200 members/orders/retirements，不分页应用半组。金额固定4位字符串，单值 decimal(19,4)、合计(28,4)溢出拒绝。QtnRev/EstRev 安全整数与 ContractRev/opaque Version/8byte rowversion 各自分轴。[S237][S239][S319]

下面保留当前主文的完整跨页面字段登记，避免把客户需求、执行状态和 Owner 身份混成一个“订单状态”。[S85]

每行除明确可编辑外只读；所有前端限制都由后端再次检查。默认值只能来自当前准确上下文，未取得值为null/不可用，不填0或本人。

|FIELD|中文/绑定|类型 长度 空值 来源|控件/权限/持久化|
|---|---|---|---|
|FIELD01|交接 HandoffId|Id UUID非空，HO提交分配|只读定位；HO_READ；HoRequestVersion|
|FIELD02|请求 RequestVersion|正Int64十进制字符串，非空|版本选择器只列实际版本；不得Number转换；原H/V|
|FIELD03|租户/法人|T取会话；targetLegalEntityKey Key1–128来自有权route|租户不可编辑；目标仅INITIAL可选，SUPPLEMENT冻结；目标Scope|
|FIELD04|需求 requirement|RequirementInput；summary1–4000，行description1–1000，完整类型附录A|只在原OPP需求编辑保存；交接弹窗只读；需求版本|
|FIELD05|需求行 quantity/unitCode|decimal(19,6)规范串或成对null；unitCode≤32|单位来自原字典，未知不推转换；准确lineId|
|FIELD06|预测 forecast.estimatedNet|Money或null；金额decimal19,4；固定净额口径|来源OPP forecast版本，提交H03不能null；不是正式价格|
|FIELD07|目标处理 processingType|仅ESTIMATE_AND_QUOTE|固定只读，不增加另一个HO processingType字段|
|FIELD08|附件 evidenceRefs|准确owner/key/version/usePurpose，商业用途COMMERCIAL_HANDOFF|从实际获准证据候选选；引用版本不复制PII；授权缺一整项拒绝|
|FIELD09|前驱 prior|INITIAL null；SUPPLEMENT七成员全部非空，附录A|受权HandoffReadPage选择；不手填MainReceiptId/digest|
|FIELD10|传输 delivery.state|PENDING/IN_FLIGHT/BROKER_ACKED/PAUSED/DEAD|只读；原Ho DeliveryRevision/CAS；attempts是次数不是业务版本|
|FIELD11|Main受理 intake|UNKNOWN/NEEDS_INFO/ACCEPTED/REJECTED_FINAL；无结果ID/ordinal为null|原OPP Intake；缺项用安全代码，错误不回隐藏内容|
|FIELD12|BP候选|CandidateSet完整visibility/items/setHash/scopeRevision|原B16；多候选人工明确选择，隐藏候选REVIEW_REQUIRED不可自动选首项|
|FIELD13|BP映射|MappingRef七字段事件对应五元组|Main决定、ACC只读投影；名/税号不是唯一键|
|FIELD14|报价 net/quoteVersion|net固定4位字符串/币种/政策，version opaque Key|原QuoteReadPage；无金额权net=null并标redactedFields，不可回写|
|FIELD15|报价 tax/gross|wire固定null|不展示估算税/含税总价，不借NET Money代税|
|FIELD16|订单结果|orders/members/coverage完整责任组|原CommercialView/GroupReadPage；Created不可推Won|
|FIELD17|原因 reason|HO1–2000；本域Main决策1–1000；依赖保持原上限|必填textarea；拒控制字符与空白-only；审计加密，消息只安全码|
|FIELD18|原操作定位|operationKind/commandKey；EventId/H/V|提交前冻结；刷新只存不含PII安全locator，敏感body不localStorage|
|FIELD19|各时点|ownerRecordedAt/receivedAt/appliedAt/asOf UTC毫秒Z|各自显示，不混成全球UpdatedAt排序|
|FIELD20|权限/能力|availableActions/readFields/blockingCodes/accessStamp|动态禁用原因；按钮隐藏不是服务端授权|

当前 read 的金额权限必须同时裁剪 current/known/lastProven、比例、金额排序、计数与导出，不能通过派生显示泄露；历史保存不等用户永远可见。[S112]

Main 新增 DATA 表是设计推荐，不是现库已存在。FK 都带 T，NO ACTION，不跨数据库 FK；原 OPP/HO/ACC/BP/QTN/Sales 仍各持其账，不新增 IC_Won 或 IC_QuoteMaster。[S375]

|DATA|表/键|逐字段及空值|约束/索引|
|---|---|---|---|
|DATA00|main_ic.HandoffIdentity PK(T,SourceSystem,H)|全租户H根；Authority/L/Account/Opportunity/ProcessingType首次冻结，完整字段与UQ/FK见R1 F04|所有入口在不含L的缺行锁下先核根；跨L异义隔离|
|DATA01|main_ic.IntakeSlot PK(T,SourceSystem,H), FK DATA00|Authority、AccountId、OpportunityId、ProcessingType=ESTIMATE_AND_QUOTE、CurrentRequestVersion bigint≥0、SlotVersion≥1、RouteVersion、RowVersion|同H不可换源target/account；IX(T,L,OpportunityId)；首次未受理current=0，不造v1业务结果|
|DATA02|IntakeRequest PK(T,SourceSystem,H,V), FK Slot|OriginalEventId Key、IntentId UUID、IntentDigest Hash、OriginalBodyCipher/BodyHash/KeyVersion、SourceTrustJson、ReceivedAt、State RECEIVED/WAITING/DECIDED/CONFLICT、PriorV NULL仅V1、DecisionId UUID NULL、SourceSnapshotCipher、SourceWatermark Key NULL未证、NextAttemptAt NULL终态|UQ(T,Authority,OriginalEventId)，查冲突仍比完整身份；UQ(T,SourceSystem,H,V,IntentId)；CHECK V1前驱NULL/后继V-1；原body不可UPDATE|
|DATA03|IntakeDecision PK(T,SourceSystem,H,V,DecisionId), FK Request|Outcome、IntakeId Key NULL只NEEDS_INFO/REJECTED、MainReceiptId Key、MissingCodesJson、ErrorCode Key NULL仅非拒绝、Reason nvarchar1000、OwnerEvidenceCipher/KeyVersion、ActorKind/Subject、PolicyVersion、RecordedAt、OperationId UUID|UQ(T,SourceSystem,H,V)普通终态最多1；UQ(T,Authority,MainReceiptId)；NEEDS_INFO缺项非空/其他空；REJECTED error非空/其余null；纠错沿原专用账不覆盖|
|DATA04|IntakeStream PK(T,SourceSystem,H,V), FK Request|Producer、Contract='OPP-INTAKE/1'、Epoch、LastOrdinal≥0、LastDigest/LastReceipt NULL仅0、RowVersion|规范流冻结；不能业务update epoch|
|DATA05|IntakeResult PK(stream,Ordinal), FK Stream/Request|MainReceiptId、PreviousOrdinal/PreviousDigest NULL仅1、OriginalBodyCipher/Hash/BusinessDigest/KeyVersion、OwnerEvidenceCipher、RecordedAt|UQ(stream,MainReceiptId)、ordinal1前驱null；后继prev=n-1且原digest核；永久业务去重|
|DATA06|Command PK(T,ActorKind,Subject,OperationKind,OperationId)|LocatorJson、OriginalInputCipher、OriginalIfMatch、InputFingerprint/KeyVersion、State IN_PROGRESS/COMMITTED/REJECTED_FINAL、SafeResultJson NULL未终态、DecisionId NULL、StartedAt、CompletedAt NULL未终态|终态结果原体不可变；不同Actor不能凭相同UUID查询；IX(T,Subject,CompletedAt)|
|DATA07|TransportOutbox PK(T,MessageId)|RouteVersion、Contract、OriginalBodyCipher/Hash/KeyVersion、BusinessResultRef、CorrelationId/CausationId Key、State READY/LEASED/SENT_UNKNOWN/ACKED/PAUSED/DEAD、AttemptTotal bigint≥0、CycleAttempts int、LeaseToken UUID NULL、LeaseUntil NULL、LeaseRevision bigint≥0、NextAttemptAt、AckAt NULL、LastCode Key NULL、RowVersion|UQ(T,BusinessResultRef,OriginalEmissionMarker=1)；IX(State,NextAttemptAt,LeaseUntil)；原payload不改；LeaseToken/Until成对|
|DATA08|TransportInbox PK(T,RouteVersion,Producer,Contract,Epoch,EventId)|RawCipher/Hash/KeyVersion、ReceivedTrustJson、StreamIdentityJson、BusinessDigest NULL语义未验证、State STORED/WAITING/APPLIED/CONFLICT、AppliedReceiptJson NULL未应用、ReceivedAt/AppliedAt NULL、LeaseToken/Until NULL、NextAttemptAt NULL终态|自然键过长用Hash索引但保存完整字段并逐字段比；普通旧event全局MessageId不能跨T串账|
|DATA09|Context PK(T,ContextId)|Actor/L/H/V、FullQuery/FullObservation/FullExpectedVector Cipher、ContextHash、SlotVersion、PolicyVersion、ExpiresAt、ETag、KeyVersion|五分钟；不含业务成功状态；IX ExpiresAt；权限改变旧上下文不能apply|
|DATA10|RouteBinding PK(T,Authority,L,RouteVersion)|Producer、Consumer、Contract、Epoch、TransportProfile、Topic、PartitionProfile、AppId、ValidatorHash、ApprovedEvidenceRef、Enabled、CreatedAt|生产值均由实际部署证据填；行未齐不可Enabled；不可变版本，新版不改变旧原件路由|
|DATA11|Attempt PK(T,MessageId,AttemptNo)|LeaseToken、StartedAt、FinishedAt NULL在途、Disposition PUBLISHED/FAILED/UNKNOWN、SafeCode NULL成功、TransportReceiptCipher NULL无证、RowVersion|同MessageId保全部attempt，不因人工重排清总次数；迟到旧lease只追加观察|
|DATA12|RecoveryJob PK(T,JobId)|Kind QUERY_ORIGINAL/REBUILD、ScopeJson、OriginalInputCipher、ExpectedGeneration、CandidateGeneration NULL非重建、CursorCipher NULL第一页、State、MissingJson、OwnerCutProofCipher NULL未证、Actor/Reason、CreatedAt/UpdatedAt、RowVersion|UQ(T,OperationKind,OperationId)，CAS generation；不创建Main业务请求|
|DATA13|Quarantine PK(T,IssueId)|ExactIdentityJson、OriginalHash NULL无原件、IncomingHash、IncomingCipher/KeyVersion、TrustCipher、Code、ResolutionRef NULL开放、State、RecordedAt|UQ(T,ConflictIdentityHash)防重复问题；无受信T的输入放独立安全隔离域，不能自行信body建立业务租户|
|DATA14|Audit PK(T,AuditId)|ActorKind/Subject、OperationId、ScopeRef、Action、BeforeRef NULL初始、AfterRef NULL失败、ReasonCipher/KeyVersion、PolicyVersion、RecordedAt|审计与业务同txn；普通日志只trace/code，不能漏敏感body|

原体/结果不可覆写，Hash 命中必须比较完整自然字段。IntakeDecision 对每(T,CRM_HO,H,V)普通终态最多1；合法纠错另走原专用账。Transport Inbox 若不能与原业务同 DB 更新，则只声明 STORED，完整业务 Inbox 自己承担原子应用；运输层不能吞掉 WAITING 续办。[S382][S395]

F01 另有 IntakeReadScope(T) 的 QueryGeneration 和 DetailRead(T,detailReadId)：保存 Actor/H/V/BodyHash/完整加密响应、scope/字段政策/requestGeneration/ExpiresAt。新收件、决定、状态变更共同事务推进 query generation。到期删除的是准备上下文，不能删业务原件。[S580]

保护协议的 ProtectionControl、Grant、ControlGrant 与 ProtectedExecution，以及恢复的 Source/Page/完整 Plan，都必须持久；详细字段及各个 UQ 在第6节。[S643][S645][S691]

## 4. 状态流程、业务动作和完整写集合

**CRM 提交**：先保存唯一 O 与冻结原 body/header，终态恢复优先；新动作重新检查当前资格、需求/预测、Account/关系、COMMERCIAL_HANDOFF 证据用途、路由。一个真实 CRM DbTransaction 同时写 OPP CommercialIntent/HandoffLink/必要TargetBaseline/Current/Pending/Supersession/History/Command/ReadGeneration 与 HO Header/RequestVersion/RouteBinding/Delivery/唯一原字节 Outbox/Audit。参与者不得远程调用或自行 commit。不能真实 Enlist 时 COMMIT_GUARD_UNPROVEN，不先存一半。[S158][S160]

**Main 接收和受理**：可靠接收保存原 UTF8/BodyHash/ReceivedTrust；RECEIVED/WAITING 仅保管。业务处理先全租户 H 根，再 V/前驱。V1 无前驱；Vn 仅能接原 n−1 NEEDS_INFO，完整 Supersession 的原 receipt/digest/slot/evidence 匹配。已 ACCEPTED/REJECTED_FINAL 不能普通补件绕过。[S162][S164]

Main M03 执行前须有真实原请求详情、来源完整性、当前本地 IAM/字段门以及已共同采用的有限用途保护。NEEDS_INFO 必须实际缺项且 missingFieldCodes 非空；ACCEPTED 必须真实 IntakeId/受理需求记录；REJECTED_FINAL 要实际有权裁决和 errorCode。临时故障只技术等待。Main 同一事务写 Inbox执行态、Slot/Request/Decision/SourceBinding、真实业务受理记录、ResultHistory/Stream/唯一 OPP-INTAKE Outbox、Command/Audit 和 ProtectedExecution COMMITTED。此处不分配 Quote/OrderId。[S170][S172][S173][S309][S632]

**H04 映射**：真实 AccountIdentityAdapter → BP B15观察 → B16完整候选/Head/Claim → 人工 USE_EXISTING 或经完整权限 CREATE_AND_LINK → B25准确决定 → B17预览/B18提交。多候选、隐藏候选、Claim占用不能自动选首项。Main Mapping/Claim/必要BP/七字段Event/Outbox/Audit/Command 同事务；ACC 独立事务应用。2xx 只是 ACKED_PROCESSING，只有当前 Main Head 与 CRM 精确五元组一致且无冲突、generation仍当前才 MATCHES_CURRENT；AHEAD 暂停修复，读失败即 UNKNOWN，旧绿色仅曾匹配。[S180][S182][S184]

**H06**：只用完整 OPP-QUOTE/1；DRAFT 是已封存版本未 ISSUE，不是可变草稿。source8成员、net4成员、ownerEvidence4成员、Sequence3成员保留，tax/gross=null。相同 QuoteVersion 的 NET/币种/口径/来源/身份不可改；状态须合法连续后继，新版须准确 supersedes。CRM保存原Inbox、OwnerResult、QuoteReferenceVersion、Head/回执同事务；不改 Forecast/Target/Won。缺链查询 QTN I13 原 event/trust，不从 GET 当前摘要造历史；Fact 补证不扩 H06 wire。[S188][S190][S192]

**H08**：Sales 原责任谱系提供整个 group 的 orders/members/retirements/coverage/changeProof。每经济份额至多一当前组，Draft/Created 无确认贡献。CRM 按 H/Intake槽→Group槽→完整自然键 Claims→受影响 Opp UUID序→Result/贡献/投影/Inbox；只替换本组快照，再从已提交所有组摘要重算各币 C−X。并发两组都要保留，不能金额增量累加；正有效净额的 Won 遵 OPP，零价有效确认另标而不触发金额型 Won。[S196][S198]

取消100中的60而有有效40，仍 E40；替换旧60为20+40仍60；全取消不删 FirstWin。缺链/冲突令 currentAmounts=null，known/lastProven 不冒充全量。Intake最终拒绝与确认冲突走准确 IntakeCorrection/Resolution，不能最新时间胜出。[S200]

业务补件、运输重排、映射替换、Owner纠错、原键查询、投影重建是六种不同动作。最终拒绝后真实新需求须明确新 INITIAL；查空、超时或旧404不得当“已拒绝”而新建。[S460][S462][S464][S466][S468]

## 5. 接口、wire 与跨域契约

下面列出主文的完整业务入口。API编号是文档定位，不是已登记 FunctionId；Main 新工作面及 Required 内部端点均须实际采用。[S74][S275]

|API|Method/Path|完整入出|权限/副作用|
|---|---|---|---|
|API-C01|POST /api/crm/v1/opportunities/{id}/handoffs/preview|原HandoffPlan→原Preview<HandoffPlan>|原OPP/HO当前权限；准备读|
|API-C02|POST同根 /handoffs/apply|原ApplyPreview<HandoffPlan>→HandoffReceipt|原Key/If-Match；CRM完整本地事务|
|API-C03|GET同根 /handoffs、/quotes、/commercial|原HandoffReadPage/QuoteReadPage/CommercialView|当前字段权、no-store|
|API-C04|POST /internal/crm/v1/opportunity-intake-results、-quote-results、-commercial-results|原IntakeResult/QuoteResult/CommercialResult→原IngestReceipt|仅实际注册服务；唯一Owner本地事务|
|API-C05|POST /internal/crm/v1/account-erp-mappings|原七字段AccountErpMappingEvent→ACC原处理回执|准确ACC服务上下文；不另设计替代回执|
|API-M01|POST /internal/main/v1/commercial-intakes/receive|原HandoffEnvelope→封闭运输响应见下文|Required服务route；接受原字节持久Inbox；不是业务终态|
|API-M00Q|POST /api/main/v1/commercial-intakes/query|IcCandidateQuery→IcCandidatePage|R1 F01当前受权范围候选；无业务写|
|API-M00D|POST /api/main/v1/commercial-intakes/detail|IcDetailQuery→IcRequestDetail|R1 F01原不可变请求逐区块裁剪及真实DetailRef|
|API-M02|POST /api/main/v1/commercial-intakes/context|IcIntakeContextQuery→IcIntakeContext|新候选受理面，intake.read/decide范围；持久短时只读上下文|
|API-M03|POST /api/main/v1/commercial-intakes/decisions|IcIntakeDecisionInput→IcIntakeCommandResult|Idempotency-Key UUID、真实强If-Match；Main原子受理|
|API-M04|GET /api/main/v1/commercial-intakes/commands/{operationId}|无→IcOriginalCommandRead|当前独立intake.command-result.read；不要求当前decide|
|API-M05|POST /internal/main/v1/crm-handoff-originals/query|原MainOriginalQuery→MainOriginalPage，仅kind COMMERCIAL|Required实现HO M01原contract；读原键，目录snapshot|
|API-M06|POST /internal/main/v1/quotations/original-results/query|QTN QuoteOriginalQuery→QuoteOriginalRead|原QTN I13；准确原区间|
|API-M07|POST /internal/main/v1/bp/mapping-snapshots|BP MappingSnapshotQuery→MappingSnapshotResult|原BP I03|
|API-R01|POST /internal/crm/v1/commercial-projections/rebuilds|IcRebuildInput→IcRebuildRead|Required受权恢复协调器；不能普通销售呼叫|
|API-R02|GET同根 /{jobId}|无→IcRebuildRead|准确scope恢复读|

Main 接收 M01 的 `{status:SUCCESS|RETRY|DROP}` 是运输响应；SUCCESS 仅原体可持久恢复。DROP 前要安全隔离或明确未受理且无业务效果，数据库不可用不得 DROP。它不返回未来 operationId 来制造受理事实。[S296]

Main 新增受理与读取的准确 DTO 如下；Owner 同名类型必须按命名空间区分，不能把 BP.SourceRef 当 OPP.EvidenceRef。[S235][S256]

```ts
interface IcReceiveAck {status:'SUCCESS'|'RETRY'|'DROP';}
interface IcRequestLocator {tenantId:Id;authority:Key;legalEntityKey:Key;handoffId:Id;requestVersion:Rev;intentDigest:Hash;}
interface IcActor {kind:'USER'|'SERVICE';subject:Key;tenantId:Id;legalEntityKey:Key;policyVersion:Key;}
interface IcSourceObservation {locator:IcRequestLocator;knowledge:'KNOWN_ALLOW'|'KNOWN_DENY'|'UNPROVEN';source:SourceHandoffRef;sourceWatermark:Key|null;purpose:'COMMERCIAL_HANDOFF';revoked:boolean|null;complete:boolean;observedAt:Utc|null;proof:EvidenceRef|null;codes:Key[];}
interface IcIntakeContextQuery {handoffId:Id;requestVersion:Rev;detail:IcDetailRef;}
interface IcIntakeContext {detail:IcDetailRef;locator:IcRequestLocator;contextId:Id;contextHash:Hash;expiresAt:Utc;editEtag:string;slotVersion:Rev;sourceObservation:IcSourceObservation;currentResult:OrdinaryIntakeResult|null;allowedDecisions:('ACCEPTED'|'NEEDS_INFO'|'REJECTED_FINAL')[];blockingCodes:Key[];}
interface IcIntakeDecisionInput {contextId:Id;contextHash:Hash;expectedSlotVersion:Rev;outcome:'ACCEPTED'|'NEEDS_INFO'|'REJECTED_FINAL';missingFieldCodes:Key[];errorCode:Key|null;reason:string;decisionEvidence:EvidenceRef[];}
interface IcIntakeCommandResult {operationId:Id;state:'COMMITTED'|'REJECTED_FINAL';scope:'MAIN_INTAKE';locator:IcRequestLocator;mainReceiptId:Key|null;intakeId:Key|null;resultEventId:Key|null;completedAt:Utc;safeCode:Key|null;replayed:boolean;}
interface IcOriginalCommandRead {operationId:Id;knowledge:'FOUND'|'IN_PROGRESS'|'NOT_OBSERVED'|'UNPROVEN';result:IcIntakeCommandResult|null;noEffectProven:false;}
interface IcProblem {code:Key;status:number;traceId:Key;operationId:Id|null;commitKnowledge:'NOT_STARTED'|'NOT_APPLIED_FINAL'|'COMMITTED'|'UNKNOWN';nextAction:'NONE'|'QUERY_ORIGINAL'|'REFRESH_CONTEXT'|'CONTACT_OWNER';fieldCodes:{path:string;code:Key}[];}
interface IcLegacyReadObservation {kind:'BP'|'QUOTE'|'ORDER';key:Key;knowledge:'OBSERVED'|'NOT_FOUND_OR_NOT_CONFIGURED'|'NOT_AUTHORIZED'|'UNAVAILABLE'|'INVALID';originHttpStatus:number|null;safeCode:Key|null;bodyHash:Hash|null;observedAt:Utc;isExecutionPermit:false;}
interface IcReplaySelection {contract:'OPP-INTAKE/1'|'OPP-QUOTE/1'|'OPP-COMMERCIAL/1';streamNaturalKey:Key;fromOrdinal:Rev;toOrdinal:Rev;}
```

KNOWN_ALLOW 要 complete=true、revoked=false、proof/watermark/observedAt非null；UNPROVEN 不得伪许可。COMMITTED 的结果身份实际非null，只有 NEEDS_INFO/REJECTED_FINAL 的 intakeId可null。OriginalCommandRead 只有 FOUND 有 result，noEffectProven 始终false。[S273]

Context 保存完整查询/Observation/ExpectedVector/Actor，5分钟且不超过最短源政策窗口。ContextHash 是 `IC-INTAKE-CONTEXT-1\n + Canonical(完整Context删除contextHash/editEtag)`；ETag 是服务器带密钥认证 Actor/T/L/context/slot 的强 tag，不能引号包客户端 hash。缺 If-Match 428、弱/多/* tag400；终态原命令先核完整原输入及 header 后回原结果，不因Context过期重新执行。[S298]

|跨域链|wire 与唯一消费者|主要限制|
|---|---|---|
|H03|CRM-HO-OPP/1 → MainIntake|冻结 route/epoch在可信传输证明，不给原Envelope加字段|
|H03结果|OPP-INTAKE/1 → 原OPP intake endpoint|普通结果或准确IntakeCorrectionSuccessor联合|
|H04|AccountErpMappingEvent → 原ACC|仅 eventId/accountId/legalEntityKey/mappingRevision/state/businessPartnerKey/sourceAsOf；T在可信上下文|
|H06|OPP-QUOTE/1 → 原OPP quote endpoint|原NET、来源、opaqueQuoteVersion，tax/gross=null|
|H08|OPP-COMMERCIAL/1 → 原OPP commercial endpoint|全组所有经济成员、范围和前驱；不接半组|
|专项纠错|OPP-RESOLUTION/1 / 原IntakeCorrection → 原OPP|原Owner精确候选和冲突集合，无通用force-success|

这些新封闭合同不等旧 C03 六事件 alias。旧 C03 SensitiveNames 拒 lines/tax，新 H03 有 requirement.lines、新Quote有tax:null，直接过旧 validator 会失败。旧 int AggregateVersion、decimal金额、8byte base64版本和 colon partition 不能强转成新 Int64字符串、Money字符串、opaqueVersion或slash规则。[S243][S252][S355]

Required BoundOriginalTransportAdapter 在应用层发送原 Owner 已存原字节，metadata独立；rawPayload 必须实际证明 roundtrip BodyHash一致，不能重新序列化为“相同意思”。RouteBinding 绑定(T,authority,L,producer,contract,epoch)，来自真实通道/登记而非调用者自填 header。新 `IC-SCOPE-HASH/1` 推荐 Canonical[T,L,authority,streamKind,streamNaturalIdentity]的SHA为partition；生产 topic/appId/TLS/ACL/raw配置仍null，不能据设计启用。[S359][S361][S363][S365]

兼容启用按每route双边确认 Producer/唯一Consumer/准确Contract/validator hash、原体矩阵、size/content-type、Trust、T/L、单写者、原体恢复、旧积压及回退。LEGACY_ONLY 只观察，NEW_CANONICAL走原消费者，DUAL_OBSERVED经Owner证绑定且只有一个PrimaryWriter，UNPROVEN隔离。回退保所有已提交事实/幂等账，不清 checkpoint或撤销订单。[S369][S371]

## 6. 事务、幂等、跨 Owner 保护与恢复

### 6.1 原键、永久业务历史和本地锁

新增 Main 命令 O=(T,ActorKind,ActorSubject,intake.decide,operationId)，同时保 H/V 业务 UQ。指纹包含完整规范 body、原 If-Match、HTTP operation、H/V、Actor/T 和密钥版本 HMAC；换 reason/target/token/header 都不是同义。已终态先当前独立查询/披露权+原全文匹配，不强求仍有新decide权；缺解密密钥为UNPROVEN，不猜成功。[S336]

新执行按注册一致 OrderKey 集合取当前 IAM/用途、原O/Execution、全T H缺行根、V、源/Intake。临时发现更早门要释放重来，不持业务锁反取；缺行用事务所有 applock 或等价范围锁。双别名应用靠业务槽和UQ防双写；最后同txn保存Inbox。Transport claim 是单独短事务，网络发送在事务外，回写匹配MessageId+LeaseToken+LeaseRevision，旧worker只能添观察。[S338][S399][S401][S701]

相同 Event 异原BodyHash是运输冲突；同业务序号异 BusinessDigest 即使换Event仍冲突。合法历史语义重复保新原体/Trust别名并回原回执，不要求旧前驱仍当前，不二次贡献。WAITING_SOURCE/WAITING_PREDECESSOR 必须持久续办，不能 Event 已存在就 DUPLICATE；CONFLICT 只能精确 Owner Resolution 后续。[S332][S340][S439]

### 6.2 IC-SOURCE-PROTECTION/1 的闭合有限用途

这个协议不是跨库原子事务，也不是 TTL 远程租约。只保护一次准确源用途，**持久、不得自行到期解除**；progressDeadline 仅报警/请求Abort，未知时不放行源控制变更。原Owner即时安全政策不能容许已有用途等待、所有源控制写者未参与、恢复单代次无法证明，都必须 Disabled。[S586][S590][S592]

|Purpose|保护对象与实际参与者|不能附加的条件|
|---|---|---|
|MAIN_INTAKE_NEW_EFFECT|准确已提交H/V的HANDOFF；HO原意图用途及每个已发送证据用途Owner；Main本地IAM/字段/H/Intake另锁|不以旧TTL观察代最终保护，不要求未来IntakeReceipt才能Prepare|
|CRM_RESULT_PROJECTION|准确Main已提交结果的OWNER_RESULT；Main结果/流Owner认可原事实及机器消费注册；CRM本地源/Group/Claim/投影共同门|不再要求原销售用户现在有编辑/交接新写权|

用户登录、敏感字段披露、附件下载及其他操作授权不在Grant内；撤销立即按原Owner规则切断。业务Grant保留不等内容继续可见。[S588][S623][S639]

准确类型保留如下。实现须逐自然字段核对 execution/subject/participant/代次/原body，Hash只是索引。[S596][S613]

```ts
type IcProtectionPurpose='MAIN_INTAKE_NEW_EFFECT'|'CRM_RESULT_PROJECTION';
interface IcExecutionRef {tenantId:Id;consumerAuthority:Key;operationKind:'intake.decide'|'opportunity.result.apply';operationId:Id;executionFingerprint:Hash;executionGeneration:Rev;participantSetDigest:Hash;}
type IcProtectedSubject={kind:'HANDOFF';locator:IcRequestLocator;source:SourceHandoffRef;originalBodyHash:Hash}|{kind:'OWNER_RESULT';authority:Key;legalEntityKey:Key;source:SourceHandoffRef;stream:StreamIdentity;businessKey:Key;eventId:Key;ordinal:Rev;businessDigest:Hash;originalBodyHash:Hash};
interface IcProtectionPrepare {schemaVersion:'IC-SOURCE-PROTECTION/1';protectionKey:Key;execution:IcExecutionRef;participantAuthority:Key;subject:IcProtectedSubject;purpose:IcProtectionPurpose;expectedControlWatermark:Key;admissionDeadline:Utc;consumerRegistration:EvidenceRef;}
interface IcSourceControlRef {kind:'SUBJECT'|'PURPOSE_POLICY'|'PRODUCER_STREAM';authority:Key;key:Key;watermark:Key;}
interface IcProtectionGrant {protectionKey:Key;execution:IcExecutionRef;participantAuthority:Key;subject:IcProtectedSubject;purpose:IcProtectionPurpose;sourceGeneration:Rev;sourceControlWatermark:Key;state:'GRANTED';grantedAt:Utc;progressDeadline:Utc;automaticExpiry:false;sharedWriterPolicy:Key;protectedControls:IcSourceControlRef[];grantDigest:Hash;grantEvidence:EvidenceRef;}
interface IcProtectionRead {protectionKey:Key;knowledge:'ABSENT'|'GRANTED'|'DENIED'|'SETTLED'|'UNPROVEN';grant:IcProtectionGrant|null;denialCode:Key|null;terminal:IcExecutionTerminal|null;newGrantsBlocked:boolean;asOf:Utc;}
interface IcExecutionTerminal {execution:IcExecutionRef;outcome:'COMMITTED'|'ABORTED';effectScope:'MAIN_INTAKE'|'CRM_PROJECTION';businessReceiptRef:Key|null;effectDigest:Hash|null;abortCode:Key|null;terminalGeneration:Rev;recordedAt:Utc;consumerEvidence:EvidenceRef;}
interface IcExecutionQuery {execution:IcExecutionRef;}
interface IcExecutionRead {execution:IcExecutionRef;state:'PREPARING'|'READY'|'COMMITTED'|'ABORTED'|'UNPROVEN';acceptedGrantDigests:Hash[];terminal:IcExecutionTerminal|null;noEffectProven:boolean;asOf:Utc;}
interface IcExecutionAbort {execution:IcExecutionRef;reasonCode:'SOURCE_REVOKE_REQUESTED'|'PREPARATION_FAILED'|'CONTEXT_EXPIRED'|'PROGRESS_DEADLINE_EXCEEDED';sourceDecision:EvidenceRef;}
interface IcProtectionSettle {protectionKey:Key;execution:IcExecutionRef;terminal:IcExecutionTerminal;}
interface IcProtectionRevocation {subject:IcProtectedSubject;purpose:IcProtectionPurpose;expectedControlWatermark:Key;revocationId:Id;reasonCode:Key;decision:EvidenceRef;}
interface IcProtectionRevocationRead {revocationId:Id;newGrants:'BLOCKED';existingGrants:'SETTLED'|'PENDING_TERMINAL';pendingProtectionKeys:Key[];effectiveControlWatermark:Key|null;requestedAt:Utc;settledAt:Utc|null;}
```

HANDOFF自然保护键(T,H,V,purpose)，L/authority/source/body是不可变绑定；OWNER_RESULT键(T,authority,L,stream,业务key,ordinal,purpose)，Quote业务key=quoteKey、Commercial=groupKey、Intake=mainReceiptId，Event/签名不能另起源槽。完整 participantSet 在准备前固定，失败参与者不能删除。protectionKey=H(IC-PROTECTION-KEY-1,{execution,participantAuthority,subject,purpose})；GrantDigest删除自身与grantEvidence.proofRef。所有Owner根和策略/规范流控制闭包由真实Owner解析，不能让客户端给空保护集。[S613][S625]

1. 消费方短事务持久 PREPARING：原输入/指纹、业务键、全部参与集、generation与可信Registration，无业务效果。
2. 事务外固定顺序 Prepare。源控制根下核原全文/当前服务用途/真实已提交源/expectedWatermark/deadline/撤销状态/全部写者，共同写不可变GRANTED及所有Control→ActiveGrant反向索引。响应丢也已保护，须查原key。
3. 消费方逐项核真实Grant，全部齐全才CAS READY；部分拒绝/失联不能进入业务。已授部分必须先消费方ABORTED，再分别settle。
4. 最终消费txn取当前本地IAM/安全/字段→Execution根X→业务锁。重核READY/原指纹/完整集合/context/无Abort墓碑，业务全写集+Command+Execution COMMITTED同txn；失败全回滚。
5. Source撤销同源txn立即 NEW_GRANTS_BLOCKED，然后向所有未决Execution Abort。Abort与业务争相同Execution根：Abort先则ABORTED永久墓碑、零业务；业务先持根并commit则Abort等后读COMMITTED，不能反改。
6. 源需向已登记消费方终态查询核匹配完整Execution/Terminal，只有真实COMMITTED/ABORTED才SETTLED。所有活跃保护收口后才existingGrants SETTLED及新的effectiveControlWatermark。
7. sourcePrepare丢响应、consumerCommit丢响应、settleACK丢、重启皆按原key持久账恢复。未知超过deadline仍PENDING_TERMINAL；旧worker晚到必须同Execution根，已Abort永拒，已commit回原结果。

以上顺序由正文629–637规定。特别是“撤销请求先于commit”但已有grant时仍可能业务先持根成功；不能替用户作更强零效果承诺。[S629][S630][S631][S632][S633][S634][S635][S636]

Terminal COMMITTED 要实际 businessReceipt/effectDigest非null、abortCode=null，ABORTED反之。noEffectProven仅准确Execution已有ABORTED墓碑为true，不代表Main其他请求全球无效果。ConsumerEvidence只能commit后从权威持久终态 READ COMMITTED读生成，不能预签拟提交内容；源失联/证不明保持GRANTED，不信单一proofRef。[S615][S617]

Required源接口为 `/internal/{owner}/v1/source-protections/{prepare|query|settle}`；消费方为 `/internal/{consumer}/v1/protected-executions/{query|abort}`。owner/consumer由注册Authority映射，用户不能传任意URL或直接调用。原源撤销命令内部执行Revocation状态机，不另给普通用户协议按钮。[S621]

持久表：ProtectionControl保存完整subject/controlWatermark/sourceGeneration/newGrantsAllowed/pendingRevocation；Grant存完整Prepare/Grant密文、原digest、GRANTED/SETTLED、Terminal/KeyVersion，UQ绑定原Execution+participant+subject+purpose；ControlGrant反向索引覆盖SUBJECT/PURPOSE_POLICY/PRODUCER_STREAM全部实际控制根。所有入口、worker、管理员、callback、策略和恢复工具参加同源根；只新服务加锁、旧旁路不参加不合格。ProtectedExecution PK(T,ConsumerAuthority,OperationKind,OperationId)固定generation、指纹/集合/原件/GrantSet/状态/Terminal，业务原UQ仍独立。保护账丢失/双主/回滚恢复即UNPROVEN并阻断，管理员不能force释放；单独事故隔离接口本稿未提供。[S625][S637][S643][S645]

### 6.3 有界整商机投影重建

R01 只接 `{scope:{opportunityId},expectedActiveGeneration,reason}`，T来自可信会话。不能接客户端sources/originalQuery/complete，不允许按L漏掉贡献却称整商机；操作者须有全部涉及来源/法人的恢复权。范围是原消息可推导投影及本域历史，不重新执行销售/Main命令。[S472][S651]

```ts
interface IcRebuildInput {scope:{opportunityId:Id};expectedActiveGeneration:Rev;reason:string;}
interface IcRebuildSource {source:SourceHandoffRef;authority:Key;legalEntityKey:Key;reasons:('DIRECT_HANDOFF'|'ATTRIBUTION_HISTORY'|'CURRENT_GROUP'|'PENDING_CONFLICT')[];query:MainOriginalQuery;}
interface IcDiscoveryStamp {opportunityId:Id;sourceSetGeneration:Rev;handoffLinkGeneration:Rev;attributionHistoryGeneration:Rev;groupBindingGeneration:Rev;conflictSourceGeneration:Rev;ingressGeneration:Rev;unassignedIngressGeneration:Rev;activeProjectionGeneration:Rev;discoveredAt:Utc;sourceSetDigest:Hash;}
interface IcRebuildPlan {planId:Id;scope:{opportunityId:Id};stamp:IcDiscoveryStamp;sources:IcRebuildSource[];participantAuthorities:Key[];querySetDigest:Hash;localHistoryCut:{salesRevision:Rev;commercialRevision:Rev;attributionLedgerDigest:Hash;lossTargetHistoryDigest:Hash};scopeStamp:Key;expiresAt:Utc;}
interface IcDirectoryCutQuery {planId:Id;querySetDigest:Hash;query:MainOriginalQuery;expectedMinimumResults:{stream:StreamIdentity;businessKey:Key;ordinal:Rev;businessDigest:Hash}[];}
interface IcDirectoryCut {cutId:Key;query:MainOriginalQuery;directoryGeneration:Rev;upperDirectorySequence:Rev;manifestDigest:Hash;resultCount:number;complete:boolean;registeredWriterPolicy:Key;issuedAt:Utc;proof:EvidenceRef;}
interface IcDirectoryCutRead {knowledge:'PROVEN'|'UNPROVEN'|'NOT_AUTHORIZED';cut:IcDirectoryCut|null;codes:Key[];}
interface IcDirectoryPageQuery {cutId:Key;query:MainOriginalQuery;pageToken:Key|null;maxResults:100;}
interface IcDirectoryPage {cutId:Key;page:MainOriginalPage;pageSequence:Rev;pageDigest:Hash;nextToken:Key|null;}
interface IcCutRecheckQuery {cut:IcDirectoryCut;}
interface IcCutRecheck {cutId:Key;knowledge:'UNCHANGED'|'ADVANCED'|'UNPROVEN'|'NOT_AUTHORIZED';observedDirectoryGeneration:Rev|null;observedUpperSequence:Rev|null;checkedAt:Utc;proof:EvidenceRef|null;}
interface IcRebuildCutVector {querySetDigest:Hash;cuts:{source:SourceHandoffRef;authority:Key;legalEntityKey:Key;cut:IcDirectoryCut;pagesComplete:boolean;validatedManifestDigest:Hash;latestRecheck:IcCutRecheck}[];vectorDigest:Hash;}
interface IcRebuildRead {jobId:Id;scope:{opportunityId:Id};state:'PREPARING'|'WAITING'|'READY_FOR_CAS'|'COMMITTED'|'CONFLICT';fromGeneration:Rev;candidateGeneration:Rev;plan:IcRebuildPlan;cutVector:IcRebuildCutVector|null;rawCount:number;appliedCount:number;missing:IcReplaySelection[];sourceCoverage:'PROVEN_AT_VECTOR_CUT'|'UNPROVEN';safeCode:Key|null;isMainBusinessChange:false;}
```

服务端在真实scope writer fence下枚举全部OppHandoffLink（所有V/所有L，不只current）、归属历史中from/to含P的每组、现归属于P的Group、受信未决Conflict原候选；转入组原source.opportunityId可不同P，仍取原冻结source/authority/L。完整(T,authority,L,H,V,IntentId,IntentDigest)去重，异义冲突不拆成合法两源。缺历史/原绑定即UNPROVEN。[S679][S680]

SourceSetGeneration由Handoff/Supersession、Attribution、Group绑定/归属、Conflict来源写者同txn推进，另保存分轴；完整新收件推进Ingress，不能归属的可信待核通过tenant未分配入站水位阻门。短txn保存Plan、完整集合、Sales/Commercial与Loss/Target/Attribution/FirstWin历史cut，不能只扫已观察到的消息。[S681][S682]

每个准确source查询固定COMMERCIAL、H/V/IntentDigest/T/L/authority；Required Main cuts / cut-pages / cut-rechecks 适配是新增只读保证，**不向 MainOriginalPage 添字段**。Main各原业务结果及后继/归属声明必须与真实业务commit同txn登记目录，涵盖全部发行者/原Outbox，不能只扫发成功队列。多数据库分别有界cut向量，不称全球同一时点。[S671][S673]

cut manifest按(contract,businessKey,producer/contract/epoch,ordinal,bodyHash)排序，保完整原体/Trust定位；空目录也须真正complete/resultCount0/上界0证明。遍历到nextToken=null，原query除cursor全等且两个nextToken相等，核manifest/resultCount、所有已知expectedMinimumResults、原体/Trust/前驱。缺页、剪裁、缺项、异义页不得pagesComplete。H2只要本地Link存在，即使结果通知全漏仍查目录发现新组。[S675][S683]

影子generation g+1用**同原OPP内核**重放，保本域FirstWin/Loss/Target/Attribution原历史。新结果涉及未授权范围先待核，不擅自扩大权限。逐cut recheck：ADVANCED/UNPROVEN/NOT_AUTHORIZED都重新准备；UNCHANGED只截至checkedAt。最终CRMtxn重取fence及原锁序，重新枚举全文集合，核全部generation/history/Ingress/tenant未分配水位、原active=g及每cut/pages/recheck完整身份，再一次切换，失败全回滚。切换后继续原消费者处理cut后队列。[S475][S684][S685][S686]

结果只能声称 **PROVEN_AT_VECTOR_CUT**。Main可在最后recheck后提交，重建COMMITTED不等“此刻Main全球没有更新”；各Owner asOf/vector必须可见，当前金额仍原OPP `REGISTERED_RESPONSIBILITIES_AS_OBSERVED`。业务若要求绝对实时全球一致，本版没有提供。[S687]

恢复Job持久完整PlanCipher、集合/查询摘要、所有stamp generation、各cut/页manifest/原体、向量、最新recheck、本域history/candidate；RebuildSource/Page存完整自然字段及加密原页。更新recheck需重算VectorDigest，hash不替原证明。重启已COMMITTED返回原g+1，不生g+2/再次FirstWin；缺所有writer共同fence或完整本域历史则不开放切换。[S478][S689][S691]

### 6.4 故障与人工恢复

|失败cut|数据库真实结果|恢复输入与动作|禁止副作用|
|---|---|---|---|
|CRM commit前fault|所有OPP/HO写回滚|确定回滚后原O/冻结body可技术重试|不能生成第二H或半Outbox|
|CRM commit后响应丢|原O/H/V/Outbox已存在|查原OPP O，返回同回执再读原H|不能换Key重新交接|
|Main收件后业务前重启|完整原Inbox RECEIVED/WAITING|持久worker解密原体重新当前门|不能用最新CRM需求代原intent|
|Main已commit结果未发送|原业务结果+Outbox已存在|Outbox续原Event/Body；按H/V查询原结果|不能再创建Intake/BP/Quote/Order|
|broker接受后本地ACK丢|可能已送，Outbox状态UNKNOWN/lease过期|同原Event至少一次重投|不能认业务失败或分配新Event逃去重|
|CRM已应用ACK丢|完整投影/Inbox/回执已commit|原Event返回原DUPLICATE/APPLIED证据|不能二次加贡献/推进CommercialRevision|
|结果到达顺序n+2,n|n+2完整WAITING，n处理后仍缺n+1|请求原Owner缺口，n+1应用后续n+2|不能最高checkpoint跨过缺口|
|Main成功CRM尚未知|Main真实结果保留；CRM未知/等待|原键目录/原Owner重投|不能Main回滚或再次确认|
|服务/用户撤权|历史存储不删；新的授权动作不执行|恢复当前授权后原件续办；有查询权可安全回历史|不能拿历史token长期执行|
|误Tenant/L或Producer|不进入可信业务账|安全隔离和实际Owner查route|不能换body T/L重投|
|密钥/原件损坏|无法证明原消息|REPLAY_EVIDENCE_UNAVAILABLE/INVALID，恢复备份原件再验证|不能从索引/当前GET拼造原body|
|源查空/404|未获原结果证据|保UNKNOWN/noEffectProven=false|不能新生主账|

原HO/BP/Main重排只改原body的调度、DeliveryRevision/lease及审计，不换业务版本。旧C03 replay仅OperationId+byte[8] RowVersion+PayloadSha256+ReasonCode（dependency-recovered/contract-verified），不能直接声称支持新wire。原键query只是job：FOUND结果仍喂同消费者，存在localWaitingKeys仍WAITING；NOT_OBSERVED可令查询job完成，业务仍UNKNOWN。[S460][S468]

保留至少覆盖全部业务/审计/重放义务的永久业务去重、原链、Trust与恢复材料；通用7/30/90天不能删本域唯一证据，WAITING/CONFLICT/未完query/rebuild不得清理。生产retention政策null时cleanup关闭；并非自行承诺永久明文PII。归档要真实可回读、原密钥版本可解密、当前读权及审计，缺证UNPROVEN。[S405]

## 7. 权限、租户、法人和数据披露

用户T/Actor由服务端会话；请求L只是候选且需当前权。机器每T独立注册，service sub与实际用户requestedBy分别记，不能机器冒用户。内部端点不收浏览器Bearer当service。跨T/L/route/authority先拒且不泄对象存在或hidden count；H04从已受信单T通道取T，不从accountId反查任意租户。每个明确附件/证据的新用途逐项核，失权不能悄悄删项后发送不同意图。[S484][S486]

原命令独立查询权与新写权分开；有RQ而失正文权可返回原安全COMMITTED且canOpen=false（沿原DTO），无RQ不泄存在。原body加密+KeyVersion+访问审计，bodyHash另字段权；Secret只保引用，文档深链由注册目的和原Owner短时导航签发，不接任意URL/open redirect、不顺带Confirm。[S488]

旧 ERP 受控读通过固定 cp6-core，key单路径段URI编码、1–20 ASCII字母数字及-_.:，拒 . / ..；correlation1–128同安全字符。tenantid须等token tenant_id；核C01issuer/sub/唯一client_id/scope=cp6.services、tenant/jti规范UUID、client启用同T、ReaderClientIds独立allowlist、tenant有效、token未撤销未到期。AllowAnonymous控制器之下仍有C03.Services认证，DaprApiToken只是sidecar证明。[S206][S208]

旧DTO没L；必须真实Owner key→L映射才能展示，不默认一tenant一法人。200仅旧观察；旧rowversion12字符canonical8bytebase64、旧ApprovalStatus/Amount/CustomerAccepted不当新QTN决定。401/403剔被拒token缓存，本次不偷偷重试；404只能NOT_FOUND_OR_NOT_CONFIGURED；deadline≤10秒含token锁/交换/headers/body；JSON≤65,536、错tenant/key/重复属性/错version整响应拒绝。源HTTP被旧client合并时originHttpStatus=null，不能猜还原。[S210][S214][S215][S216][S218][S219][S221]

## 8. 页面、交互和用户恢复闭环

CRM 沿MSBBCR400/420商机页tabs/drawer显示原交接三轴、报价/商业引用、补件与原键恢复；不存在新“集成总控台”。Main PAGE03工作面是需登记的新候选，不虚构FunctionId。按钮根据当前capabilities；选择切换、scope/fieldPolicy/generation变动清旧Context/token并重新读。[S74][S580]

Main空页完整闭环：M00Q受权候选 → 取真实locator/bodyHash → M00D原请求详情 → 展示原需求与当前可用性 → M02携真实DetailRef → M03冻结Context/Key/header。Candidate仅安全locator/state/time/hash/slot/canRead/code，不回明文需求、理由、人员或Trust；query L空是当前受权全部，显式越权L403而非静默筛去。最多50L，无重复；dateFrom≤To、范围≤366天；states空为全合法态。默认30/60/100，ReceivedAt DESC、H BIN2、V数值DESC稳定页；cursor绑定T/Actor/scope/query/generation/末行，变动409 CURSOR_STALE首查，不能拼页。空受权页不等全Main无请求。[S558][S561][S570]

Detail先查全T H根再核L，无权或不可知均404 REQUEST_NOT_AVAILABLE；原bodyhash不等409 ORIGINAL_REQUEST_CHANGED。source及复合区块逐字段来自原Intent，不用最新需求替旧。任一区块含不可见子字段则整块REDACTED/null；证据引用可见不授权下载，Trust/proof载体不公开。普通ACCEPTED须requirement/forecast/stakeholders/evidenceRefs四块真实VISIBLE；缺读权不是客户缺件，禁止据此NEEDS_INFO/REJECTED。reason/requestedBy可单独遮罩，字段政策未证则敏感块UNPROVEN。[S572][S574]

DetailRef五分钟、不超scope有效期，持久FullResponse/Actor/bodyhash/policy/generation，DetailDigest=`H(IC-DETAIL-1,完整Detail删除reference.detailDigest)`。M02读服务器原Detail逐项重核；M03最终本地字段门再核，不能接受浏览器complete=true。已提交命令仍先独立RQ历史分支。[S576][S578]

不新增CSV/Excel客户导入、批量业务写或附件上传。受权运维导出只安全locator/version/status/code与脱敏审计；完整原body不是销售下载文件。人工恢复界面应明确“仅重排”“仅查询”“Owner纠错”“有界重建”的真实作用。[S129][S300][S458]

## 9. 固定源码证据、复用限制和整合次序

|原文固定来源|真正已知能力|对当前设计的差距|
|---|---|---|
|Main `157630594e3371fe181955d2f6227ff3b6962c84` ErpRequestHandler|旧messageId hash与business request两账；Serializable；Inbox/业务/结果Outbox同SQL|只有旧BP注册/Order创建，无新H03/完整H06/H08；旧OrderFactory按Opportunity已订单全拒，与多责任组不符|
|CRM `c778a3052a4416b82facb07b1244fc98fb6ad8a1` C03 client|固定cp6-core、每tenant凭据、校验/超时/隔离probe|未正式把Account/Link/Process/Opp/Won接成当前商业全链；probe不是E2E|
|Platform `30bd23af6808d217a23878bd9437513043c52834`|Outbox参与模式、leaseCAS、I/O/telemetry；int checkpoint|非任意原JSON直接publisher，不能Int64强塞int，也不能最高checkpoint吞迟到商业审查|
|Main OrderBridge WMS/MES retry hook|既有传输完成/SKIPPED|不是CRM业务投影APPLIED/商业确认|

这些是正文固定SHA的源码分析，不是对dd-1010主线重新跑过的代码验收。[S42][S43][S44][S45][S46][S176]

实施建议按依赖推进：先真正 OP/HO共同事务及准确原体/原O恢复；再Main Intake本域和全部源保护写者/终态恢复；再H04原Owner人工链与ACC读桥；接H06/H08原消费者和原链；最后做真实各Owner目录完整性、有界重建及积压分类。运输先可恢复持久接收，不能先开放新业务按钮再补安全守卫。每项仍需其Own Gate真实采用，整理不替用户批准实施。[S541][S542][S543][S544][S545][S547]

INT-COM接受时将ERP-ORD记作PB01历史功能文本100，PLT-MSG/DEL当时Stage10；这些是当时准确输入记录，不是2026-10-10所有后继的当前结论。必须与现ORD/Platform选定组合核对，不能用旧历史状态覆盖后继。[C239][C248]

## 10. 验收设计、验证证据与未运行范围

106项最终AC以AC105/106及CURRENT/独审为准；正文713仍保留“共104”的R1历史概述，不应遗漏R2两项。全部NOT_RUN；静态JSON parse、原字节hash与关系断言不等Host/SQL/撤权/并发通过。[S713][S715][S823][S824][R7]

|AC范围|要验证的具体结果|
|---|---|
|01–16 H03/H04/H06/H08|本地202、BrokerACK仅分轴；补件同H下一V；实际候选/映射；Quote接受非Won；多组C/X/E；取消有效40；准确拒绝纠错；责任替换守恒|
|17–28 受控读/原件|真实C01/tenant/jti/allowlist、旧404语义、整响应校验、未知L阻断、准确原键/原链/页水位与未知源门|
|29–40 封闭wire|额外字段拒绝、H04七字段、tax/gross=null、opaque/Int64、旧新topic/key隔离、原体roundtrip、金额不改精度、Created不Confirm|
|41–54 幂等/事务|同O原header/body恢复、异义/别名/缺前驱/错摘要、H/V双worker、同Opp双Group完整、故障回滚、当前门竞争、历史RQ和跨T幂等|
|55–72 恢复|丢ACK、WAITING重启、缺密钥、原体重排、旧lease晚回、拒绝不自动重执、映射AHEAD/乱序、准确纠错、原键UNKNOWN、重建历史/CAS/重启|
|73–84 配置/启用|生产null关闭；Storage/Identity映射；两token不混用；真实SQL/MARS=false；schemahash/单writer/真实源保护；积压/保留/Secret/SC24/再开启|
|85–95 R1交互和保护|空页到真实详情；字段裁剪不能裁决；旧Detail失效；新grant阻断；业务先根可commit、Abort先根0效果；超deadline不释；崩溃原账；敏感访问立即撤销；历史Main投影不要求原销售新写权|
|96–104 R1全集/身份|第二H漏全通知仍发现；转入来源保原；集合/目录增长重准备；客户端不声明全集；同T跨L全根冲突；同H跨T独立；时间因果摘要传播|
|105–106 R2联合传播|独立B/H2全Envelope唯一，原键不覆盖；Sales8→9→10、Commercial归属6→7及160→180恢复切点正确|

未来实际运行须隔离真实SQL Server、正式Host注册、Tenant A/B、真实当前/撤销身份，保API/页面三轴/完整SQL写集/InboxOutbox/源码配置schemaSHA和RunId。模拟Owner fixture必须明确，与真实Owner能力分开记录；本轮未构建、未执行这些AC。[S715][S801]

## 10.1 连续原体与恢复样例的实施含义

下表来自159个完整JSON和8个解码原体；准确重复对象沿原指针复用，未执行原稿业务流程。

| 样例族 | 开发规则及预期变化 | 原文 |
|---|---|---|
| S01–S42：提交、补件、确认 | 初次同事务登记唯一Envelope、目标基线100与Sales6；Broker ACK只改运输。原H/v1查询NOT_OBSERVED仍不能证无效；Main NEEDS_INFO后销售补需求使Qualification陈旧，重资格后用准确七字段及slot1提交同H/v2，旧v1 SUPERSEDED、slot2、Sales9。迟到v1重复回原Commercial1历史不回滚当前。Main ACCEPTED、Quote ISSUED及Order CREATED的confirmed0均非Won；真实确认100的完整组后才Won。 | [主稿 L836](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/52/5282d5545f8c9b1d__INT-COM-01_CRM与Main商业交接合同_前后端开发Spec_v1.0.2_REVIEW_R2_CANDIDATE.md:836>) |
| 双订单、取消与Loss | A确认40+B确认60形成100；旧Created别名不重算。A取消10→90，A全取消→60，B纠正55→55，B全取消→0 NO_ACTIVE_WIN。明确销售Loss后虽pursuit STOPPED，后续准确Owner纠正B仍有效10则恢复Won、清activeLoss而pursuit不重开，firstWin历史保留。单头CANCELLED但实际仍履约40的组保持PARTIAL/40，不能按订单标题强清零。 | [主稿 L2839](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/52/5282d5545f8c9b1d__INT-COM-01_CRM与Main商业交接合同_前后端开发Spec_v1.0.2_REVIEW_R2_CANDIDATE.md:2839>) |
| BP准确映射与报价原链 | 候选/ScopeRevision与当前BP/mapping双修订绑定，替换需准确DecisionBinding并保历史订单；运输ACKED_PROCESSING和CRM MATCHES_CURRENT是不同事实。重放BP_CREATE回原BP1，不冒当前BP2。CurrentSnapshot含真实原body；projection UNPROVEN须清当前知识。报价DRAFT→ISSUED→ACCEPTED按原链/Trust完整复原，超JS安全数的requestVersion保持字符串；接受报价仍不贡献Won。 | [主稿 L3603](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/52/5282d5545f8c9b1d__INT-COM-01_CRM与Main商业交接合同_前后端开发Spec_v1.0.2_REVIEW_R2_CANDIDATE.md:3603>) |
| F01：真实需求与裁决权限 | 受权空页只表当时尚未看到；必须从候选真实locator取原需求/forecast/stakeholders/evidence完整块。requestedBy可单独遮罩；需求块REDACTED则decisionContentComplete=false、仅历史读，不能借缺读权做NEEDS_INFO。完整DetailRef与当前scope/slot才可准备；同原键改outcome返回冲突但commitKnowledge可仍COMMITTED，须查原结果。 | [主稿 L4737](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/52/5282d5545f8c9b1d__INT-COM-01_CRM与Main商业交接合同_前后端开发Spec_v1.0.2_REVIEW_R2_CANDIDATE.md:4737>) |
| F02：保护、撤权与终态 | Execution PREPARING记录完整参与集合，再逐Owner取准确subject/purpose/body保护；持久完整grant后READY。撤权立即挡新grant，旧grant等待互斥COMMITTED/ABORTED：业务先取Execution根可在同业务事务提交结果/终态，Abort先根则晚grant不得提交。deadline过去仍UNKNOWN/GRANTED，不自动释放。历史Main结果投影用CRM_RESULT_PROJECTION独立用途，不要求原销售者仍有新请求写权；敏感当前读撤权仍立即生效。 | [主稿 L5298](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/52/5282d5545f8c9b1d__INT-COM-01_CRM与Main商业交接合同_前后端开发Spec_v1.0.2_REVIEW_R2_CANDIDATE.md:5298>) |
| F03：四来源而非当前本地组 | 独立B以自己的商机/意图/H形成60，再经双Owner授权归属P，保原来源且Main业务写0；P另一次INITIAL形成20且所有通知丢失。重建服务端枚举H1v1/H1v2/H2v1/转入B四来源，七个原结果各带原体、Trust及cut。四源完整清单和原链全部核完，当前确认贡献为100+20+60=180；本地只看H1和已转入B会漏20。 | [主稿 L5922](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/52/5282d5545f8c9b1d__INT-COM-01_CRM与Main商业交接合同_前后端开发Spec_v1.0.2_REVIEW_R2_CANDIDATE.md:5922>) |
| F03/F04负例与安全恢复 | READY_FOR_CAS仍须重核本地来源集合/各目录cut/完整多轴；新增H3使旧plan WAITING，activeGeneration保持1。任一目录ADVANCED说明旧cut不足，不能沿旧页继续宣布完整。同租户同H/v却改法人到CN02，即使换Event也HANDOFF_IDENTITY_BINDING_CONFLICT，回包不泄露原法人，新法人slot/decision/outbox全部0。所有这些只是文书预期。 | [主稿 L7357](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/52/5282d5545f8c9b1d__INT-COM-01_CRM与Main商业交接合同_前后端开发Spec_v1.0.2_REVIEW_R2_CANDIDATE.md:7357>) |

## 11. 实际待核事项与关闭条件

|待核项|影响及关闭条件|
|---|---|
|12个 O-IC 门|CRM-TX/MAIN/BP/RESULT/TRANSPORT/READ/GUARD/QUERY/SCHEMA/RETENTION/REBUILD/RUNTIME均UNPROVEN；需真实双边Host/DB/配置/原体/恢复证据，不能由mock填绿|
|有限用途共同采用|逐源全部控制写者、宽政策/流反向索引、实际Execution终态、原件解密/备份单代次与安全政策一致；不符合即该用途Disabled|
|运输实际注册|topic/subscription/appId/ACL/TLS/原体roundtrip/Trust/封闭validator/单writer全部确证，保旧流分类与回退，不改原wire迁就通用publisher|
|跨Owner目录完整性|每个业务结果同txn登记目录、准确cut/manifest/全部已知结果、CRM入站和本域历史共同fence；无证不切重建|
|旧C03与新Sales/当前EST后继|固定旧工厂/版本/validator不适配新语义；按现ORD和ESTv2合同复核，本册不能直接替换旧实现|
|准确夹具采用与实际运行|159 JSON、8解码原体及20准确窗口已完成有效语义阅读；运行仍NOT_RUN，原独审的计算不冒称本代理业务执行|

以上是实际接线/证据边界，不把已关闭F01–F05、R2身份与修订问题重新登记为缺陷。源已接受设计若实际Owner无法采用，应记录准确缺成员/保证/受阻动作，最小补充决定另行办理，不能扩编Platform或擅改原协议。[S551][S553][S647]

## 12. 来源与实际阅读覆盖

状态：**required_materials_consolidated**。6当前必要输入和CURRENT引用的2189行窗口包全部有效语义闭合。主稿10758行中的核心、106 AC及非JSON说明已实读；附录六段2126行按同源逐行内容相等复用已读OPP/HO/BP。159 JSON完整字段、参与者、请求、预期、事务与写集已读，184重复原JSON子树按准确前序复用，8 Base64先还原真实JSON再读。摘要字面属于身份元数据；不把紧凑对象阅读称全部原字面逐行重读。

2189行窗口包20段逐行等于当前主稿，所有新增边界说明已读；它仅是窗口，不新增规范或接受。CURRENT/INDEX/独审全文及SR/接受正文、内嵌CURRENT准确差异证据保留。read_ranges、structured_ranges、reused_ranges和literal_not_reread_ranges分别记录，当前有效语义无待读。12实际Owner门仍 **UNPROVEN**，106业务AC仍 **NOT_RUN**。

证据：[159对象语义视图](<D:/CP6-archives/consolidation-20261010/commercial-cache/int-com/fixture-reading-view.json>)、[原对象重复及Base64身份](<D:/CP6-archives/consolidation-20261010/commercial-cache/int-com/fixture-exact-reuse.json>)、[20窗口准确比对](<D:/CP6-archives/consolidation-20261010/commercial-cache/int-com/source-window-exact-reuse.json>)、[商业阅读台账](<D:/CP6/docs/CP6_开发设计文档_20261010/evidence/commercial-reading.json>)。没有执行源命令、数据库、构建或远端写入。

关联：[CRM-HO-01](<D:/CP6/docs/CP6_开发设计文档_20261010/modules/CRM-HO-01.md>)、[CRM-OPP-01](<D:/CP6/docs/CP6_开发设计文档_20261010/modules/CRM-OPP-01.md>)、[ERP-BP-01](<D:/CP6/docs/CP6_开发设计文档_20261010/modules/ERP-BP-01.md>)、[ERP-QTN-01](<D:/CP6/docs/CP6_开发设计文档_20261010/modules/ERP-QTN-01.md>)。机器合同入口：[commercial-contracts.json](<D:/CP6/docs/CP6_开发设计文档_20261010/contracts/commercial-contracts.json>)。

[A3]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/56/5622d3750c57c1a5__INT-COM-acceptance.md:3>
[C83]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/e9/e93594e77568cb75__CURRENT.json:83>
[C239]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/e9/e93594e77568cb75__CURRENT.json:239>
[C248]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/e9/e93594e77568cb75__CURRENT.json:248>
[R5]: <D:/CP6/docs/CP6_完整成果归档_20261009/CP6_遗漏原包补充_20261009/优先找回评审原件/INT-COM-01_v1.0.2_R2_BOUNDED_REVIEW.md:5>
[R7]: <D:/CP6/docs/CP6_完整成果归档_20261009/CP6_遗漏原包补充_20261009/优先找回评审原件/INT-COM-01_v1.0.2_R2_BOUNDED_REVIEW.md:7>
[R17]: <D:/CP6/docs/CP6_完整成果归档_20261009/CP6_遗漏原包补充_20261009/优先找回评审原件/INT-COM-01_v1.0.2_R2_BOUNDED_REVIEW.md:17>
[R23]: <D:/CP6/docs/CP6_完整成果归档_20261009/CP6_遗漏原包补充_20261009/优先找回评审原件/INT-COM-01_v1.0.2_R2_BOUNDED_REVIEW.md:23>
[R36]: <D:/CP6/docs/CP6_完整成果归档_20261009/CP6_遗漏原包补充_20261009/优先找回评审原件/INT-COM-01_v1.0.2_R2_BOUNDED_REVIEW.md:36>
[S42]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/52/5282d5545f8c9b1d__INT-COM-01_CRM与Main商业交接合同_前后端开发Spec_v1.0.2_REVIEW_R2_CANDIDATE.md:42>
[S43]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/52/5282d5545f8c9b1d__INT-COM-01_CRM与Main商业交接合同_前后端开发Spec_v1.0.2_REVIEW_R2_CANDIDATE.md:43>
[S44]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/52/5282d5545f8c9b1d__INT-COM-01_CRM与Main商业交接合同_前后端开发Spec_v1.0.2_REVIEW_R2_CANDIDATE.md:44>
[S45]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/52/5282d5545f8c9b1d__INT-COM-01_CRM与Main商业交接合同_前后端开发Spec_v1.0.2_REVIEW_R2_CANDIDATE.md:45>
[S46]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/52/5282d5545f8c9b1d__INT-COM-01_CRM与Main商业交接合同_前后端开发Spec_v1.0.2_REVIEW_R2_CANDIDATE.md:46>
[S54]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/52/5282d5545f8c9b1d__INT-COM-01_CRM与Main商业交接合同_前后端开发Spec_v1.0.2_REVIEW_R2_CANDIDATE.md:54>
[S56]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/52/5282d5545f8c9b1d__INT-COM-01_CRM与Main商业交接合同_前后端开发Spec_v1.0.2_REVIEW_R2_CANDIDATE.md:56>
[S66]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/52/5282d5545f8c9b1d__INT-COM-01_CRM与Main商业交接合同_前后端开发Spec_v1.0.2_REVIEW_R2_CANDIDATE.md:66>
[S74]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/52/5282d5545f8c9b1d__INT-COM-01_CRM与Main商业交接合同_前后端开发Spec_v1.0.2_REVIEW_R2_CANDIDATE.md:74>
[S85]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/52/5282d5545f8c9b1d__INT-COM-01_CRM与Main商业交接合同_前后端开发Spec_v1.0.2_REVIEW_R2_CANDIDATE.md:85>
[S112]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/52/5282d5545f8c9b1d__INT-COM-01_CRM与Main商业交接合同_前后端开发Spec_v1.0.2_REVIEW_R2_CANDIDATE.md:112>
[S129]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/52/5282d5545f8c9b1d__INT-COM-01_CRM与Main商业交接合同_前后端开发Spec_v1.0.2_REVIEW_R2_CANDIDATE.md:129>
[S158]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/52/5282d5545f8c9b1d__INT-COM-01_CRM与Main商业交接合同_前后端开发Spec_v1.0.2_REVIEW_R2_CANDIDATE.md:158>
[S160]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/52/5282d5545f8c9b1d__INT-COM-01_CRM与Main商业交接合同_前后端开发Spec_v1.0.2_REVIEW_R2_CANDIDATE.md:160>
[S162]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/52/5282d5545f8c9b1d__INT-COM-01_CRM与Main商业交接合同_前后端开发Spec_v1.0.2_REVIEW_R2_CANDIDATE.md:162>
[S164]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/52/5282d5545f8c9b1d__INT-COM-01_CRM与Main商业交接合同_前后端开发Spec_v1.0.2_REVIEW_R2_CANDIDATE.md:164>
[S170]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/52/5282d5545f8c9b1d__INT-COM-01_CRM与Main商业交接合同_前后端开发Spec_v1.0.2_REVIEW_R2_CANDIDATE.md:170>
[S172]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/52/5282d5545f8c9b1d__INT-COM-01_CRM与Main商业交接合同_前后端开发Spec_v1.0.2_REVIEW_R2_CANDIDATE.md:172>
[S173]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/52/5282d5545f8c9b1d__INT-COM-01_CRM与Main商业交接合同_前后端开发Spec_v1.0.2_REVIEW_R2_CANDIDATE.md:173>
[S176]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/52/5282d5545f8c9b1d__INT-COM-01_CRM与Main商业交接合同_前后端开发Spec_v1.0.2_REVIEW_R2_CANDIDATE.md:176>
[S180]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/52/5282d5545f8c9b1d__INT-COM-01_CRM与Main商业交接合同_前后端开发Spec_v1.0.2_REVIEW_R2_CANDIDATE.md:180>
[S182]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/52/5282d5545f8c9b1d__INT-COM-01_CRM与Main商业交接合同_前后端开发Spec_v1.0.2_REVIEW_R2_CANDIDATE.md:182>
[S184]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/52/5282d5545f8c9b1d__INT-COM-01_CRM与Main商业交接合同_前后端开发Spec_v1.0.2_REVIEW_R2_CANDIDATE.md:184>
[S188]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/52/5282d5545f8c9b1d__INT-COM-01_CRM与Main商业交接合同_前后端开发Spec_v1.0.2_REVIEW_R2_CANDIDATE.md:188>
[S190]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/52/5282d5545f8c9b1d__INT-COM-01_CRM与Main商业交接合同_前后端开发Spec_v1.0.2_REVIEW_R2_CANDIDATE.md:190>
[S192]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/52/5282d5545f8c9b1d__INT-COM-01_CRM与Main商业交接合同_前后端开发Spec_v1.0.2_REVIEW_R2_CANDIDATE.md:192>
[S196]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/52/5282d5545f8c9b1d__INT-COM-01_CRM与Main商业交接合同_前后端开发Spec_v1.0.2_REVIEW_R2_CANDIDATE.md:196>
[S198]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/52/5282d5545f8c9b1d__INT-COM-01_CRM与Main商业交接合同_前后端开发Spec_v1.0.2_REVIEW_R2_CANDIDATE.md:198>
[S200]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/52/5282d5545f8c9b1d__INT-COM-01_CRM与Main商业交接合同_前后端开发Spec_v1.0.2_REVIEW_R2_CANDIDATE.md:200>
[S206]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/52/5282d5545f8c9b1d__INT-COM-01_CRM与Main商业交接合同_前后端开发Spec_v1.0.2_REVIEW_R2_CANDIDATE.md:206>
[S208]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/52/5282d5545f8c9b1d__INT-COM-01_CRM与Main商业交接合同_前后端开发Spec_v1.0.2_REVIEW_R2_CANDIDATE.md:208>
[S210]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/52/5282d5545f8c9b1d__INT-COM-01_CRM与Main商业交接合同_前后端开发Spec_v1.0.2_REVIEW_R2_CANDIDATE.md:210>
[S214]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/52/5282d5545f8c9b1d__INT-COM-01_CRM与Main商业交接合同_前后端开发Spec_v1.0.2_REVIEW_R2_CANDIDATE.md:214>
[S215]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/52/5282d5545f8c9b1d__INT-COM-01_CRM与Main商业交接合同_前后端开发Spec_v1.0.2_REVIEW_R2_CANDIDATE.md:215>
[S216]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/52/5282d5545f8c9b1d__INT-COM-01_CRM与Main商业交接合同_前后端开发Spec_v1.0.2_REVIEW_R2_CANDIDATE.md:216>
[S218]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/52/5282d5545f8c9b1d__INT-COM-01_CRM与Main商业交接合同_前后端开发Spec_v1.0.2_REVIEW_R2_CANDIDATE.md:218>
[S219]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/52/5282d5545f8c9b1d__INT-COM-01_CRM与Main商业交接合同_前后端开发Spec_v1.0.2_REVIEW_R2_CANDIDATE.md:219>
[S221]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/52/5282d5545f8c9b1d__INT-COM-01_CRM与Main商业交接合同_前后端开发Spec_v1.0.2_REVIEW_R2_CANDIDATE.md:221>
[S235]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/52/5282d5545f8c9b1d__INT-COM-01_CRM与Main商业交接合同_前后端开发Spec_v1.0.2_REVIEW_R2_CANDIDATE.md:235>
[S237]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/52/5282d5545f8c9b1d__INT-COM-01_CRM与Main商业交接合同_前后端开发Spec_v1.0.2_REVIEW_R2_CANDIDATE.md:237>
[S239]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/52/5282d5545f8c9b1d__INT-COM-01_CRM与Main商业交接合同_前后端开发Spec_v1.0.2_REVIEW_R2_CANDIDATE.md:239>
[S243]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/52/5282d5545f8c9b1d__INT-COM-01_CRM与Main商业交接合同_前后端开发Spec_v1.0.2_REVIEW_R2_CANDIDATE.md:243>
[S252]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/52/5282d5545f8c9b1d__INT-COM-01_CRM与Main商业交接合同_前后端开发Spec_v1.0.2_REVIEW_R2_CANDIDATE.md:252>
[S256]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/52/5282d5545f8c9b1d__INT-COM-01_CRM与Main商业交接合同_前后端开发Spec_v1.0.2_REVIEW_R2_CANDIDATE.md:256>
[S273]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/52/5282d5545f8c9b1d__INT-COM-01_CRM与Main商业交接合同_前后端开发Spec_v1.0.2_REVIEW_R2_CANDIDATE.md:273>
[S275]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/52/5282d5545f8c9b1d__INT-COM-01_CRM与Main商业交接合同_前后端开发Spec_v1.0.2_REVIEW_R2_CANDIDATE.md:275>
[S296]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/52/5282d5545f8c9b1d__INT-COM-01_CRM与Main商业交接合同_前后端开发Spec_v1.0.2_REVIEW_R2_CANDIDATE.md:296>
[S298]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/52/5282d5545f8c9b1d__INT-COM-01_CRM与Main商业交接合同_前后端开发Spec_v1.0.2_REVIEW_R2_CANDIDATE.md:298>
[S300]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/52/5282d5545f8c9b1d__INT-COM-01_CRM与Main商业交接合同_前后端开发Spec_v1.0.2_REVIEW_R2_CANDIDATE.md:300>
[S308]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/52/5282d5545f8c9b1d__INT-COM-01_CRM与Main商业交接合同_前后端开发Spec_v1.0.2_REVIEW_R2_CANDIDATE.md:308>
[S309]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/52/5282d5545f8c9b1d__INT-COM-01_CRM与Main商业交接合同_前后端开发Spec_v1.0.2_REVIEW_R2_CANDIDATE.md:309>
[S319]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/52/5282d5545f8c9b1d__INT-COM-01_CRM与Main商业交接合同_前后端开发Spec_v1.0.2_REVIEW_R2_CANDIDATE.md:319>
[S332]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/52/5282d5545f8c9b1d__INT-COM-01_CRM与Main商业交接合同_前后端开发Spec_v1.0.2_REVIEW_R2_CANDIDATE.md:332>
[S336]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/52/5282d5545f8c9b1d__INT-COM-01_CRM与Main商业交接合同_前后端开发Spec_v1.0.2_REVIEW_R2_CANDIDATE.md:336>
[S338]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/52/5282d5545f8c9b1d__INT-COM-01_CRM与Main商业交接合同_前后端开发Spec_v1.0.2_REVIEW_R2_CANDIDATE.md:338>
[S340]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/52/5282d5545f8c9b1d__INT-COM-01_CRM与Main商业交接合同_前后端开发Spec_v1.0.2_REVIEW_R2_CANDIDATE.md:340>
[S355]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/52/5282d5545f8c9b1d__INT-COM-01_CRM与Main商业交接合同_前后端开发Spec_v1.0.2_REVIEW_R2_CANDIDATE.md:355>
[S359]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/52/5282d5545f8c9b1d__INT-COM-01_CRM与Main商业交接合同_前后端开发Spec_v1.0.2_REVIEW_R2_CANDIDATE.md:359>
[S361]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/52/5282d5545f8c9b1d__INT-COM-01_CRM与Main商业交接合同_前后端开发Spec_v1.0.2_REVIEW_R2_CANDIDATE.md:361>
[S363]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/52/5282d5545f8c9b1d__INT-COM-01_CRM与Main商业交接合同_前后端开发Spec_v1.0.2_REVIEW_R2_CANDIDATE.md:363>
[S365]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/52/5282d5545f8c9b1d__INT-COM-01_CRM与Main商业交接合同_前后端开发Spec_v1.0.2_REVIEW_R2_CANDIDATE.md:365>
[S369]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/52/5282d5545f8c9b1d__INT-COM-01_CRM与Main商业交接合同_前后端开发Spec_v1.0.2_REVIEW_R2_CANDIDATE.md:369>
[S371]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/52/5282d5545f8c9b1d__INT-COM-01_CRM与Main商业交接合同_前后端开发Spec_v1.0.2_REVIEW_R2_CANDIDATE.md:371>
[S375]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/52/5282d5545f8c9b1d__INT-COM-01_CRM与Main商业交接合同_前后端开发Spec_v1.0.2_REVIEW_R2_CANDIDATE.md:375>
[S382]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/52/5282d5545f8c9b1d__INT-COM-01_CRM与Main商业交接合同_前后端开发Spec_v1.0.2_REVIEW_R2_CANDIDATE.md:382>
[S395]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/52/5282d5545f8c9b1d__INT-COM-01_CRM与Main商业交接合同_前后端开发Spec_v1.0.2_REVIEW_R2_CANDIDATE.md:395>
[S399]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/52/5282d5545f8c9b1d__INT-COM-01_CRM与Main商业交接合同_前后端开发Spec_v1.0.2_REVIEW_R2_CANDIDATE.md:399>
[S401]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/52/5282d5545f8c9b1d__INT-COM-01_CRM与Main商业交接合同_前后端开发Spec_v1.0.2_REVIEW_R2_CANDIDATE.md:401>
[S405]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/52/5282d5545f8c9b1d__INT-COM-01_CRM与Main商业交接合同_前后端开发Spec_v1.0.2_REVIEW_R2_CANDIDATE.md:405>
[S439]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/52/5282d5545f8c9b1d__INT-COM-01_CRM与Main商业交接合同_前后端开发Spec_v1.0.2_REVIEW_R2_CANDIDATE.md:439>
[S458]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/52/5282d5545f8c9b1d__INT-COM-01_CRM与Main商业交接合同_前后端开发Spec_v1.0.2_REVIEW_R2_CANDIDATE.md:458>
[S460]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/52/5282d5545f8c9b1d__INT-COM-01_CRM与Main商业交接合同_前后端开发Spec_v1.0.2_REVIEW_R2_CANDIDATE.md:460>
[S462]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/52/5282d5545f8c9b1d__INT-COM-01_CRM与Main商业交接合同_前后端开发Spec_v1.0.2_REVIEW_R2_CANDIDATE.md:462>
[S464]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/52/5282d5545f8c9b1d__INT-COM-01_CRM与Main商业交接合同_前后端开发Spec_v1.0.2_REVIEW_R2_CANDIDATE.md:464>
[S466]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/52/5282d5545f8c9b1d__INT-COM-01_CRM与Main商业交接合同_前后端开发Spec_v1.0.2_REVIEW_R2_CANDIDATE.md:466>
[S468]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/52/5282d5545f8c9b1d__INT-COM-01_CRM与Main商业交接合同_前后端开发Spec_v1.0.2_REVIEW_R2_CANDIDATE.md:468>
[S472]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/52/5282d5545f8c9b1d__INT-COM-01_CRM与Main商业交接合同_前后端开发Spec_v1.0.2_REVIEW_R2_CANDIDATE.md:472>
[S475]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/52/5282d5545f8c9b1d__INT-COM-01_CRM与Main商业交接合同_前后端开发Spec_v1.0.2_REVIEW_R2_CANDIDATE.md:475>
[S478]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/52/5282d5545f8c9b1d__INT-COM-01_CRM与Main商业交接合同_前后端开发Spec_v1.0.2_REVIEW_R2_CANDIDATE.md:478>
[S484]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/52/5282d5545f8c9b1d__INT-COM-01_CRM与Main商业交接合同_前后端开发Spec_v1.0.2_REVIEW_R2_CANDIDATE.md:484>
[S486]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/52/5282d5545f8c9b1d__INT-COM-01_CRM与Main商业交接合同_前后端开发Spec_v1.0.2_REVIEW_R2_CANDIDATE.md:486>
[S488]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/52/5282d5545f8c9b1d__INT-COM-01_CRM与Main商业交接合同_前后端开发Spec_v1.0.2_REVIEW_R2_CANDIDATE.md:488>
[S541]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/52/5282d5545f8c9b1d__INT-COM-01_CRM与Main商业交接合同_前后端开发Spec_v1.0.2_REVIEW_R2_CANDIDATE.md:541>
[S542]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/52/5282d5545f8c9b1d__INT-COM-01_CRM与Main商业交接合同_前后端开发Spec_v1.0.2_REVIEW_R2_CANDIDATE.md:542>
[S543]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/52/5282d5545f8c9b1d__INT-COM-01_CRM与Main商业交接合同_前后端开发Spec_v1.0.2_REVIEW_R2_CANDIDATE.md:543>
[S544]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/52/5282d5545f8c9b1d__INT-COM-01_CRM与Main商业交接合同_前后端开发Spec_v1.0.2_REVIEW_R2_CANDIDATE.md:544>
[S545]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/52/5282d5545f8c9b1d__INT-COM-01_CRM与Main商业交接合同_前后端开发Spec_v1.0.2_REVIEW_R2_CANDIDATE.md:545>
[S547]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/52/5282d5545f8c9b1d__INT-COM-01_CRM与Main商业交接合同_前后端开发Spec_v1.0.2_REVIEW_R2_CANDIDATE.md:547>
[S551]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/52/5282d5545f8c9b1d__INT-COM-01_CRM与Main商业交接合同_前后端开发Spec_v1.0.2_REVIEW_R2_CANDIDATE.md:551>
[S553]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/52/5282d5545f8c9b1d__INT-COM-01_CRM与Main商业交接合同_前后端开发Spec_v1.0.2_REVIEW_R2_CANDIDATE.md:553>
[S558]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/52/5282d5545f8c9b1d__INT-COM-01_CRM与Main商业交接合同_前后端开发Spec_v1.0.2_REVIEW_R2_CANDIDATE.md:558>
[S561]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/52/5282d5545f8c9b1d__INT-COM-01_CRM与Main商业交接合同_前后端开发Spec_v1.0.2_REVIEW_R2_CANDIDATE.md:561>
[S570]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/52/5282d5545f8c9b1d__INT-COM-01_CRM与Main商业交接合同_前后端开发Spec_v1.0.2_REVIEW_R2_CANDIDATE.md:570>
[S572]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/52/5282d5545f8c9b1d__INT-COM-01_CRM与Main商业交接合同_前后端开发Spec_v1.0.2_REVIEW_R2_CANDIDATE.md:572>
[S574]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/52/5282d5545f8c9b1d__INT-COM-01_CRM与Main商业交接合同_前后端开发Spec_v1.0.2_REVIEW_R2_CANDIDATE.md:574>
[S576]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/52/5282d5545f8c9b1d__INT-COM-01_CRM与Main商业交接合同_前后端开发Spec_v1.0.2_REVIEW_R2_CANDIDATE.md:576>
[S578]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/52/5282d5545f8c9b1d__INT-COM-01_CRM与Main商业交接合同_前后端开发Spec_v1.0.2_REVIEW_R2_CANDIDATE.md:578>
[S580]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/52/5282d5545f8c9b1d__INT-COM-01_CRM与Main商业交接合同_前后端开发Spec_v1.0.2_REVIEW_R2_CANDIDATE.md:580>
[S586]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/52/5282d5545f8c9b1d__INT-COM-01_CRM与Main商业交接合同_前后端开发Spec_v1.0.2_REVIEW_R2_CANDIDATE.md:586>
[S588]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/52/5282d5545f8c9b1d__INT-COM-01_CRM与Main商业交接合同_前后端开发Spec_v1.0.2_REVIEW_R2_CANDIDATE.md:588>
[S590]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/52/5282d5545f8c9b1d__INT-COM-01_CRM与Main商业交接合同_前后端开发Spec_v1.0.2_REVIEW_R2_CANDIDATE.md:590>
[S592]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/52/5282d5545f8c9b1d__INT-COM-01_CRM与Main商业交接合同_前后端开发Spec_v1.0.2_REVIEW_R2_CANDIDATE.md:592>
[S596]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/52/5282d5545f8c9b1d__INT-COM-01_CRM与Main商业交接合同_前后端开发Spec_v1.0.2_REVIEW_R2_CANDIDATE.md:596>
[S613]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/52/5282d5545f8c9b1d__INT-COM-01_CRM与Main商业交接合同_前后端开发Spec_v1.0.2_REVIEW_R2_CANDIDATE.md:613>
[S615]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/52/5282d5545f8c9b1d__INT-COM-01_CRM与Main商业交接合同_前后端开发Spec_v1.0.2_REVIEW_R2_CANDIDATE.md:615>
[S617]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/52/5282d5545f8c9b1d__INT-COM-01_CRM与Main商业交接合同_前后端开发Spec_v1.0.2_REVIEW_R2_CANDIDATE.md:617>
[S621]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/52/5282d5545f8c9b1d__INT-COM-01_CRM与Main商业交接合同_前后端开发Spec_v1.0.2_REVIEW_R2_CANDIDATE.md:621>
[S623]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/52/5282d5545f8c9b1d__INT-COM-01_CRM与Main商业交接合同_前后端开发Spec_v1.0.2_REVIEW_R2_CANDIDATE.md:623>
[S625]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/52/5282d5545f8c9b1d__INT-COM-01_CRM与Main商业交接合同_前后端开发Spec_v1.0.2_REVIEW_R2_CANDIDATE.md:625>
[S629]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/52/5282d5545f8c9b1d__INT-COM-01_CRM与Main商业交接合同_前后端开发Spec_v1.0.2_REVIEW_R2_CANDIDATE.md:629>
[S630]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/52/5282d5545f8c9b1d__INT-COM-01_CRM与Main商业交接合同_前后端开发Spec_v1.0.2_REVIEW_R2_CANDIDATE.md:630>
[S631]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/52/5282d5545f8c9b1d__INT-COM-01_CRM与Main商业交接合同_前后端开发Spec_v1.0.2_REVIEW_R2_CANDIDATE.md:631>
[S632]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/52/5282d5545f8c9b1d__INT-COM-01_CRM与Main商业交接合同_前后端开发Spec_v1.0.2_REVIEW_R2_CANDIDATE.md:632>
[S633]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/52/5282d5545f8c9b1d__INT-COM-01_CRM与Main商业交接合同_前后端开发Spec_v1.0.2_REVIEW_R2_CANDIDATE.md:633>
[S634]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/52/5282d5545f8c9b1d__INT-COM-01_CRM与Main商业交接合同_前后端开发Spec_v1.0.2_REVIEW_R2_CANDIDATE.md:634>
[S635]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/52/5282d5545f8c9b1d__INT-COM-01_CRM与Main商业交接合同_前后端开发Spec_v1.0.2_REVIEW_R2_CANDIDATE.md:635>
[S636]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/52/5282d5545f8c9b1d__INT-COM-01_CRM与Main商业交接合同_前后端开发Spec_v1.0.2_REVIEW_R2_CANDIDATE.md:636>
[S637]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/52/5282d5545f8c9b1d__INT-COM-01_CRM与Main商业交接合同_前后端开发Spec_v1.0.2_REVIEW_R2_CANDIDATE.md:637>
[S639]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/52/5282d5545f8c9b1d__INT-COM-01_CRM与Main商业交接合同_前后端开发Spec_v1.0.2_REVIEW_R2_CANDIDATE.md:639>
[S643]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/52/5282d5545f8c9b1d__INT-COM-01_CRM与Main商业交接合同_前后端开发Spec_v1.0.2_REVIEW_R2_CANDIDATE.md:643>
[S645]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/52/5282d5545f8c9b1d__INT-COM-01_CRM与Main商业交接合同_前后端开发Spec_v1.0.2_REVIEW_R2_CANDIDATE.md:645>
[S647]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/52/5282d5545f8c9b1d__INT-COM-01_CRM与Main商业交接合同_前后端开发Spec_v1.0.2_REVIEW_R2_CANDIDATE.md:647>
[S651]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/52/5282d5545f8c9b1d__INT-COM-01_CRM与Main商业交接合同_前后端开发Spec_v1.0.2_REVIEW_R2_CANDIDATE.md:651>
[S671]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/52/5282d5545f8c9b1d__INT-COM-01_CRM与Main商业交接合同_前后端开发Spec_v1.0.2_REVIEW_R2_CANDIDATE.md:671>
[S673]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/52/5282d5545f8c9b1d__INT-COM-01_CRM与Main商业交接合同_前后端开发Spec_v1.0.2_REVIEW_R2_CANDIDATE.md:673>
[S675]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/52/5282d5545f8c9b1d__INT-COM-01_CRM与Main商业交接合同_前后端开发Spec_v1.0.2_REVIEW_R2_CANDIDATE.md:675>
[S679]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/52/5282d5545f8c9b1d__INT-COM-01_CRM与Main商业交接合同_前后端开发Spec_v1.0.2_REVIEW_R2_CANDIDATE.md:679>
[S680]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/52/5282d5545f8c9b1d__INT-COM-01_CRM与Main商业交接合同_前后端开发Spec_v1.0.2_REVIEW_R2_CANDIDATE.md:680>
[S681]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/52/5282d5545f8c9b1d__INT-COM-01_CRM与Main商业交接合同_前后端开发Spec_v1.0.2_REVIEW_R2_CANDIDATE.md:681>
[S682]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/52/5282d5545f8c9b1d__INT-COM-01_CRM与Main商业交接合同_前后端开发Spec_v1.0.2_REVIEW_R2_CANDIDATE.md:682>
[S683]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/52/5282d5545f8c9b1d__INT-COM-01_CRM与Main商业交接合同_前后端开发Spec_v1.0.2_REVIEW_R2_CANDIDATE.md:683>
[S684]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/52/5282d5545f8c9b1d__INT-COM-01_CRM与Main商业交接合同_前后端开发Spec_v1.0.2_REVIEW_R2_CANDIDATE.md:684>
[S685]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/52/5282d5545f8c9b1d__INT-COM-01_CRM与Main商业交接合同_前后端开发Spec_v1.0.2_REVIEW_R2_CANDIDATE.md:685>
[S686]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/52/5282d5545f8c9b1d__INT-COM-01_CRM与Main商业交接合同_前后端开发Spec_v1.0.2_REVIEW_R2_CANDIDATE.md:686>
[S687]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/52/5282d5545f8c9b1d__INT-COM-01_CRM与Main商业交接合同_前后端开发Spec_v1.0.2_REVIEW_R2_CANDIDATE.md:687>
[S689]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/52/5282d5545f8c9b1d__INT-COM-01_CRM与Main商业交接合同_前后端开发Spec_v1.0.2_REVIEW_R2_CANDIDATE.md:689>
[S691]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/52/5282d5545f8c9b1d__INT-COM-01_CRM与Main商业交接合同_前后端开发Spec_v1.0.2_REVIEW_R2_CANDIDATE.md:691>
[S695]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/52/5282d5545f8c9b1d__INT-COM-01_CRM与Main商业交接合同_前后端开发Spec_v1.0.2_REVIEW_R2_CANDIDATE.md:695>
[S697]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/52/5282d5545f8c9b1d__INT-COM-01_CRM与Main商业交接合同_前后端开发Spec_v1.0.2_REVIEW_R2_CANDIDATE.md:697>
[S701]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/52/5282d5545f8c9b1d__INT-COM-01_CRM与Main商业交接合同_前后端开发Spec_v1.0.2_REVIEW_R2_CANDIDATE.md:701>
[S713]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/52/5282d5545f8c9b1d__INT-COM-01_CRM与Main商业交接合同_前后端开发Spec_v1.0.2_REVIEW_R2_CANDIDATE.md:713>
[S715]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/52/5282d5545f8c9b1d__INT-COM-01_CRM与Main商业交接合同_前后端开发Spec_v1.0.2_REVIEW_R2_CANDIDATE.md:715>
[S801]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/52/5282d5545f8c9b1d__INT-COM-01_CRM与Main商业交接合同_前后端开发Spec_v1.0.2_REVIEW_R2_CANDIDATE.md:801>
[S823]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/52/5282d5545f8c9b1d__INT-COM-01_CRM与Main商业交接合同_前后端开发Spec_v1.0.2_REVIEW_R2_CANDIDATE.md:823>
[S824]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/52/5282d5545f8c9b1d__INT-COM-01_CRM与Main商业交接合同_前后端开发Spec_v1.0.2_REVIEW_R2_CANDIDATE.md:824>
