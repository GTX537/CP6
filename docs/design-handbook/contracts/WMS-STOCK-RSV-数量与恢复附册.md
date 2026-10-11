# WMS-STOCK / RSV：数量、准备与恢复的开发附册

本册补齐两个模块当前主文此前未读的封闭类型、连续请求链、证据样例和验收反例；与 [Stock模块](<D:/CP6/docs/CP6_开发设计文档_20261010/modules/WMS-STOCK-01.md>)、[RSV模块](<D:/CP6/docs/CP6_开发设计文档_20261010/modules/WMS-RSV-01.md>) 合读。主文语义现已覆盖全部 7429 / 4070 行；其中 90 个 JSON 按完整新增字段和值阅读，272 处相同子树精确复用，还原核对 90 / 90 一致。这里没有运行接口、数据库、业务测试或来源检查器。共同 H31 的三份大型夹具已由 operations 组完成全业务字段阅读（491 个可逆节点），本册按准确 SHA 和内嵌切片核对后复用；两目标当前必要材料的语义阅读已闭合，真实运行门仍保留。

## 1. 准确版本和 Owner 分工

Stock 固定 v1.1.1、SHA `5aed7ccb52e1878b80dc45324e467437314f9157e55a17c10094a25e35421700`；RSV 固定 v1.1.2、SHA `43eb8a01d98631beb15c85d524125f2b848cb4f2e106170edf132aa417c50cfd`。原文件名 REVIEW / REPAIR_CANDIDATE 和模拟段历史“尚待接受”不覆盖后来 CURRENT / UA。Stock 原接受及 H31 有界补充均保留；RSV D-RSV-01～14 已接受，O-RSV-01～12 实例仍未证明。[Stock CURRENTL7](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/90/90b6c19cd6ab391f__CURRENT.json:7>) [RSV CURRENTL12](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/2e/2e201da3fa9c96e9__CURRENT.json:12>) [运行边界L56](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/2e/2e201da3fa9c96e9__CURRENT.json:56>)

库存唯一数量内核负责 Root、位置、Movement、Claim 及数量 Receipt；RSV 负责真实来源请求、动作、选取计划、路由、Demand 映射和下游使用协调。RSV 数量部分与 Stock 同真实数据库连接和事务；远端 RSV 只能承载申请/投影，不能先远程改量再声称原子。Quality 决定、工程技术/用途、MASTER 位置/容量、源预算和会计处理各保有自己的权威。[StockL25](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/5a/5aed7ccb52e1878b__WMS-STOCK-01_库存数量账与移动_前后端开发Spec_v1.1.1_REVIEW.md:25>) [StockL1169](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/5a/5aed7ccb52e1878b__WMS-STOCK-01_库存数量账与移动_前后端开发Spec_v1.1.1_REVIEW.md:1169>)

操作者从有权来源准备动作，不能编辑余额或制造 Stock/Claim ID；服务端返回准备记录和下一条请求，页面仅提交原引用。后台集成者按登记 Producer、租户和范围调用内部入口；浏览器使用 RSV public 路由。[StockL534](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/5a/5aed7ccb52e1878b__WMS-STOCK-01_库存数量账与移动_前后端开发Spec_v1.1.1_REVIEW.md:534>) [RSVL1402](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/43/43eb8a01d98631be__WMS-RSV-01_库存预留与批次分配_前后端开发Spec_v1.1.2_REPAIR_CANDIDATE.md:1402>) [RSVL1870](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/43/43eb8a01d98631be__WMS-RSV-01_库存预留与批次分配_前后端开发Spec_v1.1.2_REPAIR_CANDIDATE.md:1870>)

## 2. 必须分开的身份与数据字段

|对象|实现必须保存的字段/身份|不能替代的概念|
|---|---|---|
|StockDimensions|Site、产品 Ref、技术 KNOWN/NOT_APPLICABLE/UNKNOWN、SELF/CUSTOMER Owner、原用途、基本 UOM/精度、Lot/Serial/HandlingUnit|同 SKU 不等同一数量池；客户货不是 SELF|
|QuantitySpan|rootId 与半开 from/to；Root 原坐标不随移动重建|范围是账内份额，不默认物理序列号|
|WireSource|producer/sourceKind/documentId/lineId/executionKey/sourceVersion 六字段|HTTP key、显示单号、随机新版本不是新的发生事实|
|OccurrenceLeaf|producer/occurrenceKey/effectKey，规范 Producer 别名归一后唯一|Manifest 改包装、换 SourceVersion 不能重复消费同叶|
|ExecutionIdentity|locator + writeKind + businessSchema + businessDigest|operationId 是受理结果，不是查未知提交的前置输入|
|RequestOutcome|原请求接受量/未接受终态量、对应 Demand ranges、Claim 和 Stock Receipt|不是当前 Unbacked/Open/Consumed/Released 快照|
|Reservation bindings|bindingId、allocationId、完整 Place、Root spans、当前状态|非 LOCATION 时不伪填仓库/库位|
|Plan/Intent|冻结 body、业务摘要、技术引用、原 CAS/依赖向量、权限/批准引用|不是临时 UI 数据，后继不能悄换原 CAS 或来源范围|

