# PLAN-PO-01：CP19 正常场景、原命令恢复与行像

本册覆盖当前交付保留的 9 份 CP19 normal 附件。它们是当前设计样例，不能因为文件大或继承 CP18 就归为无须阅读的历史；也不能把样例里的 ADOPTED、Synthetic=false、完整前后行像当成实际生产采用。原件明确 business=NOT_RUN、runtime=UNPROVEN、实际 Owner/Core grant=0；本册未执行 SQL、作者 checker 或远程调用。[PR /actualOwnerProposalsAdopted](<D:/CP6-archives/consolidation-20261010/root-planning-cache/plan-po-cp22/members/CP19_R02_R03_PR_RELEASE_CONTINUOUS_NORMAL_DATA.json:110841>)；[WO /illustrationBoundary](<D:/CP6-archives/consolidation-20261010/root-planning-cache/plan-po-cp22/members/CP19_R05_WO_CONTINUOUS_NORMAL_DATA.json:109633>)；[FIRST /runtime](<D:/CP6-archives/consolidation-20261010/root-planning-cache/plan-po-cp22/members/CP19_R05_R06_WMS_FIRST_ACCEPT_NORMAL_DATA.json:29766>)。

阅读方式为逐场景请求、参数、native 结果、写集、重放及分支语义阅读；177 组完整行像通过 1,461 个唯一完整 wrapper、47 张表的全部列与业务值类别复用；384 个独立内嵌 JSON 正文全部阅读，21 组 Head/CurrentReads 读其完整结构与差量。未声称逐字符解释不透明 UUID、hash、rowversion。逐件 SHA、原文行号及精确 JSON Pointer 见 [CP19 阅读证据](<D:/CP6/docs/CP6_开发设计文档_20261010/evidence/PLAN-PO-01-cp19-normal-reading.json>)；总模块是否 required，仍由其他当前附件共同决定。

## 1. 共同数据身份与事务边界

完整样例源建议为 100 EA，按 PR 40、WO 40、TRANSFER 20 分配到互不重叠的原始区间 0–40、40–80、80–100。PR 内两条 pegging 各 20；不能把 branch 的局部数量重新解释为另一条源区间。EnvironmentId/TenantId、PlannedOrderId、PartitionGeneration/PartitionDigest、PortionId、Owner/OwnerCommandKey/RequestDigest 必须保留原身份，不能凭同数量拼接。[PR /firstExplicitPrIntent](<D:/CP6-archives/consolidation-20261010/root-planning-cache/plan-po-cp22/members/CP19_R02_R03_PR_RELEASE_CONTINUOUS_NORMAL_DATA.json:5508>)；[WO /sourceRange](<D:/CP6-archives/consolidation-20261010/root-planning-cache/plan-po-cp22/members/CP19_R05_WO_CONTINUOUS_NORMAL_DATA.json:109631>)；[WMS /originalCreateRequest](<D:/CP6-archives/consolidation-20261010/root-planning-cache/plan-po-cp22/members/CP19_R05_WMS_CONTINUOUS_NORMAL_DATA.json:45227>)。

读取闭包包含 UOM/ITEM/SITE/LEGAL_ENTITY、日历/批量/提前期/安全库存、技术依据、需求与 pegging、容量/保护/供给，以及 MRP result/run/publication/head/suggestion/current-proof；常规当前读取集合为 40 条，按 LockOrder 保留次序。ExpectedHeads 同时绑定计划版本、rowversion、分区、源与 policy/supply 等头、manifest 及 writer epoch/lease rowversion。旧命令原结果的纯观察分支可有空 CurrentReads，它不因此获得重新发送或新建权限。[PR /firstExplicitPrIntent](<D:/CP6-archives/consolidation-20261010/root-planning-cache/plan-po-cp22/members/CP19_R02_R03_PR_RELEASE_CONTINUOUS_NORMAL_DATA.json:5508>)；[ABSENCE /actual62ParameterValues](<D:/CP6-archives/consolidation-20261010/root-planning-cache/plan-po-cp22/members/CP19_VALID_PR_ORIGINAL_ABSENCE_NORMAL_DATA.json:170>)。

