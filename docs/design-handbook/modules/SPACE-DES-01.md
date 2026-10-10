# SPACE-DES-01 · 空间场景、设计版本、模板与来源

整理状态：`core_semantics_consolidated`；当前组合的业务规则已整理，原Owner采用和运行验收保持UNPROVEN/NOT_RUN。

## 1. 目的、操作者和业务 Owner

Main Space设计者维护版本化场景、模板、底图和来源资产；设计版本不是PLM工程BOM版本。模板资产维护者与当前模型编辑者按原权限操作，发布者源封存和WMS采纳分别独立。[原文X:95](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:95) [原文X:107](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:107)

## 2. 精确规范与接受组合

本Target采用S4 R2 `15f966e5afe3dc71…`＋X6 R2 `6f8585ca3b85c6aa…`准确完整SHA组合（全值见本组冻结合同），独立UA的Disposition=ACCEPTED_CURRENT_MARKDOWN_STATIC_DETAILED_DESIGN；正文CANDIDATE/STOPPED/待独审是历史，不是当前缺设计。范围：5 SPEC，3 HIT＋2 NO_HIT_PRESERVE；9 AC。NO_HIT_PRESERVE仅对这次公共变更有界保留，不是无业务/免回归。静态接受不授Owner生产采用；全部AC NOT_RUN。[原文A:6](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/93/93ffe88e908d9c40__UA-20261008-X6-SPACE-DES-01-R2-MD02.json:6) [原文X:7](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:7) [原文X:13](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:13) [原文R:194](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/c4/c46bf5d8d9ed7e2c__CP6_S4_X6_R2_独立定点复核与累计范围.md:194)

公共冻结、合法后继、源发布和current/Runtime的具体合同统一见 [SPACE-PUB-01](D:/CP6/docs/CP6_开发设计文档_20261010/modules/SPACE-PUB-01.md)，本模块不另建gate或WMS主账。[原文X:29](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:29)

## 3. 数据身份、字段与量纲

Model/Version保VersionNo/Name/BasedOnVersionId/ContentRevision/ContentHash/ValidatedHash/PublishedAt/RowVersion/创建来源，floor logical ID、租约、commandBatch形成编辑上下文。Source保SourceId/VersionId/SourceType/FileId/DisplayName/Sha256/ParserVersion/MappingProfileVersion/Unit/Scale/State。模板准确SourceTemplateId/Version/ContentHash，底图保page/pixel尺寸、两校准点/验证点、mm-per-pixel/offset/rotation/error threshold、history schema/undo-redo hash。[原文X:99](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:99) [原文X:107](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:107) [原文X:113](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:113) [原文X:121](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:121) [原文X:127](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:127)

新增FrozenBinding/writeGate、source/current两指针和Promotion是S4同一合同；冻结源、当前编辑draft、已晋升current三者不得合一。FrozenSourceVersion/SourceStopSuccessor是拟增CreationSource判别值，通过新版DTO专用入口暴露，不伪成旧PublishedVersion或Blank。[原文X:31](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:31) [原文X:37](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:37)

## 4. 流程、状态和入口写集

旧状态 Initializing→Draft→Validating→Ready→Publishing→Published→Superseded，及Failed/Abandoned/ReconciliationRequired保原义。原允许Draft/Ready编辑的TouchContent使修改版回Draft并使验证hash失效；Frozen/Publishing/Published不可直接写。封存只Production Ready且同hash验证；旧Status仍Ready但永久Frozen，封存不MarkPublished、不递增内容，不改current。[原文X:99](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:99) [原文X:113](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:113) [原文X:35](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:35)

专用successor-drafts固定原FrozenBinding/content/source frontier/model draft-slot，生成新Initializing→Draft；已有ActiveDraft冲突不覆盖，不继承源批准/WMS许可。UNKNOWN原效果可隔离克隆/编辑但不交付/晋升。独立资产模板维护和文件解析算法保留，写入该版本仍须共同门；模板找不到原版不拿latest代替。[原文X:35](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:35) [原文X:109](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:109)

