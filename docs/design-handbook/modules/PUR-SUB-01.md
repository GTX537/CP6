# PUR-SUB-01 委外加工、发料与成本对账

## 1. 业务目的、操作者与责任边界

采购员关联准确既有委外PO行，技术/采购人员采用材料支给计划；有权仓储执行人员发料，采购按真实GR收回和QA结果观察，财务人员预览并提交累计成本，业务人员按固定单耗及商业验收量对账、显式建立调查。商业关联、物理发料/退料、供应商托管/真实消耗、成品收回/验收和成本结算各有独立事实，不能压成一个“已完成”。 [原文 L55](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/95/951bf766a6f96118__PUR-SUB-01_完整静态详细设计_RECOVERY-R2_20261008.md:55>)、[原文 L57](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/95/951bf766a6f96118__PUR-SUB-01_完整静态详细设计_RECOVERY-R2_20261008.md:57>)、[原文 L59](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/95/951bf766a6f96118__PUR-SUB-01_完整静态详细设计_RECOVERY-R2_20261008.md:59>)、[原文 L61](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/95/951bf766a6f96118__PUR-SUB-01_完整静态详细设计_RECOVERY-R2_20261008.md:61>)、[原文 L73](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/95/951bf766a6f96118__PUR-SUB-01_完整静态详细设计_RECOVERY-R2_20261008.md:73>)、[原文 L75](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/95/951bf766a6f96118__PUR-SUB-01_完整静态详细设计_RECOVERY-R2_20261008.md:75>)、[原文 L286](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/95/951bf766a6f96118__PUR-SUB-01_完整静态详细设计_RECOVERY-R2_20261008.md:286>)

委外保持采购商业归属，不为接自制链而制造WO、EXEC或OUTPUT。PO是原商业Owner，WMS拥有物理移动与托管事实，GR/PO拥有收回事实，QA拥有质量判断，Finance拥有成本/加工费应付及更正。材料退回、成品退供应商、原物理错误更正和财务冲销是不同后继，不用负发料或重复收货代替。 [原文 L55](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/95/951bf766a6f96118__PUR-SUB-01_完整静态详细设计_RECOVERY-R2_20261008.md:55>)、[原文 L57](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/95/951bf766a6f96118__PUR-SUB-01_完整静态详细设计_RECOVERY-R2_20261008.md:57>)、[原文 L198](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/95/951bf766a6f96118__PUR-SUB-01_完整静态详细设计_RECOVERY-R2_20261008.md:198>)、[原文 L220](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/95/951bf766a6f96118__PUR-SUB-01_完整静态详细设计_RECOVERY-R2_20261008.md:220>)

保留旧正确且适用的能力需要明确profile：正剩余发料、按实际出库累加、单库存行策略、旧基准成本公式、严格超容差比较；既有代码不是必须保持的接受条件。纸、墨只属于原代码例，不能把所有委外强制建成印刷BOM。所需工程、单位、耗用和成本政策由专业Owner采用。 [原文 L39](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/95/951bf766a6f96118__PUR-SUB-01_完整静态详细设计_RECOVERY-R2_20261008.md:39>)、[原文 L41](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/95/951bf766a6f96118__PUR-SUB-01_完整静态详细设计_RECOVERY-R2_20261008.md:41>)、[原文 L43](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/95/951bf766a6f96118__PUR-SUB-01_完整静态详细设计_RECOVERY-R2_20261008.md:43>)、[原文 L47](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/95/951bf766a6f96118__PUR-SUB-01_完整静态详细设计_RECOVERY-R2_20261008.md:47>)、[原文 L51](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/95/951bf766a6f96118__PUR-SUB-01_完整静态详细设计_RECOVERY-R2_20261008.md:51>)

## 2. 当前有效版本、共同接受与历史评审

| 材料 | SHA-256 | 效力 | 出处 |
| --- | --- | --- | --- |
| SUB RECOVERY-R2 | 951bf766a6f96118cebbc63d7a633875314dab96c5d48ed50bc818d20ab77f51 | 56310字节／395行，4SPEC、48AC、12任务 | [原文 L1](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/95/951bf766a6f96118__PUR-SUB-01_完整静态详细设计_RECOVERY-R2_20261008.md:1>) |
| 当前CURRENT | 8e317fd970163ec20a0720230c013da080c78c7379841f99b7702122318848d7 | Stage100静态详细设计接受，不是实际实施 | [原文 L1](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/8e/8e317fd970163ec2__CURRENT.json:1>) |
| SUB准确UA | 198b0ee808eb7f01ef6fc162b106b7187b5b97675ca1dfdd5790a51567a63695 | 2026-10-08T11:39:09Z授权代理root接受 | [原文 L1](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/19/198b0ee808eb7f01__UA-20261008-X2-PUR-SUB-01-R2-STATIC-MD02.json:1>) |
| 三目标组合 | 56686a14c436f4e6bda87ddd9e53608dbdd0c8ba7e35d3ad5cddef48a386b9e7 | SUB R2、SCHED R2、PERF R2.1精确组合 | [原文 L1](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/56/56686a14c436f4e6__EXACT-ACCEPTED-COMPOSITION.json:1>) |
| 共同接受 | 89d2585b7cd327a4719b9cd66fa2205299d3676ddc5b1e1f6bfa7a677da5ac5c | 3目标／12SPEC／132AC范围 | [原文 L1](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/89/89d2585b7cd327a4__THREE-TARGET-ACCEPTANCE.md:1>) |
| R1完整独审 | 255258742277341c2f710e4eda9f5cf69952cbe470583b7dac52df606bf3fb2f | 历史五根CHANGES_REQUIRED，保历史不改PASS | [原文 L1](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/25/255258742277341c__CP6_X2_原12SPEC_独立完整静态审阅及最小根表_20261008.md:1>) |
| R2累计独审 | dc82fc44306d5773d2bdf937d3baadde5d8828e43ef7a31c3a006c5de2808f02 | SUB两根已闭；历史仅PERF R05-a待修 | [原文 L1](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/dc/dc82fc44306d5773__CP6_X2_R2_同五根复审与原12SPEC累计结论_20261008.md:1>) |
| R2.1最终独审 | 79e83e4c1789b8a38c818a1c9d1c0cb047512f7c0ad7d4e179bb1b4343f2a6b8 | PERF收口和原12SPEC累计可接受 | [原文 L1](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/79/79e83e4c1789b8a3__FINAL-R2.1-CUMULATIVE-INDEPENDENT-REVIEW.md:1>) |
| 准确限定 | a8b358659d1b9d83e3745183f704266182ea312fdd5398c69897ccca0dc6f6d5 | 跨行业范围、源代码与运行边界 | [原文 L1](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/a8/a8b358659d1b9d83__STATIC-ACCEPTANCE-QUALIFICATIONS.json:1>) |

SUB正文与R2复审字节一致；R2.1只修改PERF，不能把SCHED/PERF后继混写成SUB的新修订。原作者STOPPED/PENDING、原编目NOT_DESIGNED和历史RETURN保留原意，后继根接受决定当前静态效力。root以USER_AUTHORIZED_DELEGATE身份接受，不是用户亲笔签名，也不声称用户亲读；MessageId为null，使用有日期的实际决策证据。 [原文 L1](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/f6/f6639ba72b420ca6__ROOT-DECISION-EVIDENCE.json:1>)

R1-R01缺公共apply/唯一PlanWork/材料保护/送前取消竞争，R01由R2正文§5.2关闭；R1-R02验收与实收口径/PO变版分母不稳，由固定COMMERCIAL_ACCEPTANCE_GROSS和ConsumptionBasis关闭。这两个历史缺陷不得再列当前文书新缺陷。原132AC及本册48AC均NOT_RUN、执行0；真实Source/Owner、共同事务、物理效果、性能与实现UNPROVEN。 [原文 L54](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/79/79e83e4c1789b8a3__FINAL-R2.1-CUMULATIVE-INDEPENDENT-REVIEW.md:54>)、[原文 L55](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/79/79e83e4c1789b8a3__FINAL-R2.1-CUMULATIVE-INDEPENDENT-REVIEW.md:55>)、[原文 L66](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/79/79e83e4c1789b8a3__FINAL-R2.1-CUMULATIVE-INDEPENDENT-REVIEW.md:66>)、[原文 L67](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/79/79e83e4c1789b8a3__FINAL-R2.1-CUMULATIVE-INDEPENDENT-REVIEW.md:67>)、[原文 L68](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/79/79e83e4c1789b8a3__FINAL-R2.1-CUMULATIVE-INDEPENDENT-REVIEW.md:68>)、[原文 L69](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/79/79e83e4c1789b8a3__FINAL-R2.1-CUMULATIVE-INDEPENDENT-REVIEW.md:69>)、[原文 L85](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/79/79e83e4c1789b8a3__FINAL-R2.1-CUMULATIVE-INDEPENDENT-REVIEW.md:85>)、[原文 L89](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/79/79e83e4c1789b8a3__FINAL-R2.1-CUMULATIVE-INDEPENDENT-REVIEW.md:89>)

## 3. 实体、字段、身份和准确数值类型

根API是/api/pur/sub/v1。以下为Required目标合同，不能冒旧端点已实现。Tenant/Actor/代理从可信认证上下文取；Id小写UUID，Seq为Int64十进制字符串，Version为不透明CAS，Instant带时区且持久UTC。Qty decimal(21,8)字符串，UnitCost原18,4；桥接PO Qty6须准确可表达，不能截整，不能用JS number算金额。 [原文 L81](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/95/951bf766a6f96118__PUR-SUB-01_完整静态详细设计_RECOVERY-R2_20261008.md:81>)

| 字段 | 必填/默认/编辑性 | 校验与来源 | 出处 |
| --- | --- | --- | --- |
| siteId | 查询/准备必填 | IAM可选且与PO一致 | [原文 L85](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/95/951bf766a6f96118__PUR-SUB-01_完整静态详细设计_RECOVERY-R2_20261008.md:85>) |
| poRef,poLineId,scheduleId,sourcePortionIds | 关联必填，只选原Owner结果 | orderKind=SUBCONTRACT；范围同一行；不以PoNo拼键 | [原文 L86](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/95/951bf766a6f96118__PUR-SUB-01_完整静态详细设计_RECOVERY-R2_20261008.md:86>) |
| subcontractId,lineId,version | 服务只读 | Tenant+POline唯一活动关联；历史后继保留 | [原文 L87](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/95/951bf766a6f96118__PUR-SUB-01_完整静态详细设计_RECOVERY-R2_20261008.md:87>) |
| supplierRef,finishedItemRef,uomRef | PO只读 | 变更由PO正规流程，不在SUB随意编辑 | [原文 L88](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/95/951bf766a6f96118__PUR-SUB-01_完整静态详细设计_RECOVERY-R2_20261008.md:88>) |
| technicalBasisRef,consumptionBasisRef | 正式支给必需 | 工程/BOM或获准手工单耗证据；后者须解析§7.3固定成品计划量、单位、PO范围/版和逐料单耗；未知草稿可null | [原文 L89](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/95/951bf766a6f96118__PUR-SUB-01_完整静态详细设计_RECOVERY-R2_20261008.md:89>) |
| materialLineId,itemRef,uomRef | 每支给行必需 | 同物料不同批/用途须明确独立purposeKey，不误合并 | [原文 L90](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/95/951bf766a6f96118__PUR-SUB-01_完整静态详细设计_RECOVERY-R2_20261008.md:90>) |
| plannedConsignQty | Qty>0正式必填 | 旧原量独立保存；已发后变更须新修订与影响评估 | [原文 L91](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/95/951bf766a6f96118__PUR-SUB-01_完整静态详细设计_RECOVERY-R2_20261008.md:91>) |
| unitCost,currencyRef,valuationRef | 成本有权且正式结算必需 | 货币/估值日期/来源；0须零成本有效证明，缺失不是0 | [原文 L92](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/95/951bf766a6f96118__PUR-SUB-01_完整静态详细设计_RECOVERY-R2_20261008.md:92>) |
| issuedGross,returnedPhysical,currentCustody,consumedKnown | 全部只读、可null | 不同Owner含义分列；没有消耗证据不以发出量代消耗 | [原文 L93](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/95/951bf766a6f96118__PUR-SUB-01_完整静态详细设计_RECOVERY-R2_20261008.md:93>) |
| receiptGross,accepted,rejected,returnedFinished | 只读 | 同ReceiptSlice多事实不重复计数；coverage分别显示 | [原文 L94](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/95/951bf766a6f96118__PUR-SUB-01_完整静态详细设计_RECOVERY-R2_20261008.md:94>) |
| toleranceRate | Rate 0..1，无用户隐式默认正式版 | 草稿可预填旧0.05并标未采用；正式依据policyRef | [原文 L95](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/95/951bf766a6f96118__PUR-SUB-01_完整静态详细设计_RECOVERY-R2_20261008.md:95>) |
| reason | 每写必填1..1000 | 不代替技术/财务批准 | [原文 L96](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/95/951bf766a6f96118__PUR-SUB-01_完整静态详细设计_RECOVERY-R2_20261008.md:96>) |
| expectedVersion,expectedPoVersion,expectedManifestDigest | 正式写必填 | 以准备保存值为准，不自动刷新后重试 | [原文 L97](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/95/951bf766a6f96118__PUR-SUB-01_完整静态详细设计_RECOVERY-R2_20261008.md:97>) |

