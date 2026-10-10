# SPACE-PUB-01 · 源空间发布、WMS 采纳与独立 Runtime 效果

整理状态：`core_semantics_consolidated`。本册六个原 SPEC 为发布预览、差异、WMS 采纳、停用、重试、来源对应。H35、SC23、DP11/12/16 保留原命名空间。生产者新协议与共同冻结门是已选定静态设计，仍待实际采用、实现和运行验收；27 AC 为 NOT_RUN。[原文P:7](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15f966e5afe3dc71__SPACE-PUB-01_完整详细设计_R2_CANDIDATE.md:7) [原文A:6](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/97/9782d6d7ebdf7b49__UA-20261008-S4-SPACE-PUB-01-R2-MD02.json:6)

## 1. 目的、操作者和业务 Owner

设计者维护源设计；Space 发布者批准固定版本/hash/完整 Target 的源发布；生命周期负责人提出停止新使用的准确后继；WMS 接收者在 WMS 做映射、前检、批准、整目标采纳；恢复员查原键并执行服务当前允许的原步骤。后台发送者使用可信服务身份，不能把客户端租户头当权威。[原文P:43](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15f966e5afe3dc71__SPACE-PUB-01_完整详细设计_R2_CANDIDATE.md:43)

Space 写设计 S、不可变 Publication、TargetDefinition、源生命周期与 lineage；WMS 写目录 B、正式作业 L、映射和 Canonical 结果；Stock/Reservation/Task 持有实物、占用及作业事实；Runtime 决定并证明真实激活。S/B/L 可有历史 S.Id=B.Id，正式 L 必须具名映射/批准创建，不能按 Code/Path 猜相同。发布、运输、WMS APPLIED、Runtime Desired、Actual、设计 current 晋升分别留证。[原文P:13](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15f966e5afe3dc71__SPACE-PUB-01_完整详细设计_R2_CANDIDATE.md:13) [原文P:33](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15f966e5afe3dc71__SPACE-PUB-01_完整详细设计_R2_CANDIDATE.md:33) [原文W:1944](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/df/df146652520facaf__WMS-MASTER-01_仓库库位与Space采纳_前后端开发Spec_v1.1.1_REVIEW.md:1944)

## 2. 精确规范、接受与范围

当前接受是 S4 R2 SHA `15f966e5afe3dc71a41b3c7722e6c0813bf1e630e9516e266558a779442aa8fd` 与 X6 R2 SHA `6f8585ca3b85c6aaafb2a8eaf2dfda7ce8a3b1ae590987b3bcf19b1e3d5f4290` 的准确组合。正文 CANDIDATE/STOPPED/待独审是保留历史，后继 UA-20261008-S4-SPACE-PUB-01-R2-MD02 已接受当前静态 Markdown。独审 R01～R04 CLOSED_STATIC 不等真实能力通过；8 Target/41 SPEC 累计 27 HIT＋14 有界保留，87 新 AC 与 207 历史 WMS AC 均未运行。[原文A:3](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/97/9782d6d7ebdf7b49__UA-20261008-S4-SPACE-PUB-01-R2-MD02.json:3) [原文A:160](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/97/9782d6d7ebdf7b49__UA-20261008-S4-SPACE-PUB-01-R2-MD02.json:160) [原文A:556](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/97/9782d6d7ebdf7b49__UA-20261008-S4-SPACE-PUB-01-R2-MD02.json:556) [原文R:5](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/c4/c46bf5d8d9ed7e2c__CP6_S4_X6_R2_独立定点复核与累计范围.md:5) [原文R:194](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/c4/c46bf5d8d9ed7e2c__CP6_S4_X6_R2_独立定点复核与累计范围.md:194)

WMS 消费权威是 v1.1.1、SHA `df146652520facaf080762b5320a2872cc93fbed2fcbb193eef38488fe71d354`。S4 给 producer 输入和兼容映射，不改写 WMS 规范，也不代表 S1 已采用。旧反向 location/adopt 是 WMS→Space 设计导入，旧逐项 B 写入/modern saga Completed 不能升格为新 Canonical 全目标 APPLIED。[原文P:11](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15f966e5afe3dc71__SPACE-PUB-01_完整详细设计_R2_CANDIDATE.md:11) [原文P:19](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15f966e5afe3dc71__SPACE-PUB-01_完整详细设计_R2_CANDIDATE.md:19) [原文P:179](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15f966e5afe3dc71__SPACE-PUB-01_完整详细设计_R2_CANDIDATE.md:179)

## 3. 实体、字段、身份与摘要

