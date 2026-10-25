# W135 D6冻结流程适用与真实来源核定

结论：原D6的公开配置、Attempt、提交/H01、必要审计/同意/保留和原事件发布已有明确采用；本轮把剩余公共能力适用与资料来源逐项固定。原合同允许本路径不分配Lead业务号，闭合公开/H01合同没有附件调用；不再为此路径另造NUM/FILE协议。配置Owner已经在原事务发布日历，并以显式激活完成当前授权批准，不重复实现发布器。企业政策与日历依据、真实身份/密钥/路由登记、部署宿主资格及真实消息链路仍未证明，D6继续PARTIALLY_ADOPTED，完整任务卡及验收不提升。下一实现窗口转D2新对象可信归属。

本轮无产品代码、合同、数据库或运行配置变更。依据[原七天计划](../../planning/20261022-seven-day-delivery.md)、[WP03-01/02](../../task-plans/WP03.md)、[W129出口审计](../W129-01/DELIVERY.md)，保留其原始要求；本结论补充历史审计中尚未核清的部分，不把旧未完成项直接抹掉。当前Main基线`8919cd385a5f40dfb11e92fa0b99e9f92e2474b1`，CRM基线`88328a6025527e3b7751d5d003dbadbf2f008066`，均经远端核对。W134最终proof证实Main549/CRM159/Handoff110已完成，Issue157关闭；本轮只核销其台账收尾快照一次。

## 冻结调用边界

