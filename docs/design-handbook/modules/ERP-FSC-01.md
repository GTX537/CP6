# ERP-FSC-01 FSC 清单与行业证明

## 1. 目的、操作者与职责

Main ERP 根据准确报价／估算关系和已核模板，签发有来源的行业检查清单并保留原成品下载。查询员读范围内来源，签发员以 issue＋完整明细／金额读权发起，格式维护与业务签发分责，下载员每次核当前文档／来源／文件披露。系统不替外部机构作 FSC 认证、工程 PASS、客户接受或法律合规结论。 [原文 L3](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/e2/e2674e740f069490__ERP-FSC-01_完整四SPEC兼容开发设计_R2.md:3>)、[原文 L20](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/e2/e2674e740f069490__ERP-FSC-01_完整四SPEC兼容开发设计_R2.md:20>)、[原文 L22](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/e2/e2674e740f069490__ERP-FSC-01_完整四SPEC兼容开发设计_R2.md:22>)

覆盖清单格式、签发、下载、来源业务引用四 SPEC。QTN 拥有报价事实／关系／状态，EST 拥有 Version 与 Control，PLM 拥有技术用途；FSC 仅保存本次事实快照与产物。SYS-NUM／FILE 提供技术能力，不宣告业务有效，当前范围不增加 WMS 库存／MES 实物认证链。 [原文 L22](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/e2/e2674e740f069490__ERP-FSC-01_完整四SPEC兼容开发设计_R2.md:22>)、[原文 L93](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/e2/e2674e740f069490__ERP-FSC-01_完整四SPEC兼容开发设计_R2.md:93>)

## 2. 当前设计、组合与接受范围

当前采用 X1 R2 Markdown 静态详设，Stage 100。原四条 SPEC 完整保留；作者包内 STOPPED／PENDING、R1 RETURN 都是冻结历史，不覆盖后继 CURRENT。用户授权 root／COORD 于 2026-10-07T16:55:28.910726Z 接受，UserPersonallySigned=false；重建 UA 保存既有决定，不声称是丢失临时 UA 的同字节副本。 [原文 L4](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/af/afd1d67732952bff__CURRENT.json:4>)、[原文 L7](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/af/afd1d67732952bff__CURRENT.json:7>)

| 原件 | SHA-256 | 作用 | 入口 |
| --- | --- | --- | --- |
| 本目标正文 | e2674e740f069490df79b23b90f492122911e2d3ede434cd49d9240444e0940b | 四 SPEC 具体规范 | [原文 L1](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/e2/e2674e740f069490__ERP-FSC-01_完整四SPEC兼容开发设计_R2.md:1>) |
| X1 R2 独审 | 918bbd4508551b64470398bf65697954bff495fd37d1ef8cb5efa66e56bd0462 | 四根 STATIC PASS，无剩余 RETURN；生产采用不在结论内 | [原文 L1](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/91/918bbd4508551b64__00_X1_R2_独立回归与完整范围静态审阅报告.md:1>) |
| 当前接受 UA | 42581c0a33c3229b4a25dbdefd43e241c72511d47b2161605048358fe7459a1a | 本目标四 SPEC 静态接受 | [原文 L1](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/42/42581c0a33c3229b__UA-20261008-X1-ERP-FSC-R2.json:1>) |
| CURRENT | afd1d67732952bffa2d33e7c9ad21fb61b2407221e3d980fc660fc180682d6b8 | 正文／独审／UA 准确选择 | [原文 L1](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/af/afd1d67732952bff__CURRENT.json:1>) |
| 本域摘要 | 7fe872fc8d85d676a13d314bf9679029c34bdb8942ce6ed10228a9d7743e9280 | 强制新本域序列化，不替外 Owner digest | [原文 L1](<D:/CP6-archives/consolidation-20261010/commercial-cache/x1/X1-R2/R2_LOCAL_DIGEST_RULES.md:1>) |
| 源与影响 | 0afa8b4b022cf9efbbf304b8b71b4051a8a330b565391568344cae229139b47f | 固定源码窗口及新旧接口差距 | [原文 L1](<D:/CP6-archives/consolidation-20261010/commercial-cache/x1/X1-R2/SOURCE-AND-IMPACT.md:1>) |
| 任务拆分 | 7c31188c14e093cbc8a0f4a78643536b04d7dc945c919541f624aae722c01e10 | PRICE 9／FSC 7 项，PLANNED_NOT_AUTHORIZED | [原文 L1](<D:/CP6-archives/consolidation-20261010/commercial-cache/x1/X1-R2/DEVELOPMENT-TASKS.json:1>) |

X1 全组三目标 14 SPEC、68 AC、21 任务；本商业两册分别 28／24 AC，全部 NOT_RUN。ENG 的六 SPEC 属原接受复用下有界兼容补充，本册不替其扩张接受。真实 Profile、模板、IAM、文件、事务、旧 writer 收敛及 Owner 采用仍 UNPROVEN；旧四根已静态关闭，不能把真实接入门再报告成作者尚未选择设计方案。 [原文 L11](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/91/918bbd4508551b64__00_X1_R2_独立回归与完整范围静态审阅报告.md:11>)、[原文 L12](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/91/918bbd4508551b64__00_X1_R2_独立回归与完整范围静态审阅报告.md:12>)、[原文 L13](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/91/918bbd4508551b64__00_X1_R2_独立回归与完整范围静态审阅报告.md:13>)、[原文 L15](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/91/918bbd4508551b64__00_X1_R2_独立回归与完整范围静态审阅报告.md:15>)、[原文 L133](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/91/918bbd4508551b64__00_X1_R2_独立回归与完整范围静态审阅报告.md:133>)、[原文 L158](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/91/918bbd4508551b64__00_X1_R2_独立回归与完整范围静态审阅报告.md:158>)、[原文 L164](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/91/918bbd4508551b64__00_X1_R2_独立回归与完整范围静态审阅报告.md:164>)

## 3. 数据对象、版本、字段和身份

| 旧字段组 | 准确来源和规则 | 出处 |
| --- | --- | --- |
| BaseCd、StaffCd、CustomerCd、ProjectNo | Base必填且权限覆盖。代码和展示名称分别读当前授权来源；ProjectNo精确过滤或明确不支持，不再显示假生效筛选 | [原文 L32](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/e2/e2674e740f069490__ERP-FSC-01_完整四SPEC兼容开发设计_R2.md:32>) |
| IssueDateFrom/To | 旧服务实际筛的是Quotation.QtnIssueDate，兼容DTO保原名，UI文案明确“报价签发日期”；新DTO命名quotationIssueDateFrom/To，不冒FSC发行日 | [原文 L33](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/e2/e2674e740f069490__ERP-FSC-01_完整四SPEC兼容开发设计_R2.md:33>) |
| QtnNoFrom/To | 保原比较语义并验证from≤to；新过滤支持准确quote ref，列表只显示当前可见范围 | [原文 L34](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/e2/e2674e740f069490__ERP-FSC-01_完整四SPEC兼容开发设计_R2.md:34>) |
| IncludeUnissued/IncludeIssued | 至少一项；未发/已发按本域明确状态，legacy仅管理号存在时标“旧记录已编号，成品未核”，不当新ISSUED | [原文 L35](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/e2/e2674e740f069490__ERP-FSC-01_完整四SPEC兼容开发设计_R2.md:35>) |
| Page/PageSize/MaxRows | page≥1、pageSize1..200；来源排序稳定加准确业务键；不接受无限行导出代替范围授权 | [原文 L36](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/e2/e2674e740f069490__ERP-FSC-01_完整四SPEC兼容开发设计_R2.md:36>) |
| QtnNo/QtnCalcNo | 保原字符串native，不等准确QTN/EST版本；新引用另存Ref，不把旧号后缀猜成Version | [原文 L37](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/e2/e2674e740f069490__ERP-FSC-01_完整四SPEC兼容开发设计_R2.md:37>) |
| CustomerItemName1/2、Quantity、UnitPrice、Amount | 原QuotationDetail DetailNo=1；保此原语义，新模板如需更多行须明确映射，不自行把所有报价明细合并成认证内容 | [原文 L38](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/e2/e2674e740f069490__ERP-FSC-01_完整四SPEC兼容开发设计_R2.md:38>) |
| TotalAmount、Status、FscProductDiv | 来自准确EST/QTN读取；金额不本地重算，不从Status整数推新Owner批准；1/2/3解释保原模型，未知码不能按非0自动视可签发 | [原文 L39](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/e2/e2674e740f069490__ERP-FSC-01_完整四SPEC兼容开发设计_R2.md:39>) |
| FscManagementNo、IssueDate、FormatName、ExcelFileName | 保原native；新对象将编号、签发动作、格式版本、成品内容分开 | [原文 L40](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/e2/e2674e740f069490__ERP-FSC-01_完整四SPEC兼容开发设计_R2.md:40>) |

