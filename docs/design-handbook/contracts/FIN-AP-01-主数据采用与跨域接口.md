# FIN-AP-01 主数据采用与跨域接口

本册补充[应付模块](D:/CP6/docs/CP6_开发设计文档_20261010/modules/FIN-AP-01.md)的必要附件。MASTER65 全部新增内容已归并；完整上游/H31/治理附件仍按模块阅读台账推进。本册描述设计目标，未运行付款、数据库或归档脚本。

## 1. 两个税码的来源必须分开

[MASTER65 原件](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/9f/9fa9c6de844cf5d3__AP-R1-MASTER65-DOCUMENT-EXAMPLE.json:1)不是一笔实际应付交易。它仅展示 MATCH_CREATE 下 AP_INPUT 的主数据分批采用，不能证明采购匹配所有权、发票金额、原义务、付款、过账或真实 AP 事务。

原 FIN-MASTER 案例只有一个不同税码根、两个内容版本。本例从 TX07 的 scopeRevision26 分叉，复用81份准确原件；第一根 DEMO.IN10（进项10%、可抵扣）供33条 item 使用。第二根 DEMO.AP65.IN20（进项20%、可抵扣）是本附件新增的隔离 DEMO 原件，供32条 item 使用。样例币种 XXX、地区与类别均是 DEMO，不能把第二根称为上游已接受生产资料。

第二根的18份新增原件完整表示 Version、SUSPENDED control1、Proposal DRAFT1→SUBMITTED2→APPROVED3→RELEASED4、批准、Scope约束29、preview、release、timeline1、ENABLED control2、当前Root、AP consumer registration 和 IAM。Root 的 timeline/control 引用与版本内容分开。Scope 经27/28/29到30；发布的真实原子性仍须实现证明，目录顺序和不同 recordedAt 不是多次业务提交的证据。

本例税条目不需要 BP 准入，supplierBpId、beneficiaryBpId、partnerRef、subLedgerType 全为 null。继承记录中可达的银行/机器/组织仅是历史发布证据，没有因此被 AP 新采用。新增 Finance AP service 注册与 IAM 也只是 DEMO，不能当作已安装的当前服务授权。

## 2. 冻结采用计划与两种摘要域

65个 item 各有自己的 itemKey、rootId、准确 expectedVersionRef、need=AP_INPUT、direction=INPUT 及地区/类别。按小写 UUID 的 ASCII 顺序排列，按 purpose 分组再分64+1两批；不能按税码根去重成两条。purpose 顺序固定 AP_INPUT、SUPPLIER_PAYMENT_PREPARE、COST_ATTRIBUTION，本例只出现第一类。

先冻结完整 immutableSourceInput，Hap(canonicalJSON)产生 inputDigest；AdoptionRootBody 固定 Scope、consumerOwner、effectId、action、objectId/objectRevision 和 inputDigest，再得到 effectRoot。BatchKeyBody 包含 schema、effectRoot、purpose、batchOrdinal、完整 itemKeys；其摘要派生 APB:batchKey 业务效果键与 APU:batchKey 使用键。两个 resultId 独立预留，不得复用同一个 invoiceId 充当两批结果。

AP 的这三类计划摘要只对 canonical JSON 计算；FM 原件与 UseIntentSubject 仍使用 typeName + LF + canonical body。AP 自有持久原件另外遵循主文 AP.Type.1 命名空间，不能把通用裸 JSON hash 用于所有三类数据。

真正可跨重试保留的是 AdoptionPlanBody 的 planId、root/effectRoot、batches 中的完整 item、batch/use/effect/result 身份和 frozenAt。辅助 planning.plannedIds 还列出了第一次尝试的 intent/proof/receiptId；它不是持久成功原件。按主文778的优先解释，回滚后用新真实 UoW 和新 intent/proof/receipt重新核当前门，不能复用失败事务的采用事实。

## 3. 一次外层事务中的完整采用链

本例先收齐八类实际资源：财务上下文、专业政策、资格、consumer registration、IAM、FM scope 六个 S 门，以及 AP effect/object 两个 X 门，按 ordinal 获取，持有到整个外层提交或回滚。字符串 databaseBinding/UowId 相等只是设计形状，实际同连接同事务句柄、所有 writer 共同门必须在实现时证明。

