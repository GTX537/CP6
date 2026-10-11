# QA-IPQC-01 F01物理身份与连续事务附册

本册整理当前资料中的 F01 checkpoint 与 legalVariant，覆盖两个世界的字段、请求/响应、写集、原件与全部差量。源文件为 48,570,859 字节、610,761 行、SHA256 5dbc9a77c6468acfd644623f1aebc44982388fbbc122bc8f9d338acfd2057708。它明确属于 SYNTHETIC_DEMO_INPUT / NONE_FIXTURE_ONLY；作者 validation.PASS 不提升 businessAC=NOT_RUN 或 runtimeAcceptance=UNPROVEN。[F01 L2](D:/CP6-archives/consolidation-20261010/objects/5dbc9a77c6468acfd644623f1aebc44982388fbbc122bc8f9d338acfd2057708.json:2) [F01 L610385](D:/CP6-archives/consolidation-20261010/objects/5dbc9a77c6468acfd644623f1aebc44982388fbbc122bc8f9d338acfd2057708.json:610385)

## 1. 业务目的与身份链

场景从 MES-EXEC 的 WO-F01 / OP-F01 已有工序来源开始，规划员解析 QA-POL、创建 IPQC 任务；检验员登记100份测量；独立复核员VERIFY；冻结全量结果、起草完整处置分区、冻结批准包，最后批准者形成决定及待发 MES 处置提案。每一步保原 commandId/requestKey、输入摘要、预期版本、终态正文及审计；生产采用身份尚未证明。[F01 L57465](D:/CP6-archives/consolidation-20261010/objects/5dbc9a77c6468acfd644623f1aebc44982388fbbc122bc8f9d338acfd2057708.json:57465) [F01 L228649](D:/CP6-archives/consolidation-20261010/objects/5dbc9a77c6468acfd644623f1aebc44982388fbbc122bc8f9d338acfd2057708.json:228649)

物理域为同一实物发生、同一批次、100 EA、离散全集[0,100)。F01-M001…M100各拥有准确[i−1,i)及独立memberEvidenceRef。来源中F01-P001…P100为原portion身份；测量slot、Observation、Outcome、决定segment各有自身身份，不因范围相同而互换。计划需要FULL_COUNT 100份；QAP-HASH-RANK-1只给选取次序，本例最终选中全集，不能把排序前几项当已足量抽样。[F01 L279724](D:/CP6-archives/consolidation-20261010/objects/5dbc9a77c6468acfd644623f1aebc44982388fbbc122bc8f9d338acfd2057708.json:279724) [F01 L282474](D:/CP6-archives/consolidation-20261010/objects/5dbc9a77c6468acfd644623f1aebc44982388fbbc122bc8f9d338acfd2057708.json:282474) [F01 L287178](D:/CP6-archives/consolidation-20261010/objects/5dbc9a77c6468acfd644623f1aebc44982388fbbc122bc8f9d338acfd2057708.json:287178)

## 2. 八步事务、状态与写集

所有步骤额外写1条Command、1条AuditEvent、1条CommandTerminal。以下统计包含这3条；每步before/after是同一有序世界的行映像，本文未把它执行到SQL。[F01 L57464](D:/CP6-archives/consolidation-20261010/objects/5dbc9a77c6468acfd644623f1aebc44982388fbbc122bc8f9d338acfd2057708.json:57464)