表的职责分为以下几组；逐列取值及原件位置在阅读证据中，不能用分组概述代替类型/DDL 附件。

| 表族 | 设计用途与关键写入约束 |
|---|---|
| PlanningFactCatalog / PlanningFactHead / PlanningTransferFactAdmission | immutable 正文、当前头、来源准入分别存储；publisher 搬运事实不改变原 Owner/issuer。 |
| CurrentHeadManifest / Member；SourceSuggestion / Head | 保存完整来源闭包与源建议；不能只用一个根 digest 省略成员身份。 |
| PlannedOrder / Revision / Head / Decision | immutable revision 加 head 指针；业务版本、DecisionState、ConversionState、WriterName/Epoch 按当前程序同事务发布。 |
| Partition / Portion / Pegging / BranchEligibility / Protection | 固定源区间与分支资格；保护状态 AVAILABLE、OWNER_PENDING、OWNER_UNKNOWN、OWNER_ACCEPTED、RELEASED_NO_EFFECT 含义不同。 |
| WriterLeaseHistory / Head；CommandSlot | epoch CAS 与显式 cutover；本地命令存完整 principal/request/terminal canonical，重放不换 key。 |
| ConversionPreview / Work / Leg / LegPortion | preview 捕获版本且有有效期；work 聚合多个 leg；leg 保留 Owner、步骤、原 portion 与更正 hold。 |
| OwnerWorkflow / Step / Command；RecoveryCheckpoint | 步骤模板不等于立即可发送命令；真实 predecessor 完成后才固化下步请求/命令，checkpoint 保存恢复代次及必要本地重放元组。 |
| OwnerObservation / LookupEvidence / ReceiptInbox / Receipt / AcceptanceWinner | 查询观察、native 证据、流消息与接受赢家分开；仅完整原身份的成功结果可形成 winner。 |
| OwnerNoEffectFence / PortionNoEffectRelease | 原创建路径永久封闭的充分证明后才释放；NOT_OBSERVED 或一般拒绝不够。 |
| WmsTransferContext/Catalog/CreateEvidence；TransferAuthority/Scope/Dependency；SourceDemandRevision/Head | 保留原 Context、完整 Catalog、原 201 与独立源授权；WMS 草稿接受不形成库存变化。 |
| AuditEvent / OutboxEvent | 与本地业务变更同事务记审计/待发；网络发送在 claim 提交后，不能把异地 Owner 表并入 Planning 同库事务。 |

本次仅对 68 组完整 table+row 的 pre + writes → post 作静态集合重建，差异 0；这证明附件内部这些行像与声明写集的对应关系，不证明 SQL 已运行、实际 schema 允许、所有 digest 前像已重算或跨域事务成立。

## 2. PR：释放后新意图，与普通忽略分开

第一条显式 PR 意图使用 36 参数 preview，捕获当前 40 条读取、产生 3 项写入；随后 44 参数 start 固化 40 EA 原分配、原命令与保护等 15 项写入。22 参数 claim 仅更改 OwnerCommand 与 Outbox 两行，先记 MAY_HAVE_BEEN_SENT/LEASED，再在事务外调用 Owner；样例租约为 30 秒。[PR /firstExplicitPrIntent](<D:/CP6-archives/consolidation-20261010/root-planning-cache/plan-po-cp22/members/CP19_R02_R03_PR_RELEASE_CONTINUOUS_NORMAL_DATA.json:5508>)；[PR /firstOriginalClaim22](<D:/CP6-archives/consolidation-20261010/root-planning-cache/plan-po-cp22/members/CP19_R02_R03_PR_RELEASE_CONTINUOUS_NORMAL_DATA.json:29738>)。

