# W76 / 七天一期D6：原生不可变Draft Bundle保存

COORD本聊天单人，2026-10-23T19:13:07.448884+00:00。Main `ad5f1e939985a395c08a784994e1fd89060a1d19`，CRM `318661183a8c9e72c4a5b1f2bfa1e5512d4495cf`，Handoff `752b6a1365dc1dc93917ce16737941b3b708e4e4`。W75 CRM115/Main456/Handoff51全部采用，33独立PG通过，三准确分支删除、固定树干净detached、全部席位释放、Actions0、自有PG停止保留；实际18.14min＋前置2min。W37–W75不重复。

## 前置与限定范围

最新AGENTS/POLICY哈希未变；status/capacity无告警，无重复W76 PR，CRM仅Issue109开放。复核R3 V15原BundleIdentity、ConfigurationView、configurationPersistence及原SQL复合父键；核对W69草稿/渲染、W73真实对象、W74精确Form版本和W75实际Intake/Calendar版本父图均已远端采用。ContractVersion=1技术映射、别名和当前授权不互相推定。

本窗新增显式PostgreSQL namespace crm_inquiry_draft_bundles_v1，保存不可变Draft Bundle快照。可信调用方明确提供Bundle UUID/正整数版本、三个实际RevisionId、CreatedBy、精确CreatedAtUtc和ETag；组件从实际同租户父图派生完整BundleIdentity、Draft ConfigurationView与配置内容，不接受独立伪造的父身份、摘要或业务归属。调用方仍负责真实Owner来源、最终权限与时钟/ETag产生；组件不自动分配版本、选择最新、commit、安装宿主或启动业务入口。

父关系采用真实tenant-qualified FK，并由原生插入guard和应用重读核对Form所绑定的实际PreparedIntake及其Calendar、来源Kind与对应真实UUID。保存原Bundle版本token/完整规范键，每Form版本唯一用实际行Gate，RC/Serializable混合竞争沿已验证模式；摘要仅加速，不把SHA或有界numeric当身份。历史不覆盖，等值版本同ID同父关系/创建事实幂等返回原存储。

持久化完整BundleIdentity/Draft ConfigurationView以及对应CanonicalDraftJson、PolicyJson、RenderedFormJson、PrivacyNotice/Hash、ContentHash、RenderedHash、Department/分钟/Timezone/Periods/Holidays等规范要求的内容事实；保持原操作草稿与NFC摘要视图的区别，不用jsonb规范化替代原数值或时间文本，不把原告知改成NFC后重算。原始ETag字符串按合同验证并保持，不额外要求base64规范拼写；ExactJsonTimestamp保留年0000、合法闰秒及任意小数精度，不转有界主机时间。Bundle原合同要求的UUIDv4通过现有严格编码校验，旧准备对象允许范围不变，不能把不符合原Bundle合同的准备ID伪装为合法输出。

ConfigurationView只允许Draft、previewProof=null，effectiveInterval.from来自实际配置effectiveAtUtc/to=null，不宣称当前生效。返回不可变容器及解码副本，Read重核实际父图、保存的输出/内容事实和摘要，CanAuthorize/CanActivate/CanIssueAttempt仍false。W68稳定请求槽保持独立但验证真实同事务组合：槽取得后回调中分配BundleId/创建事实并保存，回滚/后续SQL错误撤销新写集，重放返回原结果且不再分配；仍未代替真实Owner原子权限/audit/outbox。

新schema显式安装、既有结构漂移拒绝不修复；runtime最小SELECT/INSERT/Gate列UPDATE，历史ALWAYS不可变。只用新自有PG55444，结束正常停止保留记录。验证覆盖合法完整DTO恢复、无界版本/精确时间/ETag边界、同号与幂等、真实父图/FK/错类型/跨tenant、原生多行/混合隔离/取消、W68原结果共同事务与重放、内容损坏及执行结构漂移；一次作者完整SPEC/QUALITY，精简相关构建，不全仓/UI。

