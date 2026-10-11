# PLT-RELEASE-01 · 发布身份、信任与逐应用采用

整理状态：`core_semantics_consolidated`。静态设计接受不升级为实现或运行接受。

## 1. 业务目的、操作者和Owner边界

Platform维护发布合同、包身份及非生产资格化支撑，部署/业务Owner拥有具体环境执行与采用。此Target为TO_ASSESS共享支撑，不以静态设计授权发布、部署、改配置/权限或占用新包版本。[原文R:20](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/39/397b935b08bca80e__S7_Platform_v0.2.1_七稿组合完整静态接受_独立复审结论.txt:20) [原文B:39](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/92/9246ef6b40f500da__PLT-RELEASE-01_完整静态设计_v0.1.md:39) [原文B:103](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/92/9246ef6b40f500da__PLT-RELEASE-01_完整静态设计_v0.1.md:103)
实际采用按SourceFrozen→ContractShapeValid→ArtifactBytesVerified→TrustCurrentlyAccepted→ConsumerQualified→HostRegistered→ConfigEnabled→RuntimeObserved→BusinessApplied→IndependentlyAccepted分层，不能折成一个绿色版本号。[原文B:19](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/92/9246ef6b40f500da__PLT-RELEASE-01_完整静态设计_v0.1.md:19) [原文B:21](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/92/9246ef6b40f500da__PLT-RELEASE-01_完整静态设计_v0.1.md:21)

## 2. 选定规范与接受范围

当前正文v0.1、SHA256 `9246ef6b40f500da22c570bf3c31a51701442bbbed50250d9f259d12cb1bbdbf`、121行。精确采用S7 v0.2.1混合七稿：CTX/OBS/RELEASE/TEST v0.1，HTTP/MSG v0.2，DEL v0.2.1；本Target 32项AC。接受为ACCEPTED_CURRENT_MARKDOWN_STATIC_DETAILED_DESIGN。[原文A:165](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/be/beae7ea24d922f82__UA-20261008-S7-PLT-RELEASE-01-STATIC-MD02.json:165) [原文R:9](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/39/397b935b08bca80e__S7_Platform_v0.2.1_七稿组合完整静态接受_独立复审结论.txt:9)
原作者freeze索引中的DEL pending/null保留历史，最终独审关闭B01/B02/B03及B03-R1，非当前缺稿。正式delegate接受与用户亲签分开，原决定逐字/messageId未恢复，后续04:09:04Z恢复确认另记；258组合AC全NOT_RUN，运行采用UNPROVEN，未授权实施/部署。[原文R:7](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/39/397b935b08bca80e__S7_Platform_v0.2.1_七稿组合完整静态接受_独立复审结论.txt:7) [原文R:132](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/39/397b935b08bca80e__S7_Platform_v0.2.1_七稿组合完整静态接受_独立复审结论.txt:132) [原文A:8](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/be/beae7ea24d922f82__UA-20261008-S7-PLT-RELEASE-01-STATIC-MD02.json:8) [原文A:330](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/be/beae7ea24d922f82__UA-20261008-S7-PLT-RELEASE-01-STATIC-MD02.json:330)

## 3. 对象、字段与不可替代的身份

|合同/对象|精确身份与作用|
|---|---|
|system-release-manifest.v1|System/deployable=true，精确CP6/CRM/Platform/Portal四仓真实身份；缺Portal不能造占位，deployable只是形状不等授权。[原文B:27](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/92/9246ef6b40f500da__PLT-RELEASE-01_完整静态设计_v0.1.md:27)|
|platform-release-candidate.v1|PlatformReference/deployable=false；root含platformSource/packages/buildProvenance/images/evidence/crmConsumer/publisher/verifier/releaseGateResult/policyVersions，不改label冒System。[原文B:27](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/92/9246ef6b40f500da__PLT-RELEASE-01_完整静态设计_v0.1.md:27) [原文B:29](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/92/9246ef6b40f500da__PLT-RELEASE-01_完整静态设计_v0.1.md:29)|
|candidate-result.v2 / candidate-locator.v1|Result绑定System manifest与gate；Locator subjectKind只SystemCandidateResult或PlatformReleaseCandidate，绑定subject/policy/signer/createdAt。各lane字段精确且拒unknown。[原文B:27](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/92/9246ef6b40f500da__PLT-RELEASE-01_完整静态设计_v0.1.md:27) [原文B:29](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/92/9246ef6b40f500da__PLT-RELEASE-01_完整静态设计_v0.1.md:29)|
|package|packageId/version/sourceGitSha、authorSignedPackageSha256、publishedPackageSha256、feedIdentity/transformation、signerFingerprint/timestampPolicy；七包Abstractions/AspNetCore/Contracts/Deployment/EntityFramework/Messaging/Release，统一CP6.Platform前缀和version/source，Testing除外。[原文B:31](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/92/9246ef6b40f500da__PLT-RELEASE-01_完整静态设计_v0.1.md:31)|
|DependencyCompatibilityRow|consumer/producer精确包/feed/runtime/SDK/OS/arch、schema/validator、精确消息版本、DB provider/driver/migration、API/Host/config、decision/evidence/blocked原因，不能只比major。[原文B:75](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/92/9246ef6b40f500da__PLT-RELEASE-01_完整静态设计_v0.1.md:75)|

