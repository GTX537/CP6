# SYS-NUM-01 · 业务编号与独立代码预览

整理状态：`core_semantics_consolidated`。精确规则/候选字段与现实现区分；AC和运行资格维持NOT_RUN/UNPROVEN。

## 1. 业务目的、操作者与边界

业务Owner在创建对象时内部采番，规则管理员使用pub-seq:add/edit/delete，普通获准用户只预览；CodeGen元数据管理员使用pub-codegen:save/view。采番与代码预览是两个独立能力，不共享权限/状态。编号只是业务显示码，不代替不可变ID、tenant、operation幂等键或messageId；保留原编号Owner，不强制ERP/WMS/CRM和Common/DocNumber迁入SeqService。[原文B:9](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/bf/bf7ac9ccfb3a59bb__S5_SYS-NUM-01_完整设计候选_v0.1.md:9) [原文B:11](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/bf/bf7ac9ccfb3a59bb__S5_SYS-NUM-01_完整设计候选_v0.1.md:11) [原文B:45](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/bf/bf7ac9ccfb3a59bb__S5_SYS-NUM-01_完整设计候选_v0.1.md:45)

## 2. 当前规范与静态接受

正文v0.1，SHA256 `bf7ac9ccfb3a59bb6050aa9cc6fd5f8e531e77977e51df6255bccb6ee98eac13`，71行，4原SPEC/16AC/11任务；纳入S5 v0.2混合十稿，属于八册原字节继承项。复审仅IAM/CLIENT认证双根，未把本册改v0.2。[原文A:146](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/a7/a7c83b1f0e790f6c__UA-20261008-S5-SYS-NUM-01-STATIC-MD02.json:146) [原文R:14](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/e3/e3522cf5a9c30a6a__S5_v0.2_认证双根独立复审与最小问题报告_20261008.txt:14) [原文B:71](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/bf/bf7ac9ccfb3a59bb__S5_SYS-NUM-01_完整设计候选_v0.1.md:71)

delegate接受非亲签，原02:18:29Z逐字决定未重建，04:09:04Z恢复确认独立记录；作者STOPPED历史保留，16AC NOT_RUN、全部实施任务PLANNED、Owner采用UNPROVEN且实施false。[原文A:8](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/a7/a7c83b1f0e790f6c__UA-20261008-S5-SYS-NUM-01-STATIC-MD02.json:8) [原文A:299](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/a7/a7c83b1f0e790f6c__UA-20261008-S5-SYS-NUM-01-STATIC-MD02.json:299)

## 3. 数据身份、字段、周期

