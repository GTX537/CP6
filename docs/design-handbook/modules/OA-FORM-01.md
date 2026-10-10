# OA-FORM-01 — 表单目录、草稿、提交和检索

整理状态：`core_semantics_consolidated`。本轮已全文读当前正文，共同事务和界面语义已整理；强制附件/跨域采用仍有待核项。当前材料身份见本组合同索引 `frozen_inputs[OA-FORM-01]`。

## 1. 目的、操作者与职责

发起人从获准目录选择表单，直接提交或保存自己未提交草稿；本人可修改、重基、删除草稿并检索授权历史。覆盖六项 SPEC：目录收藏、草稿、提交、重基、删除、表单/实例检索。Main Workflow 只拥有本域 FormData/流程事实，不能经通用表单修改订单、PLM 等 Owner 冻结 Subject。任务代理权也不授草稿代填权。[F L7](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/10/1099d259ef39a8e9__OA-FORM-01-DEVELOPER-SPEC-R1.md:7) [F L22](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/10/1099d259ef39a8e9__OA-FORM-01-DEVELOPER-SPEC-R1.md:22) [F L27](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/10/1099d259ef39a8e9__OA-FORM-01-DEVELOPER-SPEC-R1.md:27)

## 2. 当前组合与接受边界

当前 FORM R1 SHA `1099d259ef39a8e983c17a7c95259c50cb1c57e76b92aa76fac9a3f932cd31d3`，189 行。强制组合为 Common R1 + DEF R1 + FORM R1 + TASK R2 + accepted APP 及复用索引/矩阵/AC trace/累计复核。`UA-20261008-S3-OA-FORM-01-R1-MD02` 接受六项 Markdown 静态详设，35 AC 仍 NOT_RUN；实现、Owner 采用和原母版 Excel 不在接受宣称内。[AF L169](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/78/7802303ae8a12840__UA-20261008-S3-OA-FORM-01-R1-MD02.json:169) [AF L307](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/78/7802303ae8a12840__UA-20261008-S3-OA-FORM-01-R1-MD02.json:307) [AF L374](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/78/7802303ae8a12840__UA-20261008-S3-OA-FORM-01-R1-MD02.json:374) [R L91](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/f4/f47806d8b42afa57__S3-FINAL-CUMULATIVE-REVIEW-R2-20261007.md:91)

## 3. 数据与身份

|字段/对象|来源和约束|依据|
|---|---|---|
|draftId/formDataId/instanceId|服务端 GUID；合法 unbound 提交的 instanceId 为 null，不因此判失败|[F L33](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/10/1099d259ef39a8e9__OA-FORM-01-DEVELOPER-SPEC-R1.md:33) [F L46](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/10/1099d259ef39a8e9__OA-FORM-01-DEVELOPER-SPEC-R1.md:46)|
|ownerUserId/submittedBy|可信登录者；不从客户端 body 取 actor|[F L34](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/10/1099d259ef39a8e9__OA-FORM-01-DEVELOPER-SPEC-R1.md:34)|
|formKey/formDefVersionId/formVersion|稳定 head key、准确版本 GUID、显示整数字号分开；草稿 pin 不随 head 更新|[F L35](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/10/1099d259ef39a8e9__OA-FORM-01-DEVELOPER-SPEC-R1.md:35)|
|flowDefVersion/bindingRevision|有绑定时精确引用，不能仅以当前流程名称关联|[F L36](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/10/1099d259ef39a8e9__OA-FORM-01-DEVELOPER-SPEC-R1.md:36)|
|data/title|JSON 对象最大 1 MiB、顶层 500 字段、深度 8；title 可空、最大 200，trim 后空归 null，不是单据号|[F L37](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/10/1099d259ef39a8e9__OA-FORM-01-DEVELOPER-SPEC-R1.md:37) [F L38](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/10/1099d259ef39a8e9__OA-FORM-01-DEVELOPER-SPEC-R1.md:38)|
|rowVersion/state|opaque CAS；Active=0、Submitted=1，v2 增 DELETED tombstone；stale 是上下文事实，不触发自动迁移|[F L39](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/10/1099d259ef39a8e9__OA-FORM-01-DEVELOPER-SPEC-R1.md:39) [F L40](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/10/1099d259ef39a8e9__OA-FORM-01-DEVELOPER-SPEC-R1.md:40) [F L41](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/10/1099d259ef39a8e9__OA-FORM-01-DEVELOPER-SPEC-R1.md:41)|
|证据等级|EXACT_PIN / LEGACY_RECONSTRUCTED / UNVERIFIED_LEGACY；SubmissionKey/requestHash 保持原幂等域，不能当 APP SubmissionId/Owner hash|[F L42](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/10/1099d259ef39a8e9__OA-FORM-01-DEVELOPER-SPEC-R1.md:42) [F L44](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/10/1099d259ef39a8e9__OA-FORM-01-DEVELOPER-SPEC-R1.md:44) [X L40](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/bd/bd3b9c6f0d6fee8b__OA-APP-REUSE-INDEX-R1.md:40)|

