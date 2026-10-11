# PLAN-SUP-01 供给投影与覆盖开发设计

状态：`required_materials_consolidated`。当前登记24份必需载体的业务语义、136项场景、来源和接受关系已整理；HTML镜像、PB01结构/历史进度、原预览按限定核对。完整原生视觉、真实参数、物理实现和运行验收仍独立保留。

## 1. 目的与权威

SUP把采购、MES、WMS、质量及技术Owner的真实事实组装为可追溯供给代表，解释某份Demand什么时候、由哪一份供给、在什么资格下覆盖。六原SPEC涵盖现存/在途/在制/计划、来源唯一性、分配、时间、缺口与用途技术/试制排除。SUP只持事实接收、映射、快照、资格解释和Planning关联，不能编辑源PO/WO/库存或签专业许可。Pegging不是锁货；真实预留由WMS裁决，完整MRP/转单由各册负责。[职责](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/95/9501a166e513e743__CP6_Planning_策略与供给_R01_v0.1_REVIEW_2026-09-28.md:50)

## 2. 版本与接受组合

PS-R01 `9501a166e513e743`＋PS-R02 `98c0cbdc6614a1ee`＋PS-R03 `e1aed8dc46c13a8d`＋必读C01–C05 `17f532540f6e7d01`为联合当前功能文本。R04接受叠加准确指payload `4c4f69c8c0b1036fc541fd068ecbb2b64817dea4`，认可正文及澄清；载体REVIEW、7Owner证据门全保留，非数据库/API/运行批准。[接受原件](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/da/daa4da427e469056__PROGRESS-ACCEPTANCE.json:1)

这里的八合同、十门、22OP和状态是功能语义，不能擅定微服务路由/SQL表。ATP R11另有精确供给wire与验证器；双方被采用接口需独立映射。准确成员与SHA见[必需输入](D:/CP6/docs/CP6_开发设计文档_20261010/indexes/required-inputs.json)，策略算法详见[POL](D:/CP6/docs/CP6_开发设计文档_20261010/modules/PLAN-POL-01.md)。

## 3. 对象、身份和状态轴

|对象|必需信息|
|---|---|
|DemandInputManifest|正式Demand/Version、SourceLineage、准确份额/量/UOM/需用节点/控制、正式H10或获准LEGACY来源模式|
|ReadScope/FactSetManifest|必要Owner及必要性、准确对象/分页/水位/查询身份、共享承诺、原事实及语义、缺失/受限/冲突、转换证明|
|Snapshot/InputSet|准确不可变需求/策略/事实/资格/保护集合、前驱、完整性和独立当前适用性；非全局原子快照|
|SourceFact/StageOccurrence|Owner+原对象/行/事实版，SNAPSHOT/DELTA/CORRECTION、原量UOM、发生/记录/接收三时点、技术/用途/活动/地点/所有权|
|SupplyLineage/StableSlice/TransferProof|逐份额原与后继、转出转入、真实转换/移动/产出凭证、版本替代、未决、OutputOccurrence与角色|
|QuantityLedger/Representation|未转、采购处理中、未到/未产、待检、实收库存、受控退出、真实差额；指定视图唯一当前代表与原版本组合|
|ConditionDecision/Exclusion|必要性、政策版、证据状态/范围/效期、阻塞动作；真实实物份额与多原因交叠集合|
|Pegging/ProtectionSet|Run/候选版、DemandSlice/SupplySlice、期/技术/UOM/优先级/净量，旧毛溯源、自有/他人WMS占用、范围外有效计划/Firm/未决与映射水位|
|ReservationIntent/Outcome/Residual|原H17请求/内容/需求版/量/限制/部分依据，真实Reservation及子分配、接受/拒绝/未决与当前效力，已终结余量后继引用|
|SupplyReadiness/Impact/Repair/Closure|准确InputSet与G01–10、当前适用性；变化/共享者/未知入口；期望投影前驱和真实原事实；仅本事项闭合依据及未结义务|