|对象|字段/范围|约束|
|---|---|---|
|Pub_DocSequence|BizKey/Prefix/DateFormat/SeqLength/ResetCycle/CurrentPeriod/CurrentValue及时间|现即时SaveChanges、可跳号；ResetCycle1日/2月/3年/其他无周期。[原文B:9](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/bf/bf7ac9ccfb3a59bb__S5_SYS-NUM-01_完整设计候选_v0.1.md:9)|
|AllocationRequest/Receipt候选|operationRef/scope/ruleVersion/periodKey/allocatedNumber/allocationState/allocatedAtUtc/ownerReceiptRef|输入bizKey、真实tenant/编号scope、businessOperationRef、业务时间/时区政策、expectedRuleVersion由Owner产生，客户端不填CurrentValue。[原文B:15](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/bf/bf7ac9ccfb3a59bb__S5_SYS-NUM-01_完整设计候选_v0.1.md:15) [原文B:21](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/bf/bf7ac9ccfb3a59bb__S5_SYS-NUM-01_完整设计候选_v0.1.md:21)|
|RuleDraft候选|expectedRuleVersion/effectivePolicy/reason/operationId +Prefix/DateFormat/SeqLength/ResetCycle|原PUT不改BizKey/CurrentValue/CurrentPeriod；已分配号不改写。[原文B:31](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/bf/bf7ac9ccfb3a59bb__S5_SYS-NUM-01_完整设计候选_v0.1.md:31)|
|PreviewView候选|sample/isReservation=false/evaluatedAt/timeZone/wouldReset/ruleVersion/periodKey/warnings|格式示例不占号，无论已存或草稿预览都不写CurrentValue。[原文B:33](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/bf/bf7ac9ccfb3a59bb__S5_SYS-NUM-01_完整设计候选_v0.1.md:33)|
|编号scope登记|sourceOwner/businessType或bizKey/tenantOrGlobalScope/受控warehouse或company/resetCycle/timeZonePolicy/periodAnchor/ruleVersion/uniqueConstraintDomain/originalAllocatorEntry|真实Owner登记，同显示串不合并全局与tenant对象。[原文B:43](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/bf/bf7ac9ccfb3a59bb__S5_SYS-NUM-01_完整设计候选_v0.1.md:43)|
|NumberScopeView候选|scope/rule/currentPeriod/compatibility/cutoverState|仅获准管理域；普通用户只见自己的业务号。[原文B:47](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/bf/bf7ac9ccfb3a59bb__S5_SYS-NUM-01_完整设计候选_v0.1.md:47)|
|CodeGen元数据候选|metadataId/version/entityName/namespace/displayName/columns定义与顺序/templateRef+version/targetStack/ownerScope|完整GenTable/GenColumn源未读，其他字段不冒已有。[原文B:59](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/bf/bf7ac9ccfb3a59bb__S5_SYS-NUM-01_完整设计候选_v0.1.md:59)|
|PreviewResult候选|previewId/inputDigest/templateRef/outputs(filenameHint,language,text,warnings)/isPersistedInput|文件名是建议不是写磁盘路径，文本未应用。[原文B:63](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/bf/bf7ac9ccfb3a59bb__S5_SYS-NUM-01_完整设计候选_v0.1.md:63)|

周期按Owner固定服务器瞬时与scope时区，非设备钟；tenant时区变化不溯及已分配period。旧DateTime.Now逐调用者定义保留/迁移，不直接改称已有时区正确性。[原文B:47](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/bf/bf7ac9ccfb3a59bb__S5_SYS-NUM-01_完整设计候选_v0.1.md:47)

## 4. 采番、规则变更和预览写集

采番：先查业务原operation分配→取获准rule/version→在编号Owner真实互斥/CAS内决定period+next→持久allocation receipt和rule推进→返回号→业务保存关联allocation。现自包含提交保可跳号；业务失败留ABANDONED/保留，不回收给别人。与业务不同事务就明确ALLOCATED而业务UNKNOWN，不假同事务。[原文B:17](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/bf/bf7ac9ccfb3a59bb__S5_SYS-NUM-01_完整设计候选_v0.1.md:17)

候选每scope+bizKey+period跨实例唯一串行化/原子CAS，用Owner数据库能力并证明唯一约束/失败重试，不能只进程锁/秒级拼接。达到容量或格式非法返回SEQUENCE_EXHAUSTED/RULE_INVALID，不截断、不回绕0、不换scope。[原文B:19](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/bf/bf7ac9ccfb3a59bb__S5_SYS-NUM-01_完整设计候选_v0.1.md:19)

规则保存校长度、格式、容量、reset、历史唯一性和调用者影响，CAS生效只作用未来未分配号；前缀/日期变化若撞历史则阻断或由Owner明确新范围，不暗重置。预览纯读；旧preview的CurrentValue+1不按新period重置，跨周期可能非下一真实号，不能做正式号。[原文B:29](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/bf/bf7ac9ccfb3a59bb__S5_SYS-NUM-01_完整设计候选_v0.1.md:29) [原文B:31](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/bf/bf7ac9ccfb3a59bb__S5_SYS-NUM-01_完整设计候选_v0.1.md:31) [原文B:33](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/bf/bf7ac9ccfb3a59bb__S5_SYS-NUM-01_完整设计候选_v0.1.md:33)

