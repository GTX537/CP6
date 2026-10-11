# PLAN-PO-01｜计划订单、三分支转单和恢复开发设计

状态：`required_materials_consolidated`。准确 CP22 原六 SPEC/37 AC 的功能设计及新专业接口已获根接受；17 个上游合同及独立 Core 有本次专业设计采用，实际运行注册/授权仍为0，37业务用例全部 NOT_RUN。[A:6] [A:14] 本册把跨 CP18–CP22 的有效规则合并阅读，保留全部原件与后继限定。

## 1. 目的、操作者与Owner

Main Planning 把正式 MRP 建议变为可追溯的计划订单，允许 Firm、Ignore，按明确份额转成真实 PR、WO 或 WMS 调拨请求，并处理未知、部分成功、源更正及原键恢复。Firm 不创造下游对象；下游对象建立不等于采购批准、生产开工、调拨执行或库存完成。[M:39] [M:55]

Planning 拥有 PlannedOrder、决策、不可变份额分区、转换 Work/Leg、保护、恢复和投影；MRP 拥有 Run/ResultSet/Publication/Suggestion/Pegging；POL/SUP 提供原政策、供给/保护资格；PUR-PR、MES-WO、WMS-TRANSFER 各签自己的真实接收结果。Owner 间为持久协调流程，不宣称分布式原子事务。[M:39] [M:41]

操作分读者、计划决策、预览/转单、恢复、更正、特权 writer 切换/接管、注册源/回执摄取。所有能力受当前 Scope、Site、对象、目的及字段权限约束；机器生产者不能变成人工通用授权入口。[M:301] [C:34]

## 2. 当前版本与接受组合

| 原件或层次 | 有效解释 |
|---|---|
| CP22整册主文 | SHA `2fc131613a8433d347ca869999a5f8550daaefa0768b487f4610c67f75b4526d`，87,627B/596行。§1–20完整继承，§21为CP22当前修订；开头仍有历史CP21标题，不能据此选错版本。[M:570] |
| 冻结ZIP | SHA `936aae4d27712a077074c20fe15f303d9d445c2eefd7437362630c47750aa865`，26,952,717B/573成员；本地由准确分片恢复，描述文件与ZIP本体各有独立SHA。[A:8] |
| 独立整册静态结论 | 当前PASS，无剩余RETURN；不是执行或运行通过。[R:3] [R:50] |
| 专业采用 | Planning11份、OwnerTech6份及role15内部五技术责任、Core独立本轮采用；不能把五nested另算五个顶层合同。[S:52] [T:3] [C:3] |
| 根最终接受 | 2026-10-04T15:37:56Z，准确六SPEC文本和明确新专业接口/业务方案，保留Q1–Q5；ImplementationAuthorized与ProductionDeploymentAuthorized均false。[A:2] [A:4] [A:15] |
| 历史更正 | CP18-R04关于PUR原convertRequestId的误判正式撤回；CP20原PASS被补充RETURN覆盖；CP21闭合两consumer后仅余publisher参数；CP22纠正后最终接受。[A:17] |

CP22最小差量是首次publisher27的可信调用者 `PLANNING→PLAN-PO`，不是改 SourceDemand.owner。源/委托责任仍为 `PLANNING`；SQL、原pin、SourceDemand和其它normal字节不因这次参数修正改变。[M:576] [M:580]

旧R2 ZIP原字节未恢复是历史资格，不能用现CP22重建包冒充。原scope commit `3195d7d0a810833cf46147865e0fe473f68e3484`、旧Main `157630594e3371fe181955d2f6227ff3b6962c84`仅说明历史评估基线，不能宣称本仓库现有实现仍与它相同。[A:11] [M:25]

## 3. 数据、身份、数量和状态

### 3.1 三个业务轴与两种Leg状态

| 轴 | 值及意义 |
|---|---|
| Suggestion | ACTIVE / SUPERSEDED / WITHDRAWN；来源效力 |
| Planning decision | SUGGESTED / FIRM / IGNORED / SUPERSEDED；本域决定 |
| Conversion | NOT_STARTED / PENDING / PARTIAL / SUCCEEDED / FAILED_NO_EFFECT / UNKNOWN |
| Leg.OwnerEffect | REGISTERED / OWNER_PENDING / OWNER_UNKNOWN / OWNER_ACCEPTED / REJECTED_NO_EFFECT |
| Leg.CorrectionHold | NONE / CORRECTION_REQUIRED / RECONCILING / RESOLVED；不覆盖真实接受事实 |
| PortionProtection | AVAILABLE / RESERVED_FOR_COMMAND / OWNER_PENDING / OWNER_UNKNOWN / OWNER_ACCEPTED / RELEASED_NO_EFFECT |

状态值来自当前主文，不能沿旧 Converted 一个布尔判断。[M:45] 聚合必须基于完整唯一有效份额集合：全accepted才SUCCEEDED；至少一份accepted且其余全有完整no-effect并释放才PARTIAL；accepted+pending或unknown一律PENDING；无accepted且全no-effect才FAILED_NO_EFFECT；无accepted、存在unknown且其余全no-effect才UNKNOWN。缺行不能算“全成功”。[M:51] [M:503]

### 3.2 不可变份额和业务身份

PlannedOrderPartition 是不可变 generation，Head 指向当前分区。份额使用半开区间，完整并集等于授权范围、无gap/overlap；ConversionAllocation等于整份额，必要拆分须先一次明确split产生不可变子份额，不能在Owner调用后暗切。两条20EA Pegging可以共享不可拆40EA parent，不能要求每条Pegging覆盖整个parent。[M:68] [M:124] [M:284]

ConversionLegPortion 的复合身份包含order/generation/portion/from/to/quantity/UOM；Protection.CurrentLeg精确关联该关系；每份额仅一个OwnerAcceptanceWinner并绑定真实receipt。OwnerReceipt保原规范响应字节、重算digest、原command/query/request/结果身份。PUR结果用resultRef，MES用commandId+operation，WMS用id+version，不把这些硬改为统一EvidenceRef；WMS原无编号时DisplayNumber为null。[M:233] [M:285] [M:311]

CommandSlot业务唯一键为scope+operation+plannedOrderId+requestKey，principal是授权/审计事实，不参与去重；Outbox为scope+targetOwner+effectId，payloadDigest不进唯一键，同effect异payload冲突。文件rawSHA包含全部字节，业务JCS摘要只针对明确preimage；SQL ExactTupleHash的UTF-16LE域也不能换成JCS UTF-8。[M:286] [M:289] [M:505]

Opaque Owner version可为`A`，只能按其协议相等比较；rowversion是8字节，不作业务算术。业务数量精确有理/定点计算，禁止float/epsilon/尾差塞最后段；UOM转换必须整除并保方向/量纲。源端已排除的safety/reservation不重复扣。[M:124] [M:331] [S:120]

### 3.3 版本的不同用途