表单提交、审批结论和业务应用为三个不同事实。unbound 只显示“表单已提交，未发起审批”；没有真实 Owner resultRef 时业务应用保持 UNPROVEN。[F L46](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/10/1099d259ef39a8e9__OA-FORM-01-DEVELOPER-SPEC-R1.md:46) [F L152](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/10/1099d259ef39a8e9__OA-FORM-01-DEVELOPER-SPEC-R1.md:152)

## 4. 流程、前置与事务写集

目录先筛当前 Published + 资源准入；收藏不授访问权。失效收藏只留 unavailable 占位，不能泄漏旧表单名。收藏为 `{formKey,on:boolean}` 幂等赋值，不用 toggle；Scope/user/form 唯一并与 receipt 原子写入。启动前重新取得 initiation-context，不用目录缓存作为提交前驱。[F L50](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/10/1099d259ef39a8e9__OA-FORM-01-DEVELOPER-SPEC-R1.md:50) [F L52](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/10/1099d259ef39a8e9__OA-FORM-01-DEVELOPER-SPEC-R1.md:52) [F L54](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/10/1099d259ef39a8e9__OA-FORM-01-DEVELOPER-SPEC-R1.md:54)

草稿创建固定本人的准确 form pin，只做 partial 校验；required 可缺，类型、未知字段、未经授权 FileRef 不可放过。保存锁本人 Active + CAS，保 unset/null/empty 的区别，不能因隐藏字段不返回而把它们擦除；历史及 receipt 与数据同事务。不会创建实例、任务或通知。[F L63](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/10/1099d259ef39a8e9__OA-FORM-01-DEVELOPER-SPEC-R1.md:63) [F L65](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/10/1099d259ef39a8e9__OA-FORM-01-DEVELOPER-SPEC-R1.md:65) [F L67](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/10/1099d259ef39a8e9__OA-FORM-01-DEVELOPER-SPEC-R1.md:67)

提交具有两个封闭入口，不能设计成任意可选字段的大 DTO：[F L80](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/10/1099d259ef39a8e9__OA-FORM-01-DEVELOPER-SPEC-R1.md:80)

|入口|完整业务输入（另加 WFC envelope）|并发前驱|
|---|---|---|
|POST `/api/oa/v2/forms/{formKey}/submissions`|`kind:DIRECT, expectedFormVersionId, expectedActivationRevision, expectedBindingRevision, expectedFlowVersionId, expectedFlowActivationRevision, data`|expectedVersion 必为 null；五个 expected context 值来自本次授权 context。[F L84](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/10/1099d259ef39a8e9__OA-FORM-01-DEVELOPER-SPEC-R1.md:84)|
|POST `/api/oa/v2/drafts/{draftId}/submit`|`kind:DRAFT` + 上述五个 expected 字段；不接 data|expectedVersion 是最新 draft rowVersion；真实 data/form identity 从锁定草稿取得；禁止额外 formKey/draftId/expectedDraftVersion。[F L85](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/10/1099d259ef39a8e9__OA-FORM-01-DEVELOPER-SPEC-R1.md:85) [F L87](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/10/1099d259ef39a8e9__OA-FORM-01-DEVELOPER-SPEC-R1.md:87)|

