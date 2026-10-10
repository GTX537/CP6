# CRM-ACT-01 客户互动、修订与工程来源

整理状态：`core_semantics_consolidated`。当前接受设计为 v1.1.1，主文 SHA-256 `67dc3667fb852c5672f73710b80a4af17810cd09fe03ed69cbb2c82dff8dfa91`。本文重组已接受规则供开发使用；业务验收、真实 Owner 接线及运行证据均未完成。主文中的“待接受”需与后继 UA 一起读，不能重新开启已接受的设计选择。[接受原件与范围][U19]

## 1. 目的、操作者与业务边界

销售人员从已有 Account 320 或 Contact 350 登记、读取和更正一次实际互动。模块保存“何时发生、发生了什么、谁首次登记、当时与哪些明确对象有关”的历史；Lead 200/240 和 Opportunity 420 消费该事实。一个 Activity 可在多个明确宿主显示，仍只有一个事实，不能在每个页面复制登记。[范围][S29]

CRM 原生活动只接受 Account/Contact 主锚。Lead 原生事实、首响/SLA和其更正继续由 Lead Owner 负责。Activity 不拨号、不发送邮件或消息、不建提醒/任务、不创建商机/订单/工程、不更新 Account/Contact 主数据；NextContactAt 仅记录安排。主锚、原作者、首次 RecordedAt 不可普通更改；非 Lead 活动允许独立追加 Voided，没有 Unvoid 或外域级联撤销。[边界与政策][S31]

操作者区分历史读者、新登记销售、内容更正者、具备管理范围的事实更正者、独立 VOID 授权者、引用编辑者、附件下载者、原结果读取者，以及工程收件服务身份。前端是否显示按钮不是最终许可；任何动作都需当前宿主和原主锚授权。原作者身份也不能单独充当内容更正权限。[能力矩阵][S74]

## 2. 当前版本、组合与接受效力

当前组合为 v1.1.1 完整主文、CURRENT、`UA-20260930-A-CRM-ACT-MD01` 和限定复审 `SR-20260930-A-CRM-ACT-MD02`。UA 核定主文 265104 字节、3434 行和上述 SHA；接受即使候选 Payload/STATE 当时尚未发布也成立，不能把远端手续当作内容未接受。[准确对象][U28]

限定复审只覆盖 F01～F03 及受影响的 DTO、持久化、事务、恢复和连续例，结论为 `MAJOR_REVISIONS_VERIFIED_WITH_EXPLICIT_COORDINATOR_CORRECTIONS / REVIEWED_FOR_USER_ACCEPTANCE`；两个总控补正已进入 v1.1.1。它没有重新审计全部 Account/Contact 或运行实现。[复审范围][R6]

UA 已接受：外联/内部 Note 分类；外联至少一个已建 Contact；`ACT-CONTACT-READ/1.1` 最小读取扩展；FACT 使用 PrepareExisting；每个原工程请求冻结一条规范结果流；SourceVersion 随真实修订推进。主文 §17、DEV-ACT-07、G08 和复审中早于 UA 的“待接受”因此只剩真实接线/运行 Gate。G07 复合 FACT/引用原子治理仍 `NOT_ENABLED`；G01～06/08 是实施 Gate；本次接受没有授予业务编码/数据库/部署许可。[当前接受][U19]

## 3. 实体、字段、身份与版本

| 实体 | 关键字段和身份 | 约束与责任 |
|---|---|---|
| ActivityHead | TenantId、ActivityId、PrimaryKind/Id、OriginalPrimaryVersion、OriginalActorId、RecordedAt、CurrentRevision、CurrentReferenceSetVersion、RowVersion | 主锚仅 Account/Contact；原作者/登记时刻/主锚不可 UPDATE；无独立“当前已受理源”可变布尔值 |
| ActivityRevision | Tenant/Activity/Revision；SupersedesRevision、ChangeKind、ReferenceChangeKind、RecordClass、Kind/Outcome/OccurredAt、Content、NextContactAt、FactState、SetVersion、ChangedAt/By、Reason、CommandKind/Key、PiiRedacted | 每次 CREATE/CONTENT/FACT/REFERENCE/VOID 保存可重建的完整标量；历史文本 nvarchar(max)，不以新长度限制截断历史 |
| ReferenceSet/Member/SetMember | SetVersion、SetDigest、CreatingRevision；ReferenceId、Category、CreatedInRevision；Set→Member 关系 | 初始空集合也存在，初版1；成员稳定身份不可改绑；新集合复制未改成员关系，旧集合不覆盖 |
| ParticipantReference | ContactId/Revision/SetVersion；Relation/Primary 的 ID与版本；Channel/Value/Policy 版本；PurposeCode、原准入 EvidenceId | 历史明确参与，不由当前主要账户重新推导；nullable 成对字段严格一致 |
| ObjectReference / AttachmentReference | 对象 Kind/Id/BoundVersion；附件 AttachmentId/AttachmentVersion/ContentSHA256/Bytes/AddEvidenceId | 业务语境不等实际参与；附件只存精确版本引用，不存文件二进制，不自动 latest |
| AdmissionEvidence / FactAdmissionLink | 封闭 Request/Decision 密文、签名/密钥版本、BindingDigest、期限、完整 GuardSet；新 FACT Revision＋旧 ReferenceId＋新 EvidenceId | 新 FACT 证据不覆盖旧参与证据；Link 同 Tenant/Activity 外键，每修订每 Reference 最多一条 |
| SourceSnapshot / Audit / Command / Outbox | SourceVersion=真实 ActivityRevision；SourceDigest/KeyVersion；审计前后版及原因；原 O/请求摘要/终态；最小事件 | Source 与精确 Revision/Set 一一定位；无变化不新增这四类业务事实；命令可以留下无变化或终态拒绝结果 |
| TimelineScope | Tenant、ObjectKind、ObjectId、Generation、RowVersion | Activity 原主锚及原/当前明确引用变更推进水位；Contact 关系解释不更新它 |
| EngineeringBinding/Stream/Result/Inbox/Conflict | 原请求 B、规范 Producer/Contract/Epoch、SlotVersion、当前/终态指针、ordinal、完整加密收件、业务冲突键 | 只有工程投影，没有工程业务写；准确唯一键见第5节 |

