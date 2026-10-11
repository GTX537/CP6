# PLT-HTTP-01 · HTTP问题响应、网关与有界重试

整理状态：`core_semantics_consolidated`。静态设计接受不升级为实现或运行接受。

## 1. 业务目的、操作者和Owner边界

Platform提供Problem、gateway、HttpClient resilience等原语；远端业务Owner保有operation、输入摘要、结果和原子去重，Host拥有认证/CSRF/目的地址/代理信任。HTTP超时不能替业务Owner判未执行。[原文B:19](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/9d/9d1416cbd7d0ea6e__PLT-HTTP-01_完整静态设计_v0.2.md:19) [原文B:21](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/9d/9d1416cbd7d0ea6e__PLT-HTTP-01_完整静态设计_v0.2.md:21) [原文B:23](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/9d/9d1416cbd7d0ea6e__PLT-HTTP-01_完整静态设计_v0.2.md:23) [原文B:73](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/9d/9d1416cbd7d0ea6e__PLT-HTTP-01_完整静态设计_v0.2.md:73)
业务操作者从原业务页查询提交状态，运维仅诊断路由和技术调用；不能开放任意URL、appId或无限重试开关。[原文B:35](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/9d/9d1416cbd7d0ea6e__PLT-HTTP-01_完整静态设计_v0.2.md:35) [原文B:51](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/9d/9d1416cbd7d0ea6e__PLT-HTTP-01_完整静态设计_v0.2.md:51) [原文B:123](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/9d/9d1416cbd7d0ea6e__PLT-HTTP-01_完整静态设计_v0.2.md:123)

## 2. 选定规范与接受范围

当前正文v0.2，SHA256 `9d1416cbd7d0ea6e58a0da79de80dcb2a4e675890345dfb4558194c61048eb3f`，133行，纳入S7 v0.2.1七稿混合组合：CTX/OBS/RELEASE/TEST v0.1、HTTP/MSG v0.2、DEL v0.2.1。接受记录Disposition为ACCEPTED_CURRENT_MARKDOWN_STATIC_DETAILED_DESIGN。[原文A:175](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/51/51efd28fa1ddfe3f__UA-20261008-S7-PLT-HTTP-01-STATIC-MD02.json:175) [原文R:9](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/39/397b935b08bca80e__S7_Platform_v0.2.1_七稿组合完整静态接受_独立复审结论.txt:9)
delegate静态接受，非用户亲签；原02:18:29Z决定未恢复逐字或messageId，04:09:04Z另有恢复确认。组合索引中DEL的pending/null保留上传前历史，最终接受以精确正文+独审+接受记录为准。258项组合AC全NOT_RUN，本Target 42 AC；运行和Owner采用UNPROVEN、实施/部署未授权。[原文A:8](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/51/51efd28fa1ddfe3f__UA-20261008-S7-PLT-HTTP-01-STATIC-MD02.json:8) [原文A:340](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/51/51efd28fa1ddfe3f__UA-20261008-S7-PLT-HTTP-01-STATIC-MD02.json:340) [原文R:132](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/39/397b935b08bca80e__S7_Platform_v0.2.1_七稿组合完整静态接受_独立复审结论.txt:132)

## 3. 对象、字段与不可替代的身份

|身份或配置|开发约束|
|---|---|
|ProblemDefinition|type HTTPS；title 1..200；status 400..599；code大写分段；messageKey命名空间.error.camel。现固定401 CP6_AUTHENTICATION_REQUIRED、403 CP6_FORBIDDEN、429 CP6_RATE_LIMIT_EXCEEDED等以原定义为准，不能替所有应用自选业务码。[原文B:19](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/9d/9d1416cbd7d0ea6e__PLT-HTTP-01_完整静态设计_v0.2.md:19)|
|Problem响应|type/title/status/code/messageKey/traceId/correlationId/errors；不含detail/instance。traceId取Activity的32位trace或新建；errors现原语未全面限制，候选最多32字段×4信息×256字符、整体目标16KiB，白名单且无字段值。[原文B:29](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/9d/9d1416cbd7d0ea6e__PLT-HTTP-01_完整静态设计_v0.2.md:29) [原文B:33](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/9d/9d1416cbd7d0ea6e__PLT-HTTP-01_完整静态设计_v0.2.md:33)|
|HttpOperationBindingV1|client/endpoint Owner、tenant、kind、requestVersion/businessKey/digest、原结果lookup/retention、method/destination/credential profile/config/routeEpoch、运行证据。Idempotency-Key头单独不够。[原文B:21](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/9d/9d1416cbd7d0ea6e__PLT-HTTP-01_完整静态设计_v0.2.md:21) [原文B:23](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/9d/9d1416cbd7d0ea6e__PLT-HTTP-01_完整静态设计_v0.2.md:23)|
|HttpDeadlineContext|startedAtUtc、total/attempt/remaining毫秒、source Client/User/Worker及operation profile；实际预算用monotonic elapsed，子调用不得重置。[原文B:71](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/9d/9d1416cbd7d0ea6e__PLT-HTTP-01_完整静态设计_v0.2.md:71)|

