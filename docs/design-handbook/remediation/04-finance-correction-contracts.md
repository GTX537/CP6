# W3：财务身份映射、更正关闭与会计边界

本章与 [01-version-and-authority.md](01-version-and-authority.md) 同属 `CP6-DESIGN-REVISION-20261011/R1`。新增契约独立版本化；不向原 MATCH/AP/GL/Credit 的封闭 DTO 填字段，不改变原 Scope、会计事实归属、金额 wire 或历史接受效力。逐项证据的完整 SHA、引用行、原版本与 consumer 实际采用门槛见 [owner-adoption.json](assets/finance-recovery/owner-adoption.json)。实际采用 **UNPROVEN**，业务实现及验证 **NOT_IMPLEMENTED / NOT_RUN**。

## 1. OPS-IF-001：Credit→GL 的无损身份绑定

Credit R4/F7 wire 的 Scope 是 `{tenantId,legalEntityId,ledgerId,environmentId,mode}`，mode 为 PRODUCTION/SIMULATION，身份值1..200；Ref 为 `{owner,type,id,version,purpose,digest}`。GL R2 的 Scope 是 `{environmentId,tenantId,legalEntityKey,ledgerKey,mode}`，tenantId 为 lowercase UUID，其余 Key1..128且无控制字符，mode为 REAL/DEMO；GL Ref只有 `{owner,id,version,digest,purpose}`。这些域并不等价，直接改名、截断、去 type 或按显示名猜租户均禁止。

### 1.1 ScopeBinding 与 alias registry

`FIN-CREDIT-GL-SCOPE-BINDING/1` 的封闭 schema 为 `ScopeBindingBody/ScopeBinding`。受信平台注册员在独立管理入口建立完整五维 source 与完整五维 target 的绑定，source/target authority 分别证明同一真实租户、环境、法人、账簿，不由业务 caller 提交两个对象便形成授权。管理入口不在本任务 close API 内；实际 owner、权限和登记记录齐全前拒绝业务使用。

|原轴|目标轴|规则|
|---|---|---|
|tenantId|tenantId|必须由真实租户身份目录批准 UUID alias；不能随机生成 UUID 当作另一个租户权限|
|environmentId|environmentId|完整环境身份对应；同名不同环境不同绑定；禁止默认环境|
|legalEntityId|legalEntityKey|法人原 identity 完整保留且同一真实法人|
|ledgerId|ledgerKey|账簿原 identity 完整保留且属于同一法人/环境/租户|
|PRODUCTION/SIMULATION|REAL/DEMO|仅两个明确配对；混合、未知及缺失全部拒绝|

数据库逻辑唯一键为完整 source Scope→binding revision 与完整 target Scope→原 source identity 的双向一对一索引，不按单轴裸 id 全局合并。每个轴的 alias 记录 source exact Unicode、target exact Unicode、authority assignment；四个轴都必须出现一次且与 binding 对象逐值一致，等值 identity alias 也记录。原合法 200 字符或控制字符身份可注册新短 alias，但不能把原字符删除后假称相同身份。hash可用于索引而不是唯一可回溯凭据；摘要碰撞比较完整原值并拒绝不一致。别名分配不复用、不修改、不转让给别的身份。

bindingId+revision不可变；修订只能新增带 predecessor 的记录，原 ACTIVE→RETIRED 为受控当前头状态，历史 body/digest 保留。registry当前头及alias唯一索引变更X锁，新使用S锁并持到业务自己的最终授权点，精确pin source/target scope、binding id/revision/digest。多个 active、无 active、未核实 authority、跨 mode、retired 新使用：拒绝；历史原 receipt/replay在当前读权限下沿原绑定读取，不能因已退休自动改用新绑定或重新做原业务。

### 1.2 六字段 Ref 与五字段 Ref

`FIN-CREDIT-GL-REF-BINDING/1` 新不可变 body 保存 Credit 原六字段、原 Ref UTF-8 原文 base64、原文 digest、原对象 content proof、ScopeBinding 完整 proof、consumerContract、允许 type/purpose 与 alias。原文 decode 后必须恰为原六字段对象；duplicate JSON key、未知属性、非法UTF8/非同值拒绝。允许空白等原字节差异但 byte digest 各自完整保留；原 object digest 不被替换成 Ref serialization digest。

