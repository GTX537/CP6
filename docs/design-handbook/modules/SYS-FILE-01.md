# SYS-FILE-01 · 附件与精确技术内容引用

整理状态：`core_semantics_consolidated`。已接受静态规则的开发展开；候选字段/接口仍与已有实现分开，运行采用UNPROVEN。

## 1. 业务目的、操作者与Owner边界

Main保留统一附件入口：业务上传者/编辑者上传，当前读者预览/下载，目标业务Owner建立绑定，内容保留管理员和受控worker处置。文件访问继承目标当前tenant/action/行/字段，并叠加技术用途、商业许可、保留、安全状态的更严限制；平台支持权不隐含全文件阅读。[原文B:5](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15d5aa240b03f6ab__S5_SYS-FILE-01_完整设计候选_v0.1.md:5) [原文B:11](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15d5aa240b03f6ab__S5_SYS-FILE-01_完整设计候选_v0.1.md:11)

文件服务只提供不可变内容和受控引用，不成为PLM图纸/技术包/基线主账，也不替业务Owner签制造、报价、发布资格。technical purpose grant与当前安全权是两道门，业务Owner负责最终使用/绑定和adoption receipt。[原文B:87](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15d5aa240b03f6ab__S5_SYS-FILE-01_完整设计候选_v0.1.md:87) [原文B:99](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15d5aa240b03f6ab__S5_SYS-FILE-01_完整设计候选_v0.1.md:99) [原文B:107](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15d5aa240b03f6ab__S5_SYS-FILE-01_完整设计候选_v0.1.md:107)

## 2. 选定组合与接受范围

当前v0.1正文SHA256 `15d5aa240b03f6ab711a1e1bedabee76e11d81e2325247ed55826b34f8c9a22b`，122行，7原SPEC（含PLM-AC-19技术内容子项）/30AC/17开发任务；进入S5 v0.2混合十稿的八个未变字节成员，继承原完整独审，双认证根复审没有将该册改v0.2。[原文A:179](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/8b/8bf0ee339ba1031d__UA-20261008-S5-SYS-FILE-01-STATIC-MD02.json:179) [原文R:14](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/e3/e3522cf5a9c30a6a__S5_v0.2_认证双根独立复审与最小问题报告_20261008.txt:14) [原文B:122](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15d5aa240b03f6ab__S5_SYS-FILE-01_完整设计候选_v0.1.md:122)

delegate接受是静态设计且非用户亲签，原02:18:29Z决定逐字文本未恢复，04:09:04Z恢复确认另记；作者STOPPED历史不回写，30AC NOT_RUN、Owner采用/运行UNPROVEN、实施false。[原文A:8](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/8b/8bf0ee339ba1031d__UA-20261008-S5-SYS-FILE-01-STATIC-MD02.json:8) [原文A:332](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/8b/8bf0ee339ba1031d__UA-20261008-S5-SYS-FILE-01-STATIC-MD02.json:332)

## 3. 对象、字段与精确身份

