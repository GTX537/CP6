# WMS-ANALYTICS-01 仓储报表与运行分析开发设计

状态：核心语义已整理，必要附件补读中；候选实现/实际采用/验收均未由本轮证明。[模块导航](D:/CP6/docs/CP6_开发设计文档_20261010/模块目录.md)。

## 1. 用途与权威边界

用户查看库存/月报、ABC/呆滞、库龄、桥接健康和每项指标来源，并导出固定快照。Analytics 只拥有 ReportRequest、版本化公式、快照、来源清单和导出；Stock 管库存/Claim，Finance 管估值，Integration 管桥接业务状态。报表不调账、不补偿、不派任务，ABC 仅为建议，货位/补货需各 Owner 独立采用。[范围与职责](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/2f/2f90c5537845e5cb__WMS-ANALYTICS-01_仓储报表与运行分析_完整详设_R2_CANDIDATE.md:16)

LOCATION 实存与 TRANSIT/EXTERNAL/CONSUMED/VOID 分列；客户/VMI 与 SELF 分列，UNKNOWN 货主不能归 SELF。数量只能在相同基本 UOM 汇总，金额只能在相同币种且有明确权威估值时汇总，缺价不等 0。

## 2. 当前设计和接受边界

当前 R2 正文 SHA 前缀 `2f90c5537845e5cb`，172 行；五 SPEC、18 AC、8 任务保持原编号。精确 SHA/必要组合见 [规范输入](D:/CP6/docs/CP6_开发设计文档_20261010/indexes/required-inputs.json)。R2 补齐 Stock RequestedUse、四张运营卡的来源和 frequency 排序，未改原范围。[版本声明](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/2f/2f90c5537845e5cb__WMS-ANALYTICS-01_仓储报表与运行分析_完整详设_R2_CANDIDATE.md:3)

X4 后续接受覆盖正文静态 NOT_ACCEPTED；实际 native adapter 采用仍 false、所有业务 AC NOT_RUN，原源码/测试阅读不是本次新执行。[X4 接受](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/59/59dff54660e0a2fe__X4-SIX-TARGET-ACCEPTANCE.md:1)

## 3. 模型、双时间和知识状态

ReportDefinition 不可变保存 metric/revision、输入 schema、维度、量义、公式、覆盖/排除规则；Request 保存完整 filter、asOfBusinessTime、knowledgeCut、definition、mode、权限及 useContext；Snapshot 冻结 sourceCuts、行 manifest/总数、分 UOM/币种合计、卡片、未知/排除数量和来源摘要。分页排序只能在同冻结集合上进行。[模型](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/2f/2f90c5537845e5cb__WMS-ANALYTICS-01_仓储报表与运行分析_完整详设_R2_CANDIDATE.md:40)

CURRENT 取新的完整 cut；ORIGINAL_KNOWN 只纳当时已知且业务生效不晚于 asOf 的事实；RESTATED 在较晚固定 knowledgeCut 纳历史纠正。跨 Owner 保存 native frontier 向量，不能凭相近时钟称全局原子快照；Stock 自身数量/Claim/限制必须同 cut。sourceCut 保原 nativeRef/schema/digestDomain，不改成自造全局序号。[时间口径](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/2f/2f90c5537845e5cb__WMS-ANALYTICS-01_仓储报表与运行分析_完整详设_R2_CANDIDATE.md:22)

Request 状态 QUEUED/BUILDING/READY_COMPLETE/READY_PARTIAL/FAILED/EXPIRED。单行数量 decimal(21,8)、合计 decimal(38,8)，money 最多八位十进制字符串；溢出 METRIC_OVERFLOW，不静默截断。未知使用显式 state/reasons，不转成 0。数量完整与金额未知可分别展示。

## 4. 五类算法与发布流程

**库存。** 向 Stock 取同 cut 的 Root/Slice/Movement/Claim/Restriction 全成员，checked 聚合后原子发布行 manifest/合计/READY。月界为 Site 时区 `[月首00:00,次月首00:00)`。每行 `Opening+In−Out+SignedAdjustment=Closing`，腿符号来自不可变事实与纠错关系，不能凭 TxnType 猜；范围内内部 MOVE 净 0，RSV/PICK 不增物理量，Free 不直接 P−Allocated。差异输出 UNEXPLAINED 与原事件，不自动调账。历史 Claim 缺失则历史 Free UNKNOWN，不能拼当前占用；每快照首批上限 100000 明细，超限 REPORT_LIMIT 而非假全量。[库存算法](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/2f/2f90c5537845e5cb__WMS-ANALYTICS-01_仓储报表与运行分析_完整详设_R2_CANDIDATE.md:54)