具体类型见 [StockL239](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/5a/5aed7ccb52e1878b__WMS-STOCK-01_库存数量账与移动_前后端开发Spec_v1.1.1_REVIEW.md:239>) [StockL468](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/5a/5aed7ccb52e1878b__WMS-STOCK-01_库存数量账与移动_前后端开发Spec_v1.1.1_REVIEW.md:468>) [RSVL237](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/43/43eb8a01d98631be__WMS-RSV-01_库存预留与批次分配_前后端开发Spec_v1.1.2_REPAIR_CANDIDATE.md:237>) [RSVL262](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/43/43eb8a01d98631be__WMS-RSV-01_库存预留与批次分配_前后端开发Spec_v1.1.2_REPAIR_CANDIDATE.md:262>)。所有 wire 拒未知成员；必填 nullable 要显式 null。数量按十进制文本精确处理，Stock 单量最多 13 整数位/8 小数位，聚合最多 30/8；版本 Int64 是字符串，非初态不得零。Digest 是内容身份，不是授权签名。[StockL235](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/5a/5aed7ccb52e1878b__WMS-STOCK-01_库存数量账与移动_前后端开发Spec_v1.1.1_REVIEW.md:235>) [RSVL191](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/43/43eb8a01d98631be__WMS-RSV-01_库存预留与批次分配_前后端开发Spec_v1.1.2_REPAIR_CANDIDATE.md:191>)

每行收货仅提交来源许可、合法维度、地点、输入量/UOM 和质量依据；新 RootId 由提交结果给出。RECEIPT、MOVE、TRANSIT_DISPATCH/RECEIVE、SHIP、CONSUME、ADJUSTMENT、RETURN 是封闭八种 data。ADJUSTMENT GAIN 提供维度并新建根；LOSS 引现存 spans，不接“目标余额”。退回保新质量依据，不复用过期 ACCEPTED。单次最多 500 行、2000 spans、2 MiB，超限整次拒绝，不能暗拆原子来源。[StockL313](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/5a/5aed7ccb52e1878b__WMS-STOCK-01_库存数量账与移动_前后端开发Spec_v1.1.1_REVIEW.md:313>) [StockL363](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/5a/5aed7ccb52e1878b__WMS-STOCK-01_库存数量账与移动_前后端开发Spec_v1.1.1_REVIEW.md:363>)

## 3. 四种摘要和两种“准备”不能混用

|摘要/准备|覆盖内容|排除或保留要求|
|---|---|---|
|Source Manifest / lineBusinessDigest|准确完整父源、逐行发生叶、量/UOM/Allowance 与业务行|行摘要只去掉 ClaimSpend.expectedClaimRevision；仍保 Claim/binding/spans|
|STK-ACTION-EFFECT-1|动作类型、真实 locator、目标和业务输入|动态 CAS 不进业务摘要；完整冻结执行输入仍保存 CAS|
|RSV-FROZEN-PLAN-1 的 PlanRef.digest|完整冻结计划，不含自引用/展示候选|包含 expectedClaimRevision、expectedPools、依赖；不同于 RSV Plan.BusinessDigest|
|ManualSelectionApproval|Plan digest、量/spans digest、Action、范围、Demand/Claim、路由/selector、权限和有效期|可空 businessDigest 不是省略 planDigest；非空必须是 RSV 业务摘要，不能用 Stock 摘要|
|SP00 / RP02|先保存不可变准备与下一请求|无物理/Claim 数量效果；SP00 不是任意 URL 或 SQL 执行器|