原 PR 完整无效果证据绑定原 key、原请求、原 allocation，57 参数消费以 16 项写入形成 observation/fence/release 并清除该 portion 的占用。该 work 可为 FAILED_NO_EFFECT；整张订单仍可能 PENDING，因为其他部分仍可处理。释放后第二次显式意图必须取得新 preview、新本地命令与新 Owner key；它不是恢复已终结旧 leg。Firm 的业务版本与 later release 后的当前版本分别保留，不能把旧 expected revision 直接用于新 start。[PR /fullOriginalNoEffect](<D:/CP6-archives/consolidation-20261010/root-planning-cache/plan-po-cp22/members/CP19_R02_R03_PR_RELEASE_CONTINUOUS_NORMAL_DATA.json:41330>)；[PR /secondNewExplicitIntent](<D:/CP6-archives/consolidation-20261010/root-planning-cache/plan-po-cp22/members/CP19_R02_R03_PR_RELEASE_CONTINUOUS_NORMAL_DATA.json:59623>)；[PR /versionSeparation](<D:/CP6-archives/consolidation-20261010/root-planning-cache/plan-po-cp22/members/CP19_R02_R03_PR_RELEASE_CONTINUOUS_NORMAL_DATA.json:110823>)。

另一个分支在全部原 portion 可复用之后显式 IGNORE，以 51 参数/6 项写入从 FIRM 转 IGNORED；样例保留既有 ConversionState=PENDING。独立 SUGGESTED-ignore 则从 revision 1 转 2、DecisionState=IGNORED、ConversionState=NOT_STARTED，源 pegging/protection 不变、无 native 效果；两条路径不能合成同一个“忽略必清转换状态”规则。[PR /separateIgnoreBranch](<D:/CP6-archives/consolidation-20261010/root-planning-cache/plan-po-cp22/members/CP19_R02_R03_PR_RELEASE_CONTINUOUS_NORMAL_DATA.json:98217>)；[IGNORE /case](<D:/CP6-archives/consolidation-20261010/root-planning-cache/plan-po-cp22/members/CP19_R02_SUGGESTED_IGNORE_NORMAL_DATA.json:3>)；[IGNORE /allSourcePeggingAndProtectionRowsUnchanged](<D:/CP6-archives/consolidation-20261010/root-planning-cache/plan-po-cp22/members/CP19_R02_SUGGESTED_IGNORE_NORMAL_DATA.json:11492>)。

原 PR 纯 lookup 返回 NOT_OBSERVED 的 native arm 本身没有 requestDigest/source allocation；Planning 从冻结原命令补齐其本地证据，不把补齐字段伪装为 Owner 原响应。62 参数、14 项写入后保持 UNKNOWN/PENDING，公开返回 202；不建 no-effect fence/release/winner/stock。若已有可能发送历史，不能因为这次查无就换 key 再建。[ABSENCE /nativeCapturedResult](<D:/CP6-archives/consolidation-20261010/root-planning-cache/plan-po-cp22/members/CP19_VALID_PR_ORIGINAL_ABSENCE_NORMAL_DATA.json:17>)；[ABSENCE /ownEnrichedLookup](<D:/CP6-archives/consolidation-20261010/root-planning-cache/plan-po-cp22/members/CP19_VALID_PR_ORIGINAL_ABSENCE_NORMAL_DATA.json:41>)；[ABSENCE /publicResult](<D:/CP6-archives/consolidation-20261010/root-planning-cache/plan-po-cp22/members/CP19_VALID_PR_ORIGINAL_ABSENCE_NORMAL_DATA.json:12331>)；[ABSENCE /sendHistory](<D:/CP6-archives/consolidation-20261010/root-planning-cache/plan-po-cp22/members/CP19_VALID_PR_ORIGINAL_ABSENCE_NORMAL_DATA.json:12356>)。

