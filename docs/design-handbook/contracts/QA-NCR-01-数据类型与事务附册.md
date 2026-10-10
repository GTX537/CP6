# QA-NCR-01 数据、类型与事务附册

整理状态：当前必要材料已闭合。本附册覆盖主 WIRE 224 个定义、FIELD 的 3,673 条映射、STORAGE 的 37 个事实族/932 个列声明、25 个事务条目、23 个摘要域和语言声明义务。外部必要 schemas、Main6 TS/C#、CP4_R1、普通对象及准确采用材料的完成范围见主册与 [独立阅读记录](D:/CP6/docs/CP6_开发设计文档_20261010/evidence/QA-NCR-01-supplement-reading.json)。整理完成不表示实际运行或验收通过。

## 1. 当前规范身份和使用方法

R12 的当前普通值入口是 NORMAL_R12；R6/R9/Candidate10 是保留的规范声明族名。R12 只修普通值第 93 项的完整 CLOSED head 引用及 Receipt raw/base64，其他 170 项与 224 定义保持冻结。每份原件的候选阶段标签、后续准确设计采用与真实运行资格分层读取，不重命名 wire 来暗示已经部署。[当前保留规范](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/f9/f98ed1c8a0fdb9d4__NCR-R12-ACTIVE-ARTIFACT-INDEX.json:16) [R12 变更边界](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/14/14766addb4ca6889__README-R12-CANDIDATE.md:3)

主 WIRE 是闭型对象、显式 null、准确 oneOf 和有界数组的权威入口；FIELD 说明字段来源、编辑边界与落点，STORAGE 是声明式映射，并非可执行迁移。337 个引用节点已机械定位，无未解析引用；这只是路径闭包，外部正文必须另有实读或精确复用证据。TS/C# 当前仅声明，不自带已实现的严格 codec/validator。[语言合同](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/9e/9e9ee118ded0250f__NCR-R6-LANGUAGE-CONTRACTS.json:1) [存储声明](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/45/458319eab88e2fb7__NCR-R6-STORAGE-MAP.json:1)

## 2. 身份、数量、原件及摘要

|数据|准确约束及开发含义|
|---|---|
|E|tenantId、environmentId、mode（REAL/DEMO）、siteId；来自可信认证，全部键/FK/锁/恢复保完整 E。不可给外域三成员原 body 私加 siteId。|
|UUID / Version / Sequence0|UUID 为规范小写、版本 1–5；正版本是正 Int64 十进制字符串，Sequence0 另允许 0。业务序号不是 SQL rowversion。|
|Decimal / Time|数量为非负规范串：最多 13 整数位和 8 小数位，不留小数尾零；本域时间固定 UTC 六位小数，同时保存外域准确原时间文本。|
|Evidence / OwnerRef / OwnerBusinessRef|分别为 owner/id/version/digest、owner/id/version、owner/streamId/businessId/revision/digest；不能为 OR3 造 digest，也不能把本地 Evidence 直接改 Owner 名称成为 Finance Ref。|
|CoordinateRange|domainRef、portionId、from、to、UnitIdentity；范围与原域/portion/单位同时参与语义，不能只比较长度。|
|UnitIdentity|OWNER_REF 或 QAP_UOM 两个闭型分支，QAP 原 Uom 身份独立保留。|
|Intent / Principal|IntentKey 允许 1–128 个规范字符；PrincipalEnvelope 保存 authenticatedService、actorId、delegationId、delegationKey、purpose、scopeDigest，不含凭据。|

原型依据：[基础标量](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/39/392443aa5b52e47d__CONTRACT-SCHEMAS.json:10) [四成员环境](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/39/392443aa5b52e47d__CONTRACT-SCHEMAS.json:49) [原引用类型](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/39/392443aa5b52e47d__CONTRACT-SCHEMAS.json:103) [意图键](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/a3/a3e5c08a426bef34__CONSUMER-WIRE.json:81) [Principal](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/a3/a3e5c08a426bef34__CONSUMER-WIRE.json:400) [CoordinateRange](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/11/11b9077c2ccacb39__NCR-R6-WIRE.json:100) [UnitIdentity](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/11/11b9077c2ccacb39__NCR-R6-WIRE.json:48)。

