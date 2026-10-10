# PLAN-ATP-01 可供量与建议交期开发设计

状态：R11当前规范与全部案例已整理，40包成员逐件分层处置；参考程序实现审计另列。业务验收NOT_RUN，真实Owner采用UNPROVEN。

## 1. 目的与职责

销售在报价/订单草稿输入准确物料版本、用途、Site、数量单位、请求日期和评估时点，ATP依据有效策略与完整供给快照计算可分配量、缺口和建议交期。它保存可重演的评估事实，不创建Demand、Reservation、Order承诺、采购/工单/调拨/发货/Stock/Finance事实。请求日、建议日、Owner承诺日分别显示；本域`OwnerCommittedDate`始终null。H09查询与SC03草稿/确认仍无经济效果；Release H10由真正Owner负责。[职责](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d4/d4e2a2805932996e__A-PLAN-ATP-R11-MAIN-SPEC.md:34)

## 2. 当前版本与采用范围

准确单元是A_PLAN_ATP_SUCCESSOR_RECOVERY_R11，正文SHA `d4e2a2805932996e8733a3782847c66e8add8a06ae171fc3b80188a237126a6e`；原ZIP SHA `ff67f4ec57f6ca99c81d3c1e302d44a26378e37732ea71641bb04eab387aeccb`。2026-10-02T08:26Z（原件分钟精度）接受五SPEC、50AC的静态设计，不继承R10的PASS为R11运行证明。R11最终修复是DDL政策身份门相对于阶段恢复的顺序，前轮未改字节按接受组合保留；正文§27是当前政策绑定规则，前轮响应段仅解释演变。[最终评审](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6b/6bd1b8bc99ab9daa__SR-20261002-A-PLAN-ATP-MD01-FINAL.md:1)；[接受原件](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/20/2065a4ebe8f4221f__UA-20261002-A-PLAN-ATP-MD01.md:1)

PLAN-POL、PLAN-SUP在ATP组合内是RequiredFenced；PLAN-PO、PLAN-EXC仍为Reserved，不能以各自后来被接受直接改写ATP采用清单。FIN-CREDIT H09相邻设计不赋予ATP信用/可供量权威。CTP明确DISABLED。准确附件、源包和继承材料见[必需输入](D:/CP6/docs/CP6_开发设计文档_20261010/indexes/required-inputs.json)。

## 3. 数据、身份与数量

|对象|实现要点|
|---|---|
|Evaluation|组织、服务端主体/委托、稳定requestKey、body/envelope两种digest、原请求与评估时点、CAS、终态结果；终态不可改写|
|Segment/Basis|日期、排序、可分配与累计量、来源Owner和lineage、native量与换算、预留与安全量扣减、segment-basis映射|
|Lineage atoms|数量×时间不重叠原子、representation、sequence/predecessor和替换证明；新表示只取代交叠区，保留旧的洞状余段|
|ReservationLedger|完整Demand tuple与唯一reservationId；顶层与嵌套身份一致，不按单据号近似排除|
|Policy event/binding|Owner事件原字节、scope/effectivity、全局observationSequence、readAt、政策完整digest与评估绑定|
|Carrier/Receipt|REQUEST、AUTHORIZATION、POLICY、SUPPLY、CALCULATION五载体；六步收据逐项验证，不以数量为完整性证明|
|Projection/Freshness|公开脱敏结果、受保护依据；终态后仅合法Freshness追加，原评估不随最新供给重算|

22个闭合wire根使用必传/明确nullable、UUID、安全整数1..9007199254740991、毫秒UTC、IANA时区、十进制字符串、UTF-8 NFC排序紧凑canonical。重复键、浮点、未知/漏字段、非canonical载体在业务前拒绝。量为非负22位整数及8位小数上限，request>0，结果按政策scale；C#解析用任意精度字面与有理数，不能先塞入System.Decimal。有理数还有原规范76位限制。[对象](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d4/d4e2a2805932996e__A-PLAN-ATP-R11-MAIN-SPEC.md:80)；[wire](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d4/d4e2a2805932996e__A-PLAN-ATP-R11-MAIN-SPEC.md:182)；[精度](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d4/d4e2a2805932996e__A-PLAN-ATP-R11-MAIN-SPEC.md:241)

