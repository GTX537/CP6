# QA-IPQC-01 连续案例、状态行与恢复绑定附册

本册覆盖 Recovery-R4 Systemic-R4 的 F02–F10（F06 分 A/B）10 个世界、62 步。CONTINUOUS_FIXTURES_RECOVERY_R4.json 的 SHA-256 为 76161b6b2ef5944c2d7a445edbe33546c056364f804f1fd10b851ad30ef6a79f，12,699,093 字节、198,217 行。源件声明 DESIGN_ONLY_STATIC_EVALUATION、SYNTHETIC_FIXTURE_INPUT、NONE_FIXTURE_ONLY；业务 AC 为 NOT_RUN，运行接受为 UNPROVEN。本次补读与静态比较均不升级这些状态。[原件身份与状态](D:/CP6-archives/consolidation-20261010/objects/76161b6b2ef5944c2d7a445edbe33546c056364f804f1fd10b851ad30ef6a79f.json:1)

## 1. 阅读方式与规范入口

本次补读全部请求/结果业务字段、before/after/write/final 状态行和 transactionContract。427 个 fixedOwnerReads 正文精确复用根代理[既有记录](D:/CP6/docs/CP6_开发设计文档_20261010/evidence/QA-IPQC-01-supplement-reading.json)的 continuous_progress；再检查事务中533次Owner正文与锁描述，并补读 Method、Specification、QualityRequirementHead、ActorAuthority 的9类新增参数化形状。规范字段以[类型索引](D:/CP6/docs/CP6_开发设计文档_20261010/contracts/QA-IPQC-01-类型字段索引.md)和[关系约束](D:/CP6/docs/CP6_开发设计文档_20261010/contracts/QA-IPQC-01-关系约束与检验边界.md)为入口。

状态图有1,419个finalRows、1,439个不同完整行版本、65个表名，全部表名在当前68表关系Schema内。相同完整行按原字典相等复用。展示把UUID/SHA保留为类型占位，将业务字段列为完整列式值、连续序列或逐段值；不是逐个肉眼重读所有随机字面量，也不是原字节无损展示。原值保留在源件，定向身份比较范围见§5。

## 2. 连续业务世界

### F02：来源修订、稳定任务与100件FQC

I03在来源/映射revision3注册Family/Policy；I05创建一个稳定Task、各100条PopulationMember、MeasurementSlot、SampleSelectionRoster，均为REQ-DIM-01/EA/replicate1。I09的100个INITIAL中，成员1–80数值5.0，81–90为HOLD，91–100为PENDING，value/code互斥，rawRef为F02-raw-1…100。六类资格是METHOD/SPECIFICATION/EQUIPMENT/CALIBRATION/ENVIRONMENT/ACTOR_AUTHORITY，各携fact、current head、authority、coverage和有效期。[原件 L6533](D:/CP6-archives/consolidation-20261010/objects/76161b6b2ef5944c2d7a445edbe33546c056364f804f1fd10b851ad30ef6a79f.json:6533) [原件 L17396](D:/CP6-archives/consolidation-20261010/objects/76161b6b2ef5944c2d7a445edbe33546c056364f804f1fd10b851ad30ef6a79f.json:17396)

I11独立复核全部100条；I15冻结全部槽与复核，100个[i,i+1)outcome形成A80/HOLD10/PENDING10；I16将后两类分别投影REWORK/UNEXAMINED；I17冻结审批包。I18绑定真实包、批准和权限证据、source3/task4/resultSet1，写完整Decision/100 partitions/Head、Family后继和发布原件。Out payload含80个accepted、10个rework、10个unexamined逐件区间；Stock实际应用量仍须Owner结果。迟到source1返回409 STALE_SOURCE_REVISION，只有Command/Audit。InitialClaim缺口见§6。[原件 L38411](D:/CP6-archives/consolidation-20261010/objects/76161b6b2ef5944c2d7a445edbe33546c056364f804f1fd10b851ad30ef6a79f.json:38411) [原件 L60923](D:/CP6-archives/consolidation-20261010/objects/76161b6b2ef5944c2d7a445edbe33546c056364f804f1fd10b851ad30ef6a79f.json:60923) [原件 L70690](D:/CP6-archives/consolidation-20261010/objects/76161b6b2ef5944c2d7a445edbe33546c056364f804f1fd10b851ad30ef6a79f.json:70690)

