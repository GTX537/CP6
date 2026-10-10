# PLAN-EXC-01｜缺料、供给异常与重算协调开发设计

状态：`required_materials_consolidated`。当前 CP12 原五 SPEC 的有效规则与必要附件已整理，旧版本、作者工具和来源准备材料另有逐项阅读边界。原设计已静态接受；40 REQ、103 AC 为该轮新编设计身份，业务执行全部 `NOT_RUN`。本状态不表示351份历史成员逐字全读或产品已实现。[M:632] [S:5] [A:5]

## 1. 目的、操作者与Owner

EXC 为每份真实责任建立缺料/延迟事项，解释原因、定位完整影响、协调真实责任Owner，允许有证关闭、限时隐藏告警和有边界重算。EXC 拥有 Case/Episode、Assessment、Impact、协调 Work/Receipt、告警、原请求槽及本域投影；不拥有 PR/PO/WO/Transfer 创建、采购审批、Stock 主账、质量放行、财务结清或客户承诺日期。[M:19] [M:284] [M:442]

Main Planning 为业务Owner；Reader、SourceReader、ImpactReader、Coordinator、Resolver、AlertClassifier、Recalc、Recovery、Export 能力分开授权。SUP/MRP 是原直接依赖，POL 是继承依赖；STOCK、ATP、PUR-PR、已接受 PO CP22 是有限支持来源，各自只能证明自己的事实。[S:39] [M:446]

## 2. 当前版本、完整组合和接受边界

| 层次 | 开发使用规则 |
|---|---|
| 当前主文 | `77b33224eaeb51c0b4cd1fc5b8ccb601fb33eec69ce9f421393f1206ce199b7d`，164,281B/973行，v1.11 CP12；后继前言/§26及当前接受记录优先于正文保留的旧检查点标签。[M:957] |
| 组合目录 | `90017237467eb05e10942a98a551e5d8aff83ee298be294675803f0a2cdbecbd`。每个逻辑文件选择唯一指定完整成员：CP12→CP11→CP10→CP09→准确CP08基包，不拼接差量，不以基包旧同名文件替换。[I:8] |
| 五载体 | 当前351成员/487,356,950解码字节，分别继承CP08 326、CP09 7、CP10 4、CP11 2、CP12 12；本地选择账见 [组合成员账](../evidence/root-plan-exc-current-members.json)。其中历史成员保留历史用途。 |
| 最终独审 | 当前五SPEC静态PASS；CP11-R01已由CP12新判断关闭，旧CP07/CP10 PASS撤回继续有效。[R:10] [R:103] |
| 根接受 | 2026-10-06T03:26:38Z，保留QE01–E08及PO Q1–Q5；136专业scope=A103+B21+C12，不能与103业务AC混数。[A:7] [S:28] |
| 归档恢复E09 | 后继CURRENT记录10:01:57Z批准：182份恢复原件+4份新恢复记录=186；旧192计划保历史，10份辅助身份回执正文未恢复。它们不等于缺失设计或独立评审正文，也不以新材料冒充原回执。[U:646] |

两项正式阅读澄清有效：原SPEC导航的80项更新为完整AC001–103；B-PC09引用A-PC04的旧OPEN文字由精确外置勘误按指定字段解释为当前静态已关闭。不能全局替换旧历史RETURN或据此宣布实际部署完成。[E:1] [K:1]

专业意见是根任命的新方案静态审读，97叶与10role的实际生产采用、provider pin、Core注册/grant仍为0或null。当前静态世界的新Case/Key/SourceFact身份只表达勘误后完整因果，不能证明生产旧不可变行可以覆盖、原业务body可以改发或旧Case已完成迁移。[S:39]

## 3. 数据、身份、数量与状态

### 3.1 事项、责任和独立状态轴

Case围绕真实Owner/document/line/schedule/occurrence的稳定责任；版本与显示单号不是稳定责任身份。Episode是一轮原因周期，关闭后更正产生新Episode及复核，不覆盖旧Closure。[M:87] [M:499]

| 轴 | 规范值或意义 |
|---|---|
| Case生命周期 | OPEN / IN_PROGRESS / REVIEW_REQUIRED / CLOSED |
| 知识完整性 | COMPLETE / PARTIAL / UNKNOWN / CONFLICT / LEGACY_UNVERIFIED |
| 业务结果 | KNOWN_SHORTAGE / KNOWN_DELAY / NO_ACTIVE_GAP / RESPONSIBILITY_RETIRED / UNDETERMINED |
| 适用性 | CURRENT / STALE / UNKNOWN / RESTRICTED |
| 告警 | VISIBLE / DISMISSED / EXPIRED_DISMISSAL；不会改短缺或履约事实 |
| 协调Work | REGISTERED / WAITING_OWNER / OWNER_UNKNOWN / APPLIED / REJECTED / BLOCKED_AUTH / REQUIRED_FENCED |
| 执行责任 | OPEN / PROCESSING / OUTCOME_UNKNOWN / SETTLED / REJECTED_NO_EFFECT / ACCEPTED_INDEPENDENT |
| 重算 | 登记、准备、封装输入、请求Run、计算、封存结果、选择、发布各阶段分开；动态本地状态与MRP回执stage另有映射 |

`businessRevision`为正整数；原source version是opaque字符串；SQL `rowversion`为8字节原生token，允许间隔，客户端不得自行+1。CaseHead完整绑定同Case、RevisionId/digest、businessRevision、WriterName/Epoch和LocalCommitSeq。活跃writer epoch9可以针对历史Revision/Head epoch8操作；只有实际新Revision与新Head一起改9。[M:87] [M:438] [M:505]

### 3.2 数量与材料责任

仅完整必要输入下，`knownShortage=max(0, RequiredOutstanding−OnTimeQualifiedCoverage)`；已知需求100/可用80但另一必要源未知时，结果为UNDETERMINED且短缺null，不能断言20。已知Hold15遇服务失联仍保已知限制。UNKNOWN、明确不适用、明确已知失败、REDACTED分别表达。[M:121] [M:638]

