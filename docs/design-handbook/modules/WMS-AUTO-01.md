# WMS-AUTO-01 仓储设备任务与遥测开发设计

状态：核心语义已整理；必要附件补读中，真实接入和所有 AC 尚未验证。[模块导航](D:/CP6/docs/CP6_开发设计文档_20261010/模块目录.md)。

## 1. 目的和责任边界

本模块支持 WCS 任务派发/结果、传感读数、告警以及模拟/真实隔离。WMS 保存受控 TaskIntent、派发尝试、设备观察及源作业结果交接；设备/OEM/现场 Owner 管理真实能力、执行事实和安全联锁；Stock 管数量，IN/OUT/TRANSFER/StockTake 各管自己的应用。COUNT 完成只证明盘点观察，PICK/PUT/MOVE 的 Task 状态也不能直接写库存。遥测/告警不等 Quality 合格、库存位置或设备安全证明。[Owner 决定](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/65/65c512abcd614f27__WMS-AUTO-01_仓储设备任务与遥测_完整详设_R2_CANDIDATE.md:16)

REAL 任务只能来自有权源作业准备好的稳定指令、原范围和正式位置；孤立手工演示只属 SIMULATION。缺站点/OEM 采用证据则 REAL 派发 Disabled，历史读取和人工观察仍可用。本文是应用层设计，没有执行设备连接、派送或模拟。

## 2. 有效版本和接受边界

当前 R2 正文 SHA 前缀 `65c512abcd614f27`，147 行；四 SPEC、18 AC、8 任务不变。完整 SHA 和强制组合见 [规范输入索引](D:/CP6/docs/CP6_开发设计文档_20261010/indexes/required-inputs.json)。R2 补齐来源腿、设备事件及实际执行身份，保持原 UNKNOWN、四轴和模式隔离要求。[版本声明](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/65/65c512abcd614f27__WMS-AUTO-01_仓储设备任务与遥测_完整详设_R2_CANDIDATE.md:3)

X4 后续接受覆盖正文静态 NOT_ACCEPTED，但所有新增类型/路由仍候选、actualAdopted=false，原 AC 全部 NOT_RUN。设计接受不能授权生产接入、现场操作或把旧模拟测试当真实验收。[X4 接受原件](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/59/59dff54660e0a2fe__X4-SIX-TARGET-ACCEPTANCE.md:1)

## 3. 数据、身份和结果轴

| 身份 | 精确组成与作用 |
|---|---|
| 来源指令槽 | `(trustedScope,sourceOwner,stableSourceOperationId,sourceLineId,instructionLegId)`；同 line 的不同真实腿可各有 Task，同腿换版本不能多派发 |
| commandKey | 固定原 manifest bytes/digest；attempt/delivery 只是运输 |
| producerEventIdentity | `(producerAuthorityId,deviceNativeIdentity,deviceEventNamespaceId,sourceEventId)`；scope 内去重，不用本域 eventId 或 sequence 代替 |
| deviceOccurrenceIdentity | `(physicalOwner,deviceNativeIdentity,executionNamespaceId,physicalExecutionId,executionLeafId)`；多个 STARTED/COMPLETED 事件可指同一次执行 |
| sourceApplicationKey | `(trustedScope,sourceOwner,stableSourceOperationId,sourceLineId,instructionLegId,deviceOccurrenceIdentity)`；源 Owner 按完整 tuple 查询和应用 |

事件 namespace/session 与执行 namespace 必须有原设备协议证据，不能用随机 bindingEpoch 或每条消息新 session 洗掉重复。单一整命令 leaf=WHOLE 也须原 schema 明确；部分执行必须有真实稳定 leaf/range，缺失只能 UNKNOWN。[R2 身份与封闭事件 body](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/65/65c512abcd614f27__WMS-AUTO-01_仓储设备任务与遥测_完整详设_R2_CANDIDATE.md:104)

Task 分开显示运输、物理执行、本域 journal、源业务应用四轴。DispatchState 为 PREPARED/QUEUED/SENT_UNKNOWN/ACCEPTED/REJECTED_FINAL；physicalOutcome 为 NO_EFFECT_PROVEN/PARTIAL_EFFECT/EFFECT_COMPLETED/UNKNOWN，FAILED 不等零效果；ApplicationState 为 NOT_APPLIED/PENDING/APPLIED/REJECTED/UNKNOWN。必须有设备必要完工证据及原 Owner 应用回执才称闭环。