OriginalAnchor 必须分开保存 sourceProtocol、ReferenceIdentity 判别分支、原始 bytes/Base64、raw SHA、Owner semantic digest（可显式 null）、原 digestProfile、Owner commit、原时间、typedSourceSchema、producerRegistration；一次严格解析绑定 typed 列与原文，不以重新序列化后的本地 body 替换原始 bytes。其持久化专列包含 OriginalBytes varbinary(max)、RawSHA256 binary(32)、原时间 nvarchar(64)、schema pin nvarchar(512)。[OriginalAnchor](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/11/11b9077c2ccacb39__NCR-R6-WIRE.json:2253) [OriginalAnchor 列映射](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/45/458319eab88e2fb7__NCR-R6-STORAGE-MAP.json:8347)

23 个本域摘要均保各自准确域。一般公式是 SHA256(UTF8(domain) + 单 NUL + UTF8(JCS(payload)))；raw Evidence 是完整非 self body 的 SHA，不能混为业务摘要。Main head body 不含自身 ref/etag；HEAD 域的 payload 是 headRef。初始来源 root 键排除版本/digest/range/purpose/profile，版本冲突另存。返工授权键只取 E+ncr+decision 逻辑身份+partition，换 approval 或 HTTP key 不能生成第二周期。[摘要公式](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/eb/ebb0386a3a64d072__NCR-R6-DIGEST-DOMAINS.json:3) [全部 payload 定义](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/eb/ebb0386a3a64d072__NCR-R6-DIGEST-DOMAINS.json:124)

H30 另保 FIN-INV 原 typedResult:v1 object-wrapper、queryIdentity plain JCS SHA、raw bytes SHA；proof2 使用两个新的准确 NUL 域，不能倒算替换原 profile1。proof2 是后来接受的新设计，旧 proof1 原公式未恢复的资格仍保留。真实 QUALIFIED/ACTIVE 必须来自登记 authority。[Finance 原摘要分层](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/eb/ebb0386a3a64d072__NCR-R6-DIGEST-DOMAINS.json:150) [proof2 签发与迁移](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/2c/2cc950c88782413c__FIN-INV-H30-QA-PROOF-ENCODING-ADDENDUM-2-CANDIDATE.json:7)

## 3. 事实表、父身份与一次提交

37 个事实族覆盖来源/记录/决定/批准/更正、报废/退回/原样使用依据、工程反馈与偏离、返工意图/映射/命令、完整 close 以及 H30 三阶段。普通事实 PK 是 E+NcrId+FactId+Version；特殊父身份如下。所有事实保 canonical bytes、准确 typed 子行/分支/顺序和完整基数；不能把一个 JSON 列当作省略关系约束的理由。[37 事实生产者](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/4b/4bfc1d1febd24c7f__NCR-R6-FIELD-MAP.json:3) [逐事实列声明](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/45/458319eab88e2fb7__NCR-R6-STORAGE-MAP.json:3)

|事实|准确主键或身份|
|---|---|
|MainHeadEvidence|E+NcrId+HeadVersion；唯一 current aggregate，原 v5 与 Main6 各保原 codec/ref/etag。|
|MainResult|E+NcrId+ResultId，Evidence.version=1；结果含 resultingHeadVersion 数值而不引用未来 head body。|
|AuthContextCapture|E+NcrId+CaptureId+CaptureVersion；在业务事实前形成，不含未来 result/head。|
|WorkIdentity|E+NcrId+WorkId；先分配稳定 work/cycle，不反向依赖未来 dispatch/result。|
|OriginalAnchor|E+NcrId+AnchorId+AnchorVersion；来源身份不可借用途或 schema 重命名。|
|PreparedOwnerCommand|E+NcrId+CommandId+CommandVersion；CommandId 同原 X-Command-Id，重试不换。|

依据：[Head 与后续事实](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/45/458319eab88e2fb7__NCR-R6-STORAGE-MAP.json:3528) [Work/Anchor 父身份](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/45/458319eab88e2fb7__NCR-R6-STORAGE-MAP.json:8207) [MainResult](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/11/11b9077c2ccacb39__NCR-R6-WIRE.json:10820) [PreparedOwnerCommand](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/11/11b9077c2ccacb39__NCR-R6-WIRE.json:12617)。

