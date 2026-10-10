# ERP-PRICE-01 板纸单价与行业价格参数

## 1. 目的、操作者和业务范围

Main ERP 维护 Standard／Estimate 两类行业板纸价，供查询与 EST 准确来源读取。导入员准备原表、维护员确认所选行、价格控制责任人停用／恢复来源版本；EST 是受信消费者。PRICE 不决定正式报价、销售成交价或财务估值，也不替客户、币种、UOM、产品和政策 Owner 建新主账。 [原文 L3](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/a1/a18753ac35341b9b__ERP-PRICE-R2.original.md:3>)、[原文 L20](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/a1/a18753ac35341b9b__ERP-PRICE-R2.original.md:20>)、[原文 L67](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/a1/a18753ac35341b9b__ERP-PRICE-R2.original.md:67>)

四条原 SPEC 为价格导入、批量维护、适用范围、估算引用。目标是让一个完整纸层构成、适用主体和依据日的价格能追到准确版本、真实元数据与当前控制；旧行仍可保留历史查询，但缺证不能生成可用于估算的 OWNER MATCH。 [原文 L13](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/a1/a18753ac35341b9b__ERP-PRICE-R2.original.md:13>)、[原文 L14](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/a1/a18753ac35341b9b__ERP-PRICE-R2.original.md:14>)、[原文 L15](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/a1/a18753ac35341b9b__ERP-PRICE-R2.original.md:15>)、[原文 L16](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/a1/a18753ac35341b9b__ERP-PRICE-R2.original.md:16>)、[原文 L148](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/a1/a18753ac35341b9b__ERP-PRICE-R2.original.md:148>)

## 2. 当前设计、组合与接受范围

当前采用 X1 R2 Markdown 静态详设，Stage 100。原四条 SPEC 完整保留；作者包内 STOPPED／PENDING、R1 RETURN 都是冻结历史，不覆盖后继 CURRENT。用户授权 root／COORD 于 2026-10-07T16:55:28.910726Z 接受，UserPersonallySigned=false；重建 UA 保存既有决定，不声称是丢失临时 UA 的同字节副本。 [原文 L4](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/e0/e0e004eebce1d702__CURRENT.json:4>)、[原文 L7](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/e0/e0e004eebce1d702__CURRENT.json:7>)

| 原件 | SHA-256 | 作用 | 入口 |
| --- | --- | --- | --- |
| 本目标正文 | a18753ac35341b9bba15837c1c12141b63eab0f26c364213bdf0f017ee690c2c | 四 SPEC 具体规范 | [原文 L1](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/a1/a18753ac35341b9b__ERP-PRICE-R2.original.md:1>) |
| X1 R2 独审 | 918bbd4508551b64470398bf65697954bff495fd37d1ef8cb5efa66e56bd0462 | 四根 STATIC PASS，无剩余 RETURN；生产采用不在结论内 | [原文 L1](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/91/918bbd4508551b64__00_X1_R2_独立回归与完整范围静态审阅报告.md:1>) |
| 当前接受 UA | 479b30dd13530c1ddb55b04026a61ec9600091af195e56cfdf2c358d6b208842 | 本目标四 SPEC 静态接受 | [原文 L1](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/47/479b30dd13530c1d__UA-20261008-X1-ERP-PRICE-R2.json:1>) |
| CURRENT | e0e004eebce1d7021b0e51650a5ee34d6b70e8ef197c27358a4c3bd00bbfee1c | 正文／独审／UA 准确选择 | [原文 L1](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/e0/e0e004eebce1d702__CURRENT.json:1>) |
| 本域摘要 | 7fe872fc8d85d676a13d314bf9679029c34bdb8942ce6ed10228a9d7743e9280 | 强制新本域序列化，不替外 Owner digest | [原文 L1](<D:/CP6-archives/consolidation-20261010/commercial-cache/x1/X1-R2/R2_LOCAL_DIGEST_RULES.md:1>) |
| 源与影响 | 0afa8b4b022cf9efbbf304b8b71b4051a8a330b565391568344cae229139b47f | 固定源码窗口及新旧接口差距 | [原文 L1](<D:/CP6-archives/consolidation-20261010/commercial-cache/x1/X1-R2/SOURCE-AND-IMPACT.md:1>) |
| 任务拆分 | 7c31188c14e093cbc8a0f4a78643536b04d7dc945c919541f624aae722c01e10 | PRICE 9／FSC 7 项，PLANNED_NOT_AUTHORIZED | [原文 L1](<D:/CP6-archives/consolidation-20261010/commercial-cache/x1/X1-R2/DEVELOPMENT-TASKS.json:1>) |

X1 全组三目标 14 SPEC、68 AC、21 任务；本商业两册分别 28／24 AC，全部 NOT_RUN。ENG 的六 SPEC 属原接受复用下有界兼容补充，本册不替其扩张接受。真实 Profile、模板、IAM、文件、事务、旧 writer 收敛及 Owner 采用仍 UNPROVEN；旧四根已静态关闭，不能把真实接入门再报告成作者尚未选择设计方案。 [原文 L11](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/91/918bbd4508551b64__00_X1_R2_独立回归与完整范围静态审阅报告.md:11>)、[原文 L12](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/91/918bbd4508551b64__00_X1_R2_独立回归与完整范围静态审阅报告.md:12>)、[原文 L13](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/91/918bbd4508551b64__00_X1_R2_独立回归与完整范围静态审阅报告.md:13>)、[原文 L15](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/91/918bbd4508551b64__00_X1_R2_独立回归与完整范围静态审阅报告.md:15>)、[原文 L133](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/91/918bbd4508551b64__00_X1_R2_独立回归与完整范围静态审阅报告.md:133>)、[原文 L158](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/91/918bbd4508551b64__00_X1_R2_独立回归与完整范围静态审阅报告.md:158>)、[原文 L164](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/91/918bbd4508551b64__00_X1_R2_独立回归与完整范围静态审阅报告.md:164>)

## 3. 实体、字段、金额精度与版本

| 旧字段 | 类型／上限 | 新规则 | 出处 |
| --- | --- | --- | --- |
| ImportDiv | enum Standard=1、Estimate=2 | 闭合枚举，类型不同永不互相覆盖；不可未识别值自动回Standard | [原文 L30](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/a1/a18753ac35341b9b__ERP-PRICE-R2.original.md:30>) |
| RevisionDate | DateTime | 保原值native；新输入DateOnly YYYY-MM-DD，所属Base业务日，不用服务器today代basisDate | [原文 L31](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/a1/a18753ac35341b9b__ERP-PRICE-R2.original.md:31>) |
| BaseCd | 非空string max10 | 准确Base并归属当前LegalEntity，保持大小写/代码，不以名称匹配 | [原文 L32](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/a1/a18753ac35341b9b__ERP-PRICE-R2.original.md:32>) |
| CustomerCd | string max20 | 必须可映射真实BP身份及作用域；代码显示原样，改码不能改历史身份 | [原文 L33](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/a1/a18753ac35341b9b__ERP-PRICE-R2.original.md:33>) |
| SheetFlute | string max4 | 当前受信楞型字典；未知为行错误，不能默填默认 | [原文 L34](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/a1/a18753ac35341b9b__ERP-PRICE-R2.original.md:34>) |
| PaperCdF/C/B、PrintCdF/C/B、EmbossCdF/C/B | 九字段string各max20 | 固定F/C/B位置；未知、空白、明确不适用分开，不能排序纸层或把空值通配 | [原文 L35](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/a1/a18753ac35341b9b__ERP-PRICE-R2.original.md:35>) |
| SalesStaffCd | string nullable max20 | 显示/责任归属，不是13字段业务键；需实际存在/作用域证据，不能冒客户映射 | [原文 L36](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/a1/a18753ac35341b9b__ERP-PRICE-R2.original.md:36>) |
| UnitPrice | decimal(15,4) | 接口十进制定点字符串，不用JS浮点；最多11位整数+4位小数；负价拒绝；零价必须zeroReason与价格Owner允许零价的政策版本 | [原文 L37](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/a1/a18753ac35341b9b__ERP-PRICE-R2.original.md:37>) |
| RowNo、Selected、SourceLine | UI/来源行字段 | 不可作数据库身份；SourceLine只追导入原行，选中集合明确提交 | [原文 L38](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/a1/a18753ac35341b9b__ERP-PRICE-R2.original.md:38>) |
| BaseName、CustomerName、SalesStaffName | 只读显示 | 由已授权准确源读取，不信任客户端作为主数据更新 | [原文 L39](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/a1/a18753ac35341b9b__ERP-PRICE-R2.original.md:39>) |

