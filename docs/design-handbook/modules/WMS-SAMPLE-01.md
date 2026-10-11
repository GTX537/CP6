# WMS-SAMPLE-01 样品借还开发设计

状态：核心语义已整理，必要附件补读中；所有业务验收仍 NOT_RUN。入口：[模块目录](D:/CP6/docs/CP6_开发设计文档_20261010/模块目录.md)。

## 1. 业务目的与权威边界

仓管人员登记样品、按实际交接借出和归还，管理约定期限、逾期跟进和异常。Sample 拥有资料、每轮 Loan/Return、期限后继和跟进；Stock 拥有纳入库存的实物数量、Root/Slice、位置/保管状态及唯一移动回执。借出改变保管地点或外部保管账户，保留原 Root、客户货主和技术身份，不自动成为销售、赠送、报废、所有权转移或财务收入。[职责与规则](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/4b/4bc78efb8ce7d865__WMS-SAMPLE-01_样品借还_完整详设_R2_CANDIDATE.md:16)

STOCK_BOUND 必须绑定 Owner 核验的原范围；LEGACY_OBSERVATION 保存未核历史，可读资料和真实观察，但不派发正式借还、不进入 Stock 汇总。转正需原事实核对和迁移收据，不能换 mode 再建第二 Root。序列品整件不可分，非序列品允许基本单位下精确部分借还。损坏/短少为独立异常，失效/退役只影响未来资格，不清除尚未收回的义务。

## 2. 当前版本与接受范围

当前 R2 正文 SHA 前缀 `4bc78efb8ce7d865`，全文 148 行；精确 SHA/组合见 [必需输入](D:/CP6/docs/CP6_开发设计文档_20261010/indexes/required-inputs.json)。原五 SPEC 为借出、归还、到期、逾期、库存来源；R2 主要补齐稳定实际发生的生产和消费，不改原 17 AC、7 任务编号。[原范围](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/4b/4bc78efb8ce7d865__WMS-SAMPLE-01_样品借还_完整详设_R2_CANDIDATE.md:3)

正文停止时的 NOT_ACCEPTED 后被 X4 六册接受记录覆盖；该接受是设计接受，actualAdopted=false、业务/SQL/DB/测试/CI 均未由此执行。不能据接受记录解除实际采用门。[X4 接受](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/59/59dff54660e0a2fe__X4-SIX-TARGET-ACCEPTANCE.md:1)

## 3. 数据和不可变身份

| 对象 | 实现重点 |
|---|---|
| Sample / SourceBinding | 资料修订与库存绑定分开；保准确 stockOwner、Root 半开区间、基本单位、货主/技术/原用途和证据 |
| Loan | 每轮独立 loanId/loanNo、借用人快照、原范围和原量、稳定实发、政策、Stock 操作/回执、原期限及当前期限版本 |
| ReturnFact | 原 loanId、稳定实收、精确原借用区间映射、实收量/时间、状态、目标位置和原 Stock 回执；追加不覆盖 |
| DueRevision | 原/新期限版本、原因、批准、操作者和记录时间；保过去逾期历史 |
| FollowUp / Observation | 保存逾期/损坏/短少/映射缺口和待核现场事实，不持数量账 |
| Operation / OccurrenceUse | HTTP 原操作与业务实际发生分别持久防重，原 Loan 归还后业务墓碑仍保留 |

数量 decimal(21,8)、汇总 decimal(38,8)，均十进制字符串；旧 decimal(18,4) 原值可精确保留。范围 `[from,to)` 不重叠、不跨 UOM。CurrentOutstanding 是 **原借出范围减已真实归还范围减有权处置闭合范围** 的互斥集合，不能只减数量掩盖交叉重叠。[数据原文](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/4b/4bc78efb8ce7d865__WMS-SAMPLE-01_样品借还_完整详设_R2_CANDIDATE.md:42)

