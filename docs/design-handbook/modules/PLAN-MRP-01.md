# PLAN-MRP-01 物料需求运算与重算开发设计

状态：`required_materials_consolidated`。当前登记的22份必读原件已完整实读：R01–R03、必读限定、字段/操作、140条验收、来源/INDEX/TRACE/文档检查及PD01接受。功能文本已认可；特殊模型、真实参数、正式原生画布和运行仍待证。材料阅读闭合不等于实现完成。

## 1. 目的、角色与Owner

计划员从正式需求、准确工程图、策略、已有供给和受保护执行责任形成净需求、计划建议及覆盖关系；发布者把一套完整结果变成Planning有效计划。MRP负责派生账和发布，Demand、工程、QA、采购、MES、WMS分别持原业务事实。它不直接建PR/WO、不预留或扣库存、不消费REL/DEV额度，材料齐备也不证明机器/人员/工装或有限产能可用。[范围](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/24/24141aeae8485be5__MRP-R01.md:26)；[执行边界](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/24/24141aeae8485be5__MRP-R01.md:214)

七原SPEC为范围、需求归集、BOM展开、净需求、批量提前期、运行快照、失败重算。Planner维护准备与后继，Collector/Resolver消费原事实，Calculator计算，Reviewer校验/选择，Publisher发布，RunOperator取消，RecoveryOperator恢复，Exporter导出；这些是逻辑能力名，不是已配置真实账号。[角色](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/23/230559546ec081a0__MRP-R02.md:82)

## 2. 当前版本与接受组合

当前是MRP-R01（`24141aeae8485be5`）及CLARIFICATIONS（`5b624a769d4c88be`）、R02（`230559546ec081a0`）＋FIELDS（`71f92c100f515e11`）＋OPERATIONS-ERRORS（`30f1e68ccfc0f571`）＋SOURCES-REVIEW的C-R02-01–04（`d466fe5b5daba07b`），再由R03（`14e648e1342492b0`）九合同/十二门明确当前规则。三个阶段验收分别42/50/48，原号全保留。

PD01接受叠加认可准确payload commit `482efdf29df81479076c0aeb2d32e3d62d97a7ed` 的R03及继承R01/R02功能文本，阶段80→100；7个Q门、6个MRP OPEN关闭0项。未接受特殊模型已完整、真实Owner证据、正式Excel/原生视觉或实现运行。旧REVIEW标记保留历史，不用进度分冒源码完成率。[接受原件](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d94b4f32aa6bf409__PROGRESS-ACCEPTANCE.json:1)

逻辑Ref/状态/操作不能直接当冻结DTO/表/路由。PLM RC1＋RV01原政策及[POL](D:/CP6/docs/CP6_开发设计文档_20261010/modules/PLAN-POL-01.md)/[SUP](D:/CP6/docs/CP6_开发设计文档_20261010/modules/PLAN-SUP-01.md)组合继续，后继Owner版本需显式采用。全SHA与所有载体见[必需输入](D:/CP6/docs/CP6_开发设计文档_20261010/indexes/required-inputs.json)。

## 3. 数据、身份与状态

|对象|必须保留的身份和含义|
|---|---|
|Draft/Capture|DraftRef/EditVersion/保存意图；RequestedScope、实际闭包、Owner读取计划、候选Capture/Version及缺口；可PARTIAL，非正式Run|
|InputSet/Request/Run|SealIntent、固定成员/模型/技术/策略/保护/来源模式、ContentProof；RequestKey绑定模式/Scope/算法/InputSet；Run固定输入，不原地换版|
|Attempt/Control|StartIntent、Run/InputSet/Algorithm、AttemptId、ExecutionAuthorityGeneration、OutputNamespace、OutputSeal；Cancel/Recovery独立意图|
|MaterialObligation|原Firm/WO/MaterialLine/Occurrence/Version/SourceLineage、Item技术SiteUOM、NeedNode、责任及变化来源|
|RemainingBasis/OffsetManifest|GROSS_WITH_EXPLICIT_OFFSETS或NET_RESIDUAL及语义版；逐OffsetId/类别/责任/原事实版/量、包含或阶段关系、资格节点|
|DerivedRequirement|ParentSuggestion/Version/Lot、技术行/Occurrence、ConsumptionActivity、Child技术/量/UOM/NeedNode和推导依据|
|CalculationLedger|相容AccountScope、源贡献、期初与新增供给、保护、净不足/安全目标、批量与盈余、计划余额、真实/到期/未来/未知分量|
|ResultSet/Selection|单个成功有效Attempt的完整封存输出、Report、成员/依赖/净账/建议/Pegging/例外/退出保留清单；SelectionIntent及前驱|
|Publication|FORMAL Run、选定版、全ResultSet、ExactReplacement/ProtectedRetained、ExpectedEffective/Protection、RunControl/代次、外部适用前沿和实际结果|
|Impact/Recovery/Export|准确变化和共享者、历史结果/当前效力/未知、原键与隔离证明、查询制品范围及当前访问权|