原FscChecklist实体有FscManagementNo(max20)、QtnNo/QtnCalcNo(max20)、CustomerCd/StaffCd(max20)、BaseCd(max10)、IssueDate、FormatName(max100)、TemplatePath(max500)、ExcelFileName(max200)、FscProductDiv(max1)，继承Id/Tenant/RowVersion/IsDeleted；新逻辑不修改旧历史值以补证。 [原文 L42](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/e2/e2674e740f069490__ERP-FSC-01_完整四SPEC兼容开发设计_R2.md:42>)

FormatVersion：formatId、revision、displayName、templateFileRef、templateNativeFormat XLS/XLSX、templateBytesDigest、mappingVersion、mappingDigest、requiredSourceFields、supportedProductDivs、allowedSourceStates、outputMime、outputExtension、lifecycle DRAFT/ACTIVE/RETIRED、controlRevision、OwnerDecisionRef。原配置中的路径只作为受控legacy locator，由服务解析已登记文件，不返前端可任意改路径。真实模板映射单元格/命名区域、合并单元格、打印页、公式缓存、图片/徽标等尚未核，FormatVersion初态UNPROVEN，不填猜测单元格。 [原文 L48](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/e2/e2674e740f069490__ERP-FSC-01_完整四SPEC兼容开发设计_R2.md:48>)

FscDocumentRoot：documentId、Tenant、LegalEntityKey、BaseCd、managementNo、原legacyFscId/legacyManagementNo、当前issueRevision、currentState。管理号对同一DocumentRoot唯一；新签发修订不再在同唯一管理号下无区别重复新根。 [原文 L50](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/e2/e2674e740f069490__ERP-FSC-01_完整四SPEC兼容开发设计_R2.md:50>)

FscIssueRevision：documentId、issueRevision正整数、issueId、supersedesIssueId可空、issueReason FIRST_ISSUE/CORRECTION、来源SourceSnapshot、formatRef、artifactRef、artifactDigest/bytes/MIME、issuedAt/By、sourceGuardReceipts、contentDigest、currentControlRevision。修订内容与原成品不可变。重下载/重印使用原issueId，不生成新修订/新管理号；内容纠正生成新修订和明确reason，原件保持可追，不重新签署原来源。 [原文 L52](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/e2/e2674e740f069490__ERP-FSC-01_完整四SPEC兼容开发设计_R2.md:52>)

SourceSnapshot：legalEntity/base；guardRequirementVersion=ERPFSC-X1-R2-FG1；relationshipKind ADOPTED_ESTIMATE/UNADOPTED_LINK、既存QTN EstUseReceiptRef（仅ADOPTED非null）及真实资格policyRef；准确quoteNativeRef及其原version/control/watermark；准确estimate VersionRef及当前ControlRevision；QtnCalc关联关系的真实Owner回执；sourceDetailRef（默认原明细1）；customerBpRef及原code/name快照；staff原code/name；project ref；FscProductDiv；本次模板需要的品名、数量、单位、币种、各金额；字段来源/单位；observedAt与完整OwnerEvidence。每项原native字段/raw digest单列，canonical内容摘要是新对象，不能重命名原来源digest。 [原文 L54](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/e2/e2674e740f069490__ERP-FSC-01_完整四SPEC兼容开发设计_R2.md:54>)

IssueBatch：batchId、commandKey、Actor/T/L/Base、formatRef、orderedItems[{itemId,selectionRef,expectedSourceHeads,mode,reason}]、requestDigest、state ACCEPTED/PROCESSING/COMPLETE/COMPLETE_WITH_ERRORS、items[]。每item稳定键绑定batch+itemId，不依据行序变化重新编号。 [原文 L56](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/e2/e2674e740f069490__ERP-FSC-01_完整四SPEC兼容开发设计_R2.md:56>)

IssueItem：itemId、state PREPARING/ARTIFACT_READY/ISSUED/REJECTED/UNKNOWN、numberReservationRef、stagedArtifactRef、issueId、attemptGeneration（十进制非负整数字符串）、lastError、resumeGate（NONE/DEPENDENCY_WAIT/ORIGINAL_EFFECT_LOOKUP/NEW_SELECTION_REQUIRED）、retryable:boolean、terminal:boolean、effectKnowledge（NOT_COMMITTED/COMMITTED/UNKNOWN）、lastConfirmedCheckpoint、originalReceipt。PREPARING或ARTIFACT_READY绝不显示已签发；UNKNOWN用于恢复时尚无法确认提交结果，不推零效果。 [原文 L58](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/e2/e2674e740f069490__ERP-FSC-01_完整四SPEC兼容开发设计_R2.md:58>)

ArtifactRef由SYS-FILE真实不可变版本身份、bytes/hash/MIME及保留策略构成；保存方确认稳定可读后可成为候选。Content base64不再作为批量唯一交付方式。普通hash只是完整性摘要，不代真实性、签名或合规。 [原文 L60](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/e2/e2674e740f069490__ERP-FSC-01_完整四SPEC兼容开发设计_R2.md:60>)

新唯一域：DocumentRoot 为 (Tenant,LegalEntity,managementNo)，IssueRevision 为 (root,revision)，命令为 (Tenant,LegalEntity,Actor,key)，item 为 (batch,itemId)。FIRST_ISSUE 业务唯一域由行业 Owner 确认准确 source relation＋format 适用目的，不能只 QtnCalcNo 全局锁死合法不同清单。缺政策 ISSUE_POLICY_UNBOUND，受理前拒绝；受理后发现且权威证明无提交才终态 REJECTED。 [原文 L165](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/e2/e2674e740f069490__ERP-FSC-01_完整四SPEC兼容开发设计_R2.md:165>)、[原文 L167](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/e2/e2674e740f069490__ERP-FSC-01_完整四SPEC兼容开发设计_R2.md:167>)

## 4. 格式、来源、签发与下载主流程

### 4.1 固定格式与来源准备

格式列表只返稳定 formatId/revision、名称、支持 ProductDiv、state、outputMime 和可选择性；旧 formatName 必须唯一映射 ACTIVE 版本，重名不能取第一。原路径仅 server 受控 locator。真实 PA100／RA040 与三个 xls 模板的单元格／命名区／合并／打印／公式缓存／图片尚未核，格式激活 UNPROVEN；不猜单元格或空路径默认模板。 [原文 L48](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/e2/e2674e740f069490__ERP-FSC-01_完整四SPEC兼容开发设计_R2.md:48>)、[原文 L64](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/e2/e2674e740f069490__ERP-FSC-01_完整四SPEC兼容开发设计_R2.md:64>)、[原文 L65](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/e2/e2674e740f069490__ERP-FSC-01_完整四SPEC兼容开发设计_R2.md:65>)、[原文 L66](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/e2/e2674e740f069490__ERP-FSC-01_完整四SPEC兼容开发设计_R2.md:66>)

渲染必须写入固定 SourceSnapshot，实际文件容器／MIME／后缀一致；原 xls 只能返回真实 xls 或 Owner 批准的转换版本，缺模板不能返回文本假 Excel。徽标、声明、签名区、证书号只保真实已核内容，不自动添加认证结论。新模板生成新 FormatVersion，不覆盖旧 artifact。 [原文 L67](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/e2/e2674e740f069490__ERP-FSC-01_完整四SPEC兼容开发设计_R2.md:67>)、[原文 L68](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/e2/e2674e740f069490__ERP-FSC-01_完整四SPEC兼容开发设计_R2.md:68>)、[原文 L69](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/e2/e2674e740f069490__ERP-FSC-01_完整四SPEC兼容开发设计_R2.md:69>)