## 4. 计算与状态流程

1. 严格解析四输入carrier，核对kind→schema、字节digest、引用、主体scope与当前授权。策略必须唯一适用且完整；草稿、过期、交叠、缺失或歧义得到UNEVALUATED_POLICY。供给分页1..N连续、唯一Final，重算数量、ordinal、各层digest、完整地平线和全部扣减标记；超时或不全为UNEVALUATED_SUPPLY。
2. 核定技术/质量/货主/效期/UOM/用途资格，将native边界精确映射为结果UOM有理数。不可无损表达坐标则失败。切出数量×时间原子，以sequence及predecessor+1替换链证明选择赢家，不把父与后继重复计供给。
3. 按选中的representation聚合native份额后精确换算，仅在规定结果层HALF_UP一次，不能每片先舍入。0.005BOX×100=0.50EA（scale2）。预留按准确身份只扣一次；排除自身需求须完整tuple恰好命中一项，0项或多项拒绝，不能把同订单全部需求加回。
4. 安全量若Owner已扣，只验证准确策略/层/量/模式/一次标记；ATP_APPLIES才从最低优先级反向扣一次。已扣+未扣等于配置量。
5. 排序依次availableAtUTC、policyPriority、Firm、supplyClass、OwnerType/ID/version、lineage、representation；只有完整同key可合并。每次分配min(剩余请求,eligible)，剩余0停止。`totalAvailable=allocated`，不是原始eligible之和；unavailable=max(0,requested-allocated)，所有映射/分段总和守恒。
6. 独立终态验证器从四载体重算、逐字节比CALCULATION/PUBLIC/PROTECTED和关系行。请求10却给999必须失败，不因schema合法通过。

日历由UTC转IANA且绑定calendarVersion，节假日/非工作日或达到cutoff移至下个工作日，保留DST/闰日边界。所有候选日期不越completeThrough。有效期是授权、策略、resultValidityThrough、requiredCompleteThrough、snapshotexpiresAt、completeThrough的最小值，且晚于evaluatedAt。[完整算法](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d4/d4e2a2805932996e__A-PLAN-ATP-R11-MAIN-SPEC.md:201)

状态主线ACCEPTED→SNAPSHOTS→CALCULATED→COMPLETED/PARTIAL/UNAVAILABLE。失败状态包括REJECTED、CONFLICT、UNEVALUATED_POLICY/SUPPLY、STALE、RECOVERY_REQUIRED、FAILED_FINAL；按错误类别与是否有原阶段事实返回，不能统一空结果。终态不接受重新计算覆盖；重新评估建立新请求。[状态](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d4/d4e2a2805932996e__A-PLAN-ATP-R11-MAIN-SPEC.md:400)

## 5. API与Owner接口

|入口|行为与约束|
|---|---|
|A01 OPTIONS|只公开Allow/wire1.0/maxbytes/幂等/CORS/CSRF，不泄露身份、权限、策略|
|A02 POST `/api/plan/v1/atp/evaluations`|Idempotency-Key、服务端主体、CSRF、plan-atp:evaluate/ATP_EVALUATE；evaluate权限可完成内部完整性检查，不另要求recover|
|A03 GET evaluations/{id}|当前read权限及完整资源scope；返回PUBLIC和latestFreshness，ETag同时绑定两者；304无实体|
|A04 recover / recover-original|当前recover权限、原组织主体/委托/key/bodyDigest/envelopeDigest；原请求路径先授权再查存在性，拒绝时已存在/不存在都403|
|A05 GET evaluations/{id}/basis|独立basis权限，准确wireVersion/site/item/version/usage/asOf/purpose/pageSize1..200；成功`{kind:"BASIS",page}`，cursor绑完整查询和权限投影|
|A06 GET `/api/plan/v1/atp/policies/effective`|全scope先授权，0/1/多身份分别typed结果；同一policy被多次evaluation使用不算歧义|

