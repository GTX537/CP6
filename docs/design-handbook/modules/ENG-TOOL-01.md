# ENG-TOOL-01 · 工装技术定义与现有版型相容

整理状态：`core_semantics_consolidated`。基线为RC1/RV01，X1-R2只增加准确版本/Owner当前证据相容合同；真实Owner参与和实施均未证明。

## 1. 目的、角色与Owner

工程人员维护工具、版型、模具的技术规格和产品/工序适用关系；WMS/实际资产Owner持实物实例、位置、使用次数、维护、校准、寿命及报废。技术规格通过验证不表示每个实物现在可用于某工单，实际实例Ready也不是MES工单或REL许可。[原文S:592](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/37/374d1b7782cb72d0__11_SUP_当前阅读主册.md:592) [原文S:694](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/37/374d1b7782cb72d0__11_SUP_当前阅读主册.md:694) [原文T:5](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/76/760c35b523260b23__ENG-TOOL-COMPATIBILITY.md:5)

技术变更由ENG/ECM/CFG/LCM控制；采购/订单/PE由原ERP Owner控制。现有版型的订单联动必须收束到原Owner，不由新工具页生成第二套商业订单。[原文T:69](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/76/760c35b523260b23__ENG-TOOL-COMPATIBILITY.md:69) [原文T:71](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/76/760c35b523260b23__ENG-TOOL-COMPATIBILITY.md:71)

## 2. 设计组合和接受边界

继承RC1/RV01整包 `4b1934e7747e8ac3f0cb6787469775d28cf891b1b35ff0a351323466e0bdbc91`，合读SUP工装/影响、X1-R2 `ENG-TOOL-COMPATIBILITY.md` SHA `760c35b523260b23`前缀、`R2_LOCAL_DIGEST_RULES.md` SHA `7fe872fc8d85d676`前缀、独审SHA `918bbd4508551b64`前缀。准确全值保存在机器合同。[原文A:21](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/01/0170b29fed20a30b__APR-CP6-PLM-BASELINE-001.md:21) [原文T:3](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/76/760c35b523260b23__ENG-TOOL-COMPATIBILITY.md:3) [原文X:19](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/91/918bbd4508551b64__00_X1_R2_独立回归与完整范围静态审阅报告.md:19)

独审覆盖6个ENG SPEC的继承/命中相容变更，结论`STATIC_DESIGN_PASS_WITH_REQUIRED_INPUT_GATES`，不是重新接受全部历史Excel/开发规范；5个工程工作包`PLANNED_NOT_AUTHORIZED`、16个新增AC全`NOT_RUN`。接受来源与Review分开，Review自身不构成用户最终批准或运行采用。[原文X:7](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/91/918bbd4508551b64__00_X1_R2_独立回归与完整范围静态审阅报告.md:7) [原文X:13](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/91/918bbd4508551b64__00_X1_R2_独立回归与完整范围静态审阅报告.md:13) [原文X:15](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/91/918bbd4508551b64__00_X1_R2_独立回归与完整范围静态审阅报告.md:15) [原文X:129](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/91/918bbd4508551b64__00_X1_R2_独立回归与完整范围静态审阅报告.md:129) [原文X:164](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/91/918bbd4508551b64__00_X1_R2_独立回归与完整范围静态审阅报告.md:164)

## 3. 数据字段、身份与版本轴

