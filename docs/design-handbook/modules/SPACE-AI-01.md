# SPACE-AI-01 · 空间 AI 策略、人工决定、草稿应用与恢复

整理状态：`core_semantics_consolidated`；当前业务规则有据整理，实际Owner采用和运行验收保持UNPROVEN/NOT_RUN。

## 1. 目的、操作者和业务 Owner

AI策略管理员管理授权provider/数据范围/预算；有权reviewer人工核提案，设计编辑者将完整有据决定应用固定Draft。Space提案只是设计候选，不是WMS/PLM/库存或源发布批准；不新增provider、模型预算、密钥或网络权限。外部模型真实执行另需证据。[原文X:303](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:303) [原文X:309](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:309)

## 2. 精确规范与接受组合

本Target独立UA接受S4 R2 `15f966e5afe3dc71…`＋X6 R2 `6f8585ca3b85c6aa…`准确完整SHA组合（全值见本组冻结合同）；Disposition=ACCEPTED_CURRENT_MARKDOWN_STATIC_DETAILED_DESIGN。正文CANDIDATE/待审为历史，接受后仍保Runtime NOT_RUN、Owner采用UNPROVEN。范围：5 SPEC，3 HIT＋2有界保留；10 AC。NO_HIT仅当前公共变化未直接触及原算法，不是无设计或永久免回归。[原文A:6](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/07/075a86bdb9055eaa__UA-20261008-X6-SPACE-AI-01-R2-MD02.json:6) [原文X:7](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:7) [原文X:13](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:13) [原文R:194](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/c4/c46bf5d8d9ed7e2c__CP6_S4_X6_R2_独立定点复核与累计范围.md:194)

共同source/current/Runtime身份、冻结写门和合法后继统一使用 [SPACE-PUB-01](D:/CP6/docs/CP6_开发设计文档_20261010/modules/SPACE-PUB-01.md)，不能各自建立权威WMS结果或另一个gate。[原文X:15](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:15) [原文X:29](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:29)

## 3. 数据身份、字段与量纲

Policy保Version/DataPolicy/AllowedSiteIds/AllowedProviderAliases/MaxConcurrentRuns/ExternalProviderEnabled、日月预算Minor/Currency/ApprovedProviders；更新ExpectedVersion。Usage保RunId/provider/model/input/output/estimated/actual cost/latency/outcome，unknown预算不等0，HasUnpricedUsage不能计实际0成本。[原文X:307](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:307)

Proposal保SourceHash/Key、SuggestedGeometry/Attributes/Relations、SourceRefs/Evidence/FieldProvenance、confidence/status/blocking/HumanPatch/LockedFields、AppliedLogicalId/RowVersion/AllowedPatchPaths。review保summary/ReviewEtag/分页filter hash/问题与决定历史；DecisionBatch保before/after、actor/time。Apply保RunId/JobId/Status/AppliedContentRevision/Counts/ApplyCommitState/failure/RecoveryAction。Retry保BasedOnRun/ReplacementRunId/job/mode/recovery/Retryable/CancellationPending及旧source/policy/ReviewEtag。[原文X:313](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:313) [原文X:319](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:319) [原文X:327](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:327) [原文X:335](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:335)

## 4. 流程、状态和入口写集

先明确已批准DataPolicy/provider范围/预算/真实配置，才能外部调用；ClosedQuotaLeaseManager类注册不证明provider已用。审核逐项查来源/锁字段，confidence高不自动批准，未知证据保未知。[原文X:307](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:307) [原文X:313](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:313)

单项或批量决定只针对该run/source/BaseContentRevision/ReviewEtag，接受/修正/拒绝留before/after；不作为sourceDecisionRef/WMSapprovalRef。批量选择由服务固定当前集合，不拿分页总数伪造全量review。[原文X:319](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:319) [原文X:321](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:321)

AtomicApply必须原Draft、run.AwaitingReview、review complete、无blocking、内容同基版及准确content/run前驱，在最终事务同核共同Frozen门，固定job/apply plan和设计写集；生成LocationLogicalId只是S，AppliedCounts不代表正式L。应用结果仅草稿，仍待验证/源批准/发布。[原文X:327](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:327) [原文X:329](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:329)

已Published/Frozen/内容已变的旧run只读，后续应用用合法新draft/new run/new review；RuleOnly降级保DegradedReason，不冒外部模型曾执行成功，旧run不能改标签为另一模型来源。[原文X:323](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:323) [原文X:335](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:335) [原文X:337](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:337)

