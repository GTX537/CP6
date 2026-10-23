# W69 / 七天一期 D6：不可变配置草稿持久化与原结果共同事务

COORD本聊天单人，2026-10-23T15:14:06.848863+00:00。Main `a6f9853be72547199ab8a38463d31d3c2adf7fb3`，CRM `10985b5ca7f1f127f32768dec3983ee2e2d29571`，Handoff `e25fb253fa991417d55ff2b404dd9c9fa3a73683`。W68 Main443/CRM111/Handoff44全部采用，22项有效实际PG用例通过；登记实际47.59min、前置约8min，三条任务分支删除，固定树干净detached，全部席位释放，Actions0；自有PG停止、数据保留。W37–W68不重复，CRM Issue109保持OPEN，七天目标ACTIVE。

## 前置、任务与准确边界

AGENTS/POLICY哈希未变，status/capacity无告警。Main身份快照已有角色字段行，不另造重复运输接口；完整CRM字段注册域、Owner签认映射、最终共同提交门仍缺。原Site/Form/Intake绑定与完整Bundle版本图也未采用，不能猜测授权或照搬旧SQL Server int版本。保留这些未完成项，本窗不接Program/HTTP、不分配Bundle版本、不声明正式configuration.save成功。

本窗仅实现PostgreSQL不可变prepared configuration内容存储，作为后续Bundle内容部分的独立基础。复用W61严格原草稿及同事务日历匹配、W63规范化内容与W67完整表单内容；显式新库安装crm_inquiry_drafts_v1，不改旧crm_v1权威表、不迁移既有环境。记录trusted tenant/id/calendarId、原UTF-8输入及原hash、规范化内容hash、表单hash；每次读取重新严格解码并校对派生摘要，不把NFC摘要视图写回原业务值。

准备内容ID由调用方Owner提供，按tenant/id唯一。同ID精确原文与日历相同才幂等；不同原文（即便NFC后hash相同）、日历或内容拒绝，不覆盖历史。不同ID可保存语义等价但原文不同的合法输入，不把内容摘要当全局唯一身份。通过真实tenant-qualified FK绑定W60已发布日历快照；业务别名/时区/完整period与holiday匹配复用W61。配置effectivity保留原ExactJsonTimestamp token，不转换CLR DateTimeOffset，不收窄year0000、闰秒或超100ns小数；calendar自己的物理时间保持原边界。

组件参与调用方ReadCommitted/Serializable事务且不commit；保存点恢复失败前的调用方工作。表/唯一性/FK/不可变guard定义漂移拒绝，不修补已有schema。runtime最小USAGE/SELECT/INSERT，原结果、准备内容及日历实例仅在新自有隔离PG测试。组合W68真实产品执行器回调写本内容存储，证明外层commit/rollback、同键重复与失败恢复覆盖两类产品表；仍不把基础组件组合当作真实Owner授权、正式ConfigurationView、Bundle/audit/outbox写集或生产验收。

## 实际耗时、验证、席位

本窗前置只读核对约17min。预计实际75–105min：存储与精确读取实现25–35；相关真实PG行为和与W68组合25–35；一次作者SPEC/QUALITY、三仓合并和分支/席位收尾25–35。只跑相关项目；失败留存，成功同输入证据复用，不跑全仓或Actions。

DEV-1总控；DEV-2按Handoff START可靠保存/finish deferred→CRM→Handoff复用。预计PG55437，先核空闲与ownership，测试后正常停止保留数据。各仓正常PR合并、远端采用核对、准确任务分支清理、finish释放，无重复ACK等待。完整24/375整卡、125目标0交付、247 NOT_RUN保持；七天目标ACTIVE。

真实Owner最终身份/对象/字段门、Site/Form/Intake完整父图、Bundle版本与状态、生效选择、审计/outbox、PreviewProof、公开Attempt/提交、隐私生命周期仍待各对应窗口。W51指定停止data删除曾被自动审批拒绝（blocked by policy），保留不重试。

## W69实际交付

CRM [PR112](https://github.com/GTX537/CP6.CRM/pull/112)已采用，source `4477a42e3bc271461570cad4733f4c368be1a95c`，远端main `6c049934bad09ba230cb47cf8e6606db19331f18`，11条任务路径exact blob核对通过。[单份产品报告](https://github.com/GTX537/CP6.CRM/blob/6c049934bad09ba230cb47cf8e6606db19331f18/docs/development/evidence/W69-01/DELIVERY.md)。最终21项相关本地PG用例重新执行通过、无跳过，相关API及必要依赖构建成功；真实Owner业务验收NOT_RUN，无Actions。

新增PostgreSQL不可变prepared配置内容，以租户/ID为主键、租户/CalendarId真实外键绑定原生日历。逐字保留原UTF-8与精确生效时间；读取重新严格验证原文、实际日历绑定和Content/Rendered摘要。同ID原文冲突拒绝，NFC等价不同原文可分ID保存；没有Bundle版本收窄。Save参与调用方显式事务及保存点，不commit；实际组合W68原结果共同commit/rollback，后续原生SQL失败同时撤销本窗内容和结果并保留调用方此前工作。

本次只在自有新库安装独立schema，普通runtime角色与实际PG唯一键等待、取消、跨租户FK拒绝、损坏摘要和不可变guard均已验证。一次作者集中审查发现FK定义存在不等于实际执行，两个红灯复现后修正四个内部FK触发器状态和replica会话拒绝；最终21项全部重跑通过，不累加旧19项。自有PG已停止，数据/红灯日志保留。CRM准确任务分支已删除、固定树干净detached、finish释放。

未接完整Site/Form/Intake关系、正式Bundle/ConfigurationView、真实Owner最终身份/对象/字段授权、audit/outbox、Preview/Activate/publicAttempt、隐私保留或Program/HTTP。CanAuthorize/CanActivate仍false；Issue109冻结fixture矛盾保持OPEN。无新包、既有环境迁移、Actions或部署；完整24/375整卡、125目标0交付、247运行NOT_RUN保持，七天目标ACTIVE。W51指定停止data删除被自动审批拒绝原因为blocked by policy，保留不重试。

Handoff [PR45](https://github.com/GTX537/CP6.AI-Handoff/pull/45)已采用 `8e9652dcaa45e0cb0ea6326f37f645d31c502ee8`，准确分支删除、固定树干净detached、finish释放。Main仅剩协调PR/finish，不提前声明释放。登记开工至此约43.04min，原预算75–105min，最终以finish为准、子任务不重复累计；前置只读核对约17min。
