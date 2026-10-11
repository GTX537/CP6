# WMS-SLOT-01 库位优化开发设计

状态：当前正文及X3接受/评审元数据已整理，待双方合同集中核对。[总目录](D:/CP6/docs/CP6_开发设计文档_20261010/模块目录.md)。

## 1. 目的与职责

仓管分析固定窗口的真实出库频次，形成ABC和可解释库位建议，选择准确拟移份额/目标，审批固定Proposal，再按行登记Transfer工作并追踪原Stock移动结果。五SPEC是分析、建议方案、审批、执行关联、取消。分析不改Space布局/MASTER主数据；批准不搬货，任务登记不等物理完成。WMS拥有分析/方案/关联，实际数量路径由TRANSFER→Stock承担。[完整闭环](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/ec/ec7dcc5179f0e584__WMS-SLOT-01_库位优化_完整设计_RECOVERY_v1.1_R2.md:13)

## 2. 当前版本和采用范围

本轮补核结论：2026-10-08T11:56:55Z代理接受准确六册R2及具名profile；原九根累计静态闭合，全部业务AC仍NOT_RUN、Owner运行采用UNPROVEN。[X3共同阅读与采用边界](D:/CP6/docs/CP6_开发设计文档_20261010/evidence/X3-六专项阅读与采用边界.md)保存准确组合、112AC/31任务逐条等值核对和历史R1原ZIP缺口。下文提及原候选标签均属保留历史。

本轮选择 v1.1 R2 RECOVERY，SHA前缀 `ec7dcc5179f0e584`。准确全SHA、UA/CURRENT及评审组合见[必需输入索引](D:/CP6/docs/CP6_开发设计文档_20261010/indexes/required-inputs.json)。它是恢复设计的有界后继，原件标签STOPPED保留；设计选择与实际采用独立，15 AC均NOT_RUN，新路由/类型仍候选。目标接受文件与共同评审已补核，详见下述X3组合阅读证据。[版本和范围](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/ec/ec7dcc5179f0e584__WMS-SLOT-01_库位优化_完整设计_RECOVERY_v1.1_R2.md:3)

## 3. 对象、唯一键与量义

AnalysisInput含稳定analysisRequestKey、可信Scope、半开from/to、FREQUENCY、ruleRef。AnalysisView必须给**不同的Analysis.ref与planRef**，initialPlanState=ANALYZED、sourceCut/coverage及完整维度行；Proposal冻结analysisRef/cut、全部SelectedMove、omitted原因和digest；Approval绑定原Proposal/摘要、申请人/批准人、期限。

SelectedMove保lineKey、原Root spans、from/to正式Location、Quantity、选择理由、MASTER条件和Stock观察。Plan lifecycle为ANALYZED/DRAFT_SELECTION/SUBMITTED/APPROVED/STOP_REQUESTED/STOPPED/SETTLED；每行另有UNSELECTED、APPROVED_NOT_ISSUED、ISSUING、TASK_ISSUED、IN_PROGRESS、MOVED、PARTIAL_MOVED、CANCELLED_NO_EFFECT、BLOCKED、UNKNOWN，不能合并成一个“成功”。[DTO](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/ec/ec7dcc5179f0e584__WMS-SLOT-01_库位优化_完整设计_RECOVERY_v1.1_R2.md:39)

分析唯一 `(Tenant,Environment,Actor,analysisRequestKey)`；行派发唯一 `(tenant,env,planId,proposalVersion,lineKey)`，原Owner operationId只生成一次。X3 Quantity写入最多13位整数/8位小数，TotalQuantity只读最多30位整数/8位小数；已知空0、未知null、跨维度NOT_AGGREGATABLE分别表达。同EA但不同产品/技术/货主也不能加为业务可用量。[共同类型](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/ec/ec7dcc5179f0e584__WMS-SLOT-01_库位优化_完整设计_RECOVERY_v1.1_R2.md:123)

