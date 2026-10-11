# PLAN-POL-01 计划策略开发设计

状态：`required_materials_consolidated`。当前登记24份必需载体的业务语义、136项场景、来源和接受关系已整理；HTML镜像、PB01结构/历史进度、原预览按限定核对。完整原生视觉、真实参数、物理实现和运行验收仍独立保留。

## 1. 业务目的与责任

计划员需要解释为什么某产品在特定Site采用MAKE/BUY、何时启动、补多少以及怎样拆批。POL持有策略内容、解析与应用事实；工程/计量/物流拥有技术参数、单位与日历事实，审批Owner拥有审批事实。策略选MAKE不等MES准入，选BUY不等合格供方或采购批准。调拨/委外范围保留，但缺路线和实际能力只能列候选，不能把A的库存直接当B的当日供给。[R01目的与规则](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/95/9501a166e513e743__CP6_Planning_策略与供给_R01_v0.1_REVIEW_2026-09-28.md:50)

五原SPEC：Make/Buy、提前期、安全库存、批量、适用地点和版本。POL不编BOM净算算法、不发布MRP、不自动转PR/WO、不授库存或制造资格；[SUP](D:/CP6/docs/CP6_开发设计文档_20261010/modules/PLAN-SUP-01.md)负责供给解释。

## 2. 当前组合与接受边界

有效组合为PS-R01（`9501a166e513e743`）业务规则、PS-R02（`98c0cbdc6614a1ee`）页级操作、PS-R03（`e1aed8dc46c13a8d`）八合同十门，以及必读REVIEW-AND-READ-SCOPE（`17f532540f6e7d01`）C01–C05。PS-R04接受叠加记录用户“认可，请继续推进”，准确指向payload commit `4c4f69c8c0b1036fc541fd068ecbb2b64817dea4`，认可上述功能文本；R04载体仍REVIEW，7项Owner门关闭0项。原REVIEW历史不回写，100分不等实现完成。[接受范围](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/da/daa4da427e469056__PROGRESS-ACCEPTANCE.json:1)

本册的48项联合字段、逻辑状态/操作/合同不是冻结物理列、路由或DTO。ATP R11虽另有精确wire，POL与其端口需明确采用，不能用本册“已认可”直接宣布ATP RequiredFenced已关闭。准确全SHA/所有成员见[输入索引](D:/CP6/docs/CP6_开发设计文档_20261010/indexes/required-inputs.json)。

## 3. 对象、身份和配置

|对象/原字段|开发语义|
|---|---|
|Context FLD001–004|可信Tenant/Site、当前ActorAction、写意图OperationKey、同Owner ExpectedVersion；查询不强造业务对象|
|Policy FLD005–010|PolicySetId/Version、产品技术/用途/来源/活动Scope、日期节点/生效区间、继承优先规则、MAKE/BUY与真实候选来源|
|Policy FLD011–016|分段LeadTime及串并关系、版本化Calendar/Cutoff、FLOOR/DEMAND/NONE、按日目标与专用Demand来源、LotRule及语义版、MOQ/Multiple/Max/基点/拆批|
|Policy FLD017–018|Firm资格/保护视图和准确批准/影响证据；Firm不是执行事实|
|Draft/Submission/Application|DraftRef、BasedOnPolicyRef、ContentProof、EditConcurrencyRef、SubmissionRef、ApprovalOutcomeRef、ApplicationIntentRef、EffectiveScope、ChangeImpactManifest|
|Resolution|RequestRef、InputFingerprint、CandidateSet/SelectionEvidence、逐成员PolicyMemberBindings、DerivationSteps/CalculationBasis、UnresolvedMembers、独立ApplicabilityAssessment|

技术EditConcurrencyRef防覆盖，业务BasedOnPolicyRef表达前驱，不能相互代替；不可把旧RowVersion变成已批准业务版。内容、审批、应用、生效范围分别留身份。每个解析成员都能追到Owner/版本/适用范围/继承依据；两套互斥配置不能拼成第三套。空scope不是全域，未知枚举不退L4L，null安全模式不是NONE。[字段原表](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/95/9501a166e513e743__CP6_Planning_策略与供给_R01_v0.1_REVIEW_2026-09-28.md:184)；[页级成员](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/98/98c0cbdc6614a1ee__PS-R02.md:143)

数量采用精确十进制、明确UOM/换算方向/精度/版本，原量保留。字段物理长度、数据库精度、上限和真实日历未由这些材料核定，不能将设计夹具填生产默认。

## 4. 正常流程与算法