原 13 字段自然键为 RevisionDate、BaseCd、CustomerCd、SheetFlute 和九个 F/C/B 纸／印刷／压纹字段，表种类另外区分；SalesStaffCd 不是此键。原 Id/TenantId/RowVersion/软删/审计分别保留。新 LegalEntity、准确 BP/Product/UOM/Currency 不是旧代码串别名；同 scope＋完整构成只一 PriceIdentity，code→BP 映射须有唯一版本证据。 [原文 L41](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/a1/a18753ac35341b9b__ERP-PRICE-R2.original.md:41>)、[原文 L63](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/a1/a18753ac35341b9b__ERP-PRICE-R2.original.md:63>)

PriceScope包含真实Tenant、LegalEntityKey、BaseCd和ImportDiv。Tenant/Actor来自可信请求上下文；请求有LegalEntityKey供明确选择，必须与授权上下文一致。Base必须实际绑定法人，缺绑定为UNPROVEN。 [原文 L47](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/a1/a18753ac35341b9b__ERP-PRICE-R2.original.md:47>)

PriceIdentity持系统priceId、PriceScope、准确customerBpId、原customerCd、SheetFlute和九构成值。业务报价可按产品无关组合定义；productApplicability明确SPECIFIC_PRODUCT（准确productReference）或COMPOSITION_ONLY（有Owner政策证明产品维度NOT_APPLICABLE），不能从旧表没product列推所有产品通用。活动数量档同理：若原价不按数量分层，Owner必须明确数量无差别策略及适用范围，不能省略EST查询的quantities。 [原文 L49](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/a1/a18753ac35341b9b__ERP-PRICE-R2.original.md:49>)

PriceVersion包含priceId、versionNo（正整数的十进制字符串）、businessRevisionDate、unitPrice、currency、measureUnit、outputUnitApplicability、validFrom/validTo、完整scope/identity/构成快照、sourceMasterRefs、zeroReason、sourceImportRef或manualReason、createdAt/By、supersedesVersion、contentDigest。版号由服务产生，不复用日期或RowVersion。主业务体不可变；同一原日期订正也生成新Version，不覆盖已引用旧内容。新币种/单位必须有真实Owner版本及政策，旧表无证不能自动填JPY或m²。 [原文 L51](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/a1/a18753ac35341b9b__ERP-PRICE-R2.original.md:51>)

PriceControl独立持priceId、controlRevision、activeVersion集合/有效期裁决、state ACTIVE/RETIRED、decisionReason、policyVersion、updatedAt/By。RETIRED只停新采用，不删历史，不自动重算EST，不撤正式QTN。历史版本被当前Owner明确允许在原basisDate继续使用时可返回USABLE；有新版本不等于所有旧版撤销。跨日期重叠按§7规则，不隐含最大version获胜。 [原文 L53](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/a1/a18753ac35341b9b__ERP-PRICE-R2.original.md:53>)

LegacyPriceRef保原repo模型名、Tenant/Id（若可读）、完整13字段、rawRowVersion、rawContentDigest、originalImportDiv、observedAt和mappingStatus UNMAPPED/VERIFIED/CONFLICT。不把本轮新versionNumber塞入原native RowVersion；新canonicalDigest与旧rawDigest分列。 [原文 L55](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/a1/a18753ac35341b9b__ERP-PRICE-R2.original.md:55>)

ImportPreview包含previewId、Actor/Scope、文件原bytesDigest/文件名、parserVersion、formatVersion、预检generation、日期/ImportDiv、rows、masterReadRefs、当前头向量、expiresAt、status。每行含rowId、sourceLine、原单元格rawValues、规范值、fullKey、classification CREATE/REPLACE/NO_CHANGE/ERROR、expectedPriceHead或ABSENT、冲突明细和fieldErrors。临时预检是准备证据，不是价表写入。 [原文 L57](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/a1/a18753ac35341b9b__ERP-PRICE-R2.original.md:57>)

PriceCommand保commandKey、Actor/Scope、完整请求指纹、选中rowId集合/排序、原expected头、状态及终态回执。PriceChangeNoticeOutbox仅存EST原I04要求的SourceNotice、准确source/controlVersion/evidence、目的Owner、原bodyHash与投递状态。 [原文 L59](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/a1/a18753ac35341b9b__ERP-PRICE-R2.original.md:59>)

关键区别：versionNo 是服务生成的正整数十进制串，不是日期或 RowVersion；同日期纠正也生成不可变新 Version。Control 独立决定 ACTIVE／RETIRED 和允许版本／窗口；RETIRED 只停新采用，不删历史、不重算旧 EST、不撤 QTN。旧版本在原依据日是否仍可用，必须由当前 Owner 准确证明。 [原文 L51](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/a1/a18753ac35341b9b__ERP-PRICE-R2.original.md:51>)、[原文 L53](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/a1/a18753ac35341b9b__ERP-PRICE-R2.original.md:53>)、[原文 L150](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/a1/a18753ac35341b9b__ERP-PRICE-R2.original.md:150>)

### 3.1 R2 唯一元数据输入路径

原 Excel 保 13 列；P09 读取 Owner 版本化 Profile，操作者显式选择 profileRef，再仅补逐行允许的 productReference／zeroReason。Profile 是真实主数据与政策的只读组合，操作员不能在此编辑币种、单位、政策正文或给自己批准。无真实 Profile 返回 knowledge=UNPROVEN、profiles=[]，不填默认 JPY／m²。 [原文 L67](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/a1/a18753ac35341b9b__ERP-PRICE-R2.original.md:67>)、[原文 L69](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/a1/a18753ac35341b9b__ERP-PRICE-R2.original.md:69>)

P09 输入 legalEntityKey、baseCd、baseDate、importDiv 1/2、formatVersion；返回 contextId/contextDigest、可信 scope、日期／格式、actorPolicyRef、生成后 10 分钟 expiresAt、profiles、knowledge COMPLETE/INCOMPLETE/UNPROVEN 与 issues。ProfileRef={profileId,revision（正整数字符串）,digest（64 小写 hex）}；Snapshot 保 rawOwnerBody/ref、rawOwnerDigest、profileControlRevision、scope、允许格式、适用日期及以下全部政策。 [原文 L69](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/a1/a18753ac35341b9b__ERP-PRICE-R2.original.md:69>)、[原文 L71](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/a1/a18753ac35341b9b__ERP-PRICE-R2.original.md:71>)

