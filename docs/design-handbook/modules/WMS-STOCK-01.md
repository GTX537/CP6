# WMS-STOCK-01 库存数量账与移动

整理状态：**required_materials_consolidated**。本册主文7429行全部业务语义、当前接受及共享合同已通过直接阅读和精确复用闭合；包括58个JSON全部新增字段及递归差异。InventoryQuantityKernel唯一写入、真实Owner共同门与157项场景仍须实际实现和验证。

## 1. 业务目的、操作者与权威边界

Stock回答“哪个真实来源的哪一份货，现在在哪、由谁拥有、能否用于这次动作”。它是InventoryQuantityKernel唯一数量写者。仓库人员查看库存/流水、从受权来源规划移动；有专门权限者申请纠错、冻结、恢复。机器入口分别接受IN、OUT、MAT、TRANSFER、Quality及控制Owner事实。预留选择策略归RSV，采购实收及商业接受归GR/IN，制造耗用依据归MAT，质量决定归QA，会计归Finance。库存成功和下游业务完成独立。[库存原文L1159](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/5a/5aed7ccb52e1878b__WMS-STOCK-01_库存数量账与移动_前后端开发Spec_v1.1.1_REVIEW.md:1159>)

盘点批准、工单领料、质量拒绝、交付签收均不能直接赋值Physical/Available。领到线边20是两地点MOVE，实际耗12才CONSUME；真正发运以SHIP消费指定Claim；交付证明不再扣量。[库存原文L1163](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/5a/5aed7ccb52e1878b__WMS-STOCK-01_库存数量账与移动_前后端开发Spec_v1.1.1_REVIEW.md:1163>)

## 2. 当前正文、强制组合与接受边界

主文为v1.1.1 REVIEW，SHA-256 `5aed7ccb52e1878b80dc45324e467437314f9157e55a17c10094a25e35421700`。当前准确选择继承原整合台账；用户接受正文、STK-DEC-01～08及三项COR，157项设计场景仍NOT_RUN。[用户接受L15](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d0/d03871181d9cbc53__HISTORY__handoffs__PLAN-REUSE-01B__documentation__MD02__acceptances__D-WMS__WMS-STOCK-01__UA-20260930-D-WMS-STOCK-MD01.md.txt:15>) [接受范围L41](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d0/d03871181d9cbc53__HISTORY__handoffs__PLAN-REUSE-01B__documentation__MD02__acceptances__D-WMS__WMS-STOCK-01__UA-20260930-D-WMS-STOCK-MD01.md.txt:41>) 不能因正文保留“提案/候选”而擅选其他版本。强制组合包括共同CURRENT选择器 `4581907f…` 和共同接受 `d69d65cd…`：H31 v0.7、MRX v0.2、OUT-EFFECTIVE v3及Stock correction discovery v1是追加接受，Stock原正文不替换。[共同接受L14](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d6/d69d65cde52092ea__SHARED_ACCEPTANCE.md:14>)

这些接受不证明运行。共同接受记录保留B/D真实Owner门UNVERIFIED及304业务AC NOT_RUN；Stock自己仍需真实来源、Claim同事务、IAM、质量、容量、切换实例。[共同接受L40](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d6/d69d65cde52092ea__SHARED_ACCEPTANCE.md:40>) [库存原文L7314](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/5a/5aed7ccb52e1878b__WMS-STOCK-01_库存数量账与移动_前后端开发Spec_v1.1.1_REVIEW.md:7314>)

## 3. 数据、身份、数量与版本轴

|对象|业务含义及约束|
|---|---|
|DimensionIdentity / InventoryPool|产品、收到时技术身份、货主、原用途、基本单位精确成组；Pool再限定Site。同品码不等同一库存身份。|
|QuantityRoot|一次明确来源实收的初始Q及维度不可普通更新；串号一根且不能分数移动。|
|QuantitySpan / StockSlice|根内半开区间`[from,to)`用于可追溯份额；当前互不重叠完整覆盖`[0,Q)`，位置为LOCATION/TRANSIT/EXTERNAL/CONSUMED/VOID。区间不是实际物品序号。|
|Claim / Binding|同一需求承诺容器；Bindings指准确Root范围和RESERVED/ALLOCATED/PICKED状态，不是另一份Physical账。|
|SourceAllowance / SourceOccurrenceLeaf|前者是来源授权总预算；后者是实际发生叶，跨包装/HTTP键防重复。版本变化不重开已消费额度。|
|LedgerGroup / Movement / Receipt|一次数量决定完整组；Movement才表示地点/终点改变，限制与Claim事件可无物理腿。Receipt不可变。|
|CapacityHead / OccupancyShare|地点/指标共享容量域及规范未收义务份额；Task/IN别名共用一份，不独立可消费。|