|对象|身份/字段|禁止混同|
|---|---|---|
|旧附件记录|Id、BizType/BizId、DraftToken、FileName、StoreName/Path、Size、ContentType、FileHash、Uploader、CreateDate|StorePath不是分享URL；MD5旧秒传/共享存储提示不是安全版本身份。[原文B:9](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15d5aa240b03f6ab__S5_SYS-FILE-01_完整设计候选_v0.1.md:9)|
|FileId / ContentRef / BusinessRef / Link|记录身份 / 不可变bytes身份 / `{owner,id,version}` / 导航|四者互不替代；去重不授跨对象权。[原文B:13](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15d5aa240b03f6ab__S5_SYS-FILE-01_完整设计候选_v0.1.md:13)|
|候选上传输入|multipart原file/bizType/bizId或draftToken；uploadOperationId/targetBusinessRef/version/purpose/declaredMediaType/expectedContentLength|tenant/actor服务端绑定；正式与draft语义不可混用，文件名不作路径。[原文B:17](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15d5aa240b03f6ab__S5_SYS-FILE-01_完整设计候选_v0.1.md:17)|
|UploadReceipt|fileRef、contentRef(version/digestAlgorithm/digest/byteLength)、state、inspectionStatus、operationRef、allowedNextActions|不回StorePath/存储凭据。[原文B:21](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15d5aa240b03f6ab__S5_SYS-FILE-01_完整设计候选_v0.1.md:21)|
|列表safe metadata|FileId/显示名/size/mediaType/createdAt/安全uploader摘要/bindingVersion/contentVersion/availability/retentionFlag|不回StoreName/StorePath/DraftToken；冻结业务按绑定版本取集合。[原文B:31](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15d5aa240b03f6ab__S5_SYS-FILE-01_完整设计候选_v0.1.md:31) [原文B:33](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15d5aa240b03f6ab__S5_SYS-FILE-01_完整设计候选_v0.1.md:33)|
|ContentReadReceipt|requestedContentRef/deliveredContentRef/representation(original或preview)/byteLength/mediaType/policyDecisionRef/integrityStatus|可仅服务端留，不泄敏感grant；衍生件另Ref。[原文B:47](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15d5aa240b03f6ab__S5_SYS-FILE-01_完整设计候选_v0.1.md:47)|
|正式绑定命令候选|BusinessRef、expectedDraftSetVersion、fileContentRefs、operationId、reason|目标Owner核真实对象版本/可编辑状态；不是仅换BizId。[原文B:57](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15d5aa240b03f6ab__S5_SYS-FILE-01_完整设计候选_v0.1.md:57)|
|DispositionRequest / Result|operationId/expectedBindingVersion/reason/policyVersion/requestedMode；UNLINKED/BLOB_RETAINED/DISPOSAL_PENDING/DELETED/HELD/UNKNOWN|解绑与物理删除独立；tombstone原Id不能重用给新bytes。[原文B:75](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15d5aa240b03f6ab__S5_SYS-FILE-01_完整设计候选_v0.1.md:75)|
|FileAccessDecision|tenant/actor/objectRef/contentRef/action/purpose/currentPolicyVersion/fieldMappingVersion/decisionId|server构造，前端canRead不权威。[原文B:89](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15d5aa240b03f6ab__S5_SYS-FILE-01_完整设计候选_v0.1.md:89)|
|TechnicalContentRef|contentOwner/contentId/contentVersion/hashAlgorithm/hash/byteLength/mediaType|具体PLM原包未给处不造真实id。[原文B:99](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15d5aa240b03f6ab__S5_SYS-FILE-01_完整设计候选_v0.1.md:99)|
|TechnicalBusinessRef / AuthorizedReference|原owner/id/version；requestingOwner/purpose/scopeRef+version/来源技术Ref/当前security decision与purpose grant/原operation+Owner receipt|内容、技术业务版本、当前授权采用分开。[原文B:99](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15d5aa240b03f6ab__S5_SYS-FILE-01_完整设计候选_v0.1.md:99)|

候选UPLOADING/STAGED/PENDING_INSPECTION/AVAILABLE/QUARANTINED/BOUND/DISPOSITION_PENDING/DELETED是展示/处理轴，不冒称旧Pub_Attachment已有枚举列。[原文B:13](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15d5aa240b03f6ab__S5_SYS-FILE-01_完整设计候选_v0.1.md:13)

## 4. 上传、绑定、读取、处置写集

上传：当前身份/tenant→目标Owner upload范围→流式大小边界→实际允许类型/安全检查→持久stage→真实byteLength+安全digest→本tenant记录与原operation receipt→返回STAGED或AVAILABLE。安全检查未证时禁新预览/技术引用，legacy范围单列。旧先全读内存后检查只是旧能力，不是新安全保证；复用同摘要还核size/type/确实同bytes/存储域且不泄别对象存在性。[原文B:19](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15d5aa240b03f6ab__S5_SYS-FILE-01_完整设计候选_v0.1.md:19) [原文B:23](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15d5aa240b03f6ab__S5_SYS-FILE-01_完整设计候选_v0.1.md:23)

绑定：当前actor草稿全集→同tenant/inspection/content→目标当前add-attachment与business expectedVersion→真实Owner事务保存绑定集合、业务引用、receipt/audit。store与DB不同时先stage再确认引用，如实呈部分/unknown；新正式迁移关系需源/目标双边权+冻结/保留门，不能用draft rebind改已审批证据。[原文B:59](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15d5aa240b03f6ab__S5_SYS-FILE-01_完整设计候选_v0.1.md:59)