五字段 GL target 指向适配绑定对象：owner固定 `CP6-CREDIT-GL-BINDING`，id/version/purpose逐值等于 body.adapterId/adapterVersion/adapterPurpose，digest等于新 RefBindingBody 的域分离摘要。原六字段（特别 type）全部参与原身份唯一键；同 owner/id/version 而 type 不同必须不同 binding，不能静默折叠。`allowedType/allowedPurpose` 必须恰等于原Ref对应值，并由精确 consumer contract的注册白名单授权，不是 caller 自证。

新 target digest 证明**新绑定体**，原Credit digest仅证明原对象，二者不互换；target没有type字段，consumer必须通过注册适配器resolve，取得全部原Ref并验证原 owner/type/purpose。未采用适配 resolver 的 GL source port保持fenced，不能将adapter owner当成新的经济事实生产者。该绑定 `accountingAuthority=NONE`：Credit PASS/reservation/check不因获得GL Ref就成为会计 source，也不扩张GL原封闭 source union。

`BindingUse` receipt 保存两scope、两Ref与两binding原版本、operationKey/requestDigest；幂等自然键为受信scope+purpose+operationKey，改变body冲突。实际current消费仍需原 source currentness和最终授权，身份映射不替代这些门。拒绝错误包括 `SCOPE_BINDING_MISSING/AMBIGUOUS`、`BINDING_RETIRED`、`REFERENCE_TYPE_UNREGISTERED`、`REFERENCE_PROOF_INVALID`；全部零新增效果。

### 1.3 金额与identity互不代替

保持 [06-wire-types-and-fixtures.md](06-wire-types-and-fixtures.md) 的原金额域：Credit原p38/s8 wire，存储coefficient `decimal(38,0)`+scale、计算用足够精度整数；INV任意精度integer text+scale保持原bytes，不转有限decimal。GL原decimal(28,8)较窄时，只有精确可表示的合法会计源金额可进入；超域拒绝原posting，不隐式舍入、拆分或改currency。此identity绑定本身不携带金额、不发布财务效果。

## 2. OPS-IF-024：CorrectionAdmission 独立 cancel/close

MATCH R2 的 correctionAdmission 与 forwardAdmission 是两个命名空间；原 FinanceClosure 属于 forward 的 requestKey/admissionEpoch，不能套在correctionKey上。原 MATCH FinanceAdjustmentResult 的 RECEIVED/PENDING/APPLIED/REJECTED/UNKNOWN union 不加 CANCELLED/CLOSED；原 AP Credit 与 GL Draft 也没有通用 VOID 状态。本补充增加独立close控制轴和终态receipt。

新物理操作见 [contracts.openapi.json](assets/finance-recovery/contracts.openapi.json)：

|操作|请求/响应|含义|
|---|---|---|
|POST `/internal/finance/match-adjustments/closures/v1/fence-and-close`|CloseRequest → CloseStatus|200仅返回已持久终态；202返回PENDING/UNKNOWN以及原query。先注册原closeKey并把该correction admission FENCED，再全路径关闭；不是普通forward close。|
|POST `/internal/finance/match-adjustments/closures/v1/query`|CloseQuery → CloseQueryResult|FOUND原状态；NOT_OBSERVED/UNAVAILABLE均 `canInferNoEffect=false`；从不创建业务或close记录。|

