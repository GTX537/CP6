# PLAN-EXC-01：本地事务、回执与恢复

本附册把原包的 65 个业务场景拆成可实现的行为。227 个来源接纳包、297 个完整快照和 287 个名册增量构成对应证据。所有场景仍是静态设计；结构核对不能代替数据库、业务测试或真实 Owner 接入验证。

## 1. 四种状态不能混成一个状态

|状态对象|表达什么|不能推导什么|
|原始 CommandReceipt / IngressReceipt|首次请求的原始返回体及原始 HTTP 语义|当前 Operation 已完成、后来结果已经应用|
|Operation / RecalcWork / Inbox|当前处理进度、重试与等待|改写原始回执、重新生成请求键|
|Owner 原生回执|Owner 确切观察或作用结果|EXC 已消费结果或 Case 已关闭|
|CaseHead / CaseRevision|EXC 实际本地业务版本|上游原生事实由 EXC 拥有或可重写|

首次 202 注册回执永久保留。相同槽位和相同正文的回放返回 200 并标明 replay，正文仍是原始结果；不能拿当前 OperationView 替代它。相同槽位不同正文返回 409 `IDEMPOTENCY_BODY_CONFLICT`。读权限撤销返回 403，保留原件但不披露资源；提交结果未知返回 503 `QUERY_ORIGINAL`，不换 key 重做。[注册与五类替代结果](<D:/CP6-archives/consolidation-20261010/root-planning-cache/plan-exc-cp12/CP03_LOCAL_UOW_LEDGER_INDEX.json:102577>)

## 2. 本地 UoW 的实际写入边界

同一个成功 UoW 中保留请求 artifact、操作与唯一槽位、结果 artifact、原始回执、必要业务行和 Outbox。Operation 在回执尚不存在时先保存空引用，待 Slot/Artifact/Receipt 存在后在原事务内绑定，以符合即时外键顺序。只有事务提交后才允许分发。

不可变记录同 PK 不同 body 必须冲突，不能覆盖。可变记录按完整 expected 行及真实 RowVersion 做 CAS；受影响行为 0 时整 UoW 回滚。StoreCommitRecord 及前驱先存在，最终 StoreCommitHead CAS 防止用压缩名册的父节点冒充真实提交前驱。

`BodyCanonical` 是读回投影字段；RowVersion 是数据库生成字段。完整后继行快照不是 SQL 整行赋值输入，不能显式写 RowVersion，也不能为了保存 canonical 再做第二次更新。详见 [数据与类型](D:/CP6/docs/CP6_开发设计文档_20261010/contracts/PLAN-EXC-01-数据与类型.md)。

结构核对覆盖全部 292 包、29,903 条写序、16,262 个不同不可变引用及 417 个可变行副本：按原包声明的写序，before 加实际插入/CAS 后得到声明的 after，未发现集合或引用差异。这只证明文档关系一致。[初始化例子](<D:/CP6-archives/consolidation-20261010/root-planning-cache/plan-exc-cp12/CP03_LOCAL_UOW_LEDGER_INDEX.json:8>)

## 3. 新事实重算与 Owner 进度

NEW_FACTS 注册必须匹配完整 Case、Episode、businessRevision、RowVersion、assessment/impact/current 的 ID+Digest 和 writerEpoch，并保留精确 closure、changeFactRefs、algorithm、旧 effective/protection set。此时未来 Run/InputSet 仍为空。

|观察的 Owner 阶段|本地进度|下一步|
|ACCEPTED|WAITING_OWNER|查询原槽|
|PREPARING|PREPARING|查询原槽|
|INPUT_SEALED|INPUT_SEALED|查询原槽|
|RUN_REQUESTED|RUN_REQUESTED|查询原槽|
|ATTEMPT_STARTED|CALCULATING|查询原槽|
|RESULT_SEALED|OUTPUT_SEALED|查询原槽|
|SELECTED|SELECTED|查询原槽|
|PUBLISHED|PUBLISHED|另一个本地消费 UoW|