|步骤/PS-R02 OP|条件、写集与结果|
|---|---|
|001保存草稿/后继|核编辑权、业务前驱、原key；记录差分和缺项，仅保存Draft与审计，有效v1继续生效|
|002预检|核准确内容、单位/时间/批量/双安全模式/重叠范围；返回字段问题与依据，不建审批|
|003送审|固定内容、必要预检满足、有据审批路线；先保存SubmissionIntent，再交审批Owner，回真实受理或未决|
|004应用批准|当前批准覆盖准确内容，再核有效前驱/范围/相容性/影响/生效节点；本域保存ApplicationResult、有效范围、持久影响通知|
|005停止新用|准确停止决定与范围/节点；保存停止后继和影响，不删除历史、不自动回退未获准v1，不取消Firm/PO/WO|
|006解析/模拟|精确上下文找候选，按有权优先/继承逐成员选择和例算，回MATCH/MISSING/CONFLICT/UNDETERMINED及来源，无经济写|

送审后修改需新后继，复制草稿只带可编辑业务值和候选证据，不复制审批、应用、原key或有效身份。Approved与应用拒绝可同时真实存在；时间到点不伪造应用回执。MATCH只对这次输入成立，输入变更旧结果保留但STALE。[操作原表](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/98/98c0cbdc6614a1ee__PS-R02.md:227)；[CON001](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/e1/e1aed8dc46c13a8d__PS-R03.md:64)

MAKE/BUY按Tenant/Site/技术/用途/来源客户约束/活动/时间节点匹配。两个合法方式没有有权选择规则则待决定，不按枚举、价格、最近使用或上传时间暗选。人工选择只有政策允许且记录权限/理由才能成为依据，不能越过硬质量/技术条件。

日期分需用、库存可用、实到、发出/开工；每一段保留Owner/持续量/工作日历/时区/cutoff/串并顺序。设计D0–D8全工作日、D8需用、检验1日+供方3日顺序，得到D7实到、D4启动；不是实际地区日历。缺日历不能用自然日或提前期0；启动已逾期展示风险，不改客户承诺让指标消失。[日期](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/95/9501a166e513e743__CP6_Planning_策略与供给_R01_v0.1_REVIEW_2026-09-28.md:79)

安全量同一Item/技术/Site/UOM/有效Run范围只选FLOOR、专用DEMAND或有权NONE。FLOOR是时点余额目标：补前余额=B+S−G，净不足=max(0,F−补前余额)。Gross100/供给100/目标20→补20；D1补后余20、D2无消耗目标仍20，不再补20。目标已经作为专用Demand加入Gross则不能再加FLOOR；未知供给不填S=0。旧Gross1000/Supply300/SafetyStock200算净500是旧历史，新获准FLOOR范围净900，不能回写旧Run或批量重释旧字段。[安全模式](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/95/9501a166e513e743__CP6_Planning_策略与供给_R01_v0.1_REVIEW_2026-09-28.md:87)；[旧新例](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/7f/7f851186e14ce4a9__ACCEPTANCE.md:59)

批量L4L遵守准确精度；MOQ_MULTIPLE先满足不足及MOQ，再取有据基点的合法倍数，并满足每批Max及是否可拆。原0基点例：不足25/MOQ20/倍10/Max50→30；不足75且可拆、大合法批优先→50+30=80，超额5是供给余量，不增客户Demand。MOQ25/倍10/Max25无解，返回CONFLICT，不能松约束取25或30。损耗/良率/成本优化不暗藏批量函数。[批量](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/95/9501a166e513e743__CP6_Planning_策略与供给_R01_v0.1_REVIEW_2026-09-28.md:95)

## 5. Owner接口与事件

PS-R03-CON001是逻辑合同：PolicyContext、PolicyMemberBindings、SubmissionAndApplication、PolicyImpactSet、ResolutionOutcome；POL提供内容/应用/解析，审批、工程、UOM、物流分别提供自己的证据。不得自行签专业许可。内容变更只标准确依赖范围、通知重评；不相关备注更正不要求全系统重算，Firm/真实单据由原Owner后继处理。[合同成员](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/e1/e1aed8dc46c13a8d__PS-R03.md:68)

向SUP/MRP输出准确策略成员与当前适用性，供给侧Ready不代MRP发布。ATP的RequiredFenced政策事件、identity gate和canonical wire见[ATP设计](D:/CP6/docs/CP6_开发设计文档_20261010/modules/PLAN-ATP-01.md)，本册逻辑解析尚不能直接充当该物理端口；当前OA审批运输也需按真实被采用接口落实。

## 6. 并发、事务与恢复

共用C0–C5：当前Scope/动作/字段权→原key/内容→同Owner前驱/控制→准确输入/证据→本Owner一致边界保存产物、结果、必需交接→回真实身份、应用情况、未知与可用恢复。历史同键同内容回原结果并附当前效力，异内容冲突；实质补输入是后继。跨Owner分别提交，不因共用流程假设跨库事务。[共同门](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/98/98c0cbdc6614a1ee__PS-R02.md:64)

