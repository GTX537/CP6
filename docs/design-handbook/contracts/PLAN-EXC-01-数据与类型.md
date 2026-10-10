# PLAN-EXC-01：数据、类型与本地存储合同

本册把当前 CP12 组合中四份类型/关系附件整理为开发约束，供实现 codec、仓储适配器、查询投影和本地 UoW 使用。阅读为完整结构化语义阅读：417 个类型定义、109 个入口根、87 张表的全部 1,458 个列声明、346 个外键、各表 CHECK（归一表名后 172 种）、15 个可变域及正常写集索引。结构展开、静态对照不等于已执行 Schema checker、SQL、迁移或业务验收。四份来源的 SHA、完整 JSON Pointer 和行段见[阅读记录](D:/CP6/docs/CP6_开发设计文档_20261010/evidence/PLAN-EXC-01-data-reading.json)。

附件中的 CP02/CP03/CP05 名称和旧 review-copy 状态是内容沿革；本册遵循 [PLAN-EXC-01 当前组合](D:/CP6/docs/CP6_开发设计文档_20261010/modules/PLAN-EXC-01.md)，不由单个附件标签重新选择基线。它们明确是 SQL Server / EF 待采用的设计，implementationAuthorized=false、candidateSqlExecuted=false、NOT_RUN；可变域声明 actualAdoption=0、runtime=UNPROVEN。设计整理完成不授予实施/生产权限。[关系目标与未执行声明](D:/CP6-archives/consolidation-20261010/objects/8127f36528aa69e0c67b104e6cef109de007a4163ce5085769ff58cf88fa58c0.json:4)；[实施状态](D:/CP6-archives/consolidation-20261010/objects/8127f36528aa69e0c67b104e6cef109de007a4163ce5085769ff58cf88fa58c0.json:28291)；[采用与运行状态](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d0/d0efd40b8cac81c4__CP03_MUTABLE_WRITE_AND_READBACK_DOMAINS.json:1203)。

## 1. 身份、字节和数量先于业务表

| 类型轴 | 必须保持的开发规则 | 来源 |
|---|---|---|
| E/T/主键 | 数据库各表以 EnvironmentId、TenantId、Id 为作用域身份；E/T 来自可信执行上下文，不能借公共 DTO 自报。跨表引用带相同 E/T，不能只用 Id 查关系。 | [E/T 前缀](D:/CP6-archives/consolidation-20261010/objects/8127f36528aa69e0c67b104e6cef109de007a4163ce5085769ff58cf88fa58c0.json:5)；[KeyIdentity](D:/CP6-archives/consolidation-20261010/objects/8127f36528aa69e0c67b104e6cef109de007a4163ce5085769ff58cf88fa58c0.json:23) |
| OpaqueKey | 原 UTF-8 文本、完整字节长度和 rawDigest 一起保留；禁止 trim、大小写折叠或把任意业务键强转 UUID。哈希只是桶定位，冲突时比较完整字节。 | [OpaqueKey](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/43/43376e6abeb9fa95__EXC_TYPED_SCHEMA.json:189)；[完整字节身份](D:/CP6-archives/consolidation-20261010/objects/8127f36528aa69e0c67b104e6cef109de007a4163ce5085769ff58cf88fa58c0.json:183) |
| FactRef | owner、namespace、objectKey、可空 lineKey/scheduleKey/occurrenceKey、versionKey、bodyDigest 共同定位源事实。不同版本、行、发生点不能折成一个对象号。 | [FactRef](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/43/43376e6abeb9fa95__EXC_TYPED_SCHEMA.json:217) |
| Artifact 与信任 | SourceArtifactRef 保原始字节长度/raw SHA、原 codec 和独立 canonicalDigest；storageLocator 是私有定位，不是下载 URL。SourceTrust 是已认证适配器侧带，客户端/传输者不能自称 issuer 或入场资格。 | [原件身份](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/43/43376e6abeb9fa95__EXC_TYPED_SCHEMA.json:280)；[信任侧带](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/43/43376e6abeb9fa95__EXC_TYPED_SCHEMA.json:697)；[wire 规则](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/43/43376e6abeb9fa95__EXC_TYPED_SCHEMA.json:26426) |
| RowVersion | wire 是 Base64，解码必须恰好 8 字节；数据库类型为真正的 rowversion。值是不透明 CAS token，既不能 old+1，也不能从样本推测连续编号。 | [RowVersion](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/43/43376e6abeb9fa95__EXC_TYPED_SCHEMA.json:29)；[实际回读要求](D:/CP6-archives/consolidation-20261010/objects/8127f36528aa69e0c67b104e6cef109de007a4163ce5085769ff58cf88fa58c0.json:1893) |
| 明细数量 | Decimal / DecimalQty 非负、最多 13 位整数、恰 8 位小数；范围使用原 UOM 的半开区间，from < to，禁止浮点、悄然舍入或换单位。 | [Decimal](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/43/43376e6abeb9fa95__EXC_TYPED_SCHEMA.json:36)；[存储数量别名](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/43/43376e6abeb9fa95__EXC_TYPED_SCHEMA.json:23080)；[范围](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/43/43376e6abeb9fa95__EXC_TYPED_SCHEMA.json:632) |
| 汇总与映射 | TotalDecimal 最多 30 位整数、8 位小数；精确有理映射 numerator/denominator 为正整数并有依据。汇总不能降成明细的 13 位域。 | [TotalDecimal](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/43/43376e6abeb9fa95__EXC_TYPED_SCHEMA.json:40)；[有理映射](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/43/43376e6abeb9fa95__EXC_TYPED_SCHEMA.json:657) |
| 已知与未知 | 已知数量保 value/uom/basis；UNKNOWN/CONFLICT 等不以 0 代替。数据库 nullable 数量和 knowledge 联动；公共脱敏不是删除私有已知值。 | [数量判别联合](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/43/43376e6abeb9fa95__EXC_TYPED_SCHEMA.json:1023)；[原生数量](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/43/43376e6abeb9fa95__EXC_TYPED_SCHEMA.json:13801)；[列规则](D:/CP6-archives/consolidation-20261010/objects/8127f36528aa69e0c67b104e6cef109de007a4163ce5085769ff58cf88fa58c0.json:9) |
| 时间 | Utc 明确 Z；BusinessNode 的 DATE 与 INSTANT 分支互斥，保 timezone、calendar、cutoff、nodeRole 和精度。没有日历/业务节点不能自行默认为 UTC 午夜。 | [UTC](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/43/43376e6abeb9fa95__EXC_TYPED_SCHEMA.json:20)；[业务时间节点](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/43/43376e6abeb9fa95__EXC_TYPED_SCHEMA.json:454) |

JSON 采用闭合类型：拒绝 raw duplicate keys、未知字段和缺少 required 字段；只有明示 nullable 才接受 null。Schema 形状通过仍不足以证明状态、范围和 ref 依赖成立，必须执行正文语义规则。原生源正文按各 Owner 自己的 codec/qualification 校验，不能把任意 JSON map 当作合格事实。[解析与语义规则](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/43/43376e6abeb9fa95__EXC_TYPED_SCHEMA.json:26426)。

存储整数显式受 JSON 安全整数域约束（bigint 为 ±9,007,199,254,740,991，int 为 32 位范围），再叠加各表正数/序号 CHECK。nvarchar 的容量是 UTF-16 code units；JSON maxLength 是 Unicode scalar 数，不可相互代替。Reason/Note 的 1,000 scalar 域在声明的相关存储列使用 nvarchar(2000)，过宽应拒绝，不能截断。[存储标量域](D:/CP6-archives/consolidation-20261010/objects/8127f36528aa69e0c67b104e6cef109de007a4163ce5085769ff58cf88fa58c0.json:28599)；[Resolution 存储类型](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/43/43376e6abeb9fa95__EXC_TYPED_SCHEMA.json:13919)；[Resolution 列](D:/CP6-archives/consolidation-20261010/objects/8127f36528aa69e0c67b104e6cef109de007a4163ce5085769ff58cf88fa58c0.json:16697)。

## 2. 事实、私有计算与公共投影分层

