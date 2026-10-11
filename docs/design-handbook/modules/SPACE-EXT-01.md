# SPACE-EXT-01 · 外部组织授权与只读空间接口

整理状态：`core_semantics_consolidated`；当前业务规则有据整理，实际Owner采用和运行验收保持UNPROVEN/NOT_RUN。

## 1. 目的、操作者和业务 Owner

Main中的外部组织管理员维护组织/成员/grant；外部主体只读被授权场景、库存和任务。Main Space拥有此API边界，SYS-IAM提供身份授权；不宣称独立CP6.Portal、官网或自动建站。外部主体不拥有design edit/source publish/approval/task执行权。[原文X:271](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:271) [原文X:291](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:291)

## 2. 精确规范与接受组合

本Target独立UA接受S4 R2 `15f966e5afe3dc71…`＋X6 R2 `6f8585ca3b85c6aa…`准确完整SHA组合（全值见本组冻结合同）；Disposition=ACCEPTED_CURRENT_MARKDOWN_STATIC_DETAILED_DESIGN。正文CANDIDATE/待审为历史，接受后仍保Runtime NOT_RUN、Owner采用UNPROVEN。范围：4 SPEC，2 HIT＋2有界保留；6 AC。NO_HIT仅当前公共变化未直接触及原算法，不是无设计或永久免回归。[原文A:6](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/21/21839cb639f6b420__UA-20261008-X6-SPACE-EXT-01-R2-MD02.json:6) [原文X:7](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:7) [原文X:13](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:13) [原文R:194](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/c4/c46bf5d8d9ed7e2c__CP6_S4_X6_R2_独立定点复核与累计范围.md:194)

共同source/current/Runtime身份、冻结写门和合法后继统一使用 [SPACE-PUB-01](D:/CP6/docs/CP6_开发设计文档_20261010/modules/SPACE-PUB-01.md)，不能各自建立权威WMS结果或另一个gate。[原文X:15](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:15) [原文X:29](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:29)

## 3. 数据身份、字段与量纲

Organization保Type/Code/Name/BusinessPartnerType/Id/Status/SecurityStamp；合作方关联是准确身份引用，不按显示名猜。Membership保UserId/Role/ValidFrom-To/Status/InvitedBy/AcceptedAt/SecurityStamp。Grant保Site/Floor/Zone/Owner/Object/FieldPolicy/CanExport、有效期/Status/GrantVersion。[原文X:275](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:275) [原文X:281](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:281)

场景基版固定CurrentPublished且Published；附属availability/version仅按外部有权且需要知道的范围裁剪。内部sourceDecision、服务身份、库存保护细节和全manifest不外露；null字段有RESTRICTED/UNKNOWN语义，不可作为客户端CLEAR输入。[原文X:287](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:287) [原文X:289](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:289)

## 4. 流程、状态和入口写集

管理员在本授权域明确组织和合作方身份，邀请/接受成员，指定角色/范围/期限并更新stamp/version；新publication不激活Invited、不扩Grant/导出。组织停用影响当前访问，不能倒改历史source/WMS事实。[原文X:275](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:275) [原文X:281](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:281)

读请求先核Tenant/组织/Active membership/有效Grant/FieldPolicy/资源Site和location/Owner/Object交集，再筛同一已核published场景的几何，按有权准确mapping连接同版stock/tasks并逐字段遮罩。S4原子Target不是安全grant，不能多个grant无条件并成全量；未知mapping不构成全仓查询条件。[原文X:295](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:295) [原文X:297](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:297)

Frozen新源不外露为Published；Finalizer晋升前保旧已授权场景，无current则无可用场景。mapping/WMS/Runtime不同版时仍可按授权读场景，但相关库存/任务叠加不可用，不错配。[原文X:49](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:49) [原文X:289](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:289)

## 5. 接口和依赖合同