完整物理列和类型入口为主文 879～918，索引 DDL 在 920～955；它们是 SQL Server 设计提案，未执行迁移。所有本地从表带 Tenant/Activity 复合 FK；已有源表有确认同库复合键才加对应 FK，不造跨库 FK。Head 与 Revision 不做循环非空 FK，统一事务维护当前指针。[完整物理模型][S879]

本地 ID 为 UUID 字符串；本地 Revision 为 Int64 十进制字符串；外部 Key/Version 最多128字符，Version 是 opaque，不能与 ContactRevision 取大小。UTC 精确到毫秒。JSON 拒绝重复/未知字段；NFC、trim、UTF-16 长度；必须有的数组空时传 `[]`，不能传 null。新人工 Content 1～4000，Reason 1～1000；历史 Content 可 null 或 4001～10000，受权读取时原样保留。请求256KiB；每类新增/移除最多50；显式可编辑窗口每类100，仅是单次窗口限制。[协议与值约束][S120]

Phone→Connected/NoAnswer/Failed，Email/Message→Sent/Failed，Meeting→Held/NoAnswer/Failed，Note→Recorded。非 Note 无默认 Outcome，切 Kind 必须清除不合法旧值。新外联 `EXTERNAL_INTERACTION` 至少一个已建 Contact；Phone/Email/Message 对每名参与者有相应 Channel/Purpose，Meeting 用 IN_PERSON 证明且无数字渠道。Contact 主锚的新外联必须包含该 Contact。`INTERNAL_NOTE` 的 Kind/Outcome 固定 Note/Recorded，participants=[]；主锚 Contact 不因此成为实际参与者。规范不承诺用 NLP 识别所有虚假分类。[事实矩阵][S149]

OccurredAt 不得晚于首次 RecordedAt；FACT 更正也受这个原时刻约束。ChangedAt 不得早于原 RecordedAt 或前一修订，否则 CLOCK_UNTRUSTED，不能偷偷调整时钟。NextContactAt 过去只警告，不产生任务或外联。[时间与分类][S159]

## 4. 状态流程与准确写入集合

本地 Composer 草稿不是业务实体；保存后 FactState=Committed，撤销后 Voided。ActivityRevision、ReferenceSetVersion、Contact 的 SetVersion、工程 ordinal/SlotVersion 是不同计数器，不互相取最大值。每次真实修订 SourceVersion 同步推进；CONTENT/FACT 若无引用变化继续原 SetVersion。[版本规则][S45]

| 操作 | 最终前置 | 成功写入与禁止效果 |
|---|---|---|
| 创建 | 当前主锚 ActiveRetained、RECORD；精确参与/对象/文件准入；首录时钟；唯一 O | 一 Head、一 CREATE Revision1、一 Set1、实际 Member/快照、Source、Audit、Outbox、Command；无附件不依赖 File；Account/Contact/Lead/Task/工程业务写0 |
| CONTENT | Committed、原主锚 ActiveRetained、COR-C 与字段范围；只改 Content/NextContact | 追加完整 CONTENT，保留事实/原作者/首次时刻/Set；不要求参与渠道现在仍 Allowed，纯叙述不是再次联系；无变化仅命令结果 |
| FACT preview | 读取当前完整参与集合；固定每个 ReferenceId；PrepareExisting 当前证明 | 返回完整依赖向量及短期封闭 token；不写 Head/Audit/Command/Outbox 或持久 pending FACT |
| FACT apply | 新意图检验当前 ETag、原 preview、所有固定绑定/新证明/Guard；旧 O 成功优先回放 | 新 FACT、新 FACT_EXISTING Evidence/Link、Source/Audit/Outbox/Command；旧 Member/原 Evidence/Set 不动；无变化不建 Link |
| VOID | 独立 VOID 能力、ActiveRetained、原因 | 追加 VOID，保留之前完整事实/叙述/Set；不硬删、不重算 Lead SLA、不取消工程；已 Voided 用最新条件新意图可 no-op，原 Key 回原结果 |
| 对象/附件引用修订 | 准确完整窗口 token、当前 Revision/Set、每项 keep/remove/add、全部实际准入 | 一 REFERENCE 修订＋新 Set＋Source/Audit/Outbox/Command；整意图全成或全拒，外部实体/文件内容写0 |

[登记事务][S416]、[内容和 FACT][S435]、[VOID][S471]、[引用写入][S484]。

FACT 不能重开选择器自动换当前电话/主要关系。服务从原 Member 构建 `PrepareExisting`：固定 ReferenceId、CreatedInRevision、历史 Contact/Relation/Channel/Value/Purpose 和原 Evidence，重新验证拟 OccurredAt 覆盖及当前 Contact/Policy/资格。只改显示姓名使 current ContactRevision 从12变13可以重新准备，历史仍12；原 Channel Value1 变当前 Value2 则拒绝 `FACT_EXISTING_VALUE_CHANGED`；原 Relation 不覆盖则 `FACT_REFERENCE_REBIND_REQUIRED`。预览后依赖再变就重预览，不以“无害变化”绕过当前向量。[重新准入][S443]

previewToken 自含完整 Request/Decision、依赖、固定绑定摘要、Actor/Tenant/Activity/Revision/Set/PlanDigest、认证引用和期限，最多5分钟且不超所有证据窗口；不能只存 preparationRef 或依靠进程内字典。超过256KiB拒绝，不能丢参与者或把一次 FACT 拆多次。Apply 已 UNKNOWN 时先查原 O，不能重准备换 token/Key。不可安全分步的 FACT＋引用重绑属于 G07，当前不启用。[封闭准备][S1091]

引用编辑将完整旧集合记 S0、实际可改窗口记 V；V 每个成员必须恰好出现一次 keep/remove，禁止省略即删除。`S1=(S0−remove)∪add`，未列出的隐藏成员由服务绑定保留；token 不授予隐藏写权。更换附件版本或参与绑定必须旧 Ref remove＋新 Ref add，不能修改稳定成员。Contact 主锚外联的必需参与者不可删，Note 不可加入参与者。[完整集合][S484]

## 5. API、事件与跨 Owner 合同

公开 API 基址 `/api/crm/v1`；严格 DTO 完整入口主文178～315，错误矩阵317～387。写命令必须有 Idempotency-Key；既有 Activity 写必须一个强 If-Match，拒绝 weak、`*`、多个值。EditETag 绑定可编辑上下文/当前授权/主锚资格，但不混入 now、工程回执或动态扫描状态；最终仍靠数据库 RowVersion/Guard 裁决。[公共 wire][S172]

