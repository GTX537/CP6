# FIN-BUD-01 预算管理

整理状态：**required_materials_consolidated**。本域当前正文、强制规范、接受/评审及相关溯源已经语义整理；历史源码、旧版本与别域大册剩余内容另列，不随此状态声称全归档已读。真实Owner采用及业务运行仍未证明。

## 1. 业务目的、操作者和Owner边界

编制员建立年度PnL方案和版本、复制/导入预算行，复核员审核精确冻结内容，生效人将批准版切为唯一活跃基准，分析员在同一知识cut比较预算与GL实际。预算Owner拥有方案/修订/激活时间线/预算桶与Guard证据；GL拥有真实会计事实，FM拥有科目/成本中心版本，OA拥有批准决定。范围保原预算能力，不新增采购承诺、资金预测或完整管理会计。[S L5](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/47/4793a84b5a7d4d0e__FIN-BUD-01.md:5) [S L7](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/47/4793a84b5a7d4d0e__FIN-BUD-01.md:7) [S L12](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/47/4793a84b5a7d4d0e__FIN-BUD-01.md:12)

## 2. 当前版本、强制组合和接受边界

当前准确组合是 **BANK R2 + BUD/ASSET/OA-CONN/COMMON R1**。组合索引保存各文件SHA，不把五份正文一律改称R2；主文的STOPPED/候选/非accepted是冻结时标签，后继root于2026-10-08T04:09:11Z明确接受四项19 SPEC及COMMON的详细设计。116 AC全部NOT_RUN，实际Owner/政策/运行UNPROVEN，设计接受不授实施或金融动作。[I L3](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/e5/e5934d9a539b1c78__COMPOSITION-IDENTITIES.json:3) [A L6](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/76/763bb5a2ea791bb5__UA-20261008-X5-FIN-BUD-01-STATIC-MD02.json:6) [A L12](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/76/763bb5a2ea791bb5__UA-20261008-X5-FIN-BUD-01-STATIC-MD02.json:12) [A L14](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/76/763bb5a2ea791bb5__UA-20261008-X5-FIN-BUD-01-STATIC-MD02.json:14)

R1完整独审闭合八根及R05主要占用机制，剩银行恢复出口一个P2；R2只核该出口及直接传播，继承未变正文结论后累计无静态接受阻断。它不是本轮重跑业务验收；R0/R1当时退回保持历史。共同依赖按准确GL source/1.0、FM v1.3 R3、OA决定/应用分离和S2 R1消费，真实新增Provider/SourceProfile须独立采用。[R1 L9](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/02/024ed86db0a7a24c__CP6_X5_R1_独立完整静态复核_20261008.md:9) [R1 L20](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/02/024ed86db0a7a24c__CP6_X5_R1_独立完整静态复核_20261008.md:20) [R L13](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/09/096df8bb50ccf094__CP6_X5_R2_定点独立复核与四专项累计结论_20261008.md:13) [R L15](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/09/096df8bb50ccf094__CP6_X5_R2_定点独立复核与四专项累计结论_20261008.md:15) [R L24](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/09/096df8bb50ccf094__CP6_X5_R2_定点独立复核与四专项累计结论_20261008.md:24) [C L12](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/62/6271f981bb9187c1__COMMON.md:12)

## 3. 数据、身份、单位与结构约束

共同FinanceScope为(environmentId,tenantId,legalEntityKey,ledgerKey,mode)，mode来自认证登记；OA仍原两元Scope并另核财务对象Scope。Id为UUID；Key 1..128；业务revision为正Int64十进制字符串，RV为opaque Base64 CAS。Money为decimal(28,8)字符串并由准确政策规定scale；KNOWN(value,currency,basisLocator)、MISSING(missingKeys)、REDACTED分开，缺币种不推默认。ExactRef保原Owner类型；locator随机opaque，未有完整原件权不公开低熵hash。[C L20](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/62/6271f981bb9187c1__COMMON.md:20) [C L21](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/62/6271f981bb9187c1__COMMON.md:21) [C L22](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/62/6271f981bb9187c1__COMMON.md:22) [C L23](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/62/6271f981bb9187c1__COMMON.md:23)