## 4. 状态流程、入口与本地写集

发送观察为NotSent（仅确证尚未入网）、SentAwaitingResponse、Confirmed、Unknown，均不自动等于业务结果。HttpRequestException不足以判NotSent；先查原operation，再决定是否当前新写。取消只是停止等待，不是业务撤回。[原文B:73](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/9d/9d1416cbd7d0ea6e__PLT-HTTP-01_完整静态设计_v0.2.md:73) [原文B:75](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/9d/9d1416cbd7d0ea6e__PLT-HTTP-01_完整静态设计_v0.2.md:75)
HttpOperationStatusViewV1分Pending/Applied/Rejected/Unknown/Blocked、OwnerRef/safeCode/read mask/观察时间/correlation及是否能查原结果、申请恢复；HTTP层不写Owner主账。响应已开始时不再拼第二段JSON；断开取消不伪回成功。[原文B:31](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/9d/9d1416cbd7d0ea6e__PLT-HTTP-01_完整静态设计_v0.2.md:31) [原文B:35](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/9d/9d1416cbd7d0ea6e__PLT-HTTP-01_完整静态设计_v0.2.md:35)

## 5. 接口、消息与依赖

新网关route必须显式policy、methods和destination allowlist；旧validator允许空methods（全部）及空policy，不可声称默认安全。route/cluster非空，routeId小写DNS≤63，methods限原7种；限流每route/RemoteIP的进程内固定窗口、Queue=0，不是全局配额。[原文B:47](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/9d/9d1416cbd7d0ea6e__PLT-HTTP-01_完整静态设计_v0.2.md:47) [原文B:51](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/9d/9d1416cbd7d0ea6e__PLT-HTTP-01_完整静态设计_v0.2.md:51) [原文B:53](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/9d/9d1416cbd7d0ea6e__PLT-HTTP-01_完整静态设计_v0.2.md:53)
同时清request/content头的X-CP6-*、X-Organization-*、X-Tenant-*、X-User-*及原文列明Forwarded等精确名字；不是清全部X-Forwarded-*，下游不能信剩余头做身份。X-Correlation-Id须唯一ASCII、1..128，首字母数字，其后字母数字._:-，否则重生。[原文B:49](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/9d/9d1416cbd7d0ea6e__PLT-HTTP-01_完整静态设计_v0.2.md:49) [原文B:55](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/9d/9d1416cbd7d0ea6e__PLT-HTTP-01_完整静态设计_v0.2.md:55)
IdempotentRead只GET/HEAD/OPTIONS；POST/PUT/PATCH/DELETE按Owner证据才能IdempotentWrite；NonIdempotent retries=0。401/403/409/422不重试；大体积流、一次性code/login、付款未知结果等保NonIdempotent或查原结果。[原文B:87](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/9d/9d1416cbd7d0ea6e__PLT-HTTP-01_完整静态设计_v0.2.md:87) [原文B:101](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/9d/9d1416cbd7d0ea6e__PLT-HTTP-01_完整静态设计_v0.2.md:101)

## 6. 事务、幂等、并发及恢复