| 方法和相对路径 | 输入/输出的重点 |
|---|---|
| POST `activity-options/context` | 主锚→版本/生命周期/时区/服务时间/能力/可写字段/依赖 Availability |
| POST `activity-options/participants/query`、`object-references/query`、`attachments/query` | 精确已存在来源身份；RECORD 与 REFERENCE_ADD 不同，后者绑定当前 Activity/Rev/Set |
| POST `activity-options/time-resolution` | 用户本地时间解析；DST 跳空拒绝、重叠需明确 offset，冻结 UTC 后恢复不重算 |
| POST `activities` | `CRM-ACTIVITY/1`、primary/expectedPrimaryVersion、recordClass、facts、content/nextContact、participants/objectReferences/attachments/timeContexts |
| GET `activities/{id}`（可 `revision`）、`edit-context` | 历史完整标量/当前能力；不从工程读取反推编辑权限 |
| POST `activities/{id}/content-corrections` | expectedRevision＋content/nextContact patch＋reason；区分字段未给与给 null |
| POST `activities/{id}/fact-corrections/preview`、`apply` | FactPlan→FactPreview；apply 只 plan＋previewToken，不重新提交任意参与者 |
| POST `activities/{id}/void` | expectedRevision＋reason |
| POST `activity-timeline/query`、`activities/{id}/revisions/query` | 宿主/筛选/视图/游标；按 Activity 去重再分页 |
| POST `activities/{id}/references/query`、`object-reference-plans/apply`、`attachment-plans/apply` | READ_ONLY 或明确 EDIT 模式；准确 keep/remove/add 与 preserveUnlisted=true |
| POST `activities/{id}/attachment-access` | 精确 ActivityRevision/Set/ReferenceId/FileVersion/Purpose；安全字节流授权 |
| GET `activities/{id}/engineering-source?sourceVersion=` | 精确旧源或当前源、原绑定结果、当前限制；只读 |
| GET `activity-commands/{operationKind}/{key}` | 原 O 状态、历史结果、当前适用性、可安全下一步 |

路径直接见主文321～342；内部工程端点是**绝对** `/internal/crm/v1/activity-engineering-bindings` 与 `/internal/crm/v1/activity-engineering-results`，不要再拼公开 API 基址。六种 operationKind 为 `activity.create/content.correct/facts.apply/void/references.apply/attachments.apply`。[完整路由][S321]

`CommandResult.outcome` 仅 COMMITTED/REJECTED_FINAL、scope=ENTITY_APPLY；未知是 Problem/Receipt 状态，不能向这个枚举随便加 UNKNOWN。Receipt 分离 historicalOutcome、outcomeScope、currentApplicability、canOpen。安全问题响应同时带 commitKnowledge 和 retryMode，客户端不能仅依 HTTP 状态决定重试。[命令与结果 DTO][S246]

**Contact 准入。** `ACT-CONTACT-ADMISSION/1` 区分 RECORD/REFERENCE_ADD，完整绑定 O、主体、精确主锚、Activity上下文、Contact/Relation/Channel/Value/Purpose、事实种类/时点和水位；返回 PREPARED_NOT_APPLIED。`ACT-EXISTING-PARTICIPANT/1` 为 FACT 固定历史引用准备，不能套创建 selector。Owner 批准的共同权威本地 Gate 必须支持最终撤权裁决；普通 GET/TTL 只是 UI 提示，不足则写503，无隐含 reserve/confirm 两阶段许可。[Contact Adapter][S1016]

**File 准入。** `ACT-FILE-REFERENCE/1` 绑定 Owner、O/意图摘要、primary/Activity/Set、ReferenceId、文件版本、Purpose、Decision、扫描/策略/保留水位及 Gate 证明。Add 要精确版本 Clean/current purpose；Retain 可以没有当前读取权或 Clean，但必须有保留许可；Remove 要精确 Unlink，不需要下载权，也不删除文件。单次意图所有 Add/Retain/Remove 全核，任何失败整意图0写。[文件操作差异][S509]

当前只启用 `LOCAL_AUTHORITATIVE_GUARD`：本地 Gate 本身是 File Owner 承认的实际撤权裁决点，业务共享持有与撤权排他互斥并持至最终提交。远端普通 GET、推测 TTL、远端 reserve/hold/confirm 均未获本版支持；不能把别域的通用 lease 自动视为本合同满足，缺证明确503。未来等价跨域原子协议需要后继明确批准。[File 最终裁决][S531]

**Contact 读取。** 既有 Contact ActivityPage 的原 `asRecorded` 仍按原参与→原主锚→原 OBJECT 优先级适配，同 Activity 只一行。原 Contact 主锚内部 Note 可 `asRecorded.contactId=PrimaryId`、关系/渠道全 null、非参与；只有后继才加入的 Contact 不能伪造原关联。当前接受 `ACT-CONTACT-READ/1.1` 允许 asRecorded=null，并新增 association/relationExplanations 及读取时点；须双方实际接线。尚用旧 DTO 遇到无法无损表达的分支应 Partial/nextCursor=null，而不是空且完整。[兼容与扩展][S666] [接受覆盖][U21]

**工程来源及结果。** SourceIdentity 包含精确 Tenant、Activity、SourceVersion、SetVersion、SourceDigest/KeyVersion。`ACT-SOURCE-CANON-1` 对不可变标量和按 ReferenceId 排序的精确成员作目的隔离 HMAC，不包含当前权限/文件名/临时许可/工程结果。PII 擦除后如受准许最小墓碑仍可验证则保留受限证明，否则 UNPROVEN，不能从最新 Owner 数据补造原源。[源证明][S726]

原请求 `B=(Tenant,RequestOwner,RequestKey)` 唯一绑定精确 Source；规范流 `(Producer,ContractVersion,Epoch)` 必须由原 Request Owner 明确批准，不能“第一个可信到达者”选流。一个 B 只一 Stream，generation固定1；另一 epoch/producer/contract 隔离。本版不支持换代，checkpoint 也不能换代。[Binding 合同][S780] [规范唯一性][S812]

结果先检查规范代次，再检查 ordinal/previous/checkpoint。INITIAL ordinal1/prev=null，连续结果 prev=current、ordinal=prev+1；同序同业务摘要去重，同序异内容即使旧 ordinal 低于当前也形成冲突。首次 ACCEPTED/REJECTED 固定六项 TerminalIdentity：outcome、OwnerReceipt、EngineeringId、EngineeringRevision、业务证据、OwnerRecordedAt；更大 ordinal 不能换工程身份、清终态或降级 Pending。Binding/Stream/Result/Inbox 及当前指针/SlotVersion 同事务更新。[结果序与终态][S826]