| 必需信息 | 封闭字段／来源／nullable | 映射与错误 | 出处 |
| --- | --- | --- | --- |
| currency | Profile.currency={code:string,currencySourceRef:原Owner准确Ref,policySourceRef:原政策准确Ref}；全非null。Owner读取正文有code、版本、精度/舍入政策与当前头 | P09取得、用户选Profile；P01固定原body/refs。PriceVersion.currency及sourceMasterRefs取这里；CURRENCY_SOURCE_UNPROVEN拒绝READY | [原文 L75](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/a1/a18753ac35341b9b__ERP-PRICE-R2.original.md:75>) |
| measureUnit | Profile.measureUnit={unitKey:string,unitSourceRef:原Owner准确Ref,dimension:string,quantityScale:0..18整数}；全非null | 同币种，PriceVersion.measureUnit与真实UOM依赖固定；UOM_SOURCE_UNPROVEN。不能只传“㎡”显示串 | [原文 L76](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/a1/a18753ac35341b9b__ERP-PRICE-R2.original.md:76>) |
| outputUnitApplicability | Profile.outputUnitPolicy={mode:EXACT/NOT_APPLICABLE,unitSourceRef:准确Ref或null,policySourceRef:准确Ref,reason:string或null}。EXACT时unitSourceRef非null/reason=null；NOT_APPLICABLE时unitSourceRef=null/reason非空，政策原文必须支持 | 固定入PriceVersion.outputUnitApplicability；EST查询仍包含实际outputUnit，逐项核EXACT或受信不适用证据；OUTPUT_UNIT_POLICY_UNPROVEN | [原文 L77](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/a1/a18753ac35341b9b__ERP-PRICE-R2.original.md:77>) |
| 产品适用 | Profile.productPolicy={mode:SPECIFIC_PRODUCT/COMPOSITION_ONLY,policySourceRef:准确Ref,allowedProductSourceRefs:准确Ref[],reason:string或null}。SPECIFIC_PRODUCT至少一个允许准确Ref，reason=null；COMPOSITION_ONLY允许集为空且reason非空，政策证明对本scope/构成产品维度不适用 | 逐行supplement.productReference:准确Ref或null；SPECIFIC_PRODUCT必须在允许集逐字段匹配且当前有权；COMPOSITION_ONLY只能null，不省政策；映射PriceIdentity.productApplicability及Version快照；PRODUCT_POLICY_UNPROVEN/PRODUCT_SELECTION_REQUIRED | [原文 L78](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/a1/a18753ac35341b9b__ERP-PRICE-R2.original.md:78>) |
| 数量适用 | Profile.quantityPolicy={mode:ALL_POSITIVE/BOUNDED,policySourceRef:准确Ref,outputQuantityUnitRef:准确Ref,intervals:[{minInclusive:Dec,maxExclusive:Dec或null}],reason:string}。ALL_POSITIVE intervals=[]且有证理由；BOUNDED至少一段、不交叠、min≥0且max>min；null上界仅末段 | PriceVersion持完整政策原body/Ref。EST每一个活动数量档必须正数、单位相同并落入允许集；不把quantities省略。QUANTITY_POLICY_UNPROVEN/RATE_OUT_OF_QUANTITY_SCOPE | [原文 L79](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/a1/a18753ac35341b9b__ERP-PRICE-R2.original.md:79>) |
| validFrom/validTo | Profile.windowPolicy={policySourceRef:准确Ref,fromRule:BASE_DATE,toRule:OPEN_END/FIXED_END,fixedEnd:DateOnly或null}；OPEN_END fixedEnd=null，FIXED_END固定非null日期 | P01从明确BASE_DATE规则映射validFrom=本次baseDate，validTo=原fixedEnd或明确开放null，核from≤to及Profile适用窗口。不是隐式默认；WINDOW_POLICY_UNPROVEN/DATE_OUTSIDE_POLICY | [原文 L80](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/a1/a18753ac35341b9b__ERP-PRICE-R2.original.md:80>) |
| zeroReason与零价政策 | Profile.zeroPolicy={policySourceRef:准确Ref,allowZero:boolean}非null。逐行supplement.zeroReason:string或null，允许非空长度1..500（去首尾空白后），非零价格必须null | price=0且allowZero=true且reason非空才READY，PriceVersion.zeroReason/sourceMasterRefs固定；缺理由ZERO_REASON_REQUIRED，禁止零ZERO_PRICE_FORBIDDEN。政策Ref不由操作者手填 | [原文 L81](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/a1/a18753ac35341b9b__ERP-PRICE-R2.original.md:81>) |

metadataSelection={contextId,contextDigest,profileRef,rowSupplements[{rowKey,productReference,zeroReason}]}。Excel rowKey 为 L:物理行号，人工为 M:UUID rowId；重复／额外／不属于原文件的 key 均 ROW_SUPPLEMENT_MISMATCH。可省不需补充的行，解释为两个 null 后照常执行 Required，不代填产品或理由。每批一个 Profile，不同 Profile 必须用户明确拆批。 [原文 L85](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/a1/a18753ac35341b9b__ERP-PRICE-R2.original.md:85>)

补充不足时原预检 INVALID，填齐后携相同文件原 bytes 重新 P01 得新 previewId，不原地改 READY。不可变 supplementSnapshot 固定 metadataContext、完整 Profile 原 body/Ref/control、dependencyHeadVector、resolvedRows 的币种、UOM、输出／产品／数量／窗口／零价政策和实际补充值；diff 比较全部元数据与政策版本，旧值无证显示 UNKNOWN_TO_KNOWN。 [原文 L87](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/a1/a18753ac35341b9b__ERP-PRICE-R2.original.md:87>)、[原文 L89](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/a1/a18753ac35341b9b__ERP-PRICE-R2.original.md:89>)

P08输入只有legalEntityKey:string、baseCd:string、baseDate:DateOnly、importDiv:1|2、formatVersion:string、manualReason:string（1..500）、metadataSelection（§3.4）、rows:ManualPriceRow[]。rows非空，业务容量受同一文件/批量政策约束。ManualPriceRow恰含rowId:UUID、salesStaffCd:string(max20且非空)、customerCd:string(max20且非空)、sheetFlute:string(max4且非空)、paperCdF/printCdF/embossCdF/paperCdC/printCdC/embossCdC/paperCdB/printCdB/embossCdB九个非空max20字符串、unitPrice:Dec。rowId唯一；没有Selected、RowNo、SourceLine、RowVersion、BaseName或客户端Owner证据输入。RevisionDate/BaseCd从明确请求上下文取得，展示名称由服务读，源类型MANUAL与manualReason保存。真实P08字段、Profile和原13列共同进入同一READY/INVALID、完整键、权限、版本及P03提交机制，不能绕过P01新增必需信息。 [原文 L95](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/a1/a18753ac35341b9b__ERP-PRICE-R2.original.md:95>)

## 4. 预检、批次写入、适用选择和状态

导入流程保持 PA130 上传→预览→选择→确认→批更。新入口只宣称 xlsx；xls 原件保身份并报 UNSUPPORTED_FORMAT，不能改后缀或伪转换。文件读不执行宏、公式或外链。请求上限 50,000,000 bytes；展开大小、sheet/row/cell 等更严上限由 SYS-FILE 真实运行政策给出，缺配置拒绝。 [原文 L111](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/a1/a18753ac35341b9b__ERP-PRICE-R2.original.md:111>)、[原文 L114](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/a1/a18753ac35341b9b__ERP-PRICE-R2.original.md:114>)、[原文 L115](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/a1/a18753ac35341b9b__ERP-PRICE-R2.original.md:115>)

读取选定首工作表并返回 sheetName；13 列表头按原 RequiredColumns 完整顺序逐项校验，无表头仅显式获准 legacyFormatVersion 接收。逐行收集全部错误，不使一行坏值吞掉后续正确行。客户、Base、担当、楞型及九构成代码逐项核 Owner 的存在／作用域／生效日期；未证 MASTER_UNPROVEN。 [原文 L115](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/a1/a18753ac35341b9b__ERP-PRICE-R2.original.md:115>)、[原文 L116](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/a1/a18753ac35341b9b__ERP-PRICE-R2.original.md:116>)、[原文 L117](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/a1/a18753ac35341b9b__ERP-PRICE-R2.original.md:117>)、[原文 L118](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/a1/a18753ac35341b9b__ERP-PRICE-R2.original.md:118>)

文件内按完整 scope＋13 字段分组，同键即 DUPLICATE_ROW_KEYS 并列所有 sourceLine，即使同值也不自动去重或最后行获胜。既存提示采用同一完整键与准确 head，不把同楞型不同背纸误作覆盖。覆盖确认仅针对该旧 head 与新值；更换 Base／日期／ImportDiv／文件提高 pageGeneration 并失去旧可提交资格。 [原文 L119](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/a1/a18753ac35341b9b__ERP-PRICE-R2.original.md:119>)、[原文 L120](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/a1/a18753ac35341b9b__ERP-PRICE-R2.original.md:120>)、[原文 L121](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/a1/a18753ac35341b9b__ERP-PRICE-R2.original.md:121>)