K=`(environmentId,tenantId,siteId,mode,producerOwner,sourceKind,sourceDocumentId,sourceLineId,occurrenceLeafId,direction)`；sourceKind 为 OWNER_SAMPLE_HANDOVER/WMS_SAMPLE_HANDOVER，direction=LEND/RETURN。recordVersion、HTTP key、Loan revision、日期和外观单号均非新发生。LEND 新实际记录 loanId=null；RETURN 必指原 loanId。真实再借需要新交接 leaf，旧凭据在 Root 已归还后仍只能返回 Loan1 原回执。[稳定发生](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/4b/4bc78efb8ce7d865__WMS-SAMPLE-01_样品借还_完整详设_R2_CANDIDATE.md:104)

## 4. 五项业务流程与状态

库存绑定由 Stock selector 取得原范围并重新解析；资料/绑定/操作/审计同事务保存，不能凭相似产品代码或 LotNo 模糊认领。多个标签指同物时物理范围只计一次，资料 PUT 不搬库。

借出先 prepare 完整 manifest，再于最终 Stock UoW 复验范围、当前限制、未闭 Loan、借用人/政策、交付实际和目标保管身份。合法移动与 `Loan OPEN`、原回执、Audit、Operation 一次提交；缺实际适配保持 PREPARED/WAITING_ADOPTION。已有别人的 Claim 不能被擅自释放。Loan 正常沿 OPEN → PARTIALLY_RETURNED → RETURNED；有权损失处置另成 CLOSED_WITH_DISPOSITION。[绑定与借出](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/4b/4bc78efb8ce7d865__WMS-SAMPLE-01_样品借还_完整详设_R2_CANDIDATE.md:56)

归还绑定原 Loan 未归还范围，Stock 将**同一 Root** 从保管位置移回正式地点，ReturnFact/Loan 投影/原回执同事务提交。归还不是新采购 IN；quantity 只验证范围度量。损坏实收可入受限待检位置，但不自动变可借或 Quality PASS。缺原来源或 Owner 结果未知时保 ReturnObservation，正式 outstanding 不减少、可借池不释放。[归还](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/4b/4bc78efb8ce7d865__WMS-SAMPLE-01_样品借还_完整详设_R2_CANDIDATE.md:68)

期限以 Site 当地日期计算：today=dueDate 为 DUE_TODAY，today>dueDate 且 outstanding>0 才 OVERDUE；无日期为 UNSPECIFIED，需政策允许。回录过去真实约定使用 historicalRecord 并留理由/双时间，不能改成今天。延期追加 DueRevision 且 CAS；退役保未闭 Loan 和 Return 通道。逾期按 Loan 未闭范围查询，不能按 Sample.Status 过滤；FollowUp 结案不等借用义务关闭，不自动收费或生成应收。[期限与逾期](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/4b/4bc78efb8ce7d865__WMS-SAMPLE-01_样品借还_完整详设_R2_CANDIDATE.md:74)

## 5. API 和 Owner 合同

候选前缀 `/api/wms/specials/v1/samples`：options、样品/来源、每轮 loan/history、due、operations；register/profile、loans/prepare/submit、returns、due-revisions、retire、observations。所有对象写需 If-Match（缺 428、过期 412）；业务条件错误 422，范围竞争/重复 409，Owner 不可达 503，跨范围安全 404。profile 拒只读 status/qty/borrower/actor/tenant 字段。[完整 API](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/4b/4bc78efb8ce7d865__WMS-SAMPLE-01_样品借还_完整详设_R2_CANDIDATE.md:84)

拟议 SampleStockAdapter：ReadCandidates 返回精确范围/来源向量/限制与 coverage；PrepareCustodyChange 冻结 LEND/RETURN、实际发生、目标和 expectedHeads；ApplyInUow 返回不可变 StockReceipt、原 Root 范围和 from/to custody；QueryOriginal 保 FOUND_TERMINAL/PROCESSING/UNKNOWN/NOT_FOUND_OBSERVED。Root 不得换新、货主不变、用途明确非销售。MASTER 只核正式 Location/容量，不能把旧 SourceLocationId/WmsBinId 当正式 LocationId；Quality 管理当前资格及受限实收。