数量用固定8位十进制字符串：单行decimal(21,8)，汇总decimal(38,8)；排序数量允许30整数位/8小数位的完整汇总域，不误缩为单行域。UOM转换用精确有理数和scaled integer，不能float/epsilon/无声舍入。原范围坐标不是实物序号，原coordinate80可与数量UNKNOWN/null同时存在。[M:127] [M:426] [M:503] [M:638]

供给阶段替代：计划100→PO60→收货30，当前贡献40+30+30；收货中的接受20/拒绝10不把拒绝10重新变为未收货。自有RSV只覆盖自身，Allocation是其内部关系不得二扣；`NET_RESIDUAL`已含offset不得再减。原主文未授EXC更改SUP或MAT台账。[M:239] [M:781]

材料例保留两个Case：旧WO gross60已抵20→NET40且offset/reserve20继续保护；新MAKE父100按min20/increment20组成100，OP10技术9/5派生180，独立free50只覆盖新责任→新缺130。兼容组总170=40+130，不能合成一个事项；晚到PUR10仅属于旧[20,30)且晚一天，不抵按时旧40或新130。父建议显式version必须等原ParentSuggestionRef及父header版本，不能拿schema/algorithm/generation代替。[M:783] [M:940] [M:944]

### 3.3 原键、原件与存储根

命令槽为E/T+kind+Case+Episode+requestKey，不包含actor；Export用E/T+稳定bundle+EXPORT+key。KeyIdentity在相同namespace/hash桶锁内比较完整原UTF8长度和字节，碰撞以collisionOrdinal分开；摘要不是最终身份。public requestKey256不会缩短native长键。[M:106] [M:499] [M:531]

SourceFact业务摘要、原artifact完整raw SHA、native canonical摘要、Stored行BodyDigest及receipt排除自身摘要各守自己的领域。公开DTO/AllowedAction不是持久化权威根。15张可变rowversion表的BodyCanonical是同statement取得原生token后在可信adapter生成的非持久读回投影，不SET、不computed、不为它二次UPDATE。[M:440] [M:509] [M:835]

## 4. 五SPEC正常流程

### SPEC01 清单与解释

EX01取得当前有权选项，EX02按完整CaseOrigin、状态、精确兼容维度及固定readCut检索，EX03读事项。来源与数量依据只有独立目的获准才展开；汇总和列表来自同cut。EX09显式登记CaptureWork，按完整RequiredOwnerRead分页收集，封存SourceGraph/Assessment并一次推进Head；刷新失败保留旧解释与失败原因。[M:149] [M:225]

### SPEC02 来源与责任

M01验证独立issuer/schema/pin/audience/E/T及原字节，保SourceKey、Fact、Provenance、原event身份。SourceRead必须同query/capture/current/watermark、完整前驱与最后页证明；失败Owner仍在必要集合。实际发生、原记录、首次接收、观察、评估时间分开；unknown occurredAt仍null。[M:239]

MRP来源保存RunRequest→Run→InputSet→Attempt→OutputSeal→ResultSet/Manifest→Validation→Selection→Publication→EffectiveSet；非MRP来源用明确OwnerOccurrence，不伪造MRP祖先。MAT依据真实Obligation、RemainingBasis及offset/transition，不由WorkOrderNo或CompletedQty猜净量。[M:239] [M:469]

### SPEC03 影响与责任受理

Impact从精确变化范围出发，沿依赖、Pegging、代表关系、转换、材料派生、执行、当前控制双向求完整闭包；保全受益者、不可拆共享组、Firm/MAT及范围外保护。A取消共享PO中的30，不等于可删A/B共享100或归还B保护。[M:265]

PRIMARY/REPLAY/RECOVERY/LEGACY全路径分别证明；“收到停止”“禁止新执行”“已结清在途”不是同一事实。完整列出未知在途只证明图完整，仍可阻止业务关闭。EX11受理以真实Owner receipt为准，通知/Outbox Delivered不算接受。[M:265] [M:573]

PC05的acceptedObligationIds为累计受理集合，settled为当前已结清集合且settled⊆accepted；同事项可同时存在ACCEPTED与SETTLED关系。当前accepted-active=accepted−settled，unresolved单独表示。只有原请求允许的固定子份额可部分接受，不得同key扩大范围。[M:463]

### SPEC04 解决、忽略和恢复可见

| ResolveKind | 必须满足 |
|---|---|
| GAP_ELIMINATED | CURRENT、完整NO_ACTIVE_GAP、short0/late0，全部必要执行责任已结清或有权证明独立；草稿PR、未来供给、备注不算 |
| RESPONSIBILITY_RETIRED | 真正源Owner准确范围终止，全部旧路径/在途有完整证明，保留共享范围外责任 |
| INDEPENDENT_RESIDUAL_ACCEPTED | 必要业务风险已闭，真实有权Owner接受准确残余后果；不能把仍缺料或可能继续执行转成普通人工待办绕过 |

Resolve最终重新核原slot、ExpectedCase完整组合、current和义务，在一次UoW写Resolution/Evidence/Closure/Revision/Head/原receipt/audit/outbox。提交前变化412；合法关闭后的真实更正产生新Episode REVIEW_REQUIRED/VISIBLE，原Closure保留。[M:284] [M:286]

Dismiss只改变告警轴。必须有真实AlertPolicy、允许理由、知识状态及serverNow之后且不超过maxDismissUntil的时间；缺policy不能默认允许。Restore追加决定，不删除原Dismissal。到期或相关源变化时读取/动作即派生VISIBLE，无需等待定时器；随后CAS持久化一次到期决定。它们都不写ResolvedAt、不消执行义务。[M:302] [M:823]

### SPEC05 重算衔接

