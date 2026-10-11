# SYS-PRIV-01 · 隐私导出、保留与处置

整理状态：`core_semantics_consolidated`。已接受静态规则的开发展开；候选字段/接口仍与已有实现分开，运行采用UNPROVEN。

## 1. 业务目的、操作者与Owner边界

各应用原数据Owner执行导出、匿名化和删除，共享政策协调；不建跨应用隐私主数据库。平台隐私操作者、申请人/接收人、审批人、被处理主体和maintenance identity分开。代码名GDPR不是合规认证；本册不决定法律期限或执行隐私操作。[原文B:5](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/77/77aae5c558d86a2a__S5_SYS-PRIV-01_完整设计候选_v0.1.md:5) [原文B:17](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/77/77aae5c558d86a2a__S5_SYS-PRIV-01_完整设计候选_v0.1.md:17) [原文B:37](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/77/77aae5c558d86a2a__S5_SYS-PRIV-01_完整设计候选_v0.1.md:37) [原文B:51](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/77/77aae5c558d86a2a__S5_SYS-PRIV-01_完整设计候选_v0.1.md:51)

Main提供现tenant/subject导出和anonymize/purge；CRM保留自身Lead原24日历月政策及历史command逐版本脱敏，不把Main通用接口套CRM。AUD/FILE/JOB/S6消费分类保留、当前安全投影和原处置回执，原账仍归各Owner。[原文B:9](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/77/77aae5c558d86a2a__S5_SYS-PRIV-01_完整设计候选_v0.1.md:9) [原文B:11](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/77/77aae5c558d86a2a__S5_SYS-PRIV-01_完整设计候选_v0.1.md:11) [原文B:45](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/77/77aae5c558d86a2a__S5_SYS-PRIV-01_完整设计候选_v0.1.md:45) [原文B:79](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/77/77aae5c558d86a2a__S5_SYS-PRIV-01_完整设计候选_v0.1.md:79)

## 2. 选定组合与接受范围

正文v0.1，SHA256 `77aae5c558d86a2a6b4ab384f0f87386a48dc3021a213adce16ac503c183aa63`，81行，4原SPEC/22AC/12任务，是S5 v0.2十稿中的未变八册之一；继承原完整独审，当前复核只新审IAM/CLIENT双认证根。[原文A:167](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/b5/b522b3da592b9d9c__UA-20261008-S5-SYS-PRIV-01-STATIC-MD02.json:167) [原文R:14](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/e3/e3522cf5a9c30a6a__S5_v0.2_认证双根独立复审与最小问题报告_20261008.txt:14) [原文B:81](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/77/77aae5c558d86a2a__S5_SYS-PRIV-01_完整设计候选_v0.1.md:81)

delegate接受当前静态设计，不是亲签；原02:18:29Z接受逐字文本未重建，04:09:04Z恢复确认另存。作者STOPPED保留历史，22AC NOT_RUN、Owner采用和运行UNPROVEN、实施false。[原文A:8](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/b5/b522b3da592b9d9c__UA-20261008-S5-SYS-PRIV-01-STATIC-MD02.json:8) [原文A:320](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/b5/b522b3da592b9d9c__UA-20261008-S5-SYS-PRIV-01-STATIC-MD02.json:320)

## 3. 数据目录、字段和时间身份

