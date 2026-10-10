# WMS-VMI-01 · VMI作业与结算依据

整理状态：核心规则已整理，共用前驱评审、来源与必要依赖继续核对。原16项AC全部NOT_RUN，所有新adapter、政策与实际接线UNPROVEN。本模块提供保管库存和服务计费依据，不能据本域确认宣称已开票、过账或收款。

## 1. 业务目的与责任边界

覆盖客户库存、用量、计费计算、确认、财务接口边界5项原SPEC。Main WMS保存客户保管库存的查询投影、用量依据、计算版本和本域确认；Stock仍是Root/Slice/数量/Claim权威。Finance负责服务商业约定的采用、币种/税/舍入、AR发票及会计；OA只提供不可变决定，应用仍由业务Owner完成。[原范围与Owner](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/b6/b6a79341caa5168a__WMS-VMI-01_VMI作业与结算依据_完整详设_R2_CANDIDATE.md:8)、[责任](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/b6/b6a79341caa5168a__WMS-VMI-01_VMI作业与结算依据_完整详设_R2_CANDIDATE.md:18)

客户所有库存不自动是企业资产；WMS确认不等于客户接受或Finance受理。MOVE、预留、拣货、样品借出不是销售消费。用量只采用合同指明的实际CONSUME或准确控制转移事实，未知种类为UNCLASSIFIED，不生成扣库。[唯一业务口径](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/b6/b6a79341caa5168a__WMS-VMI-01_VMI作业与结算依据_完整详设_R2_CANDIDATE.md:20)

## 2. 当前规范与接受范围

当前为R2 / R02323，以[必需输入](D:/CP6/docs/CP6_开发设计文档_20261010/indexes/required-inputs.json)保存的完整SHA选定。它是X4六册准确接受组合之一：INK R2.1，其他五册R2，共27 SPEC、95 AC、46任务。正文的STOPPED/NOT_ACCEPTED保原历史身份，由另存的2026-10-08静态接受覆盖当前设计处置，未改其运行状态。[六册接受](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/59/59dff54660e0a2fe__X4-SIX-TARGET-ACCEPTANCE.md:5)

VMI涉及最终累计评审的R01计价数量与基数、R02完整RequestedUse两根关闭；关闭的是可实现的静态规则，真实Finance服务义务adapter和Stock当前门仍需准确采用。[八根结论](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/58/5847ad3a0d225be4__FINAL-R2.1-INDEPENDENT-REVIEW.md:55)

## 3. 数据、单位、时间与业务身份

Scope从服务端取得environmentId/tenantId/siteId/mode；FinanceContext另有legalEntityKey/ledgerKey，不能用Site猜账套。SourceRef保原owner/id/version/schema/digestDomain，外域版本视为opaque字符串。[类型边界](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/b6/b6a79341caa5168a__WMS-VMI-01_VMI作业与结算依据_完整详设_R2_CANDIDATE.md:42)

| 对象 | 关键内容 | 唯一性/不变性 |
|---|---|---|
| VmiStockSnapshot | 生效时点、knowledgeCut、各Owner frontier、完整性/缺口、Root spans、UOM分组、用途问题及评价知识态 | 历史和当前切面分别固定；不能用当前Stock替历史 |
| UsageFact | 原发生、客户、合同线、Root ranges、数量/UOM、occurredAt/recordedAt、分类及更正 | Scope+sourceOwner+稳定发生+line+range+kind唯一；源修订不是新用量 |
| Calculation | 年月、serviceObligationId、revision、合同/快照/政策、linesDigest、同币种总额、状态 | 新计算新版本，已确认不可变 |
| BillingLine | 当地日、价段、产品、基本量/UOM、计价量/UOM、基数、精确换算、费率、raw/rounded/delta | 同一PriceTerm贯穿计算、审批、确认与Finance |
| ConfirmationFact | 准确计算版本与digest、确认人/时间、批准/政策、operation/audit | 一条稳定义务槽只允许一个有效正向确认 |
| FinanceHandoff | 原确认、stableObligationKey、manifest、transport/application分轴、原Finance结果 | intakeKey不因重算、换消息或重试改变 |