代码预览：编辑元数据→纯算输出文本/建议名/模板版本/inputDigest→用户审阅→明确保存才CAS元数据。即时预览不偷偷save，持久metadata preview与即时preview区别；不写仓库/DB、不安装依赖/执行生成物。[原文B:57](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/bf/bf7ac9ccfb3a59bb__S5_SYS-NUM-01_完整设计候选_v0.1.md:57) [原文B:61](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/bf/bf7ac9ccfb3a59bb__S5_SYS-NUM-01_完整设计候选_v0.1.md:61)

## 5. API与调用者合同

NextAsync是内部service，没有本轮已证公开Next控制器；未配置E-PUB-051。现GET `/api/pub/seq/preview/{bizKey}`纯格式预览，POST/PUT `/api/pub/seq`管理规则；业务调用者须给真实operation/scope和原allocation查询，不暴露随意领号接口。[原文B:21](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/bf/bf7ac9ccfb3a59bb__S5_SYS-NUM-01_完整设计候选_v0.1.md:21) [原文B:29](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/bf/bf7ac9ccfb3a59bb__S5_SYS-NUM-01_完整设计候选_v0.1.md:29) [原文B:31](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/bf/bf7ac9ccfb3a59bb__S5_SYS-NUM-01_完整设计候选_v0.1.md:31)

CodeGen现 `/api/pub/codegen`：GET tables；POST save按EntityName upsert table并整体替换columns，需pub-codegen:save；GET `/{id}/preview`读持久metadata再Generate；POST `/preview`需pub-codegen:view接GenSaveReq(Table,Columns)即时算不持久。原GET preview无额外view特性是固定事实，不冒新权限已统一；候选两入口同scope/实际能力检查。[原文B:57](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/bf/bf7ac9ccfb3a59bb__S5_SYS-NUM-01_完整设计候选_v0.1.md:57) [原文B:59](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/bf/bf7ac9ccfb3a59bb__S5_SYS-NUM-01_完整设计候选_v0.1.md:59)

每调用者登记KEEP_EXISTING或ADAPTER_REQUIRED及理由/原allocatorEntry/唯一域/未完operation归属。公共框架不得越界给所有业务重编号或虚构共用数据库事务。[原文B:43](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/bf/bf7ac9ccfb3a59bb__S5_SYS-NUM-01_完整设计候选_v0.1.md:43) [原文B:45](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/bf/bf7ac9ccfb3a59bb__S5_SYS-NUM-01_完整设计候选_v0.1.md:45)

## 6. 幂等、CAS与灾后恢复

同业务operation恢复复用原allocation，不再Next；同键异义不能复用。号码一旦展示不能重新分配给另一对象。保存unknown查业务原operation，规则412重读留草稿；preview可同inputDigest纯重算，metadata保存未知查原版本。[原文B:17](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/bf/bf7ac9ccfb3a59bb__S5_SYS-NUM-01_完整设计候选_v0.1.md:17) [原文B:21](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/bf/bf7ac9ccfb3a59bb__S5_SYS-NUM-01_完整设计候选_v0.1.md:21) [原文B:35](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/bf/bf7ac9ccfb3a59bb__S5_SYS-NUM-01_完整设计候选_v0.1.md:35) [原文B:63](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/bf/bf7ac9ccfb3a59bb__S5_SYS-NUM-01_完整设计候选_v0.1.md:63)

切换保证同域唯一allocator writer，旧新路由及存量未决意图归属明确；unknown查原allocationOwner。counter丢失/回退不能用max(现有业务号)续接，因为跳号/已外露未落业务号可能存在；必须核完整allocation/外露清单+Owner批准，未证关闭新采番，历史仍读。[原文B:45](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/bf/bf7ac9ccfb3a59bb__S5_SYS-NUM-01_完整设计候选_v0.1.md:45) [原文B:49](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/bf/bf7ac9ccfb3a59bb__S5_SYS-NUM-01_完整设计候选_v0.1.md:49)

