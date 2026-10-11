# WMS-RSV-01 库存预留与批次分配

整理状态：**required_materials_consolidated**。本册主文4070行全部业务语义、当前接受及共享合同已通过直接阅读和精确复用闭合；包括32个JSON、手选批准追加规则及全部AC。真实Owner接入、O-RSV实例及业务场景仍未验证。

## 1. 业务目的、操作者与模块边界

RSV决定哪个有效需求依什么策略占用哪份库存、何时可以释放。业务/计划人员从正式H17请求发起，仓储分配员确认候选，协调员处理重配/取消/到期，规则管理员维护路由，独立有权者批准规则和手选例外。RSV只持请求、需求份额、选择、路由、效力与交接；数量复用Stock唯一Claim/Binding/Event，不再维护可写Reserved/Remaining账。分配成功不生成真实发运、质量通过或财务完成。[RSV原文L41](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/43/43eb8a01d98631be__WMS-RSV-01_库存预留与批次分配_前后端开发Spec_v1.1.2_REPAIR_CANDIDATE.md:41>)

一个请求限一个Site和产品/技术/货主/用途/UOM数量域，可跨该Site多个仓批位。跨Site/维度须源事前拆成独立请求及份额，不能失败时临时拆成功子项。[RSV原文L51](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/43/43eb8a01d98631be__WMS-RSV-01_库存预留与批次分配_前后端开发Spec_v1.1.2_REPAIR_CANDIDATE.md:51>)

## 2. 当前版本、接受范围与组合

当前正文v1.1.2 REPAIR_CANDIDATE，SHA-256 `43eb8a01d98631beb15c85d524125f2b848cb4f2e106170edf132aa417c50cfd`，4070行。准确接受记录已经接受完整正文及D-RSV-01～14，故正文DRAFT/REVIEW_PROPOSAL是历史标签。simulation接受仅隔离生产效果，没有另创持久simulation产品范围。O-RSV-01～12仍必需；业务AC NOT_RUN、实现NOT_STARTED。[准确接受L18](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/1a/1a5260e6050a9666__HISTORY__handoffs__PLAN-REUSE-01B__documentation__MD02__acceptances__D-WMS__UA-20260930-D-WMS-RSV-MD01.md.txt:18>) [准确接受L24](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/1a/1a5260e6050a9666__HISTORY__handoffs__PLAN-REUSE-01B__documentation__MD02__acceptances__D-WMS__UA-20260930-D-WMS-RSV-MD01.md.txt:24>)

依赖Stock `5aed7ccb…` 与MASTER `df146652…`精确原合同，当前mandatory清单无另列规范组合附件；这不等可以略过正文内的Stock适配、F01～03修订或对原Owner的采用门。[RSV原文L11](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/43/43eb8a01d98631be__WMS-RSV-01_库存预留与批次分配_前后端开发Spec_v1.1.2_REPAIR_CANDIDATE.md:11>) [RSV原文L572](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/43/43eb8a01d98631be__WMS-RSV-01_库存预留与批次分配_前后端开发Spec_v1.1.2_REPAIR_CANDIDATE.md:572>)

## 3. 数据、主键、数量和版本

|身份/实体|规则|
|---|---|
|ReservationRequestId / requestId|前者源H17持久业务键，后者RSV记录Guid；HTTP Key只防请求重投。|
|DemandIdentity / DemandCoverage|owner/document/line/lineage稳定，版本不重置唯一域；授权Demand半开区间与物理RootSpan不同。|
|RequestAdmission|在途请求可能覆盖范围的控制；阻重复承诺，但不扣Stock Free。|
|Reservation / Claim|一个Request一个共享Claim；Reservation没有独立可写Reserved/Available/Remaining。|
|ClaimDemandMap|需求份额到真实ClaimEvent/Binding/Root的映射；逻辑Unbacked可无Root。必须与Stock事件同事务更新。|
|Action / Plan / Preparation|独立操作持久身份；Plan只候选/冻结输入；`rsv-action/<actionId>`不是HTTP键。|
|Use / UseLeg / UseSpan|一个真实Shipment/Consumption使用身份可含多个Binding/Allocation腿；总量等互斥腿和，不能任拆为不同业务执行。|
|ValidityHead / RouteSelector / UseFence|效力、路线当前指针、使用围栏各有版，不等Claim数量版。|