|对象/合同|候选字段/内容|要求|
|---|---|---|
|ExportRequest|operationId/requestRef/targetKind/targetRef/purpose/approvedScope/recipientRef/expectedPolicyVersion/requestedFormat|target受控选定，tenant+稳定主体，不能同名跨租推断。[原文B:17](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/77/77aae5c558d86a2a__S5_SYS-PRIV-01_完整设计候选_v0.1.md:17) [原文B:19](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/77/77aae5c558d86a2a__S5_SYS-PRIV-01_完整设计候选_v0.1.md:19)|
|导出目录/Manifest|实体/原Ref/筛选/时间/脱敏规则/缺项，sourceWatermark/asOf/sourceVersion|INCLUDED/EXCLUDED_WITH_REASON/UNKNOWN_SCOPE明确，不把未覆盖当无数据；跨库非同快照。[原文B:19](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/77/77aae5c558d86a2a__S5_SYS-PRIV-01_完整设计候选_v0.1.md:19) [原文B:21](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/77/77aae5c558d86a2a__S5_SYS-PRIV-01_完整设计候选_v0.1.md:21)|
|ExportOperationView|REQUESTED/GENERATING/READY/PARTIAL/FAILED/UNKNOWN/EXPIRED，fileRef/manifestRef/scopeSummary/downloadExpiresAt/evidenceGaps|候选异步模型，不冒已有API。[原文B:23](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/77/77aae5c558d86a2a__S5_SYS-PRIV-01_完整设计候选_v0.1.md:23)|
|CRM RetentionPlan/UnitResult|planId/organizationRef/policyVersion/anchorRef/deadlineUtc/unitKind/ref/version/disposition/holdStatus/resultRef/errorClass/checkpoint|候选维护投影DUE/HELD/PROCESSING/ANONYMIZED/FAILED/UNKNOWN；原PiiState只有Retained/Anonymized，不另立业务状态真相。[原文B:39](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/77/77aae5c558d86a2a__S5_SYS-PRIV-01_完整设计候选_v0.1.md:39)|
|处置计划|requesterRef/operatorRef/approverRef/targetTenant或subject/actionKind/purpose/policyRef+version/scopeDigest/expectedTargetVersion/confirmationRef/legalHoldDecisionRef|机器仅执行已批准准确模式与范围，不能扩大实体/tenant/purge。[原文B:51](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/77/77aae5c558d86a2a__S5_SYS-PRIV-01_完整设计候选_v0.1.md:51)|
|DispositionReceipt|plan/actor/action/object version/policy/ref/cutoff/outcome/redactedCategories/evidenceGaps|只安全证明，不再存被删原PII；原native证据、privacy derivative、tombstone不同。[原文B:69](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/77/77aae5c558d86a2a__S5_SYS-PRIV-01_完整设计候选_v0.1.md:69)|

CRM固定规则：source为Manual/Website，OriginalReceivedAt≤CreatedAt且Manual相等；`deadline = OriginalReceivedAt.UTC.AddMonths(24)`，不是730天。修改、分配、软删、再联系不重启锚点；截止前常规任务不匿名化，执行时间不得早于创建/既存审计；Anonymized必须有效AnonymizedAt且真实redacted。[原文B:33](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/77/77aae5c558d86a2a__S5_SYS-PRIV-01_完整设计候选_v0.1.md:33)

处置字段：ContactName→`[anonymized]`，email/phone/need/company、DisqualificationReason、活动OriginalContent、内容纠正Content/Reason、事实纠正Reason清除；清search tokens；组织/LeadId、非识别事实、各旧版本自身业务事实保留。public submission/attempt凭据有更早独立deadline先处理，先验父子绑定。历史command逐原版本脱敏，保operation/resource/key/payload hash/response code/原ETag/command time，不用最新LeadSnapshot覆盖旧结果。[原文B:35](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/77/77aae5c558d86a2a__S5_SYS-PRIV-01_完整设计候选_v0.1.md:35)

## 4. 导出与处置流程及写集

导出：当前平台门+具体purpose/范围→先解析原operation→新意图验证Owner来源/分类/接收方→冻结plan/sourceWatermark→各应用在自身当前scope最小读取→Manifest列完整/缺项及各asOf→完整性和当前下载权均通过才交付。不得为了“全包”扫未授权第三方，不拼跨库同一快照。[原文B:21](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/77/77aae5c558d86a2a__S5_SYS-PRIV-01_完整设计候选_v0.1.md:21)

CRM：注册maintenance identity限定organization→扫描到期candidate→authenticate/decode完整存储图/绑定→当前Owner retention/hold→同一受控事务处置当前对象/search/各原command结果/关联submission-attempt→原AppendEffects记真实匿名化与进度→核提交结果。原文只证明各方法存在，完整ProcessRetention事务/进度和Host启用当时未全读，不臆定全批次原子。[原文B:37](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/77/77aae5c558d86a2a__S5_SYS-PRIV-01_完整设计候选_v0.1.md:37)

破坏计划：分别导出/主体匿名化/tenant匿名化/purge/hold/处置审批能力→精确对象、引用、不可逆影响→批准固定scopeDigest/版本/mode→真实Owner共同fence+隐私/保留门持到效果提交→各Owner原receipt汇总。preview后撤权/hold变化必须阻旧计划，跨库无共同门禁新破坏效果；不能只靠confirm=true或短TTL。[原文B:49](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/77/77aae5c558d86a2a__S5_SYS-PRIV-01_完整设计候选_v0.1.md:49) [原文B:53](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/77/77aae5c558d86a2a__S5_SYS-PRIV-01_完整设计候选_v0.1.md:53) [原文B:55](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/77/77aae5c558d86a2a__S5_SYS-PRIV-01_完整设计候选_v0.1.md:55)

