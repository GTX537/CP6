# WMS-REP-01 库内补货开发设计

状态：当前正文及X3接受/评审元数据已整理，待双方合同集中核对。入口见[模块目录](D:/CP6/docs/CP6_开发设计文档_20261010/模块目录.md)。

## 1. 目的与职责

仓管按正式拣选位规则，观察目标现量、已登记且未到的补货义务与源可移动份额，生成解释性建议，冻结多源分配，登记订单，再由唯一Transfer/Stock路线实际搬运。四SPEC分别是建议、批量生成、执行和取消。REP拥有规则读本、建议、订单、目标义务与关联；不创造采购/制造供给，不维护第二套Stock主账。生成、派发、搬完各有独立结果。

例：现量3、目标10、已有未到2、源可移动20，只建议5。派发5不变库存，Stock真正MOVE后源减5、目标加5；原在途2仍独立。[原规范](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/9d/9d108fba82cd4683__WMS-REP-01_库内补货_完整设计_RECOVERY_v1.1_R2.md:13)

## 2. 当前版本与接受边界

本轮补核结论：2026-10-08T11:56:55Z代理接受准确六册R2及具名profile；原九根累计静态闭合，全部业务AC仍NOT_RUN、Owner运行采用UNPROVEN。[X3共同阅读与采用边界](D:/CP6/docs/CP6_开发设计文档_20261010/evidence/X3-六专项阅读与采用边界.md)保存准确组合、112AC/31任务逐条等值核对和历史R1原ZIP缺口。下文提及原候选标签均属保留历史。

采用矩阵指定 v1.1 R2 RECOVERY，源SHA前缀`9d108fba82cd4683`；全SHA、CURRENT、UA和评审清单见[必需输入](D:/CP6/docs/CP6_开发设计文档_20261010/indexes/required-inputs.json)。原正文STOPPED_FOR_INDEPENDENT_REVIEW是历史作者状态，不能单凭标签否定后继接受，也不能凭矩阵选择宣称本次已读全部接受元数据。正文18AC全部NOT_RUN、六Owner采用门UNPROVEN。规则核心面向通用仓储，PIK/RES只作历史例；本稿不把旧纸业前缀当正式库位身份。[原规范](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/9d/9d108fba82cd4683__WMS-REP-01_库内补货_完整设计_RECOVERY_v1.1_R2.md:3)

## 3. 数据、业务键与计量

|对象|最小必要内容及边界|
|---|---|
|ReplenishRuleView|目标正式Location集合、产品/技术/货主/baseUom、min/max、可空lotMultiple、sourceRuleRef、有效期与覆盖|
|SuggestionRow|rule/cut、完整维度、onHand/openIncoming/effectivePosition/needed/proposed、不可变allocationPlanRef、准确Root源候选与排除原因|
|AllocationPlan|suggestion/line/cut、demandSlotKey、目标、总量、1..200稳定splitKey，每split一个源Location及互斥Root范围|
|Order|一个源地点到一个目标；可含同维度多个Root；selection版、quantity、priority1或2、MANUAL/BATCH/ALERT与原建议；计划/已移/剩余分开|
|ActiveIntent|一个目标义务总量及所有split，REGISTERED_COUNTED/ISSUING/IN_PROGRESS/STOP_PENDING/SETTLED；保存confirmedMoved/closedUnexecuted及知识|
|Batch|每行原allocationPlan及其split→orderRef/quantity/CREATED或EXISTING；registration与dispatchSummary分轴|
|TransferExecutionLink|WMS_TRANSFER、原businessKey、可空requestRef、完整stageRefs/receiptRefs、覆盖和原Owner状态|
|StopView|准确scopeSpans、原关闭/Stock结果、futureDispatchBlocked和知识，区分CLOSED_NO_EFFECT/WITH_EFFECT|

DemandSlot按准确目标+产品+技术+货主+基本UOM；Batch唯一(Tenant,Environment,suggestionId,selectionDigest)，split唯一(allocationPlanId,splitKey)，派发唯一(tenant,environment,orderId,selectionVersion)。换HTTP幂等键、Batch或plan版本不能绕原同义义务/Source预算。ActiveIntent是工作范围登记，不是Stock Claim、预留量或可消费账。[原规范](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/9d/9d108fba82cd4683__WMS-REP-01_库内补货_完整设计_RECOVERY_v1.1_R2.md:39)