两批分别执行 ConsumerIntent → ReadInput/Proof（STAGED_IN_CURRENT_UOW）→ UseInput/UseReceipt → ConsumerCommitInput/ConsumerResult。每份结果绑定本批 subjectDigest、businessEffectKey、intent、receipt 与同一 UoW。UseReceipt 每个 used item 保留准确 Version/Timeline/Control 与依赖 refs；isPaymentAuthorization=false。两个结果中的 COMMITTED 仍是待外层事务提交的数据，不能先提交第一批。

成功期望为外层 commit1次：两批 FM receipt/result 与一份 AP 效果一起可见。第二批返回 DENIED/UNPROVEN 或最终校验失败时，外层 rollback1次：AP invoice/obligation、consumer intent/proof/result、FM receipt/audit、source replay binding 和外部副作用全部新增0，先前原件保持。已成功的原请求重放只在当前披露权限通过后返回旧两份 receipt/result/adoptions，新采用0；失权则 WITHHELD/NOT_AUTHORIZED，不改旧事实，也不新采用。

## 4. 附件 schema 与本轮阅读证据

本地七个封闭定义是 AdoptionScope、AdoptionRootBody、BatchKeyBody、Master65SourceInput、BatchIdentity、AdoptionPlanBody、AdoptionBatch，只服务于本 DEMO 文书，不是给 FM wire 增字段或替代已接受 MATCH payload。源输入恰65项、单批1–64项、采用计划恰两批；计划冻结时间使用24字符毫秒 UTC，业务日为10字符。各对象 additionalProperties=false，原引用使用相应域的完整形状。

[阅读证据](D:/CP6/docs/CP6_开发设计文档_20261010/evidence/FIN-AP-01-master65-reading.json:1)记录1,288,029字节/33,745行原件的725结构节点：270个新增节点全部读取，455个节点通过81份完整 reference/type/body 与167定义 schema 的精确复用覆盖；继承记录唯一增加 evidenceStatus=DEMO_DOCUMENT_ORIGINAL_NOT_RUNTIME，已明确读取。所有111个 type/body/reference 出现的 typeName+LF 摘要均重算一致，完整投影还原0差异。该检查不证明业务回滚、锁、Schema validator 或实际 Owner 采用已执行。

## 5. 跨域类型镜像不可消除 Owner 边界

[DEPENDENCY-WIRE](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/86/869b2cae139913a3__DEPENDENCY-WIRE.md:1)保留 MATCH、RETURN、GL、FM、OA、BP、PO、GR 八个独立命名空间的80份 TypeScript 摘录。本轮源 SHA 与各围栏全文逐字比对全部一致。具体规则复用对应模块实读证据；RETURN 追加四段镜像已补读并登记[独立阅读范围](D:/CP6/docs/CP6_开发设计文档_20261010/evidence/PUR-RETURN-01-dependency-mirrors-reading.json:1)。

PO 仍用 Qty6/Amount4 和三字段 Ref；事实含原商业 identity、精确 allocation、连续 factVersion/previousVersion、各类实物/质量/AP引用与纠正授权。Stock 使用 Root 上的精确区间、最多13位整数/8位小数量与30位整数聚合量，ReceiptRef 三字段、EvidenceRef 四字段，不能直接转成 AP 五字段引用而丢失来源定义。

Stock PostingPlan 冻结整个 ExecutionManifest、source、当前 expectedPools、capacityBindings 和完整行；prepare 的 canSubmit 不是执行授权。CommandResponse 分开 status 与 commitKnowledge，操作查询 NOT_OBSERVED/UNAVAILABLE 均 UNKNOWN，前者明确 canInferNoEffect=false。Seal 需要原 locator/digest/intent 与来源关闭权威。迟到或未授权实物先登记 ActualFact 和未决数量，再由精确 Root/既有过账腿/假观察证据归结；RISK_ACCEPTED_WITH_UNRESOLVED 不等于数量已解析。

GR 的 ReceiptCoordinateDomain 固定原实物区间和映射；ReceiptEffectiveScope 保存 effectiveRanges/excludedOriginalRanges，ScopeSet 保完整当前各行。验收分别显示要求/投影 scopeSet、质量区间、PO投影与Stock应用，不用一个总状态掩盖。ReturnNotice 和 ingress ACK 不自动证明下游应用。