**ABC。** 使用明确命名 `END_CUMULATIVE_LE_V1`：末项累计≤80%为 A、≤95%为 B、其他 C；单产品占 100% 得 C，不能暗换算法。QUANTITY 按同 UOM/Owner 分区的有效正向出库量降序，再 product 稳定 Owner+id ordinal；FREQUENCY 可跨 UOM 比稳定实际出库发生次数，只以该次数降序、再稳定产品键，不用量/币种作同频次第二键。原发生拆多腿只计一次；内部 MOVE/RSV/模拟/全冲正排除，部分纠正按准确份额，负净量/关系缺失为 UNRESOLVED，不取绝对值。零 score 单列 NO_ACTIVITY，零分母 NO_DATA；不完整或遮罩分母不给正式 rank。业务真实退货另列，默认不抵已真实出库次数。[ABC](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/2f/2f90c5537845e5cb__WMS-ANALYTICS-01_仓储报表与运行分析_完整详设_R2_CANDIDATE.md:64)、[唯一排序器](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/2f/2f90c5537845e5cb__WMS-ANALYTICS-01_仓储报表与运行分析_完整详设_R2_CANDIDATE.md:143)

**呆滞。** 逐剩余 Root range 追踪最后适用活动。PHYSICAL_HANDLING 含真实位置 MOVE/受托进入离开；DEMAND_CONSUMPTION 只含明确销售/制造消费，内部移仓不刷新。baseline 取原始入管与该范围最后适用活动的较晚者，不能用实体 CreateDate 补证。同产品别库活动不消除此 Root 呆滞。按 Site 日差≥阈值入候选。[呆滞](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/2f/2f90c5537845e5cb__WMS-ANALYTICS-01_仓储报表与运行分析_完整详设_R2_CANDIDATE.md:70)

**库龄。** 先恢复 asOf+cut 当时剩余原范围，再按原入管日期分 0–30/31–60/61–90/>90。移动和拆段不刷新 ORIGINAL_RECEIPT，合并容器仍分原 Root；缺来源进 UNKNOWN 桶且保总量，未来来源异常进 FUTURE_ANOMALY、不夹为 0。LOCATION_DWELL 是另一口径，须有准确进入当前位置事件。不能对今天余额换个日期就称历史库龄。[库龄](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/2f/2f90c5537845e5cb__WMS-ANALYTICS-01_仓储报表与运行分析_完整详设_R2_CANDIDATE.md:72)

**桥接健康/来源。** failedRetryQueue 只数原 FAILED，Pending/InFlight/Dead/ApplicationUnknown 分开；创建窗口在 cut 的当前状态不冒当时历史。transport successRate 以窗口完整有权事件作分母，零分母 NO_DATA；业务成功率独立依原 Owner 完整结果及业务去重集合，SKIPPED/人工 COMPENSATED 不算业务成功。最近样本与全量总数分开；只跳转已采用 INT-OPS navigationRef，不调用旧 compensate。每卡/导出都附定义、单位/分母、双时间、原 schema/refs/向量、权限/未知/排除和 lineage。[桥接与来源](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/2f/2f90c5537845e5cb__WMS-ANALYTICS-01_仓储报表与运行分析_完整详设_R2_CANDIDATE.md:78)

## 5. RequestedUse、运营卡与只读接口

Stock RequestedUse 必须完整保 `action,purposeRef,demandRef,technicalRequirementRef`，后两字段显式可 null，不能省略或以报表描述/原用途替代。use-contexts producer 返回准确 context/revision、四字段、允许 evaluation 和范围。未选 context→NOT_EVALUATED；已选但证据缺→UNKNOWN；只有 KNOWN 才能填 Free 数值。[R2 RequestedUse](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/2f/2f90c5537845e5cb__WMS-ANALYTICS-01_仓储报表与运行分析_完整详设_R2_CANDIDATE.md:114)

冻结 CURRENT 用 Stock SQ07 AS_KNOWN/CAPTURE_NOW/effectiveAt=null，保存返回 vector/evaluatedAt；即时 CURRENT/PLANNED 预览用 SQ02 balances/search 的完整 use/evaluation，冻结须固定全部原响应成员/向量，否则 PARTIAL。历史用 SQ07 AS_KNOWN 或 EFFECTIVE_RESTATED、准确 effectiveAt 和 EXACT_VECTOR；knowledgeCut UTC 不能编成原向量。RequestedUse/evaluation 全链进入 Request/Snapshot/digest/cursor/cache/页面/导出，换 action 清旧 Free。

四张 `OPS-CARDS-1` 卡首批 **CURRENT_ONLY**：

| metric | 原 header 与计数谓词 |
|---|---|
| INBOUND_PLANS_TODAY | InboundOrders 非删除，Status 1/2，ExpectedArrivalDate=各 Site 当地当天，按 header Id |
| SHIPPING_PLANS_TODAY | OutboundOrders 非删除，Type=2，Status 1/2/3，PlannedDate=各 Site 当天 |
| STOCKTAKES_OPEN | StockTakes 非删除，Status 0/1/2/3，不限计划当天 |
| STOCKTAKES_AWAITING_APPROVAL | 同上但 Status=3，是 open 子集，不能两卡相加 |