读取：当前认证tenant→记录归属/绑定版本→Owner read+purpose→hold/处置/inspection→准确store bytes/digest→受控stream。技术原件摘要错隔离CONTENT_INTEGRITY_UNAVAILABLE，不fallback latest/同名；预览/OCR/转换另derivedFrom Ref。[原文B:43](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15d5aa240b03f6ab__S5_SYS-FILE-01_完整设计候选_v0.1.md:43) [原文B:45](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15d5aa240b03f6ab__S5_SYS-FILE-01_完整设计候选_v0.1.md:45) [原文B:47](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15d5aa240b03f6ab__S5_SYS-FILE-01_完整设计候选_v0.1.md:47)

删除：当前delete、目标可编辑、非冻结批准集、无hold、完整引用图、无UNKNOWN/待重放必要证据→先持久解绑/保留决定及receipt→受控store任务做允许物理处置。全域共享blob只能由store Owner完整引用与处置门决定，tenant局部count0不等可删物理文件。[原文B:73](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15d5aa240b03f6ab__S5_SYS-FILE-01_完整设计候选_v0.1.md:73) [原文B:75](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15d5aa240b03f6ab__S5_SYS-FILE-01_完整设计候选_v0.1.md:75)

技术采用：技术Owner提出原BusinessRef/operation/purpose/scope/批准凭据→IAM当前安全门与用途门分别判→File核exact manifest逐项bytes/版本/hash/长度/inspection→历史read只返当前可见原Ref；新业务绑定在真正业务Owner本地事务持共同最终门和用途保护→保存AuthorizedReference、receipt/outbox/audit。adoption receipt精确含owner/operation/subjectVersion/technicalBusinessRef/manifestDigest/contentRefs/purpose/scopeRef/grantRef/securityDecisionRef/outcome。[原文B:103](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15d5aa240b03f6ab__S5_SYS-FILE-01_完整设计候选_v0.1.md:103) [原文B:105](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15d5aa240b03f6ab__S5_SYS-FILE-01_完整设计候选_v0.1.md:105) [原文B:107](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15d5aa240b03f6ab__S5_SYS-FILE-01_完整设计候选_v0.1.md:107) [原文B:108](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15d5aa240b03f6ab__S5_SYS-FILE-01_完整设计候选_v0.1.md:108)

## 5. API、数据域及具名消费者限制

现POST `/api/pub/attachment/upload`；空文件400、E-PUB-061超限/062类型/063菜单权。现GET `/api/pub/attachment/list?bizType&bizId`返回该业务所有记录/CreateDate排序，ListDraftAsync存在但没有本轮已证公开draft route。现GET `/api/pub/attachment/{id}/download`、`/preview`；POST `/api/pub/attachment/rebind`接DraftToken/BizId。候选补operation、分页、对象/版本、完整性与当前用途权，不能声称旧菜单校验已满足。[原文B:21](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15d5aa240b03f6ab__S5_SYS-FILE-01_完整设计候选_v0.1.md:21) [原文B:31](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15d5aa240b03f6ab__S5_SYS-FILE-01_完整设计候选_v0.1.md:31) [原文B:43](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15d5aa240b03f6ab__S5_SYS-FILE-01_完整设计候选_v0.1.md:43) [原文B:57](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15d5aa240b03f6ab__S5_SYS-FILE-01_完整设计候选_v0.1.md:57)

技术逻辑接口为ResolveExactTechnicalContent、ReadAuthorizedRepresentation、ConfirmReferenceAdoption、QueryOriginalReferenceReceipt；HTTP路由尚未创建。DTO包含requestRef/domain/schemaVersion/sourceNativeRef和digest域，业务内容digest与运输digest分开，沿S6原operation优先和三类typed replay，不用generic retry重取latest。[原文B:112](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15d5aa240b03f6ab__S5_SYS-FILE-01_完整设计候选_v0.1.md:112)

**CRM-ACT v1.1.1的本版限制必须单独遵守。** ACT选择无远端副作用预检+文件Owner认可的本地权威Gate作为唯一提交模式；Owner授权/保留/扫描变更通过该Gate生效，Activity事务共享锁保护，撤权writer排他。它不是异步缓存。预检不占文件、不建临时hold、不二次remote commit、不生成异步待完成Activity。Owner只有普通远端GET、TTL票据、remote reserve/confirm时返回NO_COMMIT_GUARANTEE/COMMIT_WINDOW_UNPROVEN并禁相关动作；通用FILE stage描述不能推成ACT采用远端协议。[原文C:531](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/67/67dc3667fb852c56__CRM-ACT-01_互动记录与修订追踪_前后端开发Spec_v1.1.1_REVIEW.md:531) [原文C:533](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/67/67dc3667fb852c56__CRM-ACT-01_互动记录与修订追踪_前后端开发Spec_v1.1.1_REVIEW.md:533)