提交事务按以下顺序实现（原算法见 [F L91](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/10/1099d259ef39a8e9__OA-FORM-01-DEVELOPER-SPEC-R1.md:91)）：

1. 当前 read 后查原 command 已完成结果；新写取得 WF_AUTH→command→Form activation/binding/Flow heads→published refs→draft。不能先重解析最新表单再返回原结果。
2. 门内核 submit/字段/FileRef 权限、当前 epoch、本人 Active、CAS；Submitted 草稿只有原命令可返回原结果，另一命令不得假报自己成功。
3. 草稿必须满足 `draft.FormDefId=head.Id=version.FormDefId`，且 `draft.FormDefVersionId=body.expectedFormVersionId=resolved.Id`；resolved 必为锁定 head 下当前可用 Published。不能用另一 schema 解释旧数据。
4. form activation 与 flow activation 各自核对。binding revision、所有 ID 和 flow pin 对应；无绑定仅在显式 `allowsUnbound=true`、所有 flow 引用 null 时允许，并锁 absence 的 key range。上下文变化 409 FORM_CONTEXT_CHANGED，零新增 FormData/Instance，草稿仍 Active。
5. 服务端按准确 schema 校验必填、类型、表格、权限和附件，并复算；原请求 hash 与服务端结果 hash 分开。
6. FormData、精确 FlowFormRef/Instance pin、初始 token/tasks、draft Submitted、command result、通知 Outbox 同一个 DbTransaction；unbound 不建 Instance。引擎 Start 不能参加同库事务时关闭能力，不拆成 FORM 成功后再异步补实例。[F L98](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/10/1099d259ef39a8e9__OA-FORM-01-DEVELOPER-SPEC-R1.md:98)

重基是独立草稿动作。预览含 source/target pin、draft token、schemaDigest、差异 path 和 10 分钟 TTL；同名同类型保值，新增取允许 default，否则 null；类型变化禁止自动强转。子表逐列/逐行比较。MaxRows 收紧保全部原行并阻止 Submit，由有权用户明确删除选定行；不可见字段有值损失返回 REBASE_HIDDEN_LOSS_BLOCKED，不泄漏 path/value，也不接受盲确认。[F L113](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/10/1099d259ef39a8e9__OA-FORM-01-DEVELOPER-SPEC-R1.md:113) [F L115](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/10/1099d259ef39a8e9__OA-FORM-01-DEVELOPER-SPEC-R1.md:115) [F L117](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/10/1099d259ef39a8e9__OA-FORM-01-DEVELOPER-SPEC-R1.md:117)

确认重基核 previewId/digest、targetVersionId、confirmRemovedValues 与 expectedVersion；保存准确前快照/差异/留存 Ref 后更新 pin、DataJson、RebasedFromVersionId、token、receipt，一次 commit。有实际损失未确认 422；预览后 draft/target 变化 409。缺 required 可继续 Active，但不能提交；重基不建审批任务。[F L119](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/10/1099d259ef39a8e9__OA-FORM-01-DEVELOPER-SPEC-R1.md:119) [F L121](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/10/1099d259ef39a8e9__OA-FORM-01-DEVELOPER-SPEC-R1.md:121)

删除仅本人 Active+CAS，写 DELETED tombstone、原命令、保留内容 Ref/retentionPolicyRef；默认不物理删除。submit 先赢则删除 SUBMITTED_IMMUTABLE，delete 先赢则 submit DRAFT_DELETED。恢复是单独 Required capability，仅留存和授权允许时建新 Active revision，并保原删除记录；已 purge 不可承诺恢复。[F L132](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/10/1099d259ef39a8e9__OA-FORM-01-DEVELOPER-SPEC-R1.md:132) [F L136](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/10/1099d259ef39a8e9__OA-FORM-01-DEVELOPER-SPEC-R1.md:136)

## 5. API、读模型和依赖