这些观察使用原 Work/Slot/34,755 字节 Owner 请求，不能通过查询创建新 Run。Preparation 必须已真实签发并已被本地接纳；示例中 18:10:10.500 先于 ACCEPTED 18:10:11。八次观察不改首次 202。[原请求及前置条件](<D:/CP6-archives/consolidation-20261010/root-planning-cache/plan-exc-cp12/CP03_LOCAL_UOW_LEDGER_INDEX.json:126101>)；[发布观察](<D:/CP6-archives/consolidation-20261010/root-planning-cache/plan-exc-cp12/CP03_LOCAL_UOW_LEDGER_INDEX.json:160938>)

PUBLISHED 与本地 ResultConsumption 分离。本地消费更新 Case，但本身不写 Resolution/Closure；关闭还需独立满足处置条件。[结果消费](<D:/CP6-archives/consolidation-20261010/root-planning-cache/plan-exc-cp12/CP03_LOCAL_UOW_LEDGER_INDEX.json:194641>)

## 4. 共享责任、关闭与提示状态

共享 PO100 的例子只移交已退休 A[30,60) 的责任，保留 A[0,30)、B[60,100) 及实际 PO100。ACCEPTED 不等于 SETTLED，捕获这两个回执均不立刻改 CaseHead。完整 settlement/current 被评估为后继后，才能按 `RESPONSIBILITY_RETIRED` 关闭。[共享移交](<D:/CP6-archives/consolidation-20261010/root-planning-cache/plan-exc-cp12/CP03_LOCAL_UOW_LEDGER_INDEX.json:121888>)；[结清回执](<D:/CP6-archives/consolidation-20261010/root-planning-cache/plan-exc-cp12/CP03_LOCAL_UOW_LEDGER_INDEX.json:167045>)；[退休责任关闭](<D:/CP6-archives/consolidation-20261010/root-planning-cache/plan-exc-cp12/CP03_LOCAL_UOW_LEDGER_INDEX.json:218888>)

另一条正常分支以 `GAP_ELIMINATED` 关闭。Resolution、CaseClosure、不可变 HeadObservation 后继、CaseRevision 与 Outbox 同 UoW 提交；writerEpoch 或源 current 变化时全部回滚。[缺口消除关闭](<D:/CP6-archives/consolidation-20261010/root-planning-cache/plan-exc-cp12/CP03_LOCAL_UOW_LEDGER_INDEX.json:289154>)

Dismiss/Restore 只改变提示可见性，短缺和业务生命周期仍保持 OPEN。过期提示可直接由 serverNow 判断恢复可见，不要求 worker 写库；历史 dismissal ID 仍保留。[隐藏提示](<D:/CP6-archives/consolidation-20261010/root-planning-cache/plan-exc-cp12/CP03_LOCAL_UOW_LEDGER_INDEX.json:225529>)；[无worker过期读取](<D:/CP6-archives/consolidation-20261010/root-planning-cache/plan-exc-cp12/CP03_LOCAL_UOW_LEDGER_INDEX.json:233072>)

## 5. 可选投影失败与必需本地等待

可选投影分支允许业务 Case3 和唯一 ResultConsumption 先成功，投影仍停留在 prepared revision2。后续 Repair 以真实已消费 publication 和准备好的 RepairPlan 修复投影；不再发布 MRP、不再消费结果，也不改 CaseHead。[业务成功与投影分离](<D:/CP6-archives/consolidation-20261010/root-planning-cache/plan-exc-cp12/CP03_LOCAL_UOW_LEDGER_INDEX.json:233123>)；[投影修复](<D:/CP6-archives/consolidation-20261010/root-planning-cache/plan-exc-cp12/CP03_LOCAL_UOW_LEDGER_INDEX.json:769922>)

必须执行的本地任务尚未完成时，Impact/Repair 注册返回 202，当前 Operation=`WAITING_LOCAL_APPLY`，原回执仍为 REGISTERED。此时不产生新 Head/Impact/Projection/Publication/ResultConsumption；最终执行要重新检查 current 和权限。不能把“来源已完整”误当成“本地工作已应用”。[Impact等待](<D:/CP6-archives/consolidation-20261010/root-planning-cache/plan-exc-cp12/CP03_LOCAL_UOW_LEDGER_INDEX.json:769946>)；[Repair等待](<D:/CP6-archives/consolidation-20261010/root-planning-cache/plan-exc-cp12/CP03_LOCAL_UOW_LEDGER_INDEX.json:769949>)

