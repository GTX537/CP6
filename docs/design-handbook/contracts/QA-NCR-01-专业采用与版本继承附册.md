# QA-NCR-01：26 项设计采用、责任边界与版本继承

本附册解释当前 R12 的 26 项独立设计责任记录，供开发者确定“谁提供什么原件、谁判断当前状态、哪个历史问题已在后继关闭”。它记录归档结论，不签发新的专业采用或运行许可。机器账见 [完整采用阅读记录](D:/CP6/docs/CP6_开发设计文档_20261010/evidence/QA-NCR-01-adoption-reading.json)，主册见 [QA-NCR-01](D:/CP6/docs/CP6_开发设计文档_20261010/modules/QA-NCR-01.md)。

## 1. 当前入口和版本链

准确候选为 `QA-NCR-SIX-SPEC-R12-20261004`，主文 SHA `b341a22f91328370e92bd7817664fd12cfd71fb3724b410a8a5e6748e2e2b29c`，清单 SHA `00bd4bd92b51579fa5c7a27715311ae528640dd7869a1a39f10ebbd0560bc036`，ZIP SHA `aa5764a71ada9447c7d0481a5d6faca3ead66b6965a204fd8452cf558307887d`。三组当前原记录分别是 [Finance/WMS 五项](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/b5/b5c7cf18171b011d__QA-NCR-R12-FINANCE-WMS-PRECISE-DESIGN-DELTA-ADOPTIONS-20261004.json>)、[Main/技术/PUR/权限十一项](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/70/706343f425114b3c__QA-NCR-R12-MAIN-TECH-PUR-AUTHORITY-PRECISE-DESIGN-DELTA-ADOPTION-REVIEW.txt>)、[QA/MES 十项](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/5a/5a52092d2e699241__QA-MES-R12-PRECISE-OWNER-DELTA-ADOPTION-RECORDS.json>)；26 项均为 `AUTHOR_DESIGN_ADOPTED_EXACT_CANDIDATE`，各自限定专业责任。包内 ADOPTION-DELTA 历史索引不能代替这些当前原记录，也不能因旧记录存在就自动升级签名。

| 版本 | 当时的问题与后续处置 | 阅读入口 |
| --- | --- | --- |
| R9 | OUTPUT 嵌套闭型仅五个映射字段，拒绝原件要求的 receiptSubject；策略选择旧 18 字段而原 ResolutionEvidence 为 27 字段。Main 完整 S03 与 QA-IPQC/QAP 对应范围保留 RETURN。 | [Main R9 原记录](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/b9/b91c7b2e289c020a__MAIN-TECH-PUR-R9-PRECISE-OWNER-ADOPTION-RECORDS.json>)、[QA/MES R9 原记录](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/41/41018cdb5c770814__QA-MES-R9-PRECISE-OWNER-ADOPTION-RECORDS.json>) |
| R10 | 用显式版本化无损 adapter 保存 API 五字段与 standalone 六字段两份原件，完整消费 27 字段。源形状问题关闭，但示例 Decision 的 Family/PlanInstance 与新 QAP context 不同，不能声明 ALLOWED 正常源链。 | [R10 原记录](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/03/03dce6ea7c69b9ca__QA-MES-R10-PRECISE-OWNER-DELTA-ADOPTION-RECORDS.json>)、[R10 原报告](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/9e/9e267ec497ca1a7a__QA-MES-R10-PRECISE-DESIGN-DELTA-ADOPTION-REVIEW.txt>) |
| R11 | 六个 native proof、实际 Task 计划、同登记 Family、完整目录与 cut 已修正；只剩 NON_STOCK Receipt93 引用旧 Head 摘要，完整直接依赖成功链仍 RETURN。 | [R11 原记录](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/68/687eb233b383c18f__QA-MES-R11-PRECISE-OWNER-DELTA-ADOPTION-RECORDS.json>) |
| R12 | Receipt93 指向最终 Head91 的完整原 body/raw；当前记录确认各自设计范围采用。170 个其余普通行保持 R11；历史 RETURN 原件仍保留。 | 上述三组当前原记录及 [QA/MES R12 原报告](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/18/18c79dfa59c770cd__QA-MES-R12-PRECISE-DESIGN-DELTA-ADOPTION-REVIEW.txt>) |