依据：[库存原文L81](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/5a/5aed7ccb52e1878b__WMS-STOCK-01_库存数量账与移动_前后端开发Spec_v1.1.1_REVIEW.md:81>) [库存原文L103](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/5a/5aed7ccb52e1878b__WMS-STOCK-01_库存数量账与移动_前后端开发Spec_v1.1.1_REVIEW.md:103>) [库存原文L1273](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/5a/5aed7ccb52e1878b__WMS-STOCK-01_库存数量账与移动_前后端开发Spec_v1.1.1_REVIEW.md:1273>) [库存原文L1291](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/5a/5aed7ccb52e1878b__WMS-STOCK-01_库存数量账与移动_前后端开发Spec_v1.1.1_REVIEW.md:1291>) [库存原文L1305](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/5a/5aed7ccb52e1878b__WMS-STOCK-01_库存数量账与移动_前后端开发Spec_v1.1.1_REVIEW.md:1305>) [库存原文L1381](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/5a/5aed7ccb52e1878b__WMS-STOCK-01_库存数量账与移动_前后端开发Spec_v1.1.1_REVIEW.md:1381>)。

单数量为decimal(21,8)，最多13整数位；聚合decimal(38,8)。JSON数量为十进制字符串，禁止指数和静默舍入；正常写入无法精确换算则拒绝。Pool、Claim、Restriction、Projection版本各有域，ETag不代替全部多对象前驱。[库存原文L166](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/5a/5aed7ccb52e1878b__WMS-STOCK-01_库存数量账与移动_前后端开发Spec_v1.1.1_REVIEW.md:166>) [库存原文L170](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/5a/5aed7ccb52e1878b__WMS-STOCK-01_库存数量账与移动_前后端开发Spec_v1.1.1_REVIEW.md:170>)

`Granted=Unbacked+OpenBacked+ConsumedNet+ReleasedNet`；`OpenBacked=ReservedUnallocated+AllocatedUnpicked+PickedNotIssued`。Free是本次用途下实存集合减去预留、质量、冻结、技术、地点限制的**并集**。P100、R30、Q25且交10，Free55；覆盖不明返回UNKNOWN/null，不能任选交集。[库存原文L113](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/5a/5aed7ccb52e1878b__WMS-STOCK-01_库存数量账与移动_前后端开发Spec_v1.1.1_REVIEW.md:113>) [库存原文L138](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/5a/5aed7ccb52e1878b__WMS-STOCK-01_库存数量账与移动_前后端开发Spec_v1.1.1_REVIEW.md:138>) [库存原文L154](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/5a/5aed7ccb52e1878b__WMS-STOCK-01_库存数量账与移动_前后端开发Spec_v1.1.1_REVIEW.md:154>)

## 4. 正常流程、状态转移与事务写集

```mermaid
flowchart LR
 A[受权Source及完整Manifest] --> B[SP01计划 / SP00具名准备]
 B --> C[冻结业务身份与完整输入]
 C --> D[受理Execution并取得全部最终门]
 D --> E[同事务Root Slice 全腿 Claim 预算 容量 Receipt Outbox]
 E --> F[提交后派送原结果]
 F --> G[Owner独立应用 / 原键恢复]
```

准备只创建不可变计划/FrozenIntent及必要空元数据，不能提前造Root或占实存。真正提交双向核Manifest父、part、leaf与SourceUse；同Allowance本批60+50要合计检查100上限，不逐行各自放过。[库存原文L923](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/5a/5aed7ccb52e1878b__WMS-STOCK-01_库存数量账与移动_前后端开发Spec_v1.1.1_REVIEW.md:923>) [库存原文L942](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/5a/5aed7ccb52e1878b__WMS-STOCK-01_库存数量账与移动_前后端开发Spec_v1.1.1_REVIEW.md:942>) [库存原文L948](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/5a/5aed7ccb52e1878b__WMS-STOCK-01_库存数量账与移动_前后端开发Spec_v1.1.1_REVIEW.md:948>)

|动作|同一事务的真实效果|不发生的效果|
|---|---|---|
|RECEIPT|新Root、LOCATION份额、来源消费、容量义务转化|不隐式预留|
|MOVE|原源负腿+目标正腿，Claim跟随原Root|不消费Claim，不转货主|
|TRANSIT_DISPATCH / RECEIVE|地点与同具名在途账户间分阶段实际移动|不新收第二Root，不将两阶段同时算实存|
|CLAIM_CREATE/BACKING|合法Grant登记、Binding与ClaimEvent、PoolRevision|不写Physical腿；Unbacked不扣现货|
|ALLOCATE / PICK|同Binding状态推进；真实拣货发生引用必需|不二次扣量；确有暂存搬位另做MOVE|
|SHIP / CONSUME|准确份额离开LOCATION并消费自己的Binding、来源额度|不借其他Claim，不靠异步UNRSV补扣|
|Claim RELEASE / REBIND|准确未耗释放；旧新Binding整组交换|释放不消除Hold；目标失败不先放旧占用|

