# WMS-XDOCK-01 越库作业开发设计

状态：当前正文及X3接受/评审元数据已整理，待双方合同集中核对。[模块目录](D:/CP6/docs/CP6_开发设计文档_20261010/模块目录.md)。

## 1. 目的与权威边界

越库安排准确到货份额经正式暂存/出货位，交付准确出库需求，减少常规上架环节。WMS负责草稿、逐行来源映射、Owner步骤编排及结果投影；原PUR/生产来源授权、IN真实Receipt/Root、RSV Claim/Use、OUT Shipment/Stock SHIP各由其Owner负责。本域计划不替PO、FINAL_OUTPUT、收货许可或销售发运授权，也不创建第三套库存账。

**IN与OUT是分阶段发生，可能部分成功。** 各自物理组遵其共同UoW，不宣称跨两个Owner总原子，不用删除已收事实补偿发运失败。计划30，原IN实收20，原OUT实发15：当前已收20、已发15、已收未发5、未收计划10，没有未来10的Root；原OUT取消仍保20真实库存，交受权储存/退回处置，不能再OUT20凑净零。[原规范](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/43/433993a9448ae3c0__WMS-XDOCK-01_越库作业_完整设计_RECOVERY_v1.1_R2.md:13)

## 2. 当前组合与采用状态

本轮补核结论：2026-10-08T11:56:55Z代理接受准确六册R2及具名profile；原九根累计静态闭合，全部业务AC仍NOT_RUN、Owner运行采用UNPROVEN。[X3共同阅读与采用边界](D:/CP6/docs/CP6_开发设计文档_20261010/evidence/X3-六专项阅读与采用边界.md)保存准确组合、112AC/31任务逐条等值核对和历史R1原ZIP缺口。下文提及原候选标签均属保留历史。

v1.1 R2 RECOVERY，主件SHA前缀`433993a9448ae3c0`，4原SPEC为越库单/入出关联/执行/取消。完整准确身份及UA/CURRENT/评审见[必需输入](D:/CP6/docs/CP6_开发设计文档_20261010/indexes/required-inputs.json)。保留原STOPPED作者状态，矩阵选定不冒成本轮全部接受证据已读。通用仓储来源、位置、产品、计量须真实Owner提供，旧行业代码只作兼容示例。18AC NOT_RUN，6采用门UNPROVEN；拟议API不代表原IN/OUT已提供同名端点。[原规范](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/43/433993a9448ae3c0__WMS-XDOCK-01_越库作业_完整设计_RECOVERY_v1.1_R2.md:3)

## 3. 核心对象、范围和计量

|对象|实现所需语义|
|---|---|
|XDockDraft/PlanLine|可信Scope、正式inDock/outDock/tempLocation、预计时间、逐lineKey原inSource/outDemand及产品/技术/货主/baseUom、sourceRange、计划量/转换|
|LinkageVersion/Line|冻结planVersion、原入源→准确OUT需求范围的sourceMapping、原instructionLine、摘要、接收映射集合、覆盖|
|ReceivedMapping|原Receipt及ReceiptLine、原sourceRange、真实rootSpans/baseQuantity、sourceToRootMap、当前Stock cut和质量观察|
|ShipLink|mappingKey、原OUT work/可空Shipment与StockReceipt、原需求范围/Root、量、REQUESTED/STARTED/POSTED/SEALED_NO_EFFECT/UNKNOWN、原locator|
|LineQuantities|完整4Ref维度、计划量、historicalConfirmed原确认事实及receiptRefs/时间、current四量/cut/覆盖/原因、retainedRootSpans|
|HeaderAmounts|有据同维度且范围互斥的SINGLE_DIMENSION，或NOT_AGGREGATABLE及MIXED_PRODUCT/TECHNICAL/OWNER/UOM/UNPROVEN_DISJOINT_COVERAGE|
|OwnerStep/StopItem|原键、完整Body、发送知识/epoch、精确关闭、保留原件和剩余处置归属|

sourceRange、Root span、outDemandRange可属不同坐标；必须有批准转换和同量映射，不能只比较SKU/总数。相同EA的A产品20+B产品5不能返回25业务总量。历史无法读取时historicalConfirmed整个null并给HISTORY_UNAVAILABLE；完整无事实才0。当前未知量null/UNKNOWN与维度不可汇总不同。[原规范](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/43/433993a9448ae3c0__WMS-XDOCK-01_越库作业_完整设计_RECOVERY_v1.1_R2.md:47)

