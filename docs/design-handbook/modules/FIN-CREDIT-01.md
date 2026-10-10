# FIN-CREDIT-01 交易信用检查

整理状态：**required_materials_consolidated**。当前 R4 主文、149 Schema定义、63向量、45连续案例和104AC的规范语义已整理；十二准确窗口全十八段与原fixture字节相等。306成员包已按当前规范、历史及来源分清角色。这个状态不代表全部历史源码已读，真实参数、Owner采用与业务执行仍UNPROVEN/NOT_RUN；§11保留具体原文差异。

## 1. 业务目的、操作者与边界

本模块在Sales创建、确认、释放、授权发运时检查交易信用，并把“查看结果”“预占正增量”“实际Sales动作”分开。Sales人员按服务器上下文发起，Finance有权人决定有界特批，可信Sales/AR服务在最终业务事务内使用参与者，迁移管理员切换唯一writer。Main Finance拥有政策、评估、信用预留/特批和回执；Main Sales拥有订单及外层动作；FIN-AR拥有发票、收款核销和贷项事实；ERP-BP拥有伙伴身份及当前使用资格。信用PASS不创建Order、Demand、WO、PR、PO或WMS指令。[S原件L25](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/94/9442439f12ceb101__FIN-CREDIT-01_交易信用检查_前后端开发Spec_v1.0_R4_DRAFT.md:25>) [S原件L36](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/94/9442439f12ceb101__FIN-CREDIT-01_交易信用检查_前后端开发Spec_v1.0_R4_DRAFT.md:36>) [S原件L445](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/94/9442439f12ceb101__FIN-CREDIT-01_交易信用检查_前后端开发Spec_v1.0_R4_DRAFT.md:445>) [S原件L8515](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/94/9442439f12ceb101__FIN-CREDIT-01_交易信用检查_前后端开发Spec_v1.0_R4_DRAFT.md:8515>)

H07保QTN接受来源/orderIntent和BP使用；H09仅ATP四态观察，既不产生供给许可，也不代替信用许可。原范围四SPEC：信用输入、检查结果、阻塞/特批依据、订单动作引用；PLAN-ATP在本冻结源中的未接受状态不能当所有后继的现状，实际后继采用由全局版本关系核定。[S原件L29](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/94/9442439f12ceb101__FIN-CREDIT-01_交易信用检查_前后端开发Spec_v1.0_R4_DRAFT.md:29>) [S原件L541](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/94/9442439f12ceb101__FIN-CREDIT-01_交易信用检查_前后端开发Spec_v1.0_R4_DRAFT.md:541>) [S原件L545](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/94/9442439f12ceb101__FIN-CREDIT-01_交易信用检查_前后端开发Spec_v1.0_R4_DRAFT.md:545>)

## 2. 有效版本、强制组合和接受

当前主文v1.0 R4，8598行、359236字节、SHA256 `9442439f12ceb101fb49f469f596a529f061526d0a7b504289ef5fd3185e9349`。准确接受还包括306成员完整规范包`c2cf761a…`、R4 INDEX`394714a3…`、DELIVERY-IDENTITY`8c109aca…`及十二准确原字节窗口和manifest。原正文DRAFT/NOT_ACCEPTED保历史字节，后续2026-10-01接受覆盖其标签。不能只采用正文省略schema和规范附件。[A原件L12](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/5c/5c529369eed1398e__UA-20261001-A-FIN-CREDIT-MD01.md:12>) [A原件L23](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/5c/5c529369eed1398e__UA-20261001-A-FIN-CREDIT-MD01.md:23>)

R4独审仅关闭FC-R3-01/02及type/schema/vector/fixture传播，前轮已闭合事项保留；它确认144 TypeScript声明、149 defs、69根分支、63向量、45例、104AC，这些为历史静态结果，本轮不重跑。接受是四SPEC设计文本，真实Finance参数/批准者/Owner采用/IAM/同MainUow/迁移和运行仍UNPROVEN。[R原件L7](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/ac/ac27a9fbc21ed72f__SR-20261001-A-FIN-CREDIT-MD01-INDEPENDENT-R4.md:7>) [R原件L13](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/ac/ac27a9fbc21ed72f__SR-20261001-A-FIN-CREDIT-MD01-INDEPENDENT-R4.md:13>) [A原件L69](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/5c/5c529369eed1398e__UA-20261001-A-FIN-CREDIT-MD01.md:69>)

## 3. 数据身份、分类、金额与版本

本域Scope为tenantId/legalEntityId/ledgerId/environmentId和mode=`PRODUCTION|SIMULATION`；Ref六字段owner/type/id/version/purpose/digest。外域语义version原字节保留，本域Rev规范无前导零十进制字符串。时间固定毫秒Z，金额字符串最多38总位/8小数、无指数/+号/负零/尾零；`25.0`不合法规范金额。原Money币种、policyAmount、准确FX、rule和sourceAsOf一并保留，同币种FX=null。[S原件L139](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/94/9442439f12ceb101__FIN-CREDIT-01_交易信用检查_前后端开发Spec_v1.0_R4_DRAFT.md:139>) [S原件L202](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/94/9442439f12ceb101__FIN-CREDIT-01_交易信用检查_前后端开发Spec_v1.0_R4_DRAFT.md:202>) [S原件L397](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/94/9442439f12ceb101__FIN-CREDIT-01_交易信用检查_前后端开发Spec_v1.0_R4_DRAFT.md:397>) [S原件L537](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/94/9442439f12ceb101__FIN-CREDIT-01_交易信用检查_前后端开发Spec_v1.0_R4_DRAFT.md:537>)