ACT附件Add/Retain/Remove各自决定：新增需当前范围/用途/精确版本/安全状态/最终Add守卫；Retain证原属S0及Owner历史保留规则，不强迫无内容读权者重新下载Clean；Remove需精确Ref/版本/范围/原因和Unlink决定，不隐含物理删除。任何新项未许可整意图写0，不能先删旧项；UNKNOWN保原S1/Key，成功物理删除数0。[原文C:525](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/67/67dc3667fb852c56__CRM-ACT-01_互动记录与修订追踪_前后端开发Spec_v1.1.1_REVIEW.md:525) [原文C:537](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/67/67dc3667fb852c56__CRM-ACT-01_互动记录与修订追踪_前后端开发Spec_v1.1.1_REVIEW.md:537) [原文C:539](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/67/67dc3667fb852c56__CRM-ACT-01_互动记录与修订追踪_前后端开发Spec_v1.1.1_REVIEW.md:539) [原文C:541](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/67/67dc3667fb852c56__CRM-ACT-01_互动记录与修订追踪_前后端开发Spec_v1.1.1_REVIEW.md:541)

## 6. 幂等、并发与恢复

upload响应丢失查原uploadOperation；绑定同operation+同draft集合+同目标读原结果，同键异内容/目标409；旧draftToken不能抢回已bound集合；冻结变化走新业务修订/审批。任一必需Owner门缺失保持STAGED/BLOCKED，无业务完成事实。[原文B:21](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15d5aa240b03f6ab__S5_SYS-FILE-01_完整设计候选_v0.1.md:21) [原文B:61](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15d5aa240b03f6ab__S5_SYS-FILE-01_完整设计候选_v0.1.md:61)

绑定、解绑等新效果持Owner真实共同fence到commit；外域无法等价协调就禁新效果，当前合法历史read仍可查询。file服务不能用普通lease替代消费者具体提交保证，ACT限制见上节。列表游标绑定tenant/对象版本；412展示draft/目标差异；unknown查原receipt而非多次rebind。[原文B:35](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15d5aa240b03f6ab__S5_SYS-FILE-01_完整设计候选_v0.1.md:35) [原文B:63](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15d5aa240b03f6ab__S5_SYS-FILE-01_完整设计候选_v0.1.md:63) [原文B:89](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15d5aa240b03f6ab__S5_SYS-FILE-01_完整设计候选_v0.1.md:89)

物理删失败不等逻辑行未删。候选unknown查plan、对象和store状态，不能反复重建原行假修复；仅获准恢复，不能复活擦除PII。exact内容暂不可读只重试同Ref；采用unknown找Owner原receipt，NOT_FOUND可能是保留过期/副本延迟，不能推断从未采用。[原文B:71](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15d5aa240b03f6ab__S5_SYS-FILE-01_完整设计候选_v0.1.md:71) [原文B:77](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15d5aa240b03f6ab__S5_SYS-FILE-01_完整设计候选_v0.1.md:77) [原文B:114](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15d5aa240b03f6ab__S5_SYS-FILE-01_完整设计候选_v0.1.md:114)

## 7. 权限、tenant、字段与用途

旧BizType→HasMenu是最低旧门，EnforceBizPermission=false可配置不是新授权就绪。候选逻辑file.upload/list/preview/download/bind/unlink、technical.reference需IAM/业务Owner真实码映射。按IAM MIN后完整注册域物化：对象不可见无列表；附件字段隐藏不出名称/数量/正文；readonly不能改绑定，但read允许才可下载；服务/模拟不继承人类隐式许可。[原文B:85](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15d5aa240b03f6ab__S5_SYS-FILE-01_完整设计候选_v0.1.md:85) [原文B:87](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15d5aa240b03f6ab__S5_SYS-FILE-01_完整设计候选_v0.1.md:87)

下载每个新请求、分段、续传都核当前权；短链即使有也仅限目标/用户/用途/时间，不能绕Owner规则。已合法发给用户的bytes无法承诺远程收回。无权不能通过缩略/OCR/搜索片段旁路；audit只safe Ref/action/actor/purpose，不抄正文。[原文B:45](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15d5aa240b03f6ab__S5_SYS-FILE-01_完整设计候选_v0.1.md:45) [原文B:49](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15d5aa240b03f6ab__S5_SYS-FILE-01_完整设计候选_v0.1.md:49) [原文B:91](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15d5aa240b03f6ab__S5_SYS-FILE-01_完整设计候选_v0.1.md:91)

