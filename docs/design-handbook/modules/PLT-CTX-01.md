# PLT-CTX-01 · 请求上下文、租户映射与可信服务身份

整理状态：`core_semantics_consolidated`。静态设计接受不升级为实现或运行接受。

## 1. 业务目的、操作者和Owner边界

Platform提供不可变上下文、解析器接线和格式验证；Main IAM拥有tenant/user/service注册、权限与撤权，CRM拥有映射和本地身份投影，业务Owner拥有最终效果和数据库路由。注册的HTTP/worker服务是操作者；不能拿客户端tenant头、最后登录用户或假UserId当可信服务身份。[原文B:26](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/26/265aecc406e50ca8__PLT-CTX-01_完整静态设计_v0.1.md:26) [原文B:30](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/26/265aecc406e50ca8__PLT-CTX-01_完整静态设计_v0.1.md:30) [原文B:48](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/26/265aecc406e50ca8__PLT-CTX-01_完整静态设计_v0.1.md:48)
请求Snapshot用于本次身份解释，不是永久写许可。新业务必须通过Owner的EvaluateAndHold共同安全门至commit；原结果读取、可信事实接收、已提交结果技术重投分别授权。[原文B:38](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/26/265aecc406e50ca8__PLT-CTX-01_完整静态设计_v0.1.md:38) [原文B:40](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/26/265aecc406e50ca8__PLT-CTX-01_完整静态设计_v0.1.md:40)

## 2. 选定规范与接受范围

当前正文v0.1，SHA256 `265aecc406e50ca8117d1823d09fac61fa5694894baecdaff31df87013f8eb6f`，122行，纳入S7 v0.2.1七稿混合组合：CTX/OBS/RELEASE/TEST v0.1、HTTP/MSG v0.2、DEL v0.2.1。接受记录Disposition为ACCEPTED_CURRENT_MARKDOWN_STATIC_DETAILED_DESIGN。[原文A:167](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/be/bef16a902d6fb364__UA-20261008-S7-PLT-CTX-01-STATIC-MD02.json:167) [原文R:9](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/39/397b935b08bca80e__S7_Platform_v0.2.1_七稿组合完整静态接受_独立复审结论.txt:9)
delegate静态接受，非用户亲签；原02:18:29Z决定未恢复逐字或messageId，04:09:04Z另有恢复确认。组合索引中DEL的pending/null保留上传前历史，最终接受以精确正文+独审+接受记录为准。258项组合AC全NOT_RUN，本Target 28 AC；运行和Owner采用UNPROVEN、实施/部署未授权。[原文A:8](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/be/bef16a902d6fb364__UA-20261008-S7-PLT-CTX-01-STATIC-MD02.json:8) [原文A:332](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/be/bef16a902d6fb364__UA-20261008-S7-PLT-CTX-01-STATIC-MD02.json:332) [原文R:132](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/39/397b935b08bca80e__S7_Platform_v0.2.1_七稿组合完整静态接受_独立复审结论.txt:132)

## 3. 对象、字段与不可替代的身份

|对象|精确字段与责任|
|---|---|
|Snapshot/IRequestContext|TenantId为非空Guid；UserId为真实映射人或null；Subject/Audience/CorrelationId非空；空TokenId归null；IsPublic由受信resolver分类。复制后不可变，不能臆加必需issuer/region/epoch字段。[原文B:30](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/26/265aecc406e50ca8__PLT-CTX-01_完整静态设计_v0.1.md:30)|
|ContextResolutionEvidenceV1（候选独立对象）|resolutionId Guid，hostId/profileId 1..128，registrationRef 1..256，registrationVersion 1..128，issuer精确HTTPS，tenantId，登记region，actorKind Human/Service/Public，authenticatedAtUtc；可选sourceIdentityVersion/permissionDecisionRef，contextContractVersion=ctx-v1。只是证据。[原文B:32](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/26/265aecc406e50ca8__PLT-CTX-01_完整静态设计_v0.1.md:32)|
|MappingBindingV1|tenant与organization对应、region/issuer/profile/config版本、validFrom、state、Owner批准及内部databaseRef；状态Unconfigured/Validated/Active/Suspended/Retired；不得装连接串。[原文B:66](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/26/265aecc406e50ca8__PLT-CTX-01_完整静态设计_v0.1.md:66)|
|AppContextAdoptionV1|app/host/repoSHA、package版本digest、resolver/scheme/profile、context版本、tenantScope/registration/Public集合、middleware、DB路由/read policy/final gate/all writers及运行证据，逐层填写。[原文B:102](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/26/265aecc406e50ca8__PLT-CTX-01_完整静态设计_v0.1.md:102)|

## 4. 状态流程、入口与本地写集

