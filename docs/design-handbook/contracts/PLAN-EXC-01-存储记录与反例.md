# PLAN-EXC-01：存储记录与隔离反例

本册补齐本地记录、物理记录和六个隔离反例附件的语义。两份记录册共 81 类表、16,679 个记录实例：612 个本地不可变记录、417 个可变行快照、15,650 个来源/私有投影记录。实例不是生产数据，也不是执行通过的验收结果。

## 1. 开发时按职责读取表

|职责|主要表与实现边界|
|---|---|
|来源资格与身份|SourceArtifact 保存准确原字节；ProducerQualification 是独立资格观察；SourceProvenance 是 SourceTrust；SourceFact 是 Owner 原生根；KeyIdentity 保存原 UTF-8 键。不能混用这些表的摘要。|
|可追溯输入|FactDependency 记录准确 native 依赖；CurrentManifest/Member 保存具体资源、动作、head/subject/trust；目录中存在不代表该 read cut 已接纳。|
|私有业务根|SourceGraph、Assessment、ImpactManifest、SourceReadPlan 保留精确私有业务前像；公开脱敏视图不是存储根。|
|图内关系|DemandSlice、SupplySpan、BeneficiaryShare、ImpactNode/Edge 等每个 graph/impact 分配本地 PK；原 wire child ID 留在私有根。跨图同 wire ID 不能覆盖不可变行。|
|业务版本|CaseRevision/Head、HeadObservation、Resolution/CaseClosure 关联完整 Assessment、Impact、Current、epoch、commit；不能只比较 revision 数字。|
|命令与观察|CommandSlot/Receipt 保留原始请求和结果；Operation、RecalcWork、DispositionWork 保存当前进度；Owner 原回执与本地消费分别保存。|
|消息与恢复|SourceStream、Inbox、WaitingDependency、ProcessingAttempt、RecoveryCheckpoint 保存原顺序、claim、原槽和未知结果；新进程不能从零开始或换请求。|
|提示、投影、导出|AlertHead/Dismissal 只控制提示；ProjectionHead/RepairPlan 管独立投影；ExportBundle/Preparation/Checkpoint/RowRef 固定读集与原文件。|
|提交控制|WriterLeaseHistory/Head 和 StoreCommitRecord/Head 保存真实租约与提交链；可变行 RowVersion 是读回值；完整 CAS 失败回滚整个 UoW。|

表根、字段类型、外键和约束详见[数据与类型](D:/CP6/docs/CP6_开发设计文档_20261010/contracts/PLAN-EXC-01-数据与类型.md)；正常场景顺序详见[本地事务与恢复](D:/CP6/docs/CP6_开发设计文档_20261010/contracts/PLAN-EXC-01-本地事务与恢复.md)。

## 2. 资格样本的 QUALIFIED 不等于真实注册

636 份 SourceFact 包含 633 个上游事实和 3 个本地 CurrentApplied 原事实。对应资格观察有 371 种不同正文、80 类 `NATIVE_SOURCE/<Kind>` 角色、17 个 Owner。样本的 `QualificationState=QUALIFIED` 与 `CONDITIONAL_NORMAL_ONLY/<Owner>` 同时存在。

资格正文明确限定为假设未来专业采用下的观察，或指定 EXC writer 的 current-apply 提案；两者都不代表真实注册。实际 adoption、registration、grant 仍为零，运行仍未证明。不能把样本字符串写成“Owner 已接入”。

NativeKey 的 owner、namespace、objectKey、lineKey、scheduleKey、occurrenceKey 六元组与原 FactRef 逐对象匹配；version 和 bodyDigest 另有身份维度。键的 UTF-8 字节、长度、原哈希和显示文本对应；不是只靠显示字符串或散列判同。

## 3. 三个本地 CurrentApplied 结果

|场景|原 expected / 实际结果|回执含义|
|---|---|---|
|M04 迟到 sequence1|expected2，保留实际 Case3；commit95、epoch8|HISTORY_ONLY，返回确切保留的 revision/head tuple；不新增业务版本。|
|M04 sequence2 重试成功|expected3→Case4；commit97、epoch8|APPLIED，保存该次 source observation、current、完整新 head/revision。|
|epoch9 非终态分支|expected4→仍 OPEN 的 Case5；commit102、epoch9|APPLIED，但与另一分支 CLOSED5 是不同完整记录，不能按数值合并。|