| 对象 | 关键字段与不可混淆的身份 |
|---|---|
| DesignVersion / FrozenSourceBinding | Model/Site、VersionNo、Purpose、BasedOnVersionId、ContentRevision/ContentHash/ValidatedHash；FrozenBinding 保存准确设计版/内容/验证、publicationId/ref/version、freezeRevision、源批准/前驱、预期 current 指针值与 revision。Tenant＋DesignVersionId 唯一，永久不可解冻。[原文P:35](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15f966e5afe3dc71__SPACE-PUB-01_完整详细设计_R2_CANDIDATE.md:35) [原文P:275](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15f966e5afe3dc71__SPACE-PUB-01_完整详细设计_R2_CANDIDATE.md:275) |
| Publication / TargetDefinition | 源 namespace/Site、opaque publicationRef/version、layout/base（初始显式 INITIAL）、完整快照/manifest、sourceDecision、原生引用；targetUnitId、scopeDefinitionVersion、完整 members/dependencies/aliases、partitionAuthorizationRef。首次冻结全部目标固定。[原文P:36](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15f966e5afe3dc71__SPACE-PUB-01_完整详细设计_R2_CANDIDATE.md:36) [原文P:65](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15f966e5afe3dc71__SPACE-PUB-01_完整详细设计_R2_CANDIDATE.md:65) [原文P:130](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15f966e5afe3dc71__SPACE-PUB-01_完整详细设计_R2_CANDIDATE.md:130) |
| SourceLocationRevisionRecord / VersionItemBinding | 可信 Tenant/Site/logical ID、positiveRevision、item digest、源状态、native 来源/版本、前修订；Binding 把设计版/ContentRevision/logical ID 锁定到具体源修订。真正改变内容或生命周期才增号，克隆未改沿旧修订。[原文P:134](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15f966e5afe3dc71__SPACE-PUB-01_完整详细设计_R2_CANDIDATE.md:134) |
| Delivery / Attempt / outbox | 一个 publication/target 一个逻辑 Delivery、可多个网络 Attempt；原 operationKey/requestDigest、attemptRevision、transportState、consumerLocator、原结果可用性/回执、错误/允许恢复步骤。接收只改运输状态。[原文P:98](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15f966e5afe3dc71__SPACE-PUB-01_完整详细设计_R2_CANDIDATE.md:98) [原文P:110](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15f966e5afe3dc71__SPACE-PUB-01_完整详细设计_R2_CANDIDATE.md:110) [原文P:175](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15f966e5afe3dc71__SPACE-PUB-01_完整详细设计_R2_CANDIDATE.md:175) |
| ProducerCommandResult | locator＝operationKind≤40、规范 targetKey≤160、operationKey 1～80；canonicalizationVersion/requestDigest、intentDigest、固定 resultKind/id/ref、命令状态、currentEffect、coverage。Tenant 从可信上下文取得。[原文P:114](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15f966e5afe3dc71__SPACE-PUB-01_完整详细设计_R2_CANDIDATE.md:114) |
| StopIntent / StopSuccessorSlot | 原 publication/Target、明确非空停止集合 D、完整成员 M、原前驱/current 指针、源决定、非空后继设计版/Binding/publicationId/ref/version、原 outbox/交付/结果；M 与 D 不混用。[原文P:214](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15f966e5afe3dc71__SPACE-PUB-01_完整详细设计_R2_CANDIDATE.md:214) [原文P:226](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15f966e5afe3dc71__SPACE-PUB-01_完整详细设计_R2_CANDIDATE.md:226) |
| CompletionObservation / DesignPromotion | 一个 Binding 一个 promotionId；准确全部目标 WMS 原生 APPLIED、immutable receipt/semanticDigest/writeSet、Runtime 全范围 desired/actual/原生回执和两类投影 watermark、源/current 预期、原发布者、原子晋升结果。[原文P:315](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15f966e5afe3dc71__SPACE-PUB-01_完整详细设计_R2_CANDIDATE.md:315) |

线型 `sourceLocationVersion` 是 1～9223372036854775807 的正 int64 十进制字符串：禁止符号、前导零、空白、小数、指数、null/缺失和非数；最大值可读、不可再增。非法 native 原值留 lineage，不补 1/INITIAL/发布 VersionNo。源内容不满足新封存返回 422 SOURCE_LOCATION_VERSION_INVALID，严格接收 DTO 格式错误按 400。旧 NextExternalVersion fallback 仅 legacy，迁移必须有真实源修订/内容/前驱证据。[原文P:136](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15f966e5afe3dc71__SPACE-PUB-01_完整详细设计_R2_CANDIDATE.md:136) [原文P:138](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15f966e5afe3dc71__SPACE-PUB-01_完整详细设计_R2_CANDIDATE.md:138) [原文P:140](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15f966e5afe3dc71__SPACE-PUB-01_完整详细设计_R2_CANDIDATE.md:140)

schemaVersion 准确为 `"1"`；信封最多 2 MiB、manifest 最多 2000 项，不能为过限自动切业务 Target。源 System/Namespace 各≤100；publication/layout 引用是不透明≤100字符串。源码 100 域与新 L 30 域分开，禁止截断/散列补码；路径仅来源几何，尺寸与未知单位不能推 WMS 容量。EvidenceRef 原 owner/id/version/digest 保留，缺 digest 不伪填。[原文P:65](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15f966e5afe3dc71__SPACE-PUB-01_完整详细设计_R2_CANDIDATE.md:65) [原文P:75](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15f966e5afe3dc71__SPACE-PUB-01_完整详细设计_R2_CANDIDATE.md:75) [原文P:85](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15f966e5afe3dc71__SPACE-PUB-01_完整详细设计_R2_CANDIDATE.md:85) [原文P:205](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15f966e5afe3dc71__SPACE-PUB-01_完整详细设计_R2_CANDIDATE.md:205)