## 3. WMS：Context → Catalog → 原 201 → 当前查询

ContextRead 是独立 native 读取；ContextDigest 的规则与包含 contextRef 的完整 body digest 不同。第一步 7 项写入保留 context/observation、推进步骤与 checkpoint；第二步用同一 context、原 keyword/order、pageSize=200、null cursor 读取完整 Catalog，9 项写入保留页和来源闭包，并固化 T04 创建体、OwnerCommand 与 outbox。授权 source 仍为原 80–100 区间，WMS request 内部 0–20 不改变源对应关系。[WMS /steps](<D:/CP6-archives/consolidation-20261010/root-planning-cache/plan-po-cp22/members/CP19_R05_WMS_CONTINUOUS_NORMAL_DATA.json:6043>)；[WMS /originalCreateRequest](<D:/CP6-archives/consolidation-20261010/root-planning-cache/plan-po-cp22/members/CP19_R05_WMS_CONTINUOUS_NORMAL_DATA.json:45227>)；[WMS /sourceAuthority](<D:/CP6-archives/consolidation-20261010/root-planning-cache/plan-po-cp22/members/CP19_R05_WMS_CONTINUOUS_NORMAL_DATA.json:45293>)。

T04 首次 claim 仍只改两行并在锁外调用。真正原始 201 返回的是 DRAFT 的 TransferRequest：20 EA，尚未审批、提交、分配或出入库，只允许该版本声明的编辑/提交动作。只有这个原 201（或经准确资格恢复的同一原 201）可构成创建接受。公开 65 参数路径 20 项写入；机器 68 参数路径 22 项写入，额外含 Inbox 的收取/应用，原 stream seq=1 与 OWNER_CURRENT_WIRE 资格独立校验。[WMS /t04FirstOriginalClaim](<D:/CP6-archives/consolidation-20261010/root-planning-cache/plan-po-cp22/members/CP19_R05_WMS_CONTINUOUS_NORMAL_DATA.json:32543>)；[FIRST /public201](<D:/CP6-archives/consolidation-20261010/root-planning-cache/plan-po-cp22/members/CP19_R05_R06_WMS_FIRST_ACCEPT_NORMAL_DATA.json:3>)；[FIRST /machineOriginal201](<D:/CP6-archives/consolidation-20261010/root-planning-cache/plan-po-cp22/members/CP19_R05_R06_WMS_FIRST_ACCEPT_NORMAL_DATA.json:14789>)。

首次接受形成 WmsTransferCreateEvidence、OwnerReceipt、winner 与 protection，并创建后继当前查询的 Step 3/TransferResultQuery。初始化模板中将 Step 3 列为 Commanded=true、但 OwnerCommandKey=null，不是可直接 dispatch 的命令；后继实体需由真实前序结果固化。最终 checkpoint 仍明确终结已接受的创建 Step 2，不因已建后继查询槽改称另一次创建成功。[FIRST /public201](<D:/CP6-archives/consolidation-20261010/root-planning-cache/plan-po-cp22/members/CP19_R05_R06_WMS_FIRST_ACCEPT_NORMAL_DATA.json:3>)。

PO27 创建槽查询是内部 PURE_ORIGINAL_LOOKUP，query 自身 commandKey=null；它查询冻结原 actor/body/key，不能借查询生产新草稿。FOUND_CREATED 必须返回同一原 201 并按 TRANSFER_REQUEST_CREATION_CLOSURE 1.1-proposed 资格消费。成功后公开本地 replay 依 principal/request 精确元组返回保存结果，零 native I/O、零新业务 SQL 写入；不能回落到新的 mutable gate 后再另建。[SLOT /case](<D:/CP6-archives/consolidation-20261010/root-planning-cache/plan-po-cp22/members/CP19_R05_PO27_SLOT_TERMINAL_REPLAY_NORMAL_DATA.json:3>)；[SLOT /additionalOwnerProposal](<D:/CP6-archives/consolidation-20261010/root-planning-cache/plan-po-cp22/members/CP19_R05_PO27_SLOT_TERMINAL_REPLAY_NORMAL_DATA.json:21727>)。

