# SYS-AUD-01 · 操作、安全与字段审计

整理状态：`core_semantics_consolidated`。本册为已接受静态设计的开发展开，候选接口/字段明确标识；不等于现有代码已采用。

## 1. 业务目的、操作者与证据边界

复用Main OperLog/SecurityLog/FieldAudit及查询/保留入口，不新增统一业务主账。操作日志是请求活动，安全日志是认证/授权事件，字段历史是被标注实体的before/after；Owner原operation/receipt才是业务结果权威，Platform trace为技术观察。可关联ID，不能从日志文字相似或HTTP成功推出业务已应用。[原文B:5](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/c9/c9943c2bdead2009__S5_SYS-AUD-01_完整设计候选_v0.1.md:5) [原文B:9](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/c9/c9943c2bdead2009__S5_SYS-AUD-01_完整设计候选_v0.1.md:9)

操作者是本tenant审计读者、平台带外审计读者、独立导出员、保留政策Owner和清理worker；读日志不等于读全部业务内容，操作员不得编辑自己的既有审计。保存原证据完整性与返回当前脱敏视图分层。[原文B:13](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/c9/c9943c2bdead2009__S5_SYS-AUD-01_完整设计候选_v0.1.md:13)

## 2. 选定组合与接受范围

正文v0.1，SHA256 `c9943c2bdead200996e9ce7cf4e9d6de67c62ab28f435086d8d7fd303f72f623`，87行。它是S5 v0.2混合十稿中八个字节未改成员之一；5原SPEC/21AC/15任务。最终复核继承这八册原完整独审，仅IAM/CLIENT认证双根新复审，不能把它写成十册重新全文审计。[原文A:154](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/a0/a0bc63cf4f218d81__UA-20261008-S5-SYS-AUD-01-STATIC-MD02.json:154) [原文R:14](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/e3/e3522cf5a9c30a6a__S5_v0.2_认证双根独立复审与最小问题报告_20261008.txt:14) [原文R:94](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/e3/e3522cf5a9c30a6a__S5_v0.2_认证双根独立复审与最小问题报告_20261008.txt:94) [原文B:87](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/c9/c9943c2bdead2009__S5_SYS-AUD-01_完整设计候选_v0.1.md:87)

delegate接受当前静态设计，非亲签；原02:18:29Z决定逐字文本未重建，04:09:04Z恢复确认另存。后续接受不擦除原STOPPED，21AC仍NOT_RUN、实施false、真实Owner采用/运行UNPROVEN。[原文A:8](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/a0/a0bc63cf4f218d81__UA-20261008-S5-SYS-AUD-01-STATIC-MD02.json:8) [原文A:307](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/a0/a0bc63cf4f218d81__UA-20261008-S5-SYS-AUD-01-STATIC-MD02.json:307)

## 3. 事件、历史、策略与身份

|对象/投影|候选字段/身份|约束|
|---|---|---|
|操作事件|logId、tenantBinding、actualActor/effectiveActor、requestRouteKey、actionCode、resourceNativeRef、operationRef、requestReceivedAtUtc、duration、outcome、safeReasonCode、traceId/correlationId、retentionClass、schemaVersion|白名单采集；业务operation与技术trace不是同一身份。[原文B:19](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/c9/c9943c2bdead2009__S5_SYS-AUD-01_完整设计候选_v0.1.md:19)|
|安全事件|actual/effective actor、authDecisionId、permissionDomain、policyVersion/epoch、action/targetRef、outcome/reasonCode、nativeEvidenceRef、occurredAtUtc/timeSource、recordedAtUtc、operationRef|服务、模拟、真实用户分型；body.UserName不是认证主体。[原文B:33](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/c9/c9943c2bdead2009__S5_SYS-AUD-01_完整设计候选_v0.1.md:33)|
|FieldHistoryView|auditId、entityNativeRef(owner/type/id/version)、changedAt/timeSource、operationKind、actorRefs、changes[{fieldKey,oldValue,newValue,visibility,redactionReason}]、nativeDigest/evidenceStatus|受控原bytes/hash不因脱敏覆写；privacy派生hash不冒原hash。[原文B:47](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/c9/c9943c2bdead2009__S5_SYS-AUD-01_完整设计候选_v0.1.md:47)|
|SafeOperLogPage|nextCursor/asOf/redactedFields/evidenceClass|稳定(时间,ID)顺序，旧page/rows/total兼容。[原文B:23](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/c9/c9943c2bdead2009__S5_SYS-AUD-01_完整设计候选_v0.1.md:23)|
|AuditQuery/ExportRequest|scope/filter/timeRange/purpose/expectedPolicyVersion|导出artifact有范围/到期，逐次下载检查当前权。[原文B:63](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/c9/c9943c2bdead2009__S5_SYS-AUD-01_完整设计候选_v0.1.md:63)|
|RetentionPolicy|policyRef/version、jurisdiction/businessBasis、anchor、duration或reviewAt、redactableFields、holdRefs、dependentNativeRefs、disposalMethod|每类实际Owner指定，不套公共天数，也不能借“审计”永久保全部PII。[原文B:73](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/c9/c9943c2bdead2009__S5_SYS-AUD-01_完整设计候选_v0.1.md:73)|