|字段/对象|规则|
|---|---|
|Legacy Native Identity|`BaseCd`20、完整`WdPtnNo`40、整数`WdRev`；`VrsnName`100。截前15位ProductCd/ItemCd只是旧映射，不能当新准确技术身份，也不批量改历史订单ID。[原文T:34](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/76/760c35b523260b23__ENG-TOOL-COMPATIBILITY.md:34) [原文T:44](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/76/760c35b523260b23__ENG-TOOL-COMPATIBILITY.md:44)|
|业务与技术快照|估价/得意先/代表产品/供应商20位等原字段，ProcessCd/TypeClass/NewVerCd、日期/纸板/印刷快照；商业金额和销售/采购单据仍ERP Owner。`StDate/EndDate`不是执行许可。[原文T:44](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/76/760c35b523260b23__ENG-TOOL-COMPATIBILITY.md:44)|
|尺寸数量|SheetWidth/Flow、BladeWidth/Flow为decimal(15,4)，CompositionQty/MfgQty至少1；颜色、册数、厚度等沿实际单位/Owner，缺事实不能填0。8个附件bool和Need*不能当受控文档证明。[原文T:44](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/76/760c35b523260b23__ENG-TOOL-COMPATIBILITY.md:44)|
|三套状态|旧Rev/Status0或9/McTransferFlg；技术定义与受控内容；实物当前状态。任何一套都不能推导另两套。[原文T:53](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/76/760c35b523260b23__ENG-TOOL-COMPATIBILITY.md:53) [原文T:63](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/76/760c35b523260b23__ENG-TOOL-COMPATIBILITY.md:63)|
|Association Proposal|proposalId/revision/digest、准确工具技术Ref与实物原生Ref或DOC Ref、适用产品/工艺/配置/用途/效期、双方凭据、actor/reason/supersedes。INSTANCE状态PROPOSED/CONFIRMED/REJECTED/REVOKED/UNKNOWN。[原文T:89](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/76/760c35b523260b23__ENG-TOOL-COMPATIBILITY.md:89) [原文T:97](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/76/760c35b523260b23__ENG-TOOL-COMPATIBILITY.md:97)|
|ProjectionHead|proposalControlRevision保护可控状态，observationGeneration使旧观察轮次失效，projectionRevision随当前投影变化；activeRoundId、knowledge KNOWN/UNKNOWN/CONFLICT、lastAppliedEvidenceRefs、everConfirmed（只false→true）。三版轴不能合并。[原文T:133](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/76/760c35b523260b23__ENG-TOOL-COMPATIBILITY.md:133)|
|影响申请|changeKind UPDATE/SUBSTITUTE/RETIRE；前两种from!=to，RETIRE的to固定null。技术PROPOSED/UNDER_OWNER_REVIEW/EFFECTIVE/RETIRED/UNKNOWN与impactCompletion OPEN/COMPLETE分轴。[原文T:105](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/76/760c35b523260b23__ENG-TOOL-COMPATIBILITY.md:105) [原文T:109](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/76/760c35b523260b23__ENG-TOOL-COMPATIBILITY.md:109) [原文T:176](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/76/760c35b523260b23__ENG-TOOL-COMPATIBILITY.md:176)|

历史技术内容不可证明为`LEGACY_CONTENT_UNPROVEN`。DB RowVersion、业务修订、技术摘要、提案内容摘要、原Owner原始字节摘要是不同东西。[原文T:57](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/76/760c35b523260b23__ENG-TOOL-COMPATIBILITY.md:57) [原文T:61](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/76/760c35b523260b23__ENG-TOOL-COMPATIBILITY.md:61) [原文T:63](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/76/760c35b523260b23__ENG-TOOL-COMPATIBILITY.md:63) [原文D:3](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/7f/7fe872fc8d85d676__R2_LOCAL_DIGEST_RULES.md:3)

## 4. 命令、状态和本地写集

旧创建/改版先满足expectedNativeRowVersion及expectedTechnicalControl，缺版本返回`VERSION_REQUIRED`，不可自动取latest补齐。新修订/旧有效区间/命令回执在一事务提交，并发只能有合法后继；不能简单max+1失败后改Rev重试。[原文T:57](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/76/760c35b523260b23__ENG-TOOL-COMPATIBILITY.md:57) [原文T:61](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/76/760c35b523260b23__ENG-TOOL-COMPATIBILITY.md:61) [原文T:65](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/76/760c35b523260b23__ENG-TOOL-COMPATIBILITY.md:65)