## 5. 接口和依赖合同

决定POST generation-runs/{runId}/decisions和decisions:batch：单项proposalId/decision/expectedProposalRowVersion/patch/locked/reason/comment；批量明确ids或selection、decision/ReviewEtag。AtomicApply请求ExpectedContentRevision/ExpectedRunRowVersion/ReviewEtag。恢复请求BasedOnRunId/ExpectedContentRevision/ExpectedBasedOnRunRowVersion、Mode=SamePolicy或RuleOnly。原接口路由以source为准，本册不猜未列route。[原文X:319](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:319) [原文X:327](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:327) [原文X:335](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:335)

依赖DES准确Draft/lease/版本、S4同一冻结门与专用Frozen successor；AI策略/provider资格独立。source publish、WMS mapping/creation与Runtime由各Owner，不能把AI接受/图元ID串成其许可。[原文X:329](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:329) [原文X:337](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:337)

## 6. 事务、并发、幂等和恢复

queue、实际commit及新retry都要当前写门；仅排队Draft检查不够。编辑与freeze串行化，冻结成功后即使旧Status仍Ready/Draft也拒写；AI原本Draft-only不放宽。未知在途commit先阻源封存，不伪FAILED_NO_EFFECT。[原文X:31](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:31) [原文P:279](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15f966e5afe3dc71__SPACE-PUB-01_完整详细设计_R2_CANDIDATE.md:279) [原文P:289](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15f966e5afe3dc71__SPACE-PUB-01_完整详细设计_R2_CANDIDATE.md:289)

超时按原run/job查ApplyCommitState：COMMITTED只回原设计结果不重应用；UNKNOWN不释放/重置，不通过new run隐藏；FAILED_NO_EFFECT仅在有据原状态下走合法后继。CancellationPending非已取消，重复点击不能排两个应用job；412/ReviewEtag变更须新合法review，不能自动套最新版重用旧接受。[原文X:331](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:331) [原文X:337](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:337)

## 7. 权限、租户和审计

提案查看/审核需space:model:review-ai，决定再需model:edit；政策管理与普通reviewer分权，当前Tenant/Site和DataPolicy限制贯穿文件/模型调用。LockedFields/AllowedPatchPaths服务端强校验，高confidence无特权。审计保原provider/model/source/policy、决定before/after、actor/time、原job和commit、后继lineage/成本是否未定价。[原文X:307](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:307) [原文X:313](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:313) [原文X:319](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:319)

## 8. 页面与服务端行为

保原提案人工review、置信度、来源/字段provenance、问题和分页；过时cursor重读当前review，不将一页当全体；决定/apply/retry区域明确固定基版和“已应用设计草稿”。Frozen旧run可查历史与COMMITTED，不能给编辑/apply按钮；unknown commit显示查原结果/当前recovery，RuleOnly显示降级原因，CancellationPending保待定。[原文X:315](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:315) [原文X:323](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:323) [原文X:331](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:331) [原文X:337](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:337)

## 9. 开发顺序及固定代码差异

X6-AI-D01补决定/apply/retry固定基版和同一永久冻结门、原COMMITTED回放、新draft/run/review；策略/用量、提案算法/patch policy有界保留，不新增provider或重新设计AI系统。固定Main@90c871fe已有Draft/事务检查仍须补最终Frozen门，未以源码或注册冒运行。[原文X:373](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:373) [原文X:309](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:309) [原文P:293](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15f966e5afe3dc71__SPACE-PUB-01_完整详细设计_R2_CANDIDATE.md:293)

### 原41 SPEC范围的本模块兼容边界

`HIT`表示本次发布/采纳合同需要调整；`NO_HIT_PRESERVE`保留原算法与Owner，但凡写入受控版本，仍须服从共同冻结门。这些分类不是实现完成或现场验收。矩阵保留的 `TO_AUTHOR` 为原范围快照，当前接受组合仍依本册第2节UA，不回退为无设计。