关联写序是 GlobalCommit → 稳定 FactIdentity → AuthCapture/原件/typed facts → result → 准确 profile head/current CAS → terminal slot/audit/outbox/checkpoint/完整 FactCommitMap；保存 FactCommitMap 时最终 head 已存在，不能用立即 FK 指向尚未形成的未来 head。Correction 提前分配 child 的 id/version，Correction body 只列这些稳定身份，child 再 FK 到 Correction，避免互含 digest 的环。[统一写序](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/72/72912f600e745376__NCR-R6-STATE-AND-TRANSACTION-RULES.json:291) [Correction](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/11/11b9077c2ccacb39__NCR-R6-WIRE.json:2139) [InvalidationItem](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/11/11b9077c2ccacb39__NCR-R6-WIRE.json:13020)。

索引不能对长自然键直接假设可建：声明要求 surrogate bigint clustered key；完整自然唯一键在 1,700 字节内才直接建 nonclustered unique，否则需受 SERIALIZABLE 锁保护的有界 digest bucket，加完整自然键相等比较/碰撞拒绝，不能仅凭 hash 判同业务身份。数组子表带完整 E/父身份/branch/ordinal，并另核语义唯一与次序。[长键与数组约束](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/45/458319eab88e2fb7__NCR-R6-STORAGE-MAP.json:14752)

## 4. 命令、流程与写集

|入口|生产事实与必须保留的边界|
|---|---|
|CREATE_NCR|三来源准确读和 final/current、完整 incident 范围核完后保存 Original/Identity/Projection/固定 SourceUseSlot/Binding/Record；同 commit 产生 SOURCE_BOUND→RECORDED 两个连续 head、一个结果。|
|APPEND_RECORD / CORRECT_RECORD|未有 Decision 才可普通 append；有决定后更正必须完整列 downstream invalidation/coordination，保可能已执行的 Owner effects；CLOSED 禁普通更正。|
|DECIDE_NCR / RECORD_APPROVAL_EVENT|全部原范围无洞无重叠；批准 NONE→APPROVED/REJECTED、REJECTED→APPROVED、APPROVED→WITHDRAWN，撤销不删专业履行。|
|PREPARE_REWORK → APPROVE_REWORK_SCOPE → ISSUE_REWORK|先唯一 work/cycle，再双方有权 L→M 范围/单位/有效期映射，再生成原 MES command/work/outbox；OWNER_PENDING 是请求准备或准入，非制造完成。|
|PREPARE_SCRAP/RETURN/USE_AS_IS_BASIS|只写质量依据；非库存必须真实 responsibleOwner/object/requirement，不能以无库存证明 NA；原样使用若需技术偏离必须走 H50。|
|PREPARE_FEEDBACK / ASSESS_QUALITY_CHANGE|本地草稿与 Quality assessment；PLM 接受/合并、实施和验证均需原 Owner 事实。|
|APPROVE_QUALITY_USE|APPROVED 核当前技术与质量依据；WITHDRAWN 先选本地保护分支，只需原本地前驱及当前撤销权，不因 PLM 过期/不可达阻止撤销，不释放额度。|
|RECONCILE_SOURCE|准确同 root 的真实来源后继，加 successor Binding/Record 与完整 Correction/Invalidation/Coordination，不能造新 NCR 绕源唯一。|
|CLOSE_MAIN|完整义务、逐类原证据及同 UoW guards/Use/Receipt；all-REWORK 仍导航原 v5 profile，mixed/non-rework 走准确 Main close 采用；候选/实际启用与旧标签分层。|
|H30_PREPARE → H30_PUBLISH → Finance final use|先 Control/InputBinding/PreparationReceipt 和 Notice；再 Publication；最后真实 Finance effect 与 Guard/Use 同提交。前两步不推进 NCR head 或 Finance value。|
|reads/preview/history/input lookup|只读固定 cut/准确原意图；不凭原 terminal replay 续期专业 currentness，不将 unavailable 变为空成功。|

