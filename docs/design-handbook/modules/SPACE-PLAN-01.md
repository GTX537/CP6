# SPACE-PLAN-01 · 布局情景、历史回放、仿真比较与人类决策

整理状态：`core_semantics_consolidated`；当前业务规则有据整理，实际Owner采用和运行验收保持UNPROVEN/NOT_RUN。

## 1. 目的、操作者和业务 Owner

空间规划员/分析员建立离线情景、数据集、运行和比较，决策者选择方案并记理由。Main Space拥有规划事实；MRP需求计划、正式Task、成本凭证和库存变更仍归原业务Owner。本模块不把规划选择改称发布批准或真实布局采用。[原文X:181](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:181) [原文X:211](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:211)

## 2. 精确规范与接受组合

本Target独立UA接受S4 R2 `15f966e5afe3dc71…`＋X6 R2 `6f8585ca3b85c6aa…`准确完整SHA组合（全值见本组冻结合同）；Disposition=ACCEPTED_CURRENT_MARKDOWN_STATIC_DETAILED_DESIGN。正文CANDIDATE/待审为历史，接受后仍保Runtime NOT_RUN、Owner采用UNPROVEN。范围：5 SPEC，2 HIT＋3有界保留；7 AC。NO_HIT仅当前公共变化未直接触及原算法，不是无设计或永久免回归。[原文A:6](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/90/90a587d8cd0947a2__UA-20261008-X6-SPACE-PLAN-01-R2-MD02.json:6) [原文X:7](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:7) [原文X:13](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:13) [原文R:194](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/c4/c46bf5d8d9ed7e2c__CP6_S4_X6_R2_独立定点复核与累计范围.md:194)

共同source/current/Runtime身份、冻结写门和合法后继统一使用 [SPACE-PUB-01](D:/CP6/docs/CP6_开发设计文档_20261010/modules/SPACE-PUB-01.md)，不能各自建立权威WMS结果或另一个gate。[原文X:15](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:15) [原文X:29](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:29)

## 3. 数据身份、字段与量纲

ScenarioBranch保branch/model/site、BasePublishedVersionId/scenario版、clone job/status、DefinitionVersion、ProductionIsolated/Limitations；场景ContentRevision和基版固定。Dataset保HistoricalFrom/To、ReplayStart/Speed、SourceDatasetHash、ConfirmDeidentified、DeidentificationVersion、ReplayClock；任务为去标识TaskToken/WorkerToken、type/outcome、原创建/完成时刻、From/ToLogicalId、Quantity。[原文X:185](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:185) [原文X:193](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:193)

Simulation输入DatasetId、数量/并发任务容量假设、throughput窗口、距离/工时/拥堵费用率、currency、location capacity override；结果固定ScenarioRevision、DatasetRequest/ResultHash、distance coverage、unknown task、拥堵/容量/吞吐/估算成本、HighPrecisionPhysicalSimulation/ProductionWriteAllowed/Limitations。Comparison保BaselineRunId/RunIds及各result hash、source dataset hash、币种/历史窗、阈值和metrics/delta/risk。Decision保Outcome/SelectedRunId/Rationale/SupersedesDecisionId、ComparisonHash/DefinitionVersion、HumanDecision/AutomatedRecommendation/ProductionWriteAllowed。[原文X:199](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:199) [原文X:205](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:205) [原文X:211](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:211)

## 4. 流程、状态和入口写集

选真实CurrentPublished且Status=Published建立branch，固定创建时base，不占production draft slot；没有current或只有Frozen不能创建此Published分支。后续production/current变化只提示差异/过时，原branch不重基。[原文X:185](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:185) [原文X:47](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:47)

选择固定branch和历史窗口→明确脱敏→生成数据集（原时间与回放时间并存）→按明确假设固定dataset/场景revision运行→比较兼容runs→人类决定。缺几何使距离coverage未知，不补0；数值相似不能跨不相容数据集/币种/窗口硬比较；修改参数/阈值新建run/comparison，不改旧结果。[原文X:193](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:193) [原文X:199](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:199) [原文X:205](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:205)

