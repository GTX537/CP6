### 七、CP6.WebApi — 基础设施（启动/中间件/过滤器/SignalR/本地化/种子/后台服务）

#### (WebApi 根)

- `CP6.WebApi/Program.cs` — 应用启动总入口：①DI 注册（控制器全局挂 OperLogFilter、SignalR、Swagger、SqlServer DbContext + Dapper IDbConnection、Redis/内存缓存、DbStringLocalizer 本地化、Kafka 操作日志生产者+消费者、RabbitMQ 通知发布器+消费者、JWT 认证、AllowAll CORS，以及 PUB/OA(Wf)/Fin/Pur/Plan/A2~A5/ERP/MES/WMS 全模块服务与按 appsettings 开关切真实/桩/NoOp 的跨模块 Bridge Hook、各 HostedService 后台 Worker、Security 认证加固服务、Prometheus 指标）；②首启种子（Migrate 建库、默认租户/自动凭证规则/A3 科目/admin 用户与全套 Sys_Menu 菜单+角色菜单/密码哈希迁移/A5+采购审批流程/通用主数据码 等幂等播种）；③中间件管线（UseCors→UseHttpMetrics→UseAuthentication→TenantMiddleware→UseRequestLocalization→BizExceptionMiddleware→UseAuthorization→MapControllers、映射 3 个 SignalR Hub 与 /metrics）。
- `CP6.WebApi/GlobalUsings.cs` — 全局 using 集约：把 Erp/Sys/Integration/Common/Mes/Wms 等子命名空间的实体、DTO、Core 服务在 WebApi 全程统一可解析。

#### BackgroundServices

- `CP6.WebApi/BackgroundServices/KafkaOperLogConsumer.cs` — Kafka 操作日志消费者：独立长线程阻塞拉取 cp6.operlog topic，手动提交位移（至少一次），把日志落库 Sys_OperLog 并经 NotifyHub 推送 "NewOperLog"。
- `CP6.WebApi/BackgroundServices/NotificationConsumer.cs` — RabbitMQ 业务通知消费者：订阅 cp6.notification 队列，per-message ack/失败 requeue，把 BusinessNotification 经 NotifyHub fanout 广播给所有客户端（预留邮件/Webhook 扩展）。
- `CP6.WebApi/BackgroundServices/TenantScopeRunner.cs` — 后台任务"按租户循环"静态助手：枚举全部启用租户，逐租户开独立 scope 并设当前租户后执行 body，保证后台扫描/对账/重试正确作用域+写入盖章；单租户异常吞掉记日志不影响其余。
- `CP6.WebApi/BackgroundServices/IntegrationEventRetryWorker.cs` — IntegrationEvent 重试 Worker：按 PollInterval 周期、按租户循环，重投到期的 Failed 事件（指数退避），超过 MaxAttempts 转死信并经 IDeadLetterNotifier 告警。
- `CP6.WebApi/BackgroundServices/WfTimeoutScanWorker.cs` — OA 审批超时扫描 Worker：每分钟按租户循环调 IWfTimeoutService.ScanOnceAsync，按节点 TimeoutAction（提醒/自动批准/驳回/升级）处理到期待办（v1 单实例）。
- `CP6.WebApi/BackgroundServices/FinReconciliationWorker.cs` — 财务每日对账 Worker：启动延迟首跑后每 24h、按租户循环跑 AP/AR 子账↔GL+试算平衡只读勾稽，不一致 LogError 告警。
- `CP6.WebApi/BackgroundServices/OperLogCleanupService.cs` — 操作日志保留期清理：按 OperLog:RetentionDays/CleanupIntervalHours，用 ExecuteDeleteAsync 跨租户（IgnoreQueryFilters）批量删除过期 Sys_OperLog（≤0 天则永久保留不清）。
- `CP6.WebApi/BackgroundServices/OeeCalculationService.cs` — OEE 定时计算 Worker：每 5 分钟按租户循环用 Scoped IOeeService 重算当日 OEE 落 T_OeeDaily，日期变更时补算前日（多线程/委托示范）。
- `CP6.WebApi/BackgroundServices/MachineStatusMonitor.cs` — 设备状态监视 Worker：每 30 秒按租户循环扫稼働中设备，直近 10 分钟无生产实绩判定空闲→自动改"停止(Status=0)"并经 IMesNotifier 推送状态变更。
- `CP6.WebApi/BackgroundServices/AssetDepreciationWorker.cs` — 月末折旧 Worker：每日按租户循环检查，当前开启期为月末且无本期批次时生成折旧 Draft 草稿（不过账，过账交人工/结账钩子）。

