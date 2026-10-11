# SPACE-MASTER-01 · 空间主数据、结构与编码

整理状态：`core_semantics_consolidated`；当前组合的业务规则已整理，原Owner采用和运行验收保持UNPROVEN/NOT_RUN。

## 1. 目的、操作者和业务 Owner

设计者维护 Site/Floor/Zone/Aisle/Rack/设计位置 S；规则维护者维护编码段，设计应用者将有据提案写入可编辑草稿。Main Space 拥有设计空间，不取代 WMS 正式作业库位；工程 BOM、财务价格及库存移动不在本模块写集。[原文X:57](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:57) [原文X:71](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:71) [原文X:79](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:79)

## 2. 精确规范与接受组合

本Target采用S4 R2 `15f966e5afe3dc71…`＋X6 R2 `6f8585ca3b85c6aa…`准确完整SHA组合（全值见本组冻结合同），独立UA的Disposition=ACCEPTED_CURRENT_MARKDOWN_STATIC_DETAILED_DESIGN；正文CANDIDATE/STOPPED/待独审是历史，不是当前缺设计。范围：4 SPEC，全4项HIT；8 AC。NO_HIT_PRESERVE仅对这次公共变更有界保留，不是无业务/免回归。静态接受不授Owner生产采用；全部AC NOT_RUN。[原文A:6](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/21/2171264c2f2689a6__UA-20261008-X6-SPACE-MASTER-01-R2-MD02.json:6) [原文X:7](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:7) [原文X:13](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:13) [原文R:194](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/c4/c46bf5d8d9ed7e2c__CP6_S4_X6_R2_独立定点复核与累计范围.md:194)

公共冻结、合法后继、源发布和current/Runtime的具体合同统一见 [SPACE-PUB-01](D:/CP6/docs/CP6_开发设计文档_20261010/modules/SPACE-PUB-01.md)，本模块不另建gate或WMS主账。[原文X:29](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:29)

## 3. 数据身份、字段与量纲

Site 保 SiteCode/SiteName/Address/Lng/Lat/Enable/WarehouseCd；Floor 保 SiteId/Level/Code/Name/Height/底图比例偏移；Zone 保 FloorId/Code/Name/Type/Polygon/Color/Enable。Aisle 是 ZoneId/Polygon/Centerline；Rack 保 ZoneId/AisleId/TemplateId、Code/XYZ/RotationZ、Cols/Levels/DepthCount、CellW/H/D/Enable/RowVersion。列层深生成设计 S，不能自动生成正式 L。[原文X:61](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:61) [原文X:71](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:71)

编码 schemaVersion=1，mode=fill-empty/rebuild；Segment含Key/Source/Width/Pad/Start/Step/Separator/Upper/FixedValue/Optional，保 RuleId/RuleHash、ProposalHash/RuleSetHash、Changed/Unchanged/ProtectedCount。源100字符域、新L30字符域、规范显示和匹配键分列，不截断/散列长码，也不更换 S 身份绕受保护项。[原文X:79](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:79) [原文X:81](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:81)

## 4. 流程、状态和入口写集

读可信 Site及当前版本/字段权→仅编辑允许设计字段→按所属profile原并发资格及共同门保存→旧发布预览失效→重新预览。Site WarehouseCd为空以SiteCode兜底只保legacy原义，新 SourceSite→Warehouse/Target 须批准映射；没有映射可保存设计，生产发布预览 MAPPING_UNPROVEN。[原文X:63](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:63) [原文X:65](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:65)

编码先选楼层/区域、按全量数据生成 proposal，显示current/proposed/decision/reason及changed/unchanged/protected；固定ProposalHash提交Apply，实际设计变更递增ContentRevision与相应源位置修订，返回设计AppliedCount。预检不建正式L；不能删失败/受保护行绕门。父子结构变化、货架移动或设计软退役都不搬库存；有发布/来源/映射引用不得破坏历史删除。[原文X:73](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:73) [原文X:83](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:83) [原文X:87](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:87)

## 5. 接口和依赖合同

