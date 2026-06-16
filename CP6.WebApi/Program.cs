using System.Text;
using CP6.Core.BaseProvider;
using CP6.Core.EFDbContext;
using CP6.Core.Services;
using CP6.Entity.DomainModels;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.Data;
using Microsoft.Data.SqlClient;
using CP6.Core.Utilities;
using CP6.WebApi.Filters;
using CP6.WebApi.Hubs;
using Prometheus;

var builder = WebApplication.CreateBuilder(args);

// 本地凭证覆盖（appsettings.Local.json 在 .gitignore，绝不入仓库）。
// 加载顺序：appsettings.json → appsettings.{Env}.json → appsettings.Local.json → env vars
builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: true);

// 1. 注册控制器（全局注册 OperLogFilter）
builder.Services.AddScoped<OperLogFilter>();
builder.Services.AddControllers(options =>
{
    options.Filters.AddService<OperLogFilter>();
});

// 1.1 注册 SignalR
builder.Services.AddSignalR();

// 2. 注册 Swagger
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    // 完全修飾名で schemaId を一意化（入れ子型 DeleteRequest 等の衝突回避）
    c.CustomSchemaIds(t => (t.FullName ?? t.Name).Replace("+", "."));
});

// 3. 注册数据库上下文
builder.Services.AddDbContext<CP6Context>(options =>
    options
        .UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

// 3.1 注册 Dapper 用的 IDbConnection（每次请求新建连接）
builder.Services.AddScoped<IDbConnection>(_ =>
    new SqlConnection(builder.Configuration.GetConnectionString("DefaultConnection")));

// 3.2 注册缓存（开发用 Memory，生产切 Redis 只需改这里）
var redisConn = builder.Configuration.GetConnectionString("Redis");
if (!string.IsNullOrEmpty(redisConn))
{
    // 生产模式：Redis（配置了连接字符串就用 Redis）
    builder.Services.AddStackExchangeRedisCache(options =>
    {
        options.Configuration = redisConn;
        options.InstanceName = "CP6:";  // Key 前缀，区分不同应用
    });
}
else
{
    // 开发模式：内存缓存（零配置，行为和 Redis 一致）
    builder.Services.AddDistributedMemoryCache();
}
builder.Services.AddSingleton<CacheService>();

// 3.2.1 i18n 优化 P1：DB 支持的本地化（IStringLocalizer 读 Sys_Lang，复用 CacheService）。
//  - DbStringLocalizer 可 Singleton（缓存未命中时经 IServiceScopeFactory 取 scoped DbContext）。
//  - 同时覆盖 IStringLocalizerFactory，使 IStringLocalizer<T> 也复用同一张 Sys_Lang。
builder.Services.AddLocalization();
builder.Services.AddSingleton<CP6.WebApi.Localization.DbStringLocalizer>();
builder.Services.AddSingleton<Microsoft.Extensions.Localization.IStringLocalizerFactory, CP6.WebApi.Localization.DbStringLocalizerFactory>();
builder.Services.AddSingleton<Microsoft.Extensions.Localization.IStringLocalizer>(
    sp => sp.GetRequiredService<CP6.WebApi.Localization.DbStringLocalizer>());

// 3.2.2 i18n 优化 P4：发布模式（版本化静态包导出 + manifest）。
builder.Services.AddSingleton<CP6.WebApi.Services.LangPublishService>();

// 3.3 操作日志 = Kafka 专任（高吞吐・append-only・可保留可回放的审计流）
//  - 生产者 KafkaProducerService 实现 IOperLogTransport；OperLogFilter 注入单一通道。
//  - Kafka 消费者是唯一落库担当；不可用时 Filter 降级直接写 DB。
builder.Services.AddSingleton<KafkaProducerService>();
builder.Services.AddSingleton<IOperLogTransport>(sp => sp.GetRequiredService<KafkaProducerService>());
builder.Services.AddHostedService<CP6.WebApi.BackgroundServices.KafkaOperLogConsumer>();

// 操作日志保留期清理（默认 7 天，OperLog:RetentionDays 可配置）
builder.Services.AddHostedService<CP6.WebApi.BackgroundServices.OperLogCleanupService>();

// 3.4 RabbitMQ = 业务事件通知/告警 专任（低频・确实配信・可路由可重试）
//  - RabbitMQService 实现 INotificationPublisher（出荷完了・棚卸差異 等业务事件）。
//  - NotificationConsumer 消费 cp6.notification → SignalR(NotifyHub) fanout，可扩展邮件/Webhook。
builder.Services.AddSingleton<RabbitMQService>();
builder.Services.AddSingleton<INotificationPublisher>(sp => sp.GetRequiredService<RabbitMQService>());
builder.Services.AddHostedService<CP6.WebApi.BackgroundServices.NotificationConsumer>();

// 4. 注册仓储和服务（依赖注入）
builder.Services.AddScoped(typeof(IRepository<>), typeof(RepositoryBase<>));

// 4.0 PUB 章00 组织模型
builder.Services.AddScoped<CP6.Core.Services.Sys.IDeptService, CP6.Core.Services.Sys.DeptService>();

// 4.0a OA(Wf) 阶段1 运行时
builder.Services.AddScoped<CP6.Core.Services.Wf.IApproverResolver, CP6.Core.Services.Wf.ApproverResolver>(); // 章01 审批人解析（消费 PUB 组织）
builder.Services.AddScoped<CP6.Core.Services.Wf.IFormService, CP6.Core.Services.Wf.FormService>();           // 章02 表单引擎（JSON 列 + 服务端 schema 复核）
builder.Services.AddScoped<CP6.Core.Services.Wf.IFlowEngine, CP6.Core.Services.Wf.FlowEngine>();             // 章03 流程引擎状态机（会签/条件/幂等）
builder.Services.AddScoped<CP6.Core.Services.Wf.IFlowDefService, CP6.Core.Services.Wf.FlowDefService>();     // 章03/04 流程定义 + 实例详情查询
builder.Services.AddScoped<CP6.Core.Services.Wf.IWfNotifier, CP6.WebApi.Services.SignalRWfNotifier>();        // 章04 待办 SignalR 推送（注入给 FlowEngine）
builder.Services.AddScoped<CP6.Core.Services.Wf.ITaskCenterService, CP6.Core.Services.Wf.TaskCenterService>(); // 章04 待办中心（待办/我的申请/撤回）

// 4.0b OA(Wf) 阶段2 集成（章05 ★）：业务接入 OA 的同步回调
builder.Services.AddScoped<CP6.Core.Services.Wf.IApprovalService, CP6.Core.Services.Wf.ApprovalService>();      // 章05 §2 业务侧入口（按绑定起流程/防重/状态）
builder.Services.AddScoped<CP6.Core.Services.Wf.ApprovalDispatcher>();                                          // 章05 §4 终态分发（注入所有 IApprovalCallback）
builder.Services.AddScoped<CP6.Core.Services.Wf.IApprovalCallback, CP6.Core.Services.Fin.JournalApprovalCallback>(); // 章05 §7 财务凭证示范回调（兑现 MVP）

// 4.0c OA(Wf) 阶段3 高级流程（章07）：超时扫描 + Worker（退回/加签/委派为 FlowEngine 自带方法）
builder.Services.AddScoped<CP6.Core.Services.Wf.IWfTimeoutService, CP6.Core.Services.Wf.WfTimeoutService>();    // 章07 §4 超时扫描（remind/approve/reject/escalate）
builder.Services.AddHostedService<CP6.WebApi.BackgroundServices.WfTimeoutScanWorker>();                         // 章07 §4 超时扫描 Worker（周期扫到期待办，v1 单实例）

// 4.0.2 财务（Fin）章01 总账内核
builder.Services.AddScoped<CP6.Core.Services.Fin.IGlAccountService, CP6.Core.Services.Fin.GlAccountService>(); // 章01 §3 会计科目 + 多国别模板包
builder.Services.AddScoped<CP6.Core.Services.Fin.IFinSequenceService, CP6.Core.Services.Fin.FinSequenceService>(); // 章01 §4 凭证采番（GL-yyyy-MM-NNNNN）
builder.Services.AddScoped<CP6.Core.Services.Fin.IFiscalPeriodService, CP6.Core.Services.Fin.FiscalPeriodService>(); // 章02 §1 会计期间（财年起始月可配）
builder.Services.AddScoped<CP6.Core.Services.Fin.IJournalEntryService, CP6.Core.Services.Fin.JournalEntryService>(); // 章01 §5/§6 凭证：借贷恒等+maker-checker+红冲
builder.Services.AddScoped<CP6.Core.Services.Fin.ITrialBalanceService, CP6.Core.Services.Fin.TrialBalanceService>(); // 章02 §2 三栏试算平衡表
builder.Services.AddScoped<CP6.Core.Services.Fin.IBalanceSheetService, CP6.Core.Services.Fin.BalanceSheetService>(); // 章08 §2 资产负债表（期末余额重组,复用试算表）
builder.Services.AddScoped<CP6.Core.Services.Fin.IIncomeStatementService, CP6.Core.Services.Fin.IncomeStatementService>(); // 章08 §3 损益表（本期发生）
builder.Services.AddScoped<CP6.Core.Services.Fin.IFxRevaluationService, CP6.Core.Services.Fin.FxRevaluationService>(); // 章07 §4 期末未实现汇兑重估（结账前跑+下期初冲回）
builder.Services.AddScoped<CP6.Core.Services.Fin.IPeriodCloseService, CP6.Core.Services.Fin.PeriodCloseService>(); // 章02 §3 月结/锁期/反结账（结账前调用汇兑重估）
builder.Services.AddScoped<CP6.Core.Services.Fin.IAutoVoucherEngine, CP6.Core.Services.Fin.AutoVoucherEngine>(); // 章05 自动凭证引擎（规则即数据→AutoPost）
builder.Services.AddScoped<CP6.Core.Services.Fin.IApInvoiceService, CP6.Core.Services.Fin.ApInvoiceService>(); // 章03 §3① 应付发票录入+过账
builder.Services.AddScoped<CP6.Core.Services.Fin.IPaymentService, CP6.Core.Services.Fin.PaymentService>(); // 章03 §3② 付款+预付+撤销
builder.Services.AddScoped<CP6.Core.Services.Fin.IApSettlementService, CP6.Core.Services.Fin.ApSettlementService>(); // 章03 §3③/§4 核销+尾差+汇差
builder.Services.AddScoped<CP6.Core.Services.Fin.IApReconcileService, CP6.Core.Services.Fin.ApReconcileService>(); // 章03 §4 子账↔GL 勾稽
builder.Services.AddScoped<CP6.Core.Services.Fin.IApAgingService, CP6.Core.Services.Fin.ApAgingService>(); // 章03 §5 应付账龄
builder.Services.AddScoped<CP6.Core.Services.Fin.IApMasterService, CP6.Core.Services.Fin.ApMasterService>(); // 章03 银行账户/税码主数据
builder.Services.AddScoped<CP6.Core.Services.Fin.IArInvoiceService, CP6.Core.Services.Fin.ArInvoiceService>(); // 章04 §2 应收发票+出货自动开票+双凭证+销售红字
builder.Services.AddScoped<CP6.Core.Services.Fin.IReceiptService, CP6.Core.Services.Fin.ReceiptService>(); // 章04 §3 收款+预收+撤销
builder.Services.AddScoped<CP6.Core.Services.Fin.IArSettlementService, CP6.Core.Services.Fin.ArSettlementService>(); // 章04 §3 应收核销+尾差+汇差
builder.Services.AddScoped<CP6.Core.Services.Fin.IArReconcileService, CP6.Core.Services.Fin.ArReconcileService>(); // 章04 §3 子账↔GL 勾稽
builder.Services.AddScoped<CP6.Core.Services.Fin.ICostCollectService, CP6.Core.Services.Fin.CostCollectService>(); // 章06 成本归集（料吃MES真实消耗×BOM单价+工费标准估算）
builder.Services.AddScoped<CP6.Core.Services.Fin.ICostSettleService, CP6.Core.Services.Fin.CostSettleService>(); // 章06 完工结转（料工费→WIP→FG凭证+FG单位成本）
builder.Services.AddScoped<CP6.Core.Services.Fin.IFinReconciliationService, CP6.Core.Services.Fin.FinReconciliationService>(); // 章10 §5 每日对账（AP/AR子账↔GL+试算平衡）
builder.Services.AddScoped<CP6.Core.Services.Fin.IArAgingService, CP6.Core.Services.Fin.ArAgingService>(); // 章04 §3 应收账龄
builder.Services.AddScoped<CP6.Core.Services.Fin.ICreditControlService, CP6.Core.Services.Fin.CreditControlService>(); // 章04 §3 信用控制（出货前反向约束）
builder.Services.AddScoped<CP6.Core.Services.Integration.IFinBridgeHook, CP6.Core.Services.Fin.FinBridgeHook>(); // F2-D4 出货→AR 自动开票/红冲（Phase6 桥，WMS|FIN 路由）
builder.Services.AddScoped<CP6.Core.Services.Fin.IFinAp>(sp => (CP6.Core.Services.Fin.IFinAp)sp.GetRequiredService<CP6.Core.Services.Fin.IApInvoiceService>()); // F2-D3 采购对外契约（同一 ApInvoiceService 实例）

// 4.0.3 采购（Pur）MVP 章01~04：主数据→PO→收货→三单匹配→自动建应付（补全财务 AP 前置）
builder.Services.AddScoped<CP6.Core.Services.Pur.ISupplierPriceService, CP6.Core.Services.Pur.SupplierPriceService>(); // 章01 §3/§4 采购价表 + 阶梯带价
builder.Services.AddScoped<CP6.Core.Services.Pur.Contracts.IApprovalService, CP6.Core.Services.Pur.Contracts.StubApprovalService>(); // P-D1 审批委托（桩；OA 引擎接真实流程后换适配器）
builder.Services.AddScoped<CP6.Core.Services.Pur.IPurchaseOrderService, CP6.Core.Services.Pur.PurchaseOrderService>(); // 章02 PO 建单带出 + 派生状态机 + 送审
builder.Services.AddScoped<CP6.Core.Services.Pur.Contracts.IWmsReceiveService, CP6.Core.Services.Pur.Contracts.StubWmsReceiveService>(); // P-D1 WMS 入库委托（桩；WMS 落地后换适配器）
builder.Services.AddScoped<CP6.Core.Services.Pur.Contracts.IWmsQcQuery, CP6.Core.Services.Pur.Contracts.StubWmsQcQuery>(); // P-D1 WMS 检收查询（桩=全合格）
builder.Services.AddScoped<CP6.Core.Services.Pur.IGoodsReceiptService, CP6.Core.Services.Pur.GoodsReceiptService>(); // 章03 双基准收货 + 委托入库 + 回写三累计锚
builder.Services.AddScoped<CP6.Core.Services.Pur.Contracts.IFinApService, CP6.Core.Services.Pur.Contracts.FinApServiceAdapter>(); // 章04 建应付适配器（委托财务 IFinAp，借方按 GL 角色 INVENTORY）
builder.Services.AddScoped<CP6.Core.Services.Pur.IThreeWayMatchService, CP6.Core.Services.Pur.ThreeWayMatchService>(); // 章04 ★三单匹配→自动建应付/挂起

// 4.0.1 PUB 章01 权限引擎地基（多角色聚合 + 请求级上下文缓存）
builder.Services.AddMemoryCache();                 // 权限上下文存活对象缓存（单机；多实例转 Redis）
builder.Services.AddHttpContextAccessor();          // 解析当前请求登录用户
builder.Services.AddScoped<CP6.Core.Services.Common.ITenantContext, CP6.Core.Services.Common.TenantContext>(); // 章10 当前租户上下文（TenantMiddleware 写入，CP6Context 过滤/盖章读取）
builder.Services.AddScoped<CP6.Core.Services.Common.ITenantEnumerator, CP6.Core.Services.Common.TenantEnumerator>(); // 章10 活跃租户枚举（后台 Worker 按租户循环用）
builder.Services.AddScoped<CP6.Core.Services.Sys.IPermissionAggregator, CP6.Core.Services.Sys.PermissionAggregator>();
builder.Services.AddScoped<CP6.Core.Services.Sys.ICurrentPermissionContext, CP6.Core.Services.Sys.CurrentPermissionContext>();
builder.Services.AddScoped<CP6.Core.Services.Sys.IUserRoleService, CP6.Core.Services.Sys.UserRoleService>();
builder.Services.AddScoped<CP6.Core.Services.Sys.IDictService, CP6.Core.Services.Sys.DictService>();   // PUB 章05 字典缓存翻译
builder.Services.AddScoped<CP6.Core.Services.Pub.ISeqService, CP6.Core.Services.Pub.SeqService>();     // PUB 章05 富采番
builder.Services.AddScoped<CP6.Core.Services.Pub.IExcelService, CP6.Core.Services.Pub.ExcelService>(); // PUB 章07 Excel 导入导出
builder.Services.AddSingleton<CP6.Core.Services.Pub.CodeGenService>();                                 // PUB 章08 代码生成（无状态）
// PUB 章06 附件存储（v1 本地盘；Storage:Provider 预留 OSS/MinIO）
builder.Services.AddSingleton<CP6.Core.Services.Pub.IFileStore>(_ =>
{
    var root = builder.Configuration["Storage:LocalRoot"]
        ?? Path.Combine(builder.Environment.ContentRootPath, "App_Data", "uploads");
    return new CP6.Core.Services.Pub.LocalFileStore(root);
});
builder.Services.AddScoped<CP6.Core.Services.Pub.IAttachmentService>(sp =>
{
    var maxMb = builder.Configuration.GetValue<int?>("Attachment:MaxSizeMb") ?? 20;
    var exts = builder.Configuration.GetSection("Attachment:AllowedExt").Get<string[]>();
    return new CP6.Core.Services.Pub.AttachmentService(
        sp.GetRequiredService<CP6.Core.EFDbContext.CP6Context>(),
        sp.GetRequiredService<CP6.Core.Services.Pub.IFileStore>(),
        maxMb, exts);
});
builder.Services.AddScoped<CP6.Core.Services.Sys.IPermissionService, CP6.Core.Services.Sys.PermissionService>();
builder.Services.AddScoped<CP6.Core.Services.Sys.IRolePermService, CP6.Core.Services.Sys.RolePermService>();
builder.Services.AddScoped<CP6.Core.Services.Sys.IDataScopeFilter, CP6.Core.Services.Sys.DataScopeFilter>();
builder.Services.AddScoped<CP6.Core.Services.Sys.IFieldPermService, CP6.Core.Services.Sys.FieldPermService>();
// 章03 数据权限资源注册（业务实体接 IDataScoped 后即生效；范围 1本人/2本部门/3及下级/4自定义/5全部）
CP6.Core.Services.Sys.DataScopeRegistry.Register("order", "受注", new[] { 1, 2, 3, 4, 5 }, 2);
CP6.Core.Services.Sys.DataScopeRegistry.Register("product", "製品", new[] { 1, 2, 3, 4, 5 }, 5);
// 章04 字段权限资源/字段注册（业务返回 DTO 贴 [FieldMask] 后即生效）
CP6.Core.Services.Sys.FieldRegistry.Register("order",
    new CP6.Core.Services.Sys.FieldRegistry.Field("UnitPrice", "単価"),
    new CP6.Core.Services.Sys.FieldRegistry.Field("Amount", "金額"));

// 4.1 MSBBPA010 見積計算書 相关服务
builder.Services.AddScoped<IEstimateCalcService, EstimateCalcService>();
builder.Services.AddScoped<IMasterDataService, MasterDataService>();

// 4.2 MSBBPA030/040 御見積書 相关服务
builder.Services.AddScoped<IQuotationService, QuotationService>();

// 4.3 MSBBPA050/060 Web 製品マスタ 相关服务
builder.Services.AddScoped<IProductService, ProductService>();
// 仕掛チェック：mcframe7 連携無し時は NoOp 実装（Phase 3 で実装差替え）
builder.Services.AddScoped<IWipCheckService, NoOpWipCheckService>();

// 4.4 MSBBPA070/080/090 Web 受注 相关服务
builder.Services.AddScoped<IOrderService, OrderService>();
builder.Services.AddScoped<IUnshippedOrderService, UnshippedOrderService>();
builder.Services.AddScoped<ICreditNoteService, CreditNoteService>();
builder.Services.AddScoped<IBridgeHealthService, BridgeHealthService>();
builder.Services.AddScoped<IOrderTraceService, OrderTraceService>();
builder.Services.AddScoped<IBackorderService, BackorderService>();
builder.Services.AddScoped<IOtdReportService, OtdReportService>();
// Gap 4.3 多通貨 — 為替レート（受注時凍結 + マスタ CRUD）
builder.Services.AddScoped<IFxRateService, FxRateService>();
// PA090 POWER EGG WF 起票：実環境では HTTP 実装に差し替え
builder.Services.AddScoped<IPowerEggWorkflowService, NoOpPowerEggWorkflowService>();

// 4.5 MSBBPA100/110/120 FSC・取引先マスタ
builder.Services.AddScoped<IBusinessPartnerService, BusinessPartnerService>();
builder.Services.AddScoped<IFscChecklistService, FscChecklistService>();

// 4.6 MSBBPA130/140/150 シート単価・版型/木型マスタ
builder.Services.AddScoped<ISheetUnitPriceService, SheetUnitPriceService>();
builder.Services.AddScoped<IPlateMoldService, PlateMoldService>();
// PA140 §8 PE API 版型発注書連携：実環境では HTTP 実装に差し替え
builder.Services.AddScoped<IPlateMoldPeApiService, NoOpPlateMoldPeApiService>();

// 4.7 MSBBME010〜090 MES 製造執行 相关服务
builder.Services.AddScoped<CP6.Core.Services.Mes.IMesSequenceService, CP6.Core.Services.Mes.MesSequenceService>();
builder.Services.AddScoped<CP6.Core.Services.Mes.IWorkOrderService, CP6.Core.Services.Mes.WorkOrderService>();
builder.Services.AddScoped<CP6.Core.Services.Mes.IProductionResultService, CP6.Core.Services.Mes.ProductionResultService>();
builder.Services.AddScoped<CP6.Core.Services.Mes.IQualityInspectionService, CP6.Core.Services.Mes.QualityInspectionService>();
builder.Services.AddScoped<CP6.Core.Services.Mes.IDefectRecordService, CP6.Core.Services.Mes.DefectRecordService>();
builder.Services.AddScoped<CP6.Core.Services.Mes.IPlanningBoardService, CP6.Core.Services.Mes.PlanningBoardService>();
builder.Services.AddScoped<CP6.Core.Services.Mes.IMesDashboardService, CP6.Core.Services.Mes.MesDashboardService>();
builder.Services.AddScoped<CP6.Core.Services.Mes.IMachineService, CP6.Core.Services.Mes.MachineService>();
builder.Services.AddScoped<CP6.Core.Services.Mes.IOeeService, CP6.Core.Services.Mes.OeeService>();
builder.Services.AddScoped<CP6.Core.Services.Mes.MesDashboardDapperService>();
// Gap 3.3 生産計画達成率レポート
builder.Services.AddScoped<CP6.Core.Services.Mes.IPlanAchievementService, CP6.Core.Services.Mes.PlanAchievementService>();

// 4.8 MSBBWM010〜090 WMS 倉庫管理 Phase 1 コア
builder.Services.AddScoped<CP6.Core.Services.Wms.IWmsSequenceService, CP6.Core.Services.Wms.WmsSequenceService>();
builder.Services.AddScoped<CP6.Core.Services.Wms.IStockMovementService, CP6.Core.Services.Wms.StockMovementService>();
builder.Services.AddScoped<CP6.Core.Services.Wms.IStockQcService, CP6.Core.Services.Wms.StockQcService>();
builder.Services.AddScoped<CP6.Core.Services.Wms.IStockDwellService, CP6.Core.Services.Wms.StockDwellService>();
builder.Services.AddScoped<CP6.Core.Services.Wms.IMaterialShortageService, CP6.Core.Services.Wms.MaterialShortageService>();
builder.Services.AddScoped<CP6.Core.Services.Wms.IMaterialShortageNotifier, CP6.Core.Services.Wms.MaterialShortageNotifier>();

// 4.9 MSBBWM030/040 WMS Phase 2 入庫
builder.Services.AddScoped<CP6.Core.Services.Wms.IInboundService, CP6.Core.Services.Wms.InboundService>();

// 4.10 MSBBWM050/070/080 WMS Phase 3 出庫・出荷
builder.Services.AddScoped<CP6.Core.Services.Wms.IOutboundService, CP6.Core.Services.Wms.OutboundService>();

// 4.10.1 Gap 4.2 / T14 — 多倉庫ルーティング（出庫引当の倉庫候補解決）
// appsettings.json の OutboundRouting:Enabled で切替（既定 false＝従来の単一倉庫引当を維持）。
// 無効時は NoOp 実装でヘッダ倉庫のみを返すため、既存挙動・テストに影響なし。
var outboundRoutingEnabled = builder.Configuration.GetValue<bool?>("OutboundRouting:Enabled") ?? false;
if (outboundRoutingEnabled)
{
    builder.Services.AddScoped<CP6.Core.Services.Wms.IOutboundRoutingService, CP6.Core.Services.Wms.OutboundRoutingService>();
}
else
{
    builder.Services.AddScoped<CP6.Core.Services.Wms.IOutboundRoutingService, CP6.Core.Services.Wms.NoOpOutboundRoutingService>();
}

// 4.11 MSBBWM090 + WM-DASH WMS Phase 4 棚卸・ダッシュボード
builder.Services.AddScoped<CP6.Core.Services.Wms.IStockTakeService, CP6.Core.Services.Wms.StockTakeService>();
builder.Services.AddScoped<CP6.Core.Services.Wms.IWmsDashboardService, CP6.Core.Services.Wms.WmsDashboardService>();

// 4.13 MSBBWM100/150/170 WMS Phase 5 拡張（QC検品 + RMA + FEFO期限）
builder.Services.AddScoped<CP6.Core.Services.Wms.IExpiryService, CP6.Core.Services.Wms.ExpiryService>();
builder.Services.AddScoped<CP6.Core.Services.Wms.IQcInspectionService, CP6.Core.Services.Wms.QcInspectionService>();
builder.Services.AddScoped<CP6.Core.Services.Wms.IRmaService, CP6.Core.Services.Wms.RmaService>();

// 4.14 WMS SignalR 通知（依存逆転 — Core は IWmsNotifier のみ依存、SignalR 実装は WebApi 層）
builder.Services.AddScoped<CP6.Core.Services.Wms.IWmsNotifier, CP6.WebApi.Services.SignalRWmsNotifier>();

// 4.15 MSBBWM160 ロット追溯（純クエリ、新規テーブルなし）
builder.Services.AddScoped<CP6.Core.Services.Wms.ILotTraceService, CP6.Core.Services.Wms.LotTraceService>();

// 4.16 MSBBWM140 キッティング・組立
builder.Services.AddScoped<CP6.Core.Services.Wms.IKittingService, CP6.Core.Services.Wms.KittingService>();

// 4.17 MSBBWM110/120/130 Logistics（スロッティング + 補充 + クロスドック）
builder.Services.AddScoped<CP6.Core.Services.Wms.ICrossDockService, CP6.Core.Services.Wms.CrossDockService>();
builder.Services.AddScoped<CP6.Core.Services.Wms.IReplenishService, CP6.Core.Services.Wms.ReplenishService>();
builder.Services.AddScoped<CP6.Core.Services.Wms.ISlottingService, CP6.Core.Services.Wms.SlottingService>();

// 4.18 MSBBWM200/230/240/250 紙器業特化（原紙ロール + インキ + パレット + VMI）
builder.Services.AddScoped<CP6.Core.Services.Wms.IPaperRollService, CP6.Core.Services.Wms.PaperRollService>();
builder.Services.AddScoped<CP6.Core.Services.Wms.IInkService, CP6.Core.Services.Wms.InkService>();
builder.Services.AddScoped<CP6.Core.Services.Wms.IPalletService, CP6.Core.Services.Wms.PalletService>();
builder.Services.AddScoped<CP6.Core.Services.Wms.IVmiService, CP6.Core.Services.Wms.VmiService>();

// 4.19 MSBBWM210/220/260 紙器業特化 第2弾（残材 + 印版・木型 + サンプル）
builder.Services.AddScoped<CP6.Core.Services.Wms.IRemnantService, CP6.Core.Services.Wms.RemnantService>();
builder.Services.AddScoped<CP6.Core.Services.Wms.IPlateMoldService, CP6.Core.Services.Wms.PlateMoldService>();
builder.Services.AddScoped<CP6.Core.Services.Wms.ISampleStockService, CP6.Core.Services.Wms.SampleStockService>();

// 4.20 MSBBWM900 帳票センター
builder.Services.AddScoped<CP6.Core.Services.Wms.IReportCenterService, CP6.Core.Services.Wms.ReportCenterService>();

// 4.21 MSBBWM310/320/330 連携・モバイル・IoT
builder.Services.AddScoped<CP6.Core.Services.Wms.IWcsService, CP6.Core.Services.Wms.WcsService>();
builder.Services.AddScoped<CP6.Core.Services.Wms.ICarrierService, CP6.Core.Services.Wms.CarrierService>();
builder.Services.AddScoped<CP6.Core.Services.Wms.IIotService, CP6.Core.Services.Wms.IotService>();

// 4.22 MSBBWM300 モバイル作業指示（RFハンディ）
builder.Services.AddScoped<CP6.Core.Services.Wms.IMobileService, CP6.Core.Services.Wms.MobileService>();

// 4.12 WM-3.5 WMS 自動展開フック（MES IssueAsync / PA CreateAsync 後に自動発火）
// appsettings.json の WmsBridge:Enabled で切替（既定 true）。false の場合は no-op に置換。
var wmsBridgeEnabled = builder.Configuration.GetValue<bool?>("WmsBridge:Enabled") ?? true;
if (wmsBridgeEnabled)
{
    builder.Services.AddScoped<CP6.Core.Services.Integration.IWmsBridgeHook, CP6.Core.Services.Wms.WmsBridgeHook>();
}
else
{
    builder.Services.AddScoped<CP6.Core.Services.Integration.IWmsBridgeHook, CP6.Core.Services.Integration.NoOpWmsBridgeHook>();
}

// 4.13 WMS→ERP 逆方向フック（OutboundService.ShipAsync 後に受注へ出荷実績を回写）
// appsettings.json の ErpBridge:Enabled で切替（既定 true）。false の場合は no-op に置換。
var erpBridgeEnabled = builder.Configuration.GetValue<bool?>("ErpBridge:Enabled") ?? true;
if (erpBridgeEnabled)
{
    builder.Services.AddScoped<CP6.Core.Services.Integration.IErpBridgeHook, CP6.Core.Services.Wms.ErpBridgeHook>();
}
else
{
    builder.Services.AddScoped<CP6.Core.Services.Integration.IErpBridgeHook, CP6.Core.Services.Integration.NoOpErpBridgeHook>();
}

// 4.14 ERP→MES 前方向フック（OrderService.CreateAsync 後に受注を製造指図へ自動展開）
// appsettings.json の MesBridge:Enabled で切替（既定 false＝手動展開を既定動作として維持）。
var mesBridgeEnabled = builder.Configuration.GetValue<bool?>("MesBridge:Enabled") ?? false;
if (mesBridgeEnabled)
{
    builder.Services.AddScoped<CP6.Core.Services.Integration.IMesBridgeHook, CP6.Core.Services.Mes.MesBridgeHook>();
}
else
{
    builder.Services.AddScoped<CP6.Core.Services.Integration.IMesBridgeHook, CP6.Core.Services.Integration.NoOpMesBridgeHook>();
}

// MES 実時間通知（SignalR 実装）
builder.Services.AddScoped<CP6.Core.Services.Mes.IMesNotifier, CP6.WebApi.Services.SignalRMesNotifier>();

// MES BackgroundService（多線程）
builder.Services.AddHostedService<CP6.WebApi.BackgroundServices.OeeCalculationService>();
builder.Services.AddHostedService<CP6.WebApi.BackgroundServices.MachineStatusMonitor>();

// ─────────────────────────────────────────────────────────────
// 4.15 Phase 6 — 受注取消連動 + IntegrationEvent 持久化 + 自動リトライ
// ─────────────────────────────────────────────────────────────

// 4.15.1 受注取消 Bridge Hook（OrderService.CancelAsync の反向級联）
// appsettings.json の OrderCancelBridge:Enabled で切替（既定 true）。false の場合は no-op に置換。
var orderCancelBridgeEnabled = builder.Configuration.GetValue<bool?>("OrderCancelBridge:Enabled") ?? true;
if (orderCancelBridgeEnabled)
{
    builder.Services.AddScoped<CP6.Core.Services.Integration.IOrderCancelBridgeHook, CP6.Core.Services.Integration.OrderCancelBridgeHook>();
}
else
{
    builder.Services.AddScoped<CP6.Core.Services.Integration.IOrderCancelBridgeHook, CP6.Core.Services.Integration.NoOpOrderCancelBridgeHook>();
}

// 4.15.2 IntegrationEvent 配置（appsettings.json の IntegrationEvent 段から）
builder.Services.Configure<CP6.Core.Options.IntegrationEventOptions>(
    builder.Configuration.GetSection("IntegrationEvent"));

// 4.15.3 IntegrationEvent Dispatcher（HookName → 元 hook 経路）
builder.Services.AddScoped<CP6.Core.Services.Integration.IIntegrationEventDispatcher, CP6.Core.Services.Integration.IntegrationEventDispatcher>();

// 4.15.4 DeadLetter 通知（SignalR + Sys_OperLog 双通知）
builder.Services.AddScoped<CP6.Core.Services.Integration.IDeadLetterNotifier, CP6.Core.Services.Integration.DeadLetterNotifier>();

// 4.15.5 Retry Worker — 60s ごとに Failed + NextRetryAt 到期 のイベントをリトライ
builder.Services.AddHostedService<CP6.WebApi.BackgroundServices.IntegrationEventRetryWorker>();

// 财务每日对账 worker（章10 §5）：每日跑 AP/AR 子账↔GL + 试算平衡勾稽，不一致告警
builder.Services.AddHostedService<CP6.WebApi.BackgroundServices.FinReconciliationWorker>();

// 4.15.6 T15 / Gap 2.3 — Prometheus /metrics（ブリッジ業務指標）
//  - Snapshot Provider は T_IntegrationEvent を scrape 毎に集計（DB が単一の真実・再起動で値が消えない）。
//  - Collector は prometheus-net BeforeCollect への薄いアダプタ（Singleton）。
builder.Services.AddScoped<CP6.Core.Services.Integration.IBridgeMetricsSnapshotProvider, CP6.Core.Services.Integration.BridgeMetricsSnapshotProvider>();
builder.Services.AddSingleton<CP6.WebApi.Observability.BridgeMetricsCollector>();

// 5. 配置 JWT 认证
var jwt = builder.Configuration.GetSection("JWT");
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwt["Issuer"],
            ValidAudience = jwt["Audience"],
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt["Secret"]!))
        };
    });