保留类为TECH_TELEMETRY、SECURITY_EVENT、FIELD_HISTORY、OWNER_RESULT_EVIDENCE、REPLAY_AUDIT、TECHNICAL_CONTENT_EVIDENCE、PRIVACY_EXECUTION_EVIDENCE。日志可靠性明确BEST_EFFORT_TELEMETRY/REQUIRED_AUDIT；后者要实际Owner事务，不是加个布尔字段。[原文B:21](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/c9/c9943c2bdead2009__S5_SYS-AUD-01_完整设计候选_v0.1.md:21) [原文B:73](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/c9/c9943c2bdead2009__S5_SYS-AUD-01_完整设计候选_v0.1.md:73)

## 4. 记录、读取与清理流程

操作记录：请求边界建关联→只收允许字段→如实记拒绝/失败/未知/已提交→存既有通道→安全只读投影。提交unknown记UNKNOWN，后续原Owner receipt核实只追加关联事件，不覆盖原日志；缺失时追加gap说明，不能伪造事故当时原事件。[原文B:21](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/c9/c9943c2bdead2009__S5_SYS-AUD-01_完整设计候选_v0.1.md:21) [原文B:23](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/c9/c9943c2bdead2009__S5_SYS-AUD-01_完整设计候选_v0.1.md:23)

安全记录：登录失败/锁定、改密/2FA、SSO配置、角色/平台权、tenant停复、模拟、隐私导出/删除、重放分别由原Owner负责。新高影响动作同Owner事务持久强制审计及原决定版本，缺采用关闭该类新动作；低风险登录观察保旧best-effort不阻主流程且报警evidenceGap，不把整个系统改成日志失败即不准登录。[原文B:35](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/c9/c9943c2bdead2009__S5_SYS-AUD-01_完整设计候选_v0.1.md:35)

字段记录：真实Owner事务捕获旧/新值→排除秘密/禁止字段→记录原版本/operation→当前读者定位本tenant实体→按当前行/字段/PII投影→稳定分页。缺实体映射/字段政策只留摘要和gap，关闭该详情，不原样返Changes JSON；时间线浏览不写回oldValue，业务恢复走原Owner新命令。[原文B:49](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/c9/c9943c2bdead2009__S5_SYS-AUD-01_完整设计候选_v0.1.md:49)

清理：当前worker→冻结时间/政策/范围plan→逐件hold/引用检查→排除UNKNOWN/PROCESSING/待重排/争议及必要业务/工程引用→保最小处置依据→真实Owner事务重核policy/对象版本和hold共同门→允许删除或匿名化→持久处置receipt。跨tenant逐域明确政策；预览后新hold必须挡删除。[原文B:75](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/c9/c9943c2bdead2009__S5_SYS-AUD-01_完整设计候选_v0.1.md:75)

## 5. API与跨Owner合同