`NEW_FACTS`固定影响范围和新事实，先保存Work+ordinal0Step+原frozenrequest+checkpoint+Outbox+202；此时Run/InputSet为null。MRP真实准备后再依次封装输入、请求Run、创建Attempt、单Attempt完整封存、选择、发布。每阶段仅出现真正已有的引用，未来层保持null。[M:314] [M:469]

`SAME_INPUT_RECOVERY`仅用于原InputSet/算法不变的技术恢复，必须由MRP真实证明旧generation不能选择/发布，不以heartbeat消失代隔离。输入变化走NEW_FACTS。EXC新的协调意图引用原MRP recoverySlot/body，不自主新建Run。[M:314] [M:339]

MRP真实PUBLISHED回执由M03一次消费到EXC本地Assessment/Head；MRP已发布而EXC失败保WAITING_LOCAL_APPLY，只修本地，不再Publish。新解释无缺口也不自动关闭，仍走EX12。Suggestions空不等于责任/ResultManifest空；旧有效结果在新Run失败时继续保留。[M:314] [M:485] [M:597]

## 5. 十项新专业合同与消费边界

| 合同 | 真正producer/义务 | 未采用或未知的出口 |
|---|---|---|
| PC01 | SUP完整来源、份额、阶段、资格/保护、RequiredOwnerRead、当前前沿 | DIRECT_SOURCE_REQUIRED_FENCED，仍可展示partial诊断 |
| PC02 | MRP完整Run/InputSet/ResultSet/Publication和影响关系读取 | 同上，不能伪Run |
| PC03 | POL继承AlertPolicy、日历/精度/理由必要性及current | ALERT_POLICY_REQUIRED_FENCED |
| PC04 | MRP新/同输入协调，完整stage/slot/单Attempt/发布winner | MRP_COORDINATION_REQUIRED_FENCED |
| PC05 | 每个实际责任Owner准确义务受理、独立残余、真实settlement | OWNER_DISPOSITION_REQUIRED_FENCED |
| PC06 | 同一Owner的原slot纯query，原immutable结果与current分开 | UNKNOWN、canInferNoEffect=false；不query-by-create |
| PC07 | 各source Owner独立current、schema/body/pin/audience/provenance | SOURCE_CURRENT_GUARANTEE_UNKNOWN |
| PC08 | Core/IAM fresh精确目的/对象/字段SDK；EXC有界cursor issuer独立资格 | CORE_AUTHORIZATION_REQUIRED_FENCED或PROVIDER_REQUIRED_FENCED |
| PC09 | Main/Core DB共享UoW/allwriters；完整私有cursor retention/query/binding | mutation按对应fence；纯分页provider缺503，无伪EF或retention已部署 |
| PC10 | Main/legacy/WMS/PUR/MES原legacy identity及逐scope单writer切换 | LEGACY_UNVERIFIED；只保真读 |

方法、闭口根和原责任详见主文§13及准确附件。PC06 FOUND必须带完整原native或本协调receipt/trust；NOT_OBSERVED/PENDING/UNAVAILABLE均不能签永久无效果。永久no-effect需要原Owner另有allpaths、future creation fence和slot closure，不从PO四proof直接迁入EXC。[M:446] [M:465]

97叶=原69ID+CP03新增26+CP04新增2；十proposal保各role的完整codec/producer范围。相同codec名称、传输者MRP/SUP或邻册已接受，不让其签PLM/CFG/ENG/DEV/REL/QA/UOM的truth；actual pin和组织生产授权另行落地。[M:789] [S:39]

## 6. 事务、锁、幂等与恢复

选择方案是单Main EXC store、单connection外层SERIALIZABLE事务及E/T排他storeGate，所有source/current/权限观察/command/receipt/expiry/resume/repair/cutover writer共用。它有scope内串行吞吐成本；纯读用MVCC/fixedReadCut，不假称持mutation gate。Owner外部HTTP/SDK、文件和队列等待均在锁外。[M:537]

锁序：source stream/完整key桶/事实/current(10)→WriterHead/lease及权限资格(20)→Case/Episode/Revision(30)→Graph/Assessment/Impact/份额/义务(31)→Work/Step/checkpoint/projection/alert(32)→Operation/slot/Inbox(40)→Receipt/Audit/Outbox(50)。同类按完整规范键排序，空范围也锁；需扩展资源集合则回滚后按完整新集合重开。[M:539]

新命令在第一笔domain write前重新核writer holder/epoch/RV/有效期、fresh权限、原slot、完整Expected tuple及实际依赖CurrentManifest集合。既有同body原receipt在fresh读权下原样重放，不重新执行或拿今天新资格否认历史结果；同槽异body409保原并记冲突审计。[M:543]

本域成功必须在outercommit `CONFIRMED_COMMITTED`后发送；UNKNOWN返回503+原operation/key，只查原。Operation/slot/Work/ordinal0Step先登记，Receipt/Checkpoint插入后同事务CAS回绑nullable循环关系；SourceArtifact/Fact/Provenance/Inbox先于receipt关联。不得提交dangling FK。[M:525] [M:548]

各TX只写EXC自己的完整关系与结果，不写Owner业务集合。MRP/Owner已成功不因本地回滚撤销；接收事实后本地复杂投影待办必须有durable WAITING_LOCAL_APPLY/dependency/WakeOutbox，不能只永久回复“已收到”。[M:550] [M:569]

首次发送前保存MAY_HAVE_BEEN_SENT与原body/context后释放锁。重启时连NOT_SENT也先原query；只有最新durable absence、无更晚观察、原NOT_SENT、实际allwriters无法另发且fresh原资格仍真，才首次发送同body。任何maybeSent遇absence仍未知，不能换key重建或释放。[M:573] [M:575]

worker lease只竞争处理权，过期不证明业务无效果。每笔domain write校store WriterHead和claimEpoch；takeover不改未发生业务后继的历史CaseHead。冷扫描REGISTERED/WAITING/UNKNOWN及Inbox前驱待办；wake丢失仍可DB扫描。缺前驱不推进AppliedSequence，受信fullsnapshot须完整replacement proof。[M:577] [M:581]