builder.Services.AddAuthorization();

// 6. 注册 CORS（SignalR 需要 AllowCredentials + 指定 Origin）
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
        policy.SetIsOriginAllowed(_ => true)  // 允许所有来源
              .AllowAnyMethod()
              .AllowAnyHeader()
              .AllowCredentials());  // SignalR WebSocket 需要
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// 7. 初始化种子数据（首次启动时自动创建）
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<CP6Context>();

    // Docker 环境下自动创建数据库并应用所有迁移
    db.Database.Migrate();

    // 章10 默认租户种子（幂等，按 Id 判存）——登记进 Sys_Tenant 注册表供枚举/登录/管理用
    CP6.Core.Services.Common.TenantSeed.EnsureSeeded(db);

    // 章05 自动凭证记账规则种子（幂等，按 EventType 判存；只引用 Role 锚点，与 COA 模板包解耦）
    CP6.Core.Services.Fin.PostingRuleSeed.EnsureSeeded(db);

    if (!db.Sys_Menus.Any())
    {
        // 菜单ID和角色ID都是自定义的，手动指定
        db.Sys_Menus.AddRange(
            new Sys_Menu { MenuId = 2, MenuName = "仪表盘", RoutePath = "/dashboard", Icon = "Odometer", OrderNo = 0, Enable = true },
            new Sys_Menu { MenuId = 100, MenuName = "系统管理", Icon = "Setting", OrderNo = 100, Enable = true },
            new Sys_Menu { MenuId = 101, MenuName = "角色管理", RoutePath = "/role", Icon = "UserFilled", ParentId = 100, OrderNo = 101, Enable = true },
            new Sys_Menu { MenuId = 102, MenuName = "菜单管理", RoutePath = "/menu", Icon = "Menu", ParentId = 100, OrderNo = 102, Enable = true },
            new Sys_Menu { MenuId = 103, MenuName = "权限分配", RoutePath = "/permission", Icon = "Lock", ParentId = 100, OrderNo = 103, Enable = true },
            new Sys_Menu { MenuId = 104, MenuName = "用户管理", RoutePath = "/user", Icon = "User", ParentId = 100, OrderNo = 104, Enable = true },
            new Sys_Menu { MenuId = 105, MenuName = "多语言管理", RoutePath = "/lang", Icon = "ChatLineSquare", ParentId = 100, OrderNo = 105, Enable = true },
            new Sys_Menu { MenuId = 106, MenuName = "数据字典", RoutePath = "/dict", Icon = "Collection", ParentId = 100, OrderNo = 106, Enable = true },
            new Sys_Menu { MenuId = 107, MenuName = "操作日志", RoutePath = "/operlog", Icon = "Notebook", ParentId = 100, OrderNo = 107, Enable = true },
            new Sys_Menu { MenuId = 200, MenuName = "販売管理(ERP)", Icon = "ShoppingBag", OrderNo = 200, Enable = true },
            new Sys_Menu { MenuId = 201, MenuName = "見積計算書 照会", RoutePath = "/estimate-calc-list", Icon = "List", ParentId = 200, OrderNo = 201, Enable = true },
            new Sys_Menu { MenuId = 202, MenuName = "見積計算書 登録", RoutePath = "/estimate-calc", Icon = "Money", ParentId = 200, OrderNo = 202, Enable = true },
            new Sys_Menu { MenuId = 203, MenuName = "御見積書 一覧", RoutePath = "/quotation-list", Icon = "Tickets", ParentId = 200, OrderNo = 203, Enable = true },
            new Sys_Menu { MenuId = 204, MenuName = "御見積書 登録", RoutePath = "/quotation", Icon = "EditPen", ParentId = 200, OrderNo = 204, Enable = true },
            new Sys_Menu { MenuId = 205, MenuName = "製品マスタ 一覧", RoutePath = "/product-list", Icon = "Goods", ParentId = 200, OrderNo = 205, Enable = true },
            new Sys_Menu { MenuId = 206, MenuName = "製品マスタ 登録", RoutePath = "/product", Icon = "Box", ParentId = 200, OrderNo = 206, Enable = true },
            new Sys_Menu { MenuId = 207, MenuName = "受注一覧照会", RoutePath = "/order-list", Icon = "Files", ParentId = 200, OrderNo = 207, Enable = true },
            new Sys_Menu { MenuId = 208, MenuName = "受注入力", RoutePath = "/order", Icon = "DocumentAdd", ParentId = 200, OrderNo = 208, Enable = true },
            new Sys_Menu { MenuId = 209, MenuName = "単価訂正", RoutePath = "/order-price-correction", Icon = "PriceTag", ParentId = 200, OrderNo = 209, Enable = true },
            new Sys_Menu { MenuId = 210, MenuName = "FSC チェックシート", RoutePath = "/fsc-checklist", Icon = "Document", ParentId = 200, OrderNo = 210, Enable = true },
            new Sys_Menu { MenuId = 211, MenuName = "取引先マスタ 一覧", RoutePath = "/business-partner-list", Icon = "OfficeBuilding", ParentId = 200, OrderNo = 211, Enable = true },
            new Sys_Menu { MenuId = 212, MenuName = "取引先マスタ 登録", RoutePath = "/business-partner", Icon = "User", ParentId = 200, OrderNo = 212, Enable = true },
            new Sys_Menu { MenuId = 213, MenuName = "シート単価マスタ", RoutePath = "/sheet-unit-price", Icon = "Coin", ParentId = 200, OrderNo = 213, Enable = true },
            new Sys_Menu { MenuId = 214, MenuName = "版型/木型 一覧", RoutePath = "/plate-mold-list", Icon = "Tools", ParentId = 200, OrderNo = 214, Enable = true },
            new Sys_Menu { MenuId = 215, MenuName = "版型/木型 登録", RoutePath = "/plate-mold", Icon = "Stamp", ParentId = 200, OrderNo = 215, Enable = true }
        );

        // 管理员角色 RoleId = 1
        db.Sys_Roles.Add(new Sys_Role { RoleId = 1, RoleName = "管理员", Description = "拥有全部权限", Enable = true, OrderNo = 0 });

        // 给管理员角色分配所有菜单
        db.Sys_RoleMenus.AddRange(
            new Sys_RoleMenu { RoleId = 1, MenuId = 2 },
            new Sys_RoleMenu { RoleId = 1, MenuId = 100 },
            new Sys_RoleMenu { RoleId = 1, MenuId = 101 },
            new Sys_RoleMenu { RoleId = 1, MenuId = 102 },
            new Sys_RoleMenu { RoleId = 1, MenuId = 103 },
            new Sys_RoleMenu { RoleId = 1, MenuId = 104 },
            new Sys_RoleMenu { RoleId = 1, MenuId = 105 },
            new Sys_RoleMenu { RoleId = 1, MenuId = 106 },
            new Sys_RoleMenu { RoleId = 1, MenuId = 107 },
            new Sys_RoleMenu { RoleId = 1, MenuId = 200 },
            new Sys_RoleMenu { RoleId = 1, MenuId = 201 },
            new Sys_RoleMenu { RoleId = 1, MenuId = 202 },
            new Sys_RoleMenu { RoleId = 1, MenuId = 203 },
            new Sys_RoleMenu { RoleId = 1, MenuId = 204 },
            new Sys_RoleMenu { RoleId = 1, MenuId = 205 },
            new Sys_RoleMenu { RoleId = 1, MenuId = 206 },
            new Sys_RoleMenu { RoleId = 1, MenuId = 207 },
            new Sys_RoleMenu { RoleId = 1, MenuId = 208 },
            new Sys_RoleMenu { RoleId = 1, MenuId = 209 },
            new Sys_RoleMenu { RoleId = 1, MenuId = 210 },
            new Sys_RoleMenu { RoleId = 1, MenuId = 211 },
            new Sys_RoleMenu { RoleId = 1, MenuId = 212 },
            new Sys_RoleMenu { RoleId = 1, MenuId = 213 },
            new Sys_RoleMenu { RoleId = 1, MenuId = 214 },
            new Sys_RoleMenu { RoleId = 1, MenuId = 215 }
        );

        // 管理员账号绑定 RoleId = 1
        if (!db.Sys_Users.Any())
        {
            db.Sys_Users.Add(new Sys_User
            {
                UserName = "admin",
                Password = "123456",
                NickName = "管理员",
                RoleId = 1,
                Enable = true,
                CreateDate = DateTime.Now
            });
        }

        db.SaveChanges();
    }
    // 补充：如果已有菜单数据但缺少用户管理菜单，追加插入
    if (!db.Sys_Menus.Any(m => m.MenuId == 107))
    {
        db.Sys_Menus.Add(new Sys_Menu { MenuId = 107, MenuName = "操作日志", RoutePath = "/operlog", Icon = "Notebook", ParentId = 100, OrderNo = 107, Enable = true });
        db.Sys_RoleMenus.Add(new Sys_RoleMenu { RoleId = 1, MenuId = 107 });
        db.SaveChanges();
    }
    // PUB 章00 组织模型：部门管理菜单
    if (!db.Sys_Menus.Any(m => m.MenuId == 108))
    {
        db.Sys_Menus.Add(new Sys_Menu { MenuId = 108, MenuName = "部门管理", RoutePath = "/pub/dept", Icon = "OfficeBuilding", ParentId = 100, OrderNo = 108, Enable = true });
        db.Sys_RoleMenus.Add(new Sys_RoleMenu { RoleId = 1, MenuId = 108 });
        db.SaveChanges();
    }
    // PUB 章02 功能权限：角色功能权限菜单
    if (!db.Sys_Menus.Any(m => m.MenuId == 109))
    {
        db.Sys_Menus.Add(new Sys_Menu { MenuId = 109, MenuName = "功能权限", RoutePath = "/pub/role-perm", Icon = "Key", ParentId = 100, OrderNo = 109, Enable = true });
        db.Sys_RoleMenus.Add(new Sys_RoleMenu { RoleId = 1, MenuId = 109 });
        db.SaveChanges();
    }
    // PUB 章03 数据权限：数据权限菜单
    if (!db.Sys_Menus.Any(m => m.MenuId == 110))
    {
        db.Sys_Menus.Add(new Sys_Menu { MenuId = 110, MenuName = "数据权限", RoutePath = "/pub/data-scope", Icon = "Files", ParentId = 100, OrderNo = 110, Enable = true });
        db.Sys_RoleMenus.Add(new Sys_RoleMenu { RoleId = 1, MenuId = 110 });
        db.SaveChanges();
    }
    // PUB 章04 字段权限：字段权限菜单
    if (!db.Sys_Menus.Any(m => m.MenuId == 111))
    {
        db.Sys_Menus.Add(new Sys_Menu { MenuId = 111, MenuName = "字段权限", RoutePath = "/pub/field-perm", Icon = "Lock", ParentId = 100, OrderNo = 111, Enable = true });
        db.Sys_RoleMenus.Add(new Sys_RoleMenu { RoleId = 1, MenuId = 111 });
        db.SaveChanges();
    }
    // PUB 章05 公共模组：采番规则菜单
    if (!db.Sys_Menus.Any(m => m.MenuId == 112))
    {
        db.Sys_Menus.Add(new Sys_Menu { MenuId = 112, MenuName = "采番规则", RoutePath = "/pub/seq", Icon = "Ticket", ParentId = 100, OrderNo = 112, Enable = true });
        db.Sys_RoleMenus.Add(new Sys_RoleMenu { RoleId = 1, MenuId = 112 });
        db.SaveChanges();
    }
    // PUB 章08 代码生成菜单
    if (!db.Sys_Menus.Any(m => m.MenuId == 113))
    {
        db.Sys_Menus.Add(new Sys_Menu { MenuId = 113, MenuName = "代码生成", RoutePath = "/pub/codegen", Icon = "MagicStick", ParentId = 100, OrderNo = 113, Enable = true });
        db.Sys_RoleMenus.Add(new Sys_RoleMenu { RoleId = 1, MenuId = 113 });
        db.SaveChanges();
    }
    // PUB 章02 资源键回填（B1-D2）：给现有菜单按 RoutePath 派生稳定 MenuKey（权限资源键前缀）
    var menusNoKey = db.Sys_Menus.Where(m => m.MenuKey == null && m.RoutePath != null).ToList();
    if (menusNoKey.Count > 0)
    {
        foreach (var m in menusNoKey)
            m.MenuKey = m.RoutePath!.Trim('/').Replace('/', '-');   // "/pub/dept" → "pub-dept"
        db.SaveChanges();
    }
    if (!db.Sys_Menus.Any(m => m.MenuId == 106))
    {
        db.Sys_Menus.Add(new Sys_Menu { MenuId = 106, MenuName = "数据字典", RoutePath = "/dict", Icon = "Collection", ParentId = 100, OrderNo = 106, Enable = true });
        db.Sys_RoleMenus.Add(new Sys_RoleMenu { RoleId = 1, MenuId = 106 });
        db.SaveChanges();
    }
    if (!db.Sys_Menus.Any(m => m.MenuId == 105))
    {
        db.Sys_Menus.Add(new Sys_Menu { MenuId = 105, MenuName = "多语言管理", RoutePath = "/lang", Icon = "ChatLineSquare", ParentId = 100, OrderNo = 105, Enable = true });
        db.Sys_RoleMenus.Add(new Sys_RoleMenu { RoleId = 1, MenuId = 105 });
        db.SaveChanges();
    }
    // i18n 优化 ③：激活 lang 管理页功能权限（首次启用 action 权限子系统）。
    // ActionKeys 计算过滤 MenuKey==null，故菜单105必须有 MenuKey（既有库无 key 需补）。幂等。
    {
        var langMenu = db.Sys_Menus.FirstOrDefault(m => m.MenuId == 105);
        if (langMenu != null && string.IsNullOrEmpty(langMenu.MenuKey))
        {
            langMenu.MenuKey = "lang";
            db.SaveChanges();
        }
        var langActions = new[]
        {
            new { Code = "update", Name = "编辑" },
            new { Code = "delete", Name = "删除" },
            new { Code = "review", Name = "审校" },
            new { Code = "publish", Name = "发布" },
        };
        foreach (var a in langActions)
        {
            if (!db.Sys_MenuActions.Any(x => x.MenuId == 105 && x.ActionCode == a.Code))
                db.Sys_MenuActions.Add(new Sys_MenuAction { MenuId = 105, ActionCode = a.Code, ActionName = a.Name, Sort = 0 });
            if (!db.Sys_RoleActions.Any(x => x.RoleId == 1 && x.MenuId == 105 && x.ActionCode == a.Code))
                db.Sys_RoleActions.Add(new Sys_RoleAction { RoleId = 1, MenuId = 105, ActionCode = a.Code });
        }
        db.SaveChanges();
    }
    if (!db.Sys_Menus.Any(m => m.MenuId == 104))
    {
        db.Sys_Menus.Add(new Sys_Menu { MenuId = 104, MenuName = "用户管理", RoutePath = "/user", Icon = "User", ParentId = 100, OrderNo = 104, Enable = true });
        db.Sys_RoleMenus.Add(new Sys_RoleMenu { RoleId = 1, MenuId = 104 });
        db.SaveChanges();
    }
    // MSBBPA010 販売管理 菜单（父）+ 見積計算書 照会/登録（子）
    if (!db.Sys_Menus.Any(m => m.MenuId == 200))
    {
        db.Sys_Menus.Add(new Sys_Menu { MenuId = 200, MenuName = "販売管理(ERP)", Icon = "ShoppingBag", OrderNo = 200, Enable = true });
        db.Sys_RoleMenus.Add(new Sys_RoleMenu { RoleId = 1, MenuId = 200 });
        db.SaveChanges();
    }
    if (!db.Sys_Menus.Any(m => m.MenuId == 201))
    {
        db.Sys_Menus.Add(new Sys_Menu { MenuId = 201, MenuName = "見積計算書 照会", RoutePath = "/estimate-calc-list", Icon = "List", ParentId = 200, OrderNo = 201, Enable = true });
        db.Sys_RoleMenus.Add(new Sys_RoleMenu { RoleId = 1, MenuId = 201 });
        db.SaveChanges();
    }
    if (!db.Sys_Menus.Any(m => m.MenuId == 202))
    {
        db.Sys_Menus.Add(new Sys_Menu { MenuId = 202, MenuName = "見積計算書 登録", RoutePath = "/estimate-calc", Icon = "Money", ParentId = 200, OrderNo = 202, Enable = true });
        db.Sys_RoleMenus.Add(new Sys_RoleMenu { RoleId = 1, MenuId = 202 });
        db.SaveChanges();
    }
    // MSBBPA030 / MSBBPA040 御見積書 一覧 / 登録
    if (!db.Sys_Menus.Any(m => m.MenuId == 203))
    {
        db.Sys_Menus.Add(new Sys_Menu { MenuId = 203, MenuName = "御見積書 一覧", RoutePath = "/quotation-list", Icon = "Tickets", ParentId = 200, OrderNo = 203, Enable = true });
        db.Sys_RoleMenus.Add(new Sys_RoleMenu { RoleId = 1, MenuId = 203 });
        db.SaveChanges();
    }
    if (!db.Sys_Menus.Any(m => m.MenuId == 204))
    {
        db.Sys_Menus.Add(new Sys_Menu { MenuId = 204, MenuName = "御見積書 登録", RoutePath = "/quotation", Icon = "EditPen", ParentId = 200, OrderNo = 204, Enable = true });
        db.Sys_RoleMenus.Add(new Sys_RoleMenu { RoleId = 1, MenuId = 204 });
        db.SaveChanges();
    }
    // MSBBPA050 / MSBBPA060 製品マスタ 登録 / 一覧
    if (!db.Sys_Menus.Any(m => m.MenuId == 205))
    {
        db.Sys_Menus.Add(new Sys_Menu { MenuId = 205, MenuName = "製品マスタ 一覧", RoutePath = "/product-list", Icon = "Goods", ParentId = 200, OrderNo = 205, Enable = true });
        db.Sys_RoleMenus.Add(new Sys_RoleMenu { RoleId = 1, MenuId = 205 });
        db.SaveChanges();
    }
    if (!db.Sys_Menus.Any(m => m.MenuId == 206))
    {
        db.Sys_Menus.Add(new Sys_Menu { MenuId = 206, MenuName = "製品マスタ 登録", RoutePath = "/product", Icon = "Box", ParentId = 200, OrderNo = 206, Enable = true });
        db.Sys_RoleMenus.Add(new Sys_RoleMenu { RoleId = 1, MenuId = 206 });
        db.SaveChanges();
    }
    // MSBBPA070 / 080 / 090 受注入力 / 一覧 / 単価訂正
    if (!db.Sys_Menus.Any(m => m.MenuId == 207))
    {
        db.Sys_Menus.Add(new Sys_Menu { MenuId = 207, MenuName = "受注一覧照会", RoutePath = "/order-list", Icon = "Files", ParentId = 200, OrderNo = 207, Enable = true });
        db.Sys_RoleMenus.Add(new Sys_RoleMenu { RoleId = 1, MenuId = 207 });
        db.SaveChanges();
    }
    if (!db.Sys_Menus.Any(m => m.MenuId == 208))
    {
        db.Sys_Menus.Add(new Sys_Menu { MenuId = 208, MenuName = "受注入力", RoutePath = "/order", Icon = "DocumentAdd", ParentId = 200, OrderNo = 208, Enable = true });
        db.Sys_RoleMenus.Add(new Sys_RoleMenu { RoleId = 1, MenuId = 208 });
        db.SaveChanges();
    }
    if (!db.Sys_Menus.Any(m => m.MenuId == 209))
    {
        db.Sys_Menus.Add(new Sys_Menu { MenuId = 209, MenuName = "単価訂正", RoutePath = "/order-price-correction", Icon = "PriceTag", ParentId = 200, OrderNo = 209, Enable = true });
        db.Sys_RoleMenus.Add(new Sys_RoleMenu { RoleId = 1, MenuId = 209 });
        db.SaveChanges();
    }
    // MSBBPA100 / 110 / 120 FSC・取引先マスタ
    if (!db.Sys_Menus.Any(m => m.MenuId == 210))
    {
        db.Sys_Menus.Add(new Sys_Menu { MenuId = 210, MenuName = "FSC チェックシート", RoutePath = "/fsc-checklist", Icon = "Document", ParentId = 200, OrderNo = 210, Enable = true });
        db.Sys_RoleMenus.Add(new Sys_RoleMenu { RoleId = 1, MenuId = 210 });
        db.SaveChanges();
    }
    if (!db.Sys_Menus.Any(m => m.MenuId == 211))
    {
        db.Sys_Menus.Add(new Sys_Menu { MenuId = 211, MenuName = "取引先マスタ 一覧", RoutePath = "/business-partner-list", Icon = "OfficeBuilding", ParentId = 200, OrderNo = 211, Enable = true });
        db.Sys_RoleMenus.Add(new Sys_RoleMenu { RoleId = 1, MenuId = 211 });
        db.SaveChanges();
    }
    if (!db.Sys_Menus.Any(m => m.MenuId == 212))
    {
        db.Sys_Menus.Add(new Sys_Menu { MenuId = 212, MenuName = "取引先マスタ 登録", RoutePath = "/business-partner", Icon = "User", ParentId = 200, OrderNo = 212, Enable = true });
        db.Sys_RoleMenus.Add(new Sys_RoleMenu { RoleId = 1, MenuId = 212 });
        db.SaveChanges();
    }
    // MSBBPA130 / 140 / 150 シート単価・版型/木型マスタ
    if (!db.Sys_Menus.Any(m => m.MenuId == 213))
    {
        db.Sys_Menus.Add(new Sys_Menu { MenuId = 213, MenuName = "シート単価マスタ", RoutePath = "/sheet-unit-price", Icon = "Coin", ParentId = 200, OrderNo = 213, Enable = true });
        db.Sys_RoleMenus.Add(new Sys_RoleMenu { RoleId = 1, MenuId = 213 });
        db.SaveChanges();
    }
    if (!db.Sys_Menus.Any(m => m.MenuId == 214))
    {
        db.Sys_Menus.Add(new Sys_Menu { MenuId = 214, MenuName = "版型/木型 一覧", RoutePath = "/plate-mold-list", Icon = "Tools", ParentId = 200, OrderNo = 214, Enable = true });
        db.Sys_RoleMenus.Add(new Sys_RoleMenu { RoleId = 1, MenuId = 214 });
        db.SaveChanges();
    }
    if (!db.Sys_Menus.Any(m => m.MenuId == 215))
    {
        db.Sys_Menus.Add(new Sys_Menu { MenuId = 215, MenuName = "版型/木型 登録", RoutePath = "/plate-mold", Icon = "Stamp", ParentId = 200, OrderNo = 215, Enable = true });
        db.Sys_RoleMenus.Add(new Sys_RoleMenu { RoleId = 1, MenuId = 215 });
        db.SaveChanges();
    }

    // ═══════════════════════════════════════════════════════════
    //  财务（Fin）总账内核 菜单（章01+02）
    // ═══════════════════════════════════════════════════════════
    if (!db.Sys_Menus.Any(m => m.MenuId == 600))
    {
        db.Sys_Menus.Add(new Sys_Menu { MenuId = 600, MenuName = "财务管理(Fin)", Icon = "Money", OrderNo = 250, Enable = true });
        db.Sys_RoleMenus.Add(new Sys_RoleMenu { RoleId = 1, MenuId = 600 });
        db.SaveChanges();
    }
    if (!db.Sys_Menus.Any(m => m.MenuId == 601))
    {
        db.Sys_Menus.Add(new Sys_Menu { MenuId = 601, MenuName = "会计科目", RoutePath = "/fin/account", Icon = "Collection", ParentId = 600, OrderNo = 251, Enable = true });
        db.Sys_RoleMenus.Add(new Sys_RoleMenu { RoleId = 1, MenuId = 601 });
        db.SaveChanges();
    }
    if (!db.Sys_Menus.Any(m => m.MenuId == 602))
    {
        db.Sys_Menus.Add(new Sys_Menu { MenuId = 602, MenuName = "记账凭证", RoutePath = "/fin/journal", Icon = "Document", ParentId = 600, OrderNo = 252, Enable = true });
        db.Sys_RoleMenus.Add(new Sys_RoleMenu { RoleId = 1, MenuId = 602 });
        db.SaveChanges();
    }
    if (!db.Sys_Menus.Any(m => m.MenuId == 603))
    {
        db.Sys_Menus.Add(new Sys_Menu { MenuId = 603, MenuName = "试算平衡表", RoutePath = "/fin/trial-balance", Icon = "Histogram", ParentId = 600, OrderNo = 253, Enable = true });
        db.Sys_RoleMenus.Add(new Sys_RoleMenu { RoleId = 1, MenuId = 603 });
        db.SaveChanges();
    }
    if (!db.Sys_Menus.Any(m => m.MenuId == 604))
    {
        db.Sys_Menus.Add(new Sys_Menu { MenuId = 604, MenuName = "会计期间/月结", RoutePath = "/fin/period", Icon = "Calendar", ParentId = 600, OrderNo = 254, Enable = true });
        db.Sys_RoleMenus.Add(new Sys_RoleMenu { RoleId = 1, MenuId = 604 });
        db.SaveChanges();
    }
    if (!db.Sys_Menus.Any(m => m.MenuId == 605))
    {
        db.Sys_Menus.Add(new Sys_Menu { MenuId = 605, MenuName = "应付发票", RoutePath = "/fin/ap-invoice", Icon = "Tickets", ParentId = 600, OrderNo = 255, Enable = true });
        db.Sys_RoleMenus.Add(new Sys_RoleMenu { RoleId = 1, MenuId = 605 });
        db.SaveChanges();
    }
    if (!db.Sys_Menus.Any(m => m.MenuId == 606))
    {
        db.Sys_Menus.Add(new Sys_Menu { MenuId = 606, MenuName = "付款/核销", RoutePath = "/fin/ap-payment", Icon = "Wallet", ParentId = 600, OrderNo = 256, Enable = true });
        db.Sys_RoleMenus.Add(new Sys_RoleMenu { RoleId = 1, MenuId = 606 });
        db.SaveChanges();
    }
    if (!db.Sys_Menus.Any(m => m.MenuId == 607))
    {
        db.Sys_Menus.Add(new Sys_Menu { MenuId = 607, MenuName = "应付账龄", RoutePath = "/fin/ap-aging", Icon = "Histogram", ParentId = 600, OrderNo = 257, Enable = true });
        db.Sys_RoleMenus.Add(new Sys_RoleMenu { RoleId = 1, MenuId = 607 });
        db.SaveChanges();
    }
    if (!db.Sys_Menus.Any(m => m.MenuId == 608))
    {
        db.Sys_Menus.Add(new Sys_Menu { MenuId = 608, MenuName = "应收发票", RoutePath = "/fin/ar-invoice", Icon = "Tickets", ParentId = 600, OrderNo = 258, Enable = true });
        db.Sys_RoleMenus.Add(new Sys_RoleMenu { RoleId = 1, MenuId = 608 });
        db.SaveChanges();
    }
    if (!db.Sys_Menus.Any(m => m.MenuId == 609))
    {
        db.Sys_Menus.Add(new Sys_Menu { MenuId = 609, MenuName = "收款/核销", RoutePath = "/fin/ar-receipt", Icon = "Wallet", ParentId = 600, OrderNo = 259, Enable = true });
        db.Sys_RoleMenus.Add(new Sys_RoleMenu { RoleId = 1, MenuId = 609 });
        db.SaveChanges();
    }
    if (!db.Sys_Menus.Any(m => m.MenuId == 610))
    {
        db.Sys_Menus.Add(new Sys_Menu { MenuId = 610, MenuName = "应收账龄", RoutePath = "/fin/ar-aging", Icon = "Histogram", ParentId = 600, OrderNo = 260, Enable = true });
        db.Sys_RoleMenus.Add(new Sys_RoleMenu { RoleId = 1, MenuId = 610 });
        db.SaveChanges();
    }
    if (!db.Sys_Menus.Any(m => m.MenuId == 611))
    {
        db.Sys_Menus.Add(new Sys_Menu { MenuId = 611, MenuName = "资产负债表", RoutePath = "/fin/balance-sheet", Icon = "Coin", ParentId = 600, OrderNo = 261, Enable = true });
        db.Sys_RoleMenus.Add(new Sys_RoleMenu { RoleId = 1, MenuId = 611 });
        db.SaveChanges();
    }
    if (!db.Sys_Menus.Any(m => m.MenuId == 612))
    {
        db.Sys_Menus.Add(new Sys_Menu { MenuId = 612, MenuName = "损益表", RoutePath = "/fin/income-statement", Icon = "TrendCharts", ParentId = 600, OrderNo = 262, Enable = true });
        db.Sys_RoleMenus.Add(new Sys_RoleMenu { RoleId = 1, MenuId = 612 });
        db.SaveChanges();
    }
    if (!db.Sys_Menus.Any(m => m.MenuId == 613))
    {
        db.Sys_Menus.Add(new Sys_Menu { MenuId = 613, MenuName = "成本核算", RoutePath = "/fin/cost", Icon = "ScaleToOriginal", ParentId = 600, OrderNo = 263, Enable = true });
        db.Sys_RoleMenus.Add(new Sys_RoleMenu { RoleId = 1, MenuId = 613 });
        db.SaveChanges();
    }
    // 财务（Fin）功能权限点（D-2）：GL/AP/AR 变更端点的操作点 seed + 授权 admin(RoleId=1)。
    // 资源键 = Sys_Menu.MenuKey（由 RoutePath 派生，如 /fin/ar-invoice → fin-ar-invoice）。
    // PermissionService.HasActionAsync 无 admin 旁路：贴 [RequirePermission] 的端点必须在此 seed+授权，否则 admin 也 403。幂等。
    {
        // 确保 Fin 菜单的 MenuKey 已就位（全新库首启时这些菜单在上方 §545 回填之后才创建，故此处补一次，与全局回填同算法）
        foreach (var fm in db.Sys_Menus.Where(m => m.MenuKey == null && m.RoutePath != null && m.MenuId >= 601 && m.MenuId <= 613).ToList())
            fm.MenuKey = fm.RoutePath!.Trim('/').Replace('/', '-');
        db.SaveChanges();

        // (MenuId, ActionCode, ActionName) —— 与各控制器 [RequirePermission(menuKey, action)] 一一对应
        var finActions = new (int MenuId, string Code, string Name)[]
        {
            (601, "add", "新建"), (601, "edit", "编辑"), (601, "deactivate", "停用"), (601, "import", "导入"),
            (602, "add", "新建"), (602, "submit", "提交复核"), (602, "post", "过账"), (602, "reject", "驳回"), (602, "reverse", "红冲"),
            (604, "close", "结账"), (604, "reopen", "反结账"),
            (605, "add", "新建"), (605, "post", "过账"),
            (606, "add", "新建"), (606, "settle", "核销"), (606, "reverse", "撤销"), (606, "bank", "银行账户"), (606, "tax", "税码"),
            (608, "add", "新建"), (608, "post", "过账"), (608, "credit-memo", "销售红字"), (608, "reverse", "红冲"),
            (609, "add", "新建"), (609, "settle", "核销"), (609, "reverse", "撤销"),
            (613, "collect", "归集"), (613, "settle", "结转"),
        };
        foreach (var (menuId, code, name) in finActions)
        {
            if (!db.Sys_MenuActions.Any(x => x.MenuId == menuId && x.ActionCode == code))
                db.Sys_MenuActions.Add(new Sys_MenuAction { MenuId = menuId, ActionCode = code, ActionName = name, Sort = 0 });
            if (!db.Sys_RoleActions.Any(x => x.RoleId == 1 && x.MenuId == menuId && x.ActionCode == code))
                db.Sys_RoleActions.Add(new Sys_RoleAction { RoleId = 1, MenuId = menuId, ActionCode = code });
        }
        db.SaveChanges();
    }

    // ═══════════════════════════════════════════════════════════
    //  采购（Pur）MVP 章01~04 菜单（700 组）+ 功能权限点
    // ═══════════════════════════════════════════════════════════
    if (!db.Sys_Menus.Any(m => m.MenuId == 700))
    {
        db.Sys_Menus.Add(new Sys_Menu { MenuId = 700, MenuName = "采购管理(Pur)", Icon = "ShoppingCart", OrderNo = 245, Enable = true });
        db.Sys_RoleMenus.Add(new Sys_RoleMenu { RoleId = 1, MenuId = 700 });
        db.SaveChanges();
    }
    if (!db.Sys_Menus.Any(m => m.MenuId == 701))
    {
        db.Sys_Menus.Add(new Sys_Menu { MenuId = 701, MenuName = "供应商价表", RoutePath = "/pur/supplier-price", Icon = "PriceTag", ParentId = 700, OrderNo = 701, Enable = true });
        db.Sys_RoleMenus.Add(new Sys_RoleMenu { RoleId = 1, MenuId = 701 });
        db.SaveChanges();
    }
    if (!db.Sys_Menus.Any(m => m.MenuId == 702))
    {
        db.Sys_Menus.Add(new Sys_Menu { MenuId = 702, MenuName = "采购订单", RoutePath = "/pur/po", Icon = "Document", ParentId = 700, OrderNo = 702, Enable = true });
        db.Sys_RoleMenus.Add(new Sys_RoleMenu { RoleId = 1, MenuId = 702 });
        db.SaveChanges();
    }
    if (!db.Sys_Menus.Any(m => m.MenuId == 703))
    {
        db.Sys_Menus.Add(new Sys_Menu { MenuId = 703, MenuName = "采购收货", RoutePath = "/pur/gr", Icon = "Box", ParentId = 700, OrderNo = 703, Enable = true });
        db.Sys_RoleMenus.Add(new Sys_RoleMenu { RoleId = 1, MenuId = 703 });
        db.SaveChanges();
    }
    if (!db.Sys_Menus.Any(m => m.MenuId == 704))
    {
        db.Sys_Menus.Add(new Sys_Menu { MenuId = 704, MenuName = "三单匹配", RoutePath = "/pur/match", Icon = "Connection", ParentId = 700, OrderNo = 704, Enable = true });
        db.Sys_RoleMenus.Add(new Sys_RoleMenu { RoleId = 1, MenuId = 704 });
        db.SaveChanges();
    }
    // 采购功能权限点：MenuKey 回填（派生 pur-* 对齐各控制器 [RequirePermission]）+ 操作点 seed + 授权 admin(RoleId=1)。幂等。
    {
        foreach (var pm in db.Sys_Menus.Where(m => m.MenuKey == null && m.RoutePath != null && m.MenuId >= 701 && m.MenuId <= 704).ToList())
            pm.MenuKey = pm.RoutePath!.Trim('/').Replace('/', '-');
        db.SaveChanges();

        // (MenuId, ActionCode, ActionName) —— 与各控制器 [RequirePermission(menuKey, action)] 一一对应
        var purActions = new (int MenuId, string Code, string Name)[]
        {
            (701, "add", "新增/更新"), (701, "delete", "删除"),
            (702, "add", "建单"), (702, "submit", "送审"), (702, "cancel", "取消"),
            (703, "add", "确认收货"), (703, "qc", "检收应用"),
            (704, "add", "匹配建票"), (704, "release", "放行"), (704, "reject", "拒绝"),
        };
        foreach (var (menuId, code, name) in purActions)
        {
            if (!db.Sys_MenuActions.Any(x => x.MenuId == menuId && x.ActionCode == code))
                db.Sys_MenuActions.Add(new Sys_MenuAction { MenuId = menuId, ActionCode = code, ActionName = name, Sort = 0 });
            if (!db.Sys_RoleActions.Any(x => x.RoleId == 1 && x.MenuId == menuId && x.ActionCode == code))
                db.Sys_RoleActions.Add(new Sys_RoleAction { RoleId = 1, MenuId = menuId, ActionCode = code });
        }
        db.SaveChanges();
    }

    // ═══════════════════════════════════════════════════════════
    //  MSBBME010〜090 MES 製造執行 菜单
    // ═══════════════════════════════════════════════════════════
    if (!db.Sys_Menus.Any(m => m.MenuId == 300))
    {
        db.Sys_Menus.Add(new Sys_Menu { MenuId = 300, MenuName = "製造執行(MES)", Icon = "SetUp", OrderNo = 300, Enable = true });
        db.Sys_RoleMenus.Add(new Sys_RoleMenu { RoleId = 1, MenuId = 300 });
        db.SaveChanges();
    }
    if (!db.Sys_Menus.Any(m => m.MenuId == 302))
    {
        db.Sys_Menus.Add(new Sys_Menu { MenuId = 302, MenuName = "製造指図 入力", RoutePath = "/mes/work-order", Icon = "DocumentAdd", ParentId = 300, OrderNo = 302, Enable = true });
        db.Sys_RoleMenus.Add(new Sys_RoleMenu { RoleId = 1, MenuId = 302 });
        db.SaveChanges();
    }
    if (!db.Sys_Menus.Any(m => m.MenuId == 303))
    {
        db.Sys_Menus.Add(new Sys_Menu { MenuId = 303, MenuName = "製造指図 一覧", RoutePath = "/mes/work-order-list", Icon = "Files", ParentId = 300, OrderNo = 303, Enable = true });
        db.Sys_RoleMenus.Add(new Sys_RoleMenu { RoleId = 1, MenuId = 303 });
        db.SaveChanges();
    }
    if (!db.Sys_Menus.Any(m => m.MenuId == 304))
    {
        db.Sys_Menus.Add(new Sys_Menu { MenuId = 304, MenuName = "製造実績 入力", RoutePath = "/mes/production-result", Icon = "EditPen", ParentId = 300, OrderNo = 304, Enable = true });
        db.Sys_RoleMenus.Add(new Sys_RoleMenu { RoleId = 1, MenuId = 304 });
        db.SaveChanges();
    }
    if (!db.Sys_Menus.Any(m => m.MenuId == 305))
    {
        db.Sys_Menus.Add(new Sys_Menu { MenuId = 305, MenuName = "製造実績 一覧", RoutePath = "/mes/production-result-list", Icon = "DataLine", ParentId = 300, OrderNo = 305, Enable = true });
        db.Sys_RoleMenus.Add(new Sys_RoleMenu { RoleId = 1, MenuId = 305 });
        db.SaveChanges();
    }
    // ME060 / 070 / 080
    if (!db.Sys_Menus.Any(m => m.MenuId == 306))
    {
        db.Sys_Menus.Add(new Sys_Menu { MenuId = 306, MenuName = "品質検査 入力", RoutePath = "/mes/quality-inspection", Icon = "Operation", ParentId = 300, OrderNo = 306, Enable = true });
        db.Sys_RoleMenus.Add(new Sys_RoleMenu { RoleId = 1, MenuId = 306 });
        db.SaveChanges();
    }
    if (!db.Sys_Menus.Any(m => m.MenuId == 307))
    {
        db.Sys_Menus.Add(new Sys_Menu { MenuId = 307, MenuName = "品質検査 一覧", RoutePath = "/mes/quality-inspection-list", Icon = "Histogram", ParentId = 300, OrderNo = 307, Enable = true });
        db.Sys_RoleMenus.Add(new Sys_RoleMenu { RoleId = 1, MenuId = 307 });
        db.SaveChanges();
    }
    if (!db.Sys_Menus.Any(m => m.MenuId == 308))
    {
        db.Sys_Menus.Add(new Sys_Menu { MenuId = 308, MenuName = "不良品管理", RoutePath = "/mes/defect", Icon = "Warning", ParentId = 300, OrderNo = 308, Enable = true });
        db.Sys_RoleMenus.Add(new Sys_RoleMenu { RoleId = 1, MenuId = 308 });
        db.SaveChanges();
    }
    // ME010 / ME090
    if (!db.Sys_Menus.Any(m => m.MenuId == 301))
    {
        db.Sys_Menus.Add(new Sys_Menu { MenuId = 301, MenuName = "生産計画ボード", RoutePath = "/mes/planning-board", Icon = "Calendar", ParentId = 300, OrderNo = 301, Enable = true });
        db.Sys_RoleMenus.Add(new Sys_RoleMenu { RoleId = 1, MenuId = 301 });
        db.SaveChanges();
    }
    if (!db.Sys_Menus.Any(m => m.MenuId == 309))
    {
        db.Sys_Menus.Add(new Sys_Menu { MenuId = 309, MenuName = "MESダッシュボード", RoutePath = "/mes/dashboard", Icon = "PieChart", ParentId = 300, OrderNo = 309, Enable = true });
        db.Sys_RoleMenus.Add(new Sys_RoleMenu { RoleId = 1, MenuId = 309 });
        db.SaveChanges();
    }
    // MES Phase 4：設備管理 / OEE / Control Tower
    if (!db.Sys_Menus.Any(m => m.MenuId == 310))
    {
        db.Sys_Menus.Add(new Sys_Menu { MenuId = 310, MenuName = "設備管理", RoutePath = "/mes/machine-list", Icon = "Monitor", ParentId = 300, OrderNo = 310, Enable = true });
        db.Sys_RoleMenus.Add(new Sys_RoleMenu { RoleId = 1, MenuId = 310 });
        db.SaveChanges();
    }
    if (!db.Sys_Menus.Any(m => m.MenuId == 311))
    {
        db.Sys_Menus.Add(new Sys_Menu { MenuId = 311, MenuName = "OEE 分析", RoutePath = "/mes/oee", Icon = "TrendCharts", ParentId = 300, OrderNo = 311, Enable = true });
        db.Sys_RoleMenus.Add(new Sys_RoleMenu { RoleId = 1, MenuId = 311 });
        db.SaveChanges();
    }
    if (!db.Sys_Menus.Any(m => m.MenuId == 312))
    {
        db.Sys_Menus.Add(new Sys_Menu { MenuId = 312, MenuName = "Control Tower 大屏", RoutePath = "/mes/control-tower", Icon = "Aim", ParentId = 300, OrderNo = 312, Enable = true });
        db.Sys_RoleMenus.Add(new Sys_RoleMenu { RoleId = 1, MenuId = 312 });
        db.SaveChanges();
    }
    // Gap 3.3 生産計画達成率レポート
    if (!db.Sys_Menus.Any(m => m.MenuId == 313))
    {
        db.Sys_Menus.Add(new Sys_Menu { MenuId = 313, MenuName = "生産計画達成率", RoutePath = "/mes/plan-achievement", Icon = "DataLine", ParentId = 300, OrderNo = 313, Enable = true });
        db.Sys_RoleMenus.Add(new Sys_RoleMenu { RoleId = 1, MenuId = 313 });
        db.SaveChanges();
    }

    // ─────────────────────────────────────────────────────────────
    //  設備マスタ サンプル seed
    // ─────────────────────────────────────────────────────────────
    if (!db.Machines.Any())
    {
        var now = DateTime.Now;
        var seedMachines = new (string Cd, string Name, string Type, string Process, string Wg, decimal CapPerHour, decimal CycleSec)[]
        {
            ("M001", "印刷機 4色 #1",  "PRINT",  "P01", "WG-PRINT", 6000m, 0.6m),
            ("M002", "印刷機 4色 #2",  "PRINT",  "P01", "WG-PRINT", 6000m, 0.6m),
            ("M003", "貼合機 #1",      "LAMINAT","P02", "WG-LAMI",  3000m, 1.2m),
            ("M004", "トムソン #1",    "DIECUT", "P03", "WG-DIE",   2400m, 1.5m),
            ("M005", "トムソン #2",    "DIECUT", "P03", "WG-DIE",   2400m, 1.5m),
            ("M006", "グルア #1",      "GLUE",   "P04", "WG-GLUE",  1800m, 2.0m),
        };
        foreach (var (cd, name, type, proc, wg, cap, cycle) in seedMachines)
        {
            db.Machines.Add(new CP6.Entity.DomainModels.Mes.Machine
            {
                MachineCd = cd,
                MachineName = name,
                MachineType = type,
                ProcessCd = proc,
                WgCd = wg,
                Status = 0,
                PlannedRunMinutesPerDay = 480,
                CapacityPerHour = cap,
                StandardCycleSec = cycle,
                ActiveFlg = true,
                InstallDate = DateTime.Today.AddYears(-3),
                Creator = "system",
                CreateDate = now,
            });
        }
        db.SaveChanges();
    }

    // ─────────────────────────────────────────────────────────────
    //  不良分類マスタ (M_DefectCategory / ME-M002) seed 数据 — 仕様書 §7.1
    // ─────────────────────────────────────────────────────────────
    if (!db.DefectCategories.Any())
    {
        var now = DateTime.Now;
        var seedDefects = new (string Cat, string CatName, string Detail, string DetailName)[]
        {
            // D01 寸法不良
            ("D01", "寸法不良", "01", "巾寸法外れ"),
            ("D01", "寸法不良", "02", "流れ寸法外れ"),
            ("D01", "寸法不良", "03", "型ズレ"),
            // D02 印刷不良
            ("D02", "印刷不良", "01", "色差"),
            ("D02", "印刷不良", "02", "見当ズレ"),
            ("D02", "印刷不良", "03", "インキ飛び"),
            ("D02", "印刷不良", "04", "汚れ"),
            ("D02", "印刷不良", "05", "抜け"),
            // D03 貼合不良
            ("D03", "貼合不良", "01", "段つぶれ"),
            ("D03", "貼合不良", "02", "接着不良"),
            ("D03", "貼合不良", "03", "反り"),
            ("D03", "貼合不良", "04", "ブリスター"),
            // D04 トムソン不良
            ("D04", "トムソン不良", "01", "抜きズレ"),
            ("D04", "トムソン不良", "02", "罫線割れ"),
            ("D04", "トムソン不良", "03", "バリ"),
            ("D04", "トムソン不良", "04", "角切れ"),
            // D05 グルア不良
            ("D05", "グルア不良", "01", "接着不良"),
            ("D05", "グルア不良", "02", "位置ズレ"),
            ("D05", "グルア不良", "03", "糊はみ出し"),
            // D06 材料不良
            ("D06", "材料不良", "01", "原紙不良"),
            ("D06", "材料不良", "02", "インキ不良"),
            ("D06", "材料不良", "03", "糊不良"),
            // D07 その他
            ("D07", "その他", "01", "作業ミス"),
            ("D07", "その他", "02", "設備故障"),
            ("D07", "その他", "99", "その他"),
        };

        int sort = 1;
        foreach (var (cat, catName, detail, detailName) in seedDefects)
        {
            db.DefectCategories.Add(new CP6.Entity.DomainModels.Mes.DefectCategory
            {
                CategoryCd = cat,
                DetailCd = detail,
                CategoryName = catName,
                DetailName = detailName,
                SortOrder = sort++,
                ActiveFlg = true,
                Creator = "system",
                CreateDate = now,
            });
        }
        db.SaveChanges();
    }

    if (!db.Sys_Users.Any())
    {
        db.Sys_Users.Add(new Sys_User
        {
            UserName = "admin",
            Password = "123456",
            NickName = "管理员",
            Enable = true,
            CreateDate = DateTime.Now
        });
        db.SaveChanges();
    }

    // 多语言词条种子数据
    if (!db.Sys_Langs.Any())
    {
        db.Sys_Langs.AddRange(
            new Sys_Lang { LangKey = "app.title", ZhCN = "CP6 管理系统", ZhTW = "CP6 管理系統", En = "CP6 Admin", Ja = "CP6 管理システム", Ko = "CP6 관리 시스템" },
            new Sys_Lang { LangKey = "login.title", ZhCN = "CP6 管理系统", ZhTW = "CP6 管理系統", En = "CP6 Admin", Ja = "CP6 管理システム", Ko = "CP6 관리 시스템" },
            new Sys_Lang { LangKey = "login.username", ZhCN = "请输入用户名", ZhTW = "請輸入使用者名稱", En = "Enter username", Ja = "ユーザー名を入力", Ko = "사용자명 입력" },
            new Sys_Lang { LangKey = "login.password", ZhCN = "请输入密码", ZhTW = "請輸入密碼", En = "Enter password", Ja = "パスワードを入力", Ko = "비밀번호 입력" },
            new Sys_Lang { LangKey = "login.button", ZhCN = "登 录", ZhTW = "登 入", En = "Login", Ja = "ログイン", Ko = "로그인" },
            new Sys_Lang { LangKey = "login.success", ZhCN = "登录成功", ZhTW = "登入成功", En = "Login successful", Ja = "ログイン成功", Ko = "로그인 성공" },
            new Sys_Lang { LangKey = "login.usernameRequired", ZhCN = "请输入用户名", ZhTW = "請輸入使用者名稱", En = "Please enter username", Ja = "ユーザー名を入力してください", Ko = "사용자명을 입력하세요" },
            new Sys_Lang { LangKey = "login.passwordRequired", ZhCN = "请输入密码", ZhTW = "請輸入密碼", En = "Please enter password", Ja = "パスワードを入力してください", Ko = "비밀번호를 입력하세요" },
            new Sys_Lang { LangKey = "login.tenantCode", ZhCN = "租户编码", ZhTW = "租戶編碼", En = "Tenant code", Ja = "テナントコード", Ko = "테넌트 코드" },
            new Sys_Lang { LangKey = "login.specifyTenant", ZhCN = "指定租户", ZhTW = "指定租戶", En = "Specify tenant", Ja = "テナントを指定", Ko = "테넌트 지정" },
            new Sys_Lang { LangKey = "layout.logout", ZhCN = "退出登录", ZhTW = "登出", En = "Logout", Ja = "ログアウト", Ko = "로그아웃" },
            new Sys_Lang { LangKey = "table.search", ZhCN = "请输入关键词搜索", ZhTW = "請輸入關鍵詞搜尋", En = "Search by keyword", Ja = "キーワードで検索", Ko = "키워드로 검색" },
            new Sys_Lang { LangKey = "table.add", ZhCN = "新增", ZhTW = "新增", En = "Add", Ja = "追加", Ko = "추가" },
            new Sys_Lang { LangKey = "table.delete", ZhCN = "删除", ZhTW = "刪除", En = "Delete", Ja = "削除", Ko = "삭제" },
            new Sys_Lang { LangKey = "table.edit", ZhCN = "编辑", ZhTW = "編輯", En = "Edit", Ja = "編集", Ko = "편집" },
            new Sys_Lang { LangKey = "table.operation", ZhCN = "操作", ZhTW = "操作", En = "Action", Ja = "操作", Ko = "작업" },
            new Sys_Lang { LangKey = "table.confirmDelete", ZhCN = "确定删除吗？", ZhTW = "確定刪除嗎？", En = "Are you sure to delete?", Ja = "削除してもよろしいですか？", Ko = "삭제하시겠습니까?" },
            new Sys_Lang { LangKey = "table.confirmBatchDelete", ZhCN = "确定删除选中的 {count} 条数据吗？", ZhTW = "確定刪除選中的 {count} 筆資料嗎？", En = "Are you sure to delete {count} selected items?", Ja = "選択した {count} 件のデータを削除しますか？", Ko = "선택한 {count}개 항목을 삭제하시겠습니까?" },
            new Sys_Lang { LangKey = "table.tip", ZhCN = "提示", ZhTW = "提示", En = "Tip", Ja = "確認", Ko = "확인" },
            new Sys_Lang { LangKey = "table.addSuccess", ZhCN = "新增成功", ZhTW = "新增成功", En = "Added successfully", Ja = "追加しました", Ko = "추가되었습니다" },
            new Sys_Lang { LangKey = "table.editSuccess", ZhCN = "修改成功", ZhTW = "修改成功", En = "Updated successfully", Ja = "更新しました", Ko = "수정되었습니다" },
            new Sys_Lang { LangKey = "table.deleteSuccess", ZhCN = "删除成功", ZhTW = "刪除成功", En = "Deleted successfully", Ja = "削除しました", Ko = "삭제되었습니다" },
            new Sys_Lang { LangKey = "table.selectFirst", ZhCN = "请先选择要删除的数据", ZhTW = "請先選擇要刪除的資料", En = "Please select data to delete first", Ja = "削除するデータを選択してください", Ko = "삭제할 데이터를 먼저 선택하세요" },
            new Sys_Lang { LangKey = "table.cancel", ZhCN = "取消", ZhTW = "取消", En = "Cancel", Ja = "キャンセル", Ko = "취소" },
            new Sys_Lang { LangKey = "table.confirm", ZhCN = "确定", ZhTW = "確定", En = "OK", Ja = "確定", Ko = "확인" },
            new Sys_Lang { LangKey = "table.select", ZhCN = "请选择", ZhTW = "請選擇", En = "Select", Ja = "選択してください", Ko = "선택하세요" },
            new Sys_Lang { LangKey = "article.title", ZhCN = "标题", ZhTW = "標題", En = "Title", Ja = "タイトル", Ko = "제목" },
            new Sys_Lang { LangKey = "article.author", ZhCN = "作者", ZhTW = "作者", En = "Author", Ja = "著者", Ko = "작성자" },
            new Sys_Lang { LangKey = "article.content", ZhCN = "内容", ZhTW = "內容", En = "Content", Ja = "内容", Ko = "내용" },
            new Sys_Lang { LangKey = "article.isPublished", ZhCN = "是否发布", ZhTW = "是否發佈", En = "Published", Ja = "公開済み", Ko = "게시 여부" },
            new Sys_Lang { LangKey = "article.createDate", ZhCN = "创建时间", ZhTW = "建立時間", En = "Created At", Ja = "作成日時", Ko = "생성일" },
            new Sys_Lang { LangKey = "user.userName", ZhCN = "用户名", ZhTW = "使用者名稱", En = "Username", Ja = "ユーザー名", Ko = "사용자명" },
            new Sys_Lang { LangKey = "user.password", ZhCN = "密码", ZhTW = "密碼", En = "Password", Ja = "パスワード", Ko = "비밀번호" },
            new Sys_Lang { LangKey = "user.nickName", ZhCN = "昵称", ZhTW = "暱稱", En = "Nickname", Ja = "ニックネーム", Ko = "닉네임" },
            new Sys_Lang { LangKey = "user.role", ZhCN = "角色", ZhTW = "角色", En = "Role", Ja = "役割", Ko = "역할" },
            new Sys_Lang { LangKey = "user.enable", ZhCN = "启用", ZhTW = "啟用", En = "Enabled", Ja = "有効", Ko = "활성화" },
            new Sys_Lang { LangKey = "user.createDate", ZhCN = "创建时间", ZhTW = "建立時間", En = "Created At", Ja = "作成日時", Ko = "생성일" },
            new Sys_Lang { LangKey = "role.roleId", ZhCN = "角色ID", ZhTW = "角色ID", En = "Role ID", Ja = "役割ID", Ko = "역할 ID" },
            new Sys_Lang { LangKey = "role.roleName", ZhCN = "角色名称", ZhTW = "角色名稱", En = "Role Name", Ja = "役割名", Ko = "역할명" },
            new Sys_Lang { LangKey = "role.description", ZhCN = "描述", ZhTW = "描述", En = "Description", Ja = "説明", Ko = "설명" },
            new Sys_Lang { LangKey = "role.orderNo", ZhCN = "排序", ZhTW = "排序", En = "Order", Ja = "順序", Ko = "순서" },
            new Sys_Lang { LangKey = "role.enable", ZhCN = "是否启用", ZhTW = "是否啟用", En = "Enabled", Ja = "有効", Ko = "활성화" },
            new Sys_Lang { LangKey = "role.createDate", ZhCN = "创建时间", ZhTW = "建立時間", En = "Created At", Ja = "作成日時", Ko = "생성일" },
            new Sys_Lang { LangKey = "menu.title", ZhCN = "菜单管理", ZhTW = "選單管理", En = "Menu Management", Ja = "メニュー管理", Ko = "메뉴 관리" },
            new Sys_Lang { LangKey = "menu.addTop", ZhCN = "新增顶级菜单", ZhTW = "新增頂級選單", En = "Add Top Menu", Ja = "トップメニュー追加", Ko = "최상위 메뉴 추가" },
            new Sys_Lang { LangKey = "menu.addChild", ZhCN = "添加子菜单", ZhTW = "新增子選單", En = "Add Sub Menu", Ja = "サブメニュー追加", Ko = "하위 메뉴 추가" },
            new Sys_Lang { LangKey = "menu.editMenu", ZhCN = "编辑菜单", ZhTW = "編輯選單", En = "Edit Menu", Ja = "メニュー編集", Ko = "메뉴 편집" },
            new Sys_Lang { LangKey = "menu.addTopMenu", ZhCN = "新增顶级菜单", ZhTW = "新增頂級選單", En = "Add Top Menu", Ja = "トップメニュー追加", Ko = "최상위 메뉴 추가" },
            new Sys_Lang { LangKey = "menu.addSubMenu", ZhCN = "新增子菜单", ZhTW = "新增子選單", En = "Add Sub Menu", Ja = "サブメニュー追加", Ko = "하위 메뉴 추가" },
            new Sys_Lang { LangKey = "menu.menuId", ZhCN = "菜单ID", ZhTW = "選單ID", En = "Menu ID", Ja = "メニューID", Ko = "메뉴 ID" },
            new Sys_Lang { LangKey = "menu.menuName", ZhCN = "菜单名称", ZhTW = "選單名稱", En = "Menu Name", Ja = "メニュー名", Ko = "메뉴명" },
            new Sys_Lang { LangKey = "menu.routePath", ZhCN = "路由路径", ZhTW = "路由路徑", En = "Route Path", Ja = "ルートパス", Ko = "라우트 경로" },
            new Sys_Lang { LangKey = "menu.icon", ZhCN = "图标", ZhTW = "圖示", En = "Icon", Ja = "アイコン", Ko = "아이콘" },
            new Sys_Lang { LangKey = "menu.orderNo", ZhCN = "排序", ZhTW = "排序", En = "Order", Ja = "順序", Ko = "순서" },
            new Sys_Lang { LangKey = "menu.status", ZhCN = "状态", ZhTW = "狀態", En = "Status", Ja = "ステータス", Ko = "상태" },
            new Sys_Lang { LangKey = "menu.enabled", ZhCN = "启用", ZhTW = "啟用", En = "Enabled", Ja = "有効", Ko = "활성화" },
            new Sys_Lang { LangKey = "menu.disabled", ZhCN = "禁用", ZhTW = "停用", En = "Disabled", Ja = "無効", Ko = "비활성화" },
            new Sys_Lang { LangKey = "menu.operation", ZhCN = "操作", ZhTW = "操作", En = "Action", Ja = "操作", Ko = "작업" },
            new Sys_Lang { LangKey = "menu.confirmDelete", ZhCN = "确定删除该菜单吗？", ZhTW = "確定刪除該選單嗎？", En = "Are you sure to delete this menu?", Ja = "このメニューを削除しますか？", Ko = "이 메뉴를 삭제하시겠습니까?" },
            new Sys_Lang { LangKey = "menu.menuIdRequired", ZhCN = "请输入菜单ID", ZhTW = "請輸入選單ID", En = "Please enter menu ID", Ja = "メニューIDを入力してください", Ko = "메뉴 ID를 입력하세요" },
            new Sys_Lang { LangKey = "menu.menuNameRequired", ZhCN = "请输入菜单名称", ZhTW = "請輸入選單名稱", En = "Please enter menu name", Ja = "メニュー名を入力してください", Ko = "메뉴명을 입력하세요" },
            new Sys_Lang { LangKey = "menu.routePathPlaceholder", ZhCN = "如 /article", ZhTW = "如 /article", En = "e.g. /article", Ja = "例: /article", Ko = "예: /article" },
            new Sys_Lang { LangKey = "menu.iconPlaceholder", ZhCN = "如 Document, Setting", ZhTW = "如 Document, Setting", En = "e.g. Document, Setting", Ja = "例: Document, Setting", Ko = "예: Document, Setting" },
            new Sys_Lang { LangKey = "permission.title", ZhCN = "权限分配", ZhTW = "權限分配", En = "Permission Assignment", Ja = "権限設定", Ko = "권한 설정" },
            new Sys_Lang { LangKey = "permission.selectRole", ZhCN = "选择角色", ZhTW = "選擇角色", En = "Select Role", Ja = "役割を選択", Ko = "역할 선택" },
            new Sys_Lang { LangKey = "permission.menuPermission", ZhCN = "菜单权限", ZhTW = "選單權限", En = "Menu Permissions", Ja = "メニュー権限", Ko = "메뉴 권한" },
            new Sys_Lang { LangKey = "permission.save", ZhCN = "保存", ZhTW = "儲存", En = "Save", Ja = "保存", Ko = "저장" },
            new Sys_Lang { LangKey = "permission.saveSuccess", ZhCN = "权限保存成功", ZhTW = "權限儲存成功", En = "Permissions saved successfully", Ja = "権限を保存しました", Ko = "권한이 저장되었습니다" },
            new Sys_Lang { LangKey = "common.yes", ZhCN = "是", ZhTW = "是", En = "Yes", Ja = "はい", Ko = "예" },
            new Sys_Lang { LangKey = "common.no", ZhCN = "否", ZhTW = "否", En = "No", Ja = "いいえ", Ko = "아니오" },
            new Sys_Lang { LangKey = "lang.title", ZhCN = "多语言管理", ZhTW = "多語言管理", En = "Language Management", Ja = "多言語管理", Ko = "다국어 관리" },
            new Sys_Lang { LangKey = "lang.langKey", ZhCN = "词条Key", ZhTW = "詞條Key", En = "Lang Key", Ja = "キー", Ko = "키" },
            new Sys_Lang { LangKey = "lang.zhCN", ZhCN = "简体中文", ZhTW = "簡體中文", En = "Chinese (Simplified)", Ja = "簡体中国語", Ko = "중국어(간체)" },
            new Sys_Lang { LangKey = "lang.zhTW", ZhCN = "繁体中文", ZhTW = "繁體中文", En = "Chinese (Traditional)", Ja = "繁体中国語", Ko = "중국어(번체)" },
            new Sys_Lang { LangKey = "lang.en", ZhCN = "英语", ZhTW = "英語", En = "English", Ja = "英語", Ko = "영어" },
            new Sys_Lang { LangKey = "lang.ja", ZhCN = "日语", ZhTW = "日語", En = "Japanese", Ja = "日本語", Ko = "일본어" },
            new Sys_Lang { LangKey = "lang.ko", ZhCN = "韩语", ZhTW = "韓語", En = "Korean", Ja = "韓国語", Ko = "한국어" },
            // 导航菜单翻译 nav.{menuId}
            new Sys_Lang { LangKey = "nav.100", ZhCN = "系统管理", ZhTW = "系統管理", En = "System", Ja = "システム管理", Ko = "시스템 관리" },
            new Sys_Lang { LangKey = "nav.101", ZhCN = "角色管理", ZhTW = "角色管理", En = "Roles", Ja = "役割管理", Ko = "역할 관리" },
            new Sys_Lang { LangKey = "nav.102", ZhCN = "菜单管理", ZhTW = "選單管理", En = "Menus", Ja = "メニュー管理", Ko = "메뉴 관리" },
            new Sys_Lang { LangKey = "nav.103", ZhCN = "权限分配", ZhTW = "權限分配", En = "Permissions", Ja = "権限設定", Ko = "권한 설정" },
            new Sys_Lang { LangKey = "nav.104", ZhCN = "用户管理", ZhTW = "使用者管理", En = "Users", Ja = "ユーザー管理", Ko = "사용자 관리" },
            new Sys_Lang { LangKey = "nav.105", ZhCN = "多语言管理", ZhTW = "多語言管理", En = "Languages", Ja = "多言語管理", Ko = "다국어 관리" },
            new Sys_Lang { LangKey = "nav.2", ZhCN = "仪表盘", ZhTW = "儀表盤", En = "Dashboard", Ja = "ダッシュボード", Ko = "대시보드" },
            // 販売管理 (PA010〜PA150)
            new Sys_Lang { LangKey = "nav.200", ZhCN = "销售管理(ERP)",      ZhTW = "銷售管理(ERP)",      En = "Sales (ERP)",            Ja = "販売管理(ERP)",           Ko = "판매 관리(ERP)" },
            new Sys_Lang { LangKey = "nav.201", ZhCN = "报价计算单 照会",    ZhTW = "報價計算單 照會",    En = "Estimate Calc Inquiry",  Ja = "見積計算書 照会",         Ko = "견적계산서 조회" },
            new Sys_Lang { LangKey = "nav.202", ZhCN = "报价计算单 登记",    ZhTW = "報價計算單 登記",    En = "Estimate Calc Entry",    Ja = "見積計算書 登録",         Ko = "견적계산서 등록" },
            new Sys_Lang { LangKey = "nav.203", ZhCN = "正式报价单 一览",    ZhTW = "正式報價單 一覽",    En = "Quotation List",         Ja = "御見積書 一覧",           Ko = "견적서 목록" },
            new Sys_Lang { LangKey = "nav.204", ZhCN = "正式报价单 登记",    ZhTW = "正式報價單 登記",    En = "Quotation Entry",        Ja = "御見積書 登録",           Ko = "견적서 등록" },
            new Sys_Lang { LangKey = "nav.205", ZhCN = "产品主数据 一览",    ZhTW = "產品主資料 一覽",    En = "Product Master List",    Ja = "製品マスタ 一覧",         Ko = "제품 마스터 목록" },
            new Sys_Lang { LangKey = "nav.206", ZhCN = "产品主数据 登记",    ZhTW = "產品主資料 登記",    En = "Product Master Entry",   Ja = "製品マスタ 登録",         Ko = "제품 마스터 등록" },
            new Sys_Lang { LangKey = "nav.207", ZhCN = "订单一览查询",       ZhTW = "訂單一覽查詢",       En = "Order Inquiry",          Ja = "受注一覧照会",            Ko = "수주 일람 조회" },
            new Sys_Lang { LangKey = "nav.208", ZhCN = "订单录入",           ZhTW = "訂單錄入",           En = "Order Entry",            Ja = "受注入力",                Ko = "수주 입력" },
            new Sys_Lang { LangKey = "nav.209", ZhCN = "单价订正",           ZhTW = "單價訂正",           En = "Price Correction",       Ja = "単価訂正",                Ko = "단가 수정" },
            new Sys_Lang { LangKey = "nav.210", ZhCN = "FSC 检查清单",       ZhTW = "FSC 檢查清單",       En = "FSC Checklist",          Ja = "FSC チェックシート",      Ko = "FSC 체크시트" },
            new Sys_Lang { LangKey = "nav.211", ZhCN = "交易先主数据 一览",  ZhTW = "交易夥伴主資料 一覽", En = "Business Partner List",  Ja = "取引先マスタ 一覧",       Ko = "거래처 마스터 목록" },
            new Sys_Lang { LangKey = "nav.212", ZhCN = "交易先主数据 登记",  ZhTW = "交易夥伴主資料 登記", En = "Business Partner Entry", Ja = "取引先マスタ 登録",       Ko = "거래처 마스터 등록" },
            new Sys_Lang { LangKey = "nav.213", ZhCN = "纸板单价主数据",     ZhTW = "紙板單價主資料",     En = "Sheet Unit Price",       Ja = "シート単価マスタ",        Ko = "시트 단가 마스터" },
            new Sys_Lang { LangKey = "nav.214", ZhCN = "版型/木型 一览",     ZhTW = "版型/木型 一覽",     En = "Plate/Mold List",        Ja = "版型/木型 一覧",          Ko = "판형/목형 목록" },
            new Sys_Lang { LangKey = "nav.215", ZhCN = "版型/木型 登记",     ZhTW = "版型/木型 登記",     En = "Plate/Mold Entry",       Ja = "版型/木型 登録",          Ko = "판형/목형 등록" },
            new Sys_Lang { LangKey = "dashboard.title", ZhCN = "仪表盘", ZhTW = "儀表盤", En = "Dashboard", Ja = "ダッシュボード", Ko = "대시보드" },
            new Sys_Lang { LangKey = "dashboard.todayOps", ZhCN = "今日操作", ZhTW = "今日操作", En = "Today Ops", Ja = "本日の操作", Ko = "오늘 작업" },
            new Sys_Lang { LangKey = "dashboard.weekOps", ZhCN = "本周操作", ZhTW = "本週操作", En = "Week Ops", Ja = "今週の操作", Ko = "이번주 작업" },
            new Sys_Lang { LangKey = "dashboard.totalOps", ZhCN = "总操作数", ZhTW = "總操作數", En = "Total Ops", Ja = "総操作数", Ko = "총 작업수" },
            new Sys_Lang { LangKey = "dashboard.totalUsers", ZhCN = "用户数", ZhTW = "使用者數", En = "Users", Ja = "ユーザー数", Ko = "사용자수" },
            new Sys_Lang { LangKey = "dashboard.topControllers", ZhCN = "操作排行", ZhTW = "操作排行", En = "Top Controllers", Ja = "操作ランキング", Ko = "작업 순위" },
            new Sys_Lang { LangKey = "dashboard.trend", ZhCN = "7日趋势", ZhTW = "7日趨勢", En = "7-Day Trend", Ja = "7日間推移", Ko = "7일 추이" },
            new Sys_Lang { LangKey = "dashboard.methodDist", ZhCN = "方法分布", ZhTW = "方法分佈", En = "Method Distribution", Ja = "メソッド分布", Ko = "메서드 분포" },
            new Sys_Lang { LangKey = "dashboard.noData", ZhCN = "暂无数据", ZhTW = "暫無資料", En = "No data", Ja = "データなし", Ko = "데이터 없음" },
            new Sys_Lang { LangKey = "dashboard.count", ZhCN = "次数", ZhTW = "次數", En = "Count", Ja = "回数", Ko = "횟수" }
        );
        db.SaveChanges();
    }

    // ERP/MES 画面ラベルの多言語シード（日文原文＝キー）。既存キーはスキップして冪等にアップサート。
    {
        // ログイン画面など、追加の UI 文言（ドット区切りキー、5 言語）
        var extraUi = new[]
        {
            new Sys_Lang { LangKey = "login.subtitle", ZhCN = "输入账号信息后进入系统工作台。", ZhTW = "輸入帳號資訊後進入系統工作台。", En = "Enter your credentials to access the workspace.", Ja = "アカウント情報を入力してワークスペースへ進みます。", Ko = "계정 정보를 입력하여 워크스페이스로 들어갑니다." },
            new Sys_Lang { LangKey = "login.welcomeBack", ZhCN = "欢迎回来", ZhTW = "歡迎回來", En = "Welcome Back", Ja = "おかえりなさい", Ko = "다시 오신 것을 환영합니다" },
            new Sys_Lang { LangKey = "login.entering", ZhCN = "正在进入工作台…", ZhTW = "正在進入工作台…", En = "Entering Workspace...", Ja = "ワークスペースへ移動中…", Ko = "워크스페이스로 이동 중…" },
            new Sys_Lang { LangKey = "login.language", ZhCN = "语言", ZhTW = "語言", En = "Language", Ja = "言語", Ko = "언어" },
            new Sys_Lang { LangKey = "login.selectLanguage", ZhCN = "选择语言", ZhTW = "選擇語言", En = "Select language", Ja = "言語を選択", Ko = "언어 선택" },
            // i18n 优化 P1：后端错误码（DbStringLocalizer / BizException 用，无前缀的纯码）
            new Sys_Lang { LangKey = "E10022", ZhCN = "必填项未输入", ZhTW = "必填項未輸入", En = "Required field is empty", Ja = "必須項目に値が指定されていません", Ko = "필수 항목이 입력되지 않았습니다" },
            new Sys_Lang { LangKey = "lang.keyExists", ZhCN = "词条 Key 已存在", ZhTW = "詞條 Key 已存在", En = "Language key already exists", Ja = "ラベルキーは既に存在します", Ko = "라벨 키가 이미 존재합니다" },
            // i18n 优化 P2：CI 缺 key 校验抓出的真实缺失 key（补齐 5 语）
            new Sys_Lang { LangKey = "table.selectAll", ZhCN = "全选", ZhTW = "全選", En = "Select All", Ja = "全選択", Ko = "전체 선택" },
            new Sys_Lang { LangKey = "table.noData", ZhCN = "暂无数据", ZhTW = "暫無資料", En = "No Data", Ja = "データがありません", Ko = "데이터 없음" },
            new Sys_Lang { LangKey = "table.batchSelect", ZhCN = "批量选择", ZhTW = "批量選擇", En = "Batch Select", Ja = "一括選択", Ko = "일괄 선택" },
            new Sys_Lang { LangKey = "wms.common.close", ZhCN = "关闭", ZhTW = "關閉", En = "Close", Ja = "閉じる", Ko = "닫기" },
            new Sys_Lang { LangKey = "通貨", ZhCN = "货币", ZhTW = "貨幣", En = "Currency", Ja = "通貨", Ko = "통화" },
            // i18n 优化 P4：LangView 发布模式管理条
            new Sys_Lang { LangKey = "lang.currentVersion", ZhCN = "当前发布版本", ZhTW = "目前發佈版本", En = "Current Version", Ja = "現在の公開版", Ko = "현재 게시 버전" },
            new Sys_Lang { LangKey = "lang.notPublished", ZhCN = "未发布", ZhTW = "未發佈", En = "Not Published", Ja = "未公開", Ko = "미게시" },
            new Sys_Lang { LangKey = "lang.publish", ZhCN = "发布", ZhTW = "發佈", En = "Publish", Ja = "公開", Ko = "게시" },
            new Sys_Lang { LangKey = "lang.publishSuccess", ZhCN = "发布成功", ZhTW = "發佈成功", En = "Published", Ja = "公開しました", Ko = "게시되었습니다" },
            new Sys_Lang { LangKey = "lang.publishFailed", ZhCN = "发布失败", ZhTW = "發佈失敗", En = "Publish failed", Ja = "公開に失敗しました", Ko = "게시 실패" },
            // i18n 优化 P5：审校工作流
            new Sys_Lang { LangKey = "lang.status", ZhCN = "状态", ZhTW = "狀態", En = "Status", Ja = "状態", Ko = "상태" },
            new Sys_Lang { LangKey = "lang.reviewDrafts", ZhCN = "审校草稿", ZhTW = "審校草稿", En = "Review Drafts", Ja = "下書きを審査", Ko = "초안 검수" },
            new Sys_Lang { LangKey = "lang.reviewDone", ZhCN = "已审校 {n} 条", ZhTW = "已審校 {n} 筆", En = "Reviewed {n} entries", Ja = "{n} 件を審査済みにしました", Ko = "{n}건 검수 완료" },
            new Sys_Lang { LangKey = "lang.reviewFailed", ZhCN = "审校失败", ZhTW = "審校失敗", En = "Review failed", Ja = "審査に失敗しました", Ko = "검수 실패" },
        };

        var existingKeys = db.Sys_Langs.Select(l => l.LangKey).ToHashSet();
        var toAdd = CP6.WebApi.Seed.I18nLabelSeed.Items
            .Concat(extraUi)
            .Concat(CP6.WebApi.Seed.I18nErpScreenSeed.Items)   // 遗留① ERP 旧画面词条
            .Concat(CP6.WebApi.Seed.I18nMesScreenSeed.Items)   // 遗留① MES 画面词条
            .Concat(CP6.WebApi.Seed.I18nErpScreen2Seed.Items)  // 遗留① ERP 画面词条(第2批)
            .Concat(CP6.WebApi.Seed.I18nMiscScreenSeed.Items)  // 遗留① 组件/WMS/MES步骤 杂项词条
            .Concat(CP6.WebApi.Seed.I18nCnScreenSeed.Items)    // 遗留⑦ PMS/wf/Pub 中文画面词条
            .Concat(CP6.WebApi.Seed.I18nBackendMsgSeed.Items)  // 后端控制器 return 型响应文案
            .Concat(CP6.WebApi.Seed.I18nFinScreenSeed.Items)   // 财务 GL 内核 4 视图 + nav.6xx + E-FIN-* 错误码
            .Concat(CP6.WebApi.Seed.I18nPurScreenSeed.Items)   // 采购 MVP 4 视图 + nav.70x + E-PUR-* 错误码
            .Concat(CP6.WebApi.Seed.I18nWfDesignerSeed.Items)  // OA 阶段4 自研设计器（表单/流程）画面词条
            .Where(i => !existingKeys.Contains(i.LangKey))
            .GroupBy(i => i.LangKey).Select(g => g.First())     // 跨/内部 seed 去重，防 UX_Sys_Lang_Tenant_Key 唯一键冲突
            .ToList();
        if (toAdd.Count > 0)
        {
            db.Sys_Langs.AddRange(toAdd);
            db.SaveChanges();
        }

        // vue-i18n の特殊文字（@ | { }）はメッセージ編集でコンパイル失敗→画面/タブが空白になる。
        // 該当キーは転義済みシード値へ強制同期して歴史的な未転義データを修正（対象は極少数）。
        var specialChars = new[] { '@', '|', '{', '}' };
        foreach (var it in CP6.WebApi.Seed.I18nLabelSeed.Items)
        {
            if (it.LangKey.IndexOfAny(specialChars) < 0) continue;
            var row = db.Sys_Langs.FirstOrDefault(l => l.LangKey == it.LangKey);
            if (row != null)
            {
                row.ZhCN = it.ZhCN; row.ZhTW = it.ZhTW; row.En = it.En; row.Ja = it.Ja; row.Ko = it.Ko;
            }
        }
        db.SaveChanges();

        // 新增/修正词条后清除 Redis 语言缓存（lang:*），否则旧缓存（TTL 1h）会让新词条迟迟不显示。
        try
        {
            var cache = scope.ServiceProvider.GetRequiredService<CacheService>();
            foreach (var code in new[] { "zh-CN", "zh-TW", "en", "ja", "ko" })
                cache.RemoveAsync(CacheService.LangKeyPrefix + code).GetAwaiter().GetResult();
        }
        catch { /* 缓存清理失败不应阻断启动 */ }
    }

    // 补充：已有数据库追加导航菜单词条
    if (!db.Sys_Langs.Any(l => l.LangKey == "nav.100"))
    {
        db.Sys_Langs.AddRange(
            new Sys_Lang { LangKey = "nav.100", ZhCN = "系统管理", ZhTW = "系統管理", En = "System", Ja = "システム管理", Ko = "시스템 관리" },
            new Sys_Lang { LangKey = "nav.101", ZhCN = "角色管理", ZhTW = "角色管理", En = "Roles", Ja = "役割管理", Ko = "역할 관리" },
            new Sys_Lang { LangKey = "nav.102", ZhCN = "菜单管理", ZhTW = "選單管理", En = "Menus", Ja = "メニュー管理", Ko = "메뉴 관리" },
            new Sys_Lang { LangKey = "nav.103", ZhCN = "权限分配", ZhTW = "權限分配", En = "Permissions", Ja = "権限設定", Ko = "권한 설정" },
            new Sys_Lang { LangKey = "nav.104", ZhCN = "用户管理", ZhTW = "使用者管理", En = "Users", Ja = "ユーザー管理", Ko = "사용자 관리" },
            new Sys_Lang { LangKey = "nav.105", ZhCN = "多语言管理", ZhTW = "多語言管理", En = "Languages", Ja = "多言語管理", Ko = "다국어 관리" },
            new Sys_Lang { LangKey = "nav.106", ZhCN = "数据字典", ZhTW = "資料字典", En = "Dictionary", Ja = "データ辞書", Ko = "데이터 사전" },
            // 字典管理页面词条
            new Sys_Lang { LangKey = "dict.typeCode", ZhCN = "类型编码", ZhTW = "類型編碼", En = "Type Code", Ja = "タイプコード", Ko = "유형 코드" },
            new Sys_Lang { LangKey = "dict.typeName", ZhCN = "类型名称", ZhTW = "類型名稱", En = "Type Name", Ja = "タイプ名", Ko = "유형명" },
            new Sys_Lang { LangKey = "dict.value", ZhCN = "字典值", ZhTW = "字典值", En = "Value", Ja = "値", Ko = "값" },
            new Sys_Lang { LangKey = "dict.label", ZhCN = "显示文本", ZhTW = "顯示文字", En = "Label", Ja = "ラベル", Ko = "라벨" },
            new Sys_Lang { LangKey = "dict.orderNo", ZhCN = "排序", ZhTW = "排序", En = "Order", Ja = "順序", Ko = "순서" },
            new Sys_Lang { LangKey = "dict.enable", ZhCN = "启用", ZhTW = "啟用", En = "Enabled", Ja = "有効", Ko = "활성화" },
            new Sys_Lang { LangKey = "dict.backToTypes", ZhCN = "返回类型列表", ZhTW = "返回類型列表", En = "Back to types", Ja = "タイプ一覧に戻る", Ko = "유형 목록으로" },
            new Sys_Lang { LangKey = "dict.manageData", ZhCN = "管理字典项", ZhTW = "管理字典項", En = "Manage Items", Ja = "項目管理", Ko = "항목 관리" },
            // 操作日志词条
            new Sys_Lang { LangKey = "nav.107", ZhCN = "操作日志", ZhTW = "操作日誌", En = "Operation Log", Ja = "操作ログ", Ko = "작업 로그" },
            new Sys_Lang { LangKey = "operlog.user", ZhCN = "操作人", ZhTW = "操作人", En = "User", Ja = "操作者", Ko = "사용자" },
            new Sys_Lang { LangKey = "operlog.method", ZhCN = "方法", ZhTW = "方法", En = "Method", Ja = "メソッド", Ko = "메서드" },
            new Sys_Lang { LangKey = "operlog.url", ZhCN = "请求路径", ZhTW = "請求路徑", En = "URL", Ja = "URL", Ko = "URL" },
            new Sys_Lang { LangKey = "operlog.controller", ZhCN = "控制器", ZhTW = "控制器", En = "Controller", Ja = "コントローラー", Ko = "컨트롤러" },
            new Sys_Lang { LangKey = "operlog.status", ZhCN = "状态码", ZhTW = "狀態碼", En = "Status", Ja = "ステータス", Ko = "상태" },
            new Sys_Lang { LangKey = "operlog.elapsed", ZhCN = "耗时", ZhTW = "耗時", En = "Elapsed", Ja = "所要時間", Ko = "소요시간" },
            new Sys_Lang { LangKey = "operlog.time", ZhCN = "操作时间", ZhTW = "操作時間", En = "Time", Ja = "操作時間", Ko = "시간" },
            new Sys_Lang { LangKey = "operlog.detail", ZhCN = "详情", ZhTW = "詳情", En = "Detail", Ja = "詳細", Ko = "상세" },
            new Sys_Lang { LangKey = "operlog.requestBody", ZhCN = "请求参数", ZhTW = "請求參數", En = "Request Body", Ja = "リクエスト", Ko = "요청 본문" }
        );
        db.SaveChanges();
    }

    // 販売管理 i18n 扩展 Phase 3（PA010/030/100/130/140/150 等）— sentinel: sales.fsc.format
    if (!db.Sys_Langs.Any(l => l.LangKey == "sales.fsc.format"))
    {
        db.Sys_Langs.AddRange(
            // 见积计算书 / 御见积书 ────
            new Sys_Lang { LangKey = "sales.qtn.calcInquiry",   ZhCN = "见积计算书 照会", ZhTW = "見積計算書 照會",  En = "Estimate Calc Inquiry",Ja = "見積計算書 照会", Ko = "견적계산서 조회" },
            new Sys_Lang { LangKey = "sales.qtn.calcEntry",     ZhCN = "见积计算书 登录", ZhTW = "見積計算書 登錄",  En = "Estimate Calc Entry", Ja = "見積計算書 登録",  Ko = "견적계산서 등록" },
            new Sys_Lang { LangKey = "sales.qtn.qtnList",       ZhCN = "御见积书 一览",  ZhTW = "御見積書 一覽",   En = "Quotation List",      Ja = "御見積書 一覧",   Ko = "견적서 목록" },
            new Sys_Lang { LangKey = "sales.qtn.qtnEntry",      ZhCN = "御见积书 登录",  ZhTW = "御見積書 登錄",   En = "Quotation Entry",     Ja = "御見積書 登録",   Ko = "견적서 등록" },
            new Sys_Lang { LangKey = "sales.qtn.qtnDate",       ZhCN = "见积日",         ZhTW = "見積日",          En = "Quotation Date",      Ja = "見積日",          Ko = "견적일" },
            new Sys_Lang { LangKey = "sales.qtn.issueDate",     ZhCN = "发行日",         ZhTW = "發行日",          En = "Issue Date",          Ja = "発行日",          Ko = "발행일" },
            new Sys_Lang { LangKey = "sales.qtn.relatedCalcs",  ZhCN = "关联见积计算书", ZhTW = "關聯見積計算書",  En = "Related Calcs",       Ja = "関連見積書",      Ko = "관련 계산서" },
            new Sys_Lang { LangKey = "sales.qtn.headerCase",    ZhCN = "抬头/案件",     ZhTW = "抬頭/案件",       En = "Header / Case",       Ja = "ヘッダー/案件",   Ko = "머리글/사안" },
            new Sys_Lang { LangKey = "sales.qtn.printDetail",   ZhCN = "打印明细",      ZhTW = "列印明細",        En = "Print Detail",        Ja = "印字明細",        Ko = "인쇄 명세" },
            new Sys_Lang { LangKey = "sales.qtn.calcAdd",       ZhCN = "添加计算书",    ZhTW = "新增計算書",      En = "Add Calc",            Ja = "計算書追加",      Ko = "계산서 추가" },

            // FSC ────
            new Sys_Lang { LangKey = "sales.fsc.title",         ZhCN = "FSC 检查清单",   ZhTW = "FSC 檢查清單",   En = "FSC Checklist",       Ja = "FSC チェックシート", Ko = "FSC 체크시트" },
            new Sys_Lang { LangKey = "sales.fsc.format",        ZhCN = "输出格式",       ZhTW = "輸出格式",       En = "Output Format",       Ja = "出力フォーマット", Ko = "출력 형식" },
            new Sys_Lang { LangKey = "sales.fsc.unissued",      ZhCN = "未发行",         ZhTW = "未發行",         En = "Unissued",            Ja = "未発行",          Ko = "미발행" },
            new Sys_Lang { LangKey = "sales.fsc.issued",        ZhCN = "已发行",         ZhTW = "已發行",         En = "Issued",              Ja = "発行済",          Ko = "발행 완료" },
            new Sys_Lang { LangKey = "sales.fsc.mgmtNo",        ZhCN = "FSC 管理 NO",   ZhTW = "FSC 管理 NO",   En = "FSC Management NO",   Ja = "FSC 管理 NO",     Ko = "FSC 관리 NO" },
            new Sys_Lang { LangKey = "sales.fsc.case",          ZhCN = "案件 NO",       ZhTW = "案件 NO",       En = "Case NO",             Ja = "案件 No",         Ko = "사안 NO" },
            new Sys_Lang { LangKey = "sales.fsc.itemName1",     ZhCN = "客户品名 1",    ZhTW = "客戶品名 1",    En = "Customer Item 1",     Ja = "顧客品名 1",      Ko = "고객 품명 1" },
            new Sys_Lang { LangKey = "sales.fsc.itemName2",     ZhCN = "客户品名 2",    ZhTW = "客戶品名 2",    En = "Customer Item 2",     Ja = "顧客品名 2",      Ko = "고객 품명 2" },
            new Sys_Lang { LangKey = "sales.fsc.totalAmount",   ZhCN = "合计金额",      ZhTW = "合計金額",      En = "Total Amount",        Ja = "合計金額",        Ko = "합계 금액" },
            new Sys_Lang { LangKey = "sales.fsc.estimateQty",   ZhCN = "见积数",        ZhTW = "見積數",        En = "Estimate Qty",        Ja = "見積数",          Ko = "견적 수량" },
            new Sys_Lang { LangKey = "sales.fsc.confirmed",     ZhCN = "已确认",        ZhTW = "已確認",        En = "Confirmed",           Ja = "確定",            Ko = "확정됨" },
            new Sys_Lang { LangKey = "sales.fsc.notConfirmed",  ZhCN = "未确认",        ZhTW = "未確認",        En = "Not Confirmed",       Ja = "未",              Ko = "미확정" },

            // シート単価 ────
            new Sys_Lang { LangKey = "sales.sup.title",         ZhCN = "纸板单价主数据", ZhTW = "紙板單價主資料",En = "Sheet Unit Price",    Ja = "シート単価マスタ", Ko = "시트 단가 마스터" },
            new Sys_Lang { LangKey = "sales.sup.baseDate",      ZhCN = "基准日",         ZhTW = "基準日",        En = "Base Date",           Ja = "基準日",          Ko = "기준일" },
            new Sys_Lang { LangKey = "sales.sup.importDiv",     ZhCN = "导入区分",       ZhTW = "匯入區分",      En = "Import Div",          Ja = "取込区分",        Ko = "가져오기 구분" },
            new Sys_Lang { LangKey = "sales.sup.divStandard",   ZhCN = "纸板单价",       ZhTW = "紙板單價",      En = "Sheet Unit Price",    Ja = "シート単価",      Ko = "시트 단가" },
            new Sys_Lang { LangKey = "sales.sup.divEstimate",   ZhCN = "纸板单价(见积用)",ZhTW = "紙板單價(見積用)",En = "Sheet Unit Price (Est.)",Ja = "シート単価(見積用)",Ko = "시트 단가(견적용)" },
            new Sys_Lang { LangKey = "sales.sup.opType",        ZhCN = "操作类型",       ZhTW = "操作類型",      En = "Operation",           Ja = "操作種別",        Ko = "작업 종류" },
            new Sys_Lang { LangKey = "sales.sup.import",        ZhCN = "导入",           ZhTW = "匯入",          En = "Import",              Ja = "登録",            Ko = "가져오기" },
            new Sys_Lang { LangKey = "sales.sup.refer",         ZhCN = "查看",           ZhTW = "檢視",          En = "Refer",               Ja = "参照",            Ko = "조회" },
            new Sys_Lang { LangKey = "sales.sup.filePath",      ZhCN = "文件路径",       ZhTW = "檔案路徑",      En = "File Path",           Ja = "ファイルパス",    Ko = "파일 경로" },
            new Sys_Lang { LangKey = "sales.sup.selectExcel",   ZhCN = "选择 Excel",    ZhTW = "選擇 Excel",   En = "Select Excel",        Ja = "Excel 選択",      Ko = "Excel 선택" },
            new Sys_Lang { LangKey = "sales.sup.flute",         ZhCN = "段",             ZhTW = "段",            En = "Flute",               Ja = "段",              Ko = "단" },
            new Sys_Lang { LangKey = "sales.sup.front",         ZhCN = "表",             ZhTW = "表",            En = "Front",               Ja = "表",              Ko = "표" },
            new Sys_Lang { LangKey = "sales.sup.middle",        ZhCN = "中",             ZhTW = "中",            En = "Middle",              Ja = "中",              Ko = "중" },
            new Sys_Lang { LangKey = "sales.sup.back",          ZhCN = "裏",             ZhTW = "裏",            En = "Back",                Ja = "裏",              Ko = "이" },
            new Sys_Lang { LangKey = "sales.sup.paper",         ZhCN = "原纸",           ZhTW = "原紙",          En = "Paper",               Ja = "原紙",            Ko = "원지" },
            new Sys_Lang { LangKey = "sales.sup.print",         ZhCN = "印刷",           ZhTW = "印刷",          En = "Print",               Ja = "印刷",            Ko = "인쇄" },
            new Sys_Lang { LangKey = "sales.sup.emboss",        ZhCN = "压花",           ZhTW = "壓花",          En = "Emboss",              Ja = "エンボス",        Ko = "엠보싱" },
            new Sys_Lang { LangKey = "sales.sup.revisionDate",  ZhCN = "改定日",         ZhTW = "改定日",        En = "Revision Date",       Ja = "改定日",          Ko = "개정일" },
            new Sys_Lang { LangKey = "sales.sup.allSelect",     ZhCN = "全选/全否",      ZhTW = "全選/全否",     En = "Select / Clear All",  Ja = "全選択/全解除",   Ko = "전체 선택/해제" },

            // 版型 / 木型 ────
            new Sys_Lang { LangKey = "sales.pm.title",          ZhCN = "版型/木型",     ZhTW = "版型/木型",     En = "Plate / Mold",        Ja = "版型/木型",       Ko = "판형/목형" },
            new Sys_Lang { LangKey = "sales.pm.list",           ZhCN = "版型/木型 一览", ZhTW = "版型/木型 一覽",En = "Plate/Mold List",     Ja = "版型/木型 一覧",  Ko = "판형/목형 목록" },
            new Sys_Lang { LangKey = "sales.pm.entry",          ZhCN = "版型/木型 登录", ZhTW = "版型/木型 登錄",En = "Plate/Mold Entry",    Ja = "版型/木型 登録",  Ko = "판형/목형 등록" },
            new Sys_Lang { LangKey = "sales.pm.no",             ZhCN = "版型 NO",       ZhTW = "版型 NO",       En = "Plate NO",            Ja = "版型 NO",         Ko = "판형 NO" },
            new Sys_Lang { LangKey = "sales.pm.name",           ZhCN = "版型名",        ZhTW = "版型名",        En = "Plate Name",          Ja = "版型名",          Ko = "판형명" },
            new Sys_Lang { LangKey = "sales.pm.class",          ZhCN = "版型分类",      ZhTW = "版型分類",      En = "Plate Class",         Ja = "版型分類",        Ko = "판형 분류" },
            new Sys_Lang { LangKey = "sales.pm.newVersion",     ZhCN = "新版区分",      ZhTW = "新版區分",      En = "New Ver. Div",        Ja = "新版区分",        Ko = "신판 구분" },
            new Sys_Lang { LangKey = "sales.pm.process",        ZhCN = "工程",          ZhTW = "工程",          En = "Process",             Ja = "工程",            Ko = "공정" },
            new Sys_Lang { LangKey = "sales.pm.location",       ZhCN = "场所",          ZhTW = "場所",          En = "Location",            Ja = "場所",            Ko = "위치" },
            new Sys_Lang { LangKey = "sales.pm.shelfLine",      ZhCN = "棚位",          ZhTW = "棚位",          En = "Shelf Line",          Ja = "棚・ライン",      Ko = "선반 라인" },
            new Sys_Lang { LangKey = "sales.pm.passCount",      ZhCN = "通过次数",      ZhTW = "通過次數",      En = "Pass Count",          Ja = "通し数",          Ko = "통과 횟수" },
            new Sys_Lang { LangKey = "sales.pm.limitPass",      ZhCN = "极限通过次数",  ZhTW = "極限通過次數",  En = "Limit Pass",          Ja = "限界通し数",      Ko = "한계 통과" },
            new Sys_Lang { LangKey = "sales.pm.lastUsedDate",   ZhCN = "最后使用日",    ZhTW = "最後使用日",    En = "Last Used Date",      Ja = "最終使用実績日",  Ko = "마지막 사용일" },
            new Sys_Lang { LangKey = "sales.pm.arrivalDate",    ZhCN = "入货日",        ZhTW = "入貨日",        En = "Arrival Date",        Ja = "入荷日",          Ko = "입고일" },
            new Sys_Lang { LangKey = "sales.pm.dispScheduled",  ZhCN = "废弃预定日",    ZhTW = "廢棄預定日",    En = "Disposal Date",       Ja = "廃棄予定日",      Ko = "폐기 예정일" },
            new Sys_Lang { LangKey = "sales.pm.returnScheduled",ZhCN = "退还预定日",    ZhTW = "退還預定日",    En = "Return Sched. Date",  Ja = "返却予定日",      Ko = "반환 예정일" },
            new Sys_Lang { LangKey = "sales.pm.returnDate",     ZhCN = "退还日",        ZhTW = "退還日",        En = "Return Date",         Ja = "返却日",          Ko = "반환일" },
            new Sys_Lang { LangKey = "sales.pm.applyStartDate", ZhCN = "适用开始日",    ZhTW = "適用開始日",    En = "Apply Start Date",    Ja = "適用開始日",      Ko = "적용 시작일" },
            new Sys_Lang { LangKey = "sales.pm.applyEndDate",   ZhCN = "适用结束日",    ZhTW = "適用結束日",    En = "Apply End Date",      Ja = "適用終了日",      Ko = "적용 종료일" },
            new Sys_Lang { LangKey = "sales.pm.onlyLatestRev",  ZhCN = "仅最新 Rev",   ZhTW = "僅最新 Rev",   En = "Latest Rev Only",     Ja = "最新Revのみ",     Ko = "최신 Rev만" },
            new Sys_Lang { LangKey = "sales.pm.estimateNo",     ZhCN = "决定见积 NO",   ZhTW = "決定見積 NO",   En = "Decision Estimate NO",Ja = "決定見積 NO",     Ko = "확정 견적 NO" },
            new Sys_Lang { LangKey = "sales.pm.repProductCd",   ZhCN = "代表产品 CD",   ZhTW = "代表產品 CD",   En = "Repr. Product CD",    Ja = "代表製品 CD",     Ko = "대표 제품 CD" },
            new Sys_Lang { LangKey = "sales.pm.salesAvailable", ZhCN = "销售可否区分",  ZhTW = "銷售可否區分",  En = "Sales Available",     Ja = "売上可否区分",    Ko = "매출 가능 여부" },
            new Sys_Lang { LangKey = "sales.pm.endProcessLoc",  ZhCN = "最终加工场所",  ZhTW = "最終加工場所",  En = "Final Process Loc.",  Ja = "最終加工場所",    Ko = "최종 가공 장소" },
            new Sys_Lang { LangKey = "sales.pm.lastUsedActual", ZhCN = "实际日(最后使用)",ZhTW = "實際日(最後使用)",En = "Actual (Last Used)",Ja = "実績日(最終使用)",Ko = "실적일(마지막 사용)" }
        );
        db.SaveChanges();
    }

    // 販売管理 i18n 扩展（Phase 2 追加 keys）— sentinel: sales.role.customer
    if (!db.Sys_Langs.Any(l => l.LangKey == "sales.role.customer"))
    {
        db.Sys_Langs.AddRange(
            // Step 标题
            new Sys_Lang { LangKey = "sales.step.partsSelect",   ZhCN = "部材选择",     ZhTW = "部材選擇",     En = "Parts Select",       Ja = "部材選択",     Ko = "부재 선택" },
            new Sys_Lang { LangKey = "sales.step.detail",        ZhCN = "明细",         ZhTW = "明細",         En = "Detail",             Ja = "明細",         Ko = "명세" },
            // List 通用
            new Sys_Lang { LangKey = "sales.list.totalCount",    ZhCN = "合计 {n} 件",  ZhTW = "合計 {n} 件",  En = "Total {n} items",    Ja = "合計 {n} 件",  Ko = "합계 {n} 건" },
            new Sys_Lang { LangKey = "sales.list.no",            ZhCN = "序号",         ZhTW = "序號",         En = "No",                 Ja = "No",           Ko = "번호" },
            new Sys_Lang { LangKey = "sales.list.action",        ZhCN = "操作",         ZhTW = "操作",         En = "Action",             Ja = "操作",         Ko = "작업" },
            new Sys_Lang { LangKey = "sales.list.detail",        ZhCN = "详情",         ZhTW = "詳情",         En = "Detail",             Ja = "詳細",         Ko = "상세" },
            new Sys_Lang { LangKey = "sales.list.openDetail",    ZhCN = "打开详情",     ZhTW = "開啟詳情",     En = "Open Detail",        Ja = "詳細を開く",   Ko = "상세 열기" },
            // 搜索条件
            new Sys_Lang { LangKey = "sales.search.dateFrom",    ZhCN = "起始日期",     ZhTW = "起始日期",     En = "Date From",          Ja = "日付 FROM",    Ko = "시작일" },
            new Sys_Lang { LangKey = "sales.search.dateTo",      ZhCN = "结束日期",     ZhTW = "結束日期",     En = "Date To",            Ja = "日付 TO",      Ko = "종료일" },
            new Sys_Lang { LangKey = "sales.search.from",        ZhCN = "FROM",         ZhTW = "FROM",         En = "From",               Ja = "FROM",         Ko = "FROM" },
            new Sys_Lang { LangKey = "sales.search.to",          ZhCN = "TO",           ZhTW = "TO",           En = "To",                 Ja = "TO",           Ko = "TO" },
            new Sys_Lang { LangKey = "sales.search.required",    ZhCN = "必填",         ZhTW = "必填",         En = "Required",           Ja = "必須",         Ko = "필수" },
            // 业务角色
            new Sys_Lang { LangKey = "sales.role.customer",      ZhCN = "客户",         ZhTW = "客戶",         En = "Customer",           Ja = "得意先",       Ko = "거래처" },
            new Sys_Lang { LangKey = "sales.role.ar",            ZhCN = "应收",         ZhTW = "應收",         En = "Accounts Rec.",      Ja = "売掛先",       Ko = "외상매출처" },
            new Sys_Lang { LangKey = "sales.role.billing",       ZhCN = "请款",         ZhTW = "請款",         En = "Billing",            Ja = "請求先",       Ko = "청구처" },
            new Sys_Lang { LangKey = "sales.role.receipt",       ZhCN = "收款",         ZhTW = "收款",         En = "Receipt",            Ja = "入金先",       Ko = "입금처" },
            new Sys_Lang { LangKey = "sales.role.delivery",      ZhCN = "纳品",         ZhTW = "納品",         En = "Delivery",           Ja = "納品先",       Ko = "납품처" },
            new Sys_Lang { LangKey = "sales.role.creditMgmt",    ZhCN = "信用管理",     ZhTW = "信用管理",     En = "Credit Mgmt",        Ja = "与信管理",     Ko = "신용 관리" },
            new Sys_Lang { LangKey = "sales.role.supplier",      ZhCN = "供应商",       ZhTW = "供應商",       En = "Supplier",           Ja = "発注先",       Ko = "공급사" },
            new Sys_Lang { LangKey = "sales.role.ap",            ZhCN = "应付",         ZhTW = "應付",         En = "Accounts Pay.",      Ja = "買掛先",       Ko = "외상매입처" },
            new Sys_Lang { LangKey = "sales.role.paymentSch",    ZhCN = "付款计划",     ZhTW = "付款計劃",     En = "Payment Sched.",     Ja = "支払予定管理先",Ko = "지급 예정" },
            new Sys_Lang { LangKey = "sales.role.payment",       ZhCN = "付款",         ZhTW = "付款",         En = "Payment",            Ja = "支払先",       Ko = "지급처" },
            new Sys_Lang { LangKey = "sales.role.maker",         ZhCN = "厂商",         ZhTW = "廠商",         En = "Maker",              Ja = "メーカ",       Ko = "제조사" },
            // 单价订正
            new Sys_Lang { LangKey = "sales.pc.before",          ZhCN = "变更前",       ZhTW = "變更前",       En = "Before",             Ja = "変更前",       Ko = "변경 전" },
            new Sys_Lang { LangKey = "sales.pc.after",           ZhCN = "变更后",       ZhTW = "變更後",       En = "After",              Ja = "変更後",       Ko = "변경 후" },
            new Sys_Lang { LangKey = "sales.pc.reason",          ZhCN = "变更理由",     ZhTW = "變更理由",     En = "Change Reason",      Ja = "単価変更理由", Ko = "변경 사유" },
            new Sys_Lang { LangKey = "sales.pc.provisional",     ZhCN = "暂定价",       ZhTW = "暫定價",       En = "Provisional",        Ja = "仮単価",       Ko = "잠정가" },
            new Sys_Lang { LangKey = "sales.pc.updateSelected",  ZhCN = "更新选中行",   ZhTW = "更新選取行",   En = "Update Selected",    Ja = "選択行を更新", Ko = "선택 행 업데이트" },
            // 受注一览
            new Sys_Lang { LangKey = "sales.order.consignedSale",ZhCN = "代销",         ZhTW = "代銷",         En = "Consigned",          Ja = "預り売上",     Ko = "위탁 매출" },
            new Sys_Lang { LangKey = "sales.order.mcUntransferred",ZhCN = "未转送",     ZhTW = "未轉送",       En = "Not Transferred",    Ja = "mc未転送",     Ko = "미전송" }
        );
        db.SaveChanges();
    }

    // 补充：已有数据库追加字典相关词条
    if (!db.Sys_Langs.Any(l => l.LangKey == "nav.106"))
    {
        db.Sys_Langs.AddRange(
            new Sys_Lang { LangKey = "nav.106", ZhCN = "数据字典", ZhTW = "資料字典", En = "Dictionary", Ja = "データ辞書", Ko = "데이터 사전" },
            new Sys_Lang { LangKey = "dict.typeCode", ZhCN = "类型编码", ZhTW = "類型編碼", En = "Type Code", Ja = "タイプコード", Ko = "유형 코드" },
            new Sys_Lang { LangKey = "dict.typeName", ZhCN = "类型名称", ZhTW = "類型名稱", En = "Type Name", Ja = "タイプ名", Ko = "유형명" },
            new Sys_Lang { LangKey = "dict.value", ZhCN = "字典值", ZhTW = "字典值", En = "Value", Ja = "値", Ko = "값" },
            new Sys_Lang { LangKey = "dict.label", ZhCN = "显示文本", ZhTW = "顯示文字", En = "Label", Ja = "ラベル", Ko = "라벨" },
            new Sys_Lang { LangKey = "dict.orderNo", ZhCN = "排序", ZhTW = "排序", En = "Order", Ja = "順序", Ko = "순서" },
            new Sys_Lang { LangKey = "dict.enable", ZhCN = "启用", ZhTW = "啟用", En = "Enabled", Ja = "有効", Ko = "활성화" },
            new Sys_Lang { LangKey = "dict.backToTypes", ZhCN = "返回类型列表", ZhTW = "返回類型列表", En = "Back to types", Ja = "タイプ一覧に戻る", Ko = "유형 목록으로" },
            new Sys_Lang { LangKey = "dict.manageData", ZhCN = "管理字典项", ZhTW = "管理字典項", En = "Manage Items", Ja = "項目管理", Ko = "항목 관리" }
        );
        db.SaveChanges();
    }

    // 补充：操作日志相关词条
    if (!db.Sys_Langs.Any(l => l.LangKey == "nav.107"))
    {
        db.Sys_Langs.AddRange(
            new Sys_Lang { LangKey = "nav.107", ZhCN = "操作日志", ZhTW = "操作日誌", En = "Operation Log", Ja = "操作ログ", Ko = "작업 로그" },
            new Sys_Lang { LangKey = "operlog.user", ZhCN = "操作人", ZhTW = "操作人", En = "User", Ja = "操作者", Ko = "사용자" },
            new Sys_Lang { LangKey = "operlog.method", ZhCN = "方法", ZhTW = "方法", En = "Method", Ja = "メソッド", Ko = "메서드" },
            new Sys_Lang { LangKey = "operlog.url", ZhCN = "请求路径", ZhTW = "請求路徑", En = "URL", Ja = "URL", Ko = "URL" },
            new Sys_Lang { LangKey = "operlog.controller", ZhCN = "控制器", ZhTW = "控制器", En = "Controller", Ja = "コントローラー", Ko = "컨트롤러" },
            new Sys_Lang { LangKey = "operlog.status", ZhCN = "状态码", ZhTW = "狀態碼", En = "Status", Ja = "ステータス", Ko = "상태" },
            new Sys_Lang { LangKey = "operlog.elapsed", ZhCN = "耗时", ZhTW = "耗時", En = "Elapsed", Ja = "所要時間", Ko = "소요시간" },
            new Sys_Lang { LangKey = "operlog.time", ZhCN = "操作时间", ZhTW = "操作時間", En = "Time", Ja = "操作時間", Ko = "시간" },
            new Sys_Lang { LangKey = "operlog.detail", ZhCN = "详情", ZhTW = "詳情", En = "Detail", Ja = "詳細", Ko = "상세" },
            new Sys_Lang { LangKey = "operlog.requestBody", ZhCN = "请求参数", ZhTW = "請求參數", En = "Request Body", Ja = "リクエスト", Ko = "요청 본문" }
        );
        db.SaveChanges();
    }

    // 补充：仪表盘菜单和词条
    if (!db.Sys_Menus.Any(m => m.MenuId == 2))
    {
        db.Sys_Menus.Add(new Sys_Menu { MenuId = 2, MenuName = "仪表盘", RoutePath = "/dashboard", Icon = "Odometer", OrderNo = 0, Enable = true });
        db.Sys_RoleMenus.Add(new Sys_RoleMenu { RoleId = 1, MenuId = 2 });
        db.SaveChanges();
    }
    if (!db.Sys_Langs.Any(l => l.LangKey == "nav.2"))
    {
        db.Sys_Langs.AddRange(
            new Sys_Lang { LangKey = "nav.2", ZhCN = "仪表盘", ZhTW = "儀表盤", En = "Dashboard", Ja = "ダッシュボード", Ko = "대시보드" },
            new Sys_Lang { LangKey = "dashboard.title", ZhCN = "仪表盘", ZhTW = "儀表盤", En = "Dashboard", Ja = "ダッシュボード", Ko = "대시보드" },
            new Sys_Lang { LangKey = "dashboard.todayOps", ZhCN = "今日操作", ZhTW = "今日操作", En = "Today Ops", Ja = "本日の操作", Ko = "오늘 작업" },
            new Sys_Lang { LangKey = "dashboard.weekOps", ZhCN = "本周操作", ZhTW = "本週操作", En = "Week Ops", Ja = "今週の操作", Ko = "이번주 작업" },
            new Sys_Lang { LangKey = "dashboard.totalOps", ZhCN = "总操作数", ZhTW = "總操作數", En = "Total Ops", Ja = "総操作数", Ko = "총 작업수" },
            new Sys_Lang { LangKey = "dashboard.totalUsers", ZhCN = "用户数", ZhTW = "使用者數", En = "Users", Ja = "ユーザー数", Ko = "사용자수" },
            new Sys_Lang { LangKey = "dashboard.topControllers", ZhCN = "操作排行", ZhTW = "操作排行", En = "Top Controllers", Ja = "操作ランキング", Ko = "작업 순위" },
            new Sys_Lang { LangKey = "dashboard.trend", ZhCN = "7日趋势", ZhTW = "7日趨勢", En = "7-Day Trend", Ja = "7日間推移", Ko = "7일 추이" },
            new Sys_Lang { LangKey = "dashboard.methodDist", ZhCN = "方法分布", ZhTW = "方法分佈", En = "Method Distribution", Ja = "メソッド分布", Ko = "메서드 분포" },
            new Sys_Lang { LangKey = "dashboard.noData", ZhCN = "暂无数据", ZhTW = "暫無資料", En = "No data", Ja = "データなし", Ko = "데이터 없음" },
            new Sys_Lang { LangKey = "dashboard.count", ZhCN = "次数", ZhTW = "次數", En = "Count", Ja = "回数", Ko = "횟수" }
        );
        db.SaveChanges();
    }

    // 业务经营总览 仪表盘 i18n（幂等：以 dashboard.todayOrders 为哨兵）
    if (!db.Sys_Langs.Any(l => l.LangKey == "dashboard.todayOrders"))
    {
        db.Sys_Langs.AddRange(
            // ── KPI 卡 ──
            new Sys_Lang { LangKey = "dashboard.overview",        ZhCN = "业务经营总览", ZhTW = "業務經營總覽", En = "Business Overview", Ja = "業務経営総覧", Ko = "비즈니스 개요" },
            new Sys_Lang { LangKey = "dashboard.todayOrders",     ZhCN = "今日受注",   ZhTW = "今日受注",   En = "Today Orders",    Ja = "今日の受注",   Ko = "오늘 수주" },
            new Sys_Lang { LangKey = "dashboard.monthOrders",     ZhCN = "本月受注",   ZhTW = "本月受注",   En = "Month Orders",    Ja = "今月の受注",   Ko = "당월 수주" },
            new Sys_Lang { LangKey = "dashboard.activeWorkOrders",ZhCN = "在制指令",   ZhTW = "在製指令",   En = "Active WO",       Ja = "製造中指図",   Ko = "제조중 지시" },
            new Sys_Lang { LangKey = "dashboard.monthCompleted",  ZhCN = "本月完工",   ZhTW = "本月完工",   En = "Completed (M)",   Ja = "今月完成",     Ko = "당월 완성" },
            new Sys_Lang { LangKey = "dashboard.pendingOutbound", ZhCN = "待出货",     ZhTW = "待出貨",     En = "Pending Ship",    Ja = "出荷待ち",     Ko = "출하 대기" },
            new Sys_Lang { LangKey = "dashboard.stockWarnings",   ZhCN = "库存预警",   ZhTW = "庫存預警",   En = "Stock Alerts",    Ja = "在庫警告",     Ko = "재고 경고" },
            new Sys_Lang { LangKey = "dashboard.pendingApprovals",ZhCN = "待审批",     ZhTW = "待審批",     En = "Pending Approval",Ja = "承認待ち",     Ko = "승인 대기" },
            new Sys_Lang { LangKey = "dashboard.totalProducts",   ZhCN = "产品总数",   ZhTW = "產品總數",   En = "Products",        Ja = "製品マスタ数", Ko = "제품 수" },
            // ── 快捷入口 ──
            new Sys_Lang { LangKey = "dashboard.quickEntry",      ZhCN = "快捷入口",   ZhTW = "快捷入口",   En = "Quick Entry",     Ja = "クイック入力", Ko = "빠른 입력" },
            new Sys_Lang { LangKey = "dashboard.qOrder",          ZhCN = "受注入力",   ZhTW = "受注入力",   En = "New Order",       Ja = "受注入力",     Ko = "수주 입력" },
            new Sys_Lang { LangKey = "dashboard.qWorkOrder",      ZhCN = "制造指令",   ZhTW = "製造指令",   En = "Work Order",      Ja = "製造指図",     Ko = "제조 지시" },
            new Sys_Lang { LangKey = "dashboard.qInbound",        ZhCN = "入库实绩",   ZhTW = "入庫實績",   En = "Inbound",         Ja = "入庫実績",     Ko = "입고 실적" },
            new Sys_Lang { LangKey = "dashboard.qOutbound",       ZhCN = "出库指示",   ZhTW = "出庫指示",   En = "Outbound",        Ja = "出庫指示",     Ko = "출고 지시" },
            new Sys_Lang { LangKey = "dashboard.qStock",          ZhCN = "库存照会",   ZhTW = "庫存照會",   En = "Stock Query",     Ja = "在庫照会",     Ko = "재고 조회" },
            new Sys_Lang { LangKey = "dashboard.qStockTake",      ZhCN = "盘点",       ZhTW = "盤點",       En = "Stock Take",      Ja = "棚卸",         Ko = "재고 실사" },
            // ── 面板标题 ──
            new Sys_Lang { LangKey = "dashboard.recentOrders",    ZhCN = "最近受注",   ZhTW = "最近受注",   En = "Recent Orders",   Ja = "最近の受注",   Ko = "최근 수주" },
            new Sys_Lang { LangKey = "dashboard.workOrderStatus", ZhCN = "制造进度",   ZhTW = "製造進度",   En = "WO Status",       Ja = "製造ステータス",Ko = "제조 상태" },
            new Sys_Lang { LangKey = "dashboard.liveFeed",        ZhCN = "实时业务通知", ZhTW = "即時業務通知", En = "Live Notifications", Ja = "リアルタイム通知", Ko = "실시간 알림" },
            new Sys_Lang { LangKey = "dashboard.waitingFeed",     ZhCN = "等待业务通知…", ZhTW = "等待業務通知…", En = "Waiting for activity…", Ja = "業務通知を待機中…", Ko = "알림 대기 중…" },
            // ── 表格列 ──
            new Sys_Lang { LangKey = "dashboard.orderNo",         ZhCN = "受注NO",     ZhTW = "受注NO",     En = "Order No",        Ja = "受注NO",       Ko = "수주 NO" },
            new Sys_Lang { LangKey = "dashboard.customer",        ZhCN = "得意先",     ZhTW = "得意先",     En = "Customer",        Ja = "得意先",       Ko = "거래처" },
            new Sys_Lang { LangKey = "dashboard.qty",             ZhCN = "数量",       ZhTW = "數量",       En = "Qty",             Ja = "数量",         Ko = "수량" },
            new Sys_Lang { LangKey = "dashboard.shipStatus",      ZhCN = "出货状态",   ZhTW = "出貨狀態",   En = "Ship Status",     Ja = "出荷状態",     Ko = "출하 상태" },
            // ── 出荷ステータス ──
            new Sys_Lang { LangKey = "dashboard.ship0",           ZhCN = "未出货",     ZhTW = "未出貨",     En = "Unshipped",       Ja = "未出荷",       Ko = "미출하" },
            new Sys_Lang { LangKey = "dashboard.ship5",           ZhCN = "部分出货",   ZhTW = "部分出貨",   En = "Partial",         Ja = "一部出荷",     Ko = "일부 출하" },
            new Sys_Lang { LangKey = "dashboard.ship9",           ZhCN = "已出货",     ZhTW = "已出貨",     En = "Shipped",         Ja = "出荷済",       Ko = "출하 완료" },
            // ── 製造指図ステータス ──
            new Sys_Lang { LangKey = "dashboard.wo0",             ZhCN = "草稿",       ZhTW = "草稿",       En = "Draft",           Ja = "下書き",       Ko = "초안" },
            new Sys_Lang { LangKey = "dashboard.wo1",             ZhCN = "已确定",     ZhTW = "已確定",     En = "Confirmed",       Ja = "確定済",       Ko = "확정" },
            new Sys_Lang { LangKey = "dashboard.wo2",             ZhCN = "已发行",     ZhTW = "已發行",     En = "Issued",          Ja = "発行済",       Ko = "발행" },
            new Sys_Lang { LangKey = "dashboard.wo3",             ZhCN = "进行中",     ZhTW = "進行中",     En = "In Progress",     Ja = "着手中",       Ko = "진행중" },
            new Sys_Lang { LangKey = "dashboard.wo4",             ZhCN = "已完成",     ZhTW = "已完成",     En = "Completed",       Ja = "完了",         Ko = "완료" },
            new Sys_Lang { LangKey = "dashboard.wo5",             ZhCN = "中断中",     ZhTW = "中斷中",     En = "Suspended",       Ja = "中断中",       Ko = "중단중" },
            new Sys_Lang { LangKey = "dashboard.wo6",             ZhCN = "已检验",     ZhTW = "已檢驗",     En = "Inspected",       Ja = "検査済",       Ko = "검사완료" },
            new Sys_Lang { LangKey = "dashboard.wo9",             ZhCN = "已取消",     ZhTW = "已取消",     En = "Cancelled",       Ja = "取消",         Ko = "취소" }
        );
        db.SaveChanges();
    }

    // 販売管理 通用 i18n（操作模式 / 按钮 / Section / 错误消息 / 业务术语）
    if (!db.Sys_Langs.Any(l => l.LangKey == "sales.op.register"))
    {
        db.Sys_Langs.AddRange(
            // 操作模式 ────────────────────
            new Sys_Lang { LangKey = "sales.op.register",     ZhCN = "登记",     ZhTW = "登記",     En = "New",          Ja = "登録",       Ko = "등록" },
            new Sys_Lang { LangKey = "sales.op.edit",         ZhCN = "修正",     ZhTW = "修正",     En = "Edit",         Ja = "訂正",       Ko = "수정" },
            new Sys_Lang { LangKey = "sales.op.copy",         ZhCN = "复制",     ZhTW = "複製",     En = "Copy",         Ja = "流用",       Ko = "복사" },
            new Sys_Lang { LangKey = "sales.op.view",         ZhCN = "查看",     ZhTW = "檢視",     En = "View",         Ja = "参照",       Ko = "조회" },
            new Sys_Lang { LangKey = "sales.op.delete",       ZhCN = "删除",     ZhTW = "刪除",     En = "Delete",       Ja = "削除",       Ko = "삭제" },
            new Sys_Lang { LangKey = "sales.op.revise",       ZhCN = "改定",     ZhTW = "改定",     En = "Revise",       Ja = "改定",       Ko = "개정" },
            new Sys_Lang { LangKey = "sales.op.preregister",  ZhCN = "事前登记", ZhTW = "事前登記", En = "Pre-Register", Ja = "事前登録",   Ko = "사전등록" },

            // 通用按钮 ────────────────────
            new Sys_Lang { LangKey = "sales.btn.new",         ZhCN = "新建",     ZhTW = "新建",     En = "New",          Ja = "新規",       Ko = "신규" },
            new Sys_Lang { LangKey = "sales.btn.save",        ZhCN = "保存",     ZhTW = "儲存",     En = "Save",         Ja = "保存",       Ko = "저장" },
            new Sys_Lang { LangKey = "sales.btn.register",    ZhCN = "登记",     ZhTW = "登記",     En = "Register",     Ja = "登録",       Ko = "등록" },
            new Sys_Lang { LangKey = "sales.btn.delete",      ZhCN = "删除执行", ZhTW = "刪除執行", En = "Delete",       Ja = "削除実行",   Ko = "삭제 실행" },
            new Sys_Lang { LangKey = "sales.btn.cancel",      ZhCN = "取消",     ZhTW = "取消",     En = "Cancel",       Ja = "キャンセル", Ko = "취소" },
            new Sys_Lang { LangKey = "sales.btn.clear",       ZhCN = "清空",     ZhTW = "清空",     En = "Clear",        Ja = "クリア",     Ko = "지우기" },
            new Sys_Lang { LangKey = "sales.btn.search",      ZhCN = "检索",     ZhTW = "檢索",     En = "Search",       Ja = "検索",       Ko = "검색" },
            new Sys_Lang { LangKey = "sales.btn.show",        ZhCN = "显示",     ZhTW = "顯示",     En = "Show",         Ja = "表示",       Ko = "표시" },
            new Sys_Lang { LangKey = "sales.btn.load",        ZhCN = "读取",     ZhTW = "讀取",     En = "Load",         Ja = "読込",       Ko = "불러오기" },
            new Sys_Lang { LangKey = "sales.btn.import",      ZhCN = "导入",     ZhTW = "匯入",     En = "Import",       Ja = "引入",       Ko = "가져오기" },
            new Sys_Lang { LangKey = "sales.btn.export",      ZhCN = "导出",     ZhTW = "匯出",     En = "Export",       Ja = "出力",       Ko = "내보내기" },
            new Sys_Lang { LangKey = "sales.btn.exportCsv",   ZhCN = "CSV 导出", ZhTW = "CSV 匯出", En = "CSV Export",   Ja = "CSV 出力",   Ko = "CSV 내보내기" },
            new Sys_Lang { LangKey = "sales.btn.issue",       ZhCN = "发行",     ZhTW = "發行",     En = "Issue",        Ja = "発行",       Ko = "발행" },
            new Sys_Lang { LangKey = "sales.btn.confirm",     ZhCN = "确认",     ZhTW = "確認",     En = "Confirm",      Ja = "確認",       Ko = "확인" },
            new Sys_Lang { LangKey = "sales.btn.confirmReg",  ZhCN = "确认登记", ZhTW = "確認登記", En = "Confirm",      Ja = "確認登録",   Ko = "확정 등록" },
            new Sys_Lang { LangKey = "sales.btn.confirmCancel",ZhCN = "取消确认",ZhTW = "取消確認", En = "Unconfirm",    Ja = "確認取消",   Ko = "확정 취소" },
            new Sys_Lang { LangKey = "sales.btn.next",        ZhCN = "下一步",   ZhTW = "下一步",   En = "Next",         Ja = "次へ",       Ko = "다음" },
            new Sys_Lang { LangKey = "sales.btn.prev",        ZhCN = "上一步",   ZhTW = "上一步",   En = "Previous",     Ja = "前へ",       Ko = "이전" },
            new Sys_Lang { LangKey = "sales.btn.addRow",      ZhCN = "添加行",   ZhTW = "新增行",   En = "Add Row",      Ja = "行追加",     Ko = "행 추가" },
            new Sys_Lang { LangKey = "sales.btn.delRow",      ZhCN = "删除行",   ZhTW = "刪除行",   En = "Delete Row",   Ja = "行削除",     Ko = "행 삭제" },
            new Sys_Lang { LangKey = "sales.btn.copyRow",     ZhCN = "复制行",   ZhTW = "複製行",   En = "Copy Row",     Ja = "行コピー",   Ko = "행 복사" },
            new Sys_Lang { LangKey = "sales.btn.label",       ZhCN = "标签发行", ZhTW = "標籤發行", En = "Label",        Ja = "ラベル発行", Ko = "라벨 발행" },
            new Sys_Lang { LangKey = "sales.btn.purchaseOrder",ZhCN = "采购订单",ZhTW = "採購訂單", En = "Purchase Order",Ja = "版型発注書",Ko = "구매 주문서" },
            new Sys_Lang { LangKey = "sales.btn.update",      ZhCN = "更新",     ZhTW = "更新",     En = "Update",       Ja = "更新",       Ko = "업데이트" },
            new Sys_Lang { LangKey = "sales.btn.selectReturn", ZhCN = "选择并返回", ZhTW = "選擇並返回", En = "Select & Return", Ja = "選択して戻る", Ko = "선택 후 돌아가기" },
            new Sys_Lang { LangKey = "sales.btn.openView",    ZhCN = "以查看模式打开", ZhTW = "以檢視模式開啟", En = "Open in View Mode", Ja = "参照モードで開く", Ko = "조회 모드로 열기" },

            // 业务通用术语 ────────────────────
            new Sys_Lang { LangKey = "sales.term.base",       ZhCN = "据点",     ZhTW = "據點",     En = "Base",         Ja = "拠点",       Ko = "거점" },
            new Sys_Lang { LangKey = "sales.term.staff",      ZhCN = "担当者",   ZhTW = "擔當者",   En = "Staff",        Ja = "担当者",     Ko = "담당자" },
            new Sys_Lang { LangKey = "sales.term.customer",   ZhCN = "客户",     ZhTW = "客戶",     En = "Customer",     Ja = "得意先",     Ko = "거래처" },
            new Sys_Lang { LangKey = "sales.term.customerCd", ZhCN = "客户 CD",  ZhTW = "客戶 CD",  En = "Customer CD",  Ja = "得意先 CD",  Ko = "거래처 CD" },
            new Sys_Lang { LangKey = "sales.term.supplier",   ZhCN = "供应商",   ZhTW = "供應商",   En = "Supplier",     Ja = "発注先",     Ko = "공급사" },
            new Sys_Lang { LangKey = "sales.term.deliveryTo", ZhCN = "纳品先",   ZhTW = "納品先",   En = "Delivery To",  Ja = "納品先",     Ko = "납품처" },
            new Sys_Lang { LangKey = "sales.term.salesStaff", ZhCN = "营业担当", ZhTW = "業務擔當", En = "Sales Staff",  Ja = "営業担当",   Ko = "영업 담당" },
            new Sys_Lang { LangKey = "sales.term.businessStaff",ZhCN = "业务担当",ZhTW = "業務擔當",En = "Business Staff",Ja = "業務担当",  Ko = "업무 담당" },
            new Sys_Lang { LangKey = "sales.term.product",    ZhCN = "产品",     ZhTW = "產品",     En = "Product",      Ja = "製品",       Ko = "제품" },
            new Sys_Lang { LangKey = "sales.term.productCd",  ZhCN = "产品 CD",  ZhTW = "產品 CD",  En = "Product CD",   Ja = "製品 CD",    Ko = "제품 CD" },
            new Sys_Lang { LangKey = "sales.term.itemCd",     ZhCN = "品目 CD",  ZhTW = "品目 CD",  En = "Item CD",      Ja = "品目 CD",    Ko = "품목 CD" },
            new Sys_Lang { LangKey = "sales.term.orderType",  ZhCN = "受注区分", ZhTW = "受注區分", En = "Order Type",   Ja = "受注区分",   Ko = "수주 구분" },
            new Sys_Lang { LangKey = "sales.term.orderDate",  ZhCN = "受注日",   ZhTW = "受注日",   En = "Order Date",   Ja = "受注日",     Ko = "수주일" },
            new Sys_Lang { LangKey = "sales.term.deliveryDate",ZhCN = "客户纳期",ZhTW = "客戶納期", En = "Delivery Date",Ja = "客先納期",   Ko = "납기일" },
            new Sys_Lang { LangKey = "sales.term.orderSheet", ZhCN = "订单 NO",  ZhTW = "訂單 NO",  En = "Order Sheet NO",Ja = "注文書 NO", Ko = "주문서 NO" },
            new Sys_Lang { LangKey = "sales.term.haibaiNo",   ZhCN = "手配 NO",  ZhTW = "手配 NO",  En = "Arrange NO",   Ja = "手配 NO",    Ko = "수배 NO" },
            new Sys_Lang { LangKey = "sales.term.amount",     ZhCN = "金额",     ZhTW = "金額",     En = "Amount",       Ja = "金額",       Ko = "금액" },
            new Sys_Lang { LangKey = "sales.term.qty",        ZhCN = "数量",     ZhTW = "數量",     En = "Quantity",     Ja = "数量",       Ko = "수량" },
            new Sys_Lang { LangKey = "sales.term.unit",       ZhCN = "单位",     ZhTW = "單位",     En = "Unit",         Ja = "単位",       Ko = "단위" },
            new Sys_Lang { LangKey = "sales.term.unitPrice",  ZhCN = "单价",     ZhTW = "單價",     En = "Unit Price",   Ja = "単価",       Ko = "단가" },
            new Sys_Lang { LangKey = "sales.term.indPrice",   ZhCN = "个别单价", ZhTW = "個別單價", En = "Ind. Price",   Ja = "個別単価",   Ko = "개별 단가" },
            new Sys_Lang { LangKey = "sales.term.setPrice",   ZhCN = "套装单价", ZhTW = "套裝單價", En = "Set Price",    Ja = "セット単価", Ko = "세트 단가" },
            new Sys_Lang { LangKey = "sales.term.special",    ZhCN = "特价",     ZhTW = "特價",     En = "Special",      Ja = "特値",       Ko = "특가" },
            new Sys_Lang { LangKey = "sales.term.status",     ZhCN = "状态",     ZhTW = "狀態",     En = "Status",       Ja = "ステータス", Ko = "상태" },
            new Sys_Lang { LangKey = "sales.term.qtnNo",      ZhCN = "见积NO",   ZhTW = "見積NO",   En = "Quotation NO", Ja = "御見積書 NO",Ko = "견적서 NO" },
            new Sys_Lang { LangKey = "sales.term.calcNo",     ZhCN = "计算书NO", ZhTW = "計算書NO", En = "Calc NO",      Ja = "見積計算書 NO",Ko = "계산서 NO" },
            new Sys_Lang { LangKey = "sales.term.fsc",        ZhCN = "FSC",      ZhTW = "FSC",      En = "FSC",          Ja = "FSC",        Ko = "FSC" },
            new Sys_Lang { LangKey = "sales.term.rev",        ZhCN = "Rev",      ZhTW = "Rev",      En = "Rev",          Ja = "Rev",        Ko = "Rev" },
            new Sys_Lang { LangKey = "sales.term.bp",         ZhCN = "交易先",   ZhTW = "交易夥伴", En = "Partner",      Ja = "取引先",     Ko = "거래처" },
            new Sys_Lang { LangKey = "sales.term.bpCd",       ZhCN = "交易先 CD",ZhTW = "交易夥伴 CD",En = "Partner CD", Ja = "取引先 CD",  Ko = "거래처 CD" },
            new Sys_Lang { LangKey = "sales.term.bpName",     ZhCN = "交易先名", ZhTW = "交易夥伴名稱",En = "Partner Name",Ja = "取引先名",  Ko = "거래처명" },

            // Section / Step 名 ────────────────────
            new Sys_Lang { LangKey = "sales.section.basicInfo",  ZhCN = "基本信息",     ZhTW = "基本資訊",     En = "Basic Info",         Ja = "基本情報",     Ko = "기본 정보" },
            new Sys_Lang { LangKey = "sales.section.composition",ZhCN = "构成信息",     ZhTW = "構成資訊",     En = "Composition",        Ja = "構成情報",     Ko = "구성 정보" },
            new Sys_Lang { LangKey = "sales.section.process",    ZhCN = "工程信息",     ZhTW = "工程資訊",     En = "Process Info",       Ja = "工程情報",     Ko = "공정 정보" },
            new Sys_Lang { LangKey = "sales.section.material",   ZhCN = "材料设定",     ZhTW = "材料設定",     En = "Material Setup",     Ja = "材料設定",     Ko = "재료 설정" },
            new Sys_Lang { LangKey = "sales.section.lotPrice",   ZhCN = "批量单价",     ZhTW = "批量單價",     En = "Lot Pricing",        Ja = "ロット単価",   Ko = "로트 단가" },
            new Sys_Lang { LangKey = "sales.section.notes",      ZhCN = "备注",         ZhTW = "備註",         En = "Notes",              Ja = "備考",         Ko = "비고" },
            new Sys_Lang { LangKey = "sales.section.purchase",   ZhCN = "采购信息",     ZhTW = "採購資訊",     En = "Purchase Info",      Ja = "仕入情報",     Ko = "매입 정보" },
            new Sys_Lang { LangKey = "sales.section.attachment", ZhCN = "附带信息",     ZhTW = "附帶資訊",     En = "Attachments",        Ja = "添付情報",     Ko = "첨부 정보" },
            new Sys_Lang { LangKey = "sales.section.required",   ZhCN = "必要件",       ZhTW = "必要件",       En = "Required Items",     Ja = "必要物",       Ko = "필수 항목" },
            new Sys_Lang { LangKey = "sales.section.history",    ZhCN = "历史记录",     ZhTW = "歷史記錄",     En = "History",            Ja = "過去履歴",     Ko = "이력" },
            new Sys_Lang { LangKey = "sales.section.partsList",  ZhCN = "部材一览",     ZhTW = "部材一覽",     En = "Parts List",         Ja = "部材一覧",     Ko = "부재 목록" },
            new Sys_Lang { LangKey = "sales.section.orderDetail",ZhCN = "受注明细",     ZhTW = "受注明細",     En = "Order Detail",       Ja = "受注明細",     Ko = "수주 명세" },
            new Sys_Lang { LangKey = "sales.section.searchCond", ZhCN = "检索条件",     ZhTW = "檢索條件",     En = "Search",             Ja = "検索条件",     Ko = "검색 조건" },
            new Sys_Lang { LangKey = "sales.section.advSearch",  ZhCN = "详细检索",     ZhTW = "詳細檢索",     En = "Advanced Search",    Ja = "詳細検索",     Ko = "고급 검색" },

            // 通用消息 ────────────────────
            new Sys_Lang { LangKey = "sales.msg.saveSuccess",    ZhCN = "保存成功",     ZhTW = "儲存成功",     En = "Saved successfully", Ja = "保存しました", Ko = "저장되었습니다" },
            new Sys_Lang { LangKey = "sales.msg.deleteSuccess",  ZhCN = "删除成功",     ZhTW = "刪除成功",     En = "Deleted successfully",Ja = "削除しました",Ko = "삭제되었습니다" },
            new Sys_Lang { LangKey = "sales.msg.deleteConfirm",  ZhCN = "确认删除？",   ZhTW = "確認刪除？",   En = "Confirm delete?",    Ja = "削除しますか？",Ko = "삭제하시겠습니까?" },
            new Sys_Lang { LangKey = "sales.msg.unsavedChanges", ZhCN = "存在未保存的修改，是否舍弃？",ZhTW = "存在未儲存的修改，是否捨棄？",En = "Unsaved changes. Discard?", Ja = "未保存の変更があります。破棄しますか？",Ko = "저장되지 않은 변경사항이 있습니다. 폐기할까요?" },
            new Sys_Lang { LangKey = "sales.msg.loadSuccess",    ZhCN = "读取成功",     ZhTW = "讀取成功",     En = "Loaded",             Ja = "読込みました", Ko = "불러왔습니다" },
            new Sys_Lang { LangKey = "sales.msg.confirmTitle",   ZhCN = "确认",         ZhTW = "確認",         En = "Confirm",            Ja = "確認",         Ko = "확인" },

            // 通用错误 ────────────────────
            new Sys_Lang { LangKey = "sales.err.E10008",         ZhCN = "无检索结果",   ZhTW = "無檢索結果",   En = "No results found",   Ja = "検索結果がありません", Ko = "검색 결과가 없습니다" },
            new Sys_Lang { LangKey = "sales.err.E10009",         ZhCN = "未选择行",     ZhTW = "未選擇行",     En = "No row selected",    Ja = "選択行がありません",   Ko = "선택된 행이 없습니다" },
            new Sys_Lang { LangKey = "sales.err.E10010",         ZhCN = "无发行对象",   ZhTW = "無發行對象",   En = "No issue target",    Ja = "発行対象がありません", Ko = "발행 대상이 없습니다" },
            new Sys_Lang { LangKey = "sales.err.E10022",         ZhCN = "必填项未输入", ZhTW = "必填項未輸入", En = "Required field empty",Ja = "必須項目に値が指定されていません", Ko = "필수 항목이 입력되지 않았습니다" },
            new Sys_Lang { LangKey = "sales.err.E10023",         ZhCN = "数据已被更新", ZhTW = "資料已被更新", En = "Already updated",    Ja = "すでに更新済みです", Ko = "이미 업데이트되었습니다" },
            new Sys_Lang { LangKey = "sales.err.E10030",         ZhCN = "请至少选择一项",ZhTW = "請至少選擇一項",En = "Select at least one",Ja = "いずれかを選択してください", Ko = "하나 이상 선택하세요" },
            new Sys_Lang { LangKey = "sales.err.E10036",         ZhCN = "请按 FROM≦TO 输入",ZhTW = "請按 FROM≦TO 輸入",En = "FROM must be ≤ TO",Ja = "FROM≦TO の関係で指定してください", Ko = "FROM ≤ TO 관계로 입력하세요" },
            new Sys_Lang { LangKey = "sales.err.W10002",         ZhCN = "其他用户已更新该数据，请重新读取",ZhTW = "其他用戶已更新該資料，請重新讀取",En = "Modified by another user, please reload", Ja = "他の処理によって更新されています(W10002)", Ko = "다른 사용자가 업데이트했습니다. 다시 불러오세요" },

            // 状态 ────────────────────
            new Sys_Lang { LangKey = "sales.status.draft",       ZhCN = "草稿",         ZhTW = "草稿",         En = "Draft",              Ja = "未確認",       Ko = "초안" },
            new Sys_Lang { LangKey = "sales.status.confirmed",   ZhCN = "已确认",       ZhTW = "已確認",       En = "Confirmed",          Ja = "確認済",       Ko = "확정됨" },
            new Sys_Lang { LangKey = "sales.status.pendingApproval",ZhCN = "审批中",    ZhTW = "審批中",       En = "Pending Approval",   Ja = "承認待ち",     Ko = "승인 대기" },
            new Sys_Lang { LangKey = "sales.status.approved",    ZhCN = "已审批",       ZhTW = "已審批",       En = "Approved",           Ja = "承認済",       Ko = "승인됨" },
            new Sys_Lang { LangKey = "sales.status.transferred", ZhCN = "已转送",       ZhTW = "已轉送",       En = "Transferred",        Ja = "mc転送済",     Ko = "전송됨" },
            new Sys_Lang { LangKey = "sales.status.notRegistered",ZhCN = "未登记",      ZhTW = "未登記",       En = "Not Registered",     Ja = "未作成",       Ko = "미등록" },
            new Sys_Lang { LangKey = "sales.status.autoNumber",  ZhCN = "等待自动编号", ZhTW = "等待自動編號", En = "Awaiting auto-number",Ja = "自動採番待ち", Ko = "자동 채번 대기" },

            // Step 标题（PA050 5 步 / PA070 3 步）────
            new Sys_Lang { LangKey = "sales.step.partsSelect",   ZhCN = "部材选择",     ZhTW = "部材選擇",     En = "Parts Select",       Ja = "部材選択",     Ko = "부재 선택" },
            new Sys_Lang { LangKey = "sales.step.detail",        ZhCN = "明细",         ZhTW = "明細",         En = "Detail",             Ja = "明細",         Ko = "명세" },

            // 一览页通用 ────
            new Sys_Lang { LangKey = "sales.list.totalCount",    ZhCN = "合计 {n} 件",  ZhTW = "合計 {n} 件",  En = "Total {n} items",    Ja = "合計 {n} 件",  Ko = "합계 {n} 건" },
            new Sys_Lang { LangKey = "sales.list.no",            ZhCN = "序号",         ZhTW = "序號",         En = "No",                 Ja = "No",           Ko = "번호" },
            new Sys_Lang { LangKey = "sales.list.action",        ZhCN = "操作",         ZhTW = "操作",         En = "Action",             Ja = "操作",         Ko = "작업" },
            new Sys_Lang { LangKey = "sales.list.detail",        ZhCN = "详情",         ZhTW = "詳情",         En = "Detail",             Ja = "詳細",         Ko = "상세" },
            new Sys_Lang { LangKey = "sales.list.openDetail",    ZhCN = "打开详情",     ZhTW = "開啟詳情",     En = "Open Detail",        Ja = "詳細を開く",   Ko = "상세 열기" },

            // 检索条件常用 ────
            new Sys_Lang { LangKey = "sales.search.dateFrom",    ZhCN = "起始日期",     ZhTW = "起始日期",     En = "Date From",          Ja = "日付 FROM",    Ko = "시작일" },
            new Sys_Lang { LangKey = "sales.search.dateTo",      ZhCN = "结束日期",     ZhTW = "結束日期",     En = "Date To",            Ja = "日付 TO",      Ko = "종료일" },
            new Sys_Lang { LangKey = "sales.search.from",        ZhCN = "FROM",         ZhTW = "FROM",         En = "From",               Ja = "FROM",         Ko = "FROM" },
            new Sys_Lang { LangKey = "sales.search.to",          ZhCN = "TO",           ZhTW = "TO",           En = "To",                 Ja = "TO",           Ko = "TO" },
            new Sys_Lang { LangKey = "sales.search.required",    ZhCN = "必填",         ZhTW = "必填",         En = "Required",           Ja = "必須",         Ko = "필수" },

            // 业务角色 / 属性 ────
            new Sys_Lang { LangKey = "sales.role.customer",      ZhCN = "客户",         ZhTW = "客戶",         En = "Customer",           Ja = "得意先",       Ko = "거래처" },
            new Sys_Lang { LangKey = "sales.role.ar",            ZhCN = "应收",         ZhTW = "應收",         En = "Accounts Rec.",      Ja = "売掛先",       Ko = "외상매출처" },
            new Sys_Lang { LangKey = "sales.role.billing",       ZhCN = "请款",         ZhTW = "請款",         En = "Billing",            Ja = "請求先",       Ko = "청구처" },
            new Sys_Lang { LangKey = "sales.role.receipt",       ZhCN = "收款",         ZhTW = "收款",         En = "Receipt",            Ja = "入金先",       Ko = "입금처" },
            new Sys_Lang { LangKey = "sales.role.delivery",      ZhCN = "纳品",         ZhTW = "納品",         En = "Delivery",           Ja = "納品先",       Ko = "납품처" },
            new Sys_Lang { LangKey = "sales.role.creditMgmt",    ZhCN = "信用管理",     ZhTW = "信用管理",     En = "Credit Mgmt",        Ja = "与信管理",     Ko = "신용 관리" },
            new Sys_Lang { LangKey = "sales.role.supplier",      ZhCN = "供应商",       ZhTW = "供應商",       En = "Supplier",           Ja = "発注先",       Ko = "공급사" },
            new Sys_Lang { LangKey = "sales.role.ap",            ZhCN = "应付",         ZhTW = "應付",         En = "Accounts Pay.",      Ja = "買掛先",       Ko = "외상매입처" },
            new Sys_Lang { LangKey = "sales.role.paymentSch",    ZhCN = "付款计划",     ZhTW = "付款計劃",     En = "Payment Sched.",     Ja = "支払予定管理先",Ko = "지급 예정" },
            new Sys_Lang { LangKey = "sales.role.payment",       ZhCN = "付款",         ZhTW = "付款",         En = "Payment",            Ja = "支払先",       Ko = "지급처" },
            new Sys_Lang { LangKey = "sales.role.maker",         ZhCN = "厂商",         ZhTW = "廠商",         En = "Maker",              Ja = "メーカ",       Ko = "제조사" },

            // 单价订正专用 ────
            new Sys_Lang { LangKey = "sales.pc.before",          ZhCN = "变更前",       ZhTW = "變更前",       En = "Before",             Ja = "変更前",       Ko = "변경 전" },
            new Sys_Lang { LangKey = "sales.pc.after",           ZhCN = "变更后",       ZhTW = "變更後",       En = "After",              Ja = "変更後",       Ko = "변경 후" },
            new Sys_Lang { LangKey = "sales.pc.reason",          ZhCN = "变更理由",     ZhTW = "變更理由",     En = "Change Reason",      Ja = "単価変更理由", Ko = "변경 사유" },
            new Sys_Lang { LangKey = "sales.pc.provisional",     ZhCN = "暂定价",       ZhTW = "暫定價",       En = "Provisional",        Ja = "仮単価",       Ko = "잠정가" },
            new Sys_Lang { LangKey = "sales.pc.updateSelected",  ZhCN = "更新选中行",   ZhTW = "更新選取行",   En = "Update Selected",    Ja = "選択行を更新", Ko = "선택 행 업데이트" },

            // 受注一览专用 ────
            new Sys_Lang { LangKey = "sales.order.consignedSale",ZhCN = "代销",         ZhTW = "代銷",         En = "Consigned",          Ja = "預り売上",     Ko = "위탁 매출" },
            new Sys_Lang { LangKey = "sales.order.mcUntransferred",ZhCN = "未转送",     ZhTW = "未轉送",       En = "Not Transferred",    Ja = "mc未転送",     Ko = "미전송" }
        );
        db.SaveChanges();
    }

    // 販売管理菜单 i18n（PA010〜PA150） — 已存在 DB 的补充
    if (!db.Sys_Langs.Any(l => l.LangKey == "nav.200"))
    {
        db.Sys_Langs.AddRange(
            new Sys_Lang { LangKey = "nav.200", ZhCN = "销售管理(ERP)",      ZhTW = "銷售管理(ERP)",      En = "Sales (ERP)",            Ja = "販売管理(ERP)",           Ko = "판매 관리(ERP)" },
            new Sys_Lang { LangKey = "nav.201", ZhCN = "报价计算单 照会",    ZhTW = "報價計算單 照會",    En = "Estimate Calc Inquiry",  Ja = "見積計算書 照会",         Ko = "견적계산서 조회" },
            new Sys_Lang { LangKey = "nav.202", ZhCN = "报价计算单 登记",    ZhTW = "報價計算單 登記",    En = "Estimate Calc Entry",    Ja = "見積計算書 登録",         Ko = "견적계산서 등록" },
            new Sys_Lang { LangKey = "nav.203", ZhCN = "正式报价单 一览",    ZhTW = "正式報價單 一覽",    En = "Quotation List",         Ja = "御見積書 一覧",           Ko = "견적서 목록" },
            new Sys_Lang { LangKey = "nav.204", ZhCN = "正式报价单 登记",    ZhTW = "正式報價單 登記",    En = "Quotation Entry",        Ja = "御見積書 登録",           Ko = "견적서 등록" },
            new Sys_Lang { LangKey = "nav.205", ZhCN = "产品主数据 一览",    ZhTW = "產品主資料 一覽",    En = "Product Master List",    Ja = "製品マスタ 一覧",         Ko = "제품 마스터 목록" },
            new Sys_Lang { LangKey = "nav.206", ZhCN = "产品主数据 登记",    ZhTW = "產品主資料 登記",    En = "Product Master Entry",   Ja = "製品マスタ 登録",         Ko = "제품 마스터 등록" },
            new Sys_Lang { LangKey = "nav.207", ZhCN = "订单一览查询",       ZhTW = "訂單一覽查詢",       En = "Order Inquiry",          Ja = "受注一覧照会",            Ko = "수주 일람 조회" },
            new Sys_Lang { LangKey = "nav.208", ZhCN = "订单录入",           ZhTW = "訂單錄入",           En = "Order Entry",            Ja = "受注入力",                Ko = "수주 입력" },
            new Sys_Lang { LangKey = "nav.209", ZhCN = "单价订正",           ZhTW = "單價訂正",           En = "Price Correction",       Ja = "単価訂正",                Ko = "단가 수정" },
            new Sys_Lang { LangKey = "nav.210", ZhCN = "FSC 检查清单",       ZhTW = "FSC 檢查清單",       En = "FSC Checklist",          Ja = "FSC チェックシート",      Ko = "FSC 체크시트" },
            new Sys_Lang { LangKey = "nav.211", ZhCN = "交易先主数据 一览",  ZhTW = "交易夥伴主資料 一覽", En = "Business Partner List",  Ja = "取引先マスタ 一覧",       Ko = "거래처 마스터 목록" },
            new Sys_Lang { LangKey = "nav.212", ZhCN = "交易先主数据 登记",  ZhTW = "交易夥伴主資料 登記", En = "Business Partner Entry", Ja = "取引先マスタ 登録",       Ko = "거래처 마스터 등록" },
            new Sys_Lang { LangKey = "nav.213", ZhCN = "纸板单价主数据",     ZhTW = "紙板單價主資料",     En = "Sheet Unit Price",       Ja = "シート単価マスタ",        Ko = "시트 단가 마스터" },
            new Sys_Lang { LangKey = "nav.214", ZhCN = "版型/木型 一览",     ZhTW = "版型/木型 一覽",     En = "Plate/Mold List",        Ja = "版型/木型 一覧",          Ko = "판형/목형 목록" },
            new Sys_Lang { LangKey = "nav.215", ZhCN = "版型/木型 登记",     ZhTW = "版型/木型 登記",     En = "Plate/Mold Entry",       Ja = "版型/木型 登録",          Ko = "판형/목형 등록" }
        );
        db.SaveChanges();
    }

    // 示例字典数据
    if (!db.Sys_DictTypes.Any())
    {
        db.Sys_DictTypes.AddRange(
            new Sys_DictType { TypeCode = "gender", TypeName = "性别", OrderNo = 1 },
            new Sys_DictType { TypeCode = "article_type", TypeName = "文章类型", OrderNo = 2 }
        );
        db.Sys_DictDatas.AddRange(
            new Sys_DictData { TypeCode = "gender", Value = "1", Label = "男", OrderNo = 1 },
            new Sys_DictData { TypeCode = "gender", Value = "2", Label = "女", OrderNo = 2 },
            new Sys_DictData { TypeCode = "article_type", Value = "news", Label = "新闻", OrderNo = 1 },
            new Sys_DictData { TypeCode = "article_type", Value = "tech", Label = "技术", OrderNo = 2 },
            new Sys_DictData { TypeCode = "article_type", Value = "life", Label = "生活", OrderNo = 3 }
        );
        db.SaveChanges();
    }

    // ===== MSBBPA010 見積計算書 主数据种子 =====
    if (!db.MasterBases.Any())
    {
        db.MasterBases.AddRange(
            new MasterBase { BaseCd = "01", BaseName = "岐阜事業所", IsAdminBase = true,  DiscountThreshold = 0.15m, SortOrder = 1 },
            new MasterBase { BaseCd = "02", BaseName = "東京営業所",  IsAdminBase = false, DiscountThreshold = 0.10m, SortOrder = 2 },
            new MasterBase { BaseCd = "03", BaseName = "大阪営業所",  IsAdminBase = false, DiscountThreshold = 0.10m, SortOrder = 3 }
        );
        db.SaveChanges();
    }

    if (!db.MasterStaffs.Any())
    {
        db.MasterStaffs.AddRange(
            new MasterStaff { StaffCd = "1001", StaffName = "山田 太郎",  BaseCd = "01", SortOrder = 1 },
            new MasterStaff { StaffCd = "1002", StaffName = "佐藤 花子",  BaseCd = "01", SortOrder = 2 },
            new MasterStaff { StaffCd = "2001", StaffName = "鈴木 一郎",  BaseCd = "02", SortOrder = 1 },
            new MasterStaff { StaffCd = "3001", StaffName = "田中 美咲",  BaseCd = "03", SortOrder = 1 }
        );
        db.SaveChanges();
    }

    if (!db.MasterGenericCodes.Any())
    {
        db.MasterGenericCodes.AddRange(
            // 受注区分
            new MasterGenericCode { GroupCode = "OrderType", Code = "10", Name = "通常受注", SortOrder = 1 },
            new MasterGenericCode { GroupCode = "OrderType", Code = "20", Name = "サンプル", SortOrder = 2 },
            new MasterGenericCode { GroupCode = "OrderType", Code = "30", Name = "商品",     SortOrder = 3 },

            // 親子区分
            new MasterGenericCode { GroupCode = "ParentChildDiv", Code = "1", Name = "親",     SortOrder = 1 },
            new MasterGenericCode { GroupCode = "ParentChildDiv", Code = "2", Name = "子",     SortOrder = 2 },
            new MasterGenericCode { GroupCode = "ParentChildDiv", Code = "3", Name = "部材",   SortOrder = 3 },

            // FSC 区分
            new MasterGenericCode { GroupCode = "FscDiv", Code = "0", Name = "非対象",           SortOrder = 0 },
            new MasterGenericCode { GroupCode = "FscDiv", Code = "1", Name = "FSC 100%",         SortOrder = 1 },
            new MasterGenericCode { GroupCode = "FscDiv", Code = "2", Name = "FSC MIX",          SortOrder = 2 },

            // 製品区分（大/中/小）
            // ProductCategoryBig = 大分類（独立）
            new MasterGenericCode { GroupCode = "ProductCategoryBig", Code = "A", Name = "段ボール箱", SortOrder = 1 },
            new MasterGenericCode { GroupCode = "ProductCategoryBig", Code = "B", Name = "紙箱",       SortOrder = 2 },
            new MasterGenericCode { GroupCode = "ProductCategoryBig", Code = "C", Name = "トレー",     SortOrder = 3 },

            // ProductCategoryMid = 中分類（Attr1 = 親=大分類 Code）
            new MasterGenericCode { GroupCode = "ProductCategoryMid", Code = "A01", Name = "A式箱",     Attr1 = "A", SortOrder = 1 },
            new MasterGenericCode { GroupCode = "ProductCategoryMid", Code = "A02", Name = "B式箱",     Attr1 = "A", SortOrder = 2 },
            new MasterGenericCode { GroupCode = "ProductCategoryMid", Code = "A03", Name = "ワンタッチ", Attr1 = "A", SortOrder = 3 },
            new MasterGenericCode { GroupCode = "ProductCategoryMid", Code = "B01", Name = "化粧箱",     Attr1 = "B", SortOrder = 1 },
            new MasterGenericCode { GroupCode = "ProductCategoryMid", Code = "B02", Name = "贈答箱",     Attr1 = "B", SortOrder = 2 },
            new MasterGenericCode { GroupCode = "ProductCategoryMid", Code = "C01", Name = "食品トレー", Attr1 = "C", SortOrder = 1 },
            new MasterGenericCode { GroupCode = "ProductCategoryMid", Code = "C02", Name = "工業トレー", Attr1 = "C", SortOrder = 2 },

            // ProductCategorySml = 小分類（Attr1 = 親=中分類 Code）
            new MasterGenericCode { GroupCode = "ProductCategorySml", Code = "A0101", Name = "標準A式",     Attr1 = "A01", SortOrder = 1 },
            new MasterGenericCode { GroupCode = "ProductCategorySml", Code = "A0102", Name = "半差し込み", Attr1 = "A01", SortOrder = 2 },
            new MasterGenericCode { GroupCode = "ProductCategorySml", Code = "A0201", Name = "標準B式",     Attr1 = "A02", SortOrder = 1 },
            new MasterGenericCode { GroupCode = "ProductCategorySml", Code = "A0301", Name = "ワンタッチ底", Attr1 = "A03", SortOrder = 1 },
            new MasterGenericCode { GroupCode = "ProductCategorySml", Code = "B0101", Name = "白色化粧箱", Attr1 = "B01", SortOrder = 1 },
            new MasterGenericCode { GroupCode = "ProductCategorySml", Code = "B0102", Name = "色付化粧箱", Attr1 = "B01", SortOrder = 2 },
            new MasterGenericCode { GroupCode = "ProductCategorySml", Code = "B0201", Name = "贈答化粧箱", Attr1 = "B02", SortOrder = 1 },
            new MasterGenericCode { GroupCode = "ProductCategorySml", Code = "C0101", Name = "浅型トレー", Attr1 = "C01", SortOrder = 1 },
            new MasterGenericCode { GroupCode = "ProductCategorySml", Code = "C0102", Name = "深型トレー", Attr1 = "C01", SortOrder = 2 },
            new MasterGenericCode { GroupCode = "ProductCategorySml", Code = "C0201", Name = "工業標準",   Attr1 = "C02", SortOrder = 1 },

            // シート段
            new MasterGenericCode { GroupCode = "SheetFlute", Code = "E",  Name = "E 段", SortOrder = 1 },
            new MasterGenericCode { GroupCode = "SheetFlute", Code = "B",  Name = "B 段", SortOrder = 2 },
            new MasterGenericCode { GroupCode = "SheetFlute", Code = "C",  Name = "C 段", SortOrder = 3 },
            new MasterGenericCode { GroupCode = "SheetFlute", Code = "BC", Name = "BC 段", SortOrder = 4 },
            new MasterGenericCode { GroupCode = "SheetFlute", Code = "EB", Name = "EB 段", SortOrder = 5 },

            // 単位
            new MasterGenericCode { GroupCode = "Unit", Code = "PCS", Name = "枚", SortOrder = 1 },
            new MasterGenericCode { GroupCode = "Unit", Code = "SET", Name = "セット", SortOrder = 2 },

            // 製品形状1/2
            new MasterGenericCode { GroupCode = "ProductShape1", Code = "01", Name = "A 式", SortOrder = 1 },
            new MasterGenericCode { GroupCode = "ProductShape1", Code = "02", Name = "B 式", SortOrder = 2 },
            new MasterGenericCode { GroupCode = "ProductShape2", Code = "01", Name = "通常",   SortOrder = 1 },
            new MasterGenericCode { GroupCode = "ProductShape2", Code = "02", Name = "効率形状", SortOrder = 2 },

            // 物流区分
            new MasterGenericCode { GroupCode = "DistDiv", Code = "1", Name = "直送", SortOrder = 1 },
            new MasterGenericCode { GroupCode = "DistDiv", Code = "2", Name = "倉庫経由", SortOrder = 2 },

            // M014 印刷（Attr1 = インキ性質区分：0=水性/1=油性）
            new MasterGenericCode { GroupCode = "M014", Code = "0000", Name = "なし",    Attr1 = "0", SortOrder = 0 },
            new MasterGenericCode { GroupCode = "M014", Code = "I001", Name = "オフセット", Attr1 = "1", SortOrder = 1 },
            new MasterGenericCode { GroupCode = "M014", Code = "I002", Name = "グラビア",   Attr1 = "1", SortOrder = 2 },
            new MasterGenericCode { GroupCode = "M014", Code = "I003", Name = "フレマル",   Attr1 = "0", SortOrder = 3 },

            // M038 工程
            new MasterGenericCode { GroupCode = "M038", Code = "0020", Name = "輪転（グラビア）1", SortOrder = 1 },
            new MasterGenericCode { GroupCode = "M038", Code = "0021", Name = "輪転（グラビア）2", SortOrder = 2 },
            new MasterGenericCode { GroupCode = "M038", Code = "0030", Name = "輪転（フレマル）1", SortOrder = 3 },
            new MasterGenericCode { GroupCode = "M038", Code = "0031", Name = "輪転（フレマル）2", SortOrder = 4 },
            new MasterGenericCode { GroupCode = "M038", Code = "0050", Name = "トムソン",          SortOrder = 5 },
            new MasterGenericCode { GroupCode = "M038", Code = "0060", Name = "貼合",              SortOrder = 6 },

            // M067 段成率（%，Num1）
            new MasterGenericCode { GroupCode = "M067", Code = "E",  Name = "E 段成率", Num1 = 1.35m, SortOrder = 1 },
            new MasterGenericCode { GroupCode = "M067", Code = "B",  Name = "B 段成率", Num1 = 1.40m, SortOrder = 2 },
            new MasterGenericCode { GroupCode = "M067", Code = "C",  Name = "C 段成率", Num1 = 1.50m, SortOrder = 3 },
            new MasterGenericCode { GroupCode = "M067", Code = "BC", Name = "BC 段成率", Num1 = 1.45m, SortOrder = 4 },

            // 原紙（示意，Attr1 = 段原紙区分 1/其他；Attr2 = 坪量 g/m²）
            // Paper 原紙（Num1 = 円/m² 単価；Attr2 = 坪量 g/m²）
            new MasterGenericCode { GroupCode = "Paper", Code = "P001", Name = "K220",    Attr1 = "0", Attr2 = "220", Num1 = 32.5m, SortOrder = 1 },
            new MasterGenericCode { GroupCode = "Paper", Code = "P002", Name = "K280",    Attr1 = "0", Attr2 = "280", Num1 = 38.0m, SortOrder = 2 },
            new MasterGenericCode { GroupCode = "Paper", Code = "C001", Name = "中芯 120", Attr1 = "1", Attr2 = "120", Num1 = 22.0m, SortOrder = 3 }
        );
        db.SaveChanges();
    }

    // 幂等补种：如果存在旧的 "ProductCategory" 单层数据，迁移到 3 层级联
    if (db.MasterGenericCodes.Any(x => x.GroupCode == "ProductCategory")
        && !db.MasterGenericCodes.Any(x => x.GroupCode == "ProductCategoryBig"))
    {
        // 删除旧单层
        var legacy = db.MasterGenericCodes.Where(x => x.GroupCode == "ProductCategory").ToList();
        db.MasterGenericCodes.RemoveRange(legacy);

        // 追加 3 层级联
        db.MasterGenericCodes.AddRange(
            new MasterGenericCode { GroupCode = "ProductCategoryBig", Code = "A", Name = "段ボール箱", SortOrder = 1 },
            new MasterGenericCode { GroupCode = "ProductCategoryBig", Code = "B", Name = "紙箱",       SortOrder = 2 },
            new MasterGenericCode { GroupCode = "ProductCategoryBig", Code = "C", Name = "トレー",     SortOrder = 3 },

            new MasterGenericCode { GroupCode = "ProductCategoryMid", Code = "A01", Name = "A式箱",     Attr1 = "A", SortOrder = 1 },
            new MasterGenericCode { GroupCode = "ProductCategoryMid", Code = "A02", Name = "B式箱",     Attr1 = "A", SortOrder = 2 },
            new MasterGenericCode { GroupCode = "ProductCategoryMid", Code = "A03", Name = "ワンタッチ", Attr1 = "A", SortOrder = 3 },
            new MasterGenericCode { GroupCode = "ProductCategoryMid", Code = "B01", Name = "化粧箱",     Attr1 = "B", SortOrder = 1 },
            new MasterGenericCode { GroupCode = "ProductCategoryMid", Code = "B02", Name = "贈答箱",     Attr1 = "B", SortOrder = 2 },
            new MasterGenericCode { GroupCode = "ProductCategoryMid", Code = "C01", Name = "食品トレー", Attr1 = "C", SortOrder = 1 },
            new MasterGenericCode { GroupCode = "ProductCategoryMid", Code = "C02", Name = "工業トレー", Attr1 = "C", SortOrder = 2 },

            new MasterGenericCode { GroupCode = "ProductCategorySml", Code = "A0101", Name = "標準A式",     Attr1 = "A01", SortOrder = 1 },
            new MasterGenericCode { GroupCode = "ProductCategorySml", Code = "A0102", Name = "半差し込み", Attr1 = "A01", SortOrder = 2 },
            new MasterGenericCode { GroupCode = "ProductCategorySml", Code = "A0201", Name = "標準B式",     Attr1 = "A02", SortOrder = 1 },
            new MasterGenericCode { GroupCode = "ProductCategorySml", Code = "A0301", Name = "ワンタッチ底", Attr1 = "A03", SortOrder = 1 },
            new MasterGenericCode { GroupCode = "ProductCategorySml", Code = "B0101", Name = "白色化粧箱", Attr1 = "B01", SortOrder = 1 },
            new MasterGenericCode { GroupCode = "ProductCategorySml", Code = "B0102", Name = "色付化粧箱", Attr1 = "B01", SortOrder = 2 },
            new MasterGenericCode { GroupCode = "ProductCategorySml", Code = "B0201", Name = "贈答化粧箱", Attr1 = "B02", SortOrder = 1 },
            new MasterGenericCode { GroupCode = "ProductCategorySml", Code = "C0101", Name = "浅型トレー", Attr1 = "C01", SortOrder = 1 },
            new MasterGenericCode { GroupCode = "ProductCategorySml", Code = "C0102", Name = "深型トレー", Attr1 = "C01", SortOrder = 2 },
            new MasterGenericCode { GroupCode = "ProductCategorySml", Code = "C0201", Name = "工業標準",   Attr1 = "C02", SortOrder = 1 }
        );
        db.SaveChanges();
    }
}

