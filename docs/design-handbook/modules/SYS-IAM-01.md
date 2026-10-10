# SYS-IAM-01 · 用户组织、字段授权与认证完成

整理状态：`core_semantics_consolidated`。六个SPEC均按已接受v0.2展开；新增逻辑DTO/门/错误仍是候选待实现，40AC NOT_RUN、实际采用UNPROVEN。

## 1. 业务目的、操作者与责任

Main保留身份、组织、权限权威；CRM保本地session、健康投影和自身业务执行；Platform供验证、运输、持久接缝，不建第二IAM。tenant IAM管理员以原user/role/pub-dept/pub-role-perm/sys-sso-config动作管理本tenant，不获得平台、跨tenant或业务批准权。平台管理员走带外入口，模拟分actual/effective且平台权挂起，service以真实注册身份执行特定任务。[原文B:5](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d8/d84f5873e46c4c98__S5_SYS-IAM-01_完整设计候选_v0.2.md:5) [原文B:37](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d8/d84f5873e46c4c98__S5_SYS-IAM-01_完整设计候选_v0.2.md:37)

共同顺序：认证/tenant→当前read/action→查原operation→同义terminal按当前字段可见性返回；只有新效果才进Owner最终门、版本/字段/用途、事务。历史成功不因后来撤权变未执行，当前无权字段不再返回。用途grant不替IAM安全门，任一必需门缺就禁相应新效果。[原文B:39](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d8/d84f5873e46c4c98__S5_SYS-IAM-01_完整设计候选_v0.2.md:39) [原文B:41](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d8/d84f5873e46c4c98__S5_SYS-IAM-01_完整设计候选_v0.2.md:41)

## 2. 当前组合、版本与接受范围

正文v0.2，SHA256 `d84f5873e46c4c98185677b9e8695b48bea4fed0a20b811c8433fa0fbbb90840`，375行、61836字节，Library版本**1**。S5十稿为IAM/CLIENT v0.2 +另八v0.1；IAM只修SPEC04/05认证双根，40AC/24DEV身份保留，不能新造Target或说运行通过。[原文A:253](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/4c/4c4103c34f2f17e8__UA-20261008-S5-SYS-IAM-01-STATIC-MD02.json:253) [原文B:7](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d8/d84f5873e46c4c98__S5_SYS-IAM-01_完整设计候选_v0.2.md:7) [原文R:21](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/e3/e3522cf5a9c30a6a__S5_v0.2_认证双根独立复审与最小问题报告_20261008.txt:21)

最终复核R-S5-01/02闭合、0阻塞，八册继承原完整独审而非再逐册审；组合原独审ID误拼v0后缀是N06引用笔误，准确ID与version分列，不更改源。delegate静态接受非用户亲签；02:18:29Z原逐字决定未重建，04:09:04Z恢复确认独立。正文STOPPED历史保留，实施false、40AC NOT_RUN。[原文R:5](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/e3/e3522cf5a9c30a6a__S5_v0.2_认证双根独立复审与最小问题报告_20261008.txt:5) [原文R:14](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/e3/e3522cf5a9c30a6a__S5_v0.2_认证双根独立复审与最小问题报告_20261008.txt:14) [原文R:117](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/e3/e3522cf5a9c30a6a__S5_v0.2_认证双根独立复审与最小问题报告_20261008.txt:117) [原文A:8](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/4c/4c4103c34f2f17e8__UA-20261008-S5-SYS-IAM-01-STATIC-MD02.json:8) [原文A:406](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/4c/4c4103c34f2f17e8__UA-20261008-S5-SYS-IAM-01-STATIC-MD02.json:406)

## 3. 主体、权限与认证字段