这些原生结果也保留准确 Owner、authorityScope、audience、原字节和不可变来源身份。完整封套与内部 body 不能互相替代；正文已读不自动意味着封套已读。本次补齐了 UoW 复用所需的 38 个完整 native 封套字段组及精确内部对应。

## 4. 六个隔离样例应如何实现

|样例|只改变的条件|期望行为|
|---|---|---|
|AC095|准确 native current 只允许 resolve，请求是 recalc；其余 source/body/权限/时钟有效|412 `ACTION_CURRENT_MISMATCH`，domainWrites=0、ownerDispatch=0。|
|AC096|WMS current 只保证旧 root80，未包含新 receipt20 与 movement；其余正文有效|412 `SOURCE_CURRENT_CHANGED`，不能借旧完整集合重算新来源。|
|AC100|技术用途只允许 TRIAL，当前需求为 PRODUCTION，质量已接受且其余输入完整|用途 NOT_SATISFIED；knowledge仍 COMPLETE，已知短缺50；不凭 quality 自动授予生产用途。|
|AC101|原生 Run 给 PLAN-EXC 的 audience 只有 source.read，而 receiver 本身有权限|422 `AUDIENCE_ACTION_DENIED`；无新分发/授权，历史保留。|
|AC098|原 stock80 坐标还在，但当前可用量读取不到|私有图 PARTIAL、current effectivity UNKNOWN；Quantity=null、QuantityKnowledge=UNKNOWN、CoordinateQuantity=80；不填80、不填0。|
|LEGACY_EXACT_SUCCESS|历史原报 required25、available0、status RESOLVED，尚未映射|只展示 LEGACY_UNVERIFIED/UNMAPPED；不新建 Case/Assessment，不纳入正常 KPI。|

AC098 新私有图在 18:12 记录 `CURRENT_QUANTITY_UNAVAILABLE`，阻止 assess、recalc、resolve；原 stock80 的历史事实不改写。其 SourceGraph 根摘要排除自身 digest，规范化 SupplySpan 仍保留 Graph/Root/SourceFact/UOM/CurrentHead 的准确成对引用。[原隔离样例](<D:/CP6-archives/consolidation-20261010/root-planning-cache/plan-exc-cp12/CP03_AC_DIFFERENCE_SPECIMENS.json>)

## 5. 文件、数量与关闭不能互相代替

导出样本原文件是 296 字节 CSV，包含两个 CLOSED Case：主 Case required100/shortage0、退休责任 Case required0/shortage0。文件以原 SHA 固定，并不把第二个 Case 改成“原需求从未存在”。

材料样本仍区分旧 WO gross60 已抵20→net40，以及新计划材料 gross180 可用50→net130。已抵20的保护继续 ACTIVE，但 `IncludedInAvailable=false`；不能再把这20当可用量抵一次。当前源图的 `ExecutionCertainty=UNKNOWN` 与 `GraphCompleteness=COMPLETE` 可以同时成立：来源图完整不代表 Owner 执行动作已结清。

## 6. 阅读与核对证据

全部 81 类表的字段、不同业务值、记录实例和精确来源保留在[记录阅读台账](D:/CP6/docs/CP6_开发设计文档_20261010/evidence/PLAN-EXC-01-record-reading.json)。有意义的状态/数量/范围组合已与原图及 65 个 UoW 场景对应；大量 UUID、摘要和时间实例用逐对象关系检查保留，不复制成冗长正文。

[记录表示核对](D:/CP6/docs/CP6_开发设计文档_20261010/evidence/PLAN-EXC-01-record-representation-validation.json)覆盖16,679个记录、1,341个原字节artifact和636组Fact/Provenance/Qualification身份关系；[键与反例核对](D:/CP6/docs/CP6_开发设计文档_20261010/evidence/PLAN-EXC-01-record-key-and-AC-validation.json)覆盖636个准确NativeKey、索引目标和反例表示；均无文档关系错误。

这是文档字节、对象和字段关系核对。没有执行原始脚本、SQL、业务代码、数据库约束、权限服务或验收用例，不构成运行通过。归档中的历史模板和未采用材料另按版本分类保留。