完整 25 条操作条件/状态/失败/写集见 [事务规则](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/72/72912f600e745376__NCR-R6-STATE-AND-TRANSACTION-RULES.json:4)；质量撤销的独立分支见 [保护性撤销](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/72/72912f600e745376__NCR-R6-STATE-AND-TRANSACTION-RULES.json:140)；H30 prepare 的相同 snapshot 收敛见 [H30 prepare](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/72/72912f600e745376__NCR-R6-STATE-AND-TRANSACTION-RULES.json:269)。

事务共性为先严格闭型/重复 key 检查、可信 E/权限及原 principal/稳定 slot 校验，再原 terminal 同意图恢复、完整依赖发现、按登记顺序 SERIALIZABLE 锁和集合重枚举，最后一套写集与提交点权限/资格/原件一致性检查。原 terminal lookup 先于专业 proof 的过期检查，仍须当前恢复权限；不能把历史重放当新专业效应。跨 Owner 同 UoW 只是待真实拓扑/注册/全部 writer 证明的设计义务。[共同事务步骤](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/72/72912f600e745376__NCR-R6-STATE-AND-TRANSACTION-RULES.json:291)

## 5. 完整关闭与历史 DTO

15 个 ObligationKind 覆盖质量批准、技术基线、质量应用、返工完成、重检、报废物理、退回物理、财务价值、反馈结果、变更实施、工程验证、偏离技术/质量/使用及来源协调。SATISFIED 必须有完整满足范围和 typed Subject；NOT_APPLICABLE 的 satisfied/result 数组严格空、subject 显式 null，另有原 NA 依据。UNKNOWN 不可转换为 NA。[ObligationKind](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/11/11b9077c2ccacb39__NCR-R6-WIRE.json:3132) [MainCloseMember](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/11/11b9077c2ccacb39__NCR-R6-WIRE.json:17423)。

CloseCurrentVector 逐 LOCAL_QUALITY / REWORK / PHYSICAL / FINANCE / PLM / PROJECTION / RECONCILIATION / NON_STOCK_PHYSICAL 保存各自 current 头，不能换成通用 seal。MainClosedBasis 完整枚举，逐 CKey Guard，MainCloseUse 完整成员、锁向量、原结果与范围，ClosureReceipt 逐 ordinal 对应 Use；receipt 的 closedHeadRef 必须指全部字段已经形成的完整 CLOSED head。[MainCloseCurrentVector](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/11/11b9077c2ccacb39__NCR-R6-WIRE.json:17720) [MainCloseUse](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/11/11b9077c2ccacb39__NCR-R6-WIRE.json:18635) [ClosureReceipt](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/11/11b9077c2ccacb39__NCR-R6-WIRE.json:18784) [R12 tail 修正](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/14/14766addb4ca6889__README-R12-CANDIDATE.md:3)。

PublicDetail 仅摘要；AuthorizedDetail 的 typed facts 不自动授原 bytes/授权 capture 读权。FactHistory 有 39 分支，其中三种 CP4_R1 返回原 consumer5 body，其余保各 Main6 事实类型；pageSize 1–100，cursor 固定 cut/查询/principal/权限/排序/累计 emitted，末页完整不等于可作 close basis（mayUseAsCompleteCloseBasis=false）。[AuthorizedDetail](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/11/11b9077c2ccacb39__NCR-R6-WIRE.json:3878) [FactHistoryItem](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/11/11b9077c2ccacb39__NCR-R6-WIRE.json:14138) [FactHistoryPage](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/11/11b9077c2ccacb39__NCR-R6-WIRE.json:15704)。

## 6. 来源无损适配与实施注意

IPQC adapter 从原 I02、独立完整 SourceOriginal、I04 已有政策 context 只读取证；不调用 I03 resolve 产生新政策。OUTPUT 旧五成员 receipt mapping 与完整原件按原键、全基数和顺序一对一比，receiptSubject 只从真实 standalone 原件复制，output/root/receipt 三坐标保持。准确旧 raw 与新 candidate projection 分别保全，不能通过删字段适配旧 schema。[source adapter 读取及模式](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/fc/fc35327a0862a5b1__NCR-R10-SOURCE-ADAPTER-CONTRACT.md:12) [全映射对齐](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/fc/fc35327a0862a5b1__NCR-R10-SOURCE-ADAPTER-CONTRACT.md:16)。