OUT 有效量后继在此处明确为共同拟议合同。ShipmentEffectiveSnapshot 同时保存原/已逆/有效 Root区间、来源坐标与精确纠正集合；当前 FOUND 还带 sourceObservation/appliedSourceSetDigest/effectiveAsOf，HISTORY_FOUND 不声称当前完整，UNKNOWN 不推零。CorrectionLink/Refresh 的 quantityEffect=false，不再次移动库存。AP贷项/退款不能替这些物理有效量后继。

一笔 Return Part 若跨两个地点，同一Root在L-A[10,14)与L-B[14,20)仍必须保同一个 ExecutionPart/Manifest/Stock原子执行组。每地点完整当前 metricSet、规则、CapacityHead、mapping/occupancy和换算适用门均参与最终事务；有权空指标集合才允许 NO_REGISTERED_METRICS。缺配置、拒读或未知不能推出 N/A。地点/指标成员变化必须整组回滚重新收集，禁止只提交第一个地点。
## 6. 固定旧实现怎样迁移

本次另逐行核读六份旧服务共573行，均属于归档固定提交 `157630594e3371fe181955d2f6227ff3b6962c84`。以下用于理解 R1 的重建原因；没有检查今天 main 的调用链，不作为当前代码缺陷或活动 BUG。

| 固定旧源码 | 已读行为与迁移要求 |
|---|---|
| [ApInvoiceService](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/2f/2f6eada2845c955c__ApInvoiceService.cs:1) | 普通票按供应商+发票号去重，缺税率回退0、非正汇率回退1；旧 MATCH 创建遇已有票可直接返回。新方案要明确完整账套/来源身份、载荷冲突和结果恢复，未知税汇不能当有效0/1；票据 Posted 必须有完整原会计结果，不能仅凭自动记账调用返回 Ok。 |
| [PaymentService](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/87/878f426c4d937f22__PaymentService.cs:1) | 银行账户存在检查与会计过账不证明真实出款；旧反核销减回 s.SettledAmount，而原核销还将折扣计入票已清余额。新方案分别核真实资金、原核销现金/折扣/FX等组成和精确逆向，不能以删除关联行冒退款。 |
| [ApSettlementService](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/42/4226644ffb793fdd__ApSettlementService.cs:1) | 逐行调用自动记账、更新跟踪余额并以随机 Guid 建核销项；当前文件不足以证明外围事务是否统一。R1 必须先冻结整批稳定身份、所有余额保留及差额组成，再协调完整会计结果，禁止把每行局部保存当整批成功。 |
| [ApAgingService](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/e8/e82d4146392640c3__ApAgingService.cs:1) | asOf 用于天数分桶，却查询当前已过账票及当前未清额，不能恢复历史账龄。新方案同时固定业务截止日与知识截止点，按已核事实及后续逆向还原当时未清额。 |
| [ApReconcileService](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/59/598e58dfc9fec35d__ApReconcileService.cs:1) | 当前服务直接汇总子账和 AP_CONTROL，缺账户按0处理；未在此服务内看到账套、币种、历史 cut 的完整限定。新勾稽需要一致范围、完整覆盖和明确缺证状态，不能拿缺失当合法零。 |
| [FinApServiceAdapter](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/c6/c619b4a2a2c1228c__FinApServiceAdapter.cs:1) | INVENTORY 科目缺失时回退首个末级资产科目，向 AP 传 Qty/UnitPrice 等，不能证明原收货暂估身份；operatorId 缺省字符串 system 也不是授权凭据。新交接必须采用准确 MATCH/GR/政策/原科目和真实服务身份，阻断不确定回退。 |

旧能力清单只记录该提交的22项财务能力，其中应付、付款、核销为部分能力，账龄/勾稽为独立查询能力。收货未提供价格可能跳过暂估、MES 与成本分开保存等旧交接限制不能被此轮文档整理抹去。切换必须先封住旧 writer、查清在途未知结果和身份冲突；缺真实资金原件的旧 PaymentPosted 保留 LEGACY_EXECUTION_UNPROVEN，不补造已执行事实。