源定义见[模型字典](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/b6/b6a79341caa5168a__WMS-VMI-01_VMI作业与结算依据_完整详设_R2_CANDIDATE.md:44)。intakeKey按Scope、WMS_VMI、serviceObligationId、stableLineId、yearMonth、chargeKind持久产生并冻结；更正另外使用稳定correctionId和原确认引用。[义务槽与原键](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/b6/b6a79341caa5168a__WMS-VMI-01_VMI作业与结算依据_完整详设_R2_CANDIDATE.md:50)

基本Quantity为decimal(21,8)字符串，聚合decimal(38,8)，Rate最多12位小数；金额最终按选定政策输出decimal(28,8)。不能用JS number或隐式截位算费用。生效日与记录/知悉时点分开，迟到更正在新knowledgeCut形成后继测算，旧报表保留。[精度](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/b6/b6a79341caa5168a__WMS-VMI-01_VMI作业与结算依据_完整详设_R2_CANDIDATE.md:42)、[双时间与勾稽](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/b6/b6a79341caa5168a__WMS-VMI-01_VMI作业与结算依据_完整详设_R2_CANDIDATE.md:62)

## 4. 处理流程与计价算法

正常首批只接受DAILY_CLOSING：按Site计费时区每个当地日结束边界前已生效、且固定knowledgeCut已知的合同保管量计费。DST日仍算一个当地计费日；旧(月初+月末)/2模型只能标LEGACY_ESTIMATE，不能正式确认。[按日模型](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/b6/b6a79341caa5168a__WMS-VMI-01_VMI作业与结算依据_完整详设_R2_CANDIDATE.md:20)

1. 固定客户合同集合、月份、当地全部日边界、Stock快照/frontier、费率段、换算与舍入政策。
2. 按Owner/Product/技术/基本UOM/位置类保留完整来源和范围；任一缺日、缺转换、缺币种使相应组INCOMPLETE。
3. 精确计算`priceQuantity = baseQuantity × numerator / denominator`。只有相同单位且有明确IDENTITY依据才允许1/1。
4. 每日行只算`rawAmountExact = (priceQuantity / priceBasisQty) × dailyRate`，不再乘整月天数。priceBasisQty>0，priceBasisUomRef必须与priceUomRef逐字段相等。
5. 保留约分的有理数分子/正分母，各整数最多128位；priceQuantity不能精确表示为≤8位小数则拒绝，不在数量层舍入。
6. 按Finance选定LINE或TOTAL唯一舍入点输出金额和精确roundingDelta。LINE汇总已舍入日行，TOTAL先合精确数再舍入，不能双重舍入。

完整算法及传播字段见[R2计价量纲](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/b6/b6a79341caa5168a__WMS-VMI-01_VMI作业与结算依据_完整详设_R2_CANDIDATE.md:100)。例：2500 KG按1/1000转换为2.5 T，计价基数0.5 T、费率20/基数/日，单日raw=100；这是设计示例，不是实际合同政策。[原精确反例](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/b6/b6a79341caa5168a__WMS-VMI-01_VMI作业与结算依据_完整详设_R2_CANDIDATE.md:116)

| 状态/动作 | 规则 |
|---|---|
| DRAFT→COMPUTED | 完整固定输入和计算；已知部分可预览但有缺口则INCOMPLETE |
| 重算 | 创建后继候选，不覆盖已确认版本；批量计算保固定全集和每项状态 |
| 确认 | COMPLETE且COMPUTED，核当前政策/SOD/版本；必要时冻结OA完整Subject并等待合法决定 |
| 批准后来源变化 | BLOCKED并保原决定，不在旧批准下重算 |
| CONFIRMED | Confirmation、冻结版、审计、Result和FinanceHandoff Outbox共同提交 |
| 确认后更正 | 先读原Finance结果，创建AdjustmentIntent；按Finance允许的correctionOf继续，不抹原AR |

确认和更正规范见[确认最终门](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/b6/b6a79341caa5168a__WMS-VMI-01_VMI作业与结算依据_完整详设_R2_CANDIDATE.md:70)、[财务原结果恢复](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/b6/b6a79341caa5168a__WMS-VMI-01_VMI作业与结算依据_完整详设_R2_CANDIDATE.md:78)。

## 5. API、Stock用途读取与Finance交接