现pipeline total→retry→circuit→attempt，默认attempt2秒/total10秒；相等时旧duration比较不能区分来源，新设计按cp6-total-timeout/cp6-attempt-timeout strategy来源分类，未知来源停止。一个权威总budget，同时限制外层与内层总尝试数。[原文B:67](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/9d/9d1416cbd7d0ea6e__PLT-HTTP-01_完整静态设计_v0.2.md:67) [原文B:69](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/9d/9d1416cbd7d0ea6e__PLT-HTTP-01_完整静态设计_v0.2.md:69) [原文B:105](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/9d/9d1416cbd7d0ea6e__PLT-HTTP-01_完整静态设计_v0.2.md:105)
旧profile只重试HttpRequestException及408/429/500/502/503/504，不含TimeoutRejectedException，delay=0、无jitter且忽略Retry-After，不能把新设计误写为已实现。[原文B:89](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/9d/9d1416cbd7d0ea6e__PLT-HTTP-01_完整静态设计_v0.2.md:89)
新RetryPolicyBindingV2明确opt-in：maxRetries≤5、baseDelay200ms/maxDelay2000ms、jitter 0..0.5默认0.2、respectRetryAfter=true、server cap30000ms。单值Retry-After接受非负整数或三种HTTP-date；overflow/极大合法值直接StopServerWaitExceeded，不按非法值退回短等待；日期仅一次UTC→monotonic锚定。[原文B:91](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/9d/9d1416cbd7d0ea6e__PLT-HTTP-01_完整静态设计_v0.2.md:91) [原文B:93](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/9d/9d1416cbd7d0ea6e__PLT-HTTP-01_完整静态设计_v0.2.md:93)
第k次 B=min(maxDelay,base×2^(k−1))；一次采样u后 L=ceil(clamp(B×(1−j+2ju),0,maxDelay))，S为有效服务器等待否则0；最早发送=m0+max(S,L)。jitter/maxDelay只作用L；S>30000直接停，不截成30000重试。决定与发送前都检剩余R，W+完整attempt A>R停止，等于可；早醒补等、晚醒重验R≥A，排队/解析也计budget。[原文B:95](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/9d/9d1416cbd7d0ea6e__PLT-HTTP-01_完整静态设计_v0.2.md:95) [原文B:97](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/9d/9d1416cbd7d0ea6e__PLT-HTTP-01_完整静态设计_v0.2.md:97)
同原businessKey/digest/route/identity重试，attemptId可变。旧操作未知保持查原结果；不能刷新身份再当同请求继续，不能读新key或创建替代operation。[原文B:99](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/9d/9d1416cbd7d0ea6e__PLT-HTTP-01_完整静态设计_v0.2.md:99) [原文B:101](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/9d/9d1416cbd7d0ea6e__PLT-HTTP-01_完整静态设计_v0.2.md:101) [原文B:103](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/9d/9d1416cbd7d0ea6e__PLT-HTTP-01_完整静态设计_v0.2.md:103)

## 7. 权限、租户和敏感信息

网关清头不是最终授权；下游重验当前Actor/tenant，trusted-proxy先于IP/TLS判断。目的地址和凭据由已登记配置确定，TLS失败不能降匿名或切另一region写库。租户与PII不得进入Problem errors/URL日志。[原文B:49](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/9d/9d1416cbd7d0ea6e__PLT-HTTP-01_完整静态设计_v0.2.md:49) [原文B:51](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/9d/9d1416cbd7d0ea6e__PLT-HTTP-01_完整静态设计_v0.2.md:51) [原文B:57](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/9d/9d1416cbd7d0ea6e__PLT-HTTP-01_完整静态设计_v0.2.md:57) [原文B:119](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/9d/9d1416cbd7d0ea6e__PLT-HTTP-01_完整静态设计_v0.2.md:119)
CRM原about:blank问题合同、cookie/CSRF及自己的错误语义兼容保留，不强改成新HTTPS/messageKey。依赖不可用503、远端超时504不宣称Owner事务回滚。[原文B:31](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/9d/9d1416cbd7d0ea6e__PLT-HTTP-01_完整静态设计_v0.2.md:31) [原文B:33](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/9d/9d1416cbd7d0ea6e__PLT-HTTP-01_完整静态设计_v0.2.md:33)

## 8. 页面与服务器校验

业务页收到504后查原operation，不能再次POST；403不自动重试，409展示差异，UNKNOWN显示原结果查询。诊断页分技术连接和Owner原结果；若响应stream已开始只停止流并记安全观察，不补新成功JSON。[原文B:31](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/9d/9d1416cbd7d0ea6e__PLT-HTTP-01_完整静态设计_v0.2.md:31) [原文B:35](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/9d/9d1416cbd7d0ea6e__PLT-HTTP-01_完整静态设计_v0.2.md:35) [原文B:37](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/9d/9d1416cbd7d0ea6e__PLT-HTTP-01_完整静态设计_v0.2.md:37)
新Host须约束errors数量/长度/安全字段；现库并未自动落实这些候选限制。路由状态只读展示配置版本、单writer epoch和采用层次。[原文B:33](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/9d/9d1416cbd7d0ea6e__PLT-HTTP-01_完整静态设计_v0.2.md:33) [原文B:115](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/9d/9d1416cbd7d0ea6e__PLT-HTTP-01_完整静态设计_v0.2.md:115)