|对象|字段及身份|约束|
|---|---|---|
|User|UserId/TenantId不可变；UserName必填≤100，NickName/Email≤100可空；RoleId可空主角色，RoleIds去重；Enable、DeptId/ManagerId|组织/经理/角色须本tenant有效；密码输入秘密，空密码不reset；IsPlatformAdmin/AuthenticationEpoch/锁定/External身份/2FA秘密非普通编辑白名单。[原文B:47](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d8/d84f5873e46c4c98__S5_SYS-IAM-01_完整设计候选_v0.2.md:47)|
|Role|复合(TenantId,RoleId)，名称/说明/Enable/OrderNo|更名不变id、停用留历史；ENABLED/DISABLED与LOCKED/MUST_CHANGE_PASSWORD/2FA_PENDING分轴。[原文B:49](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d8/d84f5873e46c4c98__S5_SYS-IAM-01_完整设计候选_v0.2.md:49)|
|Dept|DeptId/TenantId不可变、DeptCode创建后只读；DeptName/LeaderId/Sort/Enable；ParentId移动动作；Path `/root/.../self/`|服务端整树重算Path；null不补当前人/默认部门。[原文B:84](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d8/d84f5873e46c4c98__S5_SYS-IAM-01_完整设计候选_v0.2.md:84)|
|Admin commands候选|operationId/expectedVersion/允许字段/reason；组织用expectedTreeVersion|UserAdminView返回statusAxes/safeAuthState/assignmentRefs，不secret。[原文B:63](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d8/d84f5873e46c4c98__S5_SYS-IAM-01_完整设计候选_v0.2.md:63) [原文B:94](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d8/d84f5873e46c4c98__S5_SYS-IAM-01_完整设计候选_v0.2.md:94)|
|FieldDecisionView候选|tenantId/subjectRef/resource/action/policyVersion/registryVersion/mappingVersion/sourceRoleVersions/sourcePermissionVersion/decisionId/evaluatedAtUtc/validUntil/fields[{sourceField,apiPath,access,canRead,canWrite,origin,dependencies}]/decisionStatus|完整域/来源版本/mapping必填；validUntil不保证撤权原子。[原文B:137](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d8/d84f5873e46c4c98__S5_SYS-IAM-01_完整设计候选_v0.2.md:137)|
|CompletionContext候选|completionRef/entryKind/tenantId/subjectId/clientKind/deviceId或Web绑定/primaryMethod/primaryEvidenceRef+time/state-grant-challenge refs/authenticationVersionAtProof/policyRef+version/适用SSOconfig版本/factorEvidence/status/enrollmentTransition/session-family-refresh绑定/status/safeReason|server受控；PASSWORD/SSO/QUICK_SWITCH_PIN；PIN专属版本须真实adapter，不能假AuthSessionVersion已含QuickPinHash。[原文B:182](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d8/d84f5873e46c4c98__S5_SYS-IAM-01_完整设计候选_v0.2.md:182)|
|SessionAuthenticationEvidence候选|tenant/user/session-refresh lineage/client-device/primaryMethod/primaryVerifiedAt/localFactorSatisfied及证据时间/authenticationVersion/authenticationPolicyVersion/SSOconfig版本/restrictedState/issuedAt/原expiry|发行时真实证据，refresh不覆当前版本洗历史；不存raw token/OTP。[原文B:219](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d8/d84f5873e46c4c98__S5_SYS-IAM-01_完整设计候选_v0.2.md:219)|
|SsoIdentityAdmissionPolicyV1候选|policyRef/version/approvedIssuer-client/EmailClaim语义/allowInitialEmailLink/allowJit/approvedJitRoleIds/issuer邮箱权威认可|缺政策仅关闭对应新绑定/JIT，不拿GET默认autoProvision=true当批准。[原文B:258](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d8/d84f5873e46c4c98__S5_SYS-IAM-01_完整设计候选_v0.2.md:258)|
|writer登记候选|writerId/repo+path+entry/actorClass/authorityDomain/affectedScopes/changedPolicyFields/old-newAffectedSubjects/transactionBoundary/identityCaptureMode/authorityEpochMode/cacheInvalidationMode/fenceParticipation/recoveryOperationRef/ownerApprovalRef/runtimeEvidenceRef|无证填UNPROVEN，不以空白等于通过。[原文B:359](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d8/d84f5873e46c4c98__S5_SYS-IAM-01_完整设计候选_v0.2.md:359)|

UserId/TenantId、BrowserSessionId/family、refresh hash/id、jti各身份分开；AuthSessionVersion/AuthenticationEpoch是认证版本，不是所有业务权限epoch。Main/CRM各自session不并账。[原文B:174](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d8/d84f5873e46c4c98__S5_SYS-IAM-01_完整设计候选_v0.2.md:174)

## 4. 用户、组织及授权配置写集

用户创建：user:add+tenant→白名单/唯一/角色组织→原密码服务哈希→MustChangePassword=true→用户/关系同事务→权威版本/审计。修改user:edit+本tenant实体+CAS，仅白名单；Password非空才沿原reset同时吊销refresh并强制改密。主RoleId非空必须含RoleIds（E-PUB-011），两者同一业务命令无半保存；禁用/移除/组织变化进相应writer门。[原文B:53](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d8/d84f5873e46c4c98__S5_SYS-IAM-01_完整设计候选_v0.2.md:53) [原文B:55](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d8/d84f5873e46c4c98__S5_SYS-IAM-01_完整设计候选_v0.2.md:55)

删除旧硬删入口需补业务引用、平台末名、审批/审计、privacy hold；有未完/保留优先停用保历史，真正删除走PRIV，不默认级联。创建/修改后的投影待同步不能用再保存修；失效失败标AUTHORIZATION_REFRESH_PENDING且关闭相应新写，CRM不编辑权威副本。[原文B:57](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d8/d84f5873e46c4c98__S5_SYS-IAM-01_完整设计候选_v0.2.md:57) [原文B:65](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d8/d84f5873e46c4c98__S5_SYS-IAM-01_完整设计候选_v0.2.md:65) [原文B:69](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d8/d84f5873e46c4c98__S5_SYS-IAM-01_完整设计候选_v0.2.md:69)

部门建立核pub-dept:add/code/parent；移动核pub-dept:edit、源目标version、不自身/子孙(E-PUB-004)，同事务自身及所有descendant Path；失效受影响用户、自定义部门范围、审批路由。删除先子部门E-PUB-002/用户E-PUB-003并补业务引用，不能孤儿升root。历史审批冻结快照不重算；依赖坏时受影响scope拒，不扩大全部。[原文B:88](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d8/d84f5873e46c4c98__S5_SYS-IAM-01_完整设计候选_v0.2.md:88) [原文B:90](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d8/d84f5873e46c4c98__S5_SYS-IAM-01_完整设计候选_v0.2.md:90) [原文B:98](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d8/d84f5873e46c4c98__S5_SYS-IAM-01_完整设计候选_v0.2.md:98)