真实Owner身份/对象/字段最终共同门、真实版本来源批准、配置修改/Preview/Activate状态机与审批事实、audit/outbox、公开Attempt/受理、隐私保留及Program/HTTP仍待对应窗口。本窗仅不可变Draft Bundle基础，不提升WP03-01整卡或生产验收。Issue109仍OPEN，冻结错误fixture和历史负例不改，使用已有有效样例。

## 耗时、席位与出口

预计实际40–65min：实现15–25、相关PG验证12–20、审查/三仓收尾13–20；独立前置约2min，W75收尾内只读不重复计时。Main登记start `2026-10-23T19:11:51.408805+00:00`。沿既有七天持续目标ACTIVE，不替换或结束目标。

DEV-1 Main协调；DEV-2 Handoff START保存/finish deferred后bind CRM，CRM采用/finish后bind回Handoff交付。固定目录复用、正常PR/远端采用核对/准确分支删除/finish释放，不新增工作树/代理，不运行Actions、既有环境迁移或部署。24/375整卡、125目标0交付、247运行NOT_RUN保持。W51指定停止data删除被自动审批以blocked by policy拒绝，保留不重试。

## W76实际交付

CRM [PR116](https://github.com/GTX537/CP6.CRM/pull/116)已采用，source `631734a0ac4c620226c989b71a4ce1b710db55f3`，远端main `5deac62fc4d00a731b5d2dea79877572dc39a49b`，11条任务路径exact blob核对通过。[单份产品报告](https://github.com/GTX537/CP6.CRM/blob/5deac62fc4d00a731b5d2dea79877572dc39a49b/docs/development/evidence/W76-01/DELIVERY.md)。25项独立本轮PG用例通过，无跳过，相关API/必要依赖构建通过；真实Owner业务验收NOT_RUN，无Actions。

新增实际三版本父图派生的不可变Draft Bundle、完整BundleIdentity/ConfigurationView及规范内容持久化，五个tenant-qualified FK与原生父图guard共同核查。版本保原token/完整规范键，每Form真实行Gate唯一；保年0000/闰秒/任意小数精度、合法ETag原文及原告知文本/UTF8摘要，NFC视图不替代操作原文。

读取重建核对27列、实际父关系与所有内容摘要；DTO返回副本。新内容用继任Bundle保旧历史；混合隔离、同ID并发/取消、原生多行重复原子拒绝通过。W68稳定槽先取得后回调分配UUID/保存，回滚及后续SQL错误共同撤销，重放原视图/ETag且不再次分配。合成原结果字节测试不宣称生产加密采用。

最终24+1=25独立PG通过，初始三项未实现红灯保留，产品代码未变时复用先前24项结果；一次作者SPEC/QUALITY无剩余阻断。自有PG55444正常停止，数据/日志/TRX保留。CRM准确任务分支删除、固定树干净detached、finish释放。

这仅是Draft Bundle持久化；真实configuration.save/read Owner的身份/对象/字段最终共同门、创建事实来源、audit/outbox、Preview/Activate状态机和批准、公开Attempt/受理、隐私保留及Program/HTTP仍待接。CanAuthorize/CanActivate/CanIssueAttempt仍false，Issue109保持OPEN。无新包、既有环境迁移、Actions或部署；24/375整卡、125目标0交付、247运行NOT_RUN保持，七天目标ACTIVE。W51指定停止data删除被自动审批以blocked by policy拒绝，保留不重试。

Handoff [PR52](https://github.com/GTX537/CP6.AI-Handoff/pull/52)已采用 `77878b85bdc71d93d93ff8975951879668b4e8ee`，准确分支删除、固定树干净detached、finish释放。Main仅剩协调PR/finish，不提前声明释放。登记开工至此约13.32min，原预算40–65min，最终以finish为准、子任务不重复累计；前置只读核对约2min。
