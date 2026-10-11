# PLT-TEST-01 · 测试支撑、故障序列与证据边界

整理状态：`core_semantics_consolidated`。静态设计接受不升级为实现或运行接受。

## 1. 业务目的、操作者和Owner边界

Owner=CP6.Platform.Testing，TEST_ONLY，不计正式业务功能完成；为测试作者提供故障序列、遥测捕获和断言，真实应用/权限/DB/broker验证仍归各Owner。IsPackable=false且不属正式七包，不自动证明生产未引用测试代码。[原文B:7](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d8/d8b4ef19a1b3a0a5__PLT-TEST-01_完整静态设计_v0.1.md:7) [原文B:11](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d8/d8b4ef19a1b3a0a5__PLT-TEST-01_完整静态设计_v0.1.md:11) [原文B:71](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d8/d8b4ef19a1b3a0a5__PLT-TEST-01_完整静态设计_v0.1.md:71)
本册只整理未来测试设计，没有运行故障注入、真实调用或项目测试。[原文B:3](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d8/d8b4ef19a1b3a0a5__PLT-TEST-01_完整静态设计_v0.1.md:3)

## 2. 选定规范与接受范围

当前正文v0.1、SHA256 `d8b4ef19a1b3a0a528ddd7831f5159295d75b7eecadfa787e36ad028d362c5aa`、102行。精确采用S7 v0.2.1混合七稿：CTX/OBS/RELEASE/TEST v0.1，HTTP/MSG v0.2，DEL v0.2.1；本Target 24项AC。接受为ACCEPTED_CURRENT_MARKDOWN_STATIC_DETAILED_DESIGN。[原文A:139](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/06/06825b36804acf38__UA-20261008-S7-PLT-TEST-01-STATIC-MD02.json:139) [原文R:9](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/39/397b935b08bca80e__S7_Platform_v0.2.1_七稿组合完整静态接受_独立复审结论.txt:9)
原作者freeze索引中的DEL pending/null保留历史，最终独审关闭B01/B02/B03及B03-R1，非当前缺稿。正式delegate接受与用户亲签分开，原决定逐字/messageId未恢复，后续04:09:04Z恢复确认另记；258组合AC全NOT_RUN，运行采用UNPROVEN，未授权实施/部署。[原文R:7](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/39/397b935b08bca80e__S7_Platform_v0.2.1_七稿组合完整静态接受_独立复审结论.txt:7) [原文R:132](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/39/397b935b08bca80e__S7_Platform_v0.2.1_七稿组合完整静态接受_独立复审结论.txt:132) [原文A:8](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/06/06825b36804acf38__UA-20261008-S7-PLT-TEST-01-STATIC-MD02.json:8) [原文A:304](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/06/06825b36804acf38__UA-20261008-S7-PLT-TEST-01-STATIC-MD02.json:304)

## 3. 对象、字段与不可替代的身份

|对象|字段及意义|
|---|---|
|TestCaseContract（候选）|case/spec/source artifact、scope Unit/Contract/HostIntegration/RuntimeQualification、环境批准、Synthetic fixture/input operation、前提/stimulus、expected trace/transaction/receipt、assertions/cleanup/EvidenceStatus。[原文B:17](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d8/d8b4ef19a1b3a0a5__PLT-TEST-01_完整静态设计_v0.1.md:17)|
|TestEvidence|attempt、精确源码包/definition/toolchain/env/时间、Result NotRun/Pass/Fail/Indeterminate、断言/安全错误/原artifacts/cleanup/review。新尝试保旧失败。[原文B:19](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d8/d8b4ef19a1b3a0a5__PLT-TEST-01_完整静态设计_v0.1.md:19)|
|FaultScript|复制无null数组、Interlocked递增索引；耗尽InvalidOperation且计数继续加，计数不等网络次数；并发只保证索引唯一不保证请求映射顺序。[原文B:23](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d8/d8b4ef19a1b3a0a5__PLT-TEST-01_完整静态设计_v0.1.md:23)|
|FaultCasePlan（候选）|client/operation、outcomes/safe kind/status/delay/error、最大调用数、script/inner预期数、clock/deadline、InMemory terminal、并发scheduler/cancel/expected attempts。[原文B:29](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d8/d8b4ef19a1b3a0a5__PLT-TEST-01_完整静态设计_v0.1.md:29)|
|Telemetry records|Activity的Sequence/operation/display/kind/status/trace/span/parent/tags/baggage；Metric的sequence/meter/instrument/value(double)/tags；double可丢decimal精度，重复tag最后值保留。[原文B:47](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d8/d8b4ef19a1b3a0a5__PLT-TEST-01_完整静态设计_v0.1.md:47) [原文B:49](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d8/d8b4ef19a1b3a0a5__PLT-TEST-01_完整静态设计_v0.1.md:49)|