|入口|本地行为与完成条件|
|---|---|
|实例/DOC关联提案|保存准确proposal内容/控制版/命令身份；ENG只写本地关联意图，实际资产/DOC仍原Owner。[原文T:89](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/76/760c35b523260b23__ENG-TOOL-COMPATIBILITY.md:89) [原文T:91](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/76/760c35b523260b23__ENG-TOOL-COMPATIBILITY.md:91) [原文T:97](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/76/760c35b523260b23__ENG-TOOL-COMPATIBILITY.md:97)|
|ReceiveOwnerEvidence|真实Owner绑定的NativeEvidenceEnvelope+deliveryId/bodyDigest、完整subject/role/decision；原始Inbox/回执同一事务，返回RECORDED/DUPLICATE/CONFLICT，收件不等应用。[原文T:144](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/76/760c35b523260b23__ENG-TOOL-COMPATIBILITY.md:144) [原文T:146](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/76/760c35b523260b23__ENG-TOOL-COMPATIBILITY.md:146)|
|StartCurrentRound|锁Head，校验当前proposal/control，固定完整subject/roles/actor字段范围/Owner heads/目标集；generation++，创建round，当前knowledge转UNKNOWN。未绑定Owner schema不伪造查询。[原文T:150](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/76/760c35b523260b23__ENG-TOOL-COMPATIBILITY.md:150)|
|ApplyCurrentRound|锁Head，精确匹配round/control/generation和当前权限；每个必需role准确一项当前结果，Owner参与者保护到实际提交；写当前投影+历史+ApplicationReceipt+RoundApplied同一事务。[原文T:154](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/76/760c35b523260b23__ENG-TOOL-COMPATIBILITY.md:154) [原文T:156](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/76/760c35b523260b23__ENG-TOOL-COMPATIBILITY.md:156)|
|商业联动意图|携带完整No/Rev、准确技术身份、CREATE_OR_LINK/CHANGE_REQUEST/CANCEL_REQUEST、原订单Ref等向原Owner申请；只有真实匹配回执才OWNER_APPLIED。[原文T:71](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/76/760c35b523260b23__ENG-TOOL-COMPATIBILITY.md:71) [原文T:73](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/76/760c35b523260b23__ENG-TOOL-COMPATIBILITY.md:73)|
|停止采用/退役|本地技术控制及影响跟踪；不直接删商业已提交订单，不以技术RETIRED替代实物报废或所有义务COMPLETE。[原文T:75](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/76/760c35b523260b23__ENG-TOOL-COMPATIBILITY.md:75) [原文T:168](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/76/760c35b523260b23__ENG-TOOL-COMPATIBILITY.md:168) [原文T:172](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/76/760c35b523260b23__ENG-TOOL-COMPATIBILITY.md:172)|

INSTANCE必须ENG_SPEC_CONFIRMATION和PHYSICAL_INSTANCE_ASSOCIATION在同一当前轮次完整保护，才CONFIRMED。首次确认前真实拒绝为REJECTED，曾确认后撤销为REVOKED；Unknown不伪装撤销。DOC关联需要ENG_DOCUMENT_RELATION和DOC_REFERENCE_AUTHORITY双Role。REJECTED/REVOKED终态不能靠新观察复活，必须新supersedes提案。[原文T:140](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/76/760c35b523260b23__ENG-TOOL-COMPATIBILITY.md:140) [原文T:162](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/76/760c35b523260b23__ENG-TOOL-COMPATIBILITY.md:162) [原文T:164](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/76/760c35b523260b23__ENG-TOOL-COMPATIBILITY.md:164) [原文T:166](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/76/760c35b523260b23__ENG-TOOL-COMPATIBILITY.md:166)

## 5. API、跨Owner依赖与精确wire

旧`/api/plate-molds` next-seq、by-estimate-calc、by-product/{No}?rev、history、POST、PUT revise/update、DELETE、list/CSV/label保留入口兼容；全部写入口都受版本/权限约束，不能只修新页。[原文T:115](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/76/760c35b523260b23__ENG-TOOL-COMPATIBILITY.md:115)