字段授权编辑显式Access1/2/3、重复resource+field冲突拒、已登记域/expectedPolicyVersion；SaveRoleFieldPerms仅替本次涉及resource，Access1不落库，all-default必须明确resource，不能空items当清全。DataScopes原全量替该role；menu action必须属于已授menu。[原文B:143](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d8/d84f5873e46c4c98__S5_SYS-IAM-01_完整设计候选_v0.2.md:143) [原文B:145](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d8/d84f5873e46c4c98__S5_SYS-IAM-01_完整设计候选_v0.2.md:145)

## 5. API与角色、范围、字段算法

原API：GET/POST/PUT/DELETE `/api/User`、`/api/Role`，user/role query-add-edit-delete；GET/PUT `/api/pub/user-role/{userId}` UserRolesDto(RoleIds,PrimaryRoleId)，写user:edit；POST `/migrate`旧入口不执行。部门 `/api/pub/dept`树CRUD、POST `/{id}/move` MoveReq(NewParentId)、PUT `/{id}/leader` LeaderReq(LeaderId)。候选operation/CAS/impact须明确旧route adapter。[原文B:61](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d8/d84f5873e46c4c98__S5_SYS-IAM-01_完整设计候选_v0.2.md:61) [原文B:94](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d8/d84f5873e46c4c98__S5_SYS-IAM-01_完整设计候选_v0.2.md:94)

权限 `/api/pub/role-perm/{roleId}` RolePermDto(MenuIds,Actions)，写pub-role-perm:edit，E-PUB-021动作不属menu；`/data-scope/resources`、GET/PUT `/data-scope/{roleId}`，不支持E-PUB-031/自定义空E-PUB-032；`/field-perm/resources`、GET/PUT `/field-perm/{roleId}`，未注册E-PUB-041。新业务hidden不发送、readonly不可变，含非法字段变更整意图403/FIELD_WRITE_FORBIDDEN；旧StripReadOnly恢复原值行为只在明确legacy adapter保留，不谎称提交值都已保存。[原文B:143](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d8/d84f5873e46c4c98__S5_SYS-IAM-01_完整设计候选_v0.2.md:143) [原文B:147](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d8/d84f5873e46c4c98__S5_SYS-IAM-01_完整设计候选_v0.2.md:147)

**权限聚合固定算法。** 主RoleId∪Sys_UserRole；action按menuKey:action并集；DataScope对已存配置ScopeType数值MAX，自定义只对type4部门并集。MAX不等任意集合包含证明，不改成所有部门并集；新Owner解释原结果，再与tenant、online∩healthy projection、业务scope收窄。旧Main BuildAsync与CRM BuildEnabledRolesAsync只后者明确排disabled/deleted，不能称Main已全用enabled-only。[原文B:113](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d8/d84f5873e46c4c98__S5_SYS-IAM-01_完整设计候选_v0.2.md:113)

**MainStoredFieldMinThenMaterializeV1（逻辑名、未发布schema）**：[原文B:129](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d8/d84f5873e46c4c98__S5_SYS-IAM-01_完整设计候选_v0.2.md:129)

1. 核tenant/subject、有效角色及版本、同一决定完整registry域；新跨仓停用/删除role不供grant。[原文B:130](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d8/d84f5873e46c4c98__S5_SYS-IAM-01_完整设计候选_v0.2.md:130)
2. 只对真实存行按resource+field取MIN；Access1读写/2readonly/3hidden，缺行此时不补1。[原文B:115](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d8/d84f5873e46c4c98__S5_SYS-IAM-01_完整设计候选_v0.2.md:115) [原文B:131](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d8/d84f5873e46c4c98__S5_SYS-IAM-01_完整设计候选_v0.2.md:131)
3. MIN完成后，仅完整有效registry域的缺项物化1，origin REGISTERED_DEFAULT；存行来源EXPLICIT_AGGREGATE。[原文B:132](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d8/d84f5873e46c4c98__S5_SYS-IAM-01_完整设计候选_v0.2.md:132)
4. Owner签mappingVersion覆盖API JSON path/嵌套集合/list-detail/export/filter-sort-search/派生依赖；未映射不默许。[原文B:133](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d8/d84f5873e46c4c98__S5_SYS-IAM-01_完整设计候选_v0.2.md:133)
5. 与action/行/online∩healthy projection/PII/purpose/业务规则取更严交集；字段1不补edit或对象权，匿名化不复活。[原文B:134](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d8/d84f5873e46c4c98__S5_SYS-IAM-01_完整设计候选_v0.2.md:134)
6. 输出完整decision，实际Owner提交门使用当前同有效policy/epoch，不信浏览器canWrite。[原文B:135](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d8/d84f5873e46c4c98__S5_SYS-IAM-01_完整设计候选_v0.2.md:135)

