# PLT-MSG-01 · 精确消息合同与传输入口适配

整理状态：`core_semantics_consolidated`。静态设计接受不升级为实现或运行接受。

## 1. 业务目的、操作者和Owner边界

Platform负责CloudEvent封装、精确schema/bundle校验及选定transport适配；每producer/consumer业务Owner负责业务键、权限、原结果、真实事务、消费版本与恢复。现Platform示例bundle只有contract-example.changed.v1，不能冒充全部业务合同。[原文B:9](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/7b/7b59ff237b27e8ef__PLT-MSG-01_完整静态设计_v0.2.md:9) [原文B:17](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/7b/7b59ff237b27e8ef__PLT-MSG-01_完整静态设计_v0.2.md:17) [原文B:21](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/7b/7b59ff237b27e8ef__PLT-MSG-01_完整静态设计_v0.2.md:21)
先认证订阅/Host，再确定可信profile/route；消息body或任意header不能自选profile、schema文件路径或validator实例。[原文B:49](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/7b/7b59ff237b27e8ef__PLT-MSG-01_完整静态设计_v0.2.md:49) [原文B:51](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/7b/7b59ff237b27e8ef__PLT-MSG-01_完整静态设计_v0.2.md:51)

## 2. 选定规范与接受范围

当前正文v0.2，SHA256 `7b59ff237b27e8efc452ae1ecf776ac77002382466a9e7d9744a3bf924e505e2`，136行，纳入S7 v0.2.1七稿混合组合：CTX/OBS/RELEASE/TEST v0.1、HTTP/MSG v0.2、DEL v0.2.1。接受记录Disposition为ACCEPTED_CURRENT_MARKDOWN_STATIC_DETAILED_DESIGN。[原文A:175](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d2/d23ce6f35e044b2d__UA-20261008-S7-PLT-MSG-01-STATIC-MD02.json:175) [原文R:9](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/39/397b935b08bca80e__S7_Platform_v0.2.1_七稿组合完整静态接受_独立复审结论.txt:9)
delegate静态接受，非用户亲签；原02:18:29Z决定未恢复逐字或messageId，04:09:04Z另有恢复确认。组合索引中DEL的pending/null保留上传前历史，最终接受以精确正文+独审+接受记录为准。258项组合AC全NOT_RUN，本Target 40 AC；运行和Owner采用UNPROVEN、实施/部署未授权。[原文A:8](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d2/d23ce6f35e044b2d__UA-20261008-S7-PLT-MSG-01-STATIC-MD02.json:8) [原文A:340](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d2/d23ce6f35e044b2d__UA-20261008-S7-PLT-MSG-01-STATIC-MD02.json:340) [原文R:132](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/39/397b935b08bca80e__S7_Platform_v0.2.1_七稿组合完整静态接受_独立复审结论.txt:132)

## 3. 对象、字段与不可替代的身份

|对象|字段/不变量|
|---|---|
|CloudEvent descriptor|Id/Source/Type/Subject/Time/DataSchema/TenantId/CorrelationId/CausationId/AggregateId/AggregateVersion/SchemaVersion/Region；data须JSON object并clone，specversion1.0、structured JSON。Time UTC offset0、Tenant非空小写Guid D、AggregateVersion≥1，schema三段数字无前导零。[原文B:25](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/7b/7b59ff237b27e8ef__PLT-MSG-01_完整静态设计_v0.2.md:25) [原文B:27](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/7b/7b59ff237b27e8ef__PLT-MSG-01_完整静态设计_v0.2.md:27)|
|MessageRouteBindingV1|profile/route、双方Owner/service、type/schemaId/version/SHA、bundleVersion/hash、validator/publisher实例、topic/partition/region/tenant规则、trusted metadata、subscription/group、businessKey/digest/receipt/transaction、routeEpoch及旧路由。[原文B:17](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/7b/7b59ff237b27e8ef__PLT-MSG-01_完整静态设计_v0.2.md:17)|
|ExactContractSelectionV1|唯一键(profileId,routeId,eventType,schemaId,schemaVersion)→schemaSHA/bundleHash/bundleLocationRef/validatorInstanceRef/publisherAdapterRef/bindingVersion；相同hash重复键也拒，冲突hash报ContractIdentityConflict。[原文B:49](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/7b/7b59ff237b27e8ef__PLT-MSG-01_完整静态设计_v0.2.md:49)|
|四种身份|CloudEvent.id/Outbox messageId、Owner businessOperation、aggregateVersion、schema/bundle各自不同；rawEnvelopeSHA、Owner canonicalInput和S6 typed digest也不得混用。[原文B:19](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/7b/7b59ff237b27e8ef__PLT-MSG-01_完整静态设计_v0.2.md:19) [原文B:73](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/7b/7b59ff237b27e8ef__PLT-MSG-01_完整静态设计_v0.2.md:73)|