送审丢响应查SubmissionIntent，应用未知查ApplicationIntent，不能再建第二审批/有效版。应用成功而通知失败仅补通知。停止未知不能自动认不存在，也不自动恢复旧版。ExpectedEffectiveSet/ProtectionSet前驱须在MRP权威发布一致边界比较并生效；一次GET Ready不能跨越后续竞争，外部适用锚点/前沿/回传保证缺失则不启用对应发布。[C02强制澄清](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/17f532540f6e7d01__REVIEW-AND-READ-SCOPE.md:37)

## 7. 权限与敏感数据

PolicyEditor、Submitter、Applier、Planner、Reader、Exporter及CaseCloser分权，普通编辑不能停止策略或豁免专业条件。Tenant来自可信会话，Site选择只缩小授权范围；服务端重新核对象/字段/动作。父对象可见不意味子项全可见，局部限制不填0且不以总计反推出隐匿量。来源Owner由可信接入证明，不靠消息自称。[权限](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/98/98c0cbdc6614a1ee__PS-R02.md:56)

## 8. 页面与校验

POL01：有效/历史树、编辑差分、缺项/冲突/影响、审批和应用分栏；默认当前有效，编辑v2不冒有效。POL02：精确上下文输入、候选/排除、逐字段来源、日期推导、批量和目标结转。改已有Demand的模拟输入须显示“模拟副本”，不回写需求。每公式带单位、版本、来源、完整性。缺真实参数可存草稿，不能应用。[两页细则](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/98/98c0cbdc6614a1ee__PS-R02.md:141)

共用REC01展示变更/当前适用性、存量分类和解释。列表唯一身份稳定排序，跨页没有一致查询协议须明确非一致全量。导出另核列/范围/单位/版本/水位、安全格式和限额；文本公式按安全格式呈现，业务原值保留。错误沿原BR/ERR001–024定位字段与恢复Owner：策略缺失/冲突、来源方式、日历、双安全模式、结转、精度、批量无解、版本和恢复分别提示，不合成“请重试”。[错误映射](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/98/98c0cbdc6614a1ee__PS-R02.md:252)

## 9. 实施顺序与旧代码

先落实版本/编辑前驱/批准应用分账和参数Owner引用，再做精确解析/日期/安全模式/批量，接审批与影响恢复，之后页级字段权/模拟/解释，最后同SUP/MRP/ATP做被采用合同验证。实际参数未齐不妨碍非依赖功能设计，但依赖动作不能用示例兜底。

旧实现资料固定源码`157630594e3371fe181955d2f6227ff3b6962c84`：ItemPolicyView、Upsert、LowLevelCode/用量、Tenant/RowVersion/授权基础是复用候选；Upsert不直接等新版保存，旧SafetyStock减项语义不改。此轮未检查当前源码，也未证明部署采用同一包；同名Package版本不能代构建SHA/解析来源。[旧能力与七门](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/98/98c0cbdc6614a1ee__PS-R02.md:285)

## 10. 验收场景

联合场景R01 44、R02 52、R03 40全NOT_RUN，原号保留，不合称唯一功能数或运行次数。POL重点：多MAKE/BUY无规则、草稿不污染有效版、同级冲突不选最新、OA批准后前驱改变拒应用、停止v2不自动回退v1、缺日历/精度阻依赖、FLOOR跨桶不累加、DEMAND不双加、MOQ无解、并发保存/应用、丢响应原键恢复、Site越权和敏感导出。[44组](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/1a/1a392462da1246cc__PS_R01_44组验收场景_NOT_RUN.md:1)；[52组](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/7f/7f851186e14ce4a9__ACCEPTANCE.md:1)；[40组](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/2b/2b6b4ac26e32e3a5__ACCEPTANCE.md:1)

## 11. 保留项与开工门

七门保留：旧安全量真实含义；STUB/真实后继；旧扫描与正式Demand切换/重叠；质量/用途/RSV归属；日历/UOM/精度/批量/优先级真实实例；产出/重复更正/范围外承诺；Package源码/构建/部署身份。正式MSBBPA010两册载体、全量原生视觉、物理DTO/DDL/事务、迁移及运行证明未由本接受关闭。ATP精确政策事件适配也不能凭逻辑相似自动启用。

## 12. 来源和阅读覆盖

联合R01 721行、R02 307行、R03 294行、必读C01–05、136场景、接受及其他当前必需输入已按内容闭合。24份载体分别记全文、结构化语义、精确镜像复用、元数据核验或原预览实看；不是全部字节/像素人工全读。详见[开发定位与附件读法](../indexes/POL-SUP-trace.md)及[root阅读台账](../evidence/root-planning-reading.json)。原文中的后续命令为历史内容，本次未执行。