决定实施只给合法设计入口准备独立production draft并重审真实当前依据，不能原地修改Scenario Purpose，不能写CurrentPublished、库存、派工或复制PLM许可。[原文X:187](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:187) [原文X:213](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:213)

## 5. 接口和依赖合同

保 `/api/space/planning/v1/sites/{siteId}/scenario-branches` 的PUT {branchId}、GET detail/list；创建请求BasePublishedVersionId/Name。决定PUT comparisons/{comparisonId}/decisions/{decisionId}和GET。其他dataset/run/comparison精确DTO按原合同，不据手册发明路由。[原文X:185](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:185) [原文X:211](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:211)

只读CompatibilityContext引用固定基版publication/WMS/Runtime可用性，不使Published变现场真实证明。生产后继接DES/S4独立设计/批准；历史数据不是原Task新执行输入，费用不写Finance。[原文X:187](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:187) [原文X:201](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:201)

## 6. 事务、并发、幂等和恢复

同branchId同正文回原branch，异正文冲突；clone失败查原job/恢复原初始化，不重复分配版本。各run/comparison固定源hash和revision，后续场景变化不重算旧run；decisionId不能修改选定run，纠正用明确SupersedesDecisionId。[原文X:189](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:189) [原文X:201](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:201) [原文X:207](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:207) [原文X:215](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:215)

未知原job/版本查原定位，不自动换base/current；新profile只有专用Finalizer推进生产current，规划查询不得自己触发WMS/Runtime writer。Scenario始终生产隔离。[原文X:41](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:41) [原文X:47](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:47)

## 7. 权限、租户和审计

分支planning:scenario:create/read和Site范围；决定planning:decision:create/read；原数据去标识确认不能把真实人员明文放匿名token。可信租户/对象和字段权贯穿数据集、比较及输出；规划角色不继承publish/adopt/Runtime权。[原文X:189](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:189) [原文X:193](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:193) [原文X:211](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:211) [原文X:27](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:27)

## 8. 页面与服务端行为

保情景/数据集/仿真/比较画布，显示固定base/场景revision、原历史窗口与ReplayClock、覆盖/unknown/假设及模型限制、各run hash和可比条件、人类选择理由与后继。基版Published只是设计基版；当前Runtime不一致明确说明。决策后按钮是查看方案/合法设计入口准备实施，不是直接上线。[原文X:193](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:193) [原文X:199](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:199) [原文X:205](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:205) [原文X:213](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:213)

## 9. 开发顺序及固定代码差异

X6-PLAN-D01补基版/current解释、Scenario隔离、decision到独立production衔接；历史数据集/仿真/比较算法保留，不重建MRP。读取compatibility可UNKNOWN，不必等待真实WMS执行才能完成隔离设计，但不能以模拟数据宣称现场吞吐或高精物理能力。[原文X:370](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:370) [原文X:201](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:201)

### 原41 SPEC范围的本模块兼容边界

`HIT`表示本次发布/采纳合同需要调整；`NO_HIT_PRESERVE`保留原算法与Owner，但凡写入受控版本，仍须服从共同冻结门。这些分类不是实现完成或现场验收。矩阵保留的 `TO_AUTHOR` 为原范围快照，当前接受组合仍依本册第2节UA，不回退为无设计。