Reading 不可变，保 sourceEvent/session/sequence、measuredAt/receivedAt、原值/单位、绑定版本、校准/时钟质量和 mode。唯一 `(scope,adapter,sensor,sourceSessionId,sourceEventId)`。数量 decimal(21,8)、传感数 decimal(18,4) 字符串，DIGITAL 只 0/1；计数为正 Int64 字符串。ThresholdPolicy 保版本、严格边界、freshness、clearConsecutiveCount/滞回和批准；Alarm 分 signal 与 workflow。[DTO 与状态](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/65/65c512abcd614f27__WMS-AUTO-01_仓储设备任务与遥测_完整详设_R2_CANDIDATE.md:40)

## 4. 完整业务流程

创建只保存合法来源意图，零设备/Stock 效果。prepare 读当前源、范围、位置、设备能力/许可，冻结 manifest。dispatch 在本地 Main 事务内通过与源取消互斥的最终准入门，保存 command/Outbox/Audit/receipt；Outbox 成功不表示设备已启动。

Sender 通过登记 adapter 投原 commandKey/manifest；timeout 为 SENT_UNKNOWN。只有 QueryOriginal 或已采用同键去重协议允许继续原意图。设备可能部分失败，不能按失败猜库存回滚。事件摄取验证两种设备身份、Scope/mode/epoch/command/digest，去重后保存观察与 SourceHandoff；乱序待前驱，矛盾终态隔离。源 Owner 再独立认定实际并申请 Stock 效果，AUTO 只保存原应用回执。源已取消但设备实际执行仍保事实并进入异常应用。[任务处理](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/65/65c512abcd614f27__WMS-AUTO-01_仓储设备任务与遥测_完整详设_R2_CANDIDATE.md:54)

取消前若权威证明从未有 Outbox/发送可能，可在同槽永久 fence 为 SEALED_NO_EFFECT。已发则仅 CANCEL_REQUESTED，必须取得原设备取消证据和源 Owner 终态判断；ACK 不等停止。未决任务不能删除重建，真实停止由既有现场流程管理。

读数先验证身份、绑定、单位、时间/校准，原事件重复回原回执、异义隔离；同事务追加 Reading 和摄取证据后推进投影。迟到旧点进入历史但不盖新 latest；同 measuredAt 仅按可证 session/sequence 排序。时钟异常 SUSPECT、无单位换算/质量资格不能判正常；历史分页固定 cut。[读数](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/65/65c512abcd614f27__WMS-AUTO-01_仓储设备任务与遥测_完整详设_R2_CANDIDATE.md:66)

告警按读数业务时刻选择政策，VALID 且严格超界才触发/续写 episode。等边界不警，清除必须满足原政策连续正常/滞回；无明确规则不能默认。ACK 只改 workflow，CLEAR 要有效后继观测，CLOSE 要 CLEAR 或有权误报证据。断连/STALE/MISSING/UNTRUSTED 为当前 UNKNOWN，不清旧 ACTIVE。Quality/仓位限制另由其 Owner 应用。[告警](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/65/65c512abcd614f27__WMS-AUTO-01_仓储设备任务与遥测_完整详设_R2_CANDIDATE.md:72)

## 5. API 与跨域接口

候选前缀 `/api/wms/specials/v1/automation`：tasks/options/events/operations；task create/prepare/dispatch/cancel-requests/observations；sensors/readings、资料/阈值版本；manual-readings、simulation/readings-plan、ingest/readings-batches；alarms/ack/close。对象写需 If-Match。真实 device-events 仅登记服务入口，不能供浏览器伪造完工。批摄取最多 1000，逐项明确回执。[API](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/65/65c512abcd614f27__WMS-AUTO-01_仓储设备任务与遥测_完整详设_R2_CANDIDATE.md:84)

DeviceTaskAdapter.Required 定义 Prepare、Submit、QueryOriginal、RequestCancel 的应用合同；返回 capability/query/idempotency/currentPermit、运输接受观察、原执行证据和 physicalOutcome。QueryOriginal 缺失不是无效果证明，不指定现场低层协议。