## 4. 状态流程、入口与本地写集

生命周期DraftMetadata→ValidatedEnvelope→DurablyEnqueued→TransportAccepted→ConsumerObserved→BusinessApplied/Rejected/Unknown；codec只完成验证，publish receipt只完成transport。[原文B:35](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/7b/7b59ff237b27e8ef__PLT-MSG-01_完整静态设计_v0.2.md:35) [原文B:98](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/7b/7b59ff237b27e8ef__PLT-MSG-01_完整静态设计_v0.2.md:98)
接收先可信route/profile→size/media→parse/重复名→exact instance→schema/codec/业务envelope→topic/partition/tenant/region→应用分类门→DEL与S6同事务内容门→commit确认后ack。未知合同隔离而非SUCCESS/DROP吞掉；暂时依赖错误须有持久恢复/再投证明。[原文B:69](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/7b/7b59ff237b27e8ef__PLT-MSG-01_完整静态设计_v0.2.md:69) [原文B:71](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/7b/7b59ff237b27e8ef__PLT-MSG-01_完整静态设计_v0.2.md:71) [原文B:77](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/7b/7b59ff237b27e8ef__PLT-MSG-01_完整静态设计_v0.2.md:77)
生产者业务+Outbox同commit；消费者Inbox/receipt/投影同应用事务，所有precheckpoint早退亦受内容门。旧已存消息保原bytes与原版本instance，不重新序列化最新schema。[原文B:21](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/7b/7b59ff237b27e8ef__PLT-MSG-01_完整静态设计_v0.2.md:21) [原文B:53](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/7b/7b59ff237b27e8ef__PLT-MSG-01_完整静态设计_v0.2.md:53)

## 5. 接口、消息与依赖

|可信profile|确切地址和处理|
|---|---|
|MSG-P04P05-GENERIC-v1|pubsub=cp6-kafka-pubsub；topic=cp6.<producer>.<eventSlug>.v<major>≤249；partition=<tenant:D>/<aggregate>；独立Cp6ContractBundle→CloudEventValidator→DaprDeliveryValidator，同实例DaprEventPublisher。[原文B:91](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/7b/7b59ff237b27e8ef__PLT-MSG-01_完整静态设计_v0.2.md:91)|
|MSG-C02-IDENTITY-v1 / C02-MAIN-CRM-IDENTITY-v1|原topic=cp6.platform.events.v1；partition=<tenant:D>:<aggregate>；consumer=cp6-crm-identity-v1；五type均schemaVersion1.0.0；原bundle index hash 24df72e9446723fa4cf3ad12b7e273ff937ef9b0ed154b8c926b1bac6c7eab63；Main IdentityEventContracts/Validator/DispatchWorker的IdentityDaprPublisher和CRM专属IdentityEventValidator，不能接通用publisher改址。[原文B:92](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/7b/7b59ff237b27e8ef__PLT-MSG-01_完整静态设计_v0.2.md:92)|
C02五type为com.gtx537.platform.tenant.changed.v1、user.changed.v1、department.changed.v1、permission.changed.v1、token.revoked.v1（后四同前缀），schemaIds/五SHA均在共享机器合同中原段保留；这里的hash来源是原manifest声明，尚非本次schema bytes运行验证。[原文B:94](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/7b/7b59ff237b27e8ef__PLT-MSG-01_完整静态设计_v0.2.md:94)
C02通过原/dapr/subscribe及/internal/crm/identity/events选入口；保原payload、partitionKey、rawPayload=true、普通与撤权优先预算。两profile互相送错严格拒绝，不fallback、不按type猜profile。Dapr服务调用appId/path只取登记allowlist；BaseAddress绝对URI不证明sidecar可信。[原文B:96](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/7b/7b59ff237b27e8ef__PLT-MSG-01_完整静态设计_v0.2.md:96) [原文B:106](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/7b/7b59ff237b27e8ef__PLT-MSG-01_完整静态设计_v0.2.md:106)

## 6. 事务、幂等、并发及恢复