## 4. 状态流程、入口与本地写集

发布流水线先取原bytes→形状→真实签名和feed读回→consumer Owner采用；声明字节相等/Windows或Linux Success只是记录值，不是实际API/包检验。[原文B:31](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/92/9246ef6b40f500da__PLT-RELEASE-01_完整静态设计_v0.1.md:31) [原文B:33](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/92/9246ef6b40f500da__PLT-RELEASE-01_完整静态设计_v0.1.md:33) [原文B:39](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/92/9246ef6b40f500da__PLT-RELEASE-01_完整静态设计_v0.1.md:39)
DeploymentBindingV1候选独立记录目标环境/config/secret refs/image/schema/package/mapping/routes/migrations/fence/旧routeEpoch/批准状态，状态Draft→StaticReviewed→ApprovedForQualification→Qualified→ApprovedSpecifiedUse→Applied→Observed→Retired，不能把P09字符串改Production复用。[原文B:59](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/92/9246ef6b40f500da__PLT-RELEASE-01_完整静态设计_v0.1.md:59) [原文B:61](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/92/9246ef6b40f500da__PLT-RELEASE-01_完整静态设计_v0.1.md:61)
实施包中缺的新S7能力先ImplementationAbsent：新CTX resolver绑定、HTTP远端幂等和预算、MSG exact selection、DEL新seam/lease/hold、OBS真实identity；旧0.10.2不自动获得这些能力。[原文B:77](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/92/9246ef6b40f500da__PLT-RELEASE-01_完整静态设计_v0.1.md:77)

## 5. 接口、消息与依赖

当前formal publication语义validator仅接受0.10.0/0.10.1/0.10.2，绑定一个buildInvocation source/run/attempt、固定workflow/env/trust和七个不同包hash。新代码要独立版本演进，不复用已占版本或仅改validator allowlist。[原文B:33](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/92/9246ef6b40f500da__PLT-RELEASE-01_完整静态设计_v0.1.md:33)
NuGet政策PinnedSelfSigned、publicCaTrusted=false、internallyTrusted=true、Rfc3161Required、恰一Current signer；signer含DER/path/SHA/SPKI、subject/issuer/有效期/状态/激活/撤销。Current需签名时有效和当前仍Current/有效，HistoricalAudit只保历史事实；Formal validator没有HistoricalAudit开关。[原文B:35](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/92/9246ef6b40f500da__PLT-RELEASE-01_完整静态设计_v0.1.md:35)
Locator/OCI另为purpose-bound P-256 SPKI pin，信任政策由已批准pin输入，不能由待验Locator自带policy自证，不能把NuGet证书指纹直接当locator key。[原文B:37](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/92/9246ef6b40f500da__PLT-RELEASE-01_完整静态设计_v0.1.md:37)

## 6. 事务、幂等、并发及恢复

packageId/version/feed→不可变bytes；保签名前后hash与原签名字节，不重序列化JSON后称同签名。跨仓、feed、部署不是一个原子事务。[原文B:23](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/92/9246ef6b40f500da__PLT-RELEASE-01_完整静态设计_v0.1.md:23) [原文B:31](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/92/9246ef6b40f500da__PLT-RELEASE-01_完整静态设计_v0.1.md:31) [原文B:73](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/92/9246ef6b40f500da__PLT-RELEASE-01_完整静态设计_v0.1.md:73)
发布响应未知先查同id/version，不能重发另一bytes；修复用新version；撤销不删除历史，Historical不能晋Current。切换单writer+routeEpoch，保新已接受状态、receipt、audit和兼容reader，回退不删Migration/Inbox/Outbox或resetDB。[原文B:41](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/92/9246ef6b40f500da__PLT-RELEASE-01_完整静态设计_v0.1.md:41) [原文B:63](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/92/9246ef6b40f500da__PLT-RELEASE-01_完整静态设计_v0.1.md:63) [原文B:107](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/92/9246ef6b40f500da__PLT-RELEASE-01_完整静态设计_v0.1.md:107)
qualification同ID同hash同原结果，异义Conflict，新attempt保旧失败；cleanup不明不能Pass。旧lease/in-flight未知不等无效果，恢复用S6 typed action+原结果。[原文B:105](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/92/9246ef6b40f500da__PLT-RELEASE-01_完整静态设计_v0.1.md:105) [原文B:109](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/92/9246ef6b40f500da__PLT-RELEASE-01_完整静态设计_v0.1.md:109)