候选前缀`/api/main/v1/engineering-tools`的E01–E06分别为：GET legacy/{No}/revisions/{rev}；POST {toolId}/instance-association-proposals；GET associations；POST document-association-proposals；POST impact-requests；GET commands/{key}。准确字段组在原文表，不以这些候选URL宣称已部署。[原文T:117](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/76/760c35b523260b23__ENG-TOOL-COMPATIBILITY.md:117)

Owner Ref必须是正式schema，不接受任意JSON/URL冒充。Envelope绑定contractId/schemaRevision/schemaDigest/ownerAdoptionRef；当前回查须匹配requestedSubjectEcho/role/queryDigest/nativeHead/currentPolicy和当前权限。绑定未知只允许本地准备，不授予正式准确关联/当前正面状态。[原文T:125](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/76/760c35b523260b23__ENG-TOOL-COMPATIBILITY.md:125) [原文T:144](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/76/760c35b523260b23__ENG-TOOL-COMPATIBILITY.md:144) [原文T:152](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/76/760c35b523260b23__ENG-TOOL-COMPATIBILITY.md:152) [原文T:178](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/76/760c35b523260b23__ENG-TOOL-COMPATIBILITY.md:178)

## 6. 原子性、幂等、并发与恢复

`Tenant+Owner+DeliveryId`同body幂等且不增加generation；同delivery不同body两份保留隔离，knowledge=CONFLICT并generation++。不同delivery同native receipt/body去重，native receipt同Ref不同body也冲突。只按Owner正式版本比较规则判旧，不能按时间戳或字符串排序。[原文T:148](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/76/760c35b523260b23__ENG-TOOL-COMPATIBILITY.md:148)

一个角色结果到达仅更新该轮slot，不增加generation使另一角色无故过期；真正重新观察、提案改变、撤回或当前权限/事实失效才使旧轮无效。同round同Application body返回原回执，不同body冲突；旧round/control/generation仅历史记录并`applied=false, STALE_OBSERVATION`。[原文T:133](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/76/760c35b523260b23__ENG-TOOL-COMPATIBILITY.md:133) [原文T:150](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/76/760c35b523260b23__ENG-TOOL-COMPATIBILITY.md:150) [原文T:156](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/76/760c35b523260b23__ENG-TOOL-COMPATIBILITY.md:156) [原文T:180](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/76/760c35b523260b23__ENG-TOOL-COMPATIBILITY.md:180)

正面当前状态需要`CurrentEvidenceParticipant.HoldExact`在同一实际事务或已证明协议中保护准确subject/nativeHead直到提交。TTL、签名、老回执、先GET再保存都不够；做不到为`OWNER_PARTICIPANT_UNPROVEN`/UNKNOWN，不能发布当前CONFIRMED/EFFECTIVE/COMPLETE。[原文T:154](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/76/760c35b523260b23__ENG-TOOL-COMPATIBILITY.md:154) [原文T:178](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/76/760c35b523260b23__ENG-TOOL-COMPATIBILITY.md:178)

影响COMPLETE还要求完整where-used覆盖、准确目标集digest、每个目标真实APPLIED或Owner证明N/A及提交保护。分页未全读/权限过滤/超时/缺Owner都不是完整集；出现新义务或撤回须generation++并回OPEN，RETIRED+OPEN合法。[原文T:169](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/76/760c35b523260b23__ENG-TOOL-COMPATIBILITY.md:169) [原文T:172](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/76/760c35b523260b23__ENG-TOOL-COMPATIBILITY.md:172)

本地摘要仅用于X1新协议：X1-R2-1带domain/schemaVersion/payload，成员齐全、未知字段拒绝、nullable写null、UTF8/紧凑JSON、整数/decimal字符串规则、指定集合排序。不得重编码Owner原始bytes生成“相同摘要”；也不能把digest当跨系统信任或事务证明。[原文D:3](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/7f/7fe872fc8d85d676__R2_LOCAL_DIGEST_RULES.md:3) [原文D:5](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/7f/7fe872fc8d85d676__R2_LOCAL_DIGEST_RULES.md:5) [原文D:18](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/7f/7fe872fc8d85d676__R2_LOCAL_DIGEST_RULES.md:18) [原文D:24](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/7f/7fe872fc8d85d676__R2_LOCAL_DIGEST_RULES.md:24)