| 持久对象 | 具体保存与身份责任 | 出处 |
| --- | --- | --- |
| SubcontractLink | 准确PO行、Site/Supplier/产物、关联版本和原商业cut；Tenant＋POline唯一活动关联 | [原文 L99](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/95/951bf766a6f96118__PUR-SUB-01_完整静态详细设计_RECOVERY-R2_20261008.md:99>) [原文 L130](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/95/951bf766a6f96118__PUR-SUB-01_完整静态详细设计_RECOVERY-R2_20261008.md:130>) |
| ConsignPlan／Revision | 不可变完整材料集合、技术/用途/单位/成本来源；固定ConsumptionBasis和后继理由 | [原文 L99](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/95/951bf766a6f96118__PUR-SUB-01_完整静态详细设计_RECOVERY-R2_20261008.md:99>) [原文 L134](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/95/951bf766a6f96118__PUR-SUB-01_完整静态详细设计_RECOVERY-R2_20261008.md:134>) |
| MaterialFactCurrent／Version | WMS原事实及最新合法版本投影；不能以旧WmsIssueNo末值推全部历史 | [原文 L99](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/95/951bf766a6f96118__PUR-SUB-01_完整静态详细设计_RECOVERY-R2_20261008.md:99>) [原文 L103](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/95/951bf766a6f96118__PUR-SUB-01_完整静态详细设计_RECOVERY-R2_20261008.md:103>) |
| IssuePlan | 准确料量、来源策略、digest、有效期、完整腿manifest、acceptedWorkId；UQ(Tenant,PlanId) | [原文 L99](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/95/951bf766a6f96118__PUR-SUB-01_完整静态详细设计_RECOVERY-R2_20261008.md:99>) [原文 L101](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/95/951bf766a6f96118__PUR-SUB-01_完整静态详细设计_RECOVERY-R2_20261008.md:101>) |
| OwnerWork／Leg | Owner原operationKey、payload、协议、发送/查询、commitKnowledge；Leg revision／dispatchState | [原文 L99](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/95/951bf766a6f96118__PUR-SUB-01_完整静态详细设计_RECOVERY-R2_20261008.md:99>) [原文 L101](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/95/951bf766a6f96118__PUR-SUB-01_完整静态详细设计_RECOVERY-R2_20261008.md:101>) |
| MaterialIssueProtection | Work/Leg、Qty/Unit、保护状态与解除证据；与真实发料贡献同域事务转换 | [原文 L101](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/95/951bf766a6f96118__PUR-SUB-01_完整静态详细设计_RECOVERY-R2_20261008.md:101>) |
| ReceiptProjection | 原PoFactIdentity／ReceiptSlice的版本及完整分配，Received/Accepted/Rejected/Return分列 | [原文 L99](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/95/951bf766a6f96118__PUR-SUB-01_完整静态详细设计_RECOVERY-R2_20261008.md:99>) [原文 L208](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/95/951bf766a6f96118__PUR-SUB-01_完整静态详细设计_RECOVERY-R2_20261008.md:208>) |
| CostBasis／Candidate／Settlement | 不可变批准计价版、Receipt集合、累计目标与Finance已确认前驱/结果 | [原文 L99](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/95/951bf766a6f96118__PUR-SUB-01_完整静态详细设计_RECOVERY-R2_20261008.md:99>) [原文 L230](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/95/951bf766a6f96118__PUR-SUB-01_完整静态详细设计_RECOVERY-R2_20261008.md:230>) |
| ReconciliationSnapshot／Investigation | 可重现固定Qplan/PO版/ACCEPTANCE cut和待检解释，调查及真实Owner处置 | [原文 L99](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/95/951bf766a6f96118__PUR-SUB-01_完整静态详细设计_RECOVERY-R2_20261008.md:99>) [原文 L101](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/95/951bf766a6f96118__PUR-SUB-01_完整静态详细设计_RECOVERY-R2_20261008.md:101>) |
| Ledger／Inbox／Audit／Outbox | 原命令、不可变事实知识、审计和通知；本域成功写一次事务 | [原文 L99](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/95/951bf766a6f96118__PUR-SUB-01_完整静态详细设计_RECOVERY-R2_20261008.md:99>) [原文 L303](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/95/951bf766a6f96118__PUR-SUB-01_完整静态详细设计_RECOVERY-R2_20261008.md:303>) |

不以PoNo/LineNo/ItemId拼出新域身份，不把旧末次发料号扩成虚构全链。旧值无法补证为LEGACY_PARTIAL；received、accepted、issuedGross、returnedPhysical、currentCustody和consumedKnown各自可以未知，没有消耗证据不将发出量当已消耗。 [原文 L93](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/95/951bf766a6f96118__PUR-SUB-01_完整静态详细设计_RECOVERY-R2_20261008.md:93>)、[原文 L94](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/95/951bf766a6f96118__PUR-SUB-01_完整静态详细设计_RECOVERY-R2_20261008.md:94>)、[原文 L103](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/95/951bf766a6f96118__PUR-SUB-01_完整静态详细设计_RECOVERY-R2_20261008.md:103>)

## 4. 正常业务流程与精确计算规则

### 商业关联与整集合支给计划

选择准确既有orderKind=SUBCONTRACT的PO行/交期/SourcePortion，核Tenant/Site/Supplier及技术版本；已有同一活动关联返回原引用。准备只是可选范围，正式apply与PO在同一真实最终Guard核当前商业版/scope/控制。工程或批准手工单耗必须能解析固定成品计划量、单位、PO范围/修订和逐料计划，不只存无法解释的Ref。 [原文 L109](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/95/951bf766a6f96118__PUR-SUB-01_完整静态详细设计_RECOVERY-R2_20261008.md:109>)、[原文 L130](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/95/951bf766a6f96118__PUR-SUB-01_完整静态详细设计_RECOVERY-R2_20261008.md:130>)、[原文 L134](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/95/951bf766a6f96118__PUR-SUB-01_完整静态详细设计_RECOVERY-R2_20261008.md:134>)

采用计划保存完整集合，省略旧行明确表示删除，与旧upsert漏项保留不同。已发或未决材料不能删除；减计划不追回IssuedQty，保超发差异并走Owner处置。改成本/计划量须后继CostBasis，不能回写已经结算版。正式计划最多200料为技术上限，超限413，客户端拆组不得绕过完整集合事务。 [原文 L132](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/95/951bf766a6f96118__PUR-SUB-01_完整静态详细设计_RECOVERY-R2_20261008.md:132>)、[原文 L136](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/95/951bf766a6f96118__PUR-SUB-01_完整静态详细设计_RECOVERY-R2_20261008.md:136>)

### 外发料和实际剩余保护

FULL_REMAINING要求items为空，由服务选全部正剩余；EXPLICIT必须非空、每个料唯一且属于计划、量为正，不能沿旧行为首匹配/跳未知。来源策略LEGACY_SINGLE_ROW_MAX_AVAILABLE按AvailableQty降、PhysicalQty降和稳定StockId平手，一条真实库存行满足整量；80＋70欲发100仍不足，不能暗拼跨行或宣称FIFO/FEFO。 [原文 L43](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/95/951bf766a6f96118__PUR-SUB-01_完整静态详细设计_RECOVERY-R2_20261008.md:43>)、[原文 L144](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/95/951bf766a6f96118__PUR-SUB-01_完整静态详细设计_RECOVERY-R2_20261008.md:144>)、[原文 L146](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/95/951bf766a6f96118__PUR-SUB-01_完整静态详细设计_RECOVERY-R2_20261008.md:146>)

正式预算核issuedGross＋所有pendingProtected＋本次request ≤ 已采用计划；SUPPLEMENT要准确scope/qty/版的额外许可，超发报警不是授权。IssuePrepare固定PER_LEG manifest、来源与每腿原Owner key；准备不占最终余量，apply仍在材料保护锁内重算。两Plan竞争同剩余最多一方受理。 [原文 L148](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/95/951bf766a6f96118__PUR-SUB-01_完整静态详细设计_RECOVERY-R2_20261008.md:148>)、[原文 L170](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/95/951bf766a6f96118__PUR-SUB-01_完整静态详细设计_RECOVERY-R2_20261008.md:170>)、[原文 L172](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/95/951bf766a6f96118__PUR-SUB-01_完整静态详细设计_RECOVERY-R2_20261008.md:172>)

apply一次本域事务保存Plan唯一Work、全部Leg/原键、MaterialIssueProtection、Ledger、Audit、Outbox。Work受理COMMITTED只是ISSUE_WORK_ACCEPTED，不能页面显示所有物料已出。同Plan异command同Actor同digest复用唯一Work，异内容冲突；同原键普通重试不新建物理动作。 [原文 L166](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/95/951bf766a6f96118__PUR-SUB-01_完整静态详细设计_RECOVERY-R2_20261008.md:166>)、[原文 L170](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/95/951bf766a6f96118__PUR-SUB-01_完整静态详细设计_RECOVERY-R2_20261008.md:170>)、[原文 L172](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/95/951bf766a6f96118__PUR-SUB-01_完整静态详细设计_RECOVERY-R2_20261008.md:172>)

dispatcher先CAS NEVER_DISPATCHED→DISPATCH_INTENT_DURABLE并持久原payload，再外调Owner。cancel-unsent选中的任一腿已送前意图就整命令409零变更；取消先赢留永久墓碑，发送先赢拒取消。continue对未发腿重新核资格；已发/未知腿仅安排原key查询，不再次CommitIssue。 [原文 L164](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/95/951bf766a6f96118__PUR-SUB-01_完整静态详细设计_RECOVERY-R2_20261008.md:164>)、[原文 L168](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/95/951bf766a6f96118__PUR-SUB-01_完整静态详细设计_RECOVERY-R2_20261008.md:168>)、[原文 L174](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/95/951bf766a6f96118__PUR-SUB-01_完整静态详细设计_RECOVERY-R2_20261008.md:174>)、[原文 L176](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/95/951bf766a6f96118__PUR-SUB-01_完整静态详细设计_RECOVERY-R2_20261008.md:176>)