沿 Base→QTN→QtnCalc→EST→Detail1 读取，精确版本和关系另存，旧 QtnNo/QtnCalcNo 不等 Version。默认明细仅原 DetailNo=1，金额不本地重算，不扩成全部报价行汇总；客户／担当／Base／Project／金额读源 Owner，客户端显示值只能比较不能写为事实。F-I01 候选 ELIGIBLE 只是准备，不是签发许可。 [原文 L38](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/e2/e2674e740f069490__ERP-FSC-01_完整四SPEC兼容开发设计_R2.md:38>)、[原文 L39](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/e2/e2674e740f069490__ERP-FSC-01_完整四SPEC兼容开发设计_R2.md:39>)、[原文 L73](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/e2/e2674e740f069490__ERP-FSC-01_完整四SPEC兼容开发设计_R2.md:73>)、[原文 L75](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/e2/e2674e740f069490__ERP-FSC-01_完整四SPEC兼容开发设计_R2.md:75>)

### 4.2 每件原子签发

F03 202 只表持久受理：batch/item 稳定，itemId 不因行序换号；同批重复 source 完整关系的 FIRST_ISSUE 拒绝。整批可以部分结果，每件原子；同 key 同 body 得同 batch/item，各原终态不重做。 [原文 L56](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/e2/e2674e740f069490__ERP-FSC-01_完整四SPEC兼容开发设计_R2.md:56>)、[原文 L103](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/e2/e2674e740f069490__ERP-FSC-01_完整四SPEC兼容开发设计_R2.md:103>)、[原文 L110](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/e2/e2674e740f069490__ERP-FSC-01_完整四SPEC兼容开发设计_R2.md:110>)

先核 Actor/scope/issue/read、原意图及纠正归属，固定格式、完整快照与来源证据。FIRST_ISSUE 需要管理号时用稳定 itemKey 向 SYS-NUM 预留并保存 reservation；CORRECTION 用原 DocumentRoot managementNo，跳新根编号。号码间隙如实保留，失败号不转给别件。 [原文 L113](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/e2/e2674e740f069490__ERP-FSC-01_完整四SPEC兼容开发设计_R2.md:113>)、[原文 L114](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/e2/e2674e740f069490__ERP-FSC-01_完整四SPEC兼容开发设计_R2.md:114>)、[原文 L115](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/e2/e2674e740f069490__ERP-FSC-01_完整四SPEC兼容开发设计_R2.md:115>)

固定快照＋模板渲染后，受控不可变暂存文件取得真实可读／bytes/hash/MIME 回执，才 ARTIFACT_READY，仍未 ISSUED。随后最终 Main 事务重核 Actor、原 request/item、source heads、format control、原 root/current revision 和两个 F-I02 守卫；持有保护至 commit。渲染期间已证源／格式改变且无最终提交，则 REJECTED＋NEEDS_NEW_SELECTION。 [原文 L116](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/e2/e2674e740f069490__ERP-FSC-01_完整四SPEC兼容开发设计_R2.md:116>)、[原文 L117](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/e2/e2674e740f069490__ERP-FSC-01_完整四SPEC兼容开发设计_R2.md:117>)

事务同写不可变 FscIssueRevision、root 当前修订、真实 artifact 绑定、两个 FactGuardResult 原证据、本域 History、Command/item 终态与稳定旧号投影 outbox。不可直改 EST Version/Control 或冒 Usage。ISSUED 与旧 EstimateCalc/QuotationCalc 管理号投影分层：投影由各原 Owner 受理，NOT_REQUIRED/PENDING/ACKED/REJECTED 单列；pending/rejected 不逆改合法签发，也不能声称旧 Owner 已应用。 [原文 L118](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/e2/e2674e740f069490__ERP-FSC-01_完整四SPEC兼容开发设计_R2.md:118>)、[原文 L119](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/e2/e2674e740f069490__ERP-FSC-01_完整四SPEC兼容开发设计_R2.md:119>)

纠正明确 CORRECTION＋priorIssueRef/current expected revision，保 supersedes/reason 与新准确快照；原管理号 root 不变。重印／下载丢失只读原 issue，不新增修订／管理号。暂存后拒绝留下 ORPHAN_STAGED，清理由文件政策管理，不删除被引用文件。 [原文 L122](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/e2/e2674e740f069490__ERP-FSC-01_完整四SPEC兼容开发设计_R2.md:122>)、[原文 L124](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/e2/e2674e740f069490__ERP-FSC-01_完整四SPEC兼容开发设计_R2.md:124>)

### 4.3 原成品下载

F07 读取准确 issueRevision.artifactRef 的固定原 bytes，核当前文档、来源、金额与文件披露。hash/bytes 不符 ARTIFACT_INTEGRITY_UNPROVEN；缺件 ARTIFACT_NOT_AVAILABLE，保历史元数据和原因，不能用当前模板再生冒原签发件。旧记录无固定文件／版本标 LEGACY_ARTIFACT_UNPROVEN；如另授权辅助重建，必须新文件明示非原签发件，不覆盖历史。 [原文 L150](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/e2/e2674e740f069490__ERP-FSC-01_完整四SPEC兼容开发设计_R2.md:150>)、[原文 L152](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/e2/e2674e740f069490__ERP-FSC-01_完整四SPEC兼容开发设计_R2.md:152>)

## 5. API、双事实守卫与外部合同

候选前缀 /api/main/v1/fsc-checklists，旧 /api/fsc-checklists 保兼容；没有声称新路由已部署。 [原文 L97](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/e2/e2674e740f069490__ERP-FSC-01_完整四SPEC兼容开发设计_R2.md:97>)

| 接口 | 封闭输入 | 输出／权限 | 出处 |
| --- | --- | --- | --- |
| F01 GET /formats | legalEntityKey、baseCd | FormatSummary[]、asOf；当前读权 | [原文 L101](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/e2/e2674e740f069490__ERP-FSC-01_完整四SPEC兼容开发设计_R2.md:101>) |
| F02 POST /sources/query | legalEntityKey/baseCd、staff/customer/project、日期与号码范围、状态、page/pageSize、formatRef | SourceQueryResult；完整字段权不足只能安全投影 | [原文 L102](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/e2/e2674e740f069490__ERP-FSC-01_完整四SPEC兼容开发设计_R2.md:102>) |
| F03 POST /issue-batches | legalEntityKey/baseCd、formatRef、items[{itemId,selectionId,selectionDigest,expectedSourceHeads,mode FIRST_ISSUE/CORRECTION,priorIssueRef或null,reason}]；Idempotency-Key | 202 BatchReceipt只表已受理，不能用“已签发N份” | [原文 L103](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/e2/e2674e740f069490__ERP-FSC-01_完整四SPEC兼容开发设计_R2.md:103>) |
| F04 GET /issue-batches/{batchId} | 真实batchId | 每item当前状态、原receipt、artifact元数据及错误 | [原文 L104](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/e2/e2674e740f069490__ERP-FSC-01_完整四SPEC兼容开发设计_R2.md:104>) |
| F05 GET /commands/{commandKey} | 原key | 原批请求/当前受理与终态的授权投影 | [原文 L105](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/e2/e2674e740f069490__ERP-FSC-01_完整四SPEC兼容开发设计_R2.md:105>) |
| F06 GET /documents/{documentId}/issues/{issueRevision} | 准确键 | 固定签发内容、来源Refs、当前控制/下载资格；不是重新渲染 | [原文 L106](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/e2/e2674e740f069490__ERP-FSC-01_完整四SPEC兼容开发设计_R2.md:106>) |
| F07 GET /documents/{documentId}/issues/{issueRevision}/download | 准确键 | 原artifact bytes/MIME/filename/hash，当前披露权 | [原文 L107](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/e2/e2674e740f069490__ERP-FSC-01_完整四SPEC兼容开发设计_R2.md:107>) |
| F08 GET /by-legacy-no/{fscNo} | 原管理号 | 准确候选列表；唯一已映射才导向原issue，多义明确选择，不取First | [原文 L108](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/e2/e2674e740f069490__ERP-FSC-01_完整四SPEC兼容开发设计_R2.md:108>) |

F-I01 SourceDirectory.Query 输入可信 Actor/Tenant、T/L/Base、筛选／分页和准确 format；输出 selectionId/digest、所需字段、quote/estimate 原 Ref、关系证据、sourceHeads、ELIGIBLE/NOT_ELIGIBLE/UNKNOWN、issues/asOf。源读取不是最终许可。 [原文 L75](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/e2/e2674e740f069490__ERP-FSC-01_完整四SPEC兼容开发设计_R2.md:75>)