## 4. 状态流程、入口与本地写集

OutcomeStatus为100..599且不调inner；Throw非空且不调inner；Delay须>0且≤5分钟，TimeProvider等待后调inner，提前cancel则不调；Success也调inner，并非自动HTTP200。Delay/Success要另接InMemoryTerminal才不发真实网络。[原文B:25](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d8/d8b4ef19a1b3a0a5__PLT-TEST-01_完整静态设计_v0.1.md:25)
注册DI只精确Test或CI环境，不接受test/Development/Production；同script引用幂等、不同拒绝，script singleton、handler transient。直接new handler没有环境检查，DI注册本身也不等pipeline已接上。[原文B:27](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d8/d8b4ef19a1b3a0a5__PLT-TEST-01_完整静态设计_v0.1.md:27)
script在retry handler内外会改变step消费，明确pipeline位置和真实inner调用；耗尽视Fail，禁止fallback真实网络或循环补脚本。[原文B:29](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d8/d8b4ef19a1b3a0a5__PLT-TEST-01_完整静态设计_v0.1.md:29) [原文B:31](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d8/d8b4ef19a1b3a0a5__PLT-TEST-01_完整静态设计_v0.1.md:31)

## 5. 接口、消息与依赖

没有业务API/CRUD/UI故障开关；工具只供隔离测试，读报告的页面不发业务效果。[原文B:39](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d8/d8b4ef19a1b3a0a5__PLT-TEST-01_完整静态设计_v0.1.md:39) [原文B:94](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d8/d8b4ef19a1b3a0a5__PLT-TEST-01_完整静态设计_v0.1.md:94)
AssertTraceTopology要求每operation恰一个、同trace、span唯一、输入顺序严格direct-parent链；不能覆盖分叉/retry/中间Dapr span。复杂场景用候选GraphExpectation列nodes/counts、parent/link/ancestor edges、trace与允许中间节点/禁止跨case连接。[原文B:51](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d8/d8b4ef19a1b3a0a5__PLT-TEST-01_完整静态设计_v0.1.md:51)
allowed-tags断言只检查cp6.* key，不查vendor字段/值基数；forbidden文本只查本次捕获且明确给出的字符串，不证明所有日志/网络/exporter没PII。用合成canary并分别覆盖目标渠道。[原文B:53](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d8/d8b4ef19a1b3a0a5__PLT-TEST-01_完整静态设计_v0.1.md:53)

## 6. 事务、幂等、并发及恢复

记录器监听进程全局三组source，AllDataAndRecorded；两个无界队列按本地Sequence排序不等数据库线性化，Dispose不等隔离。候选设计需要独立进程或受控互斥、case归属过滤、最大events/time，overflow=Fail/Indeterminate，不丢数据后称完整。[原文B:47](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d8/d8b4ef19a1b3a0a5__PLT-TEST-01_完整静态设计_v0.1.md:47) [原文B:55](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d8/d8b4ef19a1b3a0a5__PLT-TEST-01_完整静态设计_v0.1.md:55)
先注册→运行并等待本case活动结束→检查scope/graph/tags/privacy/receipt→Dispose；序号不作业务commit序。必须有技术成功但业务rollback的反例；非阻塞observer异常不改变业务，强审计另有事务。[原文B:57](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d8/d8b4ef19a1b3a0a5__PLT-TEST-01_完整静态设计_v0.1.md:57) [原文B:59](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d8/d8b4ef19a1b3a0a5__PLT-TEST-01_完整静态设计_v0.1.md:59) [原文B:61](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d8/d8b4ef19a1b3a0a5__PLT-TEST-01_完整静态设计_v0.1.md:61)
重复attempt沿同case/definition/input哈希生成新attemptId；改expected需新definition version，不改失败记录修绿。取消/cleanup未知Fail或Indeterminate，不resetDB或删未证明归本case的共享资源。[原文B:88](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d8/d8b4ef19a1b3a0a5__PLT-TEST-01_完整静态设计_v0.1.md:88) [原文B:90](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d8/d8b4ef19a1b3a0a5__PLT-TEST-01_完整静态设计_v0.1.md:90)