聚合必须看其他 leg 的完整状态：accepted + unknown → PENDING；accepted + 其余全部充分 no-effect → PARTIAL；全部 accepted → SUCCEEDED。WO 的原创建无效果闭包需同时证明 NOT_CREATED、FUTURE_CREATION_FENCE、ORIGINAL_SLOT_CLOSURE、BINDING_CLOSURE；单独 REJECTED/changed=false 不够。样例 public/machine 的接受步骤都保留相同规则，不能把接受某一 WMS draft 等同整个 work 成功。[AGG /worlds](<D:/CP6-archives/consolidation-20261010/root-planning-cache/plan-po-cp22/members/CP19_R06_WMS_ALL_AGGREGATE_NORMAL_DATA.json:4>)；[AGG /aggregationPriority](<D:/CP6-archives/consolidation-20261010/root-planning-cache/plan-po-cp22/members/CP19_R06_WMS_ALL_AGGREGATE_NORMAL_DATA.json:201549>)。

## 4. WO：五步与专业来源捕获

五步依次是 SourceSearch、CreatePreparation、EngineeringCheck、PrepareSnapshot、CreateRootWorkOrder。SourceSearch 与 EngineeringCheck 是读取，其余为原命令；每次消费在一个 Planning 本地提交里记录当前观察、推进步骤、固化下一请求/key、checkpoint 与审计/outbox，Owner 网络调用分别在事务外。当前例子仅处理原 40–80 区间的 40 EA，禁止预先制造未来 preparation/candidate/root ID。[WO /steps](<D:/CP6-archives/consolidation-20261010/root-planning-cache/plan-po-cp22/members/CP19_R05_WO_CONTINUOUS_NORMAL_DATA.json:6333>)；[WO /rootFirstClaim](<D:/CP6-archives/consolidation-20261010/root-planning-cache/plan-po-cp22/members/CP19_R05_WO_CONTINUOUS_NORMAL_DATA.json:93745>)。

初始 preparation=null 时有 4 组支持捕获/12 行 publisher 例证；取得真实 preparation 后才有 11 组 preparation 绑定捕获/33 行：Planning coverage/native、PLM purpose、REL pre-root authorization、CFG request/resolution、ENG baseline/UOM/preconditions、DEV 不适用与 ENG manifest。MES 是运输方；PLM/CFG/ENG/DEV/REL 原 issuer/provider pin、同 preparation chain 与字节不能被 MES 自称替代。当前普通 DEV consumer 仅 NOT_APPLICABLE_PROVEN 且 refs=[]；不代表 APPLICABLE/UNKNOWN 已支持。[WO /initialNullPreparationSupportCaptureWrites](<D:/CP6-archives/consolidation-20261010/root-planning-cache/plan-po-cp22/members/CP19_R05_WO_CONTINUOUS_NORMAL_DATA.json:6043>)；[WO /steps](<D:/CP6-archives/consolidation-20261010/root-planning-cache/plan-po-cp22/members/CP19_R05_WO_CONTINUOUS_NORMAL_DATA.json:6333>)；[WO /issuerContexts](<D:/CP6-archives/consolidation-20261010/root-planning-cache/plan-po-cp22/members/CP19_R05_WO_CONTINUOUS_NORMAL_DATA.json:109181>)。