|稳定身份|权威/表示与防重|
|---|---|
|obligationKey|Scope+FIN-AR invoice line/slice；唯一当前应收aggregate|
|receipt effectKey|allocationId+allocationLineId+invoiceId+准确D13 BalanceEvent；D14分配行+D13发票减少为权威，D25仅必需收款会计依据|
|credit effectKey|D17 CreditFact行及D13发票减少事件|
|lineageKey|Sales order/line/schedule/source lineage；同一经济订单敞口各阶段仅一当前头|
|Reservation|只绑定原lineage、predecessor、target和正delta，不再加入AR cut|
|Evaluation|rootEvaluationId、不可变revision、唯一前驱/后继、原因COMPLETE_CUT/POLICY_CHANGE/ORDER_CHANGE/SOURCE_CORRECTION|
|Command|Scope+principal+method+canonicalRoute+commandId和完整body fingerprint；五型终态原件|

上述三个经济身份空间不能互换；当前头不等必计量。FC01–FC19包括不可变Policy、interval claim、AR cut/absorption、lineage/transfer、ReservationSet、Evaluation、Exception、Command/Receipt、CreditScope、Outbox/Audit和切换/采用/锁计划记录，头与claim走CAS。[S原件L407](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/94/9442439f12ceb101__FIN-CREDIT-01_交易信用检查_前后端开发Spec_v1.0_R4_DRAFT.md:407>) [S原件L515](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/94/9442439f12ceb101__FIN-CREDIT-01_交易信用检查_前后端开发Spec_v1.0_R4_DRAFT.md:515>)

政策必须恰好六行且各自INCLUDE/EXCLUDE、required和maxAge，不用内部存储类型替代：POSTED_OPEN_AR、PARTIALLY_SETTLED_OPEN_AR、UNBILLED_CONFIRMED_ORDER、ACTIVE_CREDIT_RESERVATION为ADD；EFFECTIVE_CREDIT_MEMO、APPLIED_RECEIPT为SUBTRACT。EXCLUDE计零但仍留rule/证据，必需来源过期/缺失为UNKNOWN。[S原件L401](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/94/9442439f12ceb101__FIN-CREDIT-01_交易信用检查_前后端开发Spec_v1.0_R4_DRAFT.md:401>)

## 4. 正常流程和经济敞口守恒

**政策**不可变且同Scope/action/asOf有效区间不重叠，ACTIVE必须真实Finance批准。额度missing/zero由明确模式处理：BLOCK/ZERO_LIMIT阻正请求，MANUAL_REVIEW为HOLD，EXPLICIT_UNLIMITED须Owner依据与政策原因；null/0不能继承旧“无限额”。缺政策或无限额证据为POLICY_UNAVAILABLE。[S原件L401](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/94/9442439f12ceb101__FIN-CREDIT-01_交易信用检查_前后端开发Spec_v1.0_R4_DRAFT.md:401>)

**建立cut**：同一证明快照读FIN-AR obligation、D14+D13+D25、D17+D13、吸收关系和frontier，核全部规则、金额、FX、时效、摘要；伙伴信用锁下另读Sales lineage与ReservationSet。缺前驱/循环/重复当前头/跨集合身份/必需来源无权都使UNKNOWN或冲突，不能填0。[S原件L431](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/94/9442439f12ceb101__FIN-CREDIT-01_交易信用检查_前后端开发Spec_v1.0_R4_DRAFT.md:431>)

总敞口=`arBookedExposure + orderUnbilledExposure + activeReservedDelta`，三者互斥。AR只含当前open aggregate及CUT_LAG_COUNTABLE的负收款/贷项effect；订单只含尚未转AR的UNBILLED lineage；预留只计RESERVED、countable=true且正delta。违例REPRESENTATION_DOUBLE_COUNT。[S原件L415](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/94/9442439f12ceb101__FIN-CREDIT-01_交易信用检查_前后端开发Spec_v1.0_R4_DRAFT.md:415>)

收款效果尚未被净额吸收时100+(-30)=70；准确AbsorptionLink将aggregate更新为70，同时effect追加ABSORBED_NONCOUNTABLE，之后70+0=70。链接核effect/predecessor/successor、相互membership、`successor.open=predecessor.open+signedEffect`并同commit。冲回不改负effect为正：新REVERSED_NONCOUNTABLE仍保-30，准确D15+D13恢复+30进入aggregate；贷项同理用D17 reversal+D13。[S原件L411](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/94/9442439f12ceb101__FIN-CREDIT-01_交易信用检查_前后端开发Spec_v1.0_R4_DRAFT.md:411>)

X35“部分取消”实际是原30全量D15反核销+D13恢复，再新D14分配20；旧effect保-30且不计量，新effect-20，不能伪造原Owner不支持的部分Unapply。原准确窗口完整读到原金额、原分配行、D13恢复/UoW/sequence及新分配身份。[X原件L14](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/bf/bfe196ee4c1dfd07__X35-PARTIAL-UNAPPLY-SUCCESSOR_EXACT.txt:14>) [X原件L146](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/bf/bfe196ee4c1dfd07__X35-PARTIAL-UNAPPLY-SUCCESSOR_EXACT.txt:146>) [X原件L287](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/bf/bfe196ee4c1dfd07__X35-PARTIAL-UNAPPLY-SUCCESSOR_EXACT.txt:287>) [X原件L361](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/bf/bfe196ee4c1dfd07__X35-PARTIAL-UNAPPLY-SUCCESSOR_EXACT.txt:361>)