依据：[库存原文L927](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/5a/5aed7ccb52e1878b__WMS-STOCK-01_库存数量账与移动_前后端开发Spec_v1.1.1_REVIEW.md:927>) [库存原文L999](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/5a/5aed7ccb52e1878b__WMS-STOCK-01_库存数量账与移动_前后端开发Spec_v1.1.1_REVIEW.md:999>) [库存原文L1009](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/5a/5aed7ccb52e1878b__WMS-STOCK-01_库存数量账与移动_前后端开发Spec_v1.1.1_REVIEW.md:1009>) [库存原文L1024](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/5a/5aed7ccb52e1878b__WMS-STOCK-01_库存数量账与移动_前后端开发Spec_v1.1.1_REVIEW.md:1024>) [库存原文L1034](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/5a/5aed7ccb52e1878b__WMS-STOCK-01_库存数量账与移动_前后端开发Spec_v1.1.1_REVIEW.md:1034>) [库存原文L1038](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/5a/5aed7ccb52e1878b__WMS-STOCK-01_库存数量账与移动_前后端开发Spec_v1.1.1_REVIEW.md:1038>)。

Execution状态`ACCEPTED/WAITING_EVIDENCE/READY/POSTED/REJECTED/SEALED_NO_EFFECT/RECONCILIATION_REQUIRED`与命令响应投影分开；Revision是可见流程版，Epoch是执行权接管版。确定REJECTED的原意图不会因后来放行而同键转成功。[库存原文L1430](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/5a/5aed7ccb52e1878b__WMS-STOCK-01_库存数量账与移动_前后端开发Spec_v1.1.1_REVIEW.md:1430>) [库存原文L1436](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/5a/5aed7ccb52e1878b__WMS-STOCK-01_库存数量账与移动_前后端开发Spec_v1.1.1_REVIEW.md:1436>)

质量应用采用独立Inbox与Application。REQUIRES_INSPECTION实收创建PENDING根；EXPLICIT_SKIP必须有准确策略证明，保SKIP原因并生成库存ACCEPTED应用，不声称检验PASS。先到Quality等待原Source Root；应用只改变资格/风险和Pool物理0事件。原100已发20后Hold[0,25)，当前受影响5、非当前20，不能伪造25在库Hold或回库。[库存原文L1048](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/5a/5aed7ccb52e1878b__WMS-STOCK-01_库存数量账与移动_前后端开发Spec_v1.1.1_REVIEW.md:1048>) [库存原文L1074](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/5a/5aed7ccb52e1878b__WMS-STOCK-01_库存数量账与移动_前后端开发Spec_v1.1.1_REVIEW.md:1074>) [库存原文L1078](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/5a/5aed7ccb52e1878b__WMS-STOCK-01_库存数量账与移动_前后端开发Spec_v1.1.1_REVIEW.md:1078>) [库存原文L1080](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/5a/5aed7ccb52e1878b__WMS-STOCK-01_库存数量账与移动_前后端开发Spec_v1.1.1_REVIEW.md:1080>)

错误冲正与真实退货分开。Correction绑定原事实、准确范围、候选版本/摘要及批准；一个MOVE两腿和相关Claim/预算解释必须整组逆向。反向预算有余不代表当前可逆；已耗或串号后继未处理时阻断。一个Proposal任一段失败则全组不应用。[库存原文L1107](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/5a/5aed7ccb52e1878b__WMS-STOCK-01_库存数量账与移动_前后端开发Spec_v1.1.1_REVIEW.md:1107>) [库存原文L1114](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/5a/5aed7ccb52e1878b__WMS-STOCK-01_库存数量账与移动_前后端开发Spec_v1.1.1_REVIEW.md:1114>) [库存原文L1118](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/5a/5aed7ccb52e1878b__WMS-STOCK-01_库存数量账与移动_前后端开发Spec_v1.1.1_REVIEW.md:1118>) [库存原文L1136](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/5a/5aed7ccb52e1878b__WMS-STOCK-01_库存数量账与移动_前后端开发Spec_v1.1.1_REVIEW.md:1136>) [库存原文L1146](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/5a/5aed7ccb52e1878b__WMS-STOCK-01_库存数量账与移动_前后端开发Spec_v1.1.1_REVIEW.md:1146>)

## 5. API、事件与Owner合同