|实际存行示例|结果|
|---|---|
|A缺+B3|3 hidden。|
|A缺+B2|2 readonly。|
|A2+B3|2。|
|全缺且完整有效registry|聚合后1。|
|旧真实显式1+B3|原MIN为1，不假装旧1不存在；迁移另审。|
|registry/Access/来源版本/依赖未知|新跨仓UNAVAILABLE，不空字典推1。|

示例全部来自[原文B:117](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d8/d84f5873e46c4c98__S5_SYS-IAM-01_完整设计候选_v0.2.md:117)。旧FieldPermService无resource配置no-op、hidden nullable→null/nonnullable→default、2/3写恢复原值，不能证明任意DTO/嵌套/搜索/统计均覆盖。CRM精确全字段映射未取得，不制造已完成表。[原文B:125](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d8/d84f5873e46c4c98__S5_SYS-IAM-01_完整设计候选_v0.2.md:125) [原文B:139](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d8/d84f5873e46c4c98__S5_SYS-IAM-01_完整设计候选_v0.2.md:139)

## 6. 最终认证、SSO映射与事务恢复

**AuthenticationCompletionGateV1** 由Main原认证Owner持到session/refresh/必需receipt commit；tenant/user启停、锁定、凭据/2FA、SSO/policy writer共相关门和版本。未真实采用则AUTH_SESSION_AUTHORITY_UNAVAILABLE禁相应新完成入口；认证门不替业务效果fence。[原文B:178](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d8/d84f5873e46c4c98__S5_SYS-IAM-01_完整设计候选_v0.2.md:178) [原文B:184](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d8/d84f5873e46c4c98__S5_SYS-IAM-01_完整设计候选_v0.2.md:184)

决定顺序严格不可被后面宽分支覆盖：[原文B:186](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d8/d84f5873e46c4c98__S5_SYS-IAM-01_完整设计候选_v0.2.md:186)

1. 原交互/凭据、client/tenant/subject绑定；非法/过期/已消费无持久本次结果→REAUTH_REQUIRED，不凭可猜completionRef读别人或领secret。[原文B:187](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d8/d84f5873e46c4c98__S5_SYS-IAM-01_完整设计候选_v0.2.md:187)
2. 锁内tenant/user存在启用、同tenant、锁定、device/session注册；必要到期资格当前版本ALLOW，否则UNAVAILABLE，停用/锁定/注销→DENIED，不自动Expire→Enable。[原文B:188](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d8/d84f5873e46c4c98__S5_SYS-IAM-01_完整设计候选_v0.2.md:188)
3. 当前policy/SSO/primary proof认证版本一致，凭据重置/改绑/模式变化→新认证；仅本次enrollmentTransitionRef可承认专属旧→新epoch。[原文B:189](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d8/d84f5873e46c4c98__S5_SYS-IAM-01_完整设计候选_v0.2.md:189)
4. password/fallback须当前Enforced/AllowPasswordFallback允许；SSO配置Enabled且issuer/client与证据一致；强制SSO故障不退密码。[原文B:190](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d8/d84f5873e46c4c98__S5_SYS-IAM-01_完整设计候选_v0.2.md:190)
5. 当前2FA：Mode2未绑定ENROLLMENT_REQUIRED；已绑定任意Mode SECOND_FACTOR_PENDING；Mode0/1未绑定才无需；未知Mode AUTH_POLICY_UNAVAILABLE。本版IdP acr/amr不豁免CP6本地因子。refresh不新起挑战，缺合格原证据REAUTH_REQUIRED。[原文B:178](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d8/d84f5873e46c4c98__S5_SYS-IAM-01_完整设计候选_v0.2.md:178) [原文B:191](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d8/d84f5873e46c4c98__S5_SYS-IAM-01_完整设计候选_v0.2.md:191)
6. 已满足因子后凭据限制：密码改密受限会话只实际最小改密/退出/安全状态白名单，无普通业务/refresh/FullAuth，未采用则AUTH_RESTRICTED_SESSION_UNAVAILABLE；SSO占位密码年龄不强迫本地改密，但MustChangePassword=true仍ACCOUNT_REMEDIATION_REQUIRED，SSO-only不任意设密码。PIN/refresh遇限制完整重认证/补救。[原文B:192](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d8/d84f5873e46c4c98__S5_SYS-IAM-01_完整设计候选_v0.2.md:192)
7. 全满足SESSION_ALLOWED，记录准确版本/方法/factor/session，只commit后输出普通cookie或TokenSessionDto。[原文B:193](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d8/d84f5873e46c4c98__S5_SYS-IAM-01_完整设计候选_v0.2.md:193)

状态轴AUTHENTICATION_REQUIRED/ENROLLMENT_REQUIRED/SECOND_FACTOR_PENDING/PASSWORD_CHANGE_REQUIRED/ACTIVE/DENIED/REAUTH_REQUIRED/UNAVAILABLE/COMPLETION_UNKNOWN，只有SESSION_ALLOWED→ACTIVE。WebSSO需要因子写pending cookie并同站挑战；native callback仅grant准备，exchange需要因子返回NativeAuthResult pending且Session空，不支持客户端升级/拒而不降级成功。所有Web/native密码、SSO、pending、family/无family/native refresh、quick-switch精确矩阵收录机器合同原文199–213。[原文B:195](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d8/d84f5873e46c4c98__S5_SYS-IAM-01_完整设计候选_v0.2.md:195) [原文B:199](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d8/d84f5873e46c4c98__S5_SYS-IAM-01_完整设计候选_v0.2.md:199)