COR01：ReceivedTrust 的 producer/contractVersion/epoch 必须逐字段等于 Envelope 的 producer/schemaVersion/epoch；TransportKey=`Tenant+Producer+ContractVersion+Epoch+EventId`。COR02：ConflictKeyHash 的业务原像包括 naturalBindingHash、规范三元组、ordinal、conflictKind、原/入站业务摘要，排除 EventId/到达时间；唯一 Tenant/ConflictKeyHash。同一业务冲突换运输 ID 只加别名，不能反复推进 SlotVersion。[补正][S3423]

当前源只汇总“已观察且授权的请求集合”：空→UNCONFIRMED；任一冲突→CONFLICT；缺证或缺前驱→UNKNOWN；全 Accepted→ACCEPTED_FOR_EXACT_SOURCE；全 Rejected→REJECTED；仅Pending→PENDING；混合→MIXED_RESULTS。旧源成功不确认新源，隐藏请求不泄露数量；汇总不是工程执行许可。[汇总规则][S838]

每次真实修订仅一个 `crm.activity.changed.v1` 最小事件，安全身份/版本/字段代码，不携正文/渠道PII；工程结果消费不再产生活动业务变更事件。Outbox 至少一次，设计重试1/5/30/120/600秒、30秒lease、20次dead为技术默认，不是业务额度。[事件入口][S1136]

## 6. 事务、幂等、并发与恢复

最终写锁序为权威 Gate→原 O→Account IDs→Contact IDs→子项→Activity→Member/Audit/Outbox/Command→TimelineScope kind/id；File Gate 在前，不允许反向抓锁。根锁内重新核旧版/完整集合/当前准入，所有本地业务效果同一次事务提交。其他服务不可各提交同一意图的一部分。[锁序][S962]

`ACT-CMD-CANON-1` 明確规范 method/path/schema/body/IfMatch/PlanToken、NFC/trim、UUID小写、UTC毫秒、Ordinal键排序、数组顺序、PATCH字段存在性，并作目的隔离 HMAC。原 O 同摘要返回原结果；异摘要冲突。先查原成功再处理新时效，不能因原 preview 现已过期抹掉历史成功。[命令原像][S970]

新 O 的占位在事务内、未提交，finality=`ACTIVITY_LOCAL_ENTITY`；这是本地实体裁决，不是跨 Owner fence。所有前置通过才业务写；回滚后不可复用仍追踪业务修改的 DbContext 写最终拒绝。commit 断开后用新 context 查原 O；查不到仍 NOT_OBSERVED/UNKNOWN，不证明未发生。客户端在原 tab 冻结原 Key/正文/UTC/IfMatch/token，1/2/5秒查询后手动；无自动 cancel-unobserved 或改 Key 再建。[执行与未知][S974]

跨刷新 public receipt/ReceiptHandle、服务 Owner fence、保留协议仍 G06，不能将本地 `ACTIVITY_LOCAL_ENTITY` 包装成 RequiredFenced 已接。最小 O 证据不能任意 TTL 清理；查询结果“原成功但当前失权/Inactive”同时保留两事实，限制打开资源。[保留与恢复][S991]

工程 Inbox 要持久化完整加密原载荷及接收信任，安全索引/摘要不足以重放。未知 Binding 且源存在→WAITING_BINDING，未知 Source→隔离；不根据结果消息自动创建 Binding。worker 重启续办必须解密原消息、重新核信任/规范流/前驱，并处理内部 waiting，不因 Transport 已存在提前当重复退出。缺原文/密钥/信任不造 READY；本地 Inbox 闭合也不自动关闭其他 IssueScope。[可靠收件][S860]

## 7. 权限、租户、隐私与下载

宿主权限与原锚权同时成立；历史读取不要求 Active/编辑，但每次核当前字段与 PII 权；写需 ActiveRetained 和独立动作能力。R/Q、VOID、FACT、附件 Unlink/Download 权限分开，不以“能看”推出“能改”，不以作者身份推出管理权。沿用既有 crm-account 等资源动作准确映射，不自建 crm-activity 万能资源，不以 admin fallback 填空。[权限][S70]

所有本地唯一域/FK/查询先有 Tenant。隐藏目标不返回名称、精确计数或可推断集合 token 内容；redacted unknown 与 false、null 与独立来源需区分。history/search/export不能还原已限制PII；日志只记录安全O/事件身份、operation、结果码/耗时，不记录正文、proof、Authorization、文件字节或敏感 SQL 参数。[历史保护][S660] [日志约束][S3182]

附件下载先证明 Reference 真属于所选 ActivityRevision/Set；允许受权读取 Inactive/Voided 历史，但仍需当前 DL/scan/Owner Gate、精确版本和数据哈希，安全审计在字节前成功。流中撤权应停止后续字节，不能假称已经全部送达，也不能声称召回已发字节。引用保留、解除和下载是三项不同决策。[下载协议][S548]

当前 privacyState 由已配置隐私 Owner、主锚与修订 PiiRedacted 投影，UNKNOWN 关闭；“不可变事实”不等永久保存PII。隐私擦除需原 Owner 授权并保留允许的墓碑，不能自行建立第二隐私账。[持久隐私][S904]

## 8. 页面、交互与时间线

继续使用原 Account/Contact 详情 tab 和 Drawer，不新增全局门户。Composer 先取 context，再选准确 Contact/关系/渠道/用途；非 Note Outcome 必填，改 Kind 清非法旧值；时区解析后冻结 UTC。无附件的文本登记不因 File 未配置整体关闭；真实外联条件不足则明确阻塞。页面分本地草稿、读取缓存、冻结命令三作用域，PII 不进全局持久 store。[页面入口][S63] [组件责任][S3152]

列表成员命中只由固定 Primary 或保存的 PARTICIPANT/OBJECT 证明；Contact 现主要账户变化不会把所有活动迁往新账户。S0=原 Revision1 的Set，Sc=所选当前Set。PRIMARY 永远按原锚；原成员可提供 AS_RECORDED 关联；后继新增成员只属于当前更正视图，不可伪造原来已关联。一个 Activity 多理由先合并再分页，原实际参与只来自 S0 PARTICIPANT。[宿主成员矩阵][S573]