|对象|开发时必须保留的字段与不变量|
|---|---|
|BudgetRoot|S、no/name、准确fiscalYearKey/calendarRef、PNL、description、control ACTIVE/SUSPENDED/RETIRED及RV；S+Calendar/财年+PNL唯一，非数字year唯一|
|BudgetRevision|displayVersionNo、businessRevision、state、policy/baseCurrency/periodSet、defaultMode NONE/WARN/BLOCK、defaultBasis PERIOD/YTD、makerSet、copySource；APPROVED不等于active|
|BudgetLine|准确accountVersion；CC和costObject各ALL或EXACT；逐行mode/basis可继承；原spreadInput、算法ref、只读annualAmount/periodAmounts、memo/RV；UNKNOWN不等ALL|
|ActivationTimeline|activationRevision、每版from/toEffectSequence、receipt及control头；最多一个当前激活，旧边界不可回写|
|CopyJob/ImportPreview|完整source cut或固定file/parser/template、全部heads、staging/行错误/合计、coverage、expiresAt和receipt；不对外暴露半版|

上述字段来自主文§2/3。[S L20](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/47/4793a84b5a7d4d0e__FIN-BUD-01.md:20) [S L21](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/47/4793a84b5a7d4d0e__FIN-BUD-01.md:21) [S L22](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/47/4793a84b5a7d4d0e__FIN-BUD-01.md:22) [S L24](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/47/4793a84b5a7d4d0e__FIN-BUD-01.md:24) [S L28](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/47/4793a84b5a7d4d0e__FIN-BUD-01.md:28) [S L47](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/47/4793a84b5a7d4d0e__FIN-BUD-01.md:47)

SpreadInput三支严格互斥：EVEN(annualAmount)；SEASONAL(annualAmount,periodWeights)；MANUAL(periodAmounts)，人工模式annual服务端求和。Calendar完整1..53期，每periodId恰一次；权重0..999999999999.99999999、最多8小数、总和>0。金额结果不可回传为权重。EVEN/SEASONAL先n−1期按批准精度舍入，末期补差；100/12例前11期8.33、末8.37。全零权重不自动改even；负预算仅批准contra政策允许，尾差造成不允许的负数报ROUNDING_ALLOCATION_INVALID；缺期不填0。12期Excel仅适用准确12期Calendar，否则UNSUPPORTED_CALENDAR_TEMPLATE。[S L23](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/47/4793a84b5a7d4d0e__FIN-BUD-01.md:23) [S L30](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/47/4793a84b5a7d4d0e__FIN-BUD-01.md:30) [S L32](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/47/4793a84b5a7d4d0e__FIN-BUD-01.md:32) [S L118](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/47/4793a84b5a7d4d0e__FIN-BUD-01.md:118)

## 4. 流程、状态转换和写集

新根由options→creation-context的真实absence创建；版号在锁Root下递增，不Max+1。每次行改动同时CAS版本头，改不同桶也不能共用旧RV双成功。复制批准版只生成新DRAFT，不继承批准/本次maker；大复制隔离staging完整后一次attach。草稿/拒绝/撤回编辑产生新DRAFT businessRevision，清当前资格并保旧Subject/Decision；PENDING_REVIEW禁止改，APPROVED通过复制新version。仅从未送审且未引用DRAFT可逻辑DISCARDED，root metadata独立修订不能改年/日历/控制。[S L25](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/47/4793a84b5a7d4d0e__FIN-BUD-01.md:25) [S L28](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/47/4793a84b5a7d4d0e__FIN-BUD-01.md:28) [S L34](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/47/4793a84b5a7d4d0e__FIN-BUD-01.md:34) [S L36](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/47/4793a84b5a7d4d0e__FIN-BUD-01.md:36)

按上年实际复制不依赖上年预算：B19建立准确GL正反事实ActualCopySnapshot→B20用户明确期间/master mapping→B04消费READY映射创建DRAFT。R1只支持同币、完整期间一对一穷尽映射；不同期数/拆并/换币具体拒。每源维度唯一对应目标，多个源落同桶冲突不合并；UNKNOWN不变ALL。目标FM/Calendar变化使映射STALE，源snapshot保持原cut；全部映射总额守恒并一次attach。[S L40](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/47/4793a84b5a7d4d0e__FIN-BUD-01.md:40) [S L42](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/47/4793a84b5a7d4d0e__FIN-BUD-01.md:42) [S L44](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/47/4793a84b5a7d4d0e__FIN-BUD-01.md:44)