Owner真实commit后同域事务记原贡献并消耗保护；实际量/来源与准备不符保真实事实并REASSESS，不能改Owner量凑计划。PER_LEG允许部分成功明确展示；UNKNOWN或OWNER_PENDING不是终态，不能释放额度、新key重做。NO_EFFECT_FINAL及真实全路径封闭才解除无效果腿。 [原文 L176](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/95/951bf766a6f96118__PUR-SUB-01_完整静态详细设计_RECOVERY-R2_20261008.md:176>)、[原文 L178](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/95/951bf766a6f96118__PUR-SUB-01_完整静态详细设计_RECOVERY-R2_20261008.md:178>)、[原文 L190](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/95/951bf766a6f96118__PUR-SUB-01_完整静态详细设计_RECOVERY-R2_20261008.md:190>)、[原文 L194](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/95/951bf766a6f96118__PUR-SUB-01_完整静态详细设计_RECOVERY-R2_20261008.md:194>)、[原文 L196](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/95/951bf766a6f96118__PUR-SUB-01_完整静态详细设计_RECOVERY-R2_20261008.md:196>)

| Work状态 | 准确含义 | 出处 |
| --- | --- | --- |
| PARTIAL | 至少一腿真实commit，另有未完成腿 | [原文 L178](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/95/951bf766a6f96118__PUR-SUB-01_完整静态详细设计_RECOVERY-R2_20261008.md:178>) |
| PAUSED | 仅未发腿当前资格不够；原已发仍查询 | [原文 L178](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/95/951bf766a6f96118__PUR-SUB-01_完整静态详细设计_RECOVERY-R2_20261008.md:178>) |
| COMPLETED | 全部腿真实COMMITTED | [原文 L178](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/95/951bf766a6f96118__PUR-SUB-01_完整静态详细设计_RECOVERY-R2_20261008.md:178>) |
| PARTIAL_FINAL | 有成功且其余全确定无效果 | [原文 L178](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/95/951bf766a6f96118__PUR-SUB-01_完整静态详细设计_RECOVERY-R2_20261008.md:178>) |
| REJECTED_NO_EFFECT | 无成功、全部确定拒绝 | [原文 L178](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/95/951bf766a6f96118__PUR-SUB-01_完整静态详细设计_RECOVERY-R2_20261008.md:178>) |
| CANCELLED_NO_EFFECT | 无成功、全部终结且至少一腿准确取消 | [原文 L178](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/95/951bf766a6f96118__PUR-SUB-01_完整静态详细设计_RECOVERY-R2_20261008.md:178>) |
| UNKNOWN／OWNER_PENDING | 原键恢复；不当0、不当终态、不重做 | [原文 L178](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/95/951bf766a6f96118__PUR-SUB-01_完整静态详细设计_RECOVERY-R2_20261008.md:178>) |

### 收回、质量与责任结清

receiptIntent只打开准确GR context，SUB不创物理入库API/Permit。GR仍核原PO ReceivingPermit与独立QA；旧缺检默认PASS不可复用。按PoFactIdentity/ReceiptSlice取完整最新分配，不把GR和WMS同一事实加两次；v3缺v2先PENDING_GAP，晚到真实事实即使PO已关仍保留为异常。 [原文 L204](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/95/951bf766a6f96118__PUR-SUB-01_完整静态详细设计_RECOVERY-R2_20261008.md:204>)、[原文 L206](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/95/951bf766a6f96118__PUR-SUB-01_完整静态详细设计_RECOVERY-R2_20261008.md:206>)、[原文 L208](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/95/951bf766a6f96118__PUR-SUB-01_完整静态详细设计_RECOVERY-R2_20261008.md:208>)、[原文 L212](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/95/951bf766a6f96118__PUR-SUB-01_完整静态详细设计_RECOVERY-R2_20261008.md:212>)、[原文 L214](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/95/951bf766a6f96118__PUR-SUB-01_完整静态详细设计_RECOVERY-R2_20261008.md:214>)

材料实退走WMS正规IN并映原IssueSlice，issuedGross不减少，returnedPhysical独列，不能把issued当consumed。成品退供应商走PUR-RETURN/WMS/Finance，真实物理退货不删Receipt gross、不自动重开PO。CloseReview只是核全部托管/退回/获准损耗、收回/质量/财务责任，无域缺证或未知才能给完整结论；它不等PO关闭或自制完成。若首版无独立有定义的apply/lookup，只发布责任汇总，不擅增“结清”效果按钮。 [原文 L198](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/95/951bf766a6f96118__PUR-SUB-01_完整静态详细设计_RECOVERY-R2_20261008.md:198>)、[原文 L220](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/95/951bf766a6f96118__PUR-SUB-01_完整静态详细设计_RECOVERY-R2_20261008.md:220>)、[原文 L222](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/95/951bf766a6f96118__PUR-SUB-01_完整静态详细设计_RECOVERY-R2_20261008.md:222>)

### 累计成本：首正收回一次带入全计划材料成本

LEGACY_FULL_MATERIAL_FIRST_POSITIVE_RECEIPT_V1是待Finance实际采用的具名计价模式，不是所有行业会计政策。CostBasis固定PO价格/单位、计划材料量成本、估值、币种、Finance舍入版；Mfull=Σ全部计划ConsignQty×unitCost，既不是已发实际量也不是按收回比例摊材料。跨币种须准确批准估值，不相加猜汇率。供应商AP仅加工费，不能再把自有支给材料付给供应商。 [原文 L47](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/95/951bf766a6f96118__PUR-SUB-01_完整静态详细设计_RECOVERY-R2_20261008.md:47>)、[原文 L49](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/95/951bf766a6f96118__PUR-SUB-01_完整静态详细设计_RECOVERY-R2_20261008.md:49>)、[原文 L228](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/95/951bf766a6f96118__PUR-SUB-01_完整静态详细设计_RECOVERY-R2_20261008.md:228>)、[原文 L230](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/95/951bf766a6f96118__PUR-SUB-01_完整静态详细设计_RECOVERY-R2_20261008.md:230>)

计价Qcum取Finance批准的完整当前有效GR RECEIPT集合；Finance政策若要求验收由它明确过滤，SUB不得猜。正Qcum时累计目标=累计加工费＋Mfull一次；不同原PO价格分段计加工费。Qcum=0只在真实全部VOID后给0目标更正候选；普通成品实退不是原Receipt VOID。按Finance边界舍入累计目标一次，再减上一已确认舍入目标得delta，不逐批重复材料或逐批舍入漂移。 [原文 L232](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/95/951bf766a6f96118__PUR-SUB-01_完整静态详细设计_RECOVERY-R2_20261008.md:232>)、[原文 L234](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/95/951bf766a6f96118__PUR-SUB-01_完整静态详细设计_RECOVERY-R2_20261008.md:234>)、[原文 L236](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/95/951bf766a6f96118__PUR-SUB-01_完整静态详细设计_RECOVERY-R2_20261008.md:236>)

| 静态例 | 正确累计与本次差额 | 禁止的混算 | 出处 |
| --- | --- | --- | --- |
| PO100件，单加工费3，全计划料成本600；首收40 | 累计720，本次delta720 | 按40%材料仅240没有该profile依据 | [原文 L238](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/95/951bf766a6f96118__PUR-SUB-01_完整静态详细设计_RECOVERY-R2_20261008.md:238>) |
| 再真实收60，累计100 | 累计900，delta180 | 第二批再600＋180=780会总记1500 | [原文 L238](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/95/951bf766a6f96118__PUR-SUB-01_完整静态详细设计_RECOVERY-R2_20261008.md:238>) |
| 原收40被真实错误更正为30，累计90 | 累计870，delta−30，经Finance更正 | 不是新正向结算键直接扣库存 | [原文 L238](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/95/951bf766a6f96118__PUR-SUB-01_完整静态详细设计_RECOVERY-R2_20261008.md:238>) |
| 正常实体退10给供应商 | 另建PUR-RETURN及成本/财务处置 | 不得把它伪装原收回纠正 | [原文 L238](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/95/951bf766a6f96118__PUR-SUB-01_完整静态详细设计_RECOVERY-R2_20261008.md:238>) |

Finance最终锁原Settlement、核准确前驱和legacy基线，未知阻本SUBline后续新revision，沿原key查实。原SC-PO-Line已有凭证必须Finance有权映射期初目标，否则LEGACY_COST_RECONCILIATION_REQUIRED；不能换新key再入全材料600。新scope关闭旧finished-cost，旧返回相同voucher不证明新的部分量已记正确金额。 [原文 L49](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/95/951bf766a6f96118__PUR-SUB-01_完整静态详细设计_RECOVERY-R2_20261008.md:49>)、[原文 L254](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/95/951bf766a6f96118__PUR-SUB-01_完整静态详细设计_RECOVERY-R2_20261008.md:254>)、[原文 L256](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/95/951bf766a6f96118__PUR-SUB-01_完整静态详细设计_RECOVERY-R2_20261008.md:256>)、[原文 L258](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/95/951bf766a6f96118__PUR-SUB-01_完整静态详细设计_RECOVERY-R2_20261008.md:258>)

### 耗用对账：固定分母与商业验收gross

正式模式LEGACY_GROSS_ISSUE_ACCEPTANCE_AT_CUT_V1以COMMERCIAL_ACCEPTANCE_GROSS为成品基数，与上面的成本RECEIPT集合明确不同。旧人工finishedQty只能SIMULATION，不可触发正式调查、Hold或成本。ConsumptionBasis固定Qplan>0、UOM、原PO scope/revision及逐料计划，单位耗用以有理数planned/Qplan保存；ConsignPlan可预分配ID避免摘要循环。 [原文 L262](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/95/951bf766a6f96118__PUR-SUB-01_完整静态详细设计_RECOVERY-R2_20261008.md:262>)、[原文 L264](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/95/951bf766a6f96118__PUR-SUB-01_完整静态详细设计_RECOVERY-R2_20261008.md:264>)

AcceptanceBasis取完整Owner集合中当前有效最新原allocation身份/片段，真VOID排除、普通退货不从gross扣；让步验收也必须有真实Acceptance证据。完整查到已验收60＋待检40是COMPLETE但PROVISIONAL；若完整集合本身未知，则expected/variance/overall为null，不能拿GR100猜验收100。 [原文 L266](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/95/951bf766a6f96118__PUR-SUB-01_完整静态详细设计_RECOVERY-R2_20261008.md:266>)、[原文 L268](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/95/951bf766a6f96118__PUR-SUB-01_完整静态详细设计_RECOVERY-R2_20261008.md:268>)、[原文 L270](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/95/951bf766a6f96118__PUR-SUB-01_完整静态详细设计_RECOVERY-R2_20261008.md:270>)

expected=fixedUnitUsage×acceptedGross，variance=issuedGross−expected，allowed=expected×获准tolerance，严格variance>allowed才hasOverIssue；相等不超，负差是少发，不自动Hold。验收0而已有发料可形成暂时超发，需明确暂时解释。PO计划100变80，旧固定单耗10保持：accepted40期望400；要500必须正式后继12.5基准，不自动改分母。 [原文 L272](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/95/951bf766a6f96118__PUR-SUB-01_完整静态详细设计_RECOVERY-R2_20261008.md:272>)、[原文 L276](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/95/951bf766a6f96118__PUR-SUB-01_完整静态详细设计_RECOVERY-R2_20261008.md:276>)