## 8. 页面与服务端检查

上传进度区分“已上传待检/可绑定”，取消已提交未知先查；列表区分可预览/待检/hold/已处置/无权/store不可用，授权真空列表不混503；切对象丢旧响应。预览明确原件/衍生件及精确版本，不以同名混替。[原文B:23](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15d5aa240b03f6ab__S5_SYS-FILE-01_完整设计候选_v0.1.md:23) [原文B:35](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15d5aa240b03f6ab__S5_SYS-FILE-01_完整设计候选_v0.1.md:35) [原文B:49](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15d5aa240b03f6ab__S5_SYS-FILE-01_完整设计候选_v0.1.md:49)

业务保存结果区分“业务成功，附件待确认”，不全绿；历史替换是新版本/引用。删除确认区分仅解绑与不可恢复物理删除；技术页显示版本/purpose/scope、仅可读/待门/已采纳/用途撤销/缺证；撤权清当前敏感预览链接，不声称抹用户副本。[原文B:63](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15d5aa240b03f6ab__S5_SYS-FILE-01_完整设计候选_v0.1.md:63) [原文B:77](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15d5aa240b03f6ab__S5_SYS-FILE-01_完整设计候选_v0.1.md:77) [原文B:91](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15d5aa240b03f6ab__S5_SYS-FILE-01_完整设计候选_v0.1.md:91) [原文B:112](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15d5aa240b03f6ab__S5_SYS-FILE-01_完整设计候选_v0.1.md:112)

## 9. 开发顺序与固定源码差距

固定Main90c871...AttachmentService/Controller：旧默认20MB、内存全读、MD5/StorePath共享、菜单级门；rebind上传者/BizType检查不是目标版本/可编辑/冻结门；Delete先删行SaveChanges再按StorePath引用检查，失败可能无引用blob且未见普遍recovery journal。[原文B:5](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15d5aa240b03f6ab__S5_SYS-FILE-01_完整设计候选_v0.1.md:5) [原文B:9](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15d5aa240b03f6ab__S5_SYS-FILE-01_完整设计候选_v0.1.md:9) [原文B:57](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15d5aa240b03f6ab__S5_SYS-FILE-01_完整设计候选_v0.1.md:57) [原文B:71](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15d5aa240b03f6ab__S5_SYS-FILE-01_完整设计候选_v0.1.md:71)

17任务按依赖：①受限stream/真实inspection/原uploadOperation；②Owner对象/草稿列表和当前安全stream；③草稿/正式绑定adapter及冻结引用；④全域引用/hold/store处置；⑤实际权限/字段/用途共同门；⑥技术Owner exact manifest、内容衍生身份、adoption receipt；⑦页面和S6/AUD/PRIV恢复保留。消费者ACT先满足指定本地Gate，不能以泛接口存在交差。[原文B:27](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15d5aa240b03f6ab__S5_SYS-FILE-01_完整设计候选_v0.1.md:27) [原文B:39](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15d5aa240b03f6ab__S5_SYS-FILE-01_完整设计候选_v0.1.md:39) [原文B:53](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15d5aa240b03f6ab__S5_SYS-FILE-01_完整设计候选_v0.1.md:53) [原文B:67](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15d5aa240b03f6ab__S5_SYS-FILE-01_完整设计候选_v0.1.md:67) [原文B:81](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15d5aa240b03f6ab__S5_SYS-FILE-01_完整设计候选_v0.1.md:81) [原文B:95](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15d5aa240b03f6ab__S5_SYS-FILE-01_完整设计候选_v0.1.md:95) [原文B:118](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15d5aa240b03f6ab__S5_SYS-FILE-01_完整设计候选_v0.1.md:118) [原文C:531](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/67/67dc3667fb852c56__CRM-ACT-01_互动记录与修订追踪_前后端开发Spec_v1.1.1_REVIEW.md:531)

## 10. 验收场景与状态