导入只读受控SYS-FILE首sheet固定模板，禁宏/公式/外链执行。code在同S/date恰一FM版，多匹配拒；重复桶全批拒。INSERT_ONLY不覆盖，UPSERT_EXACT展示逐项前后差，未列旧行保留。确认只发previewId+heads，不重传file/rows；文件、DRAFT/RV、权限、master/policy或期限任变即PREVIEW_STALE；全行/期明细/版RV/ImportReceipt/command/audit一提交。空有效文件明确NO_CHANGE。[S L47](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/47/4793a84b5a7d4d0e__FIN-BUD-01.md:47) [S L49](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/47/4793a84b5a7d4d0e__FIN-BUD-01.md:49) [S L51](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/47/4793a84b5a7d4d0e__FIN-BUD-01.md:51)

送审冻结Owner Main Finance/BizType A5_Budget/SubjectKind BUDGET_VERSION、version/businessRevision、全部桶/原spreadInput/算法/Calendar/master/拟生效替代头和maker集合。DRAFT→PENDING_REVIEW与Submission意图/outbox同提交；派发未知保原Id，OA决定只Inbox，不自动生效。旧round Approved存STALE_RECEIPT。独立ActivationRequest由context→preview→activate，最后核准确OA Decision/当前权/全部头/旧active；ActivationReceipt、唯一头切换、旧版HISTORICAL投影、OA ApplicationReceipt、command/outbox/audit一事务。两版争同active一成一412；自动应用也必须真实executor走同协议。[S L57](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/47/4793a84b5a7d4d0e__FIN-BUD-01.md:57) [S L59](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/47/4793a84b5a7d4d0e__FIN-BUD-01.md:59) [S L62](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/47/4793a84b5a7d4d0e__FIN-BUD-01.md:62) [S L63](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/47/4793a84b5a7d4d0e__FIN-BUD-01.md:63) [S L65](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/47/4793a84b5a7d4d0e__FIN-BUD-01.md:65)

## 5. 跨域合同、实际口径和预算守卫

比较固定BudgetComparisonSnapshot的版本、Calendar、periodRange、knowledgeCut、GL fact manifest、历史FM分类、桶规则和closingTreatment。GL原Posted即使今天Reversed仍按原日期/cut入账，镜像按自己的日期/cut入账；不删除原票再减镜像。费用借−贷、收入贷−借，期初/结转/重估由准确政策决定。每actual只落唯一最高具体度桶（CC、完整costObject两个维度EXACT数），同级重叠submit与运行均阻；未匹配进UNBUDGETED。差异=actual−budget，budget=0比率NA，缺预算另义；旧API反向符号须显式adapter。[S L69](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/47/4793a84b5a7d4d0e__FIN-BUD-01.md:69) [S L71](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/47/4793a84b5a7d4d0e__FIN-BUD-01.md:71) [S L73](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/47/4793a84b5a7d4d0e__FIN-BUD-01.md:73) [S L105](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/47/4793a84b5a7d4d0e__FIN-BUD-01.md:105)

GL AccountingPolicy.guardRequirements明确BUDGET REQUIRED或有据NA，缺policy或“没有活跃版”不直接pass。REQUIRED时Budget参与真实同MainUow，读active/policy/calendar/桶头及同cut GL used，合并本凭证同桶净incoming，Period/YTD校验used+incoming≤limit；不能把正在提交效果重复计入used。WARN保证据不拒，NONE需明确政策；负向只有合法原镜像用途释放额度。额度锁持至GL commit，并覆盖预算激活/政策、普通/自动/逆向/纠正writer；剩100并发60只一个post。消费索引若有必须同GL提交且可从不可变facts回放，非第二主账。[S L76](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/47/4793a84b5a7d4d0e__FIN-BUD-01.md:76) [S L78](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/47/4793a84b5a7d4d0e__FIN-BUD-01.md:78) [S L79](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/47/4793a84b5a7d4d0e__FIN-BUD-01.md:79)