X3命令Quantity为正量、最多13位整数/8位小数；只读TotalQuantity允许30位整数/8位小数。已知空集0、UNKNOWN的null、跨产品/技术/货主/UOM不可汇总分别表达。两个合法13位命令相加可得到14位只读合计，不能把该合计再当一条合法写命令。[原规范](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/9d/9d108fba82cd4683__WMS-REP-01_库内补货_完整设计_RECOVERY_v1.1_R2.md:378)

## 4. 计算、登记、派发与取消

建议固定Rule/Scope/Stock/Task/质量技术同cut，规则0≤min≤max且max>0；只有effectivePosition低于min才补至max。空目标必须有Stock完整初始化且实物0的证明才onHand=0。DRAFT不计，批量REGISTERED一提交即计，PREPARED/ISSUING/TaskIssued/进行中按准确未到范围计一次；Order、Task、Transfer是同一份义务的关联，不能分别累加。停止未知保保护且有效位置UNKNOWN，不能按0重新补货。[原规范](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/9d/9d108fba82cd4683__WMS-REP-01_库内补货_完整设计_RECOVERY_v1.1_R2.md:62)

有效位置=现量+未到并集，缺额=max−有效位置。源按当前合法可移动Root、正式储备区、同完整维度过滤，依到期、收货、sourceLocationId/rootId稳定排序。总proposed=min(缺额,全部合法可移动量)，再对**总量向下**取lotMultiple倍数；不足一倍NO_FULL_MULTIPLE。按该顺序截准确spans，再按源地点分split；不对每split各自向上取整。容量无法证明时保需求解释但不能登记派发；人工也须同DemandSlot预算。[原规范](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/9d/9d108fba82cd4683__WMS-REP-01_库内补货_完整设计_RECOVERY_v1.1_R2.md:66)

批量在一个短元数据事务锁Batch canonical→全部DemandSlot稳定序→规则head/现行ActiveIntent，核所选完整Plan/cut/digest。任一异常整组零新订单；同输入既有义务回EXISTING，异输入或超当前缺额409回滚。每行一个ActiveIntent总7，其A3/B4两个Order提交即REGISTERED且计7，不能计成7+3+4=14，不能等prepare才计。A到3必须在同观察cut转onHand3+未到4；跨轴cut不完整仍UNKNOWN。人工DRAFT的prepare先同Slot登记唯一义务，再发准备，失败/未知不自动释放。[原规范](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/9d/9d108fba82cd4683__WMS-REP-01_库内补货_完整设计_RECOVERY_v1.1_R2.md:70)

prepare冻结选择/范围/两端规则，先持久原dispatchKey/operationId/writerEpoch和发送知识，再发Transfer。正常每split一个完整Move Stage；TaskIssued只表示任务登记，IN_PROGRESS表示执行，准确Stock MOVE两腿及Owner终态齐才MOVED。缺物理回执仍UNKNOWN。实际部分交Transfer ActualFact/Case和有权后继，不自动建剩量任务。Root有销售Claim必须遵原允许MOVE的Claim/Use合同随行，不释放重建。[原规范](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/9d/9d108fba82cd4683__WMS-REP-01_库内补货_完整设计_RECOVERY_v1.1_R2.md:80)

停止先封OrderGate及所有原请求；无taskId但可能已发也在关闭集。完整NEVER_SENT且未来永久封住才CANCELLED_NO_EFFECT；已移保历史CLOSED_WITH_EFFECT；执行未知保STOP_REQUESTED/保护。关闭须原任务/Stock、完整调用别名/旧worker与现场证据，齐备后同gate释放本域剩余ActiveIntent并记Closure/Outbox；NOT_FOUND、租约过期或空查询均不足。搬错需独立授权反向MOVE，取消不清库存/容量字段。[原规范](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/9d/9d108fba82cd4683__WMS-REP-01_库内补货_完整设计_RECOVERY_v1.1_R2.md:94)

## 5. API、Owner接口与事件

候选根`/api/wms/replenish/v1`：context、rules/orders/suggestions/batches/history；POST suggestions；orders创建和DRAFT完整PATCH；POST batches、orders/:id/prepare、dispatch、stop；GET dispatches/by-key、allocation-plans、active-intents、execution-link、legacy-task-families；operations/:id/resume携原step/expectedEpoch。响应201元数据首次、200原结果/确定完成、202已持久待Owner；错误沿公共Problem。旧根code/message/data/taskNo不被偷偷换成新DTO。[原规范](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/9d/9d108fba82cd4683__WMS-REP-01_库内补货_完整设计_RECOVERY_v1.1_R2.md:52)