|步骤/操作者|核心写集与结果|before→writes→after|
|---|---|---|
|I03 / planner|FamilyControl、FamilyDirectoryVersion/Member新增，DirectoryHead由1更新2；返回QA-POL完整上下文，不把mayCreateTask误作已建任务。 [F01 L57885](D:/CP6-archives/consolidation-20261010/objects/5dbc9a77c6468acfd644623f1aebc44982388fbbc122bc8f9d338acfd2057708.json:57885)|8→7→14|
|I05 / planner|TaskRevision1与TaskHead MEASURING；100 PopulationMember、100 MeasurementSlot。 [F01 L65820](D:/CP6-archives/consolidation-20261010/objects/5dbc9a77c6468acfd644623f1aebc44982388fbbc122bc8f9d338acfd2057708.json:65820)|14→205→219|
|I09 / inspector|100 Observation、资格Cut、ObservationSetHead1；TaskRevision2/Head更新，测量事实不可覆盖。 [F01 L85614](D:/CP6-archives/consolidation-20261010/objects/5dbc9a77c6468acfd644623f1aebc44982388fbbc122bc8f9d338acfd2057708.json:85614)|219→107→325|
|I11 / reviewer|ObservationReview/ReviewHead、100复核项、100 OutcomeCoverageEvidence；TaskRevision3/Head更新。 [F01 L108261](D:/CP6-archives/consolidation-20261010/objects/5dbc9a77c6468acfd644623f1aebc44982388fbbc122bc8f9d338acfd2057708.json:108261)|325→207→531|
|I15 / reviewer|ResultSet/Head、100 EvaluatedRangeOutcome、100 ResultSetSlot；TaskRevision4/Head为RESULT_FROZEN。 [F01 L140925](D:/CP6-archives/consolidation-20261010/objects/5dbc9a77c6468acfd644623f1aebc44982388fbbc122bc8f9d338acfd2057708.json:140925)|531→207→737|
|I16 / planner|完整DispositionDraft，绑定准确ResultSet与Task4，不重新测量。 [F01 L177303](D:/CP6-archives/consolidation-20261010/objects/5dbc9a77c6468acfd644623f1aebc44982388fbbc122bc8f9d338acfd2057708.json:177303)|737→4→741|
|I17 / planner|ApprovalPacket固定draft/resultSet/task和完整partitionDigest。 [F01 L208241](D:/CP6-archives/consolidation-20261010/objects/5dbc9a77c6468acfd644623f1aebc44982388fbbc122bc8f9d338acfd2057708.json:208241)|741→4→745|
|I18 / approver|Decision/Head、Approval、DispositionIdentity、ResultCoverageEvidence、DeliveryPayload和100 DecisionPartitionRange。 [F01 L242448](D:/CP6-archives/consolidation-20261010/objects/5dbc9a77c6468acfd644623f1aebc44982388fbbc122bc8f9d338acfd2057708.json:242448)|745→109→854|

Family、Task、ObservationSet、Review、ResultSet、Decision、Source、Mapping及writerEpoch为不同轴。初始writerEpoch=8 ACTIVE；Family目录1→2，Task1→2→3→4，ResultSet1，Decision sequence1/version1；source/mapping都保持1。审批不再递增Task，Task仍RESULT_FROZEN。新实现须按实际预期轴复核，不能用一个Revision概括。[F01 L57374](D:/CP6-archives/consolidation-20261010/objects/5dbc9a77c6468acfd644623f1aebc44982388fbbc122bc8f9d338acfd2057708.json:57374) [F01 L228656](D:/CP6-archives/consolidation-20261010/objects/5dbc9a77c6468acfd644623f1aebc44982388fbbc122bc8f9d338acfd2057708.json:228656)

## 3. Owner资格、时点与角色

外部目录261份：100成员证据、100原始测量及61份来源、策略、计划、标准和资格原件。各步骤visibleOwnerCut依次135、135、259、260、260、260、260、261；未到该步骤的原件不能提前使用。内部314份事实按创建步骤0…8标记，visibleInternalBeforeKeys只含先前已创建事实，新事实另列internalCreatedKeys。[F01 L279680](D:/CP6-archives/consolidation-20261010/objects/5dbc9a77c6468acfd644623f1aebc44982388fbbc122bc8f9d338acfd2057708.json:279680) [F01 L304854](D:/CP6-archives/consolidation-20261010/objects/5dbc9a77c6468acfd644623f1aebc44982388fbbc122bc8f9d338acfd2057708.json:304854)

资格Cut包含METHOD、SPECIFICATION、EQUIPMENT、CALIBRATION、ENVIRONMENT、ACTOR_AUTHORITY六类；每类同时绑定事实、控制头、覆盖和IAM授权，validFrom/validTo/revokedAt按原观测时点判断。此例资格validFrom为2026-01-01、validTo/revokedAt=null，覆盖[0,100)。计划和标准无限结束还分别引用明确noEndAuthorityRef，不能把任意null泛化成永久授权。[F01 L79945](D:/CP6-archives/consolidation-20261010/objects/5dbc9a77c6468acfd644623f1aebc44982388fbbc122bc8f9d338acfd2057708.json:79945) [F01 L286631](D:/CP6-archives/consolidation-20261010/objects/5dbc9a77c6468acfd644623f1aebc44982388fbbc122bc8f9d338acfd2057708.json:286631)

测量者inspector-F01与复核者reviewer-F01分离；复核权原件actions=[VERIFY]且separatedFrom指检验员。最终approver-F01的APPROVE_DISPOSITION权严格绑定F01-SOURCE-CONTEXT。Fixture中的IAM/Owner名称与合成原件不证明真实服务身份或已签署采用。[F01 L284133](D:/CP6-archives/consolidation-20261010/objects/5dbc9a77c6468acfd644623f1aebc44982388fbbc122bc8f9d338acfd2057708.json:284133) [F01 L284113](D:/CP6-archives/consolidation-20261010/objects/5dbc9a77c6468acfd644623f1aebc44982388fbbc122bc8f9d338acfd2057708.json:284113)