## 6. 事务、并发、幂等和恢复

共同Command携commandId/contextToken/originalRouteKey/expectedHeads/reason。自然键=(S,Owner,routeKey,commandId)，同键同规范意图回原receipt，异载荷409；稳定effectKey另防换command重复。所有写前驱先提供commandRoutes，创建前已有creationIdentityLocator与完整查询三元组，不能等待成功回包才知道如何恢复。GET commands仅接受互斥lookupToken，或contextToken+originalRouteKey加path commandId；旧token仅定位，仍重验当前结果读权。另一恢复人可用operation-context/有权操作列表。403/503不是NOT_OBSERVED，NOT_OBSERVED也不证明永久无效果。[C L25](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/62/6271f981bb9187c1__COMMON.md:25) [C L26](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/62/6271f981bb9187c1__COMMON.md:26) [C L35](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/62/6271f981bb9187c1__COMMON.md:35) [C L46](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/62/6271f981bb9187c1__COMMON.md:46) [C L48](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/62/6271f981bb9187c1__COMMON.md:48) [C L50](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/62/6271f981bb9187c1__COMMON.md:50)

先当前AuthorityFence，再服从真实Owner登记的全局锁序，随后Command/本域对象/receipt/outbox/audit；旧Controller、import、callback、worker、close hook、repair同门。HTTP uowId或共同DbContext注释不证明共享事务。缺真实参加者只阻相关新效果，草稿、安全诊断和历史仍可达。源事实+command+outbox+audit在本域同提交；GL独立流程后若源应用未完，保ACCOUNTING_POSTED_APPLICATION_PENDING，精确查原全部LegResult再一次应用。只有实际注册MainUow时才可声明跨域原子。[C L31](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/62/6271f981bb9187c1__COMMON.md:31) [C L33](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/62/6271f981bb9187c1__COMMON.md:33) [C L37](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/62/6271f981bb9187c1__COMMON.md:37)

预算特有写集：根/版/复制全行与receipt一次；导入全行一次；激活唯一头与应用回执一次；Guard与GL准确post共享额度门。暂停预算仅禁止新激活，不卸已生效控制；退休控制需独立ControlRequest/Subject/批准及明确effectiveBoundary，普通内容批准不能关闭guard。[S L28](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/47/4793a84b5a7d4d0e__FIN-BUD-01.md:28) [S L34](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/47/4793a84b5a7d4d0e__FIN-BUD-01.md:34) [S L51](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/47/4793a84b5a7d4d0e__FIN-BUD-01.md:51) [S L62](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/47/4793a84b5a7d4d0e__FIN-BUD-01.md:62) [S L79](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/47/4793a84b5a7d4d0e__FIN-BUD-01.md:79) [S L99](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/47/4793a84b5a7d4d0e__FIN-BUD-01.md:99)

## 7. 权限、SOD和字段披露

bud.draft/import/discard、bud.review、bud.activate、bud.report/history/export、可信服务bud.guard.evaluate分别授权；复核者不能是本人或代理maker。OA任务办理权由OA核，不以bud.activate代批准。控制请求request/submit/apply独立；实际复制另需report。未知专业Policy不生效，DEMO不进入REAL。[S L7](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/47/4793a84b5a7d4d0e__FIN-BUD-01.md:7) [S L88](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/47/4793a84b5a7d4d0e__FIN-BUD-01.md:88) [S L99](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/47/4793a84b5a7d4d0e__FIN-BUD-01.md:99)

所有动作再核当前S、对象、字段权与maker/delegate SOD；allowedActions和contextToken只是前端辅助，非许可。COMMON错误400格式、401认证、403权限/字段/SOD、404安全不可见、409状态/幂等/占用/锁期、412旧头、422政策/语义、428缺CAS、503共享门未证。Page默认20最多100，cursor绑定Actor/Scope/filter/snapshot，逐页/导出重验；迟到旧scope响应丢弃，秘密原文/SQL不回显。[C L27](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/62/6271f981bb9187c1__COMMON.md:27) [C L28](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/62/6271f981bb9187c1__COMMON.md:28) [C L42](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/62/6271f981bb9187c1__COMMON.md:42) [C L52](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/62/6271f981bb9187c1__COMMON.md:52)

