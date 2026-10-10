# WMS-REM-01 余料管理开发设计

状态：当前正文及X3接受/评审元数据已整理，待双方合同集中核对。[模块目录](D:/CP6/docs/CP6_开发设计文档_20261010/模块目录.md)。

## 1. 目的与权威划分

仓管将已有真实来源的可再用材料登记为余料，按准确物料/规格/方向/可用份额匹配需求，由原RSV建立/释放唯一Claim，由真实材料Owner一次消费，余量继续保留，真正受权报损才减少Stock。四SPEC为登记、预留、使用、处置，匹配/释放/维护/历史是原闭环组成。REM负责行业身份与结果关联，不拥有可独立增减的Quantity/ReservedFor账。

例：真实Root100SHT，登记仍100；为A预留30，Physical100/OpenBacked30；消费20，Physical80/Claim余10；释放原余10，Free80；再合法LOSS5，Physical75。不能把登记/预留/使用标签分别扣库存。ROLL余边再登REM仍同childRoot/QuantityFactKey，报表不重复20M。无Root旧余料只历史/隔离。[原规范](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/31/3192726a41ddd3cf__WMS-REM-01_余料管理_完整设计_RECOVERY_v1.1_R2.md:13)

## 2. 当前版本与通用范围

本轮补核结论：2026-10-08T11:56:55Z代理接受准确六册R2及具名profile；原九根累计静态闭合，全部业务AC仍NOT_RUN、Owner运行采用UNPROVEN。[X3共同阅读与采用边界](D:/CP6/docs/CP6_开发设计文档_20261010/evidence/X3-六专项阅读与采用边界.md)保存准确组合、112AC/31任务逐条等值核对和历史R1原ZIP缺口。下文提及原候选标签均属保留历史。

v1.1 R2 RECOVERY，SHA前缀`3192726a41ddd3cf`；准确原件/UA/CURRENT/评审见[必需输入](D:/CP6/docs/CP6_开发设计文档_20261010/indexes/required-inputs.json)。24AC NOT_RUN，运行采用UNPROVEN；原STOPPED标签作为历史保留；本轮已读准确后继接受和累计评审，未据此宣称运行采用。

当前可执行profile只有IDENTIFIED_AMOUNT和RECTANGULAR_PIECE。前者必须Product/技术用途Owner证明该需求不需要几何；后者面向纸片/膜片/金属薄片等真实矩形，单位/旋转由政策确定。WEB_ROLL_LENGTH虽在公共字典内，不自动可用于本册执行。圆棒、异形、液体混配未定义算法需具名扩展；OTHER/AMOUNT_ONLY不能免未知几何检查。[原规范](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/31/3192726a41ddd3cf__WMS-REM-01_余料管理_完整设计_RECOVERY_v1.1_R2.md:54)

## 3. 对象、唯一性与单位

|对象|核心数据和约束|
|---|---|
|RemnantRegistration|originKind=EXISTING_RECEIPT/ROLL_COMPOSITION_OUTPUT/VERIFIED_LEGACY、原originRef/physicalSourceKey、产品技术货主baseUom、profile/data、准确quantity/spans/rootMapping/measurement、可空工单/卷Ref|
|ProfileData|IDENTIFIED_AMOUNT含geometryNotRequiredDecision/materialClass；RECTANGULAR_PIECE含width/length/可空厚度品级方向、rotationPolicy、geometryToStockMap|
|Intake/Create/Activation|未知现场intakeKey/Scope/reason和可空观察，不含伪来源/Root；已有源分支完整registration；激活带expectedIntakeRevision和原Command expectedRevision|
|RemnantView|registration/intake可空、currentQuantity/Stock spans/各位置、ReservationLinks、Use/Disposition links、cut/knowledge/capabilities/revision|
|ReservationLink|本域Request、可空原RSV request/claim、全部allocation、requested/open/consumed/released、原Demand、状态与原locator|
|Selection/Use/Dispose|真实需求/Grant或ConsumptionInstruction/物理发生、精确spans/Quantity、ClaimUse/measurement/发生时间；处置真实Decision/类型/目的|
|ClosureView|原operation/sourceFence/OwnerOutcome、NO_EFFECT/HAS_EFFECT/RECONCILIATION/WAITING及全部pendingOwnerKeys|