|入口|关键条件/写集/结果|
|---|---|
|C05 Evaluate|持久命令/评估/终态Receipt；PASS只观察，无预留。E1 UNKNOWN不改，完整cut形成唯一E2后继|
|C06 Reserve|15类锁计划，重算available=limit−AR−order−activeReserved；Evaluation+Reservation+set revision+Receipt/Outbox/Audit同commit，UNKNOWN/CAS/权限/超限不写Reservation|
|Sales最终动作|当前BP I02、完整subject/target/delta/line/schedule、政策/cut/reservation/exception/权限终核；Sales结果+lineage successor+消费/释放+Transfer+FinanceReceipt同外层commit|
|Sales已记账取消/减值|专用SalesExposureReductionParticipant，同Sales UoW生成减少/RELEASED successor和有符号Transfer；失败Sales结果也回滚|
|AR最终Invoice Post|专用FinArFinalPostParticipant，AR真实过账UoW同时新obligation、原lineage转AR_OPEN不再计订单、Transfer/Receipt/Outbox/Audit|

[S原件L435](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/94/9442439f12ceb101__FIN-CREDIT-01_交易信用检查_前后端开发Spec_v1.0_R4_DRAFT.md:435>) [S原件L437](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/94/9442439f12ceb101__FIN-CREDIT-01_交易信用检查_前后端开发Spec_v1.0_R4_DRAFT.md:437>) [S原件L445](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/94/9442439f12ceb101__FIN-CREDIT-01_交易信用检查_前后端开发Spec_v1.0_R4_DRAFT.md:445>) [S原件L455](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/94/9442439f12ceb101__FIN-CREDIT-01_交易信用检查_前后端开发Spec_v1.0_R4_DRAFT.md:455>) [S原件L457](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/94/9442439f12ceb101__FIN-CREDIT-01_交易信用检查_前后端开发Spec_v1.0_R4_DRAFT.md:457>)

创建取政策指定target，确认/释放按target−current；正差额才占用，负差额走明确减少。普通/部分SHIP delta=0，令牌不可计量；数量/价增25→32只新增7。发运10/25仍同lineage25，发运物理事实另域保存。取消不是复用ORDER_CONFIRM，公共Release仅释放信用预留，不减少已记账订单或AR。[S原件L419](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/94/9442439f12ceb101__FIN-CREDIT-01_交易信用检查_前后端开发Spec_v1.0_R4_DRAFT.md:419>)

## 5. API、精确参与者和跨Owner合同

public根`/api/fin/credit/v1`；不提供公开consume端点，最终消费只能在Sales实际事务。[S原件L461](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/94/9442439f12ceb101__FIN-CREDIT-01_交易信用检查_前后端开发Spec_v1.0_R4_DRAFT.md:461>) [S原件L480](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/94/9442439f12ceb101__FIN-CREDIT-01_交易信用检查_前后端开发Spec_v1.0_R4_DRAFT.md:480>)

|编号/入口|调用含义|
|---|---|
|C01 GET /options；C02 /policies/effective|当前Scope能力/政策安全视图和ETag|
|C03 /partners/{bpId}/credit-context；C04 /orders/{orderId}/credit-context|BP/AR/订单上下文，不是许可|
|C05 POST /evaluations；C06 /reservations|持久评估与原子预留分开|
|C07 GET /evaluations/{id}、/reservations/{id}|EvaluationView/ReservationView根合法分支|
|C08 POST /reservations/{id}/release|原reservation ETag，单向释放|
|C09 /exceptions；C10 /exceptions/{id}/decisions；C11 /revoke；C12 GET|申请、Finance决定、撤销及原链查询|
|C13 GET /commands/{id}?method=POST&canonicalRoute=|同principal/Scope/route/fingerprint恢复五型完整终态|
|C14–16 POST /admin/cutover/prepare、/activate、/rollback|持久管理员命令，丢响应查原ADMIN Receipt|

BP使用精确UseApplyInput/UseReceipt、accountId/mapping/RoleSet/依赖Ref和isReusablePermit=false；QTN已有SALES_NEW_SOURCE不替代确认时新SALES_CONFIRM。Finance不复制隐蔽profile也不把BP显示额度当信用决定。[S原件L171](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/94/9442439f12ceb101__FIN-CREDIT-01_交易信用检查_前后端开发Spec_v1.0_R4_DRAFT.md:171>) [S原件L445](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/94/9442439f12ceb101__FIN-CREDIT-01_交易信用检查_前后端开发Spec_v1.0_R4_DRAFT.md:445>) [S原件L541](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/94/9442439f12ceb101__FIN-CREDIT-01_交易信用检查_前后端开发Spec_v1.0_R4_DRAFT.md:541>)

SalesReduction结果必须完整SalesResult、lineageSuccessor、Transfer、FinanceReceipt、commandQueryRef和CompositeProof；ARFinalPost结果包含postingResult、aggregate、D13、retiredLineage及同组结果。C13五型EVALUATION/WRITE/ADMIN/SALES_REDUCTION/AR_FINAL_POST必须无损返回，不能只一个泛型“成功”。[S原件L362](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/94/9442439f12ceb101__FIN-CREDIT-01_交易信用检查_前后端开发Spec_v1.0_R4_DRAFT.md:362>) [S原件L457](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/94/9442439f12ceb101__FIN-CREDIT-01_交易信用检查_前后端开发Spec_v1.0_R4_DRAFT.md:457>)