QAP 使用完整 27 字段 ResolutionEvidence 与同 context 原 body/ref；正确 native body hash、实际 TaskDetail/Decision/Context 同一 PlanInstance、无 alias 的 INITIAL 同登记族全等，均是必要关系。相同计划版、相同总量或同 source 名称不足以重连。最终仍由 IPQC/QAP 各有权 writer participant 核自己的完整当前控制，不往旧 F05 DTO 私加字段。[native proof](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/33/3305bac3936f0b7c__NCR-R11-SOURCE-GRAPH-RELATION-CONTRACT.md:9) [实际计划](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/33/3305bac3936f0b7c__NCR-R11-SOURCE-GRAPH-RELATION-CONTRACT.md:15) [同登记族](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/33/3305bac3936f0b7c__NCR-R11-SOURCE-GRAPH-RELATION-CONTRACT.md:17) [最终控制](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/33/3305bac3936f0b7c__NCR-R11-SOURCE-GRAPH-RELATION-CONTRACT.md:25)

NCR-FIELD-01：字段注释有采用前待校准处：FIELD 的 MainResult/CLOSE_MAIN.priorHeadRef 标 nullable=true，但同条目 shape 与 WIRE 为非 null Evidence；InitialSourceOptionQuery 的 sourceKind/keyword/cursor 与 OptionPage.nextCursor 注释 nullable=false，但 shape 明确允许 null。部分 server-produced H30/close Body 被标 editable=true，不能据此授浏览器修改权。生成 DTO/表单须以准确 WIRE 与具名 producer/权限规则共同决定；本次仅静态文档差异，不声称运行故障。[priorHead 元数据](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/4b/4bfc1d1febd24c7f__NCR-R6-FIELD-MAP.json:30788) [source-options 元数据](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/4b/4bfc1d1febd24c7f__NCR-R6-FIELD-MAP.json:52882) [MainResult](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/11/11b9077c2ccacb39__NCR-R6-WIRE.json:10820) [InitialSourceOptionQuery](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/11/11b9077c2ccacb39__NCR-R6-WIRE.json:20782)。

FIELD 的 3,673 是已列直接字段记录数；另有 115 个嵌套 property 没有独立 FIELD 行，但其完整 shape 已包含在父条目/WIRE，不能把“没有独立行”写成“缺少字段设计”。本次已按全部主 schema 逐层阅读这些嵌套规则。

NCR-TYPE-01：Main6 C# 的 FIN C09FailedProblem 三分支 originalQuery（L1972/1988/2004）与 IPQC GuardResult 的 guardRef（L4458）把原 const:null 写成 required long。Main6 四处及 CP4 L200 的 int GuardRef => null 的 Schema 与声明证据见 [本域差异登记](D:/CP6/docs/CP6_开发设计文档_20261010/evidence/QA-NCR-01-integration-findings.json)；采用前需校准 null-only 类型和严格 codec，本次未编译或运行。

## 7. 阅读范围与运行边界

完整主类型采用可逆结构投影；224 个定义回建均与原字典相等。FIELD 全部已列 pointer/shape/required 与 WIRE 相等，所有元数据按完整字段参数化模板读取并回建相等；932 个存储 shape 与 WIRE 相等，所有原列名/分支/SQL 域归属保在阅读证据。抽取、回建相等与业务语义阅读是不同证据，均不代表作者静态 PASS 已被重跑。

CP4_R1 OpenAPI/语言、所有普通对象完整业务值和50/102/68 AC字段已补齐；26项准确专业设计采用及其R9→R12来源链已合并，见 [专业采用附册](D:/CP6/docs/CP6_开发设计文档_20261010/contracts/QA-NCR-01-专业采用与版本继承附册.md)。G1/G2/旧身份 producer/BFF/分页与字段存储映射已整理到 [返工消费与 Owner 合同](D:/CP6/docs/CP6_开发设计文档_20261010/contracts/QA-NCR-01-返工消费与Owner合同.md)。作者已有 PASS/NOT_RUN 按原来源保留；本次未执行归档脚本、SQL、业务测试、编译、Actions 或发布。
