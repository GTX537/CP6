# PLT-OBS-01 · 观测、健康与SLO证据

整理状态：`core_semantics_consolidated`。静态设计接受不升级为实现或运行接受。

## 1. 业务目的、操作者和Owner边界

Platform给遥测原语、SLO evidence形状校验、release identity与health endpoints；各应用Owner定义业务SLI、采集边界、threshold、真正receipt与生产资格。操作者为应用开发者/运维/证据审阅人，不把遥测计数做业务主账。[原文B:25](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/20/20c9a128b3f2bab0__PLT-OBS-01_完整静态设计_v0.1.md:25) [原文B:29](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/20/20c9a128b3f2bab0__PLT-OBS-01_完整静态设计_v0.1.md:29) [原文B:55](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/20/20c9a128b3f2bab0__PLT-OBS-01_完整静态设计_v0.1.md:55)
现published表示transport返回、consumed表示校验通过且在业务commit前、enqueue也在caller commit前；因此这些计数不能直接作业务成功率。[原文B:27](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/20/20c9a128b3f2bab0__PLT-OBS-01_完整静态设计_v0.1.md:27)

## 2. 选定规范与接受范围

当前正文v0.1、SHA256 `20c9a128b3f2bab0621459cc6c6494b16fb21cd8340792738ba89e90efa3bc99`、111行。精确采用S7 v0.2.1混合七稿：CTX/OBS/RELEASE/TEST v0.1，HTTP/MSG v0.2，DEL v0.2.1；本Target 32项AC。接受为ACCEPTED_CURRENT_MARKDOWN_STATIC_DETAILED_DESIGN。[原文A:167](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/37/37bd4fe95ee0451f__UA-20261008-S7-PLT-OBS-01-STATIC-MD02.json:167) [原文R:9](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/39/397b935b08bca80e__S7_Platform_v0.2.1_七稿组合完整静态接受_独立复审结论.txt:9)
原作者freeze索引中的DEL pending/null保留历史，最终独审关闭B01/B02/B03及B03-R1，非当前缺稿。正式delegate接受与用户亲签分开，原决定逐字/messageId未恢复，后续04:09:04Z恢复确认另记；258组合AC全NOT_RUN，运行采用UNPROVEN，未授权实施/部署。[原文R:7](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/39/397b935b08bca80e__S7_Platform_v0.2.1_七稿组合完整静态接受_独立复审结论.txt:7) [原文R:132](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/39/397b935b08bca80e__S7_Platform_v0.2.1_七稿组合完整静态接受_独立复审结论.txt:132) [原文A:8](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/37/37bd4fe95ee0451f__UA-20261008-S7-PLT-OBS-01-STATIC-MD02.json:8) [原文A:332](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/37/37bd4fe95ee0451f__UA-20261008-S7-PLT-OBS-01-STATIC-MD02.json:332)

## 3. 对象、字段与不可替代的身份

|对象|字段与严格边界|
|---|---|
|MetricDefinition（候选）|measurementPoint、commitRelation BeforeCommit/AfterCommit/ObserverOnly、attemptOrBusinessScope；精确业务成功以Owner原receipt统计。[原文B:29](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/20/20c9a128b3f2bab0__PLT-OBS-01_完整静态设计_v0.1.md:29)|
|SLO evidence v1.0.0|schemaVersion/evidenceId/generatedAtUtc/release/sli/window/measurement/sources/completeness/result/productionSloClaimed；JSON≤1MiB/depth32，无重复/comment/trailing/unknown；生成时刻≥window.end。[原文B:47](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/20/20c9a128b3f2bab0__PLT-OBS-01_完整静态设计_v0.1.md:47)|
|SLI/window/measurement/source|定义版本digest/unit/aggregation Ratio Percentile Gauge/comparator≥或≤/threshold；start<end、coverage0..1；sampleCount≥0、denominator>0、percentile0..100、excluded≤sample；source含definition/release/query/evidence四类digest及exclusionsVerified；digest sha256:小写64。[原文B:49](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/20/20c9a128b3f2bab0__PLT-OBS-01_完整静态设计_v0.1.md:49)|
|RuntimeIdentityBinding（候选）|declared与actual artifact kind/hash、package set、config/schema、部署时间、verifier和Match/Mismatch/Unproven证据；不能只存声明。[原文B:75](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/20/20c9a128b3f2bab0__PLT-OBS-01_完整静态设计_v0.1.md:75)|
|HealthCheckBinding（候选）|component/purpose/routes/dependencies/timeouts/启动权限/登记安全名/观察证据；Ready不是commit许可。[原文B:93](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/20/20c9a128b3f2bab0__PLT-OBS-01_完整静态设计_v0.1.md:93)|

