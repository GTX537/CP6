# CRM-CON-01 独立联系人与账户关系

整理状态：`core_semantics_consolidated`。当前接受版本是 v1.1.1，SHA-256 `6a000d26d7a638a47000b85c6f97c1b96a9efd29da7fe2650d2f8859672d4407`，245,926 bytes / 2,573 行。正文文件名和末尾的 REVIEW/待接受是编制时状态，后继 UA 明确接受完整开发 Spec，包括 API/DTO、数据模型、页面、事务和三项 COR 补正。文本接受不证明生产 Owner 接入、数据库适配或业务测试完成。[当前接受][U7] [状态覆盖][U25]

## 1. 目的、操作者与事实权威

销售人员可以先建立一个零账户关系、零渠道的联系人，再维护身份/归属/状态、多个账户关系及主要期间、渠道与用途限制，读取这个人参与的原始互动及历史导出结果。Contact 是独立个人关系对象，不是 IdentityUser、Account 或 Main BusinessPartner。Contact 自己的 Department/Owner、对象范围、PII 权限不继承 AccountOwner。[产品目标][S20] [对象范围][S35]

本模块拥有 Contact、关系和主要期间 Head/Revision、渠道值修订、用途策略及受控证据引用、本域命令账/审计/Outbox；消费 Activity 和 ExportOwner 的事实，不复制其主账。Contact 页不提供 Activity 新增、更正、Void/Unvoid，不重算 Lead 首响，不建 BP、订单、工程请求，不在 Controller 生成 CSV。Account 或 Contact 停用不会自动结束关系；恢复不复活擦除值、退役渠道、已结束关系或失效许可。[明确排除][S55]

正常开发演示链：独立创建 C → 加甲乙两关系 → 甲设主要 → 明确“仅切主要”至乙 → 甲关系继续存在 → 原 Activity 仍引用当时甲的原关系修订。当前主要改变不转移过去互动。[首条闭环][S22]

## 2. 当前设计、接受与覆盖顺序

| 来源 | 当前作用 |
|---|---|
| R01+R02+R03+CR09、UA-20260929-A-CRM-01 | 原四 SPEC、19 OP、49 Contact 场景及 DEC01/02；DEC03/04本基线不启用 |
| v1.1.1 当前完整主文 | 唯一当前联系人开发设计，采用附件133345Z体系并明示与另一个133912Z稿的差异，不能拼接两套路径/字段/分页参数 |
| SR-20260929-A-CRM-MD02 | `MAJOR_REVISIONS_VERIFIED_WITH_EXPLICIT_COORDINATOR_CORRECTIONS`；F01–F04主体核实，COR01–03已并入主文 |
| UA-20260929-A-CRM-CON-MD01 / CURRENT | `USER_ACCEPTED_DEVELOPER_SPEC`，接受完整准确字节；G01–G08及九 RFC保留；实现与75设计条目业务测试未执行 |

当前采用 `ownerId/departmentText/lifecycle`、`relationship-plans/apply`、单 SetVersion、30/60/100分页和256KiB保护。另稿 `ownerUserId/organizationUnitText/status`、`relation-plans/commit`、多套集合水位和20/50/100/64KiB不是可随意混用的同版别名。当前字段/参数来自已接受文本；其中被明确标为 DRAFT 的性能、限流、超时参数仍是实施评估起点，不改称生产 SLA。[两稿差异][S2529] [接受内容][U31]

COR01绑定读取时点/查询时点/到期；COR02连续结束RB示例必须显式撤未来PC；COR03持久化完整消息和认证证据、区别运输去重和内部等待续办。UA 已接受这些补正，无需再申请主体v1.2或ACK。G07停用期间归属失效的特殊修复仍未批准，不因整份文本接受开放 Inactive assign。[三补正][U35] [限定复审][R32]

## 3. 数据与身份：不可混用的版本和期间

### 3.1 Contact 根与输入约束

| 字段 | 定义 |
|---|---|
| `(TenantId,Id)` | 服务端随机 UUID 建议；前端 opaque，不增加联系人业务编号；复合租户主键 |
| displayName | 1–200 UTF-16 code units，NFC/trim后必填；不是唯一键 |
| givenName/familyName/title/departmentText | 各可空、最多100；任职部门文本不同于组织 DepartmentId；不自动拆姓名 |
| departmentId/ownerId | Department必需有效；Owner可为明确合法null，否则必须有效成员且属目标部门；归属专门命令维护 |
| notes / reason | notes可空2000；动作原因1–1000，受敏感字段保护，不进入普通日志 |
| lifecycle/privacyState | ACTIVE/INACTIVE 与 RETAINED/ANONYMIZED/UNKNOWN 分轴；创建须有本地政策才能RETAINED，客户端不得指定 |
| revision/setVersion | 本地 Int64 十进制字符串；根revision从1起，每次实际本域业务变化+1；关系/主要集合setVersion从0起，一整计划只+1 |
| RowVersion / EditContext ETag | SQL最终条件更新令牌 / 当前操作者编辑上下文强tag，不能替代业务修订或权限水位 |
| redactedFields/capabilities/accessStamp | 当前输出说明，非业务实体列；null+redactedFields表示受限而非未填写 |

先严格验证合法Unicode，再NFC/trim，长度按UTF-16；单行禁止控制符/换行，notes/reason可换行但禁NUL。创建遗漏/空白可选文本→null；PATCH遗漏不改、显式null仅清可空字段；数组缺省依创建规则为[]，显式null拒绝。拒重复/未知JSON成员及类型强转，尤其 tenantId/id/lifecycle/privacyState/revision/createdBy。请求256KiB、初始关系/渠道各20、单关系计划100变更是单请求保护，不是永久业务容量。[输入规则][S175] [字段][S185]

### 3.2 关系、主要、渠道与证据身份

