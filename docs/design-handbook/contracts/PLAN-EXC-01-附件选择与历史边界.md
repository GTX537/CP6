# PLAN-EXC-01｜附件选择、集合规则与历史边界

本册补齐异常规划归档的读取入口和历史用途。351 个成员的完整字节均已按唯一组合目录选择并校验；阅读深度逐项见[成员账](../evidence/root-plan-exc-current-members.json)，不能把归档校验、历史作者的阅读声明或工具解析计为本轮全文语义审读。

## 当前开发需要的完整组合

主文与[源事实及公开投影](PLAN-EXC-01-源事实与公开投影.md)、[数据与类型](PLAN-EXC-01-数据与类型.md)、[Owner 与游标](PLAN-EXC-01-Owner与游标.md)、[本地事务与恢复](PLAN-EXC-01-本地事务与恢复.md)、[存储记录与反例](PLAN-EXC-01-存储记录与反例.md)、[专业源对象补充](PLAN-EXC-01-专业源对象补充.md)、[版本与归档资格](PLAN-EXC-01-版本与归档资格.md)共同使用。每册保留准确原文、字段或 JSON 指针证据。

CP12 当前组合选择 5 个完整载体中的 351 个完整成员，不是把 CP08 主文依次打补丁。当前变更索引的 350 份其他成员身份、9 个变更源身份、10 个新原生对象、2,720 个不可变行后继、4 个版本父行、4 个可变快照和 297 个 cut 引用均与当前完整对象核对，差异为 0；见[当前索引核对](../evidence/PLAN-EXC-01-current-index-validation.json)。这是文档内部关系核对，不能证明生产旧行已迁移或数据库执行成功。

七个 `chapters/` 文本的所有非空行，除 6 行外均与已读 CP12 主文逐行精确相等；6 行差量已另读并保留历史标签。其中早期作者身份、CP04 标题/计数、旧修订索引和 AC084 作者观察不会覆盖 CP12 最终接受记录。逐行位置保留在[章节复用证据](../evidence/PLAN-EXC-01-chapter-line-reuse.json)。

## 集合、数量和材料方法补充

以下内容来自当前保留的完整 [CP04 集合规则](D:/CP6-archives/consolidation-20261010/root-planning-cache/plan-exc-cp12/current-members/CP04_MANDATORY_COLLECTION_DECISIONS.json)和[材料方法规则](D:/CP6-archives/consolidation-20261010/root-planning-cache/plan-exc-cp12/current-members/CP04_MATERIAL_METHOD_AND_ALGORITHM_SPEC.json)，并已结合当前完整图、存储行和 UoW 复核。

| 规则 | 开发含义 |
|---|---|
| Receipt20 的影响范围 | 供给坐标 `[0,20)` 对应需求 `[80,100)`；映射由完整份额关系确定，不按两个数组的同序下标配对。 |
| ImpactBeneficiary | 各阶段的精确份额各有行与阶段身份；不同阶段的相同显示 GUID 不能冒充同一个不可变持久化身份。 |
| 保护边界 | generic Firm100 不足以证明每段 offset20 的独立保护，`IndependenceProof` 可以为 null；不得凭总量推造保护证明。 |
| 处置回执 | SETTLED 必须累计 ACCEPTED 与 SETTLED，已结清范围是已接受范围的子集，与 unresolved 不相交；active 为 accepted 减 settled。 |
| MRP 结果集合 | RESULTSEALED、SELECTED、PUBLISHED 及冷恢复 FOUND 均按原件完整保留 4 个结果成员及 ordinal0–3，不补造父关系。 |
| 原请求槽 | REGISTERED 的 ordinal0 保留原请求与槽，初始 observed/predecessor 可以为空；后续 observation1 链接 ordinal0，冷恢复也不改原槽身份。 |
| 当前成员唯一性 | `CurrentManifestMember` 的业务唯一键不包含 HeadFactId，否则会容许同一资源/主体/action 的竞争 head 共存。原 558 行的顺序需保留；原关系设计没有可随意新增的 SQL Ordinal 列或唯一键。 |
| MAKE 与 BUY | 先计算父需求净额，再选方法；示例 MAKE 最小量20、增量20，批量100按9/5展开为180。BUY 不重新制造工单或二次展开制造需求。 |
| 材料量关系 | 新毛需求180、自由量50、新净额130；旧毛需求60已抵扣20，旧净额40，因此兼容责任170。受保护20不再计入可用量。 |
| 晚到份额 | 晚到 PO10 仅映射旧工单的 `[20,30)` 并提示延迟，不改作按时供给或抵扣新需求130。 |
| 算法与封存 | input 在 RunAttempt 前封存；所有输出形成完整5成员封存后，才形成 result、validation、selection 和 publish。文档中的构建/config digest 与 `$native` 选择器不证明已发布可执行二进制或真实配置。 |