## 7. 权限、租户和敏感信息

部署身份、secretRef、批准政策和工具链均精确登记，文书不存secret值。现P09是cp6-platform-p09-ci-v1非生产profile；固定Dapr1.18.2/Kafka4.3.1/kubectl1.34.1 tag不是实际观察digest。topic probe是合成例，ACL与appId/broker限制不能冒业务环境采用。[原文B:51](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/92/9246ef6b40f500da__PLT-RELEASE-01_完整静态设计_v0.1.md:51) [原文B:53](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/92/9246ef6b40f500da__PLT-RELEASE-01_完整静态设计_v0.1.md:53) [原文B:55](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/92/9246ef6b40f500da__PLT-RELEASE-01_完整静态设计_v0.1.md:55)
consumer资格化要求实际feed package且无ProjectReference/source fallback；同EF package并不证明同physical transaction。各Owner权限/跨库最终门独立，不由签名或health绿灯授权新写。[原文B:79](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/92/9246ef6b40f500da__PLT-RELEASE-01_完整静态设计_v0.1.md:79) [原文B:81](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/92/9246ef6b40f500da__PLT-RELEASE-01_完整静态设计_v0.1.md:81) [原文B:103](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/92/9246ef6b40f500da__PLT-RELEASE-01_完整静态设计_v0.1.md:103)

## 8. 页面与服务器校验

只读显示精确artifact/source/package/config、签名Current或Historical解释、各Host采用和缺失能力；不能给重新签名/任意发布/绕批准部署入口。系统候选与Platform reference要明确区分，不能用deployable=true文字让用户误认为已获环境批准。[原文B:27](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/92/9246ef6b40f500da__PLT-RELEASE-01_完整静态设计_v0.1.md:27) [原文B:35](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/92/9246ef6b40f500da__PLT-RELEASE-01_完整静态设计_v0.1.md:35) [原文B:43](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/92/9246ef6b40f500da__PLT-RELEASE-01_完整静态设计_v0.1.md:43) [原文B:103](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/92/9246ef6b40f500da__PLT-RELEASE-01_完整静态设计_v0.1.md:103)
显示原证据版本、run/attempt、实际哈希及NotRun/Failed/cleanup情况，不把历史P09 Passed复制为本轮S7 AC。[原文B:95](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/92/9246ef6b40f500da__PLT-RELEASE-01_完整静态设计_v0.1.md:95) [原文B:97](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/92/9246ef6b40f500da__PLT-RELEASE-01_完整静态设计_v0.1.md:97) [原文B:109](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/92/9246ef6b40f500da__PLT-RELEASE-01_完整静态设计_v0.1.md:109)

## 9. 实施顺序与固定源码差距

冻结正式包的实际version+published hash+source和消费者组合→核能力API/Host/migration/adoption矩阵→计划真实feed消费资格化→各Owner另授权单writer切流和实际环境观察。固定源说明P10正式七包0.10.2；较旧开发默认或历史0.10.0/1不据此判冲突。[原文B:11](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/92/9246ef6b40f500da__PLT-RELEASE-01_完整静态设计_v0.1.md:11) [原文B:75](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/92/9246ef6b40f500da__PLT-RELEASE-01_完整静态设计_v0.1.md:75) [原文B:77](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/92/9246ef6b40f500da__PLT-RELEASE-01_完整静态设计_v0.1.md:77)
S6设计与P10的S06阶段是不同命名范围；P09历史Frozen也不是生产接受。Main原ActiveTenant早于历史operation读的问题是应用adapter缺口，不能由包升级自动消除。[原文B:11](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/92/9246ef6b40f500da__PLT-RELEASE-01_完整静态设计_v0.1.md:11) [原文B:81](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/92/9246ef6b40f500da__PLT-RELEASE-01_完整静态设计_v0.1.md:81)