| 概念 | 精确身份与语义 |
|---|---|
| 关系 | `(T,C,RelationId)` Head指当前不可变RelationRevision；每版含AccountId、Kind、[RelatedFrom,RelatedTo)、Evidence、Supersedes及登记元数据 |
| 主要期间 | 独立PrimaryId/Revision，绑定RelationId及RelationEvidenceRevision，保存[PrimaryFrom,PrimaryTo)、EFFECTIVE/WITHDRAWN；不是Relation的一枚可PATCH bool |
| 渠道 | ChannelId的Kind(EMAIL/PHONE/MESSAGE)和MESSAGE PlatformCode创建后不可变；渠道状态ACTIVE/RETIRED |
| 值 | `(T,C,ChannelId,ValueRevision)` 保存原值、规范值、Kind/Platform快照、规范化版本、国家/分机、受控HMAC指纹及密钥版本；值更正不覆盖旧版 |
| 用途策略 | PolicyKey=`T+C+ChannelId+ValueRevision+PurposeCode`；Head指PolicyVersion，0=没有明确期间；每次修订保存完整不相交期间计划 |
| 用途证据 | EvidenceId绑定准确Contact/Channel/Value/Purpose及指纹；外部Evidence.revision和Account qualificationRevision均opaque，不转Int64 |

所有期间半开 `[from,to)`，to=null表示未声明结束；每Contact任一时点最多一个主要，允许零主要，主要期间必须完整包含在所引关系期间内。“主要账户关系”不是“每账户唯一主要联系人”。时间写UTC毫秒，超过3位小数拒绝而不舍入；本地输入由组织时区解析，跳空拒绝、重叠须选offset，不能用浏览器时区猜测。[期间政策][S47] [关系字段][S209] [渠道字段][S228] [时间][S248]

EMAIL只接受单地址、域名ASCII小写但local-part原样，保留plus/dot，不发送测试邮件。PHONE本地号需国家上下文，E.164规范化与分机分开；MESSAGE需受支持平台。姓名、规范邮箱/电话均不设全租户或跨联系人唯一约束；同Contact重复渠道只给受权提示、不自动合并。[渠道规范][S244]

### 3.3 物理持久化责任

本稿推荐SQL Server `crm` schema、EF Core 8；这是适配设计，不证明现有库采用。全本地从表FK包含Tenant/Contact且 `ON DELETE NO ACTION`，外部Owner不建跨库假FK。主表及完整列/类型以原§11.2为准：[完整表字段][S1451]

- Contact及ContactAudit保存根资料/业务修订与受控差异；ContactCommand保存唯一命令、规范化/HMAC版本、原结果，不保存完整个人请求正文。
- ContactRelationHead/Revision、ContactPrimaryHead/Revision、ContactChannel/ValueRevision、ContactPurposePolicyHead/Revision/Period保存对应独立版本。
- ContactEvidenceReference存受控加密proof；ContactPurposeEvidenceBinding把Channel/Value/Purpose精确绑定实列化，PolicyPeriod经六元复合证据FK关联。FK只约束身份，不证明签名、期间覆盖或撤权窗口真实。
- ContactOutbox承诺本地事件；ContactExportRequest、JobBinding、JobProjection、ArtifactProjection保存导出四层；ContactIntegrationInbox/Conflict分别保留原消息与异内容冲突证据。

Head→Revision没有循环反向FK：新Head与Revision1同事务插入并最终确认；已有对象先插后继Revision再换Head。运行身份必须只通过统一执行器提交完整图，不能单独提交孤立Head。物理DisplayName允许null用于匿名化/历史适配，Retained新建仍必填。Name/NormalizedValue索引非唯一；普通UNIQUE/CHECK不能代替全主要期间不重叠的根事务检查。[FK/Head设计][S1477] [索引边界][S1531]

## 4. 业务流程、状态转移与写集

### 4.1 身份创建、维护和状态

创建先查原命令结果，再对新意图取IAM/归属/商业及显式子项证据。无初始关系不调用Account/Main；无渠道不调用证据Owner。短事务争命令键、按统一顺序保护必要资格/根，逐项验证后写 Contact Revision1、关系/主要Head+Revision、渠道/Value及未核Policy、一次ContactAudit、一次必要ContactChanged、COMMITTED回执。明示任一子项失败，整笔业务写0，不静默剥离。SetVersion有初始关系为1，否则0；无子项不造空记录。[创建算法][S554]

Profile PATCH只接受至少一个实际字段；规范后完全相同仅写 `changed=false` 回执，不提高revision、不发事件。归属用独立ownership子操作，核原/目标范围与成员，仍归C.OP05，Active-only。停用只改Contact→Inactive；新键在已Inactive且版本匹配可changed=false。恢复只由Inactive/Retained→Active，当前归属/部门/非空Owner必须仍合法；若停用期间Owner失效，不允许用隐含Inactive assign修复，只能等目录合法恢复或后继受控范围。[维护与状态][S565]

### 4.2 关系计划：完整读取、预览、一次应用

三视图各司其职：CURRENT_RELATION解释指定queryAt并可给完整集合凭据；AS_RECORDED需经ActivityReader验证原Activity/关系修订锚；CORRECTED_INTERPRETATION给同一历史时点的后继解释。历史两视图不给写token；原Activity引用不改。重新打开详情必须能从读取API取得RelationReadItem和PrimaryReadItem的精确ID、修订、期间、证据版与当前Relation版，不能依赖创建idMap或解析token。[读取模型][S578]

| intent | 允许变化 | 权限与状态 |
|---|---|---|
| ADD_RELATION | Relation ADD；可为同计划新关系加Primary；禁止END/CORRECT | relationshipAdd；加主要再primaryChange；Contact和新Account Active |
| END_RELATION | Relation END；仅显式处理受影响主要REVISE/WITHDRAW，无替补ADD | relationshipEnd；改主要再primaryChange；有权Retained历史可Inactive |
| CORRECT_RELATION | 至少一个有据CORRECT；仅关联主要修订/撤回，无无关ADD | relationshipCorrect+证据；主要改动再primaryChange；新运营归属仍核新目标Active |
| SWITCH_PRIMARY_ONLY | relationChanges=[]；旧主要止at+一个新主要自at；未来调整须明示 | primaryChange；Contact/新目标Active；旧关系继续 |
| END_RELATION_AND_SWITCH | 显式END旧关系于at，并处理旧主要/受影响未来安排及新主要 | primaryChange **且** relationshipEnd，必要历史纠错再专权/证据 |
| REMOVE_PRIMARY_ONLY | relationChanges=[]；只一条Primary REVISE(END_PERIOD)或WITHDRAW(WITHDRAW_FUTURE) | primaryChange+精准范围；Retained可Inactive，不要求旧Account恢复 |