依据：[RSV原文L89](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/43/43eb8a01d98631be__WMS-RSV-01_库存预留与批次分配_前后端开发Spec_v1.1.2_REPAIR_CANDIDATE.md:89>) [RSV原文L91](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/43/43eb8a01d98631be__WMS-RSV-01_库存预留与批次分配_前后端开发Spec_v1.1.2_REPAIR_CANDIDATE.md:91>) [RSV原文L719](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/43/43eb8a01d98631be__WMS-RSV-01_库存预留与批次分配_前后端开发Spec_v1.1.2_REPAIR_CANDIDATE.md:719>) [RSV原文L744](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/43/43eb8a01d98631be__WMS-RSV-01_库存预留与批次分配_前后端开发Spec_v1.1.2_REPAIR_CANDIDATE.md:744>) [RSV原文L1083](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/43/43eb8a01d98631be__WMS-RSV-01_库存预留与批次分配_前后端开发Spec_v1.1.2_REPAIR_CANDIDATE.md:1083>) [RSV原文L1111](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/43/43eb8a01d98631be__WMS-RSV-01_库存预留与批次分配_前后端开发Spec_v1.1.2_REPAIR_CANDIDATE.md:1111>) [RSV原文L614](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/43/43eb8a01d98631be__WMS-RSV-01_库存预留与批次分配_前后端开发Spec_v1.1.2_REPAIR_CANDIDATE.md:614>)。

数量decimal(21,8)、合计(38,8)，正常换算无法精确到8位拒绝。有效期半开，NO_END需具名批准依据，不由null猜无限期。四Claim分量守恒复用Stock；Allocation/Pick是OpenBacked分项。[RSV原文L68](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/43/43eb8a01d98631be__WMS-RSV-01_库存预留与批次分配_前后端开发Spec_v1.1.2_REPAIR_CANDIDATE.md:68>) [RSV原文L108](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/43/43eb8a01d98631be__WMS-RSV-01_库存预留与批次分配_前后端开发Spec_v1.1.2_REPAIR_CANDIDATE.md:108>)

## 4. 正常流程、状态与写集

```mermaid
flowchart LR
 H[源持久H17请求 Demand Release Grant] --> R[RR01登记 不占货]
 R --> A[RA01独立Action]
 A --> P[RP01完整候选 RP02冻结]
 P --> S[Stock SP00准确准备]
 S --> C[RA03共享QuantityUoW]
 C --> O[Claim及DemandMap/Outcome/Outbox共同提交]
```

Planning Run/Pegging只作旁证。RR01按Tenant/Producer/ReservationRequestId去重，同键改源/量/模式冲突；只登记元数据。Grant标量上限与Demand区间都要检查：Grant100下已接[0,60)，新请求[40,80)虽然标量合计100仍交叠20而拒绝。[RSV原文L709](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/43/43eb8a01d98631be__WMS-RSV-01_库存预留与批次分配_前后端开发Spec_v1.1.2_REPAIR_CANDIDATE.md:709>) [RSV原文L711](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/43/43eb8a01d98631be__WMS-RSV-01_库存预留与批次分配_前后端开发Spec_v1.1.2_REPAIR_CANDIDATE.md:711>) [RSV原文L717](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/43/43eb8a01d98631be__WMS-RSV-01_库存预留与批次分配_前后端开发Spec_v1.1.2_REPAIR_CANDIDATE.md:717>)