v2 还包括 catalog/favorite、initiation-context、draft CRUD、rebase-preview/rebase、delete/restore、query/search、form-data 以及 submissions/by-command。旧 CreateDraft/UpdateDraft/Rebase/SubmitDraft/SubmissionRequest DTO 与旧成功 envelope 保留，由逐入口 adapter 收敛，不直接改旧客户端语义。[F L163](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/10/1099d259ef39a8e9__OA-FORM-01-DEVELOPER-SPEC-R1.md:163) [F L165](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/10/1099d259ef39a8e9__OA-FORM-01-DEVELOPER-SPEC-R1.md:165)

`FormInitiationContext` 返回 formKey/formVersionId/formVersion/schemaJson/activationRevision/bindingRevision/flowVersionId/flowVersion/flowActivationRevision/allowsUnbound/capabilities/contextVersion/evidenceState；contextVersion 只做缓存，不替代 CAS。Submit 结果保旧 FormDataId、FormDefVersionId、FormVersion、FlowInstanceId?/FlowDefVersionId?/FlowVersion?，附 receiptRef/commandId/bindingRevision/submissionState。[F L163](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/10/1099d259ef39a8e9__OA-FORM-01-DEVELOPER-SPEC-R1.md:163) [F L172](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/10/1099d259ef39a8e9__OA-FORM-01-DEVELOPER-SPEC-R1.md:172)

查询行统一为 `{formDataId?,instanceId?,formKey?,formVersion?,flowKey?,flowVersion?,title?,starterRef?,submittedAtUtc?,instanceStatus?,applicationSummary?,evidenceState,detailRef}`。缺字段 null 并注明来源；旧记录不能用当前 head 名称/版本填充。纯 FormData 以 SubmittedUnbound 进入本人或明确资源授权者查询。[F L148](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/10/1099d259ef39a8e9__OA-FORM-01-DEVELOPER-SPEC-R1.md:148)

DEF 提供不可变 schema/独立激活；TASK 提供当前任务权、returned-instance 恢复和决定；SYS-IAM/FILE/PRIV 提供当前资格、附件与保留。FORM 不重造 APP 的 Subject/Envelope；同名 SubmissionKey 也不授跨域身份等价。[F L185](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/10/1099d259ef39a8e9__OA-FORM-01-DEVELOPER-SPEC-R1.md:185) [X L40](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/bd/bd3b9c6f0d6fee8b__OA-APP-REUSE-INDEX-R1.md:40)

## 6. 幂等、并发、未知结果与恢复

遵循 WFC-1 和 WF_AUTH 公共合同。legacy tenant+SubmissionKey 唯一关系保留，WFC 到 legacy 键必须有准确 adapter；actor 不可拿另一人的旧结果。同 command 异输入冲突；两命令争一个草稿最多一笔提交。所有写的授权判断与撤权 writer 共门，不能只靠 draft CAS。[F L100](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/10/1099d259ef39a8e9__OA-FORM-01-DEVELOPER-SPEC-R1.md:100) [C L68](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/85/85ba15dfd2b01875__S3-COMMON-CONTRACT-R1.md:68)

提交、重基、删除的 commandId 按动作分别独立；响应未知先查原命令/凭证，不改 body、不换 key。CAS 冲突保本地输入、刷新授权上下文，由用户确认新意图。重基后恢复旧内容只能基于准确前快照新建草稿或再次显式转换；没有快照不承诺原值恢复。[F L69](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/10/1099d259ef39a8e9__OA-FORM-01-DEVELOPER-SPEC-R1.md:69) [F L121](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/10/1099d259ef39a8e9__OA-FORM-01-DEVELOPER-SPEC-R1.md:121) [F L136](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/10/1099d259ef39a8e9__OA-FORM-01-DEVELOPER-SPEC-R1.md:136) [F L174](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/10/1099d259ef39a8e9__OA-FORM-01-DEVELOPER-SPEC-R1.md:174)

**普通 Active FormDraft 与退回的 Instance Draft 不是同一实体。** 旧 Submitted draft 不复活，普通 submit 不接 instanceId 冒充 draftId。退回链使用 TASK 的 returned-form/resubmit，沿原 InstanceId/FormDataId/两个 pin 和双 CAS；APP 冻结 Subject 被该 SFS 入口拒绝。[F L189](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/10/1099d259ef39a8e9__OA-FORM-01-DEVELOPER-SPEC-R1.md:189)