保留原 `/api/space/site`、floor、zone GET/POST/PUT/DELETE 与编码 preview/apply。Preview 请求 mode、scopeZoneLogicalId、ExpectedFloorRevision、ExpectedContentRevision；Apply另带CommandBatchId/ClientInstanceId/LeaseId/ProposalHash。原路径字段不增伪必填并发值；兼容mapping证据只读，不随SiteDto写入。[原文X:61](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:61) [原文X:65](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:65) [原文X:87](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:87)

S4消费准确 source logical ID/positive revision/path/属性。CREATE_OPERATIONAL 的warehouse/locationLevel/parent/plannedNodeKey/hierarchyPolicy必须来自批准定义；设计无aisle可保存，但不能补造WMS通路。正式映射或创建由WMS Owner决定。[原文X:73](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:73) [原文P:179](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15f966e5afe3dc71__SPACE-PUB-01_完整详细设计_R2_CANDIDATE.md:179)

## 6. 事务、并发、幂等和恢复

Rack使用原RowVersion；DesignV1使用内容/楼层前驱＋租约＋共同VersionWriteEligibility，最终事务拒绝已Frozen；编码规则/版本变化使旧proposal失效，须新预览。legacy Site/Floor/Zone/Aisle原DTO没有通用RowVersion/lease，不能据手册假定已有412保护；新profile须完整旧writer围栏或明确版本化新保护。[原文X:67](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:67) [原文X:75](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:75) [原文P:291](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15f966e5afe3dc71__SPACE-PUB-01_完整详细设计_R2_CANDIDATE.md:291)

超时查原CommandBatch，同proposal重复回原结果，未知不换batch。已成功设计命令只回放；排队写到提交点重核冻结门，未知在途写先阻封存；新key不能重键设计位置。[原文X:91](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:91) [原文X:31](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:31)

## 7. 权限、租户和审计

设计者需当前Tenant/Site与space-site add/edit/delete、space-floor edit等原权；编码Apply需model:edit及具体版/楼层租约。新映射字段只读，不能借可编辑Site获得WMS决定权。跨租户/Site403或安全404；审计保原rule/proposal、输入前驱、位置修订、修改人/原命令与发布引用。[原文X:61](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:61) [原文X:67](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:67) [原文X:79](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:79) [原文P:327](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15f966e5afe3dc71__SPACE-PUB-01_完整详细设计_R2_CANDIDATE.md:327)

## 8. 页面与服务端行为

原Site/Floor/结构/拖拽/旋转/尺寸/模板/编码画布保留；加设计SiteCode、旧warehouse来源、正式ScopeMapping证据/版本、已发布/保护与未知标记。长码问题显示需批准映射，不能一键截断；映射未知不挡普通读图。并发失败保未保存更改供对比，不自动覆盖或重键。[原文X:63](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:63) [原文X:75](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:75) [原文X:83](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:83)

## 9. 开发顺序及固定代码差异

原X6-MASTER-D01：保CRUD与编码算法，先只读映射/来源分层，再在全部实际写点接共同冻结门和源位置修订，最后接S4完整发布Target与WMS正式映射。固定源码报告为Main@90c871fe，不是本轮新代码审计；legacy字段能力与DesignV1不能混称。[原文X:9](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:9) [原文X:367](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:367)

### 原41 SPEC范围的本模块兼容边界

`HIT`表示本次发布/采纳合同需要调整；`NO_HIT_PRESERVE`保留原算法与Owner，但凡写入受控版本，仍须服从共同冻结门。这些分类不是实现完成或现场验收。矩阵保留的 `TO_AUTHOR` 为原范围快照，当前接受组合仍依本册第2节UA，不回退为无设计。