|模式|请求60/当前可背40的解释|
|---|---|
|BACKED_ONLY+REQUIRE_FULL（默认）|无完整方案，不隐式创建Claim40。|
|BACKED_ONLY+ALLOW_FINAL_PARTIAL|源允许且达min时显式接受40，原请求终态未接受20；新残余请求引用原Outcome及[40,60)。|
|LOGICAL_WITH_DEFERRED_BACKING|源和政策批准后Granted60/Unbacked60/Open0；原请求已全部接受，以后给同Claim背书，不能另申所谓残余。|

原键部分终态始终返回40，不后台补足；纯逻辑CREATE没有实物，不能要求不存在Root质量PASS或出库路由可分配。[RSV原文L725](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/43/43eb8a01d98631be__WMS-RSV-01_库存预留与批次分配_前后端开发Spec_v1.1.2_REPAIR_CANDIDATE.md:725>) [RSV原文L731](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/43/43eb8a01d98631be__WMS-RSV-01_库存预留与批次分配_前后端开发Spec_v1.1.2_REPAIR_CANDIDATE.md:731>) [RSV原文L878](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/43/43eb8a01d98631be__WMS-RSV-01_库存预留与批次分配_前后端开发Spec_v1.1.2_REPAIR_CANDIDATE.md:878>)

|Action|Stock权威写|RSV同事务写|
|---|---|---|
|CREATE|ClaimGrantEffect、Claim/Binding、事件/回执|Admission终态、DemandCoverage、Reservation、DemandMap、原Outcome。|
|BACK|Unbacked→Binding，不增Granted|同需求到选定Root映射、路线依据、Action结果。|
|ALLOCATE|原Binding状态及Allocation，Free不再扣|AllocationDecision和真实AllocationId关联。|
|RELEASE|准确未耗Binding关闭或Unbacked减少|DemandMap关闭、UseFence处置、ReleaseOutcome。|
|REBIND|旧新Binding同量原子交换|需求映射迁移、旧Allocation保历史、Fence后继。|
|下游SHIP/CONSUME|Stock实际物理+本Claim消费|同SourceParticipant更新Demand映射/Use结果，不再UNRSV。|

依据：[RSV原文L932](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/43/43eb8a01d98631be__WMS-RSV-01_库存预留与批次分配_前后端开发Spec_v1.1.2_REPAIR_CANDIDATE.md:932>)。BACK与ALLOCATE是两个独立动作，页面连续引导也不能将第一步成功伪成整体完成。[RSV原文L914](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/43/43eb8a01d98631be__WMS-RSV-01_库存预留与批次分配_前后端开发Spec_v1.1.2_REPAIR_CANDIDATE.md:914>)

Request原结果与当前效力分轴：ACCEPTED_FULL/PARTIAL_FINAL不可随今天释放或失效改写；派生数量阶段从LOGICAL_ONLY至SETTLED，SETTLED要求Unbacked/OpenBacked都0但保Consumed/Released。当前EXPIRED/QUALITY_RESTRICTED/UNKNOWN不删原承诺。[RSV原文L95](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/43/43eb8a01d98631be__WMS-RSV-01_库存预留与批次分配_前后端开发Spec_v1.1.2_REPAIR_CANDIDATE.md:95>)

### 候选与路由

取完整声明Scope内候选，不从UI第一页选货。默认FEFO，批准政策才FIFO；过期未知不当无限期。排序依路由仓序→已核expiry/received→稳定lot/location/root/span。SINGLE_WAREHOUSE、SINGLE_LOT和跨仓多批分别受约束，缺源/质量/高优先条件不能当0后降成部分。手选必须合法候选范围和准确批准。[RSV原文L761](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/43/43eb8a01d98631be__WMS-RSV-01_库存预留与批次分配_前后端开发Spec_v1.1.2_REPAIR_CANDIDATE.md:761>) [RSV原文L769](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/43/43eb8a01d98631be__WMS-RSV-01_库存预留与批次分配_前后端开发Spec_v1.1.2_REPAIR_CANDIDATE.md:769>)