## 4. 逐实物评估与全量结果

标准例给FULL_COUNT、单一REQ-DIM-01及指定方法/规格/每成员判定规则。规格上下界均包含0与10；原始FAIL码优先判失败，否则按规格区间。基准前90成员value="5"/code=null，后10成员value=null/code="FAIL"。复核通过表示测量可用于判定，不把FAIL值变合格。最终100个slot和100个Outcome完整，quality分别90 ACCEPTED/10 REJECTED。[F01 L289137](D:/CP6-archives/consolidation-20261010/objects/5dbc9a77c6468acfd644623f1aebc44982388fbbc122bc8f9d338acfd2057708.json:289137) [F01 L288913](D:/CP6-archives/consolidation-20261010/objects/5dbc9a77c6468acfd644623f1aebc44982388fbbc122bc8f9d338acfd2057708.json:288913) [F01 L129146](D:/CP6-archives/consolidation-20261010/objects/5dbc9a77c6468acfd644623f1aebc44982388fbbc122bc8f9d338acfd2057708.json:129146)

每个slot必须能回到MemberId和原范围；Observation回原rawRecordRef；复核项回准确Observation id/revision/digest；Coverage同时回slot、Observation、Review和范围；Outcome回Coverage/规则；ResultSetSlot回Outcome；决定分区及外发payload回同一Outcome和原范围。全量覆盖与全部合格是两个事实，不能因ResultSet.state=COMPLETE发出全接受。[F01 L267187](D:/CP6-archives/consolidation-20261010/objects/5dbc9a77c6468acfd644623f1aebc44982388fbbc122bc8f9d338acfd2057708.json:267187) [F01 L289159](D:/CP6-archives/consolidation-20261010/objects/5dbc9a77c6468acfd644623f1aebc44982388fbbc122bc8f9d338acfd2057708.json:289159)

## 5. 有效变体与摘要传播

legalVariant沿用同一场景身份和全部UUID、初始状态、Schema、创建时序；这是另一个独立合成世界，不是原世界同requestKey改体后允许覆盖。唯一原始业务变化是RAW-001和首份Observation的值5→11；对应[0,1)由ACCEPTED变REJECTED，接受89、拒绝11，相关请求/审计/结果/决定/外发原件摘要随准确字节变化。初始I03/I05完全相等，后六步按新原件运行示例。[F01 L385114](D:/CP6-archives/consolidation-20261010/objects/5dbc9a77c6468acfd644623f1aebc44982388fbbc122bc8f9d338acfd2057708.json:385114) [F01 L584832](D:/CP6-archives/consolidation-20261010/objects/5dbc9a77c6468acfd644623f1aebc44982388fbbc122bc8f9d338acfd2057708.json:584832)

本轮逐差量归并19023处变动，18778处为摘要，245处非摘要：62处测量5→11、175处接受→拒绝、两项derived计数、3份accepted数组删除[0,1)及3份rejected数组增加该段。新旧UUID集合相等；没有将摘要变化或显示相同身份当成可在同一真实幂等槽位重写的授权。[F01 L305171](D:/CP6-archives/consolidation-20261010/objects/5dbc9a77c6468acfd644623f1aebc44982388fbbc122bc8f9d338acfd2057708.json:305171)

## 6. 原件、封套与发布边界

物理行封套的table/key、TenantId/EnvironmentId/Mode、专用身份列以及BodyJson须相互一致。BodyDigest覆盖解析前对应的canonical业务正文；Command请求正文、保存的requestJcs与输入摘要对应，CommandTerminal是完整响应，computedResponseDigest覆盖整个响应，而terminalDigest覆盖result。QA-POL Domain/SourceContext是ref+body证明结构，其catalog digest覆盖内层body，不能误算完整证明封套。[F01 L57465](D:/CP6-archives/consolidation-20261010/objects/5dbc9a77c6468acfd644623f1aebc44982388fbbc122bc8f9d338acfd2057708.json:57465) [F01 L279724](D:/CP6-archives/consolidation-20261010/objects/5dbc9a77c6468acfd644623f1aebc44982388fbbc122bc8f9d338acfd2057708.json:279724)

最终DeliveryPayload是MES_EXEC_QUALITY_DISPOSITION_PROPOSED_V1 / EXEC-QA-DISPOSITION-PROPOSED-1：保存原工单/工序/来源scope、完整接受和拒绝区间、QA批准、覆盖、Task4及DEMO环境。held/rework/unexamined均空；DispositionIdentity.state=PROPOSED。此checkpoint没有MES消费回执、Stock过账或跨Owner业务效果，不能将本域COMMITTED或payload存在显示为制造端已应用。[F01 L242448](D:/CP6-archives/consolidation-20261010/objects/5dbc9a77c6468acfd644623f1aebc44982388fbbc122bc8f9d338acfd2057708.json:242448)