默认 RecordedAt 倒序＋小写 UUID BIN2 倒序稳定补序，view=AS_RECORDED、includeVoided=false、pageSize30；发生时刻排序用当前事实。即使 AS_RECORDED，行的当前事实/内容和当前 Voided 筛选仍须准确，原事实单独呈现。AS_RECORDED 关系解释用原绑定时点；CURRENT_RELATION 用同 RelationId 的当前头/响应读取时点；CORRECTED_INTERPRETATION 用同当前头/更正后 OccurredAt。没有 relation 不拿当前 primary 补造。[显示与排序][S634]

cursor 绑定 Tenant/Subject/host/完整筛选/顺序/view/pageSize/ActivityGeneration/AccessStamp/原QueryAsOf/边界。Activity 数据或权限变更使 cursor stale；纯 Contact 关系解释变化不改 ActivityGeneration，可跨页看到注明水位的解释变化。每页内部采用同一个关系快照。外部 Lead/Opp 来源无稳定覆盖→Partial、nextCursor=null；只有关系解释暂不可用时，可在已证 Activity seek 范围继续并声明 Partial。[分页][S642]

Content、更正预览、引用窗口、Voided、旧源/当前源/旧受理/当前限制各用明确面板。UNKNOWN 独立显示原命令状态和查询入口，不能导航成成功；过期/撤权清表单和缓存中的禁止信息。状态有文字，键盘焦点、关闭回焦、200%缩放和长文案为设计验收项。[交付界面][S3158]

Contact 导出必须消费“RecordedThrough 前最新的准确 Revision，再按该 Revision 的 OccurredAt 过滤”的固定读取结果，使用可恢复源 cursor/证明。不能循环当前 timeline 生成历史导出；旧 Artifact 字节不因后来 Void 改写；本域不另建 CSV/导出 Owner。未支持 Contact 边缘不能报 COMPLETE。[导出消费][S715]

## 9. 实现映射与当前源码差异

本组没有重新审计 Activity 源码；已确认任务基线为 `90c871fe571fd6b390f53e8678376d7ce60bcb60`，该基线本身不证明规范已经实现。当前能明确的是 G01 尚要求确认现框架、DbContext/SQL Server Provider、已有 Activity/Reference/Audit 设施与迁移约束，文档列名不能当成已存在数据库表。[实现 Gate][S3322]

推荐实现责任已由原规范给出：Controller 严格绑定；Application 统一编排证据/事务；Domain 处理状态/期间/字段白名单/集合不变量；Adapter 对接实际 Owner。ActivityQueryService、ActivityCommandExecutor、ActivityCreate/Correction/Reference/AttachmentAccess/SourceReader/EngineeringReceiptConsumer 可作为职责划分，Domain 不直连 HTTP，禁止服务各自提交一次原子意图的局部。[开发职责][S3150]

实施顺序是先完整 wire/错误和真实 IAM/Gate 映射，再持久事实/O/修订一致性，再创建/双宿主读取，再更正/VOID，再引用与字节访问，再工程来源收件，最后 Contact/Lead/Export 接线及真实竞争恢复。前端可在明确 fixture 上并行，但最终不能用 EF InMemory/单进程锁替代数据库事务，也不能将生产 fallback 到 mock。[八项开发任务][S3158]

## 10. 验收设计与执行状态

保留4 SPEC、13 OP、51原业务要求；加8项MD、16项REV1、3项COORD，共78项全部 `NOT_RUN`，业务执行0。复审中56段 JSON 严格解析成功只证明文档语法，未运行 TypeScript/C#、SQL、HTTP、真实签名、Inbox worker 或 Owner 协议。本次整理同样未执行这些业务操作。[评审计数][R84]

| 应落地的可观察验证 | 必须检查的效果 |
|---|---|
| 首录、同O同/异载荷、响应丢失 | 一事实/一首录时刻；同O回原，异载荷409；UNKNOWN不另建；多个服务实例不能双提交 |
| CONTENT、FACT、VOID | 内容纠正不动 Lead SLA/事实；旧预览/撤权零修订；历史 Actor/RecordedAt不变；Voided不硬删不外域级联 |
| Note 双宿主 | Contact主锚Note零参与Member；旧DTO安全适配；后继才关联使用扩展或明确Partial |
| FACT current13/historical12 | 重新证明可成功；Value变更/关系不覆盖/预览后变化零FACT；只新增本次Evidence/Link，旧Member原证据不变 |
| 引用与附件混合拒绝 | 无下载权可有权保留/解绑；任一Add非法不能先移除旧项；所有旧Ref/Set完整保留；File二进制写0 |
| 工程收件 | e1Accepted后e2Pending隔离；同序异内容保留冲突；相同业务冲突换EventId不重复Slot；旧Source受理不确认当前Source |
| 故障/重启/多worker | 原加密body+信任足以重判WAITING；缺前驱不越过；缺载荷/密钥不READY；冲突域不互相代关 |
| 当前PII、cursor、下载 | 历史字段失权不泄漏；纯关系解释不变宿主/计数；授权变cursor失效；下载撤权停流且不假称完整 |

原51项 Given/When/Then 在3197～3254，技术反例3255～3298；两条连续业务链的断言见1383、1896、2188、2480、2631、2758、3025及3131～3143。本轮逐条读了这些叙述/断言；链中56段 JSON 样例未逐字段全读，不能将其记为本轮完整 fixture 审查。[原AC][S3197] [连续链入口][S1141]

非功能值是目标：SQL命令5秒/API预算10秒/客户端15秒；详情p95≤500ms、首30条≤1秒、无远端等待本地写p95≤1秒，基于给定机器/负载；限流初拟查询120、selector30、写30、RQ120次/分。尚未压测，不能因 DDL 或配置存在宣称达标。[NFR边界][S3169]

连续请求／回包的编码检查点如下；这些是完整合成样例，不能当实际验收结果。