quantity单位=baseUom，量来自Stock Source。矩形width×length只做拟合，不能反推KG/M/SHT；geometryToStockMap逐Root/份额列每单位矩形测量/原cut，证明均质对应。IDENTIFIED_AMOUNT的免几何决定必须当前用途/Demand适用，客户端不能自行签。Measure不等Stock Quantity，unknown量null不填0。[原规范](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/31/3192726a41ddd3cf__WMS-REM-01_余料管理_完整设计_RECOVERY_v1.1_R2.md:50)

同REM角色有效Root覆盖不得重叠，[0,60)与[40,100)须共同CoverageGate拒，UNIQUE(root,from,to)不足。跨ROLL/REM角色可同Root别名显示，但共享QuantityFactKey。物理Source、原需求canonical、原Consumption/occurrence、原Disposition范围分别唯一，actor/HTTP key只传输去重。PK/FK含Tenant+Environment。[原规范](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/31/3192726a41ddd3cf__WMS-REM-01_余料管理_完整设计_RECOVERY_v1.1_R2.md:89)

生命周期DRAFT/ACTIVE/PARTIALLY_USED/EXHAUSTED/DISPOSED/QUARANTINED/ARCHIVED；每Reservation独立REQUESTED/ACTIVE/RELEASE_PENDING/CLOSED/REJECTED/UNKNOWN，不能头Reserved锁/解全Root。知识KNOWN/UNKNOWN独立，历史已用/已损与当前量分别保留。

## 4. 登记、匹配、预留、使用和处置

无已核来源可先UNVERIFIED_INTAKE观察100SHT，生成同Record/Intake，registration=null/currentQuantity=null/UNKNOWN、QUARANTINED，零Root/Claim，不能匹配/预留。原Owner来源齐后，同Record/IntakeHead、physicalSourceKey、全部RootCoverage锁验证完整源/量/单位/测量/cut/权，CAS写ActivationReceipt/RegistrationVersion/Binding、BOUND/ACTIVE和新revision，零Stock。同实物第二隔离记录有原Owner证明才DUPLICATE_ALIAS指既有Record；证不足继续隔离，已Active不可换源。[原规范](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/31/3192726a41ddd3cf__WMS-REM-01_余料管理_完整设计_RECOVERY_v1.1_R2.md:82)

有源登记已有Receipt/ROLL真实输出，所有属性按真实profile核；未有权认账边角料先隔离，不能Create造货。元数据短事务按canonical→物理Source→全部Coverage门，存不可变OriginSnapshot/Audit/Result，无Stock写。普通PATCH只备注；几何、量、位置/技术更正需新提案/原测量/Source及后继核。把500×700裁小片必须由真实转换Owner生成输出再登记，不能复制父跨度给孩子。[原规范](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/31/3192726a41ddd3cf__WMS-REM-01_余料管理_完整设计_RECOVERY_v1.1_R2.md:87)

匹配要求criteria与profile严格一致，错误400 PROFILE_MISMATCH。AMOUNT_ONLY有真实免几何决定，按满足量最近、到期/收货、recordId排序。矩形精确归一单位后NORMAL宽长≥需求；仅rotationRequested且原方向/政策允许才ROTATED，每实物最多一个最优方向。按拟合宽、长、当前可用满足程度、recordId排序，不是全局排版承诺。部分量只在原Source允许、用户明确子范围才接受；不同尺寸需测量分组，SHT份额不推单张几何。完整无候选才空，缺Owner/cut/质量/技术是UNKNOWN。匹配不预留，后续重核selection/cut。[原规范](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/31/3192726a41ddd3cf__WMS-REM-01_余料管理_完整设计_RECOVERY_v1.1_R2.md:95)

预留先持久原Demand的sourceRequestRef/Grant、真实范围/用途、稳定RSV原请求与发送知识。canonical=(sourceOwner,sourceReservationRequestId,sourceDemandRange,usage purpose)，不能换remnant/actor/httpKey再预留30。首批只BACKED，真实RSV Request/Action→Stock CLAIM_CREATE/BACKING/ALLOCATE，已有Claim经原Action补背书/重绑，不另建。RSV+Stock共同结果可核才ACTIVE，Physical不变；丢响应查原键补link。whole30但仅20须拒，允许partial且明确子20才接20。[原规范](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/31/3192726a41ddd3cf__WMS-REM-01_余料管理_完整设计_RECOVERY_v1.1_R2.md:105)