## 6. M04 乱序、重试与别名

1. sequence2 先到：保存独立传输 wrapper 和原生 observation，首次 202=`WAITING_PREDECESSOR`，明确缺少 sequence1 及其 semantic digest，CaseHead 不变。
2. 迟到 sequence1 的原 expected Case2 已落后于当前 Case3：记 HISTORY_ONLY 及完整 AppliedReceipt，推进对应流处理依据，但不写新的业务 CaseRevision。
3. 应用 sequence2 失败：整个本地域 UoW 回滚，没有部分 HeadObservation 或 AppliedReceipt；首次 202、CaseHead 与来源流保留。
4. 重试真正成功：核对当前 Inbox claim、stream、epoch anchor、前序 semantic digest 和完整 CaseHead；原 expected revision3 产生 revision4，准确保留本地 commit 和原生引用。
5. 新 eventId 但同原生语义属于传输别名：保存别名关系，不能重复作用。相同 event/sequence 不同 semantic body 为 409 `SOURCE_VERSION_CONFLICT`。

[先到消息](<D:/CP6-archives/consolidation-20261010/root-planning-cache/plan-exc-cp12/CP03_LOCAL_UOW_LEDGER_INDEX.json:268983>)；[迟到历史](<D:/CP6-archives/consolidation-20261010/root-planning-cache/plan-exc-cp12/CP03_LOCAL_UOW_LEDGER_INDEX.json:272381>)；[失败回滚](<D:/CP6-archives/consolidation-20261010/root-planning-cache/plan-exc-cp12/CP03_LOCAL_UOW_LEDGER_INDEX.json:276049>)；[原消息重试](<D:/CP6-archives/consolidation-20261010/root-planning-cache/plan-exc-cp12/CP03_LOCAL_UOW_LEDGER_INDEX.json:277634>)；[别名与冲突](<D:/CP6-archives/consolidation-20261010/root-planning-cache/plan-exc-cp12/CP03_LOCAL_UOW_LEDGER_INDEX.json:282949>)

M02/M03 也保留真实流锚点和原序号。进程重启不能归零；查询已经捕获过的 ACCEPTED/SETTLED 或 ACCEPTED/PUBLISHED 回执再从消息入口抵达，只补历史接收处理，不再次消费结果或关闭 Case。[处置消息](<D:/CP6-archives/consolidation-20261010/root-planning-cache/plan-exc-cp12/CP03_LOCAL_UOW_LEDGER_INDEX.json:728182>)；[重算消息](<D:/CP6-archives/consolidation-20261010/root-planning-cache/plan-exc-cp12/CP03_LOCAL_UOW_LEDGER_INDEX.json:743973>)

## 7. Writer epoch 与不同分支的同版本号

原 lease 到 18:10:33 明确过期后，通过完整 WriterHead epoch8 元组 CAS 分配 epoch9。旧不可变 CaseRevision/receipt 保留原 epoch8；旧 writer 即使手中 Case RV 未变，也应被当前 writer 门拒绝为 412 `WRITER_EPOCH_CHANGED`。[epoch接续](<D:/CP6-archives/consolidation-20261010/root-planning-cache/plan-exc-cp12/CP03_LOCAL_UOW_LEDGER_INDEX.json:284610>)；[旧writer拒绝](<D:/CP6-archives/consolidation-20261010/root-planning-cache/plan-exc-cp12/CP03_LOCAL_UOW_LEDGER_INDEX.json:293455>)

`EPOCH9_NONTERMINAL_HEAD_TUPLE` 是另一个原始场景：Case4 观察 current 后得到仍 OPEN 的 Case5。PRIMARY 关闭分支则得到 CLOSED 的 Case5。这两个结果具有不同完整快照/正文，不能只按数字 5 合并，更不能当成同一个数据库时间点。[非终态后继分支](<D:/CP6-archives/consolidation-20261010/root-planning-cache/plan-exc-cp12/CP03_LOCAL_UOW_LEDGER_INDEX.json:758172>)

