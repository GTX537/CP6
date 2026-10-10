# PLAN-PO-01 正常场景与恢复状态设计

本册从当前 normal 原件整理具体业务数据、事务边界和恢复结果，供实现与测试时使用。本册已完成 root 负责的 20 份 CP18 场景；CP19 九份连续场景和 CP20/CP21 三份后继场景另册追踪。必要材料整体状态以模块和逐文件证据为准。所有样例仍为条件设计：business NOT_RUN、runtime UNPROVEN、actual adoption 0。

覆盖与精确来源见 [逐文件阅读记录](D:/CP6/docs/CP6_开发设计文档_20261010/evidence/PLAN-PO-01-root-normal-reading.json:1)；公共接口、权限、当前 SQL/TS/C# 合同见 [接口与交付附件](D:/CP6/docs/CP6_开发设计文档_20261010/contracts/PLAN-PO-01-接口验收与交付附件.md:1)。本册不会把样例中的 PRODUCTION、authenticated=true 或 COMMITTED 当作真实环境验收。

## 1. 基础 100 EA：四个需求切片、三个下达分段

100 EA 分成采购 40、工单 40、调拨 20，保留计划范围 [0,40)、[40,80)、[80,100)。采购分段来自两个各 20 EA 的需求切片，它们共享不可拆分组，必须由一个完整分段和同一条 leg 承接，或整体不选；不能为方便拆为两个独立采购腿。工单来自一个 40 EA 切片，调拨来自一个 20 EA 切片。需求原区间和计划位置区间分别保存，不能因计划位置从 80 开始而改变原需求数量。

40 项当前事实覆盖来源、MRP 发布、物料/地点/UOM、计划策略与供给。可用量分别为 40/40/20，安全库存 10 已从 eligibility 排除，不能再减一次；受保护的 reservation 也不能重新扣减。批量策略 EXACT_REQUEST、倍数 1，不能悄悄补超额。提前期按 7 个日历日计算：10 月 5 日释放、10 月 12 日需求；该日即使列入假日，也不能擅自改成工作日算法。

依据：[PLAN_PO_R3_CP18_SOURCE_NORMAL_DATA.json](D:/CP6-archives/consolidation-20261010/root-planning-cache/plan-po-cp22/members/PLAN_PO_R3_CP18_SOURCE_NORMAL_DATA.json:1)

## 2. OwnerProfile 与调拨桥接

每个分段用自己的 branch、portion、Owner namespace 和当前上下文。采购使用 PURCHASE 技术适用证据与 PUR 注册切面；工单用独立 PLAN_MAKE 描述、40 EA 产出与 CFG/ENG/REL 当前输入；调拨使用 TRANSFER_REPLENISHMENT、SELF 所有权和源/目的地点，不能挪用工单的 PRODUCTION purpose。三个分支种类不表示全系统最多只能有三个分段。

调拨将共同 40 项来源事实映射到调拨 20 EA 的独立上下文：五个 root 加 16 项 proof，共 21 项 transfer current reads。共同 manifest 和 transfer manifest 是不同身份。桥接的 proof key 由 Owner/id 规范化后摘要构成 CP18_TRANSFER_PROOF 命名空间；原共同来源事实保持自己的键。预算键、授权范围和需求 lineage 必须一致。启动时还没有实际调拨创建回执，不能从独立授权存在推导“已经创建”。

依据：[PLAN_PO_R3_CP18_OWNER_PROFILE_NORMAL_DATA.json](D:/CP6-archives/consolidation-20261010/root-planning-cache/plan-po-cp22/members/PLAN_PO_R3_CP18_OWNER_PROFILE_NORMAL_DATA.json:1)；[PLAN_PO_R3_CP18_TRANSFER_BRIDGE_NORMAL_DATA.json](D:/CP6-archives/consolidation-20261010/root-planning-cache/plan-po-cp22/members/PLAN_PO_R3_CP18_TRANSFER_BRIDGE_NORMAL_DATA.json:1)

## 3. 独立 100 EA 工单例证

SC06_100 是单一 100 EA 工单世界，不是将上一节的 40 EA 请求改个数量即可得到的例证。准备阶段有 4 项 capture，preparationRef 为空；真正创建 preparation 后，12 项 prepared capture 绑定实际 preparationId/revision。准备范围、原 executionRequestKey、sourceDescriptor、每项 issuer 的 body/canonical/digest 都要对齐。