CurrentHead、不可变Revision、Firm授权版、WriterEpoch和Owner当前观察各有独立身份。新意图可以捕获plannedOrderBusinessVersion=4而原Firm授权仍2；仅在该Firm仍适用且无后继决定时成立。历史真实Owner结果核原捕获链，不要求今天mutablehead相等或source仍ACTIVE。[M:489]

RecoveryCheckpoint.StepOrdinal归属已观察历史；ResumeStepOrdinal为待办，NEXT_READY时=StepOrdinal+1，其余相同，派生字段不作为INSERT/UPDATE输入。不能因接管把原观察和下一动作混为一行。[M:493]

## 4. 原六SPEC正常流程

**建议。** PO21先持久原SuggestionInbox、stream前驱及原包，核注册producer、digest、完整源图/当前head。APPLIED时同事务生成Suggestion/Head、Order/Revision/Partition/Pegging及Audit；同源id/version/digest只建一次。新Run失败、ResultSet不完整或publication非current不移动旧head，不删Firm/Pegging。[M:113] [M:122] [M:241]

**Firm。** 仅当前ACTIVE建议、可行份额与完整current source/policy/supply/calendar/actor/lease门可提交；同事务新增Decision、Revision及Head，无Owner命令。预检后变化不能自动更新expectedHeads替用户再提交。[M:91] [M:149]

**Ignore。** SUGGESTED或确无Owner影响的FIRM可忽略，保留原source/Pegging/history。AVAILABLE可用；RELEASED_NO_EFFECT须具名原proof、oldleg no-effect/holdNONE、无winner/未决case且CurrentLeg=null；不因无关历史leg永远禁用，也不凭absence释放。任何当前受保护/accepted/unknown或未处理更正阻止Ignore。[M:159] [M:487]

**Preview。** 保存不可变typed预览、准确分区/portion/branch和当前可解析原请求。PR足够原输入时可冻结intake；WO仅先固定SourceSearch；WMS先bodyless Context读。未来preparation/check/candidate/真实对象ID及body/digest必须null，待真实前驱返回才物化。Preview不保护份额、不写Owner Outbox、不调用Owner。[M:99]

**Start。** 以preview digest及完整CurrentHeads一次CAS，消费preview并同事务建立Work/Leg/Workflow/Step、LegPortion、Protection、首个可解析Owner命令/读、Checkpoint、Outbox。202只表示持久注册，原同意图重放仍返回原202；关闭页面不取消工作。[M:99] [M:101] [M:417]

**结果与部分失败。** 按完整原Owner请求/响应接收真实效果。成功永留，未决份额继续保护；新意图仅可针对有永久no-effect并明确释放的份额，且重新核当前资格。源取消后晚到成功同时写receipt/winner/OWNER_ACCEPTED与CorrectionHold REQUIRED，源仍WITHDRAWN，不复活旧需求或删除Owner对象。[M:105] [M:107] [M:237]

## 5. 三个Owner分支和17份采用合同

### 5.1 成功定义和原生协议

| 分支 | 请求和必要链 | 可接受成功终点 |
|---|---|---|
| PR / H12 | 原PrPlanIntakeCommand：stable convertRequestId、业务/行版本、sourceAllocation/root/generation、完整量/日期/pegging/technical/estimate/sourceAuthorization/Firm authority | PrPlanIntakeResult SUCCEEDED，真实prId/prNo/lineMappings及完整原sourceAllocation/requestDigest/resultRef一致。只是PR，未成PO或到货。[M:179] |
| WO / H13 | SourceSearch→真实Descriptor→CreatePreparation→EngineeringCheck→PrepareSnapshot→CreateRootWorkOrder；逐步存完整外壳/lookup及前驱 | 最后CommandResult COMMITTED、operation=CREATE_ROOT_WORK_ORDER、error=null、完整真实WO/root/snapshot/revision/control/evidence/binding/scope/许可关联。CREATED/NO_NEW_EXECUTION不等Released/投产。[M:185] [M:187] |
| Transfer | accepted SourceDemand owner=PLANNING/OPEN，完整range/UOM/site/purpose/target/budget；有效LOCAL_TRANSACTION或EXCLUSIVE_DELEGATION；Context/Catalog后真实T04 RequestCreate | 原不可变201 RequestView：真实id/version/digest、DRAFT、simulation=false，逐portion ACTIVE SourceBinding及segments精确覆盖。不是Stage或Stock移动。[M:191] [M:193] |

PUR requestDigest保原`{algorithm,canonicalizationVersion,value}`，不收窄为裸hash；NOT_OBSERVED有原convertRequestId，但无权擅读缺席requestDigest等字段。WO保存完整CommandResult而非仅内层result，202保ProcessingLookup；中间READY不等创建成功。WMS Context.environment保对象四成员，不改成字符串；source range `[80,100)` 与请求局部 `[0,20)` 由真实SourceBinding连接，不能偷换坐标。[M:181] [M:187] [M:195] [M:319] [T:98]

T04创建与T05当前观察是两个轴。T05可有更高版本、状态变化、UNKNOWN/不可用，不抹掉原201/winner。ACK丢失且无requestId时不得用POST T04做查询；CP22具名新纯original-slot lookup返回准确原201，非FOUND均不推无创建；真实adapter/pin缺失继续RequiredFenced。[M:193] [M:347] [T:58]

### 5.2 专业采用分工

| Ports | 独立责任及内容 |
|---|---|
| 0 MRP | Run/ResultSet/Publication/Head/SuggestionIdentity/CurrentHeadProof/Pegging/SourceBundle/SourceChange完整图，current完成且Suggestion恰一次。[S:66] |
| 1 POL、2 SUP | 完整政策/日历/lot/lead/priority/firmness/window；逐branch容量、安全量、保护/current资格。EXACT_REQUEST不隐增量，unsupported modes阻塞。[S:89] [S:112] |
| 3 POL profile | 三branch逐portion purpose/context/namespace/source/technical；PUR/MES真实registry observations由其Owner出具，POL不能代签。[S:135] |
| 6/7/8/9/10 | UOM、ITEM、SITE、LEGAL-ENTITY、DEMAND独立原body/current/provenance；MRP仅签mapping，不能代其事实。Site/LE显式映射，需求OPEN且range在源量内。[S:158] [S:240] |
| 13 MRP bridge、16 PLANNING authority | common→Transfer完整桥接；独立whole budget/range/source/destination/item/UOM/owner/purpose EXCLUSIVE授权，post-create delegation回执另外核。[S:261] [S:288] |
| 4 WO、5 TRANSFER closure | 原创建slot永久不再创建的四独立proof，以及只读已存在closure；WMS同role5 1.1另含纯原slot lookup。[T:28] [T:44] |
| 11 PLM | 独立技术basis/purpose/current，不冒已有部署HTTP；与role15 purpose policy不同。[T:61] |
| 12 WMS、14 PUR、15 MES | 原wire/registry/namespace/current capture与receipt流；MES含五nested技术issuer，不多算顶层数量。[T:73] [T:89] [T:106] |

所有原body、闭合schema、contract/schema digest、issuer、角色、scope、有效期及原始provenance必须精确匹配；同名接口/普通ADOPTED字段/本域hash不能创造外域事实。CP22专业采用是完整新设计意见，真实producer/provider安装与注册另有门。[A:14] [S:312]