原计划100成品、1000料、已发700，GR100其中Accepted60/待检40、容差5%：expected600、variance100、allowed30，暂时超发=true。不能用Received100得expected1000再吞掉异常，也不能直接认定损耗/私吞。显式Investigation NEW→IN_REVIEW→RESOLVED，后继cut改变应REOPEN_REQUIRED；解决要真正专业处置证据，调查状态不能替代Stock或QA命令。 [原文 L278](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/95/951bf766a6f96118__PUR-SUB-01_完整静态详细设计_RECOVERY-R2_20261008.md:278>)、[原文 L280](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/95/951bf766a6f96118__PUR-SUB-01_完整静态详细设计_RECOVERY-R2_20261008.md:280>)

## 5. 可实施接口、DTO及跨域端口

### 关联、支给计划与普通CommandReply完整DTO／路由

[原文 L109](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/95/951bf766a6f96118__PUR-SUB-01_完整静态详细设计_RECOVERY-R2_20261008.md:109>)

ContextQuery={siteId}；ContextResult={siteRef,zoneId,capabilities,poOptions,policyRefs,ownerProtocols,technicalLimits}。PoChoice={poRef,poVersion,poLineId,scheduleIds,sourcePortionIds,orderKind,supplierRef,finishedItemRef,uomRef,lifecycle,confirmation,commercialScopeHash}；仅可见选项，不作为最终许可。

|API|输入→输出|权限/语义|
|---|---|---|
|GET /context|ContextQuery→ContextResult|sub.read；不自动创建关联|
|GET /orders|Search→Page<SubcontractSummary>|Site、供应商、状态、date范围；capture固定分页|
|POST /links/prepare|LinkPrepare→LinkPlan|link.prepare；只准备|
|POST /links/:planId/apply|Apply→CommandReply|link.apply；最终PO门/CAS|
|GET /subcontracts/:id|→Detail|sub.read，字段裁剪|
|POST /subcontracts/:id/consign-plans|ConsignPlanInput→PlanView|consign.prepare|
|POST /consign-plans/:id/adopt|Apply→CommandReply|consign.adopt；影响门|
|GET /subcontracts/:id/history|→Page<AuditView>|history.read|

Search={siteId,supplierId|null,poNoPrefix|null,itemId|null,lifecycle:ANY/ACTIVE/CLOSED,exceptionsOnly,dateFrom|null,dateTo|null,captureId|null,cursor|null,pageSize,sort:UPDATED_DESC/PO_ASC}。Page={items,captureId,asOf,coverage,nextCursor,total|null}；total仅完整权限时返回。

LinkPrepare={poRef,poLineId,scheduleIds,sourcePortionIds,expectedPoVersion,reason}。LinkPlan={id,version,digest,poChoice,requiredGates,expiresAt,eligible}。ConsignPlanInput={expectedLinkVersion,expectedPoVersion,predecessorPlanRef|null,technicalBasisRef|null,consumptionBasisRef|null,lines:[{materialLineId|null,itemChoiceRef,purposeKey,uomRef,plannedConsignQty,unitCost|null,currencyRef|null,valuationRef|null}],reason}。PlanView={id,revision,version,digest,state:DRAFT/READY/ADOPTED/SUPERSEDED/REJECTED,input,previousRef|null,impactSummary,gates}。

Apply={commandId,planId,expectedPlanVersion,expectedPlanDigest,reason}。CommandReply={commandId,outcome:COMMITTED/PROCESSING/REJECTED,replayed,resultRef|null,workId|null,error|null}；只有持久Work存在可202 PROCESSING；对物理/财务业务结果没有Owner回执不回COMMITTED。§5.2另用IssueActionReply明确本域受理效果ISSUE_WORK_ACCEPTED，不能将其COMMITTED展示为物理已发。

### IssuePrepare的输入、来源、精确腿计划

[原文 L142](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/95/951bf766a6f96118__PUR-SUB-01_完整静态详细设计_RECOVERY-R2_20261008.md:142>)

GET /subcontracts/:id/issue-context 返回 {linkVersion,planRef,poGuardRef,materials,warehouseOptions,wmsProtocolRef,coverage,capabilities}。每 material 返回 currentFactSetRef、plannedQty、issuedGross、returnedPhysical、pendingProtectedQty、remainingPlanQty、eligibleQty、unitRef、selectionOptions；eligibleQty未知时null，不能按“计划−当前显示已发”猜可发。

POST /issue-plans 请求 IssuePrepare={subcontractId,expectedLinkVersion,consignPlanRef,selectionMode:EXPLICIT/FULL_REMAINING,items:[{materialLineId,qty,warehouseChoiceRef|null}],sourceStrategy:LEGACY_SINGLE_ROW_MAX_AVAILABLE,reason,supplementAuthorizationRef|null}。FULL_REMAINING时 items 必须空，服务固定全量正剩余集合并返回，空集合结果NO_ACTION，不生成0量WMS动作；EXPLICIT时非空、重复materialLineId/未知料/qty<=0都400，禁止旧“取首个/忽略未知”的歧义。

保留旧单行优先策略的业务可见性，不能默换FIFO、多库位拼料。目标实现由WMS准备 sourceChoiceRef 与准确来源切片；同Available/Physical平手以稳定stockId作展示稳定排序，最终库存资格由WMS Owner核，不由SUB直接读写余额。不足时返回 INSUFFICIENT_SINGLE_SOURCE，显示“当前策略不拆分”；若今后允许拆分须独立获准策略和完整分腿，不在本版隐藏改变。

普通量加已发与未决保护不得超已采用计划；补发可超过计划但必须显式SUPPLEMENT授权，绑定料/量/原因/用途/版本。阈值报警不能代替批准。所有qty使用同单位或精确转换Ref；没有单位证明拒绝。IssuePlan={id,version,digest,scope,materialFactCut,poGuardRef,items,ownerLegs,totalsByUom,gates,expiresAt,atomicity:PER_LEG,eligible}，ownerLeg={legId,materialLineId,qty,sourceChoiceRef,wmsOperationKey,payloadDigest}。首次发料前WMS key已稳定持久，不能在每次网络调用生成。

### 发料apply、continue、cancel-unsent与Work完整公共合同

[原文 L154](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/95/951bf766a6f96118__PUR-SUB-01_完整静态详细设计_RECOVERY-R2_20261008.md:154>)

|公共端点|准确请求→结果|权限与效力|
|---|---|---|
|POST /issue-plans/:planId/apply|IssueApply→IssueActionReply|issue.commit；一个Plan最多受理一个Work|
|GET /issue-plans/:planId/application|→IssueApplicationView|work.read；已知Plan定位原Work|
|GET /works/:workId|→IssueWorkView|work.read；Work/Leg版本及动作|
|POST /works/:workId/continue|IssueContinue→IssueActionReply|issue.commit；核准未发腿或安排已发原键查询|
|POST /works/:workId/cancel-unsent|IssueCancelUnsent→IssueActionReply|issue.cancel-unsent；只取消无发送意图的腿|
|POST /works/:workId/reconcile|IssueReconcile→IssueActionReply|work.reconcile；只查选定原key/digest|
|GET /commands/:commandId|→CommandLookup|work.read；原终态或原Work|

IssueApply={commandId,expectedPlanVersion,expectedPlanDigest,reason}；planId取route，不接受重传items/qty/Owner key。IssueContinue={commandId,expectedWorkRevision,legs:[{legId,expectedLegRevision}],reason}。IssueCancelUnsent同字段，选定腿须全部当前未发，任一已发则整条取消命令409 LEG_ALREADY_DISPATCHED、无取消新效果。IssueReconcile={commandId,expectedWorkRevision,legIds,reason}。列表非空、唯一且全属原Work；未知腿400，不忽略。

IssueApplicationView={planId,planDigest,state:NOT_ACCEPTED/ACCEPTED,acceptanceRef|null,workId|null,queriedAt}。NOT_ACCEPTED只是当前观察，不授权换Plan再发；原Plan apply仍由最后唯一约束决定。IssueActionReply={commandId,action:APPLY/CONTINUE/CANCEL_UNSENT/RECONCILE,outcome:COMMITTED/PROCESSING/REJECTED,effect:ISSUE_WORK_ACCEPTED/LEG_ACTION_RECORDED/NONE,replayed,acceptanceRef|null,workId|null,workRevision|null,affectedLegs:[{legId,result:ACCEPTED_UNSENT/QUERY_SCHEDULED/UNSENT_CANCELLED/ALREADY_TERMINAL}],problem|null}。APPLY的COMMITTED仅表示受理已commit，effect=ISSUE_WORK_ACCEPTED；物理发料结果另读Work，不能显示全料已发。

IssueWorkView={id,revision,planRef,originalActorRef,acceptanceRef,state,legs:[{legId,revision,materialLineId,qty,unitRef,originalOwnerKey,payloadDigest,state,dispatchState:NEVER_DISPATCHED/DISPATCH_INTENT_DURABLE/OWNER_TERMINAL,protectionRef,ownerResultRef|null,commitKnowledge,allowedActions}],counts,allowedActions,resultRef|null}。continue限原发起人仍有全动作权，或明确注册恢复服务在原委托内；其他有读权人员可查询，不能借原Actor发新效果。

### SUB-WMS-ISSUE-1 prepare／commit／lookup及真正物理回执

[原文 L184](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/95/951bf766a6f96118__PUR-SUB-01_完整静态详细设计_RECOVERY-R2_20261008.md:184>)

本册拟定 SUB-WMS-ISSUE-1：PrepareIssue({operationKey,sourceChoiceRef,qty,uomRef,purpose:SUBCONTRACT,subcontractLineRef,consignPlanRef,supplierCustodyRef,actorScope,expectedSourceVersion}) 返回 {intentRef,operationKey,approvedPayloadDigest,sourceSlices,guardRef,readiness,protocolRef}；CommitIssue(intentRef,expectedDigest) 返回 IssueReceipt 或 PROCESSING；LookupIssue(operationKey,payloadDigest) 返回 FOUND/NOT_OBSERVED/UNAVAILABLE。这是待WMS采用的本域适配合同，不是已接受WMS原端点改名。

IssueReceipt={receiptRef,operationKey,payloadDigest,outcome:COMMITTED/NO_EFFECT_FINAL,actualSlices:[{sourceStockRef,movementRef,qty,uomRef,lotRef,custodyDispositionRef|null}],occurredAt,ownerVersion,completenessProofRef}。WMS若采用同Main UnitOfWork，可在一个腿的最终Guard、物理账、IssueReceipt、SUB贡献账、Ledger结果、Audit/Outbox同事务落地；不可把两个独立HTTP调用称原子。跨事务按Work分阶段：保存原腿→WMS受理→原键查询真结果→SUB幂等采纳。WMS侧必须具备原键查询与防重，否则新正式发料不可启用。

托管语义需要WMS明确custodyDispositionRef，且准确说明资产仍属本企业、所在地为外协或其他已采用处理。旧OUT号单独只能证明源扣减，不补造目的托管余额。若WMS未实现托管目标，本域可在隔离的LEGACY模式显示旧事实及缺口，但新要求托管的正式Scope须返回CUSTODY_PROTOCOL_NOT_READY。

### SUB-GR-OBSERVATION-1原身份、完整分配与版本链

[原文 L210](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/95/951bf766a6f96118__PUR-SUB-01_完整静态详细设计_RECOVERY-R2_20261008.md:210>)

### 6.2 本域接收投影合同

Required SUB-GR-OBSERVATION-1：输入 SubReceiptObservation={eventId,producerRef,poFactRef,identity,factVersion,previousVersion,supersedesFactRef|null,state:POSTED/VOID_WITH_EVIDENCE,subcontractLineRef,allocations:[{poLineId,scheduleId,sourcePortionId,receiptSliceRef,qty,sourceUom,originalQty,originalUom,conversionRef|null}],movementRefs,qualityRefs,correctionAuthorityRef|null,manifestRef,occurredAt}。PO原事实不复制成不同计量身份；此观察携原PoFactRef，SUB不能冒充GR重新发布PO receipt。