公共根`/api/wms/stock/v1`，服务根`/internal/wms/stock/v1`。所有写有Idempotency-Key；编辑已有可变资源需单一强If-Match。Claim写入SP00/SC01–SC06使用内部根；用户Claim读取仍可公共。[库存原文L806](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/5a/5aed7ccb52e1878b__WMS-STOCK-01_库存数量账与移动_前后端开发Spec_v1.1.1_REVIEW.md:806>)

|调用|开发者应理解的输入/输出|
|---|---|
|SQ01 / SQ01a / SQ01b|取得授权Context、Pool元数据、完整SourceAction/Manifest；解析不造库存。|
|SQ02 / SQ04|余额查询与准确可选范围，仅观察不占用。|
|SP01→SP02|生成PostingPlan，再提交冻结PostingRequest；唯一内核过账。|
|SP00→SC01–SC06|具名Claim创建/背书/分配/拣货/释放/重绑准备后提交PreparedMutationRequest。|
|SX01–SX05|纠错草稿→修改→送审→准确批准绑定→准备并应用；批准不是已逆转。|
|SO01 / SO02 / SO03a|原locator查结果、恢复指定步骤、无operationId也可按准确原意图封闭。|
|SR00 / SR02|对账计划与冻结逐腿Resolution；禁止传目标余额直接SET。|
|内部quality-applications / actual-facts / inbox|原件接收、等待和应用分轴；接收成功不是数量或质量已应用。|

完整路由和原类型在[库存原文L810](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/5a/5aed7ccb52e1878b__WMS-STOCK-01_库存数量账与移动_前后端开发Spec_v1.1.1_REVIEW.md:810>)，不是由本摘要新造DTO。所有物理事件向Finance仅发送数量、来源、货主/技术、时间、反向关联及Receipt；不发送由Stock推导的会计科目或收入。[库存原文L1247](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/5a/5aed7ccb52e1878b__WMS-STOCK-01_库存数量账与移动_前后端开发Spec_v1.1.1_REVIEW.md:1247>)

容量合同要求RELATED或有完整覆盖证明的KNOWN_NONE；UNKNOWN不准新执行。物理60+未收40收到对应10后，物理70/未收30/总100；独立新收10则物理70/未收40/总110，不得任取旧义务抵扣。转化与Root、Allowance、CapacityHead、Receipt同事务；下游确认不得再次减未收。[库存原文L1215](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/5a/5aed7ccb52e1878b__WMS-STOCK-01_库存数量账与移动_前后端开发Spec_v1.1.1_REVIEW.md:1215>) [库存原文L1219](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/5a/5aed7ccb52e1878b__WMS-STOCK-01_库存数量账与移动_前后端开发Spec_v1.1.1_REVIEW.md:1219>) [库存原文L1223](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/5a/5aed7ccb52e1878b__WMS-STOCK-01_库存数量账与移动_前后端开发Spec_v1.1.1_REVIEW.md:1223>) [库存原文L1231](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/5a/5aed7ccb52e1878b__WMS-STOCK-01_库存数量账与移动_前后端开发Spec_v1.1.1_REVIEW.md:1231>)

## 6. 事务、并发、幂等与恢复

所有数量writer先声明资源闭包，按Canonical/全部SourceOccurrenceLeaf→祖先ScopeGate→规范细资源顺序取得最强锁；锁后资源变化即回滚重收集。跨产品也必须争同Location/Metric容量域。applock限定实际数据库/事务，负返回码回滚；同锁名在其他数据库不构成分布式锁。[库存原文L1416](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/5a/5aed7ccb52e1878b__WMS-STOCK-01_库存数量账与移动_前后端开发Spec_v1.1.1_REVIEW.md:1416>) [库存原文L1424](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/5a/5aed7ccb52e1878b__WMS-STOCK-01_库存数量账与移动_前后端开发Spec_v1.1.1_REVIEW.md:1424>)

共享Claim数量组件与Stock选同Main真实连接/事务；远程RSV只放申请/投影，不允许远库先改数再称原子。Source采用LOCAL_TRANSACTION或有准确独占委托的EXCLUSIVE_DELEGATION，仅查剩余数量不足。[库存原文L1169](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/5a/5aed7ccb52e1878b__WMS-STOCK-01_库存数量账与移动_前后端开发Spec_v1.1.1_REVIEW.md:1169>) [库存原文L1173](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/5a/5aed7ccb52e1878b__WMS-STOCK-01_库存数量账与移动_前后端开发Spec_v1.1.1_REVIEW.md:1173>)

HTTP CommandKey防请求重投，Canonical/Source叶防同一业务换键重复。Posting业务摘要EFFECT-2与RequestDigest分工：前者保真实来源、范围和业务语义，排除transport键/新planId/动态CAS；后者保原method/path/If-Match和完整收到请求。非Posting用ACTION-EFFECT-1，Correction候选摘要另域。不能统一成一个任意JSON hash。[库存原文L1432](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/5a/5aed7ccb52e1878b__WMS-STOCK-01_库存数量账与移动_前后端开发Spec_v1.1.1_REVIEW.md:1432>)