**SSO五分支。** 原验证code/PKCE、state、签名issuer/audience/寿命/nonce后sub/email；旧email_verified缺失未必拒，是旧边界。新身份准入固定：[原文B:256](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d8/d84f5873e46c4c98__S5_SYS-IAM-01_完整设计候选_v0.2.md:256) [原文B:260](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d8/d84f5873e46c4c98__S5_SYS-IAM-01_完整设计候选_v0.2.md:260)

|分支|当前版本唯一决定|
|---|---|
|tenant+issuer+sub精确命中唯一|复用原身份，不按新email迁移；不唯一SSO_IDENTITY_CONFLICT。[原文B:262](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d8/d84f5873e46c4c98__S5_SYS-IAM-01_完整设计候选_v0.2.md:262)|
|无精确命中，同tenant唯一case-insensitive email且未绑|allowInitialEmailLink、获准issuer邮箱权威、明确email_verified=true、EmailClaim策略、当前user/tenant及绑定仍空；同事务初绑不改role/platform；缺许可/证据拒，不JIT备用。[原文B:263](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d8/d84f5873e46c4c98__S5_SYS-IAM-01_完整设计候选_v0.2.md:263)|
|email歧义、另一issuer/sub、残缺Provider冲突|E-SEC-029/SSO_IDENTITY_CONFLICT，不选第一、不清旧绑定、不转JIT。[原文B:264](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d8/d84f5873e46c4c98__S5_SYS-IAM-01_完整设计候选_v0.2.md:264)|
|精确和email均零|AutoProvision+allowJit+明确verified+可信语义+DefaultRole本tenant当前Enable且获批+唯一约束；server置tenant/外部身份/role，Enabletrue/Platformfalse/MustChangefalse+随机占位hash，无真实本地密码/fallback权。[原文B:265](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d8/d84f5873e46c4c98__S5_SYS-IAM-01_完整设计候选_v0.2.md:265)|
|关闭/空role/他tenant停用已删未批role/username冲突/处理中版本变|E-SEC-026/SSO_DEFAULT_ROLE_UNAVAILABLE/CONFLICT或REAUTH；不默认Role1或无角色新建，修配置后新SSO。[原文B:266](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d8/d84f5873e46c4c98__S5_SYS-IAM-01_完整设计候选_v0.2.md:266)|

初绑/JIT在实际Main同事务：current config/policy→稳定身份根+目标/defaultRole版本→唯一/现态→写identity→IdentityChangeCapture+版本/outbox→映射receipt/强审计；旧SaveChanges/best-effort不冒新保证。IDENTITY_BOUND/USER_PROVISIONED独立于session ACTIVE；commit后失败保真实identity，新SSO按tenant+issuer+sub/原receipt复用，原callback不可重放；unknown未证未执行不再造人。[原文B:270](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d8/d84f5873e46c4c98__S5_SYS-IAM-01_完整设计候选_v0.2.md:270) [原文B:272](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d8/d84f5873e46c4c98__S5_SYS-IAM-01_完整设计候选_v0.2.md:272)

**Refresh及未知恢复。** 保存真实发行时SessionAuthenticationEvidence；旧缺证据REAUTH，不当前信息回填历史MFA。新轮转在session原事务先认证门再family→token锁（无family真实lineage），失效旧token+replacement+receipt原子后发。旧adapter已Rotate后device/policy拒时禁新token/FullAuth、撤准确replacement并封closed attempt；撤销unknown隔离该session代次，不恢复旧token、不误全撤其他family。[原文B:219](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d8/d84f5873e46c4c98__S5_SYS-IAM-01_完整设计候选_v0.2.md:219) [原文B:221](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d8/d84f5873e46c4c98__S5_SYS-IAM-01_完整设计候选_v0.2.md:221) [原文B:223](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d8/d84f5873e46c4c98__S5_SYS-IAM-01_完整设计候选_v0.2.md:223)

state/code/grant/pending/OTP一次性。SSO失败重发交互；grant耗用响应丢只原client安全查状态，不能再领raw；因子错且pending未耗/过期/变版可按限流新挑战，OTP已成功不能重用；enroll已commit保绑定，下次verify不setup覆盖。session/响应unknown有凭据先profile核，无凭据先Owner封闭无法交付精确attempt/session再新认证；封闭未知隔离等待，不换ID绕未决。业务unknown始终原业务operation，不随重登换键。[原文B:231](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d8/d84f5873e46c4c98__S5_SYS-IAM-01_完整设计候选_v0.2.md:231) [原文B:233](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d8/d84f5873e46c4c98__S5_SYS-IAM-01_完整设计候选_v0.2.md:233) [原文B:235](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d8/d84f5873e46c4c98__S5_SYS-IAM-01_完整设计候选_v0.2.md:235) [原文B:239](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d8/d84f5873e46c4c98__S5_SYS-IAM-01_完整设计候选_v0.2.md:239)