身份为受信 tenant/environment/mode + correctionKey/inputDigest/correctionEpoch/planRef+planDigest/scopeDigest/originalRequestKey/originalForwardFence，并精确pin原Admission的originalPostingSetDigest/currentSourceChangeSetDigest/authoritySetDigest。新请求的planRef为完整原AdjustmentPlan ProofRef，其.ref必须等于原Admission的三字段planRef；原三字段Ref中的version是Key，不能改为Seq。closeKey是新 lowercase UUID，唯一键包含受信scope和correction完整身份，`requestDigest=SHA256(UTF8("MATCH-CORRECTION-CLOSE/1") || NUL || JCS(CloseRequest))`。同scope closeKey+同原摘要重试返回原状态/终态原bytes；同key异body或同correction已绑定另一closeKey时409 `CLOSE_KEY_CONFLICT`，响应仅在当前可读权限下给出原查询定位。禁止换key绕过未知结果。每次读取重放都做当前读权限；close action还要当前精确correction-close能力与MATCH真实fence authority，不能借forward或历史授权开新动作。

完成认证、当前可见性和请求结构校验后，先查原close自然键，再判断新执行的expectedControlRevision。已登记请求重放不因自己已推进的fence revision而失效，也不重新做已过期plan的execute校验。关闭可针对原source已经失效或原plan已过期的历史身份；不能要求原source重新Eligible才允许关闭，更不能因此恢复forward。尚未登记的请求才用expectedControlRevision CAS建立close；失败时查原key确认无已提交登记后，才可重新准备。

### 2.1 状态机与原结果轴

|当前close轴|触发与原子动作|下一轴/输出|
|---|---|---|
|不存在|在correction source/control同门下核验身份，保存close request，MATCH correction FENCED，AP root tombstone，禁止新route/alias注册或新execute|PENDING；原forward始终FENCED|
|PENDING|完整plan、全book/leg、全部route/alias/worker/inflight/bridge扫描并按原键query/drain/dispose|仍PENDING；缺依赖或无法确认→UNKNOWN|
|UNKNOWN|重查原操作/commit结果，补齐原证据；不得生成替代经济命令|PENDING或符合全部条件的终态|
|PENDING/UNKNOWN|所有leg路径未来效果已闭，零uncovered/unknown/outstanding，所有历史Posted已完成OwnerApply；没有任何Draft/Posted等真实业务effect|CLOSED_NO_EFFECT|
|PENDING/UNKNOWN|同样全路径closed；存在Draft或Posted/Reverse等不可删历史，Draft已SEALED_UNPOSTED，OwnerApply已对齐|CLOSED_WITH_RETAINED_HISTORY|
|两种CLOSED|同requestDigest重放或query|原receipt与原bytes；永不重开、更换结果或回退PENDING|

原 forwardAdmission不重新ENABLED；原 FinanceAdjustmentResult沿原语义保留，partial Posted后不能假改APPLIED/REJECTED。terminal记录原result的不可变snapshot proof，close轴独立描述“将来不能再产生新效果”。`canInferNoEffect`始终false，仅明确读取经过验证的 `CloseTerminal.body.result=CLOSED_NO_EFFECT` 才有这一用途；任何 HTTP成功/PENDING/UNKNOWN/404/NOT_OBSERVED都不是无效证明。终态也不授权释放MATCH原allocation/OriginalObligation claim，更不自动REOPEN。

### 2.2 全路径集合和 fence 原子性

1. 原plan所有leg必须覆盖，包括required=false的leg；NOT_APPLICABLE须原有合法policy/plan proof，不能直接忽略。按 `(bookId,legKey)` 精确集合比较，不以计数相等代替。所有pathId、executionKey、book/leg、plan/correction输入身份均唯一且匹配；禁止把forward path作为correction path顶替。
2. 关闭catalogue同时覆盖源AP draft/credit创建、GL registration/post/retry/reverse恢复、manual/import/legacy alias、定时任务、补偿、已有worker claim、AP bridge/OwnerApply及新增路由注册。catalogue generation/digest及writer coverage证明必须在同source/control gate下冻结、重核；新writer只能登记受墓碑控制的不可执行路径。
3. `correctionFence`绑定原 MATCH Admission controlRevision，scopeDigest、correctionEpoch、planDigest、originalForwardFence逐一相等。启用/撤销、关闭、执行登记与最终执行取同物理gate；所有producer在最终业务commit前验证当前FENCED，不能只在排队时验证。Finance profile保持 AP `RequiredGate` 的 AUTHORITY→SOURCE→PERIOD_BANK_BUDGET→FM_SCOPE→BP_SCOPE→REGISTERED_POST→BUSINESS 原注册图，完整预收集、真实sameUow。遇到跨库只采用原独立participant恢复，不宣称跨库atomic fence。
4. 已经持有合法门并先commit的原GL效果赢；fence不能抹掉原Posted。重新query原key，恢复原GL receipt、AP bridge/owner facts，直到OwnerApply齐全。此恢复仅承认已发生历史，不受“禁止新效果”误挡；若需要尚未发生的新posting/mirror，必须单独原有效授权，不得藏在close中。
5. 每条未执行路径得到可核验source seal/closure或原永久REJECTED的全路径no-effect证据。只看attempt超时、一个grant seal、空执行列表、worker停机或缺行不充分。登记/执行集完整证明与“未来不再登记”tombstone一起才覆盖空集合。