原36FLD及所有成员完整定义见[FIELDS](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/71/71f92c100f515e11__FIELDS.md:1)。Ref含Owner/ObjectType/ObjectId/Version/Scope，显示号、摘要不代身份；版本只按同Owner前后继比较。Qty精确十进制+UOM/Basis/状态/原Ref/舍入，UNKNOWN不填0，负量须有权更正。Node保Date或Instant、时区、日历、cutoff、精度、预计/实际/原Ref，日期不能冒午夜时刻。Manifest空成员仅完整声明范围证明下为确知空。

准备、输入、计算、结果完整性、当前适用性、发布和可行性七轴独立：计算QUEUED/CALCULATING/CALCULATED/FAILED/CANCELLED/RECOVERY_REQUIRED；发布NOT_PUBLISHED/PUBLISHING/OUTCOME_UNKNOWN/PUBLISHED/SUPERSEDED。CALCULATED不是结果完整、Published或可执行；历史Attempt失败与后继成功并存，Publication不被取消改成从未发生。[状态轴](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/23/230559546ec081a0__MRP-R02.md:68)

## 4. 范围、算法与正常流程

准备链是OP001草稿→OP003闭包→OP004采集及OP006技术解析→OP005封装→OP002固定请求/Run→OP007 Attempt→OP008校验→OP009选择→OP010比较→OP011发布预检→OP012权威发布。操作编号不是点击先后；各步有独立身份和产物，不用一按钮隐式跑旧RunAsync或删建议。[准备限定](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/23/230559546ec081a0__MRP-R02.md:58)

范围至少含Tenant、计划/供给Site、物料/准确技术或有权等价类、用途/活动、货主/客户限制、标准UOM、精确需用/可用节点、来源选择。同ItemCd不足以共享池。RequestedScope与EffectiveClosure、SharedResources、ProtectedOutside和排除理由分别保存；沿技术下钻材料，还要纳入Firm/WO、预留、未决转换与范围外承诺。A+B中B失败且保护共享未知，不能隐藏B发布A；确证闭包独立的A可新建Run，原A+B仍失败/不全。[范围闭包](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/24/24141aeae8485be5__MRP-R01.md:79)

时间窗明确半开[Start,End)及规则版、时区/日历；逾期未结、窗前材料消耗和窗外共享承诺仍保留，不截到Start或把未来收货挪今天。同日09:00需、17:00到不因日桶相同算按时。扩大窗或改边界是新输入。

需求三类分账：A为正式Demand当前责任，不能另扫Order.Quantity−ShippedQty；B为本Run新MAKE建议派生，输入只固定生成模型，尚未算出的B不伪填0；C为已有Firm/WO未满足材料，须原材料Owner真实账和映射。合法不同责任即使同客户同量同日也不能去重；同一责任不同版只当前有效份额。预测、服务、维修、专用安全Demand缺合同则保留受阻范围，不能暗过滤。FLOOR是余额目标不另造Demand。[三类需求](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/24/24141aeae8485be5__MRP-R01.md:103)