状态Unresolved→Authenticated→Resolved→ApplicationEvaluated→Disposed；错误为Rejected/Unavailable。入口先correlation，再authentication、受信注册resolver、Snapshot构造、scoped accessor、应用授权，finally清Current。并发只传不可变值，不共享可变Current；消息持久化按消息合同，不能存HttpContext。[原文B:44](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/26/265aecc406e50ca8__PLT-CTX-01_完整静态设计_v0.1.md:44) [原文B:46](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/26/265aecc406e50ca8__PLT-CTX-01_完整静态设计_v0.1.md:46) [原文B:50](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/26/265aecc406e50ca8__PLT-CTX-01_完整静态设计_v0.1.md:50)
worker每任务独立scope，originalRequester只作审计；Public走明确allowlist，无租户健康探针独立路由，不能以Guid.Empty访问业务库。上下文自身不创建业务写集；最终门、业务账/原receipt/Outbox/audit仍由应用同事务负责。[原文B:48](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/26/265aecc406e50ca8__PLT-CTX-01_完整静态设计_v0.1.md:48) [原文B:108](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/26/265aecc406e50ca8__PLT-CTX-01_完整静态设计_v0.1.md:108)

## 5. 接口、消息与依赖

现API为AddCp6RequestContext<TResolver>、UseCp6RequestContext、IRequestContextResolver.ResolveAsync(HttpContext,CancellationToken)→ValueTask<Snapshot?>。不提供公共set-tenant端点。[原文B:52](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/26/265aecc406e50ca8__PLT-CTX-01_完整静态设计_v0.1.md:52)
服务JWT profile登记Authority/Issuer/Audiences；HTTPS默认true，ClockSkew默认1分钟、允许0..5；URI不得含userinfo/query/fragment。RS256、typ=at+jwt、kid非空，验证签名/过期/issuer/audience；MapInboundClaims=false、TryAllIssuerKeys=false；iss/aud/sub/tenant_id/jti/iat/nbf/exp和NumericDate顺序等逐项校验，未知key受控刷新，不能先放行。[原文B:84](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/26/265aecc406e50ca8__PLT-CTX-01_完整静态设计_v0.1.md:84) [原文B:86](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/26/265aecc406e50ca8__PLT-CTX-01_完整静态设计_v0.1.md:86)
加密校验后还需当前service登记、tenant/region/scope/token撤权，再建service context与应用分类门。Main C02.Services/originalBearer与CRM browser scheme不可互换。[原文B:88](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/26/265aecc406e50ca8__PLT-CTX-01_完整静态设计_v0.1.md:88)

## 6. 事务、幂等、并发及恢复

请求已取的映射是固定路由证据；region迁移需新版本和受控切换窗口，旧in-flight保原route/epoch和原结果读取，不双写或自动切另一region。当前新请求走新映射，新写仍过最终门。[原文B:64](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/26/265aecc406e50ca8__PLT-CTX-01_完整静态设计_v0.1.md:64) [原文B:68](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/26/265aecc406e50ca8__PLT-CTX-01_完整静态设计_v0.1.md:68) [原文B:70](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/26/265aecc406e50ca8__PLT-CTX-01_完整静态设计_v0.1.md:70)
Context无跨库授权提交保证；缓存、TTL、两次远程GET或普通lease不能替共同fence。CRM precheckpoint仍须Registration→ReconciliationState→PublishedState→Projection→Inbox→Checkpoint→Observation/Conflict的应用共同事务；CTX存在不证明内容一致。[原文B:38](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/26/265aecc406e50ca8__PLT-CTX-01_完整静态设计_v0.1.md:38) [原文B:108](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/26/265aecc406e50ca8__PLT-CTX-01_完整静态设计_v0.1.md:108)

## 7. 权限、租户和敏感信息

tenant来自注册authority且issuer/region精确匹配；CRM PlatformOrganizationContextAdapter复制region并标GatewayValidated，并不证明region真的已验证。禁止以adapter名字作安全证据。[原文B:64](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/26/265aecc406e50ca8__PLT-CTX-01_完整静态设计_v0.1.md:64)
401用于无效认证、403用于已知身份拒绝、503用于authority不可用，不能把依赖不可用伪造成已撤权；现resolver null/invalid导致403的兼容语义不全局改401。日志只放safeCode/引用，subject/jti/tenant等高基数不作metric标签，禁止输出token。[原文B:34](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/26/265aecc406e50ca8__PLT-CTX-01_完整静态设计_v0.1.md:34) [原文B:46](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/26/265aecc406e50ca8__PLT-CTX-01_完整静态设计_v0.1.md:46) [原文B:90](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/26/265aecc406e50ca8__PLT-CTX-01_完整静态设计_v0.1.md:90)

## 8. 页面与服务器校验

不做上下文编辑器。组织切换遵循S6登出再登录，晚到响应清理旧组织数据；诊断只读展示host/profile/registration/mapping/config版本和各层采用状态。PUBLIC和service分类不由UI选择。[原文B:34](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/26/265aecc406e50ca8__PLT-CTX-01_完整静态设计_v0.1.md:34) [原文B:48](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/26/265aecc406e50ca8__PLT-CTX-01_完整静态设计_v0.1.md:48) [原文B:54](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/26/265aecc406e50ca8__PLT-CTX-01_完整静态设计_v0.1.md:54)
profile无效在启动拒绝；缺映射/登记/门时明确未就绪，不能自动降匿名、默认租户或旧region。新候选安全码CONTEXT_BINDING_CHANGED/CONTEXT_MAPPING_UNAVAILABLE需由应用adapter采用后才可对外宣称。[原文B:70](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/26/265aecc406e50ca8__PLT-CTX-01_完整静态设计_v0.1.md:70) [原文B:90](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/26/265aecc406e50ca8__PLT-CTX-01_完整静态设计_v0.1.md:90)