## 7. 权限、分页、公开字段与导出

E/T/actor来自可信request context，public body不得自带issuer/grant/admission/writer holder或canResolve布尔。strict JSON先拒重复属性，再核unknown/required/null/format；opaque键不trim、折叠大小写或Unicode归一化。requestDigest是完整public body的RFC8785 JCS SHA；原Owner摘要保原域。[M:345] [M:440]

父Case可读不授source/impact/history/export/basis；数量KNOWN但basis禁读时仍保数量、basis整个REDACTED。必要分组身份不可读则整个KPI groups集合REDACTED，不能按可见子集重算出泄漏隐含分组的“总计”。原artifact必须whole-value获准才返原bytes。[M:771] [M:775]

EX02仍允许1..8192 ASCII cursor、完整原filter/site域及1..1MiB UTF8 OpaqueKey。公开JWS仅六项：schema/issuerOwner/protocolId/endpointId/bindingId/bindingDigest；filter、principal、SDK、mask/seq、排序末元组、cut、expiry留不可变私有query/binding。通用token上界6236<8192，不能用小示例长度代全域证明。[M:211] [M:426]

首/续页先确认完整query/binding/原bytes/artifact/cut retention READY，再签发handle；handle本身不授权。每页fresh权限→合格签名/pin→完整private LOOKUP→原principal/scope/normalized filter→权限head/绝对expiry；相同query/cut和首个expiresAt，不滑TTL。UNKNOWN provider503，合格MISSING/RECLAIMED或到期410，wrongprincipal/撤权403，filter差异422，仍获目的但SDKhead/seq/mask变化412。失败无rows/totals/advance或新业务slot。[M:211] [M:430]

到期例的LOOKUP在18:25:50本次仍合格至18:26:50，但原query/retention expires18:25:50，等号即410；新资格不延原窗口。文档公开RFC8032 TEST1材料必须拒绝用于生产，原记录没有真实签发/数学验签运行证据。[M:864] [M:898]

导出bundle由E/T+UUID字节排序后的distinct CaseIds决定，排除actor/Episode/cut/columns；prepare固定同cut各Case/Episode/Revision/Assessment及fresh字段权。稳定bundle同key改变列或Episode应409，逆序相同集合不绕去重。未知生成只QUERY_FILE_CAPTURE，不再生成；到期410/撤权403保原文件和result。上限50,000行/20MiB全拒、不截断、不永久公开URL。[M:827] [M:829]

## 8. 页面、API与错误恢复

八页为清单、详情、来源、影响、解决/忽略、重算、历史恢复、旧缺料保真页。筛选默认OPEN/IN_PROGRESS/REVIEW_REQUIRED与VISIBLE；源Owner/key匹配CaseOrigin，不把相关祖先当本Case。数量排序要求相同UOM及完整字段权。提交保留用户输入但重新读取冲突，不自动替换expected后重发。[M:149] [M:168] [M:205]

公共基址 `/api/plan/exc/v1`，JSON读取no-store；EX02虽POST是纯query，EX09才创建解释snapshot。36个入口的已读明细见 [API、错误和状态表](../contracts/PLAN-EXC-01-API.md)，下表按功能合并；路径ID/body identity不等400。[M:345] [M:347] [M:381]

| API | method/路径及作用 |
|---|---|
| EX01/02/03 | GET context；POST cases/search；GET cases/{caseId} |
| EX04/05/06 | GET cases/{caseId}/sources；GET cases/{caseId}/impacts/{impactId}；GET cases/{caseId}/history |
| EX07/08 | GET operations/{operationId}；GET cases/{caseId}/recovery/{operationId} |
| EX09/10/11 | POST cases/{caseId}/assessments、/impacts、/owner-dispositions |
| EX12/13/14 | POST cases/{caseId}/resolutions、/dismissals、/alert-restorations |
| EX15/16/17 | POST cases/{caseId}/recalculations；POST operations/{operationId}/resumptions；POST cases/{caseId}/projection-repairs |
| EX18/19/20 | POST exports；GET exports/{exportId}及/content；GET source-artifacts/{artifactId}及/content |
| EX21/22 | GET legacy/{legacyId}；POST legacy/search |
| 准备/原槽 | POST export-scopes；GET cases/{caseId}/action-preparation；GET operation-slots；GET export-operation-slots及/result |
| 图分页 | GET cases/{caseId}/impacts/{impactId}/nodes及/edges |
| M01–05 | POST source-events、disposition-receipts、mrp-receipts、current-observations；GET ingress/{issuerOwner}/{eventId} |

EX04固定graph的所选section仅本页成员、其它数组[]，coverage=LOCAL_PAGE；服务端原sealed graph仍完整。不得把本页当完整InputSet，图过大按固定artifact/同id-version-digest分页。[M:379]

CommandReceipt不可变，首次登记202、即时应用201；同body重放HTTP200+`Idempotency-Replayed:true`头，原body内recordedStatus/time/resultDigest不改。动态OperationView单独展示当前进度，不能把原202改为成功最终态。Export原结果读须fresh export+original-slot及全部原成员/字段权，source-artifact权限不能替代。[M:401] [M:491] [M:811]

错误分层：400输入格式/未知字段；403目的或字段拒绝；404无对象权与不存在统一且不证明Owner无效果；409原槽异body/源或winner冲突；410固定读资源过期；412已读前驱/current/epoch变化；422业务闭包/量义/恢复输入不合法；503资格未证/Owner不可用/本地commit未知；504Owner timeout回原query。登记前缺资格operationId=null且NONE，不制造业务号。PARTIAL/UNKNOWN是合法读结果，不一律422。[M:403] [M:420] [M:422]

## 9. 开发顺序与legacy切换