生产者授权→原事实可解析→Tenant/Site/关联精确→identity锁→核版本链/manifest→Inbox和不可变观察→重建最新有效集合/coverage→Audit和成本待重算通知。同event异body409；同fact版异内容隔离；缺v2先到v3置PENDING_GAP，不加v3再加v2；查原Owner补齐完整链。真实超量或迟到事实保留，标EXCEPTION并停新的可能扩大风险动作，不能因PO已关闭删历史。

### 成本预览、提交、查询与SUB-FIN-CUMULATIVE-1

[原文 L240](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/95/951bf766a6f96118__PUR-SUB-01_完整静态详细设计_RECOVERY-R2_20261008.md:240>)

### 7.2 端点和准确计价 DTO

|端点|输入→输出|权限和效果|
|---|---|---|
|GET /subcontracts/:id/cost-context|→CostContext|cost.read，返回基线/事实cut/已结算清单|
|POST /cost-previews|CostPreviewRequest→CostPreview|cost.prepare，纯预览|
|POST /cost-previews/:id/submit|Apply→CommandReply|cost.submit，Finance适配，非本域造凭证|
|GET /cost-settlements/:id|→CostSettlement|cost.read；原请求与真实结果|
|POST /reconciliations|ReconcileInput→ReconcileSnapshot|reconcile.read；纯计算 |
|POST /reconciliations/:id/investigations|InvestigationInput→Investigation|investigation.create；创建本域任务 |
|POST /investigations/:id/resolve|ResolutionInput→Investigation|investigation.resolve；专业证据必需 |

CostContext={basisOptions,currentBasisRef,receiptSetRef,receiptCoverage,currentFinanceSettlementRef,currentCumulativeByCurrency,legacyVoucherObservations,gates}。CostPreviewRequest={subcontractId,basisRef,receiptSetRef,expectedSettlementVersion,reason}。CostPreview={id,version,digest,expiresAt,basisRef,receiptManifestRef,quantityByPriceSegment,processingCumulative,materialPoolCumulative,totalTargetByCurrency,previousPostedByCurrency,deltaByCurrency,roundingEvidence,financeEligibilityRef,requiredCorrectionKind:NONE/INCREMENT/REVERSAL_OR_ADJUSTMENT,gates}。

Required SUB-FIN-CUMULATIVE-1 请求 SettlementIntent={operationKey,subcontractLineRef,settlementRevision,expectedPredecessorRef|null,candidateRef,payloadDigest,basisRef,receiptManifestRef,targetByCurrency,deltaByCurrency,correctionOfRefs,actorScope,reason}。Finance返回 {operationKey,payloadDigest,result:COMMITTED/PROCESSING/REJECTED_NO_EFFECT,settlementRef|null,acceptedTargetByCurrency,postedDeltaByCurrency,voucherRefs,predecessorRef,ownerVersion,commitKnowledge}。Lookup同operationKey/digest返回原值；不能只“ok=true”没凭证就当已过账。原旧源键作为 legacyVoucherRef 输入交叉去重，不把新revision键绕过已有成本。

### 固定ConsumptionBasis、ReconcileInput与AcceptanceBasis

[原文 L264](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/95/951bf766a6f96118__PUR-SUB-01_完整静态详细设计_RECOVERY-R2_20261008.md:264>)

ConsumptionBasis={ref,revision,predecessorRef|null,adoptionRef,poRef,poBusinessRevision,poLineId,scheduleIds,sourcePortionIds,scopeManifestRef,planFinishedQty,finishedUomRef,consignPlanId,materialUsages:[{materialLineId,plannedConsignQty,materialUomRef,unitUsage:{numerator,denominator},conversionRef|null}],authorityRef}。planFinishedQty>0，固定为该支给版对应的成品计划范围总量；每料unitUsage精确等plannedConsignQty/planFinishedQty，不动态读取当前PO量。量义/转换必须有据，不隐式舍入。consignPlanId为共同准备时预分配稳定Id，不含反向计划digest；计划原文保存此Basis的完整Ref/digest，并在同一采用事务核计划Id、基准修订及内容，避免循环摘要。

GET /subcontracts/:id/reconciliation-context→ReconcileContext={consignPlanRef,consumptionBasisRef,availableAcceptanceSetRefs,issueFactSetRef,receiptComparisonSetRef,currentPoRef,basisCurrentness:CURRENT/HISTORICAL_PENDING_REVIEW,policyRef,gates}。每AcceptanceSet为指定Owner的完整版本manifest，不由前端只选有利行。ReconcileInput={subcontractId,consignPlanRef,consumptionBasisRef,issueFactSetRef,acceptanceFactSetRef,receiptComparisonSetRef,tolerancePolicyRef,mode:LEGACY_GROSS_ISSUE_ACCEPTANCE_AT_CUT_V1,reason}；不接任意finishedQty/tolerance。所有输入同scope且cut有据，不能拿另一PO版数量作分母。

AcceptanceBasis={kind:COMMERCIAL_ACCEPTANCE_GROSS,ownerFactKind:ACCEPTANCE,factSetRef,coverageRef,cut,scopeManifestRef,finishedUomRef,currentEffectiveAllocations:[{stableIdentity,factVersion,receiptSliceRef,sourcePortionId,quantity,conversionRef|null}],acceptedGrossQty|null,knownPendingQty|null,receiptGrossComparisonQty|null,completeness:COMPLETE/PARTIAL/UNKNOWN,interpretation:SETTLED/PROVISIONAL_PENDING_ACCEPTANCE/UNKNOWN}。只对同范围每个最新有效ACCEPTANCE分配互斥归一；VOID凭原证据不贡献，物理退回不自动扣gross验收；同Receipt的GR/WMS不再加。让步/免检须原ACCEPTANCE专业证据。待检量独立，不当已验收量或质检失败。

若Owner证实当前全集为已验收60加明确待检40，completeness=COMPLETE、acceptedGrossQty=60、interpretation=PROVISIONAL_PENDING_ACCEPTANCE，可计算“相对当前已验收量”差异并显式调查，不判最终损耗。若清单不全、未知是否另有验收或专业关联未证，则正式acceptedGrossQty/expected/variance/全局结论UNKNOWN；可另列已核下界，不能从GR100猜验收100。

| 合同 | 提供者→消费者 | 准入/恢复与未采用结果 | 出处 |
| --- | --- | --- | --- |
| SUB-PO-LINK-GUARD-1 | PUR-PO→SUB | 同库准确商业scope最终核；缺失阻正式关联/发料准备，不改变PO原DTO | [原文 L311](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/95/951bf766a6f96118__PUR-SUB-01_完整静态详细设计_RECOVERY-R2_20261008.md:311>) |
| 工程/材料基线读取 | 工程→SUB | 准确用途与单耗；缺失草稿待补，不能猜BOM | [原文 L312](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/95/951bf766a6f96118__PUR-SUB-01_完整静态详细设计_RECOVERY-R2_20261008.md:312>) |
| SUB-WMS-ISSUE-1 | WMS→SUB | 原key prepare/commit/lookup、真实movement/托管；缺失阻新正式发料 | [原文 L313](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/95/951bf766a6f96118__PUR-SUB-01_完整静态详细设计_RECOVERY-R2_20261008.md:313>) |
| SUB-GR-OBSERVATION-1 | PUR-GR/PO→SUB | 沿原PoFactIdentity/ReceiptSlice最新完整分配；缺口查询原Owner | [原文 L314](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/95/951bf766a6f96118__PUR-SUB-01_完整静态详细设计_RECOVERY-R2_20261008.md:314>) |
| QA观察 | QA/PUR-GR→SUB | 只展示原真实判定/豁免；无判定UNKNOWN | [原文 L315](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/95/951bf766a6f96118__PUR-SUB-01_完整静态详细设计_RECOVERY-R2_20261008.md:315>) |
| SUB-FIN-CUMULATIVE-1 | Finance→SUB | 累计目标、前驱、准确delta、legacy去重；缺失只预览 | [原文 L316](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/95/951bf766a6f96118__PUR-SUB-01_完整静态详细设计_RECOVERY-R2_20261008.md:316>) |
| Owner责任结清读取 | WMS/GR/QA/Finance→SUB | 完整manifest和未决清单；缺任域不结清 | [原文 L317](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/95/951bf766a6f96118__PUR-SUB-01_完整静态详细设计_RECOVERY-R2_20261008.md:317>) |

SUB-PO-LINK-GUARD-1返回{poRef,version,guardRef,scopeHash,allowedActions,reasons,checkedInTransaction}，是同库只读资格及最终Guard采用，不改PUR-PO公开DTO。Owner协议未采用阻正式关联/准备；WMS缺prepare/commit/lookup真实movement/托管阻新发料；Finance缺累计/前驱/legacy去重只准预览。Source OUT一张凭据不能证明供应商托管目的地完整，缺custody协议报CUSTODY_PROTOCOL_NOT_READY。 [原文 L130](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/95/951bf766a6f96118__PUR-SUB-01_完整静态详细设计_RECOVERY-R2_20261008.md:130>)、[原文 L184](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/95/951bf766a6f96118__PUR-SUB-01_完整静态详细设计_RECOVERY-R2_20261008.md:184>)、[原文 L186](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/95/951bf766a6f96118__PUR-SUB-01_完整静态详细设计_RECOVERY-R2_20261008.md:186>)、[原文 L188](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/95/951bf766a6f96118__PUR-SUB-01_完整静态详细设计_RECOVERY-R2_20261008.md:188>)、[原文 L296](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/95/951bf766a6f96118__PUR-SUB-01_完整静态详细设计_RECOVERY-R2_20261008.md:296>)

## 6. 事务、幂等、恢复与并发

全局锁序Tenant/Site→PO精确Guard→SubcontractLink→排序MaterialHead→CostHead→CommandLedger，同库Owner须同真实连接事务和相容锁序。采用Plan整集合、受理唯一Work/全部腿/保护、采纳Owner贡献/消耗保护各有其明确本域事务；不能以两次HTTP把跨库发料叫原子。非同库只通过持久Work、原键及Owner幂等恢复，PER_LEG部分成功是有意策略。 [原文 L136](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/95/951bf766a6f96118__PUR-SUB-01_完整静态详细设计_RECOVERY-R2_20261008.md:136>)、[原文 L170](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/95/951bf766a6f96118__PUR-SUB-01_完整静态详细设计_RECOVERY-R2_20261008.md:170>)、[原文 L172](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/95/951bf766a6f96118__PUR-SUB-01_完整静态详细设计_RECOVERY-R2_20261008.md:172>)、[原文 L186](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/95/951bf766a6f96118__PUR-SUB-01_完整静态详细设计_RECOVERY-R2_20261008.md:186>)、[原文 L190](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/95/951bf766a6f96118__PUR-SUB-01_完整静态详细设计_RECOVERY-R2_20261008.md:190>)、[原文 L303](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/95/951bf766a6f96118__PUR-SUB-01_完整静态详细设计_RECOVERY-R2_20261008.md:303>)

CommandLedger唯一(Tenant,operationKind,commandId)，同键换目标也冲突；Ledger及回查先核对象读权。SUB-C14N-1按UTF8/NFC、规范decimal/UTC、材料stableID排序、腿manifest固定序；包括Actor语义scope、目标版本、料量单位、来源/策略/估值和reason，不含变化投递时间。Owner原digest原样存，不改包冒签署。 [原文 L303](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/95/951bf766a6f96118__PUR-SUB-01_完整静态详细设计_RECOVERY-R2_20261008.md:303>)、[原文 L305](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/95/951bf766a6f96118__PUR-SUB-01_完整静态详细设计_RECOVERY-R2_20261008.md:305>)