## 7. 权限、租户和敏感信息

只用合成对象、隔离tenant、测试身份及明确目的/期限；不得复制生产数据库、客户资料、密钥或session。真实外部包/云/权限必须在另有批准的范围。环境名Test本身不是隔离授权，也不能让生产引用Testing代码。[原文B:71](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d8/d8b4ef19a1b3a0a5__PLT-TEST-01_完整静态设计_v0.1.md:71) [原文B:86](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d8/d8b4ef19a1b3a0a5__PLT-TEST-01_完整静态设计_v0.1.md:86)
抓取包含baggage和完整tags，禁止生产启用AllDataRecorder；safe error与敏感资料脱敏分别验证，不把cp6.* key allowlist当全面隐私审计。[原文B:49](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d8/d8b4ef19a1b3a0a5__PLT-TEST-01_完整静态设计_v0.1.md:49) [原文B:53](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d8/d8b4ef19a1b3a0a5__PLT-TEST-01_完整静态设计_v0.1.md:53) [原文B:55](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d8/d8b4ef19a1b3a0a5__PLT-TEST-01_完整静态设计_v0.1.md:55)

## 8. 页面与服务器校验

报告逐条展示case definition/hash、attempt、执行层级、实际预置与cleanup、断言和未运行原因。没有业务页面是工具本身边界，不把覆盖业务场景要求标N/A消失。[原文B:19](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d8/d8b4ef19a1b3a0a5__PLT-TEST-01_完整静态设计_v0.1.md:19) [原文B:94](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d8/d8b4ef19a1b3a0a5__PLT-TEST-01_完整静态设计_v0.1.md:94)
缺前提为NotRun或该CI定义的Failed，不能跳过关键用例算Pass；隐藏vendor标签/queue溢出或未结束活动都不得伪完整捕获。[原文B:53](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d8/d8b4ef19a1b3a0a5__PLT-TEST-01_完整静态设计_v0.1.md:53) [原文B:55](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d8/d8b4ef19a1b3a0a5__PLT-TEST-01_完整静态设计_v0.1.md:55) [原文B:88](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d8/d8b4ef19a1b3a0a5__PLT-TEST-01_完整静态设计_v0.1.md:88)

## 9. 实施顺序与固定源码差距

先冻结case和Synthetic输入/期望→确认隔离/InMemory terminal/pipeline顺序/观测归属→原语分支→真实Host准确包/schema/provider/事务adapter→授权环境RuntimeQualification→另有生产采用。Library单测ProjectReference合理，真feed consumer qualification则禁source fallback，两者用途不能混。[原文B:73](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d8/d8b4ef19a1b3a0a5__PLT-TEST-01_完整静态设计_v0.1.md:73) [原文B:80](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d8/d8b4ef19a1b3a0a5__PLT-TEST-01_完整静态设计_v0.1.md:80)
覆盖CTX/HTTP/MSG/DEL/OBS/RELEASE全部精确合同，尤其S6原operation先读、共同fence、CRM所有早退内容门和三typed replay。Main现ActiveTenant先验是待修差距，不能把现行为写成目标期望。[原文B:82](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d8/d8b4ef19a1b3a0a5__PLT-TEST-01_完整静态设计_v0.1.md:82) [原文B:84](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d8/d8b4ef19a1b3a0a5__PLT-TEST-01_完整静态设计_v0.1.md:84)

## 10. 验收设计与未运行状态

