# FIN-AR-01 连续案例与事务边界

本册完整整理 R2-CONTINUOUS-FIXTURES.json（SHA 35c1942aff79e1815c4b564b04c33bfdda8944896173297072f8b544030be62a，45,295 行），覆盖 25 个基础记录、65 行税务采用、失败/重试两个事务、13 步空白页收款流程、26 个 AR 原记录、完整审批链、GL 来源投影及 4 步核销准备。原件全部为 DEMO / DOCUMENT_ONLY_NOT_RUN；COMMITTED、APPROVED 等是设计数据，不是本轮真实执行。

完整来源：[R2 连续案例](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/35/35c1942aff79e181__R2-CONTINUOUS-FIXTURES.json:1)。阅读和逐对象比对见 [fixture 证据](D:/CP6/docs/CP6_开发设计文档_20261010/evidence/FIN-AR-01-fixture-reading.json:1)。本册还归并当前 R2 的接口派生 schema、79 条验收、评审与接受记录。全模块的历史/上游成员分类仍以模块台账为准。

## 1. 财务主数据的两个税码根

样例在同一个法人/账簿范围配置 DEMO.OUTA（销项 0.100000）和 DEMO.OUTB（销项 0.000000），均允许 AR_OUTPUT，不可回收；不是实际适用税率建议。资格、专业政策、IAM、consumer registration 和财务上下文各自有独立原件。service 只有 fm.use，reviewer 只有 fm.review；readFields、writeFields 和 secretReveal 分开。

每个税码保留 Version → Proposal → ReviewDecision → ReleasePreview → Release → Timeline 链；最初 Control revision 1 为 SUSPENDED，正式激活后 revision 2 才 ENABLED。Root、Timeline、Version、Control 引用的用途不同，不能把任一个 revision 当成统一“主数据版本”。preview canApply=true 仍 isExecutionPermit=false，使用时继续核当前权限、策略、范围和不可变原版本。

## 2. 65 行分批采用，外层一次提交

65 个不同 itemKey 交替引用两个税码根；按 [64,1] 形成两个 batch，分别保存 operationId、candidateId、planRevision、purpose、ordinal、完整 itemKeys、batchKey、useKey、businessEffectKey 和预留 resultId。冻结结果逐行保留 selection 与 calculatedTax：A 为 1.00000000，B 为 0.00000000；不是把 65 行粗略聚合为两个税码使用。

每批在同一个外层 UoW 中先写 ConsumerIntent，ReadInUow 返回同 UoW/数据库的 STAGED_IN_CURRENT_UOW proof，然后 FIN-MASTER Use 返回 ENLISTED receipt，最后 consumer result 绑定同一 subjectDigest、intent/useReceipt、batch businessEffectKey。heldProof 中示例锁序为 IAM10、consumer registration20、finance context25、qualifications27、policy30、FM scope40，持有到外层事务结束；这只是文档值，仍需真实锁实现验证。

失败 U1：第一批已暂存自己的 COMMITTED result，但第二批把 businessEffectKey 错传为 AR-AGGREGATE-NOT-A-BATCH。应拒绝第二批，并回滚整个 U1；新 consumer result 和 use receipt 均为 0，所有暂存行不可查询。不得把第一批对象中的 COMMITTED 字段当成已经落库。

重试 U2：保留两批预留的 batch/use/effect/result 身份，重新产生本次 UoW 的 intent、proof、receipt。两批实际完成后，两个 consumer result、两个 master receipt、TaxBasisFrozenFact、业务结果一起在唯一外层 commit 后可见。其 subject 与 identity 不因重试改变，但 UoW、intent、receipt 和依赖摘要不能借用已回滚 U1。精确重放返回同一原结果，零新增 adoption。

## 3. 从空白页面登记预收 60