Preview: PARSING→READY／INVALID；业务内容变更、master/control 头变化或到期为 STALE／EXPIRED。INVALID 整份不可挑好行提交，修正后重检；READY 可以少选合法行且固定准确 selectionDigest。Command 的 PREPARING 仅事务内，持久终态 COMMITTED／REJECTED；客户端 UNKNOWN 不是数据库拒绝。 [原文 L99](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/a1/a18753ac35341b9b__ERP-PRICE-R2.original.md:99>)、[原文 L101](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/a1/a18753ac35341b9b__ERP-PRICE-R2.original.md:101>)

所选集合 ALL_OR_NOTHING。按 scope／完整 identity 稳定顺序取得头保护，核当前 Actor、原 preview/supplement、期满、Profile／全部政策／master 当前头、原 expected head／ABSENT、重复键及金额规则，再将 Identity/Version、Control、旧字段审计、本域 History、CommandReceipt 和 SourceNotice outbox 一起 commit；未选行零写。政策头即使内容值未变也 PREVIEW_STALE、整批零写，不能偷偷替换 snapshot。 [原文 L91](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/a1/a18753ac35341b9b__ERP-PRICE-R2.original.md:91>)、[原文 L103](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/a1/a18753ac35341b9b__ERP-PRICE-R2.original.md:103>)

EST 候选先限 T/L/Base/Estimate，再核准确 BP、完整 F/C/B、basisDate、产品、币种、计价／输出单位和每一个活动数量档。明确规则 LATEST_EFFECTIVE_DATE_PER_FULL_KEY：取合法窗口中最大 businessRevisionDate≤basisDate，同日多版须 Control 唯一裁决；重叠／未知／歧义为 CONFLICT／UNKNOWN，不取首项或最大 GUID。未知 null 不通配，Standard 不作 Estimate fallback，不能无价默用 30。 [原文 L144](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/a1/a18753ac35341b9b__ERP-PRICE-R2.original.md:144>)、[原文 L146](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/a1/a18753ac35341b9b__ERP-PRICE-R2.original.md:146>)、[原文 L148](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/a1/a18753ac35341b9b__ERP-PRICE-R2.original.md:148>)

## 5. API、EST wire 与跨 Owner 合同

候选前缀 /api/main/v1/sheet-prices；旧 /api/sheet-unit-prices 保兼容映射。这些 P01–P09 是设计路由，不声称已部署。 [原文 L125](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/a1/a18753ac35341b9b__ERP-PRICE-R2.original.md:125>)

| 接口 | 封闭输入 | 输出／效果 | 出处 |
| --- | --- | --- | --- |
| P01 POST /imports/preview multipart | file、legalEntityKey、baseCd、baseDate、importDiv、formatVersion、metadataSelection（§3.4） | ImportPreviewRead，含previewId/digest/generation、rows及issues；不写价格 | [原文 L129](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/a1/a18753ac35341b9b__ERP-PRICE-R2.original.md:129>) |
| P02 GET /imports/{previewId} | 原id，Actor权限来自服务 | 原预检内容及currentStatus；原预检过期仍可有权看原值，不转READY | [原文 L130](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/a1/a18753ac35341b9b__ERP-PRICE-R2.original.md:130>) |
| P03 POST /batches | previewId、previewDigest、selectedRowIds、selectionDigest、reason；header Idempotency-Key | CommandReceipt；每行expected头取服务器原预检，不信任任意客户端替换 | [原文 L131](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/a1/a18753ac35341b9b__ERP-PRICE-R2.original.md:131>) |
| P04 GET /commands/{commandKey} | 原key | COMMITTED/REJECTED/UNKNOWN与可披露原receipt；404含义是当前未可见终态，不授权更换key | [原文 L132](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/a1/a18753ac35341b9b__ERP-PRICE-R2.original.md:132>) |
| P05 POST /query | legalEntityKey、baseCd、importDiv、basisDate、customerBpId或customerCd、salesStaffCd、page1..、pageSize1..100、sort CUSTOMER/COMPOSITION/DATE、direction | PriceQueryResult items/total/asOf；准确源、当前状态/旧未证分列 | [原文 L133](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/a1/a18753ac35341b9b__ERP-PRICE-R2.original.md:133>) |
| P06 GET /{priceId}/versions/{versionNo} | 准确id/version | 不可变PriceVersionRead及独立currentControl；不默换到latest | [原文 L134](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/a1/a18753ac35341b9b__ERP-PRICE-R2.original.md:134>) |
| P07 POST /{priceId}/control | expectedControlRevision、action RETIRE/RESTORE、reason、policyRef；Idempotency-Key/If-Match | 本域ControlReceipt；RESTORE需实际Owner重新裁决，不自动恢复旧EST绿色标识 | [原文 L135](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/a1/a18753ac35341b9b__ERP-PRICE-R2.original.md:135>) |
| P08 POST /rows/preview | §3.5的完整封闭输入及ManualPriceRow[]、同一metadataSelection | 人工批量维护复用同预检对象/约束；不直接绕开预检 | [原文 L136](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/a1/a18753ac35341b9b__ERP-PRICE-R2.original.md:136>) |

P09 POST /metadata-selections 见第 3 节；所有读 DTO 带 correlationId/asOf，错误为 code/messageKey/issues[{path,sourceLine,reason}]/retryAdvice。customer ID 与 code 同给必须一致；切新范围的旧 batch 缺预检、expected 版本或 commandKey 返回 409 COMPATIBILITY_UPGRADE_REQUIRED，服务端不能代生无法恢复的新 key。 [原文 L69](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/a1/a18753ac35341b9b__ERP-PRICE-R2.original.md:69>)、[原文 L138](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/a1/a18753ac35341b9b__ERP-PRICE-R2.original.md:138>)

| 合同 | 原精确类型与行为 | 出处 |
| --- | --- | --- |
| I-P01 → EST I01 OwnerDirectory.Query | 保完整 OwnerQuery/LookupCriteria＋可信 T/L/Base；SourceRef={owner,id,version,digest,purpose}、EvidenceRef={owner,id,digest} 不改；RateValue amount/currency/measureUnit/selectorDigest/validFrom/validTo/zeroReason 来自准确 Version。COMPLETE 与 MATCH、USABLE 各分轴，缺任一维 UNKNOWN 且 sourceEvidence=null。 | [原文 L154](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/a1/a18753ac35341b9b__ERP-PRICE-R2.original.md:154>) [原文 L156](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/a1/a18753ac35341b9b__ERP-PRICE-R2.original.md:156>) |
| 完整 selector evidence | 覆盖版本全体、原 criteria 中产品准确 Ref／全部数量档、T/L/Base、币种/UOM及政策、逐项不适用理由、controlRevision/watermark/asOf；用原 EST-SELECTOR-1，不另造三字段 hash；多个候选由用户明确选。 | [原文 L158](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/a1/a18753ac35341b9b__ERP-PRICE-R2.original.md:158>) |
| I-P02 → EST I02 CurrentEstimateSourceParticipant | 原 Binding、source、operation BIND/CALCULATE/SEAL/ADOPT、expected control、intent 及冲突证据；实际同 Main 事务锁 PRICE 控制与依赖。原 OwnerGuardReceipt 的 source 精确相同、operation/intent 相同且 heldUntilTransactionEnd=true；跨库不能同参加则失败关闭。 | [原文 L160](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/a1/a18753ac35341b9b__ERP-PRICE-R2.original.md:160>) |
| I-P03 → EST I04 SourceNotice | 版本／Control 与 notice outbox 同事务；稳定 eventId＋原 body。RECORDED/DUPLICATE 只是知识影响；异 body CONFLICT，超时原事件重投。EST 增 observation generation、置 UNKNOWN 再查当前头，旧 controlVersion 字符串不能排序当许可。 | [原文 L162](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/a1/a18753ac35341b9b__ERP-PRICE-R2.original.md:162>) |

有意能力差异：PRICE 本域 quantityScale 0..18 不扩张 EST UnitValue 0..12；原 EST 精确类型继续生效，不能截断或默认。 [原文 L151](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/91/918bbd4508551b64__00_X1_R2_独立回归与完整范围静态审阅报告.md:151>)

