# W50-01 真实 START / 七天一期 D4

COORD本聊天单人；2026-10-22T11:02:33.333510+00:00；Main基线`3f75fcff219ad60611bbec013513e2a7ee2464f0`。W49产品PR414/最后状态PR415、Handoff25采用，Issue413关闭，准确分支已删除，固定工作区干净detached并finish，实际39.38分钟。W37–W49不重复；七天持续目标ACTIVE。

本窗Main库存原操作同事务OutboxV2与可信库存刷新投递。DEV-1 `codex/w50-01`；DEV-2 `codex/w50-handoff`；预计实际120–180分钟：原生存储与共同事务40–65、可信后台/恢复40–65、定向验证/一次集中审查/合并收尾40–50。实际按finish修正，按前置和完成条件推进，不等待日历。

## 现状与限定采用

- Main与origin/main一致，CRM仍`c420cf9e0711bc0c5529d2f709708a57533b341a`、Platform仍`a89e90eb4f078f37a6f8610882079073185fbcf8`。W49 Actions0，当前Main开放PR为空，最新容量无告警，AGENTS/POLICY哈希未变；根用户未跟踪资料保留。固定席位重新start，不新增物理树。
- W43 Main journal、W44 Stock Apply/Move原操作已实际采用，业务/库存/流水/审计/原结果同事务且UNKNOWN按原键读；旧Finance bridge和刷新通知仍在已知提交后best-effort，提交成功回执丢失或进程中断可留下未发送提示。此次不重新实现原操作，也不从原结果读取路径重发。
- 已读取PLT-DEL-01归并正文（仅移除重复出处链接便于完整阅读），直接核对现行0.10.3 OutboxV2实体/原生映射、真实事务绑定、不可变回执、旧writer隔离、短事务发送/UNKNOWN协议以及现行Main库存/通知代码。正式合同允许经过实证的同物理库/连接/事务跨Context；Platform技术回执不是业务效果。
- Main现有C02 Outbox使用8字节旧版本列，OutboxV2要求pg-xmin-v1与原生防绕过约束。采用独立固定技术schema/Context，保留原C02/ERP队列、既有EF映射、版本列和存量消息；新schema仍在Main所属物理数据库。生产者必须核对共享同一DbConnection/DbTransaction并加入实际库存Owner事务，不能用第二连接、自开事务或成功后补写冒充原子。
- 新库存原操作只入队一次：operationId、Tenant、精确原结果和新原生outbox/binding关系在同事务；重放不重复入队，关闭未执行命令无事件，回滚不留孤儿。原既有无键/父Owner入口不在本窗自动扩大采用。
- 首个路由只发布现有InventoryInvalidatedV1空刷新提示，不含工单、仓库、产品、数量、价格或Actor。固定Main后台使用真实当前租户/期限/登记和Platform可信Background上下文、正式OutboxV2 claim/send/complete；无字符串人类身份、无HttpContext缺失旁路。Owner原结果、原始字节、claim/mutation身份分别保留；失败或回执丢失按原键查询，不能假装回滚、换ID或回退旧MarkFailed。
- 发布器调用真实SignalR宿主的租户组，网络发送在数据库短事务之外。Confirmed仅证明服务端发送调用接受；在线客户端仍经独立当前query重读，离线接收、用户已读、财务效果、broker/端到端exactly-once均不由本窗保证。旧Finance bridge仍独立剩余，不把空提示技术完成写成业务采用。
- 技术租约/重试政策独立版本化；缺当前登记/租户/实际原载体即拒绝。未获得正式保留/归档/消费者完成证据时不删除原消息或回执；不伪造tenant角色、业务政策、旧存量epoch或移交证明。不存在新生产者自动接管旧消息。无生产部署/既有环境迁移/外部角色授予。

## 实施、验证和收尾

1. 固定新Main Outbox原生schema/migration和实际共享事务接缝，原生约束绑定准确Tenant/operation/native row，初始/失败测试保存真实输出。
2. 可信后台与固定空提示发布器接入正式0.10.3协议，处理claim/send/complete的已提交与UNKNOWN，保持当前租户和原键边界。仅实际测试通过范围记采用，其他route/SCHED/RES/PERF/C02/Finance保持未验收。
3. 本地隔离PG验证源效果/原结果/outbox一起回滚、提交丢回执后原键不重复、跨租户/伪造context/旧writer拒绝、实际claim/原生token/恢复及真实宿主发布；相关既有库存/通知检查按输入复用。一项完整任务一次作者SPEC/QUALITY，无代理或全仓回归；如发现新的实质风险，仅扩大对应验证并说明。
4. 一份DELIVERY、总台账/四状态/生成页同步；正常产品PR与Handoff、远端采用、原任务最后状态PR、准确分支清理及manage finish。整卡24/375、125目标0交付、247运行NOT_RUN不因本窗提升。七天目标继续。

写入范围：Core集成/库存服务、新Main Outbox技术Context，PG独立前向迁移，WebApi固定后台/通知注册及必要实际宿主入口，相关Tests，docs/development/project-memory/design-handbook。依赖精确0.10.3复用，不修改Platform源码或包、生产配置、旧队列及旧版本历史。独占Main共同事务/新Outboxschema/Program/PG迁移/原操作源，PG55435。

## candidate

库存原操作同事务Outbox、私有受信后台及原编号恢复已验证；22项去重相关.NET/真实PostgreSQL通过，含实际Program/登录/HTTP/WebSocket、共同事务与原编号恢复；无前端改动，未重复前端或全仓验证。真实UNKNOWN保留，SignalR确认不等于客户端ACK/Finance。产品PR/Handoff及准确分支/席位finish待执行。

[DELIVERY](../../evidence/WP02-10/stock-outbox/DELIVERY.md)。