链条依次是读取 source → CreatePreparation → EngineeringCheck → PrepareSnapshot → CreateRootWorkOrder。读/评估步骤没有 Owner command；三个写命令使用自己的原键。后续 DTO 只能在前一步真实结果持久化后构造。根工单返回 CREATED、控制轴为 NO_NEW_EXECUTION；根工单创建成功并不授予新的执行许可。已存历史 REL reservation 是根创建证据，后续 dispatch current cut 的 11 项输入不包含这份历史 reservation。

原样例保留的 08:00/17:00 排程与新 DTO 的 00:00/23:59:59 被原件明确说明为不同的有效来源投影，不能改写历史字节来使其看起来相同。另已确认同一 event/leg/command 的 revision.BodyJson 与实际 OwnerReceipt 使用不同请求/结果/快照摘要，见 PLAN-IF-014。原件保留，对齐前不能将此样本直接当作通过的验收向量。

Owner 已接受后，独立的 supply projection 提交写 revision、head、audit、outbox 四项。旧计划供应 100 切换为工单供应 100，总量仍是 100，不能叠加成 200；采购草稿与调拨请求不自行形成 active supply。重放零写入。ExactTupleHash 的此例 preimage 使用带字节长度前缀的 SQL nvarchar/UTF-16LE，不是对任意 JSON 对象做 JCS。

依据：[PLAN_PO_R3_CP18_WO_SUPPORT_NORMAL_DATA.json](D:/CP6-archives/consolidation-20261010/root-planning-cache/plan-po-cp22/members/PLAN_PO_R3_CP18_WO_SUPPORT_NORMAL_DATA.json:1)；[PLAN_PO_R3_CP18_SC06_100_NORMAL_DATA.json](D:/CP6-archives/consolidation-20261010/root-planning-cache/plan-po-cp22/members/PLAN_PO_R3_CP18_SC06_100_NORMAL_DATA.json:1)

## 4. 多分支启动只登记工作，不提前宣称 Owner 成功

Firm 从业务版本 1 到 2；Preview 捕获完整当前 heads、授权与三个 workflow，有效期五分钟；Start 消费原 preview，将版本推进为 3，并在一个本地事务内形成 34 项写入：本地命令槽、消费后的 preview、work、三条 leg/portion/workflow、十个步骤、保护占用、checkpoint、order revision/head、audit/outbox。

最初只有采购拥有完整 native intake 与一条可派发的 Owner outbox。工单只有 SourceSearchRequest READY，未来 preparation/root 请求与摘要仍为空；调拨只有无 body 的 ContextRead READY，不能越过持久化后的 Context/Catalog 发 RequestCreate。汇总 MULTI_OWNER outbox 不是可派发的原 Owner 命令。调拨后续 READ_TRANSFER_CURRENT 的 commanded 骨架也不能以尚为空的原键直接调用；须在后继流程中取得真实绑定。

Start 返回 CONVERSION_WORK_REGISTERED；work=PENDING、leg=OWNER_PENDING，不等于 Owner 已创建。相同原键同 body 返回原终态且零新增写入，异 body 是 COMMAND_MAPPING_CONFLICT。示例中的 rowversion 是任意 8-byte token，不是数据库执行结果，更不能做加一算法。所有外部 producer 资格均为 REQUIRED_FENCED 前提，没有安装任何 ADOPTED 行。

依据：[PLAN_PO_R3_CP18_MULTI_START_NORMAL_DATA.json](D:/CP6-archives/consolidation-20261010/root-planning-cache/plan-po-cp22/members/PLAN_PO_R3_CP18_MULTI_START_NORMAL_DATA.json:1)

## 5. 无效果证明与释放

PUR 原 intake 的 REJECTED 且 provesNoEffect=true，必须绑定精确原 command、allocation、request digest、结果身份。例证用 16 项同事务写入，将原 command/workflow 终结、保存 no-effect fence 与完整释放集合、更新 protection、work/head、checkpoint、audit/outbox 和 Inbox。只释放该原采购分段，保留来源与其他分段。旧原键永不重新开启；后续新意图使用新的明确请求键，并重新验证当前来源、策略、租约与权限。

WO/WMS 的一般失败或单独“没创建”字段不足以释放。当前提议闭合合同同时要求：未创建证明、全部未来创建路径永久 fence、原 slot 闭合和 binding 闭合；覆盖 PRIMARY_CREATE、IDEMPOTENT_REPLAY、RECOVERY_RETRY，且绑定原 actor/context、原 canonical request、完整原范围和已认证 Owner proof。样例中的四份 proof 不是 Owner 实际签发，仍需独立采用。

