# W50-01：库存原操作同事务 Outbox 与可信刷新投递

COORD本聊天单人，WP02-10 / 七天一期D4限定窗口。基线`3f75fcff219ad60611bbec013513e2a7ee2464f0`；DEV-1 `codex/w50-01`、DEV-2 `codex/w50-handoff`。2026-10-22 11:02:33Z START，预计实际120–180分钟；2026-10-23 00:23:17Z按原任务resume，中断时间不计实际执行。中断前最后DDL产物时间11:20:48Z；最终报告分别记录墙钟和估算执行区间，不能把约13小时中断算作开发工时。W37–W49已收尾不重复；七天目标ACTIVE。

## 改动与真实边界

W44已记录库存提交后通知best-effort、确认丢失可能漏发，本窗补齐这个既知缺口。新Apply/Move原操作在实际库存Owner同一DbConnection/NpgsqlTransaction内保存native Outbox、binding和Source，队列Context不自行提交；数据库deferred约束核同事务COMMITTED原操作、准确Owner/合同/结果摘要/租户/消息绑定。失败回滚库存、流水、采番、审计、原结果和通知；同键回放不新增，Move双腿只产生一条空提示。

独立`cp6_stock_outbox_v1`采用正式Platform `[0.10.3]` 的OutboxV2/pg-xmin-v1，保留原C02/ERP模型、八字节token和队列。迁移冻结实际采用包的模型与原生guard，补充来源、信封、路由身份和原请求防护；原生UPDATE/旧writer被拒，未采用retention/handover的路由不允许DELETE/TRUNCATE/直接改epoch。无新包或SQLServer实现。

真实TenantScopeRunner的新scope触发私有受信Background verifier，当前Tenant Enable/ExpireDate/AuthenticationPolicyRevision和Authority共同决定服务身份；每个短SERIALIZABLE写事务再核真实行锁与版本。网络在DB事务之外执行。配置缺失默认禁用，启用需明确HostId/HTTPS Issuer/Region；没有企业默认、HTTP身份伪造或无HttpContext旁路。

DispatchRun先保存原requestId，claim/ticket/mutation按原编号查询。提交回执丢失可查询原回执；已有SendAttempt不重复网络调用。真正未知发送保留原请求并阻止该租户路由换号继续，其他租户继续。此窗没有未知结果人工清空/重排、归档、外部ACK或正式保留策略。

实际publisher只向当前租户库存组发送无参数InventoryInvalidatedV1，数据库payload为`{}`。SignalR确认只表示服务端发送调用完成，不是客户端ACK、用户已读、跨宿主完整交付、Finance、broker或OwnerApplied。旧best-effort即时提示可重复但不含业务数据；原财务桥、无键/父Owner库存原语和历史原操作不自动纳入。

详细配置、锁顺序、原键恢复及边界：[stock-outbox-v1](../../../../design-handbook/remediation/stock-outbox-v1.md)。

## 验证与一次集中审查

22项去重相关.NET/真实PostgreSQL通过，含实际Program/登录/HTTP/WebSocket、共同事务与原编号恢复；无前端改动，未重复前端或全仓验证。来源`D:/CP6-local-test/wave50/foundation/final-validation.json`；同一testId只计最新有效结果，没有把旧执行改称新执行。

|批次|实际结果及用途|
|---|---|
|stock-outbox-red-20261022T110545Z|2项真实原行为失败：库存/原结果已保存，持久队列仍为0|
|stock-outbox-source-20261023T002955Z|2/2：正常提交及提交回执丢失均原子保存一条通知，按原操作重放不重复|
|stock-outbox-dispatch-fixed-20261023T003459Z|12/12：正常/UNKNOWN/停租/错误scope/缺注册、claim/send/complete确认丢失、入队失败/最终权限失败/双腿回滚、Move仅一条通知|
|stock-outbox-host-guards-20261023T003705Z|2/2：实际Program已登录WebSocket收到空提示且native Published；原生绕过/来源篡改/无原操作入队拒绝|
|stock-outbox-final-guards-20261023T003832Z|5/5：加入路由/绑定/Authority身份不可变后，相关真实入队/发送/UNKNOWN/原生拒绝定向复验|
|stock-outbox-final-races-20261023T004014Z|10原库存操作/真实HTTP通过；并发用例因EF包装40001未被fixture捕获失败，记录保留|
|stock-outbox-race-wrapper-20261023T004203Z|1/1：只识别真实底层40001，两个独立事务及下一轮恢复仍要求准确一次发送和原claim|

schema probe的ReadOnlyMemory.AsSpan编译错误、dotnet run artifacts启动路径错误、错误Fact属性的编译失败、并发fixture包装异常均保留；不把夹具/工具错误记成产品红测。实际Program夹具仅初始化Core/priority，Space后台存在缺Space_Job日志，已保留，不能作为全宿主健康通过。已存在NuGet漏洞/nullable等告警不在本窗变更；本窗不作正式安全发布证明。

一次作者完整SPEC/QUALITY覆盖所有产品与测试diff、PG来源/原生事务与guard、当前服务权限/租户、原编号UNKNOWN、真实HTTP/SignalR、旧队列兼容、配置和未采用边界。审查补充路由/绑定/Authority身份不可变，相关5项已定向通过；并发夹具只复验自身。没有剩余合并阻断，无审查代理。

7处自有PG/API已停止，按ownership/绝对边界/进程/重解析点核对后回收可再生data 807517901字节，日志/TRX/runtime/失败记录及缓存保留。无Actions触发/变更/取消、既有环境迁移或生产。输入不变的后续合并仅核远端采用及必要冒烟。

## 剩余与生命周期

正常产品PR、Handoff采用和原分支最终状态PR后，核对远端、准确删除分支并manage finish；当前DEV-1/2待收尾，集成席空闲，根用户未跟踪资产保留。完整WP02-10、PLT-DEL业务验收、金融投递/消费者、正式SCHED/RES/PERF及CRM身份/共同事务继续按原任务推进；24/375整卡、125目标0交付、247运行NOT_RUN不提升。真实企业issuer/角色、迁移/切换、跨宿主拓扑、保留/归档/hold未验收。七天目标保持ACTIVE。