app.UseCors("AllowAll");

// T15 / Gap 2.3 — HTTP リクエスト指標を収集（http_request_duration_seconds 等）。
// UseRouting 後・エンドポイント前に挿入。
app.UseHttpMetrics();

app.UseAuthentication();

// 章10 多租户：从 JWT tenant_id 解析当前租户写入 ITenantContext（须在 UseAuthentication 之后，User 已解析）。
app.UseMiddleware<CP6.WebApi.Middleware.TenantMiddleware>();

// i18n 优化 P1：请求本地化。culture 来源优先级 = 用户偏好(JWT 'lang' claim) > ?culture= > Cookie > Accept-Language > 默认 ja。
// 必须在 UseAuthentication 之后（读 claim 需 User 已认证），且在 BizExceptionMiddleware 之外（更早），
// 以保证异常上抛被捕获时 culture 仍然有效。
{
    var supportedCultures = new[] { "ja", "zh-CN", "zh-TW", "en", "ko" };
    var locOptions = new Microsoft.AspNetCore.Builder.RequestLocalizationOptions()
        .SetDefaultCulture("ja")
        .AddSupportedCultures(supportedCultures)
        .AddSupportedUICultures(supportedCultures);
    locOptions.RequestCultureProviders.Insert(0, new Microsoft.AspNetCore.Localization.CustomRequestCultureProvider(ctx =>
    {
        var lang = ctx.User?.FindFirst("lang")?.Value;
        return Task.FromResult<Microsoft.AspNetCore.Localization.ProviderCultureResult?>(
            string.IsNullOrEmpty(lang) ? null : new Microsoft.AspNetCore.Localization.ProviderCultureResult(lang));
    }));
    app.UseRequestLocalization(locOptions);
}

// i18n 优化 P1：BizException → 本地化消息（须在 UseRequestLocalization 之后）。
app.UseMiddleware<CP6.WebApi.Middleware.BizExceptionMiddleware>();

app.UseAuthorization();
app.MapControllers();

// SignalR Hub 路由
app.MapHub<NotifyHub>("/hubs/notify");
app.MapHub<CP6.WebApi.Hubs.MesHub>("/hubs/mes");
app.MapHub<CP6.WebApi.Hubs.WmsHub>("/hubs/wms");

// T15 / Gap 2.3 — Prometheus 公開エンドポイント /metrics ＋ ブリッジ業務指標コレクタ起動
app.MapMetrics();   // GET /metrics（Prometheus テキスト形式）
app.Services.GetRequiredService<CP6.WebApi.Observability.BridgeMetricsCollector>().Register();

app.Run();