## 7. 权限、租户和安全边界

scope不从用户名/浏览器tenant猜；全局与租户域分别登记，管理视图不出别tenant用量。客户端不能指定计数器或领已用号。CodeGen save/view与seq管理分开，禁止任意路径/模板脚本；字段类型/重复列/保留字/模板白名单校验，未知模板未就绪，不临时下载执行。[原文B:15](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/bf/bf7ac9ccfb3a59bb__S5_SYS-NUM-01_完整设计候选_v0.1.md:15) [原文B:43](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/bf/bf7ac9ccfb3a59bb__S5_SYS-NUM-01_完整设计候选_v0.1.md:43) [原文B:47](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/bf/bf7ac9ccfb3a59bb__S5_SYS-NUM-01_完整设计候选_v0.1.md:47) [原文B:59](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/bf/bf7ac9ccfb3a59bb__S5_SYS-NUM-01_完整设计候选_v0.1.md:59)

## 8. 页面和服务器校验

业务保存分待分配/已分配待保存/已创建；已显示原号保留，unknown先查。预览标“示例，不占号”，提示server时区/跨周期，刷新不耗号；规则影响列调用者和已用数量，412不套新草稿到旧单据。CodeGen只读显示未应用、准确模板/inputDigest/建议文件名；错误不泄服务器路径。[原文B:21](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/bf/bf7ac9ccfb3a59bb__S5_SYS-NUM-01_完整设计候选_v0.1.md:21) [原文B:33](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/bf/bf7ac9ccfb3a59bb__S5_SYS-NUM-01_完整设计候选_v0.1.md:33) [原文B:35](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/bf/bf7ac9ccfb3a59bb__S5_SYS-NUM-01_完整设计候选_v0.1.md:35) [原文B:63](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/bf/bf7ac9ccfb3a59bb__S5_SYS-NUM-01_完整设计候选_v0.1.md:63)

## 9. 固定代码差距与开发顺序

固定Main90c871...SeqService/SeqController/CodeGenController；Catalogue1576305非最新。Common/DocNumber在compare有改动但完整正文不在原包，真实调用者/唯一约束/部署未证。现SeqService即时SaveChanges不能证明跨实例无重号，BizKey查询也未证明全部scope过滤。[原文B:5](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/bf/bf7ac9ccfb3a59bb__S5_SYS-NUM-01_完整设计候选_v0.1.md:5) [原文B:9](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/bf/bf7ac9ccfb3a59bb__S5_SYS-NUM-01_完整设计候选_v0.1.md:9) [原文B:45](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/bf/bf7ac9ccfb3a59bb__S5_SYS-NUM-01_完整设计候选_v0.1.md:45)

11任务：①原子分配/receipt；②业务调用者原键/失败语义；③规则CAS/历史重叠/impact；④完整编号域/原allocator及单writer切换/恢复；⑤CodeGen白名单/权限/templateVersion和只读预览DTO。先冻结各Owner实际scope，不先全域迁移。[原文B:25](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/bf/bf7ac9ccfb3a59bb__S5_SYS-NUM-01_完整设计候选_v0.1.md:25) [原文B:39](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/bf/bf7ac9ccfb3a59bb__S5_SYS-NUM-01_完整设计候选_v0.1.md:39) [原文B:53](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/bf/bf7ac9ccfb3a59bb__S5_SYS-NUM-01_完整设计候选_v0.1.md:53) [原文B:67](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/bf/bf7ac9ccfb3a59bb__S5_SYS-NUM-01_完整设计候选_v0.1.md:67)

## 10. 验收场景与状态