F-I02 是 FSC“准确来源事实最终核验”聚合 adapter，既不是 EST I02 Binding 参与者，也不是 I03 EstimateUseParticipant。原 ReferencePurpose 仍只有 QUOTE_BASIS／PRODUCT_REFERENCE；FSC 不借用、不扩枚举、不生成 Usage，不把历史 QTN EstUseReceipt 重放作新许可。A15 准确 EST Version 只读可准备快照，但不能证明签发最终提交。 [原文 L77](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/e2/e2674e740f069490__ERP-FSC-01_完整四SPEC兼容开发设计_R2.md:77>)

唯一当前方案是新 Required 扩展 `FscSourceFactsParticipant.HoldExact(transaction, FactGuardRequest)`：EST_FACT_READ 和 QTN_RELATION_FACT 各一个真实 Owner FactGuardResult，正式 schema／绑定／采用仍 UNPROVEN。purpose=FSC_DOCUMENT_SNAPSHOT_READ 仅属于该 Required 消费请求，不写原 EstUseInput。 [原文 L79](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/e2/e2674e740f069490__ERP-FSC-01_完整四SPEC兼容开发设计_R2.md:79>)、[原文 L81](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/e2/e2674e740f069490__ERP-FSC-01_完整四SPEC兼容开发设计_R2.md:81>)

F-I02内部调用的Required接口语义为FscSourceFactsParticipant.HoldExact(transaction, FactGuardRequest)，各Owner分别返回FactGuardResult。它不作为公众HTTP批准入口。FactGuardRequest封闭字段：requirementVersion（固定ERPFSC-X1-R2-FG1）、guardRequestId（UUID）、guardRole（EST_FACT_READ/QTN_RELATION_FACT，按被调用Owner固定）、itemId、batchCommandKey、fscIntentDigest、selectionDigest、actorPolicyRef、Tenant/LegalEntityKey/BaseCd（可信上下文）、formatRef（准确id/revision/digest）、quoteRef（QTN原准确Ref）、sourceRelationRef（QTN原报价-估算/明细关系Ref）、sourceDetailRef、estimateVersion（原EST VersionRef）、expectedEstimateControlRevision（原EstRev）、expectedEstimateSourceSetDigest、capturedFactDigest、requiredFieldPaths:string[]、expectedQtnHead、purpose=FSC_DOCUMENT_SNAPSHOT_READ。这个purpose仅属本Required消费请求，不写入原EstUseInput或ReferencePurpose。requiredFieldPaths为本格式所需的完整、有序且唯一字段集合，不能用空集合换许可；所有expected值取同一原selection，不在提交时改成latest。 [原文 L81](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/e2/e2674e740f069490__ERP-FSC-01_完整四SPEC兼容开发设计_R2.md:81>)

FactGuardResult封闭为status HELD/DENIED/UNPROVEN、owner、contractBindingRef（真实Owner的合同id/revision/digest与采用记录）、guardRequestId、guardRole、requestDigest、subjectEcho（原quote/relation/detail/estimate/format及T/L/Base）、currentHeads、factDigest、allowedFieldPaths、factKnowledge KNOWN/CHANGED/REVOKED/UNKNOWN、decisionReason、nativeEvidenceRef、heldUntilTransactionEnd:boolean。HELD必须factKnowledge=KNOWN且guardRole与请求一致、subjectEcho逐字段等请求、factDigest等capturedFactDigest、allowedFieldPaths覆盖全部requiredFieldPaths、当前头满足原expected及政策，并实际持有保护至本次FSC事务结束；只有此时true。DENIED给确定不允许/主体变化，UNPROVEN不发成功proof。Proof本身按真实Owner原schema核，不以字符串存在当真实锁或采用。 [原文 L83](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/e2/e2674e740f069490__ERP-FSC-01_完整四SPEC兼容开发设计_R2.md:83>)

HELD 必须 factKnowledge=KNOWN、role 相同、subjectEcho 逐字段相同、factDigest 等 captured、allowedFieldPaths 覆盖全部非空 requiredFieldPaths、当前头满足原 expected 与政策，并真实 heldUntilTransactionEnd。只有字符串 HELD 或 Ref 存在不够；缺正式绑定、精确 schema、资格政策或共同保护就 OWNER_PARTICIPANT_UNPROVEN，非终态等待、零 ISSUED。 [原文 L83](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/e2/e2674e740f069490__ERP-FSC-01_完整四SPEC兼容开发设计_R2.md:83>)、[原文 L89](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/e2/e2674e740f069490__ERP-FSC-01_完整四SPEC兼容开发设计_R2.md:89>)

EST 守卫负责准确 Version 内容/币种/UOM/字段、FSC 事实披露实际政策、源集合当前性、Actor/字段及实际保护；是否需要 EST 自己上游来源参加由其新正式合同保证，FSC 不直接调用内部 I02。QTN 守卫负责准确版本/业务状态、Base/客户/明细与 estimate 关系、模板用途及控制。ADOPTED_ESTIMATE 保真实既存 EstUseReceipt 作历史关系；UNADOPTED_LINK 可为 null，但两类都必须两个新守卫，资格由真实行业/QTN 政策裁决。 [原文 L85](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/e2/e2674e740f069490__ERP-FSC-01_完整四SPEC兼容开发设计_R2.md:85>)、[原文 L87](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/e2/e2674e740f069490__ERP-FSC-01_完整四SPEC兼容开发设计_R2.md:87>)

F-I03 ResumeItem(itemId,expectedAttemptGeneration) 仅受信恢复既有意图，无新业务 body；先查终态／资源／事务结果，再核原 Actor 当前权限、固定 source/format/合同需求与原 digest。后台 service 身份不绕过原人权限；未知权限等依赖，已证撤权且未提交才拒绝。 [原文 L146](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/e2/e2674e740f069490__ERP-FSC-01_完整四SPEC兼容开发设计_R2.md:146>)

## 6. 状态、并发、唯一恢复矩阵与摘要

item 终态仅 ISSUED／REJECTED。NEEDS_NEW_SELECTION 是 lastError.code，不是第三状态；REJECTED 不原地复活，补数据／换来源要新 selection＋新 key 并追 priorRejectedItemRef。依赖等待可以原请求同 item 续办，不能把这些分支任选。 [原文 L128](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/e2/e2674e740f069490__ERP-FSC-01_完整四SPEC兼容开发设计_R2.md:128>)