ATP input精确绑order/version/line/schedule/itemRevision/site/正数量/UOM/date/action/asOf及inputDigest；输出FOUND/UNPROVEN/UNAVAILABLE/NOT_AUTHORIZED+isExecutionPermit=false。subject不匹配为UNPROVEN，不能自行造Planning端点或把FOUND当信用通过。[S原件L368](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/94/9442439f12ceb101__FIN-CREDIT-01_交易信用检查_前后端开发Spec_v1.0_R4_DRAFT.md:368>) [S原件L545](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/94/9442439f12ceb101__FIN-CREDIT-01_交易信用检查_前后端开发Spec_v1.0_R4_DRAFT.md:545>)

## 6. 事务、并发、生命周期和恢复

**信用相关六路径的统一15类顺序**：IAM/组织/字段政策→BpScopeGuard→BP Command/useKey→MappingScope→ReverseClaim→BpRoot→BP子表→Sales Order→Finance Policy→Credit Scope→AR frontier/obligation→lineage→ReservationSet→exception→Command/result。只能类内字典序，不能整体按资源字符串排序；空类保固定位置。声明全计划并持权威BP前缀之后才锁下游头。[S原件L437](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/94/9442439f12ceb101__FIN-CREDIT-01_交易信用检查_前后端开发Spec_v1.0_R4_DRAFT.md:437>) [S原件L449](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/94/9442439f12ceb101__FIN-CREDIT-01_交易信用检查_前后端开发Spec_v1.0_R4_DRAFT.md:449>) [J原件L5907](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/f7/f768bfcd2f485e00__FIN-CREDIT-WIRE.schema.json:5907>)

Guard坐标(Tenant,LegalEntity)；Mapping(Tenant,**AccountId**,LegalEntity)；ReverseClaim/Root(Tenant,LegalEntity,BpId)。BP mapping与profile/status变更Guard X，其余SalesConfirm/SalesReduction/ARFinalPost/CreditReserve下游Guard S持到真实commit；实际写资源按路径模式。每步Proof/reentry绑定`H(mainUowHandle,step)`，同UoW/resource/mode方可重入；禁止Sales→BpScope、Root→Command、subtable→Mapping反边。旧Sales-first writer围栏禁用，不拿远程HTTP200冒原子。此顺序有界适用本合同六路径，不泛化全系统。[S原件L449](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/94/9442439f12ceb101__FIN-CREDIT-01_交易信用检查_前后端开发Spec_v1.0_R4_DRAFT.md:449>) [S原件L451](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/94/9442439f12ceb101__FIN-CREDIT-01_交易信用检查_前后端开发Spec_v1.0_R4_DRAFT.md:451>)

Reservation单向RESERVED→CONSUMED/RELEASED/EXPIRED/SUPERSEDED，到期是持久后继不能显示时猜测。释放不改AR。特批不可变、绑定Scope/partner/order/action/policy/amount/currency/time/use，最终同Owner事务核并写一次use；不能把UNKNOWN敞口豁免为known。撤销先赢阻动作，动作先赢保历史不自动回滚。[S原件L441](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/94/9442439f12ceb101__FIN-CREDIT-01_交易信用检查_前后端开发Spec_v1.0_R4_DRAFT.md:441>) [S原件L8474](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/94/9442439f12ceb101__FIN-CREDIT-01_交易信用检查_前后端开发Spec_v1.0_R4_DRAFT.md:8474>)

每命令先当前披露下快速查终态，再有序Command锁内二次查，再新执行。响应丢失原queryRef/commandId/fullbody恢复，异body409；明确前驱漂移412重读，commit真未知才UNKNOWN。写权撤销阻新意图；读权撤销403且不改原Receipt。参与者UNKNOWN时Sales减少或AR过账都不得先commit。[S原件L496](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/94/9442439f12ceb101__FIN-CREDIT-01_交易信用检查_前后端开发Spec_v1.0_R4_DRAFT.md:496>) [S原件L511](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/94/9442439f12ceb101__FIN-CREDIT-01_交易信用检查_前后端开发Spec_v1.0_R4_DRAFT.md:511>) [S原件L457](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/94/9442439f12ceb101__FIN-CREDIT-01_交易信用检查_前后端开发Spec_v1.0_R4_DRAFT.md:457>)


R4 的 X39 为六路径给出如下模式；表中空表示保留该类 run 但没有资源，S/X 是实际持有模式，不能只记录“检查过”。四下游路径都持 IAM S、BP Command X、Mapping/ReverseClaim/Root S、ROLE/STATUS S，再进入各自后缀；两 BP 变更路径不凭空锁 Sales/Finance。

|路径|BpScopeGuard|BP Mapping / Reverse / Root / 子表|Sales头 / FinancePolicy / CreditScope|AR frontier / Lineage / ReservationSet|Exception / CommandResult|
|---|---|---|---|---|---|
|BP_MAPPING_MUTATION|X|X / X / X / 空|空 / 空 / 空|空 / 空 / 空|空 / 空|
|BP_PROFILE_STATUS_MUTATION|X|空 / 空 / X / PROFILE+STATUS X|空 / 空 / 空|空 / 空 / 空|空 / 空|
|SALES_CONFIRM|S|S / S / S / ROLE+STATUS S|X / S / X|S / X / X|S / X|
|SALES_REDUCTION|S|S / S / S / ROLE+STATUS S|X / S / X|S / X / S|S / X|
|AR_FINAL_POST|S|S / S / S / ROLE+STATUS S|S / S / X|X / X / S|S / X|
|CREDIT_RESERVE|S|S / S / S / ROLE+STATUS S|S / S / X|S / S / X|S / X|