A05 asOf必须字节等于原毫秒UTC；先核JSON类型再转换。cursor不参与查询digest但绑定org/evaluation/resource/purpose/permissionProjection，1起ordinal；同绑定/同页复用nonce，过期新generation使旧token失效。取Binding锁后重读时钟、当前授权lease和digest，不能等待越过有效期后发票。[A01–06](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d4/d4e2a2805932996e__A-PLAN-ATP-R11-MAIN-SPEC.md:101)

POL端口提供准确scope/effectivity/calendar/IANA/工作周节假日cutoff、有理UOM、结果scale、唯一rank、安全量策略、地平线、有效性和当前证明。SUP端口提供完整分页、lineage/替换证明、预留与一次安全量标记、原native格和转换/资格证明。供给总数正确但漏页/漏候选/缺地平线仍拒。IAM端口提供可信当前完整资源投影，不能由请求者自报。以上都是RequiredFenced采用边界。[政策端口](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d4/d4e2a2805932996e__A-PLAN-ATP-R11-MAIN-SPEC.md:54)；[供给端口](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d4/d4e2a2805932996e__A-PLAN-ATP-R11-MAIN-SPEC.md:67)

政策Owner事件摄取只追加CURRENT/REVOKED/SUPERSEDED准确身份和proof。相同身份+readAt只允许原字节重放，全局observationSequence在身份门内产生。A02在同身份锁选择readAt≤evaluationAsOf的最大sequence，须CURRENT、有效并与POLICY载体语义一致；caller ownerEventId只是期望值，不能指定旧事件逃过撤回。历史recover核原不可变binding，不改绑最新政策。A06读取当前事件，不把历史评估照片充当当前资格。[政策后继规则](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d4/d4e2a2805932996e__A-PLAN-ATP-R11-MAIN-SPEC.md:565)

## 6. 事务、幂等、恢复与持久化

ReplaySlot=(org,actor/delegation,operation,requestKey)。bodyDigest绑定规范化body；envelopeDigest有独立域，含method/route/wire/action/purpose/principal/key/bodyDigest，两者不可互代。相同完整请求回原结果，异body同key冲突，竞争者只有一首胜。五carrier必须字节、类型、schema、Ref和digest皆一致，空对象自哈希并非合法载体。六Receipt校ordinal/step/handleKind/schema/input/output/predecessor，RESULT必须指CALCULATION受保护完整payload digest。[重放](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d4/d4e2a2805932996e__A-PLAN-ATP-R11-MAIN-SPEC.md:330)

实现为三个可独立持久恢复的阶段及最终CAS事务：

|阶段|必须存在的事实|
|---|---|
|ACCEPTED/CAS1|REQUEST、AUTHORIZATION和Receipt1–2；没有policy binding/event handle|
|SNAPSHOTS/CAS2|增加POLICY、SUPPLY及Receipt3–4；恰好一个准确政策绑定/事件链|
|CALCULATED/CAS3|从四输入重算，存CALCULATION/PUBLIC/PROTECTED和准确typed children；源数组与关系行双向EXCEPT一致；Receipt5–6|
|terminal/CAS4|内部完整性核验、expectedCAS、终态、精确Audit/Outbox/Replay TERMINAL同UoW|

初次INSERT只能ACCEPTED。终态head/children不可更新，Outbox不能提前预埋，只有准确terminalAt/status/publicDigest才可产生；Freshness是终态后另行授权的追加域。DDL涵盖载体、收据、投影、数量/lineage/预留/映射行、错误、audit/outbox、权限票、schema/validator、cursor、政策事件与绑定及拒绝审计。[DDL与阶段](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d4/d4e2a2805932996e__A-PLAN-ATP-R11-MAIN-SPEC.md:344)