NOT_OBSERVED/404/timeout不证明无效果，网络500不能把已发Owner命令标NO_NEW_EFFECT。失权阻未发新效果，但保原事实和有权恢复查询；事实同版异文隔离、缺前驱补链，不直接累加两个版本。发料成功实际不同也先保存真相；Cost前驱未知则后继保持阻断，不能用更大的revision越过原责任。 [原文 L176](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/95/951bf766a6f96118__PUR-SUB-01_完整静态详细设计_RECOVERY-R2_20261008.md:176>)、[原文 L194](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/95/951bf766a6f96118__PUR-SUB-01_完整静态详细设计_RECOVERY-R2_20261008.md:194>)、[原文 L196](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/95/951bf766a6f96118__PUR-SUB-01_完整静态详细设计_RECOVERY-R2_20261008.md:196>)、[原文 L256](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/95/951bf766a6f96118__PUR-SUB-01_完整静态详细设计_RECOVERY-R2_20261008.md:256>)、[原文 L288](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/95/951bf766a6f96118__PUR-SUB-01_完整静态详细设计_RECOVERY-R2_20261008.md:288>)

| 场景 | 错误及动作 | 不得执行 | 出处 |
| --- | --- | --- | --- |
| PO变为取消/范围换版 | 409 PO_SCOPE_CHANGED；重新准备 | 刷新Expected自动再发 | [原文 L292](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/95/951bf766a6f96118__PUR-SUB-01_完整静态详细设计_RECOVERY-R2_20261008.md:292>) |
| 同键异正文 | 409 COMMAND_CONTENT_CONFLICT | 覆盖原Ledger | [原文 L293](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/95/951bf766a6f96118__PUR-SUB-01_完整静态详细设计_RECOVERY-R2_20261008.md:293>) |
| 材料重复/单位不明 | 400 DUPLICATE_ITEM或422 UNIT_EVIDENCE_REQUIRED | 取第一项/猜单位 | [原文 L294](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/95/951bf766a6f96118__PUR-SUB-01_完整静态详细设计_RECOVERY-R2_20261008.md:294>) |
| 单库存行不足 | 422 INSUFFICIENT_SINGLE_SOURCE | 暗拆批或换仓 | [原文 L295](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/95/951bf766a6f96118__PUR-SUB-01_完整静态详细设计_RECOVERY-R2_20261008.md:295>) |
| WMS/Finance协议未采用 | 422 OWNER_PROTOCOL_NOT_READY | 绕旧直接写端点 | [原文 L296](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/95/951bf766a6f96118__PUR-SUB-01_完整静态详细设计_RECOVERY-R2_20261008.md:296>) |
| 成功响应丢失 | UNKNOWN+QUERY_ORIGINAL | 新键再次发料/入账 | [原文 L297](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/95/951bf766a6f96118__PUR-SUB-01_完整静态详细设计_RECOVERY-R2_20261008.md:297>) |
| 事实同版冲突或缺前驱 | 409 FACT_CONFLICT/PENDING_GAP | 删旧事实或加总两版 | [原文 L298](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/95/951bf766a6f96118__PUR-SUB-01_完整静态详细设计_RECOVERY-R2_20261008.md:298>) |
| 成本前驱已变 | 409 SETTLEMENT_PREDECESSOR_CHANGED | 后继先行造成材料重复 | [原文 L299](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/95/951bf766a6f96118__PUR-SUB-01_完整静态详细设计_RECOVERY-R2_20261008.md:299>) |
| 缺质量结果 | coverage UNKNOWN，必要动作阻断 | 默认PASS | [原文 L300](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/95/951bf766a6f96118__PUR-SUB-01_完整静态详细设计_RECOVERY-R2_20261008.md:300>) |
| 只读对账超容差 | hasOverIssue，等待显式调查 | 自动声称已挂起或私吞 | [原文 L301](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/95/951bf766a6f96118__PUR-SUB-01_完整静态详细设计_RECOVERY-R2_20261008.md:301>) |

## 7. 权限、租户隔离与审计

能力拆sub.read、link.prepare/apply、consign.prepare/adopt、issue.prepare/commit/cancel-unsent、work.read/reconcile、receipt.read、cost.read/prepare/submit、reconcile.read、investigation.create/resolve、history.read。旧pur-subcontract权限只可经IAM受控映射，旧Authorize不证明对象/Site/金额隔离。各实际Owner复核原Actor和代理，后台身份不得扩大业务权限。 [原文 L284](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/95/951bf766a6f96118__PUR-SUB-01_完整静态详细设计_RECOVERY-R2_20261008.md:284>)

审计保存原Actor/代理、UTC/Site、准确PO/料/ReceiptSlice、前后版、Plan/Owner协议、原key/digest、commitKnowledge、真实回执、理由及trace，不存秘密。成本无权为REDACTED/null，禁止敏感导出及保留上一行缓存；不把未见金额显示0。 [原文 L77](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/95/951bf766a6f96118__PUR-SUB-01_完整静态详细设计_RECOVERY-R2_20261008.md:77>)、[原文 L305](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/95/951bf766a6f96118__PUR-SUB-01_完整静态详细设计_RECOVERY-R2_20261008.md:305>)

## 8. 页面、按钮、状态与反馈

| 页面 | 用户动作和必须展示的反馈 | 出处 |
| --- | --- | --- |
| P01 /pur/subcontract-workbench | Site必选，分页20/50/100；商业/发料/收回/质量/成本五轴及asOf，不默认首供应商；空与失败分开 | [原文 L73](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/95/951bf766a6f96118__PUR-SUB-01_完整静态详细设计_RECOVERY-R2_20261008.md:73>) |
| P02 /pur/subcontracts/:id | 原PO文书/准确scope、历史材料集合；需新PO跳采购流程，不在SUB重建审批 | [原文 L73](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/95/951bf766a6f96118__PUR-SUB-01_完整静态详细设计_RECOVERY-R2_20261008.md:73>) |
| P03 /pur/subcontracts/:id/issues/new | 选择采用计划、料量和来源策略；预览全部腿；唯一Work回执后显示已受理 | [原文 L75](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/95/951bf766a6f96118__PUR-SUB-01_完整静态详细设计_RECOVERY-R2_20261008.md:75>) |
| P04 /pur/subcontracts/work/:workId | 每腿Owner原键/dispatch/commitKnowledge/真实结果；继续与取消未发分开；无全部重做 | [原文 L75](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/95/951bf766a6f96118__PUR-SUB-01_完整静态详细设计_RECOVERY-R2_20261008.md:75>) |
| P05 /pur/subcontracts/:id/receipts | GR/Stock/QC/退回各列；收回按钮仅跳准确GR context | [原文 L75](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/95/951bf766a6f96118__PUR-SUB-01_完整静态详细设计_RECOVERY-R2_20261008.md:75>) |
| P06 /pur/subcontracts/:id/costs | 原累计基线、拟目标、delta、币种、规则版、Finance回执；预览和提交分开 | [原文 L75](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/95/951bf766a6f96118__PUR-SUB-01_完整静态详细设计_RECOVERY-R2_20261008.md:75>) |
| P07 /pur/subcontracts/:id/reconciliations | 固定Qplan/PO版/单耗、当前验收gross及cut、Received/待检/容差；暂时超发标题，调查需显式确认 | [原文 L75](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/95/951bf766a6f96118__PUR-SUB-01_完整静态详细设计_RECOVERY-R2_20261008.md:75>) |

切PO/行清旧输入/成本/对账，响应只匹配route key＋request sequence，晚到A不能覆盖B。离开未保存草稿可询问丢弃，离开已受理Work不取消事实。失败保草稿及原命令；按钮锁仅防UI双击，后台Ledger防重。重开从服务Work恢复，不依赖浏览器记忆；历史COMMITTED与当前可发料、当前成本有效或当前质量合格分别展示。 [原文 L77](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/95/951bf766a6f96118__PUR-SUB-01_完整静态详细设计_RECOVERY-R2_20261008.md:77>)、[原文 L286](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/95/951bf766a6f96118__PUR-SUB-01_完整静态详细设计_RECOVERY-R2_20261008.md:286>)

## 9. 旧代码复用、迁移与开发先后

原固定Main90c871fe、CRM c778a305、Platform30bd等是本册作者源码对照基线，不是本轮新读/新执行证明。旧SubcontractService按Type=2和PoNo/Line核，upsert遗漏留原；显式发料未知料跳过、重复取首，IssuedQty按WMS实际回传加，WmsIssueNo只最后一次。新完整集合、严格EXPLICIT、原键Ledger/保护/事实链必须明示新增。 [原文 L11](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/95/951bf766a6f96118__PUR-SUB-01_完整静态详细设计_RECOVERY-R2_20261008.md:11>)、[原文 L19](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/95/951bf766a6f96118__PUR-SUB-01_完整静态详细设计_RECOVERY-R2_20261008.md:19>)、[原文 L39](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/95/951bf766a6f96118__PUR-SUB-01_完整静态详细设计_RECOVERY-R2_20261008.md:39>)、[原文 L41](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/95/951bf766a6f96118__PUR-SUB-01_完整静态详细设计_RECOVERY-R2_20261008.md:41>)

旧Wms适配按一行可用量优先整批出，不能夸成FIFO/FEFO/跨行或完整供应商托管；旧成本把全部ConsignQty计料，SC-PO-Line单一源键重放不等二次分批金额正确。旧GR同步Accepted和缺QC默认PASS应沿新GR/QA门隔离；旧strict >容差可复用，但新验收基数与固定分母必须采用。 [原文 L43](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/95/951bf766a6f96118__PUR-SUB-01_完整静态详细设计_RECOVERY-R2_20261008.md:43>)、[原文 L47](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/95/951bf766a6f96118__PUR-SUB-01_完整静态详细设计_RECOVERY-R2_20261008.md:47>)、[原文 L49](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/95/951bf766a6f96118__PUR-SUB-01_完整静态详细设计_RECOVERY-R2_20261008.md:49>)、[原文 L51](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/95/951bf766a6f96118__PUR-SUB-01_完整静态详细设计_RECOVERY-R2_20261008.md:51>)、[原文 L204](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/95/951bf766a6f96118__PUR-SUB-01_完整静态详细设计_RECOVERY-R2_20261008.md:204>)

存量分类LEGACY_READ_ONLY、MAPPABLE、DISPUTED、NEW_SCOPE；按Site＋POline单writer切换，关旧consign/issue/finished-cost并返回MIGRATED_SCOPE_NEW_FLOW_REQUIRED。旧UI、空body、导入、后台/恢复Job都要涵盖；新Scope已有结果后只前向协调，不能删除Ledger、回旧直写或由末次发料号补虚构历史。 [原文 L321](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/95/951bf766a6f96118__PUR-SUB-01_完整静态详细设计_RECOVERY-R2_20261008.md:321>)