分开保存 raw SHA、native PlanHash、sourceContent、DELTA manifestDigest、完整成员 memberManifestDigest、producer request/intentDigest、WMS request/semanticDigest 及各规范版本。sourceContent 排除自身摘要、接收时间、运输 attempt；manifest 包含协议有序操作项；memberManifest 固定全目标成员/依赖。WMS semanticDigest 由 WMS 结合映射、效果与前驱计算，Space 不拿 PlanHash 冒充。wms-intent-v1 的 Guid 小写、int64字符串、decimal固定8位、集合去重排序、有序清单保序、presence/CLEAR/N/A/UNKNOWN 不折叠等只用于声明的规范域，不全局改 native。[原文P:81](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15f966e5afe3dc71__SPACE-PUB-01_完整详细设计_R2_CANDIDATE.md:81) [原文P:85](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15f966e5afe3dc71__SPACE-PUB-01_完整详细设计_R2_CANDIDATE.md:85) [原文P:267](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15f966e5afe3dc71__SPACE-PUB-01_完整详细设计_R2_CANDIDATE.md:267) [原文W:3080](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/df/df146652520facaf__WMS-MASTER-01_仓库库位与Space采纳_前后端开发Spec_v1.1.1_REVIEW.md:3080)

## 4. 状态、命令与本地写集

设计旧生命周期保持 Initializing→Draft→Validating→Ready→Publishing→Published→Superseded 及原失败分支；FROZEN 是独立源绑定事实，不新增 Version.Status 枚举。源当前性为 FROZEN/SUPERSEDED/REVOKED_FOR_FUTURE_USE；运输的 AWAITING_DELIVERY_AUTHORIZATION/HOLD_OWNER_GATE/QUEUED/SENDING/RECEIVED/UNKNOWN/RETRYABLE_FAILED/TERMINAL_REJECTED 与消费者状态分开；Promotion 为 WAITING_OWNER_FACTS/READY/COMMITTED/CONFLICT/UNKNOWN。[原文X:113](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:113) [原文P:108](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15f966e5afe3dc71__SPACE-PUB-01_完整详细设计_R2_CANDIDATE.md:108) [原文P:110](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15f966e5afe3dc71__SPACE-PUB-01_完整详细设计_R2_CANDIDATE.md:110) [原文P:315](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15f966e5afe3dc71__SPACE-PUB-01_完整详细设计_R2_CANDIDATE.md:315)

1. 预览读取同一设计内容修订、源位置 Binding、完整 Target、validation、current/source frontier 与写门；先算全量后分页。Draft/Ready 可诊断，但封存只能 Production Ready，验证必须同 ContentHash/RuleSetVersion/WmsCapabilityHash；Scenario 无生产资格。[原文P:145](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15f966e5afe3dc71__SPACE-PUB-01_完整详细设计_R2_CANDIDATE.md:145)
2. freezeContext 在 Tenant＋DesignVersionId 唯一槽预留 publicationId/ref/version；源批准绑定已知身份和准确 previewDigest。预留不代表已封存；废弃预留留墓碑，不给另一版重复用。[原文P:130](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15f966e5afe3dc71__SPACE-PUB-01_完整详细设计_R2_CANDIDATE.md:130)
3. SOURCE_FREEZE 本地事务锁 Model→Version→WriteEligibility→位置修订/完整目标→Command/Binding，复核内容/validation、写门、source/current 前驱，写 Publication/全 Target/Manifest/SourceRef、FrozenBinding、source frontier、命令结果/审计、不可发送 outbox。不增 ContentRevision、不清 ValidatedHash、不 MarkPublished；ActiveDraft 仅恰指该版才 CAS 置空，current 不变。[原文P:173](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15f966e5afe3dc71__SPACE-PUB-01_完整详细设计_R2_CANDIDATE.md:173) [原文P:275](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15f966e5afe3dc71__SPACE-PUB-01_完整详细设计_R2_CANDIDATE.md:275)
4. SOURCE_DELIVER 是独立明确授权；Owner 门齐备才激活原逻辑 Delivery/outbox，发送准确固定内容。技术 batch 只分运输，不分业务原子单位。Consumer RECEIVED 不改 APPLIED。[原文P:175](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15f966e5afe3dc71__SPACE-PUB-01_完整详细设计_R2_CANDIDATE.md:175)
5. WMS 自身 ResolveTarget/Mapping/完整前检后竞争 Canonical、取得持久保护，在本地全目标事务写 B/L/映射/基准及 receipt；Space 只读回执。新 L 仍 DRAFT，复用 L 不初始化/重置库存控制。[原文P:179](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15f966e5afe3dc71__SPACE-PUB-01_完整详细设计_R2_CANDIDATE.md:179) [原文W:1953](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/df/df146652520facaf__WMS-MASTER-01_仓库库位与Space采纳_前后端开发Spec_v1.1.1_REVIEW.md:1953) [原文W:2106](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/df/df146652520facaf__WMS-MASTER-01_仓库库位与Space采纳_前后端开发Spec_v1.1.1_REVIEW.md:2106)
6. CurrentPublishedFinalizer 只消费已核全模型、全部目标 APPLIED、同 publication/layout 的 Runtime 原生 desired/actual 激活、完整 Scope/映射与流代次证据。先持久化不可变 Observation，再本地事务复核 current/source 指针值＋revision、已收 Owner watermark，原子 Ready→Publishing→Published、旧 current→Superseded、新 current/revision、Promotion.COMMITTED/审计/outbox；不改内容、不清别的新 draft、不调用旧 WMS writer/Runtime.Activate。[原文P:311](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15f966e5afe3dc71__SPACE-PUB-01_完整详细设计_R2_CANDIDATE.md:311)

