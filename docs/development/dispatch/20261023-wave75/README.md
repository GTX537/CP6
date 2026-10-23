# W75 / 七天一期D6：Intake、Calendar精确版本与表单父图联结

COORD本聊天单人，2026-10-23T18:55:11.444874+00:00。Main `7d5a9b9501a98b7613775812921cffa929cbc496`，CRM `67f8b28fd9b08b0be67060716e4729e0ba547c93`，Handoff `c101b055844f9ef65632eb8726d0634b9db7cc66`。W74 CRM114/Main455/Handoff50已采用，29独立PG通过，三准确分支删除、固定树干净detached、所有席位释放、Actions0、自有PG停止保留；实际17.22min＋前置2min。W37–W74不重复。

## 前置与限定范围

最新AGENTS/POLICY哈希未变，status/capacity无告警，无重复W75 PR，CRM仅Issue109开放。W73有真实Site/Form/PreparedIntake关系，W74有无界精确Form版本与同事务原结果。复核R3 V15 BundleIdentity及canonical configurationPersistence、原SQL复合父键；又核对CrmCalendarStorageCodec中ContractVersion=1明确是技术映射版本，SourceVersionAlias也不是可猜测的整数业务版本。

本窗新增显式namespace crm_inquiry_source_revisions_v1，为实际W73 PreparedIntake及W60已发布CalendarSnapshot登记调用方明确给出的精确正整数版本，保留实际父UUID，不解析别名、不复制ContractVersion、不自动补1或发明发布次数。版本声明仅是准备事实；可信Owner仍负责真实来源、当前权限、审计及采用，不把本地声明当作企业真实批准。

SourceRevisions使用类型化Intake/Calendar父引用与实际tenant-qualified FK；类型/SourceId/可空父列必须严格一致，不允许同UUID跨类型串绑。SourceGates按tenant+kind+实际SourceId保护新插入的版本唯一性。任意合法正整数token逐字保存，规范系数/指数做完整判等，摘要仅非唯一加速；沿W74原生行更新、保存点、混合隔离与40001无隐藏重试。抽出内部共用数值编码并保留W74错误合同，避免两套数值身份定义。

新增同事务父图读取：从数据库实际读FormRevision、IntakeRevision、CalendarRevision，核同租户、准确类型、Form所绑定PreparedIntake以及其实际CalendarId均一致。缺失、错类型、错内容或跨租户拒绝，不从请求推定归属；返回不可变准备版本事实，Calendar可变DTO只返回副本。CanAuthorize/CanActivate仍false，不生成完整BundleVersion、Active选择或公开响应。

写入只参加调用方RC/Serializable同连接事务，保存点包住验证/写入/冲突恢复；不commit或隐式重试，失败撤销本次Gate/Revision并保留调用方此前工作。新schema显式安装，既有结构漂移拒绝不修复；runtime最小SELECT/INSERT/Stamp列UPDATE，无DDL/历史删除，ALWAYS不可变及真实FK。管理员可信，schema不等于租户授权。

相关验证覆盖两个来源的巨大/等值数字、同ID幂等与异版本冲突、跨类型/tenant真实FK与错引用、原生日历发布/内容重核、三版本父图联结、实际混合隔离竞争与取消、W68原结果共同事务/后续SQL失败、可变DTO隔离及执行结构漂移。定向复查W74共用数值行为；一次作者完整SPEC/QUALITY，不执行UI或全仓测试。

完整BundleIdentity/ConfigurationView持久化、真实Owner最终身份/字段/对象共同门、audit/outbox、Preview/Activate/publicAttempt、隐私保留与Program/HTTP仍是后继；本窗不以父图基础完成WP03-01整卡。Issue109错误告知摘要仍OPEN，冻结原件与历史负例不改，已有效UI样例仅作本地测试来源。

## 耗时、席位与出口

预计实际35–55min：实现12–20，相关PG验证10–15，审查/三仓收尾13–20；独立前置约2min，W74收尾内只读不重复计时。Main登记start `2026-10-23T18:52:25.215254+00:00`。七天持续目标ACTIVE，保持原目标。

DEV-1 Main协调，DEV-2 Handoff START保存/finish deferred后bind CRM，CRM采用/finish后bind回Handoff交付。PG55443无监听，仅新自有测试库，结束正常停止保留证据；固定目录复用、正常PR/远端准确采用/任务分支删除/finish。不新增工作树或代理，不运行Actions、既有环境迁移或部署。24/375整卡、125目标0交付、247运行NOT_RUN保持。W51指定停止data删除自动审批以blocked by policy拒绝，保留不重试。

## W75实际交付

CRM [PR115](https://github.com/GTX537/CP6.CRM/pull/115)已采用，source `01c40f6d2e0d47ff80fa88af2ac39c99e1e3137f`，远端main `318661183a8c9e72c4a5b1f2bfa1e5512d4495cf`，12条任务路径exact blob核对通过。[单份产品报告](https://github.com/GTX537/CP6.CRM/blob/318661183a8c9e72c4a5b1f2bfa1e5512d4495cf/docs/development/evidence/W75-01/DELIVERY.md)。33项独立本地PG用例通过（29本轮+4既有W74数值回归），无跳过，相关API/必要依赖构建通过；真实Owner业务验收NOT_RUN，无Actions。

新增实际PreparedIntake/已发布Calendar的显式精确版本声明，保真实来源UUID、原数值token和完整规范键，类型化tenant FK与来源形状guard禁止串绑。同UUID在不同来源类型可存在；不解析别名、不复制技术ContractVersion=1，不冒充真实Owner批准。复用内部数值编码，W74错误合同与数值行为不变。

同事务重读并联结Form/Intake/Calendar三版本与实际不可变内容，拒绝跨tenant、错类型和同tenant错Calendar；Calendar DTO返回副本。原生行Gate应对四种混合隔离竞争，保存点恢复取消/冲突，40001无隐藏重试。三版本及W68原结果共同commit/rollback、后续SQL故障和原结果重放通过，直接多行重复全撤销。

有效并集33独立PG通过；初始两项未实现红灯及就绪函数错误自调用导致运行中止均保留，修复后成功不覆盖失败记录。一次作者完整SPEC/QUALITY无剩余阻断，自有PG55443正常停止保留数据/日志/TRX。CRM准确任务分支删除、固定树干净detached、finish释放。

此为准备版本父图；完整BundleIdentity/ConfigurationView、真实Owner当前身份/字段/对象最终共同门、audit/outbox、Preview/Activate/publicAttempt、隐私保留及Program/HTTP仍待接，CanAuthorize/CanActivate仍false。Issue109保持OPEN。无新包、既有环境迁移、Actions或部署；24/375整卡、125目标0交付、247运行NOT_RUN保持，七天目标ACTIVE。W51指定停止data删除被自动审批拒绝原因为blocked by policy，保留不重试。

Handoff [PR51](https://github.com/GTX537/CP6.AI-Handoff/pull/51)已采用 `752b6a1365dc1dc93917ce16737941b3b708e4e4`，准确分支删除、固定树干净detached、finish释放。Main仅剩协调PR/finish，不提前声明释放。登记开工至此约16.44min，原预算35–55min，最终以finish为准、子任务不重复累计；前置只读核对约2min。