| 场景 | 持续身份与字段规则 | 精确入口 |
|---|---|---|
| 创建外联 | options/context 的 `primary.version=acc-q12` 用于 `expectedPrimaryVersion`，不是未来Activity ETag；选择器的 clientRef 只作临时标签，201的 idMap 才映射永久参与Member。1次create得到 revision／ReferenceSetVersion／SourceVersion 均1。 | [原文 L1158](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/67/67dc3667fb852c56__CRM-ACT-01_互动记录与修订追踪_前后端开发Spec_v1.1.1_REVIEW.md:1158>)、[原文 L1270](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/67/67dc3667fb852c56__CRM-ACT-01_互动记录与修订追踪_前后端开发Spec_v1.1.1_REVIEW.md:1270>)、[原文 L1360](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/67/67dc3667fb852c56__CRM-ACT-01_互动记录与修订追踪_前后端开发Spec_v1.1.1_REVIEW.md:1360>) |
| 更正内容→撤销 | content-corrections 只推进 revision/sourceVersion 到2，Set仍1；重新读 edit-context/ETag 后void到3，默认列表为空、history仍三修订且只有一个事实。原Actor/RecordedAt不变。 | [原文 L1597](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/67/67dc3667fb852c56__CRM-ACT-01_互动记录与修订追踪_前后端开发Spec_v1.1.1_REVIEW.md:1597>)、[原文 L1694](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/67/67dc3667fb852c56__CRM-ACT-01_互动记录与修订追踪_前后端开发Spec_v1.1.1_REVIEW.md:1694>)、[原文 L1802](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/67/67dc3667fb852c56__CRM-ACT-01_互动记录与修订追踪_前后端开发Spec_v1.1.1_REVIEW.md:1802>) |
| Contact内部Note | `primary.kind=Contact`、`recordClass=INTERNAL_NOTE`、Note/Recorded，participants=[]；Contact adapter的asRecorded.contactId来自Head.PrimaryId，relationId/relationRevision/channelId/valueRevision全null，不造外联或用途Allowed。撤销后仍一个空Set、零Member。 | [原文 L1929](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/67/67dc3667fb852c56__CRM-ACT-01_互动记录与修订追踪_前后端开发Spec_v1.1.1_REVIEW.md:1929>)、[原文 L2099](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/67/67dc3667fb852c56__CRM-ACT-01_互动记录与修订追踪_前后端开发Spec_v1.1.1_REVIEW.md:2099>)、[原文 L2188](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/67/67dc3667fb852c56__CRM-ACT-01_互动记录与修订追踪_前后端开发Spec_v1.1.1_REVIEW.md:2188>) |
| 历史绑定的FACT更正 | 浏览器仅提交FactPlan；服务从持久Member重建 PrepareExisting，历史Contact12与当前13并存。新Decision/Guard须覆盖固定Value1/R1和修正发生时点；成功只追加FACT AdmissionLink、revision/source到2，Set1及历史绑定不改。旧创建handle过期不是拒绝该路线的理由。 | [原文 L2822](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/67/67dc3667fb852c56__CRM-ACT-01_互动记录与修订追踪_前后端开发Spec_v1.1.1_REVIEW.md:2822>)、[原文 L2877](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/67/67dc3667fb852c56__CRM-ACT-01_互动记录与修订追踪_前后端开发Spec_v1.1.1_REVIEW.md:2877>)、[原文 L3025](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/67/67dc3667fb852c56__CRM-ACT-01_互动记录与修订追踪_前后端开发Spec_v1.1.1_REVIEW.md:3025>) |
| v1回执晚于v2事实 | 回调source完整引用v1，投影APPLIED/appliedSourceVersion=1而observedCurrentSourceVersion=2；v1的ACCEPTED成立，v2的currentSourceAcceptance仍UNCONFIRMED、automaticResubmission=false。 | [原文 L2484](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/67/67dc3667fb852c56__CRM-ACT-01_互动记录与修订追踪_前后端开发Spec_v1.1.1_REVIEW.md:2484>)、[原文 L2521](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/67/67dc3667fb852c56__CRM-ACT-01_互动记录与修订追踪_前后端开发Spec_v1.1.1_REVIEW.md:2521>)、[原文 L2609](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/67/67dc3667fb852c56__CRM-ACT-01_互动记录与修订追踪_前后端开发Spec_v1.1.1_REVIEW.md:2609>) |
| 附件混合操作全拒 | 下载无权仍可有canRetain/canRemove；keep Fkeep、remove Fold、add Fnew是一个意图。Fnew SCAN_PENDING导致409/NOT_APPLIED_FINAL/NEW_INTENT_AFTER_RELOAD，全Set保持1，Fold不先删，Fkeep不因无读权删。 | [原文 L2635](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/67/67dc3667fb852c56__CRM-ACT-01_互动记录与修订追踪_前后端开发Spec_v1.1.1_REVIEW.md:2635>)、[原文 L2712](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/67/67dc3667fb852c56__CRM-ACT-01_互动记录与修订追踪_前后端开发Spec_v1.1.1_REVIEW.md:2712>)、[原文 L2739](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/67/67dc3667fb852c56__CRM-ACT-01_互动记录与修订追踪_前后端开发Spec_v1.1.1_REVIEW.md:2739>) |
| 第二epoch不得另立头 | 已授权PLM_OWNER/ACT-ENGINEERING/1/plm-e1后，同服务plm-e2初始PENDING在ordinal比较前409 RESULT_GENERATION_UNSUPPORTED。旧Accepted不降级；BindingSlotVersion从2到3因冲突记录，terminalIdentity保留而resultUsability=CONFLICT。 | [原文 L3031](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/67/67dc3667fb852c56__CRM-ACT-01_互动记录与修订追踪_前后端开发Spec_v1.1.1_REVIEW.md:3031>)、[原文 L3092](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/67/67dc3667fb852c56__CRM-ACT-01_互动记录与修订追踪_前后端开发Spec_v1.1.1_REVIEW.md:3092>)、[原文 L3119](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/67/67dc3667fb852c56__CRM-ACT-01_互动记录与修订追踪_前后端开发Spec_v1.1.1_REVIEW.md:3119>) |

## 11. 尚未确认事项与整合动作