商业动作UNKNOWN按原业务键查原Owner，不能换Key或新建订单；NoOp返回Ok仍为`STUB_NOT_EXECUTED`，不能称ERP已应用。前向恢复保留已接受事实，不倒写各Owner原账。[原文T:32](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/76/760c35b523260b23__ENG-TOOL-COMPATIBILITY.md:32) [原文T:73](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/76/760c35b523260b23__ENG-TOOL-COMPATIBILITY.md:73) [原文T:184](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/76/760c35b523260b23__ENG-TOOL-COMPATIBILITY.md:184) [原文T:188](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/76/760c35b523260b23__ENG-TOOL-COMPATIBILITY.md:188)

## 7. 权限、租户和敏感字段

DP11要求Tenant/法人/Base/工具组；DP12覆盖API/UI/内部writer/PE/导出；DP16区分技术用途、目标接收、Ready、采用。读取回执/历史也按当前权限裁剪，旧权限证明不能恢复当前正面状态。[原文T:186](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/76/760c35b523260b23__ENG-TOOL-COMPATIBILITY.md:186) [原文T:127](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/76/760c35b523260b23__ENG-TOOL-COMPATIBILITY.md:127) [原文T:154](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/76/760c35b523260b23__ENG-TOOL-COMPATIBILITY.md:154)

标签每个{WdPtnNo,WdRev}单独检查可见性，不可见/缺失逐项说明不静默丢弃；全部可读才允许旧CSV全结果，公式样文本按字面导出。实物历史字段保原来源只读，不伪装WMS当前值。[原文T:51](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/76/760c35b523260b23__ENG-TOOL-COMPATIBILITY.md:51) [原文T:81](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/76/760c35b523260b23__ENG-TOOL-COMPATIBILITY.md:81)

## 8. 页面和标签相容

页面并列三状态轴、真实Owner/asOf/Unknown，读请求带页面generation，旧No/Rev响应不能覆盖新页面。Unknown保留原命令键，当前工单Ready由实际Owner重新判断。[原文T:127](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/76/760c35b523260b23__ENG-TOOL-COMPATIBILITY.md:127)

`POST /api/plate-molds/label`保持13列：版型NO、Rev、版型名、版型分類、得意先、代表製品CD、刃渡巾、刃渡流れ、付数、個数、色数、場所、棚ライン。布局改变须显式版本和消费者同意；扫码只查询不授予执行许可，重印不建新Rev。[原文T:79](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/76/760c35b523260b23__ENG-TOOL-COMPATIBILITY.md:79) [原文T:83](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/76/760c35b523260b23__ENG-TOOL-COMPATIBILITY.md:83)

技术文档关联展示原件/派生角色、权威语言和准确修订；8个附件bool不能打“证据齐全”。必要文档缺失可保草稿，但不能满足冻结/采用条件。[原文T:97](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/76/760c35b523260b23__ENG-TOOL-COMPATIBILITY.md:97) [原文T:99](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/76/760c35b523260b23__ENG-TOOL-COMPATIBILITY.md:99)

## 9. 固定代码差异与实施顺序

X1源在固定Main `90c871fe`观察到Create/Revise经过Engineering Save、OrderDetail/ArrangeNo/PE，UpdateRelink重建Order，Delete级联软删订单；这些是旧路径收束对象，不是允许新模块继续跨Owner写。Program注册ERP+NoOpPE（源引用588–590）及WMS（665），NoOp不能视真实成功；本册没有复审当前HEAD。[原文T:30](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/76/760c35b523260b23__ENG-TOOL-COMPATIBILITY.md:30) [原文T:32](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/76/760c35b523260b23__ENG-TOOL-COMPATIBILITY.md:32) [原文X:34](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/91/918bbd4508551b64__00_X1_R2_独立回归与完整范围静态审阅报告.md:34)