## 7. 权限、租户和信息保护

本人草稿、提交权、字段编辑和 FileRef 校验分别核验；任务代理不授草稿编辑。搜索先建立授权候选集，再筛选、计数、分页；隐藏字段不进入关键词命中和数量侧信道。附件/评论也单独投影。403 不能转换成空数据并继续覆盖。[F L27](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/10/1099d259ef39a8e9__OA-FORM-01-DEVELOPER-SPEC-R1.md:27) [F L67](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/10/1099d259ef39a8e9__OA-FORM-01-DEVELOPER-SPEC-R1.md:67) [F L146](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/10/1099d259ef39a8e9__OA-FORM-01-DEVELOPER-SPEC-R1.md:146) [F L150](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/10/1099d259ef39a8e9__OA-FORM-01-DEVELOPER-SPEC-R1.md:150)

当前 keyword 仅 FlowKey/BizId，不宣称 DataJson 全文搜索。未来注册字段搜索只允许当前可读且明确注册字段；未知字段 422。历史默认只读，编辑能力来自当前 task/draft context；切换 actual/effective 必须重新取列表与详情。[F L146](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/10/1099d259ef39a8e9__OA-FORM-01-DEVELOPER-SPEC-R1.md:146) [F L150](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/10/1099d259ef39a8e9__OA-FORM-01-DEVELOPER-SPEC-R1.md:150)

## 8. 页面与校验

目录展示可用/不可用及收藏状态；FormInitiate 直接提交不能因为旧注释被误写成“先自动保存草稿”。InboxDraft 只列本人 Active，摘要不整包展开 DataJson；详情使用准确 pinned schema；stale 草稿可按旧 schema 编辑，重基由用户明确选择。[F L22](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/10/1099d259ef39a8e9__OA-FORM-01-DEVELOPER-SPEC-R1.md:22) [F L23](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/10/1099d259ef39a8e9__OA-FORM-01-DEVELOPER-SPEC-R1.md:23) [F L65](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/10/1099d259ef39a8e9__OA-FORM-01-DEVELOPER-SPEC-R1.md:65)

表单查询将实例、审批、业务应用分轴显示；没有回执不显示 Applied。时间筛选以用户选定租户时区转 UTC 半开 `[fromInclusive,toExclusive)`，固定 UTC 时间倒序+ID 倒序，snapshotAsOf 下分页；不拿服务器 local date 比较。重基差异显示保留、增加、移除、类型变化和必填结果；明确行超限/隐蔽损失阻断，不能自动丢行。[F L113](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/10/1099d259ef39a8e9__OA-FORM-01-DEVELOPER-SPEC-R1.md:113) [F L117](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/10/1099d259ef39a8e9__OA-FORM-01-DEVELOPER-SPEC-R1.md:117) [F L152](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/10/1099d259ef39a8e9__OA-FORM-01-DEVELOPER-SPEC-R1.md:152)

## 9. 实施顺序与固定源码差异

依原 F-WP1–6：目录/context→草稿 CRUD/CAS/tombstone→两类封闭提交与同事务→nested rebase→授权查询/页面→AC 实现。固定 `CP6@90c871fe571fd6b390f53e8678376d7ce60bcb60` 的历史静态事实包括目录仅检查 Enable、重基只看顶层类型、删除物理 remove 且无 row token、未覆盖无实例 FormData。本文未重新审计当前工作树，不把规范的 Required 部分称为已实现。[F L9](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/10/1099d259ef39a8e9__OA-FORM-01-DEVELOPER-SPEC-R1.md:9) [F L178](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/10/1099d259ef39a8e9__OA-FORM-01-DEVELOPER-SPEC-R1.md:178)

旧 DELETE 必须适配同一 tombstone 服务；附件解除关联要由 FILE Owner 核引用和保留，不随草稿删除连带删原件。迁移和生产切换需另留真实证据。[F L134](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/10/1099d259ef39a8e9__OA-FORM-01-DEVELOPER-SPEC-R1.md:134) [F L183](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/10/1099d259ef39a8e9__OA-FORM-01-DEVELOPER-SPEC-R1.md:183)

