# W131 原生身份增量接收、提交后确认与撤权传播

COORD本聊天单人。W130 Main545/CRM154/Handoff106及独立BUG543/PR544已正常合并、准确分支删除和官方finish，最终proof核销一次，不重复实施。当前Main e11613a324cdddbfa88f93210df4f8b15b5c593d、CRM04bf47e1078c8f741ba79eee94f3e94b44fa84f3、Handoff82fef5963d59ed9e3994be46bf948e76c61eeca5、Platforma89e90eb4f078f37a6f8610882079073185fbcf8均已远端核对。

|任务|预计本聊天执行时间|
|---|---|
|固定可信sidecar、租户登记和消息元数据边界|20–30分钟|
|原生增量入口、共同事务与提交后确认|45–75分钟|
|重复/冲突/未知提交/撤权及实际Main来源验证|35–60分钟|
|集中审查、三仓正常合并与准确分支/席位收尾|15–25分钟|

合计115–190分钟。Main START 2026-10-25T07:21:01.855956+00:00至最后finish计一次，跨仓不叠加。DEV-1/Main、DEV-2/Handoff→CRM→Handoff复用；Main W131-01、CRM W131-02、Handoff W131-HANDOFF，同任务同仓一个分支。

已核真实Main IdentityEventDispatchWorker/IdentityDaprPublisher、CRM旧入口与原生PostgreSqlIdentityIngressV2、正式0.10.3 TrustedContextExecutor/CallerTransactionInbox接口及原C02传输证据。现Main仍发cp6.platform.events.v1、冒号partition、原structured bytes和rawPayload=true；消费rawPayload=false。新宿主保留/dapr/subscribe及/internal/crm/identity/events，只启用原生路径，不启动旧EF消费者、不套通用斜杠partition。

新可信来源边界必须验证实际sidecar凭据/peer以及受控有效登记、明确租户来源和原始broker topic/key；不从body/header自报profile、actor、登记或自算key冒充传输事实。保留已核Dapr Kafka reserved-header过滤与原始字节证据，登记记录transport版本/配置身份/来源引用；当前部署或凭据不可用时关闭新入口。显式配置默认关闭，无既有环境切换、数据库安装或权限授予。

每次重投重新验证入口与当前登记，以新上下文/Serializable事务进入原七锁及PreCheckpoint，全原文、Inbox/原结果、漂移/撤回和已有会话撤权共同提交。只有提交已确认且读取持久结果后映射SUCCESS或持久冲突DROP；未知COMMIT/依赖错误RETRY，同原message重新走内容门，不提前ACK或产生新操作ID。未持久隔离的格式/来源错误不得冒充成功或已隔离DROP；安全拒绝和重试边界明确。

验证重点为实际原生HTTP宿主、冻结/到期/撤销登记、凭据和metadata伪造、两个租户、重复/历史冲突、数据库/提交错误、同原键恢复、消息导致的原生会话撤权及最终持锁门竞争。实际Main原始事件经受信入口采用，源时间/字节不改写。当前Docker Linux daemon未运行；先完成独立PG/HTTP工作，再判断可复用的真实Dapr/Kafka字节证据和可执行的本轮运输范围，不冒称当前broker或生产验收。

一次任务级集中SPEC/QUALITY，局部相关验证，成功未变输入直接复用。无Actions、公共线合同/依赖/迁移变动或生产部署。真实企业transport ACL/凭据/注册及生产SLO不由本地夹具伪造。原生API可观测性后继；D2/D3/D6/D7必要出口继续。七天目标ACTIVE，D1/D4完成，2/7仅阶段比例；正式整卡/125目标/247验收状态不提升。

## 交付与继续范围

12项唯一相关本地检查通过：CRM11、Main1；必需0跳过。实际原生HTTP、普通PostgreSQL写入、提交后确认、最终持锁门撤权及实际Main原文跨进程采用均通过；未变成功来源复用，原失败保留。 CRM155/Handoff107已正常合并、准确分支删除并finish；Main正常收尾继续。[单份报告](../../evidence/W131-01/DELIVERY.md)。D5原生API可观测性仍待；本轮实际Dapr/Kafka链路NOT_RUN，真实企业传输ACL/凭据/登记、协调撤销和生产传播SLO未证明。D2、D3、D6、D7继续原计划必要出口，完整D5与正式验收不提升。