[SL3645](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/94/9442439f12ceb101__FIN-CREDIT-01_交易信用检查_前后端开发Spec_v1.0_R4_DRAFT.md:3645>) [SchemaL5907](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/f7/f768bfcd2f485e00__FIN-CREDIT-WIRE.schema.json:5907>)

每个 Proof 的所有 acquired binding 与 reentry 都要匹配同一个真实 MainUow、准确 resource 和 mode；另一 UoW 自称 REUSED_HELD_LOCK 仍 LOCK_ORDER_VIOLATION。顺序先 BP 再 Sales/Finance 的旧路径围栏在取下游锁之前生效；本表只适用这六条信用合同路径，不是其他模块的默认锁表。[SL6710](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/94/9442439f12ceb101__FIN-CREDIT-01_交易信用检查_前后端开发Spec_v1.0_R4_DRAFT.md:6710>) [SL6733](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/94/9442439f12ceb101__FIN-CREDIT-01_交易信用检查_前后端开发Spec_v1.0_R4_DRAFT.md:6733>) [SchemaL9151](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/f7/f768bfcd2f485e00__FIN-CREDIT-WIRE.schema.json:9151>)

## 7. 权限、隔离和安全披露

Scope和principal可信服务端取得，credit.options.read/policy.read/exposure.read/evaluate/reserve/exception.request/exception.decide/result.read与Sales动作能力分别核；决定还需Finance角色+当前SOD/证据/政策。前端不能提交limit/policy/exposure/BP状态/权限/decision真值。无金额权SafeCell REDACTED不能0，隐藏hash亦不返；对象不存在/不可见统一404。C13每一种终态和legacy墓碑都当前重新核完整资源/字段披露。[S原件L480](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/94/9442439f12ceb101__FIN-CREDIT-01_交易信用检查_前后端开发Spec_v1.0_R4_DRAFT.md:480>) [S原件L486](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/94/9442439f12ceb101__FIN-CREDIT-01_交易信用检查_前后端开发Spec_v1.0_R4_DRAFT.md:486>) [S原件L511](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/94/9442439f12ceb101__FIN-CREDIT-01_交易信用检查_前后端开发Spec_v1.0_R4_DRAFT.md:511>) [S原件L537](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/94/9442439f12ceb101__FIN-CREDIT-01_交易信用检查_前后端开发Spec_v1.0_R4_DRAFT.md:537>)

所有示例是虚构组织SIMULATION数据，无自然人信用资料；不能将示例阈值/币种/期限/批准者带到PRODUCTION。[S原件L41](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/94/9442439f12ceb101__FIN-CREDIT-01_交易信用检查_前后端开发Spec_v1.0_R4_DRAFT.md:41>) [S原件L582](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/94/9442439f12ceb101__FIN-CREDIT-01_交易信用检查_前后端开发Spec_v1.0_R4_DRAFT.md:582>)

## 8. 页面交互和校验

顺序固定C01→BP B02/B04选伙伴→C03→Sales订单原读+C04→C02→C05/C06→必要C09/C12/C10→Sales最终动作携refs/ETag→C07/C13恢复。应分别显示policy模式/是否可见、AR/order/reserved三项、完整度与sourceAsOf、CHECK_ONLY结果、当前Reservation/特批使用次数、Sales结果；PASS按钮不直接执行。原queryRef刷新，未知后不生成新命令。[S原件L480](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/94/9442439f12ceb101__FIN-CREDIT-01_交易信用检查_前后端开发Spec_v1.0_R4_DRAFT.md:480>)

服务端以Sales重算金额/target/line-schedule为准；未知字段/noncanonical金额给400及准确JSON Pointer，缺幂等/前提428。政策/BP/order/exposure/reservation版本变更412零新跨Owner效果；FX/UNKNOWN/超限422零受保护Sales动作；远程或锁序不符503/409并禁下游写。金额和理由遮罩不能伪装没数据。[S原件L498](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/94/9442439f12ceb101__FIN-CREDIT-01_交易信用检查_前后端开发Spec_v1.0_R4_DRAFT.md:498>)

## 9. 实施顺序、切换与旧代码

建议先准确Policy和六类rule/AR来源身份、相互吸收守恒及lineage，再同伙伴CreditScope/完整锁计划、持久C05/C06和特批，接Sales/AR两个最终参与者及恢复，最后全量legacy采用和安全页面。该排序为整合建议，实际专业参数与Owner接线尚未有运行凭据。

旧Main固定`15763059…`的CreditControlService读Posted/PartiallySettled AR和贷项，旧`/api/fin/ar/credit/check`、`/api/orders/credit-check`仅显示兼容；BusinessPartner.CreditLimit只是额度来源观察，不能默认为公司政策。DI scoped注册、4个合成旧测试不证明新参与者；旧OrderService未enlist该信用门是固定源差距，不声称当前部署状态。[S原件L20](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/94/9442439f12ceb101__FIN-CREDIT-01_交易信用检查_前后端开发Spec_v1.0_R4_DRAFT.md:20>) [S原件L560](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/94/9442439f12ceb101__FIN-CREDIT-01_交易信用检查_前后端开发Spec_v1.0_R4_DRAFT.md:560>)

CutoverGuard为PREPARED→ACTIVATING→ACTIVE；准备冻结Inventory和AdoptionManifest，逐legacy order/line/schedule保原Money/FX/rule和targetLineage，原子保存预分配AdoptionResult和LEGACY_ADOPTION lineage，不伪造LIVE_ACTION。**订单数与曝光条目数分开**：一订单两schedule完整是1/1、2/2；第二未知则订单1/0、条目2/1阻激活。每单全部expectedEntry IDs/digest都采用才计已采用订单。[S原件L576](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/94/9442439f12ceb101__FIN-CREDIT-01_交易信用检查_前后端开发Spec_v1.0_R4_DRAFT.md:576>)