有效组合为`CRM-INQ-TERMINAL-PROJECTION/20261013-R1`，来源Main `ce38171868312588ce8f4b9e843ed6c39f4d0b0f`；[采用说明](https://github.com/GTX537/CP6.CRM/blob/88328a6025527e3b7751d5d003dbadbf2f008066/contracts/s01/README.md)固定profile、patch与effective各自来源。本轮重算[有效schema](https://github.com/GTX537/CP6.CRM/blob/88328a6025527e3b7751d5d003dbadbf2f008066/contracts/s01/revisions/CRM-INQ-TERMINAL-20261013-R1/crm-inq.terminal.openapi.json)为`fd9a9444eac23225ecbf8ab91856dc7df2c0e4da53d9b765cbfccdeb2223ba2d`，与生成清单及profile相同。不是以当前代码缺调用反推设计不需要。

|能力|原要求与当前实际路径|适用结论及保留范围|
|---|---|---|
|NUM|原INQ V15正文第179行明确`LeadNo`可为NULL，除非当前数据库分配器实际提供业务号；[leads-0.2.0 overlay](https://github.com/GTX537/CP6.CRM/blob/88328a6025527e3b7751d5d003dbadbf2f008066/contracts/data/runtime-overlays/leads-0.2.0.json)明确第一片无业务编号，不从UUID伪造。当前原生[WriteGraph](https://github.com/GTX537/CP6.CRM/blob/88328a6025527e3b7751d5d003dbadbf2f008066/src/CP6.CRM.Api/Inquiry/CrmInquirySubmissionOwnerV1.Graph.cs)创建Lead没有调用分配器；生成映射为nullable、ValueGeneratedNever。|当前冻结公开/H01路径采用原合同的未分配表示。`displayReference`是原回执中的不透明显示值，不能称业务号或NUM分配收据。WP02-16完整提供者与WP03-02整卡的共同服务前置仍保留；将来引入真实Lead编号消费，必须另以准确规则版本、scope、原键及共同事务/恢复协议采用，不能把本判断当作运行采用证明。|
|FILE|`PublicSubmissionRequest`、`PublicReceipt`、`H01ConvertInquiryCommand`和`LeadCreationReceipt`四个根均`additionalProperties:false`；递归解析引用与组合，未定义attachment/file-operation/allocation-receipt/LeadNo成员。公开请求的allOf只约束已有email/phone，不开放额外字段。实际原生公开/H01图没有FILE调用。|本冻结输入不接收附件，也无FILE operation需要传递。拒绝额外成员的原合同不扩宽。WP02-14、隐私导出制品及当前下载仍未完成；完整WP03-03/04不因本次核定消失。若新增附件/导出路径，须先按对应正式版本与当前授权/扫描/绑定协议采用。|
|AUD与原结果|实际Submission/可选H01全图、原回执、审计与Outbox在原Owner事务；W134已修正Website10审计，原重放不改变原ID/字节。|本期必要本地审计/原结果范围已采用。不是SYS-AUD全域查询/保留或CRM-ACT的完成证明；历史审计不回写。|
|PRIV|冻结Bundle含实际提交所用notice hash、policy version、purpose、允许字段与各处置保留单位。Main当前真人撤回来源和独立维护服务来源，经CRM精确用途/资源及同事务身份检查；Owner另核原持久期限和hold。|本期提交同意、当前披露、撤回与必要保留范围复用已有交付。没有跨库直写Main隐私主账，也不声称采用了不存在的全域PRIV接口。W128的export/download来源已交付；原生导出制品、FILE单次下载及全subject编排仍是后续完整目标。|
|消息|W133已将五类原生事件接正式Platform0.10.3 Outbox，具备持久原run/claim/mutation恢复、有限publisher与当前身份门。|补齐W129记录的原生持久入队后缺发布代码；证据是隔离PG/HTTP与本地Dapr接口夹具。真实Dapr/Kafka、企业ACL/凭据/消费者及SLO仍NOT_RUN，不把发送接口成功当下游业务完成。|

原文精确入口：[INQ V15归档来源索引](../../../design-handbook/modules/CRM-INQ-01.md)，正文SHA-256 `fa7d0f3388911b30992162f693b8f3340739171d17ae6256c4e72fc569f7890d`，LeadNo规则位于第179行。原件在主目录保留，不复制进工作树；本轮已直接读取。其早期枚举示例须继续服从已接受CP22勘误；本次没有改变有效合同或合法输入。

Main当前PRIV来源分别为[真人同意](https://github.com/GTX537/CP6/blob/8919cd385a5f40dfb11e92fa0b99e9f92e2474b1/CP6.WebApi/Services/InquiryConsentPermissionSourceV1.cs)与[维护服务](https://github.com/GTX537/CP6/blob/8919cd385a5f40dfb11e92fa0b99e9f92e2474b1/CP6.WebApi/Services/InquiryRetentionPermissionSourceV1.cs)。前者交集当前管理部门/隐私部门与consent写权限；后者读取实际服务登记及当前IAM角色/action。CRM的[ConsentCapabilities](https://github.com/GTX537/CP6.CRM/blob/88328a6025527e3b7751d5d003dbadbf2f008066/src/CP6.CRM.Api/Inquiry/CrmInquiryConsentCapabilitiesV1.cs)和[RequireRetentionSource](https://github.com/GTX537/CP6.CRM/blob/88328a6025527e3b7751d5d003dbadbf2f008066/src/CP6.CRM.Api/Inquiry/CrmInquiryServiceIdentityV1.cs)保留独立用途、精确Site/Form与当前身份约束；维护身份本身不授权清除某行。

## 资料发布已经存在，真实来源仍须证明

当前[SaveCore](https://github.com/GTX537/CP6.CRM/blob/88328a6025527e3b7751d5d003dbadbf2f008066/src/CP6.CRM.Api/Inquiry/CrmInquiryConfigurationOwnerV2.cs)先取得当前原会话、Main目的签名、持久信任、实际Site/Form、候选部门与原保存槽；在原生外层事务中创建calendar draft、调用`PublishDraft`、`SavePublishedAsync`、保存完整draft/绑定及审计/原结果。`PostgreSqlCalendarSnapshotsV1`只是存储helper的注释不等于没有上游发布Owner。

[Preview/Activate](https://github.com/GTX537/CP6.CRM/blob/88328a6025527e3b7751d5d003dbadbf2f008066/src/CP6.CRM.Api/Inquiry/CrmInquiryConfigurationOwnerV2.Lifecycle.cs)重新检查当前权限、同一actor的短时一次性proof、原内容/渲染hash/精确生效时间；激活保存批准授权的盲化引用及真实actor/time，追加前驱/后继生效区间。政策版本/notice等是获权配置者提交的资料，字段合法与技术批准不自动证明企业法定依据。

[CalendarStorageCodec.Publication](https://github.com/GTX537/CP6.CRM/blob/88328a6025527e3b7751d5d003dbadbf2f008066/src/CP6.CRM.Infrastructure/Persistence/CrmCalendarStorageCodec.Publication.cs)记录实际TimeZoneInfo序列化规则指纹；新期限计算前检查当前规则，历史冻结期限不重新解释。`TimeZoneRulesVersion`保留NULL，不杜撰tzdb发布号。这个指纹证明本宿主实际算法输入，不能替代专业日历来源或另一部署宿主的时区资格。

## 未证前置与恢复入口

以下材料在本期交付证据中未证明；不声称已搜索或否定所有企业系统中的资料。责任指所需权威来源，不是另行派工。技术工作继续，不等待虚构默认。

|未证材料|必须来自什么事实|恢复时使用的现有入口与验明内容|
|---|---|---|
|企业隐私告知与保留政策|业务/政策责任人批准的真实notice、privacyPolicyVersion/policyVersion、允许字段、处置保留单位与值及生效时间；不能把测试例值当默认|原配置页/API `POST /api/crm/v1/configuration`，再同一版本`/preview`、`/activate`；核对内容hash、当前真人批准及准确生效区间。本轮不代写法律政策。|
|专业日历与目标宿主时区|真实工作区间/节假日依据、timeZone、适用版本和目标宿主实际TimeZoneInfo规则/资格|同一配置保存内的日历发布入口及`AssertCurrentRules`；用真实候选宿主验证DST/边界、原冻结期限不变。不能用本机指纹冒充目标tzdb版本。|
|租户/Site/Form与真实IAM/服务信任|真实原对象、源事件版本、受信issuer/region、登记/撤权事实及业务负责人的实际授权|[NativeOptions](https://github.com/GTX537/CP6.CRM/blob/88328a6025527e3b7751d5d003dbadbf2f008066/src/CP6.CRM.Api/Inquiry/CrmInquiryNativeOptionsV2.cs)显式Tenants/PrimaryPair/PublicForms、持久Trust、Main当前签发与CRM身份基线/增量；管理primary pair不自动公开。先验证真实登记，再独立安排目标环境采用。|
|保护密钥与部署来源|受控fingerprint/key version、实际DataProtection保护/轮换、受信BFF来源及服务凭据|[原生Host](https://github.com/GTX537/CP6.CRM/blob/88328a6025527e3b7751d5d003dbadbf2f008066/src/CP6.CRM.Api/Inquiry/CrmInquiryNativeHostV2.cs)既有配置/持久key ring/保护证书或Windows DPAPI，原注册摘要检查及当前身份路径。不得以隔离测试密钥或单纯启用开关代替。|
|目标宿主与真实消息链路资格|实际候选源码/包/镜像身份、canonical normalizer/runtime版本及适用验证器；企业broker TLS/ACL/订阅/凭据、生产传播SLO与实际消费者|D5独立宿主资格与原生身份/Outbox已存在的运行入口；在真实候选上核对实际规范化/保护实现及已冻结合同输入。运行身份仍Unproven；真实broker继续NOT_RUN。旧Docker socket阻塞保留，本轮不操作该socket或启动Docker。|
|最终相关集成|最后已采用输入上的跨仓PG/HTTP/页面集成及本期剩余Owner交付|D7在D2/D3与上述适用前置明确后执行受影响检查。此前页面组件/BFF/浏览器证据不自动改称完整Next SSR或真实IdP/TLS验收。|

原生Host没有自动安装/迁移；公开、原图写入、保留等开关默认关闭。本报告不授予真实角色、切换环境、部署或降低门禁。上述证据补齐前D6和相关正式验收保持未完成。

## 实际核对、状态和下一实现

本轮阅读当前远端精确源码、冻结原文/schema/任务卡；重算有效schema hash，检查四根的闭合性与引用属性，逐项追踪日历发布/激活和Main→CRM隐私权限消费。复核W105/114/117/118/124/127/128/133/134最终proof均PASS、已释放，所引CRM源提交均为当前主线祖先。其原验证范围分别保留在对应交付报告；没有把历史成功称作本轮重跑或当前全仓构建。W133原生发布和W134审计修复分别复用[W133](../W133-01/DELIVERY.md)、[W134](../W134-01/DELIVERY.md)。

验证仅文档完整diff、引用/来源及台账一致性；不新增静态断言测试，不启动产品/数据库/浏览器或GitHub Actions。唯一台账、七天计划和四份状态文件同步；W134归档CLOSED一次。D1/D4 DONE，D2/D3/D5/D6/D7继续PARTIALLY_ADOPTED；375正式卡24DONE/2IN_PROGRESS/349PLANNED、125完整目标0关闭、247NOT_RUN+41REFERENCE_ONLY不变。旧W129剩余42–70h是历史低置信估计，本轮不以审计行数或阶段比率重算工时。

下一窗口从最新Main实施D2新对象可信部门归属，先结合Machine等实际创建入口确定准确边界。当前`MachineDto`没有部门输入，`CreateAsync`只有创建后的`RequireMachine`；映射INSERT会更新身份根中的standalone revision，而OperationsOwner最终门要求组织版本不变。故必须显式确定可信目标部门、创建专用受权入口、同UoW可证明的版本转换与原结果/审计，保留并发撤权和未知提交恢复；不能嵌套现有IAM自有事务，也不能全面跳过版本比较。新库存流水只能按本次真实来源对象继承，不能用当前业务键回填历史。REP/RMA共同Owner门、D3计划写协议及D5/D7原出口继续保留。

本轮估25–40分钟；最终Main START至finish由官方生命周期计一次。两仓采用与分支/席位处置在本报告同轮收尾记录，并由`wave135-final-verification.json`核销Main提交前快照。固定目录保留复用，用户未跟踪文件与缓存保留。

## 本轮采用快照

Handoff [PR](https://github.com/GTX537/CP6.AI-Handoff/pull/111)：`6e727735e4eddfad5a08b04f1cf0b412192439f0` → `36aaa8b3dbef4d1e7ae374560bd852a0eb081fb5`；远端完整树采用，codex/w135-handoff本地/远端已删除，DEV-2已finish释放且干净。Main本轮正常PR/finish待最后执行，不能把此提交前快照称为两仓全部完成。当前登记6.92分钟，最终以Main finish计。CRM/Platform无本轮写入；固定路径和缓存保留。
