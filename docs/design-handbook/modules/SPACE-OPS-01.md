# SPACE-OPS-01 · 空间运营、位置叠加、容量与作业建议

整理状态：`core_semantics_consolidated`；当前业务规则有据整理，实际Owner采用和运行验收保持UNPROVEN/NOT_RUN。

## 1. 目的、操作者和业务 Owner

运营读者查看受权库存/任务/人员与空间几何；分析者给上架/派工候选和诊断。Space负责定位投影、解释/建议，WMS拥有库存/规则/任务执行，Runtime提供原生实际状态；有建议或Workflow批准不能填实际库存已上架/Task已派工。[原文X:219](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:219) [原文X:247](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:247) [原文X:251](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:251)

## 2. 精确规范与接受组合

本Target独立UA接受S4 R2 `15f966e5afe3dc71…`＋X6 R2 `6f8585ca3b85c6aa…`准确完整SHA组合（全值见本组冻结合同）；Disposition=ACCEPTED_CURRENT_MARKDOWN_STATIC_DETAILED_DESIGN。正文CANDIDATE/待审为历史，接受后仍保Runtime NOT_RUN、Owner采用UNPROVEN。范围：6 SPEC，5 HIT＋1有界保留；12 AC。NO_HIT仅当前公共变化未直接触及原算法，不是无设计或永久免回归。[原文A:6](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/c4/c41f80b01c76123c__UA-20261008-X6-SPACE-OPS-01-R2-MD02.json:6) [原文X:7](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:7) [原文X:13](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:13) [原文R:194](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/c4/c46bf5d8d9ed7e2c__CP6_S4_X6_R2_独立定点复核与累计范围.md:194)

共同source/current/Runtime身份、冻结写门和合法后继统一使用 [SPACE-PUB-01](D:/CP6/docs/CP6_开发设计文档_20261010/modules/SPACE-PUB-01.md)，不能各自建立权威WMS结果或另一个gate。[原文X:15](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:15) [原文X:29](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:29)

## 3. 数据身份、字段与量纲

Inventory/locate保SiteId/PublishedVersionId/WarehouseCode，Source.Kind/Adapter/DataSource/Observed/Received/Delay/Skew/IsSimulated/IsAvailable；item含LocationLogicalId/WmsLogicalId、Space/WmsCode/CodeMatches、floor、Physical/AllocatedQuantity、物料/批次/容器/Owner。正式L→设计S由准确MappingRef与版本关联，不靠CodeMatches。[原文X:223](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:223) [原文X:225](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:225)

容量分别为几何容量、位置占用率和WMS typed作业容量；LocationCount、nullable OccupiedCount/Percent、CapacityUtilizationPercent/Status/Reason，量纲/Owner/分母覆盖各自说明。UNKNOWN不等0，无单位换算/完整分母不能算全局比例；旧CapacityQty=0不可改UNLIMITED。[原文X:231](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:231) [原文X:233](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:233)

上架建议输入Material/Owner/Lot/InboundQty、Floor/Zone、尺寸/载重、合并/max candidates；结果RecommendationId/PublishedVersion/DefinitionVersion/Sources、排除/样本/排序/rule hits。派工输入类型/范围/跨层/距离/是否含模拟人员，结果TaskContractVersion/ExecutionVersion/RowVersion、人员源/位置时刻、任务位置/排除。诊断固定PublishedVersion/Warehouse/WindowFrom-To/CalculatedAt/DefinitionVersion、阈值、来源、回走/停留/拥堵/容量/Limitations。[原文X:247](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:247) [原文X:263](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:263)

## 4. 流程、状态和入口写集

定位先核Site/库存读权→固定场景和mapping→取WMS快照/coverage→同版连接→字段脱敏。P2源Frozen但WMS/Actual=P1时用有据一致视图并另给P2设计预览，不把P1库存落到P2改动几何；无准确mapping保受权作业列表UNLOCATED，不投原点。[原文X:225](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:225) [原文X:227](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:227)

读取库存/任务与当前资格生成建议；code mismatch、mapping未知、inactive/blocked未知、过时事实应排除或明确unknown，新采纳DRAFT L不自动可上架。选择只是后续业务请求候选，执行Owner再核任务前驱/库位规则/权限/保护，原locator保留。诊断只解释，布局变更后旧结果保原GeometryBasis/CalculatedAt并标STALE_FOR_CURRENT_LAYOUT，不自动改历史热区。[原文X:249](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:249) [原文X:251](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:251) [原文X:265](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:265)