停止只选模式 A：StopPreview 明确 D⊆完整原 Target 的 M，D 非空；预留后继版/Publication 和 D 下一源修订。提交同一 Space 事务锁原模型/frontier/停止槽/位置预留，兑现非空新后继、复制完整原几何/来源，D 改停止、M\D 内容/修订保留；专用流程按固定新 hash/规则/能力验证完成 Initializing→Draft→Validating→Ready＋FROZEN，不借旧验证直接 Ready、不占普通 ActiveDraft。写 Intent/StateEvent/frontier CAS/原键结果及原 T 的不可发送 outbox。[原文P:210](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15f966e5afe3dc71__SPACE-PUB-01_完整详细设计_R2_CANDIDATE.md:210) [原文P:220](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15f966e5afe3dc71__SPACE-PUB-01_完整详细设计_R2_CANDIDATE.md:220)

停止 DELTA 仅发送 D 的 DISABLE，未选成员按遗漏语义 KEEP，不能新增 KEEP/RESTORE 线枚举或把 D 改成小 Target；TargetDefinition 仍完整 M/依赖，WMS 整 T 前检保护并全提交或全拒绝。WMS 拒绝不回滚源已提出事实；UNKNOWN 固定 P2/原 Canonical 查询，不再造 P3试一次。WMS RESTORE 独立核原停止回执、全部控制前驱和新批准，RETIRED 不恢复，旧任务不重开。[原文P:216](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15f966e5afe3dc71__SPACE-PUB-01_完整详细设计_R2_CANDIDATE.md:216) [原文P:228](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15f966e5afe3dc71__SPACE-PUB-01_完整详细设计_R2_CANDIDATE.md:228) [原文P:230](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15f966e5afe3dc71__SPACE-PUB-01_完整详细设计_R2_CANDIDATE.md:230)

## 5. 接口与消费者对应

新候选前缀 `/api/space/publications/v1`，不是已上线端点；旧 design publish-attempts 与 floor publish 路由/profile 保持原义，不自动转发。[原文P:87](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15f966e5afe3dc71__SPACE-PUB-01_完整详细设计_R2_CANDIDATE.md:87)

| 接口 | 必需请求和效果 |
|---|---|
| POST /previews；GET /previews/{id}/items | versionId、expectedContentRevision、expectedPublishedVersionId 或 INITIAL、targetDefinitionRef、manifestKind；返回固定 preview/context/预留身份/完整清单引用。cursor 绑定身份版本筛选，limit 1～200。[原文P:93](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15f966e5afe3dc71__SPACE-PUB-01_完整详细设计_R2_CANDIDATE.md:93) |
| POST /publications；GET /publications/{id} | previewId/digest、contextRef、expectedContentRevision/writeGateRevision、sourceDecisionRef、reason 1～1000 和 Idempotency-Key；201 新封存或200原结果；GET 原 immutable 信封和当前附属效力。[原文P:95](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15f966e5afe3dc71__SPACE-PUB-01_完整详细设计_R2_CANDIDATE.md:95) |
| GET /publications/{id}/targets/{targetId}/manifest | 传输分块、完整 header/digest/totalItems，禁止只采纳当前页。[原文P:97](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15f966e5afe3dc71__SPACE-PUB-01_完整详细设计_R2_CANDIDATE.md:97) |
| POST /publications/{id}/deliveries；GET /deliveries/{id} | target、expectedPublicationDigest/context/reason/adapterRef 和 key；202仅激活固定运输；GET 原内容/locator/结果和可用性。[原文P:98](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15f966e5afe3dc71__SPACE-PUB-01_完整详细设计_R2_CANDIDATE.md:98) |
| POST /deliveries/{id}/resume | attemptId、expectedAttemptRevision、step、contextRef/reason/key；仅服务当前 QUERY_ORIGINAL 或 REDISPATCH_SAME_CONTENT，不能代 WMS 业务恢复。[原文P:100](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15f966e5afe3dc71__SPACE-PUB-01_完整详细设计_R2_CANDIDATE.md:100) [原文P:132](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15f966e5afe3dc71__SPACE-PUB-01_完整详细设计_R2_CANDIDATE.md:132) |
| POST /source-stop-previews；POST /source-stop-intents；GET 原 intent | 预览原 publication/T/非空 D/前驱；提交固定预览摘要/context/source-state/frontier/current预期/源决定/key；返回非空后继和原 outbox。[原文P:101](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15f966e5afe3dc71__SPACE-PUB-01_完整详细设计_R2_CANDIDATE.md:101) [原文P:222](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15f966e5afe3dc71__SPACE-PUB-01_完整详细设计_R2_CANDIDATE.md:222) |
| POST /publications/{id}/successor-drafts | sourceFrozenBinding/expectedFreezeRevision、source frontier、model RowVersion/draft-slot、name/reason/context/key；唯一 clone command/job/new Version，不解冻原版。[原文P:297](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15f966e5afe3dc71__SPACE-PUB-01_完整详细设计_R2_CANDIDATE.md:297) |
| GET /operations/lookup | 仅 operationKind/规范 targetKey/operationKey，首响应丢失无需结果 ID/旧 ETag/旧写权/在线源；按当前读权返回原定位与结果。[原文P:104](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15f966e5afe3dc71__SPACE-PUB-01_完整详细设计_R2_CANDIDATE.md:104) [原文P:126](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15f966e5afe3dc71__SPACE-PUB-01_完整详细设计_R2_CANDIDATE.md:126) |
| GET /source-correspondences | publication/T/cursor，受权来源 S/B/L、原版本、映射决定及覆盖，只读。[原文P:106](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15f966e5afe3dc71__SPACE-PUB-01_完整详细设计_R2_CANDIDATE.md:106) |