## 7. 接受对象、修订和证据边界

[CURRENT](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/c8/c8304202bdee2017__CURRENT.json:1)接受的是准确 R1 主文（997行，SHA256 `f8bfbd77ef015500328d24cc03c4beb6c6822a8d0e4cc7db60ecfc507780e567`）连同127成员规范附件包；仅接受孤立主文无效。库中历史 CANDIDATE/DRAFT 字样与接受状态分开。原始705行草稿及[91行首轮审阅](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d3/d3df61b2929986de__FIN-AP-01_v1.0_INDEPENDENT_REVIEW.md:1)保留为历史，R1 修订不删除其证据。

首轮四项阻断已由 R1 给出设计：B01 为 A51–A53 真实主数据选择前驱和会话绑定；B02 为 APB/APU 稳定64+1两批与完整结果集合；B03 为七种最终来源分支及真实资金 Required 合同；B04 为 TRANSFER/CASH/CHECK 判别结构，现金不伪造银行采用，支票只有真实 CLEARED 才认定资金执行。另统一会计状态、区分 A28 解核销和 A29 付款会计反向、补全 FM wire。17段差异逐段核原/新文本，并在内存完整还原当前 R1；新增及未变段复用已读主文，所有删除的历史语句已读。

独立 R1 结论为 PASS_BOUNDED_B01_B04_AND_DIRECT_PROPAGATION：只表示有界设计复审，不能扩大成127成员逐行审阅或业务执行通过。当前接受、SR和UA的业务内容一致；两份回执中的 JSON 与 CURRENT 差异仅为 Review/Acceptance 的 Commit、Blob、Path 六个追溯字段，所附80行审阅逐字相等。阶段10→80→100对应一次增量90，不重复计分。业务87项 AC 全 NOT_RUN，真实 Owner、专业政策及生产写入门仍 UNPROVEN。

归档作者的421项静态检查按 ID/status/detail 全结构分类，只有“1594次引用、107个唯一引用”的非空 detail，其他无新增业务字段；作者 PASS 未重跑。本轮独立做的是字节、原文片段、摘要、引用和无损表示核对，实际数据库提交与业务测试执行数为0。

## 8. 必要附件覆盖与保留项

详见[28份当前及补充材料证据](D:/CP6/docs/CP6_开发设计文档_20261010/evidence/FIN-AP-01-current-supplement-reading.json:1)。规范包127成员全部与 CURRENT 的逐项字节数及 SHA 一致，包内清单126项覆盖除其自身外的全部成员；99来源的 SOURCE-MAP、SOURCE-MANIFEST、PACKAGE-PATH-MAP、字节和 Git blob 一致。角色逐一分为：8份已接受上游主设计、13份共同 H31 文件、17份上游接受/登记溯源、55份旧源码、4份历史范围/目录、1份蓝图及1份旧能力表。

阅读覆盖严格分层：27个 PREP 窗口全部已实际读或与同 SHA 已读原件精确复用；38个审阅窗口的21份内联文本逐字核对，17份仅定位记录已指向实读样例，不能把定位本身算全文；80个 wire 块复用各 Owner 的准确阅读范围。六份旧源码全文已读，其余49份旧源码只核身份和历史用途，不宣称全读；其余历史目录和上游回执也不因出现于包清单而变成新规范。

共享 H31 的三组长 fixture 共491节点和六份短规范、两份观察已由运营/仓储专项实际读完，见[共同契约证据](D:/CP6/docs/CP6_开发设计文档_20261010/evidence/WMS-STOCK-RSV-supplement-reading.json:1)。AP 当前必要材料据此联合闭合；此状态不扩大为99份原件全部逐行阅读或业务通过。历史发布回执 M06（93,481字节，SHA256 `c361248131da8137e9b03f09d907d57394b6504dd54c319c4a310d92a567dd42`）仍未找到，现有 CURRENT/SR/UA 不冒充该原件；其原状态为 PUBLISHED_REVIEWED_NOT_ACCEPTED，与后来的准确接受记录分开。MATCH 纠正取消专用 close wire 仍为 Required，缺口影响该取消/保护释放分支，不妨碍整理其它已定义路径。