## 9. 实施顺序与固定源码差距

先冻结每Host的真实认证/映射/路由与所有writer清单，再实现独立binding证据与最终门、CRM共同事务内容门，最后切换单writer和准备真实资格化。现Main CrmIdentityConfiguration条件启用；固定CRM Program拥有自己的auth/transport/DB，未调用AddCrmPlatformAuthentication，类存在不等Host接入。[原文B:102](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/26/265aecc406e50ca8__PLT-CTX-01_完整静态设计_v0.1.md:102) [原文B:104](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/26/265aecc406e50ca8__PLT-CTX-01_完整静态设计_v0.1.md:104)
兼容保旧Snapshot构造器及C02 wire；回退保receipt、撤权和原消息，不清库恢复。本文引用的是正文钉定Platform 30bd23af、Main 90c871fe、CRM c778a305，未重查最新仓库。[原文B:9](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/26/265aecc406e50ca8__PLT-CTX-01_完整静态设计_v0.1.md:9) [原文B:110](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/26/265aecc406e50ca8__PLT-CTX-01_完整静态设计_v0.1.md:110)

## 10. 验收设计与未运行状态

4 SPEC、28AC均NOT_RUN。准备覆盖：请求释放/并发隔离和Public边界（CTX01）；同tenant issuer/region/mapping切换（CTX02）；服务JWT及撤权/不可用分类（CTX03）；逐Host接入、共同事务与原消息兼容（CTX04）。不得将格式校验或包引用当跨库提交证据。[原文B:56](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/26/265aecc406e50ca8__PLT-CTX-01_完整静态设计_v0.1.md:56) [原文B:76](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/26/265aecc406e50ca8__PLT-CTX-01_完整静态设计_v0.1.md:76) [原文B:96](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/26/265aecc406e50ca8__PLT-CTX-01_完整静态设计_v0.1.md:96) [原文B:114](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/26/265aecc406e50ca8__PLT-CTX-01_完整静态设计_v0.1.md:114)

## 11. 缺口、依赖与采用门

Required-CTX-01至06分别为tenant/issuer/client全writer、CRM真实映射路由、Owner最终门、precheckpoint同事务、精确包配置单writer、真实AC。缺项维持对应新能力关闭；合法历史read与新写的门分开。库接口存在、Host注册、运行观察、业务效果与独立接受都要独立证据。[原文B:120](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/26/265aecc406e50ca8__PLT-CTX-01_完整静态设计_v0.1.md:120) [原文B:102](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/26/265aecc406e50ca8__PLT-CTX-01_完整静态设计_v0.1.md:102)

## 12. 阅读覆盖与证据

本轮新全文语义阅读正文L1–122；S7最终独审L1–161和组合索引L1–248全文。接受JSON实读Target/Disposition/AcceptanceDecision/AcceptedBody/AcceptedCurrentCombination/ReviewQualifications/OwnerAndExecutionBoundary完整七字段，其他来源历史字段仍待最终L1/L2核对，未称全文。[原文B:1](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/26/265aecc406e50ca8__PLT-CTX-01_完整静态设计_v0.1.md:1) [原文R:1](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/39/397b935b08bca80e__S7_Platform_v0.2.1_七稿组合完整静态接受_独立复审结论.txt:1) [原文C:1](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/3e/3ec93373fda65d46__S7_PLATFORM_COMBINED_FREEZE_v0.2.1.json:1)
可直接使用的精确字段/协议段存[公共合同](D:/CP6/docs/CP6_开发设计文档_20261010/contracts/platform-engineering-contracts.json)，逐源覆盖和未读行段存[阅读记录](D:/CP6/docs/CP6_开发设计文档_20261010/evidence/platform-engineering-reading.json)。本文是源规范的开发解释；候选API/表/字段保持候选，不新增接受身份。

**当前必要材料归并状态。** 本册为 `required_materials_consolidated`：选定正文、规范附件及准确接受/复核范围已按直接实读、结构化语义或精确复用归并。身份清单核对不等底层源码全文阅读；历史草稿、候选实现和Office/HTML视觉未核范围仍单列。可复查[逐件阅读与处置](D:/CP6/docs/CP6_开发设计文档_20261010/evidence/platform-engineering-reading.json)及[本组闭包台账](D:/CP6/docs/CP6_开发设计文档_20261010/worklogs/platform-engineering-progress.json)。当前必需规范未读清单为空；具体Owner采用、参数/物理接口、真实运行和本节已有来源缺口不因整理闭包而改变。