新profile仅CurrentPublishedFinalizer核全目标WMS APPLIED＋同版Runtime真实全范围事实与指针CAS原子晋升；普通兼容投影不改Domain，不调用旧writer；原current在成功前继续供读，无current则不可用。[原文X:41](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:41) [原文X:45](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:45)

## 5. 接口和依赖合同

保留 `/api/space/design/v1/versions/{id}/floors/.../scene` 与elements/layout命令、模板create/preview/instantiate/apply-floor、underlay/source关联和校准/undo/redo入口。请求保floor/content revision、client、lease、commandBatch；模板输入Name/TemplateId/TemplateVersionId/TemplateProposalHash。[原文X:99](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:99) [原文X:107](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:107) [原文X:121](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:121)

新只读GET版本compatibility与专用Frozen successor入口见发布册；旧 create-mode PublishedVersion只接受真Published，旧客户端遇新来源只读不降级Blank。source lineage固定原解析/映射版，不以显示名取最新。[原文X:37](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:37) [原文X:127](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:127) [原文X:353](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:353)

## 6. 事务、并发、幂等和恢复

编辑与封存须在同一版本写门事务串行化并比较准确前驱；仅封存瞬间CAS不足，永久FrozenBinding拒后来Ready写。租约重获不恢复旧命令资格；失败保输入供对比而非自动套新revision。Undo/redo只是设计历史，不能撤回WMS已应用事实。[原文X:101](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:101) [原文X:103](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:103)

后台解析可保存隔离artifact但不能回写冻结source metadata/场景；未知在途写先CONTENT_MUTATION_UNRESOLVED。历史命令已提交只按原键读，不能被后来冻结改为失败；相应新内容必须新合法后继。[原文X:33](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:33) [原文P:287](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15f966e5afe3dc71__SPACE-PUB-01_完整详细设计_R2_CANDIDATE.md:287)

## 7. 权限、租户和审计

模板查看model:read、维护/实例化model:edit；底图显示read，操作source:upload及edit，并先通过合法文件隔离/扫描。租户模板与系统模板所有权保原规则，不因新发布扩大权。来源受限RESTRICTED，不能空文件替代；关联移除与物理文件保留分别授权。[原文X:107](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:107) [原文X:121](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:121) [原文X:129](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:129)

## 8. 页面与服务端行为

原场景、模板、底图坐标/校准/补偿算法和页面保留；版本页并列Frozen源、编辑draft、Published/current及WMS/Runtime状态。底图错误保原页码比例/history hash；来源页保解析job/artifact/issue/CAD preparation/generation/underlay/import audit，并显示发布引用阻挡破坏性移除的原因。[原文X:50](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:50) [原文X:119](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:119) [原文X:127](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:127)

## 9. 开发顺序及固定代码差异

原X6-DES-D01先完善同一冻结写门/后台最终commit覆盖，再合法Frozen后继与新来源判别，接专用Finalizer及current读者矩阵、来源保全。模板/底图资产算法不重建；固定代码报告的旧状态/注册证据不证明新gate已实现。[原文X:368](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:368) [原文P:293](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15f966e5afe3dc71__SPACE-PUB-01_完整详细设计_R2_CANDIDATE.md:293)

### 原41 SPEC范围的本模块兼容边界

`HIT`表示本次发布/采纳合同需要调整；`NO_HIT_PRESERVE`保留原算法与Owner，但凡写入受控版本，仍须服从共同冻结门。这些分类不是实现完成或现场验收。矩阵保留的 `TO_AUTHOR` 为原范围快照，当前接受组合仍依本册第2节UA，不回退为无设计。