## 5. 接口和依赖合同

原GET `/api/space/design/v1/sites/{siteId}/runtime/inventory`、inventory/locate；诊断GET `/api/space/operations/v1/sites/{siteId}/diagnostics` 按窗口只读；建议/执行适配与receipt链保原Owner，不能手册扩大任务写权。[原文X:223](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:223) [原文X:251](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:251) [原文X:263](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:263)

CompatibilityContext复用唯一来源投影：source publication/target、WMS canonical/result、runtime desired/actual、两种ProjectionPointer、source availability、original attempts/coverage。原生Desired selectionStream/epoch/generation、Actual actualStream/epoch/sequence不等本地projectionRevision。[原文X:21](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:21) [原文X:43](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:43)

## 6. 事务、并发、幂等和恢复

同scope事实按Owner原生流代次/顺序，较低Actual晚到补历史；更高真实序即使回P1也显示Desired=P2/Actual=P1 drift，不为新页面丢弃；换流/epoch无证UNKNOWN。页面请求序仅防scope切换后画错页面，不能覆盖Owner因果顺序。SignalR仅提示GET，刷新/断线不写采纳或归零。[原文X:43](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:43) [原文X:241](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:241)

历史diagnostic/recommendation固定几何/任务/源版本；执行前任务RowVersion变更不能直接用旧建议，应由实际Owner处理新合法意图。WMS APPLIED后Runtime失败只续原Runtime，不重作库存/采纳。[原文X:253](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:253) [原文X:265](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:265) [原文P:256](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15f966e5afe3dc71__SPACE-PUB-01_完整详细设计_R2_CANDIDATE.md:256)

## 7. 权限、租户和审计

当前Tenant/Site、库存/任务/人员字段读权独立；控制台刷新不获得design/adopt/device写权；轨迹按人员范围和时间授权，新publication不能扩大人员可见范围。返回数量/分母需coverage，隐藏库位不能当空闲；多源部分故障只降相应卡片并留时点。[原文X:27](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:27) [原文X:235](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:235) [原文X:241](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:241) [原文X:259](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:259)

## 8. 页面与服务端行为

现viewer/control tower/建议/诊断保留，source frozen、WMS applied/unknown、Runtime Desired/Actual、mapping readiness分栏；GeometryBasis与RuntimeBasis不一致明确展示。真库存来源时刻/延迟/模拟标记保持；无映射不投点，无源不空库存，旧缓存不称实时，unknown容量不显0%或无限。建议可展示NON_EXECUTABLE替代，不给执行完成勾选。[原文X:225](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:225) [原文X:233](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:233) [原文X:239](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:239) [原文X:249](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:249) [原文X:265](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:265)

## 9. 开发顺序及固定代码差异

X6-OPS-D01依准确WMS库存/任务/mapping/Runtime读合同补一致版本、typed容量、分层控制台、建议/诊断来源；设备协议/采样算法有界保留，但人员事件几何仍受current规则。源码能力不证明现场遥测运行，不重建独立设备控制系统。[原文X:259](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:259) [原文X:371](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:371)

### 原41 SPEC范围的本模块兼容边界

`HIT`表示本次发布/采纳合同需要调整；`NO_HIT_PRESERVE`保留原算法与Owner，但凡写入受控版本，仍须服从共同冻结门。这些分类不是实现完成或现场验收。矩阵保留的 `TO_AUTHOR` 为原范围快照，当前接受组合仍依本册第2节UA，不回退为无设计。