|原SPEC尾号/范围|范围结论|开发时保留或整合的边界|
|---|---|---|
|01 策略与用量|NO_HIT_PRESERVE|策略/用量与closed quota等现状保留；本轮不新增provider或外部调用 [矩阵:967](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/2c/2cba0684fb7cbf7e__MATRIX-8TARGET-41SPEC.json:967)|
|02 提案审核|NO_HIT_PRESERVE|提案置信度/LockedFields/AllowedPatchPaths人工工作台保留；发布契约未改建议生成语义 [矩阵:985](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/2c/2cba0684fb7cbf7e__MATRIX-8TARGET-41SPEC.json:985)|
|03 决定|HIT|接受/拒绝/修改仅作用提案，ReviewEtag绑定基版；不能授发布或采纳权 [矩阵:1004](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/2c/2cba0684fb7cbf7e__MATRIX-8TARGET-41SPEC.json:1004)|
|04 应用|HIT|AtomicApply只作用Draft，ExpectedContentRevision/RunRowVersion/ReviewEtag校验复用；结果不能标为WMS已采纳 [矩阵:1022](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/2c/2cba0684fb7cbf7e__MATRIX-8TARGET-41SPEC.json:1022)|
|05 重试|HIT|SamePolicy/RuleOnly恢复保留原run lineage；发布后过时输入拒绝，禁止借retry更改已冻结发布 [矩阵:1042](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/2c/2cba0684fb7cbf7e__MATRIX-8TARGET-41SPEC.json:1042)|

原矩阵的“已有源码/可复用”只承接准确固定源身份及原有界审读，未完成完整97源码审计；新运行、真实Owner采用和S1精确consumer确认均未证明。[原独审状态:1455](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/ff/fff3a9611b515c00__CP6_S4_X6_R2_独立复核证据与41SPEC范围.json:1455) [来源资格:3435](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/fe/fed1fafddca4ed99__SOURCE-QUALIFICATIONS.json:3435)

## 10. 验收场景与证据状态

XA01～10 NOT_RUN：缺预算/provider不报外调成功；confidence/筛选不自动接受全体且锁字段受控；AI接受无publish/adopt权；来源/Review变更阻旧决定；Apply只是草稿；freeze/commit竞态不双写；commit丢响应不重复图元；旧run重试不改Frozen；RuleOnly不混真实模型；UNKNOWN不新run掩盖。R2补队列与commit竞争、新来源判别、ActiveDraft冲突和UNKNOWN隔离后继。[原文X:309](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:309) [原文X:315](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:315) [原文X:323](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:323) [原文X:331](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:331) [原文X:339](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:339)

## 11. 缺口与退出条件

真实provider版本/许可/数据策略/预算/用量计费、closed quota启用、原commit状态与全部写点冻结采用、实际IAM当前权仍UNPROVEN。未定价保HasUnpricedUsage，不编0成本；其他强制源码/专项附件实际阅读按台账补齐，设计接受不自动授外部调用或生产写。[原文X:307](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:307) [原文X:380](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:380) [原文X:392](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:392)

## 12. 实际阅读

本轮全文读X6 R2 1～394（本Target专段301～339；公共15～52/341～394）、S4 R2 1～369、独审1～208、R2细化AC JSON 1～423；本Target UA按接受决定、主文/组合身份及执行边界结构化阅读。固定源码结论来自已接受规范，本册未新做97源码全文认证。强制附件/历史细节真实阅读与待补段见 [本组阅读台账](D:/CP6/docs/CP6_开发设计文档_20261010/evidence/platform-engineering-reading.json)。当前required_materials_consolidated，保留历史/源码/视觉边界；未执行业务测试/构建/迁移/Actions。

本輪補讀 MATRIX 8 Target/41 SPEC 全部字段（结构化）、独审41项判语/4项设计根与资格边界（选段），SOURCE-QUALIFICATIONS 3110–3440的固定源/阅读限制；不会将其记录的源码审读窗口记作本代理实读。

**当前必要材料归并状态。** 本册为 `required_materials_consolidated`：选定正文、规范附件及准确接受/复核范围已按直接实读、结构化语义或精确复用归并。身份清单核对不等底层源码全文阅读；历史草稿、候选实现和Office/HTML视觉未核范围仍单列。可复查[逐件阅读与处置](D:/CP6/docs/CP6_开发设计文档_20261010/evidence/platform-engineering-reading.json)及[本组闭包台账](D:/CP6/docs/CP6_开发设计文档_20261010/worklogs/platform-engineering-progress.json)。当前必需规范未读清单为空；具体Owner采用、参数/物理接口、真实运行和本节已有来源缺口不因整理闭包而改变。
