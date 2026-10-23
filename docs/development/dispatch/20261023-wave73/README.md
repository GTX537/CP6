# W73 / 七天一期D6：站点、表单与准备配置的原生对象绑定

COORD本聊天单人，2026-10-23T18:13:54.629124+00:00。Main `fd54b9ef9c8cc8fe01cef3a87c9572405d8eef9d`，CRM `6c049934bad09ba230cb47cf8e6606db19331f18`，Handoff `f833756a86252cd8fefa9d8f9af90c906904ab6e`。W72 Main452/453、Handoff48全部采用，Issue451关闭，73项独立本地用例和最终类型检查通过，实际28.17min＋前置3min；两准确分支删除、固定树干净detached、全部席位释放，Actions0，三个自有PG停止保留。W37–W72不重复。

## 前置与范围

最新AGENTS/POLICY哈希未变，status/capacity无告警；CRM main已fetch核对，无重复W73任务/PR，CRM Issue109仍开放。复读CRM-INQ-01当前入口、V15正文§8/9和权威增补、原SQL31–78与canonical configurationPersistence/BundleIdentity。已有Main角色字段运输不重复实现；当前CRM身份检查事实CanAuthorize=false，完整字段注册/映射/业务最终门仍缺。

现有PG配置草稿已精确绑定日历，但站点、表单仍仅在旧SQL Server运行图中；不能用请求siteKey/formKey猜UUID，也不能让草稿中字符串自动创建新站点。下一实际对象授权与Bundle保存先需要可持久解析的tenant-qualified身份关系。

本窗实现独立新schema crm_inquiry_bindings_v1：Sites保存可信tenant+SiteId+原SiteKey；Forms保存可信tenant+FormId+SiteId+原FormKey；PreparedIntakes保存可信tenant+IntakeConfigId+SiteId+FormId+PreparedDraftId。真实复合FK绑定本schema父对象及W69准备内容，保存时读取原草稿及父对象，逐字核site/form键和已有日历内容。按可信tenant和精确site/form键解析，缺父关系明确拒绝/无匹配；不自动分配UUID、不猜默认form，不从正文/当前调用人补归属。

记录不可变，同ID同完整关系幂等，异关系冲突不覆盖。tenant内站点键唯一、同站点表单键唯一，不把跨tenant同名误冲突。字符串沿原合法1–200 Unicode标量及严格UTF-8，原NFC差异、大小写、空白、NUL保持；PG bytea承载原键而非强套text禁NUL或SQL Server UTF-16长度。prepared绑定保原内容/时间/数量域。

本绑定版本明确为准备阶段身份图，不是完整FormBundleVersion或正式ConfigurationView，不分配或缩窄原ExactJsonNumber的form/intake/bundle版本。后继版本化存储必须保任意合法整数/指数表示；本窗不以旧int或rowversion代替。也不选择Active、签发授权/批准、接Program/HTTP、公开Attempt或投递成功。真实业务Owner、字段/对象当前权限与最终共同门、Bundle版本/状态、audit/outbox仍是后续窗口。

所有保存参加调用方ReadCommitted/Serializable显式事务，保存点恢复且不commit/隐藏重试。显式新namespace安装，已有结构漂移拒绝不修复；普通runtime仅USAGE/SELECT/INSERT，ALWAYS不可变guard和真实租户外键。相关本地PG验证包括精确解析、同ID冲突、跨租户/错父/草稿错键、原生FK、并发唯一键等待/取消、共同commit/rollback和W68原结果组合、漂移拒绝；一次完整作者SPEC/QUALITY。

## 耗时、席位与出口

预计实际80–120min：实现30–45，相关PG验证25–40，审查/三仓收尾25–35。独立前置约4min，W72收尾等待内只读不重复累计。Main登记start 2026-10-23T18:12:22Z；七天持续目标ACTIVE不重建。

DEV-1 Main协调；DEV-2 Handoff START可靠保存并finish deferred后bind到CRM，CRM完成后安全bind回Handoff收尾。PG55441已核无监听，新自有库仅测试，结束停止保留数据日志。正常三仓PR、准确远端采用、精确分支删除/finish。无代理、Actions、既有库迁移或部署。24/375整卡、125目标0交付、247运行NOT_RUN不提升。W51指定停止data删除自动审批曾以blocked by policy拒绝，保留不重试。

## W73实际交付

CRM [PR113](https://github.com/GTX537/CP6.CRM/pull/113)已采用，source `7da2543b996806c53f5fa68831b2b61923e2b965`，远端main `dc8784583860b78635a89924ad2576ba24a21e78`，10条任务路径exact blob核对通过。[单份产品报告](https://github.com/GTX537/CP6.CRM/blob/dc8784583860b78635a89924ad2576ba24a21e78/docs/development/evidence/W73-01/DELIVERY.md)。27项独立相关本地PG用例通过（24新增+3既有），无跳过，相关API及必要依赖构建成功；真实Owner业务验收NOT_RUN，无Actions。

新增PG不可变Site/Form/PreparedIntake身份图，实际复合FK绑定同租户父对象及W69准备内容。严格UTF-8原键1–200 Unicode标量，保留空白/NUL/NFC差异；精确解析对象，不猜UUID或默认表单。保存与读取重核真实父对象、草稿键、日历及摘要。同ID同关系幂等、异关系冲突。原生FK保障结构与租户关系，错误同租户内容键仍由组件语义验证拒绝；没有把准备关系宣称为正式版本或权限。

Save参与调用方RC/Serializable事务和保存点，不commit/隐藏重试。实际组合W68原结果、W69内容共同commit/rollback；后续原生失败撤销整组新图并保留此前工作，原生40001要求Owner重建事务。相关PG最终并集19+3+5=27独立通过；中途3项失败来自fixture重复建立日历，修正复用后定向通过，原失败保留。一次完整作者SPEC/QUALITY无剩余阻断；自有PG停止且数据/TRX保留。CRM准确任务分支删除、固定树干净detached、finish释放。

本窗仅新namespace显式安装；正式Bundle版本/ConfigurationView、真实Owner最终身份/对象/字段共同门、audit/outbox、Preview/Activate/publicAttempt、隐私保留和Program/HTTP仍待接。CanAuthorize/CanActivate仍false，任意合法ExactJsonNumber未降为有界整数；Issue109保持OPEN。无包变更、既有环境迁移、Actions或部署。24/375整卡、125目标0交付、247运行NOT_RUN保持，七天目标ACTIVE。W51指定停止data删除被自动审批拒绝原因为blocked by policy，保留不重试。

Handoff [PR49](https://github.com/GTX537/CP6.AI-Handoff/pull/49)已采用 `88566612aac8f2493a1456e8bfcd351b5c6ab257`，准确分支删除、固定树干净detached、finish释放。Main仅剩协调PR/finish，不提前声明释放。登记开工至此约17.03min，原预算80–120min，最终以finish为准、子任务不重复累计；前置只读核对约4min。