1. 实现精确codec/opaque键/数量域和事实、原件、当前头、完整关系；先确认97叶与十PC的本次准确资格，不能借相邻PO采用。
2. 在所有EXC writers共享边界下建立immutable事实/graph与固定读cut，完成有权清单/来源/legacy只读及错误区分。
3. 打通真实Owner纯查询、持久协调、原slot/result/Cold恢复，再做assessment、impact、handoff；所有未知保持原责任保护。
4. 实现Resolve与独立Alert轴、MRP各阶段接收/单次消费、导出和投影修复；补齐全writer/current竞争与崩溃窗口验证。
5. 按scope核旧writer/原pending/identity mapping，新链只有真实allwriters epoch/单事实writer成立后才启用。[M:621] [M:627]

旧WMSMaterialShortage的OPEN/RESOLVED/DISMISSED保原义。旧Available0只表示旧算法未找到一条满足need的Stock行，不证明全库零可用；可显示reportedDifference25，但正式knownShortage仍null至完整SUP scope成立。旧PR SourceRefNo/同WO同料是历史锚，PR草稿30不是confirmedCoverage30；Notifier不替真实Owner接受。[M:623] [M:625]

本次整理未对现有代码做全链实现比对，旧Main证据commit不证明当前仓库实现。迁移不得伪造历史Run/QC/Firm/Converted/winner，不删除真实Owner结果或重发旧业务。本册给编码依据和采用门，归档原件中的“执行/发布/计分”语句不构成本次用户指令。[M:627] [S:39]

## 10. 103项验收与必要场景

| SPEC | 原AC编号 | 数量 |
|---|---|---:|
| 01 清单 | 001–014、081–090、100 | 25 |
| 02 来源 | 015–028、091–092、098–099 | 18 |
| 03 影响 | 029–042、097 | 15 |
| 04 解决/忽略 | 043–060、094 | 19 |
| 05 重算 | 061–080、093、095–096、101–103 | 26 |

103项条件、步骤、断言和完整Problem已逐项实读并转排为 [验收开发阅读版](../evidence/PLAN-EXC-01-AC-guide.md)；引用的当前native/private/UoW语义与存储记录已在下列专册展开。编号与原文在当前 `EXC_NEW_AC_CATALOGUE.json`；原80项导航已由明确勘误覆盖，不新造第104项。[K:1] [M:632]

编码时优先保这些区分：完整缺20与必要来源未知null；TRIAL-only明确不适用与ordinary-use来源未知；自有RSV与已抵NET；A/B共享保护和三路径控制；Resolve与current竞争；关闭后新Episode；原body重放/异body冲突/撤权不重执行；commit未知与Owner成功回执丢失；单Attempt全结果与空Suggestions仍有责任；MRP发布成功而EXC仅修投影；旧generation晚到仅diagnostic；多Case固定导出、独立字段mask与cursor到期。[M:638] [M:644] [M:723]

所有103用例actualRunId/actualEnvironment/actualResult未形成真实执行证据。本次链接/结构/身份验证只能证明文档整理结果，不替代业务测试、DB allwriters、retention或issuer验签。[S:39]

## 11. 必要附件、未读范围与整合问题

已整理417闭口定义、87关系表/精确FK/UQ/CHECK与15可变读回域、36 API全部参数错误、97叶/十专业提案、完整103用例、披露政策、6.0 cursor/private retention、633原生事实、292事务包及16,679存储记录的必要业务语义。完整字段采用实读、精确对象复用和差量阅读，身份/hash实例另做结构核对。全部351成员已按唯一目录完整取出并分别登记当前/历史用途；详见[附件选择与历史边界](../contracts/PLAN-EXC-01-附件选择与历史边界.md)，不把历史作者工具算作产品实现。

局部文书勘误：AC087步骤1仍写readcut22，而同项当前specificConditions.fixedReadCut和当前主文/独审均为113。按精确输入及packet的113实施，旧文字保留追溯，见验收阅读版和PLAN-IF-003。

当前集成重点有三项：[M:444] [M:791] [M:963]

- EXC PC01/02/03保明确required-fenced资格；需把实际SUP/MRP/POL接口与本册完整字段、current保证、作用域逐项绑定。PO CP22已有专业方案不能自动满足。
- MAT/MRP/EXC须统一gross/offset/NET_RESIDUAL的映射、原父建议显式版本与两Case责任。相关已知跨册问题见PLAN-MRP及根规划finding账；不把源主文存在的RemainingBasis误报“没有接口”。
- CP12静态新Case/SourceFact/Key链不是生产迁移方案；真实旧对象若已存在，实施时需另留合法迁移与兼容证据，不能覆盖immutable原行。

历史原件、已撤回评审、辅助回执缺失、历史正文未重读、运行采用未证分别登记。PLAN-IF-006的数量类型域、PLAN-IF-007的Receipt20时间和PLAN-IF-008的材料算法引用仍需相应Owner澄清，未静默修改原件或声称出现真实运行故障。

## 12. 来源与阅读覆盖

主文M全文1–973、最终S/A、R独审全文1–427、I及E/K均已读。当前接受/支持依赖/归档E09治理字段以精确对象复用补齐；治理复核自身文字与136专业scope/251codec身份矩阵分别处理，未把作者阅读声明继承为本轮全文。当前类型、源对象、Owner、UoW、记录、专业补充及索引分别有专门阅读证据。详细范围以 [根规划阅读账](../evidence/root-planning-reading.json)、[组合成员账](../evidence/root-plan-exc-current-members.json) 和[补充阅读账](../evidence/PLAN-EXC-01-supplement-and-member-reading.json)为准。

归档中旧作者PENDING标签与后继接受并存时按准确记录时间和对象解释。原件字节不改；后续实施须使用当前准确契约，补齐实际采用和运行证据。