**FullAuth/PIN。** 仅当前完整新交互SESSION_ALLOWED，device active/client一致、非restricted/refresh/PIN，且设备/session协调commit才开原12小时并清失败计数。刷新/PIN不延长；当前policy/tenant/device失效即不可用。quick-switch还需明示QuickSwitchAllowed、原Shared窗口、失败<5、真实PIN版本；Mode2、目标已2FA、强制SSO或改密限制一律完整登录，不借前人的MFA。缺PIN版本/政策关PIN，完整登录仍可走。[原文B:182](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d8/d84f5873e46c4c98__S5_SYS-IAM-01_完整设计候选_v0.2.md:182) [原文B:211](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d8/d84f5873e46c4c98__S5_SYS-IAM-01_完整设计候选_v0.2.md:211) [原文B:213](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d8/d84f5873e46c4c98__S5_SYS-IAM-01_完整设计候选_v0.2.md:213) [原文B:237](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d8/d84f5873e46c4c98__S5_SYS-IAM-01_完整设计候选_v0.2.md:237)

## 7. 平台权、模拟与所有writer共同门

IsPlatformAdmin带外权，不从Role1/role:edit/普通DTO取得。三道闸：impersonator_id先E-SEC-034→claim缺E-SEC-031→DB实际user仍Enable/Platform；服务/UID不可信拒。Grant现幂等true、Revoke末名E-SEC-037；候选同管理Scope fence串行核末名集合+保存，user:disable/delete等也共不变量，计数代码存在不等并发已证。[原文B:309](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d8/d84f5873e46c4c98__S5_SYS-IAM-01_完整设计候选_v0.2.md:309) [原文B:311](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d8/d84f5873e46c4c98__S5_SYS-IAM-01_完整设计候选_v0.2.md:311)

模拟开始目标tenant/user有效且同tenant；旧未指定user选首个主Role1是legacy，候选必须明确target/reason/实际actor/effective/session/期限/范围，非人工审批代理。开始拉黑替换的平台access jti，发target且Platformfalse/impersonator_id短access，仅换access cookie，refresh/CSRF留原；结束拉黑imp jti并DB重核原人再发自身身份，失权不恢复平台。未取得PlatformAdmin实际controller route不猜URL。[原文B:315](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d8/d84f5873e46c4c98__S5_SYS-IAM-01_完整设计候选_v0.2.md:315) [原文B:317](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d8/d84f5873e46c4c98__S5_SYS-IAM-01_完整设计候选_v0.2.md:317) [原文B:321](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d8/d84f5873e46c4c98__S5_SYS-IAM-01_完整设计候选_v0.2.md:321)

原writer完整责任矩阵338–357已逐行实读并原文收入机器合同；明确区分显式cache失效、EF捕获、共同门。UserController、Role/Menu旧入口、Dept、tenant、SSO config/AllowPasswordFallback、平台权、PRIV、raw/bulk/import/工具都不能由少量EF捕获推全覆盖；SaveMenuActions未在capture switch、UserName/Email不在原user属性列表等具体缺口保留。[原文B:338](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d8/d84f5873e46c4c98__S5_SYS-IAM-01_完整设计候选_v0.2.md:338)

业务共同门顺序Scope/permissionDomain fence→Owner operation根→业务/queue→receipt/outbox→audit持至commit；撤权/role/delegation/SOD/tenant状态同fence推进epoch。cache删除可提交后广播补偿，新写必须持久epoch+门；30分钟滑动cache持续命中不是撤权硬上限。跨库Main/CRM无等价协议采用禁对应新业务/下游效果AUTHORITY_FENCE_UNAVAILABLE；历史read、注册事实receive、既有结果delivery各自门保留。[原文B:153](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d8/d84f5873e46c4c98__S5_SYS-IAM-01_完整设计候选_v0.2.md:153) [原文B:361](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d8/d84f5873e46c4c98__S5_SYS-IAM-01_完整设计候选_v0.2.md:361)

## 8. 页面、安全投影与认证API适配

用户/角色页同选主附角色，停用role仅历史不可新授；Main保存/投影待同步分列；412留非secret草稿，不旧实体自动重发。部门拖动只是准备移动，显人数/旧新父/引用类别，不能重算历史批准人。权限页显完整registry/policy与显式/默认来源，未知域未就绪，失权从DOM/state/export缓存/search提示清除；不把hidden输出0冒真实值。[原文B:65](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d8/d84f5873e46c4c98__S5_SYS-IAM-01_完整设计候选_v0.2.md:65) [原文B:96](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d8/d84f5873e46c4c98__S5_SYS-IAM-01_完整设计候选_v0.2.md:96) [原文B:151](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d8/d84f5873e46c4c98__S5_SYS-IAM-01_完整设计候选_v0.2.md:151)