|现接口|既有行为与候选对接|
|---|---|
|GET /api/OperLog|operlog:query；UserName/RequestUrl/Controller关键词；CreateDate倒序分页。候选补稳定cursor、白名单、证据级别。[原文B:17](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/c9/c9943c2bdead2009__S5_SYS-AUD-01_完整设计候选_v0.1.md:17) [原文B:23](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/c9/c9943c2bdead2009__S5_SYS-AUD-01_完整设计候选_v0.1.md:23)|
|DELETE /api/OperLog|operlog:delete清查询范围旧入口；新保留类必须纳入同hold/引用门，本册不授权执行。[原文B:17](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/c9/c9943c2bdead2009__S5_SYS-AUD-01_完整设计候选_v0.1.md:17) [原文B:77](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/c9/c9943c2bdead2009__S5_SYS-AUD-01_完整设计候选_v0.1.md:77)|
|GET /api/sys/security-log|sys-security-log:query；eventType/userName/from/to；page≥1,size1..200，to按次日右开。原字段Id/UserId/UserName/RequestTenantCode/EventType/Reason/ClientIp/UserAgent/CreatedAt，原CreatedAt本地时间。[原文B:31](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/c9/c9943c2bdead2009__S5_SYS-AUD-01_完整设计候选_v0.1.md:31)|
|GET /api/sys/field-audit|sys-field-audit:query；entityName/entityKey/userId/date；列表changeCount而非整个Changes。[原文B:45](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/c9/c9943c2bdead2009__S5_SYS-AUD-01_完整设计候选_v0.1.md:45)|
|GET /api/sys/field-audit/record|原entityName+entityKey返回全正序Changes JSON；候选必须当前实体/字段映射安全投影和分页，不保留旁路。[原文B:45](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/c9/c9943c2bdead2009__S5_SYS-AUD-01_完整设计候选_v0.1.md:45) [原文B:49](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/c9/c9943c2bdead2009__S5_SYS-AUD-01_完整设计候选_v0.1.md:49)|

Required-AUD-01答复：Owner结果/撤权/重放/技术内容独立保留类；UNKNOWN/争议/待重排/必要引用不走通用成功过期清理；原nativeRef/hash与当前安全view分离；metadata/content/export分能力。实际期限、依据、完整引用图、hold竞争门和Owner采用仍Required/UNPROVEN。FILE负责受控下载，JOB负责运行原plan，IAM提供真实能力映射。[原文B:67](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/c9/c9943c2bdead2009__S5_SYS-AUD-01_完整设计候选_v0.1.md:67) [原文B:81](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/c9/c9943c2bdead2009__S5_SYS-AUD-01_完整设计候选_v0.1.md:81) [原文B:85](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/c9/c9943c2bdead2009__S5_SYS-AUD-01_完整设计候选_v0.1.md:85)

## 6. 事务、幂等、UNKNOWN与恢复

当前SecurityAuditService异常只LogWarning不阻主流，字符串限100/64/64/256/256；CP6Context字段SaveChanges事务/保存点只覆盖标注对象/字段及EF路径。新REQUIRED_AUDIT必须与真实高影响Owner写同事务，跨库无证据不能假同commit；非EF路径列evidenceGap。[原文B:11](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/c9/c9943c2bdead2009__S5_SYS-AUD-01_完整设计候选_v0.1.md:11) [原文B:35](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/c9/c9943c2bdead2009__S5_SYS-AUD-01_完整设计候选_v0.1.md:35) [原文B:53](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/c9/c9943c2bdead2009__S5_SYS-AUD-01_完整设计候选_v0.1.md:53)

清理中断续原plan/cursor，已处置返回原结果；UNKNOWN先查，不随机重删；解除hold是新授权+新计划，不复用旧允许决定。永久删除不可逆，静态AC不能证明可恢复；政策批准备份恢复另审且不复活已匿名化PII。[原文B:77](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/c9/c9943c2bdead2009__S5_SYS-AUD-01_完整设计候选_v0.1.md:77)

原operation历史只需当前安全read，不要求过期旧eligibility/新写资格；新export是新敏感访问动作，需当前export/字段权+purpose，旧下载链接不能绕撤权。日志解析失败是UNREADABLE，不是零变化。[原文B:51](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/c9/c9943c2bdead2009__S5_SYS-AUD-01_完整设计候选_v0.1.md:51) [原文B:61](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/c9/c9943c2bdead2009__S5_SYS-AUD-01_完整设计候选_v0.1.md:61)

## 7. 权限、租户、秘密与隐私

逻辑候选能力audit.read.metadata、audit.read.content、audit.export、audit.retention.manage分别映射；不能由原query/delete动作自动扩大。平台管理员仍过当前三道闸，模拟不恢复平台日志权；平台事件落平台tenant，普通tenant不可读跨域全量。[原文B:31](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/c9/c9943c2bdead2009__S5_SYS-AUD-01_完整设计候选_v0.1.md:31) [原文B:59](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/c9/c9943c2bdead2009__S5_SYS-AUD-01_完整设计候选_v0.1.md:59)