WMS `ReadPublication` 保外部 ref/version，与 Space 内部 Guid、WMS 本地 publicationId 分列；`ResolveTarget` 返回完整 CanonicalTarget/范围版/memberManifestDigest/members/dependencies/分区依据；`ReadMappingDecision` 由具名 Mapping Owner 提供正式 S/B/L；WMS lookup 与 Runtime ReadTarget 保各自 locator/revision/receipt，producer locator 不能混用。[原文P:195](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15f966e5afe3dc71__SPACE-PUB-01_完整详细设计_R2_CANDIDATE.md:195) [原文W:3097](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/df/df146652520facaf__WMS-MASTER-01_仓库库位与Space采纳_前后端开发Spec_v1.1.1_REVIEW.md:3097)

Problem 保标准 type/title/status/detail/instance，加 code/correlation/resource/retryable/affectedItems/current/expected/coverage；400格式，401/403鉴权，404安全隐藏，409意图/谱系冲突，412本地前驱过时，422业务不允许，503依赖无配置/不可用。错误不泄漏他租户名称。[原文P:112](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15f966e5afe3dc71__SPACE-PUB-01_完整详细设计_R2_CANDIDATE.md:112)

## 6. 幂等、并发、未知与恢复

请求唯一 `Tenant＋OperationKind＋TargetKey＋OperationKey` 绑定持久 canonicalizationVersion/requestDigest；摘要含原 context/expected/body，排除 key、显示 actor、correlation、观察时间。同键同内容回原；异内容 REQUEST_CONTENT_CONFLICT。不同 key 命中相同业务唯一槽按 intentDigest 比较：等价关联原结果，不等价 SOURCE_INTENT_CONFLICT。intentDigest 含 sourceDecision、全快照/targets/业务前驱，排除自由说明；说明可追加审计。[原文P:128](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15f966e5afe3dc71__SPACE-PUB-01_完整详细设计_R2_CANDIDATE.md:128)

| OperationKind | 规范 TargetKey / 业务唯一槽 |
|---|---|
| SOURCE_FREEZE | source-version:{versionId} / Tenant＋Site＋DesignVersion，终身一 FrozenBinding |
| SOURCE_DELIVER | source-delivery:{publicationId}:{targetUnitId} / Tenant＋publication＋Canonical T |
| SOURCE_STOP | source-stop:{originalPublicationId}:{targetUnitId} / 同原 publication/T/expected frontier，仅一个后继胜出 |
| SOURCE_DELIVERY_RESUME | source-resume:{deliveryId}:{step} / 原 delivery/attemptId/revision/step |
| SOURCE_SUCCESSOR_DRAFT | source-successor:{frozenBindingId} / 原 Binding＋当前 model draft-slot 前驱 |

所有 targetKey 的 Guid 用小写 D 格式，不用 code/筛选/别名拼身份。发送前持久化 locator 与完整原请求字节；context 只固定意图，不授效果批准。[原文P:116](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15f966e5afe3dc71__SPACE-PUB-01_完整详细设计_R2_CANDIDATE.md:116)

每个设计写入口在原权限、原 Draft/Ready资格、revision/lease/字段校验外，最终写事务必须同核 `frozenBindingId=null`、writeGateRevision；AI/匹配原本 Draft-only 不放宽。覆盖场景、编码、来源/底图、模板写入、匹配/确认、AI queue/commit/retry、反向 bind/place，legacy重叠域还须完整 writer fence。queued job 提交前重核；已 COMMITTED 原键回放不倒判失败；原写是否提交未知先阻封存 CONTENT_MUTATION_UNRESOLVED。[原文P:279](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15f966e5afe3dc71__SPACE-PUB-01_完整详细设计_R2_CANDIDATE.md:279) [原文P:281](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15f966e5afe3dc71__SPACE-PUB-01_完整详细设计_R2_CANDIDATE.md:281) [原文X:31](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:31)

旧 Frozen 未交付、确定拒绝或 UNKNOWN 均可隔离克隆；新版精确 binding/hash/frontier/draft-slot，不继承源批准、WMS启用或 Runtime 成功。已有 ActiveDraft 返回冲突不覆盖；UNKNOWN 未封闭仍阻后继交付/current晋升，不能自动改 expected 前驱套最新 token。[原文P:297](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15f966e5afe3dc71__SPACE-PUB-01_完整详细设计_R2_CANDIDATE.md:297) [原文P:301](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15f966e5afe3dc71__SPACE-PUB-01_完整详细设计_R2_CANDIDATE.md:301)