SourceTaskAdapter.Required 提供 ReadInstruction、AdmitDispatchInUow、ReceiveExecutionFact、QueryApplication；接收事实成功只等 receipt，不等 Stock 已应用。R2 source-instructions selector 必须返回真实 stable operation/line/leg、native schema/envelope/digest、范围/位置/量及准入头，浏览器不得自行拼 leg。SourceHandoff 和 QueryApplication 使用完整 sourceApplicationKey。[两类适配](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/65/65c512abcd614f27__WMS-AUTO-01_仓储设备任务与遥测_完整详设_R2_CANDIDATE.md:90)、[selector 与 R2 传播](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/65/65c512abcd614f27__WMS-AUTO-01_仓储设备任务与遥测_完整详设_R2_CANDIDATE.md:108)

## 6. 事务、重试和恢复

两处本地原子边界：Operation/Audit/Task/Outbox；Inbox/Observation/投影/SourceHandoff。设备执行和源数量事务独立，不能把整条 HTTP 链说成共同事务。Outbox 至少一次保持原 bytes/hash；设备一次性能力依真实 adapter 证明，不能泛称 exactly-once。[原子边界](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/65/65c512abcd614f27__WMS-AUTO-01_仓储设备任务与遥测_完整详设_R2_CANDIDATE.md:96)

未知沿原 command/操作/来源槽查回，不能换键、换设备、新建任务重发。COMPLETED 后迟到 STARTED 不回退；同 event identity 异内容隔离，不以大 sequence 覆盖。多条完工事件共享同实际发生时只有一次源应用；STARTED 不能形成数量事实。未决/部分执行不因 lease 到期清除义务或墓碑。当前禁用不改昨日成功，历史回放按读权；新准入另核当前绑定。[事件处理与恢复](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/65/65c512abcd614f27__WMS-AUTO-01_仓储设备任务与遥测_完整详设_R2_CANDIDATE.md:60)

错误族：400 shape/unknown field；422 source binding、单位政策、阈值、设备能力；409 version/command/event 内容或 mode 冲突；503 adapter/outcome unknown；跨范围安全 404。5xx 不代表回滚。

## 7. 模式、权限和审计

SIMULATION、MANUAL_OBSERVATION、REAL 由可信环境/登记 adapter 绑定，Scope 含 environment/tenant/site/mode。模式贯穿 FK/UQ、存储、队列、缓存、查询、审计和所有派生引用；REAL 最终门拒模拟来源。人工报告只生 manualObservationId，不能造 REAL 发生或 sourceApplicationKey。旧来源统一 LEGACY_ORIGIN_UNPROVEN，不能从 Creator='sim' 或类似 AGV 的设备名反推真实。[模式隔离](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/65/65c512abcd614f27__WMS-AUTO-01_仓储设备任务与遥测_完整详设_R2_CANDIDATE.md:78)

人类操作、sensor 服务和运维重投各自授权；Admin 不是现场许可。旧权限迁移与新 read/history/recover/ack/threshold-review/source-binding 须 IAM 注册。真实凭证留安全配置，不进入文档/UI/日志；错误只显示安全码/supportRef。审计保 actor、绑定/政策、原摘要域、操作、mode 和前后版本。通知通道/收件人/升级依批准政策，本次未发通知。[权限审计](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/65/65c512abcd614f27__WMS-AUTO-01_仓储设备任务与遥测_完整详设_R2_CANDIDATE.md:102)

## 8. 页面、交互与可用性

P01 任务列表永久 mode 标识、四轴、准确总数，默认 50/最大 100；P02 六页签，源范围/数量/位置只读，REAL 无人工 Start/Complete；P03 原键恢复，取消不冒已停止；P04 sensor 当前值/质量/双时间/新鲜度；P05 同 cut 历史，默认 200/最大 1000，图不插值冒实测、不同单位/模式不混；P06 告警 signal/workflow 分开；P07 隔离模拟工作区。

模拟计划每 sensor 1..100 条、最多 100 个、合计 ≤10000。轮询失败保旧数据并标 STALE，页面卸载停轮询、迟返不串对象。空数据不能显示全部正常，缺值不能显示 0。资料位置/单位/阈值改版保历史语义。[七页规则](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/65/65c512abcd614f27__WMS-AUTO-01_仓储设备任务与遥测_完整详设_R2_CANDIDATE.md:24)

## 9. 实施顺序和旧代码差异