读链：认证→tenant/平台域→scope read→对象历史read→当前字段/PII最小化→原证据定位→safe投影。无权/不存在对普通角色不泄存在性，未知policy返回503/DETAIL_UNAVAILABLE，不fallback全原件。[原文B:61](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/c9/c9943c2bdead2009__S5_SYS-AUD-01_完整设计候选_v0.1.md:61)

禁止默认保存整request/response、cookie、Authorization、密码、OTP、密钥或secret URL；IP/UA/username按受限字段最小返回/保留。原AuditIgnore沿用；过去可见字段不代表现在可读old/new；匿名化与必要保留依PRIV政策分派生与原hash。[原文B:19](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/c9/c9943c2bdead2009__S5_SYS-AUD-01_完整设计候选_v0.1.md:19) [原文B:33](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/c9/c9943c2bdead2009__S5_SYS-AUD-01_完整设计候选_v0.1.md:33) [原文B:45](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/c9/c9943c2bdead2009__S5_SYS-AUD-01_完整设计候选_v0.1.md:45) [原文B:47](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/c9/c9943c2bdead2009__S5_SYS-AUD-01_完整设计候选_v0.1.md:47)

## 8. 页面与服务器校验

日志列表默认摘要，详情按需授权，不提供“重试业务”按钮；安全事件过滤含事件类型/安全状态/时间来源，UTC候选查询兼容旧本地时间/右开区间。缺审计显示记录不完整，不造“未发生”。字段diff只显允许值，解析错UNREADABLE、长时间线cursor分页，不把全JSON先下浏览器再隐藏。[原文B:23](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/c9/c9943c2bdead2009__S5_SYS-AUD-01_完整设计候选_v0.1.md:23) [原文B:37](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/c9/c9943c2bdead2009__S5_SYS-AUD-01_完整设计候选_v0.1.md:37) [原文B:51](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/c9/c9943c2bdead2009__S5_SYS-AUD-01_完整设计候选_v0.1.md:51)

导出页面显purpose、范围、有效期/限制，不能靠下载绕详情限制；诊断仅safe metadata/trace。清理旧Clear进入plan预览，未知保留类关闭破坏动作；显示plan/receipt/未决项，不能将HTTP超时写成全部清理失败再一键重删。[原文B:63](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/c9/c9943c2bdead2009__S5_SYS-AUD-01_完整设计候选_v0.1.md:63) [原文B:75](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/c9/c9943c2bdead2009__S5_SYS-AUD-01_完整设计候选_v0.1.md:75) [原文B:77](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/c9/c9943c2bdead2009__S5_SYS-AUD-01_完整设计候选_v0.1.md:77)

## 9. 开发顺序与固定代码差距

固定Main90c871...五控制/服务与CP6Context的范围见原正文。OperLogCleanup默认RetentionDays7、CleanupIntervalHours24、≤0不清理、启动延迟30秒、跨tenant按CreateDate批删，只针对OperLog；未见通用hold/UNKNOWN/引用排除。这是静态实现范围，不执行清理或判断运行已采纳。[原文B:5](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/c9/c9943c2bdead2009__S5_SYS-AUD-01_完整设计候选_v0.1.md:5) [原文B:11](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/c9/c9943c2bdead2009__S5_SYS-AUD-01_完整设计候选_v0.1.md:11) [原文B:71](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/c9/c9943c2bdead2009__S5_SYS-AUD-01_完整设计候选_v0.1.md:71)

顺序：①事件schema/可靠性和安全分页；②安全Owner责任/高影响真实同事务；③字段Owner映射/PII投影/非EF覆盖；④IAM四能力及FILE逐次下载；⑤各Owner保留依据/引用/hold共同门；⑥JOB和旧clear适配、原plan恢复与页面。15任务PLANNED。[原文B:27](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/c9/c9943c2bdead2009__S5_SYS-AUD-01_完整设计候选_v0.1.md:27) [原文B:41](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/c9/c9943c2bdead2009__S5_SYS-AUD-01_完整设计候选_v0.1.md:41) [原文B:55](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/c9/c9943c2bdead2009__S5_SYS-AUD-01_完整设计候选_v0.1.md:55) [原文B:67](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/c9/c9943c2bdead2009__S5_SYS-AUD-01_完整设计候选_v0.1.md:67) [原文B:81](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/c9/c9943c2bdead2009__S5_SYS-AUD-01_完整设计候选_v0.1.md:81)

## 10. 验收场景与状态