### F03：试制测量到PLM原证据

10个样本值5.0…5.9，各有PLM-DOC raw记录，I09同时保存10条InitialClaim。I11/I15/I16/I17/I18构成全量accepted[0,10)的复核、结果、分区和批准链。I29 payload是RawEvidenceHandoff正文：10 sampleRefs、10 rawRecordRefs、BASELINE-F03 v7、TEST-F03、DIMENSIONAL-FULL-COUNT v4、ENV-F03 v3、REPORT-F03、Task/ResultSet/Decision、coverage[0,10)和contentHashes，不能拿命令包装替代。[原件 L95681](D:/CP6-archives/consolidation-20261010/objects/76161b6b2ef5944c2d7a445edbe33546c056364f804f1fd10b851ad30ef6a79f.json:95681) [原件 L107044](D:/CP6-archives/consolidation-20261010/objects/76161b6b2ef5944c2d7a445edbe33546c056364f804f1fd10b851ad30ef6a79f.json:107044)

正常交付写PlmEvidenceHandoff/DeliveryPayload/Delivery/Outbox。Delivery的decision列可空，handoff正文仍有decisionRef。过期、改为BASELINE-F03-OTHER v8、PRODUCTION来源三个否定分支分别返回422 TRIAL_AUTH_EXPIRED/TRIAL_BASELINE_MISMATCH/TRIAL_PURPOSE_NOT_AUTHORIZED，均零domain写。OUT-TRIAL-F03与OUT-QA-F03身份差异须先澄清。[原件 L107318](D:/CP6-archives/consolidation-20261010/objects/76161b6b2ef5944c2d7a445edbe33546c056364f804f1fd10b851ad30ef6a79f.json:107318) [原件 L108292](D:/CP6-archives/consolidation-20261010/objects/76161b6b2ef5944c2d7a445edbe33546c056364f804f1fd10b851ad30ef6a79f.json:108292) [原件 L109110](D:/CP6-archives/consolidation-20261010/objects/76161b6b2ef5944c2d7a445edbe33546c056364f804f1fd10b851ad30ef6a79f.json:109110) [原件 L109928](D:/CP6-archives/consolidation-20261010/objects/76161b6b2ef5944c2d7a445edbe33546c056364f804f1fd10b851ad30ef6a79f.json:109928)

### F04：源修订重开整个Family

I23由OUT-QA-F04 revision1到2，affected[40,50)，新scope=[0,40)∪[50,100)。同UoW插入SourceOriginal/Mapping后继、更新SourceHead、Family/Task为REOPENED，保存ResultInvalidation/Impact/DependencyWake；旧Decision/ResultSet保留。I19不能把旧A90/R10分区换source版本发布，返回409 SUCCESSOR_MEASUREMENT_REQUIRED，publicationState=NOT_PUBLISHED。[原件 L118588](D:/CP6-archives/consolidation-20261010/objects/76161b6b2ef5944c2d7a445edbe33546c056364f804f1fd10b851ad30ef6a79f.json:118588) [原件 L118729](D:/CP6-archives/consolidation-20261010/objects/76161b6b2ef5944c2d7a445edbe33546c056364f804f1fd10b851ad30ef6a79f.json:118729) [原件 L119674](D:/CP6-archives/consolidation-20261010/objects/76161b6b2ef5944c2d7a445edbe33546c056364f804f1fd10b851ad30ef6a79f.json:119674)

### F05：竞争INITIAL、更正与复测

