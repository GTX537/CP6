# WMS-ROLL-01 卷材作业开发设计

状态：当前正文及X3接受/评审元数据已整理，待双方合同集中核对。[模块目录](D:/CP6/docs/CP6_开发设计文档_20261010/模块目录.md)。

## 1. 目的、操作者和范围

仓管登记卷材真实身份，匹配材料需求，组织经授权的纵切、消费和处置，保留原来源与父子谱系。五原SPEC编号仍为纸卷建立、匹配、分切、消耗、处置，通用核心负责身份/来源/数量映射，纸质/克重是可空扩展。Stock独占Root、当前份额/位置、Claim和物理数量；行业记录建立零库存效果。

正常例：原IN真实1000M→登记卷→原生产消费300→Stock剩700→将全部700全长纵切→父TRANSFORMED，子取得准确新Root→继续使用或同Root登记REM。多个不同宽子卷各700M不能裸加后与父700M求守恒；工艺用批准面积口径，各Root按自己的baseUom记账。阈值以下只标余料候选，不等实物报废。[原规范](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/fc/fcfddec26e2fa8fd__WMS-ROLL-01_原纸卷作业_完整设计_RECOVERY_v1.1_R2.md:13)

## 2. 当前版本和适用性

本轮补核结论：2026-10-08T11:56:55Z代理接受准确六册R2及具名profile；原九根累计静态闭合，全部业务AC仍NOT_RUN、Owner运行采用UNPROVEN。[X3共同阅读与采用边界](D:/CP6/docs/CP6_开发设计文档_20261010/evidence/X3-六专项阅读与采用边界.md)保存准确组合、112AC/31任务逐条等值核对和历史R1原ZIP缺口。下文提及原候选标签均属保留历史。

v1.1 R2 RECOVERY，SHA前缀`fcfddec26e2fa8fd`，准确全SHA/UA/CURRENT和评审在[必需输入](D:/CP6/docs/CP6_开发设计文档_20261010/indexes/required-inputs.json)。主件历史STOPPED保留；本轮未以标签覆盖后继接受。22AC NOT_RUN、所有运行采用UNPROVEN，新内部桥和候选HTTP不能说已实现。

完整设计的分切profile为`WEB_ROLL_LENGTH`：连续等宽卷带、全部当前剩余长度一次纵切，纸/膜/箔/纺织卷在产品Owner正式采用同几何/来源约束时可适用。圆棒、线缆、不规则卷、变厚复合物及局部长二维切割不自动支持，需具名新profile；不能OTHER/空配置绕检。[原规范](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/fc/fcfddec26e2fa8fd__WMS-ROLL-01_原纸卷作业_完整设计_RECOVERY_v1.1_R2.md:51)

## 3. 对象、量纲和身份

|对象|必须保留的具体含义|
|---|---|
|MaterialProfile|Product/技术Owner版本注册kind、允许材料类/必需属性、Stock单位、动作、geometry/precision/schema政策；WMS只读|
|RollProfileData|WEB_ROLL_LENGTH width/originalLength、可空芯径/品级/方向/阈值与PaperExtension；单位由profile规定|
|VerifiedRollIdentity|原IN ReceiptLine、完整RootBinding/准确spans/数量/Receipt/sourceMap、产品技术货主baseUom、Profile、sourceQuantity、stockToGeometryMap、测量与可空供应卷号/批号/LocalDate|
|RollMeasureMap|同完整身份/Root范围、baseUom/lengthUom、正numerator/denominator、测量/cut、UNIFORM_OVER_DECLARED_SPANS/COMPLETE；length=StockQty×分子/分母须精确可表示|
|RollIntake|UNVERIFIED_INTAKE、intakeKey、可信Scope、可空已知现场位置/物理键/profile/量及reason；不含伪造Source/Root|
|RollView|identity/intake可空、当前Stock量和几何长度分开、当前spans/cut/knowledge、父composition/子卷与revision|
|SlitInput/OutputDefinition|父/cut、真实指令/occurrence、slitProfile、子宽/切缝/余边选择、每outputKey产品技术单位/quantityMapping/目的/质量政策|
|SlitProposal/Approval|完整Preview/input、父revision/cut、subjectDigest、request/proposer/approver/policy/有效期/决定；不是客户端自签Ref|
|SlitReceipt|一个composition、父全部consume refs、所有outputKey→同子卷/StockReceipt/RootBinding、损耗离场原件、lineage与真实commit|