## 10. 验收与未执行范围

35 AC 全 NOT_RUN：目录4、草稿6、提交8、重基6、删除5、查询6。重点验证同键原结果、三方 schema 等式及 binding 竞态、同库原子性、隐藏字段、nested 表格损失、submit/delete 竞争、无实例 FormData、时区窗口、准确 pin 与 APP 应用状态。完整逐项断言见原文。[F L56](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/10/1099d259ef39a8e9__OA-FORM-01-DEVELOPER-SPEC-R1.md:56) [F L71](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/10/1099d259ef39a8e9__OA-FORM-01-DEVELOPER-SPEC-R1.md:71) [F L102](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/10/1099d259ef39a8e9__OA-FORM-01-DEVELOPER-SPEC-R1.md:102) [F L123](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/10/1099d259ef39a8e9__OA-FORM-01-DEVELOPER-SPEC-R1.md:123) [F L138](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/10/1099d259ef39a8e9__OA-FORM-01-DEVELOPER-SPEC-R1.md:138) [F L154](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/10/1099d259ef39a8e9__OA-FORM-01-DEVELOPER-SPEC-R1.md:154)

本轮未运行业务测试、迁移、数据库、编译或 Actions；采用证据为 UNPROVEN，原 xlsx 母版未取得不等于当前 Markdown 未接受。[F L183](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/10/1099d259ef39a8e9__OA-FORM-01-DEVELOPER-SPEC-R1.md:183) [F L185](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/10/1099d259ef39a8e9__OA-FORM-01-DEVELOPER-SPEC-R1.md:185) [AF L374](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/78/7802303ae8a12840__UA-20261008-S3-OA-FORM-01-R1-MD02.json:374)

## 11. 缺口与决策边界

- restore 的真实留存/hold 政策和能力开关、FILE 引用处置及新 IAM 键需要实际 Owner 采用证据。[F L136](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/10/1099d259ef39a8e9__OA-FORM-01-DEVELOPER-SPEC-R1.md:136) [F L185](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/10/1099d259ef39a8e9__OA-FORM-01-DEVELOPER-SPEC-R1.md:185)
- unbound 必须有明确允许策略，不能在缺 binding 时自行认为允许提交。[F L89](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/10/1099d259ef39a8e9__OA-FORM-01-DEVELOPER-SPEC-R1.md:89)
- 草稿中金额/UOM、业务 Subject 与业务状态不由本模块自定义；APP 当前全文和S3资格对象已补读；本组当前必要L1/L2证据已核对并按准确组合归并；跨域Owner实际采用不由文档闭包授予。

## 12. 证据与实读范围

当前 FORM R1 L1–189 和 Common R1 L1–108 已全文；相关 DEF R1 L1–160、TASK R2 L1–294、APP 复用索引 L1–56、S3 CURRENT R2 L1–216、最终累计复核 L1–126 已全文。接受 JSON 为关键决策/AcceptedBody/原 SPEC/ReviewQualifications/OwnerAndExecutionBoundary 结构化阅读。矩阵/AC trace 保留其历史身份；不把本文当业务测试。逐来源 SHA、继承阅读、准确区间和待补内容在 `evidence/platform-engineering-reading.json`。

**当前必要材料归并状态。** 本册为 `required_materials_consolidated`：选定正文、规范附件及准确接受/复核范围已按直接实读、结构化语义或精确复用归并。身份清单核对不等底层源码全文阅读；历史草稿、候选实现和Office/HTML视觉未核范围仍单列。可复查[逐件阅读与处置](D:/CP6/docs/CP6_开发设计文档_20261010/evidence/platform-engineering-reading.json)及[本组闭包台账](D:/CP6/docs/CP6_开发设计文档_20261010/worklogs/platform-engineering-progress.json)。当前必需规范未读清单为空；具体Owner采用、参数/物理接口、真实运行和本节已有来源缺口不因整理闭包而改变。