EST 后继通用材料 R2 的 PRICE v2 仅支持有真实 coveragePolicy 的整张覆盖；本 X1 R2 主文消费原 EST wire，不自动证明已经具备后继 v2 完整 criteria＋roles 签署／SourceMember／最终保护。编码接缝须保持这项有意版本差异，不能让旧 OWNER MATCH 穿过新 coverage 门。 [原文 L326](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/c4/c48a0460236a9a6e__ERP-EST-01_GENERAL_MATERIAL_SUCCESSOR_R2.md:326>)、[原文 L339](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/c4/c48a0460236a9a6e__ERP-EST-01_GENERAL_MATERIAL_SUCCESSOR_R2.md:339>)

## 6. 幂等、并发、摘要与恢复

命令唯一 (Tenant,LegalEntity,Actor,commandKey)；同规范 body 回原终态，异 body KEY_REUSED_WITH_DIFFERENT_INTENT。原 terminal 查询重核当前披露权；回相同 businessReceipt/receiptHash，replayed 仅封套。NO_CHANGE 有明确回执，但无新版本／notice。原预检文件必须先稳定可读，外部文件和 DB 不是同一事务；锁后不能维持真实 Owner 当前性到 commit 则 OWNER_PARTICIPANT_UNPROVEN。 [原文 L63](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/a1/a18753ac35341b9b__ERP-PRICE-R2.original.md:63>)、[原文 L105](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/a1/a18753ac35341b9b__ERP-PRICE-R2.original.md:105>)、[原文 L107](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/a1/a18753ac35341b9b__ERP-PRICE-R2.original.md:107>)

所有新本域摘要采用 SHA-256：UTF-8 JSON 恰含 domain、schemaVersion="X1-R2-1"、payload；键按 Unicode 码点排序，声明成员不可省略，nullable 写 null，未知成员拒绝，无非必要空白。字符串保原 Unicode，不自行全半角／大小写归一化；整数用规范十进制字符串；Dec 无浮点、指数或正号，零为 0、去多余尾零但仍受业务精度约束。DateOnly／UTC RFC3339 按各字段定义，原 native 日期另存。 [原文 L3](<D:/CP6-archives/consolidation-20261010/commercial-cache/x1/X1-R2/R2_LOCAL_DIGEST_RULES.md:3>)、[原文 L5](<D:/CP6-archives/consolidation-20261010/commercial-cache/x1/X1-R2/R2_LOCAL_DIGEST_RULES.md:5>)

| domain | 完整 payload 与顺序 | 出处 |
| --- | --- | --- |
| ERPPRICE_METADATA_SELECTION | P09原scope/Actor策略、baseDate/formatVersion、contextId/expiresAt、完整Profiles原Ref/body保留引用；profiles按profileId/revision；每Profile允许产品Ref按其完整canonical序；数量区间按min升序 | [原文 L9](<D:/CP6-archives/consolidation-20261010/commercial-cache/x1/X1-R2/R2_LOCAL_DIGEST_RULES.md:9>) |
| ERPPRICE_PREVIEW | previewId、完整Actor/Scope、原文件bytesDigest或P08 manualReason+完整输入行、parser/formatVersion、预检generation、日期/ImportDiv、完整supplementSnapshot、masterReadRefs/当前头向量、所有解析行及错误、expiresAt；排除previewDigest自身及后续可变currentStatus。Excel行按物理sourceLine，人工行按rowId；错误按rowKey/path/code | [原文 L10](<D:/CP6-archives/consolidation-20261010/commercial-cache/x1/X1-R2/R2_LOCAL_DIGEST_RULES.md:10>) |
| ERPPRICE_BATCH_INTENT | 完整P03封闭body加可信T/L/Actor与目标动作；selectedRowIds是集合按rowId升序，不能重复；previewDigest外还保原preview全文证据，不凭hash重建内容 | [原文 L11](<D:/CP6-archives/consolidation-20261010/commercial-cache/x1/X1-R2/R2_LOCAL_DIGEST_RULES.md:11>) |
| ERPPRICE_SELECTION | 原previewId/digest和排序后的selectedRowIds；不含未选行的新意图 | [原文 L12](<D:/CP6-archives/consolidation-20261010/commercial-cache/x1/X1-R2/R2_LOCAL_DIGEST_RULES.md:12>) |
| ERPPRICE_VERSION_CONTENT | PriceVersion全部不可变业务成员（含明确Profile政策原Ref/body引用及原rawDigest、来源/createdAt/By）；排除contentDigest自身、独立PriceControl和当前观察 | [原文 L13](<D:/CP6-archives/consolidation-20261010/commercial-cache/x1/X1-R2/R2_LOCAL_DIGEST_RULES.md:13>) |

不可变本域回执用 X1_LOCAL_COMMAND_RECEIPT，排除自身 digest/hash；封套 replayed 或当前披露投影不改原回执。原文件 digest 对原 bytes，外 Owner Ref/proof/rawDigest 保其原规范；本域 hash 不能证明 Producer 真实性或真实持锁。 [原文 L22](<D:/CP6-archives/consolidation-20261010/commercial-cache/x1/X1-R2/R2_LOCAL_DIGEST_RULES.md:22>)、[原文 L24](<D:/CP6-archives/consolidation-20261010/commercial-cache/x1/X1-R2/R2_LOCAL_DIGEST_RULES.md:24>)

| 错误 | 已知效果 | 恢复 | 出处 |
| --- | --- | --- | --- |
| 400 MALFORMED_FILE / UNSUPPORTED_FORMAT | 零价表写 | 修文件或选明示格式，再预检 | [原文 L168](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/a1/a18753ac35341b9b__ERP-PRICE-R2.original.md:168>) |
| 422 ROW_INVALID / MASTER_UNPROVEN / DUPLICATE_ROW_KEYS | 预检INVALID、逐行原因 | 由对应Owner补数据或修原文件；不自动填默认 | [原文 L169](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/a1/a18753ac35341b9b__ERP-PRICE-R2.original.md:169>) |
| 403 SCOPE_OR_FIELD_FORBIDDEN | 不泄漏不可读旧价 | 换获准范围；不因管理员身份跳过业务Owner | [原文 L170](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/a1/a18753ac35341b9b__ERP-PRICE-R2.original.md:170>) |
| 409 PREVIEW_STALE / PRICE_HEAD_CHANGED | 所选全部零提交 | 回读差异、新预检与重新确认；保原拒绝回执 | [原文 L171](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/a1/a18753ac35341b9b__ERP-PRICE-R2.original.md:171>) |
| 409 KEY_REUSED_WITH_DIFFERENT_INTENT | 保原命令不变 | 改正客户端意图管理，不覆盖原Key | [原文 L172](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/a1/a18753ac35341b9b__ERP-PRICE-R2.original.md:172>) |
| 409 PRICE_AMBIGUOUS / SOURCE_CONFLICT | 不产生MATCH/Guard成功 | Owner裁决确切版本及范围；保双方原数据 | [原文 L173](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/a1/a18753ac35341b9b__ERP-PRICE-R2.original.md:173>) |
| 503 SOURCE_UNKNOWN / OWNER_PARTICIPANT_UNPROVEN | 不造新源采用 | 可保存准备内容，待真实Owner恢复后原意图再核 | [原文 L174](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/a1/a18753ac35341b9b__ERP-PRICE-R2.original.md:174>) |
| 503 COMMIT_UNKNOWN | 效果未知 | 保原key/body查P04；不能换key补交或宣称零写 | [原文 L175](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/a1/a18753ac35341b9b__ERP-PRICE-R2.original.md:175>) |
| OUTBOX送达未知 | 原价格提交事实不反转 | 重投原event、业务来源当前查证；不重造价版本 | [原文 L176](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/a1/a18753ac35341b9b__ERP-PRICE-R2.original.md:176>) |

COMMIT_UNKNOWN 保原 key/body 查 P04；普通 404 只是当前未可见终态，不授权换 key。outbox 送达未知不反转已提交价格；只能原 event/body 重投，不新造 Version。 [原文 L132](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/a1/a18753ac35341b9b__ERP-PRICE-R2.original.md:132>)、[原文 L175](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/a1/a18753ac35341b9b__ERP-PRICE-R2.original.md:175>)、[原文 L176](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/a1/a18753ac35341b9b__ERP-PRICE-R2.original.md:176>)