状态DRAFT→LINKED→ACTIVE，终态/异常另有STOP_REQUESTED、STOPPED、COMPLETED、RECONCILIATION；知识KNOWN/UNKNOWN独立。源Owner状态独立，不能用一个头状态覆盖IN/OUT结果。所有PK/FK含Tenant+Environment；UQ(tenant,env,order,lineageVersion,lineKey,step,physical/source occurrence)不能用planVersion绕原SourceOccurrence唯一性。[原规范](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/43/433993a9448ae3c0__WMS-XDOCK-01_越库作业_完整设计_RECOVERY_v1.1_R2.md:137)

## 4. 草稿、关联、收发和终态

创建校验当前字段权、正式地点、正量/精确单位与真实来源基本维度；源许可未知可DRAFT并列blocker，不接受伪空Ref。一次短事务保存Command/Order/PlanVersion/Line/Audit/Result，无Root/Claim/IN/OUT。DRAFT修改CAS，内容无变NO_CHANGE；真实变化保新版本。冻结需真实采购Arrival或生产FINAL_OUTPUT，IN计划只能引用不能造货；缺合法发生WAITING_SOURCE，原批管来源缺失不生成日期批号。[原规范](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/43/433993a9448ae3c0__WMS-XDOCK-01_越库作业_完整设计_RECOVERY_v1.1_R2.md:66)

计划映射限制本域重复安排，但外域另路消费仍需真正排他授权/原Owner最终门。实收后LINK_RECEIPT读取原ReceiptLine和Stock RootMap，登记准确源范围/目的唯一性。一个Receipt20拆A12+B8允许，A12+B12交叠4整次拒绝；Root/source共同gate判范围，不以总量小于Receipt替代。原RSV已有Claim直接引用，未有走合法Source申请；XDOCK映射不能再扣一次。已有Receipt/Use/Shipment后不改旧图，变更需有据重路由后继和Owner处置。[原规范](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/43/433993a9448ae3c0__WMS-XDOCK-01_越库作业_完整设计_RECOVERY_v1.1_R2.md:74)

入库先持久order/linkage/line/step canonical、原Owner键和完整Body、发送状态、epoch。采购沿PUR_GR→IN共享ReceiptUoW，生产沿FINAL_OUTPUT→IN原Grant；已收只LINK。不能造XDOCK_RECEIPT producer重复原Arrival/Output。Receipt+Stock确定才计received，准备成功只是PROCESSING；现场收到但账未记交原ActualFact，超时沿原键查询。[原规范](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/43/433993a9448ae3c0__WMS-XDOCK-01_越库作业_完整设计_RECOVERY_v1.1_R2.md:88)

发运只取真实Root与原OUT授权/Claim的可发交集；未来计划量不在候选。移至出货位用合法MOVE保Claim，不当SHIP；越库不自动免检/免拣/免包装，须Owner适用政策。原ShipmentInput冻结实际包与范围，Stock最后门核当前IN有效范围、质量、OUT授权、全部Claim/Use/容量。原OUT POSTED且StockReceipt/ShipmentLine可核才shipped，DEL提货/签收不再扣库存。[原规范](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/43/433993a9448ae3c0__WMS-XDOCK-01_越库作业_完整设计_RECOVERY_v1.1_R2.md:96)

四量按同当前cut的集合计算：received是当前有效实收映射，shipped是其中真实发运互斥范围，receivedUnshipped为集合差，notReceivedPlanned为未被事实覆盖的计划范围，超量保EXCEPTION不截掉。头只有完整四维相同、互斥覆盖已证、同完整cut才可合TotalQuantity。COMPLETED要求全部真实收发完成或受权缩减/处置使无未处理计划、留存、UNKNOWN；净0不足。STOPPED仍可有已收未发且明确后续归属，详情必须继续显示货物。[原规范](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/43/433993a9448ae3c0__WMS-XDOCK-01_越库作业_完整设计_RECOVERY_v1.1_R2.md:104)

## 5. API和Owner编排合同

候选`/api/wms/cross-dock/v1`：context/orders/history；inbound/outbound/location-options；orders创建/DRAFT PATCH；orders/:id/linkages与linkages/:id/freeze；orders/:id/steps；operations/by-key/read/resume；orders/:id/stop。OwnerStepInput步骤仅OPEN_INBOUND_WORK、LINK_RECEIPT、OPEN_OUTBOUND_WORK、REFRESH_ORIGINAL，不能自填POSTED/StockReceipt。结果给受控同站原Owner链接，客户端不拼任意URL。[原规范](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/43/433993a9448ae3c0__WMS-XDOCK-01_越库作业_完整设计_RECOVERY_v1.1_R2.md:61)