源封存成功响应丢失查 SOURCE_FREEZE；Delivery/resume 各自丢响应查自己的 locator；WMS APPLIED 以后只续其原 Owner 确认或 Runtime，不能再次 WMS应用。NOT_FOUND_OBSERVED/403/归档缺口不证明无效果、不释放保护、不换键重造。412 应重新选服务允许的原步骤，不能自动用最新 revision 重做旧意图。[原文P:246](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15f966e5afe3dc71__SPACE-PUB-01_完整详细设计_R2_CANDIDATE.md:246) [原文W:2543](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/df/df146652520facaf__WMS-MASTER-01_仓库库位与Space采纳_前后端开发Spec_v1.1.1_REVIEW.md:2543)

Runtime Desired 的 stream/epoch/generation 与 Actual 的 stream/epoch/sequence 独立，Actual 晚到低序只补历史；真实较新 P1 在 P2 后发生必须显示 Desired=P2/Actual=P1 drift，跨流换代无证 UNKNOWN。晋升只证明某原生观察点，不承诺以后远端不变；新 drift 不倒改历史 promotion，也不自动回退 current。[原文P:317](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15f966e5afe3dc71__SPACE-PUB-01_完整详细设计_R2_CANDIDATE.md:317) [原文P:323](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15f966e5afe3dc71__SPACE-PUB-01_完整详细设计_R2_CANDIDATE.md:323) [原文W:2551](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/df/df146652520facaf__WMS-MASTER-01_仓库库位与Space采纳_前后端开发Spec_v1.1.1_REVIEW.md:2551)

## 7. 权限、租户与审计

先可信 Tenant/Site/Actor与资源归属，再动作/字段权限。设计 edit 与源 publish、停止、WMS采纳、Runtime和恢复分权；现 `space:model:publish` 只是入口依据，新 producer能力仍需 Main IAM 精确采用。历史查询只需当前读权，旧发布者写权撤回不妨碍有权读原结果；新恢复效果必须当前执行权。外部安全 grant 不等于业务原子 Target。[原文P:43](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15f966e5afe3dc71__SPACE-PUB-01_完整详细设计_R2_CANDIDATE.md:43) [原文P:55](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15f966e5afe3dc71__SPACE-PUB-01_完整详细设计_R2_CANDIDATE.md:55) [原文X:27](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:27) [原文X:295](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:295)

保存原请求/context、决策版本、raw/native/canonical 摘要、源/业务前驱、全部目标、每次 attempt、确定拒绝/未知、原 Owner receipt、当前效力与本地投影版。审计者受当前字段范围，RESTRICTED/coverage 不得以空数组变“全无”；保留源 file/job/artifact/audit/published 引用，缺 SYS-FILE 保留政策不得清理原件。[原文P:267](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15f966e5afe3dc71__SPACE-PUB-01_完整详细设计_R2_CANDIDATE.md:267) [原文P:327](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15f966e5afe3dc71__SPACE-PUB-01_完整详细设计_R2_CANDIDATE.md:327) [原文P:366](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15f966e5afe3dc71__SPACE-PUB-01_完整详细设计_R2_CANDIDATE.md:366)

## 8. 页面、状态与用户恢复

现 PublishView/ManagementView/DesignWmsAdoptionPanel 保留；旧反向采纳面板不冒称 WMS正式采纳。预览顶栏固定 Site/版/base/T/manifest，四栏设计完整性、源批准、WMS接入、Runtime；差异按 logical ID 固定前后，Before/AfterHash、master/geometry/provenance/Wms变化、请求效果、正式 L/计划节点、coverage及阻塞。[原文P:149](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15f966e5afe3dc71__SPACE-PUB-01_完整详细设计_R2_CANDIDATE.md:149) [原文P:159](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15f966e5afe3dc71__SPACE-PUB-01_完整详细设计_R2_CANDIDATE.md:159) [原文P:334](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15f966e5afe3dc71__SPACE-PUB-01_完整详细设计_R2_CANDIDATE.md:334)

FULL 缺项是 MissingFromSnapshot/REVIEW_REQUIRED，不自动 DISABLE/DELETE；DELTA遗漏 KEEP，显式清空只按 Owner 字段定义；null/false/0/未知分开。筛选失败行不缩 Target；legacy部分成功逐项保留，不能全绿或全未发生。[原文P:161](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15f966e5afe3dc71__SPACE-PUB-01_完整详细设计_R2_CANDIDATE.md:161)

页面明确 LOADING、EMPTY（完整有权且无记录）、RESTRICTED、INCOMPLETE/UNKNOWN、CONFLICT、ACCEPTED_PENDING、PARTIAL_LEGACY、WMS_APPLIED_RUNTIME_PENDING。SignalR只触发精确 GET。按钮分别“查原结果”“续原交付”“查看 WMS 允许恢复”“新建后继草稿”；Back/取消只关展示，刷新先查原 locator，不自动新键、整对象 PUT 或套最新预期。[原文P:258](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15f966e5afe3dc71__SPACE-PUB-01_完整详细设计_R2_CANDIDATE.md:258) [原文P:338](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15f966e5afe3dc71__SPACE-PUB-01_完整详细设计_R2_CANDIDATE.md:338)

## 9. 开发顺序与固定代码差异