依据：[StockL509](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/5a/5aed7ccb52e1878b__WMS-STOCK-01_库存数量账与移动_前后端开发Spec_v1.1.1_REVIEW.md:509>) [StockL534](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/5a/5aed7ccb52e1878b__WMS-STOCK-01_库存数量账与移动_前后端开发Spec_v1.1.1_REVIEW.md:534>) [RSVL500](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/43/43eb8a01d98631be__WMS-RSV-01_库存预留与批次分配_前后端开发Spec_v1.1.2_REPAIR_CANDIDATE.md:500>) [RSVL344](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/43/43eb8a01d98631be__WMS-RSV-01_库存预留与批次分配_前后端开发Spec_v1.1.2_REPAIR_CANDIDATE.md:344>)。Stock 的 ActionDefinition 核心摘要与包含权威证据的动作业务摘要也不同；不要拿“同为 64 hex”强行合并公共 DTO。H31 permitBusinessDigest 又有独立域，保真实批准/协议/Manifest 引用，仅排动态前驱。[StockL496](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/5a/5aed7ccb52e1878b__WMS-STOCK-01_库存数量账与移动_前后端开发Spec_v1.1.1_REVIEW.md:496>) [OUT内嵌合同L684](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/7e/7e25b78ea1261553__WMS-OUT-01_出库拣配包装与发运_前后端开发Spec_v1.3_R3_REVIEW_CANDIDATE.md:684>)

RSV 登记 SourceRequest → 创建 ActionDraft → RP01 产生候选/冻结 Plan → RP02 核人工批准并保完整 Stock preparation → RA03 凭原 intentRef 提交。用户指定策略必须是当前真实策略；null 表示解析当前后冻结，不能在最终提交换成新选取结果。[RSVL303](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/43/43eb8a01d98631be__WMS-RSV-01_库存预留与批次分配_前后端开发Spec_v1.1.2_REPAIR_CANDIDATE.md:303>) [RSVL1855](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/43/43eb8a01d98631be__WMS-RSV-01_库存预留与批次分配_前后端开发Spec_v1.1.2_REPAIR_CANDIDATE.md:1855>) [RSVL1870](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/43/43eb8a01d98631be__WMS-RSV-01_库存预留与批次分配_前后端开发Spec_v1.1.2_REPAIR_CANDIDATE.md:1870>)

手工选择批准在 RP02 和 RA03 都重核当前权威、资格、完整绑定、未撤销/替代及有效期。快照 revokeRef=null 不证明当前有效；RA03 不接受新 manualSelectionApproval 覆盖已冻结批准。自动选择、纯逻辑、释放按规定允许 null，并非略过 Source/QC 门。[RSVL593](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/43/43eb8a01d98631be__WMS-RSV-01_库存预留与批次分配_前后端开发Spec_v1.1.2_REPAIR_CANDIDATE.md:593>) [RSVL3786](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/43/43eb8a01d98631be__WMS-RSV-01_库存预留与批次分配_前后端开发Spec_v1.1.2_REPAIR_CANDIDATE.md:3786>)

## 4. Claim 算术与实际写集

Claim 数量守恒为 `Granted = Unbacked + OpenBacked + ConsumedNet + ReleasedNet`。OpenBacked 的 RESERVED、ALLOCATED、PICKED 是同份额的互斥阶段；Allocation/Pick 不能再扣一次。背书从 Unbacked 转入 OpenBacked，实际 SHIP/CONSUME 才同时减少物理量和 Claim 未耗量。[StockL369](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/5a/5aed7ccb52e1878b__WMS-STOCK-01_库存数量账与移动_前后端开发Spec_v1.1.1_REVIEW.md:369>) [RSVL266](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/43/43eb8a01d98631be__WMS-RSV-01_库存预留与批次分配_前后端开发Spec_v1.1.2_REPAIR_CANDIDATE.md:266>) [RSVL2067](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/43/43eb8a01d98631be__WMS-RSV-01_库存预留与批次分配_前后端开发Spec_v1.1.2_REPAIR_CANDIDATE.md:2067>)

Stock A 链是收货 100（待检，Free=0）→质量接受（物理 delta=0，Free=100）→背书 30 →分配同 30 →拣 20 →发 20。最终 P=80、Open=10、Consumed=20、Free=70。再释放未耗 [20,25) 的 5，P 仍 80、Open=5、Released=5、Free=75。新 HTTP key + 新 CAS 重放同 ReleaseAction 返回原 Receipt；改成 [25,30) 必须新的真实动作。[StockL3796](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/5a/5aed7ccb52e1878b__WMS-STOCK-01_库存数量账与移动_前后端开发Spec_v1.1.1_REVIEW.md:3796>) [StockL3918](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/5a/5aed7ccb52e1878b__WMS-STOCK-01_库存数量账与移动_前后端开发Spec_v1.1.1_REVIEW.md:3918>) [StockL4721](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/5a/5aed7ccb52e1878b__WMS-STOCK-01_库存数量账与移动_前后端开发Spec_v1.1.1_REVIEW.md:4721>) [StockL5226](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/5a/5aed7ccb52e1878b__WMS-STOCK-01_库存数量账与移动_前后端开发Spec_v1.1.1_REVIEW.md:5226>) [StockL5431](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/5a/5aed7ccb52e1878b__WMS-STOCK-01_库存数量账与移动_前后端开发Spec_v1.1.1_REVIEW.md:5431>)