[A:5]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/94/945e7b3535b3eb39__UA-20261006-C-PLAN-EXC-CP12.json:5>
[A:7]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/94/945e7b3535b3eb39__UA-20261006-C-PLAN-EXC-CP12.json:7>
[E:1]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d6/d6351317674a6c6b__CP12_B_PC09_A_DEPENDENCY_STATUS_ERRATUM_20261006.txt:1>
[I:8]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/1a/1a2b16fcf2cfe39d__C-PLAN-EXC-MD-SPEC-01_INDEX.md:8>
[K:1]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/a6/a659d25c3b90f609__PLAN_EXC_CP12_AC_SCOPE_METADATA_CLARIFICATION_20261006.txt:1>
[M:19]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/77/77b33224eaeb51c0__PLAN-EXC-01_缺料与供给异常处置_前后端开发Spec_v1.11_CP12_REVIEW_COPY.md:19>
[M:87]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/77/77b33224eaeb51c0__PLAN-EXC-01_缺料与供给异常处置_前后端开发Spec_v1.11_CP12_REVIEW_COPY.md:87>
[M:106]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/77/77b33224eaeb51c0__PLAN-EXC-01_缺料与供给异常处置_前后端开发Spec_v1.11_CP12_REVIEW_COPY.md:106>
[M:121]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/77/77b33224eaeb51c0__PLAN-EXC-01_缺料与供给异常处置_前后端开发Spec_v1.11_CP12_REVIEW_COPY.md:121>
[M:127]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/77/77b33224eaeb51c0__PLAN-EXC-01_缺料与供给异常处置_前后端开发Spec_v1.11_CP12_REVIEW_COPY.md:127>
[M:149]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/77/77b33224eaeb51c0__PLAN-EXC-01_缺料与供给异常处置_前后端开发Spec_v1.11_CP12_REVIEW_COPY.md:149>
[M:168]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/77/77b33224eaeb51c0__PLAN-EXC-01_缺料与供给异常处置_前后端开发Spec_v1.11_CP12_REVIEW_COPY.md:168>
[M:205]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/77/77b33224eaeb51c0__PLAN-EXC-01_缺料与供给异常处置_前后端开发Spec_v1.11_CP12_REVIEW_COPY.md:205>
[M:211]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/77/77b33224eaeb51c0__PLAN-EXC-01_缺料与供给异常处置_前后端开发Spec_v1.11_CP12_REVIEW_COPY.md:211>
[M:225]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/77/77b33224eaeb51c0__PLAN-EXC-01_缺料与供给异常处置_前后端开发Spec_v1.11_CP12_REVIEW_COPY.md:225>
[M:239]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/77/77b33224eaeb51c0__PLAN-EXC-01_缺料与供给异常处置_前后端开发Spec_v1.11_CP12_REVIEW_COPY.md:239>
[M:265]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/77/77b33224eaeb51c0__PLAN-EXC-01_缺料与供给异常处置_前后端开发Spec_v1.11_CP12_REVIEW_COPY.md:265>
[M:284]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/77/77b33224eaeb51c0__PLAN-EXC-01_缺料与供给异常处置_前后端开发Spec_v1.11_CP12_REVIEW_COPY.md:284>
[M:286]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/77/77b33224eaeb51c0__PLAN-EXC-01_缺料与供给异常处置_前后端开发Spec_v1.11_CP12_REVIEW_COPY.md:286>
[M:302]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/77/77b33224eaeb51c0__PLAN-EXC-01_缺料与供给异常处置_前后端开发Spec_v1.11_CP12_REVIEW_COPY.md:302>
[M:314]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/77/77b33224eaeb51c0__PLAN-EXC-01_缺料与供给异常处置_前后端开发Spec_v1.11_CP12_REVIEW_COPY.md:314>
[M:339]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/77/77b33224eaeb51c0__PLAN-EXC-01_缺料与供给异常处置_前后端开发Spec_v1.11_CP12_REVIEW_COPY.md:339>
[M:345]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/77/77b33224eaeb51c0__PLAN-EXC-01_缺料与供给异常处置_前后端开发Spec_v1.11_CP12_REVIEW_COPY.md:345>
[M:347]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/77/77b33224eaeb51c0__PLAN-EXC-01_缺料与供给异常处置_前后端开发Spec_v1.11_CP12_REVIEW_COPY.md:347>
[M:379]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/77/77b33224eaeb51c0__PLAN-EXC-01_缺料与供给异常处置_前后端开发Spec_v1.11_CP12_REVIEW_COPY.md:379>
[M:381]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/77/77b33224eaeb51c0__PLAN-EXC-01_缺料与供给异常处置_前后端开发Spec_v1.11_CP12_REVIEW_COPY.md:381>
[M:401]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/77/77b33224eaeb51c0__PLAN-EXC-01_缺料与供给异常处置_前后端开发Spec_v1.11_CP12_REVIEW_COPY.md:401>
[M:403]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/77/77b33224eaeb51c0__PLAN-EXC-01_缺料与供给异常处置_前后端开发Spec_v1.11_CP12_REVIEW_COPY.md:403>
[M:420]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/77/77b33224eaeb51c0__PLAN-EXC-01_缺料与供给异常处置_前后端开发Spec_v1.11_CP12_REVIEW_COPY.md:420>
[M:422]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/77/77b33224eaeb51c0__PLAN-EXC-01_缺料与供给异常处置_前后端开发Spec_v1.11_CP12_REVIEW_COPY.md:422>
[M:426]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/77/77b33224eaeb51c0__PLAN-EXC-01_缺料与供给异常处置_前后端开发Spec_v1.11_CP12_REVIEW_COPY.md:426>
[M:430]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/77/77b33224eaeb51c0__PLAN-EXC-01_缺料与供给异常处置_前后端开发Spec_v1.11_CP12_REVIEW_COPY.md:430>
[M:438]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/77/77b33224eaeb51c0__PLAN-EXC-01_缺料与供给异常处置_前后端开发Spec_v1.11_CP12_REVIEW_COPY.md:438>
[M:440]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/77/77b33224eaeb51c0__PLAN-EXC-01_缺料与供给异常处置_前后端开发Spec_v1.11_CP12_REVIEW_COPY.md:440>
[M:442]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/77/77b33224eaeb51c0__PLAN-EXC-01_缺料与供给异常处置_前后端开发Spec_v1.11_CP12_REVIEW_COPY.md:442>
[M:444]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/77/77b33224eaeb51c0__PLAN-EXC-01_缺料与供给异常处置_前后端开发Spec_v1.11_CP12_REVIEW_COPY.md:444>
[M:446]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/77/77b33224eaeb51c0__PLAN-EXC-01_缺料与供给异常处置_前后端开发Spec_v1.11_CP12_REVIEW_COPY.md:446>
[M:463]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/77/77b33224eaeb51c0__PLAN-EXC-01_缺料与供给异常处置_前后端开发Spec_v1.11_CP12_REVIEW_COPY.md:463>
[M:465]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/77/77b33224eaeb51c0__PLAN-EXC-01_缺料与供给异常处置_前后端开发Spec_v1.11_CP12_REVIEW_COPY.md:465>
[M:469]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/77/77b33224eaeb51c0__PLAN-EXC-01_缺料与供给异常处置_前后端开发Spec_v1.11_CP12_REVIEW_COPY.md:469>
[M:485]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/77/77b33224eaeb51c0__PLAN-EXC-01_缺料与供给异常处置_前后端开发Spec_v1.11_CP12_REVIEW_COPY.md:485>
[M:491]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/77/77b33224eaeb51c0__PLAN-EXC-01_缺料与供给异常处置_前后端开发Spec_v1.11_CP12_REVIEW_COPY.md:491>
[M:499]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/77/77b33224eaeb51c0__PLAN-EXC-01_缺料与供给异常处置_前后端开发Spec_v1.11_CP12_REVIEW_COPY.md:499>
[M:503]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/77/77b33224eaeb51c0__PLAN-EXC-01_缺料与供给异常处置_前后端开发Spec_v1.11_CP12_REVIEW_COPY.md:503>
[M:505]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/77/77b33224eaeb51c0__PLAN-EXC-01_缺料与供给异常处置_前后端开发Spec_v1.11_CP12_REVIEW_COPY.md:505>
[M:509]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/77/77b33224eaeb51c0__PLAN-EXC-01_缺料与供给异常处置_前后端开发Spec_v1.11_CP12_REVIEW_COPY.md:509>
[M:525]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/77/77b33224eaeb51c0__PLAN-EXC-01_缺料与供给异常处置_前后端开发Spec_v1.11_CP12_REVIEW_COPY.md:525>
[M:531]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/77/77b33224eaeb51c0__PLAN-EXC-01_缺料与供给异常处置_前后端开发Spec_v1.11_CP12_REVIEW_COPY.md:531>
[M:537]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/77/77b33224eaeb51c0__PLAN-EXC-01_缺料与供给异常处置_前后端开发Spec_v1.11_CP12_REVIEW_COPY.md:537>
[M:539]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/77/77b33224eaeb51c0__PLAN-EXC-01_缺料与供给异常处置_前后端开发Spec_v1.11_CP12_REVIEW_COPY.md:539>
[M:543]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/77/77b33224eaeb51c0__PLAN-EXC-01_缺料与供给异常处置_前后端开发Spec_v1.11_CP12_REVIEW_COPY.md:543>
[M:548]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/77/77b33224eaeb51c0__PLAN-EXC-01_缺料与供给异常处置_前后端开发Spec_v1.11_CP12_REVIEW_COPY.md:548>
[M:550]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/77/77b33224eaeb51c0__PLAN-EXC-01_缺料与供给异常处置_前后端开发Spec_v1.11_CP12_REVIEW_COPY.md:550>
[M:569]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/77/77b33224eaeb51c0__PLAN-EXC-01_缺料与供给异常处置_前后端开发Spec_v1.11_CP12_REVIEW_COPY.md:569>
[M:573]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/77/77b33224eaeb51c0__PLAN-EXC-01_缺料与供给异常处置_前后端开发Spec_v1.11_CP12_REVIEW_COPY.md:573>
[M:575]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/77/77b33224eaeb51c0__PLAN-EXC-01_缺料与供给异常处置_前后端开发Spec_v1.11_CP12_REVIEW_COPY.md:575>
[M:577]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/77/77b33224eaeb51c0__PLAN-EXC-01_缺料与供给异常处置_前后端开发Spec_v1.11_CP12_REVIEW_COPY.md:577>
[M:581]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/77/77b33224eaeb51c0__PLAN-EXC-01_缺料与供给异常处置_前后端开发Spec_v1.11_CP12_REVIEW_COPY.md:581>
[M:597]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/77/77b33224eaeb51c0__PLAN-EXC-01_缺料与供给异常处置_前后端开发Spec_v1.11_CP12_REVIEW_COPY.md:597>
[M:621]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/77/77b33224eaeb51c0__PLAN-EXC-01_缺料与供给异常处置_前后端开发Spec_v1.11_CP12_REVIEW_COPY.md:621>
[M:623]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/77/77b33224eaeb51c0__PLAN-EXC-01_缺料与供给异常处置_前后端开发Spec_v1.11_CP12_REVIEW_COPY.md:623>
[M:625]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/77/77b33224eaeb51c0__PLAN-EXC-01_缺料与供给异常处置_前后端开发Spec_v1.11_CP12_REVIEW_COPY.md:625>
[M:627]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/77/77b33224eaeb51c0__PLAN-EXC-01_缺料与供给异常处置_前后端开发Spec_v1.11_CP12_REVIEW_COPY.md:627>
[M:632]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/77/77b33224eaeb51c0__PLAN-EXC-01_缺料与供给异常处置_前后端开发Spec_v1.11_CP12_REVIEW_COPY.md:632>
[M:638]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/77/77b33224eaeb51c0__PLAN-EXC-01_缺料与供给异常处置_前后端开发Spec_v1.11_CP12_REVIEW_COPY.md:638>
[M:644]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/77/77b33224eaeb51c0__PLAN-EXC-01_缺料与供给异常处置_前后端开发Spec_v1.11_CP12_REVIEW_COPY.md:644>
[M:723]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/77/77b33224eaeb51c0__PLAN-EXC-01_缺料与供给异常处置_前后端开发Spec_v1.11_CP12_REVIEW_COPY.md:723>
[M:771]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/77/77b33224eaeb51c0__PLAN-EXC-01_缺料与供给异常处置_前后端开发Spec_v1.11_CP12_REVIEW_COPY.md:771>
[M:775]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/77/77b33224eaeb51c0__PLAN-EXC-01_缺料与供给异常处置_前后端开发Spec_v1.11_CP12_REVIEW_COPY.md:775>
[M:781]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/77/77b33224eaeb51c0__PLAN-EXC-01_缺料与供给异常处置_前后端开发Spec_v1.11_CP12_REVIEW_COPY.md:781>
[M:783]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/77/77b33224eaeb51c0__PLAN-EXC-01_缺料与供给异常处置_前后端开发Spec_v1.11_CP12_REVIEW_COPY.md:783>
[M:789]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/77/77b33224eaeb51c0__PLAN-EXC-01_缺料与供给异常处置_前后端开发Spec_v1.11_CP12_REVIEW_COPY.md:789>
[M:791]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/77/77b33224eaeb51c0__PLAN-EXC-01_缺料与供给异常处置_前后端开发Spec_v1.11_CP12_REVIEW_COPY.md:791>
[M:811]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/77/77b33224eaeb51c0__PLAN-EXC-01_缺料与供给异常处置_前后端开发Spec_v1.11_CP12_REVIEW_COPY.md:811>
[M:823]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/77/77b33224eaeb51c0__PLAN-EXC-01_缺料与供给异常处置_前后端开发Spec_v1.11_CP12_REVIEW_COPY.md:823>
[M:827]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/77/77b33224eaeb51c0__PLAN-EXC-01_缺料与供给异常处置_前后端开发Spec_v1.11_CP12_REVIEW_COPY.md:827>
[M:829]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/77/77b33224eaeb51c0__PLAN-EXC-01_缺料与供给异常处置_前后端开发Spec_v1.11_CP12_REVIEW_COPY.md:829>
[M:835]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/77/77b33224eaeb51c0__PLAN-EXC-01_缺料与供给异常处置_前后端开发Spec_v1.11_CP12_REVIEW_COPY.md:835>
[M:864]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/77/77b33224eaeb51c0__PLAN-EXC-01_缺料与供给异常处置_前后端开发Spec_v1.11_CP12_REVIEW_COPY.md:864>
[M:898]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/77/77b33224eaeb51c0__PLAN-EXC-01_缺料与供给异常处置_前后端开发Spec_v1.11_CP12_REVIEW_COPY.md:898>
[M:940]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/77/77b33224eaeb51c0__PLAN-EXC-01_缺料与供给异常处置_前后端开发Spec_v1.11_CP12_REVIEW_COPY.md:940>
[M:944]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/77/77b33224eaeb51c0__PLAN-EXC-01_缺料与供给异常处置_前后端开发Spec_v1.11_CP12_REVIEW_COPY.md:944>
[M:957]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/77/77b33224eaeb51c0__PLAN-EXC-01_缺料与供给异常处置_前后端开发Spec_v1.11_CP12_REVIEW_COPY.md:957>
[M:963]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/77/77b33224eaeb51c0__PLAN-EXC-01_缺料与供给异常处置_前后端开发Spec_v1.11_CP12_REVIEW_COPY.md:963>
[R:10]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/9e/9e0c8873c03611ac__PLAN_EXC_CP12_INDEPENDENT_WHOLE_BOOK_REVIEW_20261006.txt:10>
[R:103]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/9e/9e0c8873c03611ac__PLAN_EXC_CP12_INDEPENDENT_WHOLE_BOOK_REVIEW_20261006.txt:103>
[S:5]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/38/38454a8ff63983ff__SR-20261006-C-PLAN-EXC-CP12-FINAL.json:5>
[S:28]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/38/38454a8ff63983ff__SR-20261006-C-PLAN-EXC-CP12-FINAL.json:28>
[S:39]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/38/38454a8ff63983ff__SR-20261006-C-PLAN-EXC-CP12-FINAL.json:39>
[U:646]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/ef/ef2087f07e47b1a8__CURRENT.json:646>