## 8. 冷恢复与 same-input 恢复

可能已发送原请求而未捕获回执，代表结果未知。冷恢复先取得新 writer 身份和独立 recover 权限，只查询原 Owner Slot；`NOT_OBSERVED` 的 `canInferNoEffect=false`，不能再发业务请求。

查到 FOUND 时保留原 receipt 的 published-at/current 与原正文，同时记录当前 recovery 的权限和 current；二者不同。FOUND 捕获本身既不本地消费、不发布，也不 Resolve；后续 LOCAL_APPLY 是使用新业务授权的另一个 UoW。[未知发送](<D:/CP6-archives/consolidation-20261010/root-planning-cache/plan-exc-cp12/CP03_LOCAL_UOW_LEDGER_INDEX.json:293472>)；[未观察到](<D:/CP6-archives/consolidation-20261010/root-planning-cache/plan-exc-cp12/CP03_LOCAL_UOW_LEDGER_INDEX.json:297460>)；[找到原回执](<D:/CP6-archives/consolidation-20261010/root-planning-cache/plan-exc-cp12/CP03_LOCAL_UOW_LEDGER_INDEX.json:323180>)

Same-input 恢复复用原失败 Run、InputSet、Algorithm、EffectiveSet、RecoverySlot 与完整 all-writer isolation。只恢复 generation2 Attempt，不能建立新 Run/InputSet、改变 source membership 或执行 Owner conversion。迟到 generation1 只保留诊断，不能覆盖、选中或发布它。[原输入恢复](<D:/CP6-archives/consolidation-20261010/root-planning-cache/plan-exc-cp12/CP03_LOCAL_UOW_LEDGER_INDEX.json:518477>)；[迟到旧generation](<D:/CP6-archives/consolidation-20261010/root-planning-cache/plan-exc-cp12/CP03_LOCAL_UOW_LEDGER_INDEX.json:534911>)

## 9. 多 Case 导出

导出先在同一个真实 readCut=113 上准备各 Case 的完整 head 和独立权限，排序后的成员集合确定 bundle。注册保存固定成员、列、格式和原 202；READY 是后来的当前状态，不能改写原结果。

可能已生成文件时只能查询原文件捕获，不能换 key 再生成。原 CSV 为 296 字节、两条固定成员数据；相同集合改顺序可回放，相同槽改列或 Episode 则冲突。读撤权返回 403；到期返回 410 `EXPORT_EXPIRED`，两者都不重新生成文件。[固定cut准备](<D:/CP6-archives/consolidation-20261010/root-planning-cache/plan-exc-cp12/CP03_LOCAL_UOW_LEDGER_INDEX.json:553418>)；[原文件捕获](<D:/CP6-archives/consolidation-20261010/root-planning-cache/plan-exc-cp12/CP03_LOCAL_UOW_LEDGER_INDEX.json:558695>)；[回放与拒绝](<D:/CP6-archives/consolidation-20261010/root-planning-cache/plan-exc-cp12/CP03_LOCAL_UOW_LEDGER_INDEX.json:559023>)

## 10. 材料与阅读范围

原 WO/MAT NET40 已扣除原 offset20，不能再次扣20或把相同保护量当自由供给。新父需求100派生180、另有自由供给50，形成独立新责任130；兼容组可汇总旧40+新130=170，但仍为两条责任和两个 Case。[材料双责任](<D:/CP6-archives/consolidation-20261010/root-planning-cache/plan-exc-cp12/CP03_LOCAL_UOW_LEDGER_INDEX.json:710340>)

本附册已阅读全部 65 场景的非结构字段，并对 227 个来源接纳包的共同规则及每项原始/本地时间作对照；31 项晚于原始来源时间，其余相等，未见提前接纳。底层 local/physical 原始行字段、隔离差异样例与其他必要载体仍按 [UoW 阅读证据](D:/CP6/docs/CP6_开发设计文档_20261010/evidence/PLAN-EXC-01-UoW-reading.json) 继续核定。这里没有把结构检查改称完整逐字段审读或运行验收。
