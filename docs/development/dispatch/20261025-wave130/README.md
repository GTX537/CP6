# W130 CRM 原生 PostgreSQL 身份基线宿主

COORD本聊天单人。W129 Main542/Handoff105已合并、准确分支删除、全部席位释放；本轮只核销其最终证明。Main基线9297eb12cc997f25c5c10271f5abf3f19b1846c5，CRM基线f96de22c2df0f79f695890c4efb956e2d3cbf5a4，Handoff基线ec184810553bd78c23ef21783fcdd7c22061eca7。无重复任务或PR。

|任务|本聊天预计执行时间|
|---|---|
|核对真实Main目录鉴权、CRM原生宿主、注册及提交边界|15–25分钟|
|接入明确启用的凭据采集、原生基线恢复与发布后台宿主|35–55分钟|
|定向PostgreSQL/HTTP故障与真实跨仓来源验证|25–45分钟|
|一次任务审查、正常PR、准确分支清理与官方finish|15–25分钟|

合计90–150分钟。Main START 2026-10-25T06:25:23.245775+00:00计一次、跨仓不叠加。复用DEV-1/Main与DEV-2/Handoff→CRM→Handoff；跨仓先可靠保存并官方finish，再bind，不新建物理目录。Main W130-01、CRM W130-02、Handoff W130-HANDOFF。

范围：将已有完整目录/原字节基线采集与PostgreSQL恢复/发布接入实际CrmInquiryNativeHostV2。显式启用，冻结可信租户/来源/注册和专用服务凭据；生产传输HTTPS，不把浏览器header或Inquiry服务上下文当C2 bearer。沿用实际Main目录鉴权及原始source合同；无旧IdentityProjectionContext回退。

事务：注册与BeginRecovery在独立Serializable事务，网络收集在事务外，RecoverAndPublish在调用者拥有的Serializable事务。复用原生2分钟租约、90秒collection及最长20分钟publication边界；不自动安装/迁移/伪造来源。提交结果不明不当成功，不盲目重新发布同一结果；后续从持久current/lease核对恢复。租户独立故障隔离、取消可停止；日志不含令牌、秘密、原始身份内容。

验收：实际后台服务启停与DI，真实HTTP完整目录和原文入PG、当前publication复用/撤回、并发租约、故障/取消、未知提交和租户隔离；真实Main→CRM既有两进程夹具按受影响范围扩展或复用。不得把fixture身份、注册或测试来源冒充企业环境采用。相关局部测试+一次完整SPEC/QUALITY审查；不触发Actions或全仓测试。

本轮仅D5必要第一宿主窗口，可信delta入口/commit后ACK与native API observability仍为D5后继，不将完整D5或正式125目标关闭。D1/D4已完成，其他原阶段保持部分采用；2/7只表示阶段出口关闭数。七天持续目标ACTIVE，剩余42–70h低置信估计保持，外部真实注册/密钥/政策/日历条件不虚构。当前Full Access/never授权充分，不再重复询问。

## 已复现前置 BUG #543

真实最新Main目录安装被旧函数清单拒绝；[Issue543](https://github.com/GTX537/CP6/issues/543)已登记P1。W130保存同一分支后，官方释放DEV-1，在最新main以独立BUG-543任务修复精确函数准入并复测。完成后resume本分支继续W130；不回退旧来源或放宽未知函数检查。预计增加20–35分钟，计入本轮实际时间。