状态英文是报表语义别名，原数字/注释保真；未知码计 unresolvedHeaderCount/coverage PARTIAL。日期映射、仓→Site、权限不明则 UNKNOWN。operational-card-snapshots 在一次真实只读快照冻结三类 header 与完整成员；多 Site 显示各自日期。历史请求 count=null/HISTORY_UNAVAILABLE，不能进入历史 READY_COMPLETE；若另看当前卡，另起 snapshot 并明确 MIXED_EXPLICIT。某卡失败只影响该卡，完整空集合才 0。[四卡合同](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/2f/2f90c5537845e5cb__WMS-ANALYTICS-01_仓储报表与运行分析_完整详设_R2_CANDIDATE.md:124)

候选 `/api/wms/specials/v1/analytics` 提供 options/reports/snapshots rows/totals/sources/lineage、bridge-health、exports/download 和 operations。StockAnalyticsReadAdapter 必含精确行、有符号腿、纠错、量义、同 cut/完整 manifest；CurrentOnly 不可退化回答历史。BridgeHealthReadAdapter 与 FinanceValuationReadAdapter 均只读，估值未采用只影响金额，不默认币种。[API 与 adapter](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/2f/2f90c5537845e5cb__WMS-ANALYTICS-01_仓储报表与运行分析_完整详设_R2_CANDIDATE.md:92)

## 6. 原子发布、重试与错误

Operation 唯一 `(scope,actor,operationId)`，analytics.v1 canonical 保完整 filters、两时间、definition/mode 和 RequestedUse。相同键同义复用原快照，异义 409；worker lease 只调度计算资源，不换 sourceCut。行 manifest/合计/sourceCut/READY receipt 原子发布，半成品不能报全量。迟到新事实建立新 report，旧快照不重写；关闭/断线按原键恢复。[并发规则](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/2f/2f90c5537845e5cb__WMS-ANALYTICS-01_仓储报表与运行分析_完整详设_R2_CANDIDATE.md:104)

cursor 绑定 snapshot/person/scope/filter/permissionEpoch，过期 409 需新查询但保旧 report 记录。400 filter/未知字段，422 混单位/溢出/不支持年龄基准，409 definition/inputkey/cursor，503 source/history unavailable，202 building/waiting。READY_PARTIAL 必须列具体未知范围；只读重试不能触发 Stock 或桥接写。

## 7. 权限、导出与缓存

read/history/valuation/bridge/evidence/export 独立权限，聚合域权与明细权分开；过滤须在源读和聚合前完成，不能对前 100 个可见对象冒全域分母。Scope 为可信 environment/tenant/siteIds/mode，缓存还含 Owner、permissionEpoch、definition/cut，换租户或 403 清浏览器受限缓存。

导出只用原 snapshot 与白名单列，在创建、worker 生成、下载三次核当前行/字段权。撤权产生 AUTHORIZATION_CHANGED，不能给旧 artifact URL 绕过；新有权内容需新制品/hash。CSV 文本用安全文本表示并保原值 schema，JSON 保 machine-readable units/knowledge；无权字段不能藏在隐藏列。服务端文件名、安全到期下载和审计，不外发第三人。[导出与权限](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/2f/2f90c5537845e5cb__WMS-ANALYTICS-01_仓储报表与运行分析_完整详设_R2_CANDIDATE.md:108)

## 8. 页面与用户理解

P01 dashboard 每卡可展开定义/cut/coverage，金额分币种、不硬编码 ¥；P02 当前/月报/流水明确历史与当前量轴；P03 ABC 与两种呆滞（期间 1..366 天、idleDays 1..3660）；P04 库龄保 UNKNOWN/FUTURE 桶；P05 bridge 分运输/业务/应用，不提供直接补偿；P06 完整来源；P07 导出队列/行数/byte/hash/cut。

任何卡失败单独 UNKNOWN，不能初始化 0 造成全绿；旧数据可标 STALE，迟返不盖新筛选，Back 恢复原 snapshot，卸载清图表/轮询。金额未知不删除已知数量，来源明细无权显示 REDACTED 且不泄隐藏计数。[七页设计](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/2f/2f90c5537845e5cb__WMS-ANALYTICS-01_仓储报表与运行分析_完整详设_R2_CANDIDATE.md:24)

## 9. 实施顺序、旧行为和 schema 变化