组织管理保 `/api/space/external-organization`；外部GET `/api/space/portal/v1`下organizations/sites/sites/{site}/published-scene/stock/tasks；AllowSpaceExternalSubject仅该读边界。原DTO、membership/grant安全stamp和audit保留，不加可写publication/WMS状态接口。[原文X:277](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:277) [原文X:287](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:287)

新compatibility投影按当前读权裁剪，共享S4原结果与X6上下文，客户端不可POST MappingRef/APPLIED来更新。历史报告文件访问按SYS-FILE政策独立判定，撤权不等于篡改报告历史。[原文X:283](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:283) [原文X:355](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:355)

## 6. 事务、并发、幂等和恢复

查询、分页、缓存、导出均绑定GrantVersion/SecurityStamp/fieldPolicy/scope；撤销/到期/版本变更重新检验，旧缓存不能继续泄漏。总数/hasMore考虑权限覆盖，不能泄漏隐含未授权库位数。scope变化使cursor/缓存失效，不能自动换宽范围重试。[原文X:283](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:283) [原文X:297](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:297) [原文X:355](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:355)

权限拒绝安全403/隐藏式404，不让locator存在性泄漏；没有当前读权也不能把空对象重建历史或从源冻结推可见。外部读无原Owner业务效果，不需要伪建幂等写事务；授权变更沿原版本/审计合同，不在R2虚构新成员状态流。[原文X:27](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:27) [原文P:126](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15f966e5afe3dc71__SPACE-PUB-01_完整详细设计_R2_CANDIDATE.md:126) [原文X:361](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:361)

## 7. 权限、租户和审计

安全约束核心是有效身份、组织/成员与Grant及字段策略交集。CanExport独立，读场景不授导出；新Target扩大不扩Grant；管理者不能被source发布者角色替代。审计保邀请/接受、有效期、stamp/GrantVersion与资源/字段scope，来源回执访问也需当前权。[原文X:281](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:281) [原文X:295](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:295)

## 8. 页面与服务端行为

保现Main读界面/API，不新增门户产品。场景可读/叠加不可用分别显示，RESTRICTED不暴露对象/数量；外部只显示必要availability/version，不展内部保护/服务凭据/源决定。授权撤销后分页/缓存重新取权，不能继续展示旧受限数据。[原文X:289](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:289) [原文X:299](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:299) [原文X:359](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:359)

## 9. 开发顺序及固定代码差异

X6-EXT-D01先核当前身份/授权与字段策略精确版本，再接同版只读projection和分页缓存失效；外部组织/成员原能力有界保留。S5/S6后续准确公共合同影响要定点核，不把当年“未交付”当永久无影响，也不据猜测新增身份种类/账号。[原文X:277](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:277) [原文X:283](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:283) [原文X:372](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:372)

### 原41 SPEC范围的本模块兼容边界

`HIT`表示本次发布/采纳合同需要调整；`NO_HIT_PRESERVE`保留原算法与Owner，但凡写入受控版本，仍须服从共同冻结门。这些分类不是实现完成或现场验收。矩阵保留的 `TO_AUTHOR` 为原范围快照，当前接受组合仍依本册第2节UA，不回退为无设计。

|原SPEC尾号/范围|范围结论|开发时保留或整合的边界|
|---|---|---|
|01 外部组织|NO_HIT_PRESERVE|外部组织主数据及SecurityStamp当前源码可复用；未收到S5新身份合同，不能凭推测重写 [矩阵:857](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/2c/2cba0684fb7cbf7e__MATRIX-8TARGET-41SPEC.json:857)|
|02 成员授权|NO_HIT_PRESERVE|成员授权有效期/GrantVersion/字段策略保留；IAM变化待Required，不自行扩大权限 [矩阵:875](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/2c/2cba0684fb7cbf7e__MATRIX-8TARGET-41SPEC.json:875)|
|03 只读门户接口|HIT|只读portal/v1查Published场景已有；新合同仍按授权范围给版本/采纳可用性，不宣称独立门户产品 [矩阵:894](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/2c/2cba0684fb7cbf7e__MATRIX-8TARGET-41SPEC.json:894)|
|04 范围控制|HIT|库存/任务与场景需相同已核版本/范围，缺映射不泄漏；不可通过新发布扩grant [矩阵:914](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/2c/2cba0684fb7cbf7e__MATRIX-8TARGET-41SPEC.json:914)|

