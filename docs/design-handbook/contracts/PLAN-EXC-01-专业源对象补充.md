# PLAN-EXC-01 专业源对象补充

本册只补充六份 CP12 A/C 专业材料中的来源、精确对象和有边界的消费语义。A 研究说明不是专业采用签字；C 的 qualified read 也不授予 B 的存储、CAS、FK、提交或原 Owner 的源事实权限。真正的专业意见、根接受、归档资格和 actual adoption 分别由[版本与归档资格](D:/CP6/docs/CP6_开发设计文档_20261010/contracts/PLAN-EXC-01-版本与归档资格.md)与根台账处理。本次没有运行归档脚本、checker、SQL、应用或签名流程。[A 研究全文](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/de/de3aa3bc0420031c__CP12_A_current_scope_identity_role_dependency_research_20261006.md:1) [C 消费者边界](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/46/4666bfca590a7149__CP12_C_417_MUTABLE_READER_IDENTITIES_AND_FOUR_FULL_CHANGES.json:3)

## 1. 三种“相同”不能混为一谈

A 的六类比较必须保留原基线：当前完整合同对象相同、旧 CP10 到当前的原事实变动、前一 CP11 到当前 CP12 的十对象变动，并不是同一个比较。A 研究原文报告：103 个分配范围为81叶、15 Owner 原生聚合和PC01–PC07；相对 CP10，A 的547原事实中29变化，整体633中36变化；相对CP11则十对象变化。这些历史比较数量在本册作为来源声称保留，没有重新审计旧 CP10 全部547对象。[A 基线和复用边界](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/de/de3aa3bc0420031c__CP12_A_current_scope_identity_role_dependency_research_20261006.md:21)

本次自行逐完整 JSON 值核对的范围是：81叶与当前 EXC_OWNER_LEAF_CONTRACTS 的对应全文、7提案与当前 EXC_OWNER_PROPOSALS_AND_ADOPTION_GATES 对应全文、15聚合与当前 native registry 的合同全文全部相同；A parent/case 包的13处原事实、C source 包的10处原事实也逐完整对象等于当前633 registry。只有这些精确相同对象复用已经记录的语义阅读，未把历史 PASS 当作阅读。81叶/7提案的语义复用根的[Owner 阅读证据](D:/CP6/docs/CP6_开发设计文档_20261010/evidence/PLAN-EXC-01-owner-cursor-reading.json)，15聚合与原事实复用[源事实阅读证据](D:/CP6/docs/CP6_开发设计文档_20261010/evidence/PLAN-EXC-01-source-reading.json)。[81 个完整叶对象](D:/CP6-archives/consolidation-20261010/root-planning-cache/plan-exc-cp12/source-public/professional-source/555de87d358db68a__CP12_A_COMPLETE_SELECTED_CURRENT_CONTRACT_OBJECTS_20261006.json:24) [7 个完整提案](D:/CP6-archives/consolidation-20261010/root-planning-cache/plan-exc-cp12/source-public/professional-source/555de87d358db68a__CP12_A_COMPLETE_SELECTED_CURRENT_CONTRACT_OBJECTS_20261006.json:115565) [15 个完整聚合](D:/CP6-archives/consolidation-20261010/root-planning-cache/plan-exc-cp12/source-public/professional-source/555de87d358db68a__CP12_A_COMPLETE_SELECTED_CURRENT_CONTRACT_OBJECTS_20261006.json:171664) [A 当前10源对象](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/89/89836b4e5c94b3e2__CP12_A_parent_pair_case_key_exact_evidence.json:1906) [C 当前10源对象](D:/CP6-archives/consolidation-20261010/root-planning-cache/plan-exc-cp12/source-public/professional-source/2fed58a39af8859e__CP12_C_NEW_SOURCE10_COMPLETE_OBJECTS.json:2)

合同 body 相同并不自动延长旧专业采用到新的 native、stored、consumer、public 或 read-cut 身份。本册也不签署任何新的专业采用；“已完成精确阅读复用”只表示没有重复阅读完全相同的合同正文。[A 合同阅读与采用的区别](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/de/de3aa3bc0420031c__CP12_A_current_scope_identity_role_dependency_research_20261006.md:9)

## 2. 当前父派生、Case 与原读取上下文

材料派生自身版本为 CP11-A，其完整 parentSuggestionRef 和 parentSuggestionVersion 精确指向父建议 CP09-A；不能因为二者版本字符串不同就认定不一致。A 的 parentNative/derivedNative 与当前原对象完全相同。本次再次核对的是精确父引用，而非按相同版本名或显示标签配对。[完整父建议](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/89/89836b4e5c94b3e2__CP12_A_parent_pair_case_key_exact_evidence.json:466) [完整派生](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/89/89836b4e5c94b3e2__CP12_A_parent_pair_case_key_exact_evidence.json:1175)