|原SPEC尾号/范围|范围结论|开发时保留或整合的边界|
|---|---|---|
|01 可视化定位|HIT|发布几何与WMS库存锚必须按映射版本连接；未采纳/旧实际布局不得显示为新布局已生效 [矩阵:713](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/2c/2cba0684fb7cbf7e__MATRIX-8TARGET-41SPEC.json:713)|
|02 容量/利用率|HIT|空值/availability及容量状态保留；WMS typed容量UNKNOWN不能用Space几何容量代填 [矩阵:731](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/2c/2cba0684fb7cbf7e__MATRIX-8TARGET-41SPEC.json:731)|
|03 运营控制台|HIT|控制台对发布、采纳、Desired、Actual分栏显示，禁止统一绿色成功 [矩阵:750](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/2c/2cba0684fb7cbf7e__MATRIX-8TARGET-41SPEC.json:750)|
|04 上架/派工建议|HIT|建议有独立身份、输入和结果；建议/审批不能越过WMS执行Owner与当前库位准入 [矩阵:769](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/2c/2cba0684fb7cbf7e__MATRIX-8TARGET-41SPEC.json:769)|
|05 设备人员轨迹|NO_HIT_PRESERVE|人员设备SourceKind/观测时间与模拟排除保持；未改变遥测入站和设备控制合同 [矩阵:787](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/2c/2cba0684fb7cbf7e__MATRIX-8TARGET-41SPEC.json:787)|
|06 诊断|HIT|诊断绑定PublishedVersion与数据可用性；更换当前发布/映射后旧诊断须过时标签，不改历史 [矩阵:804](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/2c/2cba0684fb7cbf7e__MATRIX-8TARGET-41SPEC.json:804)|

原矩阵的“已有源码/可复用”只承接准确固定源身份及原有界审读，未完成完整97源码审计；新运行、真实Owner采用和S1精确consumer确认均未证明。[原独审状态:1455](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/ff/fff3a9611b515c00__CP6_S4_X6_R2_独立复核证据与41SPEC范围.json:1455) [来源资格:3435](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/fe/fed1fafddca4ed99__SOURCE-QUALIFICATIONS.json:3435)

## 10. 验收场景与证据状态

XO01～12 NOT_RUN：同码异身份/版不误定位，未采纳设计不冒实际；unknown容量不归0或无限、部分coverage不报全仓比例；source成功/WMS未知/旧Actual并存，断线不归零；建议不写Stock/Task，新DRAFT/未知mapping无准入，任务RowVersion改变阻旧建议；模拟轨迹非现场证据；旧诊断显过时，缺轨迹/容量不造0/自动派工。[原文X:227](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:227) [原文X:235](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:235) [原文X:243](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:243) [原文X:253](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:253) [原文X:259](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:259) [原文X:267](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:267)

## 11. 缺口与退出条件

真实WMS类型化容量、位置/任务主账读范围、正式mapping/current一致性、Runtime流/epoch转换与coverage、真实人员源和执行适配均需有据确认。当前SPACE文中的Required未获得本次独立生产采用；无源能力可做受限列表/隔离诊断，不模拟现场成功。原算法细读余量按台账。[原文X:382](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:382) [原文X:386](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:386)

## 12. 实际阅读

本轮全文读X6 R2 1～394（本Target专段217～267；公共15～52/341～394）、S4 R2 1～369、独审1～208、R2细化AC JSON 1～423；本Target UA按接受决定、主文/组合身份及执行边界结构化阅读。固定源码结论来自已接受规范，本册未新做97源码全文认证。强制附件/历史细节真实阅读与待补段见 [本组阅读台账](D:/CP6/docs/CP6_开发设计文档_20261010/evidence/platform-engineering-reading.json)。当前required_materials_consolidated，保留历史/源码/视觉边界；未执行业务测试/构建/迁移/Actions。

本輪補讀 MATRIX 8 Target/41 SPEC 全部字段（结构化）、独审41项判语/4项设计根与资格边界（选段），SOURCE-QUALIFICATIONS 3110–3440的固定源/阅读限制；不会将其记录的源码审读窗口记作本代理实读。

**当前必要材料归并状态。** 本册为 `required_materials_consolidated`：选定正文、规范附件及准确接受/复核范围已按直接实读、结构化语义或精确复用归并。身份清单核对不等底层源码全文阅读；历史草稿、候选实现和Office/HTML视觉未核范围仍单列。可复查[逐件阅读与处置](D:/CP6/docs/CP6_开发设计文档_20261010/evidence/platform-engineering-reading.json)及[本组闭包台账](D:/CP6/docs/CP6_开发设计文档_20261010/worklogs/platform-engineering-progress.json)。当前必需规范未读清单为空；具体Owner采用、参数/物理接口、真实运行和本节已有来源缺口不因整理闭包而改变。