## 4. 分析、ABC、审批与执行流程

POST analyses固定范围、时窗和一致sourceCut，读已确认OUT物理事件；重复运输不计，发生拆多数据库腿不能增频次，纠错依版本化统计政策。完整零总数NO_SAMPLE，缺源/更正/cut为UNKNOWN且不可审批派发。不可变Analysis/rows与独立Plan、CommandResult、Audit同一短事务保存，源读失败可以保存明确UNKNOWN的分析/计划。只有Analysis计算而Plan提交失败不能回成功。[分析生产](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/ec/ec7dcc5179f0e584__WMS-SLOT-01_库位优化_完整设计_RECOVERY_v1.1_R2.md:55)

排序按outCount降序，再依产品owner/id/version、技术owner/id/version、货主owner/id/version、基本UOM owner/id/version、lineKey组成完整ordinal顺序。分类用**加入当前行之前**累计占比：<0.80为A，<0.95为B，其他C，首行A、零次C。这与ANALYTICS的END_CUMULATIVE_LE_V1（加入当前行之后比较≤80/95）不同，不能只按“ABC”名字共用算法。此处保原规则/版本，不擅自统一。[SLOT公式](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/ec/ec7dcc5179f0e584__WMS-SLOT-01_库位优化_完整设计_RECOVERY_v1.1_R2.md:59)、[ANALYTICS公式](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/2f/2f90c5537845e5cb__WMS-ANALYTICS-01_仓储报表与运行分析_完整详设_R2_CANDIDATE.md:66)

建议只以PIK/RES pattern辅助搜索，真正目标需MASTER正式身份/条件/容量。选择原Root正量且不交叠，保缺候选NO_ELIGIBLE_TARGET及遗漏原因。Proposal修改建新版本，旧批准不能用于新选择。批准绑定摘要/有效期，双人审批是本稿推荐待采用政策；当前来源/规则变STALE须新Proposal重审。完整NO_MOVE可以SETTLED并明确physicalEffect=false，不造任务。[建议和审批](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/ec/ec7dcc5179f0e584__WMS-SLOT-01_库位优化_完整设计_RECOVERY_v1.1_R2.md:61)

派发按批准行独立原子登记，多个行允许PARTIAL，不称全方案完成。物理MOVED须真实Owner terminal receipt与Stock MOVE两腿、Root/位置/UOM/范围一一对应；task Completed缺Stock证据仍UNKNOWN。实际部分移动保原范围并交Transfer ActualFact/Case，不自动建剩量任务。stop先封新dispatch，枚举全部已登记调用含尚无requestRef窗口；未执行须全Stage永久关闭/释放证明，已移保历史，UNKNOWN不能STOPPED/SETTLED。[执行和停止](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/ec/ec7dcc5179f0e584__WMS-SLOT-01_库位优化_完整设计_RECOVERY_v1.1_R2.md:67)

## 5. API与唯一Transfer路线

候选 `/api/wms/slotting/v1`：context/plans/history；analyses create/by-key/read；plans proposals、proposal submit、approval decide；plans dispatch/stop；dispatches by-key/read/resume/execution-link；location/stock options。前端首次分析前持久analysisRequestKey，后续只用返回planRef.id构造Proposal路径，不能拿analysisRef当Plan。[API](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/ec/ec7dcc5179f0e584__WMS-SLOT-01_库位优化_完整设计_RECOVERY_v1.1_R2.md:49)

R2 NEW唯一 `ACCEPTED_TRANSFER`，通过候选 `SlotTransferAdapter-1`登记Transfer Request→原批准/完整Stage，SourceAllowance/Root/原Claim/容量按Transfer共同UoW。NEW每行一次完整Move Stage，不能调用旧MobileTaskV2 Create/Reserve/Complete/Partial/Cancel，也不能包装旧任务当NEW链；S1 MOBILE候选不自动成为替代。TransferExecutionLink保owner、originalBusinessKey、可空requestRef、全部stageRefs/receiptRefs、coverage和REGISTERING/REGISTERED/STARTED/POSTED/FENCING/CLOSED_NO_EFFECT/RECONCILIATION/UNKNOWN。