材料剩余两模式必须Owner声明：NET_RESIDUAL已经抵扣指定集合，既要从再次可抵池排除该集合，又保留其中仍真实占用的保护；GROSS_WITH_EXPLICIT_OFFSETS仅在相交/互斥、单位和Owner规则可证时算派生剩余，不回写原主账。Issued/LineSide/Reserved不是天然互斥桶。净剩余40已计入20、新派生180、另自由50不含20→220−50=170，不能再扣20成150，也不能把20供他人使用。已抵20中Hold10使依据失效，未获Owner后继不自改40；有权新剩50后才新算50+180−50=180，旧170留历史。Firm材料80已承接WO50/原余30→50+30，不80+50；未知承接不当未转重造。[材料CON002](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/14/14e648e1342492b0__MRP-R03.md:75)

技术先解析准确Baseline/BOM/Route/Output/活动/生效/方向选择；B可替A不推反向或传递兼容。多个合法路径无决定待CHOICE_UNRESOLVED。只去同技术行重复读取，不去同材料不同工序Occurrence。当前有效图循环/缺边受阻，无关历史循环不阻独立图。普通MAKE先父净不足→合法批量→逐批派生；已有成品不再爆料，旧WO沿材料剩余合同；BUY停止制造BOM展开。共享子料收齐相容父贡献和旧责任后统一净算，不沿每父分支各扣完整库存。[技术图](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/24/24141aeae8485be5__MRP-R01.md:127)

例：成品需100/存30/新MAKE倍20→净70/建议80，每件X3→X240；另父B新20每件X2→再40，共X280只扣一次共享库存50→净230。已有合格WO20时成品新净50/建议60/X180，旧WO剩X40另计，总220再净独立供给，不能重复对WO20爆料。

用量基数必须声明良品或投入、分子分母、固定/比例项、已含因素、发生对象、舍入点及版。目标良品100/投入良率0.8/每投入料2→250；每良品2.5也250，不能再除0.8得312.5。制造80分两批、每件1/每批固定损耗2→84；一批82，两方案总制造80相同但语义不等价。缺良率/损耗不默认1/0。时间倒排跨技术生效边界，旧InputSet不换图；明确有界重解析和稳定选择规则才能新后继，缺上限不虚填。委外/调拨/虚拟件/联产/回用无支持模型须保留受阻，不提前用循环预期副产抵当前投入；已真实合格副产可由SUP另取证。[技术CON003](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/14/14e648e1342492b0__MRP-R03.md:100)

相容且全部必要量/资格/保护可证时，每节点：Pre=Opening+EligibleExistingReceipts−Gross；Net=max(0,Floor−Pre)；Lot=ApprovedLot(Net)；Closing=Pre+Lot，Closing只结转一次。FLOOR与专用安全Demand互斥，NONE要明确批准；Net0不因MOQ造批。计划余额含新建议，不能当ActualStock或按时可执行；WMS Available已扣Allocated，不再扣一次，自有预留只用于自身责任。[净账CON004](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/14/14e648e1342492b0__MRP-R03.md:115)

批量每批同时满足MOQ/倍数/基点/Max/精度/批数及包装保存约束。限定最小总超量例：净65/MOQ30/倍10/Max50→40+30=70，不能50+20；净75/MOQ20/倍10/Max50→50+30=80。较少批次、较大首批只是原例偏好，不变企业通用默认。材料日期按真实消耗工序反推，不统一成品交期。D2缺60、D3原供60，可有新建议补D2并同时保留D3潜在多余风险，不自动取消PO/WO。[批量限定](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/5b/5b624a769d4c88be__CLARIFICATIONS.md:1)

全源确定供80需100可完整缺20并产建议；必要来源未知不能填0算同样20。完整含已知时间/执行限制可以送发布评审且限制随结果传递，未知必要输入/不支持分支不能藏在备注放行。GrossTrace、NetAllocation、RemainderReason、WMSReservationRef分开；盈余及FLOOR补足不是客户Demand。[完整性限定](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d4/d466fe5b5daba07b__SOURCES-REVIEW.md:25)

## 5. Owner接口、结果集与消费

九合同：CON001采集封装请求、002材料义务抵扣、003技术损耗时间、004相容净账批量、005尝试取消恢复、006完整结果确定性、007发布共享前驱、008PLAN-PO/EXC消费影响、009可见性比较导出。均为逻辑合同，尚非可调用URL。[合同目录](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/14/14e648e1342492b0__MRP-R03.md:35)