## 附件补读：Owner 与公开分页

已将 97 叶义务、10 提案语义、23 字段披露和 EX02 v6 协议及条件样例整理为 [Owner 与游标开发说明](D:/CP6/docs/CP6_开发设计文档_20261010/contracts/PLAN-EXC-01-Owner与游标.md)。同时使用[数据与类型](../contracts/PLAN-EXC-01-数据与类型.md)、[源事实与公开投影](../contracts/PLAN-EXC-01-源事实与公开投影.md)和[专业源对象补充](../contracts/PLAN-EXC-01-专业源对象补充.md)。


## 附件补读：版本与归档资格

已补读当前接受、原始范围、来源资格与正式更正，见 [版本与归档资格](D:/CP6/docs/CP6_开发设计文档_20261010/contracts/PLAN-EXC-01-版本与归档资格.md)。其中区分历史 OPEN/PENDING、更正后的静态关闭、十份辅助回执缺失和实际运行采用仍未证明。


## 附件补读：本地事务与恢复

已将 65 个业务场景、227 个来源接纳包的共同规则与文档关系整理为 [本地事务与恢复](D:/CP6/docs/CP6_开发设计文档_20261010/contracts/PLAN-EXC-01-本地事务与恢复.md)。底层原始行字段与孤立反例使用下一专册；文档内部前后状态核对不作为数据库事务执行证据。


### 存储记录与隔离反例补册

[存储记录与反例](D:/CP6/docs/CP6_开发设计文档_20261010/contracts/PLAN-EXC-01-存储记录与反例.md)补齐81类表字段、资格正文、原生身份、三种本地应用回执及六个隔离样例；不代表实际业务执行。
