# SPACE-IMPORT-01 · CAD、Excel 导入、匹配与人工审核

整理状态：`core_semantics_consolidated`；当前组合的业务规则已整理，原Owner采用和运行验收保持UNPROVEN/NOT_RUN。

## 1. 目的、操作者和业务 Owner

空间导入者上传/预检/解析，映射配置者维护CAD/Excel→Space设计语义，审核者核候选后应用到固定草稿。Main Space持设计来源，SYS-FILE持文件安全，外部CAD provider能力独立；不导销售需求、不做库存交易或正式WMS库位创建。[原文X:135](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:135) [原文X:139](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:139) [原文X:157](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:157)

## 2. 精确规范与接受组合

本Target采用S4 R2 `15f966e5afe3dc71…`＋X6 R2 `6f8585ca3b85c6aa…`准确完整SHA组合（全值见本组冻结合同），独立UA的Disposition=ACCEPTED_CURRENT_MARKDOWN_STATIC_DETAILED_DESIGN；正文CANDIDATE/STOPPED/待独审是历史，不是当前缺设计。范围：6 SPEC，2 HIT＋4 NO_HIT_PRESERVE；8 AC。NO_HIT_PRESERVE仅对这次公共变更有界保留，不是无业务/免回归。静态接受不授Owner生产采用；全部AC NOT_RUN。[原文A:6](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/78/78c631d107ade9ca__UA-20261008-X6-SPACE-IMPORT-01-R2-MD02.json:6) [原文X:7](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:7) [原文X:13](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:13) [原文R:194](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/c4/c46bf5d8d9ed7e2c__CP6_S4_X6_R2_独立定点复核与累计范围.md:194)

公共冻结、合法后继、源发布和current/Runtime的具体合同统一见 [SPACE-PUB-01](D:/CP6/docs/CP6_开发设计文档_20261010/modules/SPACE-PUB-01.md)，本模块不另建gate或WMS主账。[原文X:29](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:29)

## 3. 数据身份、字段与量纲

File DTO含OriginalName/ContentType/Extension/SizeBytes/Sha256/State/ScanResult/RowVersion；Source固定ParserVersion/MappingProfileVersion/unit/source hash。preflight产sheet/row/field、blocking/warning及后续job；CAD产provider/capability/version、原source hash、几何/语义候选与坐标关系、问题/原attempt。匹配固定source hashes/modelVersion/content revision/profile，结果是来源行↔CAD候选/问题，不凭Code相同证明同一正式位置。[原文X:139](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:139) [原文X:145](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:145) [原文X:151](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:151) [原文X:163](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:163)

人工决定保存准确candidate/版本、接受/修正/拒绝、reason/actor；候选匹配、人工决定、已应用设计三个维度分开。来源解析/映射版本变化产生新产物/review，不能改旧结果或已Frozenlineage。[原文X:153](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:153) [原文X:169](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:169) [原文X:173](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:173)

## 4. 流程、状态和入口写集

上传只建立受控file/source/job；合法归属+hash决定复用，不能同名覆盖；隔离/扫描不通过不解析，限额取真实配置不编全局数值。预检缺字段/单位保issue定位，不能丢失败行凑完整；修订映射形成新前检。[原文X:139](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:139) [原文X:145](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:145)

解析成功产生候选而非发布。匹配与审核先固定来源/版本，再接受/修正/拒绝，实际Apply只写对应可编辑Draft的几何/元数据和历史。草稿同步的“省略权威设计行停用/metadata行移除”保原语义，但不能传成WMS删除或S4 FULL快照自动DISABLE；正式源停止必须独立意图/后继和Owner保护。[原文X:151](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:151) [原文X:165](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:165) [原文X:173](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:173)

审核通过只授权该固定设计应用，不等sourceDecision或WMS approval；修改内容后重新校验/预览，旧决定不能跟最新来源漂移。[原文X:173](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:173) [原文X:175](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:175)

## 5. 接口和依赖合同