### 5.3 Transfer两个完整图与publisher

common100例包含40PR/40WO/20TRANSFER和四pegging；按TRANSFER_REQUEST+准确portion选唯一供给，不强制全图supply/pegging均只有1。common40-member图与transfer21-member图分别完整、同UoW持有重核；mandatory.subjectRef绑定common主体，proofRefs仍独立wrapper身份，不要求两图ref/set/digest相同。[M:547] [S:46]

原publisher27为可信PLAN-PO/PUBLISHER，SourceDemand及独立授权issuer仍PLANNING。先scopeUoW→可信publisher→activelease→准确admission→FIRMhead→canonical/ref/hash→SourceCut，之后才26首次publication写：AuthorityRevision、ScopeBinding、21Dependency、SourceDemandRevision及两head。SourceCut本身和原source行不写；条件CanPublish=true不是生产授权。[M:578] [M:584] [M:586]

### 5.4 WO技术支持不能循环取证

PREPARE前恰4capture：Planning coverage、MAKE source、PLM purpose policy、REL purpose authorization，preparationRef=null。CHECK/SNAPSHOT/CREATE各恰11：另加CFG request/resolved、ENG baseline/UOM/precondition/manifest、DEV applicability，绑定真实preparationId/revision与原descriptor/cut。MES只是传输者，五专业issuer及provider pin独立认证。[T:115]

当前普通路径要求CFG UNIQUE/resolutionCount1且完整operations/materials/outputs与ENG一致；ENG完整DAG、PRIMARY恰1及五member manifest；DEV仅采用有据NOT_APPLICABLE_PROVEN且refs=[]，APPLICABLE/UNKNOWN仍阻塞，未覆盖偏差制造新流程。REL前置是用途授权，REL_RESERVATION是post-root真结果，不能倒作开始前置。[T:137] [T:144] [T:154] [T:161]

## 6. 事务、锁、旧writer和恢复

本域采用SQL Server SERIALIZABLE设计，同E/T transaction-owned applock加55表prefix/空range闭包；固定source10→writer20→order30→partition31→leg/workflow32→Owner33→checkpoint34→slot40→audit/outbox50。源发布、admission/Core publisher、epoch切换及业务writer都必须同协议。该方案按E/T串行写，有明确吞吐代价，不称已验证高并发。[M:369] [M:449]

同outer connection捕获55 named rowsets直至COMMIT，锁内无Owner HTTP/队列等待/文件下载。输出包括nestedSQL结果必须buffer到outer commit确认；commit未知503保原key/body，先本地query再Owner原query，不推定回滚。所有写前核当前lease epoch/holder/RV/time及需要的完整current cut；纯读不伪称已持mutator锁。[M:371]

WriterLeaseHistory不可变，cutover插新epoch并CAS Head；旧worker首笔domainwrite前被fence。新epoch不授权改原Owner key/body/actor/context。接管同时需要fresh cutover+recover；首次commanded动作即使原NOT_SENT也先纯原query。只有最新同原键NOT_OBSERVED已持久、无更新观测、确为原NOT_SENT且当前源/native授权有效，才允许首次发原body；maybe-sent的absence仍UNKNOWN。[M:287] [M:497] [C:35]

Head与新Revision完整11tuple同时包含writerName/epoch。cutover/takeover不修改合法旧业务Head；消费新观测产生Revision epoch9时其Head也写9，不能Revision9/Head8，也不能改旧Revision消除差异。此是CP20已闭设计修订，当前不再列为未修bug。[M:523]

永久no-effect释放必须由WO/WMS原Owner同slot独立事务关闭PRIMARY_CREATE、IDEMPOTENT_REPLAY、RECOVERY_RETRY全部路径，发布NOT_CREATED、FUTURE_CREATION_FENCE、ORIGINAL_SLOT_CLOSURE、BINDING_CLOSURE四份完整独立proof。每份绑定原E/T/key/body/actor/ranges及同slot终态，集合双向相等、无既有winner。read-existing仅读已签发proof，不能查询时顺便签fence；GET404、超时、NOT_OBSERVED或generic seal均不足。[T:35] [T:51]

Receipt摄取先持久原Inbox/stream predecessor，gap保WAITING；原bytes在前驱APPLIED后续办，保原observedAt/RecordedAt，当前lease时钟另取Now。WMS机器接收先同UoW插Inbox再写receipt满足FK；canIngest的即时ACK与之后canReadOwn独立。历史source已撤销不拒绝真实旧成功，而今时披露/接收权限依然核查。[M:231] [M:435] [T:84]

## 7. 权限、编码和披露

Core是独立新增SDK集成设计：实际provider contractId/version/digest须可信composition root认可；认证E/T/principal/object/action/site/fieldmask/permissionDigest/issuedAt/validTo。issuer=CORE，IAM可为本地authorityRef owner；ResourceKey=SHA256 JCS{actorId,plannedOrderId,siteId}，E/T在外层。独立signed-int64序号及完整前驱保护current，opaque version不得本地排序。[C:30]

Core publisher25与PLAN-PO Transfer publisher27不是同权限。六execute-only私池分权，应用/source/dispatcher/receipt worker不能写授权表；DENIED也是可合法发布的当前结果，旧同版同bytes replay不回退新DENIED。[C:33] [C:34]

PO04必须`plan-po.history.read`、PO05必须`plan-po.source.read`，读前和输出前用同具体动作；普通plan-po.read没有隐式继承或fallback。whole-value REDACTED不改原不可变body，也不等null/0。scope切换、mask/权限/current变化使旧generation响应失效；无权对象/字段遵循各入口当前协议，不泄漏私有Capture55/nativeframe/SQL参数/SDK grant。[C:39] [C:43] [C:45]

raw UTF-8先拒重复property，再核unknown/null/union/format/range；JCS键按UTF-16排序，业务集合按Unicode scalar排序，两者不可互换。原Owner opaque operation/query key存储1–256 UTF-16单元，保大小写/字节、不trim；public requestKey/FactId128与holder160不同域。Owner原namespace和digest对象保持原格式。[M:288] [M:375] [M:454]

## 8. 页面、API和错误恢复

六页面：列表、详情、转换、恢复、历史、缺必要接口/证据说明。详情显示三个状态轴、来源/current、分区/份额、owner leg/receipt及allowedActions；转换显示逐portion完整workflow和未具前驱的BLOCKED_PREDECESSOR；无“强制成功”“换键重试”“关闭proof”按钮。[M:74] [M:85] [M:101]

当前25路由含PO17-TAKEOVER，统一base `/api/plan/po/v1`，具体动作见本册后续API表。用户Idempotency-Key绑定requestKey，有expectedHeads时If-Match绑定准确rowversion；机器采用event/producer/stream前驱，不发明header别名。PO24仅真实MRP_SOURCE1.1可发SourceChange，POL/SUP不能仿造。PO26是独立WMS T05当前读，PO27是原slot纯查，旧CP17同号wrapper保持历史不双dispatch。[M:379]