## 8. 接口和页面交互

基址`/api/fin/budget/v1`；所有Command沿COMMON，GET缺精确lookup分支时需contextToken。[S L82](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/47/4793a84b5a7d4d0e__FIN-BUD-01.md:82)

|接口组|准确职责|
|---|---|
|B01–B06|options/creation-contexts、根与版查询/创建、version context/lines、行create/revise/discard；LineDraft仅维度/控制/memo/互斥spreadInput，lineId只path|
|B07–B08|template/import-previews与imports确认；结果inserted/updated/unchanged/版revision/queryPath|
|B09–B12|submit/withdraw、internal approval-results/v2原OA envelope、独立review/activation context、preview/activate；没有预算自造OA approve|
|B13–B14|comparisons固定NEW_CURRENT_CUT或EXACT_SNAPSHOT明确locator；rows/lineage、snapshot导出CSV/JSON、原commands/copy-job查询|
|B15–B18|控制请求独立批准应用；版头revisions、未提交discard、root metadata-revisions|
|B19–B20|独立actual-copy-snapshot及rows/lineage；mapping-options/proposals及准确revision查询|

全部payload/返回范围按原B01–B20，头由server返回，不手填缺失RV。[S L85](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/47/4793a84b5a7d4d0e__FIN-BUD-01.md:85) [S L90](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/47/4793a84b5a7d4d0e__FIN-BUD-01.md:90) [S L91](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/47/4793a84b5a7d4d0e__FIN-BUD-01.md:91) [S L93](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/47/4793a84b5a7d4d0e__FIN-BUD-01.md:93) [S L97](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/47/4793a84b5a7d4d0e__FIN-BUD-01.md:97) [S L99](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/47/4793a84b5a7d4d0e__FIN-BUD-01.md:99) [S L103](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/47/4793a84b5a7d4d0e__FIN-BUD-01.md:103) [S L105](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/47/4793a84b5a7d4d0e__FIN-BUD-01.md:105)

保留左右方案/版本/明细工作台；空方案可新建。dirty切版明确保存或放弃；权重输入与只读金额分栏。Approved未应用显示“待生效/被阻断”；导入展示物理行/动作/错误/期间差分，不允许忽略坏行继续。实际报告固定snapshot/版本/cut/coverage标签，导出同snapshot；切年清旧版本，旧请求generation丢弃。timeout查提交前保存完整locator，不能以版本状态猜成功。[S L108](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/47/4793a84b5a7d4d0e__FIN-BUD-01.md:108) [S L110](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/47/4793a84b5a7d4d0e__FIN-BUD-01.md:110)

## 9. 实现顺序与旧入口迁移

先S/不可变修订/全writer及真实Calendar/FM，继而三种分解/完整复制/固定预览导入；再OA决定/激活应用与共同授权门；再不可变GL同cut比较；最后联合登记BudgetGuard并完成UI/兼容恢复。旧`/api/fin/budget/lines/import/preview`和`confirm`必须转准确新preview协议或NEW_PREVIEW_REQUIRED；旧root PUT/版本PUT/DELETE分别适配B18/B16/B17，不能绕历史头。[S L53](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/47/4793a84b5a7d4d0e__FIN-BUD-01.md:53) [S L141](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/47/4793a84b5a7d4d0e__FIN-BUD-01.md:141) [S L142](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/47/4793a84b5a7d4d0e__FIN-BUD-01.md:142) [S L143](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/47/4793a84b5a7d4d0e__FIN-BUD-01.md:143) [S L144](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/47/4793a84b5a7d4d0e__FIN-BUD-01.md:144) [S L145](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/47/4793a84b5a7d4d0e__FIN-BUD-01.md:145)