保留versions/{id}/cad-sources、excel-sources、ExcelPreflight/CAD准备审核；`/api/space/design/v1/mapping-profiles/cad` 管设计映射；POST versions/{id}/excel-cad-matches和同version/job GET查询候选。请求沿原源/匹配/配置版和content前驱，不臆造新字段/路由。[原文X:139](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:139) [原文X:145](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:145) [原文X:157](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:157) [原文X:163](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:163)

CAD/Excel MappingProfile不是WMS正式S/B/L MappingDecision，两类Owner/决定不合并。应用结果交DES内容版本链、S4仅消费已审核固定设计；CAD worker需自身真实provider/version/许可和外部结果证据。[原文X:151](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:151) [原文X:159](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:159)

## 6. 事务、并发、幂等和恢复

匹配/确认队列与最终Apply同时核原Draft资格、source/hash/content前驱、review/受保护绑定和共同Frozen写门；排队时可写不保证提交时可写。任务完成但版已改/Frozen保artifact历史、CanConfirm=false，禁止改冻结source metadata或场景。人工选择不得偷换已正式映射S→L。[原文X:167](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:167) [原文P:288](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15f966e5afe3dc71__SPACE-PUB-01_完整详细设计_R2_CANDIDATE.md:288)

超时查原upload/source/job或Apply提交结果；Back只关界面不取消服务器job，未知commit不换键重做。review stale/content revision stale/source unavailable/protected binding分原因；恢复新合法review后继，旧理由/before事实不覆盖。[原文X:141](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:141) [原文X:153](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:153) [原文X:169](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:169) [原文X:175](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:175)

## 7. 权限、租户和审计

上传source:upload＋model:edit及可信Site所属；映射维护edit/查看read；人工审核该Site review/edit并核受保护字段/几何/单位。文件hash相同不授跨租户复用，受限候选不是不存在。审计保原文件、解析/映射版、job/attempt、来源行问题、决定与应用命令结果。[原文X:139](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:139) [原文X:157](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:157) [原文X:167](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:167) [原文X:173](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:173)

## 8. 页面与服务端行为

保上传→扫描→预检→解析/映射→Excel匹配→人工审核→设计应用工作流；DesignExcelCadMatchPanel显示来源行/候选/问题并明确“只应用到设计草稿”。失败行、未知单位、受限候选可定位；显示基版/Frozen保护，原结果刷新不变成自动重试或完成。[原文X:135](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:135) [原文X:163](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:163) [原文X:169](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:169) [原文X:175](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:175)

## 9. 开发顺序及固定代码差异

原X6-IMPORT-D01仅在匹配/人工review的queue与commit接准确来源/前驱/统一冻结门和CanConfirm；四项未命中的上传/预检/解析/映射算法保留并按相关改变回归，不整体重建。实际CAD provider与文件安全门独立，不能用源码存在填运行成功。[原文X:369](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:369) [原文X:392](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:392)

### 原41 SPEC范围的本模块兼容边界

`HIT`表示本次发布/采纳合同需要调整；`NO_HIT_PRESERVE`保留原算法与Owner，但凡写入受控版本，仍须服从共同冻结门。这些分类不是实现完成或现场验收。矩阵保留的 `TO_AUTHOR` 为原范围快照，当前接受组合仍依本册第2节UA，不回退为无设计。

|原SPEC尾号/范围|范围结论|开发时保留或整合的边界|
|---|---|---|
|01 上传|NO_HIT_PRESERVE|文件隔离/扫描后来源创建复用；发布契约不改变文件内容、上传用途或库存交易 [矩阵:265](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/2c/2cba0684fb7cbf7e__MATRIX-8TARGET-41SPEC.json:265)|
|02 预检|NO_HIT_PRESERVE|Excel/CAD输入前检和issue保留；本轮未更换解析字段或合法性规则 [矩阵:284](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/2c/2cba0684fb7cbf7e__MATRIX-8TARGET-41SPEC.json:284)|
|03 CAD解析|NO_HIT_PRESERVE|CAD解析/来源/映射版本锚保留；外部转换器可运行性仍UNPROVEN [矩阵:303](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/2c/2cba0684fb7cbf7e__MATRIX-8TARGET-41SPEC.json:303)|
|04 映射配置|NO_HIT_PRESERVE|映射配置影响空间草稿与来源；未命中WMS正式映射Owner定义，禁止合并两类mapping [矩阵:322](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/2c/2cba0684fb7cbf7e__MATRIX-8TARGET-41SPEC.json:322)|
|05 Excel匹配|HIT|匹配人工候选与固定版本需与S4不可变来源/已绑定保护对齐，不允许match覆盖已发布 [矩阵:341](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/2c/2cba0684fb7cbf7e__MATRIX-8TARGET-41SPEC.json:341)|
|06 人工审核|HIT|人工审核决定仅授权设计应用；审核通过不得变成发布或WMS采纳批准 [矩阵:360](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/2c/2cba0684fb7cbf7e__MATRIX-8TARGET-41SPEC.json:360)|