R12 尾链具体身份：Head91 完整 body/raw SHA 为 `3007289b98fe024eab52ff9acda38d442377be08be2ac76624bef5894a780ca6`；Receipt93 完整 body/raw SHA 为 `0238296d0f488acd1d6097aa736ecc83e8bad04f8b2f252723560766995ec53f`。Head Evidence 用完整原 body 摘要；HEAD etag、业务请求域、成员域、QAP native 域各有各的规则，不能互换。原报告中的独立算术核对是当时的文档证据；本次普通数据独立核对见 [普通场景与来源闭包](D:/CP6/docs/CP6_开发设计文档_20261010/contracts/QA-NCR-01-普通场景与来源闭包.md) 及 [补读台账](D:/CP6/docs/CP6_开发设计文档_20261010/evidence/QA-NCR-01-supplement-reading.json)。

## 2. Main、技术、采购与权限：十一项

| 项 | 责任与实现要求 | 不能据此推出 |
| --- | --- | --- |
| [M01](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/70/706343f425114b3c__QA-NCR-R12-MAIN-TECH-PUR-AUTHORITY-PRECISE-DESIGN-DELTA-ADOPTION-REVIEW.txt:47) Main core / close / H30 | 唯一 `(E,ncrId)` Head；新 Main6 与旧 Consumer5 保持各自 codec/ref/etag。服务端列举全部 15 类义务，含跨分区；全 guard、Use、Receipt、checkpoint 同真实事务提交。H30 完整 selector→control→INPUT9→delivery 与冷恢复保原。 | 某一专业成功便可全单关闭；远程 CURRENT 可替事务门。 |
| [M02](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/70/706343f425114b3c__QA-NCR-R12-MAIN-TECH-PUR-AUTHORITY-PRECISE-DESIGN-DELTA-ADOPTION-REVIEW.txt:59) 完整 IPQC S03 与依赖关闭 | API5/standalone6 两原件、完整策略27、真实 Task/Plan/Family/cut/raw/current，以及直接依赖 15 类关闭链一起保全。R12 正确 Receipt 尾链纳入本范围。 | 回写历史 RETURN；删字段凑旧闭型；用其他 Owner 签名代 Main。 |
| [M03](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/70/706343f425114b3c__QA-NCR-R12-MAIN-TECH-PUR-AUTHORITY-PRECISE-DESIGN-DELTA-ADOPTION-REVIEW.txt:73) 技术 assignment | IPQC 没有 productRef 时读真实 WO/operation/output assignment；baseline OR 与 ER 查询分开；原用途、单位、序列号/范围与转换必须有权映射。 | 由技术角色自签 MES 产品；同 EA 字符串等于同单位；UNKNOWN 默认已知。 |
| [M04](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/70/706343f425114b3c__QA-NCR-R12-MAIN-TECH-PUR-AUTHORITY-PRECISE-DESIGN-DELTA-ADOPTION-REVIEW.txt:85) PLM-FBK | H47 精确 draft/key/localVersion/submissionDigest；FeedbackVersion 独立；MERGED 保 original 与 mergedInto，rank56 原 feedback current/final。 | 反馈 ACCEPTED 等于 NCR 关闭或变更批准。 |
| [M05](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/70/706343f425114b3c__QA-NCR-R12-MAIN-TECH-PUR-AUTHORITY-PRECISE-DESIGN-DELTA-ADOPTION-REVIEW.txt:96) PLM-CHG | 完整 ImpactSnapshot/watermark；每批准 item 的 target/range/window/action/command/receipt/verification；3/5 成功保存 3 个真实结果，其余继续待决。 | 用成功 subset 代表全集 APPLIED；撤销后抹掉旧实施事实。 |
| [M06](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/70/706343f425114b3c__QA-NCR-R12-MAIN-TECH-PUR-AUTHORITY-PRECISE-DESIGN-DELTA-ADOPTION-REVIEW.txt:107) PLM-DEV | 有限技术 grant 与独立 Quality 双权；完整 UseBindingIdentity；原 U/H、Meter/Profile/Unit/MeasurementPoint/Actual/暴露/未知上界；终态核全部相交后代与 noFurtherExecution。 | 预留即实际；报废、撤回、过期、超时或回滚自动返额。安全释放仍由原 DEV 证据决定。 |
| [M07](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/70/706343f425114b3c__QA-NCR-R12-MAIN-TECH-PUR-AUTHORITY-PRECISE-DESIGN-DELTA-ADOPTION-REVIEW.txt:118) PLM-VER | requirement/change/impactItem/implementation/执行 Owner/target/baseline/full range/purpose/current/authority/原提交完整链；PASSED 必须完整覆盖。 | 实施 APPLIED 或收到消息便是验证 PASS；替 Lab/MES/QA 签事实。 |
| [M08](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/70/706343f425114b3c__QA-NCR-R12-MAIN-TECH-PUR-AUTHORITY-PRECISE-DESIGN-DELTA-ADOPTION-REVIEW.txt:129) PUR-GR | 商业 receipt/domain/ScopeSet、VOID 空洞、四质量分区、AcceptanceApplication 完整 APPLIED/current/projectedScopeSet；I29/IQC source 与原接缝保留。 | QA 通知等于 GR 应用、实收或库存可用。GR 枚举不能扩到 RETURN/PR。 |
| [M09](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/70/706343f425114b3c__QA-NCR-R12-MAIN-TECH-PUR-AUTHORITY-PRECISE-DESIGN-DELTA-ADOPTION-REVIEW.txt:140) PUR-RETURN | Case/Line 永久 root/domain/PO portion/单位/坐标、原 SourceBasis/permit/supplier/Part/Work/receipt/current，以及 cause/version 完整协调集。 | NCR ReturnBasis 是商业退货许可、Stock OUT 或财务调整；UNKNOWN 可以换 key 再退。 |
| [M10](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/70/706343f425114b3c__QA-NCR-R12-MAIN-TECH-PUR-AUTHORITY-PRECISE-DESIGN-DELTA-ADOPTION-REVIEW.txt:151) PUR-PR | 永久 sourceRoot/allocationGeneration/grant/active leaf 守恒 `A=F+P+E+U+R+B`；OA 决定与 PR 应用 receipt 分开；PO execution/逐 grant SourceUse/current 可恢复。 | NCR 批准产生 PR/OA/PO，或返 E/释放 U/P；估价是成交价。 |
| [M11](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/70/706343f425114b3c__QA-NCR-R12-MAIN-TECH-PUR-AUTHORITY-PRECISE-DESIGN-DELTA-ADOPTION-REVIEW.txt:162) Registry/IAM/proof2 | 实际发行者、NameBinding、source SHA/profile/contract/service/current/撤销/IAM 与数据库全 writer 拓扑登记；proof2 精确域编码，旧 proof1/result:v1 保原。 | 当前设计记录可自签 QUALIFIED/ACTIVE、创建访问权或证明部署。 |