## 4. 状态流程、入口与本地写集

SLI证据按Planned→Collecting→Closed→ArtifactsFrozen→Qualified→Evaluated→IndependentlyAccepted；晚到/混版数据生成新version/supersedes，不覆盖原窗口或挑最佳。release identity为DeclaredFormatValid→ArtifactBound→HostObserved→IndependentlyQualified。[原文B:57](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/20/20c9a128b3f2bab0__PLT-OBS-01_完整静态设计_v0.1.md:57) [原文B:77](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/20/20c9a128b3f2bab0__PLT-OBS-01_完整静态设计_v0.1.md:77)
Evaluate fail-closed：candidate、complete、expected coverage=1且observed吻合、sample>0、source绑定hash/排除验证等齐全才比value阈值，否则Indeterminate。它不下载/校验源bytes，不证明query真实、ratio/percentile推导或production flag，需候选EvidenceQualification先验证真实来源。[原文B:51](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/20/20c9a128b3f2bab0__PLT-OBS-01_完整静态设计_v0.1.md:51) [原文B:53](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/20/20c9a128b3f2bab0__PLT-OBS-01_完整静态设计_v0.1.md:53)
Ratio按K/N与单位，0≤K≤N；percentile保method/buckets/weights，不能平均p95；Gauge需采样频率/缺口；coverage不能拿收到的数据自行定100%。[原文B:55](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/20/20c9a128b3f2bab0__PLT-OBS-01_完整静态设计_v0.1.md:55)

## 5. 接口、消息与依赖

现三组ActivitySource/Meter为AspNetCore、Messaging、EF，六操作outbound/daprinvoke/publish/consume/outboxdispatch/inboxprocess；tag keys只固定region/operation/outcome/errorcode/transport/disposition/httpoperationkind及登记值，无tenant/subject/jti高基数。[原文B:25](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/20/20c9a128b3f2bab0__PLT-OBS-01_完整静态设计_v0.1.md:25)
health默认/health/live、/health/startup、/health/ready、/health/release路径各不同且≤128；原具体API路径以注册配置为准。live常量Healthy且不查DB；startup/ready按tag checks，Healthy200，Degraded/Unhealthy503；/health/release核DI accessor与registration一致，不能证明二进制签名/批准。[原文B:81](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/20/20c9a128b3f2bab0__PLT-OBS-01_完整静态设计_v0.1.md:81) [原文B:89](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/20/20c9a128b3f2bab0__PLT-OBS-01_完整静态设计_v0.1.md:89)
同profile重复DI注册幂等、不同拒绝；service≤64、env/region≤32，资源身份一致。改变进程全局propagator会影响多Host，需明确初始化次序。[原文B:31](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/20/20c9a128b3f2bab0__PLT-OBS-01_完整静态设计_v0.1.md:31)

## 6. 事务、幂等、并发及恢复