候选前缀`/api/wms/specials/v1/vmi`。API包括options、stock-snapshots创建/分页、usage、calculations创建/详情/明细/successors/confirm、operations原结果、handoffs和adjustment-intents。对象写携If-Match；前端YYYY-MM唯一转换为API YYYYMM，年月不合法400；新接口并未证明实际存在。[完整候选路由](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/b6/b6a79341caa5168a__WMS-VMI-01_VMI作业与结算依据_完整详设_R2_CANDIDATE.md:82)

Stock RequestedUse完整四字段为action、purposeRef、demandRef、technicalRequirementRef，后两项可明确null但不能省略；类型采用原Stock schema及native成员/摘要域。用途来自有权stock-use-options selector，浏览器不自由填写purpose/demand。context=null表示不评价可用性，物理量仍可查。[精确用途与selector](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/b6/b6a79341caa5168a__WMS-VMI-01_VMI作业与结算依据_完整详设_R2_CANDIDATE.md:106)

| 查询用途 | 映射与返回要求 |
|---|---|
| CURRENT冻结快照 | Stock SQ07 history-snapshots，AS_KNOWN、CAPTURE_NOW、effectiveAt=null，保存原vector/evaluatedAt；缺资格组件为UNKNOWN，不拼另一查询值 |
| 即时CURRENT/PLANNED预览 | 可走SQ02，完整use与evaluation；若存快照需同完整观察向量，缺一致性为PARTIAL |
| ORIGINAL_KNOWN / RESTATED历史 | SQ07 AS_KNOWN / EFFECTIVE_RESTATED，明确历史生效边界+EXACT_VECTOR；knowledgeCut时钟不等于向量 |
| 无用途问题 | NOT_EVALUATED、Free=null；有问题但证据不足为UNKNOWN、Free=null；只有KNOWN才显示准确Free |

正式月费不使用PLANNED Free；换用途/评价时间必须清旧Free重新查询，并保context/use/evaluation/cut到摘要、cursor、缓存和导出。[Stock映射全规则](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/b6/b6a79341caa5168a__WMS-VMI-01_VMI作业与结算依据_完整详设_R2_CANDIDATE.md:110)

VmiFinanceBasisAdapter交付**服务计费依据**，不是Sales Shipment。manifest包含稳定义务槽、确认id/版本/hash、客户货主/billTo/payer、半开服务期间、全部日行的基本/计价/基数量纲、转换分子分母、精确金额/舍入、sourceCut与原合同/政策、adjustmentOf或null。Finance回ACCEPTED_BASIS也仅代表接受依据；invoice/AR/GL/收入/收款独立取权威结果。[manifest](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/b6/b6a79341caa5168a__WMS-VMI-01_VMI作业与结算依据_完整详设_R2_CANDIDATE.md:74)

若现有AR原生接口只支持Order/Shipment，需准确新增服务义务适配；不能把本合同字段塞进OrderId。未采用时Outbox为WAITING_ADOPTION，本地确认历史仍可读，不显示已送达。[实际采用限定](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/b6/b6a79341caa5168a__WMS-VMI-01_VMI作业与结算依据_完整详设_R2_CANDIDATE.md:76)

## 6. 事务、竞争和恢复

Operation以scope+operationId唯一并冻结`inputCodec=vmi.v1`、canonical输入快照与digest。同业务槽只有一个有效正向确认；换calcVersion或运输key不增加收费额度。计算/确认业务行、Operation、Audit、Receipt同次提交；批次按子事务记录每项结果，不能将部分成功标整批COMPLETE。[原键模型](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/b6/b6a79341caa5168a__WMS-VMI-01_VMI作业与结算依据_完整详设_R2_CANDIDATE.md:52)

本域锁序scope控制→服务义务槽→calculation→operation。读取历史cut不锁活跃库存；确认需要Owner同UoW source guard，或不可变且已封账cut的准确证明。先读HTTP再写不能冒充该门。确认commit后才派Outbox，运输状态不反写本域确认。[共同决定边界](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/b6/b6a79341caa5168a__WMS-VMI-01_VMI作业与结算依据_完整详设_R2_CANDIDATE.md:86)