|故障|恢复规则|
|---|---|
|commit响应丢失|新连接查原Execution/Receipt；未观察或不可读仍UNKNOWN，不再生成源键。|
|已POSTED、MES/RSV/Finance回写失败|只补CONFIRM_SOURCE/REDELIVER或投影；不重发库存。|
|未执行且要关闭|原Owner出具SOURCE_EXECUTION_CLOSED；SO03/03a与旧worker争同Canonical+叶。已成功优先返回原成功，否则推进Epoch并写SEALED墓碑。|
|关闭已成功、解除Guard超时|墓碑继续阻晚执行，保RELEASE_PENDING；按原prepareKey和解除Action恢复。|
|现场事实已发生但正常门不满足|保存ActualFact并进入有权认账；SEALED只证明这个正常意图无本域效果，不证明实物未发生。|

依据：[库存原文L1441](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/5a/5aed7ccb52e1878b__WMS-STOCK-01_库存数量账与移动_前后端开发Spec_v1.1.1_REVIEW.md:1441>) [库存原文L1442](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/5a/5aed7ccb52e1878b__WMS-STOCK-01_库存数量账与移动_前后端开发Spec_v1.1.1_REVIEW.md:1442>) [库存原文L1444](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/5a/5aed7ccb52e1878b__WMS-STOCK-01_库存数量账与移动_前后端开发Spec_v1.1.1_REVIEW.md:1444>) [库存原文L1445](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/5a/5aed7ccb52e1878b__WMS-STOCK-01_库存数量账与移动_前后端开发Spec_v1.1.1_REVIEW.md:1445>) [库存原文L1449](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/5a/5aed7ccb52e1878b__WMS-STOCK-01_库存数量账与移动_前后端开发Spec_v1.1.1_REVIEW.md:1449>) [库存原文L1455](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/5a/5aed7ccb52e1878b__WMS-STOCK-01_库存数量账与移动_前后端开发Spec_v1.1.1_REVIEW.md:1455>) [库存原文L1463](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/5a/5aed7ccb52e1878b__WMS-STOCK-01_库存数量账与移动_前后端开发Spec_v1.1.1_REVIEW.md:1463>)。


### 历史、现场差异与持久消息

AS_KNOWN重建已知截止向量时的记录；EFFECTIVE_RESTATED仅使用该向量前已收到的事实重述业务时点。CAPTURE_NOW取得同DB快照中的完整Pool向量，EXACT_VECTOR不能把一个跨Pool原子组切成两半；缺事件为GAP，不拿今天余额补历史。[库存原文L1490](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/5a/5aed7ccb52e1878b__WMS-STOCK-01_库存数量账与移动_前后端开发Spec_v1.1.1_REVIEW.md:1490>)

ActualFact每腿保原正申报量/UOM，符号由direction决定，基本量未知为null。Root10加尚不能归属的实际OUT15可显示book=-5及RECONCILIATION_REQUIRED，这不是负实物或可用0。对账有三条明确路径：LINK_CONFIRMED_FACT在同地点/维度/UOM把Root与Suspense反向等量重分类；LINK_TO_EXISTING_POSTING凭准确别名只消除重复观察影响，不再动Root/Claim/Allowance；REVERSE_FALSE_FACT需要反证和源关闭，不能因难解释就标错。[库存原文L1502](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/5a/5aed7ccb52e1878b__WMS-STOCK-01_库存数量账与移动_前后端开发Spec_v1.1.1_REVIEW.md:1502>) [库存原文L1508](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/5a/5aed7ccb52e1878b__WMS-STOCK-01_库存数量账与移动_前后端开发Spec_v1.1.1_REVIEW.md:1508>) [库存原文L1527](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/5a/5aed7ccb52e1878b__WMS-STOCK-01_库存数量账与移动_前后端开发Spec_v1.1.1_REVIEW.md:1527>) [库存原文L1535](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/5a/5aed7ccb52e1878b__WMS-STOCK-01_库存数量账与移动_前后端开发Spec_v1.1.1_REVIEW.md:1535>) [库存原文L1537](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/5a/5aed7ccb52e1878b__WMS-STOCK-01_库存数量账与移动_前后端开发Spec_v1.1.1_REVIEW.md:1537>)