| 工作 | 具体交付 | 前驱 | Owner及未来证据 | 出处 |
| --- | --- | --- | --- | --- |
| SUB-D01 | 准确旧身份/新Scope映射及来源记录 | 无 | Procurement；逐行映射/争议清单 | [原文 L382](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/95/951bf766a6f96118__PUR-SUB-01_完整静态详细设计_RECOVERY-R2_20261008.md:382>) |
| SUB-D02 | Context、页面/字段权限、PO关联Guard | D01+PO/IAM采用 | PO/SUB；真实选择器与最后门 | [原文 L383](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/95/951bf766a6f96118__PUR-SUB-01_完整静态详细设计_RECOVERY-R2_20261008.md:383>) |
| SUB-D03 | ConsignPlan整集合/CAS/影响评估和固定ConsumptionBasis | D02 | SUB/工程；Qplan/PO版/单耗不随当前PO漂移 | [原文 L384](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/95/951bf766a6f96118__PUR-SUB-01_完整静态详细设计_RECOVERY-R2_20261008.md:384>) |
| SUB-D04 | 公共apply/continue/cancel-unsent、唯一PlanWork、材料保护、Leg送前意图、Ledger/Audit | D01 | SUB；双Plan竞争、取消送前竞争、原键恢复 | [原文 L385](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/95/951bf766a6f96118__PUR-SUB-01_完整静态详细设计_RECOVERY-R2_20261008.md:385>) |
| SUB-D05 | WMS单行策略、原键提交/回查、托管 | D03–04+WMS采用 | WMS；真实movement/完整回执 | [原文 L386](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/95/951bf766a6f96118__PUR-SUB-01_完整静态详细设计_RECOVERY-R2_20261008.md:386>) |
| SUB-D06 | P03–04公共命令/版本/五类计数与PARTIAL_FINAL、未知/撤权恢复 | D04–05 | 前后端；受理不冒物理成功、继续已发只查 | [原文 L387](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/95/951bf766a6f96118__PUR-SUB-01_完整静态详细设计_RECOVERY-R2_20261008.md:387>) |
| SUB-D07 | GR导航及RECEIPT/ACCEPTANCE独立完整集合/版本链 | D02、D04+GR/PO采用 | GR/SUB；ReceiptSlice及待检完整性映射 | [原文 L388](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/95/951bf766a6f96118__PUR-SUB-01_完整静态详细设计_RECOVERY-R2_20261008.md:388>) |
| SUB-D08 | QA/退回/结清责任读模型 | D07+各Owner采用 | QA/WMS/Finance；未知阻门 | [原文 L389](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/95/951bf766a6f96118__PUR-SUB-01_完整静态详细设计_RECOVERY-R2_20261008.md:389>) |
| SUB-D09 | CostBasis、累计目标/delta/舍入 | D03、D07+Finance计价采用 | Finance/SUB；分批和legacy映射 | [原文 L390](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/95/951bf766a6f96118__PUR-SUB-01_完整静态详细设计_RECOVERY-R2_20261008.md:390>) |
| SUB-D10 | Finance原键和前驱锁/更正 | D04、D09+Finance协议采用 | Finance；实际凭证与回执 | [原文 L391](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/95/951bf766a6f96118__PUR-SUB-01_完整静态详细设计_RECOVERY-R2_20261008.md:391>) |
| SUB-D11 | 固定Qplan/ACCEPTANCE_AT_CUT Snapshot、暂时差异/未知、显式调查/后继失效 | D03、D07、D09 | SUB；GR100/验收60及PO减量案例，无Hold副作用 | [原文 L392](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/95/951bf766a6f96118__PUR-SUB-01_完整静态详细设计_RECOVERY-R2_20261008.md:392>) |
| SUB-D12 | 单写切换、全旧入口覆盖、48AC | D01–11 | 独立QA/业务Owner；全部仍NOT_RUN | [原文 L393](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/95/951bf766a6f96118__PUR-SUB-01_完整静态详细设计_RECOVERY-R2_20261008.md:393>) |

## 10. 48条验收定义与预期结果

全部48AC是未来独立实施验证，执行0。每项须保存输入、输出、Owner原回执、前后水位、审计及RunId；以下静态例不冒数据库并发、业务或Owner运行通过。 [原文 L323](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/95/951bf766a6f96118__PUR-SUB-01_完整静态详细设计_RECOVERY-R2_20261008.md:323>)、[原文 L325](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/95/951bf766a6f96118__PUR-SUB-01_完整静态详细设计_RECOVERY-R2_20261008.md:325>)

| AC | 原SPEC | 操作与场景 | 必须观察结果 | 状态 | 出处 |
| --- | --- | --- | --- | --- | --- |
| SUB-AC01 | 01 | 选择标准PO关联 | Type/Kind拒绝，无新关联 | NOT_RUN | [原文 L329](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/95/951bf766a6f96118__PUR-SUB-01_完整静态详细设计_RECOVERY-R2_20261008.md:329>) |
| SUB-AC02 | 01 | 同PO行重复关联同键 | 同结果重放，无第二活动关联 | NOT_RUN | [原文 L330](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/95/951bf766a6f96118__PUR-SUB-01_完整静态详细设计_RECOVERY-R2_20261008.md:330>) |
| SUB-AC03 | 01 | 跨Site/供应商伪造行引用 | 403/422，不泄露无权对象 | NOT_RUN | [原文 L331](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/95/951bf766a6f96118__PUR-SUB-01_完整静态详细设计_RECOVERY-R2_20261008.md:331>) |
| SUB-AC04 | 01 | 支给草稿缺BOM/手工授权 | 可保存待补，正式采用被阻 | NOT_RUN | [原文 L332](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/95/951bf766a6f96118__PUR-SUB-01_完整静态详细设计_RECOVERY-R2_20261008.md:332>) |
| SUB-AC05 | 01 | 旧upsert只列一料 | 旧语义其余保留，目标整集合差异明确 | NOT_RUN | [原文 L333](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/95/951bf766a6f96118__PUR-SUB-01_完整静态详细设计_RECOVERY-R2_20261008.md:333>) |
| SUB-AC06 | 01 | 减少计划低于已发且未决 | 不改实发，要求专业处置 | NOT_RUN | [原文 L334](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/95/951bf766a6f96118__PUR-SUB-01_完整静态详细设计_RECOVERY-R2_20261008.md:334>) |
| SUB-AC07 | 01 | 同版两人采用计划 | 仅一个成功，另409 | NOT_RUN | [原文 L335](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/95/951bf766a6f96118__PUR-SUB-01_完整静态详细设计_RECOVERY-R2_20261008.md:335>) |
| SUB-AC08 | 01 | 成本无权切行与返回 | 敏感值不残留，不以0代遮蔽 | NOT_RUN | [原文 L336](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/95/951bf766a6f96118__PUR-SUB-01_完整静态详细设计_RECOVERY-R2_20261008.md:336>) |
| SUB-AC09 | 01 | 先慢A后快B响应 | A不覆盖B | NOT_RUN | [原文 L337](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/95/951bf766a6f96118__PUR-SUB-01_完整静态详细设计_RECOVERY-R2_20261008.md:337>) |
| SUB-AC10 | 01 | 采用前PO取消/换版 | 最终Guard拒绝，原计划保留 | NOT_RUN | [原文 L338](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/95/951bf766a6f96118__PUR-SUB-01_完整静态详细设计_RECOVERY-R2_20261008.md:338>) |
| SUB-AC11 | 01 | 同命令异body | 409，无覆盖旧结果 | NOT_RUN | [原文 L339](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/95/951bf766a6f96118__PUR-SUB-01_完整静态详细设计_RECOVERY-R2_20261008.md:339>) |
| SUB-AC12 | 01 | 新scope旧端点写 | 拒绝并导航新流程 | NOT_RUN | [原文 L340](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/95/951bf766a6f96118__PUR-SUB-01_完整静态详细设计_RECOVERY-R2_20261008.md:340>) |
| SUB-AC13 | 02 | null旧body与目标FULL_REMAINING | 旧正剩余规则可解释，目标完整manifest固定 | NOT_RUN | [原文 L341](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/95/951bf766a6f96118__PUR-SUB-01_完整静态详细设计_RECOVERY-R2_20261008.md:341>) |
| SUB-AC14 | 02 | EXPLICIT重复/未知材料 | 400，不忽略、不取首项 | NOT_RUN | [原文 L342](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/95/951bf766a6f96118__PUR-SUB-01_完整静态详细设计_RECOVERY-R2_20261008.md:342>) |
| SUB-AC15 | 02 | 单行80+70欲发100 | 旧策略不拆分，明确不足 | NOT_RUN | [原文 L343](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/95/951bf766a6f96118__PUR-SUB-01_完整静态详细设计_RECOVERY-R2_20261008.md:343>) |
| SUB-AC16 | 02 | 同可用量取来源/指定仓 | 稳定选择且只在指定范围 | NOT_RUN | [原文 L344](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/95/951bf766a6f96118__PUR-SUB-01_完整静态详细设计_RECOVERY-R2_20261008.md:344>) |
| SUB-AC17 | 02 | 补发超计划无授权 | 阻正式发送，已有事实不删 | NOT_RUN | [原文 L345](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/95/951bf766a6f96118__PUR-SUB-01_完整静态详细设计_RECOVERY-R2_20261008.md:345>) |
| SUB-AC18 | 02 | 返回actual与request不同 | 按实际贡献并异常，不重复补发 | NOT_RUN | [原文 L346](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/95/951bf766a6f96118__PUR-SUB-01_完整静态详细设计_RECOVERY-R2_20261008.md:346>) |
| SUB-AC19 | 02 | 两腿一成一不足；同Plan两command；两个Plan争100余额 | 前者PARTIAL_FINAL或仍未决PARTIAL准确；同Plan唯一Work；双Plan一成一409 | NOT_RUN | [原文 L347](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/95/951bf766a6f96118__PUR-SUB-01_完整静态详细设计_RECOVERY-R2_20261008.md:347>) |
| SUB-AC20 | 02 | WMS成功丢响应；apply受理丢响应后由Plan回查 | 原Owner键查回、原Plan唯一Work，不再扣库存 | NOT_RUN | [原文 L348](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/95/951bf766a6f96118__PUR-SUB-01_完整静态详细设计_RECOVERY-R2_20261008.md:348>) |
| SUB-AC21 | 02 | NOT_OBSERVED；OWNER_PENDING继续；未发取消与dispatcher竞争 | 保UNKNOWN；继续只查；仅NEVER_DISPATCHED可取消，同锁一方胜 | NOT_RUN | [原文 L349](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/95/951bf766a6f96118__PUR-SUB-01_完整静态详细设计_RECOVERY-R2_20261008.md:349>) |
| SUB-AC22 | 02 | 撤权分已发/未发；A成B未发取消 | 未发停止、已发核原结果；B释放保护而A保事实，PARTIAL_FINAL | NOT_RUN | [原文 L350](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/95/951bf766a6f96118__PUR-SUB-01_完整静态详细设计_RECOVERY-R2_20261008.md:350>) |
| SUB-AC23 | 02 | 旧OUT只有源扣减 | 不得显示托管目的余额已证 | NOT_RUN | [原文 L351](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/95/951bf766a6f96118__PUR-SUB-01_完整静态详细设计_RECOVERY-R2_20261008.md:351>) |
| SUB-AC24 | 02 | 同movement重复与更正v2 | 一次贡献，后继替换不累加两版 | NOT_RUN | [原文 L352](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/95/951bf766a6f96118__PUR-SUB-01_完整静态详细设计_RECOVERY-R2_20261008.md:352>) |
| SUB-AC25 | 03 | 点击收回准备 | 只GR上下文，无库存/ReceivedQty写 | NOT_RUN | [原文 L353](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/95/951bf766a6f96118__PUR-SUB-01_完整静态详细设计_RECOVERY-R2_20261008.md:353>) |
| SUB-AC26 | 03 | 同ReceiptSlice GR和WMS到达 | 指定GR记实收一次，WMS为证据 | NOT_RUN | [原文 L354](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/95/951bf766a6f96118__PUR-SUB-01_完整静态详细设计_RECOVERY-R2_20261008.md:354>) |
| SUB-AC27 | 03 | 缺QA结果/着荷财务基准 | 质量UNKNOWN，不自动PASS | NOT_RUN | [原文 L355](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/95/951bf766a6f96118__PUR-SUB-01_完整静态详细设计_RECOVERY-R2_20261008.md:355>) |
| SUB-AC28 | 03 | receipt40更正30 | 当前30、历史40，非70 | NOT_RUN | [原文 L356](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/95/951bf766a6f96118__PUR-SUB-01_完整静态详细设计_RECOVERY-R2_20261008.md:356>) |
| SUB-AC29 | 03 | v3先于v2 | PENDING_GAP，不先增累计 | NOT_RUN | [原文 L357](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/95/951bf766a6f96118__PUR-SUB-01_完整静态详细设计_RECOVERY-R2_20261008.md:357>) |
| SUB-AC30 | 03 | 同版不同内容 | 冲突隔离，保两份来源证据 | NOT_RUN | [原文 L358](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/95/951bf766a6f96118__PUR-SUB-01_完整静态详细设计_RECOVERY-R2_20261008.md:358>) |
| SUB-AC31 | 03 | 实收100实退20 | gross100、退20、净80，额度不自动恢复 | NOT_RUN | [原文 L359](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/95/951bf766a6f96118__PUR-SUB-01_完整静态详细设计_RECOVERY-R2_20261008.md:359>) |
| SUB-AC32 | 03 | GR成功回执未到 | 查原GR operation，不造新GR | NOT_RUN | [原文 L360](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/95/951bf766a6f96118__PUR-SUB-01_完整静态详细设计_RECOVERY-R2_20261008.md:360>) |
| SUB-AC33 | 03 | 旧历史不完整 | null/UNKNOWN，不能以0结清 | NOT_RUN | [原文 L361](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/95/951bf766a6f96118__PUR-SUB-01_完整静态详细设计_RECOVERY-R2_20261008.md:361>) |
| SUB-AC34 | 03 | 跨UOM无法精确Qty6 | 拒转换，不截整超收 | NOT_RUN | [原文 L362](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/95/951bf766a6f96118__PUR-SUB-01_完整静态详细设计_RECOVERY-R2_20261008.md:362>) |
| SUB-AC35 | 03 | 结清时有一Owner未知 | BLOCKED，不能关闭PO或SUB | NOT_RUN | [原文 L363](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/95/951bf766a6f96118__PUR-SUB-01_完整静态详细设计_RECOVERY-R2_20261008.md:363>) |
| SUB-AC36 | 03 | 结清后晚到真实反证 | 保历史、当前REASSESS，不删事实 | NOT_RUN | [原文 L364](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/95/951bf766a6f96118__PUR-SUB-01_完整静态详细设计_RECOVERY-R2_20261008.md:364>) |
| SUB-AC37 | 04 | 价3、M600、一次100 | 原公式300+600=900，AP不含材料另付 | NOT_RUN | [原文 L365](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/95/951bf766a6f96118__PUR-SUB-01_完整静态详细设计_RECOVERY-R2_20261008.md:365>) |
| SUB-AC38 | 04 | 40后60两批 | 累计720→900，delta720/180，材料一次 | NOT_RUN | [原文 L366](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/95/951bf766a6f96118__PUR-SUB-01_完整静态详细设计_RECOVERY-R2_20261008.md:366>) |
| SUB-AC39 | 04 | 旧SC键已入720再请求900 | 不冒旧凭证新成功，Finance受控增量/映射 | NOT_RUN | [原文 L367](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/95/951bf766a6f96118__PUR-SUB-01_完整静态详细设计_RECOVERY-R2_20261008.md:367>) |
| SUB-AC40 | 04 | 成本提交timeout再点击 | 原键恢复，后继结算暂停 | NOT_RUN | [原文 L368](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/95/951bf766a6f96118__PUR-SUB-01_完整静态详细设计_RECOVERY-R2_20261008.md:368>) |
| SUB-AC41 | 04 | 成本原事实更正与物理退回 | 更正候选与Return分开，不自动共用冲销 | NOT_RUN | [原文 L369](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/95/951bf766a6f96118__PUR-SUB-01_完整静态详细设计_RECOVERY-R2_20261008.md:369>) |
| SUB-AC42 | 04 | 不同币种/缺估值规则 | 不裸加，正式提交阻断 | NOT_RUN | [原文 L370](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/95/951bf766a6f96118__PUR-SUB-01_完整静态详细设计_RECOVERY-R2_20261008.md:370>) |
| SUB-AC43 | 04 | 三批产生舍入尾差 | 累计目标舍入一次，delta之和精确等目标 | NOT_RUN | [原文 L371](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/95/951bf766a6f96118__PUR-SUB-01_完整静态详细设计_RECOVERY-R2_20261008.md:371>) |
| SUB-AC44 | 04 | issued1050 expected1000 tol5%；GR100/验收60/待检40、发700、料1000/计划100 | 等号不超差；验收基数600、差100/允许30，PROVISIONAL而非GR基数 | NOT_RUN | [原文 L372](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/95/951bf766a6f96118__PUR-SUB-01_完整静态详细设计_RECOVERY-R2_20261008.md:372>) |
| SUB-AC45 | 04 | 负差；PO100→80但旧支给1000、验收40 | 负差单列；旧Qplan100/单耗10仍expected400，不能漂成500 | NOT_RUN | [原文 L373](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/95/951bf766a6f96118__PUR-SUB-01_完整静态详细设计_RECOVERY-R2_20261008.md:373>) |
| SUB-AC46 | 04 | 超差未建调查；ACCEPTANCE全集缺失但GR已知 | 不写已挂起/私吞；正式expected/variance UNKNOWN，不填验收100 | NOT_RUN | [原文 L374](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/95/951bf766a6f96118__PUR-SUB-01_完整静态详细设计_RECOVERY-R2_20261008.md:374>) |
| SUB-AC47 | 04 | 来源更新/基准有据后继/调查已结 | 旧Snapshot不可改，新基准与cut成新结果，当前调查待复核 | NOT_RUN | [原文 L375](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/95/951bf766a6f96118__PUR-SUB-01_完整静态详细设计_RECOVERY-R2_20261008.md:375>) |
| SUB-AC48 | 04 | Finance真成功但SUB采纳失败 | 同回执补投影，无再次凭证 | NOT_RUN | [原文 L376](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/95/951bf766a6f96118__PUR-SUB-01_完整静态详细设计_RECOVERY-R2_20261008.md:376>) |