bundle loader按本地受控root精确hash，单bundle每type/schemaId一版本；多版本由独立不可变bundle和validator静态精确选择表承载，不创建新动态registry、不远程拉schema。未知版本/重复绑定未就绪；不能latest/minor兼容fallback。[原文B:43](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/7b/7b59ff237b27e8ef__PLT-MSG-01_完整静态设计_v0.2.md:43) [原文B:45](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/7b/7b59ff237b27e8ef__PLT-MSG-01_完整静态设计_v0.2.md:45) [原文B:49](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/7b/7b59ff237b27e8ef__PLT-MSG-01_完整静态设计_v0.2.md:49) [原文B:51](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/7b/7b59ff237b27e8ef__PLT-MSG-01_完整静态设计_v0.2.md:51)
producer commit后发送，broker成功而mark失败会同message重复；consumer原Inbox+业务receipt去重，禁止新ID规避冲突。commit未知保持原message回查，不能提前SUCCESS或DROP。transport回2xx/ack不证明业务Owner已应用。[原文B:98](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/7b/7b59ff237b27e8ef__PLT-MSG-01_完整静态设计_v0.2.md:98) [原文B:104](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/7b/7b59ff237b27e8ef__PLT-MSG-01_完整静态设计_v0.2.md:104) [原文B:110](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/7b/7b59ff237b27e8ef__PLT-MSG-01_完整静态设计_v0.2.md:110)
旧schema退休只禁止新发布，历史validator仍保留。变换必须经批准生成新message并保原引用/转换版本，不改原payload/hash。切换先盘点原消息和in-flight、只读观察、停旧writer新接纳、核唯一writer/epoch后另授权切流；回退保新schema兼容reader与原receipt，不清队列全重放。[原文B:55](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/7b/7b59ff237b27e8ef__PLT-MSG-01_完整静态设计_v0.2.md:55) [原文B:57](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/7b/7b59ff237b27e8ef__PLT-MSG-01_完整静态设计_v0.2.md:57) [原文B:124](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/7b/7b59ff237b27e8ef__PLT-MSG-01_完整静态设计_v0.2.md:124)

## 7. 权限、租户和敏感信息

外层topic/partition必须由认证sidecar/broker元数据或受信订阅常量提供，不能重算payload的值再称已核broker。TransportMetadataEvidenceV1保存sourceKind/peer/subscription/topicSource/partitionKeySource/transportVersion/configHash/evidence；没有真实metadata关闭相应新保证。[原文B:102](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/7b/7b59ff237b27e8ef__PLT-MSG-01_完整静态设计_v0.2.md:102)
候选一般reader最多1MiB/depth64、错误locations32×path256、禁止重复键；C02专属原depth32及alias/region/外内绑定限制不放宽。只受控隔离原payload，公开错误无body/exception，trace不做授权且不带baggage。[原文B:31](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/7b/7b59ff237b27e8ef__PLT-MSG-01_完整静态设计_v0.2.md:31) [原文B:67](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/7b/7b59ff237b27e8ef__PLT-MSG-01_完整静态设计_v0.2.md:67) [原文B:69](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/7b/7b59ff237b27e8ef__PLT-MSG-01_完整静态设计_v0.2.md:69) [原文B:75](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/7b/7b59ff237b27e8ef__PLT-MSG-01_完整静态设计_v0.2.md:75) [原文B:79](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/7b/7b59ff237b27e8ef__PLT-MSG-01_完整静态设计_v0.2.md:79)

## 8. 页面与服务器校验

只有按tenant/app授权的TransportAdoptionView，分别展示CodePresent/HostRegistered/ConfigurationState/RuntimeObserved/BusinessApplied/IndependentAcceptance及disableReason，不展示跨租户payload。不新增任意topic publish、上传替代payload或force重放按钮。[原文B:79](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/7b/7b59ff237b27e8ef__PLT-MSG-01_完整静态设计_v0.2.md:79) [原文B:128](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/7b/7b59ff237b27e8ef__PLT-MSG-01_完整静态设计_v0.2.md:128)
服务器按trusted route先选profile，body中的tenant/type/version仅待验证数据。界面“published/consumed”须注明transport/校验测量点，不能标业务完成。[原文B:100](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/7b/7b59ff237b27e8ef__PLT-MSG-01_完整静态设计_v0.2.md:100) [原文B:128](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/7b/7b59ff237b27e8ef__PLT-MSG-01_完整静态设计_v0.2.md:128)

## 9. 实施顺序与固定源码差距