RSV 主链另有明确逻辑承诺：CREATE Granted=60/U=60/Open=0，P/Free=100/100，原请求已全部接受 60。随后 FEFO 背书 A40+B20、分配同 60、PICK A25、SHIP 同 25、释放 B10：

|步骤|Pool / Claim 版|U / Open / Consumed / Released|P / Free|
|---|---|---|---|
|CREATE 逻辑|p3 / c1|60 / 0 / 0 / 0|100 / 100|
|BACK A40+B20|p4 / c2|0 / 60 / 0 / 0|100 / 40|
|ALLOCATE / PICK|p5/c3 → p6/c4|0 / 60 / 0 / 0|100 / 40|
|SHIP A25|p7 / c5|0 / 35 / 25 / 0|75 / 40|
|RELEASE B10|p8 / c6|0 / 25 / 25 / 10|75 / 50|

表中全部来自隔离示例，不是实测。RequestOutcome 原“接受60”不因释放10或未背书变成“未接受”。部分最终接受40/未接受20则是另一模式；余20需要来源新 RequestId 并反链原 Outcome/range。[RSVL1627](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/43/43eb8a01d98631be__WMS-RSV-01_库存预留与批次分配_前后端开发Spec_v1.1.2_REPAIR_CANDIDATE.md:1627>) [RSVL2069](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/43/43eb8a01d98631be__WMS-RSV-01_库存预留与批次分配_前后端开发Spec_v1.1.2_REPAIR_CANDIDATE.md:2069>) [RSVL2761](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/43/43eb8a01d98631be__WMS-RSV-01_库存预留与批次分配_前后端开发Spec_v1.1.2_REPAIR_CANDIDATE.md:2761>)

释放需要原来源 ReleaseDecision 和准确 NoFurtherUse。UNBACKED 用 Demand ranges；BACKED 用 Root spans，不混量。下游 PICK/SHIP 是不同阶段，可引用同 spans，但不能当两份数量。完整 ParticipantSet、预登记 Use、旧 writer 围栏和所有 pending/executed 结果必须核齐；outcomes=[] 只有确证从未派发才合法。已消费段不能释放，已拣执行段要独立处置。取消或过期先阻新用，不自动释放 Open。[RSVL307](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/43/43eb8a01d98631be__WMS-RSV-01_库存预留与批次分配_前后端开发Spec_v1.1.2_REPAIR_CANDIDATE.md:307>) [RSVL446](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/43/43eb8a01d98631be__WMS-RSV-01_库存预留与批次分配_前后端开发Spec_v1.1.2_REPAIR_CANDIDATE.md:446>) [RSVL2111](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/43/43eb8a01d98631be__WMS-RSV-01_库存预留与批次分配_前后端开发Spec_v1.1.2_REPAIR_CANDIDATE.md:2111>) [RSVL2806](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/43/43eb8a01d98631be__WMS-RSV-01_库存预留与批次分配_前后端开发Spec_v1.1.2_REPAIR_CANDIDATE.md:2806>)

## 5. 容量承接不是把实收和未收简单相加

服务器按 Source Manifest/Occurrence/输入范围解析每行容量关系，客户端不能任选 Task 或未收义务。每段属于 MATCHED_OBLIGATION、KNOWN_NONE 或 UNKNOWN；整行输入/基本量范围须完整不重叠，所有必要 metric 都要有精确转换与容量域。[StockL603](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/5a/5aed7ccb52e1878b__WMS-STOCK-01_库存数量账与移动_前后端开发Spec_v1.1.1_REVIEW.md:603>)

MATCHED_OBLIGATION 绑定 OwnerLineageShare、shareRange/UOM、转换和独占委托。Task-A 与 IN-A 若是同一个未收 share 的别名，不能重复算两份。示例物理60+未收40=100，实收同 share10后物理70+未收30=100；另一个 Manifest/HTTP key 也不能再兑现同 [0,10)。数量、share 实现、SourceAllowance、CapacityHead 和 Receipt 在同提交点变化。[StockL5823](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/5a/5aed7ccb52e1878b__WMS-STOCK-01_库存数量账与移动_前后端开发Spec_v1.1.1_REVIEW.md:5823>) [StockL5933](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/5a/5aed7ccb52e1878b__WMS-STOCK-01_库存数量账与移动_前后端开发Spec_v1.1.1_REVIEW.md:5933>) [StockL6020](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/5a/5aed7ccb52e1878b__WMS-STOCK-01_库存数量账与移动_前后端开发Spec_v1.1.1_REVIEW.md:6020>)