部分认账需先接管整事件未解Source预算；15先Hold15，认10后Consumed10/Held5，不能把余5交给其他动作。父Case可部分解决，每次冻结Plan全成全不成；全库MOVE净0不等两腿已闭合。[库存原文L1533](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/5a/5aed7ccb52e1878b__WMS-STOCK-01_库存数量账与移动_前后端开发Spec_v1.1.1_REVIEW.md:1533>) [库存原文L1549](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/5a/5aed7ccb52e1878b__WMS-STOCK-01_库存数量账与移动_前后端开发Spec_v1.1.1_REVIEW.md:1549>) [库存原文L1555](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/5a/5aed7ccb52e1878b__WMS-STOCK-01_库存数量账与移动_前后端开发Spec_v1.1.1_REVIEW.md:1555>)

消息同时保运输MessageId、业务事实键与来源流；同Message重发返回原Ingress+当前processing。先到Quality等Root依赖，依靠持久Wake与启动/定时扫表恢复。DELTA只沿AppliedSequence连续推进；ObservedHigh不是已应用，快照跨缺口须完整证明。Worker租约只是竞争手段，最终ClaimEpoch及原状态仍要锁内核。[库存原文L1619](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/5a/5aed7ccb52e1878b__WMS-STOCK-01_库存数量账与移动_前后端开发Spec_v1.1.1_REVIEW.md:1619>) [库存原文L1631](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/5a/5aed7ccb52e1878b__WMS-STOCK-01_库存数量账与移动_前后端开发Spec_v1.1.1_REVIEW.md:1631>) [库存原文L1641](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/5a/5aed7ccb52e1878b__WMS-STOCK-01_库存数量账与移动_前后端开发Spec_v1.1.1_REVIEW.md:1641>)

四类出站事件为quantity-group、claim-changed、eligibility-changed、correction-posted的v1；Consumer RECEIVED/APPLIED分别记录。投影修复同时CAS本地版和全部Pool/Source依赖头，只写读模型，不再Posting或Quality应用。[库存原文L1655](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/5a/5aed7ccb52e1878b__WMS-STOCK-01_库存数量账与移动_前后端开发Spec_v1.1.1_REVIEW.md:1655>) [库存原文L1657](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/5a/5aed7ccb52e1878b__WMS-STOCK-01_库存数量账与移动_前后端开发Spec_v1.1.1_REVIEW.md:1657>) [库存原文L1663](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/5a/5aed7ccb52e1878b__WMS-STOCK-01_库存数量账与移动_前后端开发Spec_v1.1.1_REVIEW.md:1663>)


### 与DEL证明准入的限定共同锁前缀

后继DEL-SOURCE-FENCE-1对Stock SX05及被Owner判定影响同原Movement解释的更正wrapper增加外层F0：以(Tenant,Mode,原StockMovement完整Ref)收集整个原子组，**在原Owner Canonical之前**取共同fence；然后保持原Canonical→Scope→Source相对顺序。相关Stock/OUT解释与DEL Proof准入共享同物理Connection、同SERIALIZABLE DbTransaction，成功解释同commit推进epoch，回滚不推进。旧入口已经持Canonical后不能补F0，缺wrapper采用必须拒相关准入。[DEL共同合同L15272](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/c3/c30b6885dde887be__WMS-DEL-01_承运与交付证明_前后端开发Spec_v1.3_R3_REVIEWED.md:15272>) [全writer锁序L15278](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/c3/c30b6885dde887be__WMS-DEL-01_承运与交付证明_前后端开发Spec_v1.3_R3_REVIEWED.md:15278>)

此限定只用于影响原Shipment解释的参与路由和DEL最终/当前组合读，不能推广为所有库存/出库业务全部采用SERIALIZABLE。DEL只读Stock/OUT并写自己的Proof，原Quantity/Correction审批/源关闭语义不变；最终新Stock cut核epoch/sourceSet/OUT heads后Proof/Permit同提交。真实共同采用仍UNPROVEN。[最终决定L15294](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/c3/c30b6885dde887be__WMS-DEL-01_承运与交付证明_前后端开发Spec_v1.3_R3_REVIEWED.md:15294>) [接受保留L169](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/1f/1fd0c2f53e99a13c__DEL_acceptance.md:169>) 详见[WMS-DEL-01](<D:/CP6/docs/CP6_开发设计文档_20261010/modules/WMS-DEL-01.md>)。

## 7. 权限、租户与敏感字段

权限取可信Tenant、Site/仓、货主/产品/技术范围与字段/动作能力交集；空授权不是全部。无可见权404、可见但无动作权403；合计、排序、导出也需字段权，不能先全租户聚合再隐藏明细。用户管理员不能代服务Producer签QA/MES事实。[库存原文L178](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/5a/5aed7ccb52e1878b__WMS-STOCK-01_库存数量账与移动_前后端开发Spec_v1.1.1_REVIEW.md:178>) [库存原文L182](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/5a/5aed7ccb52e1878b__WMS-STOCK-01_库存数量账与移动_前后端开发Spec_v1.1.1_REVIEW.md:182>)