M01/M02 对应当前报告原行 47–72；M03–M11 对应 73–173。机器账保存每个完整 Record ID、角色、时间及原段落，避免简称丢失采用身份。

## 3. QA/MES：十项

| 项 | 采用的专业责任 | 实现中的边界 |
| --- | --- | --- |
| [Q01](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/5a/5a52092d2e699241__QA-MES-R12-PRECISE-OWNER-DELTA-ADOPTION-RECORDS.json:259) IQC S03 | 原 R5 raw/Decision/TaskBasis/Family、I29、新 NCR_INITIAL_RECORD 同事务适配、永久 root 后继。 | 原 CurrentGuardInput 七种 action 不改；未决质量可建立 NCR，不拿普通库存 PASS 门替代。 |
| [Q02](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/5a/5a52092d2e699241__QA-MES-R12-PRECISE-OWNER-DELTA-ADOPTION-RECORDS.json:312) IPQC 完整来源 | 无损 source capture、完整 Resolution27、实际 Task PlanInstance、同登记 Family/目录/cut、当前原件与依赖关闭尾链。 | projection 不是原 Owner wire；I02/I04/I06 只读，I03 resolve 不能当原件读取。 |
| [Q03](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/5a/5a52092d2e699241__QA-MES-R12-PRECISE-OWNER-DELTA-ADOPTION-RECORDS.json:382) IPQC G2 重检与协调 | 新 FinalMainNcrCloseReinspection 与完整 guard/Use；FULL_COUNT、真实新 cycle/Task、全部测量复核、原 Decision/current 与 G1 完成。 | 不扩旧 EffectKey；旧 98 payload 不变；不代其他 15 类义务。 |
| [Q04](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/5a/5a52092d2e699241__QA-MES-R12-PRECISE-OWNER-DELTA-ADOPTION-RECORDS.json:437) QAP 原策略 | 完整 27 成员原 ResolutionEvidence 与 context body/raw；resolution/family/plan 六 native proof 的准确原体身份，实际 Task/同族关系。 | 原策略不是 Main 或 Finance 摘要域；不重签原策略、Main 或 Registry。 |
| [Q05](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/5a/5a52092d2e699241__QA-MES-R12-PRECISE-OWNER-DELTA-ADOPTION-RECORDS.json:506) QAP current/范围/单位 | source/domain/mapping/alias/Family/catalog/policy/plan/ground/unit/authority 全 writer 当前核验。 | 原 EffectLocator 三枚举不扩；范围量不当 Meter/DEV 暴露。 |
| [Q06](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/5a/5a52092d2e699241__QA-MES-R12-PRECISE-OWNER-DELTA-ADOPTION-RECORDS.json:560) MES-WO G2 | 五 Expected、真实 WO scope、资源版本、稳定 Work/cycle/occurrence、FINISHED 与注册/验证/current 完整关系。 | COMMITTED/MAY_EXECUTE 只有准入含义；相同数量不能替换 occurrence。 |
| [Q07](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/5a/5a52092d2e699241__QA-MES-R12-PRECISE-OWNER-DELTA-ADOPTION-RECORDS.json:619) MES-EXEC | 自己的 product assignment、actual baseline/purpose/site/unit/坐标、测量、专业 APPLIED/原提交/current/final。 | Root/GoodObserved/NCR 范围不等于 Actual 或 DEV U/H；新消息 key 不能导致重复发生。 |
| [Q08](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/5a/5a52092d2e699241__QA-MES-R12-PRECISE-OWNER-DELTA-ADOPTION-RECORDS.json:676) MES-OUTPUT | outputRef 原产品、actual baseline/purpose/unit、output 与 receipt 双坐标、完整 receiptSubject、自身投影与 current/final。 | Stock 应用不代表 Output 已应用；不签 WMS receipt/movement/availability。 |
| [Q09](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/5a/5a52092d2e699241__QA-MES-R12-PRECISE-OWNER-DELTA-ADOPTION-RECORDS.json:731) 实际 MES 非库存对象 Owner | 只有原 requirement 指定 MES 时，采用独立 RETIRE_OBJECT/RETURN_OBJECT 请求、操作、结果、对象转移、原 commit 和执行段。 | 完整 APPLIED、无重叠且 remaining 空才满足；UNKNOWN/PARTIAL/未知合同不能 NA。非库存对象不默认全归 MES。 |
| [Q10](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/5a/5a52092d2e699241__QA-MES-R12-PRECISE-OWNER-DELTA-ADOPTION-RECORDS.json:793) QA/MES H30 来源计量 | 一次真实 source/decision partition 对应完整 selector；真实 continuation、partition/unit 一对一证明，split/merge 分成新完整出口。 | 不猜 tuple；旧 Use 永远引用原 publication；不签 Finance、factory 或 proof2 发行权。 |