激活禁新legacy permit，等在途commit/rollback，写永久terminal/unknown/no-effect命令墓碑，核manifest/frontier/count/digest后CAS新epoch。回退只deadline前、newEffectCount=0/inFlightNew=0、无新Reservation/Result且清单未变；否则前滚，SEALED不回退。管理员Prepare/Activate/Rollback也有principal绑定命令Receipt与C13恢复，不因响应丢失重复采用。[S原件L576](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/94/9442439f12ceb101__FIN-CREDIT-01_交易信用检查_前后端开发Spec_v1.0_R4_DRAFT.md:576>) [S原件L578](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/94/9442439f12ceb101__FIN-CREDIT-01_交易信用检查_前后端开发Spec_v1.0_R4_DRAFT.md:578>)

## 10. 验收、规范案例和历史审查

104AC全部NOT_RUN：并发两个订单只有一个获足额预留；UNKNOWN/FX缺失无预留；收款/贷项吸收前后总额守恒；普通及部分发运delta0；25→32只增7；取消32→0单向减少；两后续动作争同lineage；C05/C13响应丢失/撤权；BP新用途同UoW；远程非原子拒；切换旧writer/未知订单条目阻断。全104条目录已实读，其运行证据未产生。[S原件L8421](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/94/9442439f12ceb101__FIN-CREDIT-01_交易信用检查_前后端开发Spec_v1.0_R4_DRAFT.md:8421>) [S原件L8446](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/94/9442439f12ceb101__FIN-CREDIT-01_交易信用检查_前后端开发Spec_v1.0_R4_DRAFT.md:8446>) [S原件L8471](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/94/9442439f12ceb101__FIN-CREDIT-01_交易信用检查_前后端开发Spec_v1.0_R4_DRAFT.md:8471>) [S原件L8496](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/94/9442439f12ceb101__FIN-CREDIT-01_交易信用检查_前后端开发Spec_v1.0_R4_DRAFT.md:8496>) [S原件L8521](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/94/9442439f12ceb101__FIN-CREDIT-01_交易信用检查_前后端开发Spec_v1.0_R4_DRAFT.md:8521>)

45 组案例现在已逐项读取，完整请求、初始事实、后继、期望、错误及身份都保留；X39 按六条路径读取完整基例和所有递归增删改，不以同一标题代替其他路径：

|完整案例族|实现及恢复约束|原件入口|
|---|---|---|
|X01–12|额度100、已占40时40与30并发请求都曾看到60；伙伴锁内重读只允许一方成功。政策7→8使旧预留SUPERSEDED；特批撤销先赢则阻动作，动作先赢保历史。释放一次，远程HTTP不能满足heldUntilCommit。|[SL586](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/94/9442439f12ceb101__FIN-CREDIT-01_交易信用检查_前后端开发Spec_v1.0_R4_DRAFT.md:586>)|
|X13–16、X22|收款100−30在cut更新前后均70；贷项100−20均80。预留25→未开票订单25→AR25各同Owner提交转换，总敞口无空窗/双计；Reservation永不混入AR cut。|[SL1211](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/94/9442439f12ceb101__FIN-CREDIT-01_交易信用检查_前后端开发Spec_v1.0_R4_DRAFT.md:1211>)|
|X17–21|普通/部分发运delta0；25→32只新增7；取消32→0用Sales减少参与者，公共Release不代替取消。两个后续动作竞争同lineage，后者ORDER_CHANGED、零业务效果。|[SL1553](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/94/9442439f12ceb101__FIN-CREDIT-01_交易信用检查_前后端开发Spec_v1.0_R4_DRAFT.md:1553>)|
|X23–28|C05持久评估丢响应后C13回原E1；UNKNOWN完成cut用新命令形成唯一E2，不改E1。查询重新核披露；BP原五字段consumer wire和不可复用receipt保持原形；ATP观察绑定完整subject且不是执行许可。|[SL1829](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/94/9442439f12ceb101__FIN-CREDIT-01_交易信用检查_前后端开发Spec_v1.0_R4_DRAFT.md:1829>)|
|X29–33|先停发旧permit再排空在途、保永久命令墓碑，激活不出现双writer。回退只新效果0且无新在途，产生新legacy epoch9，不复活旧epoch7；已有新效果则前滚。|[SL2130](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/94/9442439f12ceb101__FIN-CREDIT-01_交易信用检查_前后端开发Spec_v1.0_R4_DRAFT.md:2130>)|
|X34–36|同一90USD收款拆30/60两张发票，各D14行+D13事件是不同effect，按150折为4500/9000；不能按receipt合并。原30全量反核销后新分配20；跨币10USD的1500JPY减项保原Money、准确FX与来源。|[SL2346](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/94/9442439f12ceb101__FIN-CREDIT-01_交易信用检查_前后端开发Spec_v1.0_R4_DRAFT.md:2346>)|
|X37–39|SalesReduction与ARFinalPost分别持真实外层UoW、准确结果及有类型C13终态，重放不再产生效果；六路径的完整计划/Proof见§6，错误UoW不能重入。|[SL2940](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/94/9442439f12ceb101__FIN-CREDIT-01_交易信用检查_前后端开发Spec_v1.0_R4_DRAFT.md:2940>)|
|X40–44|一旧订单两排程40+15，两个采用结果逐条绑entryDigest→lineageDigest，总敞口55；1/1订单且2/2条目才激活。第二UNKNOWN时订单1/0、条目2/1且无新writer效果。Prepare/Activate/Rollback丢响应均查原ADMIN终态，不重复推进epoch。|[SL6771](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/94/9442439f12ceb101__FIN-CREDIT-01_交易信用检查_前后端开发Spec_v1.0_R4_DRAFT.md:6771>)|
|X45|恰好六种独立政策分类，EXCLUDE计零仍保ruleRef；必需来源过期为UNKNOWN。内部AR/lineage/reservation存储集合不能替代六类政策输入。|[SL8333](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/94/9442439f12ceb101__FIN-CREDIT-01_交易信用检查_前后端开发Spec_v1.0_R4_DRAFT.md:8333>)|