锁序ReplaySlot→EvaluationHead→PolicyIdentity→CarrierReceipt→Children→AuditOutbox。R11特别要求冷路径先严格比REQUEST/传入POLICY，再ReplaySlot FOR KEY SHARE、Evaluation FOR UPDATE、准确PolicyIdentity advisory；此后才能恢复任何阶段、插Receipt3、取有效快照/最新binding、插Receipt4。后续binder可重入同锁，“有token”不足以证明锁序。模型A02和Owner事件摄取也使用同repository事务锁/身份锁；每Evaluation增量提交，不清空再覆盖整repository导致丢并发Owner事件。[R11最终修复](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d4/d4e2a2805932996e__A-PLAN-ATP-R11-MAIN-SPEC.md:605)

恢复只用原不可变carrier及原请求，不取新供给冒充原结果。运行角色仅获`recover_by_id_transport`、`recover_by_original_request_transport`两wrapper，内部integrity helper不承担授权也不直接开放。可信票helper一次读取当前行返回主体，避免先查票再二次使用的TOCTOU；缺/过期票在存在性查询前拒。阶段任一digest/收据/行损坏即拒绝，不合成COMPLETED。

## 7. 权限、租户与公开投影

TrustedAuthorizationDecision绑定原主体digest、组织actor/delegation、action/purpose/time、Site/Item/revision/usage/productFamily/commercialOwner、完整commercialContextRef、授权exclusion关系谓词及projection ID/version/digest。complete/read/basis/recover都校当前完整资源行。不得用caller GUC或p_actor声称授权。数据库owner NOLOGIN；迁移BEGIN内SET LOCAL ROLE并核对象Owner；runtime NOLOGIN/NOINHERIT只调用固定search_path的受控过程，无直接DML或PUBLIC特权，不能留下superuser拥有的SECURITY DEFINER。[权限](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d4/d4e2a2805932996e__A-PLAN-ATP-R11-MAIN-SPEC.md:191)

PUBLIC仅公开坐标、policy/snapshot ID、公开分段/合计与闭合安全解释，排除commercialContext/excludeDemand/basis/lineage/representation/reservation、私有主体和重放/权限材料。按字段来源脱敏，不用`basisId|lineageId`字符串黑名单误拒合法ID。A05_BASIS_DETAIL含完整受保护账并绑定PublicSelfDigest。self digest、storage payload preimage、最终bytes digest、Receipt、representation、ETag分别使用原指定域，fullPayloadDigest含self但排除full/representation，不能用最终文件SHA替换。[投影](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d4/d4e2a2805932996e__A-PLAN-ATP-R11-MAIN-SPEC.md:279)

Freshness由服务端唯一ACTIVE observerRef提交并绑定原evaluation/publicDigest，不能信caller observer。OwnerName为NFC、1..128 Unicode码点、无C0/C1且首尾无空格，内部普通空格允许（如Main Planning）；与stable_id分开。head FOR UPDATE分配sequence，最新以sequence而非observedAt/UUID；seq2即使时间较早仍最新。ETag以固定三行ASCII前像绑定result与freshness representation digest（缺freshness为64个0），格式`W/"sha256:<64hex>"`总75字符。200规范化`{latestFreshness,result}`的字节、length、digest、ETag在各客户端一致；304 entity/null、字节null、contentLength0，不能返回旧实体。[Freshness/ETag](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d4/d4e2a2805932996e__A-PLAN-ATP-R11-MAIN-SPEC.md:295)

拒绝用typed403并持久审计，不以未捕获异常回滚审计。错误AtpProblem固定code/HTTP/retryable/titleKey/pointer/safe params/correlation；不泄漏digest、锁键、凭据或raw exception。400/422类型精度，403授权，409幂等CAS，409/422/503策略/供给，409/410过期，202/409待恢复，500完整性，422禁用CTP。[错误目录](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d4/d4e2a2805932996e__A-PLAN-ATP-R11-MAIN-SPEC.md:443)

## 8. 页面与服务端校验

`/planning/atp`填写全资源坐标、数量单位、评估时点和请求日，可带有权自身Demand排除及商业上下文。公开结果可读不意味着可打开依据抽屉；抽屉重新核basis权限。请求、建议、承诺分别展示，后者明确“无Owner承诺事实”；过期/新鲜度变更提示新评估，不修改历史。pending/conflict/denied按typed状态展示，支持键盘、焦点、ARIA和locale。报价面板绑定准确line/schedule，查询不写订单。[页面](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d4/d4e2a2805932996e__A-PLAN-ATP-R11-MAIN-SPEC.md:429)