按原 S4-D01～D07：不可变源/逐位置修订/唯一封存/原键查询；全量差异与分页；明确交付和原结果/Finalizer；停止后继；分层 UI；全写门、旧读者/后继/迁移兼容；相应测试设计。只读/封存/UI可有界并行，正式交付、危险停止与 current晋升分别依赖 Owner 门，不能误排成全部可直接开门。[原文P:342](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15f966e5afe3dc71__SPACE-PUB-01_完整详细设计_R2_CANDIDATE.md:342)

源报告对应 Main@90c871fe 的实际差距：旧 ApplyBatch 为逐项 B 效果、旧 completed 为 saga，NextExternalVersion 有 legacy兜底；原 Draft/Ready 写检查、AI Draft检查与 matching资格缺共同Frozen门；旧 Site/Floor/Zone/Aisle 没通用 RowVersion/lease，Rack才有 RowVersion。Host 注册不证明配置/运行。本文未新增代码审计，引用的固定报告不能冒充现在已实现。[原文P:15](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15f966e5afe3dc71__SPACE-PUB-01_完整详细设计_R2_CANDIDATE.md:15) [原文P:140](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15f966e5afe3dc71__SPACE-PUB-01_完整详细设计_R2_CANDIDATE.md:140) [原文P:291](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15f966e5afe3dc71__SPACE-PUB-01_完整详细设计_R2_CANDIDATE.md:291) [原文P:293](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15f966e5afe3dc71__SPACE-PUB-01_完整详细设计_R2_CANDIDATE.md:293)

切换按 Tenant/Site/全部重叠依赖域盘点旧 writers及 pending/partial→有据映射/前驱/批准→围栏旧写并确认积压归属→证明新共同门/适配→原子记录 binding。仅一行 WriterEpoch、隐藏按钮或通知不能保证单写；新正式效果已发生不能直接回退旧 writer。[原文P:331](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15f966e5afe3dc71__SPACE-PUB-01_完整详细设计_R2_CANDIDATE.md:331) [原文W:2126](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/df/df146652520facaf__WMS-MASTER-01_仓库库位与Space采纳_前后端开发Spec_v1.1.1_REVIEW.md:2126)

### 原41 SPEC范围的本模块兼容边界

`HIT`表示本次发布/采纳合同需要调整；`NO_HIT_PRESERVE`保留原算法与Owner，但凡写入受控版本，仍须服从共同冻结门。这些分类不是实现完成或现场验收。矩阵保留的 `TO_AUTHOR` 为原范围快照，当前接受组合仍依本册第2节UA，不回退为无设计。

|原SPEC尾号/范围|范围结论|开发时保留或整合的边界|
|---|---|---|
|01 发布预览|HIT|预览/校验/分页/PlanHash可复用；需固定发布信封与目标完整性，预览无WMS写效果 [矩阵:422](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/2c/2cba0684fb7cbf7e__MATRIX-8TARGET-41SPEC.json:422)|
|02 差异|HIT|已有Master/Geometry/Provenance/WmsChanged；需FULL_SNAPSHOT缺项与DELTA遗漏分义、同版异内容拒绝 [矩阵:443](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/2c/2cba0684fb7cbf7e__MATRIX-8TARGET-41SPEC.json:443)|
|03 WMS采纳|HIT|旧WMS导入绑定与逐项WmsBin回执可保留；不是accepted第9章Canonical原子采纳 [矩阵:462](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/2c/2cba0684fb7cbf7e__MATRIX-8TARGET-41SPEC.json:462)|
|04 停用|HIT|旧停用已看库存并有墓碑；新效果还需完整任务/预留等Owner保护，不能删除被引用库位 [矩阵:484](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/2c/2cba0684fb7cbf7e__MATRIX-8TARGET-41SPEC.json:484)|
|05 重试|HIT|已有job/attempt/回查/重试/对账；需原发布原目标回查，不以新键重复采纳 [矩阵:505](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/2c/2cba0684fb7cbf7e__MATRIX-8TARGET-41SPEC.json:505)|
|06 来源对应|HIT|已有来源与绑定；补Publication/Target/S/B/L精确版本映射，保native/raw/canonical [矩阵:527](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/2c/2cba0684fb7cbf7e__MATRIX-8TARGET-41SPEC.json:527)|

原矩阵的“已有源码/可复用”只承接准确固定源身份及原有界审读，未完成完整97源码审计；新运行、真实Owner采用和S1精确consumer确认均未证明。[原独审状态:1455](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/ff/fff3a9611b515c00__CP6_S4_X6_R2_独立复核证据与41SPEC范围.json:1455) [来源资格:3435](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/fe/fed1fafddca4ed99__SOURCE-QUALIFICATIONS.json:3435)

## 10. 验收与未运行事实