## 11. 实际未确认事项和有意版本差异

已接受的是完整静态设计和R1两根整改。待真实采用的门：PO同库最终Guard与锁序；WMS原key/完整movement及供应商custody；GR RECEIPT与ACCEPTANCE独立完整集合；Finance计价profile、前驱和原legacy凭证映射；IAM授权/代理、工程单耗、容差政策；单writer及48AC。文书已定义Required端口不表示现Owner已经存在生产路由。 [原文 L309](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/95/951bf766a6f96118__PUR-SUB-01_完整静态详细设计_RECOVERY-R2_20261008.md:309>)、[原文 L313](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/95/951bf766a6f96118__PUR-SUB-01_完整静态详细设计_RECOVERY-R2_20261008.md:313>)、[原文 L314](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/95/951bf766a6f96118__PUR-SUB-01_完整静态详细设计_RECOVERY-R2_20261008.md:314>)、[原文 L316](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/95/951bf766a6f96118__PUR-SUB-01_完整静态详细设计_RECOVERY-R2_20261008.md:316>)、[原文 L319](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/95/951bf766a6f96118__PUR-SUB-01_完整静态详细设计_RECOVERY-R2_20261008.md:319>)、[原文 L395](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/95/951bf766a6f96118__PUR-SUB-01_完整静态详细设计_RECOVERY-R2_20261008.md:395>)

成本用RECEIPT、耗用对账用COMMERCIAL_ACCEPTANCE_GROSS是明确不同业务口径，不能统一一个finishedQty。SUB PER_LEG允许真实部分成功，与PUR-RETURN同真实Part的Stock组全原子属于不同动作边界，不能为统一实现把本册多料暗变全原子，或把退货Part拆掉。固定原分母、新后继基准及历史cut应同时保留，不用当前PO刷新覆盖历史。 [原文 L190](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/95/951bf766a6f96118__PUR-SUB-01_完整静态详细设计_RECOVERY-R2_20261008.md:190>)、[原文 L232](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/95/951bf766a6f96118__PUR-SUB-01_完整静态详细设计_RECOVERY-R2_20261008.md:232>)、[原文 L262](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/95/951bf766a6f96118__PUR-SUB-01_完整静态详细设计_RECOVERY-R2_20261008.md:262>)、[原文 L264](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/95/951bf766a6f96118__PUR-SUB-01_完整静态详细设计_RECOVERY-R2_20261008.md:264>)、[原文 L276](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/95/951bf766a6f96118__PUR-SUB-01_完整静态详细设计_RECOVERY-R2_20261008.md:276>)

CloseReview的历史独审建议是非阻断明确化：若提供正式结清按钮，先给准确只读检查/应用/回查契约；首版仅责任汇总则无额外效果。当前整理不替业务决定结清动作、耗损责任或Finance政策。旧主线代码未具备新协议不是重新否定静态设计的理由。 [原文 L178](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/25/255258742277341c__CP6_X2_原12SPEC_独立完整静态审阅及最小根表_20261008.md:178>)、[原文 L182](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/25/255258742277341c__CP6_X2_原12SPEC_独立完整静态审阅及最小根表_20261008.md:182>)

## 12. 实际阅读、精确入口与必要材料队列

本轮主文395行全文逐段实读，48AC及12工作包全文；R1独审201行、R2独审128行、最终独审97行、组合126行、共同接受57行、根决策13行全文。SUB UA314行、CURRENT518行、接受索引70行、限定87行及SOURCE-MANIFEST66行按完整JSON结构阅读，标识/哈希/路径为机械元数据；CURRENT截断部分另按属性补核，不声称所有字符逐行人工朗读。

当前16项登记必要材料已归并，状态 `required_materials_consolidated`。原scope的三个Target／12SPEC完整结构已读，6个Catalogue／Plan原记录与ZIP内准确JSON pointer对象0差异。旧PB01和PLANNED_NOT_CREATED等为原始范围登记的历史状态，不能覆盖本次MD02接受。[范围原件](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/7d/7d6a119178ee26d9__ORIGINAL-THREE-TARGET-TWELVE-SPEC-SCOPE.json:1>)

132AC和36任务逐字段／SourceLine／ExactMarkdownRow与已读三正文精确相等的证据复用[operations阅读账](<D:/CP6/docs/CP6_开发设计文档_20261010/evidence/operations-reading.json>)；本册48AC／12任务来自商业组全文，其余84AC／24任务来自operations完整SCHED／PERF阅读。全132仍NOT_RUN、executed=false，任务实施授权仍false，未执行验收。原JSON结构复用不称逐行人工全文重读。

63文件／33目录的96成员身份（bytes、SHA256、GitBlob）及62个SHA清单项已与原ZIP逐项相等。R2五根回应94行与R2.1定点回应32行全文；R2.1只修PERF R05-a，SUB保持R2准确字节。39历史源码、旧被替代正文只分类／身份核对；原4份外域规范按冻结依赖引用，不能借本册完成声称外域全部wire已读。11个SUB固定代码条目的行为仍引用作者及独审在固定SHA下的记录，未冒本轮源码全审。[原包身份](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/ba/ba58fe283e260343__ALL-ORIGINAL-PACKAGE-MEMBER-IDENTITIES.json:1>)、[R2回应](<D:/CP6-archives/consolidation-20261010/commercial-cache/x2/CP6_X2_R2_五根问题集中回应及独审入口_20261008.md:25>)、[R2.1回应](<D:/CP6-archives/consolidation-20261010/commercial-cache/x2/CP6_X2_R2.1_R05-a定点回应及复审入口_20261008.md:5>)

精确SHA、结构复用及剩余历史／实际Owner边界见[商业阅读台账](<D:/CP6/docs/CP6_开发设计文档_20261010/evidence/commercial-reading.json>)。文档必要材料闭合不是PO／WMS／GR／Finance真实采用，也不是实现或生产验收通过。