| ID | 方法与相对路径 | 输入→输出 | 当前能力 |
|---|---|---|---|
| PO01 | `GET /options` | 无body → PlanOptions | plan-po.read / plan-po.decide |
| PO02 | `POST /planned-orders/search` | PlanSearch → PlanReadPageCp18 | plan-po.read |
| PO03 | `GET /planned-orders/{plannedOrderId}` | 无body → PlannedOrderDetailCp18 | plan-po.read |
| PO04 | `GET /planned-orders/{plannedOrderId}/revisions/{revision}` | 无body → HistoricalRevisionCp18 | plan-po.history.read |
| PO05 | `GET /planned-orders/{plannedOrderId}/source` | 无body → SourceLineageCp18 | plan-po.source.read |
| PO06 | `GET /planned-orders/{plannedOrderId}/conversion-works/{conversionWorkId}` | 无body → ConversionWorkPresentationCp18 | plan-po.read |
| PO07 | `GET /commands/{commandId}` | 无body → StoredCommandTerminal | plan-po.command.read |
| PO08 | `POST /owner-results/search` | OwnerResultQuery → OwnerResultObservation | plan-po.integration.read |
| PO09 | `GET /conversions/{conversionLegId}/recovery/{operationKey}` | 无body → RecoveryViewCp18 | plan-po.read |
| PO11 | `POST /planned-orders/{plannedOrderId}/firm` | PlanCommand → CommandOutcome | plan-po.firm |
| PO12 | `POST /planned-orders/{plannedOrderId}/ignore` | PlanCommand → CommandOutcome | plan-po.ignore |
| PO13 | `POST /planned-orders/{plannedOrderId}/conversion-previews` | ConversionPreviewCommand → ConversionPreview | plan-po.convert.preview |
| PO14 | `POST /planned-orders/{plannedOrderId}/conversion-works` | ConversionStartCommand → CommandOutcome | plan-po.convert.start |
| PO15 | `POST /conversions/{conversionLegId}/owner-steps` | ConversionResumeCommand → CommandOutcome / WmsSlotPendingOutcomeCp18 | plan-po.convert.recover |
| PO16 | `POST /planned-orders/{plannedOrderId}/corrections` | CorrectionCommand → CommandOutcome | plan-po.correction.manage |
| PO17 | `POST /writer-cutovers` | WriterCutoverCommand → CommandOutcome | plan-po.cutover |
| PO21 | `POST /mrp-suggestions` | SuggestionIngestEnvelope → IngestAck | registered PLAN-MRP producer |
| PO22 | `POST /owner-receipts` | OwnerReceiptEnvelope → IngestAck | registered PUR-PR / MES-WO / WMS-TRANSFER producer |
| PO23 | `GET /owner-receipts/{producerOwner}/{eventId}` | 无body → IngestAck | registered producer / plan-po.integration.read |
| PO24 | `POST /source-events` | SourceChangeCp18 → IngestAck | registered authenticated PLAN-MRP MRP_SOURCE exact1.1 producer |
| PO25 | `GET /mrp-suggestions/{producerOwner}/{eventId}` | 无body → IngestAck | plan-po.integration.mrp.read |
| PO26 | `POST /conversions/{conversionLegId}/transfer-current-read` | WmsCurrentReadCommandCp18 → WmsCreationCurrentCp18 | plan-po.integration.read |
| PO17-TAKEOVER | `POST /conversions/{conversionLegId}/epoch-takeovers` | TakeoverCommandCp18 → CommandOutcome | plan-po.cutover AND plan-po.convert.recover |
| PO27 | `POST /conversions/{conversionLegId}/original-creation-slot-query` | WmsSlotRecoveryCommandCp18 → CommandOutcome / WmsSlotPendingOutcomeCp18 | plan-po.convert.recover |
| PO28 | `GET /source-events/{producerOwner}/{eventId}` | 无body → IngestAck | fresh registered producer original event read scope |

源：[PLAN_PO_R3_CP18_API_REGISTRY.json](D:/CP6-archives/consolidation-20261010/root-planning-cache/plan-po-cp22/PLAN_PO_R3_CP18_API_REGISTRY.json)，准确CP22 ZIP同名字节；表中接口仍为设计，未证明已部署。

409/412/422不盲重试或自动替换expected；429/503仅在真实持久受理或原命令存在时沿原键查询/恢复；504先查原。SQL错误以(Number,invariantMessageToken)映射当前typed code/status，不能只按number；未知内部合同错误不外泄原SQL/native内容。原格式/权限、source完整性、scope/range守恒、Owner结果绑定、永久no-effect、stream gap、lease/current变化均保具体定位与恢复出口。[M:305] [M:307] [M:381]

## 9. 开发顺序和迁移边界

建议先实现严格codec、不可变事实/复合键、writer/current锁与本地capture；再接独立Core和17源合同的真实producer/pin；接MRP建议/Firm/Ignore；依次接PR、WO五步、Transfer原协议及纯lookup；最后接永久closure、源取消、跨epoch/冷恢复及完整 UI。每条分支缺真实采用只阻塞依赖动作，不能用示例ADOPTED或通用seal临时放行。

旧设计评估基线只有Purchase/Production与字符串ConvertedDocNo，adapter返回STUB即置Converted，已归为REBUILD；本次未重新运行或评估现仓库这些代码，不能把历史诊断直接声称当前bug。迁移须准确辨认真实原receipt与不可证旧Converted，legacy read-only/no double-write，不能补造receipt或重演旧订单。[M:33] [M:64] [M:277]

## 10. 原37AC和关键完整场景

六组原数量固定：SUG6、FRM6、IGN5、CNV8、PRT6、RCP6。原map和独审disposition共同解释：作者候选旧状态不覆盖后继最终PASS，但所有业务仍NOT_RUN。[R:41] [R:50]