KNOWN_NONE 必须有完整 Owner/别名集合及独立 Occurrence 的无关联证明；示例真正独立新增10应为70+40=110，上限105则拒绝。UNKNOWN 不得降成 NONE。关系稳定语义进入业务摘要，mappingRevision/当前 CAS 另保；语义变更不得默默重绑定原 FrozenIntent。[StockL6022](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/5a/5aed7ccb52e1878b__WMS-STOCK-01_库存数量账与移动_前后端开发Spec_v1.1.1_REVIEW.md:6022>) [StockL6024](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/5a/5aed7ccb52e1878b__WMS-STOCK-01_库存数量账与移动_前后端开发Spec_v1.1.1_REVIEW.md:6024>) [StockL6043](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/5a/5aed7ccb52e1878b__WMS-STOCK-01_库存数量账与移动_前后端开发Spec_v1.1.1_REVIEW.md:6043>)

既有 Root 的 MOVE/TRANSIT_RECEIVE/RETURN 按 rootOrdinalMap 和完整净腿算变化，不能因是“收进”就新建 Root 或随意扣未收量。单事务接不住真实容量 Owner 时，相应新效果门保持关闭。[StockL655](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/5a/5aed7ccb52e1878b__WMS-STOCK-01_库存数量账与移动_前后端开发Spec_v1.1.1_REVIEW.md:655>)

## 6. 未知提交、Seal、消息恢复

提交前保存真实业务 locator/digest/intentRef/HTTP key。SO01 / RO01 不需要先知道 operationId；NOT_OBSERVED（200）和 UNAVAILABLE（503）都保 commitKnowledge=UNKNOWN，不能归还预算、重新申请或推断无效果。不可见对象 404 也不能当作不存在。[StockL575](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/5a/5aed7ccb52e1878b__WMS-STOCK-01_库存数量账与移动_前后端开发Spec_v1.1.1_REVIEW.md:575>) [RSVL382](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/43/43eb8a01d98631be__WMS-RSV-01_库存预留与批次分配_前后端开发Spec_v1.1.2_REPAIR_CANDIDATE.md:382>) [StockL5435](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/5a/5aed7ccb52e1878b__WMS-STOCK-01_库存数量账与移动_前后端开发Spec_v1.1.1_REVIEW.md:5435>)

Seal 必须原 Owner 的 SOURCE_EXECUTION_CLOSED，封完整 Canonical/叶及所有别名旧 worker；封闭者和原执行争同决定点。墓碑先提交则 SEALED_NO_EFFECT，迟到 worker 不能产生量；原提交先胜只能回原 SUCCEEDED/COMMITTED。Seal 只证明本意图未来不再正常产生数量效果，不删除 Raw ActualFact，不证明设备没动作，也不代已成功 Claim 的独立释放。[StockL5482](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/5a/5aed7ccb52e1878b__WMS-STOCK-01_库存数量账与移动_前后端开发Spec_v1.1.1_REVIEW.md:5482>) [StockL5506](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/5a/5aed7ccb52e1878b__WMS-STOCK-01_库存数量账与移动_前后端开发Spec_v1.1.1_REVIEW.md:5506>) [StockL5565](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/5a/5aed7ccb52e1878b__WMS-STOCK-01_库存数量账与移动_前后端开发Spec_v1.1.1_REVIEW.md:5565>) [RSVL2731](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/43/43eb8a01d98631be__WMS-RSV-01_库存预留与批次分配_前后端开发Spec_v1.1.2_REPAIR_CANDIDATE.md:2731>)

Quality 接收和业务应用分层：原 messageId / 原 wire / ingressReceipt / receivedAt 不变，processingRevision、waitingOn、nextAttemptAt、applicationReceipt 可续办。Quality 先到而 Root 未到为 PENDING_SOURCE；根提交写唤醒，即使通知丢失或重启，也从持久依赖扫描找到原槽。seq3 先到等 seq2，ObservedHigh=3 不等 AppliedSequence=3。权限撤销可 BLOCKED_AUTH，不抹原接收。[StockL770](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/5a/5aed7ccb52e1878b__WMS-STOCK-01_库存数量账与移动_前后端开发Spec_v1.1.1_REVIEW.md:770>) [StockL5629](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/5a/5aed7ccb52e1878b__WMS-STOCK-01_库存数量账与移动_前后端开发Spec_v1.1.1_REVIEW.md:5629>) [StockL5708](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/5a/5aed7ccb52e1878b__WMS-STOCK-01_库存数量账与移动_前后端开发Spec_v1.1.1_REVIEW.md:5708>) [StockL5821](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/5a/5aed7ccb52e1878b__WMS-STOCK-01_库存数量账与移动_前后端开发Spec_v1.1.1_REVIEW.md:5821>)