拟议`XDockOwnerCoordinator-1`定义ReadSource/OriginalWork/Receipt/Shipment/CurrentCut、OpenOriginalWork、ResumeOriginalStep、PrepareClose/ReadClosure；它是受权适配约定，不声称accepted IN/OUT现有同名HTTP。采用不足可转原Owner页面人工真实完成再关联，不能旁路Source写Stock。XDOCK Ref只作外包证据，不能替原Owner Ref3、locator和摘要域。[原规范](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/43/433993a9448ae3c0__WMS-XDOCK-01_越库作业_完整设计_RECOVERY_v1.1_R2.md:84)

Outbox保存已提交元数据/原消息。OUT成功Sales未应用，只重派原OUT消息；不能再SHIP。运输ack不等物理成功，关闭浏览器不停止原请求。

## 6. 事务、未知与停止恢复

本域短事务只记编排/链接/审计；IN/OUT原共同UoW分别决定物理事实。Step同键异Body409隔离，迟到结果核原request digest/phase/epoch；原成功可补历史，不能撤StopFence或继续已停止后续。Receipt/Shipment原事件唯一，源范围/需求门和Stock/RSV/OUT最终锁共同控制，单个本域唯一索引不是全局物理锁。[原规范](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/43/433993a9448ae3c0__WMS-XDOCK-01_越库作业_完整设计_RECOVERY_v1.1_R2.md:139)

停止锁Order、全部相交Step与Linkage，先永久封未来工作并递增epoch，完整枚举含尚无OwnerId但可能发出的旧调用；不能先清映射/Budget再等Owner。各情况：NEVER_SENT草稿关原键；IN可能发送查原关闭；已收未发保Receipt/Root且处置Claim/Use，禁止OUT凑零；OUT未知查原Shipment/Stock；已发保历史，真实退回走RMA/IN，错误记账走原Correction；旧半成功进RECONCILIATION。[原规范](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/43/433993a9448ae3c0__WMS-XDOCK-01_越库作业_完整设计_RECOVERY_v1.1_R2.md:114)

全关闭要求全部未来路径封住、每个原Owner明确终态或已生效保留结果、全部未决Use/Claim有授权处置归属。一项UNKNOWN则Stop WAITING，空查询/NOT_FOUND/超时不足。原IN成功本域失联只LINK，物理已发生系统未提交存ActualFact，原正常键不自动重开。恢复只query/resume/link/redrive/request-seal，无SQL/表名/任意Owner URL入口。[原规范](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/43/433993a9448ae3c0__WMS-XDOCK-01_越库作业_完整设计_RECOVERY_v1.1_R2.md:135)

持久化Order/Plan、Linkage/LineageMap、SourceRangeGate/OutboundDemandGate、Receipt/ShipmentLink、OwnerStepCall/Alias/OriginalPayload、StopFence/Item、ObservationCut/LineQuantityProjection、Recovery/Inbox/Outbox/LegacyMap/Audit；保完整历史与当前分轴。

## 7. 权限与隔离

xdock.read、draft.write、link.edit/freeze、inbound/outbound.coordinate、stop、recover.query/resume、history/export分开。编排权还需真实IN/OUT动作权及Site/源/目的/货主范围交集；入库协调不自动包含销售批准/处置。最终动作重读当前权，导出也逐字段当前过滤，scope游标绑定主体；无权对象404不泄露商业Ref摘要，可见但不可操作403。日志不留第三方敏感凭证全文。[原规范](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/43/433993a9448ae3c0__WMS-XDOCK-01_越库作业_完整设计_RECOVERY_v1.1_R2.md:64)

## 8. 页面与校验

XD01显示计划/已收/已发/待发及知识，不能净零筛“已完成”；XD02有权原来源/位置选择器建DRAFT，不自动批号；XD03逐行Source→Receipt→Root→Claim/OUT映射，无Receipt时Root为null；XD04推进原IN或关联已收，无“再收一次”；XD05只生成受权原OUT工作；XD06四量、完整维度、原Owner状态/货物位置与历史当前分开；XD07四类剩余范围、关闭证据、处置归属。

先context及Scope/Location/SourceOption，人工单号只用于搜索。任何来源、目标、维度、量改变使preview失效；候选不完整显示UNKNOWN，不能“没有可发”伪成“全部发完”。异维度不得自动配对。[原规范](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/43/433993a9448ae3c0__WMS-XDOCK-01_越库作业_完整设计_RECOVERY_v1.1_R2.md:33)

## 9. 开发顺序与历史差异