16AC均NOT_RUN：NU01原operation复用/跨实例唯一/可跳不回收/未配非法耗尽拒/分配业务状态分开；NU02任何预览不写/旧跨周期sample局限/PUT不改计数/格式历史重叠CAS；NU03调用者scope单writer/时区不改旧号/未决原Owner/恢复不max号；NU04即时preview零持久副作用/save-view-scope正确/input-template-output可追/unknown查版本且文本不是部署。[原文B:23](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/bf/bf7ac9ccfb3a59bb__S5_SYS-NUM-01_完整设计候选_v0.1.md:23) [原文B:37](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/bf/bf7ac9ccfb3a59bb__S5_SYS-NUM-01_完整设计候选_v0.1.md:37) [原文B:51](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/bf/bf7ac9ccfb3a59bb__S5_SYS-NUM-01_完整设计候选_v0.1.md:51) [原文B:65](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/bf/bf7ac9ccfb3a59bb__S5_SYS-NUM-01_完整设计候选_v0.1.md:65)

## 11. 缺口与算法选择范围

各调用者真实唯一域、原分配器、跨实例约束、完整外露allocation清单、CodeGen完整实体字段/模板政策仍需Owner补证。规范允许实现Owner选择实际数据库互斥或CAS，但已经固定跨实例唯一/原operation/不回收语义；不把这一实现选择变成可自由删掉保证。[原文B:19](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/bf/bf7ac9ccfb3a59bb__S5_SYS-NUM-01_完整设计候选_v0.1.md:19) [原文B:43](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/bf/bf7ac9ccfb3a59bb__S5_SYS-NUM-01_完整设计候选_v0.1.md:43) [原文B:49](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/bf/bf7ac9ccfb3a59bb__S5_SYS-NUM-01_完整设计候选_v0.1.md:49) [原文B:59](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/bf/bf7ac9ccfb3a59bb__S5_SYS-NUM-01_完整设计候选_v0.1.md:59)

### 历史独审保留的实现对照线索

原R0独审指出旧SeqController存在DELETE入口，建议在兼容实施清单核清已用规则的停用/归档路径，不能删除配置时丢allocation或已外露号事实。本册继续遵守不重用和唯一发号Owner原不变量；该建议不是另行批准新端点或新发号政策。[原R0:166](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/fd/fd4afbe1e84e9931__S5_十模块50SPEC_独立完整静态审阅报告_20261008.txt:166)

## 12. 原件与阅读覆盖

正文全文1–71、4SPEC/16AC/11任务；S5复审全文1–128；接受JSON完整选定决定/正文/组合/ReviewQualifications/执行边界键。机器readlog记录语义字段与全字段身份结构核对的区别。没有调用Next、发布规则、生成或执行代码、测试/构建/CI。[原文B:1](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/bf/bf7ac9ccfb3a59bb__S5_SYS-NUM-01_完整设计候选_v0.1.md:1) [原文R:1](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/e3/e3522cf5a9c30a6a__S5_v0.2_认证双根独立复审与最小问题报告_20261008.txt:1) [原文A:8](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/a7/a7c83b1f0e790f6c__UA-20261008-S5-SYS-NUM-01-STATIC-MD02.json:8)

机器合同见 [platform-engineering-contracts.json](D:/CP6/docs/CP6_开发设计文档_20261010/contracts/platform-engineering-contracts.json)，阅读范围见 [platform-engineering-reading.json](D:/CP6/docs/CP6_开发设计文档_20261010/evidence/platform-engineering-reading.json)。

**当前必要材料归并状态。** 本册为 `required_materials_consolidated`：选定正文、规范附件及准确接受/复核范围已按直接实读、结构化语义或精确复用归并。身份清单核对不等底层源码全文阅读；历史草稿、候选实现和Office/HTML视觉未核范围仍单列。可复查[逐件阅读与处置](D:/CP6/docs/CP6_开发设计文档_20261010/evidence/platform-engineering-reading.json)及[本组闭包台账](D:/CP6/docs/CP6_开发设计文档_20261010/worklogs/platform-engineering-progress.json)。当前必需规范未读清单为空；具体Owner采用、参数/物理接口、真实运行和本节已有来源缺口不因整理闭包而改变。