| 项 | 具体尚缺 | 影响与保留结论 |
|---|---|---|
| G01 现CRM实现 | 实际框架/DbContext/既有表/索引/迁移映射 | 先对固定源码SHA核差，不并建重复主账 |
| G02 IAM/资格Owner | 真实资源动作/字段/范围映射、可信时钟、共同Gate、所有writer锁序、PrepareExisting撤权窗口 | 当前设计确定，真实新登记/FACT/引用准入仍未验证 |
| G03 File | 精确版本Add/Retain/Unlink/Download、实际权威本地Gate、流中撤权 | 无附件独立路径可实现；有附件写/字节下载不能假定通用租约已满足 |
| G04 CRM-HO/PLM | 原请求批准规范三元组、SourceAttestation、序/前驱/checkpoint、终态/信任/保留实例 | 保持UNCONFIRMED/隔离，不新增工程Submit或自动换代 |
| G05 安全基础 | Inbox加密/密钥、接收信任保留、目的隔离HMAC、PII保留擦除、安全审计可用性 | 无法解密原证据不能虚构续办成功 |
| G06 RQ/Lead/Export | public receipt/ReceiptHandle授权、跨刷新/fence保留、Lead稳定查询、导出范围窗口 | 原tab原意图恢复与Partial保留；不补自创cancel/CSV |
| G07 复合治理 | FACT同时需要不兼容参与重绑且无合法中间态 | 当前NOT_ENABLED，返回明确阻塞；需要新范围决定，不能暗作两次原子事务 |
| G08 Contact读取 | 已接受1.1扩展实际双方接线、后继关联/多关系解释消费 | 无需再次批准同一设计；旧Contact原件保持，旧形状无法表达时Partial |

这些原件缺口在3320～3329，UA 19～25覆盖了旧“待接受”表述。[原Gap表][S3318] [UA覆盖][U19]

整合优先核准入与撤权最终裁决、准确O/源身份、Contact读取差异，再做工程/导出跨域场景。本文的顺序是整理分析建议，不代替用户新增业务决定；尤其不把 G07、自动 epoch 换代或跨域原子协议列为已经授权的功能。

## 12. 原文定位、实际阅读与剩余范围

本轮主文3434行已全部语义读取：原核心与全部非fenced叙述之外，补齐1149～3129全部56段JSON样例，逐字段核对创建、更正、Note、固定参与绑定、晚回执、附件全拒和epoch冲突。准确阅读行段和文件SHA见[商业阅读证据](<D:/CP6/docs/CP6_开发设计文档_20261010/evidence/commercial-reading.json>)。CURRENT23行、UA64行、限定复审123行全文读取；另一个git-object复审表示不拿来冒第二次全文。所有fixture证明名、示例摘要与成功回包均非真实签发或新执行。

本文状态 `required_materials_consolidated`，指当前主文、准确接受／复审及必要选择材料语义归并。R01／R02／R03账户共享历史附件已有ACC本轮独立全读记录，仍按ACT当前替换关系采用；不是另一个ACT实现版本。历史代码及工程／文件Owner全部实现未因此完成审计。G07继续NOT_ENABLED，其他真实Owner采用和78AC运行仍未证明。

关联模块：[账户](<D:/CP6/docs/CP6_开发设计文档_20261010/modules/CRM-ACC-01.md>)、[联系人](<D:/CP6/docs/CP6_开发设计文档_20261010/modules/CRM-CON-01.md>)、[线索](<D:/CP6/docs/CP6_开发设计文档_20261010/modules/CRM-LEAD-01.md>)。后继需要对 [商业交接](<D:/CP6/docs/CP6_开发设计文档_20261010/modules/CRM-HO-01.md>) 和工程Owner确认原请求/来源/信任合同；模块链接不代表对应后继文档已完成。