EngineeringCheck 七类判定涵盖来源身份、ENG 闭包、CFG 唯一解析、UOM、用途、工艺/质检指令、DEV 不适用证明。candidate/snapshot/technical 前像保留一条 40 EA 物料和一个工序、主产出 FG-A 40 EA、EBOM/MBOM 及 UOM 映射；数量或 digest 相同不能替代完整结构。REL pre-root authorization 与 root 后 reservation 是两个时点的职责；root 返回 CREATED/NO_NEW_EXECUTION、reservation refs，并不证明实际制造执行已开始。[WO /steps](<D:/CP6-archives/consolidation-20261010/root-planning-cache/plan-po-cp22/members/CP19_R05_WO_CONTINUOUS_NORMAL_DATA.json:6333>)；[WO /nativeCandidatePreimage](<D:/CP6-archives/consolidation-20261010/root-planning-cache/plan-po-cp22/members/CP19_R05_WO_CONTINUOUS_NORMAL_DATA.json:108425>)；[WO /nativeSnapshotPreimage](<D:/CP6-archives/consolidation-20261010/root-planning-cache/plan-po-cp22/members/CP19_R05_WO_CONTINUOUS_NORMAL_DATA.json:108726>)；[WO /nativeTechnicalPreimage](<D:/CP6-archives/consolidation-20261010/root-planning-cache/plan-po-cp22/members/CP19_R05_WO_CONTINUOUS_NORMAL_DATA.json:109027>)；[WO /rootOriginalRequest](<D:/CP6-archives/consolidation-20261010/root-planning-cache/plan-po-cp22/members/CP19_R05_WO_CONTINUOUS_NORMAL_DATA.json:108349>)。

保留原始请求的 execution key、schedule、native bytes 是此版本的边界；继承前像与新 DTO 的表现差异不能擅自重新 canonicalize。样例中不同执行键关联的进一步一致性定位只记静态待核，未声称接口运行失败。[WO /originalKeyByteBoundary](<D:/CP6-archives/consolidation-20261010/root-planning-cache/plan-po-cp22/members/CP19_R05_WO_CONTINUOUS_NORMAL_DATA.json:109632>)；[WO /illustrationBoundary](<D:/CP6-archives/consolidation-20261010/root-planning-cache/plan-po-cp22/members/CP19_R05_WO_CONTINUOUS_NORMAL_DATA.json:109633>)。

## 5. 跨 epoch 的九个恢复窗口

九个窗口包括 WO 初始 SourceSearch、WO 在 0/1/2/3 后分别恢复 1/2/3/4，以及 WMS 初始 Context、Context 后 Catalog、Catalog 后尚未发 T04、可能已发 T04。每个世界使用新 Core v2 授权前提；23 参数 cutover 的 5 项写入 CAS epoch 8→9，24 参数 takeover 的 3 项写入新 checkpoint、本地槽与审计。旧 writer 零业务写入失败，不能复用已失效 lease 继续推进。[EPOCH /worlds](<D:/CP6-archives/consolidation-20261010/root-planning-cache/plan-po-cp22/members/CP19_R05_CROSS_STEP_EPOCH_NORMAL_DATA.json:3>)。

| 恢复窗口 | 唯一允许的下一步 |
|---|---|
| WO SourceSearch/EngineeringCheck；WMS Context/Catalog | 按冻结 read 请求重新取得同一步观察，以新 epoch 消费；不能伪造后续 Owner 命令结果。 |
| WO preparation/candidate/root 原命令仍 NOT_SENT | 先查同一原 key；原查询 NOT_OBSERVED 只记 UNKNOWN。新鲜源/Core 与无更新观察等准入均满足后，才允许首次发送同一冻结请求。 |
| WMS T04 尚 NOT_SENT | 先查原 creation slot，NOT_OBSERVED 的 35 参数消费只作观察；满足新鲜准入后首次发原 body/key。 |
| WMS T04 MAY_HAVE_BEEN_SENT | 查原 key；NOT_OBSERVED 仍 UNKNOWN，下一步继续 QUERY_SAME_OWNER_KEY，send=null。 |