同槽首条INITIAL code FAIL成功；第二操作者仍期待task1而实际2，已有Claim，返回INITIAL_ALREADY_CLAIMED。独立复核后冻结旧REJECTED[0,1)结果。Correction仍FAIL，插入observation revision2并更新Head/Task/Family/Impact，保留revision1。RetestProposal→获准Episode/新槽→Ruling→I09 RETEST value5.0，Task推进到8。只交复测PASS槽、遗漏原FAIL的I15返回RESULTSET_INCOMPLETE_FAIL_OMITTED。[原件 L128125](D:/CP6-archives/consolidation-20261010/objects/76161b6b2ef5944c2d7a445edbe33546c056364f804f1fd10b851ad30ef6a79f.json:128125) [原件 L133731](D:/CP6-archives/consolidation-20261010/objects/76161b6b2ef5944c2d7a445edbe33546c056364f804f1fd10b851ad30ef6a79f.json:133731) [原件 L135040](D:/CP6-archives/consolidation-20261010/objects/76161b6b2ef5944c2d7a445edbe33546c056364f804f1fd10b851ad30ef6a79f.json:135040) [原件 L138540](D:/CP6-archives/consolidation-20261010/objects/76161b6b2ef5944c2d7a445edbe33546c056364f804f1fd10b851ad30ef6a79f.json:138540)

### F06A/B：撤回与Stock使用的提交顺序

A先I20将Decision1→2、Family WITHDRAWN，新完整分区PENDING/UNEXAMINED，随后旧decision/family的Stock请求被FAMILY_WITHDRAWN拒绝。B先由proposed Stock adapter使用[0,10)，写QapGuardEvidence/QapConsumerResult/QapUseFact/StockConsumerResult/StockEffect及Command/Audit；之后撤回保留已提交效果，再来的新用被拒。Stock拥有事务、消费结果和物理效果，未调用I18；F15-PROPOSED-STOCK-QAP-ADAPTER和MES-EXEC REQUIRED_ADAPTER_UNPROVEN都不代表真实采用。[原件 L146266](D:/CP6-archives/consolidation-20261010/objects/76161b6b2ef5944c2d7a445edbe33546c056364f804f1fd10b851ad30ef6a79f.json:146266) [原件 L155662](D:/CP6-archives/consolidation-20261010/objects/76161b6b2ef5944c2d7a445edbe33546c056364f804f1fd10b851ad30ef6a79f.json:155662) [原件 L158767](D:/CP6-archives/consolidation-20261010/objects/76161b6b2ef5944c2d7a445edbe33546c056364f804f1fd10b851ad30ef6a79f.json:158767)

### F07：UNKNOWN、补前驱和租约恢复

预存seq3 GAP；I26先返回503 OWNER_RESULT_UNKNOWN，只有终态写。C01回原request与UNKNOWN terminal准确字节。I24写入前驱seq2的Inbox/OwnerResultOriginal/OwnerObservationRevision/Head并唤醒依赖，不由transport未知推断业务效果。下一I26重建持久payload字节、lease8→9、记录一次SENT attempt与交付观察；旧token8返回STALE_LEASE_EPOCH。I25零写读取冻结交付。canonicalKey/sequence投影差异见§6。[原件 L165904](D:/CP6-archives/consolidation-20261010/objects/76161b6b2ef5944c2d7a445edbe33546c056364f804f1fd10b851ad30ef6a79f.json:165904) [原件 L166318](D:/CP6-archives/consolidation-20261010/objects/76161b6b2ef5944c2d7a445edbe33546c056364f804f1fd10b851ad30ef6a79f.json:166318) [原件 L166411](D:/CP6-archives/consolidation-20261010/objects/76161b6b2ef5944c2d7a445edbe33546c056364f804f1fd10b851ad30ef6a79f.json:166411) [原件 L167076](D:/CP6-archives/consolidation-20261010/objects/76161b6b2ef5944c2d7a445edbe33546c056364f804f1fd10b851ad30ef6a79f.json:167076)

### F08：PLM RequiredFenced与测量事实分开