CP12 的十源对象包括 MATERIAL_NEW 的三个 QUERY、三个 PAGE、三个 CAPTURE 和一个 CASE。各 Owner PAGE/CAPTURE 属于 SUP/MRP/POL 原责任；QUERY/CASE 属于 EXC 原责任。新 Case 上下文沿 Page/Capture 的精确引用传递，不创建新的 Run、Attempt、发布或原协调 work。原 actor、slot、scope 与精确请求关系仍须保留。[10 个对象的原 issuer/codec/aggregate](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/65/6507eed294768dc8__CP12_C_NEW_SOURCE10_AND_OWNER_CONTEXT_RELATIONS.json:2) [三组原 Owner 上下文关系](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/65/6507eed294768dc8__CP12_C_NEW_SOURCE10_AND_OWNER_CONTEXT_RELATIONS.json:284)

材料新 Case 的 immutable Id 为 58c677ff-58ac-5e49-9096-ace544271de5，连接独立 CaseOrigin 以及 InitialCauseFact；其初因是 MAT_NET170_OUTPUT。该原 SourceFact 的 RecordedAt 为18:20:08Z，精确 VersionKey 的 RecordedAt 为18:20:07Z。VersionKey 的 namespace、原字节435031312d41、byteLength6、显示 CP11-A 和 body digest 都要一起保留。显示文字只是辅助，不能替代原字节身份或具体出现时间。[A 精确 key 与时钟](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/89/89836b4e5c94b3e2__CP12_A_parent_pair_case_key_exact_evidence.json:2) [实际静态 Case 行](D:/CP6-archives/consolidation-20261010/root-planning-cache/plan-exc-cp12/source-public/professional-source/2fed58a39af8859e__CP12_C_NEW_SOURCE10_COMPLETE_OBJECTS.json:29070) [初因源事实行](D:/CP6-archives/consolidation-20261010/root-planning-cache/plan-exc-cp12/source-public/professional-source/2fed58a39af8859e__CP12_C_NEW_SOURCE10_COMPLETE_OBJECTS.json:29104) [精确版本键行](D:/CP6-archives/consolidation-20261010/root-planning-cache/plan-exc-cp12/source-public/professional-source/2fed58a39af8859e__CP12_C_NEW_SOURCE10_COMPLETE_OBJECTS.json:29598) [稳定责任来源行](D:/CP6-archives/consolidation-20261010/root-planning-cache/plan-exc-cp12/source-public/professional-source/2fed58a39af8859e__CP12_C_NEW_SOURCE10_COMPLETE_OBJECTS.json:57813)

这里“actualImmutableCase”“actualCorrectKey”等字段名表示专业材料选中的具体设计对象，不表示生产数据库中已经存在这些行。所有实际采用、授权及运行资格仍沿当前组合的 actual0/NOT_RUN/UNPROVEN 边界；本次没有执行数据库读取或写入。[A 研究状态边界](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/de/de3aa3bc0420031c__CP12_A_current_scope_identity_role_dependency_research_20261006.md:1) [C qualified read 边界](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/46/4666bfca590a7149__CP12_C_417_MUTABLE_READER_IDENTITIES_AND_FOUR_FULL_CHANGES.json:3)

## 3. 原事实到存储的三对象关系

C 的 source10 包给每个新源对象保存 SourceFact、SourceArtifact、SourceProvenance 三行。此次逐组核对：SourceFact.canonicalBody 等于原 native body；SourceArtifact.RawUtf8 的十六进制解码等于原 nativeUtf8，长度与SHA相等，NativeCanonical 解码也相等；SourceProvenance.canonicalBody 等于原 trust；Fact 指向 Artifact/Provenance 的 ID+digest 与对应行一致。这是静态对象映射核对，不是数据库 FK、权限或事务执行验证。[10 组三对象全文](D:/CP6-archives/consolidation-20261010/root-planning-cache/plan-exc-cp12/source-public/professional-source/2fed58a39af8859e__CP12_C_NEW_SOURCE10_COMPLETE_OBJECTS.json:29635) [关系与消费限定](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/65/6507eed294768dc8__CP12_C_NEW_SOURCE10_AND_OWNER_CONTEXT_RELATIONS.json:489)

SourceFact 保存原 Owner/Kind、StableKey/VersionKey、Artifact/Provenance、NativeCanonicalDigest、StateForm、OccurredAt、FirstReceivedAt 与前驱对。发生时间未知时 OccurredAt=null，不用接收时间填充。SourceArtifact 保存实际原字节、媒体类型、Schema版本、canonicalization、原/native digest 和私有存储定位。SourceProvenance 保存实际 issuer、qualification、原 authority 和时间，不因 trustState 的静态标签而获得真实 Provider 资格。完整字段类型仍见数据附册。[Case 源三对象实例](D:/CP6-archives/consolidation-20261010/root-planning-cache/plan-exc-cp12/source-public/professional-source/2fed58a39af8859e__CP12_C_NEW_SOURCE10_COMPLETE_OBJECTS.json:38930)