| 原AC | 规则/验收义务 | 路由入口 |
|---|---|---|
| PO-AC-SUG-01 | 只从Proposed PLAN-MRP Required Adapter读取proof-bearing current Suggestion，不从旧`Plan_PlannedOrder`或ATP拼接。 | PO21, PO25, PO02, PO05 |
| PO-AC-SUG-02 | Suggestion携MrpRun/ResultSet/Publication、item/site/legalEntity、quantity/UOM/date/priority、完整Pegging、policy/supply/calendar/technical/current head proof。 | PO21, PO25, PO02, PO05 |
| PO-AC-SUG-03 | FOUND/PENDING/NOT_OBSERVED/UNKNOWN/UNAVAILABLE/SEALED_NO_EFFECT使用封闭联合；缺fact不建计划订单。 | PO21, PO25, PO02, PO05 |
| PO-AC-SUG-04 | 同Suggestion owner/id/version/digest只建一个PlannedOrder；异digest冲突隔离。 | PO21, PO25, PO02, PO05 |
| PO-AC-SUG-05 | 新MRP发布只有current head前移后才可supersede旧建议；失败Run不修改现行head。 | PO21, PO25, PO02, PO05 |
| PO-AC-SUG-06 | Suggestion与Pegging/portion同UoW建立；不得出现孤立Pegging或半个建议。 | PO21, PO25, PO02, PO05 |
| PO-AC-FRM-01 | Firm只作用ACTIVE current Suggestion且expectedHeads全部匹配。 | PO11, PO03, PO07 |
| PO-AC-FRM-02 | actor authority按当前Scope/action/site/branch复验；按钮可见不授权。 | PO11, PO03, PO07 |
| PO-AC-FRM-03 | Firm产生不可变Decision、Revision和Head，一次本地commit。 | PO11, PO03, PO07 |
| PO-AC-FRM-04 | Firm不创建PR/WO/transfer request，不把planned supply当实际供给。 | PO11, PO03, PO07 |
| PO-AC-FRM-05 | Firm与MRP supersession/source correction争同Source/Order锁；只有一个current后继。 | PO11, PO03, PO07 |
| PO-AC-FRM-06 | 同requestKey同body回原receipt；异body409。 | PO11, PO03, PO07 |
| PO-AC-IGN-01 | SUGGESTED可忽略；FIRM仅在全部portion仍AVAILABLE/RELEASED_NO_EFFECT且无外部效果时可忽略。 | PO12, PO03, PO04, PO07 |
| PO-AC-IGN-02 | 任何OWNER_ACCEPTED/PENDING/UNKNOWN或CorrectionHold非NONE阻断。 | PO12, PO03, PO04, PO07 |
| PO-AC-IGN-03 | Ignore保留Suggestion、Pegging、历史Decision和Owner事实，只追加新Revision。 | PO12, PO03, PO04, PO07 |
| PO-AC-IGN-04 | 来源取消不是前端Ignore；按PO24/CorrectionCase处理。 | PO12, PO03, PO04, PO07 |
| PO-AC-IGN-05 | Ignore不调用Owner、不返数量给其它Owner、不删除Outbox/receipt。 | PO12, PO03, PO04, PO07 |
| PO-AC-CNV-01 | 准确授权份额、无交叠/缺口并守恒。（依原rule整理） | PO13, PO14, PO15, PO26, PO27 |
| PO-AC-CNV-02 | 采用PR原intake/result，digest保完整对象。（依原rule整理） | PO13, PO14, PO15, PO26, PO27 |
| PO-AC-CNV-03 | WO按真实前驱顺序构造完整原生workflow。（依原rule整理） | PO13, PO14, PO15, PO26, PO27 |
| PO-AC-CNV-04 | TRANSFER原Context/Request/SourceBinding及current关系准确。（依原rule整理） | PO13, PO14, PO15, PO26, PO27 |
| PO-AC-CNV-05 | 多源和不可拆组保完整，只有明确split可改变分区。（依原rule整理） | PO13, PO14, PO15, PO26, PO27 |
| PO-AC-CNV-06 | 未来WO preparation/check/candidate/root身份不得预造。（依原rule整理） | PO13, PO14, PO15, PO26, PO27 |
| PO-AC-CNV-07 | 仅完整原Owner receipt及唯一winner证明成功。（依原rule整理） | PO13, PO14, PO15, PO26, PO27 |
| PO-AC-CNV-08 | 分支校验/currentness/错误与原键恢复完整传播。（依原rule整理） | PO13, PO14, PO15, PO26, PO27 |
| PO-AC-PRT-01 | Work state从leg事实按固定优先级派生，不由最后响应覆盖。 | PO06, PO07, PO09, PO15, PO27, PO17-TAKEOVER |
| PO-AC-PRT-02 | 成功leg和下游对象永久保留；失败/未知不回滚外部成功。 | PO06, PO07, PO09, PO15, PO27, PO17-TAKEOVER |
| PO-AC-PRT-03 | OWNER_PENDING/UNKNOWN继续保护其portion；新键、新batch或改branch不能绕过。 | PO06, PO07, PO09, PO15, PO27, PO17-TAKEOVER |
| PO-AC-PRT-04 | REJECTED_NO_EFFECT必须有同原Owner key的完整fence proof；之后新意图仍重验current heads。 | PO06, PO07, PO09, PO15, PO27, PO17-TAKEOVER |
| PO-AC-PRT-05 | 冷恢复从CommandSlot/OwnerCommand/Leg/Protection/Inbox/Receipt原行重建；不信客户端状态。 | PO06, PO07, PO09, PO15, PO27, PO17-TAKEOVER |
| PO-AC-PRT-06 | lease epoch、attempt和writer epoch在每次写前重验；旧worker零domain写。 | PO06, PO07, PO09, PO15, PO27, PO17-TAKEOVER |
| PO-AC-RCP-01 | producer、audience、stream前驱和digest精确核验。（依原rule整理） | PO22, PO23, PO24, PO05 |
| PO-AC-RCP-02 | 完整原native字节与数据库复合身份精确绑定。（依原rule整理） | PO22, PO23, PO24, PO05 |
| PO-AC-RCP-03 | 每portion唯一不可变winner，同源供给不双计。（依原rule整理） | PO22, PO23, PO24, PO05 |
| PO-AC-RCP-04 | 源取消后晚到真实Owner成功保留并进CorrectionHold。（依原rule整理） | PO22, PO23, PO24, PO05 |
| PO-AC-RCP-05 | SC06仅一个active供给代表，不把plan100和WO100相加。（依原rule整理） | PO22, PO23, PO24, PO05 |
| PO-AC-RCP-06 | SC22失败Run不改变有效source/Firm/Pegging。（依原rule整理） | PO22, PO23, PO24, PO05 |

源：[88e0db6aedd19f7d__CP22_ORIGINAL_37_AC_CURRENT_MAP.json](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/88/88e0db6aedd19f7d__CP22_ORIGINAL_37_AC_CURRENT_MAP.json) 的 `/ac/*/originalRequirement` 与 `/rule`；CNV/RCP 原 requirement 为共同章节引用，本表按各自 rule 给出可读义务，不伪造原独立逐字要求。本表不把引用数当业务覆盖结果。

跨组重点：SC06 Firm制造100转WO100后只一个active供给代表，PR不能直接成为ACTIVE supply；SC18源取消与三Owner晚结果两种顺序均保旧真实winner及新更正hold；SC22失败MRP只记失败Inbox、原有效建议/Firm/Pegging不动。[M:259] [M:263] [M:267]

实现时还需真实验证：全接受/部分永久no-effect/接受加UNKNOWN的聚合；原key首次/重复/异payload；原201丢ACK与T05变化；跨epoch恢复与Head11tuple；前驱派生body不预造；source WAITING原包续办；Core目的/字段撤权；common/Transfer双cut及publisher身份。静态normal与hash一致不是这些运行结果。[R:29] [R:33] [R:35]

## 11. 必要材料、限制和双边采用门

准确设计已含17新proposal、五nested和Core，不能再说这些接口“完全没有设计”；但产品运行所需真实issuer/provider/pin/admission、所有writer同UoW、driver/codec/锁、Owner现行适配、生产部署与业务验收全部未证。conditional normal里的ADOPTED/CanPublish不是实际授权。[A:14] [A:15]