Quality保存自己的有效观察不表示PLM生产适用性成立。I27 FOUND/COMPLETE提供plan/method/spec/ranges/control/authority；I28 UNKNOWN、I30 UNAVAILABLE、I31 NOT_AUTHORIZED后三者record=null、coverage UNKNOWN、canInferNoFact=false。REAL I29因adapter未就绪返回503 OWNER_PROTOCOL_NOT_READY，仅写REAL Command/Audit；DEMO READY不能推成真实交付能力。[原件 L172636](D:/CP6-archives/consolidation-20261010/objects/76161b6b2ef5944c2d7a445edbe33546c056364f804f1fd10b851ad30ef6a79f.json:172636) [原件 L172654](D:/CP6-archives/consolidation-20261010/objects/76161b6b2ef5944c2d7a445edbe33546c056364f804f1fd10b851ad30ef6a79f.json:172654) [原件 L172751](D:/CP6-archives/consolidation-20261010/objects/76161b6b2ef5944c2d7a445edbe33546c056364f804f1fd10b851ad30ef6a79f.json:172751) [原件 L172809](D:/CP6-archives/consolidation-20261010/objects/76161b6b2ef5944c2d7a445edbe33546c056364f804f1fd10b851ad30ef6a79f.json:172809) [原件 L172862](D:/CP6-archives/consolidation-20261010/objects/76161b6b2ef5944c2d7a445edbe33546c056364f804f1fd10b851ad30ef6a79f.json:172862) [原件 L173015](D:/CP6-archives/consolidation-20261010/objects/76161b6b2ef5944c2d7a445edbe33546c056364f804f1fd10b851ad30ef6a79f.json:173015)

### F09/F10：旧写者与当次资格

F09依次PREPARE9（head仍8）→ACTIVATE9→RETIRE8。旧PASS缺Policy/Scope/Baseline证明，只读search不产生新accepted。epoch8 LegacyMap被STALE_WRITER_EPOCH拒绝，C01回准确原字节；epoch9 JSON Export只是审计产物，downloadRef=null，不替代映射和接受。F10从METHOD validTo=2026-09-30、SPECIFICATION revokedAt=2026-09-30及Owner authority/coverage/head事实，分别得出EXPIRED/REVOKED/AUTHORITY_MISSING/COVERAGE_GAP/HEAD_CHANGED，五次均409、无观察或业务写。[原件 L179219](D:/CP6-archives/consolidation-20261010/objects/76161b6b2ef5944c2d7a445edbe33546c056364f804f1fd10b851ad30ef6a79f.json:179219) [原件 L189242](D:/CP6-archives/consolidation-20261010/objects/76161b6b2ef5944c2d7a445edbe33546c056364f804f1fd10b851ad30ef6a79f.json:189242)

## 3. 锁、当前值与事务边界

54个变更步骤均声明atomic/sameUow=true、资源OUTSIDE_TRANSACTION收集、httpUnderLock=false；更早rank改变须ROLLBACK_AND_RECOLLECT。8个只读步骤是readOnly=true/writes=0。变更结果中36次COMMITTED写domain+Command/Audit；17次REJECTED与1次UNKNOWN仅保存两个终态行。[原件 L6293](D:/CP6-archives/consolidation-20261010/objects/76161b6b2ef5944c2d7a445edbe33546c056364f804f1fd10b851ad30ef6a79f.json:6293) [原件 L166338](D:/CP6-archives/consolidation-20261010/objects/76161b6b2ef5944c2d7a445edbe33546c056364f804f1fd10b851ad30ef6a79f.json:166338)

|rank|逻辑资源与职责|
|---|---|
|10/20|proposed Stock分支的StockCanonical、ScopeLocationAncestors。|
|30|Owner SourceOriginal：OUT-QA revision1/2/3或EXEC-QA revision1。|
|34|SourceHead/SourceMappingHead；需要时Method v4、Specification v12、TechnicalBinding v4、QualityRequirements/Head v12，ordinal101–105。|
|36|FamilyDirectoryGate，以环境/DomainKeyDigest/stage锁住当前集合。|
|40|MAIN-QAP PolicyFinalGuard/FamilyCut；需要时OWNER-QA-POL PolicyContent v7、PlanContent v3。|
|41|FamilyControlHead→TaskHead→ObservationSetAndSlotHeads→ResultSetHead→DecisionHead→WriterEpochHead，ordinal10…60；ActorAuthority190；六类资格fact/head/authority/coverage为201…224。|
|50|StockConsumerResultAndEffect。|
|60|CommandAuditLedger XLOCK；发布分支另有I18/I20/I24/I29的OutboxTail XLOCK。|