| 确定观察／错误 | 唯一状态／效果知识 | 原 key／item 恢复 | 新 selection／key | 出处 |
| --- | --- | --- | --- | --- |
| F03请求语法/必填/重复item错误，尚未受理 | 不建batch/item，HTTP400；零本域签发效果 | 原请求未受理，可修正后以新key提交；不制造已存在receipt | 修正请求新key，若只是selection输入笔误且原选择仍有效可复用原准确selection | [原文 L132](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/e2/e2674e740f069490__ERP-FSC-01_完整四SPEC兼容开发设计_R2.md:132>) |
| 已受理后明确来源不存在/关系错误/格式不支持或必需业务数据确实缺失；SOURCE_NOT_FOUND、SOURCE_RELATION_INVALID、FORMAT_NOT_ELIGIBLE、FORMAT_UNPROVEN（已证所选格式缺已核映射）、ISSUE_POLICY_UNBOUND（已证清单用途政策未配置）、REQUIRED_FIELD_UNKNOWN | REJECTED，terminal=true，NOT_COMMITTED；只有取得权威未提交结论才允许写此终态 | F03同key仅返回原拒绝；F04/F05查询原结果，不后台重新签发 | 补真实业务资料/改格式/选关系后必须新selection及新key | [原文 L133](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/e2/e2674e740f069490__ERP-FSC-01_完整四SPEC兼容开发设计_R2.md:133>) |
| 仅读取不到当前依赖：超时、服务离线、明确版本原bytes暂不可读、SOURCE_READ_UNAVAILABLE、FORMAT_READ_UNAVAILABLE、FILE_STORAGE_UNAVAILABLE、NUMBER_SERVICE_UNAVAILABLE | 若尚无已核成品为PREPARING；已有原成品为ARTIFACT_READY。terminal=false，NOT_COMMITTED，resumeGate=DEPENDENCY_WAIT | 服务恢复worker可原item续办同意图；每次重核原Actor当前权、原selection/source/format头；F03重放只查同batch，不另建worker意图 | 不换selection；若随后确证业务源/格式已变，转下一行REJECTED | [原文 L134](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/e2/e2674e740f069490__ERP-FSC-01_完整四SPEC兼容开发设计_R2.md:134>) |
| OWNER_PARTICIPANT_UNPROVEN（正式FSC事实读扩展/保护尚未绑定或暂不可用），准备事实本身可精确定位 | PREPARING或ARTIFACT_READY按同一成品条件，terminal=false、NOT_COMMITTED、DEPENDENCY_WAIT；绝不ISSUED | 绑定真实合同后恢复同item，必须满足原ERPFSC-X1-R2-FG1需求及当前资格；不是直接转成功；缺证持续等候并展示阻塞 | 若新合同不满足原需求或必须更换主体/业务数据，则明确NEEDS_NEW_SELECTION终态拒绝，不改旧请求 | [原文 L135](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/e2/e2674e740f069490__ERP-FSC-01_完整四SPEC兼容开发设计_R2.md:135>) |
| 已证selection过期、SOURCE_CHANGED/FORMAT_CHANGED、事实守卫DENIED、CORRECTION_HEAD_CHANGED、当前Actor确定无签发/必需读权 | REJECTED、terminal=true、NOT_COMMITTED；lastError=NEEDS_NEW_SELECTION或PERMISSION_REVOKED；resumeGate=NEW_SELECTION_REQUIRED | 只查询原拒绝。后续权限/来源恢复不复活旧终态 | 重新选择并确认的新key；原成功item不得并入新批默认重发 | [原文 L136](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/e2/e2674e740f069490__ERP-FSC-01_完整四SPEC兼容开发设计_R2.md:136>) |
| 文件/号码操作结果未知，但最终签发事务尚未开始 | PREPARING或ARTIFACT_READY按已确认checkpoint，terminal=false，NOT_COMMITTED；resumeGate=ORIGINAL_EFFECT_LOOKUP针对该技术资源操作 | 按固定numberReservationKey/artifactOperationKey回读原结果；没有原操作无效果证明不得新资源身份重做；明确不存在且无效果才重发同操作键 | 不需新业务selection，恢复后仍核当前头；不会因资源超时换管理号 | [原文 L137](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/e2/e2674e740f069490__ERP-FSC-01_完整四SPEC兼容开发设计_R2.md:137>) |
| 最终签发commit响应丢失/事务结果不能确认，COMMIT_UNKNOWN | UNKNOWN、terminal=false、effectKnowledge=UNKNOWN、ORIGINAL_EFFECT_LOOKUP | 仅查原事务/command/item的权威结果及issue唯一键。依赖后来变绿不允许重签。查到已提交恢复同ISSUED/原receipt；查到原拒绝回同REJECTED | 不得新key补交同一未知意图；先完成原效果裁定 | [原文 L138](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/e2/e2674e740f069490__ERP-FSC-01_完整四SPEC兼容开发设计_R2.md:138>) |
| UNKNOWN获得权威“该次事务已回滚且没有签发效果”的明确结论，包含原transaction/attempt身份 | 回lastConfirmedCheckpoint：有原稳定成品ARTIFACT_READY，否则PREPARING；terminal=false、NOT_COMMITTED | 保原号/原artifact/原item，attemptGeneration递增，再完整当前核；普通查无行、超时或锁不可得不等回滚证据 | 业务主体不变可同key续办；当前主体变化按已证变更行拒绝 | [原文 L139](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/e2/e2674e740f069490__ERP-FSC-01_完整四SPEC兼容开发设计_R2.md:139>) |
| 原ISSUED的下载权限/文件读取失败 | item保持ISSUED、terminal=true、COMMITTED；只更新下载读取错误，不改原receipt | F06/F07当前权限下查询/下载；缺原文件待恢复原bytes，不重新签发 | 不创建新签发；真内容纠正走明确CORRECTION新key | [原文 L140](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/e2/e2674e740f069490__ERP-FSC-01_完整四SPEC兼容开发设计_R2.md:140>) |

batch 汇总唯一规则：受理未开始 ACCEPTED；至少一 PREPARING/ARTIFACT_READY/UNKNOWN 则 PROCESSING；全 ISSUED 为 COMPLETE；所有 item 已终态且至少一 REJECTED 为 COMPLETE_WITH_ERRORS（包括全拒绝）。issued/rejected/waiting/unknown 四计数总和=itemCount，UNKNOWN 不算拒绝，部分成功＋等待不能 COMPLETE。 [原文 L142](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/e2/e2674e740f069490__ERP-FSC-01_完整四SPEC兼容开发设计_R2.md:142>)

numberReservationKey=(batchId,itemId,NUMBER)；artifactOperationKey=(batchId,itemId,固定 snapshotDigest,formatDigest,managementNo)。保存原资源结果 Ref/digest，已有 ARTIFACT_READY 不能重启渲染最新版。attemptGeneration 是非负整数字符串，worker 先 CAS；旧 worker 迟到不能覆盖新 generation、终态或新 UNKNOWN。普通查无行／超时／锁失败不是已回滚证据；只有准确 transaction/attempt 权威零效果结论才回原 checkpoint。 [原文 L139](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/e2/e2674e740f069490__ERP-FSC-01_完整四SPEC兼容开发设计_R2.md:139>)、[原文 L144](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/e2/e2674e740f069490__ERP-FSC-01_完整四SPEC兼容开发设计_R2.md:144>)

所有新本域摘要采用 SHA-256：UTF-8 JSON 恰含 domain、schemaVersion="X1-R2-1"、payload；键按 Unicode 码点排序，声明成员不可省略，nullable 写 null，未知成员拒绝，无非必要空白。字符串保原 Unicode，不自行全半角／大小写归一化；整数用规范十进制字符串；Dec 无浮点、指数或正号，零为 0、去多余尾零但仍受业务精度约束。DateOnly／UTC RFC3339 按各字段定义，原 native 日期另存。 [原文 L3](<D:/CP6-archives/consolidation-20261010/commercial-cache/x1/X1-R2/R2_LOCAL_DIGEST_RULES.md:3>)、[原文 L5](<D:/CP6-archives/consolidation-20261010/commercial-cache/x1/X1-R2/R2_LOCAL_DIGEST_RULES.md:5>)

| domain | 完整 payload 与顺序 | 出处 |
| --- | --- | --- |
| ERPFSC_SELECTION | F-I01完整准确SourceSnapshot、关系种类/既存采用Ref、所选format、required事实守卫版本、原Owner选择证据、Actor/Scope和expiresAt；requiredFieldPaths按路径升序 | [原文 L14](<D:/CP6-archives/consolidation-20261010/commercial-cache/x1/X1-R2/R2_LOCAL_DIGEST_RULES.md:14>) |
| ERPFSC_BATCH_INTENT | 完整F03body和可信Actor/T/L/Base；items按itemId升序；每item的selection/expectedHeads/mode/priorIssue/reason完整进入 | [原文 L15](<D:/CP6-archives/consolidation-20261010/commercial-cache/x1/X1-R2/R2_LOCAL_DIGEST_RULES.md:15>) |
| ERPFSC_ITEM_INTENT | 原batchId/commandKey/itemId及该item完整选择、sourceSnapshotDigest、formatDigest、managementNo和目标issueRevision/旧revision（适用时）；一个重试不改这些值 | [原文 L16](<D:/CP6-archives/consolidation-20261010/commercial-cache/x1/X1-R2/R2_LOCAL_DIGEST_RULES.md:16>) |
| ERPFSC_FACT_GUARD_REQUEST | FactGuardRequest全部成员，requiredFieldPaths升序、subject原Refs保全；不删调用者觉得无关的条件 | [原文 L17](<D:/CP6-archives/consolidation-20261010/commercial-cache/x1/X1-R2/R2_LOCAL_DIGEST_RULES.md:17>) |

不可变本域回执用 X1_LOCAL_COMMAND_RECEIPT，排除自身 digest/hash；封套 replayed 或当前披露投影不改原回执。原文件 digest 对原 bytes，外 Owner Ref/proof/rawDigest 保其原规范；本域 hash 不能证明 Producer 真实性或真实持锁。 [原文 L22](<D:/CP6-archives/consolidation-20261010/commercial-cache/x1/X1-R2/R2_LOCAL_DIGEST_RULES.md:22>)、[原文 L24](<D:/CP6-archives/consolidation-20261010/commercial-cache/x1/X1-R2/R2_LOCAL_DIGEST_RULES.md:24>)