响应丢失按原operation恢复；交接超时按原intakeKey/inputDigest查回并重投同bytes，不换键再开票。异Owner/Scope/digest回执隔离；旧revision不倒退，缺前驱则补链。NOT_FOUND_OBSERVED不是零效果证明；UNKNOWN保原消息及义务槽，不能按普通缓存过期清除。[交接恢复](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/b6/b6a79341caa5168a__WMS-VMI-01_VMI作业与结算依据_完整详设_R2_CANDIDATE.md:78)、[结果读权](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/b6/b6a79341caa5168a__WMS-VMI-01_VMI作业与结算依据_完整详设_R2_CANDIDATE.md:88)

## 7. 权限与敏感字段

沿用calculate/confirm逻辑区分；read、usage-read、amount-read、history、export、finance-handoff-retry须核实际IAM注册。客户/Site/仓行范围、金额和合同单价分别授权；批作业需明确服务身份和范围，制作人不能借服务账号成为独立确认人。新写在最终门核当前权限/epoch，原成功按当前结果读权回读。[权限](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/b6/b6a79341caa5168a__WMS-VMI-01_VMI作业与结算依据_完整详设_R2_CANDIDATE.md:92)

金额字段无权时列、导出、筛选、排序、错误和总量都不能旁路泄露；客户端不在localStorage保存客户金额，恢复完整intent保服务端。历史已引用金额/政策/快照与未知交接须保留准确证据及当前读控。[敏感恢复资料](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/b6/b6a79341caa5168a__WMS-VMI-01_VMI作业与结算依据_完整详设_R2_CANDIDATE.md:84)

## 8. 页面交互与服务端校验

P01客户库存、P02客户库存明细、P03用量、P04新测算、P05计算详情、P06原操作/交接分别展示不同事实。数量按UOM、金额按币种分组；缺估值显示UNKNOWN，不能补0。分页默认50、上限100，cursor绑定snapshot/过滤器/permissionEpoch，迟到请求不串客户。[页面入口](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/b6/b6a79341caa5168a__WMS-VMI-01_VMI作业与结算依据_完整详设_R2_CANDIDATE.md:26)

计算明细必须显示基本量、换算比、计价量、基数单位/数量、费率、精确及舍入金额。缺日/缺转换/缺币种明示INCOMPLETE并禁确认；不能隐藏缺行后给总额。月显示YYYY-MM、API为YYYYMM，服务校验年1–9999与月1–12。缺币种/费率/单位不能用JPY、1或PCS默认值；0费率须真实零价合同。[合同完整性](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/b6/b6a79341caa5168a__WMS-VMI-01_VMI作业与结算依据_完整详设_R2_CANDIDATE.md:22)、[页面恢复](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/b6/b6a79341caa5168a__WMS-VMI-01_VMI作业与结算依据_完整详设_R2_CANDIDATE.md:38)

缺If-Match为428、旧版本412、同槽已确认/同键变义/来源变化409；缺历史/混单位/政策缺/不支持口径422，权威不可达或提交未知503。确认、本域批准应用、交接接收、AR应用、发票/GL/收入等状态分别显示。[稳定错误](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/b6/b6a79341caa5168a__WMS-VMI-01_VMI作业与结算依据_完整详设_R2_CANDIDATE.md:84)

## 9. 实施顺序与旧实现兼容

原文固定Main SHA `90c871fe571fd6b390f53e8678376d7ce60bcb60`的旧实现分析指出：当前Stock被当指定月末/首算月初，缺UnitPrice作0，按月初末均值计费，Confirm只改布尔；前端YYYY-MM与服务6位YYYYMM不一致。这是原设计保存的代码证据，本轮未重跑旧测试或完整源码审计。[既有实现](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/b6/b6a79341caa5168a__WMS-VMI-01_VMI作业与结算依据_完整详设_R2_CANDIDATE.md:10)

按V01数据与义务槽、V02历史cut/用途adapter、V03精确计算、V04冻结确认/审批、V05Finance服务义务接收、V06页面、V07权限/旧writer切换、V08验证推进。[原任务](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/b6/b6a79341caa5168a__WMS-VMI-01_VMI作业与结算依据_完整详设_R2_CANDIDATE.md:124)