原SSO GET/PUT `/api/sys/sso-config`为sys-sso-config:query/edit；Authority/ClientId/Scopes/EmailClaim/Enabled/Enforced/AutoProvision/DefaultRoleId，空ClientSecret保持原密文，GET hasSecret不回secret。candidate ConfigChangeEnvelope加expectedConfigVersion/policyRef-version/operationId/reason，开JIT须当前批准role。[原文B:254](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d8/d84f5873e46c4c98__S5_SYS-IAM-01_完整设计候选_v0.2.md:254) [原文B:274](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d8/d84f5873e46c4c98__S5_SYS-IAM-01_完整设计候选_v0.2.md:274)

2FA GET/PUT `/api/sys/two-factor-policy`，PUT user:edit Mode0/1/2；现Auth 2fa/setup/enroll/verify/email-otp/status及setup-self/enroll-self/disable-self/email-otp-self，Code/Method/必要CurrentPassword保持。Mode0也不豁免已绑用户；setup只是准备，专属pending enroll确认旧→新epoch，其他setup/reset/policy变化旧pending无效。self禁用须再验证且不绕tenant强制；admin reset强审计且不是豁免重绑。未知Method或依赖不足pending/UNAVAILABLE，不临时session。[原文B:278](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d8/d84f5873e46c4c98__S5_SYS-IAM-01_完整设计候选_v0.2.md:278) [原文B:280](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d8/d84f5873e46c4c98__S5_SYS-IAM-01_完整设计候选_v0.2.md:280) [原文B:282](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d8/d84f5873e46c4c98__S5_SYS-IAM-01_完整设计候选_v0.2.md:282) [原文B:284](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d8/d84f5873e46c4c98__S5_SYS-IAM-01_完整设计候选_v0.2.md:284) [原文B:288](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d8/d84f5873e46c4c98__S5_SYS-IAM-01_完整设计候选_v0.2.md:288)

Web普通仍原cookie，token不body/URL；SSO pending只受保护pending/CSRF和同站safe状态；native pending Session空、token安全存储。UI分SSO已验证/绑定/第二因子/受限/ACTIVE/未就绪/UNKNOWN，503不退密码无MFA；SSO拒不露同email候选用户/角色；logout多Tab清旧数据不自动重登。模拟横幅跨页常驻，结束unknown先查session，不横幅消失即平台恢复。[原文B:229](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d8/d84f5873e46c4c98__S5_SYS-IAM-01_完整设计候选_v0.2.md:229) [原文B:239](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d8/d84f5873e46c4c98__S5_SYS-IAM-01_完整设计候选_v0.2.md:239) [原文B:290](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d8/d84f5873e46c4c98__S5_SYS-IAM-01_完整设计候选_v0.2.md:290) [原文B:292](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d8/d84f5873e46c4c98__S5_SYS-IAM-01_完整设计候选_v0.2.md:292) [原文B:323](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d8/d84f5873e46c4c98__S5_SYS-IAM-01_完整设计候选_v0.2.md:323)

## 9. 开发顺序与固定源码事实

固定Main90c871...、CRMc778a3...、Platform30bd...来源表17–31；Main compare恰300条有上限，不全仓无遗漏。旧WebSSO直接cookie/native exchange直接IssueSession、无family不走family认证version核、native refresh先Rotate后device均是待改变事实；不能把本册统一门写成现代码已实现。[原文B:13](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d8/d84f5873e46c4c98__S5_SYS-IAM-01_完整设计候选_v0.2.md:13) [原文B:17](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d8/d84f5873e46c4c98__S5_SYS-IAM-01_完整设计候选_v0.2.md:17) [原文B:176](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d8/d84f5873e46c4c98__S5_SYS-IAM-01_完整设计候选_v0.2.md:176)

24任务按依赖：①用户role白名单/组织树原子+引用；②完整field registry/mapping/有效决策与封闭编辑；③全writer持久epoch/fence及跨库关闭；④CompletionContext/发行证据/全部入口最终门及安全UNKNOWN；⑤SSO五分支/身份同事务与enroll专属转换；⑥cookie/native能力及CLIENT FullAuth；⑦平台末名/模拟；⑧S6/S7确切版本/内容门/Owner采用和40AC。政策已确定，不为实现另开更宽分支。[原文B:78](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d8/d84f5873e46c4c98__S5_SYS-IAM-01_完整设计候选_v0.2.md:78) [原文B:107](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d8/d84f5873e46c4c98__S5_SYS-IAM-01_完整设计候选_v0.2.md:107) [原文B:168](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d8/d84f5873e46c4c98__S5_SYS-IAM-01_完整设计候选_v0.2.md:168) [原文B:248](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d8/d84f5873e46c4c98__S5_SYS-IAM-01_完整设计候选_v0.2.md:248) [原文B:303](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d8/d84f5873e46c4c98__S5_SYS-IAM-01_完整设计候选_v0.2.md:303) [原文B:332](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d8/d84f5873e46c4c98__S5_SYS-IAM-01_完整设计候选_v0.2.md:332) [原文B:363](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d8/d84f5873e46c4c98__S5_SYS-IAM-01_完整设计候选_v0.2.md:363)

## 10. 验收场景与状态