按 A01–08：稳定键/Task/Dispatch/Observation/Alarm/Reading → 各源作业适配及最终准入 → OEM 应用协议和原键恢复 → 读数摄取/迟到/时间/单位 → 版本化告警 → 页面 → 模式与旧 writer 隔离 → 契约/现场验收计划。[任务表](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/65/65c512abcd614f27__WMS-AUTO-01_仓储设备任务与遥测_完整详设_R2_CANDIDATE.md:128)

原稿固定 CP6 `90c871fe…` 映射：旧 WCS 是手工 dispatch/complete 仿真，IoT 随机模拟与输入同表且无 origin/mode；迟到 readAt 可覆 LastValue，另一个最新查询口径不同，旧测试不证明现场效果。该事实来自原稿有界源码阅读，本轮未新审代码。旧人工终态不生成设备回执，未证旧流不入 REAL 指标；采用新链后旧 start/complete 只读，所有后台/模拟/旧 API writer 必须明确归属。[旧代码](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/65/65c512abcd614f27__WMS-AUTO-01_仓储设备任务与遥测_完整详设_R2_CANDIDATE.md:10)

## 10. 验收与执行状态

18 AC 均 NOT_RUN。覆盖创建零效果、MOVE 完工仍需原 StockReceipt、COUNT 不调账、源取消与派发竞争、断线原 command 恢复、不换设备、事件重复/异义/乱序、FAILED 部分效果、取消 ACK 不冒停止；迟到数据、时钟、事件/session 去重、单位与校准、严格阈值、ACK 不清 signal、断连 UNKNOWN；分页/轮询/刷新、模式全链隔离、旧来源不升 REAL、权限分离和逐项批回执。

R2 还验证同 line 两真实腿分别合法、同腿版本不多发，同 sourceEvent 换 delivery 只一观察，同物理发生 STARTED/COMPLETED 只一最终应用，缺 namespace/leaf 保 UNKNOWN。示例阈值 [2,8] 的 8 不警、8.1 警只证明算法用例，不是现实环境标准。[全部 AC](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/65/65c512abcd614f27__WMS-AUTO-01_仓储设备任务与遥测_完整详设_R2_CANDIDATE.md:139)

## 11. 编码前采用门与待核

A-G1 各源 Owner 的逐任务型 adapter/最终门/应用回执；A-G2 OEM 版本协议、原键能力与现场许可；A-G3 sensor 绑定、时钟、单位、校准/阈值政策；A-G4 MASTER/Stock 正式位置与数量应用；A-G5 IAM/S6 Scope/mode/恢复及存量 writer。每项需 provider repo/commit/native method/schema、映射、适用范围、Owner 记录和有效性，缺项只阻相关新 REAL 动作。目标 UA/CURRENT、全部共享评审及采用保持附件已补核；跨Owner双方合同仍待集中核对。另保R1非阻断澄清：迟到超阈值且已有较新正常值如何进入历史episode/当前signal，以及freshness双时间关系需明确政策和验收。[采用门](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/65/65c512abcd614f27__WMS-AUTO-01_仓储设备任务与遥测_完整详设_R2_CANDIDATE.md:124)

## 12. 来源和阅读覆盖

正文 1–147 行本轮全文实读，含精确事件 body、状态机、四 SPEC、18 AC 和任务；X4 接受、最终 R2.1 独审及来源限定已读。逐件 SHA/行段/未读附件见 [root 阅读账](D:/CP6/docs/CP6_开发设计文档_20261010/evidence/root-specialty-reading.json)。提取或历史测试叙述不计本轮现场验证。

补读完成记录及准确同文复用方法见[X4六专项阅读与采用边界](D:/CP6/docs/CP6_开发设计文档_20261010/evidence/X4-六专项阅读与采用边界.md)。这不替代外域依赖的双方集中核对或实际业务验收。

## 必要材料整理结论

当前17项必需引用均已完整阅读或使用有证据的精确复用：主文、当前接受/审查、原范围、AC与任务文字以及来源资格已纳入设计。历史成员目录只证明来源身份，不等于旧代码全文审计；外域实际采用、Provider和业务运行继续按本册门禁确认。见[逐目标必需引用核对](../evidence/root-specialty-required-closure.json)。后续整任务进行一次集中跨模块复核，不将静态设计整理等同实际验收。