A01 返回当前财务上下文，A32 依次选资金账户、BillTo、Payer；A12 从已授权视图选择已确认 TX60/LEG1。selector 的 SELECTABLE、15 分钟有效期和显示字段只是选择结果，均 isExecutionPermit=false。A13 登记 ADVANCE 60，产生 DRAFT ReceiptRegistrationFact 和 REGISTER_RECEIPT 结果，不会直接记账。

A14 确认先校验真实资金与来源，形成 READY 的会计来源计划；原 confirm command 保持 PENDING。A34 可见 1 个 RECEIPT_ADVANCE / BANK_TO_ADVANCE 成员，dispatch/accounting NOT_STARTED、AR 未应用，错误 APPROVAL_REQUIRED。A28 只持久 OA SubmitCommand 与冻结 snapshot，不应用业务结果。

随后 Main Workflow 的 oa.app/2.0 APPROVED 决策精确绑定 submission、subject businessRevision/contentDigest、policy、binding/flow 版本、原 maker 集合、独立 reviewer 权限与职责分离证据。该 AR 收款决定不是 GL 记账审批：GL 样例另以 REGISTERED_AUTOMATION 的原政策和完整 leg 结果为前提，不能因 OA APPROVED 就自行宣布 GL POSTED。

## 4. 最终收款原子应用与恢复

真实完整 GL 结果及当前应用权限成立后，同一 UoW 形成 ReceiptAccountingFact(D25)、ArBusinessResult(D12)及 RECEIPT_ACCOUNTING_APPLIED(D12E)、OA ApplicationReceipt(D19)、Receipt baseline(D13)、confirm command 完成、FundsClaim CONSUMED 和 receipt outbox。原金额/基准金额均为 60；clear role 是 CUSTOMER_ADVANCE。A15 此后才可见收款可用 60、发票未清 40。

三个负分支保持独立：没有 decision 时 PENDING，零 D25/D12E/D13；decision 属于 plan1 而当前 plan2 时 STALE，不能挪用旧审批；GL 已记账后应用者权限被撤销时应用 BLOCKED，保留原 claim 与真实 GL 结果，恢复时重新检查当前权限，不假装 GL 回滚，也不提前写收款 baseline。

审批 snapshot 的 Base64 解码与独立 snapshotBody 完全相同，摘要也相同；这证明归档表示一致，不证明实际 OA 发送、接收或授权已经发生。

## 5. GL 来源身份与核销边界

AR 内部 source intent 冻结 sourceFactId/sourceLineId/sourceGuardKey、operation/planRevision、原 funds/payer/customer admission/claim、政策和五项证据。投影为 gl.source/1.0 时，evidence.key 映射为 requirementKey，原五字段引用与顺序保留；sourceDocNo 来自服务器 D08，首次派发前冻结。货币事件示例 quantity=1，单价与金额均为 CNY60，ownershipKey=MONETARY_EVENT，不能解释为收发库存 1 件。

sourceBusinessCore 排除 transportEventId 和 payloadDigest；transportHash 对不含 payloadDigest 的完整传输 envelope 计算，两者是不同摘要域。FM 类型原件摘要另为 typeName + LF + canonical body；AR demo 原件为 canonical body，不能统一拿裸 JSON SHA 去验证所有 Owner。

A16 对发票40与收款60按两边 expectedRowVersion 预留核销40，discount=0，返回 ALLOCATION_RESERVED；此时 businessApplied=false。A28 提交独立核销审批后，A34 显示 ALLOCATION/CLEARING_TO_AR、RUNNING/PENDING。没有审批就 A17 apply 返回 422 APPROVAL_REQUIRED，保留当前 rowversion。预留成功不等于核销已经入账；样例未提供这条核销后续实际完成证明。

## 6. 本轮核对的适用范围

本轮将 370 个独立结构节点按依赖顺序全部阅读，重复对象精确复用；2,149,323 字节原 JSON 无损还原，0 差异。66 个 reference/body 出现按各自摘要域核对一致；审批快照与 GL 两种摘要均一致。这些是本地文档保真检查，没有运行归档程序、schema validator、数据库、业务测试或 Owner SDK。原声明的 40 项 schema 静态通过仍是作者证据，未改称本轮执行。
## 7. 当前接受单元与旧评审的效力