查询 claim 与发送 claim 均保持原 actor/context/key，不跨锁调用；查询并不自动把 NOT_SENT 改成已发送，也不清除已有 MAY_HAVE_BEEN_SENT 历史。旧 head 与新 immutable revision 的 writer tuple 后续由 CP20 修订精确同步，本册保留 CP19 原样例及后续覆盖关系，不宣称 CP19 每个旧行像已是最终实现模板。[EPOCH /worlds](<D:/CP6-archives/consolidation-20261010/root-planning-cache/plan-po-cp22/members/CP19_R05_CROSS_STEP_EPOCH_NORMAL_DATA.json:3>)；参见 [CP20/CP21 续册](<D:/CP6/docs/CP6_开发设计文档_20261010/contracts/PLAN-PO-01-CP20与CP21续接场景.md>)。

## 6. 实施前需要保留的问题与验收入口

PLAN-IF-013：聚合样例 18 处 PlanningFactHead 外层 wrapper.key.ResourceKey 仍为旧 IAM/UUID，实际 row.ResourceKey 已是当前 hash key；首例在 [wrapper L5292](<D:/CP6-archives/consolidation-20261010/root-planning-cache/plan-po-cp22/members/CP19_R06_WMS_ALL_AGGREGATE_NORMAL_DATA.json:5292>) 与 [实际行 L5300](<D:/CP6-archives/consolidation-20261010/root-planning-cache/plan-po-cp22/members/CP19_R06_WMS_ALL_AGGREGATE_NORMAL_DATA.json:5300>)。实际 table+row 前后重建没有差异。导入/回放工具采用前要对齐 wrapper 或明确其单独含义，不得 silently 选一边；本发现不证明运行故障。

PLAN-IF-015：WO Step 1 的 [原请求 executionKey L20081](<D:/CP6-archives/consolidation-20261010/root-planning-cache/plan-po-cp22/members/CP19_R05_WO_CONTINUOUS_NORMAL_DATA.json:20081>) 与 [返回 executionKey L20131](<D:/CP6-archives/consolidation-20261010/root-planning-cache/plan-po-cp22/members/CP19_R05_WO_CONTINUOUS_NORMAL_DATA.json:20131>) 分别为 CP18-MAKE-579b… 和 MAKE-CONVERT-20261002-001；前一步 source item 与原请求一致。当前 [native kernel L117](<D:/CP6-archives/consolidation-20261010/root-planning-cache/plan-po-cp22/members/PLAN_PO_R3_CP18_NATIVE_KERNEL_SQL_DESIGN.sql:117>) 明确要求三者 executionKey 相等，且该结果原样进入参数 @ResultCanonical。因此这是当前样例与当前绑定谓词的静态不一致；采用前须确认唯一执行身份并同步依赖前像/摘要，不能绕过绑定谓词。未运行 SQL、未证明生产故障。

编码验收至少应覆盖：PR no-effect 的充分闭包与 release 后新意图；ordinary NOT_OBSERVED 不释放；WMS 原 201 的公开/机器两种入口与同 tuple 赢家；三种聚合；WO 五步同一 preparation chain；上述九个 epoch 窗口及原字节恢复；同 principal/request replay 零新增副作用。这里列的是待实现/待执行的验收场景，附件作者 checker 或静态重建结果均不能替代这些测试。[PR /runtime](<D:/CP6-archives/consolidation-20261010/root-planning-cache/plan-po-cp22/members/CP19_R02_R03_PR_RELEASE_CONTINUOUS_NORMAL_DATA.json:110844>)；[ABSENCE /runtime](<D:/CP6-archives/consolidation-20261010/root-planning-cache/plan-po-cp22/members/CP19_VALID_PR_ORIGINAL_ABSENCE_NORMAL_DATA.json:12359>)；[EPOCH /runtime](<D:/CP6-archives/consolidation-20261010/root-planning-cache/plan-po-cp22/members/CP19_R05_CROSS_STEP_EPOCH_NORMAL_DATA.json:678635>)。