NEW仅`ACCEPTED_TRANSFER`：拟议`ReplenishTransferAdapter-1`将原Order/Selection/SourceBudget登记至Transfer Request和全部Stage，真实移动/Claim/容量唯一经TRANSFER→Stock。缺采用WAITING_ADOPTION；旧MobileTaskV2不能成为NEW外壳，Create/Reserve/Complete/Partial/Cancel全部受writer门。S1候选不能提供回退授权。OLD仅准确历史及原Owner已授权续办；LegacyTaskFamilyView必须包含全部父子/原范围/回执，旧4已移+6子任务不能只看父头。[原规范](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/9d/9d108fba82cd4683__WMS-REP-01_库内补货_完整设计_RECOVERY_v1.1_R2.md:84)

最终原Owner Stock UoW检查订单执行门、原范围、Root/Claim/两端容量。补货完成关联应参与同事务；实际部署若只能异步则明确“结果关联待补”，查原结果补link，严格不再物理提交。不能以消息已发送证明MOVE成功。[原规范](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/9d/9d108fba82cd4683__WMS-REP-01_库内补货_完整设计_RECOVERY_v1.1_R2.md:88)

## 6. 事务、并发与恢复

持久层包括RuleRead、Suggestion/Rows、OrderHead/SelectionVersion、DemandSlot/ActiveIntent/SplitIntent、AllocationPlan/Split、Batch/Item/Link、DispatchJournal、MoveObservation、StopFence/Item、Recovery/Outbox/Inbox/Audit。所有PK/FK带Tenant+Environment；范围重叠须集合门，不能只用UNIQUE(from,to)。规则读取不是本模块配置权。[原规范](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/9d/9d108fba82cd4683__WMS-REP-01_库内补货_完整设计_RECOVERY_v1.1_R2.md:106)

并发两批同见缺7，首登记5后，第二须读到义务5并返回冲突或让用户明确选择剩2新建议，不能再登记7。dispatch/stop共享OrderGate；stop先赢拒晚派发，dispatch先赢则关闭集包含未返回locator的请求。Stock最终门决定完成/停止先后；UNKNOWN不换operationId。旧CANCELLED_NO_EFFECT不重启，新需求需新cut和明确前驱关闭证明、新代义务及旧canonical墓碑。[原规范](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/9d/9d108fba82cd4683__WMS-REP-01_库内补货_完整设计_RECOVERY_v1.1_R2.md:78)

通用恢复只QUERY_ORIGINAL、RESUME_ORIGINAL_STEP、LINK_CONFIRMED_RESULT、REDRIVE_ORIGINAL_EVENT、REQUEST_SEAL；锁内不HTTP，外部收集后按确定全序一次取得资源，发现新资源整次回滚。当前读不得把历史moved当新cut有效现量；成功丢回执沿原dispatch/Transfer/Stock补链。[原规范](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/9d/9d108fba82cd4683__WMS-REP-01_库内补货_完整设计_RECOVERY_v1.1_R2.md:190)

## 7. 权限、租户与输出

能力拆rep.read/suggest/order.create-edit/batch.generate/prepare/dispatch/stop/recover.query-resume/history-export。源和目标区域同时授权；仅有目标权不泄露源量。新动作最终门重读当前授权，撤写权不否认历史且当前读权仍适用。priority默认推荐2，1需相应能力与理由，不在服务内暗添加急策略。导出保完整维度、cut、coverage和水印，不跨Owner/UOM合量。[原规范](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/9d/9d108fba82cd4683__WMS-REP-01_库内补货_完整设计_RECOVERY_v1.1_R2.md:108)

## 8. 页面与交互校验

RP01服务端分页列表分列计划/已派/已移，状态0可筛，“全部”是未传；RP02只选已批准规则；RP03显示目标现量、未到并集、缺额、容量及各Root；RP04手工DRAFT/批量REGISTERED、选版和prepare；RP05展示同目标各split与整组登记结果，随后逐订单独立派发可PARTIAL；RP06只读准确Task/Stock结果，无手工标记完成；RP07显示未知、未发、已移、关闭与原Owner处置。

更换Scope/Owner/技术/UOM/目标使候选与preview失效，重算当前源目标。不能拿前500条当完整仓库或完整取消集合；服务端完整登记表决定关闭范围。派后禁止直接PATCH冻结任务。列表TaskIssued用明确标签，不借“出库已分配”混淆。[原规范](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/9d/9d108fba82cd4683__WMS-REP-01_库内补货_完整设计_RECOVERY_v1.1_R2.md:29)

## 9. 实施顺序和旧代码差异