handover-occurrence-options/occurrences 提供真实来源及 opaque selector；无上游时 evidence-options/register 以稳定凭据 `(evidenceOwner,documentId,lineId,eventOrdinal,direction)` 唯一映射实际记录。证据无稳定 leaf 时只能 OBSERVATION_ONLY，不能拿 messageId/URL/附件名/时间戳造键。prepare、Return、Stock adapter 和原回执全链保 K/native refs。[公开发生生产与传播](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/4b/4bc78efb8ce7d865__WMS-SAMPLE-01_样品借还_完整详设_R2_CANDIDATE.md:108)

## 6. 事务、并发与未知恢复

Sample 遵从 Stock 共同 UoW：先依稳定 Root 序取根/份额及相关控制，再取 Loan/绑定，不持 Loan 锁倒取 Stock。最终事务同时裁决 operation 唯一、范围排他和 OccurrenceUse。只靠“目前范围可用”不能防已经归还的旧交付重报。同 K 同正文查回原 Loan/Return/StockReceipt，无论 HTTP key、recordVersion 或当前 Loan 状态；同 K 异借用人/范围/量/时刻/loanId 拒冲突。[并发要求](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/4b/4bc78efb8ce7d865__WMS-SAMPLE-01_样品借还_完整详设_R2_CANDIDATE.md:94)、[业务防重](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/4b/4bc78efb8ce7d865__WMS-SAMPLE-01_样品借还_完整详设_R2_CANDIDATE.md:114)

丢响应保原 operation/manifest 查回；APPLIED 只回原结果。NOT_FOUND_OBSERVED 不能授权新键重做，Owner UNKNOWN 的实际收到只保观察。当前政策撤销只禁新借，不抹旧 Loan。无法同事务接线则正式应用 fenced，不能用异步最终一致宣布已收回。一次实收涉及多 Loan 时须上游稳定拆成原 line/leaf，浏览器不得自行拆键绕限。

## 7. 权限、留存和审计

行范围 Site/仓/客户/用途与动作 lend/return/extend/retire 权限共同满足；borrower 联系信息独立授权，逾期导出不能泄漏。actor/记录时间/Owner 结果来自后端，审计保原版本、原 operation、occurredAt/recordedAt、精确范围、政策和回执。新增实际记录权只允许保存有据观察，不能绕库存最终门。未决操作、Loan、映射和防重墓碑按有权政策保留，未知期间不清理。[权限](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/4b/4bc78efb8ce7d865__WMS-SAMPLE-01_样品借还_完整详设_R2_CANDIDATE.md:124)、[留存与旧权限](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/4b/4bc78efb8ce7d865__WMS-SAMPLE-01_样品借还_完整详设_R2_CANDIDATE.md:100)

## 8. 页面和输入校验

P01 服务分页列表，P02 六页签资料/来源/轮次/期限/异常/审计，P03 借出，P04 绑定 Loan 的部分归还，P05 到期/延期，P06 逾期跟进，P07 原操作恢复。默认 50、最大 100，total 来自有权查询。借用人用授权目录 Ref，姓名只显示；样品号用服务号源，不默认 PCS。due 红标、日差和时区由服务返回。

归还显示原量、已回、剩余及范围；部分归还继续原期限。P03/P04 显示稳定来源和 PROVEN/OBSERVATION_ONLY，不许改 K。UNKNOWN 页面只查原意图，刷新/Back/关闭不生成第二动作；409 保草稿，迟响应不覆盖新筛选，字段权限变化清 PII 缓存。提醒只是应用内设计，本次没有向客户/第三人发消息。[七页设计](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/4b/4bc78efb8ce7d865__WMS-SAMPLE-01_样品借还_完整详设_R2_CANDIDATE.md:26)

## 9. 实施顺序与历史迁移

按 S01–07：资料/SourceBinding/Loan/Return/DueRevision/Case/Operation 及唯一约束 → 精确 selector、实际发生 producer 和 Stock 同事务适配 → 连续借还/观察/退役 → Site 日期与期限 CAS → 页面 → IAM/旧新写入隔离 → 契约与并发验收。[任务](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/4b/4bc78efb8ce7d865__WMS-SAMPLE-01_样品借还_完整详设_R2_CANDIDATE.md:130)