### 2.3 Draft 与 Posted 明确处置

AP A11 `/invoices/{id}/void-draft`只适用于InvoiceState；不适用于CreditView/CreditRevision。GL R2 LegState只有UNRESOLVED/NOT_APPLICABLE/DRAFT/PENDING_REVIEW/APPROVED_NOT_POSTED/POSTED/FAILED/REVERSED，无VOID，现有withdraw/reverse也不是通用draft seal。因此新增 **AP CorrectionDraftDisposition** 持久记录：自然键correction+plan+leg+originalDraft，state固定SEALED_UNPOSTED，精确pin sourceRoot/originalDraft/raw wire digest，证明所有未来使用原sourceRoot的路径已fenced。它不改原Credit/GL Draft enum或bytes。

存在Draft即是保留历史，不返回CLOSED_NO_EFFECT。SEALED_UNPOSTED不删除draft、不撤销已有owner业务、不制造GL“作废”receipt；Draft已变Posted的竞态必须发现并转Posted处理。GL/AP全部create/approve/post/retry等writer不读同tombstone或不能证明已关闭的别名存在时，DraftDisposition不能出具，close留PENDING/UNKNOWN。已有Posted无自动reverse；若商业后续要求更正，由独立原source、授权、period、bank/budget、shared original-obligation预算执行，另留原历史关联。原会计结果不能用一条close receipt“抵消”。

### 2.4 终态receipt和幂等错误

`CloseTerminalBody`包括完整identity/fence/revision、catalogue generation/digest/proof、originalObligationClaimRefs、每book/leg精确sourceRoot与effectState、原effectRefs、DraftDisposition、OwnerApply receipts、全部path+registration epoch+writer coverage、executionSetProof、原结果snapshot、retainedHistoricalEffectRefs。originalObligationClaimRefs必须恰好覆盖原affectedRanges与共享预算，不允许换成新correction私有claim；它们在close后继续受保护。`uncoveredLegKeys/outstandingEffectRefs/unresolvedExecutionKeys`必须空；futureEffectsClosed=true、originalForwardAdmission=FENCED、allowsReopen=false、allowsReleaseOriginalObligation=false。receipt proof摘要覆盖完整body，不含自身digest的域分离JCS；proof的contract必须MATCH-CORRECTION-CLOSE-RECEIPT/1，authority可验证且绑定原scope。

语义校验额外要求：NO_EFFECT leg有完整noEffectProof且effectRefs/dispositions/OwnerApply全空；NOT_APPLICABLE有原policy proof；DRAFT_RETAINED至少一DraftDisposition；POSTED类至少原Posted和完整OwnerApply，若还留Draft也逐一disposition；CLOSED_NO_EFFECT所有leg只NO_EFFECT或NOT_APPLICABLE，retained列表空；CLOSED_WITH_RETAINED_HISTORY至少一个真实Draft/Posted历史，retained列表恰为各leg原effect union，不重复不漏项。terminal.receipt、requestDigest、identity与CloseStatus外层逐值一致。最大10000是本补充一次原子plan/cut支持界限，超过时COVERAGE_UNPROVEN阻断，不截断或分批冒充一个完整close。