Measure是尺寸/物性，不等库存Quantity；KG/SHT不能凭长宽或纸克重推M。输出即使同为M仍需真实identity quantityMapping原件。Root区间是数量谱系份额，不是现场第几米；需切割定位另有Measurement/SegmentMap。sourceQuantity单位等baseUom，所支持全长分切要求全部未耗份额同地点。[原规范](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/fc/fcfddec26e2fa8fd__WMS-ROLL-01_原纸卷作业_完整设计_RECOVERY_v1.1_R2.md:50)

生命周期DRAFT/ACTIVE/IN_USE/REMNANT_CANDIDATE/EXHAUSTED/TRANSFORMED/DISPOSED/QUARANTINED；EXHAUSTED、TRANSFORMED与损失绝不合并。UQ(tenant,environment,producer,physicalRollSourceKey)及RootCoverage门保证同卷唯一；供应卷号按供应者和到货权威范围归一，不假定字符串全局唯一。Stock命令最多13位整数/8小数，只读总量30位/8；单位、profile测量精度可更严格。[原规范](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/fc/fcfddec26e2fa8fd__WMS-ROLL-01_原纸卷作业_完整设计_RECOVERY_v1.1_R2.md:79)

## 4. 登记、匹配、分切与消费

未入账现场卷走UNVERIFIED_INTAKE：可观察1000M但identity=null/currentLength=null、QUARANTINED，零Root。原IN完成后向**同rollId** activate完整VerifiedIdentity；同RollGate、IntakeHead及Source/RootCoverage门核当前原件、维度、测量/量/授权，写ActivationReceipt/Binding、BOUND、ACTIVE及新revision，Stock增加0。响应丢失回同Binding；另隔离记录映同Source成为DUPLICATE_ALIAS引用canonicalRoll，不第二ACTIVE。已ACTIVE不能activate换Root。备注可改，物性更正需原Owner证据/后继核对；阈值0..初始长度，只改分类。[原规范](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/fc/fcfddec26e2fa8fd__WMS-ROLL-01_原纸卷作业_完整设计_RECOVERY_v1.1_R2.md:81)

匹配读真实Demand/用途与profile，核当前Stock/质量/技术/地点/货主；allowSlit=false仅准确需求宽，true可更宽但标requiredSlit。排序先可直接使用，再最小满足剩长、最接近宽、rollId；改策略需批准版。匹配不预留，selection绑定需求/Root/cut/规则；被别人先耗则409/412，不自动换卷。未知不从本地Remaining减LocalReserved猜Free。[原规范](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/fc/fcfddec26e2fa8fd__WMS-ROLL-01_原纸卷作业_完整设计_RECOVERY_v1.1_R2.md:91)

全长分切先精确归一单位/精度，用checked decimal(38,8)计算remainingWidth=父宽−子宽和−切缝；负值拒，余宽>0的KEEP必须唯一REMAINDER定义，APPROVED_SCRAP须真实批准及完成离场证据。例1310mm×500M分905+390、切缝0、余15KEEP：655m²=452.5+195+7.5；三输出各500M，不与父500M比裸总长。切缝5时余10。面积EXACT才prepare，不静默舍入；理论纸重只能明确theoretical，不能替库存KG测量。[原规范](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/fc/fcfddec26e2fa8fd__WMS-ROLL-01_原纸卷作业_完整设计_RECOVERY_v1.1_R2.md:101)

RL04依preview→proposal冻结完整主体→submit生成ApprovalRequest/Outbox→有权decisions产生SlitApproval→用proposal/approval和lossExitEvidence创建operation→prepare/execute。父head/cut、子宽、输出身份/地点、损耗几何变化均使旧批准失效，需新提案批准，不只更新expectedRevision。本域批准不替真实physicalInstruction/disposalDecision事前权。发生键UQ(instructionOwner,physicalOccurrenceKey,SLIT)贯穿HTTP/operation变换，Source父预算不重置。[原规范](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/fc/fcfddec26e2fa8fd__WMS-ROLL-01_原纸卷作业_完整设计_RECOVERY_v1.1_R2.md:123)