切换按S+aggregate/已有实例记录RouteOwner及全writer清单：先旧草稿/在途/完成/证据不足分类、只读影子比较，再有权切换共同门。旧链只能原链恢复或经准确接管意图迁移；首次新效果后只前滚，不能退旧重放。原单号/source/GL/批准身份保留，未知旧资料LEGACY_UNPROVEN，只读核证不补签历史。CLOSED/YEAR_CLOSED拒普通新post/reverse；S2单effect扩展须GL独立接受并实际采用才运行。[C L39](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/62/6271f981bb9187c1__COMMON.md:39) [C L57](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/62/6271f981bb9187c1__COMMON.md:57)

## 10. 验收与证据要求

原24项均已逐项读，全部NOT_RUN：BUD01–05根唯一/行CAS/复制/三分解与非法权重；06–09预览过期、整批拒、准确旧路由/查询、upsert保未列行；10–14决定不激活、旧round、新版竞争、SOD与丢回包；15–21同cut正反/唯一桶/未预算/0比率/并发Guard/AUTO门/跨期镜像；22–24旧writer未证、撤权迟到、独立控制主体。关键演示剩100争两60、原100及下一期镜像−100、首次响应全丢且同commandId不同route两槽准确查。[S L115](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/47/4793a84b5a7d4d0e__FIN-BUD-01.md:115) [S L120](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/47/4793a84b5a7d4d0e__FIN-BUD-01.md:120) [S L124](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/47/4793a84b5a7d4d0e__FIN-BUD-01.md:124) [S L129](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/47/4793a84b5a7d4d0e__FIN-BUD-01.md:129) [S L133](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/47/4793a84b5a7d4d0e__FIN-BUD-01.md:133) [S L136](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/47/4793a84b5a7d4d0e__FIN-BUD-01.md:136)

## 11. 未确认项与退出条件

专业预算policy/contra/precision/calendar、GL历史fact provider和注册MainUow锁序、FM/OA all-writer实际采用均未证；不能据静态接受设置真实政策。退出需双方准确合同接受、全writer覆盖、真实额度并发/激活/恢复证据。BUD与S2历史正反口径已明确相容，实施需核相同cut与历史FM分类，无权全量报告必须WITHHELD或授权子集。[S L71](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/47/4793a84b5a7d4d0e__FIN-BUD-01.md:71) [S L79](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/47/4793a84b5a7d4d0e__FIN-BUD-01.md:79) [S L145](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/47/4793a84b5a7d4d0e__FIN-BUD-01.md:145) [S L148](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/47/4793a84b5a7d4d0e__FIN-BUD-01.md:148)

## 12. 阅读覆盖与来源

主文L1–149全文（范围1–17、版本18–44、导入46–53、审批55–65、比较/Guard67–79、API81–105、FE107–110、AC112–138、任务140–149）；COMMON L1–94全文；R1独审1–177/R2独审1–166全文。接受记录按完整结构读取并核准确组合。组合89成员目录已核；当前必要正文、SourceReadIndex/Manifest等规范与治理材料已按下段完成语义整理。原历史源码、历史R0和其他Owner正文仍按角色保留未全文阅读范围，不随当前必要组合闭合而改称全读；不以原作者读窗代本轮覆盖。精确SHA、实际范围与未读目录见[operations-reading.json](D:/CP6/docs/CP6_开发设计文档_20261010/evidence/operations-reading.json)；组合索引见[required-inputs.json](D:/CP6/docs/CP6_开发设计文档_20261010/indexes/required-inputs.json)。本轮仅本地文档整理和静态链接检查，没有业务执行、编译、Actions、提交或推送。

本次必要附件闭合：SOURCE-QUALIFICATIONS155行、SOURCE-MANIFEST416行、SOURCE-READ-INDEX464行全部字段及限制已读；21目标scope中本组三目标对象与完整已读UA逐字段准确相等，其他18目标范围对象不声称本组重读。X5原scope668行全部根/current7/DG14/DP11、12、16/OA对象实读，三财务target/book精确复用。SPEC-COVERAGE398行全部19映射、作者回应29、最小修复35、问题表40、INDEX19、变更表27、SOURCE-AUDIT9、STOPPED18及HEAD观察77全读；88成员MANIFEST逐项与89成员ZIP核同字节，79件checkpoint按角色登记。原历史源码、FlowEngine片段、旧差异和别域正文未声称已全读；它们不引入替换当前正文的独立规范。