完整ResultSet包含InputSet、算法策略版、NetLedger、Suggestions/ParentChild、GrossTrace/NetPegging、Covered/Uncovered/Floor/盈余理由、ExceptionSet、ExactReplacement、ProtectedRetained和成员完整性/执行限制。校验数量/行数/hash只是部分证据，须查每条来源去向、全父贡献、互斥供给、损耗/节点/保护。校验报告不等Selection，Selection不等Publication。

ResultSet只取单一有当前权的成功Attempt完整封存产物；两次各缺部分不能拼接。相同InputSet/规则应语义一致，可忽略无业务显示号/生成时间，但批次、损耗、节点、技术、限制、保护、来源不能忽略。非等价SELECTION_CONFLICT调查，不挑少采购/低成本一份。Suggestions=[]不等无责任：需60存60，新建议0但需求/覆盖60/Pegging/保护必须存在。真正空业务结果也须InputSet、准确退出保留、无未决证明和审计；顶层客户需求0不证明Firm材料0。[结果CON006](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/14/14e648e1342492b0__MRP-R03.md:145)

发布后PlanConsumptionEnvelope绑定Publication/ResultSet/Member、量/期/技术/用途、BlockedActions、源/材料派生、可替换状态及真实接收结果。PLAN-PO不能因已发布而Firm/转受阻项；接收不等已建PR/WO。普通建议被Firm/Converted必须使MRP比较的有效/保护前驱变化；无此接线不能安全替换。只减A责任不删服务B的共享单；70已转/50未决继续保护50，不能重建。投递失败补原通知，不重算或转单。[消费CON008](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/14/14e648e1342492b0__MRP-R03.md:176)

后继集成已有更具体设计：[PLAN-PO CP22](PLAN-PO-01.md)对正式MRP intake、完整Publication成员和真实Owner转换结果有17份新专业方案及Core静态采用；[PLAN-EXC CP12](PLAN-EXC-01.md)PC02/04明确来源读取、准备→输入→Run→Attempt→封存→选择→发布各真实回执、原slot及本地单次消费。它们是准确新scope，不能说相邻方案完全缺失，也不把其设计采用写成实际MRP/Owner生产pin已注册。原CON008的责任边界继续适用。[PO当前采用](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/8e/8e03121644c31bba__PLAN_PO_CP22_ROOT_DESIGN_ACCEPTANCE_20261004.txt:14>)；[EXC当前PC04](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/77/77b33224eaeb51c0__PLAN-EXC-01_缺料与供给异常处置_前后端开发Spec_v1.11_CP12_REVIEW_COPY.md:451>)

## 6. 事务、并发、取消和恢复

每个DraftSave/Seal/Request/Start/Selection/Publish/Cancel/Recovery/Export意图独立原key和ContentProof。共同核权→原结果→同Owner前驱/控制→准确输入→本Owner一致提交产物/结果/交接。网络成功、接收、应用、投影、已读各自为事实；无真实对象null加原因，不填成功占位号。[操作总合同](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/30/30f1e68ccfc0f571__OPERATIONS-ERRORS.md:1)

FORMAL封装要实际必要证据；SIMULATION允许明确SCENARIO且模型语义完整，ModelCompleteness与ActualEvidenceCompleteness分开。缺模型不能用任意假数；转正式新意图、真实条件重核，不改旧Mode。Capture可PARTIAL，Seal核ExpectedCapture/Draft绑定及全成员相容；成功InputSet永不补新事实，I2是后继。[来源CON001](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/14/14e648e1342492b0__MRP-R03.md:61)

Attempt的输出接收、OutputSeal、Selection、Publication都校当前ExecutionAuthorityGeneration；只换输出文件夹而选择端不校代次不成立。旧worker晚到可诊断不可覆盖新候选。技术故障同Run/InputSet/算法新隔离Attempt，旧失败留历史；心跳消失、超时、NOT_FOUND或重启不是隔离证明。逻辑取消是不能再形成可选/发布结果，与物理进程停止分开。Cancel与Publish权威唯一胜者：取消先胜阻选择/发布，发布先胜返回ALREADY_PUBLISHED，不删实际结果或撤PO/WO；选定但未发布仍受取消。输入变化新Run，不能用历史输入恢复绕过当前失效。[尝试CON005](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/14/14e648e1342492b0__MRP-R03.md:130)