OLD LegacyTaskFamilyView必须完整列父子任务和原范围/回执：旧4已移+6子任务不可只取消父头就说全关闭。NEW范围旧四数量写点必须423且零旁写，不另RSV/UNRSV。需要移回是新合法MOVE，不篡改原完成。[路线与类型](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/ec/ec7dcc5179f0e584__WMS-SLOT-01_库位优化_完整设计_RECOVERY_v1.1_R2.md:70)

## 6. 并发、持久化和恢复

Analysis.PlanId和Plan.AnalysisId同事务唯一双向关联；Proposal/SelectedSpan冻结，Approval存摘要及消费关联，Dispatch UQ(plan,proposal,line)，原Owner/业务键Link唯一，MoveObservation只读原回执，StopFence/StopItem、Outbox/Inbox/Recovery/Audit齐全。

修改与审批争ProposalHead，派发与stop争LineGate；stop先则无新登记，dispatch先则stop必须包含原调用。两方案争Root由Transfer/Stock最终锁裁决，分析不占执行权；容量竞争按真实Domain。UNKNOWN按原dispatch→Transfer→Stock查原结果，只补link，不按相似量匹配。[持久化与竞争](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/ec/ec7dcc5179f0e584__WMS-SLOT-01_库位优化_完整设计_RECOVERY_v1.1_R2.md:85)

X3共同规则要求先原结果查回，再writer/CAS/完整来源/资源收集和最终门；持锁不远端HTTP，跨Owner无共同事务只保独立结果轴。APPLIED重放不重Stock，NOT_FOUND不证无效果，未知不换键释放保护，重复通知原结果回放、迟到Pending不盖MOVED。[共同恢复](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/ec/ec7dcc5179f0e584__WMS-SLOT-01_库位优化_完整设计_RECOVERY_v1.1_R2.md:143)

## 7. 权限与审计

analyze、proposal.edit/submit、approval.decide、dispatch、stop、recover、read/history/export分开；旧approve不能在NEW暗跨提交/批准/派发多能力。对象权同时覆盖源仓、目的仓、Site、货主和字段。审批不授移动，运维不得改输入，历史回放只核当前读权。审计保实际actor、rule/cut/Proposal摘要、原业务键、writerEpoch和各Owner回执；生成/下载导出分别核权。[能力](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/ec/ec7dcc5179f0e584__WMS-SLOT-01_库位优化_完整设计_RECOVERY_v1.1_R2.md:51)、[共同权限](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/ec/ec7dcc5179f0e584__WMS-SLOT-01_库位优化_完整设计_RECOVERY_v1.1_R2.md:170)

## 8. 页面和校验

S01分析列表分计划/任务/物理结果；S02参数from<to、analysisDays快捷1..365；S03完整维度与真实目标/范围，pattern不直接派发；S04完整Proposal与批准理由；S05逐行原dispatch/Stage/Stock结果；S06停止范围、已移份额和未知恢复。改目标保理由并重新preview/审批，源数量/技术/货主不自由改。批dispatch选1–200个批准lineKeys，ALL_REGISTERED仅登记完成。

分页默认50最大200，同cut/current权限cursor；迟返不盖新筛选，关闭页面不取消已持久操作。服务端返回capabilities和currentRevision，不接客户端MOVED。共同错误区分400输入、412版本、409冲突、423writer、202未知等待与503不可达。[页面](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/ec/ec7dcc5179f0e584__WMS-SLOT-01_库位优化_完整设计_RECOVERY_v1.1_R2.md:26)

## 9. 实施顺序和旧行为