原5工作包依次：E01字段/全writer，E02版本和原Owner订单联动，E03实物关联，E04文档/影响/标签，E05全部AC。均PLANNED_NOT_AUTHORIZED；先拿真实Owner schema和提交参与者，再启用当前正面状态，不能先上线“确认”后补保障。[原文T:213](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/76/760c35b523260b23__ENG-TOOL-COMPATIBILITY.md:213) [原文X:142](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/91/918bbd4508551b64__00_X1_R2_独立回归与完整范围静态审阅报告.md:142)

## 10. 验收与运行状态

AC-E01–E16包括旧字段/准确NoRev、并发修订、商业回执/Unknown、NoOp非执行、13列标签、规格与实例分离、DOC身份、影响RETIRE-to-null、过期轮次、双Role当前参与、完整目标义务等；全部`NOT_RUN`，静态6SPEC覆盖不能升级为真实Owner采用或旧Excel全验收。[原文T:196](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/76/760c35b523260b23__ENG-TOOL-COMPATIBILITY.md:196) [原文X:122](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/91/918bbd4508551b64__00_X1_R2_独立回归与完整范围静态审阅报告.md:122) [原文X:129](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/91/918bbd4508551b64__00_X1_R2_独立回归与完整范围静态审阅报告.md:129)

尤其要验证A角色确认后B变化使旧双方slot失效、迟到A不能恢复徽标；参与者无提交保障时不能CONFIRMED；实例已技术退役但某实际义务未完成保持RETIRED+OPEN。都是待执行场景。[原文T:169](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/76/760c35b523260b23__ENG-TOOL-COMPATIBILITY.md:169) [原文T:172](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/76/760c35b523260b23__ENG-TOOL-COMPATIBILITY.md:172) [原文T:180](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/76/760c35b523260b23__ENG-TOOL-COMPATIBILITY.md:180)

### PG04 工具规格、实例与变化直接断言

以下为共享原Owner动作的开发验收观察点，编号前缀为 `AC/TC-PLM-PG04-A`，均 **NOT_RUN**；假设前置不是现实事实，不改变本册Owner边界。

|断言|必须保持的可观察结果|
|---|---|
|191/192|技术草案不需伪造未来实物；例行保养留实际Owner，不自动形成技术修订。 [原文PG04:1005](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/b0/b066823003eadb18__b066823003eadb1853d50bacba59fb1a42cd7d1968bb3ee48cb80422bd2f088c.md:1005)|
|193/194|T1/T2各有独立规格修订证据、维护/校准；同型号不复制T1资格给T2。 [原文PG04:1108](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/b0/b066823003eadb18__b066823003eadb1853d50bacba59fb1a42cd7d1968bb3ee48cb80422bd2f088c.md:1108)|
|195/196|Spec@2/Product@3/Route@4组合交VER真实受理；ACK不造PlanId/PASS，也不换键掩未知。 [原文PG04:1211](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/b0/b066823003eadb18__b066823003eadb1853d50bacba59fb1a42cd7d1968bb3ee48cb80422bd2f088c.md:1211)|
|197/198|D@2→D@3及工具变更保已知/未知影响；不可查旧副本/WIP不当0、不全域切换。 [原文PG04:1314](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/b0/b066823003eadb18__b066823003eadb1853d50bacba59fb1a42cd7d1968bb3ee48cb80422bd2f088c.md:1314)|
|199/200|只确认SiteA已证份额，SiteB继续未结；技术OBSOLETE不等资产报废/库存清空。 [原文PG04:1418](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/b0/b066823003eadb18__b066823003eadb1853d50bacba59fb1a42cd7d1968bb3ee48cb80422bd2f088c.md:1418)|

实际使用时的实例绑定、原Owner资格与通过提交时点的有效性仍须满足本册 X1-R2 附录，不由这些PG04文本检查替代。


**PG03 必要验收附件。** 固定116动作中先补的16动作32直接断言，与后续PG04的100动作200断言互补，不重复计数。 全部 `NOT_RUN`。