顶层标签不能掩盖内含动作；即使操作者具备全部能力，ADD混入END仍 `PLAN_INTENT_MISMATCH`。服务端读取完整当前集合，保留未改成员，构造RelationFinal/PrimaryFinal，校验所有期间正长度、包含及排序后不相交；隐藏成员也检查但不得返回其ID/期间/数量。`preserveUnmentioned=true`和完整token保留不可见未改PC，不能要求用户重新枚举无权成员。[严格意图矩阵][S735] [最终算法][S752]

collectionToken保密签名包含 schemaVersion、Tenant、Subject、ContactId、ContactRevision、SetVersion、CanonicalSetDigest、AccessStamp、readAsOfUtc、queryAtUtc、expiresAt。readAsOf是真实服务器快照时点；expiresAt=readAsOf+5分钟，同cursor链固定。preview还绑定规范plan、Account opaque资格和权限/政策，期限不得越过原集合，不续期。EditContext强tag不加入读取时钟。END_PERIOD下界用签名readAsOf，正常延迟不把用户选定时点改now；WITHDRAW_FUTURE最终提交时必须还未生效，否则拒绝，不自动变END。[COR01][S727]

完整C42：RA/RB开放，PA=[t0,t2)、PC=[t2,t3)。在t1切B时开放PB=[t1,∞)冲突；用户明确PB=[t1,t2)后，PA=[t0,t1)、PB、PC完整原子形成，RA/RB不变、t3后可零主要。随后沿连续例结束RB于t2，PC绑定RB且从t2开始，必须显式WITHDRAW尚未来的PC；只END而漏PC全拒。成功一次Contact6/Set5，RB/rev2、PC/rev2，原PA/PB/RA及Activity不变。[C42][S989] [COR02连续例][S950]

有真实同一关系误填Account的证据可CORRECT产生指向新Account的后继RelationRevision，并显式处理受影响主要绑定；旧Revision.AccountId不改。证据不足须明确结束旧/新增新关系，不能自动迁移。[纠错边界][S758]

### 4.3 渠道与用途

新增Channel为ACTIVE、ValueRevision1、声明用途PolicyHead0，无Allowed期间。无用途可保存但普通联系不允许；声明非法用途整笔拒绝。Kind/Platform不可变，CorrectValue即使夹带相同kind/platform也拒 `CHANNEL_IDENTITY_IMMUTABLE`；换平台必须另建Channel，退役旧渠道是另一独立命令。改值插新ValueRevision、更新ChannelRevision，为新值各用途建立Policy0，不复制旧Allowed/证据；退役仅状态/修订变化，无普通Unretire。[渠道状态][S1090]

PUT用途策略替换准确PolicyKey的完整不相交期间。先比较新旧Allowed时间集合判断放宽/收紧权限；再独立验证最终每个ALLOWED段的完整证据，即使期间没扩大、只换证明也不能跳过。放宽需Contact/Channel Active与restrictionRelax，纯收紧可在Inactive专权执行。无Allowed的纯收紧无需捏造同意proof。任何错误保持旧策略，不把ALLOWED静默降成UNVERIFIED后报成功。[策略算法][S1115]

## 5. 精确 API、DTO、事件与 Owner 依赖

根 `/api/crm/v1`，camelCase JSON、UTC字符串。写用 `Idempotency-Key: UUID`；新建无If-Match，其余Contact业务写必须单个强If-Match，不接弱tag、*、多tag；缺少428、不匹配412。ETag来自 `/contacts/{id}/edit-context` 的确定性编辑上下文，不是活动/外部投影变化频繁的详情响应hash；关系另带expectedSetVersion/collectionToken/previewToken，渠道/策略另带子版本。[公共HTTP][S255]

| 能力 | 精确方法/路径（省略上述根） | DTO与返回 |
|---|---|---|
| 选项/选择/时间 | GET `/contact-options`；POST `/contact-options/owners/search`、`/accounts/search`、`/time-resolution` | ContactOptions、OwnerSearch/DirectoryPage、AccountPickQuery/Page、LocalTimeInput/ResolvedTime |
| 列表/候选 | POST `/contacts/search`、`/contacts/duplicate-candidates` | ContactSearch/Page、DuplicateQuery/Page |
| 创建/详情/编辑上下文 | POST `/contacts`；GET `/contacts/{id}`、`/contacts/{id}/edit-context` | CreateContact→CommandResult(首次201)；ContactDetail；EditContext+ETag |
| 身份/归属/状态 | PATCH `/contacts/{id}`；POST后缀`/ownership`、`/deactivate`、`/restore` | ProfilePatch、OwnershipChange、ReasonRequest→CommandResult |
| 关系 | POST后缀`/relationships/query`、`/relationship-plans/preview`、`/relationship-plans/apply` | RelationQuery/ViewPage；RelationshipPlan/PlanPreview；ApplyPlan=`{plan,previewToken}` |
| 渠道 | GET/POST `/contacts/{id}/channels`；POST `/channels/{channelId}/value-corrections`、`/retire`（同Contact前缀） | ChannelSummary[]；AddChannel、CorrectValue、RetireChannel |
| 用途 | GET/PUT `/contacts/{id}/channels/{channelId}/purposes/{purposeCode}` | PolicySchedule / ReplacePolicySchedule→CommandResult |
| 敏感/历史/互动 | POST后缀`/pii/read`、`/history/query`、`/activities/query` | PiiReadRequest/Result；HistoryQuery/Page；ActivityQuery/Page |
| 导出与下载 | POST `/contacts/{id}/exports`；GET `/exports/{requestId}`；POST `/exports/{requestId}/download`（同Contact前缀） | ExportRequest→LocalExportReceipt(202)；ExportStatus；DownloadRequest→受控流 |
| 原结果 | GET `/contact-commands/{operationKind}/{key}` | ReceiptView，当前独立receiptRead |

路径完整表和错误出口均在原§5；DTO公共结构在§5.3，创建§6.1、关系§7.1–2、详情/导出§9、剩余辅助类型§14.2.1。OperationKind是固定11项白名单：`contact.create/profile/ownership/deactivate/restore/relationship.apply/channel.add/channel.correct/channel.retire/policy.replace/export.request`，不是任意反射方法入口。[端点及白名单][S269] [DTO入口][S303] [辅助完整类型][S2127]

