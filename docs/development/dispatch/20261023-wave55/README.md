# W55 / 七天一期 D5：CRM原始身份来源与正式PreCheckpoint内容门

COORD本聊天单人，2026-10-23T03:41:58.519943+00:00。Main `aaddacdf44b9107a9915fa7465edec0b25996da0`，CRM `46dfd16e0052eab44c1020970767f4f47fc5cf0d`，Handoff `51eb830369d0580d16feab2c6999a21187e5e569`。W54 CRM99/Handoff30/Main423已采用；101项相关验证通过，实际32.09min、只读前置约3min，准确分支删除、固定树干净detached、全部席位finish。七天目标ACTIVE，W37–W54不重复。

## 核对与限定准入

已核最新AGENTS/POLICY哈希未变、台账WP02-06/07/09、S6 v0.2 §3.5/3.6与INT-ID-01正文、W51–54实际PG实现、正式0.10.3 caller-transaction Inbox及可信Context接口。主线已远端核对，无重复W55 PR，容量无告警；主目录用户内容保留。

W53当前引用只有完整基线Items外键，不能把收到的原事件冒充SOURCE_SNAPSHOT。新增显式版本化PG身份接收存储，把完整快照原始引用和实际原消息分别保存，当前引用能够准确选择两类来源；data原UTF8片段与raw envelope原字节分别取摘要，来源、tenant、type/version、schema/bundle/region、transport和注册向量保真，不重新序列化当作原报文。现有冻结SQL不改写。新层仅在新隔离库验证；已有V1当前投影需独立迁移准入，安装须拒绝悄悄清空或切换有现存V1权威状态的库。启用后旧V1当前写路径显式关闭，不能产生两份并行权威当前记录。

用正式0.10.3 ICp6InboxPreCheckpointV1接入实际caller Serializable事务，继承W54相同七锁键和顺序，明确新writer清单。注册与来源由可信host配置和当前TrustedContext绑定，普通请求不能提交“已验证”布尔。所有Fresh/Duplicate/Older/Equal/Conflict经完整校验和历史比较，观察不代替当前可信anchor；缺应有anchor/损坏来源DependencyUnavailable，不写成功checkpoint。首个合法事实可PendingInspection，但不形成Allow。

新增不可变原始来源、观察/冲突、身份接收原操作journal，技术Inbox结果只是原journal副本。较低版不回退，同版异内容不覆盖；冲突、Drift、修复调度与Inbox分类同事务，现有会话/精确token撤权副作用按当前已支持原语与来源共同提交。相同原操作复用原结果，未知提交须查实际持久原记录；提交前无SUCCESS。正式实例覆盖合法不同transport同data、历史同版冲突先于重复/旧版、原transport冲突只作用可信绑定、跨tenant隔离、故障/回滚和原生竞争。真实sidecar/OIDC/broker上线接入仍由后续host窗口验收，测试认证明确fixture。

本窗只接收/核查身份事实并关闭冲突，不开放Published allow或真实业务新效果。AuthoritySelection完整基线恢复、持久Published/最终Owner共同门继续后继；已存在Drift不能被Duplicate/Equal或新消息清除。Main–CRM分库不冒充共同最终门，Program/旧消费者不自动切换。相关新业务仍关闭。

## 实际预算和交付

预计实际90–150min：原始来源/新版当前引用45–70；正式内容门及事务行为30–50；定向验证、一次作者SPEC/QUALITY和三仓收尾15–30。完整目标/真实运行验收不因局部基础交付提升。

DEV-1 Main `codex/w55-coord`统一计划/台账/四状态；DEV-2 Handoff `codex/w55-handoff` START可靠保存后finish deferred，bindCRM `codex/w55-01`实现/正常PR/远端核对/精确分支删除/finish，再bindHandoff resume原分支交付并finish，Main最后合并收尾。固定目录复用，不开新树或代理；PG独立55437，编译缓存复用。

允许CRM Identity/PostgreSql显式新版来源/当前模型、正式Inbox adapter及当前PG入口的必要版本守卫、Npgsql EF provider/资源嵌入等实际依赖配置、相关PG和契约测试、单份报告及四状态；Main仅docs/台账/生成页/状态，Handoff消息。无既有库迁移、Actions或部署；不删除历史SQL/证据，不重试W51被自动审批拒绝的准确data删除。125目标/247运行验收保持真实状态。

## W55实际交付

CRM [PR100](https://github.com/GTX537/CP6.CRM/pull/100)已采用，source `6c8759caf2102849e0fc575c2f1b7ad41a5bd3f9`，远端main `8d8dd082232c6f74204c3e30be71c9922b8725ee`，21任务路径exact blob核对通过。[单份产品报告](https://github.com/GTX537/CP6.CRM/blob/8d8dd082232c6f74204c3e30be71c9922b8725ee/docs/development/evidence/W55-01/DELIVERY.md)。102个不同相关本地用例通过：原始内容/安装、正式Inbox与原生事务竞争、既有PG身份回归及Platform消费；API/Worker编译，真实C01/Owner验收NOT_RUN，无Actions。

显式PG v2统一实际EVENT原envelope/data和完整SOURCE_SNAPSHOT原引用，保留独立摘要域与来源/注册向量；Current只引用同tenant/aggregate精确原来源。正式0.10.3 PreCheckpoint所有成功/冲突路径共同校验历史和注册，原操作journal及Inbox字节副本、Current、冲突/Drift/调度、精确token撤权和tenant/user/epoch会话限制同调用方Serializable事务。原transport决定冲突目标，外tenant碰撞无Owner写入；同内容不同transport复用原结果，旧版本不回退，观察不代替当前anchor。

新版完整基线导入保留租约/Revision CAS、共同七锁和40001整事务边界；新来源使旧ticket失效，基线/消息不能清除Drift。丢失receipt可查原Owner key/digest，历史结果不ACK当前投递；最终Inbox故障后即使caller捕获并commit也无部分Owner/会话残留。真实runtime backend已证实等待identity原生锁，停用提交后不允许旧会话恢复，回滚则可继续。认证和读取策略仍是明确fixture，实际宿主接入未验收。

仅新隔离库显式安装；有旧V1当前状态则拒绝切换，成功后旧V1当前入口原生与应用层关闭。四份冻结SQL不改写。一次作者SPEC/QUALITY通过；SQL修正后全新owned库重装验证，真实失败日志保留。两个owned PG已核身份和零外部会话后停止，数据/证据保留。CRM准确本地/远端分支已删除，固定工作区干净detached，manage finish释放后DEV-2复用Handoff。

AuthoritySelection恢复、完整图/Published/当前授权、真实业务Owner共同最终门与sidecar/OIDC/broker宿主采用继续D5。Program/旧消费者未切换，CanAuthorize=false；Main–CRM分库不冒充共同门。无Actions、既有库迁移或部署，24/375整卡、125目标0交付和247运行NOT_RUN不提升。七天目标ACTIVE。W51准确data删除曾被自动审批拒绝，原因仅“blocked by policy”；继续停止保留，无重试绕过。

Handoff [PR31](https://github.com/GTX537/CP6.AI-Handoff/pull/31)已采用 `57b2d7c4288bcf1e36074679025b73ec0c206400`，准确本地/远端分支删除、工作区干净detached、finish释放。Main仅剩当前协调分支正常PR及finish，不提前声明释放。登记开工至此约45.27min，原预算90–150min，实际最终时长以finish为准，不重复累计子任务；此前只读核对约3min。