|原SPEC尾号/范围|范围结论|开发时保留或整合的边界|
|---|---|---|
|01 场景编辑|HIT|租约/内容修订与只改草稿保留；冻结发布快照后不能让编辑覆写来源 [矩阵:139](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/2c/2cba0684fb7cbf7e__MATRIX-8TARGET-41SPEC.json:139)|
|02 模板|NO_HIT_PRESERVE|系统/租户模板版本和创建来源是设计资产；S4不改模板语义、配方或模板事实Owner [矩阵:159](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/2c/2cba0684fb7cbf7e__MATRIX-8TARGET-41SPEC.json:159)|
|03 设计版本|HIT|Draft/Ready/Publishing/Published/Superseded与Purpose保留；发布/WMS采纳/Runtime三个结果新增分栏不改旧enum [矩阵:177](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/2c/2cba0684fb7cbf7e__MATRIX-8TARGET-41SPEC.json:177)|
|04 底图|NO_HIT_PRESERVE|底图校准、Undo/Redo、HistorySha256属于设计几何；本轮不改坐标/文件保留算法 [矩阵:196](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/2c/2cba0684fb7cbf7e__MATRIX-8TARGET-41SPEC.json:196)|
|05 来源历史|HIT|来源Sha256/ParserVersion/MappingProfileVersion保留；发布引用所需来源不得删除或用最新取代 [矩阵:214](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/2c/2cba0684fb7cbf7e__MATRIX-8TARGET-41SPEC.json:214)|

原矩阵的“已有源码/可复用”只承接准确固定源身份及原有界审读，未完成完整97源码审计；新运行、真实Owner采用和S1精确consumer确认均未证明。[原独审状态:1455](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/ff/fff3a9611b515c00__CP6_S4_X6_R2_独立复核证据与41SPEC范围.json:1455) [来源资格:3435](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/fe/fed1fafddca4ed99__SOURCE-QUALIFICATIONS.json:3435)

## 10. 验收场景与证据状态

XD01～09 NOT_RUN：编辑/封存不混快照；undo不取消WMS；模板原字段/来源不继承采纳；旧Published不迁为WMS成功；Scenario任一发布入口阻断；历史republish新身份保旧史；校准undo/redo保版本锚；重解析不改旧lineage；发布引用源不破坏删除。另核Ready＋Frozen所有写点拒绝、旧current/无current读行为、UNKNOWN后继隔离与promotion竞争。[原文X:103](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:103) [原文X:109](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:109) [原文X:117](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:117) [原文X:123](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:123) [原文X:131](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:131)

## 11. 缺口与退出条件

原生工作簿/历史完整算法的必要对照按台账补读；实际FrozenBinding全入口、源revision、专用clone/domain finalizer、Runtime全范围、writer切换尚UNPROVEN。退出条件为准确Owner合同和真实实现证据；设计已选定不能报成选项未决，也不能拿旧MarkPublished直接替代受限转换。[原文X:380](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:380) [原文P:321](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15f966e5afe3dc71__SPACE-PUB-01_完整详细设计_R2_CANDIDATE.md:321)

## 12. 实际阅读

本轮全文读X6 R2 1～394（本Target专段93～131，共同规则15～52/341～394），S4 R2 1～369、独审1～208、R2细化AC JSON 1～423；本Target UA结构化读接受决定、主文/组合身份和执行边界。固定源码的业务结论按当前规范引用，本册没有新做97份源代码全文认证；更细历史实现/附件实际阅读见 [本组阅读台账](D:/CP6/docs/CP6_开发设计文档_20261010/evidence/platform-engineering-reading.json)。当前为required_materials_consolidated；历史/源码/视觉边界保留。未运行构建、项目脚本、业务测试、迁移或Actions。

本輪補讀 MATRIX 8 Target/41 SPEC 全部字段（结构化）、独审41项判语/4项设计根与资格边界（选段），SOURCE-QUALIFICATIONS 3110–3440的固定源/阅读限制；不会将其记录的源码审读窗口记作本代理实读。

**当前必要材料归并状态。** 本册为 `required_materials_consolidated`：选定正文、规范附件及准确接受/复核范围已按直接实读、结构化语义或精确复用归并。身份清单核对不等底层源码全文阅读；历史草稿、候选实现和Office/HTML视觉未核范围仍单列。可复查[逐件阅读与处置](D:/CP6/docs/CP6_开发设计文档_20261010/evidence/platform-engineering-reading.json)及[本组闭包台账](D:/CP6/docs/CP6_开发设计文档_20261010/worklogs/platform-engineering-progress.json)。当前必需规范未读清单为空；具体Owner采用、参数/物理接口、真实运行和本节已有来源缺口不因整理闭包而改变。