CommandResult把COMMITTED/REJECTED_FINAL/UNKNOWN、ENTITY_APPLY/LOCAL_REQUEST、changed、replayed、可空resource、canOpen、completedAt和currentApplicability分开。Problem包含稳定code、scope、commitKnowledge、retryMode、字段路径，不回显敏感输入或隐藏对象；503本身不证明零写。无实体query时创建成功可resource=null且不给Location。[结果类型][S316] [错误规则][S427]

`crm.contact.changed.v1` 每次实际ContactRevision一个事件，changeKind七类：CREATED、PROFILE_CHANGED、OWNERSHIP_CHANGED、STATUS_CHANGED、RELATION_SET_CHANGED、CHANNEL_CHANGED、PURPOSE_POLICY_CHANGED。事件只含ID/版本/变更类别/时间/commandKey，不含姓名、渠道、notes、reason，也不证明可联系或可交易。至少一次派发同eventId；跳版且事件只是changedSections时，消费者受权读当前快照，不能仅靠增量重建完整状态。[根事件][S2053]

### 5.1 用途与互动准入合同

`CONTACT-PURPOSE-EVIDENCE/1.1` 请求由可信后端重建Binding：Tenant、Contact、Channel、ValueRevision、不可变Kind/Platform、NormalizationVersion、受保护ValueFingerprint及KeyVersion、Purpose、申请期间，并有applicantSubject、EvidenceRef、bindingDigest、CRM valueAttestation。Owner Decision返回同完整绑定、VALID/REVOKED/MISMATCH/UNPROVEN、外部证据三元组、permittedInterval、subjectRule、验证时点/窗口与commitWindow。逐字段比较，不能只信digest回显或verified=true。[用途wire][S1662]

值指纹用目的隔离HMAC密钥、`CONTACT-VALUE-ID-1`固定输入；不使用裸号码SHA或命令/搜索摘要当许可。`validUntil=null`非无限，commitWindow必须是可落实的 LOCAL_REVOCATION_GUARD 或 OWNER_COMMIT_PERMIT；UNPROVEN不放Allowed。每个最终Allowed段提交时重核根/值/策略和撤权窗口，先撤权则零写。[证据验证][S1698]

AccountReference的canCreateRelation只是读取证据；同库需实际共享资格保护持到Contact提交，跨库需Owner准入协议。ContactAdmissionSnapshot只为Activity准备，Activity最终写时核Contact/Channel/Policy/Relation/主体水位；新补录过去沟通仍是现在新写，不用过去时点、Note或镜像绕受限渠道。原事实先合法提交则后续收紧不自动Void。[账户准入][S1802] [互动准入][S1825]

### 5.2 导出完整绑定与消费

`CONTACT-HISTORY-EXPORT/1.1` 消费原170/180 Owner。CanonicalExportIntent固定 CONTACT_HISTORY、schema、Tenant/Contact、requestedBy=readerSubjectId=当前主体、view、按用户确认顺序的fieldIds、purpose、RecordedThrough、发生时间过滤、CSV_VIEW格式。任一字段无权整意图拒绝；不缩列、排序或换格式后冒同请求。RecordedThrough是已登记修订截点，非发生时段，也不冻结权限。[导出意图][S1243]

`CONTACT-EXPORT-CANON-1`：UTF-8/NFC、UTC毫秒、对象键Ordinal排序、紧凑JSON、null保留、数组保序；IntentDigest为SHA256(域前缀及换行+规范JSON)。它与命令HMAC RequestDigest不同。原四字段夹具digest不是文件hash。创建本地Request/完整意图Outbox/LOCAL_REQUEST回执一次事务，202不证明Job或文件已完成；ContactRevision不增加。[规范化和本地提交][S1301]

接受链为 Request(Tenant,RequestId,IntentDigest) → 唯一Producer/OwnerJobId → Artifact(Id,opaqueVersion,ContentSHA256,Bytes,Format,IntentDigest)。Job首次版本1 INITIAL_COMPLETE；后继CONTIGUOUS须previousRevision吻合，或实际Owner CHECKPOINT证明完整状态/覆盖/具体resolvesConflictIds。Artifact先到只进WAITING_JOB_BOUND，错误Job冲突不等待换绑；同ArtifactVersion换hash/长度/格式/意图不允许，即使stateRevision更高。Job/Artifact各自同epoch精确流比较，不能跨流取最大号。[不可变绑定][S1331] [消费步骤][S2020]

内部接收路径 `/internal/crm/v1/contact-export-job-results`、`/internal/crm/v1/contact-export-artifact-results`。Inbox完整原UTF-8消息经加密保存，BodyHash绑定原字节，ReceivedTrustEvidenceJson封闭保存接收时认证引用而非token/私钥；SafeBindingJson只作索引。等待重扫先锁外解密验证，再按Request→JobBinding→JobProjection→Artifact→Inbox重入短事务。内部续办不能被“EventId已存在”永久提前退出，外部重投则仍回原状态；启动后和前驱应用后可重新发现。检查旧业务版本的同义/冲突需查同流历史Inbox，不只当前最高投影。[COR03恢复][S2037]

下载需要当前DL/180、字段/用途/隐私、准确Request/Job/Artifact及hash/长度/格式、Owner实际读取窗口；READY历史与当前DENIED可并存。无Job绑定、冲突或缺必要协议不下载。不得本地补CSV、永久StorageURL或宣称已交付字节可远程收回。[下载][S1374]

## 6. 事务、并发与恢复

命令唯一域 `(TenantId,OperationKind,Key)`，Actor/Target为记录绑定，不能换Target绕键。规范语义指纹保留PATCH“未出现/显式null”、数组顺序、method、规范路径、If-Match、operation、目标、完整plan/previewToken；对象属性排序可变但语义不变。HMAC含NormalizationVersion/DigestKeyVersion，旧重放用原版本；旧密钥不可用是证据不足，不按新算法当新意图。[命令身份][S1381]

R路径先当前身份/RQ/结果范围，然后查原槽/指纹。COMMITTED按当前字段安全读，不重新检查Active/edit/当前新用途或重复计费；REJECTED_FINAL不因后来条件改善重执行；NOT_OBSERVED/IN_PROGRESS/EXPIRED_UNPROVEN不是NOT_APPLIED_FINAL。无RQ中性拒绝，不泄成功存在。N路径键占位与业务同未提交事务，不先持久PROCESSING再缺恢复器；同键插入竞争等待原事务，超预算IN_PROGRESS，不能写假终拒。[R/N顺序][S1389]