operation.seal独立于read/post，仍必须原Owner关闭证据；source.resume只续既有CanonicalInbox，不许改Target/Sequence/正文。每表Tenant复合键及跨租户FK约束；RawJson不代租户、唯一身份、数量和状态typed列。[库存原文L180](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/5a/5aed7ccb52e1878b__WMS-STOCK-01_库存数量账与移动_前后端开发Spec_v1.1.1_REVIEW.md:180>) [库存原文L1267](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/5a/5aed7ccb52e1878b__WMS-STOCK-01_库存数量账与移动_前后端开发Spec_v1.1.1_REVIEW.md:1267>) [库存原文L1343](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/5a/5aed7ccb52e1878b__WMS-STOCK-01_库存数量账与移动_前后端开发Spec_v1.1.1_REVIEW.md:1343>) [库存原文L7416](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/5a/5aed7ccb52e1878b__WMS-STOCK-01_库存数量账与移动_前后端开发Spec_v1.1.1_REVIEW.md:7416>)

## 8. 页面交互与服务端校验

七工作面为库存工作台、身份份额详情、移动事实、授权移动、限制质量、纠错、历史对账恢复。工作台物理/在途/承诺/可用分栏，混UOM分组；质量页没有任意“改合格”下拉。查看旧成功时CurrentEligibility另列。[库存原文L190](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/5a/5aed7ccb52e1878b__WMS-STOCK-01_库存数量账与移动_前后端开发Spec_v1.1.1_REVIEW.md:190>)

查询区分AVAILABLE_EMPTY、PARTIAL、FAILED、ACCESS_LOST及NOT_CONFIGURED。写页EDITING→PLANNING→READY→SUBMITTING→RESULT_KNOWN，另有PLAN_STALE/RESULT_UNKNOWN；提交时冻结原请求，刷新仅保存locator/hash等非敏感信息。客户端Abort不证明后端取消。UNKNOWN只查原结果或重发准确原请求，不自动换Key。[库存原文L217](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/5a/5aed7ccb52e1878b__WMS-STOCK-01_库存数量账与移动_前后端开发Spec_v1.1.1_REVIEW.md:217>)

后端400严格未知字段/格式，409处理身份冲突/不足，412处理前驱，422处理质量/技术/覆盖/能力缺失，202/503表示待定恢复。超过500行/2000跨度/2MiB整组413，禁止偷偷分包。[库存原文L857](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/5a/5aed7ccb52e1878b__WMS-STOCK-01_库存数量账与移动_前后端开发Spec_v1.1.1_REVIEW.md:857>) [库存原文L1477](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/5a/5aed7ccb52e1878b__WMS-STOCK-01_库存数量账与移动_前后端开发Spec_v1.1.1_REVIEW.md:1477>)

## 9. 实施顺序与固定旧代码差异

按原DEV01–08推进：精确维度与查询→单一数量提交→共享Claim→质量/MASTER保护→来源物理链→纠错→历史/ActualFact→消息/导出/单writer切换。每阶段保原结果恢复，不先做泛型余额CRUD。[库存原文L7081](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/5a/5aed7ccb52e1878b__WMS-STOCK-01_库存数量账与移动_前后端开发Spec_v1.1.1_REVIEW.md:7081>)

旧码差异仅继承`GTX537/CP6@157630594e3371fe181955d2f6227ff3b6962c84`的静态记录，未核本次main：旧Stock的Available=P−Allocated、PENDING可分配；旧服务查询维度缺Owner、Math.Max削Allocated、commit后best-effort通知。旧服务已有ambient事务和双腿MOVE，不能误称完全无事务。[库存原文L7354](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/5a/5aed7ccb52e1878b__WMS-STOCK-01_库存数量账与移动_前后端开发Spec_v1.1.1_REVIEW.md:7354>)

切换按QuantityWriterBinding精确范围/Epoch，旧T_Stock只兼容读投影；所有命中专项writer适配或阻断。未解释负数/货主/技术保LegacyUnresolved，不伪造合格SELF初始采购收货。[库存原文L1257](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/5a/5aed7ccb52e1878b__WMS-STOCK-01_库存数量账与移动_前后端开发Spec_v1.1.1_REVIEW.md:1257>)

## 10. 验收场景与证据状态

本次没有执行业务验证。原文AC全部NOT_RUN；关系库并发/事务不能用EF InMemory或Stub成功替代。至少实现以下可观察证据：[库存原文L7120](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/5a/5aed7ccb52e1878b__WMS-STOCK-01_库存数量账与移动_前后端开发Spec_v1.1.1_REVIEW.md:7120>)