原FLD019–048及页级复合成员保存准确来源，不是物理列。[原字段](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/95/9501a166e513e743__CP6_Planning_策略与供给_R01_v0.1_REVIEW_2026-09-28.md:206)；[合同信封](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/e1/e1aed8dc46c13a8d__PS-R03.md:46)

快照ASSEMBLING/COMPLETE_FOR_DECLARED_SCOPE/PARTIAL/CONFLICT与CURRENT/STALE分轴；计划关联CANDIDATE/已发布引用/STALE不等库存状态。资格Requiredness=REQUIRED/JUSTIFIED_NOT_APPLICABLE/UNDETERMINED，Evidence=SATISFIED/NOT_SATISFIED/UNKNOWN/STALE分别记录。原操作HistoricalOutcome与CurrentEffectivity分开，问题OPEN/WAIT_EVIDENCE/READY_TO_CLOSE/CLOSED_WITH_BASIS不级联源对象。[状态](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/98/98c0cbdc6614a1ee__PS-R02.md:70)

## 4. 来源、供给代表、资格与关联流程

正式Demand及其当前控制→固定策略/必要Owner/读取Scope→不可变候选Snapshot→接收原事实→逐份额组装代表→逐活动资格/可用时间→保护池→净关联候选→供给就绪→MRP完整发布。变化只重评受影响依赖，不重新创建客户Demand或重跑整条业务。

必要Owner依据真实依赖闭包，不强迫无关模块在线，也不能删掉失败Owner来获得COMPLETE。v1第一页+v2第二页没有一致证明不拼全量；合法完整空集合是已知无该源，超时/权限受限不是0。旧扫描/显式列表与正式H10同商业责任必须有批准映射和切换清单，不能凭同SKU/订单号去重或双计。Snapshot原成员固定，后到新事实进入日志并形成快照后继/独立适用性；不把v2需求70写入v1需求100的原Manifest。[输入CON002](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/e1/e1aed8dc46c13a8d__PS-R03.md:88)

SNAPSHOT v1累计20→v2累计30取当前30；独立DELTA10才再加成40。Owner/FactId/Version相同异载荷冲突，CORRECTION必须指原事实，不能猜负数含义。原单位保留，精确换算带方向/版本/精度，失败不默认1:1。[事实与阶段CON003](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/e1/e1aed8dc46c13a8d__PS-R03.md:109)

供给三视图不可相加：计划闭环可解释符合条件Firm/后继，但将被本次替换的普通旧建议不能抵掉自己新需求；已确认未来只采用真实合格PO/WO等，PR非供方承诺；合格实存只是真实地点/条件已成立货物。同份额每视图仅一个代表：计划100转PO60、实收30→未转40+未到30+实收30=100，不是190。实收30中合格20/拒收10仍40+30+20+10=100，普通计划候选上限90，拒收10不能退回未到；补货须供方真实后继。[三视图](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/95/9501a166e513e743__CP6_Planning_策略与供给_R01_v0.1_REVIEW_2026-09-28.md:107)

收货30已到、旧PO余100，有准确TransferProof才可组70+30并公开版本组合，不编PO新版；无关系隔离TRANSITION_UNRESOLVED份额，不能展示130。真实超收/超产保留差额，不截计划；实收30有权更正20后，供方未到责任是否恢复须Procurement后继，Planning不自动补10。最终A10、副产B5、中间I20各有OutputOccurrence和单位，不合35件A；CompletedQty工序累计不等最终良品。跨Site同物件分源址、运输、目标实收，企业汇总只计一次；短少不伪损耗。[阶段后继规则](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/e1/e1aed8dc46c13a8d__PS-R03.md:120)