固定锁顺序：可落实的本地身份准入保护 → 命令唯一键 → Account资格固定ID序 → Contact根固定ID序 → 子Head → 原Owner参与的Activity根。外部证据在锁外预取；发现Account依赖集合不全中止重读，不持Contact锁反向加Account锁。Contact根 `UPDLOCK,HOLDLOCK` 下完整集合裁决，再EF RowVersion条件更新；失败必须整事务回滚。不同Key旧ETag也不自动合并。SNAPSHOT读取需实际环境先配置，文档不在请求里ALTER DATABASE。[数据库裁决][S1535]

每次实际聚合变化只一个revision/audit/必要ContactChanged；关系计划另一次setVersion；无变化只有命令回执。确定性业务拒绝须发生业务写前，才可提交仅REJECTED_FINAL；SQL写中异常不能半提交后补终拒。commit断连用新Context查原Key，不在旧Context重建ID/重新跑业务；不可证UNKNOWN。代理重试仅限已确认整事务回滚、相同冻结意图和原Key。[事务算法][S1414] [写集][S1550]

浏览器15秒、1/2/5秒回查是DRAFT技术默认。超时保留原序列化body/Key/method/path/If-Match在当前tab内存；之后仅手工查原结果或重试原冻结请求，不无限POST。跨刷新安全Handle协议尚G04；没有句柄不能重造请求或自动新键，不默认增cancel-unobserved。最小成功/终拒防重证据不能随缓存TTL清掉；未有完整epoch/受理窗或墓碑协议前不提供命令账自动清理。[未知与留存][S1424]

## 7. 授权、租户与PII

后端从可信会话取Tenant/Subject/授权版，按Function+ResourceAction+ContactScope+FieldPolicy+PurposePolicy+Entitlement+DomainCapability求适用交集。沿现有`crm-account`的query/add/edit/view-pii词汇；contactCreate/primaryChange等是内部适配能力标签，不新增`crm-contact` Catalog或默认生产Role。inputNew允许输入新PII不等read允许查旧PII；only-add仍可合法零子项创建，成功不返无权ID/Location。[授权矩阵][S69]

OWN只匹配当前Owner；DEPARTMENTS只取显式授权部门不自动含下级；TENANT仍限当前Tenant且须允许选择。请求scope不在allowedScopes明确403，不自动升降级；Account筛选也核目标Account。敏感姓名查询/排序/渠道比对先核探测能力，不能取全表再前端遮罩，也不给“还有不可见重复”的提示。[范围][S93] [查询顺序][S1629]

PII/read显式白名单fieldIds+readPurpose+expectedAccessStamp，任一必需字段无权整请求403，不先返回其余敏感值再称全量成功。Inactive可有权读，Anonymized不恢复原值；沟通Allowed与历史读取许可分开。安全审计失败时必须的敏感解密/下载拒绝，不先泄明文；安全读不提高ContactRevision或发业务Outbox。[PII流程][S1147] [安全审计][S1637]

身份未知/过期连实体读、PII和RQ都失败关闭；旧Allow不盖新Deny，新epoch无可信衔接不放行。字段降权、匿名化、换Tenant清详情、PII、草稿/差异、候选、预览token、旧异步响应。禁止姓名/搜索/原因/token/body进入URL或local/sessionStorage；敏感响应no-store，普通日志不记值。[UI清理][S162] [身份契约][S1654]

## 8. 页面与服务端校验

MSBBCR330 `/crm/contacts` 位于Accounts下独立二级入口；340 `/crm/contacts/new`全页新增；350 `/crm/contacts/:contactId?tab=profile` 有profile/relations/channels/activities/history，资料内联编辑，复杂关系用Drawer，不增加/edit路由或Function。导出进入原170/180受权注册落点，不拼URL。[路由][S104]

列表先options，默认ACTIVE/后端默认scope/page30/createdAtDesc；按查询或Enter发送，改筛选清cursor。姓名/渠道默认不成明文批量列，无CSV按钮。null+redactedFields显示受限；主要关系NONE/UNAVAILABLE/PARTIAL分开；活动未接入不显示从未联系。LIVE_KEYSET按CreatedAt desc/Id desc等白名单排序，cursor保密绑定Tenant/Subject/filter/sort/pageSize/权限与边界行版本。边界或权限变409 CURSOR_STALE；只保证单次短快照，不承诺跨页冻结历史，导出不能循环列表冒一致快照。[列表][S127] [分页][S405]

关系编辑必须显示获准Before/After、原关系与主要双线，文字说明“关系继续/主要结束”。未改隐藏安排可固定文案保留，不泄其数量。首个错误聚焦，Dialog关闭回按钮，200%缩放，notes Enter不保存，状态不用颜色单独表达。[交互][S714] [可访问性][S2223]

前端状态 `LOADING→EDITING→PREVIEWING→SUBMITTING→SUCCEEDED`；已知零写字段错回编辑、412冲突比较、发送超时RESULT_UNKNOWN、失权ACCESS_LOST。Unknown不是Draft，关闭抽屉不等取消业务；旧响应由generation抑制。已知终拒后用户明确修改才新Key，Unknown不能以重开页面/新预览绕过。[状态机][S164] [命令组件][S2105]

## 9. 实施顺序与代码差异边界

按原DEV-CON-01–08分层落实：协议/真实授权适配 → 持久化命令执行器 → 身份列表/状态三页 → 关系完整集合 → 渠道证据/PII → Activity历史读取 → 导出Owner消费 → SQL并发/失权/异常收口。前端可在确定契约替身上开发，但真实SQL根锁、FK、Owner窗口及下载撤权不能用mock替代。缺某一可选Owner只禁对应操作，不妨碍必要基础授权成立的零关系/零渠道独立功能。[任务分解][S2234]