原矩阵的“已有源码/可复用”只承接准确固定源身份及原有界审读，未完成完整97源码审计；新运行、真实Owner采用和S1精确consumer确认均未证明。[原独审状态:1455](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/ff/fff3a9611b515c00__CP6_S4_X6_R2_独立复核证据与41SPEC范围.json:1455) [来源资格:3435](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/fe/fed1fafddca4ed99__SOURCE-QUALIFICATIONS.json:3435)

## 10. 验收场景与证据状态

XI01～08 NOT_RUN：上传重试不写库存且未扫描不解析；缺字段/单位保原源问题；解析原版/provider不可用不造成功；同名不同版profile保溯源不造WMS批准；匹配后冻结拒覆盖；同码异ID不复用L；人工接受不发source/WMS回执；review后换源/版拒Apply保旧决定。R2补后台queued/commit与冻结竞争场景。[原文X:141](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:141) [原文X:147](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:147) [原文X:153](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:153) [原文X:159](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:159) [原文X:169](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:169) [原文X:177](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:177)

## 11. 缺口与退出条件

实际provider/版本/许可、扫描隔离/文件保留、完整原DTO与算法实现、各队列最终commit门仍需证据。缺CAD外部响应保持UNKNOWN/失败；缺正式mapping只限制依赖业务，不重命名设计mapping为批准。当前必要规范材料已闭合，真实provider和writer接线仍未证明。[原文X:380](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:380) [原文X:388](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:388) [原文X:392](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/6f/6f8585ca3b85c6aa__SPACE_七专项兼容完整设计_R2_CANDIDATE.md:392)

## 12. 实际阅读

本轮全文读X6 R2 1～394（本Target专段133～177，共同规则15～52/341～394），S4 R2 1～369、独审1～208、R2细化AC JSON 1～423；本Target UA结构化读接受决定、主文/组合身份和执行边界。固定源码的业务结论按当前规范引用，本册没有新做97份源代码全文认证；更细历史实现/附件实际阅读见 [本组阅读台账](D:/CP6/docs/CP6_开发设计文档_20261010/evidence/platform-engineering-reading.json)。当前为required_materials_consolidated；历史/源码/视觉边界保留。未运行构建、项目脚本、业务测试、迁移或Actions。

本輪補讀 MATRIX 8 Target/41 SPEC 全部字段（结构化）、独审41项判语/4项设计根与资格边界（选段），SOURCE-QUALIFICATIONS 3110–3440的固定源/阅读限制；不会将其记录的源码审读窗口记作本代理实读。

**当前必要材料归并状态。** 本册为 `required_materials_consolidated`：选定正文、规范附件及准确接受/复核范围已按直接实读、结构化语义或精确复用归并。身份清单核对不等底层源码全文阅读；历史草稿、候选实现和Office/HTML视觉未核范围仍单列。可复查[逐件阅读与处置](D:/CP6/docs/CP6_开发设计文档_20261010/evidence/platform-engineering-reading.json)及[本组闭包台账](D:/CP6/docs/CP6_开发设计文档_20261010/worklogs/platform-engineering-progress.json)。当前必需规范未读清单为空；具体Owner采用、参数/物理接口、真实运行和本节已有来源缺口不因整理闭包而改变。