UI 应分别显示接收凭证、处理中/等待前驱、Stock 原结果和下游投影状态。Receipt 已提交但消费者 PENDING 时只重投原 Receipt 或确认，不能再次扣库存。[StockL3735](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/5a/5aed7ccb52e1878b__WMS-STOCK-01_库存数量账与移动_前后端开发Spec_v1.1.1_REVIEW.md:3735>) [RSVL1518](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/43/43eb8a01d98631be__WMS-RSV-01_库存预留与批次分配_前后端开发Spec_v1.1.2_REPAIR_CANDIDATE.md:1518>)

## 7. 错记冲正与已发生事实认账

真实退回是新发生事实，错记更正是精确反向原记录。Stock B 链同时有 Claim[0,30)、质量 Hold[20,45)、本域 Hold[25,40)，受限并集45而非30+25+15；Free=55。错误多收 [95,100) 的5无后继，获准确批准/SourceClosure后追加−5到 VOID；Root 初始量仍100、当前95、Free50，源上限/净用改95，不重开可收5。原[0,5)有 Claim 则拒，重叠[94,98)整次拒而不是暗改只冲1。[StockL4893](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/5a/5aed7ccb52e1878b__WMS-STOCK-01_库存数量账与移动_前后端开发Spec_v1.1.1_REVIEW.md:4893>) [StockL5176](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/5a/5aed7ccb52e1878b__WMS-STOCK-01_库存数量账与移动_前后端开发Spec_v1.1.1_REVIEW.md:5176>) [StockL5178](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/5a/5aed7ccb52e1878b__WMS-STOCK-01_库存数量账与移动_前后端开发Spec_v1.1.1_REVIEW.md:5178>) [StockL5207](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/5a/5aed7ccb52e1878b__WMS-STOCK-01_库存数量账与移动_前后端开发Spec_v1.1.1_REVIEW.md:5207>)

ActualFact 先保原发生时间、报量、原单位、各腿/原子组和 Ingress；不是再次驱动设备。PAIRED_MOVE 两腿15可共同解析10，但不能只解析 OUT10；原单位 reportedRange 与 Root 基本 spans 必须精确转换。解析事务先取得正常 Source/Leaf 接管并保未解决预算，随后两腿同提交、Hold15→5、Consumed0→10。[StockL664](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/5a/5aed7ccb52e1878b__WMS-STOCK-01_库存数量账与移动_前后端开发Spec_v1.1.1_REVIEW.md:664>) [StockL6045](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/5a/5aed7ccb52e1878b__WMS-STOCK-01_库存数量账与移动_前后端开发Spec_v1.1.1_REVIEW.md:6045>) [StockL6360](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/5a/5aed7ccb52e1878b__WMS-STOCK-01_库存数量账与移动_前后端开发Spec_v1.1.1_REVIEW.md:6360>) [StockL6888](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/5a/5aed7ccb52e1878b__WMS-STOCK-01_库存数量账与移动_前后端开发Spec_v1.1.1_REVIEW.md:6888>)

例中 A rooted100→90、Suspense−15→−5，book始终85；B rooted0→10、Suspense+15→+5，book始终15。每腿余5且 Case PARTIALLY_RESOLVED，不能因全库 signed 合计0就关闭。已经正常入账的真实原腿仅走 LINK_EXISTING_POSTING 精确别名，不再改变 Root/Claim/Allowance/Capacity；REVERSE_FALSE_FACT 须证明原观察错误，不能因找不到 Root 就选它。[StockL6199](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/5a/5aed7ccb52e1878b__WMS-STOCK-01_库存数量账与移动_前后端开发Spec_v1.1.1_REVIEW.md:6199>) [StockL6891](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/5a/5aed7ccb52e1878b__WMS-STOCK-01_库存数量账与移动_前后端开发Spec_v1.1.1_REVIEW.md:6891>) [StockL7070](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/5a/5aed7ccb52e1878b__WMS-STOCK-01_库存数量账与移动_前后端开发Spec_v1.1.1_REVIEW.md:7070>) [StockL7074](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/5a/5aed7ccb52e1878b__WMS-STOCK-01_库存数量账与移动_前后端开发Spec_v1.1.1_REVIEW.md:7074>)

## 8. 读模型、权限和界面必需分支

BalanceQuery 必须明确 CURRENT 或 PLANNED.at，以及准确 Owner/用途/技术/范围；没有 use 时只展示物理，不默认“可用”。单页同数据库快照，跨页不是已冻结历史；Root 分页不等完整池证明。聚合按 UOM/数量池分组，权限限制字段为 null+redactedFields，不补0。未来缺模型为 UNKNOWN，不把当前 Free 当 ATP。[StockL884](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/5a/5aed7ccb52e1878b__WMS-STOCK-01_库存数量账与移动_前后端开发Spec_v1.1.1_REVIEW.md:884>)