## 7. 权限、披露与审计

issue 权还须所选来源明细／金额完整读权；模板维护独立，不接受任意服务器 path。金额无权仅安全列表，不能通过签发／下载完整模板泄漏。原 FSC 无 IAuditable 是旧追加履历审计选择，不简单补标记冒满足全部审计；新增本域履历与 SYS 审计分别负责。 [原文 L20](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/e2/e2674e740f069490__ERP-FSC-01_完整四SPEC兼容开发设计_R2.md:20>)、[原文 L24](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/e2/e2674e740f069490__ERP-FSC-01_完整四SPEC兼容开发设计_R2.md:24>)

History 保存 Actor/当前策略、原请求、source/format/artifact Ref、每件结果、修订关系和恢复事件；原 Owner proof、artifact digest、本域内容摘要分列，普通日志不复制敏感全文件。下载失败不改变原 ISSUED／receipt，不能重发绕过当前披露。 [原文 L140](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/e2/e2674e740f069490__ERP-FSC-01_完整四SPEC兼容开发设计_R2.md:140>)、[原文 L169](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/e2/e2674e740f069490__ERP-FSC-01_完整四SPEC兼容开发设计_R2.md:169>)

## 8. 页面、计数与客户端校验

Base 必填，include issued/unissued 至少一个，page≥1/pageSize≤200，号码/日期范围一致校验。旧 IssueDateFrom/To 实筛 Quotation.QtnIssueDate，UI 明示“报价签发日期”，新 DTO quotationIssueDateFrom/To；ProjectNo 必须实际过滤或明确不支持。旧编号存在显示“旧记录已编号，成品未核”。 [原文 L32](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/e2/e2674e740f069490__ERP-FSC-01_完整四SPEC兼容开发设计_R2.md:32>)、[原文 L33](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/e2/e2674e740f069490__ERP-FSC-01_完整四SPEC兼容开发设计_R2.md:33>)、[原文 L34](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/e2/e2674e740f069490__ERP-FSC-01_完整四SPEC兼容开发设计_R2.md:34>)、[原文 L35](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/e2/e2674e740f069490__ERP-FSC-01_完整四SPEC兼容开发设计_R2.md:35>)、[原文 L36](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/e2/e2674e740f069490__ERP-FSC-01_完整四SPEC兼容开发设计_R2.md:36>)

格式失败显示原因并禁签，不吞异常默选；来源行分版本／业务状态／签发资格；批次完整显示每 item 与四计数，每成功件独立下载。显式下载全部需可追成员索引的包，不能只自动开首件后声称全部交付。修拒绝项后明确新批次，成功项不默认重发。 [原文 L155](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/e2/e2674e740f069490__ERP-FSC-01_完整四SPEC兼容开发设计_R2.md:155>)、[原文 L156](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/e2/e2674e740f069490__ERP-FSC-01_完整四SPEC兼容开发设计_R2.md:156>)、[原文 L158](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/e2/e2674e740f069490__ERP-FSC-01_完整四SPEC兼容开发设计_R2.md:158>)、[原文 L159](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/e2/e2674e740f069490__ERP-FSC-01_完整四SPEC兼容开发设计_R2.md:159>)

Base／format／筛选／页改变清选中并提升 pageGeneration，迟到列表不覆新页；提交后离页可回原 batch 查询，UNKNOWN 不提示“失败请重发”。取消下载只停浏览器传输，不撤销签发。 [原文 L160](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/e2/e2674e740f069490__ERP-FSC-01_完整四SPEC兼容开发设计_R2.md:160>)、[原文 L161](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/e2/e2674e740f069490__ERP-FSC-01_完整四SPEC兼容开发设计_R2.md:161>)

## 9. 实施顺序、现有源码差距和切换

归档固定 CP6 SHA 90c871fe571fd6b390f53e8678376d7ce60bcb60：不是当前运行状态。既有查询／发行／管理号／履历／下载能力保留；源码差距是模板原样返回或文本假 xlsx、下载按当前模板再生、无准确固定 payload、重复号与唯一约束冲突风险、只下载首件、ProjectNo 未应用、请求客户／担当写入而 Base=null。这些是静态行为，不是已复现实例失败。 [原文 L15](<D:/CP6-archives/consolidation-20261010/commercial-cache/x1/X1-R2/SOURCE-AND-IMPACT.md:15>)、[原文 L26](<D:/CP6-archives/consolidation-20261010/commercial-cache/x1/X1-R2/SOURCE-AND-IMPACT.md:26>)、[原文 L27](<D:/CP6-archives/consolidation-20261010/commercial-cache/x1/X1-R2/SOURCE-AND-IMPACT.md:27>)、[原文 L28](<D:/CP6-archives/consolidation-20261010/commercial-cache/x1/X1-R2/SOURCE-AND-IMPACT.md:28>)、[原文 L29](<D:/CP6-archives/consolidation-20261010/commercial-cache/x1/X1-R2/SOURCE-AND-IMPACT.md:29>)、[原文 L35](<D:/CP6-archives/consolidation-20261010/commercial-cache/x1/X1-R2/SOURCE-AND-IMPACT.md:35>)

| 代号 | 固定源码窗口 | 事实与限制 | 出处 |
| --- | --- | --- | --- |
| F-SVC | CP6.Core/Services/Erp/FscChecklistService.cs L30–109、116–190、193–262 | 报价/计算关联查询；签发及回写；下载再生；模板原样或text fallback | [原文 L26](<D:/CP6-archives/consolidation-20261010/commercial-cache/x1/X1-R2/SOURCE-AND-IMPACT.md:26>) |
| F-MODEL | CP6.Entity/DomainModels/Erp/FscChecklist.cs；DTOs/Erp/BusinessPartnerDto.cs L320–394 | 签发履历、管理号、报价号/计算号、格式与路径；无固定成品内容hash | [原文 L27](<D:/CP6-archives/consolidation-20261010/commercial-cache/x1/X1-R2/SOURCE-AND-IMPACT.md:27>) |
| F-API | CP6.WebApi/Controllers/Erp/FscChecklistController.cs 全文 | list/formats/issue/download；issue动作权限 | [原文 L28](<D:/CP6-archives/consolidation-20261010/commercial-cache/x1/X1-R2/SOURCE-AND-IMPACT.md:28>) |
| F-UI | cp6.web/src/views/erp/FscChecklistView.vue L1–207，api/erp/fsc.ts、types/erp/fsc.ts | 分页、条件、批量签发；只自动下载首件；formats失败被忽略 | [原文 L29](<D:/CP6-archives/consolidation-20261010/commercial-cache/x1/X1-R2/SOURCE-AND-IMPACT.md:29>) |
| DB-CONTEXT | CP6.Core/EFDbContext/CP6Context.cs L255–270、1621–1665 | FSC管理号唯一索引；价格13字段唯一索引；工程No+Rev唯一索引；不等实例迁移已执行 | [原文 L35](<D:/CP6-archives/consolidation-20261010/commercial-cache/x1/X1-R2/SOURCE-AND-IMPACT.md:35>) |
| DB-MIG | 20260502225006_AddBpAndFscPA110.cs；20260506103852_AddSheetPriceAndPlateMold.cs | 前者建FSC表；后者Up/Down空，不能仅凭该文件名证明建价表/版模表 | [原文 L36](<D:/CP6-archives/consolidation-20261010/commercial-cache/x1/X1-R2/SOURCE-AND-IMPACT.md:36>) |
| HOST | CP6.WebApi/Program.cs L582–590、665；cp6.web/src/router/index.ts L102–105、165 | 三ERP服务DI、ERP PE注入NoOp；WMS独立DI；四ERP旧路由与独立WMS路由 | [原文 L37](<D:/CP6-archives/consolidation-20261010/commercial-cache/x1/X1-R2/SOURCE-AND-IMPACT.md:37>) |
| T-AUD | CP6.Tests/Erp/ErpAuditTests.cs L130–143、199–252 | 已有price审计、工程DecisionAmount审计、FSC追加无字段审计负测试文本 | [原文 L38](<D:/CP6-archives/consolidation-20261010/commercial-cache/x1/X1-R2/SOURCE-AND-IMPACT.md:38>) |
| T-PERM | CP6.Tests/ErpPermissionAttributeTests.cs L55–70；ErpPermissionSeedTests.cs L37–40 | 标签纯读豁免、导入/edit/issue等种子文本；不等真实用户权限证实 | [原文 L39](<D:/CP6-archives/consolidation-20261010/commercial-cache/x1/X1-R2/SOURCE-AND-IMPACT.md:39>) |