主文推荐Vue3/TypeScript/Element Plus、ASP.NET Core8/EF Core8/SQLServer；同时明确历史CRM前端曾React，真实Provider本轮未重审。应适配现有工程，不自动换框架、并建Contact主账或运行迁移。本整理没有核当前业务源码。原文提及源码 `c778a3052a4416b82facb07b1244fc98fb6ad8a1` 是历史静态来源，不当当前实现差异证据。H02 Participant必须继续合读已接受Lead CP22的真实同库事务接口，不能从普通Contact POST派生第二个创建消息。[真实工程待核][S63] [源码来源限制][S2494]

## 10. 验收设计与NOT_RUN状态

49原Contact场景、20 REV1跨章节断言、6 COR反例合计75设计条目全部NOT_RUN，范围重叠不等75独立执行。历史39项静态文档断言不等OpenAPI/DTO编译、SQL/worker/API/浏览器通过。以下是从已读验收提取的关键落点，不替代原75条：[全场景][S2284] [REV1][S2346] [COR][S2377] [原复审边界][R93]

| 验证组 | 具体断言 |
|---|---|
| C01–04/19–20 | 零子项建1Contact/0关系渠道；含失败初始子项整笔0；同名共享邮箱不合并；only-add不返旧PII；同Key只一次 |
| C05–08/24–28/40–45 | 半开边界、完整主要集合、两并发最多一成功；C42不删未来PC；REMOVE不结束关系；END+SWITCH缺任一能力全拒；旧Activity关系修订不改 |
| C09–12/17–18/21–23/33–34/38–39 | Contact独立范围；敏感搜索明确拒绝；失权清草稿历史；停用恢复不级联；旧Allow不盖Deny；遗留AccountId不造日期 |
| C10/29–32/46–49、REV1-09–13 | 渠道平台不可改，改值无旧许可；另一Channel同ValueRevision不能套证；非扩张也核每个Allowed；Verify后改值/撤权最终拒绝；过去补录仍当前准入 |
| C13–16/35–37、REV1-14–20 | 原活动去重与Partial；无EX不建请求，当前DL撤权拒旧产物；完整四字段贯穿Request/Outbox/Job/Artifact；新Job错绑、同Artifact换hash冲突 |
| COR01–04 | queryAt不能冒readAsOf；08:00读/08:01结束/08:02提交保留08:01；过期不续期；连续END RB须显式撤PC |
| COR05–06 | Artifact先到后重启能从原加密消息续办，同Inbox仅一次APPLIED；密钥缺失不补READY；旧业务版异语义即使当前已更高版仍隔离 |

实际未来测试必须带实现SHA/环境/RunId，真实SQLServer检查复合FK、根锁、期间、Head/Revision、commit未知和Outbox，双API实例验证准确事实/审计/事件/回执数量。不能用EF InMemory通过代替这些保证。[真实验证要求][S2342]

## 11. 未确认事项与启用限制

| 缺口 | 需取得的具体证据 | 受影响范围 |
|---|---|---|
| G01 | 当前框架/DbContext/provider、已有Contact实体/迁移映射 | 物理实现，禁止自动换框架或并建主账 |
| G02 | 同库Account真实表和所有状态writer锁协议，或跨库Owner准入/撤权窗口 | 含初始关系创建、新关系/新主要；不阻零关系Contact |
| G03 | IAM资源动作到内部能力、Scope/字段/epoch、水位、证据签名/指纹/主体和提交窗口 | PII/敏感搜索/策略Allowed/关系与Activity最终准入 |
| G04 | HMAC/规范化旧版本保留、最小命令墓碑/epoch、跨刷新ReceiptHandle、实际执行Owner Fence | 永久恢复/过期重做；不阻原tab已证回查 |
| G05 | ExportOwner完整dataset/字段/格式/容量、唯一Job/Artifact流、前驱与冲突解决、实际读取中撤权协议 | 导出/下载真实接入；无本地CSV替代 |
| G06 | 加密/密钥/DB和审计访问、Inbox载荷/证明保留、擦除及通知水位 | 生产PII、历史明文、恢复与下载；无虚构法定天数 |
| G07 | Inactive期间归属失效的独立受控修复政策 | 极端恢复；目前不开放Inactive assign |
| G08 | ActivityReader原ID/修订/主锚/关系修订、字段投影、Partial/cursor与历史精度 | C17及跨源三视图 |

这些是动作级实施证据与一个明确未批准范围，不是要求用户重新表决DEC01/02。DEC03/04继续不启用。主文技术性能/限流/派发默认必须在实施包评估，不冒已部署参数；历史材料后继筛查由全局索引处理，不自动替换已接受准确字节。[完整缺口][S2423]


后继 Activity v1.1.1 的 [UA 接受书](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/19/192cc33a7bda8b5c__HISTORY__handoffs__PLAN-REUSE-01B__documentation__MD02__acceptances__A-CRM__CRM-ACT-01__UA-20260930-A-CRM-ACT-MD01.md.txt:19>) 已接受 `ACT-CONTACT-READ/1.1` 最小扩展设计（第21行），因此这里不再把该设计列为待业务批准。它允许后继才关联的 Contact 使用 nullable asRecorded，并显式返回 association/relationExplanations；原主锚 Note 仍非实际参与。旧 Contact 原件保持，实际双方接线继续是 G08；未接时旧形状无法表达的分支仍 Partial。详见 [Activity 读取合同](<D:/CP6/docs/CP6_开发设计文档_20261010/modules/CRM-ACT-01.md>)。

## 12. 来源与实际阅读覆盖

本轮全文语义阅读当前主文2,573行（含全部字段/TypeScript、DDL规格、JSON夹具、75项验收和历史身份对照）、CURRENT.json69行、UA72行、SR复审103行。输出截断的段已分段重读，不计未看见内容；所有SQL/代码/样例仅阅读，未执行。Account整理时已全文阅读的R01/R02/R03共同规范、CR09与原UA可复用，仍记录为同轮已读，不重复宣称运行。

当前声明L1/L2三原件与补充复审已实读，主文中的原历史附件、两个v1.0/作者v1.1及外部技术参考没有因此自动成为本次全文审计；其来源身份和采用差异按主文记载区分。当前3登记必要原件及补充复审已全部归并，状态`required_materials_consolidated`。ACT-CONTACT-READ/1.1扩展按本册所列准确后继解释；真实Lead H02/HO共同事务接线及全历史源码属于独立实施/来源边界。精确SHA/范围见 [commercial-reading.json](D:/CP6/docs/CP6_开发设计文档_20261010/evidence/commercial-reading.json)，wire原文按以下行号直接读取，无另造缩水schema。