## 7. 权限和敏感价格

import 仅预检、edit 仅普通选中提交；更正／停用需 Main ERP 独立控制能力，不能从 edit 默认继承。服务核 Tenant/LegalEntity/Base/客户范围、动作和逐字段 read/write；保完整 Version 的提交必须对全部所选行完整读写有权。隐藏价不得靠返回可猜 hash 泄漏；撤写保读可查历史，撤读则不返回原价／回执敏感部分。 [原文 L20](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/a1/a18753ac35341b9b__ERP-PRICE-R2.original.md:20>)、[原文 L22](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/a1/a18753ac35341b9b__ERP-PRICE-R2.original.md:22>)

## 8. 页面和交互

三个区为上下文、可展开预检行 diff／问题、命令结果。显示 Standard/Estimate、日期、币种/UOM、全构成、旧新价、版本／Control 与所选数；Profile 和逐行产品／零价理由可见且准确关联。已提交按钮绑定原 key，刷新回查；本地不长期存受限完整价格。迟到响应必须匹配 pageGeneration/Scope，取消确认零写，清空仅本地选择，不能取消已提交业务。NO_CHANGE 和 REPLACE 分开，UNPROVEN 不显示绿色“可报价”。 [原文 L85](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/a1/a18753ac35341b9b__ERP-PRICE-R2.original.md:85>)、[原文 L87](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/a1/a18753ac35341b9b__ERP-PRICE-R2.original.md:87>)、[原文 L89](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/a1/a18753ac35341b9b__ERP-PRICE-R2.original.md:89>)、[原文 L140](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/a1/a18753ac35341b9b__ERP-PRICE-R2.original.md:140>)

## 9. 实现次序、固定源码差异与切换

固定 CP6 SHA=90c871fe571fd6b390f53e8678376d7ce60bcb60；下面是归档已读源码事实，未刷新今日 HEAD，未执行代码。Search 和 BatchUpdate 都比较完整 13 字段，不能称旧实现仅三字段；简化的是既存重复提示。BaseBizEntity 已有 RowVersion，缺的是客户端原期望版本／持久原命令恢复。 [原文 L15](<D:/CP6-archives/consolidation-20261010/commercial-cache/x1/X1-R2/SOURCE-AND-IMPACT.md:15>)、[原文 L19](<D:/CP6-archives/consolidation-20261010/commercial-cache/x1/X1-R2/SOURCE-AND-IMPACT.md:19>)、[原文 L42](<D:/CP6-archives/consolidation-20261010/commercial-cache/x1/X1-R2/SOURCE-AND-IMPACT.md:42>)

| 代号 | 原固定源码窗口 | 事实／限制 | 出处 |
| --- | --- | --- | --- |
| P-SVC | CP6.Core/Services/Erp/SheetUnitPriceService.cs L40–88、93–239、246–309 | 查询分组和批量写为完整组合键；导入预检客户存在；既存重复提示简化；批量当前行更新 | [原文 L19](<D:/CP6-archives/consolidation-20261010/commercial-cache/x1/X1-R2/SOURCE-AND-IMPACT.md:19>) |
| P-DTO | CP6.Entity/DTOs/Erp/SheetUnitPriceDto.cs 全文 | Standard=1/Estimate=2；现请求无命令键、expected revision、币种/单位 | [原文 L20](<D:/CP6-archives/consolidation-20261010/commercial-cache/x1/X1-R2/SOURCE-AND-IMPACT.md:20>) |
| P-MODEL | CP6.Entity/DomainModels/Erp/SheetUnitPrice.cs 全文 | 两表同构；15,4价格；13字段自然键属性，继承BaseBizEntity | [原文 L21](<D:/CP6-archives/consolidation-20261010/commercial-cache/x1/X1-R2/SOURCE-AND-IMPACT.md:21>) |
| P-API | CP6.WebApi/Controllers/Erp/SheetUnitPriceController.cs 全文 | list/import/batch-update；import/edit权限；50,000,000请求限额 | [原文 L22](<D:/CP6-archives/consolidation-20261010/commercial-cache/x1/X1-R2/SOURCE-AND-IMPACT.md:22>) |
| P-UI | cp6.web/src/views/erp/SheetUnitPriceView.vue L1–235；api/erp/sheetUnitPrice.ts；types/erp/sheetUnitPrice.ts | 上传/参照双模式、选择行、覆盖确认、全部行提交；xls与xlsx选择项 | [原文 L23](<D:/CP6-archives/consolidation-20261010/commercial-cache/x1/X1-R2/SOURCE-AND-IMPACT.md:23>) |
| EST-SVC | CP6.Core/Services/Erp/EstimateCalcService.cs L239–285 | 旧计算fallback30和部分纸构成最近价；使用DateTime.Today | [原文 L24](<D:/CP6-archives/consolidation-20261010/commercial-cache/x1/X1-R2/SOURCE-AND-IMPACT.md:24>) |
| EST-ACCEPTED | 原Library libfile_78ad77ee3f008191b4ec7de8f6d1f808，原SHA f7d7141…29f03 | §5 L289–437完整Ref/Lookup/Owner读取/最终事务；L507 selector；L521材料模式；L688–692旧源映射；L15198–15199 AC018/019 | [原文 L25](<D:/CP6-archives/consolidation-20261010/commercial-cache/x1/X1-R2/SOURCE-AND-IMPACT.md:25>) |
| DB-CONTEXT | CP6.Core/EFDbContext/CP6Context.cs L255–270、1621–1665 | FSC管理号唯一索引；价格13字段唯一索引；工程No+Rev唯一索引；不等实例迁移已执行 | [原文 L35](<D:/CP6-archives/consolidation-20261010/commercial-cache/x1/X1-R2/SOURCE-AND-IMPACT.md:35>) |
| DB-MIG | 20260502225006_AddBpAndFscPA110.cs；20260506103852_AddSheetPriceAndPlateMold.cs | 前者建FSC表；后者Up/Down空，不能仅凭该文件名证明建价表/版模表 | [原文 L36](<D:/CP6-archives/consolidation-20261010/commercial-cache/x1/X1-R2/SOURCE-AND-IMPACT.md:36>) |
| HOST | CP6.WebApi/Program.cs L582–590、665；cp6.web/src/router/index.ts L102–105、165 | 三ERP服务DI、ERP PE注入NoOp；WMS独立DI；四ERP旧路由与独立WMS路由 | [原文 L37](<D:/CP6-archives/consolidation-20261010/commercial-cache/x1/X1-R2/SOURCE-AND-IMPACT.md:37>) |
| T-AUD | CP6.Tests/Erp/ErpAuditTests.cs L130–143、199–252 | 已有price审计、工程DecisionAmount审计、FSC追加无字段审计负测试文本 | [原文 L38](<D:/CP6-archives/consolidation-20261010/commercial-cache/x1/X1-R2/SOURCE-AND-IMPACT.md:38>) |
| T-PERM | CP6.Tests/ErpPermissionAttributeTests.cs L55–70；ErpPermissionSeedTests.cs L37–40 | 标签纯读豁免、导入/edit/issue等种子文本；不等真实用户权限证实 | [原文 L39](<D:/CP6-archives/consolidation-20261010/commercial-cache/x1/X1-R2/SOURCE-AND-IMPACT.md:39>) |
| T-EST | CP6.Tests/EstimateCalcServiceTests.cs；EstimateCalcRegressionTests.cs | 旧计算及输入行为测试文本；未找到本轮完整选择条件/新命令回执业务执行证据 | [原文 L40](<D:/CP6-archives/consolidation-20261010/commercial-cache/x1/X1-R2/SOURCE-AND-IMPACT.md:40>) |