原稿固定 CP6 `90c871fe…` 源码读取发现：旧 SampleStock.Quantity 独立，借出覆盖一行历史，Expire 可使尚未归还记录退出旧逾期查询，UI 只取 500 再分页。此为源文固定映射，本轮未新做代码审计。迁移保原 SampleNo/数量/最后借还字段；缺轮次标 HISTORY_INCOMPLETE，status3 且无 ReturnedAt 标 OUTSTANDING_UNKNOWN，缺 Stock 凭证标 SOURCE_UNPROVEN，旧自由姓名不自动匹配 CRM 联系人。采用后须隔离旧 lend/return/expire/update/delete/导入/后台写入；expire 映射 retire 必须显式兼容，不能悄改 API 意义。[旧实现证据](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/4b/4bc78efb8ce7d865__WMS-SAMPLE-01_样品借还_完整详设_R2_CANDIDATE.md:10)、[迁移](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/4b/4bc78efb8ce7d865__WMS-SAMPLE-01_样品借还_完整详设_R2_CANDIDATE.md:100)

## 10. 验收场景和状态

17 AC 全部 NOT_RUN。完整业务链：绑定原 10EA → 借 `[0,6)` → 还 `[0,2)` 后剩 4 → due 次日逾期 → 合法延期 → 剩余实收 → Loan1 RETURNED → 新真实交接建立 Loan2。验证绑定不新增库存、相同 Root/Owner 保留、竞争仅一次、越界/重叠归还零效果、损坏实收与再借资格分离、断线沿原回执恢复、旧发生在已归还后不复借、浏览器时区不影响 due、退役仍追未还、旧缺证不清零。

同时覆盖延期 CAS、1000 行不被 500 截断、UI 重复/迟返、PII/跨 Site、旧 writer 未退出时应用 fenced、多标签去重、序列品不可分、八位精度与单位错误。R2 扩充业务 K 换 HTTP/source version 不重复，无稳定 leaf 只观察。没有运行旧两例或新测试。[全部验收及 E2E](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/4b/4bc78efb8ce7d865__WMS-SAMPLE-01_样品借还_完整详设_R2_CANDIDATE.md:140)

## 11. 编码前缺口

S-G1 Stock 精确保管模式和共同 UoW；S-G2 MASTER/Quality 目标资格与受限实收；S-G3 借用、期限、延期、退役和损失处置政策；S-G4 IAM/联系人真实 selector；S-G5 全部旧 writer 清单与切换。每门需 provider repo/commit/schema/method、字段映射、Scope/mode 和专业 Owner 采用证据。归档设计接受不能代替这些证据。共享评审、目标 UA/CURRENT 与适用性/采用保持记录已补核；跨Owner双方合同与现实采用仍待集中核对。[采用门](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/4b/4bc78efb8ce7d865__WMS-SAMPLE-01_样品借还_完整详设_R2_CANDIDATE.md:128)

## 12. 来源与阅读覆盖

本轮正文 1–148 行已全文实读，含所有五 SPEC、R2 发生生产/消费合同、页面、任务和 17 AC；已读 X4 接受、最终 R2.1 独审及来源限定。逐件 SHA、读段及待读附件见 [root 阅读账](D:/CP6/docs/CP6_开发设计文档_20261010/evidence/root-specialty-reading.json)。源文历史源码/测试叙述保其原证据边界，不冒充本轮实现或验证。

补读完成记录及准确同文复用方法见[X4六专项阅读与采用边界](D:/CP6/docs/CP6_开发设计文档_20261010/evidence/X4-六专项阅读与采用边界.md)。这不替代外域依赖的双方集中核对或实际业务验收。

## 必要材料整理结论

当前17项必需引用均已完整阅读或使用有证据的精确复用：主文、当前接受/审查、原范围、AC与任务文字以及来源资格已纳入设计。历史成员目录只证明来源身份，不等于旧代码全文审计；外域实际采用、Provider和业务运行继续按本册门禁确认。见[逐目标必需引用核对](../evidence/root-specialty-required-closure.json)。后续整任务进行一次集中跨模块复核，不将静态设计整理等同实际验收。