保留冲突：Owner范围/引用图→分类必要nonPII证据、可删PII、秘密、特殊保留材料及副本→冻结政策裁决→事务重判hold/version→各Owner自有处置→安全receipt。汇总PARTIAL/UNKNOWN真实显示，不伪总成功。政策要求销毁原payload时保存允许的tombstone并声明原件不可取，不伪称原bytes仍可验证。[原文B:63](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/77/77aae5c558d86a2a__S5_SYS-PRIV-01_完整设计候选_v0.1.md:63) [原文B:69](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/77/77aae5c558d86a2a__S5_SYS-PRIV-01_完整设计候选_v0.1.md:69)

## 5. API与依赖

现GET `/api/platform/gdpr/export/tenant/{tenantId}`、`/export/subject/{userId}` 返回application/json附件流；目标不存在时报E-SEC-032。旧同步响应丢失不等于没有生成；候选异步ExportOperationView没有已证新路由。[原文B:23](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/77/77aae5c558d86a2a__S5_SYS-PRIV-01_完整设计候选_v0.1.md:23)

现DELETE `/api/platform/gdpr/erase/subject/{userId}?confirm`、`/erase/tenant/{tenantId}?mode&confirm`；false/非法mode E-SEC-038；anonymize保ID/行并停tenant，purge物理删关系库。confirm参数不是授权，候选先准确DestructionPlan且不允许临时把匿名化改purge。[原文B:55](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/77/77aae5c558d86a2a__S5_SYS-PRIV-01_完整设计候选_v0.1.md:55)

CRM仅已证worker内部ProcessRetentionAsync，没有通用人工“全部匿名化”HTTP route。所需依赖：各Owner实体/字段与稳定subject目录；IAM真实能力/共同门；FILE当前下载与副本；JOB原plan/checkpoint；AUD/S6必要结果/用途/技术证据保留。接口尚未选定不创造已有URL。[原文B:29](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/77/77aae5c558d86a2a__S5_SYS-PRIV-01_完整设计候选_v0.1.md:29) [原文B:39](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/77/77aae5c558d86a2a__S5_SYS-PRIV-01_完整设计候选_v0.1.md:39) [原文B:59](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/77/77aae5c558d86a2a__S5_SYS-PRIV-01_完整设计候选_v0.1.md:59) [原文B:75](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/77/77aae5c558d86a2a__S5_SYS-PRIV-01_完整设计候选_v0.1.md:75)

## 6. 幂等、UNKNOWN、保留竞争与恢复

相同operation先在当前合法read下查原结果，历史查询不要求旧删除权仍有效；实际重执行再核当前最终门。生成/删除UNKNOWN先查原operation/unit/receipt，不重复泄露或双击删除。CRM单元重复不恢复PII或产生重复效果；图/密文/shape/绑定不支持隔离报警，不能跳认证强删。[原文B:23](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/77/77aae5c558d86a2a__S5_SYS-PRIV-01_完整设计候选_v0.1.md:23) [原文B:41](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/77/77aae5c558d86a2a__S5_SYS-PRIV-01_完整设计候选_v0.1.md:41) [原文B:53](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/77/77aae5c558d86a2a__S5_SYS-PRIV-01_完整设计候选_v0.1.md:53) [原文B:55](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/77/77aae5c558d86a2a__S5_SYS-PRIV-01_完整设计候选_v0.1.md:55)

UNKNOWN/PROCESSING/争议/待重排/必要审签或业务引用不通用retention删除；hold有basis/Owner/复核期限，不默认无限期。CRM24月遇明确hold冲突需数据/法务Owner批准精确例外，不能擅改锚点；无决定单元隔离并报警deadline风险，不无声拖延。[原文B:67](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/77/77aae5c558d86a2a__S5_SYS-PRIV-01_完整设计候选_v0.1.md:67)