|错误/状态|HTTP与恢复|
|---|---|
|INVALID_WIRE|400；原字段域/closed-schema/额外字段/摘要失败，零新效果|
|AUTHENTICATION_REQUIRED / FORBIDDEN / NOT_VISIBLE|401/403/404；遵循原对象可见性，禁止泄露原receipt|
|CLOSE_KEY_CONFLICT / CORRECTION_IDENTITY_MISMATCH|409；相同原key查询，不重造key，不覆盖原记录|
|FENCE_STALE / RESOURCE_SET_CHANGED|409；重新获取真实current proof/plan。已登记close只能同原body继续query恢复，不能原key修改body|
|COVERAGE_UNPROVEN / NATIVE_UOW_UNPROVEN / LOCK_PLAN_UNPROVEN|503；无新合法效果；若已有close记录返回202 PENDING/UNKNOWN并保留fence|
|DEPENDENCY_UNAVAILABLE / commit确认丢失|503（登记结果不确定）或202 UNKNOWN（已知持久记录）；query原key，永不推断未发生|

所有Problem的retry需与原因一致：wire/identity/permission错误NONE，已登记或确认未知QUERY_ORIGINAL，未登记的过期plan可REPREPARE。HTTP状态、code、query、retry不能任意组合；原key未知时允许nullable query但仍canInferNoEffect=false。首次fence与close登记提交不确定也必须保留原key查询，不再尝试替代key。

## 3. W3 既有设计逐项采用/差异/后继

这些设计已给出明确规则，主要是采用或有意差异。本次不统一状态名来抹平业务区别。每项具体consumer和正反案例均在机器登记中，运行门未证明不宣布“已集成”。