## 9. 实施顺序与固定源码差距

先逐操作定义Owner的真正去重/receipt同事务证据，再补deadline与Retry-After策略、网关可信配置，最后在各Host独立注册并验证兼容。固定CRM crm-identity HttpClient为Timeout10秒、禁redirect/cookie，未调用Platform Resilience/Problem/Gateway；P_Program是SQL fixture，不是CRM Host证据。[原文B:11](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/9d/9d1416cbd7d0ea6e__PLT-HTTP-01_完整静态设计_v0.2.md:11) [原文B:115](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/9d/9d1416cbd7d0ea6e__PLT-HTTP-01_完整静态设计_v0.2.md:115)
切换按routeEpoch单writer，保in-flight原结果与旧恢复读；不得fallback其他region写入。保持原legacy profile，新增v2由明确operation binding采用。[原文B:57](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/9d/9d1416cbd7d0ea6e__PLT-HTTP-01_完整静态设计_v0.2.md:57) [原文B:91](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/9d/9d1416cbd7d0ea6e__PLT-HTTP-01_完整静态设计_v0.2.md:91)

## 10. 验收设计与未运行状态

5 SPEC/42AC全部NOT_RUN，HTTP04含10项，其余各8；B01静态修复已闭合。关键未来例：u=.5/L200，S100等200；S5000等5000；S60000无论budget足够都停止；S9000/R10000/A2000停止。实际测试必须同时观察真实尝试和原Owner结果。[原文B:99](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/9d/9d1416cbd7d0ea6e__PLT-HTTP-01_完整静态设计_v0.2.md:99) [原文B:133](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/9d/9d1416cbd7d0ea6e__PLT-HTTP-01_完整静态设计_v0.2.md:133) [原文R:70](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/39/397b935b08bca80e__S7_Platform_v0.2.1_七稿组合完整静态接受_独立复审结论.txt:70)

## 11. 缺口、依赖与采用门

Required-HTTP-01至07：真实routes、远端Owner幂等、认证兼容、timeout/retry总预算、目的地址/代理、单writer切换、实际AC。缺远端同事务去重/receipt、可查原结果和足够retention的写接口保持NonIdempotent；有幂等头不构成资格。[原文B:23](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/9d/9d1416cbd7d0ea6e__PLT-HTTP-01_完整静态设计_v0.2.md:23) [原文B:123](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/9d/9d1416cbd7d0ea6e__PLT-HTTP-01_完整静态设计_v0.2.md:123)

## 12. 阅读覆盖与证据

本轮新全文语义阅读正文L1–133；S7最终独审L1–161和组合索引L1–248全文。接受JSON实读Target/Disposition/AcceptanceDecision/AcceptedBody/AcceptedCurrentCombination/ReviewQualifications/OwnerAndExecutionBoundary完整七字段，其他来源历史字段仍待最终L1/L2核对，未称全文。[原文B:1](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/9d/9d1416cbd7d0ea6e__PLT-HTTP-01_完整静态设计_v0.2.md:1) [原文R:1](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/39/397b935b08bca80e__S7_Platform_v0.2.1_七稿组合完整静态接受_独立复审结论.txt:1) [原文C:1](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/3e/3ec93373fda65d46__S7_PLATFORM_COMBINED_FREEZE_v0.2.1.json:1)
可直接使用的精确字段/协议段存[公共合同](D:/CP6/docs/CP6_开发设计文档_20261010/contracts/platform-engineering-contracts.json)，逐源覆盖和未读行段存[阅读记录](D:/CP6/docs/CP6_开发设计文档_20261010/evidence/platform-engineering-reading.json)。本文是源规范的开发解释；候选API/表/字段保持候选，不新增接受身份。

**当前必要材料归并状态。** 本册为 `required_materials_consolidated`：选定正文、规范附件及准确接受/复核范围已按直接实读、结构化语义或精确复用归并。身份清单核对不等底层源码全文阅读；历史草稿、候选实现和Office/HTML视觉未核范围仍单列。可复查[逐件阅读与处置](D:/CP6/docs/CP6_开发设计文档_20261010/evidence/platform-engineering-reading.json)及[本组闭包台账](D:/CP6/docs/CP6_开发设计文档_20261010/worklogs/platform-engineering-progress.json)。当前必需规范未读清单为空；具体Owner采用、参数/物理接口、真实运行和本节已有来源缺口不因整理闭包而改变。