完整 Schema 为 69 个根分支、149 defs。开发时至少分别实现下面的形状门和业务门；正向Schema样例不保证其引用已存在、摘要已核、金额关系成立或批准有效：

|字段族|明确编码约束|证据|
|---|---|---|
|标量/Ref|Id/Key非空且最长200；本域Rev无前导零，外域Ref.version保语义Key。UTC毫秒Z还要真实公历解析。正/非负/有符号金额不同，最多38总位、8小数且禁尾零/负零；不能直接浮点。|[SchemaL215](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/f7/f768bfcd2f485e00__FIN-CREDIT-WIRE.schema.json:215>) [SchemaL233](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/f7/f768bfcd2f485e00__FIN-CREDIT-WIRE.schema.json:233>) [SchemaL245](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/f7/f768bfcd2f485e00__FIN-CREDIT-WIRE.schema.json:245>)|
|Policy|六种kind各恰好一行，按kind限制ADD/SUBTRACT；protectedActions 1–4，finalRecheckRequired恒true；时效maxAge为1–31536000秒。ACTIVE实际批准、有效区间唯一和无限额依据仍业务层检查。|[SchemaL1053](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/f7/f768bfcd2f485e00__FIN-CREDIT-WIRE.schema.json:1053>) [SchemaL907](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/f7/f768bfcd2f485e00__FIN-CREDIT-WIRE.schema.json:907>)|
|AR减项与更正|aggregate classification、identity、contribution及rule同类。ReducingEffect所有状态的signedPolicyAmount保负；CUT_LAG无吸收，更正字段空；ABSORBED需准确aggregate；REVERSED需前驱和匹配D15或D17恢复类型。|[SchemaL1292](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/f7/f768bfcd2f485e00__FIN-CREDIT-WIRE.schema.json:1292>) [SchemaL1469](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/f7/f768bfcd2f485e00__FIN-CREDIT-WIRE.schema.json:1469>)|
|读取/终态|SafeMoneyCell明确VISIBLE/REDACTED/UNPROVEN；C13五分支EVALUATION/WRITE/ADMIN/SALES_REDUCTION/AR_FINAL_POST各自完整。reservation终态及currentCountable另核；不能将查询view当最终许可。|[SchemaL4125](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/f7/f768bfcd2f485e00__FIN-CREDIT-WIRE.schema.json:4125>) [SchemaL3261](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/f7/f768bfcd2f485e00__FIN-CREDIT-WIRE.schema.json:3261>)|
|锁与采用|runs长度恰15且按prefixItems固定类序，资源DTO严格拒错坐标；下游Guard S、BP变更Guard X。Manifest分别计订单/条目，OrderCoverage保expected/adopted ID集合及摘要；Schema不替代持锁真实性、集合相等和当前来源检查。|[SchemaL5907](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/f7/f768bfcd2f485e00__FIN-CREDIT-WIRE.schema.json:5907>) [SchemaL9191](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/f7/f768bfcd2f485e00__FIN-CREDIT-WIRE.schema.json:9191>)|

63 个向量的全部 payload、所有差异和错误路径已读。SV01–15覆盖封闭字段、金额正负/零/尾零/8或9小数、38或39位、Rev/UTC和外域语义version；SV16–19覆盖C07/C13根；SV20–23覆盖六政策分类及D15更正；SV24–32覆盖两个最终参与者/终态和共同锁；SV33–43覆盖aggregate/effect/lineage/reservation分类与负号；SV44–47覆盖采用原件及D17恢复；SV48–58覆盖六路径、UoW证明形状、逆序/错坐标/错模式；SV59–63覆盖订单与条目双维完整/不完整表示。[SV1L6](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/df/dfc3c4002d5c9381__FIN-CREDIT-R4-SCHEMA-VECTORS.json:6>) [SV16L2186](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/df/dfc3c4002d5c9381__FIN-CREDIT-R4-SCHEMA-VECTORS.json:2186>) [SV20L2782](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/df/dfc3c4002d5c9381__FIN-CREDIT-R4-SCHEMA-VECTORS.json:2782>) [SV24L3306](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/df/dfc3c4002d5c9381__FIN-CREDIT-R4-SCHEMA-VECTORS.json:3306>) [SV33L5367](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/df/dfc3c4002d5c9381__FIN-CREDIT-R4-SCHEMA-VECTORS.json:5367>) [SV44L6708](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/df/dfc3c4002d5c9381__FIN-CREDIT-R4-SCHEMA-VECTORS.json:6708>) [SV48L7348](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/df/dfc3c4002d5c9381__FIN-CREDIT-R4-SCHEMA-VECTORS.json:7348>) [SV59L11164](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/df/dfc3c4002d5c9381__FIN-CREDIT-R4-SCHEMA-VECTORS.json:11164>)