Q02/Q04 是本轮关闭历史完整依赖 RETURN 的两条，其余八条按准确未变子域重新记录。普通两代 H30 的实际示例来源为 WMS_RMA，不能拿它证明 IPQC 来源已有真实运行资格。

## 4. Finance/WMS：五项

| 项 | 采用的专业责任 | 保留的硬边界 |
| --- | --- | --- |
| [F01](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/b5/b5c7cf18171b011d__QA-NCR-R12-FINANCE-WMS-PRECISE-DESIGN-DELTA-ADOPTIONS-20261004.json:60) FIN-INV | 直接消费准确 R10 H30 原 schema、六查询状态和 AcceptedTypedResult:v1。首次 factory 形成完整持久 tuple，generation 对同 snapshot 收敛；current 变化生成新 tuple，旧 replay 保原。FINANCE_VALUE 需要原 ValueEffectResult、CommandReceipt、COMMITTED C09 与全范围/政策/期间/池 current。 | 新 generation 不再次计价；NCR 不算价值、不填零、不发 credit/AR。RETURN_TO_SOURCE 仍 PENDING/H30_QA_RETURN_SEMANTIC_UNMAPPED，不强映 HOLD。 |
| [F02](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/b5/b5c7cf18171b011d__QA-NCR-R12-FINANCE-WMS-PRECISE-DESIGN-DELTA-ADOPTIONS-20261004.json:662) FIN-COST | Internal COST-RESULT-1 与独立 OUT.CostResult/C41 保原；SETTLED 要 missingKeys 空、全部腿/GL/current/reconciliation。合法零须 ZERO_EVIDENCED 与独立 zero reconciliation。 | Completion ACK/一条腿不等于 POSTED_COMPLETE；无成本或分母不能填零；NCR close 不代表财务结转。 |
| [F03](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/b5/b5c7cf18171b011d__QA-NCR-R12-FINANCE-WMS-PRECISE-DESIGN-DELTA-ADOPTIONS-20261004.json:955) WMS-RMA | 新 ReadNcrInitialPhysicalSourceFinal 只读原 POSTED 实收、ReturnShare/Claim/IN/Stock/correction；永久 root 由 RMA+share+receipt operation+line 组成，版本另存；原完整范围/映射 current。 | 不借 Finance 专属 H30 consumer；原 SPEC05 五终态继续 RequiredFenced，effectOwner/ownerContractRef 仍空。 |
| [F04](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/b5/b5c7cf18171b011d__QA-NCR-R12-FINANCE-WMS-PRECISE-DESIGN-DELTA-ADOPTIONS-20261004.json:1378) WMS-STOCK/IN | 原 Root/Span/Place/Owner/技术/用途/单位/Receipt/Movement/SourceManifest 原件与 WMS QualityProjection 专业 APPLIED/current/final；原 Int64 canonical string 上界保留。 | APPLIED_WITH_NONCURRENT_SUBJECTS 不制造当前可用量；质量应用物理 delta=0；INBOX RECEIVED 不是专业 APPLIED；IN 不升为 MOVE/ADJUST/SHIP 终态 Owner。 |
| [F05](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/b5/b5c7cf18171b011d__QA-NCR-R12-FINANCE-WMS-PRECISE-DESIGN-DELTA-ADOPTIONS-20261004.json:1849) 新 WMS-DISP 接口 | 明确是新 candidate 的库存处置 request/result 与 current/final；完整原原因、范围、expected head、权限及真实 movement/transaction/执行段。非库存分支只有原 requirement 确认 WMS 负责其对象才适用。 | 无已证明 MD02 accepted CURRENT；不覆盖本例 MES 对象，不伪造真实 APPLIED；不因采用接口就产生待自动执行的 durable command。 |