查询已有闭合证据是只读：AVAILABLE/NOT_AVAILABLE 为 200，UNAVAILABLE 为 202。后两种不证明 no-effect；查询不会签发证明，不写 Owner 或 Planning 状态。

聚合结果也要区分：accepted+pending 或 accepted+unknown 仍 PENDING；accepted+其余全部 no-effect 才 PARTIAL；全部 no-effect 才 FAILED_NO_EFFECT。订单还存在未转换的 available portions 时仍可能 PENDING，不能用这个聚合字样替代实际效果检查。

依据：[PLAN_PO_R3_CP18_NO_EFFECT_NORMAL_DATA.json](D:/CP6-archives/consolidation-20261010/root-planning-cache/plan-po-cp22/members/PLAN_PO_R3_CP18_NO_EFFECT_NORMAL_DATA.json:1)；[PLAN_PO_R3_CP18_CREATION_CLOSURE_NORMAL_DATA.json](D:/CP6-archives/consolidation-20261010/root-planning-cache/plan-po-cp22/members/PLAN_PO_R3_CP18_CREATION_CLOSURE_NORMAL_DATA.json:1)；[PLAN_PO_R3_CP18_EXISTING_CLOSURE_READ_NORMAL_DATA.json](D:/CP6-archives/consolidation-20261010/root-planning-cache/plan-po-cp22/members/PLAN_PO_R3_CP18_EXISTING_CLOSURE_READ_NORMAL_DATA.json:1)

## 6. Ignore 与查询失败

Ignore 可用于 SUGGESTED，也可用于已经 FIRM 但没有外部效果的状态。必须检查完整 protection/leg/winner/hold 集合，旧腿都已证明无效果且无 hold/winner 才能考虑放弃。旧 CP5 两例是保留历史世界；当前完整来源例证还要求独立 Core SDK 的新授权序列和 current-head CAS，新增 ignore 权限不能由 Planning 本地补造。当前例证 6 项写入、零 Owner/供应效果，重放不写入。

原 source read 超时形成 UNKNOWN / OWNER_TIMEOUT / QUERY_ORIGINAL，保存本地 CommandSlot 与 Audit 两项；不会写新的 OwnerCommand、checkpoint、head、winner、release 或库存。原冻结查询输入保留；同请求重放仍是原 202。若连本地 commit 都未知，则返回原命令查询入口，不能推断成功。

依据：[PLAN_PO_R3_CP18_IGNORE_NORMAL_DATA.json](D:/CP6-archives/consolidation-20261010/root-planning-cache/plan-po-cp22/members/PLAN_PO_R3_CP18_IGNORE_NORMAL_DATA.json:1)；[PLAN_PO_R3_CP18_CURRENT_FIRM_IGNORE_NORMAL_DATA.json](D:/CP6-archives/consolidation-20261010/root-planning-cache/plan-po-cp22/members/PLAN_PO_R3_CP18_CURRENT_FIRM_IGNORE_NORMAL_DATA.json:1)；[PLAN_PO_R3_CP18_OWNER_READ_FAILURE_NORMAL_DATA.json](D:/CP6-archives/consolidation-20261010/root-planning-cache/plan-po-cp22/members/PLAN_PO_R3_CP18_OWNER_READ_FAILURE_NORMAL_DATA.json:1)

## 7. 分页、切换 writer 与接管

分页例证有 21 条同一授权 cut 的结果，pageSize=20，第一页 20 条、第二页 1 条、不重叠。排序依次 requiredDate、sourcePriority、sourceTieBreakKey、plannedOrderId。cut 绑定规范化查询、E/T、principal、permissionDigest、fieldMask 和完整授权集合；cursor 五分钟过期。示例只给 payload，未执行签名，不存在可直接发送的运行 token。下一页授权或来源 cut 改变要从第一页重来。

writer 8→9 接管时，已发往外部 Owner 的请求仍可能完成；不能承诺跨服务“旧 writer 什么也没发”。新的 writer 必须同时具备 fresh cutover 与 recover 权限、精确当前 Core composite head、原 checkpoint 和租约条件。显式 takeover 三项写入只登记原操作接管；随后领取原 outbox 的两项写入仍使用原 Owner key/body，第一步查询同一原键，绝不能新建采购请求。

原文件的恢复授权前提已更新到 Core v2，但 dispatchClaim.preRows/postRows 对权限 head 的继承存在待核差异。实施时以当前完整权限合同为约束，该静态状态图在对齐前不能作为已通过的数据库用例。