#### Filters

- `CP6.WebApi/Filters/OperLogFilter.cs` — 全局操作日志 ActionFilter：采集 POST/PUT/DELETE 请求体+耗时+状态码+用户+IP+租户组装 Sys_OperLog，优先投 Kafka 通道（不可用降级直写 DB），始终跳过 /api/auth 与 /api/operlog，GET 默认不记。

#### Hubs

- `CP6.WebApi/Hubs/NotifyHub.cs` — 通用 SignalR 通知中心 Hub（/hubs/notify）：客户端连接/断开记日志，承载操作日志推送、业务通知 fanout、OA 待办推送等广播。
- `CP6.WebApi/Hubs/MesHub.cs` — MES 现场实时 Hub（/hubs/mes）：推送生产实绩/不良/设备状态/工单状态/停机事件，支持按工单(wo:)、按设备(machine:)分组订阅。
- `CP6.WebApi/Hubs/WmsHub.cs` — WMS 仓库实时 Hub（/hubs/wms）：推送在库变动/入库受领/出库出货/盘点完了，支持按仓库(wh:)、按产品(product:)分组订阅。

#### Localization

- `CP6.WebApi/Localization/BizException.cs` — 业务异常类型：只携带 i18n 错误码+格式化参数+HTTP 状态码（默认 400），不带具体语言文字，交中间件按请求 culture 解析为本地化消息。
- `CP6.WebApi/Localization/DbStringLocalizer.cs` — DB 支持的本地化器（IStringLocalizer 实现，Singleton）：以 Sys_Lang 表为唯一译文源，复用 CacheService 缓存，按"租户覆盖→全局→回退语言→源语言 ja→key"链解析，含同语义 Factory 让 IStringLocalizer&lt;T&gt; 复用同一张表。
- `CP6.WebApi/Localization/LangColumn.cs` — 多语言列/命名空间小工具：定义 5 语言码与懒加载命名空间集合，提供按语言取列值 Pick、取 key 命名空间、词条行投影成 {key:value} 字典等共享方法。

#### Middleware

- `CP6.WebApi/Middleware/BizExceptionMiddleware.cs` — 全局捕获 BizException，用 IStringLocalizer 把错误码解析为当前 culture 译文，返回统一信封 {code,message,data}（须注册在 UseRequestLocalization 之内）。
- `CP6.WebApi/Middleware/TenantMiddleware.cs` — 租户中间件：从 JWT 的 tenant_id claim 解析当前租户写入请求级 ITenantContext，供 CP6Context 全局查询过滤+写入盖章；无有效 claim 保持默认租户（须排在 UseAuthentication 之后）。

#### Observability

- `CP6.WebApi/Observability/BridgeMetricsCollector.cs` — Bridge/IntegrationEvent 业务指标 Prometheus 采集器（Singleton）：经 BeforeCollect 回调在每次 scrape 时由 T_IntegrationEvent 重新聚合，公开 hook×状态件数(Gauge)、重试队列深度、死信数三项指标，聚合失败不破坏 /metrics。

#### Seed

