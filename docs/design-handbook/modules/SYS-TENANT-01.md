# SYS-TENANT-01 · 租户权威、暂停恢复与跨系统映射

整理状态：`core_semantics_consolidated`。当前静态规范展开；候选字段/行为不冒现代码或运行已采用。

## 1. 业务目的、操作者及Owner边界

Main是租户主数据唯一权威；CRM消费注册映射/身份投影，Platform只供上下文和接收原语。平台管理员建/改/停/复租户；tenant管理员只管自身用户/角色，不得读平台花名册；注册身份服务读获准精确快照，worker用真实service/execution，支持仅安全诊断。模拟会话即使原人是平台管理员也不能做平台管理。[原文B:5](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/4b/4b2226985953845f__S5_SYS-TENANT-01_完整设计候选_v0.1.md:5) [原文B:30](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/4b/4b2226985953845f__S5_SYS-TENANT-01_完整设计候选_v0.1.md:30)

TenantId为Main生成不可变GUID；TenantCode只是登录定位短码，Name变更不改历史归属。CRM organizationId必须经受控登记明确等于TenantId，不从同名/email域推导；region/issuer/client有自己的注册Owner，不塞进通用维护DTO。[原文B:28](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/4b/4b2226985953845f__S5_SYS-TENANT-01_完整设计候选_v0.1.md:28)

## 2. 当前规范与静态接受

选定v0.1，SHA256 `4b2226985953845f50345374e36af1a2a91319d1ae8cef388db18bdbdf79b1a7`，172行，4原SPEC/23AC/15任务。S5 v0.2混合十稿中本册原字节继承，最终独审只有认证双根新复核。[原文A:146](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/f5/f5dd2c4328512cdc__UA-20261008-S5-SYS-TENANT-01-STATIC-MD02.json:146) [原文R:14](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/e3/e3522cf5a9c30a6a__S5_v0.2_认证双根独立复审与最小问题报告_20261008.txt:14) [原文B:170](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/4b/4b2226985953845f__S5_SYS-TENANT-01_完整设计候选_v0.1.md:170)

delegate接受非亲签；02:18:29Z原逐字决定未重建，04:09:04Z恢复确认另存。正文STOPPED历史不抹，23AC NOT_RUN、任务PLANNED、实施false、Owner采用/运行UNPROVEN。[原文A:8](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/f5/f5dd2c4328512cdc__UA-20261008-S5-SYS-TENANT-01-STATIC-MD02.json:8) [原文A:299](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/f5/f5dd2c4328512cdc__UA-20261008-S5-SYS-TENANT-01-STATIC-MD02.json:299)

## 3. 字段、状态和版本轴

|对象/DTO|字段|规则|
|---|---|---|
|Sys_Tenant原表|Id=TenantId、Code≤50、Name≤200、Remark≤500、Enable、ExpireDate、TwoFactorMode、TimeZoneId≤64|共享表，不普通tenant行级过滤花名册；Enable与ExpireDate独立。[原文B:15](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/4b/4b2226985953845f__S5_SYS-TENANT-01_完整设计候选_v0.1.md:15) [原文B:32](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/4b/4b2226985953845f__S5_SYS-TENANT-01_完整设计候选_v0.1.md:32)|
|CreateTenantRequest|Code/Name/AdminUserName必填，AdminUserName≤100；Expire/Remark可空|Code业务要求大小写不敏感唯一，但DB比较/并发约束仍待证；Code创建后只读。[原文B:40](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/4b/4b2226985953845f__S5_SYS-TENANT-01_完整设计候选_v0.1.md:40)|
|UpdateTenantRequest|Name/Expire/Remark/TimeZoneId|空白timezone→null，否则运行环境可解析IANA/Windows校验；TwoFactorMode归IAM，不普通更新Id/Enable/platform权/epoch。[原文B:42](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/4b/4b2226985953845f__S5_SYS-TENANT-01_完整设计候选_v0.1.md:42)|
|受控命令候选|operationId/expectedTenantVersion/reason/requestedAction|server绑定tenant/actor/receivedAtUtc，同义原键复用、异义409。[原文B:34](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/4b/4b2226985953845f__S5_SYS-TENANT-01_完整设计候选_v0.1.md:34)|
|OperationView候选|tenantId/state/appliedVersion/auditRef/needsCredentialReset/retryable/traceId|不返回历史凭据。[原文B:57](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/4b/4b2226985953845f__S5_SYS-TENANT-01_完整设计候选_v0.1.md:57)|
|StateChangeResult候选|authorityState/propagationState/effectiveVersion/affectedCapabilityStatus/auditRef|Main状态提交不等下游即刻采用。[原文B:98](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/4b/4b2226985953845f__S5_SYS-TENANT-01_完整设计候选_v0.1.md:98)|
|TenantBindingEvidence候选|tenantId/identitySource/mappingRevision/actorSubject/sessionRef/resolvedAtUtc/bindingStatus|服务器可信绑定，不是浏览器票据。[原文B:123](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/4b/4b2226985953845f__S5_SYS-TENANT-01_完整设计候选_v0.1.md:123)|
|MappingView候选|sourceTenantRef/localOrganizationId/issuer/region/mappingRevision/sourceVersion/projectionVersion/dependencyStatus/lastVerifiedAt|证据字段mappingRevision/ownerApprovalRef/effectiveAt不冒已存在列。[原文B:145](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/4b/4b2226985953845f__S5_SYS-TENANT-01_完整设计候选_v0.1.md:145)|