依据：[PLAN_PO_R3_CP18_READ_NORMAL_DATA.json](D:/CP6-archives/consolidation-20261010/root-planning-cache/plan-po-cp22/members/PLAN_PO_R3_CP18_READ_NORMAL_DATA.json:1)；[PLAN_PO_R3_CP18_EPOCH_NORMAL_DATA.json](D:/CP6-archives/consolidation-20261010/root-planning-cache/plan-po-cp22/members/PLAN_PO_R3_CP18_EPOCH_NORMAL_DATA.json:1)
## 8. 延迟 Inbox：等前序不等于接受效果

Owner sequence 2 先到时只登记 WAITING_PREDECESSOR/STREAM_GAP，保存原始字节与时间。sequence 1 接受后，原 sequence 2 恢复使用同一事件，不新造事件或时间：Inbox 转 APPLIED，插入新的 typed observation 和 audit。此阶段没有 winner、release、supply 或新 Owner command；原 observedAt/receivedAt 与恢复处理时刻分别保留。

来源侧也必须重新检查真实前序和完整发布。缺失/失败 result set 的样例在前序后来到达后仍为 REJECTED/RESULTSET_INCOMPLETE，只更新 Inbox，不能用“前序已到”代替完整来源校验。成功样例则是两个独立 source/order 世界：前序 CP18-PREV 与原等待 CP18 分别拥有完整 40 项事实、三个分段和四个 pegging；链上的 previousDigest 指向前一个 fact payloadDigest，不是整个 transport envelope 的摘要。

成功恢复以 59 项实参、四组 TVP 和当前 manifest 进入原来源应用过程。原 WAITING 事件先在同事务中转 RECEIVED，形成 22 项领域/审计/outbox 插入，再转 APPLIED，共 24 项有序写入；ACK 保留原先接收时间。新建议总量 100、分段 40/40/20，active supply 仍为 0，没有 Owner receipt/winner/库存效果。只有真实各 Owner pin 与 producer 资格满足时才可采用；样例的 0 adoption 未被改变。

依据：[DEFERRED_STATE 原件](D:/CP6-archives/consolidation-20261010/root-planning-cache/plan-po-cp22/members/PLAN_PO_R3_CP18_DEFERRED_STATE_NORMAL_DATA.json:1)

## 9. 撤回与迟到成功：两种到达顺序得到相同业务结论

保留的 SCENARIO_ROW_IMAGES 先用历史 CP10 的 40 EA 工单世界说明取消先到/接受先到；其中 SC06_100_arithmeticOnly 只验证数量投影，不能代替本册第 3 节完整 100 EA native chain。SC22 不完整发布只隔离 Inbox。该历史状态图的 revision 写入还有静态不一致，见第 12 节。

当前三 Owner 样例各有两个完整世界：先撤回再接收 PR、WO、WMS 原成功，或先接收三个原成功再撤回。SourceCorrection 同一事务写入 17 项：新的来源 revision/head、来源 Inbox、CorrectionCase、三个 CasePortion、三条 CaseLeg/leg hold、订单 revision/head、audit/outbox。它不会替 Owner 撤单，也不虚构下游无效果。

PR 原成功与 WO 原根工单成功分别形成 15 项本地接受写入。每次保留原 command/body、原结果、receipt/winner、原范围 protection、工作流终态、checkpoint、订单 revision/head 和审计通知。WMS 原 T04 201 的接受形成 20 项有序写入，其中真实创建结果才使后续 T05 查询键/body 具体化；创建 receipt 与后续 current read 是两个不同结果轴。

两种顺序最终都保持：source=WITHDRAWN，原 FIRM 决策保留，三个真实原 winner 保护总量 100，CorrectionCase=OPEN、三条 hold=CORRECTION_REQUIRED。work 的 SUCCEEDED 只表示原创建结果已确认；它不会关闭纠正任务、恢复当前来源资格或授权新创建。receipt 本身增加的 active supply 为 0。WO 的支持材料必须属于自己的 [40,80) 和 L2-1，不得挪用采购 [0,40) 的两个 20 EA 切片；准备前 4 项 capture 与准备后 12 项 capture/实际 preparation 身份分别核对，后续创建 current cut 排除历史 REL reservation。

依据：[SCENARIO_ROW_IMAGES 原件](D:/CP6-archives/consolidation-20261010/root-planning-cache/plan-po-cp22/members/PLAN_PO_R3_CP18_SCENARIO_ROW_IMAGES.json:1)；[SC18 三 Owner 原件](D:/CP6-archives/consolidation-20261010/root-planning-cache/plan-po-cp22/members/PLAN_PO_R3_CP18_SC18_THREE_OWNER_NORMAL_DATA.json:1)