释放是独立原RSV ReleaseAction。先完整UseFence、全部Use/注册路径、SourceNoFurtherUse/物理处置保证，原Stock CLAIM_RELEASE+RSV结果同UoW；未知Use/租约过期不释放。原30已耗20只能余10；release/use同UseGate/Claim/Source裁决，先release阻新耗，先use则只放合法余段。未定RELEASE_PENDING/open量UNKNOWN，不头标Available，不重置Source额度。[原规范](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/31/3192726a41ddd3cf__WMS-REM-01_余料管理_完整设计_RECOVERY_v1.1_R2.md:115)

Use绑定真正ConsumptionInstruction/ConsumptionId/材料行/物理发生/精确量/当前控制，Main材料Owner唯一执行；若WMS正式委托须采用清单与MES/MAT互斥。原结果已存在仅LINK。FREE仅原指令允许且实际无Claim，不“忽略预留”；线边MOVE保Claim不算耗用。持久原Body/键/epoch→事务外原prepare/FrozenIntent→原QuantityUoW核所有当前门、一次CONSUME+正确Claim消费及Source/Receipt/Outbox→同UoW关联或只读WAITING_LINK→完整新cut投影。20/100为PARTIALLY_USED80，0才EXHAUSTED无损失。[原规范](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/31/3192726a41ddd3cf__WMS-REM-01_余料管理_完整设计_RECOVERY_v1.1_R2.md:123)

处置QUARANTINE_MOVE保物理、AUTHORIZED_LOSS只真实受权当前剩余、ADMIN_ARCHIVE只无实物/未决义务。使用20后只可在80里明确LOSS5→75，不能整头关闭隐藏75；普通LOSS拒Claim覆盖，先原RSV合法NoFurtherUse处置。批准失效/撤销阻正常新LOSS；现实已损则ActualFact认账。RECEIVED/QUEUED不等损失，原Receipt与准确范围齐才LINK；同Decision同范围不能换version再损5。[原规范](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/31/3192726a41ddd3cf__WMS-REM-01_余料管理_完整设计_RECOVERY_v1.1_R2.md:148)

## 5. API与Owner接口

候选`/api/wms/remnant/v1`：context/material-profiles/geometry-binding/origin/demand/decision选项及原件；records create/限定PATCH/activate/by-intake-key/history；matches；reservation-requests与reservation-links/:id/releases；use-operations/dispositions；operations prepare/execute/by-key/read/close/resume。prepare/execute不接新业务body；所有DTO闭合必传，仅明确null可空，未知字段400。未知Intake夹带originRef/random rootMappingRef非法。[原规范](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/31/3192726a41ddd3cf__WMS-REM-01_余料管理_完整设计_RECOVERY_v1.1_R2.md:76)

DELETE只从未绑定生效Root、无外调/下游草稿，带版本/原因软删保墓碑。旧根/ReservedFor仅OLD兼容，新请求不足Source/精度/幂等字段423升级，旧MarkUsed不暗代理全量CONSUME。量/Claim写只原RSV/Stock Kernel；缺已采用适配WAITING_ADOPTION，完整设计接口不等生产采用。[原规范](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/31/3192726a41ddd3cf__WMS-REM-01_余料管理_完整设计_RECOVERY_v1.1_R2.md:78)

REM/ROLL/MES同一Source occurrence归一原Consumption canonical，源版本变化不重置额度。正常消费接口只受权组织原Owner，不签新的自由用途。原Stock Correction+ConsumptionOwner核后继Output/Claim/可逆权；真实未耗退料走Return/MOVE，已耗不靠Unreserve复活，消费中新边料须真实工艺Output/Receipt另登记。[原规范](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/31/3192726a41ddd3cf__WMS-REM-01_余料管理_完整设计_RECOVERY_v1.1_R2.md:142)

## 6. 并发、未知与恢复