正式Publish同一权威一致边界比较SelectedResult/SelectionVersion、RunControl/RecoveryGeneration、ExpectedEffectiveSet、ExpectedProtectionSet、精确退出/保留成员及当前权/外部条件。两个Run共享80各拟60不能仅各自RunVersion都通过；共同前驱裁决让后者冲突/重评。旧Suggested已Firm/Converted不能按旧清单删；不得自动把Expected换最新掩盖业务变化。外部保证须明确AuthorityScope/EvidenceAsOfNode/GuaranteedFrontier/ApplicableAction/变化回传和失效处理，本地ProtectionVersion不能证明全部外部当前性；缺保证则阻对应发布，不假全局锁。[发布CON007](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/14/14e648e1342492b0__MRP-R03.md:160)

成功使净账/建议/派生/Pegging/例外/保护引用同EffectiveSet可见，不能先软删旧再逐行补新。读者先固定当前返回的集合ID再读同集合，避免新建议配旧Pegging。未知Publish只查原key，成功只修通知显示，一次NOT_FOUND不能回滚/返保护。之后Hold/改需求保原Publication、另记当前影响；真正晚到事实按实际发生与保证节点判断，不凭消息到达序改历史。

局部重算精确闭包/退出/保留/共享保护/材料接续，Firm/Converted/未决/外部成员不被普通替换；失败旧有效集合仍在但适用性可STALE。协调问题关闭不能抹可能执行的发布/转换/预留未知，独立后果要真实Owner接受。

## 7. 权限、租户与证据可见性

每次读写恢复/导出核可信Tenant/Site/对象/字段/动作；扩计算闭包不扩人的权限，可用有权Owner受控覆盖证明但不泄私有成员。父Run可见不等全部材料/金额可见，差额、总计、隐藏行数、标题/错误/附件都不能泄漏。失权后原成功保留后台审计，只返回当前有权部分，不能再执行业务“找结果”。制品生成后下载再核当前权，旧链接不保永久访问。[CON009](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/14/14e648e1342492b0__MRP-R03.md:192)

## 8. 七页、校验和错误

MRP01范围/模式/时窗/闭包与准备；02三类责任、原Owner水位/抵扣/封装；03准确图/Occurrence/损耗/生效及Attempt；04精确节点净账、实际/计划/到期/未来/未知；05每批量/盈余/日期/材料时间产能分项可行性；06稳定语义差分、退出保留、预检/发布；07原意图树、取消胜者、隔离/恢复/新输入。每页明确Draft/Run/Attempt/InputSet/ResultSet/Publication，不造占位正式号。[七页正文](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/23/230559546ec081a0__MRP-R02.md:90)

修改Site/量/窗/规则/技术令原绑定不适用，旧I1仍保留；页面清空只清未提交本地选择，不删持久Draft。MRP05无直接改正式建议量/期或Firm/转换，走新后继或PLAN-PO。响应未知按钮变“查询原结果”，刷新仍按持久原意图，防抖不代幂等。当前页全选不等全筛选全选，跨页操作需准确成员清单。解释/导出固定身份、范围、单位、日历、水位及完整性；无一致分页协议标局部/非一致全量；安全格式/限额缺失不启用正式导出，公式样式文本不执行也不改原业务值。

28个BR/ERR原号全部保留：001权限、002意图冲突、003闭包、004窗、005来源、006责任、007材料、008计量、009技术、010循环、011父贡献、012损耗、013特殊模型、014阶段、015资格保护、016节点结转、017安全模式、018批量、019日历、020未知、021全结果、022阶段边界、023发布竞争、024未知发布、025取消、026隔离、027精确替换、028可见性制品。多原因数量按真实份额去重，业务拒绝不变“系统错误重试”。[操作和错误详表](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/30/30f1e68ccfc0f571__OPERATIONS-ERRORS.md:1)

