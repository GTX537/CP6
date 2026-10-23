# W68 / 七天一期 D6：PostgreSQL配置保存稳定请求槽与原结果

COORD本聊天单人，2026-10-23T14:05:25.770144+00:00。Main `f524c924b8d977ae7d84d2e33137b025a929ff7e`，CRM `969bcc0660d96b41dcad102e1ef555b1c3bc6103`，Handoff `7675390afb5752e1b15942629aff628f11991afa`。W67 Main442/CRM110/Handoff43全部采用；20项相关本地用例通过，登记实际18.51min，前置约4min。三条任务分支已清理、固定树干净detached、全部席位释放、任务Actions0。CRM Issue109原fixture告知hash矛盾保持OPEN。W37–W67不重复，七天目标ACTIVE。

## 准入、前置与边界

最新AGENTS/POLICY哈希未变，status/capacity无告警。完整WP03-01仍受WP02-09真实Owner最终授权、完整Site/Form/Bundle图和政策采用前置约束；本窗只准入可独立交付的PostgreSQL命令持久化原语，不能把普通数据库事务当成身份/字段授权。既有旧CrmBusinessService仍使用SQL Server组织锁和旧CommandRecord，不作为本轮采用范围、不迁移既有库。

依据原V15正文§2、§14、最终增补521及SQL CommandReplay部分：configuration.save按tenant/actor/operation/requestKeyHash独占稳定槽，ResourceId在认领后由Owner分配，payloadHash不属于唯一键。保存成功原状态为201 ConfigurationView；同键同指纹重放原ResourceId/受保护结果/ETag，不重新执行保存体；同键异指纹拒绝且不写业务。请求键只接收已HMAC的32byte摘要，密钥版本和payload指纹由可信宿主提供，本窗不接收原请求键、不自行生成或批准密钥。

拟新增crm_inquiry_commands_v1原生schema和配置保存执行器。只在显式新隔离库安装，版本/表/唯一键/不可变guard不匹配拒绝，不能修补已存在环境。固定operation是configuration.save，主键tenant+actor+requestKeyHash；资源、payload与keyVersion为比较/结果事实，不进入稳定身份。原结果不可改写或因过期删除后重占；保留截止到达时拒绝披露/执行，完整隐私redaction/retention worker另行采用。

执行器只参与调用方既有ReadCommitted/Serializable事务，不commit。用包含tenant/actor/key的PG事务级锁先查原槽，再调用同连接/同事务的保存体并写原结果；回调只允许本Owner数据库效果，不放HTTP/文件/外部副作用。保存点包住本窗，失败恢复先前调用方工作；原生40001/死锁交给调用方重建整笔事务，不隐藏重试回调。Serializable旧快照竞争不得冒称立即重放；异常后新事务读取持久赢家。返回结果仍为提交前暂定事实，提交未知由外层按原键恢复，不称本组件已证明COMMITTED。

原值/受保护结果冻结并按准确字节保存；NUL和Unicode合法字符串不依赖PG text限制，使用适合的bytea表示，不能因PG选型收窄原合同。保留边界不能擅自填企业默认。已有原结果但对应Bundle缺失时，不能据重放补建该Bundle；重放的当前资源/权限/PII核验、真正Bundle+audit+outbox写集、Program/HTTP采用仍由后续Owner负责，不能把测试回调表当真实业务采用。

## 实际耗时、验证与收尾

本窗前置只读核对约8min，不计入下列估计。预计75–110min：存储/事务实现30–45；实际隔离PG并发、同键异指纹、重放原结果、调用方commit/rollback、保存体与原生失败、取消/隔离级别和schema边界25–35；一次作者SPEC/QUALITY与三仓收尾20–30。测试用自有新PG库和普通runtime角色，无跳过；保留失败与stopped数据。只跑相关项目，不跑全仓或Actions。

DEV-1负责总台账和四状态，DEV-2按Handoff START保存/finish deferred→CRM→Handoff顺序复用。预计PG端口55437，先核空闲与ownership；不复用W51被拒绝删除的数据，不再申请其删除。产品相关项目仅加必要SQL嵌入资源，无新增包。正常PR合并、远端核对、准确分支清理、finish释放；无需等待重复ACK。

完整24/375整卡、125目标0交付、247 NOT_RUN保持；不签发授权、不开放业务路由、不运行Actions、不迁移既有环境或部署。七天目标继续ACTIVE。W51指定停止data删除自动审批拒绝（blocked by policy）继续保留、不重试。

## W68实际交付

CRM [PR111](https://github.com/GTX537/CP6.CRM/pull/111)已采用，source `645da4a73a0cfde56f330d41291250c14b9f18da`，远端main `10985b5ca7f1f127f32768dec3983ee2e2d29571`，10条任务路径exact blob核对通过。[单份产品报告](https://github.com/GTX537/CP6.CRM/blob/10985b5ca7f1f127f32768dec3983ee2e2d29571/docs/development/evidence/W68-01/DELIVERY.md)。22项有效本地PG用例通过、无跳过（21项成功复用加1项定向补验），相关API与必要依赖构建成功；真实Owner业务验收NOT_RUN，无Actions。

新增原生PostgreSQL configuration.save稳定请求槽与不可变原结果。租户/操作者/HMAC请求键为唯一身份，ResourceId和payload不进入槽；同键同指纹/版本恢复原201、资源、保护体、ETag与保留截止，异内容拒绝。先取事务锁查槽，再执行同连接/事务保存体；组件不commit。业务异常、原生SQL错误、取消或过期结果撤销本窗写入并保留外层已有工作；Serializable旧快照竞争交由Owner重建事务，无隐藏重试。NUL/Unicode字节、100ns截止与Owner版本表示保持。

只在新自有库显式安装独立v1 schema，runtime最小USAGE/SELECT/INSERT，ALWAYS不可变guard与实际列/唯一槽/trigger/函数体漂移检查；已有环境不修补。22项实际PG行为通过，未实现红灯保留，一次作者集中SPEC/QUALITY通过。测试库正常停止、无postmaster.pid，数据与日志保留。CRM准确任务分支已删除，固定树干净detached，finish释放。无Actions、新增包、既有迁移或部署。

测试保存体仅fixture.save_effects，尚未接真实Owner当前身份/对象/字段授权、完整Bundle/audit/outbox写集、重放当前资源/PII核验、隐私保留worker、提交未知协议或Program/HTTP。到期拒绝读取不等于隐私删除完成，密文与可信密钥由Owner提供。CRM Issue109原fixture矛盾保持OPEN。完整24/375整卡、125目标0交付、247运行NOT_RUN不提升，七天目标ACTIVE。W51指定停止data删除自动审批拒绝原因为blocked by policy，保留不重试。

Handoff [PR44](https://github.com/GTX537/CP6.AI-Handoff/pull/44)已采用 `e25fb253fa991417d55ff2b404dd9c9fa3a73683`，准确分支删除、固定树干净detached、finish释放。Main仅剩协调PR/finish，不提前声明释放。登记开工至此约42.52min，原预算75–110min，最终以finish为准、子任务不重复累计；前置只读核对约8min。

收尾过程记录：Handoff只读ls-remote一次180秒超时；先到的resume被生命周期锁拒绝且未变更状态。原命令退出/锁释放后，移除仅本次进程的代理环境变量，bind/resume正常完成。GitHub连接器一次传输错误后恢复，未合并前的只读采用检查实际拒绝；正常合并后重新核对通过。以上为运输/生命周期记录，不作为产品失败或产品重测。START的“无原结果”措辞在本计划澄清为“已有原结果但对应Bundle缺失”，无原槽的合法新请求仍可执行保存体。