候选ACTIVE/SUSPENDED/EXPIRED_POLICY_PENDING/MAPPING_NOT_READY/AUTHORITY_FENCED是展示派生，不第二主账。暂停不删租户/文件、释放编号或回滚历史；恢复生成新权威事实，不复活旧token/批准。[原文B:32](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/4b/4b2226985953845f__S5_SYS-TENANT-01_完整设计候选_v0.1.md:32)

## 4. 建立维护、暂停与恢复写集

创建：平台三道闸+action→查原operation→校非空/长度/Code/模板→同事务Tenant、tenant RoleId1、获准menu副本、首User（IsPlatformAdmin=false、MustChangePassword=true）→目标原receipt/强审计一致保存。现四对象同SaveChanges已读，但保存后另审计不是已证同commit；模板空/失效应初始化未就绪，不把无权限tenant显示可用。[原文B:46](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/4b/4b2226985953845f__S5_SYS-TENANT-01_完整设计候选_v0.1.md:46) [原文B:47](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/4b/4b2226985953845f__S5_SYS-TENANT-01_完整设计候选_v0.1.md:47) [原文B:48](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/4b/4b2226985953845f__S5_SYS-TENANT-01_完整设计候选_v0.1.md:48) [原文B:49](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/4b/4b2226985953845f__S5_SYS-TENANT-01_完整设计候选_v0.1.md:49)

临时密码仅一次受保护结果展示，不存日志/导出/operation明文。响应丢失原tenant只一个，经IAM受控重置取新凭据，不能查旧密码。维护expectedTenantVersion CAS，timezone非法E-WF-028整件拒，412保草稿重读差异。[原文B:42](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/4b/4b2226985953845f__S5_SYS-TENANT-01_完整设计候选_v0.1.md:42) [原文B:50](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/4b/4b2226985953845f__S5_SYS-TENANT-01_完整设计候选_v0.1.md:50) [原文B:51](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/4b/4b2226985953845f__S5_SYS-TENANT-01_完整设计候选_v0.1.md:51)

暂停：按稳定顺序取得全部受影响Scope+permissionDomain真实fence→锁内current state/epoch→改Enable并推进授权代次→身份快照/outbox/audit一致保存。多fence不能边提交留下部分撤权；无法原子覆盖关闭相关新接入、保原意图。所有新业务效果writer与撤权writer共门，TTL/30分钟缓存/二次GET都不代替。[原文B:84](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/4b/4b2226985953845f__S5_SYS-TENANT-01_完整设计候选_v0.1.md:84) [原文B:86](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/4b/4b2226985953845f__S5_SYS-TENANT-01_完整设计候选_v0.1.md:86)