## 5. 编码时必须保留的共同关系

1. 永久来源 root 使用 `QA-NCR:INITIAL-SOURCE:6`；请求、版本、profile、范围或用途变化不能绕成第二张 NCR。后继按原 continuity/reassessment，已关闭记录走独立 post-close reconciliation。
2. 来源必须保存原 API/standalone/Task/Plan/Family/目录/current cut/策略的完整 body、raw、profile、ref 与提交证据。OUTPUT 的 output `100..120` 与 root/receipt `500..520` 是两个坐标，不因长度同为 20 就互换。原 27 字段不得裁成旧 18 字段。
3. close 的 15 类集合由服务端全枚举；SATISFIED 核完整范围、原结果、审批、当前状态。有权 NA 需要自己的政策/来源依据，缺失、UNKNOWN、未观察、未完成、未知接口均不能替代 NA。RMA/Stock 与 EXEC/NON_STOCK 两个条件普通世界分别解释，不能合成同库两个 Head。
4. actual source/assignment/unit/technical、QA Family/策略/测量/审批、MES occurrence/Registry/Actual、专业投影、对象处置、协调、NCR 与注册/权限全部相关 writer 按规定顺序在同一个真实 SERIALIZABLE ambient UoW 锁到 commit。rank52 专业对象/投影、rank54 Finance、rank55 RMA、rank56 技术/反馈/验证、rank58 协调、rank60 NCR 的具体接口按主合同执行，不能在后级临时补早级锁。远程 HTTP CURRENT 不提供最终许可。
5. proof2 的两个域分别为 `FIN-INV:H30:QaQualificationProof:2` 与 `FIN-INV:H30:QaOwnerRegistrationProof:2`，计算是 UTF8 域＋单 NUL＋完整 payload 的 JCS，仅去该 proof 自身 digest。nested ref/state/locator/time/null 保留。内部 QA-NCR 与外部 QA-NCR-01 用原 NameBinding 明确映射，不能尝试多个 hash 域直到通过。
6. scope 数量、物理 occurrence、Meter 暴露、DEV U/H 与技术用途分别保量义；UNKNOWN 上界不填 0。报废/撤回/超时/过期/NOT_OBSERVED/本地回滚不构成返额依据。终态只能由原专业 Owner 的完整后代与不再执行证明决定。

## 6. 本附册的完成范围

本次完整审读 10 份采用原件/报告的业务内容，包括当前 26 项的理由、定义选择、继承关系、排除项和限制；对照全部 23 页结构投影及大报告拆分页，原 JSON/段落与源文件重读相等、源 SHA 独立记录。这里只为不透明 UUID/SHA 使用类型化阅读标记，准确字面身份仍保存在原件与机器账，不能用投影标记判断两个原件相等。

原报告中的 checker 结果保留为历史声明，没有执行候选脚本、SQL、编译器、业务 fixture、数据库或 Actions。运行注册、实际权限/发行者、native codec、全 writer 同库事务拓扑仍 UNPROVEN，enabled=false，业务 AC NOT_RUN。旧编号 REQ/PLM-AC12/17 逐字母版、CP8/dispatchCP6 部分字节未由本次补读恢复；历史缺件与当前可用设计采用分别列示。当前 26 记录也不自动代替全六 SPEC 的正式根接受记录，正式状态由主册精确治理入口说明。