持久层Record/RegistrationVersion、Intake/Observation/ActivationReceipt/DuplicateAlias、OriginMapping/RootBinding/CoverageGate、冻结Profile/GeometryBinding或免几何决定、ReservationRequestRoot/Link、Use/ConsumptionLink、Disposition/Link、OwnerCallJournal完整Body/hash域/locator/发送知识、ObservationCut、ClosureFence/Recovery/Epoch、Legacy/WriterFence、Audit/Inbox/Outbox。无独立ReservedQty，Ref/Receipt不可原地UPDATE。Root全资源先收集稳定序，新增资源整次rollback/recollect。[原规范](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/31/3192726a41ddd3cf__WMS-REM-01_余料管理_完整设计_RECOVERY_v1.1_R2.md:160)

操作与停止争Record/OperationGate，预留释放消费最终同RSV UseGate、Stock Root/Claim、Source控制门；普通GET ACTIVE照片不是并发保证。旧worker晚回只保存原证据，不以旧epoch覆盖新Stop。跨Owner无共同事务时原成功与本域LINK正交，不能假原子。[原规范](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/31/3192726a41ddd3cf__WMS-REM-01_余料管理_完整设计_RECOVERY_v1.1_R2.md:166)

|未知窗口|合法恢复|
|---|---|
|登记/激活丢响应|原physicalSourceKey或intakeKey查同Record/Binding，不第二登记|
|RSV请求未知或成功LINK缺|查原完整key/body，保未决范围，补原Claim/Receipt关联|
|消费/LOSS commit未知|查原Source/Stock locator，不换发生键或释放保护|
|NoFurtherUse少一Owner|保Fence和待核完整参与者，不能空集合签完成|
|更正通知漏|原Owner完整当前cut恢复后重读，不拿旧current继续用|
|现场已耗/销毁但账未知|不可变ActualFact/对账保护，不能seal无效果重放可用|
|永久Source fence+原Stock无效已证|关闭原键墓碑，真实剩余后继另审，不复活旧操作|

[原规范](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/31/3192726a41ddd3cf__WMS-REM-01_余料管理_完整设计_RECOVERY_v1.1_R2.md:170)

## 7. 权限、租户与导出

rem.read/register/activate/metadata.edit/match/reserve.request/release.request/use.coordinate/dispose.propose/dispose.execute/archive/recover.query-resume/history-export分别校验。Site、源仓/目标仓、Owner、字段权取交集；能看余料不等能看客户商业需求，受限字段redacted且不回隐藏摘要。UNKNOWN知识与redacted权限不同。导出生成和领取均检权，固定cut/筛选/列/current knowledge；新执行最后门当前权成立，撤权不改历史已成功。[原规范](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/31/3192726a41ddd3cf__WMS-REM-01_余料管理_完整设计_RECOVERY_v1.1_R2.md:168)

## 8. 页面与校验

RM01按当前量/原预留/已用/已损/知识分轴；RM02两种来源分支和同Record激活；RM03Root/位置/Claims/历史只读、备注维护；RM04真实Demand/profile、AMOUNT或矩形拟合，选择带selectionRef；RM05原Request/Grant/Claim/spans及准确余段释放；RM06真实消费指令和本次实际量，禁止一键无数量Used；RM07受权处置范围/NoFurtherUse；RM08原键/Body/locator/epoch/closure恢复。

数量十进制字符串，原四位不限制Stock新八位。AMOUNT页不填假宽长，非纸不默认PAPER/SHT；旋转默认false且政策允许，短卷M不能当一张SHT。关键身份/需求/用途变更清selection/preview，受理后body冻结；离页不取消，重入原键查询；queryGeneration丢弃晚搜索结果。服务端分页，100/1000旧上限不当全集。[原规范](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/31/3192726a41ddd3cf__WMS-REM-01_余料管理_完整设计_RECOVERY_v1.1_R2.md:33)

## 9. 开发顺序与旧代码差异

D01封闭两入口、同Record CAS激活/别名、源测量读取/Root角色覆盖；D02两profile、单位方向、非纸例/真实资格/分页；D03原RSV申请释放和全部恢复；D04唯一实际消费/部分状态；D05处置/关闭、迁移、权限导出，按依赖G01–06实施。[原规范](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/31/3192726a41ddd3cf__WMS-REM-01_余料管理_完整设计_RECOVERY_v1.1_R2.md:193)