30AC全NOT_RUN：FI01边界检查、MD5/安全身份分开、unknown原键、inspection未就绪禁新用；FI02对象/草稿授权/冻结列表/晚响应；FI03当前read续传/衍生identity/hash错隔离/无缩略旁路；FI04双目标门/原键冲突/冻结新修订/分库部分状态；FI05完整引用和hold/共享blob/解绑物理分离/tombstone；FI06菜单与对象字段用途区别、安全和用途交集、真实fence不足关闭；FI07exact manifest无latest、derived不冒原件、精确adoption、批准后变更新修订、未知必要证据不删且未签不冒采用。[原文B:25](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15d5aa240b03f6ab__S5_SYS-FILE-01_完整设计候选_v0.1.md:25) [原文B:37](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15d5aa240b03f6ab__S5_SYS-FILE-01_完整设计候选_v0.1.md:37) [原文B:51](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15d5aa240b03f6ab__S5_SYS-FILE-01_完整设计候选_v0.1.md:51) [原文B:65](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15d5aa240b03f6ab__S5_SYS-FILE-01_完整设计候选_v0.1.md:65) [原文B:79](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15d5aa240b03f6ab__S5_SYS-FILE-01_完整设计候选_v0.1.md:79) [原文B:93](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15d5aa240b03f6ab__S5_SYS-FILE-01_完整设计候选_v0.1.md:93) [原文B:116](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15d5aa240b03f6ab__S5_SYS-FILE-01_完整设计候选_v0.1.md:116)

## 11. 未完成与不能推定事项

真实PLM manifest/具体引用ID、用途grant协议、全域引用图、inspection/store完整性实现、权限映射和Owner提交门尚Required/UNPROVEN；缺项保持对应resource RequiredFenced，可查合法历史不能显示放行制造/报价/发布。审批后任一bytes/版本/用途/scope变化须技术Owner新修订和批准，文件服务无权自行判等价。[原文B:99](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15d5aa240b03f6ab__S5_SYS-FILE-01_完整设计候选_v0.1.md:99) [原文B:110](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15d5aa240b03f6ab__S5_SYS-FILE-01_完整设计候选_v0.1.md:110) [原文B:118](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15d5aa240b03f6ab__S5_SYS-FILE-01_完整设计候选_v0.1.md:118)

ACT本版限制已源文核对，不构成SYS-FILE通用规则与ACT矛盾；它是消费者更具体的准入条件。本册未声称已搭建该Gate或运行文件业务。[原文C:531](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/67/67dc3667fb852c56__CRM-ACT-01_互动记录与修订追踪_前后端开发Spec_v1.1.1_REVIEW.md:531) [原文C:533](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/67/67dc3667fb852c56__CRM-ACT-01_互动记录与修订追踪_前后端开发Spec_v1.1.1_REVIEW.md:533)

## 12. 证据与阅读覆盖

本次FILE全文1–122；ACT只读525–541（Add/Retain/Remove、唯一提交模式及整集合原子），不能据此称全文审ACT；S5复核全文1–128；FILE接受件选择完整决定/正文/组合/ReviewQualifications/执行边界键。机器readlog保存精确范围/未读段。没有实际上传、下载业务内容、处置文件、编译、测试或CI。[原文B:1](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15d5aa240b03f6ab__S5_SYS-FILE-01_完整设计候选_v0.1.md:1) [原文C:525](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/67/67dc3667fb852c56__CRM-ACT-01_互动记录与修订追踪_前后端开发Spec_v1.1.1_REVIEW.md:525) [原文R:1](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/e3/e3522cf5a9c30a6a__S5_v0.2_认证双根独立复审与最小问题报告_20261008.txt:1) [原文A:8](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/8b/8bf0ee339ba1031d__UA-20261008-S5-SYS-FILE-01-STATIC-MD02.json:8)

机器合同见 [platform-engineering-contracts.json](D:/CP6/docs/CP6_开发设计文档_20261010/contracts/platform-engineering-contracts.json)，阅读范围见 [platform-engineering-reading.json](D:/CP6/docs/CP6_开发设计文档_20261010/evidence/platform-engineering-reading.json)。

**当前必要材料归并状态。** 本册为 `required_materials_consolidated`：选定正文、规范附件及准确接受/复核范围已按直接实读、结构化语义或精确复用归并。身份清单核对不等底层源码全文阅读；历史草稿、候选实现和Office/HTML视觉未核范围仍单列。可复查[逐件阅读与处置](D:/CP6/docs/CP6_开发设计文档_20261010/evidence/platform-engineering-reading.json)及[本组闭包台账](D:/CP6/docs/CP6_开发设计文档_20261010/worklogs/platform-engineering-progress.json)。当前必需规范未读清单为空；具体Owner采用、参数/物理接口、真实运行和本节已有来源缺口不因整理闭包而改变。