| 任务 | 目的 | 准确交付 | 依赖 | 真实采用门 | 出处 |
| --- | --- | --- | --- | --- | --- |
| X1-P01 | 完善价格文件预检 | 明示xlsx格式/表头政策；逐行全部错误；完整键重复；不执行公式；文件限制及原bytes证据；PreviewRead DTO；R2同根细化：PRICE §3.4/3.5、P01/P09：原13列+Profile精确选择+逐行rowSupplements；初次缺补充返回INVALID，新preview重检；不扩列/不默认 | X1-P02 | 真实母版及SYS-FILE资源政策；未核单元格保留 | [原文 L19](<D:/CP6-archives/consolidation-20261010/commercial-cache/x1/X1-R2/DEVELOPMENT-TASKS.json:19>) |
| X1-P02 | 接入价格主数据读取 | 准确BP/字典/单位/币种与政策Ref；NOT_APPLICABLE逐项证据；未证行闭门；安全读DTO；R2同根细化：实现MetadataSelectionRead/ProfileSnapshot准确原Ref/body、货币/UOM/输出单位/产品/数量/窗口/零价政策读取；缺配置UNPROVEN | — | BP、UOM/Currency、IAM与各master Owner实际read合同；真实版本化Price Metadata Profile及政策原文映射 | [原文 L29](<D:/CP6-archives/consolidation-20261010/commercial-cache/x1/X1-R2/DEVELOPMENT-TASKS.json:29>) |
| X1-P03 | 建立价格版本回执 | 本域模型/约束设计落实；CAS/缺行竞争；ALL_OR_NOTHING；审计History/Receipt/Outbox一次commit；原key查回；R2同根细化：同一supplementSnapshot固定至Version；最终Profile及全部依赖头改变即STALE全批零写；原receipt不改 | X1-P01, X1-P02 | 数据库实际事务与唯一性、当前身份权限、持久Receipt能力 | [原文 L83](<D:/CP6-archives/consolidation-20261010/commercial-cache/x1/X1-R2/DEVELOPMENT-TASKS.json:83>) |
| X1-P04 | 落实准确价格查询 | P05/P06/P07；同日歧义拒绝；三种知识状态；无默认币种单位；旧版本适用证明；R2同根细化：产品/数量/输出单位政策与窗口准确进入Identity/Version查询；不适用需原政策证据 | X1-P02, X1-P03 | 价格Owner零价/适用/生效政策版本 | [原文 L119](<D:/CP6-archives/consolidation-20261010/commercial-cache/x1/X1-R2/DEVELOPMENT-TASKS.json:119>) |
| X1-P05 | 适配估算价格读取 | 完整LookupCriteria/selectorDigest；准确SourceRef/RateValue/Proof；禁旧partial latest降级；UNPROVEN分支；R2同根细化：EST RATE候选取精确Version元数据及原依赖Ref；P01生产端补充与完整selector消费端一致 | X1-P04 | ERP-EST原件准确合同采用与真实信任证明；不重写EST已接受正文 | [原文 L153](<D:/CP6-archives/consolidation-20261010/commercial-cache/x1/X1-R2/DEVELOPMENT-TASKS.json:153>) |
| X1-P06 | 接通估算最终参与 | OwnerGuardRequest/Receipt原shape；持锁至commit；原SourceNotice Outbox/恢复；不跨库假原子 | X1-P03, X1-P05 | 真实同Main事务与消费Owner参加；跨库未证关闭新动作 | [原文 L184](<D:/CP6-archives/consolidation-20261010/commercial-cache/x1/X1-R2/DEVELOPMENT-TASKS.json:184>) |
| X1-P07 | 完成价格双模式交互 | 完整构成/币种单位/版本/diff；所有错误和选择；generation防晚到；原命令持有/恢复；状态诚实显示；R2同根细化：增加P09准确Profile选择及逐行产品Ref/零价理由；P08封闭ManualPriceRow；diff显示政策/单位版本变化 | X1-P01, X1-P03, X1-P04 | 真实接口/权限选项；不从前端生成生产回执 | [原文 L211](<D:/CP6-archives/consolidation-20261010/commercial-cache/x1/X1-R2/DEVELOPMENT-TASKS.json:211>) |
| X1-P08 | 收敛价格存量入口 | 逐入口Owner清单；T/L/Base/ImportDiv切换；LegacyRef与new canonical分离；旧写拒绝/单writer；回退只停新写 | X1-P03, X1-P05, X1-P06, X1-P07 | 实际全writer清单与物理迁移方案，空具名迁移不当已建表证明 | [原文 L248](<D:/CP6-archives/consolidation-20261010/commercial-cache/x1/X1-R2/DEVELOPMENT-TASKS.json:248>) |
| X1-P09 | 执行价格验收回归 | 实现28项AC及保留旧测试；真实relational并发/恢复/权限/新旧入口和UI；报告NOT_RUN/失败/通过分别 | X1-P01, X1-P02, X1-P03, X1-P04, X1-P05, X1-P06, X1-P07, X1-P08 | 不得用本轮文书或历史测试文本冒新执行证据 | [原文 L276](<D:/CP6-archives/consolidation-20261010/commercial-cache/x1/X1-R2/DEVELOPMENT-TASKS.json:276>) |

按 T/L/Base/ImportDiv 登记 LEGACY／SHADOW_READ／NEW_WRITER；影子只读不双写。新 writer 前收敛旧 API、内部 BatchUpdate、旧 Calculate 直表读取、作业／运维／手工 DB／客户端；旧旁路仍可覆盖则不能切换。存量不可默补 JPY/m²/ACTIVE；新版本已消费后回退只停新动作／保历史读，不能覆盖旧价或删 receipt/notice。 [原文 L180](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/a1/a18753ac35341b9b__ERP-PRICE-R2.original.md:180>)、[原文 L182](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/a1/a18753ac35341b9b__ERP-PRICE-R2.original.md:182>)

## 10. 原验收场景

以下 28 项全部 **NOT_RUN**，保留原 SPEC 和预期，不是本轮运行结果。