遥测是observer-only，导出队列/丢弃/flush须有界，不阻塞业务commit；强合规audit另由Owner原事务承担，不能以非阻塞日志取代。[原文B:37](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/20/20c9a128b3f2bab0__PLT-OBS-01_完整静态设计_v0.1.md:37)
traceparent/tracestate保因果，不带baggage；无效parent丢弃并安全计数，不自行拒合法业务。metric retry/导出丢失不更改业务事实。[原文B:29](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/20/20c9a128b3f2bab0__PLT-OBS-01_完整静态设计_v0.1.md:29) [原文B:33](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/20/20c9a128b3f2bab0__PLT-OBS-01_完整静态设计_v0.1.md:33)
不可变证据绑定精确release/窗口/来源/query/排除规则，修改需新版本，缺证Indeterminate。运行identity unknown/mismatch限制对应Owner切流/新写，不能把Platform全局一把开关代所有Owner判权。[原文B:57](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/20/20c9a128b3f2bab0__PLT-OBS-01_完整静态设计_v0.1.md:57) [原文B:77](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/20/20c9a128b3f2bab0__PLT-OBS-01_完整静态设计_v0.1.md:77)

## 7. 权限、租户和敏感信息

自动instrumentation不等于自动PII清洗。Host export allowlist只method、routeTemplate、status class、duration、safe enum、登记资源，清URL query/auth/cookie/body/SQL参数/完整IDs；CRM禁URL日志保持。[原文B:35](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/20/20c9a128b3f2bab0__PLT-OBS-01_完整静态设计_v0.1.md:35)
health无自动认证保护，Host必须给网络/权限政策；探针专用身份，频率有界，健康缓存不作授权缓存。组件详情可隐藏但不得改总体状态，取消未观察不能判Healthy。[原文B:91](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/20/20c9a128b3f2bab0__PLT-OBS-01_完整静态设计_v0.1.md:91) [原文B:97](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/20/20c9a128b3f2bab0__PLT-OBS-01_完整静态设计_v0.1.md:97) [原文B:99](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/20/20c9a128b3f2bab0__PLT-OBS-01_完整静态设计_v0.1.md:99)

## 8. 页面与服务器校验

展示技术测量点/业务commit关系和candidate/production证据分层。不能以绿色/ready表示全部read和write可用；CRM现ready已有auth/catalog/orgDB/migration/write gate/OIDC检查，不替换空check成功。UI展示缺源、窗口、实际artifact比较和独立接受状态。[原文B:59](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/20/20c9a128b3f2bab0__PLT-OBS-01_完整静态设计_v0.1.md:59) [原文B:91](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/20/20c9a128b3f2bab0__PLT-OBS-01_完整静态设计_v0.1.md:91) [原文B:95](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/20/20c9a128b3f2bab0__PLT-OBS-01_完整静态设计_v0.1.md:95)
health响应no-store，schema1.0.0、time、status、安全component名/status，不返回exception/data。GET health不得迁移、清队列或写业务。[原文B:89](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/20/20c9a128b3f2bab0__PLT-OBS-01_完整静态设计_v0.1.md:89) [原文B:95](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/20/20c9a128b3f2bab0__PLT-OBS-01_完整静态设计_v0.1.md:95)

## 9. 实施顺序与固定源码差距

先定义精确业务成功SLI及Owner receipt来源、采样与PII allowlist，再实施有界exporter和真实identity观察，接原Host的health检查，最后生成不可变证据并独立资格化。现Evaluate/health只证明原结构和注册一致，不能替实际源采集。[原文B:29](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/20/20c9a128b3f2bab0__PLT-OBS-01_完整静态设计_v0.1.md:29) [原文B:51](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/20/20c9a128b3f2bab0__PLT-OBS-01_完整静态设计_v0.1.md:51) [原文B:53](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/20/20c9a128b3f2bab0__PLT-OBS-01_完整静态设计_v0.1.md:53) [原文B:81](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/20/20c9a128b3f2bab0__PLT-OBS-01_完整静态设计_v0.1.md:81)
ReleaseIdentity候选必须canonical SemVer无build metadata、Git SHA40、artifact/contract bundle digest；正式版本格式不能证明实际运行这些bytes。[原文B:71](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/20/20c9a128b3f2bab0__PLT-OBS-01_完整静态设计_v0.1.md:71) [原文B:73](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/20/20c9a128b3f2bab0__PLT-OBS-01_完整静态设计_v0.1.md:73)