## 10. 验收设计与未运行状态

4 SPEC/32AC全部NOT_RUN。P09原12项检查包括profile、首次和幂等provision、invoke/pubsub正例、direct Kafka/principal/appId/foreign-topic拒绝、Kube render/policy与zero residue；只保未来范围，无执行。[原文B:57](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/92/9246ef6b40f500da__PLT-RELEASE-01_完整静态设计_v0.1.md:57) [原文B:121](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/92/9246ef6b40f500da__PLT-RELEASE-01_完整静态设计_v0.1.md:121)
离线render不证明集群，synthetic probe排除真实业务DB/云采用；本地前置不可用NotRun，CI合同不可用Failed，cleanup失败覆盖成功。source结构校验、真feed consumer、实际Host、业务receipt和生产资格分别验证。[原文B:97](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/92/9246ef6b40f500da__PLT-RELEASE-01_完整静态设计_v0.1.md:97) [原文B:99](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/92/9246ef6b40f500da__PLT-RELEASE-01_完整静态设计_v0.1.md:99) [原文B:101](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/92/9246ef6b40f500da__PLT-RELEASE-01_完整静态设计_v0.1.md:101)

## 11. 缺口、依赖与采用门

Required-REL-01..07依次需完整不可变包与Current信任、source/packaged schema及真包consumer、新原语精确package/API和Host、DB/driver/migration/全writer/fence事务、目标用途/环境授权、单writer/存量/回退、真实AC与独立接受。新能力未实施或未完成迁移者保持关闭；静态材料可完整而运行仍UNPROVEN。[原文B:119](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/92/9246ef6b40f500da__PLT-RELEASE-01_完整静态设计_v0.1.md:119) [原文B:77](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/92/9246ef6b40f500da__PLT-RELEASE-01_完整静态设计_v0.1.md:77) [原文R:159](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/39/397b935b08bca80e__S7_Platform_v0.2.1_七稿组合完整静态接受_独立复审结论.txt:159)
没有真实四仓身份/Portal/包内容或Current trust就不造System候选；没有实际provider共同事务不以同包保证业务原子。[原文B:27](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/92/9246ef6b40f500da__PLT-RELEASE-01_完整静态设计_v0.1.md:27) [原文B:35](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/92/9246ef6b40f500da__PLT-RELEASE-01_完整静态设计_v0.1.md:35) [原文B:81](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/92/9246ef6b40f500da__PLT-RELEASE-01_完整静态设计_v0.1.md:81)

## 12. 阅读覆盖与证据

本轮新全文语义阅读正文L1–121；S7最终独审L1–161、组合索引L1–248全文。接受JSON读取Target/Disposition/AcceptanceDecision/AcceptedBody/AcceptedCurrentCombination/ReviewQualifications/OwnerAndExecutionBoundary完整七字段，其余来源历史字段待L1/L2集中核对，未声称全文。[原文B:1](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/92/9246ef6b40f500da__PLT-RELEASE-01_完整静态设计_v0.1.md:1) [原文R:1](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/39/397b935b08bca80e__S7_Platform_v0.2.1_七稿组合完整静态接受_独立复审结论.txt:1) [原文C:1](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/3e/3ec93373fda65d46__S7_PLATFORM_COMBINED_FREEZE_v0.2.1.json:1)
[共享合同](D:/CP6/docs/CP6_开发设计文档_20261010/contracts/platform-engineering-contracts.json)保存关键精确协议/字段原段；[阅读记录](D:/CP6/docs/CP6_开发设计文档_20261010/evidence/platform-engineering-reading.json)逐源记录新读、继承和未读行段。本文未把候选seam、表或保证写成现包已实现。

**当前必要材料归并状态。** 本册为 `required_materials_consolidated`：选定正文、规范附件及准确接受/复核范围已按直接实读、结构化语义或精确复用归并。身份清单核对不等底层源码全文阅读；历史草稿、候选实现和Office/HTML视觉未核范围仍单列。可复查[逐件阅读与处置](D:/CP6/docs/CP6_开发设计文档_20261010/evidence/platform-engineering-reading.json)及[本组闭包台账](D:/CP6/docs/CP6_开发设计文档_20261010/worklogs/platform-engineering-progress.json)。当前必需规范未读清单为空；具体Owner采用、参数/物理接口、真实运行和本节已有来源缺口不因整理闭包而改变。