这 87 张表全部由 PLAN-EXC 管理本地协调记录，不成为 WMS 库存、PLM 技术、采购订单或 MRP 发布事实的 Owner。SourceFact 保存经过独立签发与资格核验的完整原生事实；SourceProvenance、ProducerQualification、SourceArtifact 分别保存可信来源、资格绑定和原始载体。传输别名 InboxAlias 可以指同一原生事实，不能改写原 issuer。[SourceFact](D:/CP6-archives/consolidation-20261010/objects/8127f36528aa69e0c67b104e6cef109de007a4163ce5085769ff58cf88fa58c0.json:1012)；[ProducerQualification](D:/CP6-archives/consolidation-20261010/objects/8127f36528aa69e0c67b104e6cef109de007a4163ce5085769ff58cf88fa58c0.json:469)；[SourceProvenance](D:/CP6-archives/consolidation-20261010/objects/8127f36528aa69e0c67b104e6cef109de007a4163ce5085769ff58cf88fa58c0.json:744)；[InboxAlias](D:/CP6-archives/consolidation-20261010/objects/8127f36528aa69e0c67b104e6cef109de007a4163ce5085769ff58cf88fa58c0.json:2947)。

| 层 | 核心数据和关系 | 实现含义 |
|---|---|---|
| 原始依赖 | FactDependency、SourceHead、SourceStream、Inbox、WaitingDependency、ProcessingAttempt | 流身份含原 issuer/stream/epoch；已观察高水位不同于已应用连续水位。旧事实回放保历史，不能倒退当前头。[依赖](D:/CP6-archives/consolidation-20261010/objects/8127f36528aa69e0c67b104e6cef109de007a4163ce5085769ff58cf88fa58c0.json:1407)；[流头](D:/CP6-archives/consolidation-20261010/objects/8127f36528aa69e0c67b104e6cef109de007a4163ce5085769ff58cf88fa58c0.json:2011)；[Inbox](D:/CP6-archives/consolidation-20261010/objects/8127f36528aa69e0c67b104e6cef109de007a4163ce5085769ff58cf88fa58c0.json:2424) |
| 问题身份 | CaseOrigin → ExceptionCase → Episode | Origin 保稳定责任身份而非易变版本；Episode 有 Case 内序号、原因事实及可空 prior closure。新一轮责任变化不能覆盖上一轮历史。[Origin](D:/CP6-archives/consolidation-20261010/objects/8127f36528aa69e0c67b104e6cef109de007a4163ce5085769ff58cf88fa58c0.json:3743)；[Episode](D:/CP6-archives/consolidation-20261010/objects/8127f36528aa69e0c67b104e6cef109de007a4163ce5085769ff58cf88fa58c0.json:4289) |
| 完整供需图 | SourceGraph、SourceGraphMember、SourceReadCoverage/Page、DemandSlice、MaterialObligation、SupplySpan、Transition、BeneficiaryShare、Protection | 图保全量闭包；本 Case 的汇总只选择 caseDemandSliceIds/caseMaterialObligationIds，保留边界成员仍进入影响和保护。[私有图](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/43/43376e6abeb9fa95__EXC_TYPED_SCHEMA.json:8158)；[问题子集](D:/CP6-archives/consolidation-20261010/objects/8127f36528aa69e0c67b104e6cef109de007a4163ce5085769ff58cf88fa58c0.json:28538) |
| 评价与当前依据 | Assessment、CurrentManifest/Member、HeadObservation、SourceReadPlan | 评价是不可变事实；“今天是否仍可用”通过新 current/observation 表达。新的观察使用新 Id，不修改旧评价。[Assessment](D:/CP6-archives/consolidation-20261010/objects/8127f36528aa69e0c67b104e6cef109de007a4163ce5085769ff58cf88fa58c0.json:9968)；[HeadObservation](D:/CP6-archives/consolidation-20261010/objects/8127f36528aa69e0c67b104e6cef109de007a4163ce5085769ff58cf88fa58c0.json:10591)；[SourceReadPlan](D:/CP6-archives/consolidation-20261010/objects/8127f36528aa69e0c67b104e6cef109de007a4163ce5085769ff58cf88fa58c0.json:25749) |
| 影响与责任闭环 | ImpactManifest、Node/Edge/Beneficiary、BoundaryProtection、ExecutionObligation、PathCoverage、Settlement | 影响含所有义务、执行路径及不再影响的证据；不能仅靠一个 resolved 标签消掉外部未结责任。[完整影响](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/43/43376e6abeb9fa95__EXC_TYPED_SCHEMA.json:8083)；[执行义务](D:/CP6-archives/consolidation-20261010/objects/8127f36528aa69e0c67b104e6cef109de007a4163ce5085769ff58cf88fa58c0.json:12399)；[结清证据](D:/CP6-archives/consolidation-20261010/objects/8127f36528aa69e0c67b104e6cef109de007a4163ce5085769ff58cf88fa58c0.json:13047) |
| 命令与原回执 | Operation、CommandSlot、CommandReceipt、Checkpoint、Outbox、Audit | 原请求/原调用槽和原结果不可变；工作状态与投递状态可以 CAS 前进。传输完成不等于业务完成。[Operation](D:/CP6-archives/consolidation-20261010/objects/8127f36528aa69e0c67b104e6cef109de007a4163ce5085769ff58cf88fa58c0.json:19705)；[槽](D:/CP6-archives/consolidation-20261010/objects/8127f36528aa69e0c67b104e6cef109de007a4163ce5085769ff58cf88fa58c0.json:20094)；[原回执](D:/CP6-archives/consolidation-20261010/objects/8127f36528aa69e0c67b104e6cef109de007a4163ce5085769ff58cf88fa58c0.json:20517)；[投递状态](D:/CP6-archives/consolidation-20261010/objects/8127f36528aa69e0c67b104e6cef109de007a4163ce5085769ff58cf88fa58c0.json:24376) |
| 结果与读模型 | CaseRevision/Head、Resolution/Closure、AlertDecision/Head、ProjectionRevision/Head、RepairPlan/Receipt | 业务修订、告警静音与投影修复各有身份；修复投影不能伪造源业务结果或改写不可变业务历史。[CaseRevision](D:/CP6-archives/consolidation-20261010/objects/8127f36528aa69e0c67b104e6cef109de007a4163ce5085769ff58cf88fa58c0.json:18770)；[Dismissal](D:/CP6-archives/consolidation-20261010/objects/8127f36528aa69e0c67b104e6cef109de007a4163ce5085769ff58cf88fa58c0.json:17701)；[RepairPlan](D:/CP6-archives/consolidation-20261010/objects/8127f36528aa69e0c67b104e6cef109de007a4163ce5085769ff58cf88fa58c0.json:21742) |
| 导出与历史 | 独立 ExportBundle/Preparation/Operation/Slot/Receipt/Checkpoint；LegacyAnchor/Mapping/Cutover | 导出不塞入单 Case 命令族；旧状态先作为未核快照保真读，不能直接迁成新链路的完成证据。[导出集合](D:/CP6-archives/consolidation-20261010/objects/8127f36528aa69e0c67b104e6cef109de007a4163ce5085769ff58cf88fa58c0.json:26454)；[历史视图](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/43/43376e6abeb9fa95__EXC_TYPED_SCHEMA.json:9066) |

五条状态轴不能合成一个 Status：Knowledge 表示信息是否完整可信；Effectivity 表示当前适用性；BusinessOutcome 表示短缺/延迟/无缺口/责任退役等判断；Lifecycle 表示 Case 生命周期；AlertState 表示告警呈现。LEGACY_UNVERIFIED 不转成正常可信 KPI，DISMISSED 也不等同义务已结清。[Knowledge](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/43/43376e6abeb9fa95__EXC_TYPED_SCHEMA.json:103)；[Effectivity](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/43/43376e6abeb9fa95__EXC_TYPED_SCHEMA.json:113)；[Outcome](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/43/43376e6abeb9fa95__EXC_TYPED_SCHEMA.json:122)；[Lifecycle](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/43/43376e6abeb9fa95__EXC_TYPED_SCHEMA.json:132)；[Alert](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/43/43376e6abeb9fa95__EXC_TYPED_SCHEMA.json:141)。

### 工单物料、供给及分摊

MaterialObligation 必须保源种类、技术 occurrence 与数量依据。WO_MATERIAL_OBLIGATION 与 PLANNED_MATERIAL_DERIVATION 的关系分支不同：工单分支有 workOrder，计划分支有 parentSuggestion；不能靠可空列混造两者。MAT 余量依据与 MRP consumer 的 net/gross 标签各遵循自己的合同。Offset 的“已计入净余量”与“仍受物理保护”是两个独立事实，同一数量不能被重复扣减。[物料责任](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/43/43376e6abeb9fa95__EXC_TYPED_SCHEMA.json:1573)；[MAT 依据](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/43/43376e6abeb9fa95__EXC_TYPED_SCHEMA.json:23723)；[Offset](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/43/43376e6abeb9fa95__EXC_TYPED_SCHEMA.json:1431)；[净额明细](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/43/43376e6abeb9fa95__EXC_TYPED_SCHEMA.json:25161)。