原 PUB01～27 及 R2 子场景均 NOT_RUN：预览无 WMS写；源变更/筛选冲突/Scenario阻封存；FULL缺项/DELTA/同版异内容/legacy partial；不同key/actor同意图一 Canonical、映射差异冲突、批次非业务分片、复用 L不重置；停用原 T全范围保护/拒绝未知/合法恢复；封存、交付、stop和resume首响应丢失；同码异ID/原native摘要/引用保留。[原文P:155](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15f966e5afe3dc71__SPACE-PUB-01_完整详细设计_R2_CANDIDATE.md:155) [原文P:167](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15f966e5afe3dc71__SPACE-PUB-01_完整详细设计_R2_CANDIDATE.md:167) [原文P:193](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15f966e5afe3dc71__SPACE-PUB-01_完整详细设计_R2_CANDIDATE.md:193) [原文P:244](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15f966e5afe3dc71__SPACE-PUB-01_完整详细设计_R2_CANDIDATE.md:244) [原文P:258](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15f966e5afe3dc71__SPACE-PUB-01_完整详细设计_R2_CANDIDATE.md:258) [原文P:269](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15f966e5afe3dc71__SPACE-PUB-01_完整详细设计_R2_CANDIDATE.md:269)

20 条 R2 细化挂原87 AC，不增身份：严格正int64/最大值/旧adapter不能造真；同设计版不同 preview/key；原键查询与权限/归档缺口；所有写点永久冻结、queued与commit竞争、隔离克隆与来源判别；完整事实晋升/指针竞争/丢响应后真实drift/原current读者；非空停止后继、D不缩M、拒绝未知、恢复迟到UPSERT、子target不能晋升全模型。`executed_count=0`，runtime_evidence=null。[原文T:1](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/42/428444e8694aefa7__R2-AC-REFINEMENTS.json:1)

## 11. 缺口及退出条件

R-S4-01～08 分别要求：真实 SourceDecision/TargetDefinition/存量源修订与共同门、S1精确信封/摘要/Canonical消费、Stock/Reservation/Task全范围持久保护、IAM服务/字段权、Runtime原生流与全模型Observation/Finalizer、所有writers/旧pending/cutover、SYS-FILE保留/审计、X6准确消费。缺哪个门禁止哪个真实效果，合法历史读取/隔离预览可继续；这些 UNPROVEN 不表示规范还未选定。[原文P:356](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15f966e5afe3dc71__SPACE-PUB-01_完整详细设计_R2_CANDIDATE.md:356) [原文R:194](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/c4/c46bf5d8d9ed7e2c__CP6_S4_X6_R2_独立定点复核与累计范围.md:194)

WMS持久保护必须约束所有命中范围新增效果及在途申请最终提交；到期不自动撤屏障，pendingApplications非空不能WMS停止APPLIED；不支持全范围/先取消墓碑应 UNSUPPORTED_GUARANTEE，不能用TTL缓存冒保证。跨库B/L没有不放宽H35的准确Adapter合同不生产应用。[原文W:3110](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/df/df146652520facaf__WMS-MASTER-01_仓库库位与Space采纳_前后端开发Spec_v1.1.1_REVIEW.md:3110) [原文W:3122](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/df/df146652520facaf__WMS-MASTER-01_仓库库位与Space采纳_前后端开发Spec_v1.1.1_REVIEW.md:3122) [原文W:1963](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/df/df146652520facaf__WMS-MASTER-01_仓库库位与Space采纳_前后端开发Spec_v1.1.1_REVIEW.md:1963)

必要规范正文、41SPEC矩阵、独审判语和接受/来源身份已整理；97固定源码仅身份核对与原有界审读承接，没有新做全量源码/历史算法审计。未知配置不造默认、模拟夹具不当真实采用；原生 Excel 未生成也不推翻当前 Markdown接受。[原文A:556](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/97/9782d6d7ebdf7b49__UA-20261008-S4-SPACE-PUB-01-R2-MD02.json:556)

## 12. 实际阅读

S4 R2全369行、X6 R2全394行、独审报告全208行、R2-AC-REFINEMENTS JSON全423行已实读。8份UA按明确字段结构化核Disposition、AcceptanceDecision、AcceptedBody、组合两主文哈希、OwnerAndExecutionBoundary；没有把源证据及全部历史字段算全文。WMS v1.1.1本轮实读1880～1966、2080～2127、2541～2580、3048～3053、3080～3123，其余不得冒称本轮全文复审。

见 [本组阅读台账](D:/CP6/docs/CP6_开发设计文档_20261010/evidence/platform-engineering-reading.json) 与 [共享合同](D:/CP6/docs/CP6_开发设计文档_20261010/contracts/platform-engineering-contracts.json)。没有执行源码脚本、构建、业务测试、迁移或Actions。

本輪補讀 MATRIX 8 Target/41 SPEC 全部字段（结构化）、独审41项判语/4项设计根与资格边界（选段），SOURCE-QUALIFICATIONS 3110–3440的固定源/阅读限制；不会将其记录的源码审读窗口记作本代理实读。

**当前必要材料归并状态。** 本册为 `required_materials_consolidated`：选定正文、规范附件及准确接受/复核范围已按直接实读、结构化语义或精确复用归并。身份清单核对不等底层源码全文阅读；历史草稿、候选实现和Office/HTML视觉未核范围仍单列。可复查[逐件阅读与处置](D:/CP6/docs/CP6_开发设计文档_20261010/evidence/platform-engineering-reading.json)及[本组闭包台账](D:/CP6/docs/CP6_开发设计文档_20261010/worklogs/platform-engineering-progress.json)。当前必需规范未读清单为空；具体Owner采用、参数/物理接口、真实运行和本节已有来源缺口不因整理闭包而改变。