## 9. 实施顺序及旧代码

先准确定义范围/责任与原Owner输入，再建立不可变Capture/InputSet和版本图，实现相容节点账与合法批量/逐批派生，随后Attempt代次/封存/唯一选择，再做共同前驱发布/读者一致性，最后PLAN-PO/EXC变化回传与七页。每层先验证无重复或漏责任，再考虑独立分区并行；不默认重写整个仓库。

旧审计固定`157630594e3371fe181955d2f6227ff3b6962c84`：LowLevelCode层级/循环、用量基础、Tenant/RowVersion/权限审计及看板是条件复用候选；GetPolicy缺省、四源合计、单Item最早日、安全量减项、旧建议提前作废、转单Stub需适配。真实采购/MES服务存在不能只换DI便宣称闭环。当前源码/部署/包身份本次未重审；旧测试与历史500保留，不改绿冒新验收。[旧资产](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/24/24141aeae8485be5__MRP-R01.md:379)

七页/36字段落点、18操作到原14ACT的对应、九合同/十二门/跨合同TC见 [开发追踪表](../indexes/PLAN-MRP-01-trace.md)。

## 10. 验收场景和十二门

140条场景（R01 42、R02 50、R03 48）已全读，全部NOT_RUN。重点数字：共享X净230、已抵材料净170/Owner后继180、两良率基数各250、固定损耗一批82/两批84、跨桶20→0、FLOOR首20后0、净65合法40+30、两Run60+60不得超80、需60供60新建议0仍保覆盖。生命周期覆盖混分页、输入冻结、两Attempt不拼、非等价结果冲突、旧代次晚到、Cancel/Publish两胜序、发布丢响应、Firm抢先、外部门改变、合法空、局部共享保护、失权下载及真实更正。[R01](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6c/6c4b8281a1e7b59b__ACCEPTANCE.md:1)；[R02](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/65/652b6311984e396b__ACCEPTANCE.md:1)；[R03](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/ee/ee2516b6cc629af4__ACCEPTANCE.md:1)

G01权、G02模式原键来源、G03闭包固定输入、G04材料抵扣保护、G05图用量生效、G06供给资格父贡献、G07节点安全量批量可行性、G08Attempt权封存恢复取消、G09单Attempt完整确定性零建议、G10四类前驱及外部门、G11下游限制/材料接续、G12解释权限制品，在各最晚相关动作校验，不把未来产物提前当输入前置。串行Mock和文档例算不证明真实并发。[十二门](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/14/14e648e1342492b0__MRP-R03.md:205)

## 11. 尚待确认的真实材料和实现门

七Q为旧安全量、真转换、旧入口切换、资格预留、真实参数、产出/覆盖、包来源。六MRP OPEN为材料剩余合同、用量损耗模型、时间规则、合并/批量偏好、发布/Attempt机制、特殊BOM/委外/调拨/联产回用。当前MAT M22已有RemainingBasis/offsetManifest/protectionBindings/coverage原生设计，仍须把其准确字段与MRP的NET_RESIDUAL/GROSS_WITH_EXPLICIT_OFFSETS显式映射并采用，见PLAN-IF-001；接口设计存在不等于已实现或已采用。日历/批数/精度/封装分页上限不得从夹具猜。正式画布和原生打印/视觉也未由文本接受关闭。[未结门](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/14/14e648e1342492b0__MRP-R03.md:226)

## 12. 来源和阅读覆盖

本次全读R01 417行＋必读例算限定、R02 196行＋36字段全部成员＋18操作/28错误＋四必读限定、R03 242行及系统Review、三套140条GWT、PD01接受叠加。其余10份INDEX/TRACE/文档检查/R01来源记录也已完整实读，合计当前22份输入闭合，具体行段见[root规划阅读记录](D:/CP6/docs/CP6_开发设计文档_20261010/evidence/root-planning-reading.json)。旧受引用业务代码、全历史资料和未形成的正式MRP工作簿不据此算已读或已取得，不将本文或历史作者Review冒运行证明。本次没有执行业务代码、归档脚本、数据库或Actions。