匿名化后privacy derivative保原业务身份/请求摘要语义；真正销毁原payload留获准tombstone。批准恢复备份也必须先重用已完成处置tombstone和当前策略再开放读写，不能旧库复活PII；外部收件人已拿到导出无法凭本系统删除证明回收。[原文B:69](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/77/77aae5c558d86a2a__S5_SYS-PRIV-01_完整设计候选_v0.1.md:69) [原文B:71](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/77/77aae5c558d86a2a__S5_SYS-PRIV-01_完整设计候选_v0.1.md:71)

## 7. 权限、主体与秘密

平台三道闸是旧入口事实，不是永久披露/破坏许可；导出/不同处置/hold/批准能力分开，模拟期间平台操作拒。平台tenant禁擦；原代码默认平台tenant超管/末名启用超管保护不能泛称所有超管都禁擦。候选任何仍有平台权主体先独立撤权+末名检查，匿名化不暗中撤权。[原文B:49](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/77/77aae5c558d86a2a__S5_SYS-PRIV-01_完整设计候选_v0.1.md:49) [原文B:57](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/77/77aae5c558d86a2a__S5_SYS-PRIV-01_完整设计候选_v0.1.md:57)

用户导出排密码/token/2FAsecret/key；StripSensitive属性过滤不证明所有新增实体/嵌套JSON完整标注。原subject操作日志按UserName查询的范围需改成tenant+稳定主体/历史用户名关联，不能把同名他人资料混入。普通PII按具体批准scope最小化，平台角色不绕purpose/recipient/current download权。[原文B:19](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/77/77aae5c558d86a2a__S5_SYS-PRIV-01_完整设计候选_v0.1.md:19) [原文B:25](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/77/77aae5c558d86a2a__S5_SYS-PRIV-01_完整设计候选_v0.1.md:25)

## 8. 页面与服务器检查

导出前显目标、用途、范围、接收方，完成显Manifest缺项与每源asOf；FILE受控下载有期效，过期重新生成必须当前scope，不公共永久链。CRM页不从cache/旧command返回已清contact；历史receipt保原版本nonPII、显示已处置，权限提升也不还原。维护诊断只safe deadline积压/最后真实成功/error/checkpoint，扫描完不等全部成功。[原文B:25](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/77/77aae5c558d86a2a__S5_SYS-PRIV-01_完整设计候选_v0.1.md:25) [原文B:41](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/77/77aae5c558d86a2a__S5_SYS-PRIV-01_完整设计候选_v0.1.md:41)

停用/匿名化/永久删除独立操作，显示不可逆影响、排除/hold/未覆盖副本和Owner待决，确认固定target/mode。unknown不再点删除；safe证明不提供“恢复PII”按钮。服务器重核精确计划scopeDigest/expectedVersion而非信前端确认。[原文B:55](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/77/77aae5c558d86a2a__S5_SYS-PRIV-01_完整设计候选_v0.1.md:55) [原文B:69](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/77/77aae5c558d86a2a__S5_SYS-PRIV-01_完整设计候选_v0.1.md:69) [原文B:71](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/77/77aae5c558d86a2a__S5_SYS-PRIV-01_完整设计候选_v0.1.md:71)

## 9. 固定代码事实与开发順序

Main固定90c871...：现tenant导出沿实体拓扑剔敏感；subject仅用户/安全日志/按UserName操作日志，不全Creator反扫。tenant purge少量非EF适配已证：对注册CRM identity tenant收tombstone、撤token、存identity快照；不能泛化CRM全部业务匿名化、外部文件/备份清除或所有fence。源码主体擦除允许access TTL剩余自然失效，不等即时全撤销。[原文B:9](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/77/77aae5c558d86a2a__S5_SYS-PRIV-01_完整设计候选_v0.1.md:9) [原文B:65](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/77/77aae5c558d86a2a__S5_SYS-PRIV-01_完整设计候选_v0.1.md:65)

CRM固定c778a305...三个确切源件LeadRetention、CrmBusinessService.Retention、CrmRetentionWorker，小时调度/24小时deadline提示不是生产SLA。12任务：①Owner范围/稳定主体/嵌套字段目录；②导出operation/Manifest/FILE；③CRM完整ProcessRetention事务与历史结果/search/submission全图；④动作审批/共同门/精确处置；⑤多Owner引用与hold/原结果保护；⑥backup/tombstone/外副本边界与页面。[原文B:11](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/77/77aae5c558d86a2a__S5_SYS-PRIV-01_完整设计候选_v0.1.md:11) [原文B:29](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/77/77aae5c558d86a2a__S5_SYS-PRIV-01_完整设计候选_v0.1.md:29) [原文B:45](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/77/77aae5c558d86a2a__S5_SYS-PRIV-01_完整设计候选_v0.1.md:45) [原文B:59](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/77/77aae5c558d86a2a__S5_SYS-PRIV-01_完整设计候选_v0.1.md:59) [原文B:75](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/77/77aae5c558d86a2a__S5_SYS-PRIV-01_完整设计候选_v0.1.md:75)