先逐Owner冻结真实route、schema bytes/精确hash、可信metadata及原业务receipt；实现独立精确选择，再接各Host的MSG验证→DEL/S6共同事务→commit后ack。固定Main/CRM C02原合同保真，CRM其他business events和ERP各自登记，Main内部同步/Bridge保原路径，不强制全部Kafka化。[原文B:87](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/7b/7b59ff237b27e8ef__PLT-MSG-01_完整静态设计_v0.2.md:87) [原文B:114](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/7b/7b59ff237b27e8ef__PLT-MSG-01_完整静态设计_v0.2.md:114) [原文B:122](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/7b/7b59ff237b27e8ef__PLT-MSG-01_完整静态设计_v0.2.md:122)
现通用validator成功即发consumed遥测，发生在业务处理前；通用Invoke只查技术2xx，不应统一重试业务拒绝。上述是固定SHA源状，不宣称所有新适配已实现。[原文B:100](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/7b/7b59ff237b27e8ef__PLT-MSG-01_完整静态设计_v0.2.md:100) [原文B:106](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/7b/7b59ff237b27e8ef__PLT-MSG-01_完整静态设计_v0.2.md:106)

## 10. 验收设计与未运行状态

5 SPEC/40AC全部NOT_RUN；B02闭合覆盖可信profile、exact tuple、独立bundle、C02/P05分址和无metadata伪验证。未来必须覆盖未知版本隔离、同原bytes不重编码、所有commit未知与早退、两profile互拒、schema/hashes与Owner业务键分域。[原文B:81](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/7b/7b59ff237b27e8ef__PLT-MSG-01_完整静态设计_v0.2.md:81) [原文B:112](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/7b/7b59ff237b27e8ef__PLT-MSG-01_完整静态设计_v0.2.md:112) [原文B:130](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/7b/7b59ff237b27e8ef__PLT-MSG-01_完整静态设计_v0.2.md:130) [原文R:72](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/39/397b935b08bca80e__S7_Platform_v0.2.1_七稿组合完整静态接受_独立复审结论.txt:72)

## 11. 缺口、依赖与采用门

Required-MSG-01..07：Owner route/key/receipt、精确双方版本与旧消息、真实metadata/服务身份、所有事务writer、ack/有界重试和unknown、单writer切换、独立真实AC。缺01/02禁止新type；缺03不收新route；缺04/05不启副作用consumer；缺06不切流；缺07运行仍UNPROVEN。[原文B:120](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/7b/7b59ff237b27e8ef__PLT-MSG-01_完整静态设计_v0.2.md:120)
消息保留与业务审计/撤权tombstone由DEL及SYS政策共同决定，broker短保留不能缩短业务去重；PLM文档生命周期仍由PLM持账。[原文B:126](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/7b/7b59ff237b27e8ef__PLT-MSG-01_完整静态设计_v0.2.md:126)

## 12. 阅读覆盖与证据

本轮新全文语义阅读正文L1–136；S7最终独审L1–161和组合索引L1–248全文。接受JSON实读Target/Disposition/AcceptanceDecision/AcceptedBody/AcceptedCurrentCombination/ReviewQualifications/OwnerAndExecutionBoundary完整七字段，其他来源历史字段仍待最终L1/L2核对，未称全文。[原文B:1](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/7b/7b59ff237b27e8ef__PLT-MSG-01_完整静态设计_v0.2.md:1) [原文R:1](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/39/397b935b08bca80e__S7_Platform_v0.2.1_七稿组合完整静态接受_独立复审结论.txt:1) [原文C:1](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/3e/3ec93373fda65d46__S7_PLATFORM_COMBINED_FREEZE_v0.2.1.json:1)
可直接使用的精确字段/协议段存[公共合同](D:/CP6/docs/CP6_开发设计文档_20261010/contracts/platform-engineering-contracts.json)，逐源覆盖和未读行段存[阅读记录](D:/CP6/docs/CP6_开发设计文档_20261010/evidence/platform-engineering-reading.json)。本文是源规范的开发解释；候选API/表/字段保持候选，不新增接受身份。

**当前必要材料归并状态。** 本册为 `required_materials_consolidated`：选定正文、规范附件及准确接受/复核范围已按直接实读、结构化语义或精确复用归并。身份清单核对不等底层源码全文阅读；历史草稿、候选实现和Office/HTML视觉未核范围仍单列。可复查[逐件阅读与处置](D:/CP6/docs/CP6_开发设计文档_20261010/evidence/platform-engineering-reading.json)及[本组闭包台账](D:/CP6/docs/CP6_开发设计文档_20261010/worklogs/platform-engineering-progress.json)。当前必需规范未读清单为空；具体Owner采用、参数/物理接口、真实运行和本节已有来源缺口不因整理闭包而改变。