|登记|原版本、保留规则和采用范围|
|---|---|
|COM-CREDIT-INDEPENDENT-CASH-AND-DEMAND-SOURCE|ERP-CREDIT＋统一设计stage100：退款current source为完整原5S/right/currency/lineage目录，含legacy/manual/import/alias/inflight/reservation/end；不是新空registry。新demand由节点25真实owner证明，原履行100、新尝试与新需求分离；QA仅carried，RMA R3/AR R2按精确接受版本。|
|COM-MATCH-MRX-AND-ORIGINAL-OBLIGATION|RETURN R3/MRX0.2：实退不VOID GR、E100保留，PENDING_RETURN只禁新forward不自动FENCE旧admission；MATCH/RETURN/manual同OriginalObligation budget。真实Stock→PhysicalFact→MRX→SourceReceipt→OUTResult同Uow；credit/refund不消除RETURNED_RETIRED。|
|COM-RETURN-CURRENT-KNOWLEDGE-CUT|RETURN R3 T15/T16：完整recursive terminal leaf union，generation先失效，query/attempt/worker/CAS匹配；旧回复仅历史，发现更高frontier要失效current，追赶后重新capture，不把OUT local nogap冒充新cut。|
|COM-RETURN-H31-COMPOSITE-RESOURCES|RETURN R3 H31：所有warehouse祖先及location、全metricSet含N/A X锁、完整CONTROLLED资源集，一Part一native Uow；不固定49项（多点例60），SourceCapacityResult复用Stock receipt不二次消费。|
|COM-SUB-COST-RECEIPT-VS-ACCEPTANCE|SUB Recovery R2：Finance approved累计receipt Q算cost、Pcum+sameM只一次、累计舍入delta；消耗独立COMMERCIAL_ACCEPTANCE_GROSS＋冻结Qplan/unitUsage。PO100→80不改原分母100；真实return不VOID。|
|OPS-IF-012|COST R2：完整source/Cost controls→FM/BP→cost heads；GL CompletionCurrent S持到Apply、reverse X，D06/D16 revision分开；MAT/EXEC/OUTPUT及全部source/GL writer同真实Uow。|
|OPS-IF-020|S2 RP-CLOSE-03新GL consumer扩展：精确effect/date/period真状态/fence/target/approval/SOD/bankbudget及新capability，已关period不OPEN；未采用 LOCKED_PERIOD_EXTENSION_UNPROVEN，旧wire不塞扩展字段。|
|OPS-IF-021|S2 FX_SCHEDULED_CHAIN仅已计划FX次期镜像；历史period fulfilled、forward current reversed、lifecycle posted三个轴可并存；DURABLE_NOT_DUE有限purpose，不扩展为普通AP/AR错误更正豁免。|
|OPS-IF-022|S2 report完整ledger immutable forward/reverse+effectiveDate+opening coverage；GL-only/manual/source-null必含；FX取as-of历史敞口、required owner全集、rates及负credit，不用今天open替代，不夹零。|
|OPS-IF-023|AP R1七source branch保留；EffectTarget精确root/plan/manifest/event/leg绑定；AP-FUNDS-EVIDENCE-1只读method真实状态+account/control门，RECORD事实不等于未来GL目标，未清算支票不是真现金carry。|
|OPS-IF-025|AP batch独立GL成功、本地Apply失败时bridge+reservation保留；补原Apply；A28 allocation inverse、A29 paymentGL、cash refund各独立；同cut liability减unallocated payment与bridge，prepayment资产分列。|
|OPS-IF-026|AR R2每event readiness：总体UNRESOLVED不阻止policy-ready INVOICE合法推进；INV独占COGS source，AR只协调；不套AP整manifest门，不把总体状态误读为无GL效果。|
|OPS-IF-027|AR R-GL-AR-LEDGER-CUT-1完整历史role/GL-only/manual ledger；原current reversed仍历史入账。strict需同native snapshot；CUT_VECTOR_ONLY为用户明确诊断选择，不自动降级；100k超限不能截断。|
|OPS-IF-028|AR R2 AR_RECEIPT_CONFIRM独立OA；funds真实/GL Posted不替代批准，D25/D12/D13新baseline/claims/receipt同finalUow；旧plan失效不携带，blocked保留GL/claim；未Apply补偿不恢复余额。|
|OPS-IF-029|INV R10只窄范围替代handlercontext/cold C09；cold按Table/Row重建三durable Uow一致，六TransactionUowId只write envelope；API的FinalGateProblem引用R8为文档陈旧指向，精确patch改R10。所有原code/status/retry以R10定义为准，不改经济key与scope。|
|OPS-IF-031|X5货币component qty=abs(amount)、unitprice1有currencyunit profile；UOP真实qty、未舍入rate、residual最终舍入amount；allocation会计两侧按purpose不双计业务量；无qty保持UNRESOLVED，不能统一填1或NA。|
|OPS-IF-032|BANK RESTORE_OWNERSHIP_AND_APPLY全冲突组原claim无FREE间隙恢复，所有maker SOD＋AND权限；原GL current不变，无镜像更正；step receipt重放只继续apply。|
|OPS-IF-033|ASSET final depreciation current链与历史fulfilled分开，REUSE_CURRENT/REUSE_CORRECTED/BLOCKED/CREATE_EXCLUSIVE；缺mirror阻断不重造；借用普通run不属处置，处置reverse不反借用run。|
|OPS-IF-034|BUD policy显式NA/required、预算门持至GL commit；normal/auto/inverse/policy全writer纳管。100余额两笔60只能一笔过账，读预检不是permit。|

## 4. 证据、校验和剩余运行门

schema提供closed shapes；cross-field/集合/摘要/权限语义按本章及 [verify_assets.py](assets/finance-recovery/verify_assets.py) 中静态可表达部分核验。所有proof都必须由真实owner验证完整身份、原字节、当前性、issuer与scope；构造良好的JSON和合成digest不是采用证据。静态正反向量覆盖缺字段/额外字段、scope错配、ref丢type、alias冲突、锁序逆转、terminal伪no-effect和query误推断。

对于实现验收，必须补真实registry内容、完整writer关闭/切换证据、原部署接口、native transaction handles、并发/权限撤销/unknown恢复、Draft变Posted竞态、多账簿多leg晚到、bridge/OwnerApply与原义务不释放测试。这里只把这些列为明确采用条件，不把未运行门误写为另一个尚待决定的业务政策。静态报告不证明任何数据库锁、权限、财务金额或生产数据已正确运行。