## 10. WMS 创建事实与当前读数分开保存

本地 step 2 对应原生 T04 POST /requests，T03 是搜索。T04 原 201 的 DRAFT 请求、原 actor/context/native frame、source binding 与独立 PLANNING delegation 完整保存后，20 EA 保护转 OWNER_ACCEPTED。其余 80 EA 仍待定，所以 work 保持 PENDING。step 3 才获得独立 TransferResultQuery 原键、冻结 body 和 outbox，不能复用 T04 创建键作为新的创建意图。

四个 T05 世界均保留原创建事实：同版本完整结果；请求 revision 4/ACTIVE 且已发出 5、在途 5、剩余 15；content version 2 且数量 UNKNOWN；原生 503 UNAVAILABLE。第三类的 currentDispatched/currentReceived/currentTransit/returned/disposed/requestRemaining 为 null，不能当作 0；第四类不会删除此前 receipt/winner。每次当前读更新 observation、本地命令和 audit，加上查询 job 的 command/step/workflow/outbox 四项，共 7 项元数据写入。OwnerReceipt、winner、protection、leg/work、order head、source/correction 不变；查询不制造库存或供应效果。

原 actor、native frame、body/range 或 winner 不符分别拒绝；晚到 current generation 也拒绝。失败表中的“零新增有效写入”指零新增领域效果，不能误读成不存在观察/查询任务记录。

依据：[WMS_CREATION_AXES 原件](D:/CP6-archives/consolidation-20261010/root-planning-cache/plan-po-cp22/members/PLAN_PO_R3_CP18_WMS_CREATION_AXES_NORMAL_DATA.json:1)

## 11. 原创建槽查询与新公共恢复入口

ACK 丢失时，对原 actor、commandKey、draftKey、requestDigest 和 nativeFrameDigest 查询原创建槽。IN_PROGRESS、NOT_OBSERVED、UNAVAILABLE 三种回复都不允许推断 no-effect、释放范围、换新键或重发新的 T04 创建。每个样例在本地用 13 项同事务写入登记 UNKNOWN、原 lookup/observation、保护与工作状态、不可变 revision/head、checkpoint、CommandSlot、audit，winner/receipt/release 均零新增。

FOUND_CREATED 必须取到独立保留的原始 T04 201 全部 canonical bytes，而不是拿最新 T05 当前对象替代。新公共 WmsSlotRecoveryCommandCp18 具有自己的本地请求键和终态；它消费同一原 native 创建证明，保留原 native frame。原 delegation 不能只给 receiptRef，还须有完整 body 与独立 producer 资格。缺少真实 WMS 原槽查询 adapter/admission 时返回 503 WMS_ORIGINAL_SLOT_LOOKUP_REQUIRED_FENCED，零 native 调用、零业务写入，原 protection 保持。

依据：[WMS_SLOT_LOOKUP 原件](D:/CP6-archives/consolidation-20261010/root-planning-cache/plan-po-cp22/members/PLAN_PO_R3_CP18_WMS_SLOT_LOOKUP_NORMAL_DATA.json:1)；[WMS_NEW_SLOT_ROOT 原件](D:/CP6-archives/consolidation-20261010/root-planning-cache/plan-po-cp22/members/PLAN_PO_R3_CP18_WMS_NEW_SLOT_ROOT_NORMAL_DATA.json:1)

## 12. 样例采用前的静态对齐项

原件的完整性和可理解性不表示样例已经满足自己的所有约束。静态读取已发现：SC06_100 同一回执链的摘要不一致（PLAN-IF-014）；WO preparation 原请求 executionKey 与回包 executionKey 不同（PLAN-IF-015）；EPOCH dispatch 的前后 authority head 变化未包含在其两项写入中；保留 SCENARIO 图将不可变订单 revision 4→5 标为 UPDATE；三 Owner WMS 接受后外层 revision 7/WITHDRAWN/SUCCEEDED，但 BodyJson 仍写 4/ACTIVE/PENDING。

这些是源文件内部或源文件与当前合同的静态差异，未运行数据库、Owner 或归档代码。实施应遵循当前完整权限、不可变 revision 和真实原结果合同，先纠正具体例证，再执行实际契约/恢复测试。定位和值差异见 [静态来源对齐记录](D:/CP6/docs/CP6_开发设计文档_20261010/evidence/PLAN-PO-01-normal-alignment-observations.json:1)。