原矩阵的“已有源码/可复用”只承接准确固定源身份及原有界审读，未完成完整97源码审计；新运行、真实Owner采用和S1精确consumer确认均未证明。[原独审状态:1455](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/ff/fff3a9611b515c00__CP6_S4_X6_R2_独立复核证据与41SPEC范围.json:1455) [来源资格:3435](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/fe/fed1fafddca4ed99__SOURCE-QUALIFICATIONS.json:3435)

## 10. 验收场景与证据状态

XE01～06 NOT_RUN：组织字段/Owner保持、未知不fallback关联；新publication不扩成员scope/导出；外部不能读内部Frozen或全源决定；场景stock不同版不混展；更宽Target不扩旧Grant；撤销/版本变化后的分页缓存不泄漏。还要验证无current、旧current、新晋升及后续Runtime drift各读路径。[原文X:277](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:277) [原文X:283](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:283) [原文X:291](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:291) [原文X:299](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:299) [原文X:49](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:49)

## 11. 缺口与退出条件

实际IAM/服务/外部subject权限、Grant/缓存/导出策略与SYS-FILE旧报告访问规则须准确Owner合同，不由Space设计补成已配置。真实客户端版本和全部泄漏通道验证未运行；剩余来源阅读照台账，普通历史接受不等运行认证。[原文X:384](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:384) [原文X:388](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:388) [原文X:390](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:390)


R2的“尚未取得S5新身份合同”是其冻结时的组合范围。当前已有S5 v0.2；其中Main/CRM映射答复不自动构成Space consumer采用。Space的GrantVersion/SecurityStamp、字段策略、缓存/分页/导出撤权须另定精确映射，认证版本也不等业务权限epoch。[原X6:277](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:277) [S5:14](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/9e/9e3be10c13d1599c__S5_TO_S6_TENANT_IAM_CONSUMPTION_RESPONSE_v0.2.md:14) [S5:23](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/9e/9e3be10c13d1599c__S5_TO_S6_TENANT_IAM_CONSUMPTION_RESPONSE_v0.2.md:23)

## 12. 实际阅读

本轮全文读X6 R2 1～394（本Target专段269～299；公共15～52/341～394）、S4 R2 1～369、独审1～208、R2细化AC JSON 1～423；本Target UA按接受决定、主文/组合身份及执行边界结构化阅读。固定源码结论来自已接受规范，本册未新做97源码全文认证。强制附件/历史细节真实阅读与待补段见 [本组阅读台账](D:/CP6/docs/CP6_开发设计文档_20261010/evidence/platform-engineering-reading.json)。当前required_materials_consolidated，保留历史/源码/视觉边界；未执行业务测试/构建/迁移/Actions。

本輪補讀 MATRIX 8 Target/41 SPEC 全部字段（结构化）、独审41项判语/4项设计根与资格边界（选段），SOURCE-QUALIFICATIONS 3110–3440的固定源/阅读限制；不会将其记录的源码审读窗口记作本代理实读。

**当前必要材料归并状态。** 本册为 `required_materials_consolidated`：选定正文、规范附件及准确接受/复核范围已按直接实读、结构化语义或精确复用归并。身份清单核对不等底层源码全文阅读；历史草稿、候选实现和Office/HTML视觉未核范围仍单列。可复查[逐件阅读与处置](D:/CP6/docs/CP6_开发设计文档_20261010/evidence/platform-engineering-reading.json)及[本组闭包台账](D:/CP6/docs/CP6_开发设计文档_20261010/worklogs/platform-engineering-progress.json)。当前必需规范未读清单为空；具体Owner采用、参数/物理接口、真实运行和本节已有来源缺口不因整理闭包而改变。