## 10. 验收场景与状态

22AC全NOT_RUN：PV01稳定tenant/subject不串名、secret覆盖嵌套、Manifest如实范围与asOf、当前下载/原operation；PV02UTC24日历月、当前和历史逐版本PII处置且原ETag保留、child独立deadline、幂等unknown、调度不冒SLA、坏图隔离；PV03能力/模拟/末名并发、计划模式版本冻结、撤权hold共同门、历史read和新execute分开；PV04分类期限不套7/30/90天、引用hold、native/derivative/tombstone、跨库PARTIAL、backup不复活、少量非EF证据不泛化。[原文B:27](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/77/77aae5c558d86a2a__S5_SYS-PRIV-01_完整设计候选_v0.1.md:27) [原文B:43](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/77/77aae5c558d86a2a__S5_SYS-PRIV-01_完整设计候选_v0.1.md:43) [原文B:57](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/77/77aae5c558d86a2a__S5_SYS-PRIV-01_完整设计候选_v0.1.md:57) [原文B:73](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/77/77aae5c558d86a2a__S5_SYS-PRIV-01_完整设计候选_v0.1.md:73)

## 11. 未完成与不可推定

真实法律basis/期限/hold签认、完整外部副本清单、CRM全事务/Host采用、Main全部主体关系及fence覆盖仍未证。既有24月政策保留，遇冲突隔离+期限风险报告，不能本册决定延期/例外。设计接受不构成合规认证；原文件/备份不可取必须如实记录。[原文B:13](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/77/77aae5c558d86a2a__S5_SYS-PRIV-01_完整设计候选_v0.1.md:13) [原文B:37](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/77/77aae5c558d86a2a__S5_SYS-PRIV-01_完整设计候选_v0.1.md:37) [原文B:67](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/77/77aae5c558d86a2a__S5_SYS-PRIV-01_完整设计候选_v0.1.md:67) [原文B:79](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/77/77aae5c558d86a2a__S5_SYS-PRIV-01_完整设计候选_v0.1.md:79)

## 12. 原件与阅读覆盖

本次正文1–45、46–81两段完整阅读；四SPEC/全部22AC和12任务实读。S5复审全文1–128；接受件完整选定决定/正文/组合/ReviewQualifications/执行边界键结构读取。源码叙述基于固定规范明确范围，不声称本次全库/真实PII实读。未执行导出、匿名化、删除、账号或法律操作、项目脚本/测试。[原文B:1](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/77/77aae5c558d86a2a__S5_SYS-PRIV-01_完整设计候选_v0.1.md:1) [原文R:1](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/e3/e3522cf5a9c30a6a__S5_v0.2_认证双根独立复审与最小问题报告_20261008.txt:1) [原文A:8](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/b5/b522b3da592b9d9c__UA-20261008-S5-SYS-PRIV-01-STATIC-MD02.json:8)

机器合同见 [platform-engineering-contracts.json](D:/CP6/docs/CP6_开发设计文档_20261010/contracts/platform-engineering-contracts.json)，阅读范围见 [platform-engineering-reading.json](D:/CP6/docs/CP6_开发设计文档_20261010/evidence/platform-engineering-reading.json)。

**当前必要材料归并状态。** 本册为 `required_materials_consolidated`：选定正文、规范附件及准确接受/复核范围已按直接实读、结构化语义或精确复用归并。身份清单核对不等底层源码全文阅读；历史草稿、候选实现和Office/HTML视觉未核范围仍单列。可复查[逐件阅读与处置](D:/CP6/docs/CP6_开发设计文档_20261010/evidence/platform-engineering-reading.json)及[本组闭包台账](D:/CP6/docs/CP6_开发设计文档_20261010/worklogs/platform-engineering-progress.json)。当前必需规范未读清单为空；具体Owner采用、参数/物理接口、真实运行和本节已有来源缺口不因整理闭包而改变。