1. D01正式规则、空目标证明、完整cut需求计算及RP01–03。
2. D02不可变AllocationPlan、多源split、提交即计ActiveIntent、订单/批量同Slot锁。
3. D03唯一Transfer桥、全部Stage查询/关闭、原物理结果门、旧V2全部写点fence。
4. D04停止、UNKNOWN、晚包、完整任务族和有据后继。
5. D05兼容迁移、权限、完整分页/导出，集中做18AC。

原文固定CP6@90c871fe的历史源码观察：旧GenerateBatch用PIK/RES前缀、仅已有Stock行、min≤0改10、只取第一源，遗漏空目标与多源/并发预算；旧Execute只建MobileTaskV2并置状态2，不移动实物；旧Update/Cancel按Pending/Claimed限制。旧UI500条客户端分页与状态2标签需适配。旧测试文本派发时源Physical仍100/Allocated20，完成80/20两MOVE腿；本轮未执行或新审这些源码。[原规范](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/9d/9d108fba82cd4683__WMS-REP-01_库内补货_完整设计_RECOVERY_v1.1_R2.md:21)

DP11旧Pending无任务且零效果确证才转草稿；TaskIssued/Claimed保原链接HOLD；Executed缺Stock原件仅LEGACY_EXECUTED_REPORTED。DP12保旧编号/状态/响应，NEW旧写423；DP16按准确对象范围OLD→HOLD→NEW，含API/job/mobile/import全部writer，新效果后只前滚。[原规范](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/9d/9d108fba82cd4683__WMS-REP-01_库内补货_完整设计_RECOVERY_v1.1_R2.md:110)

## 10. 验收设计和验证状态

18原AC均NOT_RUN：01空目标10/未知null；02现3+在途2只补5；03维度与14位只读总量；04前缀不能绕正式禁用；05缺7多源3+4及并发唯一义务；06一行旧版整批零新增；07换HTTP键回同Order、已到3/未到4同cut；08派发无物理效果；09一次MOVE80/20；10响应丢失不换原键；11销售Claim随行；12NEW旧四写点零旁写；13stop/dispatch竞态；14未发/已移分开且OLD4+6全族；15NOT_FOUND/过期保持UNKNOWN；16源目标双权；17旧完成无映射不造Receipt；18分页/状态0/晚搜索响应。静态JSON仅形状示例，64零摘要不能执行。[原规范](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/9d/9d108fba82cd4683__WMS-REP-01_库内补货_完整设计_RECOVERY_v1.1_R2.md:122)

## 11. 待确认和实施阻断

REP-G01正式规则/完整Stock cut；G02目标准确义务去重与两端权限；G03原Transfer键/全部Stage查询关闭；G04唯一NEW路线及旧V2全writer封闭；G05MASTER Domain/Claim/技术质量；G06迁移映射。均实际采用UNPROVEN；缺对应门只阻相关新效果，允许有权读/草稿。尤其须双侧核当前cut将已到范围移出OpenIncoming与纳入onHand的共同观察协议，不能用独立两次读拼假完整。[原规范](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/9d/9d108fba82cd4683__WMS-REP-01_库内补货_完整设计_RECOVERY_v1.1_R2.md:112)

本文主语义已全读；X3 UA/CURRENT和组合评审已完成本轮补核，双方Owner合同与当前代码实现待集中核对。按原稿限制NEW每split整Stage，不能为界面方便擅开自动部分重派。

## 12. 来源与阅读覆盖

主件1–147、218–409实际读取，含全部字段、18AC及全部JSON；148–217公共A1–A6与已全文读KIT 134–203逐行相同，做精确比较并复用其语义阅读；A7差异218–235逐行读。其历史Library授权窗口/403与固定源码观察是原作者资格，不冒成本轮fresh网络或代码验证。完整身份、已核组合和逐文件覆盖见[阅读台账](D:/CP6/docs/CP6_开发设计文档_20261010/evidence/root-specialty-reading.json)。本页是开发整理，不修改原件，不执行历史指令，不证明运行通过。

## 必要材料整理结论

当前15项必需引用均已完整阅读或使用有证据的精确复用：主文、当前接受/审查、原范围、AC与任务文字以及来源资格已纳入设计。历史成员目录只证明来源身份，不等于旧代码全文审计；外域实际采用、Provider和业务运行继续按本册门禁确认。见[逐目标必需引用核对](../evidence/root-specialty-required-closure.json)。后续整任务进行一次集中跨模块复核，不将静态设计整理等同实际验收。