|场景|预期及禁止副作用|
|---|---|
|100收→预留30→分配30→拣20→发20|最终P80/OpenBacked10/Free70；分配/拣货不二扣。|
|Hold25与预留30交10|Free55；缺交集证据UNKNOWN/null。|
|两人同份额发货/释放|同最终门最多一个合法；不既发又释放给别人。|
|MOVE目标失败|两腿全回滚，无源先减。|
|新收实现旧未收份额|占容转化一次；别名不再减旧义务。|
|Source叶换HTTP包装重投|原Receipt一次；部分重叠清单不能变新业务。|
|旧worker与seal并发|先成功则回原成功；先seal则旧worker永不可写。|
|有read无seal / resume修改正文|403/400且无墓碑/应用新增。|

依据：[库存原文L7133](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/5a/5aed7ccb52e1878b__WMS-STOCK-01_库存数量账与移动_前后端开发Spec_v1.1.1_REVIEW.md:7133>) [库存原文L7140](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/5a/5aed7ccb52e1878b__WMS-STOCK-01_库存数量账与移动_前后端开发Spec_v1.1.1_REVIEW.md:7140>) [库存原文L7151](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/5a/5aed7ccb52e1878b__WMS-STOCK-01_库存数量账与移动_前后端开发Spec_v1.1.1_REVIEW.md:7151>) [库存原文L7157](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/5a/5aed7ccb52e1878b__WMS-STOCK-01_库存数量账与移动_前后端开发Spec_v1.1.1_REVIEW.md:7157>) [库存原文L7404](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/5a/5aed7ccb52e1878b__WMS-STOCK-01_库存数量账与移动_前后端开发Spec_v1.1.1_REVIEW.md:7404>) [库存原文L7424](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/5a/5aed7ccb52e1878b__WMS-STOCK-01_库存数量账与移动_前后端开发Spec_v1.1.1_REVIEW.md:7424>)。

## 11. 具体待确认事项

- STK-DEC-01～08已由后续用户接受记录确认为当前设计选择；正文保留的提案标题是历史状态。仍需验证Root份额到现场识别、真实UOM/容量实例与旧维度映射，不能将实例缺失写成设计选择未接受。[准确接受L15](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d0/d03871181d9cbc53__HISTORY__handoffs__PLAN-REUSE-01B__documentation__MD02__acceptances__D-WMS__WMS-STOCK-01__UA-20260930-D-WMS-STOCK-MD01.md.txt:15>)[库存原文L7297](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/5a/5aed7ccb52e1878b__WMS-STOCK-01_库存数量账与移动_前后端开发Spec_v1.1.1_REVIEW.md:7297>)
- Source必须签完整执行清单/真实叶与关闭权；Task/IN需精确未收份额委托及KNOWN_NONE证据。不能拿总Allowance或快速HTTP查剩余替代。[库存原文L7310](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/5a/5aed7ccb52e1878b__WMS-STOCK-01_库存数量账与移动_前后端开发Spec_v1.1.1_REVIEW.md:7310>)
- Quality即时撤销要求相关writer真实同最终门；QA-POL策略门与IQC实际结果当前门的后继采用还需跨册逐字段合并，本页不以Stock旧SKIP字样覆盖QA专业定义。
- 当前类型、查询和全部规范案例的语义阅读已完成。尚待实际接入和验证的范围不因本次文书闭合自动通过。

## 12. 来源与实际阅读覆盖

原件绝对路径和一基行号随规则给出。继承来源为[D盘前次阅读台账](<D:/CP6/docs/CP6_编码前整合_20261009/agents/operations-targets.json>)，继承不冒充本轮重读。

本组原已读174–234、804–883、995–1670、7077–7168、7291–7429及继承77–173、917–994等范围保持原归属；平台独立补齐1–76、235–803、884–916、1671–7076、7169–7290，58个JSON按完整新增字段/精确重复子树读取，两册合计90块无损还原0差。7429行主文没有未关闭实质章节，未把同构JSON的逐行重新打印当必要阅读。

[Stock/RSV完整补册](D:/CP6/docs/CP6_开发设计文档_20261010/contracts/WMS-STOCK-RSV-数量与恢复附册.md)归纳完整数量演进、Claim/容量/Seal/质量恢复与界面分支；[29源精确复用证据](D:/CP6/docs/CP6_开发设计文档_20261010/evidence/WMS-STOCK-RSV-supplement-reading.json)记录各源完整SHA、精确窗口、6份短规范和3份长fixture的镜像核对。三H31夹具491节点/229案例/158原件全部语义已读。真实部署/专业门和业务验证仍另列，原文NOT_RUN不变。

逐源记录见[operations-reading.json](D:/CP6/docs/CP6_开发设计文档_20261010/evidence/operations-reading.json)。