N01–08：定义/请求/快照/manifest/导出 → Stock 同 cut 与运营卡只读适配 → 库存/ABC/呆滞算法 → 历史剩余库龄 → Bridge 原状态与业务结果 → 页面 → Finance/IAM/导出 → 验收。source schema 变化建立 DefinitionImpact，只阻受影响指标的新查询，未采用映射则 SCHEMA_UNSUPPORTED；不追改旧快照或无理由重验无关册。[任务](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/2f/2f90c5537845e5cb__WMS-ANALYTICS-01_仓储报表与运行分析_完整详设_R2_CANDIDATE.md:151)、[schema 影响](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/2f/2f90c5537845e5cb__WMS-ANALYTICS-01_仓储报表与运行分析_完整详设_R2_CANDIDATE.md:90)

原稿固定 CP6 `90c871fe…` 映射指出：旧月报拼历史量与当前 Allocated/均价；流水有 5000 限制；Dashboard 混量纲/硬编码币种；旧库龄对今天正库存改日期，缺来源被排除、未来日期夹 0；Bridge COMPENSATED 只人工改标签，无实际业务补偿。本轮未重审业务代码。兼容保 LEGACY_MIXED_CUT、CURRENT_STOCK_AGED_AT_DATE 和业务恢复 UNKNOWN 标签，不悄改旧 CSV/原文件或将其升级为新事实。[旧实现映射](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/2f/2f90c5537845e5cb__WMS-ANALYTICS-01_仓储报表与运行分析_完整详设_R2_CANDIDATE.md:10)、[兼容](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/2f/2f90c5537845e5cb__WMS-ANALYTICS-01_仓储报表与运行分析_完整详设_R2_CANDIDATE.md:112)

## 10. 验收与状态

18 AC 全部 NOT_RUN。核心数值场景：同 cut IN100/OUT30=70，缺历史 Claim 则 Free UNKNOWN；MOVE 净零、ADJ−5 保负；月界亚秒与 late correction；ABC800/150/50 同单位为 A/B/C，单产品 100% 为 C；不同单位 quantity 分区，拆三腿 frequency 仍 1，频次8/1/1 同频 B<C 不因量/单位变化改等级；四年龄桶加 UNKNOWN/FUTURE 总量守恒；同产品另一 Root 活动不刷新本 Root 呆滞。

还须覆盖 Bridge 3SUCCESS/1FAILED/1DEAD 的运输率0.6而业务未知、人工补偿标签不算恢复、完整导出/分页或明确上限、worker 失败原键恢复、迟返/卸载、金额知识/币种、权限撤回、schema 影响/CSV 安全文本。R2 四卡需验证准确数字状态/日期谓词、盘点子集不加总、历史 CurrentOnly 为 UNKNOWN、独立卡失败不清其他卡。RequestedUse 与 evaluation 必须进入缓存/导出并使不同问题的 Free 分开。[全部 AC](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/2f/2f90c5537845e5cb__WMS-ANALYTICS-01_仓储报表与运行分析_完整详设_R2_CANDIDATE.md:145)

## 11. 编码前待核

N-G1 Stock 历史 Root/Claim/Restriction 同 cut 与旧腿/纠错；N-G2 Main WMS 指标/算法政策及 IN/OUT/StockTake 只读接线、Site 日期映射；N-G3 S6 native 健康与原结果；N-G4 Finance 估值（只影响金额）；N-G5 IAM 行/字段/聚合/导出。每项保真实 provider repo/commit/schema/method、映射、范围和 Owner 采用记录。共享评审、目标 UA/CURRENT 与采用保持附件已补核；跨Owner双方合同与现实采用仍待集中核对。[采用门](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/2f/2f90c5537845e5cb__WMS-ANALYTICS-01_仓储报表与运行分析_完整详设_R2_CANDIDATE.md:147)

## 12. 来源与实读范围

正文 1–172 行本轮全文实读，含全部算法、RequestedUse、四卡谓词、精确 API、任务和18 AC；X4 接受、最终独审及来源限定已读。逐件 SHA/读段/未读范围见 [root 阅读账](D:/CP6/docs/CP6_开发设计文档_20261010/evidence/root-specialty-reading.json)。本文没有把原稿旧测试、源码读取、NOT_RUN 用例当作本次执行。

补读完成记录及准确同文复用方法见[X4六专项阅读与采用边界](D:/CP6/docs/CP6_开发设计文档_20261010/evidence/X4-六专项阅读与采用边界.md)。这不替代外域依赖的双方集中核对或实际业务验收。

## 必要材料整理结论

当前17项必需引用均已完整阅读或使用有证据的精确复用：主文、当前接受/审查、原范围、AC与任务文字以及来源资格已纳入设计。历史成员目录只证明来源身份，不等于旧代码全文审计；外域实际采用、Provider和业务运行继续按本册门禁确认。见[逐目标必需引用核对](../evidence/root-specialty-required-closure.json)。后续整任务进行一次集中跨模块复核，不将静态设计整理等同实际验收。