| 任务 | 目的 | 准确交付 | 依赖 | 真实采用门 | 出处 |
| --- | --- | --- | --- | --- | --- |
| X1-F01 | 核定清单格式映射 | 准确format版本/模板hash/字段单元格/输出格式；激活决定；缺件闭门；不加无证认证结论 | — | 正式模板内容核及行业清单用途/允许来源状态政策 | [原文 L335](<D:/CP6-archives/consolidation-20261010/commercial-cache/x1/X1-R2/DEVELOPMENT-TASKS.json:335>) |
| X1-F02 | 接通清单准确来源 | F-I01/I02真实Owner映射；准确QTN/EST版本关系/明细/币种单位；当前身份读权；SourceSnapshot/选择context；R2同根细化：FSC §5 ERPFSC-X1-R2-FG1：QTN关系事实与新增EST事实读守卫；不使用EST I03或新增Usage/accepted purpose；精确FactGuardRequest/Result | — | QTN准确原件与Owner schema未取得部分先补证；EST不回写不可变Version；新增FSC事实读扩展由EST正式Owner绑定，不能从原I03借用途 | [原文 L359](<D:/CP6-archives/consolidation-20261010/commercial-cache/x1/X1-R2/DEVELOPMENT-TASKS.json:359>) |
| X1-F03 | 实现清单签发恢复 | 稳定batch/item/command；每件原子、批可部分；number reservation；来源最终门；签发/history/receipt；明确projectionStatus；R2同根细化：F-I02两个HELD共同门+§6.1唯一错误状态矩阵；终态拒绝不复活；UNKNOWN只裁原效果；原number/artifact/attempt恢复；旧号投影与原生签发分层 | X1-F01, X1-F02, X1-F04 | SYS-NUM及QTN/EST实际参加、清单用途唯一域政策；明确事务/资源原效果裁定与两个FactGuard当前保护 | [原文 L390](<D:/CP6-archives/consolidation-20261010/commercial-cache/x1/X1-R2/DEVELOPMENT-TASKS.json:390>) |
| X1-F04 | 保存清单原件下载 | 真实格式渲染；固定artifact bytes/MIME/hash；阶段文件处理；准确revision下载；原件缺证不可再生冒原件 | X1-F01 | 真实持久文件回执与当前披露/保留策略 | [原文 L403](<D:/CP6-archives/consolidation-20261010/commercial-cache/x1/X1-R2/DEVELOPMENT-TASKS.json:403>) |
| X1-F05 | 完善清单批次界面 | 格式加载错误显式；所有结果逐件；全量下载入口；旧编号未证；generation/恢复/部分失败；project筛选；R2同根细化：F04显示terminal/effectKnowledge/resumeGate/attempt和四计数；部分成功带等待必PROCESSING；REJECTED修正新selection/key；已签发下载不重发 | X1-F02, X1-F03, X1-F04 | 服务返回真实item状态；不得只自动打开首件后称交付全 | [原文 L455](<D:/CP6-archives/consolidation-20261010/commercial-cache/x1/X1-R2/DEVELOPMENT-TASKS.json:455>) |
| X1-F06 | 映射清单旧履历 | 保原行/字段；缺历史artifact和Base明确未证；旧issue升级拒绝；旧download多修订明确选；T/L/Base单writer；R2同根细化：旧可变管理号仅原Owner兼容投影，缺receipt不冒已应用；要求旧同步回写的客户端不得偷进NEW_ISSUE；不回写EST immutable/Usage | X1-F02, X1-F03, X1-F04, X1-F05 | 真实迁移材料/全writer清单；不自动补失踪历史数据 | [原文 L493](<D:/CP6-archives/consolidation-20261010/commercial-cache/x1/X1-R2/DEVELOPMENT-TASKS.json:493>) |
| X1-F07 | 执行清单验收回归 | 真实文件格式、下载字节稳定、并发重放、批部分结果、当前权限、源关系与旧入口回归；R2同根细化：同原AC-F10/11/12/13/14/15/23细化用途越界、未绑定零ISSUED、202中断、等待/拒绝唯一分流和commit恢复 | X1-F01, X1-F02, X1-F03, X1-F04, X1-F05, X1-F06 | 本轮全部NOT_RUN，不使用生产认证数据作伪证明 | [原文 L529](<D:/CP6-archives/consolidation-20261010/commercial-cache/x1/X1-R2/DEVELOPMENT-TASKS.json:529>) |

按 T/L/Base 登记 LEGACY_READ_ONLY／NEW_ISSUE，完整映射和模板未证只保历史读／预览；无双 writer。旧 issue 缺 commandKey/准确 selection 返回 COMPATIBILITY_UPGRADE_REQUIRED，不后端填 Base／客户或发号冒兼容成功。旧 download/fscNo 多修订要明确选择，不能 First；依赖旧同步回写完成语义的客户端不能进入分层 NEW_ISSUE。回退只停新发行、保原下载并前滚修复。 [原文 L119](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/e2/e2674e740f069490__ERP-FSC-01_完整四SPEC兼容开发设计_R2.md:119>)、[原文 L184](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/e2/e2674e740f069490__ERP-FSC-01_完整四SPEC兼容开发设计_R2.md:184>)、[原文 L186](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/e2/e2674e740f069490__ERP-FSC-01_完整四SPEC兼容开发设计_R2.md:186>)

## 10. 原验收场景

以下 24 项全部 **NOT_RUN**，不作文件渲染／下载／事务恢复实际通过声明。