databaseIdentity/resourceType是合同逻辑资源，不证明真实分库拓扑已经能sameUow。跨MAIN/OWNER需真实同提交adapter资格；不能用远端HTTP GET冒充持锁读。I09核六类资格；I18/Stock核五类加动作专属ActorAuthority，分别APPROVE_DISPOSITION和ORDINARY_STOCK_USE。撤回例authority body只有allowed:true，不能扩大为通用权限模型。[原件 L28995](D:/CP6-archives/consolidation-20261010/objects/76161b6b2ef5944c2d7a445edbe33546c056364f804f1fd10b851ad30ef6a79f.json:28995) [原件 L66185](D:/CP6-archives/consolidation-20261010/objects/76161b6b2ef5944c2d7a445edbe33546c056364f804f1fd10b851ad30ef6a79f.json:66185) [原件 L146893](D:/CP6-archives/consolidation-20261010/objects/76161b6b2ef5944c2d7a445edbe33546c056364f804f1fd10b851ad30ef6a79f.json:146893) [原件 L155987](D:/CP6-archives/consolidation-20261010/objects/76161b6b2ef5944c2d7a445edbe33546c056364f804f1fd10b851ad30ef6a79f.json:155987)

常见expected八轴是sourceOwnerRevision/mappingHeadRevision/familyDirectoryRevision/taskRevision/observationSetRevision/resultSetRevision/decisionSequence/writerEpoch。Stock另比较decision/family的version+digest及writer epoch；epoch转换核expectedHeadEpoch。F02 source1/3、F05 task1/2、F09 writer8/9及撤回后的family/decision差异均按原值保留，不能机械要求所有matches=true。

## 4. ConsumerReceipt与ConsumerResult身份

FK_R3_QapUse_ConsumerResult把S=(TenantId,EnvironmentId,Mode)+ConsumerReceiptOwner/Id/Version+ConsumerResultDigest，映射到QapConsumerResult的S+ConsumerOwner/ConsumerResultId/ConsumerResultVersion+ResultBodyDigest。F02/F06B两例七元值全部相等；没有“FK指错表”的证据。Receipt命名不允许塞入任意收据或movement ID。[Schema L4150](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/41/41a16ac59112f143__RELATIONAL_SCHEMA_RECOVERY_R4_SYSTEMIC.json:4150) [原件 L87555](D:/CP6-archives/consolidation-20261010/objects/76161b6b2ef5944c2d7a445edbe33546c056364f804f1fd10b851ad30ef6a79f.json:87555) [原件 L160801](D:/CP6-archives/consolidation-20261010/objects/76161b6b2ef5944c2d7a445edbe33546c056364f804f1fd10b851ad30ef6a79f.json:160801)

F06B的QAP投影id为ordinary-use-F06B，wire consumerResultRef为ordinary-use-result-F06B，Stock内部结果/效果关联为UUID。相同ResultBodyDigest只绑定相同正文，不证明三种ID互换；生产adapter须明确解析/投影映射。[原件 L160760](D:/CP6-archives/consolidation-20261010/objects/76161b6b2ef5944c2d7a445edbe33546c056364f804f1fd10b851ad30ef6a79f.json:160760) [原件 L155662](D:/CP6-archives/consolidation-20261010/objects/76161b6b2ef5944c2d7a445edbe33546c056364f804f1fd10b851ad30ef6a79f.json:155662)

## 5. 静态核对与未验证范围

自写stdlib比较：62步/1,300写正反重建，2,748状态行摘要，1,012次本地readSet对准确提交前行，533次Owner正文摘要与rank/ordinal/database/key/lockMode，402个expected布尔值，1,249锁项rank/ordinal顺序，两条命名QAP FK，113条成功I09的request→response ref→row body/id/version/digest，124组请求/结果与1,439行规范化列式反还原，均0差异。根既有427 Owner hash、124 JCS/输入输出摘要和FE40比较按原执行归属复用。