SupplySpan 同时保原范围、CoordinateQuantity 与 evaluated QuantityKnowledge/Quantity：坐标长度已知不代表当前可用量已知。转换必须保真实 Owner 结果、精确范围映射及转换状态。BeneficiaryShare 明确是 DemandSlice 或 MaterialObligation 之一，不能同时填两个受益者，也不能把保留边界分摊计入本问题汇总。[SupplySpan](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/43/43376e6abeb9fa95__EXC_TYPED_SCHEMA.json:1732)；[坐标与数量检查](D:/CP6-archives/consolidation-20261010/objects/8127f36528aa69e0c67b104e6cef109de007a4163ce5085769ff58cf88fa58c0.json:7711)；[Transition](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/43/43376e6abeb9fa95__EXC_TYPED_SCHEMA.json:1852)；[分摊](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/43/43376e6abeb9fa95__EXC_TYPED_SCHEMA.json:1917)；[受益者互斥](D:/CP6-archives/consolidation-20261010/objects/8127f36528aa69e0c67b104e6cef109de007a4163ce5085769ff58cf88fa58c0.json:8705)。

MRP 链路保 preparation/capture/input/run/attempt/generation/seal/result/validate/select/protection/effective/publication/control winner/isolation 的精确身份与依赖；零 suggestion 不代表不存在完整 ResultSet。EXC 本地 consumption 不替 MRP 修改 head。新的 MAKE/BUY suggestion 与 planning method policy 还有独立原生根，不能用旧通用 Output 形状推定同等含义。[MRP 阶段引用](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/43/43376e6abeb9fa95__EXC_TYPED_SCHEMA.json:5363)；[协调回执](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/43/43376e6abeb9fa95__EXC_TYPED_SCHEMA.json:5720)；[方法政策](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/43/43376e6abeb9fa95__EXC_TYPED_SCHEMA.json:25742)；[建议事实](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/43/43376e6abeb9fa95__EXC_TYPED_SCHEMA.json:25914)；[本地消费](D:/CP6-archives/consolidation-20261010/objects/8127f36528aa69e0c67b104e6cef109de007a4163ce5085769ff58cf88fa58c0.json:16416)。

## 3. canonical 和 digest 的域必须逐表固定

| 表或数据域 | BodyCanonical / digest 的精确含义 |
|---|---|
| 普通不可变存储记录（63 表） | Stored<Table>Record 的完整物理记录前像，排除 BodyDigest 与 BodyCanonical，包含 E/T/Id/RecordedAt 和完整 FK 元组；不能拿公共 DTO 做存储根。[通用存储根](D:/CP6-archives/consolidation-20261010/objects/8127f36528aa69e0c67b104e6cef109de007a4163ce5085769ff58cf88fa58c0.json:192) |
| 15 个可变表 | 对实际回读行序列化的非持久 JCS 快照，包含实际 RowVersion；无独立 immutable digest 列。不能创建 BodyCanonical 数据库列/计算列，也不能二次 UPDATE 保存它。[可变快照域](D:/CP6-archives/consolidation-20261010/objects/8127f36528aa69e0c67b104e6cef109de007a4163ce5085769ff58cf88fa58c0.json:1903) |
| ProducerQualification | 完整独立注册者资格绑定；条件式资格样本不是实际 producer registration。[资格根](D:/CP6-archives/consolidation-20261010/objects/8127f36528aa69e0c67b104e6cef109de007a4163ce5085769ff58cf88fa58c0.json:709) |
| SourceProvenance | 完整 SourceTrust，BodyDigest=H(SourceTrust)；ArtifactDigest 是本地 SourceArtifact 记录摘要，不等于原 native raw SHA/canonical digest。[信任根](D:/CP6-archives/consolidation-20261010/objects/8127f36528aa69e0c67b104e6cef109de007a4163ce5085769ff58cf88fa58c0.json:981) |
| SourceFact | 由精确 issuer 合同选定的完整 native 根，BodyDigest=NativeCanonicalDigest=FactRef.bodyDigest；本地 Id/RecordedAt/provenance 不塞进源业务前像。[原生根](D:/CP6-archives/consolidation-20261010/objects/8127f36528aa69e0c67b104e6cef109de007a4163ce5085769ff58cf88fa58c0.json:1369) |
| SourceGraph / Assessment | 私有完整图/评价；图排除自己的 digest，Case/Episode 本地关联不插进业务前像。Assessment 保原维度、数量、knowledge、basis，绝不是当前用户脱敏结果。[图根](D:/CP6-archives/consolidation-20261010/objects/8127f36528aa69e0c67b104e6cef109de007a4163ce5085769ff58cf88fa58c0.json:4778)；[评价根](D:/CP6-archives/consolidation-20261010/objects/8127f36528aa69e0c67b104e6cef109de007a4163ce5085769ff58cf88fa58c0.json:10538) |
| CurrentManifest / ImpactManifest | current 保精确 subject/action/head/cut；impact 保完整节点/边/义务/闭包，排除自己的 impact digest，但包含 closure 自己的精确 digest。[当前依据根](D:/CP6-archives/consolidation-20261010/objects/8127f36528aa69e0c67b104e6cef109de007a4163ce5085769ff58cf88fa58c0.json:9593)；[影响根](D:/CP6-archives/consolidation-20261010/objects/8127f36528aa69e0c67b104e6cef109de007a4163ce5085769ff58cf88fa58c0.json:11215) |
| CommandReceipt | BodyCanonical 保完整原结果，包括 resultDigest；只有计算 BodyDigest=resultDigest 时排除自己的 resultDigest。原响应 raw SHA、本地 Artifact 记录 digest 继续独立。[原回执摘要](D:/CP6-archives/consolidation-20261010/objects/8127f36528aa69e0c67b104e6cef109de007a4163ce5085769ff58cf88fa58c0.json:20745) |
| SourceReadPlan | 完整私有 plan，排除 own digest，保 source.read 目的、current 和 RequiredOwnerReads；公开 handle 不授予读取源事实的权限。[私有读取计划](D:/CP6-archives/consolidation-20261010/objects/8127f36528aa69e0c67b104e6cef109de007a4163ce5085769ff58cf88fa58c0.json:25987) |

以上共有 9 个业务/原生根覆盖项，加普通不可变与可变快照两类。由表上 normativeBodyCanonical 决定实际摘要域；不能全局套一种“序列化整行取 hash”。相同不可变 PK 只有全部正文、digest、FK 元组精确相同才复用；有差异必须冲突，禁止覆盖或用旧 Id 重插新 body。[全局摘要域](D:/CP6-archives/consolidation-20261010/objects/8127f36528aa69e0c67b104e6cef109de007a4163ce5085769ff58cf88fa58c0.json:28540)；[同 PK 精确复用](D:/CP6-archives/consolidation-20261010/objects/8127f36528aa69e0c67b104e6cef109de007a4163ce5085769ff58cf88fa58c0.json:183)。

## 4. 本地事务与 15 个可变域

正常写入必须走同一个 E/T mutator gate 和外层事务。在第一个领域写之前，先检查/保留准确 StoreCommitHead 前驱；新增 StoreCommitRecord 必须先于引用它的 Audit/revision。所有 FK 目标应已存在或在本事务较早阶段插入；不能假定数据库会延迟检查 FK。[即时 FK 写入协议](D:/CP6-archives/consolidation-20261010/objects/8127f36528aa69e0c67b104e6cef109de007a4163ce5085769ff58cf88fa58c0.json:28589)；[提交与 readCut](D:/CP6-archives/consolidation-20261010/objects/8127f36528aa69e0c67b104e6cef109de007a4163ce5085769ff58cf88fa58c0.json:28539)。

可变域只有这 15 个：SourceHead、SourceStream、Inbox、WaitingDependency、DispositionWork、RecalcWork、AlertHead、CaseHead、Operation、ProjectionHead、WriterHead、OutboxDelivery、Export、StoreCommitHead、ExportOperation。每一域独立附件与关系 Schema 内嵌 mutationAndReadbackDomains 精确相等，本轮只做字典及列声明对照。[全部可变域](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d0/d0efd40b8cac81c4__CP03_MUTABLE_WRITE_AND_READBACK_DOMAINS.json:3)。