DEV有偏差适用或未知的制造路径、缺Closure、未落实独占委托或任一source current无法证明时继续RequiredFenced。现普通链支持范围不能扩成所有工程/产线/组织模式。跨模块采用需引用CP22准确合同/版本/hash，不能只把旧MRP/POL/SUP或MES/WMS模块“已接受”三个字拼成整体runtime通过。[T:155] [S:312]

历史R2容器缺失、原NEXT/本地后继NEXT、WMS原分配/当前accepted主文和接受记录commit各保独立身份；当前CP22573成员可用不消除旧源资格。当前schema/DDL/代码/normal图和37AC细粒度证据仍有本轮未读范围，列第12节，不以核心文本已整理冒整包审完。

## 12. 来源和阅读覆盖

本轮全文分段读取当前主文596行、CP22入口、最终根接受、64行独审、Planning318行/OwnerTech176行/Core55行专业采用。API registry读取全部25项id/method/path/request/response/capability及整体headers/重放顺序；原AC表提取原37requirement/规则/路由，保留其余artifact/历史传播待核。前端已读六页面、状态轴、current/历史/权限/恢复重要说明；错误全集和逐路由全字段未声明完整审读。

573成员ZIP已在前期容器盘点核实身份，本轮仅读取上述规范与选定成员，不声称读过284MB全部内容；888,998B的17份原closed schemas、SQL/TS/C#具体实现和巨大normal row图仍须继续归并。所有阅读状态见 [root-planning-reading](D:/CP6/docs/CP6_开发设计文档_20261010/evidence/root-planning-reading.json)。本次未执行原SQL/脚本/fixture、未编译部署或产生真实授权。