消费须真实ConsumptionInstruction（issuer、工单/材料行、DIRECT或LINE_SIDE、occurrence、允许spans/量/技术/质量/ClaimUse/SourceAllowance/期限）；MES/MAT同Consumption已提交只LINK。Stock与Claim消费原子，行业关联同UoW或只读WAITING_LINK，不再扣量。1000耗300再660→700/40，40只是REMNANT_CANDIDATE；正好0→EXHAUSTED且零损失；移至机器不是最终耗用。[原规范](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/fc/fcfddec26e2fa8fd__WMS-ROLL-01_原纸卷作业_完整设计_RECOVERY_v1.1_R2.md:152)

处置分QUARANTINE_MOVE保实物/Root、AUTHORIZED_LOSS真实受权剩余LOSS、ADMIN_ARCHIVE只对已EXHAUSTED/TRANSFORMED且无实物/未决工作行政归档。Claim先原RSV完整UseFence/NoFurtherUse合法处置，UNKNOWN不得释放，普通LOSS不能消费他人Claim。旧Disposed有40不等已LOSS40，已耗范围不能再损失。[原规范](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/fc/fcfddec26e2fa8fd__WMS-ROLL-01_原纸卷作业_完整设计_RECOVERY_v1.1_R2.md:162)

## 5. API和跨Owner合同

候选根`/api/wms/paper-roll/v1`含context/Profile/measure-map/receipt/instruction/output-definition选项及原件查询；rolls create/by-intake-key/activate/限定PATCH/history/lineage；matches；slit-previews/proposals/submit/approval-request-decisions/approval GET；loss-evidence-options/read；slit/consume-operations及dispositions；operations prepare/execute/by-key/close/resume、receipts查询。operation.kind为SLIT/CONSUME/DISPOSITION，prepare读取已冻结完整body，不接受替换。[原规范](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/fc/fcfddec26e2fa8fd__WMS-ROLL-01_原纸卷作业_完整设计_RECOVERY_v1.1_R2.md:75)

拟议内部`RollCompositionBridge-1`：ResolveComposition→PrepareComposition完整父CONSUME与各输出RECEIPT的FrozenIntent/Source/资源→CollectResources→CommitComposition同连接/事务→ReadOriginalComposition或SealComposition。Source种类/producer须受权注册，WMS_ROLL不是已被Stock接受的新Posting枚举；不能新造TRANSFORM、借ADJUSTMENT或父IN原源重复收子卷。子leaf固定outputKey并绑定整组真实发生，不能拆移到另一组单独执行。[原规范](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/fc/fcfddec26e2fa8fd__WMS-ROLL-01_原纸卷作业_完整设计_RECOVERY_v1.1_R2.md:131)

`ReadCompletedSlitDisposition`由正式Disposition Owner读取自身完成/交接/实际离场/完整范围返回原SlitLossExitEvidence或PROCESSING/UNKNOWN；ROLL选项只经采用适配投影，客户端不能上传自签OUTSIDE_WMS。measure-map/output-definition也须原Product/测量Owner真实原件；无采用则WAITING/BLOCKED_ADOPTION，不异步先扣父后补子。[原规范](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/fc/fcfddec26e2fa8fd__WMS-ROLL-01_原纸卷作业_完整设计_RECOVERY_v1.1_R2.md:113)

## 6. 整组事务、实际废边与恢复

同一实际DbConnection/DbTransaction、一个顶层commit，资源包括CompositionCanonical、指令/发生/Source预算、Stock canonical/leaf、全部父Root/Claim/Pool、输入输出Location/完整CapacityDomain/MetricSet、技术质量和批准head。锁后核完整当前门/父可用范围/无未决消耗分切、现场前驱与各子容量，在内存定全写集；一起提交父CONSUME、全部子RECEIPT/Root/Receipt、容量Claim、CompositionReceipt/Lineage、父TRANSFORMED/子绑定、Audit/Outbox。最后一子容量失败整组零效果，不提前对外成功。[原规范](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/fc/fcfddec26e2fa8fd__WMS-ROLL-01_原纸卷作业_完整设计_RECOVERY_v1.1_R2.md:135)

**非零切缝/弃边必须已实际离开保管及全部相关占容域。** SlitLossExitEvidence完整绑定指令、occurrence、父、partKey KERF或REMAINDER、精确宽/长/面积/测量、批准/完成/physicalExit、时间、Scope、OUTSIDE_WMS、KNOWN_ZERO、COMPLETE/cut。每非零part各一件，不能余边证据复用切缝；同Source/DispositionCompletion/PhysicalExit/Capacity门持有至commit并永久消费该part。已用于别的Stock损失则阻断对账，不能再父LOSS一次。[原规范](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/fc/fcfddec26e2fa8fd__WMS-ROLL-01_原纸卷作业_完整设计_RECOVERY_v1.1_R2.md:111)