1. INSERT 仅发送 insertValueColumns；UPDATE 仅发送 updateSetColumns。二者均排除 RowVersion 和 BodyCanonical，UPDATE 另排除 E/T/Id。列表是允许的物理写集，业务身份仍受具体槽、FK、前驱与状态规则约束，不能因列可写就改换原命令身份。[值域与 CAS 域](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d0/d0efd40b8cac81c4__CP03_MUTABLE_WRITE_AND_READBACK_DOMAINS.json:4)；[跨行约束](D:/CP6-archives/consolidation-20261010/objects/8127f36528aa69e0c67b104e6cef109de007a4163ce5085769ff58cf88fa58c0.json:28278)。
2. 用完整预期持久元组以及实际旧 RowVersion/processingRevision/epoch 做 guarded write；数据库返回该语句之后的完整行和新 8 字节 token。不得自行生成 token 或把 complete snapshot 当整行 SET。[Inbox 完整比较域](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d0/d0efd40b8cac81c4__CP03_MUTABLE_WRITE_AND_READBACK_DOMAINS.json:168)。
3. 可信适配器对实际返回行序列化一次，形成非持久 BodyCanonical；后续 CAS 和新不可变记录关联使用真实回读值。序列化/回读失败使整个尚未确认的 UoW 回滚。[四步回读](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d0/d0efd40b8cac81c4__CP03_MUTABLE_WRITE_AND_READBACK_DOMAINS.json:72)；[失败边界](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d0/d0efd40b8cac81c4__CP03_MUTABLE_WRITE_AND_READBACK_DOMAINS.json:78)。
4. 最终精确比较 CaseHead、活跃 WriterHead/lease、fresh Core current 和 StoreCommitHead；affected rows=0 整体回滚。对外响应等外层提交确认后才释放；提交未知按原槽/原回执查询，不能重新做业务。[最终 guard 与提交顺序](D:/CP6-archives/consolidation-20261010/objects/8127f36528aa69e0c67b104e6cef109de007a4163ce5085769ff58cf88fa58c0.json:28589)。
5. Operation/原回执或 ExportOperation/Export 的可空循环必须先插入允许 null 的一端，再建槽/结果/回执并在同一 UoW guarded update 绑定；不能先返回 202 再补齐。[Operation 绑定](D:/CP6-archives/consolidation-20261010/objects/8127f36528aa69e0c67b104e6cef109de007a4163ce5085769ff58cf88fa58c0.json:19966)；[ExportOperation 绑定](D:/CP6-archives/consolidation-20261010/objects/8127f36528aa69e0c67b104e6cef109de007a4163ce5085769ff58cf88fa58c0.json:27283)。

StoreCommitSeq 只描述这个 E/T 本地存储的确认提交顺序；MVCC 首读捕获 committed head，后续 readCut 由它固定。它不是跨 Owner 分布式事务钟。所有 ingress、授权发布、source/current、命令、回执、resume、repair、告警到期写者都要参加同一协议，不能只包住用户按钮处理。[本地读切片](D:/CP6-archives/consolidation-20261010/objects/8127f36528aa69e0c67b104e6cef109de007a4163ce5085769ff58cf88fa58c0.json:28539)；[全部写者](D:/CP6-archives/consolidation-20261010/objects/8127f36528aa69e0c67b104e6cef109de007a4163ce5085769ff58cf88fa58c0.json:28278)。

WriterEpoch 另有两个用途：ExpectedCase.writerEpoch 表示今天活跃 mutator 的 WriterHead；CaseHead.WriterEpoch 是它指向的不可变 Revision 的历史写者。新 writer 为 9、旧 head 历史为 8 可以合法共存，但仍应精确比较旧 head 全元组，新 Revision/Head 才共同写 9。不能把历史 epoch 赋予当前写权限，也不能先改旧 history 使数字相同。[两个 epoch 域](D:/CP6-archives/consolidation-20261010/objects/8127f36528aa69e0c67b104e6cef109de007a4163ce5085769ff58cf88fa58c0.json:28604)。

## 5. 唯一性和完整闭包比单列索引更重要

| 对象 | 身份/验证要求 |
|---|---|
| SourceFact | E/T + Owner + Kind + StableKey + VersionKey 唯一；同身份不同原生摘要冲突。源原始完整键不能只按 hash 判等。[事实唯一性](D:/CP6-archives/consolidation-20261010/objects/8127f36528aa69e0c67b104e6cef109de007a4163ce5085769ff58cf88fa58c0.json:1243)；[键冲突桶](D:/CP6-archives/consolidation-20261010/objects/8127f36528aa69e0c67b104e6cef109de007a4163ce5085769ff58cf88fa58c0.json:183) |
| Sealed Graph/Assessment/Impact/ReadPlan | version 是该不可变 objectId 的正整数标签，不是 Case 的连续计数器；不同 Id 可以同 version。正文变更分配新 Id；禁止 MAX(version) 跨 Id 选当前。[不可变版本轴](D:/CP6-archives/consolidation-20261010/objects/8127f36528aa69e0c67b104e6cef109de007a4163ce5085769ff58cf88fa58c0.json:28607) |
| CurrentManifestMember | Manifest + resource kind/key + SubjectFact + Action 唯一。同一 head 可有不同 subject 的多个成员；同一个完整身份出现两 head 应拒绝，不能把 HeadFactId 加入键隐藏冲突。[成员唯一性](D:/CP6-archives/consolidation-20261010/objects/8127f36528aa69e0c67b104e6cef109de007a4163ce5085769ff58cf88fa58c0.json:9813)；[成员语义](D:/CP6-archives/consolidation-20261010/objects/8127f36528aa69e0c67b104e6cef109de007a4163ce5085769ff58cf88fa58c0.json:28608) |
| CaseRevision / Head | 外键绑定同 E/T/Case/Episode 的 Assessment、Impact、AlertDecision、Observation、Closure；Head 的 businessRevision/writer/epoch/commitSeq 必须等于所指完整 Revision。[11 组精确绑定](D:/CP6-archives/consolidation-20261010/objects/8127f36528aa69e0c67b104e6cef109de007a4163ce5085769ff58cf88fa58c0.json:28294)；[CaseHead](D:/CP6-archives/consolidation-20261010/objects/8127f36528aa69e0c67b104e6cef109de007a4163ce5085769ff58cf88fa58c0.json:19364) |
| CommandSlot | 按 Case/Episode/Kind/RequestKey 定槽，actor、commandId、body hash 不另造槽；冲突判断仍比较原完整请求。OperationKind 不含 EXPORT。[普通槽身份](D:/CP6-archives/consolidation-20261010/objects/8127f36528aa69e0c67b104e6cef109de007a4163ce5085769ff58cf88fa58c0.json:20325)；[普通命令族](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/43/43376e6abeb9fa95__EXC_TYPED_SCHEMA.json:149) |
| ResultConsumption | 同 work + MRP publication receipt 只消费一次，不能把重试当新源发布。[消费唯一性](D:/CP6-archives/consolidation-20261010/objects/8127f36528aa69e0c67b104e6cef109de007a4163ce5085769ff58cf88fa58c0.json:16567) |
| DispositionReceiptObligation | 同 Receipt/Obligation/Relation 唯一；accepted 与 settled 可分别记录累计关系，settled 必须是 accepted 子集并与 unresolved 区分。[累计责任集合](D:/CP6-archives/consolidation-20261010/objects/8127f36528aa69e0c67b104e6cef109de007a4163ce5085769ff58cf88fa58c0.json:14597) |
| Outbox | TargetOwner/EffectKey 决定业务 effect，payloadDigest 不应加入键以放行同 effect 的不同载荷；OutboxDelivery 只是传输状态。[effect 唯一性](D:/CP6-archives/consolidation-20261010/objects/8127f36528aa69e0c67b104e6cef109de007a4163ce5085769ff58cf88fa58c0.json:24212)；[投递](D:/CP6-archives/consolidation-20261010/objects/8127f36528aa69e0c67b104e6cef109de007a4163ce5085769ff58cf88fa58c0.json:24376) |

范围不重叠和完整覆盖、图 member 的完整集合、当前保证双向相等、影响边端点属于同一图等约束必须在相应 gate 内比较；单个 UNIQUE、Id/Digest 外键或闭合 JSON 不能替代这些集合校验。SourceReadPage/SourceChange 的 factDocuments 要精确等于可达 memberRefs，每件带完整 native body 和独立 issuer/pin；缺失时 RequiredFenced，不能用空数组表示完整。[跨行规则](D:/CP6-archives/consolidation-20261010/objects/8127f36528aa69e0c67b104e6cef109de007a4163ce5085769ff58cf88fa58c0.json:28278)；[源页双向集合相等](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/43/43376e6abeb9fa95__EXC_TYPED_SCHEMA.json:26426)。