原文固定CP6@90c871fe的历史源码观察：旧Create只T_Remnant，默认PAPER/SHT、decimal(18,4)；Update可覆盖身份数量仓位，Delete任意状态软删；Reserve/Unreserve只头状态+自由ReservedFor，MarkUsed无量且不Stock，Dispose包括Used可设状态。旧Match仅宽长/类型取100，UI最多1000/默认500×700；这些均不能当新库存/权威能力。旧测试只头生命周期和纸尺寸匹配，本轮未运行也未新审源码。[原规范](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/31/3192726a41ddd3cf__WMS-REM-01_余料管理_完整设计_RECOVERY_v1.1_R2.md:21)

DP11真实Root可核才零数量LegacyMap，自由文本来源隔离；旧Reserved不造Claim，Used/Disposed只reported，不补消费/损失，软删有物料须纳盘。DP12保旧原号/四位/状态/时间读，不反截新八位到旧写；API/import/job/mobile/跨域材料所有writer同范围封闭。DP16OLD→HOLD排/永久封未决→核head/存量→NEW，新事实后只前滚。[原规范](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/31/3192726a41ddd3cf__WMS-REM-01_余料管理_完整设计_RECOVERY_v1.1_R2.md:183)

## 10. 验收与状态

24原AC均NOT_RUN：登记100零增、未知现场与同ID激活、重叠覆盖/重复观察、ROLL共享20M、金属矩形旋转与有据KG免几何、UNKNOWN与完整空；预留30物理100、RSV丢包不二30、耗20释放余10得到Free80、未知Use不释放、whole30不足20拒；消费20剩80、MES只link、线边MOVE、Use/Release竞态、耗尽无LOSS；LOSS5余75、旧Used无证不LOSS100、有Claim先原处置、隔离MOVE保量；软删存量不归零、NEW旧Update423、分页/状态0/14位只读总量与13位写限、现实销毁未知不封零效。[原规范](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/31/3192726a41ddd3cf__WMS-REM-01_余料管理_完整设计_RECOVERY_v1.1_R2.md:195)

完整JSON读到Intake无Source/Root、RemnantActivation完整原件和两量域正反例；64零digest只静态形状。没有运行Stock/DB/迁移、业务测试或CI。

## 11. 待闭合采用门

REM-G01真实Source/Root/测量及ROLL别名；G02精确Demand/RSV Claim/Use/原键/NoFurtherUse；G03唯一ConsumptionOwner与MES/ROLL去重、Stock同UoW；G04Disposition/质量/技术/MASTER容量及正式目的；G05永久原请求查询/全路径fence/ActualFact/当前cut；G06完整旧writer/软删存量/映射，均实际UNPROVEN。缺门阻相应新效果，有权读/隔离观察可保。[原规范](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/31/3192726a41ddd3cf__WMS-REM-01_余料管理_完整设计_RECOVERY_v1.1_R2.md:191)

X3共同接受/范围/评审已核；本轮继续核双方Owner合同，尤其免几何决定的当前用途适用性、Root角色覆盖与跨角色报表去重，不能靠页面选择profile直接授予合法用料资格。

## 12. 来源与覆盖

主件1–225独有规范/24AC全文读；226–313附录与已读REP148–235逐行完全相同复用；314–604所有说明/JSON完整对象读取，仅压缩空白无字段省略。原件SHA/待核组合与阅读记录见[台账](D:/CP6/docs/CP6_开发设计文档_20261010/evidence/root-specialty-reading.json)。历史作者源码/Library读取资格沿原件，不冒成本轮执行；原件未改，历史指令只作为材料。

## 必要材料整理结论

当前15项必需引用均已完整阅读或使用有证据的精确复用：主文、当前接受/审查、原范围、AC与任务文字以及来源资格已纳入设计。历史成员目录只证明来源身份，不等于旧代码全文审计；外域实际采用、Provider和业务运行继续按本册门禁确认。见[逐目标必需引用核对](../evidence/root-specialty-required-closure.json)。后续整任务进行一次集中跨模块复核，不将静态设计整理等同实际验收。