恢复：查原终态→当前平台权核原因/Expire/订阅，不自动续期→共同门内CAS Enable+新版本，保暂停史→下游完整映射/健康投影→重新认证。worker沿原operation/input/目标版本查终态或当前门，缺依赖保持BLOCKED_WAITING_AUTHORITY，不批量重放。[原文B:92](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/4b/4b2226985953845f__S5_SYS-TENANT-01_完整设计候选_v0.1.md:92) [原文B:93](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/4b/4b2226985953845f__S5_SYS-TENANT-01_完整设计候选_v0.1.md:93) [原文B:94](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/4b/4b2226985953845f__S5_SYS-TENANT-01_完整设计候选_v0.1.md:94) [原文B:95](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/4b/4b2226985953845f__S5_SYS-TENANT-01_完整设计候选_v0.1.md:95) [原文B:96](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/4b/4b2226985953845f__S5_SYS-TENANT-01_完整设计候选_v0.1.md:96)

## 5. API与跨系统映射

现GET `/api/platform/tenant` keyword/enable/page/pageSize（page≥1,size1..200）、GET `/{id}`、POST CreateTenantRequest、PUT `/{id}` UpdateTenantRequest；E-SEC-033重码/032不存在/E-WF-028timezone沿用。POST `/{id}/suspend`、`/reactivate`只切现Enable且带外审计的范围保留。候选GET `/api/platform/tenant/operations/{operationId}`、CommandEnvelope/OperationView尚未创建，版本header/route适配由API Owner签认。[原文B:55](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/4b/4b2226985953845f__S5_SYS-TENANT-01_完整设计候选_v0.1.md:55) [原文B:57](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/4b/4b2226985953845f__S5_SYS-TENANT-01_完整设计候选_v0.1.md:57) [原文B:80](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/4b/4b2226985953845f__S5_SYS-TENANT-01_完整设计候选_v0.1.md:80) [原文B:98](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/4b/4b2226985953845f__S5_SYS-TENANT-01_完整设计候选_v0.1.md:98)

跨系统严格注册同GUID：Main TenantId、OIDC organization、client tenant许可、identity map、CRM storage organization一致；issuer准确HTTPS origin，region登记，slug只是解析入口。Main不替CRM建第二tenant，CRM session/投影不是Main账户本体。[原文B:143](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/4b/4b2226985953845f__S5_SYS-TENANT-01_完整设计候选_v0.1.md:143)

权威保存→配置Owner复核→S6bootstrap/快照→全部依赖健康才READY；同tenant改名不重建组织，slug迁移窗口明确，region/issuer/client改动独立版本/会话影响审批。S5不新建写CRM映射接口，沿S6解析/versions/snapshot与候选只读MappingView。[原文B:149](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/4b/4b2226985953845f__S5_SYS-TENANT-01_完整设计候选_v0.1.md:149) [原文B:153](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/4b/4b2226985953845f__S5_SYS-TENANT-01_完整设计候选_v0.1.md:153)

## 6. 原键、并发、传播与恢复

同key同目标同义原结果，异义409；unknown原operation/码核对，副本NOT_FOUND不证未创建。后置审计失败应“已创建，审计待补证”，不能再建tenant；现路径和候选原子增强分别留证。[原文B:34](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/4b/4b2226985953845f__S5_SYS-TENANT-01_完整设计候选_v0.1.md:34) [原文B:63](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/4b/4b2226985953845f__S5_SYS-TENANT-01_完整设计候选_v0.1.md:63)

业务先合法持门commit则历史保留；暂停先commit旧epoch新写拒。事实接收走当前服务注册接收门，可存PendingInspection不自动执行；既有结果技术投递走delivery门而非原人类新建权；历史查询走当前read/字段/PII。暂停不清Inbox/Outbox。[原文B:88](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/4b/4b2226985953845f__S5_SYS-TENANT-01_完整设计候选_v0.1.md:88)

映射同版本同内容复用，同版本异内容在checkpoint前按S6内容门记录冲突、DRIFT、Published不可用同事务；不能先checkpoint后验。恢复需新权威版本+完整对账，旧enable消息不覆盖新停用。原配置Owner修映射、S6可信快照修投影，不手改CRM副本；历史TenantId不随映射改迁。[原文B:151](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/4b/4b2226985953845f__S5_SYS-TENANT-01_完整设计候选_v0.1.md:151) [原文B:157](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/4b/4b2226985953845f__S5_SYS-TENANT-01_完整设计候选_v0.1.md:157)