## 6. 权限、页面和导出

公开页面使用 disclosure 判别联合：VISIBLE 带完整获准值；REDACTED 仅带声明的原因。不能额外泄露被隐藏成员计数、数量、稳定 hash 或由隐藏维度推导的分组键。公共 QuantityCell 的 uomCode 与 basis 披露分别受控，不能塞回私有 UOM FactRef。私有评价、图和 impact 的 digest 保持原值，不能因用户权限变化重新计算成另一个业务事实。[脱敏分支](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/43/43376e6abeb9fa95__EXC_TYPED_SCHEMA.json:1049)；[公开数量](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/43/43376e6abeb9fa95__EXC_TYPED_SCHEMA.json:17162)；[私有摘要披露](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/43/43376e6abeb9fa95__EXC_TYPED_SCHEMA.json:10127)；[投影与私有摘要](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/43/43376e6abeb9fa95__EXC_TYPED_SCHEMA.json:26426)。

列表、来源页、影响节点/边页、历史回执和 recovery 页各有闭合 DTO，不得透传存储行。LegacyReadView 独立于正式 Case；没有新 Case/Episode 时仍能保真读取已获准历史快照，不得补造新身份。动作准备必须固定 ExpectedCase、current、权限目的与原输入；恢复继续同一槽和 checkpoint 的 next action，不把“查原结果”变成一次新 effect。[列表](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/43/43376e6abeb9fa95__EXC_TYPED_SCHEMA.json:4043)；[来源页](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/43/43376e6abeb9fa95__EXC_TYPED_SCHEMA.json:4098)；[恢复页](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/43/43376e6abeb9fa95__EXC_TYPED_SCHEMA.json:4628)；[旧记录页](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/43/43376e6abeb9fa95__EXC_TYPED_SCHEMA.json:9066)；[动作准备](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/43/43376e6abeb9fa95__EXC_TYPED_SCHEMA.json:8638)；[恢复命令](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/43/43376e6abeb9fa95__EXC_TYPED_SCHEMA.json:3358)。

导出采用独立集合身份：同 E/T 内按规范排序的 CaseId 集合，排除 actor、episode、readCut、列掩码和 request digest；哈希碰撞仍比较完整集合字节。Preparation 固定每个 CaseRevision/Assessment、readCut 与逐成员 fresh export-purpose authority，再创建 bundle-scoped 原槽、独立 Operation 和回执。普通 Case Operation/Slot 必须保持非空 Case/Episode，不能塞 EXPORT。[ExportBundle](D:/CP6-archives/consolidation-20261010/objects/8127f36528aa69e0c67b104e6cef109de007a4163ce5085769ff58cf88fa58c0.json:26454)；[Preparation](D:/CP6-archives/consolidation-20261010/objects/8127f36528aa69e0c67b104e6cef109de007a4163ce5085769ff58cf88fa58c0.json:26600)；[逐成员权限](D:/CP6-archives/consolidation-20261010/objects/8127f36528aa69e0c67b104e6cef109de007a4163ce5085769ff58cf88fa58c0.json:26819)；[导出槽](D:/CP6-archives/consolidation-20261010/objects/8127f36528aa69e0c67b104e6cef109de007a4163ce5085769ff58cf88fa58c0.json:27379)；[导出独立族](D:/CP6-archives/consolidation-20261010/objects/8127f36528aa69e0c67b104e6cef109de007a4163ce5085769ff58cf88fa58c0.json:28278)。

EX18 注册的原始结果（202 或 422）不可变；重放返回同一原结果正文并按合同表示 replay，当前进度另由 EX19 查询。UNKNOWN generation 不能推论没有文件；必须捕获 File 原件。UI 的当前进度、原命令 receipt 和私有文件结果是三个不同对象。[原结果](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/43/43376e6abeb9fa95__EXC_TYPED_SCHEMA.json:9694)；[导出运行视图](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/43/43376e6abeb9fa95__EXC_TYPED_SCHEMA.json:9756)；[文件视图](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/43/43376e6abeb9fa95__EXC_TYPED_SCHEMA.json:4660)；[导出 checkpoint](D:/CP6-archives/consolidation-20261010/objects/8127f36528aa69e0c67b104e6cef109de007a4163ce5085769ff58cf88fa58c0.json:28015)。

## 7. 正常写集入口及阅读边界

EXC_RELATIONAL_NORMAL_WRITESETS.json 是 25 个精确位置的索引，不是完整执行台账。它指向 24 个 CP03_LOCAL_UOW_LEDGER_INDEX packets 和 1 个 CP03_AC_DIFFERENCE_SPECIMENS 的 Legacy 例。本册已读全部索引语义与顺序要求；不把链接解析或文件存在等同那些大对象正文已经由本册阅读。那些原始 rows/old-new snapshots/conditionalWrites 的语义覆盖由主册关联阅读记录独立核定。[完整写集索引](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/a8/a8ea3acc71e58ef7__EXC_RELATIONAL_NORMAL_WRITESETS.json:1)。

| 入口组 | 编码时必须顺着索引追到的内容 |
|---|---|
| resolve / sharedResolve | 普通及共享责任的完整本地提交，含 M04/current 与 epoch 9。[resolve](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/a8/a8ea3acc71e58ef7__EXC_RELATIONAL_NORMAL_WRITESETS.json:6)；[sharedResolve](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/a8/a8ea3acc71e58ef7__EXC_RELATIONAL_NORMAL_WRITESETS.json:11) |
| coldRecovery（4） | 原调用可能已发、过期 writer8 换9、只读查原未观察/找到；不凭超时重放 effect。[冷恢复](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/a8/a8ea3acc71e58ef7__EXC_RELATIONAL_NORMAL_WRITESETS.json:16) |
| sameInputRecovery（3） | 注册、capture G2、晚到 G1 诊断，原输入与新 generation 的身份不能混用。[同输入恢复](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/a8/a8ea3acc71e58ef7__EXC_RELATIONAL_NORMAL_WRITESETS.json:38) |
| localProjectionRepair（4） | 真实业务 consumption 与本地投影失败边界、prepare、repair 各自完整写集。[投影修复](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/a8/a8ea3acc71e58ef7__EXC_RELATIONAL_NORMAL_WRITESETS.json:55) |
| exportMultiple（5） | 多 Case prepare/register/可能生成/捕获原文件/replay 与拒绝。[多 Case 导出](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/a8/a8ea3acc71e58ef7__EXC_RELATIONAL_NORMAL_WRITESETS.json:77) |
| ingressCurrent（5） | sequence2 收取、晚到1仅历史、本地应用可重试失败、应用原2、原生 alias。[入站与当前](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/a8/a8ea3acc71e58ef7__EXC_RELATIONAL_NORMAL_WRITESETS.json:104) |
| epochHead / publicLegacy | epoch9 非终态头完整元组、旧记录保真成功例。[epoch](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/a8/a8ea3acc71e58ef7__EXC_RELATIONAL_NORMAL_WRITESETS.json:131)；[Legacy](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/a8/a8ea3acc71e58ef7__EXC_RELATIONAL_NORMAL_WRITESETS.json:136) |

每个 conditionalWrites 数组是一笔有序完整 UoW：StoreCommit root 先于依赖 Audit/revision，不可变 PK 只复用精确 body，可空环在同一 UoW guarded update，可变旧/新快照完整，最终 StoreCommitHead CAS 最后。索引明确 candidateSqlExecuted=false、candidateCodeExecuted=false、authorPASS=false；本次也未执行。[写集完整性](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/a8/a8ea3acc71e58ef7__EXC_RELATIONAL_NORMAL_WRITESETS.json:141)；[未运行声明](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/a8/a8ea3acc71e58ef7__EXC_RELATIONAL_NORMAL_WRITESETS.json:142)。

## 8. 实施顺序、待决适配与验收场景

建议先完成闭合 codec/opaque key/数量/8 字节 token 与各 digest profile，再实现 E/T 仓储和即时 FK 写集规划，再接全部 mutable 的真实回读协议、StoreCommit gate、原槽回执及 readCut，最后接 current/source qualification、公开投影与导出。这是由上述依赖整理的开发顺序，不是新的 Owner 采用或业务授权。当前 provider、权限注册、生产数据库适配与实际执行证据仍按主册门禁确认。[剩余采用边界](D:/CP6-archives/consolidation-20261010/objects/8127f36528aa69e0c67b104e6cef109de007a4163ce5085769ff58cf88fa58c0.json:28605)。