|原SPEC尾号/范围|范围结论|开发时保留或整合的边界|
|---|---|---|
|01 场地/楼层/分区|HIT|Site.WarehouseCd旧空值回退SiteCode有源码；新采纳映射不得沿用无证默认，场地楼层分区CRUD保留 [矩阵:31](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/2c/2cba0684fb7cbf7e__MATRIX-8TARGET-41SPEC.json:31)|
|02 巷道货架|HIT|巷道货架几何和模板参数保留；发布路径不是WMS五层建树指令，正式父子需显式批准映射 [矩阵:50](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/2c/2cba0684fb7cbf7e__MATRIX-8TARGET-41SPEC.json:50)|
|03 编码规则|HIT|fill-empty/rebuild、rule/proposal hash、protected count复用；长Space码不得截断为30字符作业码 [矩阵:69](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/2c/2cba0684fb7cbf7e__MATRIX-8TARGET-41SPEC.json:69)|
|04 预检生成|HIT|预检生成仅改设计草稿；绑定/已发布代码受保护，生成不制造L或库存 [矩阵:88](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/2c/2cba0684fb7cbf7e__MATRIX-8TARGET-41SPEC.json:88)|

原矩阵的“已有源码/可复用”只承接准确固定源身份及原有界审读，未完成完整97源码审计；新运行、真实Owner采用和S1精确consumer确认均未证明。[原独审状态:1455](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/ff/fff3a9611b515c00__CP6_S4_X6_R2_独立复核证据与41SPEC范围.json:1455) [来源资格:3435](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/fe/fed1fafddca4ed99__SOURCE-QUALIFICATIONS.json:3435)

## 10. 验收场景与证据状态

XM01～08保持NOT_RUN：空WarehouseCd仅legacy兜底、新发布缺映射阻断；分区移动/停用不改库存；无aisle不补正式树；货架改动保可追溯后继；31字符源码不截成L；同proposal重复/规则变化；预检Apply均不写WMS；Apply后旧发布预览拒绝、保护项不被rebuild。R2增补冻结后的所有实际写点和源r7/r8/最大值子场景，不新增AC身份。[原文X:67](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:67) [原文X:75](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:75) [原文X:83](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:83) [原文X:91](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:91) [原文P:142](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15f966e5afe3dc71__SPACE-PUB-01_完整详细设计_R2_CANDIDATE.md:142)

## 11. 缺口与退出条件

真实 SourceSite/仓库和S/B/L MappingDecision、合法存量positive修订、编码保护的准确前驱、全部legacy writer围栏和IAM当前能力仍须取得；缺映射限制依赖的新生产效果，保合法设计与历史。不能把主数据表存在/Host注册当生产采用证明。[原文X:380](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:380) [原文P:360](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15f966e5afe3dc71__SPACE-PUB-01_完整详细设计_R2_CANDIDATE.md:360)

## 12. 实际阅读

本轮全文读X6 R2 1～394（本Target专段55～91，共同规则15～52/341～394），S4 R2 1～369、独审1～208、R2细化AC JSON 1～423；本Target UA结构化读接受决定、主文/组合身份和执行边界。固定源码的业务结论按当前规范引用，本册没有新做97份源代码全文认证；更细历史实现/附件实际阅读见 [本组阅读台账](D:/CP6/docs/CP6_开发设计文档_20261010/evidence/platform-engineering-reading.json)。当前为required_materials_consolidated；历史/源码/视觉边界保留。未运行构建、项目脚本、业务测试、迁移或Actions。

本輪補讀 MATRIX 8 Target/41 SPEC 全部字段（结构化）、独审41项判语/4项设计根与资格边界（选段），SOURCE-QUALIFICATIONS 3110–3440的固定源/阅读限制；不会将其记录的源码审读窗口记作本代理实读。

**当前必要材料归并状态。** 本册为 `required_materials_consolidated`：选定正文、规范附件及准确接受/复核范围已按直接实读、结构化语义或精确复用归并。身份清单核对不等底层源码全文阅读；历史草稿、候选实现和Office/HTML视觉未核范围仍单列。可复查[逐件阅读与处置](D:/CP6/docs/CP6_开发设计文档_20261010/evidence/platform-engineering-reading.json)及[本组闭包台账](D:/CP6/docs/CP6_开发设计文档_20261010/worklogs/platform-engineering-progress.json)。当前必需规范未读清单为空；具体Owner采用、参数/物理接口、真实运行和本节已有来源缺口不因整理闭包而改变。