路由按customer/product/prefix/outboundUse条件AND，选择rank最小的第一确定匹配；rank唯一。fallback默认NEVER，NO_MATCH_ONLY与AFTER_INSUFFICIENT只追加明确仓清单，不全租户兜底。批准不自动激活，Selector CAS决定当前版本；到期不自动回前版。[RSV原文L804](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/43/43eb8a01d98631be__WMS-RSV-01_库存预留与批次分配_前后端开发Spec_v1.1.2_REPAIR_CANDIDATE.md:804>) [RSV原文L808](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/43/43eb8a01d98631be__WMS-RSV-01_库存预留与批次分配_前后端开发Spec_v1.1.2_REPAIR_CANDIDATE.md:808>) [RSV原文L814](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/43/43eb8a01d98631be__WMS-RSV-01_库存预留与批次分配_前后端开发Spec_v1.1.2_REPAIR_CANDIDATE.md:814>)

### 释放、到期与重绑定

释放UNBACKED需求区间或BACKED Root范围二选一，独立ReleaseDecision必需。先登记完整下游参与者/Use，再在同门安装持久UseFence；逐使用核关闭全部别名/worker、实际Pick/Move处置、真实SHIP/CONSUME结果。查询空、NOT_OBSERVED、TTL过期都不证明NoFurtherUse。[RSV原文L827](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/43/43eb8a01d98631be__WMS-RSV-01_库存预留与批次分配_前后端开发Spec_v1.1.2_REPAIR_CANDIDATE.md:827>) [RSV原文L833](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/43/43eb8a01d98631be__WMS-RSV-01_库存预留与批次分配_前后端开发Spec_v1.1.2_REPAIR_CANDIDATE.md:833>)

安全RELEASE不要求原Demand仍ACTIVE、旧路线仍有效或Root合格，而要求当前释放决定、准确未耗范围、Fence/NoFurtherUse；不能用全API同一Active布尔堵安全处置。到期只关新使用并形成处置事项，绝不自动清OpenBacked。[RSV原文L876](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/43/43eb8a01d98631be__WMS-RSV-01_库存预留与批次分配_前后端开发Spec_v1.1.2_REPAIR_CANDIDATE.md:876>) [RSV原文L888](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/43/43eb8a01d98631be__WMS-RSV-01_库存预留与批次分配_前后端开发Spec_v1.1.2_REPAIR_CANDIDATE.md:888>)

Rebind同Claim同维度等量，新Binding回RESERVED，不复制旧PICKED；原Allocation留历史。新目标失败旧占用保留，重绑定不搬实物。临时Fence仅据准确关闭/成功后继解除，较新取消控制仍保留。[RSV原文L857](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/43/43eb8a01d98631be__WMS-RSV-01_库存预留与批次分配_前后端开发Spec_v1.1.2_REPAIR_CANDIDATE.md:857>) [RSV原文L861](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/43/43eb8a01d98631be__WMS-RSV-01_库存预留与批次分配_前后端开发Spec_v1.1.2_REPAIR_CANDIDATE.md:861>)

## 5. API与跨Owner合同

Public `/api/wms/reservations/v1`，Internal `/internal/wms/reservations/v1`。写带UUID Idempotency-Key、可变编辑强If-Match，可信Tenant/Actor/Producer不由body覆盖。[RSV原文L639](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/43/43eb8a01d98631be__WMS-RSV-01_库存预留与批次分配_前后端开发Spec_v1.1.2_REPAIR_CANDIDATE.md:639>)

|入口|结果/责任|
|---|---|
|RQ02→RR01/RR02|取真实来源、登记/找回Request；无库存效果。|
|RA01/02→RP01/02→RA03|草稿、候选、批准冻结与Stock准备、最终同事务提交。RA03仅`{intentRef,reason}`。|
|RD01/02/03|释放上下文、安装UseFence并drain、读取完整NoFurtherUse。|
|RU01/02 internal|发送前登记真实Use及全部腿，保存准确Source/Stock结果。|
|RO01–04|原键查询、指定步骤恢复、Source关闭授权、Stock封闭裁决。|
|RT01–07|规则候选/送审/批准/激活/停用/试算；试算不占货。|
|RI01–03 / RH02|消息收件/查/续办及只读投影修复，不重造Claim。|