## 7. 内嵌Schema与实际关系模型的边界

两个世界内嵌同一191类型；当前canonical总集为192。182个定义在仅剔除$id后与当前同名定义结构/约束相等，仍保留原recovery-r3元数据；另9个定义已单独阅读。OutReceiptMapping及OutQualitySource.receiptMappings内缺当前receiptSubject字段及required，不能拿这两个旧形状生成当前Owner提交DTO。其余7个为QapDomainAccepted、QapPhysicalMembersAccepted、QapPlanContentAccepted、QapPopulationAccepted、QapSourceContextAccepted、QapStandardApprovalAccepted及SampleSelectionEvidence，须按原证明包装/字段准确适配，不伪装与总集同名逐字相等。[F01 L20885](D:/CP6-archives/consolidation-20261010/objects/5dbc9a77c6468acfd644623f1aebc44982388fbbc122bc8f9d338acfd2057708.json:20885) [F01 L20105](D:/CP6-archives/consolidation-20261010/objects/5dbc9a77c6468acfd644623f1aebc44982388fbbc122bc8f9d338acfd2057708.json:20105)
当前receiptSubject权威定义见[canonical L1186](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/ec/ec15532bb8820016__CANONICAL_CONTRACT_RECOVERY_R4_SYSTEMIC.json:1186)。当前总集另有8个未在本fixture声明的类型，包括WriterEpochTransitionCommand/Outcome；不能用本fixture覆盖全模块采用/迁移能力。

本册37族行映像是checkpoint表示，不是49表SQL完整插入样本。例如ReviewHead在fixture以ReviewHead|ReviewId命名；关系模型的PK为TenantId/EnvironmentId/Mode/TaskId，并要求真实rowversion。两者需保持角色映射，不能把fixture字符串key直接当数据库唯一键，或以缺失的SQL字段推演其已运行。[F01 L267187](D:/CP6-archives/consolidation-20261010/objects/5dbc9a77c6468acfd644623f1aebc44982388fbbc122bc8f9d338acfd2057708.json:267187) [关系模型ReviewHead](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/41/41a16ac59112f143__RELATIONAL_SCHEMA_RECOVERY_R4_SYSTEMIC.json:4723)

## 8. 作者验证、四份变体清单与本次核对

文件末validation记作者baseReplay/legalVariantReplay=true、70个mutation拒绝、1个wire拒绝（"5.00"冗余尾零）、2个时点边界拒绝（validTo等于observedAt、offset起点晚于observedAt）。覆盖封套身份、物理FK、原件角色/可见性、重算后跨域错链、检验与复核角色、全slot守恒、时点与环境隔离；这些是原文件的验证记录，本次未执行作者程序。[F01 L610385](D:/CP6-archives/consolidation-20261010/objects/5dbc9a77c6468acfd644623f1aebc44982388fbbc122bc8f9d338acfd2057708.json:610385)

variantManifest另登记FAIL_TO_PASS_91_9（91/9）、LOWER_BOUNDARY_0（90/10）、UPPER_BOUNDARY_10（90/10）、OVER_BOUNDARY_10_00000001（89/11）四个独立文件及SHA/bytes/replayed。此次已读这些身份和作者断言，未据这份manifest声称四个独立文件正文已读；它们不等于本文件legalVariant(value11)。[F01 L610340](D:/CP6-archives/consolidation-20261010/objects/5dbc9a77c6468acfd644623f1aebc44982388fbbc122bc8f9d338acfd2057708.json:610340)

本次自写标准库核对限于文档数据：16步的before→writeSet→after一致，1290个唯一物理行版本的正文摘要一致，两个世界1150份catalog原件的身份与适用摘要一致；200成员各22条范围/身份关联断言无差；全部最终行/摘要及内部创建/可见性集合吻合。没有执行数据库事务、作者replay、变异测试或业务AC。

## 9. 阅读证据

覆盖用字段列视图、相同子树复用及全部变体差量表达；包括191 Schema的复用/差量、全部16请求响应、各37族行封套、261外部/314内部原件每世界、全部业务值、资格时点、最终状态以及manifest/validation。UUID/hash的每个合成字面量未逐字人工阅读，原值保留并按完整字节/键关系机械核对；不将48MB原行数称为逐行实读。

[独立阅读账](D:/CP6/docs/CP6_开发设计文档_20261010/evidence/QA-IPQC-01-F01-reading.json)保留源SHA、行段/指针、差量、静态核对范围与作者运行界限。主册/主台账由根合并；本册不变更当前总模块阅读状态。