SLOT-D01统计cut/完整排序/Analysis+Plan；D02候选/Proposal/控件；D03批准摘要/CAS；D04唯一Transfer桥/完整结果族/旧V2全部写点fence；D05停止/未知/行投影；D06权限/导出/旧JSON兼容。[任务](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/ec/ec7dcc5179f0e584__WMS-SLOT-01_库位优化_完整设计_RECOVERY_v1.1_R2.md:94)

原稿固定CP6 `90c871fe…` 的SlottingService映射：旧分析按StockTransactions行数/产品最大库存位置及命名prefix推荐；旧Approve逐行随机operationId建Mobile任务，可部分派发仍Approved；Cancel只取消Pending。源文指出这些事实，未证明本次运行或当前全部权限。迁移保LEGACY_ROW_COUNT和RecommendationsJson；旧Approved不等MOVED，坏JSON保LEGACY_PARSE_ERROR、不当空方案。OLD/HOLD/NEW与全部后台/客户端writer按范围切换，既有任务完整后继族未闭不得迁移。[旧实现](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/ec/ec7dcc5179f0e584__WMS-SLOT-01_库位优化_完整设计_RECOVERY_v1.1_R2.md:17)、[兼容](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/ec/ec7dcc5179f0e584__WMS-SLOT-01_库位优化_完整设计_RECOVERY_v1.1_R2.md:178)

## 10. 验收及执行状态

15AC全NOT_RUN：非法时间零对象/合法丢响应回同Analysis+独立Plan；完整零样本与UNKNOWN分开；同频完整维度排序及14位汇总；非正式/blocked位置不可选；重叠范围拒；改Proposal旧批准412；NO_MOVE无假任务；丢派发响应原键同Stage；三行两登记一失败逐项PARTIAL；缺Stock回执未知；NEW旧四写点423且Claim随Root不重释放；stop/dispatch唯一先后；OLD4+6完整族不假关闭；源仓无权/跨租户拒；坏旧JSON不丢原文。

JSON例已逐字段读，特别POSTED但receiptRefs=[]不得映射MOVED，分析ID不能当PlanID；全零摘要为隔离形状占位，不是生产结果。[验收](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/ec/ec7dcc5179f0e584__WMS-SLOT-01_库位优化_完整设计_RECOVERY_v1.1_R2.md:96)、[完整例与反例](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/ec/ec7dcc5179f0e584__WMS-SLOT-01_库位优化_完整设计_RECOVERY_v1.1_R2.md:198)

## 11. 编码前待核

S-G01 MASTER正式LocationKind/规则/容量；S-G02统计事件单位/sourceCut；S-G03 SlotTransferAdapter创建/查询/全部Stage关闭；S-G04 NEW唯一路线与旧V2全写点围栏；S-G05批准政策与legacy writer。现实采用均UNPROVEN。跨模块复用ABC时明确选择规则版本，不能将ANALYTICS末项累计与本模块前缀累计互换。X3共用接受/独审和目标CURRENT已补核；待双方Owner合同集中比对。

## 12. 来源与阅读覆盖

本轮新读正文1–114、198–410；115–197共同A1–A7经逐字比较与已全文读KIT正文134–216完全相同，明确复用该准确段，不冒称重复逐行新读。所有独有DTO/算法/15AC/JSON均已读。记录及SHA见[root阅读账](D:/CP6/docs/CP6_开发设计文档_20261010/evidence/root-specialty-reading.json)。

## 必要材料整理结论

当前15项必需引用均已完整阅读或使用有证据的精确复用：主文、当前接受/审查、原范围、AC与任务文字以及来源资格已纳入设计。历史成员目录只证明来源身份，不等于旧代码全文审计；外域实际采用、Provider和业务运行继续按本册门禁确认。见[逐目标必需引用核对](../evidence/root-specialty-required-closure.json)。后续整任务进行一次集中跨模块复核，不将静态设计整理等同实际验收。
