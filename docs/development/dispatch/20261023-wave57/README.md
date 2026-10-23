# W57 / 七天一期 D5：本次完整采集凭据与可失效发布读模型

COORD本聊天单人，2026-10-23T05:57:29.114085+00:00。Main `10361a554dce0230fae7860b6cc08e66082adab3`，CRM `6bd4dc30c962c86fe6ba3661fc52e8bbbfc4b8b0`，Handoff `87399631c8f9b25a8779047f90e729f0b5440454`。W56 CRM101/Handoff32/Main425全部采用，64项相关本地验证及API/Worker编译通过，实际75.33min；准确分支删除、固定树干净detached、全部席位finish，Actions均0。七天目标ACTIVE，W37–W56不重复。

## 前置与限定范围

最新AGENTS/POLICY哈希未变；已核W56最终证明、当前源码、S6 §3.5/§4.5和最新台账。WP02-07正式Inbox已DONE，完整WP02-06/09/29仍PLANNED；WP03-01/02依赖真实Owner共同门，不能提前宣布可用。旧CRM业务工厂仍使用SQL Server，当前PG身份基础不能冒充已经共事务；本窗不切换旧业务工厂或迁移既有数据。无W57重复PR，固定席位/容量无告警，用户主目录内容保留。

W56 VerifiedIdentityBaseline证明内容完整一致，不证明它是本次实际采集；现有Collector默认十分钟也不是本次对账九十秒deadline。因此本窗增加只由具体原来源Collector实际执行完整首尾HTTP采集才能产生的不可外部构造凭据，绑定原registration/source和本次已提交Attempt/Revision/lease。外层严格九十秒取消与单调耗时检查，原两分钟数据库lease及CAS仍最终生效；不允许传旧baseline/已验证布尔充当新采集。HTTP原source、no-store、原字节和C02完整合法范围沿用W52。失败/超时/换源/新事件/过期凭据均不能发布，rollback后重试也须原ticket仍有效。

新增版本化PostgreSQL发布附加层，保存本次完成的AuthoritySelection、精确Current源引用集合、State revision及保守采集时间上限，发布在同一Recover调用方Serializable事务完成。依赖图不完整则不产生可用发布；原事件/快照/冲突/原journal不改写。持久发布仍是可重建派生读模型，不成为身份权威。当前指针只指向完整、同实际事务完成且源集合/CAS一致的发布；任一State变化、Current变化关联revision、冲突或新调和令指针失效。历史发布保留，读取重核当前状态、源集合/依赖及期限，不能用缓存旧图继续。

freshness最多沿原二十分钟基线年龄，从本次原生lease起点保守计算；期限只表示本次CRM完整采集观察的年龄，不是Main全局时钟或新的跨库权限保证，opaque boundary不比较大小。可配置更短期限仅收紧运行窗口，不改变C02合法输入或精度。所有返回继续CanAuthorize=false，不创建新业务Allow；实际每请求Main在线校验、字段授权、真实Owner共同最终门和sidecar/OIDC/broker采用留对应后继。本窗Program/旧consumer不切换。

原W51–56七份SQL冻结保留，新附加层显式安装并拒绝已有V2状态隐式升级；只在全新owned隔离PG验证。扩展存在但版本/守卫缺失、错误函数/时机或部分安装必须使接收和会话入口失败关闭。原七锁顺序不变，writer能力分开且runtime仅最小schema版本读取；没有直接table override或生产DB权限扩张。

## 时间、验证和收尾

预计实际110–170min：45–70min本次采集凭据/deadline/来源及ticket绑定；35–60min持久发布、原生完整提交/CAS和自动失效；30–40min相关故障/边界验证、一次作者SPEC/QUALITY和三仓收尾。沿用既有七天目标，不另建目标或按日历等待，实际以Main登记start到最后finish计，不重复累计子任务。

定向覆盖真实HTTP客户端夹具的完整采集/换源/取消/目录变化、凭据过期与换ticket、新事件竞争40001/CAS、缺依赖、发布部分提交拒绝、自动失效与短期限超龄、故障后caller commit无残留，以及未装附加层旧路径兼容。夹具不冒充实际服务注册或生产Main新鲜度；复用不变证据，不全仓/双库/Actions。单份产品报告记录真实失败及范围。

DEV-1 Main `codex/w57-coord`维护计划/唯一台账/四状态；DEV-2 Handoff `codex/w57-handoff` START可靠保存后finish deferred，再bindCRM `codex/w57-01`。CRM正常PR/远端采用/准确分支清理/finish后复用Handoff交付，最后Main收尾。固定路径复用、不新增代理或工作树；PG55437。允许CRM身份PG发布/采集/必要readiness与相关测试/报告，Main/Handoff仅协调文档。无既有库迁移、环境切换、Actions或部署；24/375整卡、125目标0交付、247 NOT_RUN保持真实。W51被自动审批拒绝的准确data删除停止保留，不重试绕过。

## W57实际交付

CRM [PR102](https://github.com/GTX537/CP6.CRM/pull/102)已采用，source `b7e55240f583d9db287f7371a387485ed81b8aee`，远端main `6ff205740ac5290154588acd7840ad9f70cb7c96`，18任务路径exact blob核对通过。[单份产品报告](https://github.com/GTX537/CP6.CRM/blob/6ff205740ac5290154588acd7840ad9f70cb7c96/docs/development/evidence/W57-01/DELIVERY.md)。77项相关本地用例通过（含实际九十秒超时与原生发布边界、64项既有身份回归）；API/Worker编译；真实Owner/宿主验收NOT_RUN，无Actions。

原具体HTTP Collector实际完整采集产生不可外部构造凭据，绑定本次registration/source/Attempt/Revision/lease，九十秒外层取消及单调耗时阻止旧结果冒充新采集。可信恢复、精确来源发布及当前指针在同一个调用方Serializable事务内完成；原七锁、原生CAS/deferred完整提交及savepoint保护继续生效。

任一State变化、事件、冲突或新调和同事务撤下当前指针；原历史发布、源和冲突不覆盖。读取重核revision、原来源集合、依赖及期限，最多二十分钟的保守CRM采集年龄不冒充Main时钟或业务权限。依赖不完整、换源/注册、过期凭据、旧事务selection、跨租户指针和部分提交均关闭；返回始终CanAuthorize=false。

仅全新空状态PG显式安装，拒绝已有状态隐式升级；七份旧SQL未改，十二项守卫检查包含函数/时机/deferred属性。一次作者SPEC/QUALITY通过，实际九十秒HTTP阻塞已被期限终止；先前凭据在原生lease仍有效时也因超龄被拒绝。两个owned PG停止留证，CRM准确本地/远端分支已删除、固定工作区干净detached且finish释放。

后继继续当前身份/字段授权、真实业务Owner共同最终门和实际宿主采用；旧业务工厂仍走历史SQL Server路径，Program/consumer未切换，PG读模型不冒充共同事务。无Actions、既有库迁移或部署；24/375整卡、125目标0交付、247 NOT_RUN不提升，七天目标ACTIVE。W51准确data删除曾被自动审批拒绝，原因仅“blocked by policy”；停止保留，无重试绕过。

Handoff [PR33](https://github.com/GTX537/CP6.AI-Handoff/pull/33)已采用 `caccd556bcc3f7346990156f803355476f3319af`，准确本地/远端分支删除、工作区干净detached、finish释放。Main仅剩当前协调分支正常PR及finish，不提前声明释放。登记开工至此约75.07min，原预算110–170min，实际最终时长以finish为准，不重复累计子任务；此前只读核对约8min。