数量与资格分账：Hold/Reject货仍是真实库存。100中Hold30、Reject20、交集10→排除并集40，候选60；交集未知不能输出假精确60或50。先定活动必要性再取专业证据，Pending不默认通过或否决；已知FAIL在查询失败时保留FAIL和查询限制，不改UNKNOWN/PASS。TRIAL QA通过不提升PRODUCTION；普通现货发运不重做制造预约。当前或未来使用节点的技术、质量、客户、货主、Site和效期分别核。[资格CON004](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/e1/e1aed8dc46c13a8d__PS-R03.md:131)

预留/Allocation包含关系不得重扣：合格物理60、Reservation25含Allocation15→自由35。A自有25/B占20/自由15时A候选覆盖最多40，不把A25返全局池。相同池80、A/B各需60、有权A优先→A60/B20；无顺序待决定，不靠DB返回序。两个Run可各模拟80，却不能合称有效160；范围外B承诺50时只重评A也须保护50。[净关联CON005](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/e1/e1aed8dc46c13a8d__PS-R03.md:152)

时间桶期末只结转一次：D1供80需60余20，D2新增40需60余0，不能再使用D1全部80。D2需60/D3才可用60→按时0、到期缺60、未来60，不改客户日期。需求100已核80但必要另源未知→已核80/未证20/最终缺口未定；所有源完整只有80→确定缺20，可交MRP正常净算，短缺不是输入不完整。[时间/缺口](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/95/9501a166e513e743__CP6_Planning_策略与供给_R01_v0.1_REVIEW_2026-09-28.md:153)；[C03](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/17f532540f6e7d01__REVIEW-AND-READ-SCOPE.md:39)

## 5. 接口、操作与发布/转换合同

|PS-R02 OP|边界和产物|
|---|---|
|007候选快照/008事实消费者|固定已知集合和缺口，不调用RunAsync、不删旧Suggested；可信消费者无普通用户手填收货入口|
|009组装/010资格/011选视图|只组本域代表与资格/保护，不建执行单；无证份额隔离|
|012净关联候选|同Planning候选前驱内校数量/日期/保护，存候选版，无WMS写|
|013查预留/014申请预留|查询零占用；申请有权新H17意图交WMS裁决，存原回执/未决，双方各自提交|
|015供给就绪|绑定准确输入、完整性、资格、守恒、保护、水位；非Run Published|
|016变化/017原结果查询|定位真实影响与共享者；查询不源业务重放|
|018修投影/019存量分类|原成功证据+ExpectedProjection+RepairIntent保存后继；分类只判定不迁移|
|020解释/021导出/022关闭|范围/字段权、完整性和原始单位版本；仅本问题闭合，不自动关Demand/QA/Finance|

全部输入、处理和错误精确定位见[22操作原表](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/98/98c0cbdc6614a1ee__PS-R02.md:221)，OP不是22个物理API。

CON006交MRP：SupplyReadiness＋Run/算法/需求BOM/完整ResultSet＋ExpectedEffectiveSet/共享Protection前驱＋真实PublicationOutcome（旧成员退出/保留、新净需求/建议/Pegging统一生效）＋发布后独立适用性。不能在新结果齐前删旧，不可新建议搭旧Pegging冒同Run；原子切换或同库事务保证未实现则不启用。已知相关变化失效旧Ready，无关备注不全局阻塞；真正闭包独立范围可另建独立Run，不能删除原总Run失败行伪装成功。[MRP消费合同](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/e1/e1aed8dc46c13a8d__PS-R03.md:174)

G01权限、G02正式来源/控制/切换、G03策略/单位/日历/安全模式、G04必要源完整、G05阶段唯一/差额、G06活动资格/时点、G07净关联/占用/范围外保护、G08同视图数量/结转、G09提交时适用和共享前驱、G10下游完整结果/原子切换；逐项保留SATISFIED/NOT_SATISFIED/UNKNOWN/STALE和有据不适用，不跳失败。SUP仅产SupplyReadiness，不自签MRP算法/事务已实现。[十门](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/e1/e1aed8dc46c13a8d__PS-R03.md:255)