- `CP6.WebApi/Seed/A3AccountSeed.cs` — A3 固定资产科目对账种子（异步幂等）：给既有 CoA 补 Role 到 1601/1602/4301 并新增 1606/1901/6115/6711 资产清理/损益类科目（空库交模板导入）。
- `CP6.WebApi/Seed/A5BudgetFlowSeed.cs` — A5 预算审批流程种子（幂等）：建单审批节点（Specified 指定 admin）的 budget-approve 流程定义并绑定 A5_Budget 业务类型。
- `CP6.WebApi/Seed/PurApprovalFlowSeed.cs` — 采购 PR/PO 审批流程种子（幂等）：各建单审批节点流程 po-approve/pr-approve 并绑定 PUR_PO/PUR_PR，配后送审走真实 OA（删绑定即回退自动放行）。
- `CP6.WebApi/Seed/PasswordHashMigrationSeed.cs` — 密码哈希迁移种子（启动幂等）：把 Sys_User 中现存明文密码就地 BCrypt 哈希（已哈希则跳过），一次性不可逆原地迁移，返回本次哈希条数。
- `CP6.WebApi/Seed/I18nLabelSeed.cs` — ERP/MES 画面标签多语言词条种子（自动生成，日文原文＝key，5 语）。
- `CP6.WebApi/Seed/I18nMiscScreenSeed.cs` — 杂项画面（组件/WMS 占位/MES 步骤等）硬编码日文 t() 化补充词条种子（日文＝key，5 语）。
- `CP6.WebApi/Seed/I18nErpScreenSeed.cs` — ERP 旧画面硬编码日文 t() 化补充词条种子（第 1 批，日文＝key，5 语）。
- `CP6.WebApi/Seed/I18nErpScreen2Seed.cs` — ERP 画面 t() 化补充词条种子（第 2 批：产品主数据/估算/受注/取引先/版型，日文＝key，5 语）。
- `CP6.WebApi/Seed/I18nMesScreenSeed.cs` — MES 画面硬编码日文 t() 化补充词条种子（日文＝key，5 语）。
- `CP6.WebApi/Seed/I18nFinScreenSeed.cs` — 财务 GL 内核前端 4 视图+菜单 nav.6xx+E-FIN-* 错误码词条种子（中文＝key，5 语，幂等合并）。
- `CP6.WebApi/Seed/I18nWfDesignerSeed.cs` — OA 自研表单/流程设计器画面词条种子（中文＝key，5 语）。
- `CP6.WebApi/Seed/I18nBackendMsgSeed.cs` — 后端控制器响应里硬编码错误/提示文案词条种子（原文＝key，经 LocalizedControllerBase 按 culture 解析，5 语）。
- `CP6.WebApi/Seed/I18nCnScreenSeed.cs` — PMS/OA/Pub 组件等后期中文新建画面硬编码中文 t() 化补充词条种子（中文＝key，5 语）。
- `CP6.WebApi/Seed/I18nPlanScreenSeed.cs` — 计划中台 P1 MRP 看板+计划主数据视图+菜单 nav.73x+枚举+E-PLAN-* 错误码词条种子（中文＝key，5 语）。
- `CP6.WebApi/Seed/I18nA2ScreenSeed.cs` — A2 工艺路线（工作中心+工序费率）主数据画面+菜单 nav.31x+字段+W/E-A2-* 错误码词条种子（中文＝key，5 语）。
- `CP6.WebApi/Seed/I18nA3ScreenSeed.cs` — A3 固定资产菜单/枚举/字段/错误码词条种子（点分语义 key，5 语）。
- `CP6.WebApi/Seed/I18nBankReconScreenSeed.cs` — A4 银行对账菜单/视图/字段/按钮+E/W-A4-* 错误码词条种子（5 语）。
- `CP6.WebApi/Seed/I18nA5BudgetScreenSeed.cs` — A5 预算管理菜单 nav.62x/视图/字段/按钮/枚举+E-A5-* 错误码词条种子（5 语）。
- `CP6.WebApi/Seed/I18nPurScreenSeed.cs` — 采购 MVP 前端 4 视图+菜单 nav.70x+枚举+E-PUR-* 错误码词条种子（中文＝key，5 语，幂等去重）。

#### Services

- `CP6.WebApi/Services/SignalRMesNotifier.cs` — IMesNotifier 的 SignalR 实现：把生产实绩/不良/设备状态/工单状态/停机通知经 MesHub 广播+分组定向推送，将 SignalR 依赖封在 WebApi 层。
- `CP6.WebApi/Services/SignalRWfNotifier.cs` — IWfNotifier 的 SignalR 实现：OA 待办创建经 NotifyHub 广播 "WfTodoCreated"（阶段1 客户端按 assigneeId 过滤，TODO 定向推送）。
- `CP6.WebApi/Services/SignalRWmsNotifier.cs` — IWmsNotifier 的 SignalR 实现：在库变动经 WmsHub 实时广播+仓库/产品分组推送；入库/出货/盘点等"需人察觉"业务事件再 best-effort 经 RabbitMQ(INotificationPublisher) 确实配信（含本地化标题/消息）。
- `CP6.WebApi/Services/LangPublishService.cs` — i18n 发布模式服务：把 Sys_Lang 全局值导出成版本化静态 JSON（{version}/{lang}.json + manifest.json）供前端不可变长缓存，支持读取/回滚到历史版本与目录穿越防护。