**PLAN-IF-006：Legacy 数量类型出现静态收窄，待 Owner 决定。** LegacyAnchor.reportedRequiredQty/reportedAvailableQty 使用 SignedDecimal（允许负号），并要求保留原值；StoredLegacyAnchorRecord 对应两列却使用 DecimalQty→非负 Decimal。关系列为 decimal(21,8)，本表未另列 signed legacy 例外。若原值为负，wire 可以表达，声明的 Stored codec 却不能无损接纳。此处只证明类型域不同，未证明实际历史数据有负值，也未证明运行故障；禁止自行 clamp/abs/补 0。退出条件是 Owner 明确 signed 历史值的存储/拒绝与保真呈现合同，使原件、typed stored 根、关系映射及相应边界样例一致，并另行获得实施证据。[wire required qty](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/43/43376e6abeb9fa95__EXC_TYPED_SCHEMA.json:3642)；[wire available qty](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/43/43376e6abeb9fa95__EXC_TYPED_SCHEMA.json:3645)；[带符号域](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/43/43376e6abeb9fa95__EXC_TYPED_SCHEMA.json:44)；[原值保留](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/43/43376e6abeb9fa95__EXC_TYPED_SCHEMA.json:3737)；[Stored required qty](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/43/43376e6abeb9fa95__EXC_TYPED_SCHEMA.json:22313)；[Stored available qty](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/43/43376e6abeb9fa95__EXC_TYPED_SCHEMA.json:22316)；[非负别名](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/43/43376e6abeb9fa95__EXC_TYPED_SCHEMA.json:23080)；[关系数量列](D:/CP6-archives/consolidation-20261010/objects/8127f36528aa69e0c67b104e6cef109de007a4163ce5085769ff58cf88fa58c0.json:22411)。

后续采用后的验收至少覆盖以下语义；本册未运行这些场景：

- 原始键 hash 相同而 bytes 不同、同源版本不同 body、跨 E/T 或跨 Case/Episode 的 Id/Digest 引用拒绝。
- 可变 full snapshot 不作为 SQL SET；真实 rowversion 回读后序列化失败整笔回滚；提交未知只查原槽。
- 同 immutable Id 不同 version/body 冲突，不同 Id 同 version 合法；epoch9 写者精确比较 epoch8 历史 head。
- 不同 subject 共用 head 的 current 完整集合、同 subject/action 双 head 冲突；源页缺一 native body 不得标 COMPLETE。
- UNKNOWN 数量不补 0；净余量已含 offset 与仍受保护不重复扣减；保留边界不计入本 Case 汇总。
- 权限变化只影响公共 disclosure；私有 digest 和原 receipt 不变化；多 Case 导出不能用首个成员权限代表全部成员。
- Legacy signed 原值的 Owner 决策需有正负/零边界例，不能拿本册静态发现充当通过。

上述验收是来源约束的整理，按前文相应类型、约束及写集引用追溯；Schema/SQL/业务运行状态保持 NOT_RUN/UNPROVEN。

## 9. 全部表的开发定位

下表完整列出 87 个关系声明，便于从职责层进入精确字段、PK/UQ/FK/check/profile；详细字段语义见前文，完整定义不可由本表的列数替代。