D01草稿/真实选择器/精确源行映射；D02完整OwnerStep journal及双Owner原件读/登记/恢复；D03行级四量、历史当前分轴和头不可合量投影；D04停止/剩余处置/半成功迁移；D05权限/分页/导出/审计和采用证据。均无独立Stock写服务。[原规范](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/43/433993a9448ae3c0__WMS-XDOCK-01_越库作业_完整设计_RECOVERY_v1.1_R2.md:155)

原文固定CP6@90c871fe源码观察：旧Execute依次Stock.Apply(IN)、Apply(OUT)，本service没有全组关系事务；接口注释“一事务”不能证明实现。IN已提交而OUT失败，头可能仍Planned，存在旧重入风险；本轮未运行验证。旧Source是可选自由文本、Lot为空会合成XD日期号，不能迁成真实Arrival/Shipment；旧UI读取row.xDockNo正确，Create匿名xdockNo与原实体形状须兼容，旧500条和两位小数不继续作为新规范。[原规范](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/43/433993a9448ae3c0__WMS-XDOCK-01_越库作业_完整设计_RECOVERY_v1.1_R2.md:21)

DP11旧双腿准确可映射、只IN的PARTIAL_LEGACY_FACTS、未知分别处理，不按旧status迁移；商业自由文本UNRESOLVED_SOURCE。DP12保旧编号/lot/Txn/JSON，NEW旧Execute/import/job全fence；DP16OLD→HOLD排未知→NEW，新事实后只前滚。[原规范](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/43/433993a9448ae3c0__WMS-XDOCK-01_越库作业_完整设计_RECOVERY_v1.1_R2.md:149)

## 10. 验收与未执行范围

18AC均NOT_RUN：01完整草稿且缺Ref400；02计划不能收；03不造批号；04Receipt20拆12+8；05重叠12+12整次拒；06同SKU不同货主拒；07计划30收20发15得到20/15/5/10且A/B同EA不可合；08IN成功只补link；09OUT丢包回原结果；10MOVE不计发；11原Receipt不重复收；12DEL零Stock；13停止留存/历史20不替当前UNKNOWN；14stop/外发竞态；15保原已发15；16NOT_FOUND/503等待；17旧IN成功OUT失败不重Execute；18当前更正/撤权阻新发。[原规范](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/43/433993a9448ae3c0__WMS-XDOCK-01_越库作业_完整设计_RECOVERY_v1.1_R2.md:159)

JSON示例完整读取了Draft必填Ref、LineQuantities/retainedRootSpans和NOT_AGGREGATABLE头；64零摘要仅形状，非真实执行证据。14位只读TotalQuantity合法不等单条Quantity写入合法。[原规范](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/43/433993a9448ae3c0__WMS-XDOCK-01_越库作业_完整设计_RECOVERY_v1.1_R2.md:265)

## 11. 待核采用与设计边界

XD-G01真实Arrival/FINAL_OUTPUT及Receipt/关闭；G02原OUT授权/Shipment/RSV Use；G03Source→Root→Demand映射与全域预算；G04正式地点/容量/质量/技术；G05原键查询/全路径fence；G06历史双腿/所有旧writer，均UNPROVEN。缺对应门阻相关新效果，可保草稿/读或转原Owner完成。[原规范](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/43/433993a9448ae3c0__WMS-XDOCK-01_越库作业_完整设计_RECOVERY_v1.1_R2.md:147)

X3 UA/CURRENT/共同评审已核；继续做双侧契约比对，尤其current四量跨IN/OUT更正cut与范围覆盖，不可仅拼独立“最新”HTTP结果。本文保留有据阶段编排；业务希望“两段一键原子”需另行明确设计，不能靠UI成功标签扩权。

## 12. 来源与实际覆盖

当前主件1–172规范/18AC全文读；173–260公共附录逐行与已读REP148–235完全相同并复用；261–608全部说明和JSON完整读取（JSON压成完整对象展示，未删字段/值）。历史作者读取窗口和固定代码观察继承原件来源资格，不冒成本轮网络/运行验证。逐件SHA、覆盖和已核接受组合见[阅读台账](D:/CP6/docs/CP6_开发设计文档_20261010/evidence/root-specialty-reading.json)。整理不改原件，不执行历史指令。

## 必要材料整理结论

当前15项必需引用均已完整阅读或使用有证据的精确复用：主文、当前接受/审查、原范围、AC与任务文字以及来源资格已纳入设计。历史成员目录只证明来源身份，不等于旧代码全文审计；外域实际采用、Provider和业务运行继续按本册门禁确认。见[逐目标必需引用核对](../evidence/root-specialty-required-closure.json)。后续整任务进行一次集中跨模块复核，不将静态设计整理等同实际验收。