[CURRENT 接受记录](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/c0/c0caf60d3fc83052__CURRENT.json:1)接受的是 SHA 3ada4bf6… 的 R2 主文加完整 373 成员规范附件包（SHA 4dc51cc3…，7,145,643 字节），两者是一个设计单元。记录时间 2026-10-01T13:58:28Z 是协调者观察到的完成时刻，不冒充服务器精确时间。原主文仍保留当时 DRAFT/待复审字样；后续接受证据改变其设计效力，不改写冻结字节。

R1 独审的三组问题分别是收款自身审批、GL evidence 字段转换、FM proposal/control 前驱身份。R2 只修这三组及直接传播；完整 154 行差异、R1 195 行评审、R2 120 行复审均已阅读。R2 的设计 PASS 和用户授权委托接受均有原件，但 79 条业务 AC 仍全部 NOT_RUN，真实 Owner 采用、企业专业政策和共同事务门均 UNPROVEN。

计分记录先由 Stage10 到80（+70），再到100（+20），唯一 Main/All 增量90只计一次，Shared 为0。六份原 SPEC 映射、DG11 REBUILD、Main Finance 及 DP01/06/11/15 的范围保持。SR/UA 与 CURRENT 的实质接受内容完全相等，仅 CURRENT 增补自身归档 commit/blob/path 追踪；两个回执尾部均逐字复用同一 R2 独审报告。

归档当时标作未接受的 RMA/NCR/CREDIT/FIN-INV 逆向依赖，应按各自后续模块状态核对。不能把旧 AR 接受时点的状态当成今天所有模块的最终状态；也不能因某模块后来被接受，就省掉 AR 对准确范围、来源和共同门的核验。

## 8. GL 派生封闭 schema 的编码约束

[GL-SOURCE-DERIVED-CLOSED-SCHEMA](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/fd/fde707600df5bc93__GL-SOURCE-DERIVED-CLOSED-SCHEMA.json:1)由归档作者根据已接受 GL §11 转写，用于文书核对，不是上游另行发布的 schema。根对象封闭，包含21字段；业务核心为其中19字段。Scope 固定五字段，mode 为 DEMO/REAL；引用保留 owner/id/version/digest/purpose；UUID 使用小写规范形式，金额保留八位小数的字符串表达。

SourceLine 逐项包含 lineId、version、originalRef、quantity、unitPrice、amount、ownershipKey、valuationRefs 共八个字段，源数组为1–1000项。KNOWN 金额携 value/currency/basisRefs，MISSING 金额携 currency/missingKeys；缺金额不能填0。evidence 每项只有 requirementKey/reference，priorLegResults 与 evidence 上限各256。correctionOf、supersedesUnposted 是必须出现的可空引用；coverage 固定 COMPLETE。嵌套对象同样拒绝额外字段，不能把 AR 的内部 key 原名直接传入 GL。

本轮将 AR 附件里的 FM-ACCEPTED-SCHEMA 与已全文整理的 FIN-MASTER 167 定义逐值比较，整体一致。仅复用被证明相同的内容，没有将“已解析”冒作新规则阅读，也没有运行作者 validator。

## 9. 79 条验收如何用于实现

[AC-REGISTER_R2](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/3e/3ee0e30403422de0__AC-REGISTER_R2.md:1)包含49条基础验收、20条 R1 和10条 R2，以下按实现约束归并，编号和逐条原文仍以注册表为准。