21AC全NOT_RUN：AU01白名单无secret、UNKNOWN与Owner结果分开、分页隔离、旧Clear纳门；AU02真实actual/effective/service与版本、best-effort/强制区别、平台域和IP/UA限制、时间兼容；AU03详情不JSON旁路、解析错不零、原digest/投影分开且不写业务、非EFgap；AU04四能力/撤权详情下载搜索/未知关闭/模拟域；AU05七天仅OperLog、UNKNOWN/引用/争议/hold阻清理、同门重判、最小安全处置依据、原plan续跑且无政策不删。[原文B:25](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/c9/c9943c2bdead2009__S5_SYS-AUD-01_完整设计候选_v0.1.md:25) [原文B:39](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/c9/c9943c2bdead2009__S5_SYS-AUD-01_完整设计候选_v0.1.md:39) [原文B:53](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/c9/c9943c2bdead2009__S5_SYS-AUD-01_完整设计候选_v0.1.md:53) [原文B:65](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/c9/c9943c2bdead2009__S5_SYS-AUD-01_完整设计候选_v0.1.md:65) [原文B:79](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/c9/c9943c2bdead2009__S5_SYS-AUD-01_完整设计候选_v0.1.md:79)

## 11. 未完成与不能推定事项

真实保留期限/依据需数据及隐私Owner确认；本册不给法律认证。完整引用图、hold竞争门、强审计writer范围、跨库原子边界仍需对接；不能从“已接受”推断S6重放、工程内容或隐私执行证据已经被实际安全保留。字段原对象删除后的最小历史读另需获准政策，不等于复活对象访问。[原文B:51](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/c9/c9943c2bdead2009__S5_SYS-AUD-01_完整设计候选_v0.1.md:51) [原文B:73](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/c9/c9943c2bdead2009__S5_SYS-AUD-01_完整设计候选_v0.1.md:73) [原文B:85](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/c9/c9943c2bdead2009__S5_SYS-AUD-01_完整设计候选_v0.1.md:85)

### 历史独审保留的实现对照线索

原R0独审指出旧OperLog Clear无GET keyword/page筛选输入，作用为当前tenant过滤域；FE不得仅因当前页/搜索框可见而把它解释为“只清这些行”。当前设计仍把旧Clear接入保留计划和最终hold/引用门。此为保留的非阻塞来源精化线索，不新增删除权限或缩窄/扩大原端点。[原R0:162](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/fd/fd4afbe1e84e9931__S5_十模块50SPEC_独立完整静态审阅报告_20261008.txt:162)

## 12. 原件与阅读覆盖

正文全文1–87，五SPEC/全部21AC和15任务实读；S5复核全文1–128，组合10模块语义字段结构读；接受件按决定/正文/组合/ReviewQualifications/OwnerAndExecutionBoundary结构读。真实行范围和未读字段在本组reading JSON，不把来源索引算源码全文。未读取实际隐私日志、执行导出/清理/权限修改、构建或测试。[原文B:1](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/c9/c9943c2bdead2009__S5_SYS-AUD-01_完整设计候选_v0.1.md:1) [原文R:1](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/e3/e3522cf5a9c30a6a__S5_v0.2_认证双根独立复审与最小问题报告_20261008.txt:1) [原文A:8](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/a0/a0bc63cf4f218d81__UA-20261008-S5-SYS-AUD-01-STATIC-MD02.json:8)

机器合同见 [platform-engineering-contracts.json](D:/CP6/docs/CP6_开发设计文档_20261010/contracts/platform-engineering-contracts.json)，精确阅读记录见 [platform-engineering-reading.json](D:/CP6/docs/CP6_开发设计文档_20261010/evidence/platform-engineering-reading.json)。

**当前必要材料归并状态。** 本册为 `required_materials_consolidated`：选定正文、规范附件及准确接受/复核范围已按直接实读、结构化语义或精确复用归并。身份清单核对不等底层源码全文阅读；历史草稿、候选实现和Office/HTML视觉未核范围仍单列。可复查[逐件阅读与处置](D:/CP6/docs/CP6_开发设计文档_20261010/evidence/platform-engineering-reading.json)及[本组闭包台账](D:/CP6/docs/CP6_开发设计文档_20261010/worklogs/platform-engineering-progress.json)。当前必需规范未读清单为空；具体Owner采用、参数/物理接口、真实运行和本节已有来源缺口不因整理闭包而改变。