若15mm×500M废边仍在隔离位，647.5m²好料+7.5m²废边全部SlitActualObservation保地点/测量/原发生，并交原Stock ActualFact/Case；原父Source/容量保护不提前释放，相关容量域UNKNOWN禁止新准入，不能只造好料正常输出。后续实际离场沿原Case/废边身份一次处置，不再正常Composition耗父。实际观察POST只持久事实/保护，缺采用仍WAITING_ADOPTION，不新颁正常Root。[原规范](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/fc/fcfddec26e2fa8fd__WMS-ROLL-01_原纸卷作业_完整设计_RECOVERY_v1.1_R2.md:119)

重试回同SlitReceipt/子ID，commit丢失查原composition及全部locator；仅部分原件→RECONCILIATION_REQUIRED，不自动补齐物理腿。Slit/Consume/Dispose争同RollGate+Stock Root/Source，新前驱冲突拒后者。全组关闭先封全部Source/worker/alias，原canonical/leaf全未效且现场未切才SEALED_NO_EFFECT；已经切开系统未知保RECONCILIATION，不能重激活父。原错误消费由Stock Correction核全后继/Claim，真实退料走RETURN不负Consume。[原规范](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/fc/fcfddec26e2fa8fd__WMS-ROLL-01_原纸卷作业_完整设计_RECOVERY_v1.1_R2.md:146)

## 7. 权限与持久化要求

roll.read/register/metadata.edit/match/slit.propose/approve/execute/consume.coordinate/disposition.coordinate/recover/history/export分别校验，生产源/Owner/源目的区域交集，最后门当前授权。推荐独立审批者，是否强制职责隔离由真正采用policyRef决定；批准不自带Stock执行权，恢复不能改冻结工艺。导出维度分组，跨卷总长度不暗示面积守恒。[原规范](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/fc/fcfddec26e2fa8fd__WMS-ROLL-01_原纸卷作业_完整设计_RECOVERY_v1.1_R2.md:178)

Identity/MetadataVersion、Intake/Observation/ActivationReceipt/DuplicateAlias、RootBinding/CoverageGate、PhysicalInstruction/OccurrenceRegistry、SlitIntent/OutputDefinition/GeometryBalance、Proposal/ApprovalRequest/Approval/Consumption、LossExitEvidenceLink/DispositionCompletionConsumption、ActualObservation/StockCaseLink、CompositionPreparation/StockCall/Receipt/Lineage、Consumption/DispositionLink、ObservationCut/ClosureFence/Epoch、Legacy/WriterFence/Audit/Outbox都保Tenant+Environment。profile/映射/输出计量规则冻结，数量镜像只读，不能写RemainingLengthM主账。[原规范](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/fc/fcfddec26e2fa8fd__WMS-ROLL-01_原纸卷作业_完整设计_RECOVERY_v1.1_R2.md:172)

## 8. 页面与输入校验

RL01列当前Stock量、几何剩长、知识、状态及适用材质；RL02明确已证来源/未知现场两种入口和同ID激活，仓位跟Root只读；RL03需求匹配及排除原因服务端分页；RL04逐行严格子宽/切缝、输出定义/面积与完整提案审批；RL05只选真实消费指令；RL06选原处置/真实范围；RL07双向谱系/原结果；RL08原键/Body/发送知识/closure恢复。

宽度905x、0或溢出整组拒，不split/parseInt/filter静默丢项。Quantity/Measure字符串保精度，非纸不填假纸质/默认T/3英寸。mfgDate为LocalDate不转UTC，原未知时间保未知。current失联为null/UNKNOWN，不显示已核0；修改profile/物性不能靠备注或普通PATCH绕工艺。[原规范](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/fc/fcfddec26e2fa8fd__WMS-ROLL-01_原纸卷作业_完整设计_RECOVERY_v1.1_R2.md:31)

## 9. 实施顺序与旧代码映射

