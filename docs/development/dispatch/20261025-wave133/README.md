# W133 原生询盘Outbox发布与原消息恢复

COORD本聊天单人。W132 Main547/CRM156/Handoff108已完整合并、准确分支删除和官方finish，登记32.84分钟，本轮按最终proof核销一次。四仓最新目标及AGENTS已核对，当前没有开放PR、重复W133任务或本轮活动Actions；固定席位空闲。

|任务|预计本聊天实际执行时间|
|---|---|
|五类原生路由、受控登记、数据库约束与旧队列接入|30–45分钟|
|原生宿主发布、原请求恢复与停止接线|40–60分钟|
|PostgreSQL、故障、并发与权限的定向验证|35–55分钟|
|一次任务级审查、三仓合并和准确分支/席位收尾|15–20分钟|

合计120–180分钟。Main START 2026-10-25T08:40:54.849109+00:00至最后finish计一次，跨仓不叠加；属于原七天持续目标D6必要消息出口。DEV-1/Main和DEV-2/Handoff→CRM→Handoff固定复用；Main W133-01、CRM W133-02、Handoff W133-HANDOFF，不加工作树，不派代理。

前置实际源码确认：configuration/submission/decision/consent/retention五类已采用原生Owner将正式事件与原图同事务写入crm_inquiry_configuration_v2的Platform0.10.3 OutboxV2，路由均Blocked/unassigned，AssignedEpoch=1；只有持久入队。现有CrmWorkerDispatch走另一套CrmDatabaseFactory/EF业务Outbox，不能当作本账发布完成。采用已有固定0.10.3 caller-transaction/claim/send/complete原协议，不另造公共消息合同。

在CRM内增加显式受控的原生发布者/当前服务权限和路由登记；首次接入只在独立授权安装/登记调用中进行，核对原队列及无已有发布者/未知claim/send历史，不能应用启动自动DDL或擅自迁移既有环境。旧原键、envelope字节/hash、Owner/租户/队列/路由身份及原图不重建。普通业务enqueue继续原事务/固定身份七锁，不通过多拿任意advisory锁破坏共同门；发布使用独立可信后台原生事务、当前权限与实际authority行fence。未知/关闭的路由不fallback旧EF或伪成功。

发布循环需在claim前持久化原requestId，进程重启先查询原claim/completion，UNKNOWN保留原消息/原键与可核查状态，不能改新键强发；确认提交的claim才可发，网络调用不在DB事务内，send/complete依正式租约/预算/封印和当前权限。真实发送确认只表示传输层接受，不冒称下游业务消费。故障/取消/多实例/租户隔离和撤销竞争保留定向检查，原业务提交/原结果不由遥测或发送失败改写。后台停止不得遗留失控任务。

接入实际原生宿主，默认关闭、明确配置服务和transport后启用；核对实际受控目的地和原包publisher语义，不把operator声明当broker ACL或生产资格。当地隔离HTTP传输/服务凭据/数据是明确测试夹具，真实Dapr/Kafka/企业TLS/ACL/注册仍按可用证据另列，不触碰上轮被拒绝移除的Docker socket。不运行Actions/正式签名发包/生产部署。

只对本次实际输入变化做必要PG/HTTP/原Owner回归，一次任务级SPEC/QUALITY。若新增嵌入式SQL资源，单独核对csproj只影响本地构建输入及当前workflows/过滤，不能间接触发远程运行。Main仅同步台账、交付和四状态文档。保留原失败/数据/cache，无无关重构或完整隐私导出扩展。五路由的受控发布是已采用主线的必要公共支撑，不因此关闭全部WP03-03/04或125目标。

本轮之后继续NUM/FILE/PRIV准确适用、真实政策/日历来源及D2/D3/D5/D7原剩余。正式卡、125目标、247运行验收状态不预先提升；七天保持ACTIVE。

## 交付与继续范围

20项唯一相关CRM本地检查通过、必需0跳过：真实原生Owner/HTTP/PG、五类原事件、原run/提交收据恢复、并发/当前权限及角色隔离、取消停止、保护漂移，含真实60分钟匿名化及最终代码原文复核。 CRM/Handoff已正常合并、准确分支删除并finish；Main正常收尾继续。[交付报告](../../evidence/W133-01/DELIVERY.md)。下一独立任务先处理[CRM #157](https://github.com/GTX537/CP6.CRM/issues/157)，再核D6 NUM/FILE/PRIV在冻结公开提交/H01路径的准确适用、版本接口和真实政策/日历来源继续核对；当前真实Dapr/Kafka/企业TLS/ACL/凭据/登记/消费者/SLO仍未证。D2新对象归属及REP/RMA写Owner共同门、D3计划写入口、D5实际宿主独立资格和D7最终集成保留，不扩展全部隐私导出或125目标。