## 10. 验收设计与未运行状态

4 SPEC/32AC全部NOT_RUN。未来用commit回滚但技术计数增加的反例核测量点；缺source/混release/采样缺口应Indeterminate；identity mismatch、空ready、cancelled check、导出溢出与PII泄露边界均独立核，不引用旧run作本轮通过。[原文B:27](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/20/20c9a128b3f2bab0__PLT-OBS-01_完整静态设计_v0.1.md:27) [原文B:51](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/20/20c9a128b3f2bab0__PLT-OBS-01_完整静态设计_v0.1.md:51) [原文B:91](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/20/20c9a128b3f2bab0__PLT-OBS-01_完整静态设计_v0.1.md:91) [原文B:99](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/20/20c9a128b3f2bab0__PLT-OBS-01_完整静态设计_v0.1.md:99) [原文B:111](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/20/20c9a128b3f2bab0__PLT-OBS-01_完整静态设计_v0.1.md:111)

## 11. 缺口、依赖与采用门

Required-OBS-01..07依次为真实Host采集注册、自动遥测安全字段/目的地/保留权限、指标measurementPoint与业务receipt口径、SLO定义/query/来源/覆盖资格化、declared/observed artifact、health检查与Host兼容、原AC/原日志及独立接受。未有真实证据productionSloClaimed不得因此置true；只有候选阈值通过不等生产SLO已接受。[原文B:59](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/20/20c9a128b3f2bab0__PLT-OBS-01_完整静态设计_v0.1.md:59) [原文B:109](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/20/20c9a128b3f2bab0__PLT-OBS-01_完整静态设计_v0.1.md:109)

## 12. 阅读覆盖与证据

本轮新全文语义阅读正文L1–111；S7最终独审L1–161、组合索引L1–248全文。接受JSON读取Target/Disposition/AcceptanceDecision/AcceptedBody/AcceptedCurrentCombination/ReviewQualifications/OwnerAndExecutionBoundary完整七字段，其余来源历史字段待L1/L2集中核对，未声称全文。[原文B:1](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/20/20c9a128b3f2bab0__PLT-OBS-01_完整静态设计_v0.1.md:1) [原文R:1](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/39/397b935b08bca80e__S7_Platform_v0.2.1_七稿组合完整静态接受_独立复审结论.txt:1) [原文C:1](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/3e/3ec93373fda65d46__S7_PLATFORM_COMBINED_FREEZE_v0.2.1.json:1)
[共享合同](D:/CP6/docs/CP6_开发设计文档_20261010/contracts/platform-engineering-contracts.json)保存关键精确协议/字段原段；[阅读记录](D:/CP6/docs/CP6_开发设计文档_20261010/evidence/platform-engineering-reading.json)逐源记录新读、继承和未读行段。本文未把候选seam、表或保证写成现包已实现。

**当前必要材料归并状态。** 本册为 `required_materials_consolidated`：选定正文、规范附件及准确接受/复核范围已按直接实读、结构化语义或精确复用归并。身份清单核对不等底层源码全文阅读；历史草稿、候选实现和Office/HTML视觉未核范围仍单列。可复查[逐件阅读与处置](D:/CP6/docs/CP6_开发设计文档_20261010/evidence/platform-engineering-reading.json)及[本组闭包台账](D:/CP6/docs/CP6_开发设计文档_20261010/worklogs/platform-engineering-progress.json)。当前必需规范未读清单为空；具体Owner采用、参数/物理接口、真实运行和本节已有来源缺口不因整理闭包而改变。