RSV 候选先策略匹配，再资格、范围和精确容量；SINGLE_LOT 允许同批多个位置，SINGLE_WAREHOUSE 选择能满足整个请求的第一合法仓，不私拆30+30。高优先级 UNKNOWN 不能跳过、缺到期日不能当无限、人工批准不能越 QC。旧计划 POOL_STALE 保原确定拒绝，不以新 Pool 自动削量补5/10。[RSVL3868](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/43/43eb8a01d98631be__WMS-RSV-01_库存预留与批次分配_前后端开发Spec_v1.1.2_REPAIR_CANDIDATE.md:3868>) [RSVL2567](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/43/43eb8a01d98631be__WMS-RSV-01_库存预留与批次分配_前后端开发Spec_v1.1.2_REPAIR_CANDIDATE.md:2567>)

页面应并列原请求 Outcome、当前 Claim 数量/资格、原冻结 Plan/批准、下游 Use 及恢复信息。可见且 enabled 是当前展示，不代最终权限/资格检查。Stock历史查询与新写权限分开，旧成功须保留；下一读已无范围权时不得继续下发先前明文。[RSVL2288](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/43/43eb8a01d98631be__WMS-RSV-01_库存预留与批次分配_前后端开发Spec_v1.1.2_REPAIR_CANDIDATE.md:2288>) [StockL4889](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/5a/5aed7ccb52e1878b__WMS-STOCK-01_库存数量账与移动_前后端开发Spec_v1.1.1_REVIEW.md:4889>) [StockL180](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/5a/5aed7ccb52e1878b__WMS-STOCK-01_库存数量账与移动_前后端开发Spec_v1.1.1_REVIEW.md:180>)

## 9. 后继共享合同的精确边界

H31 v0.7 的完整 326 行在 RETURN / OUT 是精确镜像，本轮切片按 LF+末尾换行重建为 49962 bytes、SHA `903617f5d8617384a9acaf27a592b835ea3ce1887e98b428955ad1c9ba5c744d`，与 manifest 相同。规范全文复用 operations / commercial 已读窗口；三份大夹具复用 operations 已完成的 56 / 126 / 47 个案例及 48 / 55 / 55 个证据对象；RETURN 内嵌切片与三个独立原件 SHA 均相同。它把采购退运定义为 SHIP/SUPPLIER_RETURN，一真实 Part 可同原 Root 多地点全组过账；父域预算不含版本，第一批 AUTHORIZED_FREE，交叠现有 Claim 先由 RSV 有权处置。Stock→物理事实→MRX→SourceReceipt→结果的同 UoW 是接受设计，真实采用仍未验证。[OUT内嵌合同L493](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/7e/7e25b78ea1261553__WMS-OUT-01_出库拣配包装与发运_前后端开发Spec_v1.3_R3_REVIEW_CANDIDATE.md:493>) [OUT内嵌合同L676](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/7e/7e25b78ea1261553__WMS-OUT-01_出库拣配包装与发运_前后端开发Spec_v1.3_R3_REVIEW_CANDIDATE.md:676>) [共同接受L20](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d6/d69d65cde52092ea__SHARED_ACCEPTANCE.md:20>)

Stock correction discovery v1 是已追加接受的只读新设计：`/movement-corrections/discover` 与 `/read-applied`。完整原 Movement/反向链/已提交 Correction/全部 Pool 同快照，缺原件、局部可见或解释闭包不全则 UNKNOWN。entry 每个真实 Correction 一项；首次固定 ReadApplied ref/body/observedAt 后不重签；sourceSetDigest 排除观察时间但保全部身份和范围。空集合仅在完整读取后才成立。[OUT内嵌合同L1178](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/7e/7e25b78ea1261553__WMS-OUT-01_出库拣配包装与发运_前后端开发Spec_v1.3_R3_REVIEW_CANDIDATE.md:1178>) [OUT内嵌合同L1203](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/7e/7e25b78ea1261553__WMS-OUT-01_出库拣配包装与发运_前后端开发Spec_v1.3_R3_REVIEW_CANDIDATE.md:1203>) [OUT内嵌合同L1209](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/7e/7e25b78ea1261553__WMS-OUT-01_出库拣配包装与发运_前后端开发Spec_v1.3_R3_REVIEW_CANDIDATE.md:1209>) [OUT内嵌合同L1211](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/7e/7e25b78ea1261553__WMS-OUT-01_出库拣配包装与发运_前后端开发Spec_v1.3_R3_REVIEW_CANDIDATE.md:1211>)