未运行作者程序、SQL、项目构建/测试、AC或运行集成；没有完成所有SQL类型/所有FK/CHECK、网络事务拓扑或实际并发恢复验证。业务数值/区间已全读，随机UUID/hash未逐字肉眼重读。源状态仍NOT_RUN/UNPROVEN。

## 6. 采用前澄清

1. **IPQC-CONT-01：F02 InitialClaim。** 100个INITIAL、声明含InitialClaim，但211实际写和967finalRows均无该表；F03有10条对照。API I09将F02-03列为COMMITTED证据，不能把表名声明当实际行。需修正夹具/摘要或给当前规范明确例外。[原件 L20140](D:/CP6-archives/consolidation-20261010/objects/76161b6b2ef5944c2d7a445edbe33546c056364f804f1fd10b851ad30ef6a79f.json:20140) [原件 L20170](D:/CP6-archives/consolidation-20261010/objects/76161b6b2ef5944c2d7a445edbe33546c056364f804f1fd10b851ad30ef6a79f.json:20170) [API L153](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/c6/c6e3e6f4c8d0e25c__API_REGISTRY_RECOVERY_R4_SYSTEMIC.json:153)
2. **IPQC-CONT-02：F03源ID。** I05 request/result和I29 handoff为OUT-TRIAL-F03；持久SourceOriginal/TaskRevision、I18、Delivery为OUT-QA-F03，v1均用8aacdd7c…摘要，originalSourceAliases=[]。需统一或补有权alias。[原件 L94099](D:/CP6-archives/consolidation-20261010/objects/76161b6b2ef5944c2d7a445edbe33546c056364f804f1fd10b851ad30ef6a79f.json:94099) [原件 L113408](D:/CP6-archives/consolidation-20261010/objects/76161b6b2ef5944c2d7a445edbe33546c056364f804f1fd10b851ad30ef6a79f.json:113408) [原件 L91313](D:/CP6-archives/consolidation-20261010/objects/76161b6b2ef5944c2d7a445edbe33546c056364f804f1fd10b851ad30ef6a79f.json:91313)
3. **IPQC-CONT-03：F07冻结交付身份。** 同deliveryId=22a15a95-a62a-557b-9970-8adaeb4e9e02/messageId，I26返回F07:I26/sequence4；没有Delivery写，持久行与I25仍F07:OWNER-SEQUENCE/sequence3。需明确attempt和冻结交付字段边界并对齐例证；UNKNOWN/PENDING/APPLIED属于不同状态投影，本次不只凭状态不同判故障。[原件 L167002](D:/CP6-archives/consolidation-20261010/objects/76161b6b2ef5944c2d7a445edbe33546c056364f804f1fd10b851ad30ef6a79f.json:167002) [原件 L167076](D:/CP6-archives/consolidation-20261010/objects/76161b6b2ef5944c2d7a445edbe33546c056364f804f1fd10b851ad30ef6a79f.json:167076) [原件 L168351](D:/CP6-archives/consolidation-20261010/objects/76161b6b2ef5944c2d7a445edbe33546c056364f804f1fd10b851ad30ef6a79f.json:168351)

另保留字段观察：F05旧ResultSet行TaskRevision=4，而BodyJson.taskRevision和请求/返回正文为3；本次未将冻结前评估版本与提交后Task版本直接判错，需作者解释二者字段语义。[原件 L140081](D:/CP6-archives/consolidation-20261010/objects/76161b6b2ef5944c2d7a445edbe33546c056364f804f1fd10b851ad30ef6a79f.json:140081) [原件 L132818](D:/CP6-archives/consolidation-20261010/objects/76161b6b2ef5944c2d7a445edbe33546c056364f804f1fd10b851ad30ef6a79f.json:132818)

以上均是静态例证采用前对齐，未证生产故障；不改变接受范围、不恢复历史缺件。

## 7. 实读窗口