旧BillingNo/年月/Begin/End/Avg/Confirmed原值保持，标POINT_IN_TIME_ESTIMATE；旧Confirmed不冒充AR结果，也不能自动再发Finance。逐条LegacyDisposition才说明如何承接；旧计算/导入/定时/confirm写者按Scope+义务槽切epoch，旧writer未围住就禁新确认。新模块的精确数与锁/唯一约束要按Main实际数据库Provider验证。[迁移限定](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/b6/b6a79341caa5168a__WMS-VMI-01_VMI作业与结算依据_完整详设_R2_CANDIDATE.md:94)

## 10. 验收场景

| 原AC | 核心断言 |
|---|---|
| 01 | 客户及企业库存隔离，100 EA+5 KG不能105；用途不同Free按准确问题分别算 |
| 02 | 5月末不进6月收货；原知/重述分cut；无用途NOT_EVALUATED，缺用途证据UNKNOWN |
| 03 | 仓内MOVE不计消费，真实CONSUME10仅一Usage |
| 04 | 任一日缺来源为INCOMPLETE，不能用现在库存补历史 |
| 05 | 31天中10×100+21×80=2680量日，示例0.5/EA/日得1340；计价基数和跨UOM转换一致 |
| 06–07 | 多库不重复、费率按日拆段；不可表示数量拒绝，有理1/3只在选定点舍入；非法月份/科学数/缺币种拒绝 |
| 08 | 同槽竞争仅一Confirmation及Handoff |
| 09 | 批准后金额/cut变化阻应用，完整计价字段贯穿审批 |
| 10 | 已确认重算建后继，旧Confirmed不显示AR POSTED |
| 11–12 | 接收ACK与业务应用分开；响应丢失沿原键查回；异hash隔离且不重复开票 |
| 13 | 换客户、关闭窗口、刷新只恢复原操作，不串结果 |
| 14 | 未封旧writer/映射不全禁新确认，不倒造sourceCut |
| 15 | 同快照分页/总量与全部渠道金额字段保护 |
| 16 | 撤权后原结果按当前读权可见，新写禁止；未知交接和原键保留 |

16项与R2子例均NOT_RUN；上述数字是规范中的合成算法例子。[原AC](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/b6/b6a79341caa5168a__WMS-VMI-01_VMI作业与结算依据_完整详设_R2_CANDIDATE.md:135)、[R2附加断言](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/b6/b6a79341caa5168a__WMS-VMI-01_VMI作业与结算依据_完整详设_R2_CANDIDATE.md:116)。

## 11. 待核采用与材料范围

G1 Stock历史cut/完整frontier/Source控制；G2 Finance服务义务、费率/币种/舍入/税与实际受理映射；G3 WMS按日边界/确认政策/旧账处置；G4 IAM/OA范围/字段/SOD/Subject都须给实际provider源码/schema/native方法、逐字段映射、Owner决定和适用Scope/mode/效期。没有实际证据时actualAdopted=false，不借文档接受开放生产动作。[四类采用门](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/b6/b6a79341caa5168a__WMS-VMI-01_VMI作业与结算依据_完整详设_R2_CANDIDATE.md:120)

逐Target UA/CURRENT、三轮X4评审、原范围及AC/任务已补核。实际Stock/FIN-AR/OA的双方版本合同和运行采用仍待集中核对；不把原报告的部分源码阅读扩大成本轮完整源码审计。

## 12. 来源与阅读覆盖

本轮全文实读VMI R2 1–141；复用本轮已全文读的同SHA X4接受1–55、最终审查1–81、根决定1–13、复用限定1–102、来源阅读资格1–17。精确记录见[root-specialty-reading.json](D:/CP6/docs/CP6_开发设计文档_20261010/evidence/root-specialty-reading.json)。未运行计算候选、SQL、测试、真实计费或部署。

补读完成记录及准确同文复用方法见[X4六专项阅读与采用边界](D:/CP6/docs/CP6_开发设计文档_20261010/evidence/X4-六专项阅读与采用边界.md)。这不替代外域依赖的双方集中核对或实际业务验收。

## 必要材料整理结论

当前17项必需引用均已完整阅读或使用有证据的精确复用：主文、当前接受/审查、原范围、AC与任务文字以及来源资格已纳入设计。历史成员目录只证明来源身份，不等于旧代码全文审计；外域实际采用、Provider和业务运行继续按本册门禁确认。见[逐目标必需引用核对](../evidence/root-specialty-required-closure.json)。后续整任务进行一次集中跨模块复核，不将静态设计整理等同实际验收。