例如 SV39 是Schema正向样例，openOriginal/openPolicyAmount改为20，而contribution中的原金额和policyAmount仍25；它只证明分类和线格式可表达，不能据此接受一次敞口计算结果。实际聚合、FX、吸收、hash和跨Ref守恒必须按§4业务规则核。SV60/61/63的“不完整采用”对象Schema合法，也仍禁止激活。这是案例适用范围，不能把结构合法升级为业务合法。[SV39L6145](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/df/dfc3c4002d5c9381__FIN-CREDIT-R4-SCHEMA-VECTORS.json:6145>) [SV60L11218](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/df/dfc3c4002d5c9381__FIN-CREDIT-R4-SCHEMA-VECTORS.json:11218>) [SL431](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/94/9442439f12ceb101__FIN-CREDIT-01_交易信用检查_前后端开发Spec_v1.0_R4_DRAFT.md:431>)


## 11. 具体待确认和资料覆盖缺口

1. **跨册wire不可直接共用**：Credit的Scope mode PRODUCTION/SIMULATION、legalEntityId/ledgerId及六字段Ref，GL为DEMO/REAL、legalEntityKey/ledgerKey及五字段Ref。必须Owner注册明确映射，不能删type或替换mode字符串后声称同合同。[S原件L139](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/94/9442439f12ceb101__FIN-CREDIT-01_交易信用检查_前后端开发Spec_v1.0_R4_DRAFT.md:139>) [GL原件L26](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/da/dabe7fa5cc4da4e4__FIN-GL-01_会计事件与凭证过账_前后端开发Spec_v1.0_R2_REVIEW_CANDIDATE.md:26>) [GL原件L255](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/da/dabe7fa5cc4da4e4__FIN-GL-01_会计事件与凭证过账_前后端开发Spec_v1.0_R2_REVIEW_CANDIDATE.md:255>)
2. **金额wire到数据库域待核**：schema允许38位纯整数且最多8小数，主文称decimal(38,8)。直接SQL decimal(38,8)只有30整数位；需明确无损存储/业务范围和迁移约束，不静默截断/缩窄接受wire。这是静态整合分析，尚未核任何真实DB实现。[S原件L537](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/94/9442439f12ceb101__FIN-CREDIT-01_交易信用检查_前后端开发Spec_v1.0_R4_DRAFT.md:537>) [J原件L245](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/f7/f768bfcd2f485e00__FIN-CREDIT-WIRE.schema.json:245>)
3. 真实Finance阈值/币种/protectedActions/专业批准、Current IAM、BP/AR/Sales同连接UoW、全部writer覆盖和切换时间清单仍UNPROVEN。后继ERP-ORD/PLAN-ATP正式合同的覆盖关系由全局索引核，不以本册旧冻结状态永久拒绝，也不以别域Stage100自动采用。[S原件L8548](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/94/9442439f12ceb101__FIN-CREDIT-01_交易信用检查_前后端开发Spec_v1.0_R4_DRAFT.md:8548>)
4. **OPS-IF-042 原文错误码差异：** 主文错误表把未知字段INVALID_FIELD列为HTTP400，SV02的why却写422。服务端错误适配、前端展示和契约验收需Owner确定一致值；向量本身只保存schema拒绝，并没有HTTP执行结果。[SL500](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/94/9442439f12ceb101__FIN-CREDIT-01_交易信用检查_前后端开发Spec_v1.0_R4_DRAFT.md:500>) [SV02L154](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/df/dfc3c4002d5c9381__FIN-CREDIT-R4-SCHEMA-VECTORS.json:154>)
5. 当前R4规范附件已闭合；306成员原包中历史v1–R3、旧代码、分配/发布记录及其他Owner整册不因本轮身份核对成为已实读。其剩余范围属于全归档/跨册采用队列。

## 12. 来源与实读记录

主文1–583及8415–8598全文实读；584–8414所有45案例的业务字段完整结构语义读，X39的meta及六plan/六proof各保精确JSON pointer/行段和全部差异，其余44组所有字段直接读。149 Schema定义及69根分支全部读，63向量的输入/期望/历史actualValid/全部错误字段全读；基例与所有递归差异可逆还原逐值核对149+63对象零差异。这是阅读表示的本地文档校验，没有运行Schema校验器或任何归档程序。

独立7710行fixture的45完整对象与主文逐值相等；265行TypeScript与主文完整嵌入块逐行相等；948行AC的全部104条given/when/then/status等与原表逐项相等（80原始+24 directed，均NOT_RUN）。十二准确窗口的十八段按原始byte offset/length逐字节相等、SHA相符，所有包装说明另读；保留完整来源、行号、字节范围，不因缩排/行末差异错误升版。

R4 INDEX31、身份106、最终协调评审71、独审45、接受69、准确窗口manifest202、SOURCE-READ-RECEIPT58、FIX-MAP10、CHANGELOG9行全文；CURRENT262、SOURCE-MAP249、STATIC-CHECKS75全字段结构读。305条原包manifest身份与306 ZIP成员全部核准本地字节：当前R4规范/支持14、准确窗口13、历史候选/评审53、历史diff4、固定上游/源码/范围212、分配/发布9；另1为manifest自身。目录/身份检查不等上述历史或上游内容的全部语义阅读，静态结果保原作者归属。

全部精确范围、字段族、来源身份和未读历史边界见[operations-reading.json](D:/CP6/docs/CP6_开发设计文档_20261010/evidence/operations-reading.json)。没有运行归档脚本、编译、测试、数据库或Actions；实际参数/采用/切换与104业务AC未因此获证。