[A:2]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/8e/8e03121644c31bba__PLAN_PO_CP22_ROOT_DESIGN_ACCEPTANCE_20261004.txt:2>
[A:4]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/8e/8e03121644c31bba__PLAN_PO_CP22_ROOT_DESIGN_ACCEPTANCE_20261004.txt:4>
[A:6]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/8e/8e03121644c31bba__PLAN_PO_CP22_ROOT_DESIGN_ACCEPTANCE_20261004.txt:6>
[A:8]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/8e/8e03121644c31bba__PLAN_PO_CP22_ROOT_DESIGN_ACCEPTANCE_20261004.txt:8>
[A:11]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/8e/8e03121644c31bba__PLAN_PO_CP22_ROOT_DESIGN_ACCEPTANCE_20261004.txt:11>
[A:14]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/8e/8e03121644c31bba__PLAN_PO_CP22_ROOT_DESIGN_ACCEPTANCE_20261004.txt:14>
[A:15]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/8e/8e03121644c31bba__PLAN_PO_CP22_ROOT_DESIGN_ACCEPTANCE_20261004.txt:15>
[A:17]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/8e/8e03121644c31bba__PLAN_PO_CP22_ROOT_DESIGN_ACCEPTANCE_20261004.txt:17>
[C:3]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/56/56ebc0240965beab__CP22_CORE_DESIGN_ADOPTION.md:3>
[C:30]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/56/56ebc0240965beab__CP22_CORE_DESIGN_ADOPTION.md:30>
[C:33]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/56/56ebc0240965beab__CP22_CORE_DESIGN_ADOPTION.md:33>
[C:34]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/56/56ebc0240965beab__CP22_CORE_DESIGN_ADOPTION.md:34>
[C:35]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/56/56ebc0240965beab__CP22_CORE_DESIGN_ADOPTION.md:35>
[C:39]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/56/56ebc0240965beab__CP22_CORE_DESIGN_ADOPTION.md:39>
[C:43]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/56/56ebc0240965beab__CP22_CORE_DESIGN_ADOPTION.md:43>
[C:45]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/56/56ebc0240965beab__CP22_CORE_DESIGN_ADOPTION.md:45>
[M:25]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/2f/2fc131613a8433d3__PLAN-PO-01_CP22_FULL_SCOPE_DESIGN.md:25>
[M:33]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/2f/2fc131613a8433d3__PLAN-PO-01_CP22_FULL_SCOPE_DESIGN.md:33>
[M:39]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/2f/2fc131613a8433d3__PLAN-PO-01_CP22_FULL_SCOPE_DESIGN.md:39>
[M:41]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/2f/2fc131613a8433d3__PLAN-PO-01_CP22_FULL_SCOPE_DESIGN.md:41>
[M:45]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/2f/2fc131613a8433d3__PLAN-PO-01_CP22_FULL_SCOPE_DESIGN.md:45>
[M:51]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/2f/2fc131613a8433d3__PLAN-PO-01_CP22_FULL_SCOPE_DESIGN.md:51>
[M:55]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/2f/2fc131613a8433d3__PLAN-PO-01_CP22_FULL_SCOPE_DESIGN.md:55>
[M:64]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/2f/2fc131613a8433d3__PLAN-PO-01_CP22_FULL_SCOPE_DESIGN.md:64>
[M:68]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/2f/2fc131613a8433d3__PLAN-PO-01_CP22_FULL_SCOPE_DESIGN.md:68>
[M:74]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/2f/2fc131613a8433d3__PLAN-PO-01_CP22_FULL_SCOPE_DESIGN.md:74>
[M:85]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/2f/2fc131613a8433d3__PLAN-PO-01_CP22_FULL_SCOPE_DESIGN.md:85>
[M:91]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/2f/2fc131613a8433d3__PLAN-PO-01_CP22_FULL_SCOPE_DESIGN.md:91>
[M:99]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/2f/2fc131613a8433d3__PLAN-PO-01_CP22_FULL_SCOPE_DESIGN.md:99>
[M:101]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/2f/2fc131613a8433d3__PLAN-PO-01_CP22_FULL_SCOPE_DESIGN.md:101>
[M:105]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/2f/2fc131613a8433d3__PLAN-PO-01_CP22_FULL_SCOPE_DESIGN.md:105>
[M:107]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/2f/2fc131613a8433d3__PLAN-PO-01_CP22_FULL_SCOPE_DESIGN.md:107>
[M:113]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/2f/2fc131613a8433d3__PLAN-PO-01_CP22_FULL_SCOPE_DESIGN.md:113>
[M:122]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/2f/2fc131613a8433d3__PLAN-PO-01_CP22_FULL_SCOPE_DESIGN.md:122>
[M:124]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/2f/2fc131613a8433d3__PLAN-PO-01_CP22_FULL_SCOPE_DESIGN.md:124>
[M:149]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/2f/2fc131613a8433d3__PLAN-PO-01_CP22_FULL_SCOPE_DESIGN.md:149>
[M:159]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/2f/2fc131613a8433d3__PLAN-PO-01_CP22_FULL_SCOPE_DESIGN.md:159>
[M:179]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/2f/2fc131613a8433d3__PLAN-PO-01_CP22_FULL_SCOPE_DESIGN.md:179>
[M:181]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/2f/2fc131613a8433d3__PLAN-PO-01_CP22_FULL_SCOPE_DESIGN.md:181>
[M:185]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/2f/2fc131613a8433d3__PLAN-PO-01_CP22_FULL_SCOPE_DESIGN.md:185>
[M:187]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/2f/2fc131613a8433d3__PLAN-PO-01_CP22_FULL_SCOPE_DESIGN.md:187>
[M:191]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/2f/2fc131613a8433d3__PLAN-PO-01_CP22_FULL_SCOPE_DESIGN.md:191>
[M:193]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/2f/2fc131613a8433d3__PLAN-PO-01_CP22_FULL_SCOPE_DESIGN.md:193>
[M:195]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/2f/2fc131613a8433d3__PLAN-PO-01_CP22_FULL_SCOPE_DESIGN.md:195>
[M:231]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/2f/2fc131613a8433d3__PLAN-PO-01_CP22_FULL_SCOPE_DESIGN.md:231>
[M:233]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/2f/2fc131613a8433d3__PLAN-PO-01_CP22_FULL_SCOPE_DESIGN.md:233>
[M:237]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/2f/2fc131613a8433d3__PLAN-PO-01_CP22_FULL_SCOPE_DESIGN.md:237>
[M:241]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/2f/2fc131613a8433d3__PLAN-PO-01_CP22_FULL_SCOPE_DESIGN.md:241>
[M:259]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/2f/2fc131613a8433d3__PLAN-PO-01_CP22_FULL_SCOPE_DESIGN.md:259>
[M:263]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/2f/2fc131613a8433d3__PLAN-PO-01_CP22_FULL_SCOPE_DESIGN.md:263>
[M:267]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/2f/2fc131613a8433d3__PLAN-PO-01_CP22_FULL_SCOPE_DESIGN.md:267>
[M:277]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/2f/2fc131613a8433d3__PLAN-PO-01_CP22_FULL_SCOPE_DESIGN.md:277>
[M:284]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/2f/2fc131613a8433d3__PLAN-PO-01_CP22_FULL_SCOPE_DESIGN.md:284>
[M:285]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/2f/2fc131613a8433d3__PLAN-PO-01_CP22_FULL_SCOPE_DESIGN.md:285>
[M:286]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/2f/2fc131613a8433d3__PLAN-PO-01_CP22_FULL_SCOPE_DESIGN.md:286>
[M:287]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/2f/2fc131613a8433d3__PLAN-PO-01_CP22_FULL_SCOPE_DESIGN.md:287>
[M:288]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/2f/2fc131613a8433d3__PLAN-PO-01_CP22_FULL_SCOPE_DESIGN.md:288>
[M:289]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/2f/2fc131613a8433d3__PLAN-PO-01_CP22_FULL_SCOPE_DESIGN.md:289>
[M:301]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/2f/2fc131613a8433d3__PLAN-PO-01_CP22_FULL_SCOPE_DESIGN.md:301>
[M:305]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/2f/2fc131613a8433d3__PLAN-PO-01_CP22_FULL_SCOPE_DESIGN.md:305>
[M:307]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/2f/2fc131613a8433d3__PLAN-PO-01_CP22_FULL_SCOPE_DESIGN.md:307>
[M:311]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/2f/2fc131613a8433d3__PLAN-PO-01_CP22_FULL_SCOPE_DESIGN.md:311>
[M:319]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/2f/2fc131613a8433d3__PLAN-PO-01_CP22_FULL_SCOPE_DESIGN.md:319>
[M:331]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/2f/2fc131613a8433d3__PLAN-PO-01_CP22_FULL_SCOPE_DESIGN.md:331>
[M:347]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/2f/2fc131613a8433d3__PLAN-PO-01_CP22_FULL_SCOPE_DESIGN.md:347>
[M:369]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/2f/2fc131613a8433d3__PLAN-PO-01_CP22_FULL_SCOPE_DESIGN.md:369>
[M:371]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/2f/2fc131613a8433d3__PLAN-PO-01_CP22_FULL_SCOPE_DESIGN.md:371>
[M:375]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/2f/2fc131613a8433d3__PLAN-PO-01_CP22_FULL_SCOPE_DESIGN.md:375>
[M:379]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/2f/2fc131613a8433d3__PLAN-PO-01_CP22_FULL_SCOPE_DESIGN.md:379>
[M:381]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/2f/2fc131613a8433d3__PLAN-PO-01_CP22_FULL_SCOPE_DESIGN.md:381>
[M:417]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/2f/2fc131613a8433d3__PLAN-PO-01_CP22_FULL_SCOPE_DESIGN.md:417>
[M:435]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/2f/2fc131613a8433d3__PLAN-PO-01_CP22_FULL_SCOPE_DESIGN.md:435>
[M:449]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/2f/2fc131613a8433d3__PLAN-PO-01_CP22_FULL_SCOPE_DESIGN.md:449>
[M:454]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/2f/2fc131613a8433d3__PLAN-PO-01_CP22_FULL_SCOPE_DESIGN.md:454>
[M:487]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/2f/2fc131613a8433d3__PLAN-PO-01_CP22_FULL_SCOPE_DESIGN.md:487>
[M:489]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/2f/2fc131613a8433d3__PLAN-PO-01_CP22_FULL_SCOPE_DESIGN.md:489>
[M:493]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/2f/2fc131613a8433d3__PLAN-PO-01_CP22_FULL_SCOPE_DESIGN.md:493>
[M:497]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/2f/2fc131613a8433d3__PLAN-PO-01_CP22_FULL_SCOPE_DESIGN.md:497>
[M:503]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/2f/2fc131613a8433d3__PLAN-PO-01_CP22_FULL_SCOPE_DESIGN.md:503>
[M:505]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/2f/2fc131613a8433d3__PLAN-PO-01_CP22_FULL_SCOPE_DESIGN.md:505>
[M:523]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/2f/2fc131613a8433d3__PLAN-PO-01_CP22_FULL_SCOPE_DESIGN.md:523>
[M:547]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/2f/2fc131613a8433d3__PLAN-PO-01_CP22_FULL_SCOPE_DESIGN.md:547>
[M:570]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/2f/2fc131613a8433d3__PLAN-PO-01_CP22_FULL_SCOPE_DESIGN.md:570>
[M:576]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/2f/2fc131613a8433d3__PLAN-PO-01_CP22_FULL_SCOPE_DESIGN.md:576>
[M:578]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/2f/2fc131613a8433d3__PLAN-PO-01_CP22_FULL_SCOPE_DESIGN.md:578>
[M:580]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/2f/2fc131613a8433d3__PLAN-PO-01_CP22_FULL_SCOPE_DESIGN.md:580>
[M:584]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/2f/2fc131613a8433d3__PLAN-PO-01_CP22_FULL_SCOPE_DESIGN.md:584>
[M:586]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/2f/2fc131613a8433d3__PLAN-PO-01_CP22_FULL_SCOPE_DESIGN.md:586>
[R:3]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/81/81aa15a906806c35__PLAN_PO_CP22_WHOLE_SIX_SPEC_INDEPENDENT_STATIC_REVIEW_20261004.md:3>
[R:29]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/81/81aa15a906806c35__PLAN_PO_CP22_WHOLE_SIX_SPEC_INDEPENDENT_STATIC_REVIEW_20261004.md:29>
[R:33]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/81/81aa15a906806c35__PLAN_PO_CP22_WHOLE_SIX_SPEC_INDEPENDENT_STATIC_REVIEW_20261004.md:33>
[R:35]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/81/81aa15a906806c35__PLAN_PO_CP22_WHOLE_SIX_SPEC_INDEPENDENT_STATIC_REVIEW_20261004.md:35>
[R:41]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/81/81aa15a906806c35__PLAN_PO_CP22_WHOLE_SIX_SPEC_INDEPENDENT_STATIC_REVIEW_20261004.md:41>
[R:50]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/81/81aa15a906806c35__PLAN_PO_CP22_WHOLE_SIX_SPEC_INDEPENDENT_STATIC_REVIEW_20261004.md:50>
[S:46]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/be/be121794b4e0ec0b__PLANNING_SOURCE_PROFESSIONAL_DESIGN_ADOPTION_20261004.md:46>
[S:52]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/be/be121794b4e0ec0b__PLANNING_SOURCE_PROFESSIONAL_DESIGN_ADOPTION_20261004.md:52>
[S:66]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/be/be121794b4e0ec0b__PLANNING_SOURCE_PROFESSIONAL_DESIGN_ADOPTION_20261004.md:66>
[S:89]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/be/be121794b4e0ec0b__PLANNING_SOURCE_PROFESSIONAL_DESIGN_ADOPTION_20261004.md:89>
[S:112]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/be/be121794b4e0ec0b__PLANNING_SOURCE_PROFESSIONAL_DESIGN_ADOPTION_20261004.md:112>
[S:120]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/be/be121794b4e0ec0b__PLANNING_SOURCE_PROFESSIONAL_DESIGN_ADOPTION_20261004.md:120>
[S:135]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/be/be121794b4e0ec0b__PLANNING_SOURCE_PROFESSIONAL_DESIGN_ADOPTION_20261004.md:135>
[S:158]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/be/be121794b4e0ec0b__PLANNING_SOURCE_PROFESSIONAL_DESIGN_ADOPTION_20261004.md:158>
[S:240]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/be/be121794b4e0ec0b__PLANNING_SOURCE_PROFESSIONAL_DESIGN_ADOPTION_20261004.md:240>
[S:261]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/be/be121794b4e0ec0b__PLANNING_SOURCE_PROFESSIONAL_DESIGN_ADOPTION_20261004.md:261>
[S:288]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/be/be121794b4e0ec0b__PLANNING_SOURCE_PROFESSIONAL_DESIGN_ADOPTION_20261004.md:288>
[S:312]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/be/be121794b4e0ec0b__PLANNING_SOURCE_PROFESSIONAL_DESIGN_ADOPTION_20261004.md:312>
[T:3]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cb/cbd38c11127b763e__OWNER_TECH_CP22_PROFESSIONAL_DELTA_ADOPTION_20261004.md:3>
[T:28]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cb/cbd38c11127b763e__OWNER_TECH_CP22_PROFESSIONAL_DELTA_ADOPTION_20261004.md:28>
[T:35]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cb/cbd38c11127b763e__OWNER_TECH_CP22_PROFESSIONAL_DELTA_ADOPTION_20261004.md:35>
[T:44]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cb/cbd38c11127b763e__OWNER_TECH_CP22_PROFESSIONAL_DELTA_ADOPTION_20261004.md:44>
[T:51]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cb/cbd38c11127b763e__OWNER_TECH_CP22_PROFESSIONAL_DELTA_ADOPTION_20261004.md:51>
[T:58]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cb/cbd38c11127b763e__OWNER_TECH_CP22_PROFESSIONAL_DELTA_ADOPTION_20261004.md:58>
[T:61]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cb/cbd38c11127b763e__OWNER_TECH_CP22_PROFESSIONAL_DELTA_ADOPTION_20261004.md:61>
[T:73]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cb/cbd38c11127b763e__OWNER_TECH_CP22_PROFESSIONAL_DELTA_ADOPTION_20261004.md:73>
[T:84]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cb/cbd38c11127b763e__OWNER_TECH_CP22_PROFESSIONAL_DELTA_ADOPTION_20261004.md:84>
[T:89]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cb/cbd38c11127b763e__OWNER_TECH_CP22_PROFESSIONAL_DELTA_ADOPTION_20261004.md:89>
[T:98]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cb/cbd38c11127b763e__OWNER_TECH_CP22_PROFESSIONAL_DELTA_ADOPTION_20261004.md:98>
[T:106]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cb/cbd38c11127b763e__OWNER_TECH_CP22_PROFESSIONAL_DELTA_ADOPTION_20261004.md:106>
[T:115]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cb/cbd38c11127b763e__OWNER_TECH_CP22_PROFESSIONAL_DELTA_ADOPTION_20261004.md:115>
[T:137]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cb/cbd38c11127b763e__OWNER_TECH_CP22_PROFESSIONAL_DELTA_ADOPTION_20261004.md:137>
[T:144]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cb/cbd38c11127b763e__OWNER_TECH_CP22_PROFESSIONAL_DELTA_ADOPTION_20261004.md:144>
[T:154]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cb/cbd38c11127b763e__OWNER_TECH_CP22_PROFESSIONAL_DELTA_ADOPTION_20261004.md:154>
[T:155]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cb/cbd38c11127b763e__OWNER_TECH_CP22_PROFESSIONAL_DELTA_ADOPTION_20261004.md:155>
[T:161]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cb/cbd38c11127b763e__OWNER_TECH_CP22_PROFESSIONAL_DELTA_ADOPTION_20261004.md:161>

## 必要附件整理完成与例证采用边界

当前接口/数据/状态/权限/SQL与TS/C#设计、17上游闭合合同及必要治理内容已整理；32份正常/恢复场景按根20、CP19九、CP20/21三完成语义归并与精确复用。573包成员逐项保留当前/历史/作者工具身份，不把元数据核对说成全部历史代码审计。

开发时并读 [正常场景与恢复状态](D:/CP6/docs/CP6_开发设计文档_20261010/contracts/PLAN-PO-01-正常场景与恢复状态.md:1)、[CP19连续场景](D:/CP6/docs/CP6_开发设计文档_20261010/contracts/PLAN-PO-01-CP19正常场景.md:1) 和 [CP20/21续接场景](D:/CP6/docs/CP6_开发设计文档_20261010/contracts/PLAN-PO-01-CP20与CP21续接场景.md:1)。规范阅读完成并不关闭 PLAN-IF-009–015 及正常场景中的静态对齐项；旧R2缺包、实际Owner采用0、业务NOT_RUN和运行UNPROVEN继续保留。精确范围见 [必要附件证据](D:/CP6/docs/CP6_开发设计文档_20261010/evidence/PLAN-PO-01-supplement-reading.json:1)。