准确输入输出见[RSV原文L643](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/43/43eb8a01d98631be__WMS-RSV-01_库存预留与批次分配_前后端开发Spec_v1.1.2_REPAIR_CANDIDATE.md:643>)。`IReservationSourceParticipant`含ResolveAction、CollectResources、ValidateUnderLock、RecordSourceLinks、ReadOriginalResult：资源在锁外收齐，最终校验本Demand和Use，RecordSourceLinks使用传入同连接同事务，失败整Stock事务回滚。[RSV原文L1026](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/43/43eb8a01d98631be__WMS-RSV-01_库存预留与批次分配_前后端开发Spec_v1.1.2_REPAIR_CANDIDATE.md:1026>)

Planning收到原ReservationOutcome包含acceptedDemandRanges与unacceptedFinalRanges、原时点amounts和当前Validity引用；晚到40仍保存真实WMS事实，再处理当前取消，不能把旧成功累加为第二承诺。[RSV原文L1046](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/43/43eb8a01d98631be__WMS-RSV-01_库存预留与批次分配_前后端开发Spec_v1.1.2_REPAIR_CANDIDATE.md:1046>)

## 6. 同UoW、并发、幂等和恢复

RSV最终数量事务不HTTP调用Stock；所有必要来源关系、Claim及回执同一Main连接/事务。锁序沿Stock：全部Canonical/别名决定点→祖先ScopeGate→Grant/Coverage/Admission/Validity/Route/Claim/Map/Pool/Root/Use等规范序最强锁。锁后新资源出现回滚重收集，区间交叉不能只靠UNIQUE端点。[RSV原文L914](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/43/43eb8a01d98631be__WMS-RSV-01_库存预留与批次分配_前后端开发Spec_v1.1.2_REPAIR_CANDIDATE.md:914>) [RSV原文L922](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/43/43eb8a01d98631be__WMS-RSV-01_库存预留与批次分配_前后端开发Spec_v1.1.2_REPAIR_CANDIDATE.md:922>)

先合法提交者得自由份额；高优先级只调度未提交申请，不能抢已授Claim。原动作确定拒绝后新选择要新Action及successorOf，未知旧前驱仍保护在途范围。[RSV原文L945](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/43/43eb8a01d98631be__WMS-RSV-01_库存预留与批次分配_前后端开发Spec_v1.1.2_REPAIR_CANDIDATE.md:945>) [RSV原文L951](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/43/43eb8a01d98631be__WMS-RSV-01_库存预留与批次分配_前后端开发Spec_v1.1.2_REPAIR_CANDIDATE.md:951>)

丢响应按各阶段原键恢复：Request、Action、Prepared identity分别有lookup；Stock已成RSV映射不一致进入对账，不按同量猜关联。seal已有POSTED时回原成功，若要退占必须独立RELEASE，不能把CREATE改成未发生。[RSV原文L960](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/43/43eb8a01d98631be__WMS-RSV-01_库存预留与批次分配_前后端开发Spec_v1.1.2_REPAIR_CANDIDATE.md:960>) [RSV原文L968](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/43/43eb8a01d98631be__WMS-RSV-01_库存预留与批次分配_前后端开发Spec_v1.1.2_REPAIR_CANDIDATE.md:968>)