C 包的 actualSelectedSourceFacts 是空占位对象，但 actualSelectedSourceNormalizationTriples 有10组非空完整三对象；不得从该空占位推断“没有存储源事实”。本次另将这30行以及 Case、初因、正确Key、CaseOrigin 四行，共34个完整对象，与当前 physical registry 对应完整行逐值核对相同。完整 physical registry 的其他行与事务语义由根独立阅读。[空占位字段](D:/CP6-archives/consolidation-20261010/root-planning-cache/plan-exc-cp12/source-public/professional-source/2fed58a39af8859e__CP12_C_NEW_SOURCE10_COMPLETE_OBJECTS.json:29634) [完整三对象集合](D:/CP6-archives/consolidation-20261010/root-planning-cache/plan-exc-cp12/source-public/professional-source/2fed58a39af8859e__CP12_C_NEW_SOURCE10_COMPLETE_OBJECTS.json:29635)

## 4. 417 个可变读取身份与四个设计后继

C 的417条关系每条保留 table、复合 primaryKey、RowVersion、旧/新 snapshotId，以及同一主键/时钟、完整body是否相同和 consumerOnlyNoBStorageAdoption。当前417条的主键与RowVersion已逐个核对当前 local registry；有14类表。旧413项 whole-body相同是专业原记录的比较结论，本册未独立重读前一版完整 local registry，因此不把该413结论改称本次重新验证。[417 条具体身份关系](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/46/4666bfca590a7149__CP12_C_417_MUTABLE_READER_IDENTITIES_AND_FOUR_FULL_CHANGES.json:17)

四个完整前后 body 则已独立读取并比较，当前 body 又逐个等于当前 local registry：

| 设计对象 | 本次实读到的差量 | 保留字段 |
|---|---|---|
| 旧材料 CaseHead | RevisionId、RevisionDigest 以及派生 BodyCanonical | 主键、RowVersion、RecordedAt、Case、BusinessRevision1、WriterEpoch9、LocalCommitSeq139 |
| 旧材料 ProjectionHead | RevisionId、RevisionDigest 以及派生 BodyCanonical | 主键、RowVersion、RecordedAt、Case、ProjectionKind=CASE_LIST、ProjectionVersion1 |
| 新材料 CaseHead | CaseId/CaseDigest 与 RevisionId/RevisionDigest 以及派生 BodyCanonical | 主键、RowVersion、RecordedAt、BusinessRevision1、WriterEpoch9、LocalCommitSeq140 |
| 新材料 ProjectionHead | CaseId/CaseDigest 与 RevisionId/RevisionDigest 以及派生 BodyCanonical | 主键、RowVersion、RecordedAt、ProjectionKind=CASE_LIST、ProjectionVersion1 |

这些是两个冻结文档版本之间的静态后继传播。它们不授权应用修改数据库行而维持旧 RowVersion；实际 Provider 的8字节行版本、CAS和事务责任依然按 B 存储合同执行。BodyCanonical 的展示副本亦不能误作额外持久列。[四个完整前后 body](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/46/4666bfca590a7149__CP12_C_417_MUTABLE_READER_IDENTITIES_AND_FOUR_FULL_CHANGES.json:6274) [数据与类型](D:/CP6/docs/CP6_开发设计文档_20261010/contracts/PLAN-EXC-01-数据与类型.md)

## 5. 对开发入口的影响与阅读边界

开发者应继续使用 CP12 精确组合，保留父版本 CP09-A、派生版本 CP11-A、Case/Query/Page/Capture 后继 CP12-A 各自的用途。当前合同重复可在字节/完整JSON值相同后复用；有改变的具体源对象、存储引用和公开 cut 仍需要自己的采用与消费边界。SUP/MRP 是 direct RequiredFenced，POL 是继承 RequiredFenced；PC06 是原 slot 的 read-existing，不能 query-by-create；PC07 的历史发行事实与 fresh 接收/用途授权分开。[A PC01–PC07 职责](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/de/de3aa3bc0420031c__CP12_A_current_scope_identity_role_dependency_research_20261006.md:35)

这六份未引入新的独立业务设计正文，也未发现必须新增的静态差异项。它们没有关闭已登记的 PLAN-IF-007/008，更不把根的 local/physical/AC 剩余阅读升级为已完成。详细 SHA、压缩原件→只读展开路径、精确行段、逐对象复用映射与来源声称边界见[六份专业材料阅读证据](D:/CP6/docs/CP6_开发设计文档_20261010/evidence/PLAN-EXC-01-professional-source-reading.json)。

两份 gzip 只作标准库只读解压到分析缓存；展开文本保留原解压字节，未执行其中内容。A 研究57行全文读；其余五份为当前对象语义阅读、完整值相等复用及所有差量字段阅读，未声称按行阅读全部重复 raw JSON 字符串。