40AC均NOT_RUN：IA01主附role原子/tenant/密码reset/失效/删除hold/原键；IA02整树无环/关系同tenant/组织scope失效/历史冻结/缺父关闭/CAS；IA03十场景完整覆盖存行MIN后default、显式旧1、未知registry、资源局部替换、legacy mask、新DTO整拒、所有映射和cache门；IA04六场景覆盖入口矩阵/各refresh谱系证据/current变更/enroll专属转换/一次性凭据unknown/FullAuth/精确replacement隔离；IA05五SSO分支+Mode矩阵+身份同事务+UNKNOWN+配置变更；IA06平台三门/带外权限/末名并发/真实模拟actor/精确cookie处理/跨Tab。[原文B:71](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d8/d84f5873e46c4c98__S5_SYS-IAM-01_完整设计候选_v0.2.md:71) [原文B:100](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d8/d84f5873e46c4c98__S5_SYS-IAM-01_完整设计候选_v0.2.md:100) [原文B:157](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d8/d84f5873e46c4c98__S5_SYS-IAM-01_完整设计候选_v0.2.md:157) [原文B:241](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d8/d84f5873e46c4c98__S5_SYS-IAM-01_完整设计候选_v0.2.md:241) [原文B:296](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d8/d84f5873e46c4c98__S5_SYS-IAM-01_完整设计候选_v0.2.md:296) [原文B:325](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d8/d84f5873e46c4c98__S5_SYS-IAM-01_完整设计候选_v0.2.md:325)

开发验证须为这些真实输入/事务/竞争留下证据，正文静态示例和旧test源码不计PASS。[原文B:375](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d8/d84f5873e46c4c98__S5_SYS-IAM-01_完整设计候选_v0.2.md:375)

## 11. Required与明确未完成项

Required-IAM-01仅给政策和已读writer责任矩阵，完整真实writer+fence/cache/epoch运行仍UNPROVEN。Required-IAM-02算法已确定，CRM逐resource字段mapping/版本/全部消费者采用仍RequiredFenced。未知registry/版本/映射关对应能力，合法无依赖历史read不扩大关闭。[原文B:367](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d8/d84f5873e46c4c98__S5_SYS-IAM-01_完整设计候选_v0.2.md:367) [原文B:369](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d8/d84f5873e46c4c98__S5_SYS-IAM-01_完整设计候选_v0.2.md:369)

实际tenant的SSO邮箱权威/allowInitialEmailLink/allowJit/defaultRole许可、QuickSwitchAllowed/PIN版本、restricted路由、session/设备同事务、所有认证writer及真实法律privacy例外需Owner证据；不编造值，也不借缺值保留不确定算法。S6已接受precheckpoint/typed replay/原operation优先/最终门未被本册重写。[原文B:182](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d8/d84f5873e46c4c98__S5_SYS-IAM-01_完整设计候选_v0.2.md:182) [原文B:184](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d8/d84f5873e46c4c98__S5_SYS-IAM-01_完整设计候选_v0.2.md:184) [原文B:192](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d8/d84f5873e46c4c98__S5_SYS-IAM-01_完整设计候选_v0.2.md:192) [原文B:258](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d8/d84f5873e46c4c98__S5_SYS-IAM-01_完整设计候选_v0.2.md:258) [原文B:371](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d8/d84f5873e46c4c98__S5_SYS-IAM-01_完整设计候选_v0.2.md:371)

## 12. 原件与真实阅读范围

正文375行全文按1–90/91–173/174–216/217–277/278–375五段读完，补齐此前234–250未读段；六SPEC、40AC、24DEV、全部writer行/SSO与session分支实读。S5最终复核全文128行；本Target接受完整选定决定/正文/组合/ReviewQualifications/执行边界JSON键结构读。精确模式/字段/入口矩阵和writer原段收入机器合同用于后续对照，不算再一次运行验证。[原文B:1](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d8/d84f5873e46c4c98__S5_SYS-IAM-01_完整设计候选_v0.2.md:1) [原文R:1](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/e3/e3522cf5a9c30a6a__S5_v0.2_认证双根独立复审与最小问题报告_20261008.txt:1) [原文A:8](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/4c/4c4103c34f2f17e8__UA-20261008-S5-SYS-IAM-01-STATIC-MD02.json:8)

没有认证请求、账号/权限/密钥操作、源码执行、测试/构建/Actions。共享合同见 [platform-engineering-contracts.json](D:/CP6/docs/CP6_开发设计文档_20261010/contracts/platform-engineering-contracts.json)，阅读范围见 [platform-engineering-reading.json](D:/CP6/docs/CP6_开发设计文档_20261010/evidence/platform-engineering-reading.json)。

**当前必要材料归并状态。** 本册为 `required_materials_consolidated`：选定正文、规范附件及准确接受/复核范围已按直接实读、结构化语义或精确复用归并。身份清单核对不等底层源码全文阅读；历史草稿、候选实现和Office/HTML视觉未核范围仍单列。可复查[逐件阅读与处置](D:/CP6/docs/CP6_开发设计文档_20261010/evidence/platform-engineering-reading.json)及[本组闭包台账](D:/CP6/docs/CP6_开发设计文档_20261010/worklogs/platform-engineering-progress.json)。当前必需规范未读清单为空；具体Owner采用、参数/物理接口、真实运行和本节已有来源缺口不因整理闭包而改变。