[U19]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/19/192cc33a7bda8b5c__HISTORY__handoffs__PLAN-REUSE-01B__documentation__MD02__acceptances__A-CRM__CRM-ACT-01__UA-20260930-A-CRM-ACT-MD01.md.txt:19>
[S29]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/67/67dc3667fb852c56__CRM-ACT-01_互动记录与修订追踪_前后端开发Spec_v1.1.1_REVIEW.md:29>
[S31]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/67/67dc3667fb852c56__CRM-ACT-01_互动记录与修订追踪_前后端开发Spec_v1.1.1_REVIEW.md:31>
[S74]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/67/67dc3667fb852c56__CRM-ACT-01_互动记录与修订追踪_前后端开发Spec_v1.1.1_REVIEW.md:74>
[U28]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/19/192cc33a7bda8b5c__HISTORY__handoffs__PLAN-REUSE-01B__documentation__MD02__acceptances__A-CRM__CRM-ACT-01__UA-20260930-A-CRM-ACT-MD01.md.txt:28>
[R6]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cf/cf421e167aff1f20__SR-20260930-A-CRM-ACT-MD02_限定复审与接受建议.md:6>
[S879]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/67/67dc3667fb852c56__CRM-ACT-01_互动记录与修订追踪_前后端开发Spec_v1.1.1_REVIEW.md:879>
[S120]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/67/67dc3667fb852c56__CRM-ACT-01_互动记录与修订追踪_前后端开发Spec_v1.1.1_REVIEW.md:120>
[S149]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/67/67dc3667fb852c56__CRM-ACT-01_互动记录与修订追踪_前后端开发Spec_v1.1.1_REVIEW.md:149>
[S159]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/67/67dc3667fb852c56__CRM-ACT-01_互动记录与修订追踪_前后端开发Spec_v1.1.1_REVIEW.md:159>
[S45]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/67/67dc3667fb852c56__CRM-ACT-01_互动记录与修订追踪_前后端开发Spec_v1.1.1_REVIEW.md:45>
[S416]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/67/67dc3667fb852c56__CRM-ACT-01_互动记录与修订追踪_前后端开发Spec_v1.1.1_REVIEW.md:416>
[S435]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/67/67dc3667fb852c56__CRM-ACT-01_互动记录与修订追踪_前后端开发Spec_v1.1.1_REVIEW.md:435>
[S471]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/67/67dc3667fb852c56__CRM-ACT-01_互动记录与修订追踪_前后端开发Spec_v1.1.1_REVIEW.md:471>
[S484]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/67/67dc3667fb852c56__CRM-ACT-01_互动记录与修订追踪_前后端开发Spec_v1.1.1_REVIEW.md:484>
[S443]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/67/67dc3667fb852c56__CRM-ACT-01_互动记录与修订追踪_前后端开发Spec_v1.1.1_REVIEW.md:443>
[S1091]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/67/67dc3667fb852c56__CRM-ACT-01_互动记录与修订追踪_前后端开发Spec_v1.1.1_REVIEW.md:1091>
[S172]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/67/67dc3667fb852c56__CRM-ACT-01_互动记录与修订追踪_前后端开发Spec_v1.1.1_REVIEW.md:172>
[S321]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/67/67dc3667fb852c56__CRM-ACT-01_互动记录与修订追踪_前后端开发Spec_v1.1.1_REVIEW.md:321>
[S246]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/67/67dc3667fb852c56__CRM-ACT-01_互动记录与修订追踪_前后端开发Spec_v1.1.1_REVIEW.md:246>
[S1016]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/67/67dc3667fb852c56__CRM-ACT-01_互动记录与修订追踪_前后端开发Spec_v1.1.1_REVIEW.md:1016>
[S509]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/67/67dc3667fb852c56__CRM-ACT-01_互动记录与修订追踪_前后端开发Spec_v1.1.1_REVIEW.md:509>
[S531]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/67/67dc3667fb852c56__CRM-ACT-01_互动记录与修订追踪_前后端开发Spec_v1.1.1_REVIEW.md:531>
[S666]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/67/67dc3667fb852c56__CRM-ACT-01_互动记录与修订追踪_前后端开发Spec_v1.1.1_REVIEW.md:666>
[U21]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/19/192cc33a7bda8b5c__HISTORY__handoffs__PLAN-REUSE-01B__documentation__MD02__acceptances__A-CRM__CRM-ACT-01__UA-20260930-A-CRM-ACT-MD01.md.txt:21>
[S726]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/67/67dc3667fb852c56__CRM-ACT-01_互动记录与修订追踪_前后端开发Spec_v1.1.1_REVIEW.md:726>
[S780]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/67/67dc3667fb852c56__CRM-ACT-01_互动记录与修订追踪_前后端开发Spec_v1.1.1_REVIEW.md:780>
[S812]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/67/67dc3667fb852c56__CRM-ACT-01_互动记录与修订追踪_前后端开发Spec_v1.1.1_REVIEW.md:812>
[S826]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/67/67dc3667fb852c56__CRM-ACT-01_互动记录与修订追踪_前后端开发Spec_v1.1.1_REVIEW.md:826>
[S3423]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/67/67dc3667fb852c56__CRM-ACT-01_互动记录与修订追踪_前后端开发Spec_v1.1.1_REVIEW.md:3423>
[S838]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/67/67dc3667fb852c56__CRM-ACT-01_互动记录与修订追踪_前后端开发Spec_v1.1.1_REVIEW.md:838>
[S1136]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/67/67dc3667fb852c56__CRM-ACT-01_互动记录与修订追踪_前后端开发Spec_v1.1.1_REVIEW.md:1136>
[S962]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/67/67dc3667fb852c56__CRM-ACT-01_互动记录与修订追踪_前后端开发Spec_v1.1.1_REVIEW.md:962>
[S970]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/67/67dc3667fb852c56__CRM-ACT-01_互动记录与修订追踪_前后端开发Spec_v1.1.1_REVIEW.md:970>
[S974]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/67/67dc3667fb852c56__CRM-ACT-01_互动记录与修订追踪_前后端开发Spec_v1.1.1_REVIEW.md:974>
[S991]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/67/67dc3667fb852c56__CRM-ACT-01_互动记录与修订追踪_前后端开发Spec_v1.1.1_REVIEW.md:991>
[S860]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/67/67dc3667fb852c56__CRM-ACT-01_互动记录与修订追踪_前后端开发Spec_v1.1.1_REVIEW.md:860>
[S70]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/67/67dc3667fb852c56__CRM-ACT-01_互动记录与修订追踪_前后端开发Spec_v1.1.1_REVIEW.md:70>
[S660]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/67/67dc3667fb852c56__CRM-ACT-01_互动记录与修订追踪_前后端开发Spec_v1.1.1_REVIEW.md:660>
[S3182]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/67/67dc3667fb852c56__CRM-ACT-01_互动记录与修订追踪_前后端开发Spec_v1.1.1_REVIEW.md:3182>
[S548]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/67/67dc3667fb852c56__CRM-ACT-01_互动记录与修订追踪_前后端开发Spec_v1.1.1_REVIEW.md:548>
[S904]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/67/67dc3667fb852c56__CRM-ACT-01_互动记录与修订追踪_前后端开发Spec_v1.1.1_REVIEW.md:904>
[S63]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/67/67dc3667fb852c56__CRM-ACT-01_互动记录与修订追踪_前后端开发Spec_v1.1.1_REVIEW.md:63>
[S3152]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/67/67dc3667fb852c56__CRM-ACT-01_互动记录与修订追踪_前后端开发Spec_v1.1.1_REVIEW.md:3152>
[S573]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/67/67dc3667fb852c56__CRM-ACT-01_互动记录与修订追踪_前后端开发Spec_v1.1.1_REVIEW.md:573>
[S634]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/67/67dc3667fb852c56__CRM-ACT-01_互动记录与修订追踪_前后端开发Spec_v1.1.1_REVIEW.md:634>
[S642]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/67/67dc3667fb852c56__CRM-ACT-01_互动记录与修订追踪_前后端开发Spec_v1.1.1_REVIEW.md:642>
[S3158]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/67/67dc3667fb852c56__CRM-ACT-01_互动记录与修订追踪_前后端开发Spec_v1.1.1_REVIEW.md:3158>
[S715]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/67/67dc3667fb852c56__CRM-ACT-01_互动记录与修订追踪_前后端开发Spec_v1.1.1_REVIEW.md:715>
[S3322]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/67/67dc3667fb852c56__CRM-ACT-01_互动记录与修订追踪_前后端开发Spec_v1.1.1_REVIEW.md:3322>
[S3150]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/67/67dc3667fb852c56__CRM-ACT-01_互动记录与修订追踪_前后端开发Spec_v1.1.1_REVIEW.md:3150>
[R84]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cf/cf421e167aff1f20__SR-20260930-A-CRM-ACT-MD02_限定复审与接受建议.md:84>
[S3197]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/67/67dc3667fb852c56__CRM-ACT-01_互动记录与修订追踪_前后端开发Spec_v1.1.1_REVIEW.md:3197>
[S1141]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/67/67dc3667fb852c56__CRM-ACT-01_互动记录与修订追踪_前后端开发Spec_v1.1.1_REVIEW.md:1141>
[S3169]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/67/67dc3667fb852c56__CRM-ACT-01_互动记录与修订追踪_前后端开发Spec_v1.1.1_REVIEW.md:3169>
[S3318]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/67/67dc3667fb852c56__CRM-ACT-01_互动记录与修订追踪_前后端开发Spec_v1.1.1_REVIEW.md:3318>