[U7]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cb/cb8f03ecf16c3625__UA-20260929-A-CRM-CON-MD01.md:7>
[U25]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cb/cb8f03ecf16c3625__UA-20260929-A-CRM-CON-MD01.md:25>
[S20]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6a/6a000d26d7a638a4__CRM-CON-01_联系人与账户关系_前后端开发Spec_v1.1.1_REVIEW.md:20>
[S35]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6a/6a000d26d7a638a4__CRM-CON-01_联系人与账户关系_前后端开发Spec_v1.1.1_REVIEW.md:35>
[S55]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6a/6a000d26d7a638a4__CRM-CON-01_联系人与账户关系_前后端开发Spec_v1.1.1_REVIEW.md:55>
[S22]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6a/6a000d26d7a638a4__CRM-CON-01_联系人与账户关系_前后端开发Spec_v1.1.1_REVIEW.md:22>
[S2529]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6a/6a000d26d7a638a4__CRM-CON-01_联系人与账户关系_前后端开发Spec_v1.1.1_REVIEW.md:2529>
[U31]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cb/cb8f03ecf16c3625__UA-20260929-A-CRM-CON-MD01.md:31>
[U35]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cb/cb8f03ecf16c3625__UA-20260929-A-CRM-CON-MD01.md:35>
[R32]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/b3/b319ca5567d0b664__SR-20260929-A-CRM-MD02_复审与总控限定补正.md:32>
[S175]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6a/6a000d26d7a638a4__CRM-CON-01_联系人与账户关系_前后端开发Spec_v1.1.1_REVIEW.md:175>
[S185]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6a/6a000d26d7a638a4__CRM-CON-01_联系人与账户关系_前后端开发Spec_v1.1.1_REVIEW.md:185>
[S47]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6a/6a000d26d7a638a4__CRM-CON-01_联系人与账户关系_前后端开发Spec_v1.1.1_REVIEW.md:47>
[S209]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6a/6a000d26d7a638a4__CRM-CON-01_联系人与账户关系_前后端开发Spec_v1.1.1_REVIEW.md:209>
[S228]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6a/6a000d26d7a638a4__CRM-CON-01_联系人与账户关系_前后端开发Spec_v1.1.1_REVIEW.md:228>
[S248]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6a/6a000d26d7a638a4__CRM-CON-01_联系人与账户关系_前后端开发Spec_v1.1.1_REVIEW.md:248>
[S244]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6a/6a000d26d7a638a4__CRM-CON-01_联系人与账户关系_前后端开发Spec_v1.1.1_REVIEW.md:244>
[S1451]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6a/6a000d26d7a638a4__CRM-CON-01_联系人与账户关系_前后端开发Spec_v1.1.1_REVIEW.md:1451>
[S1477]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6a/6a000d26d7a638a4__CRM-CON-01_联系人与账户关系_前后端开发Spec_v1.1.1_REVIEW.md:1477>
[S1531]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6a/6a000d26d7a638a4__CRM-CON-01_联系人与账户关系_前后端开发Spec_v1.1.1_REVIEW.md:1531>
[S554]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6a/6a000d26d7a638a4__CRM-CON-01_联系人与账户关系_前后端开发Spec_v1.1.1_REVIEW.md:554>
[S565]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6a/6a000d26d7a638a4__CRM-CON-01_联系人与账户关系_前后端开发Spec_v1.1.1_REVIEW.md:565>
[S578]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6a/6a000d26d7a638a4__CRM-CON-01_联系人与账户关系_前后端开发Spec_v1.1.1_REVIEW.md:578>
[S735]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6a/6a000d26d7a638a4__CRM-CON-01_联系人与账户关系_前后端开发Spec_v1.1.1_REVIEW.md:735>
[S752]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6a/6a000d26d7a638a4__CRM-CON-01_联系人与账户关系_前后端开发Spec_v1.1.1_REVIEW.md:752>
[S727]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6a/6a000d26d7a638a4__CRM-CON-01_联系人与账户关系_前后端开发Spec_v1.1.1_REVIEW.md:727>
[S989]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6a/6a000d26d7a638a4__CRM-CON-01_联系人与账户关系_前后端开发Spec_v1.1.1_REVIEW.md:989>
[S950]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6a/6a000d26d7a638a4__CRM-CON-01_联系人与账户关系_前后端开发Spec_v1.1.1_REVIEW.md:950>
[S758]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6a/6a000d26d7a638a4__CRM-CON-01_联系人与账户关系_前后端开发Spec_v1.1.1_REVIEW.md:758>
[S1090]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6a/6a000d26d7a638a4__CRM-CON-01_联系人与账户关系_前后端开发Spec_v1.1.1_REVIEW.md:1090>
[S1115]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6a/6a000d26d7a638a4__CRM-CON-01_联系人与账户关系_前后端开发Spec_v1.1.1_REVIEW.md:1115>
[S255]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6a/6a000d26d7a638a4__CRM-CON-01_联系人与账户关系_前后端开发Spec_v1.1.1_REVIEW.md:255>
[S269]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6a/6a000d26d7a638a4__CRM-CON-01_联系人与账户关系_前后端开发Spec_v1.1.1_REVIEW.md:269>
[S303]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6a/6a000d26d7a638a4__CRM-CON-01_联系人与账户关系_前后端开发Spec_v1.1.1_REVIEW.md:303>
[S2127]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6a/6a000d26d7a638a4__CRM-CON-01_联系人与账户关系_前后端开发Spec_v1.1.1_REVIEW.md:2127>
[S316]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6a/6a000d26d7a638a4__CRM-CON-01_联系人与账户关系_前后端开发Spec_v1.1.1_REVIEW.md:316>
[S427]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6a/6a000d26d7a638a4__CRM-CON-01_联系人与账户关系_前后端开发Spec_v1.1.1_REVIEW.md:427>
[S2053]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6a/6a000d26d7a638a4__CRM-CON-01_联系人与账户关系_前后端开发Spec_v1.1.1_REVIEW.md:2053>
[S1662]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6a/6a000d26d7a638a4__CRM-CON-01_联系人与账户关系_前后端开发Spec_v1.1.1_REVIEW.md:1662>
[S1698]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6a/6a000d26d7a638a4__CRM-CON-01_联系人与账户关系_前后端开发Spec_v1.1.1_REVIEW.md:1698>
[S1802]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6a/6a000d26d7a638a4__CRM-CON-01_联系人与账户关系_前后端开发Spec_v1.1.1_REVIEW.md:1802>
[S1825]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6a/6a000d26d7a638a4__CRM-CON-01_联系人与账户关系_前后端开发Spec_v1.1.1_REVIEW.md:1825>
[S1243]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6a/6a000d26d7a638a4__CRM-CON-01_联系人与账户关系_前后端开发Spec_v1.1.1_REVIEW.md:1243>
[S1301]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6a/6a000d26d7a638a4__CRM-CON-01_联系人与账户关系_前后端开发Spec_v1.1.1_REVIEW.md:1301>
[S1331]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6a/6a000d26d7a638a4__CRM-CON-01_联系人与账户关系_前后端开发Spec_v1.1.1_REVIEW.md:1331>
[S2020]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6a/6a000d26d7a638a4__CRM-CON-01_联系人与账户关系_前后端开发Spec_v1.1.1_REVIEW.md:2020>
[S2037]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6a/6a000d26d7a638a4__CRM-CON-01_联系人与账户关系_前后端开发Spec_v1.1.1_REVIEW.md:2037>
[S1374]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6a/6a000d26d7a638a4__CRM-CON-01_联系人与账户关系_前后端开发Spec_v1.1.1_REVIEW.md:1374>
[S1381]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6a/6a000d26d7a638a4__CRM-CON-01_联系人与账户关系_前后端开发Spec_v1.1.1_REVIEW.md:1381>
[S1389]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6a/6a000d26d7a638a4__CRM-CON-01_联系人与账户关系_前后端开发Spec_v1.1.1_REVIEW.md:1389>
[S1535]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6a/6a000d26d7a638a4__CRM-CON-01_联系人与账户关系_前后端开发Spec_v1.1.1_REVIEW.md:1535>
[S1414]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6a/6a000d26d7a638a4__CRM-CON-01_联系人与账户关系_前后端开发Spec_v1.1.1_REVIEW.md:1414>
[S1550]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6a/6a000d26d7a638a4__CRM-CON-01_联系人与账户关系_前后端开发Spec_v1.1.1_REVIEW.md:1550>
[S1424]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6a/6a000d26d7a638a4__CRM-CON-01_联系人与账户关系_前后端开发Spec_v1.1.1_REVIEW.md:1424>
[S69]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6a/6a000d26d7a638a4__CRM-CON-01_联系人与账户关系_前后端开发Spec_v1.1.1_REVIEW.md:69>
[S93]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6a/6a000d26d7a638a4__CRM-CON-01_联系人与账户关系_前后端开发Spec_v1.1.1_REVIEW.md:93>
[S1629]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6a/6a000d26d7a638a4__CRM-CON-01_联系人与账户关系_前后端开发Spec_v1.1.1_REVIEW.md:1629>
[S1147]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6a/6a000d26d7a638a4__CRM-CON-01_联系人与账户关系_前后端开发Spec_v1.1.1_REVIEW.md:1147>
[S1637]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6a/6a000d26d7a638a4__CRM-CON-01_联系人与账户关系_前后端开发Spec_v1.1.1_REVIEW.md:1637>
[S162]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6a/6a000d26d7a638a4__CRM-CON-01_联系人与账户关系_前后端开发Spec_v1.1.1_REVIEW.md:162>
[S1654]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6a/6a000d26d7a638a4__CRM-CON-01_联系人与账户关系_前后端开发Spec_v1.1.1_REVIEW.md:1654>
[S104]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6a/6a000d26d7a638a4__CRM-CON-01_联系人与账户关系_前后端开发Spec_v1.1.1_REVIEW.md:104>
[S127]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6a/6a000d26d7a638a4__CRM-CON-01_联系人与账户关系_前后端开发Spec_v1.1.1_REVIEW.md:127>
[S405]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6a/6a000d26d7a638a4__CRM-CON-01_联系人与账户关系_前后端开发Spec_v1.1.1_REVIEW.md:405>
[S714]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6a/6a000d26d7a638a4__CRM-CON-01_联系人与账户关系_前后端开发Spec_v1.1.1_REVIEW.md:714>
[S2223]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6a/6a000d26d7a638a4__CRM-CON-01_联系人与账户关系_前后端开发Spec_v1.1.1_REVIEW.md:2223>
[S164]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6a/6a000d26d7a638a4__CRM-CON-01_联系人与账户关系_前后端开发Spec_v1.1.1_REVIEW.md:164>
[S2105]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6a/6a000d26d7a638a4__CRM-CON-01_联系人与账户关系_前后端开发Spec_v1.1.1_REVIEW.md:2105>
[S2234]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6a/6a000d26d7a638a4__CRM-CON-01_联系人与账户关系_前后端开发Spec_v1.1.1_REVIEW.md:2234>
[S63]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6a/6a000d26d7a638a4__CRM-CON-01_联系人与账户关系_前后端开发Spec_v1.1.1_REVIEW.md:63>
[S2494]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6a/6a000d26d7a638a4__CRM-CON-01_联系人与账户关系_前后端开发Spec_v1.1.1_REVIEW.md:2494>
[S2284]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6a/6a000d26d7a638a4__CRM-CON-01_联系人与账户关系_前后端开发Spec_v1.1.1_REVIEW.md:2284>
[S2346]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6a/6a000d26d7a638a4__CRM-CON-01_联系人与账户关系_前后端开发Spec_v1.1.1_REVIEW.md:2346>
[S2377]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6a/6a000d26d7a638a4__CRM-CON-01_联系人与账户关系_前后端开发Spec_v1.1.1_REVIEW.md:2377>
[R93]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/b3/b319ca5567d0b664__SR-20260929-A-CRM-MD02_复审与总控限定补正.md:93>
[S2342]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6a/6a000d26d7a638a4__CRM-CON-01_联系人与账户关系_前后端开发Spec_v1.1.1_REVIEW.md:2342>
[S2423]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6a/6a000d26d7a638a4__CRM-CON-01_联系人与账户关系_前后端开发Spec_v1.1.1_REVIEW.md:2423>