Demand控制流高序缺口即设置SourceDependencyBlock，旧Applied头ACTIVE不授新使用。纯显示队列延迟若权威同库完整可读则不扩大成业务停机。投影修复同时比Claim/Pool/Demand/Validity/Route/Use完整向量。[RSV原文L991](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/43/43eb8a01d98631be__WMS-RSV-01_库存预留与批次分配_前后端开发Spec_v1.1.2_REPAIR_CANDIDATE.md:991>) [RSV原文L997](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/43/43eb8a01d98631be__WMS-RSV-01_库存预留与批次分配_前后端开发Spec_v1.1.2_REPAIR_CANDIDATE.md:997>) [RSV原文L1003](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/43/43eb8a01d98631be__WMS-RSV-01_库存预留与批次分配_前后端开发Spec_v1.1.2_REPAIR_CANDIDATE.md:1003>)

## 7. 权限、批准和simulation隔离

人权限、服务ingest/use/register/close/resume分别配置，Tenant/Site/Owner/产品/字段交集；管理员不自动拥有专业批准。浏览器不能直用Stock内部身份。所有正式下游使用最后加入UseGate，绕过旧writer未围栏则相关生产范围不开。[RSV原文L131](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/43/43eb8a01d98631be__WMS-RSV-01_库存预留与批次分配_前后端开发Spec_v1.1.2_REPAIR_CANDIDATE.md:131>)

F01明确simulation/environmentIdentity隔离生产头；simulation不得创建权威Claim、推进Validity/UseFence/RouteSelector，不返回生产可执行nextCommand，RA03拒SIMULATION_EFFECT_FORBIDDEN。[RSV原文L576](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/43/43eb8a01d98631be__WMS-RSV-01_库存预留与批次分配_前后端开发Spec_v1.1.2_REPAIR_CANDIDATE.md:576>)

F02要求PrepareActionRequest显式manualSelectionApproval；自动/纯逻辑/释放可null，手选完整对象必需。Plan digest、RSV业务摘要与Stock摘要不同域。RP02保存准确批准快照于StockPreparationBinding，RA03凭原intentRef反查且重读当前批准权威；revokeRef=null不是免查当前性。Client不能提交新批准覆盖。[RSV原文L593](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/43/43eb8a01d98631be__WMS-RSV-01_库存预留与批次分配_前后端开发Spec_v1.1.2_REPAIR_CANDIDATE.md:593>) [RSV原文L606](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/43/43eb8a01d98631be__WMS-RSV-01_库存预留与批次分配_前后端开发Spec_v1.1.2_REPAIR_CANDIDATE.md:606>) [RSV原文L610](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/43/43eb8a01d98631be__WMS-RSV-01_库存预留与批次分配_前后端开发Spec_v1.1.2_REPAIR_CANDIDATE.md:610>)

## 8. 页面与交互校验

七页覆盖列表、源请求、Reservation详情、候选计划、路由、处置、操作恢复。页面分别显示原接受、未背书、当前占用、已耗/释放和效力；候选确认需区分“新增占用40”与“仅分配原占用40”。无真实AllocationId不开下游交接。[RSV原文L119](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/43/43eb8a01d98631be__WMS-RSV-01_库存预留与批次分配_前后端开发Spec_v1.1.2_REPAIR_CANDIDATE.md:119>)

source变更清旧候选/前驱；持久Request原源不可改。SUBMITTING冻结原body/key，Abort仅停止等待。409/412保差分不自动取新token重发；敏感正文不存localStorage，恢复只保存locator/hash。CSV查看版不允许无损回库，生成和下载各核权限。[RSV原文L183](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/43/43eb8a01d98631be__WMS-RSV-01_库存预留与批次分配_前后端开发Spec_v1.1.2_REPAIR_CANDIDATE.md:183>)

## 9. 实施顺序与旧代码差异

先建立H17稳定源及Demand↔Claim映射，再接单动作同UoW、完整候选/路由、Use登记与Fence、释放重绑定，最后消息/恢复及旧入口切换。此为整理建议，字段与业务规则仍由原文固定。