CON007转换按钮属PLAN-PO：采购H12要求原Firmed及采购门，真实PR仍草稿审批、非PO承诺；制造H13按准确技术/活动/工单建立门，不以单Released代所有PLM条件。WO Created不等下达/投产/消费/入库。部分转换须事前批准固定Manifest、稳定子份额和可独立证明；120分70/50，70真PR、50未知→仅已知转70/未决50，不能再转120或释放50。不可拆组缺共同生效能力不能启用；STUB/单号字符串/无异常不是成功。[转换消费合同](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/e1/e1aed8dc46c13a8d__PS-R03.md:198)；[C04](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/17f532540f6e7d01__REVIEW-AND-READ-SCOPE.md:41)

## 6. 事务、幂等、竞争与恢复

所有写按当前权→原key/内容→Owner前驱/控制→准确输入→本Owner一致提交产物/结果/交接→真实回执。查询不造写意图。同key同内容回历史结果与当前效力，异内容冲突。事实身份与操作身份分开，业务拒绝不能无限技术重试；实质输入改变走后继。[C0–C5](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/98/98c0cbdc6614a1ee__PS-R02.md:64)

预留60实收40，仅当原Owner证明剩余20已终态且原key/子份额绝不再执行，才能新意图申请20；仍会自动补足则余段UNRESOLVED/ACTIVE，不换key再占。原请求重传仍回原40，不能在原key下暗补到60。取消A与占25竞争，迟到真成功保留25和A当前取消，由WMS后继解除；解除未证不返自由、不恢复Demand。已拣/耗/发只处置可合法解除余段，不能用取消Pegging隐式UNRSV。[C01强制终态](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/17f532540f6e7d01__REVIEW-AND-READ-SCOPE.md:35)

MRP竞争不仅比较各RunVersion：同80池两Run各60共享旧保护前驱，权威一致边界必须比较并生效ExpectedEffectiveSet及ProtectionSet，使后者冲突/重评，不能都合法120。外部时间锚点/控制前沿/变化回传有明确保证，无证不能GET Ready后直接写。真实发布后再变则留原成功、做后继影响。[C02](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/17f532540f6e7d01__REVIEW-AND-READ-SCOPE.md:37)

收到停止、入口禁止、旧在途核清三层不同；一次NOT_FOUND/超时不证明未执行或今后不执行。原Owner成功投影漏项只在证据/映射齐、期望前驱一致时修投影，源更正/投影v2先到则拒旧修复。修投影不再建PR/WO/收货。带旧SourceVersion的真实晚到事实保留，旧未执行命令则受当前控制；不按消息到达序编造控制前后的事实。[CON008恢复矩阵](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/e1/e1aed8dc46c13a8d__PS-R03.md:220)

关闭仅本事项：必要执行未知未核清不能用“转人工”移走；独立后果须真实有权接收且不影响本必要未知才可保留后继义务。关闭后新更正留旧Closure、新关联复核，不自动补制造/重开Demand。正常退货不冲原发运历史，退货实物资格未齐不进普通保证，入库不等退款。

## 7. 权限、租户与可见范围

可信Tenant、授权Site/对象/字段/动作每次核。Planner不能冒AuthorizedFactConsumer手填原实物量；预留Requester、Reader、ReconciliationOperator、Exporter、CaseCloser分权。Parent可见但子数据受限，展示准确覆盖限制，不填0或用汇总泄露。无对象权连标题/存在性/原因对象也不泄露；服务自称Owner不足以认证。[访问规则](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/98/98c0cbdc6614a1ee__PS-R02.md:56)

## 8. 画面、校验与错误

SUP01选择正式Demand/Scope，逐Owner水位/缺口/权限和原事实三时点；SUP02谱系、互斥份额、原量/当前代表、转移证明/歧义；SUP03活动资格、物理/自由/自有/他人/未知占用、时间桶净关联、到期/未来/未证覆盖及供给就绪分区。旧GrossTraceLinks只读，新NetPegging可有权候选调整，不改旧毛数据。[三个画面](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/98/98c0cbdc6614a1ee__PS-R02.md:167)