| 原案例 | 本模块必须保留的观察/拒绝条件 |
|---|---|
| PG03-A031–032 | T1本次维护/检查依据只支持该实例该活动；T2同型号但未知仍阻断，不永久标型号合格。 [原文:932](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/20/20b2dbf9b6670419__20b2dbf9b66704191955e894640c30de9f28a2103e4f4179abd7da313667f04f.md:932) |

**PG05 必要验收附件。** 12个跨页场景仍是验收设计；两项澄清按[准确APR](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/1c/1c6dc5f451727af6__APR-CP6-PLM-PG05-001.md:15)承接，不扩大到实施或真实业务批准。 全部 `NOT_RUN`。

| 原案例 | 本模块必须保留的观察/拒绝条件 |
|---|---|
| PG05-X010 | DC1要求逐次工具实例核验，T1本次已证不支持未知T2；原带条件快照不改为永久无条件。 [原文:291](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/f8/f8187487d223de16__f8187487d223de16d39b5717c08264e42964b0b4bb5083e40b3dea765bd13bc3.md:291) |

## 11. Required输入与退出条件

真实Owner schema/binding、current read与HoldExact能力、订单正式receipt/真实PE外部执行、全部旧writer清单、Office字段对照、物理provider/事务实现仍未证明。未来Owner规范若不满足此消费者保障，继续fail-closed并登记差异，不能以X1静态通过强迫Owner改变边界。[原文X:142](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/91/918bbd4508551b64__00_X1_R2_独立回归与完整范围静态审阅报告.md:142) [原文X:143](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/91/918bbd4508551b64__00_X1_R2_独立回归与完整范围静态审阅报告.md:143) [原文X:144](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/91/918bbd4508551b64__00_X1_R2_独立回归与完整范围静态审阅报告.md:144) [原文X:146](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/91/918bbd4508551b64__00_X1_R2_独立回归与完整范围静态审阅报告.md:146)

本地准备允许并不等于正式准确关系提交；绑好schema/恢复依赖必须启动新当前轮次，不能直接把历史待核状态改为CONFIRMED。[原文T:178](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/76/760c35b523260b23__ENG-TOOL-COMPATIBILITY.md:178) [原文X:153](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/91/918bbd4508551b64__00_X1_R2_独立回归与完整范围静态审阅报告.md:153)

## 12. 来源与阅读深度

SUP1–938、X1工装正文1–213、局部摘要规则1–24、独立静态审阅1–168本轮全文语义阅读；03_ENG1–3207全文用于共用技术身份。没有执行源脚本、业务AC、Host或Owner探测；静态代码事实仅按固定SHA原记录。全SHA和其余未读源在[reading](D:/CP6/docs/CP6_开发设计文档_20261010/evidence/platform-engineering-reading.json)，Owner相容合同见[contracts](D:/CP6/docs/CP6_开发设计文档_20261010/contracts/platform-engineering-contracts.json)。[原文T:1](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/76/760c35b523260b23__ENG-TOOL-COMPATIBILITY.md:1) [原文X:129](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/91/918bbd4508551b64__00_X1_R2_独立回归与完整范围静态审阅报告.md:129)

本轮已补齐该PG04附件的非重复业务语义：新增直接断言实读；原规则/过程/旧场景按已读主册精确文字复用并补读全部未匹配语义。表中取本模块相关动作；TRACE/字段ID/来源定位等仅结构核对，不称逐行独立全文，精确复用与未读行见台账。

**当前必要材料归并状态。** 本册为 `required_materials_consolidated`：选定正文、规范附件及准确接受/复核范围已按直接实读、结构化语义或精确复用归并。身份清单核对不等底层源码全文阅读；历史草稿、候选实现和Office/HTML视觉未核范围仍单列。可复查[逐件阅读与处置](D:/CP6/docs/CP6_开发设计文档_20261010/evidence/platform-engineering-reading.json)及[本组闭包台账](D:/CP6/docs/CP6_开发设计文档_20261010/worklogs/platform-engineering-progress.json)。当前必需规范未读清单为空；具体Owner采用、参数/物理接口、真实运行和本节已有来源缺口不因整理闭包而改变。