固定旧证据仅`GTX537/CP6@157630594e3371fe181955d2f6227ff3b6962c84`：Outbound单行找足needed、循环可部分分配；PENDING未在候选排除；取消按Allocated−Shipped直接UNRSV；旧routing追加全部仓及fallback；Stock Math.Max截Allocated。记录未证明所有上层无ambient事务，也未核本次main。[RSV原文L1141](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/43/43eb8a01d98631be__WMS-RSV-01_库存预留与批次分配_前后端开发Spec_v1.1.2_REPAIR_CANDIDATE.md:1141>)

数量唯一开关沿Stock QuantityWriterBinding，RSV EntryBinding只记入口转换，不另创第二开关。KERNEL_ACTIVE旧allocate/cancel/ship要转新用例或拒绝；新失败不退旧。旧多批映射不能压第一批覆LotNo。[RSV原文L1156](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/43/43eb8a01d98631be__WMS-RSV-01_库存预留与批次分配_前后端开发Spec_v1.1.2_REPAIR_CANDIDATE.md:1156>)

## 10. 验收与运行状态

全部业务AC NOT_RUN，未运行SQL/API/并发。必须覆盖：逻辑60不扣Free；60只背40的两种不同语义；同源换key一次Claim；Demand重叠拒绝；Free40两请求各30只有一个先得；释放不返Grant；多腿Use完整关闭；手选批准在RP02后撤销使RA03零新效果；simulation不可污染生产。[RSV原文L3815](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/43/43eb8a01d98631be__WMS-RSV-01_库存预留与批次分配_前后端开发Spec_v1.1.2_REPAIR_CANDIDATE.md:3815>) [RSV原文L3839](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/43/43eb8a01d98631be__WMS-RSV-01_库存预留与批次分配_前后端开发Spec_v1.1.2_REPAIR_CANDIDATE.md:3839>) [RSV原文L3848](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/43/43eb8a01d98631be__WMS-RSV-01_库存预留与批次分配_前后端开发Spec_v1.1.2_REPAIR_CANDIDATE.md:3848>)

## 11. 明确待核事项

算法选择D-RSV-01～14已接受；未确认的是O-RSV真实实例和实现接线：H17/残余/再申请权威、同UoW SourceParticipant、全部旧/新使用入口、路由业务参数、现场身份/UOM、QA/MASTER即时门及保留/导出策略。缺能力阻对应动作，历史和安全处置按自身资格继续。[准确接受L20](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/1a/1a5260e6050a9666__HISTORY__handoffs__PLAN-REUSE-01B__documentation__MD02__acceptances__D-WMS__UA-20260930-D-WMS-RSV-MD01.md.txt:20>) [RSV原文L3975](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/43/43eb8a01d98631be__WMS-RSV-01_库存预留与批次分配_前后端开发Spec_v1.1.2_REPAIR_CANDIDATE.md:3975>)

封闭DTO、连续请求JSON和全部AC已完成业务语义整理；Ref/摘要域与可空字段仍必须按对应Owner原型实现，不能跨域统一改名。

## 12. 来源与阅读覆盖

本组已读1–190、572–1171、3815–3867、3948–3992、4065–4070及接受33行；平台独立补齐191–571、1172–3814、3868–3947、3993–4064，包括32个JSON完整新增字段及全部差异、人工批准修补和剩余AC。4070行主文没有未关闭实质章节。两册90块/272精确子树复用、无损还原0差只证明文书保真，不是业务执行。

[Stock/RSV完整补册](D:/CP6/docs/CP6_开发设计文档_20261010/contracts/WMS-STOCK-RSV-数量与恢复附册.md)与[29源精确复用证据](D:/CP6/docs/CP6_开发设计文档_20261010/evidence/WMS-STOCK-RSV-supplement-reading.json)提供完整调用/数量/恢复说明及29源准确SHA归属；共享6短规范、3大fixture及2观察均已语义读取/精确复用。所有真实路由参数、Owner接入及业务场景继续未证。

逐源范围见[operations-reading.json](D:/CP6/docs/CP6_开发设计文档_20261010/evidence/operations-reading.json)。