D01两分支登记与CAS同ID激活、原Root/只读余量；D02具名profile/非纸样例、精确测量/资格匹配；D03实际提案审批/非零损耗离场证据及在场ActualFact/共同Composition；D04真实消费处置Owner；D05谱系/UNKNOWN/永久关闭/全writer切换，联合Stock适配需单独有界审查。[原规范](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/fc/fcfddec26e2fa8fd__WMS-ROLL-01_原纸卷作业_完整设计_RECOVERY_v1.1_R2.md:184)

原文固定CP6@90c871fe观察：旧Create只插T_PaperRoll，Consume直接改RemainingLength、0变Disposed、低阈值Remnant，Slit全父余长生成子后父Disposed，无统一Stock账；Dispose只改状态不清余长。旧测试1000→700→40、1310→905+390+15只验证旧表，本轮未运行/复审源码；模型注释不能替service实际语义。迁移保旧状态原文，不猜真实LOSS。[原规范](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/fc/fcfddec26e2fa8fd__WMS-ROLL-01_原纸卷作业_完整设计_RECOVERY_v1.1_R2.md:21)

有据IN/Root才零数量LegacyMap；来源未知/状态矛盾QUARANTINED只读，旧分切家树无Stock对应不得造新Receipt。OLD→HOLD排未知→核最后旧revision→NEW；旧Create/Consume/Slit/Dispose/import/script跨端所有最后writer共同封住，NEW物理提交后只前滚。[原规范](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/fc/fcfddec26e2fa8fd__WMS-ROLL-01_原纸卷作业_完整设计_RECOVERY_v1.1_R2.md:174)

## 10. 验收与证据

22AC全部NOT_RUN：登记零增量/同ID激活/同源幂等；膜卷无纸扩展及不支持profile；allowSlit/候选被耗；1310几何/提案批准链；切缝或改父子使旧批准失效；非法宽度拒；最后子失败全回滚；丢响应同子ID；非零损耗仅批准、在场、已离场三分支；1000→700→40；MES消费只link；同父竞态；耗尽无LOSS；在场7.5m²保护及后续一次处置；旧Disposed40不造损失；REM同Root零增量；现场已切UNKNOWN；当前无权/失联null；NEW旧Consume423零效果。[原规范](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/fc/fcfddec26e2fa8fd__WMS-ROLL-01_原纸卷作业_完整设计_RECOVERY_v1.1_R2.md:186)

完整JSON分别读了LossExitEvidence、SlitOperationCreate、SlitApproval、ActivateRollInput及14位总量正反例。例中64零digest仅形状占位，非真实签发/执行/通过。[原规范](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/fc/fcfddec26e2fa8fd__WMS-ROLL-01_原纸卷作业_完整设计_RECOVERY_v1.1_R2.md:305)

## 11. 开发前待闭合

ROLL-G01 IN/Root、Profile与stockToGeometryMap；G02输出定义/测量/精度与真实Proposal/Approval生产者；G03全Composition UoW及所有Source叶一次；G04完整容量/质量/Claim；G05真实消费/处置Owner、非零损耗实际离场和在场ActualFact/容量保护、原键关闭；G06全部旧writer与映射，均运行UNPROVEN。缺对应门阻相应新效果，保读/登记/候选。[原规范](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/fc/fcfddec26e2fa8fd__WMS-ROLL-01_原纸卷作业_完整设计_RECOVERY_v1.1_R2.md:180)

X3接受/评审元数据已补核；本轮还需核双方当前Owner合同。不能把“全组原子”简化成多次成功HTTP，也不能把废边批准当离场、理论基重当精确实际库存换算。

## 12. 来源和阅读覆盖

主件1–212全部独有规范/22AC已读（截断窗口50–107另外完整补读）；213–300公共附录与已读REP148–235逐行完全相同并复用；301–767说明/全部JSON完整对象读取，只有空白压缩无字段省略。准确SHA、待核共同来源与覆盖在[阅读台账](D:/CP6/docs/CP6_开发设计文档_20261010/evidence/root-specialty-reading.json)。历史源码和Library窗口资格沿原件，不冒成本次执行；原件未改。

## 必要材料整理结论

当前15项必需引用均已完整阅读或使用有证据的精确复用：主文、当前接受/审查、原范围、AC与任务文字以及来源资格已纳入设计。历史成员目录只证明来源身份，不等于旧代码全文审计；外域实际采用、Provider和业务运行继续按本册门禁确认。见[逐目标必需引用核对](../evidence/root-specialty-required-closure.json)。后续整任务进行一次集中跨模块复核，不将静态设计整理等同实际验收。