## 7. 权限、租户与认证绑定

平台三道闸：先拒模拟，再claim，再DB当前Enable/IsPlatformAdmin；tenant角色不能替代。旧TenantMiddleware无有效claim保默认tenant是legacy兼容，所有新跨系统入口必须显式非空/注册/有效TenantId，缺binding或依赖unknown不得降级默认组织。[原文B:19](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/4b/4b2226985953845f__S5_SYS-TENANT-01_完整设计候选_v0.1.md:19) [原文B:117](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/4b/4b2226985953845f__S5_SYS-TENANT-01_完整设计候选_v0.1.md:117) [原文B:127](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/4b/4b2226985953845f__S5_SYS-TENANT-01_完整设计候选_v0.1.md:127)

server-derived绑定→当前read/action范围→本tenant对象→字段→实际提交门；body/header/query自称tenant不改变上下文。EF全局过滤不证明raw/bulk/import隔离；不存在/他租对象对普通人统一不可见。Sys_Tenant全表仅平台受控服务职责。[原文B:117](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/4b/4b2226985953845f__S5_SYS-TENANT-01_完整设计候选_v0.1.md:117) [原文B:119](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/4b/4b2226985953845f__S5_SYS-TENANT-01_完整设计候选_v0.1.md:119) [原文B:125](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/4b/4b2226985953845f__S5_SYS-TENANT-01_完整设计候选_v0.1.md:125)

## 8. 页面与服务器检查

沿TenantListView扩能力，不另建控制台。创建提交防重复、unknown显示确认结果，密码仅短暂展示不URL/持久cache；维护Code/Id只读，timezone说明不重算历史。暂停页显示新登录/新业务影响及未完任务保留，成功区分“Main停用”与下游待确认；Enable绿灯不掩DRIFT/fence缺口。[原文B:59](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/4b/4b2226985953845f__S5_SYS-TENANT-01_完整设计候选_v0.1.md:59) [原文B:100](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/4b/4b2226985953845f__S5_SYS-TENANT-01_完整设计候选_v0.1.md:100)

切tenant/account清页面及草稿cache、停旧轮询、跨Tab过期、重新认证加载；平台花名册不能复用作tenant目录。映射诊断Source/Projection版本分栏、lastVerified与缺依赖，普通登录只组织不可用；授权支持才看原因。[原文B:125](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/4b/4b2226985953845f__S5_SYS-TENANT-01_完整设计候选_v0.1.md:125) [原文B:153](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/4b/4b2226985953845f__S5_SYS-TENANT-01_完整设计候选_v0.1.md:153)

## 9. 固定代码与开发顺序

固定Main90c871...观察，旧Catalogue1576305非当前；比较69提交/文件恰300受API上限，不能证明完整仓差异。T01–T10表显示Create四对象SaveChanges、维护Enable后审计、三道闸、默认tenant和少量EF身份捕获边界；ExpireDate不是已建自动暂停任务。[原文B:11](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/4b/4b2226985953845f__S5_SYS-TENANT-01_完整设计候选_v0.1.md:11) [原文B:15](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/4b/4b2226985953845f__S5_SYS-TENANT-01_完整设计候选_v0.1.md:15) [原文B:16](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/4b/4b2226985953845f__S5_SYS-TENANT-01_完整设计候选_v0.1.md:16) [原文B:17](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/4b/4b2226985953845f__S5_SYS-TENANT-01_完整设计候选_v0.1.md:17) [原文B:20](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/4b/4b2226985953845f__S5_SYS-TENANT-01_完整设计候选_v0.1.md:20)

15任务：①受控写DTO/CAS/operation/Code唯一+初始化模板；②原子审计和一次凭据恢复；③tenant状态全部writer/fence/下游新版本；④每worker暂停安全点与原键；⑤逐入口绑定/legacy清单及非EF；⑥Main/CRM真实映射批准、S6内容门和S7precheckpoint采用、唯一消费者切换。[原文B:74](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/4b/4b2226985953845f__S5_SYS-TENANT-01_完整设计候选_v0.1.md:74) [原文B:111](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/4b/4b2226985953845f__S5_SYS-TENANT-01_完整设计候选_v0.1.md:111) [原文B:137](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/4b/4b2226985953845f__S5_SYS-TENANT-01_完整设计候选_v0.1.md:137) [原文B:166](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/4b/4b2226985953845f__S5_SYS-TENANT-01_完整设计候选_v0.1.md:166)