| 表 | 模式 | 列声明数 | FK 数 | 完整定义 |
|---|---|---:|---:|---|
| KeyIdentity | IMMUTABLE | 12 | 0 | [/tables/0](D:/CP6-archives/consolidation-20261010/objects/8127f36528aa69e0c67b104e6cef109de007a4163ce5085769ff58cf88fa58c0.json:23) |
| SourceArtifact | IMMUTABLE | 17 | 0 | [/tables/1](D:/CP6-archives/consolidation-20261010/objects/8127f36528aa69e0c67b104e6cef109de007a4163ce5085769ff58cf88fa58c0.json:223) |
| ProducerQualification | IMMUTABLE | 19 | 1 | [/tables/2](D:/CP6-archives/consolidation-20261010/objects/8127f36528aa69e0c67b104e6cef109de007a4163ce5085769ff58cf88fa58c0.json:469) |
| SourceProvenance | IMMUTABLE | 15 | 3 | [/tables/3](D:/CP6-archives/consolidation-20261010/objects/8127f36528aa69e0c67b104e6cef109de007a4163ce5085769ff58cf88fa58c0.json:744) |
| SourceFact | IMMUTABLE | 22 | 5 | [/tables/4](D:/CP6-archives/consolidation-20261010/objects/8127f36528aa69e0c67b104e6cef109de007a4163ce5085769ff58cf88fa58c0.json:1012) |
| FactDependency | IMMUTABLE | 13 | 3 | [/tables/5](D:/CP6-archives/consolidation-20261010/objects/8127f36528aa69e0c67b104e6cef109de007a4163ce5085769ff58cf88fa58c0.json:1407) |
| SourceHead | CAS | 15 | 3 | [/tables/6](D:/CP6-archives/consolidation-20261010/objects/8127f36528aa69e0c67b104e6cef109de007a4163ce5085769ff58cf88fa58c0.json:1662) |
| SourceStream | CAS | 18 | 4 | [/tables/7](D:/CP6-archives/consolidation-20261010/objects/8127f36528aa69e0c67b104e6cef109de007a4163ce5085769ff58cf88fa58c0.json:2011) |
| Inbox | CAS | 25 | 4 | [/tables/8](D:/CP6-archives/consolidation-20261010/objects/8127f36528aa69e0c67b104e6cef109de007a4163ce5085769ff58cf88fa58c0.json:2424) |
| InboxAlias | IMMUTABLE | 11 | 2 | [/tables/9](D:/CP6-archives/consolidation-20261010/objects/8127f36528aa69e0c67b104e6cef109de007a4163ce5085769ff58cf88fa58c0.json:2947) |
| WaitingDependency | CAS | 15 | 4 | [/tables/10](D:/CP6-archives/consolidation-20261010/objects/8127f36528aa69e0c67b104e6cef109de007a4163ce5085769ff58cf88fa58c0.json:3158) |
| ProcessingAttempt | IMMUTABLE | 13 | 1 | [/tables/11](D:/CP6-archives/consolidation-20261010/objects/8127f36528aa69e0c67b104e6cef109de007a4163ce5085769ff58cf88fa58c0.json:3527) |
| CaseOrigin | IMMUTABLE | 17 | 5 | [/tables/12](D:/CP6-archives/consolidation-20261010/objects/8127f36528aa69e0c67b104e6cef109de007a4163ce5085769ff58cf88fa58c0.json:3743) |
| ExceptionCase | IMMUTABLE | 11 | 2 | [/tables/13](D:/CP6-archives/consolidation-20261010/objects/8127f36528aa69e0c67b104e6cef109de007a4163ce5085769ff58cf88fa58c0.json:4077) |
| Episode | IMMUTABLE | 14 | 3 | [/tables/14](D:/CP6-archives/consolidation-20261010/objects/8127f36528aa69e0c67b104e6cef109de007a4163ce5085769ff58cf88fa58c0.json:4289) |
| SourceGraph | IMMUTABLE | 14 | 2 | [/tables/15](D:/CP6-archives/consolidation-20261010/objects/8127f36528aa69e0c67b104e6cef109de007a4163ce5085769ff58cf88fa58c0.json:4554) |
| SourceGraphMember | IMMUTABLE | 12 | 2 | [/tables/16](D:/CP6-archives/consolidation-20261010/objects/8127f36528aa69e0c67b104e6cef109de007a4163ce5085769ff58cf88fa58c0.json:4811) |
| SourceReadCoverage | IMMUTABLE | 22 | 5 | [/tables/17](D:/CP6-archives/consolidation-20261010/objects/8127f36528aa69e0c67b104e6cef109de007a4163ce5085769ff58cf88fa58c0.json:5042) |
| SourceReadPage | IMMUTABLE | 16 | 4 | [/tables/18](D:/CP6-archives/consolidation-20261010/objects/8127f36528aa69e0c67b104e6cef109de007a4163ce5085769ff58cf88fa58c0.json:5431) |
| DemandSlice | IMMUTABLE | 26 | 8 | [/tables/19](D:/CP6-archives/consolidation-20261010/objects/8127f36528aa69e0c67b104e6cef109de007a4163ce5085769ff58cf88fa58c0.json:5736) |
| MaterialObligation | IMMUTABLE | 28 | 8 | [/tables/20](D:/CP6-archives/consolidation-20261010/objects/8127f36528aa69e0c67b104e6cef109de007a4163ce5085769ff58cf88fa58c0.json:6215) |
| MaterialOffset | IMMUTABLE | 18 | 4 | [/tables/21](D:/CP6-archives/consolidation-20261010/objects/8127f36528aa69e0c67b104e6cef109de007a4163ce5085769ff58cf88fa58c0.json:6714) |
| SupplyRoot | IMMUTABLE | 13 | 3 | [/tables/22](D:/CP6-archives/consolidation-20261010/objects/8127f36528aa69e0c67b104e6cef109de007a4163ce5085769ff58cf88fa58c0.json:7033) |
| SupplySpan | IMMUTABLE | 28 | 7 | [/tables/23](D:/CP6-archives/consolidation-20261010/objects/8127f36528aa69e0c67b104e6cef109de007a4163ce5085769ff58cf88fa58c0.json:7285) |
| SupplyTransition | IMMUTABLE | 13 | 3 | [/tables/24](D:/CP6-archives/consolidation-20261010/objects/8127f36528aa69e0c67b104e6cef109de007a4163ce5085769ff58cf88fa58c0.json:7770) |
| SupplyTransitionMapping | IMMUTABLE | 18 | 4 | [/tables/25](D:/CP6-archives/consolidation-20261010/objects/8127f36528aa69e0c67b104e6cef109de007a4163ce5085769ff58cf88fa58c0.json:8017) |
| BeneficiaryShare | IMMUTABLE | 24 | 6 | [/tables/26](D:/CP6-archives/consolidation-20261010/objects/8127f36528aa69e0c67b104e6cef109de007a4163ce5085769ff58cf88fa58c0.json:8336) |
| EligibilityCondition | IMMUTABLE | 20 | 4 | [/tables/27](D:/CP6-archives/consolidation-20261010/objects/8127f36528aa69e0c67b104e6cef109de007a4163ce5085769ff58cf88fa58c0.json:8756) |
| Protection | IMMUTABLE | 21 | 4 | [/tables/28](D:/CP6-archives/consolidation-20261010/objects/8127f36528aa69e0c67b104e6cef109de007a4163ce5085769ff58cf88fa58c0.json:9097) |
| CurrentManifest | IMMUTABLE | 11 | 0 | [/tables/29](D:/CP6-archives/consolidation-20261010/objects/8127f36528aa69e0c67b104e6cef109de007a4163ce5085769ff58cf88fa58c0.json:9451) |
| CurrentManifestMember | IMMUTABLE | 18 | 5 | [/tables/30](D:/CP6-archives/consolidation-20261010/objects/8127f36528aa69e0c67b104e6cef109de007a4163ce5085769ff58cf88fa58c0.json:9622) |
| Assessment | IMMUTABLE | 36 | 9 | [/tables/31](D:/CP6-archives/consolidation-20261010/objects/8127f36528aa69e0c67b104e6cef109de007a4163ce5085769ff58cf88fa58c0.json:9968) |
| HeadObservation | IMMUTABLE | 16 | 4 | [/tables/32](D:/CP6-archives/consolidation-20261010/objects/8127f36528aa69e0c67b104e6cef109de007a4163ce5085769ff58cf88fa58c0.json:10591) |
| ImpactManifest | IMMUTABLE | 18 | 5 | [/tables/33](D:/CP6-archives/consolidation-20261010/objects/8127f36528aa69e0c67b104e6cef109de007a4163ce5085769ff58cf88fa58c0.json:10896) |
| ImpactNode | IMMUTABLE | 15 | 2 | [/tables/34](D:/CP6-archives/consolidation-20261010/objects/8127f36528aa69e0c67b104e6cef109de007a4163ce5085769ff58cf88fa58c0.json:11252) |
| ImpactEdge | IMMUTABLE | 17 | 5 | [/tables/35](D:/CP6-archives/consolidation-20261010/objects/8127f36528aa69e0c67b104e6cef109de007a4163ce5085769ff58cf88fa58c0.json:11503) |
| ImpactBeneficiary | IMMUTABLE | 14 | 4 | [/tables/36](D:/CP6-archives/consolidation-20261010/objects/8127f36528aa69e0c67b104e6cef109de007a4163ce5085769ff58cf88fa58c0.json:11837) |
| BoundaryProtection | IMMUTABLE | 14 | 4 | [/tables/37](D:/CP6-archives/consolidation-20261010/objects/8127f36528aa69e0c67b104e6cef109de007a4163ce5085769ff58cf88fa58c0.json:12117) |
| ExecutionObligation | IMMUTABLE | 18 | 3 | [/tables/38](D:/CP6-archives/consolidation-20261010/objects/8127f36528aa69e0c67b104e6cef109de007a4163ce5085769ff58cf88fa58c0.json:12399) |
| ExecutionPathCoverage | IMMUTABLE | 18 | 5 | [/tables/39](D:/CP6-archives/consolidation-20261010/objects/8127f36528aa69e0c67b104e6cef109de007a4163ce5085769ff58cf88fa58c0.json:12702) |
| ObligationSettlement | IMMUTABLE | 15 | 4 | [/tables/40](D:/CP6-archives/consolidation-20261010/objects/8127f36528aa69e0c67b104e6cef109de007a4163ce5085769ff58cf88fa58c0.json:13047) |
| DispositionWork | CAS | 22 | 8 | [/tables/41](D:/CP6-archives/consolidation-20261010/objects/8127f36528aa69e0c67b104e6cef109de007a4163ce5085769ff58cf88fa58c0.json:13334) |
| DispositionRequestObligation | IMMUTABLE | 9 | 2 | [/tables/42](D:/CP6-archives/consolidation-20261010/objects/8127f36528aa69e0c67b104e6cef109de007a4163ce5085769ff58cf88fa58c0.json:13871) |
| DispositionReceipt | IMMUTABLE | 18 | 5 | [/tables/43](D:/CP6-archives/consolidation-20261010/objects/8127f36528aa69e0c67b104e6cef109de007a4163ce5085769ff58cf88fa58c0.json:14060) |
| DispositionReceiptObligation | IMMUTABLE | 13 | 2 | [/tables/44](D:/CP6-archives/consolidation-20261010/objects/8127f36528aa69e0c67b104e6cef109de007a4163ce5085769ff58cf88fa58c0.json:14399) |
| RecalcWork | CAS | 22 | 8 | [/tables/45](D:/CP6-archives/consolidation-20261010/objects/8127f36528aa69e0c67b104e6cef109de007a4163ce5085769ff58cf88fa58c0.json:14637) |
| RecalcStep | IMMUTABLE | 17 | 5 | [/tables/46](D:/CP6-archives/consolidation-20261010/objects/8127f36528aa69e0c67b104e6cef109de007a4163ce5085769ff58cf88fa58c0.json:15175) |
| MrpReceipt | IMMUTABLE | 30 | 12 | [/tables/47](D:/CP6-archives/consolidation-20261010/objects/8127f36528aa69e0c67b104e6cef109de007a4163ce5085769ff58cf88fa58c0.json:15508) |
| MrpResultMember | IMMUTABLE | 16 | 4 | [/tables/48](D:/CP6-archives/consolidation-20261010/objects/8127f36528aa69e0c67b104e6cef109de007a4163ce5085769ff58cf88fa58c0.json:16106) |
| ResultConsumption | IMMUTABLE | 14 | 4 | [/tables/49](D:/CP6-archives/consolidation-20261010/objects/8127f36528aa69e0c67b104e6cef109de007a4163ce5085769ff58cf88fa58c0.json:16416) |
| Resolution | IMMUTABLE | 22 | 9 | [/tables/50](D:/CP6-archives/consolidation-20261010/objects/8127f36528aa69e0c67b104e6cef109de007a4163ce5085769ff58cf88fa58c0.json:16697) |
| ResolutionEvidence | IMMUTABLE | 11 | 2 | [/tables/51](D:/CP6-archives/consolidation-20261010/objects/8127f36528aa69e0c67b104e6cef109de007a4163ce5085769ff58cf88fa58c0.json:17165) |
| CaseClosure | IMMUTABLE | 15 | 5 | [/tables/52](D:/CP6-archives/consolidation-20261010/objects/8127f36528aa69e0c67b104e6cef109de007a4163ce5085769ff58cf88fa58c0.json:17380) |
| Dismissal | IMMUTABLE | 20 | 6 | [/tables/53](D:/CP6-archives/consolidation-20261010/objects/8127f36528aa69e0c67b104e6cef109de007a4163ce5085769ff58cf88fa58c0.json:17701) |
| AlertDecision | IMMUTABLE | 19 | 6 | [/tables/54](D:/CP6-archives/consolidation-20261010/objects/8127f36528aa69e0c67b104e6cef109de007a4163ce5085769ff58cf88fa58c0.json:18082) |
| AlertHead | CAS | 13 | 3 | [/tables/55](D:/CP6-archives/consolidation-20261010/objects/8127f36528aa69e0c67b104e6cef109de007a4163ce5085769ff58cf88fa58c0.json:18455) |
| CaseRevision | IMMUTABLE | 25 | 13 | [/tables/56](D:/CP6-archives/consolidation-20261010/objects/8127f36528aa69e0c67b104e6cef109de007a4163ce5085769ff58cf88fa58c0.json:18770) |
| CaseHead | CAS | 14 | 3 | [/tables/57](D:/CP6-archives/consolidation-20261010/objects/8127f36528aa69e0c67b104e6cef109de007a4163ce5085769ff58cf88fa58c0.json:19364) |
| Operation | CAS | 17 | 4 | [/tables/58](D:/CP6-archives/consolidation-20261010/objects/8127f36528aa69e0c67b104e6cef109de007a4163ce5085769ff58cf88fa58c0.json:19705) |
| CommandSlot | IMMUTABLE | 22 | 7 | [/tables/59](D:/CP6-archives/consolidation-20261010/objects/8127f36528aa69e0c67b104e6cef109de007a4163ce5085769ff58cf88fa58c0.json:20094) |
| CommandReceipt | IMMUTABLE | 14 | 3 | [/tables/60](D:/CP6-archives/consolidation-20261010/objects/8127f36528aa69e0c67b104e6cef109de007a4163ce5085769ff58cf88fa58c0.json:20517) |
| RecoveryCheckpoint | IMMUTABLE | 21 | 5 | [/tables/61](D:/CP6-archives/consolidation-20261010/objects/8127f36528aa69e0c67b104e6cef109de007a4163ce5085769ff58cf88fa58c0.json:20777) |
| ProjectionRevision | IMMUTABLE | 16 | 4 | [/tables/62](D:/CP6-archives/consolidation-20261010/objects/8127f36528aa69e0c67b104e6cef109de007a4163ce5085769ff58cf88fa58c0.json:21154) |
| ProjectionHead | CAS | 12 | 2 | [/tables/63](D:/CP6-archives/consolidation-20261010/objects/8127f36528aa69e0c67b104e6cef109de007a4163ce5085769ff58cf88fa58c0.json:21458) |
| RepairPlan | IMMUTABLE | 15 | 4 | [/tables/64](D:/CP6-archives/consolidation-20261010/objects/8127f36528aa69e0c67b104e6cef109de007a4163ce5085769ff58cf88fa58c0.json:21742) |
| RepairReceipt | IMMUTABLE | 11 | 3 | [/tables/65](D:/CP6-archives/consolidation-20261010/objects/8127f36528aa69e0c67b104e6cef109de007a4163ce5085769ff58cf88fa58c0.json:22028) |
| LegacyAnchor | IMMUTABLE | 21 | 1 | [/tables/66](D:/CP6-archives/consolidation-20261010/objects/8127f36528aa69e0c67b104e6cef109de007a4163ce5085769ff58cf88fa58c0.json:22256) |
| LegacyMappingDecision | IMMUTABLE | 18 | 5 | [/tables/67](D:/CP6-archives/consolidation-20261010/objects/8127f36528aa69e0c67b104e6cef109de007a4163ce5085769ff58cf88fa58c0.json:22562) |
| CutoverScope | IMMUTABLE | 16 | 3 | [/tables/68](D:/CP6-archives/consolidation-20261010/objects/8127f36528aa69e0c67b104e6cef109de007a4163ce5085769ff58cf88fa58c0.json:22900) |
| WriterLeaseHistory | IMMUTABLE | 13 | 1 | [/tables/69](D:/CP6-archives/consolidation-20261010/objects/8127f36528aa69e0c67b104e6cef109de007a4163ce5085769ff58cf88fa58c0.json:23180) |
| WriterHead | CAS | 13 | 1 | [/tables/70](D:/CP6-archives/consolidation-20261010/objects/8127f36528aa69e0c67b104e6cef109de007a4163ce5085769ff58cf88fa58c0.json:23402) |
| PermissionObservation | IMMUTABLE | 19 | 5 | [/tables/71](D:/CP6-archives/consolidation-20261010/objects/8127f36528aa69e0c67b104e6cef109de007a4163ce5085769ff58cf88fa58c0.json:23684) |
| Outbox | IMMUTABLE | 17 | 6 | [/tables/72](D:/CP6-archives/consolidation-20261010/objects/8127f36528aa69e0c67b104e6cef109de007a4163ce5085769ff58cf88fa58c0.json:24031) |
| OutboxDelivery | CAS | 15 | 1 | [/tables/73](D:/CP6-archives/consolidation-20261010/objects/8127f36528aa69e0c67b104e6cef109de007a4163ce5085769ff58cf88fa58c0.json:24376) |
| Audit | IMMUTABLE | 23 | 8 | [/tables/74](D:/CP6-archives/consolidation-20261010/objects/8127f36528aa69e0c67b104e6cef109de007a4163ce5085769ff58cf88fa58c0.json:24688) |
| Export | CAS | 18 | 4 | [/tables/75](D:/CP6-archives/consolidation-20261010/objects/8127f36528aa69e0c67b104e6cef109de007a4163ce5085769ff58cf88fa58c0.json:25135) |
| ExportRowRef | IMMUTABLE | 10 | 2 | [/tables/76](D:/CP6-archives/consolidation-20261010/objects/8127f36528aa69e0c67b104e6cef109de007a4163ce5085769ff58cf88fa58c0.json:25542) |
| SourceReadPlan | IMMUTABLE | 14 | 3 | [/tables/77](D:/CP6-archives/consolidation-20261010/objects/8127f36528aa69e0c67b104e6cef109de007a4163ce5085769ff58cf88fa58c0.json:25749) |
| StoreCommitRecord | IMMUTABLE | 12 | 1 | [/tables/78](D:/CP6-archives/consolidation-20261010/objects/8127f36528aa69e0c67b104e6cef109de007a4163ce5085769ff58cf88fa58c0.json:26020) |
| StoreCommitHead | CAS | 9 | 1 | [/tables/79](D:/CP6-archives/consolidation-20261010/objects/8127f36528aa69e0c67b104e6cef109de007a4163ce5085769ff58cf88fa58c0.json:26237) |
| ExportBundle | IMMUTABLE | 8 | 0 | [/tables/80](D:/CP6-archives/consolidation-20261010/objects/8127f36528aa69e0c67b104e6cef109de007a4163ce5085769ff58cf88fa58c0.json:26454) |
| ExportPreparation | IMMUTABLE | 12 | 2 | [/tables/81](D:/CP6-archives/consolidation-20261010/objects/8127f36528aa69e0c67b104e6cef109de007a4163ce5085769ff58cf88fa58c0.json:26600) |
| ExportPreparationMember | IMMUTABLE | 15 | 4 | [/tables/82](D:/CP6-archives/consolidation-20261010/objects/8127f36528aa69e0c67b104e6cef109de007a4163ce5085769ff58cf88fa58c0.json:26819) |
| ExportOperation | CAS_MUTABLE | 11 | 2 | [/tables/83](D:/CP6-archives/consolidation-20261010/objects/8127f36528aa69e0c67b104e6cef109de007a4163ce5085769ff58cf88fa58c0.json:27118) |
| ExportCommandSlot | IMMUTABLE | 20 | 7 | [/tables/84](D:/CP6-archives/consolidation-20261010/objects/8127f36528aa69e0c67b104e6cef109de007a4163ce5085769ff58cf88fa58c0.json:27379) |
| ExportCommandReceipt | IMMUTABLE | 12 | 3 | [/tables/85](D:/CP6-archives/consolidation-20261010/objects/8127f36528aa69e0c67b104e6cef109de007a4163ce5085769ff58cf88fa58c0.json:27776) |
| ExportCheckpoint | IMMUTABLE | 14 | 3 | [/tables/86](D:/CP6-archives/consolidation-20261010/objects/8127f36528aa69e0c67b104e6cef109de007a4163ce5085769ff58cf88fa58c0.json:28015) |

列声明数含 15 个仅作展示的 BodyCanonical，不能误读为 1,458 个实际持久数据库列；实际 databaseColumn=true 为 1,443。所有 RowVersion 为持久但 provider-generated/readback-only。