|世界|步骤窗口|finalRows窗口|
|---|---|---|
|F02|[L5631–73700](D:/CP6-archives/consolidation-20261010/objects/76161b6b2ef5944c2d7a445edbe33546c056364f804f1fd10b851ad30ef6a79f.json:5631)|[L73702–90964](D:/CP6-archives/consolidation-20261010/objects/76161b6b2ef5944c2d7a445edbe33546c056364f804f1fd10b851ad30ef6a79f.json:73702)|
|F03|[L93186–110522](D:/CP6-archives/consolidation-20261010/objects/76161b6b2ef5944c2d7a445edbe33546c056364f804f1fd10b851ad30ef6a79f.json:93186)|[L110524–113532](D:/CP6-archives/consolidation-20261010/objects/76161b6b2ef5944c2d7a445edbe33546c056364f804f1fd10b851ad30ef6a79f.json:110524)|
|F04|[L118582–120595](D:/CP6-archives/consolidation-20261010/objects/76161b6b2ef5944c2d7a445edbe33546c056364f804f1fd10b851ad30ef6a79f.json:118582)|[L120597–121326](D:/CP6-archives/consolidation-20261010/objects/76161b6b2ef5944c2d7a445edbe33546c056364f804f1fd10b851ad30ef6a79f.json:120597)|
|F05|[L128119–138986](D:/CP6-archives/consolidation-20261010/objects/76161b6b2ef5944c2d7a445edbe33546c056364f804f1fd10b851ad30ef6a79f.json:128119)|[L138988–140378](D:/CP6-archives/consolidation-20261010/objects/76161b6b2ef5944c2d7a445edbe33546c056364f804f1fd10b851ad30ef6a79f.json:138988)|
|F06A|[L146223–149169](D:/CP6-archives/consolidation-20261010/objects/76161b6b2ef5944c2d7a445edbe33546c056364f804f1fd10b851ad30ef6a79f.json:146223)|[L149171–149724](D:/CP6-archives/consolidation-20261010/objects/76161b6b2ef5944c2d7a445edbe33546c056364f804f1fd10b851ad30ef6a79f.json:149171)|
|F06B|[L155569–160290](D:/CP6-archives/consolidation-20261010/objects/76161b6b2ef5944c2d7a445edbe33546c056364f804f1fd10b851ad30ef6a79f.json:155569)|[L160292–160994](D:/CP6-archives/consolidation-20261010/objects/76161b6b2ef5944c2d7a445edbe33546c056364f804f1fd10b851ad30ef6a79f.json:160292)|
|F07|[L165877–168089](D:/CP6-archives/consolidation-20261010/objects/76161b6b2ef5944c2d7a445edbe33546c056364f804f1fd10b851ad30ef6a79f.json:165877)|[L168091–168790](D:/CP6-archives/consolidation-20261010/objects/76161b6b2ef5944c2d7a445edbe33546c056364f804f1fd10b851ad30ef6a79f.json:168091)|
|F08|[L170441–173397](D:/CP6-archives/consolidation-20261010/objects/76161b6b2ef5944c2d7a445edbe33546c056364f804f1fd10b851ad30ef6a79f.json:170441)|[L173399–173828](D:/CP6-archives/consolidation-20261010/objects/76161b6b2ef5944c2d7a445edbe33546c056364f804f1fd10b851ad30ef6a79f.json:173399)|
|F09|[L179219–181225](D:/CP6-archives/consolidation-20261010/objects/76161b6b2ef5944c2d7a445edbe33546c056364f804f1fd10b851ad30ef6a79f.json:179219)|[L181227–181503](D:/CP6-archives/consolidation-20261010/objects/76161b6b2ef5944c2d7a445edbe33546c056364f804f1fd10b851ad30ef6a79f.json:181227)|
|F10|[L189242–197846](D:/CP6-archives/consolidation-20261010/objects/76161b6b2ef5944c2d7a445edbe33546c056364f804f1fd10b851ad30ef6a79f.json:189242)|[L197848–198213](D:/CP6-archives/consolidation-20261010/objects/76161b6b2ef5944c2d7a445edbe33546c056364f804f1fd10b851ad30ef6a79f.json:197848)|

逐步pointer/行号、复用归属、检查范围及未验证项见[阅读证据](D:/CP6/docs/CP6_开发设计文档_20261010/evidence/QA-IPQC-01-continuous-reading.json)。