## 10. 验收场景与状态

23AC全NOT_RUN：TN01四对象原子、输入拒无残留、原键无重建/密码不回查、CAS与Code稳定、平台/模拟边界、后置审计差距；TN02暂停竞争共门、合法历史读、恢复新版本无旧session、跨库缺门关闭、原键unknown、Expire独立；TN03显式可信绑定、无默认回落、legacy与新入口分列、切组织旧草稿终止、普通人不泄他租；TN04同GUID/issuer/region、依赖不齐不READY、前置内容冲突、改名不重建、旧enable不复活、单消费者责任。[原文B:67](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/4b/4b2226985953845f__S5_SYS-TENANT-01_完整设计候选_v0.1.md:67) [原文B:104](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/4b/4b2226985953845f__S5_SYS-TENANT-01_完整设计候选_v0.1.md:104) [原文B:131](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/4b/4b2226985953845f__S5_SYS-TENANT-01_完整设计候选_v0.1.md:131) [原文B:159](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/4b/4b2226985953845f__S5_SYS-TENANT-01_完整设计候选_v0.1.md:159)

## 11. Required与未证明

真实全writer、跨库共同门、Code大小写和并发唯一、版本header兼容、初始化模板、映射配置/健康与各worker安全点仍Required。设计已固定暂停先行阻新写等语义，不能靠一个Enable开关或认证缓存声称保证完成。过期政策/具名job由Owner决定，本册不自动改Expire→Enable。[原文B:40](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/4b/4b2226985953845f__S5_SYS-TENANT-01_完整设计候选_v0.1.md:40) [原文B:51](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/4b/4b2226985953845f__S5_SYS-TENANT-01_完整设计候选_v0.1.md:51) [原文B:80](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/4b/4b2226985953845f__S5_SYS-TENANT-01_完整设计候选_v0.1.md:80) [原文B:86](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/4b/4b2226985953845f__S5_SYS-TENANT-01_完整设计候选_v0.1.md:86) [原文B:166](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/4b/4b2226985953845f__S5_SYS-TENANT-01_完整设计候选_v0.1.md:166)

## 12. 证据与阅读覆盖

本次正文1–100、101–172全文；4SPEC/23AC/15任务与来源表实读。S5复审全文1–128；接受关键完整JSON键结构读，精确范围见reading JSON。未执行建tenant、暂停/恢复、账号/数据库/配置、项目脚本或测试。[原文B:1](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/4b/4b2226985953845f__S5_SYS-TENANT-01_完整设计候选_v0.1.md:1) [原文R:1](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/e3/e3522cf5a9c30a6a__S5_v0.2_认证双根独立复审与最小问题报告_20261008.txt:1) [原文A:8](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/f5/f5dd2c4328512cdc__UA-20261008-S5-SYS-TENANT-01-STATIC-MD02.json:8)

共享合同见 [platform-engineering-contracts.json](D:/CP6/docs/CP6_开发设计文档_20261010/contracts/platform-engineering-contracts.json)，阅读范围见 [platform-engineering-reading.json](D:/CP6/docs/CP6_开发设计文档_20261010/evidence/platform-engineering-reading.json)。

**当前必要材料归并状态。** 本册为 `required_materials_consolidated`：选定正文、规范附件及准确接受/复核范围已按直接实读、结构化语义或精确复用归并。身份清单核对不等底层源码全文阅读；历史草稿、候选实现和Office/HTML视觉未核范围仍单列。可复查[逐件阅读与处置](D:/CP6/docs/CP6_开发设计文档_20261010/evidence/platform-engineering-reading.json)及[本组闭包台账](D:/CP6/docs/CP6_开发设计文档_20261010/worklogs/platform-engineering-progress.json)。当前必需规范未读清单为空；具体Owner采用、参数/物理接口、真实运行和本节已有来源缺口不因整理闭包而改变。