|约束族|实现与回归需覆盖的行为|
|---|---|
|商业来源与计量|OUT 场景按政策要求 POD；观察到的“已交付”文本不是事实。VMI 成本未知不可制造 COGS；有权0与缺失不同；来源头变化拒绝旧选择。采购退货不进入应收。3×10+2×12=54；25EA×120/10=300；实发102、授权100要显示异常，不截成100。|
|GL 分腿与重放|原 Draft 失败保留同键 PENDING；换运输身份但核心相同才是 alias，新来源版无纠正依据不能绕防重。全部腿就绪后才发第一腿；收入已成功而成本技术失败只续原成本腿；缺成本阻首腿。全部 NA 为 NO_ACCOUNTING_EFFECT，不是 Posted。GL 完成通知 ACK 不等于 AR 已应用。|
|核销金额与并发|收款70对应票60/40与收款30的多轮分配必须按原精确剩余额结算；70上并发两次50只能一个成功。任一行失败，本地整批零应用。USD100 carry700，现金98 carry705.6、折扣2时，FX19.6与折扣14分别保留；反向恢复100，不能只恢复98。|
|资金、预收与补偿|无原资金事实拒绝；同交易金额98变99是重复冲突。预收60核销40不再过一次银行，剩20。GL 成功而 AR 未知保留 claim；逆向未知先解析所有原核销。业务 claim 不因执行租约过期自动释放。账务反向不代表银行退款。|
|历史查询与下载|asOf 同时考虑事实时间与知识切面；晚到回填可使历史观察100变80。今日已逆向不抹掉过去真实已过账事实。A−20/B+20即使合计0仍显示差异。无历史证明就是 UNPROVEN；币种和账簿分开；失去权限后不能下载文件或其 hash。|
|贷项、退货与成本|退20不把历史发货100改小，也不自动重开需求；贷项200只一次。无实物折让须准确 NA 授权；共用100预算的两个60不能都成功。必须定位原发票行，贷项超过未清金额先撤销准确核销集合。source fenced 后子命令 NOT_OBSERVED 仍需解析。退20可分可售15/隔离5；贷项不代表退款。成本层60@360、40@320，退最后20应160，不能用平均136。|
|R1 身份与跨域切面|命令结果查询绑定方法/路由，即使 UUID 相同也不可串用；提前 Header 不引用未来业务事实。GL 切面纳入 GL-only20，不能只查询 AR 自己的 D11。相同日期不是共同快照。DEL 的 NA 依据保存原 readKey/asOf；AR 的 BP 四角色准入独立于旧销售采购 UsePurpose。|
|R1 逆向与主数据|未应用补偿不得把余额100改成“先1200再冲回”；已应用走 A18。D27 在首个 GL 效果前冻结98/2/19.6完整组成。fenced 来源可做已授权镜像反向，不能做新正向。无 TaxMasterFact 不伪 FM 使用；合法零税率主数据仍需实际采用条目。|
|R2 完整传播|收款批准与 GL 批准分开；无决定、旧 Plan、GL 后失权/BLOCKED 恢复各有独立结果。BLOCKED1 → APPLIED2 同流连接 previousReceipt，重核当前门、查询原 GL，不能重记账。GL key→requirementKey 全量转换且冻结完整核心；FM 两根按 SUSPENDED1→ENABLED2 合法前驱，65项采用与重试身份逐值保持。|

这些是实现目标与未来自动化用例的输入，不是本轮已执行测试清单。

## 10. 补充阅读保真与明确范围

[当前附件阅读证据](D:/CP6/docs/CP6_开发设计文档_20261010/evidence/FIN-AR-01-current-supplement-reading.json:1)登记18份当前附加输入。30个评审窗口的源文件和切片 SHA 全部吻合；23个内嵌窗口中8个捕获字节直接相等，15个文本窗口的代码围栏表示恰好省略一个尾随 LF，补该分隔符后精确相等。7个 LOCATION_ONLY 大窗口实际内容已在完整 fixture 的370节点阅读中覆盖。

13个 identity minipack 窗口的字符范围、原文、SHA 与 JSON pointer 逐项相等。CURRENT 的成员目录按身份和角色盘点，不据此声明其所有历史主文已逐字阅读；上游主文只在对应模块有充分阅读证据时复用。作者静态检查输出作为作者证据保存，本轮自己的检查只证明文档表示与摘要一致。