## 旧代码与准备包的准确用途

完整读取的[旧复用差距](D:/CP6-archives/consolidation-20261010/root-planning-cache/plan-exc-cp12/current-members/source_evidence/OLD-REUSE-GAPS.md)分析的是原指定旧 Main，不是当前仓库代码审计。旧 `AvailableQty=0` 可以表示没有一条 stock 行独立满足请求，并不证明整个仓库可用量为0。旧 Resolve/Dismiss 的状态和备注也不能证明新业务责任已结清。

旧代码有从基类继承的 `RowVersion` 与 `TenantId`；不能在迁移说明中误写为“旧系统没有并发或租户字段”。旧列表/KPI/remark UI 可以作为复用候选，仍需按新的知识完整性、数量来源、授权和 Case 责任重做语义对接。SignalR 通知不是持久 Owner 回执；PR 草稿中的 shortage 引用也不是已生效 MRP 供给。

[原范围与依赖读取包](D:/CP6-archives/consolidation-20261010/root-planning-cache/plan-exc-cp12/current-members/source_evidence/DEPENDENCY-READING-PACKET.md)只固定原五 SPEC、直接 SUP/MRP 和继承 POL 的边界。SC/PLM-AC 空数组不是本轮丢失的验收用例。Stock、ATP、PUR-PR、PO 为有界支持，不因同包出现而授予 EXC 写入这些 Owner 的权限。[PO Q1–Q5](D:/CP6-archives/consolidation-20261010/root-planning-cache/plan-exc-cp12/current-members/source_evidence/PO_ACCEPTED_Q1_Q5_EXACT.txt)继续保留原 R2 载体缺失、源提交与接受提交区别、37 AC 未运行及实际采用为0的限定。

## 351 成员的阅读与保留状态

| 成员数 | 本轮处置 | 不计为完成的内容 |
|---|---|---|
| 50 | 当前规则或上下文已读，含完整业务字段、已验证对象/章节精确复用及明确差量 | 复用账中保留的未输出旧作者对象，不自动继承全文阅读 |
| 110 | 已替代版本、旧报告、变更映射、作者观察等历史身份保留 | 旧正文未全部重读；其 PASS、RETURN、阶段和身份不覆盖当前版本 |
| 169 | 历史文档生成工具，只解析 AST、导入名和顶层结构 | 未执行、未逐段业务语义审读，不是候选产品实现 |
| 22 | 准备阶段清单、来源载体和早期评审的可追溯材料 | 清单、源代码摘录或嵌套 ZIP 不自动产生代码全文审计结论 |

50 不是“只读了50份小文件”：其中包含完整主文、417类型定义、87表、97叶合同、633原生事实、292事务包与16,679存储记录等大成员；每类覆盖方式仍以专册和阅读证据为准。反过来，数量也不证明生产实现完成。

旧作者脚本的用途依据来自完整[CP04工具边界](D:/CP6-archives/consolidation-20261010/root-planning-cache/plan-exc-cp12/current-members/authoring_tools_cp04/CP04_DOCUMENT_OPERATIONS_BOUNDARY.md)、[CP07工具边界](D:/CP6-archives/consolidation-20261010/root-planning-cache/plan-exc-cp12/current-members/authoring_tools_cp07/CP07_DOCUMENT_OPERATIONS_BOUNDARY.md)及[CP08变更索引](D:/CP6-archives/consolidation-20261010/root-planning-cache/plan-exc-cp12/current-members/CP08_FULL_CHANGE_INDEX.md)，不是只凭文件名猜测。历史文档中的作者指令、状态声明和执行记录均作为来源材料，不作为本次操作指令。

## 开发前仍需保留的边界

当前原设计已静态接受，相关跨附件差异仍列在整合问题中：数量类型的负值域差异、Receipt20实际可用时间差异、材料算法引用差异需由相应 Owner 澄清。原件不被本轮整理静默改写。实际 Owner 采用、Core 注册/grant、provider、DB/事务/签名验证及全部业务 AC 仍为未证明或 NOT_RUN。

本册依据及逐文件阅读深度见[补充阅读账](../evidence/PLAN-EXC-01-supplement-and-member-reading.json)；没有执行原作者脚本、SQL、业务或部署。