## 9. 实施顺序及既有代码

依次实现canonical/schema与任意精度库、POL/SUP/IAM/日历端口、原子化lineage与分配器、五载体/六收据及阶段持久化、恢复与权限票、PUBLIC/BASIS/Freshness、A01–06与页面，最后迁移和旧入口切换。先保证公开脱敏和零经济写，再接商业查询调用；Owner采用失败保持RequiredFenced。

原文代码审读基线是`157630594e3371fe181955d2f6227ff3b6962c84`：当时`/api/plan/mrp`只有控制器授权，run/confirm/convert/ignore为有写入入口，未有ATP路由/权限。此事实不能冒称当前`90c871f…`代码状态，也不能复用MRP写权限实现ATP。实际权限目录、组织Site授权和迁移角色需当前实现核对。[旧代码及任务](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d4/d4e2a2805932996e__A-PLAN-ATP-R11-MAIN-SPEC.md:490)

## 10. 验收与验证边界

五SPEC共50业务AC均NOT_RUN。至少覆盖：混合单位重叠后继去重、一次舍入、自需求0/多匹配拒、安全量只扣一次、请求10不能分999、漏页/地平线不全、DST/cutoff、并发同key异body、四阶段中断原键恢复、政策撤回/同readAt不同字节、R11锁序、先授权后查存在性、basis权限到期/cursor换代、终态不可变、Freshness迟到仍按序、新ETag/真304、公开无私有依据、所有路径零经济写。

原包另含22根×10=220 raw schema向量（重复键须原字节验证）、25计算、29有持久前态/CAS/写集的workflow、10日历案例及canonical KAT。原作者静态/model结果只为归档证据，不是本次运行、更不是数据库/编译器或真实业务验收。附件25计算、29工作流、10日历、220Schema向量已逐条读取并按精确基准复用/差异归并；覆盖限于这些具体样例，不由数量推断生产正确。[AC与验证边界](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d4/d4e2a2805932996e__A-PLAN-ATP-R11-MAIN-SPEC.md:467)

## 11. 待核与采用门

- 当前Schema、DDL、数据约束、50AC、跨语言types及全部canonical/负例已读。PLAN-IF-004单位宽度、PLAN-IF-005终结锁序及两项SQL局部候选仍须编码前解决；9个静态参考/生成验证程序实现体未全面审计，DDL旧拒绝入口94行保留未读。
- POL/SUP当前版本与ATP R11端口是双边采用关系，不能直接用最新模块状态代替接口一致性与当前性门证明。
- IAM票、数据库执行角色、唯一有效Owner事件/observer、日历和schema注册必须有真实实现与撤权/竞争验证，均未在本次整理中证明。
- 原恢复前不可找回材料在原SourceMap中保留缺失边界；不反推其已完整审读。当前源码适配与数据库执行还未验证。

## 12. 来源与实际阅读覆盖

正文617行（含R2–R11响应附录）、最终评审63行、接受55行及CURRENT完整阅读；原ZIP成员清单已核，未执行其中任何脚本。准确路径、SHA、已读/未读范围见[root规划阅读记录](D:/CP6/docs/CP6_开发设计文档_20261010/evidence/root-planning-reading.json)。本册当前为required_materials_consolidated：5登记输入和40成员有明确设计归并/处置，含参考代码非生产审计边界；不代表全部归档源码或业务实现已验证。


### 12.1 R11附件开发入口

[字段、事务、计算/工作流/日历案例及待决差异](D:/CP6/docs/CP6_开发设计文档_20261010/contracts/PLAN-ATP-01-R11-附件说明.md)给出83定义、50AC、25计算、29工作流、10日历的阅读地图。全部状态仍为NOT_RUN，不能把公开查询完成视为订单承诺或真实Owner采用。