3 SPEC/24AC均NOT_RUN。UnitContract只证明原语/DTO，HostIntegration只证明指定入口，RuntimeQualification只证明特定环境/版本/时间；ProductionAdoption另需真实部署权限配置/业务receipt/独立接受。SC23/SC24引用保留，但TEST工具存在不算这些业务场景完成。[原文B:73](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d8/d8b4ef19a1b3a0a5__PLT-TEST-01_完整静态设计_v0.1.md:73) [原文B:80](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d8/d8b4ef19a1b3a0a5__PLT-TEST-01_完整静态设计_v0.1.md:80) [原文B:102](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d8/d8b4ef19a1b3a0a5__PLT-TEST-01_完整静态设计_v0.1.md:102)
未来必须区分script-step与inner请求数、NonIdempotent零重试、Unknown原结果查询、复杂trace DAG与全局捕获干扰、synthetic canary安全和cleanup失败覆盖整体Pass。[原文B:23](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d8/d8b4ef19a1b3a0a5__PLT-TEST-01_完整静态设计_v0.1.md:23) [原文B:33](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d8/d8b4ef19a1b3a0a5__PLT-TEST-01_完整静态设计_v0.1.md:33) [原文B:51](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d8/d8b4ef19a1b3a0a5__PLT-TEST-01_完整静态设计_v0.1.md:51) [原文B:55](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d8/d8b4ef19a1b3a0a5__PLT-TEST-01_完整静态设计_v0.1.md:55) [原文B:88](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d8/d8b4ef19a1b3a0a5__PLT-TEST-01_完整静态设计_v0.1.md:88)

## 11. 缺口、依赖与采用门

Required-TEST-01..07为case/spec、隔离授权、artifact、真实事务、capture、cleanup、独立执行。缺02不运行；缺04不声称集成；缺05/06不Pass；缺07不Accepted。工具库不能证明真实共同事务、内容门/最终fence和typed native数据，需Owner实际接入。[原文B:35](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d8/d8b4ef19a1b3a0a5__PLT-TEST-01_完整静态设计_v0.1.md:35) [原文B:92](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d8/d8b4ef19a1b3a0a5__PLT-TEST-01_完整静态设计_v0.1.md:92)
本次只读/文档工作没有测试执行授权，所有结果保持NOT_RUN；历史run仍按原来源保存不追认为本次AC。[原文B:3](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d8/d8b4ef19a1b3a0a5__PLT-TEST-01_完整静态设计_v0.1.md:3) [原文B:90](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d8/d8b4ef19a1b3a0a5__PLT-TEST-01_完整静态设计_v0.1.md:90)

## 12. 阅读覆盖与证据

本轮新全文语义阅读正文L1–102；S7最终独审L1–161、组合索引L1–248全文。接受JSON读取Target/Disposition/AcceptanceDecision/AcceptedBody/AcceptedCurrentCombination/ReviewQualifications/OwnerAndExecutionBoundary完整七字段，其余来源历史字段待L1/L2集中核对，未声称全文。[原文B:1](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d8/d8b4ef19a1b3a0a5__PLT-TEST-01_完整静态设计_v0.1.md:1) [原文R:1](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/39/397b935b08bca80e__S7_Platform_v0.2.1_七稿组合完整静态接受_独立复审结论.txt:1) [原文C:1](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/3e/3ec93373fda65d46__S7_PLATFORM_COMBINED_FREEZE_v0.2.1.json:1)
[共享合同](D:/CP6/docs/CP6_开发设计文档_20261010/contracts/platform-engineering-contracts.json)保存关键精确协议/字段原段；[阅读记录](D:/CP6/docs/CP6_开发设计文档_20261010/evidence/platform-engineering-reading.json)逐源记录新读、继承和未读行段。本文未把候选seam、表或保证写成现包已实现。

**当前必要材料归并状态。** 本册为 `required_materials_consolidated`：选定正文、规范附件及准确接受/复核范围已按直接实读、结构化语义或精确复用归并。身份清单核对不等底层源码全文阅读；历史草稿、候选实现和Office/HTML视觉未核范围仍单列。可复查[逐件阅读与处置](D:/CP6/docs/CP6_开发设计文档_20261010/evidence/platform-engineering-reading.json)及[本组闭包台账](D:/CP6/docs/CP6_开发设计文档_20261010/worklogs/platform-engineering-progress.json)。当前必需规范未读清单为空；具体Owner采用、参数/物理接口、真实运行和本节已有来源缺口不因整理闭包而改变。