REC01并列原请求/历史结果/当前效力/共享者/缺证，分别查原结果、修投影、导航原Owner新后继。无万能“重跑采购制造”按钮。存量分类可一记录多缺口，取得新证据形成后继，不删历史。查询唯一身份稳定排序；导出固定范围/列/版本/单位/水位，跨页无一致协议标部分，格式安全/限额未定义不启用正式导出。[恢复画面](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/98/98c0cbdc6614a1ee__PS-R02.md:207)

原BR/ERR009事实冲突、010未映射、011阶段重叠、012PR非承诺、013实收账矛盾、014交集未知、015资格、016产出角色、017重复扣预留、018超用/范围外保护、019日期地点、020不完整、021陈旧、022多Owner控制、023恢复、024旧值待核保留细分；错误条数不等影响数量，警告确认不消硬门。[错误到控件](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/98/98c0cbdc6614a1ee__PS-R02.md:252)

## 9. 实施顺序与旧系统差异

先事实身份/载荷语义/完整读取与不可变快照，再逐量阶段映射、资格排除并集和日期、共享保护/净关联、WMS原结果接续，之后MRP就绪/发布双边合同及对账恢复，最后画面/受控导出。先验证来源和份额不重复，才允许用聚合总量做解释。

原审计代码固定`157630594e3371fe181955d2f6227ff3b6962c84`：四源汇总、单Item最早日期、旧毛Pegging/安全量减项及两个转单Stub需适配；真实PR/MES服务存在不等更换DI就符合新合同。LowLevelCode/用量计算、Tenant/RowVersion/权限审计可条件复用，旧Run/STUB/实账保留。本次没有重新审当前源码/旧库或证明包部署一致。[旧代码边界](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/e1/e1aed8dc46c13a8d__PS-R03.md:274)

## 10. 验收与运行边界

R01 44＋R02 52＋R03 40条联合设计场景完整保留、全部NOT_RUN。关键联测：100→40+30+20+10、累计更正/独立事件、收货先到不130、真实超量不截断、供方义务不自动恢复、多Output不合单位、交叠标签、D3不消D2、预留包含分配、自有不返池、部分原键终态、两个Run共享80竞争、部分Owner失联、先停入口再结在途、晚到真成功、修投影无经济写、关闭后纠正及正常退货分账。[R03完整反例](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/2b/2b6b4ac26e32e3a5__ACCEPTANCE.md:1)

并发需真实竞争顺序和权威结果，串行Mock不能证明SQL/跨Owner一致性；本轮例算或文档校验不能填业务PASS。

## 11. 待核与准入门

Q001旧安全量；Q002 STUB/真实后继/在途；Q003旧来源切换与商业责任重叠；Q004活动专业必要性/RSV归属；Q005真实UOM/精度/日历/批量/优先级；Q006最终产出/份额/源水位/共享保护/更正；Q007源码、构建、Package实际解析/部署身份，全部仍需真实证据。正式画布/物理Schema/分页保证/并发/迁移/运行未由功能接受关闭。

ATP R11需要每页digest/ordinal、完整地平线、native量坐标、replacement chain、一次安全量及exact exclusion tuple，本册当前逻辑合同必须映射并被双方采用；不能用“SUP有完整快照”文字替代ATP所要求的canonical载体和currentness证明。与Stock/QA/MES的使用提交门同样需实际采用，读模型资格不是提交保护。

## 12. 来源与阅读覆盖

联合R01 721行、R02 307行、R03 294行、必读C01–05、136场景、接受及其他当前必需输入已按内容闭合。24份载体分别记全文、结构化语义、精确镜像复用、元数据核验或原预览实看；不是全部字节/像素人工全读。详见[开发定位与附件读法](../indexes/POL-SUP-trace.md)及[root阅读台账](../evidence/root-planning-reading.json)。原文中的后续命令为历史内容，本次未执行。