| AC | 准确预期 | SPEC | 出处 |
| --- | --- | --- | --- |
| AC-F01 | 无模板配置时明确不可用，不出现假默认正式格式 | 01 | [原文 L194](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/e2/e2674e740f069490__ERP-FSC-01_完整四SPEC兼容开发设计_R2.md:194>) |
| AC-F02 | 同名多格式必须选稳定id/revision，不取首项 | 01 | [原文 L195](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/e2/e2674e740f069490__ERP-FSC-01_完整四SPEC兼容开发设计_R2.md:195>) |
| AC-F03 | 正式模板需原单元格映射/真实Owner决定；缺件保持UNPROVEN | 01 | [原文 L196](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/e2/e2674e740f069490__ERP-FSC-01_完整四SPEC兼容开发设计_R2.md:196>) |
| AC-F04 | 生成文件包含准确payload且实际格式/MIME/后缀一致，无text伪Excel | 01 | [原文 L197](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/e2/e2674e740f069490__ERP-FSC-01_完整四SPEC兼容开发设计_R2.md:197>) |
| AC-F05 | 模板更新不改已签发artifact和原format ref | 01 | [原文 L198](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/e2/e2674e740f069490__ERP-FSC-01_完整四SPEC兼容开发设计_R2.md:198>) |
| AC-F06 | 客户端客户/担当/Base不被当来源事实；关系不一致拒绝 | 04 | [原文 L199](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/e2/e2674e740f069490__ERP-FSC-01_完整四SPEC兼容开发设计_R2.md:199>) |
| AC-F07 | 明细1/币种/单位/金额每项有准确出处，不隐式汇总其他行 | 04 | [原文 L200](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/e2/e2674e740f069490__ERP-FSC-01_完整四SPEC兼容开发设计_R2.md:200>) |
| AC-F08 | ProjectNo、报价日期及号码范围实际应用；坏范围前后端一致拒绝 | 04 | [原文 L201](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/e2/e2674e740f069490__ERP-FSC-01_完整四SPEC兼容开发设计_R2.md:201>) |
| AC-F09 | 仅管理号非空的旧记录显示旧未证，不称新ISSUED | 04 | [原文 L202](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/e2/e2674e740f069490__ERP-FSC-01_完整四SPEC兼容开发设计_R2.md:202>) |
| AC-F10 | 选后源变化或明确缺关系/字段，原item按§6.1终态REJECTED；暂态读失败保持等待，不能由旧preview或后来新版本放行 | 04 | [原文 L203](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/e2/e2674e740f069490__ERP-FSC-01_完整四SPEC兼容开发设计_R2.md:203>) |
| AC-F11 | 同key同body得同batch/item；异body拒绝；REJECTED后来补齐资料仍回原拒绝，修正须新selection/key；重复目标不能发两份 | 02 | [原文 L204](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/e2/e2674e740f069490__ERP-FSC-01_完整四SPEC兼容开发设计_R2.md:204>) |
| AC-F12 | 文件先备妥、QTN事实守卫与新EST事实读守卫实际HELD、签发内容/关系/回执共同终态；任何未绑定门零ISSUED；202后中断以原checkpoint恢复 | 02 | [原文 L205](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/e2/e2674e740f069490__ERP-FSC-01_完整四SPEC兼容开发设计_R2.md:205>) |
| AC-F13 | 批3件2成功1拒绝为COMPLETE_WITH_ERRORS/issuedCount2；2成功1等待或UNKNOWN必为PROCESSING，未终态不计失败自动补发 | 02 | [原文 L206](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/e2/e2674e740f069490__ERP-FSC-01_完整四SPEC兼容开发设计_R2.md:206>) |
| AC-F14 | 预留号码但渲染暂态失败不算签发，原item保号；号码/文件结果未知先查原操作；CORRECTION跳新根取号用原managementNo | 02 | [原文 L207](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/e2/e2674e740f069490__ERP-FSC-01_完整四SPEC兼容开发设计_R2.md:207>) |
| AC-F15 | commit未知只查原效果，依赖恢复不能重签；只有权威回滚零效果证明才回原checkpoint重核；普通查无行不足；迟到旧attempt不覆新状态 | 02 | [原文 L208](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/e2/e2674e740f069490__ERP-FSC-01_完整四SPEC兼容开发设计_R2.md:208>) |
| AC-F16 | CORRECTION生成新修订并保旧原件；重印只下载原revision | 02 | [原文 L209](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/e2/e2674e740f069490__ERP-FSC-01_完整四SPEC兼容开发设计_R2.md:209>) |
| AC-F17 | 下载固定原bytes，模板改动/当前源改动不改变原件 | 03 | [原文 L210](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/e2/e2674e740f069490__ERP-FSC-01_完整四SPEC兼容开发设计_R2.md:210>) |
| AC-F18 | 原artifact缺失或digest不同明确阻断，不再生伪原件 | 03 | [原文 L211](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/e2/e2674e740f069490__ERP-FSC-01_完整四SPEC兼容开发设计_R2.md:211>) |
| AC-F19 | 批成功所有文件均有独立下载，不只首件；取消传输不撤签发 | 03 | [原文 L212](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/e2/e2674e740f069490__ERP-FSC-01_完整四SPEC兼容开发设计_R2.md:212>) |
| AC-F20 | 无金额/来源/file披露权不能通过下载拿全模板；回执读也现权核 | 03 | [原文 L213](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/e2/e2674e740f069490__ERP-FSC-01_完整四SPEC兼容开发设计_R2.md:213>) |
| AC-F21 | 旧fscNo映射多修订要求明确选择，不First命中；未映射保历史缺证 | 03 | [原文 L214](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/e2/e2674e740f069490__ERP-FSC-01_完整四SPEC兼容开发设计_R2.md:214>) |
| AC-F22 | 晚到格式/列表响应不覆盖新Base/筛选和勾选；未知命令离页可查回 | 02/03 | [原文 L215](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/e2/e2674e740f069490__ERP-FSC-01_完整四SPEC兼容开发设计_R2.md:215>) |
| AC-F23 | FSC不能调用QUOTE_BASIS/PRODUCT_REFERENCE伪装用途，不新增EST Usage；既有QTN采用receipt只作关系历史；新Required事实读门缺失零ISSUED；旧号投影PENDING不冒Owner已应用 | 02/04 | [原文 L216](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/e2/e2674e740f069490__ERP-FSC-01_完整四SPEC兼容开发设计_R2.md:216>) |
| AC-F24 | 文件、管理号或清单状态不能推FSC认证/工程PASS/客户接受 | 全四项 | [原文 L217](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/e2/e2674e740f069490__ERP-FSC-01_完整四SPEC兼容开发设计_R2.md:217>) |

## 11. 真实政策、模板和 Owner 采用缺口

需行业 Owner 核模板单元格与视觉内容、FscProductDiv 1/2/3 的适用格式、允许来源状态及清单用途唯一域；不能从 MasterConfirmFlg=1 回写条件猜完整政策。真实 QTN 关系守卫、EST 新事实读扩展和同事务保护、NUM/FILE/IAM/AUD 接入尚待准确绑定。设计已规定缺失时的唯一分支，X1-R02/R03 已静态关闭。 [原文 L39](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/e2/e2674e740f069490__ERP-FSC-01_完整四SPEC兼容开发设计_R2.md:39>)、[原文 L48](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/e2/e2674e740f069490__ERP-FSC-01_完整四SPEC兼容开发设计_R2.md:48>)、[原文 L91](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/e2/e2674e740f069490__ERP-FSC-01_完整四SPEC兼容开发设计_R2.md:91>)、[原文 L167](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/e2/e2674e740f069490__ERP-FSC-01_完整四SPEC兼容开发设计_R2.md:167>)、[原文 L221](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/e2/e2674e740f069490__ERP-FSC-01_完整四SPEC兼容开发设计_R2.md:221>)

没有真实 Producer 合同满足完整 subject／字段／expected／held 条件就继续 UNPROVEN，不简化为一个字段存在或字符串 HELD。新守卫不能借原 I03；已证业务缺失终态拒绝、暂时未绑定等待、提交未知原键查回各有不同退出条件。 [原文 L140](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/91/918bbd4508551b64__00_X1_R2_独立回归与完整范围静态审阅报告.md:140>)、[原文 L146](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/91/918bbd4508551b64__00_X1_R2_独立回归与完整范围静态审阅报告.md:146>)、[原文 L152](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/91/918bbd4508551b64__00_X1_R2_独立回归与完整范围静态审阅报告.md:152>)

## 12. 实际阅读覆盖与继续队列

本轮全文：PRICE 223 行、FSC 221 行、X1 R2 独审 168 行；只读 ZIP 展开的摘要附录 24 行、SOURCE-AND-IMPACT 76 行、主索引 38 行、FROZEN-REUSE／ACCEPTED-REUSE／BASELINES。DEVELOPMENT-TASKS 全部顶层语义与 PRICE 9＋FSC 7 对象结构实读，ENG 5 项未据此宣称已读。两份 UA／CURRENT 按接受、字节身份、范围、评审和执行边界对象结构阅读；精确选择器及非全文边界见[商业阅读台账](<D:/CP6/docs/CP6_开发设计文档_20261010/evidence/commercial-reading.json>)。

R2 ZIP SHA `fe2a9354e5e1d04c928565953d5e38437cb955648d0b5dc371462e41693ad434`，84,173 bytes、15 成员；14成员字节清单已全部核对。R2_RETURN_RESPONSE 四根回复、全部47 SOURCE-LOCATORS、AUTHOR-STOPPED、原Scope的两商业Target和DG14／DP11／12／16已补充结构合读。R1冻结原包按清单逐成员核对0差异；47源码文本统一为LF和一个末尾换行，其中3份迁移恢复UTF8 BOM，全部匹配所登Git blob。原文本导出本来就允许追加末行，不能拿新文本SHA冒原Git blob，也不能把本次身份核验当47源码实现全文审查。 [导出规则 L15](<D:/CP6-archives/consolidation-20261010/commercial-cache/x1/X1-R2/SOURCE-AND-IMPACT.md:15>)

原三份xls模板／PA100、RA040、PA130等母版在当前全量ZIP成员索引按.xls/.xlsx及上述名称检索无命中；实际单元格、格式、图形仍未核。这个结论仅限当前本地索引，不声明源端319队列已经完成。R1旧正文与首轮RETURN保留历史身份，以当前R2原独审的关闭关系解释；不因此制造新缺陷。

状态 `required_materials_consolidated`：当前必要规范及辅助语义完成归并，历史实现审计、原母版和真实Owner采用保持上述边界；没有执行源脚本、SQL、业务测试、构建或远程操作。相关：[EST](<D:/CP6/docs/CP6_开发设计文档_20261010/modules/ERP-EST-01.md>)、[QTN](<D:/CP6/docs/CP6_开发设计文档_20261010/modules/ERP-QTN-01.md>)、[商业合同](<D:/CP6/docs/CP6_开发设计文档_20261010/contracts/commercial-contracts.json>)。