| AC | 场景与预期 | SPEC | 出处 |
| --- | --- | --- | --- |
| AC-P01 | 合法原13列xlsx+显式有效Profile及所需逐行补充得READY；currency/UOM原Ref/政策body/日期/Base/ImportDiv准确固定，价表零写；P08同字段同结果 | 01 | [原文 L190](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/a1/a18753ac35341b9b__ERP-PRICE-R2.original.md:190>) |
| AC-P02 | xls、加密或损坏文件返回明确不支持/格式错误，不假xlsx成功 | 01 | [原文 L191](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/a1/a18753ac35341b9b__ERP-PRICE-R2.original.md:191>) |
| AC-P03 | 表头交换/缺列、单价超15,4/负值逐行报错；零价无zeroReason为ZERO_REASON_REQUIRED，禁止零价政策不因有理由放行；后续行仍可定位 | 01 | [原文 L192](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/a1/a18753ac35341b9b__ERP-PRICE-R2.original.md:192>) |
| AC-P04 | Base/担当/楞型/任一九构成码未证，INVALID且无假默认 | 01 | [原文 L193](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/a1/a18753ac35341b9b__ERP-PRICE-R2.original.md:193>) |
| AC-P05 | 同日期客户楞型、背纸不同不标同键覆盖；完整同键列精确旧head | 01 | [原文 L194](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/a1/a18753ac35341b9b__ERP-PRICE-R2.original.md:194>) |
| AC-P06 | 文件内完整同键重复列全sourceLine，拒绝自动最后行胜出 | 01 | [原文 L195](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/a1/a18753ac35341b9b__ERP-PRICE-R2.original.md:195>) |
| AC-P07 | 上传后切ImportDiv或Base，迟到旧预检不能成为当前可提交 | 01 | [原文 L196](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/a1/a18753ac35341b9b__ERP-PRICE-R2.original.md:196>) |
| AC-P08 | 仅选2/3合法行提交，恰两行效果及回执，未选零写 | 02 | [原文 L197](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/a1/a18753ac35341b9b__ERP-PRICE-R2.original.md:197>) |
| AC-P09 | 任一selected价格头或Profile/货币/UOM/政策头变化（即使值相同），全批PREVIEW_STALE零写，不替换旧snapshot；新选择重检后新意图新key | 02 | [原文 L198](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/a1/a18753ac35341b9b__ERP-PRICE-R2.original.md:198>) |
| AC-P10 | 同key同body回同原receipt；异body拒绝且原结果不变 | 02 | [原文 L199](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/a1/a18753ac35341b9b__ERP-PRICE-R2.original.md:199>) |
| AC-P11 | 提交返回未知，原key回读唯一效果，不按超时断言失败 | 02 | [原文 L200](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/a1/a18753ac35341b9b__ERP-PRICE-R2.original.md:200>) |
| AC-P12 | NO_CHANGE有回执，无新Version/SourceNotice | 02 | [原文 L201](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/a1/a18753ac35341b9b__ERP-PRICE-R2.original.md:201>) |
| AC-P13 | 当前撤edit保read可查原结果；撤read不泄漏价格/原receipt | 02 | [原文 L202](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/a1/a18753ac35341b9b__ERP-PRICE-R2.original.md:202>) |
| AC-P14 | 价格版本、Control、审计、Command与outbox原子；任一失败不半提交 | 02 | [原文 L203](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/a1/a18753ac35341b9b__ERP-PRICE-R2.original.md:203>) |
| AC-P15 | Standard与Estimate同组合并存且不互覆盖，EST仅选明示Estimate | 03 | [原文 L204](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/a1/a18753ac35341b9b__ERP-PRICE-R2.original.md:204>) |
| AC-P16 | 原date保持，basisDate边界选明确适用版本，不用服务器today | 03 | [原文 L205](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/a1/a18753ac35341b9b__ERP-PRICE-R2.original.md:205>) |
| AC-P17 | 同日多有效版本或范围重叠无明确裁决返回CONFLICT | 03 | [原文 L206](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/a1/a18753ac35341b9b__ERP-PRICE-R2.original.md:206>) |
| AC-P18 | 币种/单位准确版本或BP映射缺证返回UNPROVEN；P09无Profile不填默认，P01/P08缺依赖不READY；真实UOM后继不能默代原Ref | 03 | [原文 L207](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/a1/a18753ac35341b9b__ERP-PRICE-R2.original.md:207>) |
| AC-P19 | COMPOSITION_ONLY、outputUnit不适用、ALL_POSITIVE均需原政策证据，null不通配；SPECIFIC_PRODUCT缺产品Ref/越允许集、BOUNDED档外或单位不一致拒绝 | 03 | [原文 L208](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/a1/a18753ac35341b9b__ERP-PRICE-R2.original.md:208>) |
| AC-P20 | 停用不删除旧版本/历史价，旧准确版本可用性由当前Owner单独证明 | 03 | [原文 L209](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/a1/a18753ac35341b9b__ERP-PRICE-R2.original.md:209>) |
| AC-P21 | 完整F/C/B/印刷压纹匹配，差一项不得选入；对应EST AC018 | 04 | [原文 L210](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/a1/a18753ac35341b9b__ERP-PRICE-R2.original.md:210>) |
| AC-P22 | 客户/产品准确Ref/构成/数量档变动令原selector失效；对应EST AC019 | 04 | [原文 L211](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/a1/a18753ac35341b9b__ERP-PRICE-R2.original.md:211>) |
| AC-P23 | 旧表缺完整单位币种来源不能计算OWNER rate，EST显式假设走原合同 | 04 | [原文 L212](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/a1/a18753ac35341b9b__ERP-PRICE-R2.original.md:212>) |
| AC-P24 | Guard最终覆盖原Binding、source、operation、intent并持有到实际事务结束 | 04 | [原文 L213](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/a1/a18753ac35341b9b__ERP-PRICE-R2.original.md:213>) |
| AC-P25 | 只发缓存proof/不能参与实际事务，拒绝新采用，历史可读 | 04 | [原文 L214](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/a1/a18753ac35341b9b__ERP-PRICE-R2.original.md:214>) |
| AC-P26 | SourceNotice重复不重复影响、异body隔离；迟到成功不覆盖较新UNKNOWN | 04 | [原文 L215](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/a1/a18753ac35341b9b__ERP-PRICE-R2.original.md:215>) |
| AC-P27 | 被引用旧EST Run/Version数值不因改价被改写，正式QTN决定不自动变更 | 04 | [原文 L216](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/a1/a18753ac35341b9b__ERP-PRICE-R2.original.md:216>) |
| AC-P28 | 新writer范围旧batch或旧Calculate旁路未收敛时切换保持关闭 | 04 | [原文 L217](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/a1/a18753ac35341b9b__ERP-PRICE-R2.original.md:217>) |

## 11. 具体未证门与退出条件

真实币种/UOM/BP/master、Profile 全政策与原 13 列母版、零价批准、生效唯一裁决、全 writer、Main 同事务 participant、IAM/文件资源限制必须逐个取得准确版本；没有则相应预检、new writer／OWNER 采用继续关闭。它们是已定义真实输入门，X1-R01 的传递路径设计已关闭。 [原文 L223](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/a1/a18753ac35341b9b__ERP-PRICE-R2.original.md:223>)

真实 Owner 合同不满足消费条件时继续失败关闭并做有界兼容修订；不能以本次静态 PASS 压过 Owner 事实。 [原文 L137](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/91/918bbd4508551b64__00_X1_R2_独立回归与完整范围静态审阅报告.md:137>)、[原文 L138](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/91/918bbd4508551b64__00_X1_R2_独立回归与完整范围静态审阅报告.md:138>)、[原文 L146](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/91/918bbd4508551b64__00_X1_R2_独立回归与完整范围静态审阅报告.md:146>)

## 12. 实际阅读覆盖与继续队列

本轮全文：PRICE 223 行、FSC 221 行、X1 R2 独审 168 行；只读 ZIP 展开的摘要附录 24 行、SOURCE-AND-IMPACT 76 行、主索引 38 行、FROZEN-REUSE／ACCEPTED-REUSE／BASELINES。DEVELOPMENT-TASKS 全部顶层语义与 PRICE 9＋FSC 7 对象结构实读，ENG 5 项未据此宣称已读。两份 UA／CURRENT 按接受、字节身份、范围、评审和执行边界对象结构阅读；精确选择器及非全文边界见[商业阅读台账](<D:/CP6/docs/CP6_开发设计文档_20261010/evidence/commercial-reading.json>)。

R2 ZIP SHA `fe2a9354e5e1d04c928565953d5e38437cb955648d0b5dc371462e41693ad434`，84,173 bytes、15 成员；14成员字节清单已全部核对。R2_RETURN_RESPONSE 四根回复、全部47 SOURCE-LOCATORS、AUTHOR-STOPPED、原Scope的两商业Target和DG14／DP11／12／16已补充结构合读。R1冻结原包按清单逐成员核对0差异；47源码文本统一为LF和一个末尾换行，其中3份迁移恢复UTF8 BOM，全部匹配所登Git blob。原文本导出本来就允许追加末行，不能拿新文本SHA冒原Git blob，也不能把本次身份核验当47源码实现全文审查。 [导出规则 L15](<D:/CP6-archives/consolidation-20261010/commercial-cache/x1/X1-R2/SOURCE-AND-IMPACT.md:15>)

原三份xls模板／PA100、RA040、PA130等母版在当前全量ZIP成员索引按.xls/.xlsx及上述名称检索无命中；实际单元格、格式、图形仍未核。这个结论仅限当前本地索引，不声明源端319队列已经完成。R1旧正文与首轮RETURN保留历史身份，以当前R2原独审的关闭关系解释；不因此制造新缺陷。

状态 `required_materials_consolidated`：当前必要规范及辅助语义完成归并，历史实现审计、原母版和真实Owner采用保持上述边界；没有执行源脚本、SQL、业务测试、构建或远程操作。相关：[EST](<D:/CP6/docs/CP6_开发设计文档_20261010/modules/ERP-EST-01.md>)、[QTN](<D:/CP6/docs/CP6_开发设计文档_20261010/modules/ERP-QTN-01.md>)、[商业合同](<D:/CP6/docs/CP6_开发设计文档_20261010/contracts/commercial-contracts.json>)。