|原SPEC尾号/范围|范围结论|开发时保留或整合的边界|
|---|---|---|
|01 情景分支|HIT|情景已固定BasePublishedVersionId且ProductionIsolated；展示采纳状态附属信息，禁止从情景直接发布 [矩阵:582](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/2c/2cba0684fb7cbf7e__MATRIX-8TARGET-41SPEC.json:582)|
|02 历史数据集|NO_HIT_PRESERVE|脱敏历史task token/回放时钟/源dataset hash保持；本轮不变历史数据集算法 [矩阵:602](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/2c/2cba0684fb7cbf7e__MATRIX-8TARGET-41SPEC.json:602)|
|03 仿真|NO_HIT_PRESERVE|容量/成本/距离覆盖计算是离线情景；S4未变仿真模型，不升级现场性能 [矩阵:619](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/2c/2cba0684fb7cbf7e__MATRIX-8TARGET-41SPEC.json:619)|
|04 比较|NO_HIT_PRESERVE|比较固定RunResultHash/同dataset等原规则保留；公共发布不授权自动排名或运行 [矩阵:636](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/2c/2cba0684fb7cbf7e__MATRIX-8TARGET-41SPEC.json:636)|
|05 决策|HIT|决策保留HumanDecision/ProductionWriteAllowed=false；选择运行结果不是实际切换，后继生产草稿须独立入口 [矩阵:654](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/2c/2cba0684fb7cbf7e__MATRIX-8TARGET-41SPEC.json:654)|

原矩阵的“已有源码/可复用”只承接准确固定源身份及原有界审读，未完成完整97源码审计；新运行、真实Owner采用和S1精确consumer确认均未证明。[原独审状态:1455](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/ff/fff3a9611b515c00__CP6_S4_X6_R2_独立复核证据与41SPEC范围.json:1455) [来源资格:3435](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/fe/fed1fafddca4ed99__SOURCE-QUALIFICATIONS.json:3435)

## 10. 验收场景与证据状态

XP01～07 NOT_RUN：生产版变化不改branch基版；Scenario不发布/采纳；原时间/回放/hash并列且不建正式Task；容量/费用只作用仿真、unknown不归零；不合法数据集/币种等比较拒绝；选择run不写任务/current；后继decision保ComparisonHash不能同ID改选。R2补冻结尚未晋升/无current时的分支条件。[原文X:189](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:189) [原文X:195](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:195) [原文X:201](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:201) [原文X:207](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:207) [原文X:215](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:215)

## 11. 缺口与退出条件

更细原仿真公式、默认参数值和可比阈值只在原实现/专项合同有据处采用，本R2未重新冻结数值不能臆填；真实脱敏来源、模型限制、所有客户端版本与新current读合同需验证。数据缺几何保unknown覆盖，规划通过永不补成生产采用。强制材料余量仍在阅读台账。[原文X:199](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:199) [原文X:205](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:205) [原文X:390](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:390)

## 12. 实际阅读

本轮全文读X6 R2 1～394（本Target专段179～215；公共15～52/341～394）、S4 R2 1～369、独审1～208、R2细化AC JSON 1～423；本Target UA按接受决定、主文/组合身份及执行边界结构化阅读。固定源码结论来自已接受规范，本册未新做97源码全文认证。强制附件/历史细节真实阅读与待补段见 [本组阅读台账](D:/CP6/docs/CP6_开发设计文档_20261010/evidence/platform-engineering-reading.json)。当前required_materials_consolidated，保留历史/源码/视觉边界；未执行业务测试/构建/迁移/Actions。

本輪補讀 MATRIX 8 Target/41 SPEC 全部字段（结构化）、独审41项判语/4项设计根与资格边界（选段），SOURCE-QUALIFICATIONS 3110–3440的固定源/阅读限制；不会将其记录的源码审读窗口记作本代理实读。

**当前必要材料归并状态。** 本册为 `required_materials_consolidated`：选定正文、规范附件及准确接受/复核范围已按直接实读、结构化语义或精确复用归并。身份清单核对不等底层源码全文阅读；历史草稿、候选实现和Office/HTML视觉未核范围仍单列。可复查[逐件阅读与处置](D:/CP6/docs/CP6_开发设计文档_20261010/evidence/platform-engineering-reading.json)及[本组闭包台账](D:/CP6/docs/CP6_开发设计文档_20261010/worklogs/platform-engineering-progress.json)。当前必需规范未读清单为空；具体Owner采用、参数/物理接口、真实运行和本节已有来源缺口不因整理闭包而改变。