同 readKey 原 body 恢复原 snapshot/cut，不冒新刷新；新当前读使用新 readKey。OUT 发现未通知更正后保存缺项/RefreshWork，先 UNKNOWN，按原快照逐项补链再 APPLIED_AS_OF；不得以已收通知列表证明当前完整。这里的查询证据保存允许读服务库，但数量/最终准入仍遵守相应 Owner 共同门。[OUT内嵌合同L1219](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/7e/7e25b78ea1261553__WMS-OUT-01_出库拣配包装与发运_前后端开发Spec_v1.3_R3_REVIEW_CANDIDATE.md:1219>) [OUT内嵌合同L1221](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/7e/7e25b78ea1261553__WMS-OUT-01_出库拣配包装与发运_前后端开发Spec_v1.3_R3_REVIEW_CANDIDATE.md:1221>) [OUT内嵌合同L1229](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/7e/7e25b78ea1261553__WMS-OUT-01_出库拣配包装与发运_前后端开发Spec_v1.3_R3_REVIEW_CANDIDATE.md:1229>)

DEL-SOURCE-FENCE-1 有界采用要求 Stock/OUT/DEL 同物理 Connection 和 SERIALIZABLE DbTransaction；相关原 Movement 全集 F0 在原 Canonical 前取得，再保 Canonical→Scope→Source 顺序，成功解释同 commit 推 epoch。不能在已持旧锁后补 F0，也不能全局推广为所有库存事务均采用此隔离等级；缺 wrapper 采用不启相关 REAL Proof。[DELL15272](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/c3/c30b6885dde887be__WMS-DEL-01_承运与交付证明_前后端开发Spec_v1.3_R3_REVIEWED.md:15272>) [DELL15280](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/c3/c30b6885dde887be__WMS-DEL-01_承运与交付证明_前后端开发Spec_v1.3_R3_REVIEWED.md:15280>) [DELL15282](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/c3/c30b6885dde887be__WMS-DEL-01_承运与交付证明_前后端开发Spec_v1.3_R3_REVIEWED.md:15282>) [DEL采用保留L169](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/1f/1fd0c2f53e99a13c__DEL_acceptance.md:169>)

RMA 实收只经 WMS-IN-RMA-ADAPTER-1。可沿原 Root 为 RETURN/RETURN；新可追来源为 RECEIPT/RECEIVE；RMA 专属 claim/range/conversion 不加进 Stock wire。同 databaseBinding/MainUow 采用后，Stock、IN Outcome、RMA PhysicalReceipt/H30 和 share claim 才同提交；缺采用 INBOUND_ADOPTION_UNPROVEN。五种终端处置仍 REQUIRED_FENCED、effectOwner/ownerContractRef=null，不能因 Stock 八种 data 已定义就替 RMA 决定执行 Owner。[RMAL356](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/dc/dc0ca1dd23d08f92__WMS-RMA-01_客户退货实物处理_前后端开发Spec_v1.0_DRAFT(3).md:356>) [RMA接受边界L29](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6b/6bc33b9183f5e3da__ACCEPTANCE.md:29>)

## 10. 编码前衔接顺序与尚未关闭范围

建议依赖顺序：先冻结类型/身份/摘要和单数据库数量内核；再 SourceManifest/预算/Claim 同事务、容量份额与最终资格；再 SP00/SP01、原键查询/Seal/Inbox 恢复；接 RSV 来源/选取/人工批准/UseGate；最后逐项接 correction discovery、H31、DEL F0、RMA 受限 adapter，并按参与范围做旧 writer 单写切换。这是从已述依赖得出的整合建议，不是新增业务 Owner 决定。

本次补读及共同材料精确复用可以关闭两本主文和两个目标当前必需文件的语义未读清单；不能关闭真实部署拓扑、IAM/Owner 注册、质量/容量/来源原件、并发/崩溃/租户隔离验证。Stock 的三份 H31 共享夹具现已按 operations 精确阅读证据复用：SC20-H31-SHARED-FIXTURE v1.6、OUT-EFFECTIVE-SHARED-FIXTURE v3、SC20-H31-MULTILOC-FIXTURE v1。本组必要材料未读队列为空；没有把当前示例降为历史或把文档验算升为生产通过。

本册未发现需要扩大 Owner 权限或替换当前版本的依据。静态 JSON 解析、精确子树复用和范围核对仅证明文档整理过程；157 Stock 场景、96 RSV 原场景及人工批准追加场景仍 NOT_RUN。实际覆盖、源 SHA、逐段范围、共享待办见 [独立阅读证据](<D:/CP6/docs/CP6_开发设计文档_20261010/evidence/WMS-STOCK-RSV-supplement-reading.json>)。
