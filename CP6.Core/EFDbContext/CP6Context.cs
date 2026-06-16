using System.Linq.Expressions;
using CP6.Core.Services.Common;
using CP6.Entity;
using CP6.Entity.DomainModels;
using CP6.Entity.DomainModels.Fin;
using CP6.Entity.DomainModels.Mes;
using CP6.Entity.DomainModels.Pub;
using CP6.Entity.DomainModels.Wf;
using CP6.Entity.DomainModels.Wms;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.EFDbContext;

/// <summary>
/// 数据库上下文 - 管理所有实体与数据库表的映射
/// 每新增一个实体，就在这里加一个 DbSet
/// </summary>
public class CP6Context : DbContext
{
    private readonly ITenantContext? _tenant;

    /// <summary>
    /// 多租户（章10）：可选注入 <see cref="ITenantContext"/>。生产由 DI 注入请求级租户；
    /// 单测/后台用单参构造 → 回退 <see cref="TenantContext.DefaultTenant"/>，故既有测试无需改造。
    /// </summary>
    public CP6Context(DbContextOptions<CP6Context> options, ITenantContext? tenant = null) : base(options)
    {
        _tenant = tenant;
    }

    /// <summary>当前租户 Id（全局查询过滤 + 写入盖章用）。无注入则默认租户。</summary>
    public Guid CurrentTenantId => _tenant?.CurrentTenantId ?? TenantContext.DefaultTenant;

    /// <summary>
    /// 用户表
    /// </summary>
    public DbSet<Sys_User> Sys_Users { get; set; }

    /// <summary>
    /// 租户注册表 —— OA 章10 §7 多租户花名册（共享表，Id 即各表 TenantId 来源）
    /// </summary>
    public DbSet<Sys_Tenant> Sys_Tenants { get; set; }

    /// <summary>
    /// 部门（组织树）—— PUB 章00 组织模型
    /// </summary>
    public DbSet<Sys_Dept> Sys_Depts { get; set; }

    /// <summary>
    /// 角色表
    /// </summary>
    public DbSet<Sys_Role> Sys_Roles { get; set; }

    /// <summary>
    /// 菜单表
    /// </summary>
    public DbSet<Sys_Menu> Sys_Menus { get; set; }

    /// <summary>
    /// 角色-菜单映射表
    /// </summary>
    public DbSet<Sys_RoleMenu> Sys_RoleMenus { get; set; }

    /// <summary>
    /// 用户-角色 中间表 —— PUB 章01 多角色 RBAC
    /// </summary>
    public DbSet<Sys_UserRole> Sys_UserRoles { get; set; }

    /// <summary>
    /// 菜单操作点 —— PUB 章02 功能权限
    /// </summary>
    public DbSet<Sys_MenuAction> Sys_MenuActions { get; set; }

    /// <summary>
    /// 角色-操作点 授权 —— PUB 章02 功能权限
    /// </summary>
    public DbSet<Sys_RoleAction> Sys_RoleActions { get; set; }

    /// <summary>
    /// 角色数据范围 —— PUB 章03 数据权限
    /// </summary>
    public DbSet<Sys_RoleDataScope> Sys_RoleDataScopes { get; set; }

    /// <summary>
    /// 角色字段权限 —— PUB 章04 字段权限
    /// </summary>
    public DbSet<Sys_RoleFieldPerm> Sys_RoleFieldPerms { get; set; }

    /// <summary>
    /// 富采番规则 —— PUB 章05 公共模组
    /// </summary>
    public DbSet<Pub_DocSequence> Pub_DocSequences { get; set; }

    /// <summary>
    /// 统一附件 —— PUB 章06 公共模组
    /// </summary>
    public DbSet<Pub_Attachment> Pub_Attachments { get; set; }

    /// <summary>代码生成 — 表元数据 —— PUB 章08</summary>
    public DbSet<GenTable> Pub_GenTables { get; set; }

    /// <summary>代码生成 — 列元数据 —— PUB 章08</summary>
    public DbSet<GenColumn> Pub_GenColumns { get; set; }

    /// <summary>
    /// 多语言词条表
    /// </summary>
    public DbSet<Sys_Lang> Sys_Langs { get; set; }

    /// <summary>
    /// 字典类型表
    /// </summary>
    public DbSet<Sys_DictType> Sys_DictTypes { get; set; }

    /// <summary>
    /// 字典数据表
    /// </summary>
    public DbSet<Sys_DictData> Sys_DictDatas { get; set; }

    /// <summary>
    /// 操作日志表
    /// </summary>
    public DbSet<Sys_OperLog> Sys_OperLogs { get; set; }

    // ───── Phase 6 跨模块集成事件 ─────
    /// <summary>跨模块 Bridge Hook 持久化记录（重试 / DLQ / trace 基础）</summary>
    public DbSet<IntegrationEvent> IntegrationEvents { get; set; }

    // ───── MSBBPA010 見積計算書 ─────

    /// <summary>見積計算書 主表</summary>
    public DbSet<EstimateCalc> EstimateCalcs { get; set; }

    /// <summary>見積加工工程 明细表</summary>
    public DbSet<EstimateCalcProcess> EstimateCalcProcesses { get; set; }

    /// <summary>拠点主数据</summary>
    public DbSet<MasterBase> MasterBases { get; set; }

    /// <summary>担当者主数据</summary>
    public DbSet<MasterStaff> MasterStaffs { get; set; }

    /// <summary>汎用マスタ（通用代码）</summary>
    public DbSet<MasterGenericCode> MasterGenericCodes { get; set; }

    // ───── MSBBPA030 御見積書 ─────

    /// <summary>御見積書 主表</summary>
    public DbSet<Quotation> Quotations { get; set; }

    /// <summary>御見積書 ⇄ 見積計算書 关联表</summary>
    public DbSet<QuotationCalc> QuotationCalcs { get; set; }

    /// <summary>御見積書 明細(印字用)</summary>
    public DbSet<QuotationDetail> QuotationDetails { get; set; }

    // ───── MSBBPA050 Web 製品マスタ ─────

    /// <summary>製品基本マスタ</summary>
    public DbSet<ProductMaster> ProductMasters { get; set; }

    /// <summary>製品加工工程マスタ</summary>
    public DbSet<ProductProcess> ProductProcesses { get; set; }

    /// <summary>製品加工材料マスタ</summary>
    public DbSet<ProductMaterial> ProductMaterials { get; set; }

    /// <summary>製品ロット別単価マスタ</summary>
    public DbSet<ProductLotPrice> ProductLotPrices { get; set; }

    /// <summary>製品連産品マスタ</summary>
    public DbSet<ProductCoProduct> ProductCoProducts { get; set; }

    // ───── MSBBPA070/080/090 受注 ─────

    /// <summary>受注ヘッダー</summary>
    public DbSet<Order> Orders { get; set; }

    /// <summary>受注明細</summary>
    public DbSet<OrderDetail> OrderDetails { get; set; }

    /// <summary>RMA 返品に対する ERP CreditNote</summary>
    public DbSet<CreditNote> CreditNotes { get; set; }

    /// <summary>受注加工工程</summary>
    public DbSet<OrderProcess> OrderProcesses { get; set; }

    /// <summary>受注工程備考</summary>
    public DbSet<OrderProcessNote> OrderProcessNotes { get; set; }

    /// <summary>受注加工材料</summary>
    public DbSet<OrderMaterial> OrderMaterials { get; set; }

    // ───── MSBBPA100/110/120 取引先 / FSC ─────

    /// <summary>Web 取引先マスタ（PA110/PA120）</summary>
    public DbSet<BusinessPartner> BusinessPartners { get; set; }

    /// <summary>FSC 製品化チェックシート発行履歴（PA100）</summary>
    public DbSet<FscChecklist> FscChecklists { get; set; }

    /// <summary>全社統一採番カウンタ（機能コード+年月+自増13桁）</summary>
    public DbSet<DocSequence> DocSequences { get; set; }

    // ───── MSBBPA130 シート単価 ─────
    public DbSet<SheetUnitPrice> SheetUnitPrices { get; set; }
    public DbSet<SheetUnitPriceEstimate> SheetUnitPriceEstimates { get; set; }

    // ───── MSBBPA140/150 木型・版型管理マスタ ─────
    public DbSet<PlateMold> PlateMolds { get; set; }

    // ───── MSBBME010〜090 MES 製造執行 ─────
    /// <summary>製造指図ヘッダ（ME020）</summary>
    public DbSet<WorkOrder> WorkOrders { get; set; }
    /// <summary>製造指図工程明細</summary>
    public DbSet<WorkOrderProcess> WorkOrderProcesses { get; set; }
    /// <summary>製造指図材料明細</summary>
    public DbSet<WorkOrderMaterial> WorkOrderMaterials { get; set; }
    /// <summary>製造実績（ME040）</summary>
    public DbSet<ProductionResult> ProductionResults { get; set; }
    /// <summary>品質検査ヘッダ（ME060）</summary>
    public DbSet<QualityInspection> QualityInspections { get; set; }
    /// <summary>品質検査項目明細</summary>
    public DbSet<QualityInspectionItem> QualityInspectionItems { get; set; }
    /// <summary>不良品記録（ME080）</summary>
    public DbSet<DefectRecord> DefectRecords { get; set; }
    /// <summary>検査項目テンプレート</summary>
    public DbSet<InspectionTemplate> InspectionTemplates { get; set; }
    /// <summary>不良分類マスタ</summary>
    public DbSet<DefectCategory> DefectCategories { get; set; }
    /// <summary>MES採番管理</summary>
    public DbSet<MesSequence> MesSequences { get; set; }

    // ───── MES Phase 4：設備管理 / OEE ─────
    /// <summary>設備マスタ</summary>
    public DbSet<Machine> Machines { get; set; }
    /// <summary>設備停止記録</summary>
    public DbSet<MachineDowntime> MachineDowntimes { get; set; }
    /// <summary>OEE 日次集計</summary>
    public DbSet<OeeDaily> OeeDailies { get; set; }

    // ───── MSBBWM010〜090 WMS Phase 1 コア ─────
    /// <summary>倉庫マスタ（WM010）</summary>
    public DbSet<Warehouse> Warehouses { get; set; }
    /// <summary>ロケーション（棚位）マスタ（WM010）</summary>
    public DbSet<Location> Locations { get; set; }
    /// <summary>在庫実況（WM020 + 全 WMS 中核）</summary>
    public DbSet<Stock> Stocks { get; set; }
    /// <summary>在庫トランザクション（不可変ログ）</summary>
    public DbSet<StockTransaction> StockTransactions { get; set; }
    /// <summary>WMS 採番管理</summary>
    public DbSet<WmsSequence> WmsSequences { get; set; }

    // ───── MSBBWM030/040 WMS Phase 2 入庫 ─────
    /// <summary>入庫予定ヘッダ（WM030）</summary>
    public DbSet<InboundOrder> InboundOrders { get; set; }
    /// <summary>入庫予定明細</summary>
    public DbSet<InboundOrderDetail> InboundOrderDetails { get; set; }
    /// <summary>入庫実績ヘッダ（WM040）</summary>
    public DbSet<InboundReceipt> InboundReceipts { get; set; }
    /// <summary>入庫実績明細</summary>
    public DbSet<InboundReceiptDetail> InboundReceiptDetails { get; set; }

    // ───── MSBBWM050/070/080 WMS Phase 3 出庫 ─────
    /// <summary>出庫指示ヘッダ（WM050/070 共有）</summary>
    public DbSet<OutboundOrder> OutboundOrders { get; set; }
    /// <summary>出庫指示明細</summary>
    public DbSet<OutboundOrderDetail> OutboundOrderDetails { get; set; }
    /// <summary>出荷梱包（WM080）</summary>
    public DbSet<ShippingPackage> ShippingPackages { get; set; }
    /// <summary>材料不足バックフロー</summary>
    public DbSet<MaterialShortage> MaterialShortages { get; set; }
    /// <summary>出庫ルーティングルール（多倉庫引当 Gap 4.2 / T14）</summary>
    public DbSet<OutboundRoutingRule> OutboundRoutingRules { get; set; }
    /// <summary>為替レートマスタ（多通貨 Gap 4.3）</summary>
    public DbSet<FxRate> FxRates { get; set; }

    // ───── MSBBWM090 WMS Phase 4 棚卸 ─────
    /// <summary>棚卸ヘッダ（WM090）</summary>
    public DbSet<StockTake> StockTakes { get; set; }
    /// <summary>棚卸明細</summary>
    public DbSet<StockTakeDetail> StockTakeDetails { get; set; }

    // ───── MSBBWM100/150 WMS Phase 5 拡張（QC検品 + RMA返品） ─────
    /// <summary>入荷検品ヘッダ（WM100）</summary>
    public DbSet<QcInspection> QcInspections { get; set; }
    /// <summary>入荷検品明細</summary>
    public DbSet<QcInspectionItem> QcInspectionItems { get; set; }
    /// <summary>RMA返品ヘッダ（WM150）</summary>
    public DbSet<RmaHeader> RmaHeaders { get; set; }
    /// <summary>RMA返品明細</summary>
    public DbSet<RmaDetail> RmaDetails { get; set; }

    // ───── MSBBWM140 WMS キッティング ─────
    /// <summary>キット品マスタ</summary>
    public DbSet<KitMaster> KitMasters { get; set; }
    /// <summary>キット品 構成部品（BOM）</summary>
    public DbSet<KitMasterComponent> KitMasterComponents { get; set; }
    /// <summary>キット 組立/バラシ指示</summary>
    public DbSet<KitOrder> KitOrders { get; set; }

    // ───── MSBBWM110/120/130 WMS Logistics（スロッティング + 補充 + クロスドック） ─────
    /// <summary>クロスドック指示（WM130）</summary>
    public DbSet<CrossDockOrder> CrossDockOrders { get; set; }
    /// <summary>補充指示（WM120）</summary>
    public DbSet<ReplenishOrder> ReplenishOrders { get; set; }
    /// <summary>スロッティング最適化（WM110）</summary>
    public DbSet<SlottingPlan> SlottingPlans { get; set; }

    // ───── MSBBWM200/230/240/250 WMS 紙器業特化 ─────
    /// <summary>原紙ロール（WM200）</summary>
    public DbSet<PaperRoll> PaperRolls { get; set; }
    /// <summary>インキ・接着剤 ロット（WM230）</summary>
    public DbSet<InkLot> InkLots { get; set; }
    /// <summary>インキ色合せ履歴</summary>
    public DbSet<InkColorMatchHistory> InkColorMatchHistories { get; set; }
    /// <summary>パレット（WM240）</summary>
    public DbSet<Pallet> Pallets { get; set; }
    /// <summary>VMI 月次保管料（WM250）</summary>
    public DbSet<VmiBilling> VmiBillings { get; set; }

    // ───── MSBBWM210/220/260 WMS 紙器業特化 第2弾 ─────
    /// <summary>残材（WM210）</summary>
    public DbSet<RemnantMaterial> RemnantMaterials { get; set; }
    /// <summary>印版・木型（WM220）</summary>
    public DbSet<PlateMoldStock> PlateMoldStocks { get; set; }
    /// <summary>サンプル品（WM260）</summary>
    public DbSet<SampleStock> SampleStocks { get; set; }

    // ───── MSBBWM310/320/330 WMS 連携・モバイル・IoT ─────
    /// <summary>WCS タスク（WM310）</summary>
    public DbSet<WcsTask> WcsTasks { get; set; }
    /// <summary>配送業者 シップメント（WM320）</summary>
    public DbSet<CarrierShipment> CarrierShipments { get; set; }
    /// <summary>IoT センサ マスタ（WM330）</summary>
    public DbSet<IotSensor> IotSensors { get; set; }
    /// <summary>IoT センサ 計測値</summary>
    public DbSet<IotSensorReading> IotSensorReadings { get; set; }

    // ───── MSBBWM300 WMS モバイル作業指示 ─────
    /// <summary>モバイル作業指示（WM300）</summary>
    public DbSet<MobileTask> MobileTasks { get; set; }

    // ───── OA(Wf) 阶段1 运行时 ─────
    /// <summary>表单定义（OA 章02，JSON 列）</summary>
    public DbSet<Wf_FormDef> Wf_FormDefs { get; set; }
    /// <summary>表单数据（OA 章02，JSON 列）</summary>
    public DbSet<Wf_FormData> Wf_FormDatas { get; set; }
    /// <summary>流程定义（OA 章03，节点/边 schema JSON）</summary>
    public DbSet<Wf_FlowDef> Wf_FlowDefs { get; set; }
    /// <summary>流程实例（OA 章03，状态机状态载体）</summary>
    public DbSet<Wf_FlowInstance> Wf_FlowInstances { get; set; }
    /// <summary>流程待办任务（OA 章03，会签多条/节点）</summary>
    public DbSet<Wf_FlowTask> Wf_FlowTasks { get; set; }
    /// <summary>流程审批痕迹（OA 章03，仅追加时间线）</summary>
    public DbSet<Wf_FlowHistory> Wf_FlowHistories { get; set; }
    /// <summary>审批绑定（OA 章05 阶段2，业务类型→流程映射）</summary>
    public DbSet<Wf_ApprovalBinding> Wf_ApprovalBindings { get; set; }
    /// <summary>审批委派（OA 章07 §5，委托人→代理人有效期）</summary>
    public DbSet<Wf_FlowDelegate> Wf_FlowDelegates { get; set; }

    // ───── 财务（Fin）章01 总账内核 ─────
    /// <summary>会计科目（章01，多国别模板包 + Role 角色锚点）</summary>
    public DbSet<GlAccount> GlAccounts { get; set; }
    /// <summary>成本中心（章01，机台/工序/部门分析维度）</summary>
    public DbSet<CostCenter> CostCenters { get; set; }
    /// <summary>记账凭证头（章01，maker-checker 状态机 + 红冲）</summary>
    public DbSet<JournalEntry> JournalEntries { get; set; }
    /// <summary>记账凭证分录行（章01，借贷本位币 decimal）</summary>
    public DbSet<JournalLine> JournalLines { get; set; }
    /// <summary>财务采番计数器（章01，凭证号按月采番）</summary>
    public DbSet<FinSequence> FinSequences { get; set; }
    /// <summary>会计期间（章02，月结锁期）</summary>
    public DbSet<FiscalPeriod> FiscalPeriods { get; set; }
    /// <summary>记账规则（章05，自动凭证"规则即数据"）</summary>
    public DbSet<PostingRule> PostingRules { get; set; }
    /// <summary>记账规则行（章05，固定角色行 / 单据行透传）</summary>
    public DbSet<PostingRuleLine> PostingRuleLines { get; set; }
    /// <summary>应付发票（章03）</summary>
    public DbSet<ApInvoice> ApInvoices { get; set; }
    /// <summary>应付发票明细行（章03）</summary>
    public DbSet<ApInvoiceLine> ApInvoiceLines { get; set; }
    /// <summary>付款单（章03）</summary>
    public DbSet<Payment> Payments { get; set; }
    /// <summary>银行账户（章03）</summary>
    public DbSet<BankAccount> BankAccounts { get; set; }
    /// <summary>税码（章03）</summary>
    public DbSet<TaxCode> TaxCodes { get; set; }
    /// <summary>应付核销（章03）</summary>
    public DbSet<ApSettlement> ApSettlements { get; set; }
    /// <summary>应收发票（章04）</summary>
    public DbSet<ArInvoice> ArInvoices { get; set; }
    /// <summary>应收发票明细行（章04）</summary>
    public DbSet<ArInvoiceLine> ArInvoiceLines { get; set; }
    /// <summary>收款单（章04）</summary>
    public DbSet<Receipt> Receipts { get; set; }
    /// <summary>应收核销（章04）</summary>
    public DbSet<ArSettlement> ArSettlements { get; set; }
    /// <summary>成本单（章06，按工单归集料工费 + FG 单位成本）</summary>
    public DbSet<CostSheet> CostSheets { get; set; }
    /// <summary>成本归集明细行（章06）</summary>
    public DbSet<CostSheetLine> CostSheetLines { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // OA 章10 §7 租户注册表：租户编码全局唯一（共享表，不带 TenantId，不参与行级过滤）
        modelBuilder.Entity<Sys_Tenant>()
            .HasIndex(x => x.TenantCode).IsUnique().HasDatabaseName("UX_Sys_Tenant_Code");

        // PUB 章00 组织模型：部门树索引（B0-D1 本阶段不带 TenantId，DeptCode 单列唯一；多租户后升级为 (TenantId,DeptCode)）
        modelBuilder.Entity<Sys_Dept>(e =>
        {
            e.HasIndex(x => x.DeptCode).IsUnique();   // 部门编码唯一
            e.HasIndex(x => x.Path);                  // 子树前缀匹配
            e.HasIndex(x => x.ParentId);              // 取直接下级
        });
        modelBuilder.Entity<Sys_User>().HasIndex(x => x.DeptId);   // 按部门取人（DataScope）

        // PUB 章01 多角色：用户-角色中间表（B1-D1 RoleId int；B1-D3 章09 再加 TenantId）
        modelBuilder.Entity<Sys_UserRole>(e =>
        {
            e.HasIndex(x => new { x.UserId, x.RoleId }).IsUnique();   // 防重复授予同一角色
            e.HasIndex(x => x.UserId);                               // 按用户取全部角色
        });
        // PUB 章02 资源键：菜单稳定业务键唯一（NULL 不计入，过滤式唯一索引）
        modelBuilder.Entity<Sys_Menu>()
            .HasIndex(x => x.MenuKey).IsUnique()
            .HasFilter("[MenuKey] IS NOT NULL");

        // i18n 优化 P1/P3：多语言词条唯一（根治「重复 key 静默覆盖」）。
        // P3 加 TenantId 后改 UNIQUE(TenantId, LangKey)：SQL Server 唯一索引把 NULL 视为同值，
        // 故每个 LangKey 至多一行全局值(TenantId=null) + 各租户各一行覆盖值。
        // HasFilter(null) 关键：禁掉 EF 对可空列默认加的 [TenantId] IS NOT NULL 过滤——
        // 否则全局行(TenantId=null)被排除在唯一约束外，全局重复 key 不再被拦。
        // 无过滤时 SQL Server 把 NULL 视同值，恰好实现「每 key 至多一全局行 + 各租户各一行」。
        modelBuilder.Entity<Sys_Lang>()
            .HasIndex(x => new { x.TenantId, x.LangKey }).IsUnique()
            .HasFilter(null).HasDatabaseName("UX_Sys_Lang_Tenant_Key");

        // PUB 章02 功能权限：操作点 + 角色授权
        modelBuilder.Entity<Sys_MenuAction>(e =>
        {
            e.HasIndex(x => new { x.MenuId, x.ActionCode }).IsUnique()
                .HasDatabaseName("UX_Sys_MenuAction_MenuAction");
        });
        modelBuilder.Entity<Sys_RoleAction>(e =>
        {
            e.HasIndex(x => new { x.RoleId, x.MenuId, x.ActionCode }).IsUnique()
                .HasDatabaseName("UX_Sys_RoleAction_RoleMenuAction");
            e.HasIndex(x => x.RoleId).HasDatabaseName("IX_Sys_RoleAction_Role");
        });

        // PUB 章03 数据权限：角色数据范围（资源键 + 角色 唯一）
        modelBuilder.Entity<Sys_RoleDataScope>(e =>
        {
            e.HasIndex(x => new { x.RoleId, x.ResourceKey }).IsUnique()
                .HasDatabaseName("UX_Sys_RoleDataScope_RoleResource");
        });

        // PUB 章04 字段权限：角色字段访问级（角色 + 资源 + 字段 唯一）
        modelBuilder.Entity<Sys_RoleFieldPerm>(e =>
        {
            e.HasIndex(x => new { x.RoleId, x.ResourceKey, x.FieldName }).IsUnique()
                .HasDatabaseName("UX_Sys_RoleFieldPerm_RoleResourceField");
        });

        // PUB 章05 富采番：业务键唯一
        modelBuilder.Entity<Pub_DocSequence>(e =>
        {
            e.HasIndex(x => x.BizKey).IsUnique().HasDatabaseName("UX_Pub_DocSequence_BizKey");
        });

        // PUB 章06 附件：按业务取附件 + 按 hash 秒传/引用计数 + 草稿 token
        modelBuilder.Entity<Pub_Attachment>(e =>
        {
            e.HasIndex(x => new { x.BizType, x.BizId }).HasDatabaseName("IX_Pub_Attachment_Biz");
            e.HasIndex(x => x.FileHash).HasDatabaseName("IX_Pub_Attachment_Hash");
            e.HasIndex(x => x.DraftToken).HasDatabaseName("IX_Pub_Attachment_Draft");
        });

        // PUB 章08 代码生成元数据
        modelBuilder.Entity<GenTable>(e =>
        {
            e.HasIndex(x => x.EntityName).IsUnique().HasDatabaseName("UX_Pub_GenTable_Entity");
        });
        modelBuilder.Entity<GenColumn>(e =>
        {
            e.HasIndex(x => new { x.GenTableId, x.Sort }).HasDatabaseName("IX_Pub_GenColumn_Table");
        });

        // OA 章02 表单引擎：FormKey 唯一（本阶段全局；多租户后升 (TenantId,FormKey)）；FormData 按 FormKey/BizId 取
        modelBuilder.Entity<Wf_FormDef>(e =>
        {
            e.HasIndex(x => x.FormKey).IsUnique().HasDatabaseName("UX_Wf_FormDef_FormKey");
        });
        modelBuilder.Entity<Wf_FormData>(e =>
        {
            e.HasIndex(x => x.FormKey).HasDatabaseName("IX_Wf_FormData_FormKey");
            e.HasIndex(x => x.BizId).HasDatabaseName("IX_Wf_FormData_Biz");
        });

        // OA 章03 流程引擎：FlowKey 唯一；任务按 实例+节点 / 处理人+状态 取（待办中心高频）
        modelBuilder.Entity<Wf_FlowDef>(e =>
        {
            e.HasIndex(x => x.FlowKey).IsUnique().HasDatabaseName("UX_Wf_FlowDef_FlowKey");
        });
        modelBuilder.Entity<Wf_FlowInstance>(e =>
        {
            e.HasIndex(x => x.StarterId).HasDatabaseName("IX_Wf_FlowInstance_Starter");   // 我的申请
            e.HasIndex(x => new { x.FlowKey, x.Status }).HasDatabaseName("IX_Wf_FlowInstance_FlowStatus");
        });
        modelBuilder.Entity<Wf_FlowTask>(e =>
        {
            e.HasIndex(x => new { x.InstanceId, x.NodeId }).HasDatabaseName("IX_Wf_FlowTask_InstanceNode");  // 会签判定取本节点全部任务
            e.HasIndex(x => new { x.AssigneeId, x.Status }).HasDatabaseName("IX_Wf_FlowTask_AssigneeStatus"); // 待办中心
            e.HasIndex(x => new { x.Status, x.DueAt }).HasDatabaseName("IX_Wf_FlowTask_StatusDue");           // 章07 §4 超时扫描
        });
        modelBuilder.Entity<Wf_FlowDelegate>(e =>
        {
            e.HasIndex(x => new { x.GrantorId, x.Enable }).HasDatabaseName("IX_Wf_FlowDelegate_GrantorEnable"); // 建待办时查委派
        });
        modelBuilder.Entity<Wf_FlowHistory>(e =>
        {
            e.HasIndex(x => x.InstanceId).HasDatabaseName("IX_Wf_FlowHistory_Instance");  // 审批痕迹时间线
        });
        modelBuilder.Entity<Wf_ApprovalBinding>(e =>
        {
            e.HasIndex(x => x.BizType).IsUnique().HasDatabaseName("UX_Wf_ApprovalBinding_BizType");  // 一种业务类型一条绑定
        });

        // ═══════════════════════════════════════════════════════════
        //  财务（Fin）章01 总账内核
        // ═══════════════════════════════════════════════════════════

        // 会计科目：Code 唯一（单模板包部署）；Role 锚点供自动凭证查找；ParentId 取子科目
        modelBuilder.Entity<GlAccount>(e =>
        {
            e.HasIndex(x => x.Code).IsUnique();
            e.HasIndex(x => x.Role);
            e.HasIndex(x => x.ParentId);
            e.HasIndex(x => new { x.StandardScheme, x.IsActive });
        });

        // 成本中心：Code 唯一
        modelBuilder.Entity<CostCenter>(e =>
        {
            e.HasIndex(x => x.Code).IsUnique();
            e.HasIndex(x => x.ParentId);
        });

        // 记账凭证头：No 唯一 + 期间/状态检索 + 红冲互指
        modelBuilder.Entity<JournalEntry>(e =>
        {
            e.HasIndex(x => x.No).IsUnique();
            e.HasIndex(x => new { x.PeriodId, x.Status });
            e.HasIndex(x => x.Status);
            e.HasIndex(x => x.VoucherDate);
            e.HasIndex(x => x.ReverseOfId);

            // 章05 自动凭证幂等的 DB 兜底：同一来源单据至多一张已过账自动凭证。
            // 过滤：Source≠Manual(0) ∧ Status=Posted(2) ∧ SourceDocNo 非空（手工凭证/草稿不约束）。
            // InMemory 不强制过滤唯一索引，故引擎层另有代码级幂等查重（双保险）。
            e.HasIndex(x => new { x.Source, x.SourceDocNo })
                .IsUnique()
                .HasFilter("[Source] <> 0 AND [Status] = 2 AND [SourceDocNo] IS NOT NULL")
                .HasDatabaseName("UX_Fin_JournalEntry_AutoVoucherSource");

            // 分录行级联：FK = EntryId（凭证删除时行随删；业务上凭证不删，红冲产生新凭证）
            e.HasMany(x => x.Lines)
                .WithOne()
                .HasForeignKey(l => l.EntryId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // 凭证分录行：按凭证 + 科目检索（试算/余额滚算用）
        modelBuilder.Entity<JournalLine>(e =>
        {
            e.HasIndex(x => x.EntryId);
            e.HasIndex(x => x.AccountId);
            e.HasIndex(x => x.CostCenterId);
        });

        // 财务采番：(SeqKey + SeqDate) 唯一
        modelBuilder.Entity<FinSequence>(e =>
        {
            e.HasIndex(x => new { x.SeqKey, x.SeqDate }).IsUnique();
        });

        // 会计期间：(Year + Month) 唯一 + 财年口径检索
        modelBuilder.Entity<FiscalPeriod>(e =>
        {
            e.HasIndex(x => new { x.Year, x.Month }).IsUnique();
            e.HasIndex(x => new { x.FiscalYear, x.PeriodNo });
            e.HasIndex(x => x.Status);
        });

        // 记账规则（章05）：按 (事件类型 + 启用) 检索；规则行级联随头删
        modelBuilder.Entity<PostingRule>(e =>
        {
            e.HasIndex(x => new { x.EventType, x.IsActive });

            e.HasMany(x => x.Lines)
                .WithOne()
                .HasForeignKey(l => l.RuleId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // 记账规则行（章05）：按规则取行
        modelBuilder.Entity<PostingRuleLine>(e =>
        {
            e.HasIndex(x => x.RuleId);
        });

        // 应付发票（章03）：No 唯一 + 供应商发票号防重（过滤式，红字/空号不约束）+ 检索
        modelBuilder.Entity<ApInvoice>(e =>
        {
            e.HasIndex(x => x.No).IsUnique();
            e.HasIndex(x => new { x.SupplierId, x.SupplierInvoiceNo })
                .IsUnique()
                .HasFilter("[SupplierInvoiceNo] IS NOT NULL AND [IsCreditMemo] = 0")
                .HasDatabaseName("UX_Fin_ApInvoice_SupplierDupGuard");
            e.HasIndex(x => x.SupplierId);
            e.HasIndex(x => x.Status);
            e.HasIndex(x => x.DueDate);

            e.HasMany(x => x.Lines)
                .WithOne()
                .HasForeignKey(l => l.InvoiceId)
                .OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<ApInvoiceLine>(e => e.HasIndex(x => x.InvoiceId));

        // 付款单（章03）：No 唯一 + 按供应商/状态检索
        modelBuilder.Entity<Payment>(e =>
        {
            e.HasIndex(x => x.No).IsUnique();
            e.HasIndex(x => x.SupplierId);
            e.HasIndex(x => x.Status);
        });

        // 银行账户 / 税码：编码唯一
        modelBuilder.Entity<BankAccount>(e => e.HasIndex(x => x.Code).IsUnique());
        modelBuilder.Entity<TaxCode>(e => e.HasIndex(x => x.Code).IsUnique());

        // 应付核销（章03）：按付款 / 发票取核销关系
        modelBuilder.Entity<ApSettlement>(e =>
        {
            e.HasIndex(x => x.PaymentId);
            e.HasIndex(x => x.ApInvoiceId);
        });

        // 应收发票（章04）：No 唯一 + 出货号防重开票（过滤式幂等）+ 检索
        modelBuilder.Entity<ArInvoice>(e =>
        {
            e.HasIndex(x => x.No).IsUnique();
            e.HasIndex(x => new { x.ShipmentId })
                .IsUnique()
                .HasFilter("[ShipmentId] IS NOT NULL AND [IsCreditMemo] = 0")
                .HasDatabaseName("UX_Fin_ArInvoice_ShipmentDupGuard");
            e.HasIndex(x => x.CustomerId);
            e.HasIndex(x => x.Status);
            e.HasIndex(x => x.DueDate);

            e.HasMany(x => x.Lines)
                .WithOne()
                .HasForeignKey(l => l.InvoiceId)
                .OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<ArInvoiceLine>(e => e.HasIndex(x => x.InvoiceId));

        // 收款单（章04）：No 唯一 + 按客户/状态检索
        modelBuilder.Entity<Receipt>(e =>
        {
            e.HasIndex(x => x.No).IsUnique();
            e.HasIndex(x => x.CustomerId);
            e.HasIndex(x => x.Status);
        });

        // 应收核销（章04）
        modelBuilder.Entity<ArSettlement>(e =>
        {
            e.HasIndex(x => x.ReceiptId);
            e.HasIndex(x => x.ArInvoiceId);
        });

        // 見積計算書：QtnCalcNo 唯一
        modelBuilder.Entity<EstimateCalc>(e =>
        {
            e.HasIndex(x => x.QtnCalcNo).IsUnique();
            e.HasIndex(x => new { x.CustomerCd, x.IsDeleted });
            e.HasIndex(x => new { x.QtnDate, x.IsDeleted });

            // 级联加载工程明细（业务键关联，不走 FK）
            e.HasMany(x => x.Processes)
                .WithOne()
                .HasForeignKey(p => p.QtnCalcNo)
                .HasPrincipalKey(x => x.QtnCalcNo)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // 工程明细：QtnCalcNo + SeqNo 唯一
        modelBuilder.Entity<EstimateCalcProcess>(e =>
        {
            e.HasIndex(x => new { x.QtnCalcNo, x.SeqNo }).IsUnique();
        });

        // 全社統一採番カウンタ：FuncCode 唯一
        modelBuilder.Entity<DocSequence>(e =>
        {
            e.HasIndex(x => x.FuncCode).IsUnique();
        });

        // 拠点：BaseCd 唯一
        modelBuilder.Entity<MasterBase>(e =>
        {
            e.HasIndex(x => x.BaseCd).IsUnique();
        });

        // 担当者：StaffCd 唯一；BaseCd 索引（联动查询）
        modelBuilder.Entity<MasterStaff>(e =>
        {
            e.HasIndex(x => x.StaffCd).IsUnique();
            e.HasIndex(x => x.BaseCd);
        });

        // 汎用マスタ：GroupCode + Code 唯一
        modelBuilder.Entity<MasterGenericCode>(e =>
        {
            e.HasIndex(x => new { x.GroupCode, x.Code }).IsUnique();
        });

        // 御見積書：QtnNo 唯一；子表级联加载
        modelBuilder.Entity<Quotation>(e =>
        {
            e.HasIndex(x => x.QtnNo).IsUnique();
            e.HasIndex(x => new { x.CustomerCd, x.IsDeleted });
            e.HasIndex(x => new { x.QtnIssueDate, x.IsDeleted });
            e.HasIndex(x => new { x.BaseCd, x.StaffCd });

            // 業務键關聯（非 FK，方便软删与迁移）
            e.HasMany(x => x.Calcs)
                .WithOne()
                .HasForeignKey(p => p.QtnNo)
                .HasPrincipalKey(x => x.QtnNo)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasMany(x => x.Details)
                .WithOne()
                .HasForeignKey(p => p.QtnNo)
                .HasPrincipalKey(x => x.QtnNo)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // 御見積書×見積計算書 中间表：复合唯一
        modelBuilder.Entity<QuotationCalc>(e =>
        {
            e.HasIndex(x => new { x.QtnNo, x.QtnCalcNo }).IsUnique();
            e.HasIndex(x => x.QtnCalcNo);
        });

        // 御見積書明細：QtnNo + DetailNo 唯一
        modelBuilder.Entity<QuotationDetail>(e =>
        {
            e.HasIndex(x => new { x.QtnNo, x.DetailNo }).IsUnique();
        });

        // ───── MSBBPA050 Web 製品マスタ ─────

        // 製品基本マスタ：ProductCd 唯一；得意先・親案件・ステータスで检索
        modelBuilder.Entity<ProductMaster>(e =>
        {
            e.HasIndex(x => x.ProductCd).IsUnique();
            e.HasIndex(x => new { x.CustomerCd, x.IsDeleted });
            e.HasIndex(x => new { x.SetProductCd, x.IsDeleted });
            e.HasIndex(x => new { x.QuotationNo, x.IsDeleted });
            e.HasIndex(x => new { x.EstimateCalcNo, x.IsDeleted });
            e.HasIndex(x => new { x.Status, x.IsDeleted });
            e.HasIndex(x => new { x.ProjectNoParent, x.ProjectNoChild });
            e.HasIndex(x => x.ItemCd);

            // 子表级联加载（业务键关联，不走 FK，以便软删除/迁移）
            e.HasMany(x => x.Processes)
                .WithOne()
                .HasForeignKey(p => p.ProductCd)
                .HasPrincipalKey(x => x.ProductCd)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasMany(x => x.Materials)
                .WithOne()
                .HasForeignKey(p => p.ProductCd)
                .HasPrincipalKey(x => x.ProductCd)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasMany(x => x.LotPrices)
                .WithOne()
                .HasForeignKey(p => p.ProductCd)
                .HasPrincipalKey(x => x.ProductCd)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasMany(x => x.CoProducts)
                .WithOne()
                .HasForeignKey(p => p.ProductCd)
                .HasPrincipalKey(x => x.ProductCd)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // 製品加工工程：ProductCd + TaskCd 唯一
        modelBuilder.Entity<ProductProcess>(e =>
        {
            e.HasIndex(x => new { x.ProductCd, x.TaskCd }).IsUnique();
            e.HasIndex(x => new { x.ProductCd, x.SortOrder });
            e.HasIndex(x => x.ProcessCd);
        });

        // 製品加工材料：ProductCd + ProcessCd + MaterialCd 唯一
        modelBuilder.Entity<ProductMaterial>(e =>
        {
            e.HasIndex(x => new { x.ProductCd, x.ProcessCd, x.MaterialCd }).IsUnique();
            e.HasIndex(x => new { x.ProductCd, x.SortOrder });
        });

        // 製品ロット別単価：ProductCd + DetailNo 唯一
        modelBuilder.Entity<ProductLotPrice>(e =>
        {
            e.HasIndex(x => new { x.ProductCd, x.DetailNo }).IsUnique();
        });

        // 製品連産品：ProductCd + ProcessCd + RowNo 唯一
        modelBuilder.Entity<ProductCoProduct>(e =>
        {
            e.HasIndex(x => new { x.ProductCd, x.ProcessCd, x.RowNo }).IsUnique();
        });

        // ───── MSBBPA070/080/090 受注 ─────

        // 受注ヘッダー：WebOrderNo 唯一；得意先/受注日/受注区分で検索
        modelBuilder.Entity<Order>(e =>
        {
            e.HasIndex(x => x.WebOrderNo).IsUnique();
            e.HasIndex(x => new { x.CustomerCd, x.IsDeleted });
            e.HasIndex(x => new { x.OrderDate, x.IsDeleted });
            e.HasIndex(x => new { x.OrderType, x.IsDeleted });
            e.HasIndex(x => new { x.Status, x.IsDeleted });
            e.HasIndex(x => x.McOrderNo);

            // 子表级联加载（业务键关联）
            e.HasMany(x => x.Details)
                .WithOne(d => d.Order)
                .HasForeignKey(d => d.WebOrderNo)
                .HasPrincipalKey(x => x.WebOrderNo)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // 受注明細：WebOrderNo + WebOrderDetailNo 唯一；検索用 index 多数
        modelBuilder.Entity<OrderDetail>(e =>
        {
            e.HasIndex(x => new { x.WebOrderNo, x.WebOrderDetailNo }).IsUnique();
            e.HasIndex(x => x.HaibaiNo1);
            e.HasIndex(x => new { x.HaibaiNo2, x.HaibaiNo3 });
            e.HasIndex(x => x.ProductCd);
            e.HasIndex(x => x.ItemCd);
            e.HasIndex(x => new { x.CustomerDeliveryDate, x.IsDeleted });
            e.HasIndex(x => new { x.ProductCatBig, x.ProductCatMid, x.ProductCatSml });
            e.HasIndex(x => new { x.ApprovalStatus, x.IsDeleted });
            e.HasIndex(x => new { x.McTransferFlg, x.IsDeleted });

            // 工程・備考・材料 子表（業務键 — WebOrderNo + WebOrderDetailNo + ProductCd）
            e.HasMany(x => x.Processes)
                .WithOne()
                .HasForeignKey(p => new { p.WebOrderNo, p.WebOrderDetailNo, p.ProductCd })
                .HasPrincipalKey(x => new { x.WebOrderNo, x.WebOrderDetailNo, x.ProductCd })
                .OnDelete(DeleteBehavior.Cascade);

            e.HasMany(x => x.ProcessNotes)
                .WithOne()
                .HasForeignKey(p => new { p.WebOrderNo, p.WebOrderDetailNo, p.ProductCd })
                .HasPrincipalKey(x => new { x.WebOrderNo, x.WebOrderDetailNo, x.ProductCd })
                .OnDelete(DeleteBehavior.Cascade);

            e.HasMany(x => x.Materials)
                .WithOne()
                .HasForeignKey(p => new { p.WebOrderNo, p.WebOrderDetailNo, p.ProductCd })
                .HasPrincipalKey(x => new { x.WebOrderNo, x.WebOrderDetailNo, x.ProductCd })
                .OnDelete(DeleteBehavior.Cascade);
        });

        // OrderDetail の (WebOrderNo, WebOrderDetailNo, ProductCd) を主体キー扱いするための一意制約
        modelBuilder.Entity<OrderDetail>()
            .HasIndex(x => new { x.WebOrderNo, x.WebOrderDetailNo, x.ProductCd })
            .IsUnique()
            .HasDatabaseName("UX_OrderDetail_OrderProduct");

        modelBuilder.Entity<CreditNote>(e =>
        {
            e.HasIndex(x => x.CreditNoteNo).IsUnique();
            e.HasIndex(x => x.WebOrderNo);
        });

        // 受注加工工程：(WebOrderNo, WebOrderDetailNo, ProductCd, OperationCd) 唯一
        modelBuilder.Entity<OrderProcess>(e =>
        {
            e.HasIndex(x => new { x.WebOrderNo, x.WebOrderDetailNo, x.ProductCd, x.OperationCd }).IsUnique();
            e.HasIndex(x => new { x.WebOrderNo, x.WebOrderDetailNo, x.SortOrder });
            e.HasIndex(x => x.ProcessCd);
        });

        // 受注工程備考：(WebOrderNo, WebOrderDetailNo, ProductCd, OperationCd) 唯一
        modelBuilder.Entity<OrderProcessNote>(e =>
        {
            e.HasIndex(x => new { x.WebOrderNo, x.WebOrderDetailNo, x.ProductCd, x.OperationCd }).IsUnique();
        });

        // 受注加工材料：(WebOrderNo, WebOrderDetailNo, ProductCd, ProcessCd, MaterialCd) 唯一
        modelBuilder.Entity<OrderMaterial>(e =>
        {
            e.HasIndex(x => new { x.WebOrderNo, x.WebOrderDetailNo, x.ProductCd, x.ProcessCd, x.MaterialCd }).IsUnique();
            e.HasIndex(x => new { x.WebOrderNo, x.WebOrderDetailNo, x.SortOrder });
        });

        // ───── MSBBPA110/120 Web 取引先マスタ ─────

        // 取引先：BpCd 唯一；検索条件で多用される列に index
        modelBuilder.Entity<BusinessPartner>(e =>
        {
            e.HasIndex(x => x.BpCd).IsUnique();
            e.HasIndex(x => new { x.BaseCd, x.IsDeleted });
            e.HasIndex(x => new { x.Status, x.IsDeleted });
            e.HasIndex(x => x.SalesStaffCd);
            e.HasIndex(x => x.BusinessStaffCd);
            e.HasIndex(x => x.AreaCd);
            e.HasIndex(x => x.CustomerFlg);
            e.HasIndex(x => x.SupplierFlg);
            e.HasIndex(x => new { x.CreateDate, x.IsDeleted });
        });

        // ───── MSBBPA100 FSC チェックシート発行履歴 ─────

        modelBuilder.Entity<FscChecklist>(e =>
        {
            e.HasIndex(x => x.FscManagementNo).IsUnique();
            e.HasIndex(x => new { x.QtnNo, x.QtnCalcNo });
            e.HasIndex(x => new { x.IssueDate, x.IsDeleted });
        });

        // ───── MSBBPA130 シート単価 — 13 項目複合 PK ─────
        modelBuilder.Entity<SheetUnitPrice>(e =>
        {
            e.HasIndex(x => new {
                x.RevisionDate, x.BaseCd, x.CustomerCd, x.SheetFlute,
                x.PaperCdF, x.PrintCdF, x.EmbossCdF,
                x.PaperCdC, x.PrintCdC, x.EmbossCdC,
                x.PaperCdB, x.PrintCdB, x.EmbossCdB,
            }).IsUnique().HasDatabaseName("UX_SheetUnitPrice_Pk13");
            e.HasIndex(x => new { x.BaseCd, x.CustomerCd, x.IsDeleted });
            e.HasIndex(x => x.RevisionDate);
        });
        modelBuilder.Entity<SheetUnitPriceEstimate>(e =>
        {
            e.HasIndex(x => new {
                x.RevisionDate, x.BaseCd, x.CustomerCd, x.SheetFlute,
                x.PaperCdF, x.PrintCdF, x.EmbossCdF,
                x.PaperCdC, x.PrintCdC, x.EmbossCdC,
                x.PaperCdB, x.PrintCdB, x.EmbossCdB,
            }).IsUnique().HasDatabaseName("UX_SheetUnitPriceEst_Pk13");
            e.HasIndex(x => new { x.BaseCd, x.CustomerCd, x.IsDeleted });
            e.HasIndex(x => x.RevisionDate);
        });

        // ───── MSBBPA140/150 木型・版型管理マスタ ─────
        modelBuilder.Entity<PlateMold>(e =>
        {
            e.HasIndex(x => new { x.WdPtnNo, x.WdRev }).IsUnique();
            e.HasIndex(x => new { x.BaseCd, x.IsDeleted });
            e.HasIndex(x => x.CustomerCd);
            e.HasIndex(x => x.SupplierCd);
            e.HasIndex(x => x.RepresentativeProductCd);
            e.HasIndex(x => new { x.StDate, x.EndDate });
            e.HasIndex(x => x.PlaceCd);
            e.HasIndex(x => x.ProcessCd);
            e.HasIndex(x => x.TypeClass);
        });

        // ═══════════════════════════════════════════════════════════
        //  MSBBME010〜090 MES 製造執行
        // ═══════════════════════════════════════════════════════════

        // 製造指図ヘッダ：WORK_ORDER_NO 唯一
        modelBuilder.Entity<WorkOrder>(e =>
        {
            e.HasIndex(x => x.WorkOrderNo).IsUnique();
            e.HasIndex(x => new { x.Status, x.IsDeleted });
            e.HasIndex(x => new { x.ProductCd, x.IsDeleted });
            e.HasIndex(x => new { x.CustomerCd, x.IsDeleted });
            e.HasIndex(x => new { x.DeliveryDate, x.IsDeleted });
            e.HasIndex(x => x.WebOrderNo);

            // 子表 — 業務键 WORK_ORDER_NO 級聯
            e.HasMany(x => x.Processes)
                .WithOne()
                .HasForeignKey(p => p.WorkOrderNo)
                .HasPrincipalKey(x => x.WorkOrderNo)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasMany(x => x.Materials)
                .WithOne()
                .HasForeignKey(p => p.WorkOrderNo)
                .HasPrincipalKey(x => x.WorkOrderNo)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // 指図工程：(WORK_ORDER_NO, PROCESS_CD, TASK_CD) 唯一
        modelBuilder.Entity<WorkOrderProcess>(e =>
        {
            e.HasIndex(x => new { x.WorkOrderNo, x.ProcessCd, x.TaskCd }).IsUnique();
            e.HasIndex(x => new { x.WorkOrderNo, x.SortOrder });
            e.HasIndex(x => x.ProcessStatus);
            e.HasIndex(x => x.MachineCd);
            e.HasIndex(x => x.WgCd);
        });

        // 指図材料：(WORK_ORDER_NO, PROCESS_CD, MATERIAL_CD) 唯一
        modelBuilder.Entity<WorkOrderMaterial>(e =>
        {
            e.HasIndex(x => new { x.WorkOrderNo, x.ProcessCd, x.MaterialCd }).IsUnique();
            e.HasIndex(x => new { x.WorkOrderNo, x.SortOrder });
        });

        // 製造実績：RESULT_NO 唯一 + 検索 index
        modelBuilder.Entity<ProductionResult>(e =>
        {
            e.HasIndex(x => x.ResultNo).IsUnique();
            e.HasIndex(x => new { x.WorkOrderNo, x.ProcessCd, x.IsDeleted });
            e.HasIndex(x => new { x.OperatorCd, x.IsDeleted });
            e.HasIndex(x => new { x.ActualStartTime, x.IsDeleted });
            e.HasIndex(x => new { x.ActualEndTime, x.IsDeleted });
            e.HasIndex(x => x.ResultType);
        });

        // 品質検査ヘッダ：INSPECTION_NO 唯一
        modelBuilder.Entity<QualityInspection>(e =>
        {
            e.HasIndex(x => x.InspectionNo).IsUnique();
            e.HasIndex(x => new { x.WorkOrderNo, x.IsDeleted });
            e.HasIndex(x => new { x.InspectionDate, x.IsDeleted });
            e.HasIndex(x => x.OverallResult);

            e.HasMany(x => x.Items)
                .WithOne()
                .HasForeignKey(p => p.InspectionNo)
                .HasPrincipalKey(x => x.InspectionNo)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // 品質検査項目明細：(INSPECTION_NO + ITEM_SEQ_NO) 唯一
        modelBuilder.Entity<QualityInspectionItem>(e =>
        {
            e.HasIndex(x => new { x.InspectionNo, x.ItemSeqNo }).IsUnique();
        });

        // 不良品：DEFECT_NO 唯一
        modelBuilder.Entity<DefectRecord>(e =>
        {
            e.HasIndex(x => x.DefectNo).IsUnique();
            e.HasIndex(x => new { x.WorkOrderNo, x.IsDeleted });
            e.HasIndex(x => new { x.CategoryCd, x.DetailCd });
            e.HasIndex(x => new { x.Status, x.IsDeleted });
            e.HasIndex(x => new { x.OccurDate, x.IsDeleted });
        });

        // 検査項目テンプレート：(TEMPLATE_CD + ITEM_SEQ_NO) 唯一
        modelBuilder.Entity<InspectionTemplate>(e =>
        {
            e.HasIndex(x => new { x.TemplateCd, x.ItemSeqNo }).IsUnique();
            e.HasIndex(x => new { x.TemplateCd, x.ActiveFlg });
            e.HasIndex(x => x.ProcessCd);
        });

        // 不良分類：(CATEGORY_CD + DETAIL_CD) 唯一
        modelBuilder.Entity<DefectCategory>(e =>
        {
            e.HasIndex(x => new { x.CategoryCd, x.DetailCd }).IsUnique();
            e.HasIndex(x => x.ActiveFlg);
        });

        // MES採番管理：(SEQ_KEY + SEQ_DATE) 唯一
        modelBuilder.Entity<MesSequence>(e =>
        {
            e.HasIndex(x => new { x.SeqKey, x.SeqDate }).IsUnique();
        });

        // ═══════════════════════════════════════════════════════════
        //  MES Phase 4：設備管理 / OEE
        // ═══════════════════════════════════════════════════════════

        // 設備マスタ：MachineCd 唯一
        modelBuilder.Entity<Machine>(e =>
        {
            e.HasIndex(x => x.MachineCd).IsUnique();
            e.HasIndex(x => new { x.ProcessCd, x.ActiveFlg });
            e.HasIndex(x => new { x.WgCd, x.ActiveFlg });
            e.HasIndex(x => new { x.Status, x.ActiveFlg });
            e.HasIndex(x => x.BaseCd);
        });

        // 設備停止記録：DowntimeNo 唯一
        modelBuilder.Entity<MachineDowntime>(e =>
        {
            e.HasIndex(x => x.DowntimeNo).IsUnique();
            e.HasIndex(x => new { x.MachineCd, x.StartTime });
            e.HasIndex(x => new { x.MachineCd, x.EndTime });
            e.HasIndex(x => new { x.DowntimeType, x.IsDeleted });
            e.HasIndex(x => x.WorkOrderNo);
        });

        // OEE 日次：(OeeDate + MachineCd) 唯一
        modelBuilder.Entity<OeeDaily>(e =>
        {
            e.HasIndex(x => new { x.OeeDate, x.MachineCd }).IsUnique();
            e.HasIndex(x => new { x.MachineCd, x.OeeDate });
        });

        // ═══════════════════════════════════════════════════════════
        //  MSBBWM010〜090 WMS Phase 1 コア
        // ═══════════════════════════════════════════════════════════

        // 倉庫マスタ：WarehouseCd 唯一
        modelBuilder.Entity<Warehouse>(e =>
        {
            e.HasIndex(x => x.WarehouseCd).IsUnique();
            e.HasIndex(x => new { x.BaseCd, x.IsDeleted });
            e.HasIndex(x => new { x.WarehouseType, x.IsDeleted });
            // 多倉庫ルーティング（T14）：既存行の backfill も既定 100 で揃える
            e.Property(x => x.OutboundPriority).HasDefaultValue(100);
            e.HasIndex(x => x.OutboundPriority);
        });

        // ロケーション：LocationCd 唯一；倉庫+親ツリー；製品検索
        modelBuilder.Entity<Location>(e =>
        {
            e.HasIndex(x => x.LocationCd).IsUnique();
            e.HasIndex(x => new { x.WarehouseCd, x.ParentLocationCd });
            e.HasIndex(x => new { x.WarehouseCd, x.IsBlocked, x.IsPickable });
            e.HasIndex(x => x.Barcode);
        });

        // 在庫：(Warehouse, Location, Product, Lot) 業務 UK；FEFO / 製品検索用 index
        modelBuilder.Entity<Stock>(e =>
        {
            e.HasIndex(x => new { x.WarehouseCd, x.LocationCd, x.ProductCd, x.LotNo })
                .IsUnique()
                .HasDatabaseName("UX_Stock_WLPL");
            e.HasIndex(x => new { x.ProductCd, x.ExpiryDate });
            e.HasIndex(x => new { x.ProductCd, x.OwnerType, x.OwnerCd });
            e.HasIndex(x => new { x.WarehouseCd, x.IsDeleted });
            e.HasIndex(x => new { x.PaperRollNo });
            e.Property(x => x.QcStatus).HasMaxLength(10).HasDefaultValue(StockQcStatus.Pending).IsRequired();
            e.HasIndex(x => x.QcStatus);
        });

        // トランザクション：TxnNo 唯一；履歴照会・ロット追溯・伝票逆引き
        modelBuilder.Entity<StockTransaction>(e =>
        {
            e.HasIndex(x => x.TxnNo).IsUnique();
            e.HasIndex(x => x.TxnDateTime);
            e.HasIndex(x => new { x.ProductCd, x.LotNo, x.TxnDateTime });
            e.HasIndex(x => new { x.RelatedType, x.RelatedNo });
            e.HasIndex(x => new { x.TxnType, x.TxnDateTime });
            e.HasIndex(x => new { x.WarehouseCd, x.LocationCd, x.TxnDateTime });
        });

        // 採番：(Prefix, DateKey) 唯一
        modelBuilder.Entity<WmsSequence>(e =>
        {
            e.HasIndex(x => new { x.Prefix, x.DateKey }).IsUnique();
        });

        // ═══════════════════════════════════════════════════════════
        //  MSBBWM030/040 WMS Phase 2 入庫
        // ═══════════════════════════════════════════════════════════

        // 入庫予定：InboundNo 業務一意 + 検索 index
        modelBuilder.Entity<InboundOrder>(e =>
        {
            e.HasIndex(x => x.InboundNo).IsUnique();
            e.HasIndex(x => new { x.Status, x.IsDeleted });
            e.HasIndex(x => new { x.SupplierCd, x.IsDeleted });
            e.HasIndex(x => new { x.ExpectedArrivalDate, x.IsDeleted });
            e.HasIndex(x => new { x.WarehouseCd, x.IsDeleted });
        });

        // 入庫予定明細：(InboundNo, LineNo) 一意
        modelBuilder.Entity<InboundOrderDetail>(e =>
        {
            e.HasIndex(x => new { x.InboundNo, x.LineNo }).IsUnique();
            e.HasIndex(x => x.ProductCd);
        });

        // 入庫実績：ReceiptNo 一意 + 検索 index
        modelBuilder.Entity<InboundReceipt>(e =>
        {
            e.HasIndex(x => x.ReceiptNo).IsUnique();
            e.HasIndex(x => new { x.InboundNo, x.IsDeleted });
            e.HasIndex(x => new { x.WorkOrderNo, x.IsDeleted });
            e.HasIndex(x => new { x.ReceiveDateTime, x.IsDeleted });
            e.HasIndex(x => new { x.Status, x.IsDeleted });
        });

        // 入庫実績明細：(ReceiptNo, LineNo) 一意
        modelBuilder.Entity<InboundReceiptDetail>(e =>
        {
            e.HasIndex(x => new { x.ReceiptNo, x.LineNo }).IsUnique();
            e.HasIndex(x => new { x.ProductCd, x.LotNo });
            e.HasIndex(x => x.StockTxnNo);
        });

        // ═══════════════════════════════════════════════════════════
        //  MSBBWM050/070/080 WMS Phase 3 出庫
        // ═══════════════════════════════════════════════════════════

        // 出庫指示：OutboundNo 業務一意
        modelBuilder.Entity<OutboundOrder>(e =>
        {
            e.HasIndex(x => x.OutboundNo).IsUnique();
            e.HasIndex(x => new { x.Status, x.IsDeleted });
            e.HasIndex(x => new { x.OutboundType, x.Status });
            e.HasIndex(x => new { x.WorkOrderNo, x.IsDeleted });
            e.HasIndex(x => new { x.WebOrderNo, x.IsDeleted });
            e.HasIndex(x => new { x.CustomerCd, x.IsDeleted });
            e.HasIndex(x => new { x.PlannedDate, x.IsDeleted });
        });

        // 出庫指示明細：(OutboundNo, LineNo) 一意
        modelBuilder.Entity<OutboundOrderDetail>(e =>
        {
            e.HasIndex(x => new { x.OutboundNo, x.LineNo }).IsUnique();
            e.HasIndex(x => new { x.ProductCd, x.LotNo });
            e.HasIndex(x => x.AllocateTxnNo);
            e.HasIndex(x => x.ShipTxnNo);
        });

        // 出荷梱包：PackageNo 業務一意
        modelBuilder.Entity<ShippingPackage>(e =>
        {
            e.HasIndex(x => x.PackageNo).IsUnique();
            e.HasIndex(x => x.OutboundNo);
            e.HasIndex(x => x.TrackingNo);
            e.HasIndex(x => new { x.CarrierCd, x.DepartureTime });
        });

        modelBuilder.Entity<MaterialShortage>(e =>
        {
            e.HasIndex(x => new { x.Status, x.DetectedAt });
            e.HasIndex(x => x.WorkOrderNo);
        });

        modelBuilder.Entity<OutboundRoutingRule>(e =>
        {
            e.HasIndex(x => new { x.Enabled, x.SortOrder });
            e.HasIndex(x => new { x.CustomerCd, x.IsDeleted });
            e.HasIndex(x => x.TargetWarehouseCd);
        });

        modelBuilder.Entity<FxRate>(e =>
        {
            e.HasIndex(x => new { x.CurrencyCd, x.RateDate });
            // Order の凍結レート既定（既存行の backfill も基軸通貨 1.0 に揃える）
            modelBuilder.Entity<Order>().Property(x => x.CurrencyCd).HasDefaultValue(FxConstants.BaseCurrency);
            modelBuilder.Entity<Order>().Property(x => x.FxRate).HasDefaultValue(1m);
        });

        // ═══════════════════════════════════════════════════════════
        //  MSBBWM090 WMS Phase 4 棚卸
        // ═══════════════════════════════════════════════════════════

        modelBuilder.Entity<StockTake>(e =>
        {
            e.HasIndex(x => x.StockTakeNo).IsUnique();
            e.HasIndex(x => new { x.Status, x.IsDeleted });
            e.HasIndex(x => new { x.TargetWarehouseCd, x.IsDeleted });
            e.HasIndex(x => new { x.PlannedDate, x.IsDeleted });
        });

        modelBuilder.Entity<StockTakeDetail>(e =>
        {
            e.HasIndex(x => new { x.StockTakeNo, x.LineNo }).IsUnique();
            e.HasIndex(x => x.StockId);
            e.HasIndex(x => new { x.ProductCd, x.LotNo });
            e.HasIndex(x => x.ApprovalStatus);
        });

        // ═══════════════════════════════════════════════════════════
        //  MSBBWM100/150 WMS Phase 5 拡張
        // ═══════════════════════════════════════════════════════════

        modelBuilder.Entity<QcInspection>(e =>
        {
            e.HasIndex(x => x.InspectionNo).IsUnique();
            e.HasIndex(x => new { x.InboundNo, x.IsDeleted });
            e.HasIndex(x => new { x.Status, x.IsDeleted });
            e.HasIndex(x => new { x.ArrivalDateTime, x.IsDeleted });
            e.HasIndex(x => x.SupplierCd);
        });

        modelBuilder.Entity<QcInspectionItem>(e =>
        {
            e.HasIndex(x => new { x.InspectionNo, x.LineNo }).IsUnique();
            e.HasIndex(x => x.ProductCd);
        });

        modelBuilder.Entity<RmaHeader>(e =>
        {
            e.HasIndex(x => x.RmaNo).IsUnique();
            e.HasIndex(x => new { x.CustomerCd, x.IsDeleted });
            e.HasIndex(x => new { x.OriginalShippingNo, x.IsDeleted });
            e.HasIndex(x => new { x.Status, x.IsDeleted });
            e.HasIndex(x => new { x.AppliedDate, x.IsDeleted });
        });

        modelBuilder.Entity<RmaDetail>(e =>
        {
            e.HasIndex(x => new { x.RmaNo, x.LineNo }).IsUnique();
            e.HasIndex(x => new { x.ProductCd, x.LotNo });
            e.HasIndex(x => x.Judgement);
        });

        // ═══════════════════════════════════════════════════════════
        //  MSBBWM140 キッティング
        // ═══════════════════════════════════════════════════════════

        modelBuilder.Entity<KitMaster>(e =>
        {
            e.HasIndex(x => x.KitSku).IsUnique();
            e.HasIndex(x => new { x.ActiveFlg, x.IsDeleted });
        });

        modelBuilder.Entity<KitMasterComponent>(e =>
        {
            e.HasIndex(x => new { x.KitSku, x.LineNo }).IsUnique();
            e.HasIndex(x => x.ComponentProductCd);
        });

        modelBuilder.Entity<KitOrder>(e =>
        {
            e.HasIndex(x => x.KitOrderNo).IsUnique();
            e.HasIndex(x => new { x.KitSku, x.IsDeleted });
            e.HasIndex(x => new { x.Direction, x.Status });
            e.HasIndex(x => new { x.ExecutedAt, x.IsDeleted });
        });

        // ═══════════════════════════════════════════════════════════
        //  MSBBWM110/120/130 Logistics
        // ═══════════════════════════════════════════════════════════

        modelBuilder.Entity<CrossDockOrder>(e =>
        {
            e.HasIndex(x => x.XDockNo).IsUnique();
            e.HasIndex(x => new { x.Status, x.IsDeleted });
            e.HasIndex(x => new { x.InboundNo, x.IsDeleted });
            e.HasIndex(x => new { x.OutboundNo, x.IsDeleted });
            e.HasIndex(x => new { x.ProductCd, x.IsDeleted });
        });

        modelBuilder.Entity<ReplenishOrder>(e =>
        {
            e.HasIndex(x => x.ReplenishNo).IsUnique();
            e.HasIndex(x => new { x.Status, x.IsDeleted });
            e.HasIndex(x => new { x.ProductCd, x.IsDeleted });
            e.HasIndex(x => new { x.WarehouseCd, x.Status });
            e.HasIndex(x => new { x.Priority, x.Status });
        });

        modelBuilder.Entity<SlottingPlan>(e =>
        {
            e.HasIndex(x => x.SlottingPlanNo).IsUnique();
            e.HasIndex(x => new { x.WarehouseCd, x.IsDeleted });
            e.HasIndex(x => new { x.Status, x.IsDeleted });
            e.HasIndex(x => new { x.AnalyzedAt, x.IsDeleted });
        });

        // ═══════════════════════════════════════════════════════════
        //  MSBBWM200/230/240/250 紙器業特化
        // ═══════════════════════════════════════════════════════════

        // 原紙ロール：(紙質, 巾, 流れ) で適合検索が頻繁
        modelBuilder.Entity<PaperRoll>(e =>
        {
            e.HasIndex(x => x.RollNo).IsUnique();
            e.HasIndex(x => new { x.PaperGrade, x.WidthMm, x.GrainDirection, x.Status });
            e.HasIndex(x => new { x.Status, x.IsDeleted });
            e.HasIndex(x => new { x.WarehouseCd, x.LocationCd });
            e.HasIndex(x => x.RemainingLengthM);
        });

        modelBuilder.Entity<InkLot>(e =>
        {
            e.HasIndex(x => x.InkLotNo).IsUnique();
            e.HasIndex(x => x.ColorCode);
            e.HasIndex(x => new { x.InkType, x.IsDeleted });
            e.HasIndex(x => new { x.ExpiryDate, x.IsDeleted });
            e.HasIndex(x => new { x.OpenStatus, x.IsDeleted });
        });

        modelBuilder.Entity<InkColorMatchHistory>(e =>
        {
            e.HasIndex(x => x.MatchNo).IsUnique();
            e.HasIndex(x => new { x.CustomerCd, x.ColorCode });
            e.HasIndex(x => x.MatchedAt);
        });

        modelBuilder.Entity<Pallet>(e =>
        {
            e.HasIndex(x => x.PalletNo).IsUnique();
            e.HasIndex(x => new { x.ProductCd, x.LotNo });
            e.HasIndex(x => new { x.Status, x.IsDeleted });
            e.HasIndex(x => new { x.WarehouseCd, x.LocationCd });
            e.HasIndex(x => x.ShippedOutboundNo);
        });

        modelBuilder.Entity<VmiBilling>(e =>
        {
            e.HasIndex(x => x.BillingNo).IsUnique();
            e.HasIndex(x => new { x.CustomerCd, x.YearMonth }).IsUnique();
            e.HasIndex(x => x.YearMonth);
            e.HasIndex(x => x.Confirmed);
        });

        // ═══════════════════════════════════════════════════════════
        //  MSBBWM210/220/260 紙器業特化 第2弾
        // ═══════════════════════════════════════════════════════════

        // 残材：サイズ範囲検索 + 素材区分で再利用候補絞り込み
        modelBuilder.Entity<RemnantMaterial>(e =>
        {
            e.HasIndex(x => x.RemnantNo).IsUnique();
            e.HasIndex(x => new { x.MaterialType, x.Status, x.IsDeleted });
            e.HasIndex(x => new { x.WidthMm, x.LengthMm });
            e.HasIndex(x => new { x.WarehouseCd, x.LocationCd });
            e.HasIndex(x => x.SourceWorkOrderNo);
            e.HasIndex(x => x.SourceRollNo);
        });

        // 印版・木型：顧客×製品で検索、寿命警報
        modelBuilder.Entity<PlateMoldStock>(e =>
        {
            e.HasIndex(x => x.PlateNo).IsUnique();
            e.HasIndex(x => new { x.CustomerCd, x.ProductCd });
            e.HasIndex(x => new { x.PlateType, x.Status, x.IsDeleted });
            e.HasIndex(x => x.NextMaintenanceDate);
            e.HasIndex(x => new { x.WarehouseCd, x.LocationCd });
        });

        // サンプル：顧客×種別、未返却検索
        modelBuilder.Entity<SampleStock>(e =>
        {
            e.HasIndex(x => x.SampleNo).IsUnique();
            e.HasIndex(x => new { x.CustomerCd, x.SampleType });
            e.HasIndex(x => new { x.Status, x.IsDeleted });
            e.HasIndex(x => x.ExpectedReturnDate);
            e.HasIndex(x => new { x.WarehouseCd, x.LocationCd });
        });

        // ═══════════════════════════════════════════════════════════
        //  MSBBWM310/320/330 連携・モバイル・IoT
        // ═══════════════════════════════════════════════════════════

        modelBuilder.Entity<WcsTask>(e =>
        {
            e.HasIndex(x => x.TaskNo).IsUnique();
            e.HasIndex(x => new { x.Status, x.Priority, x.IsDeleted });
            e.HasIndex(x => x.DeviceCd);
            e.HasIndex(x => new { x.RelatedNo, x.RelatedType });
            e.HasIndex(x => x.CreatedAt);
        });

        modelBuilder.Entity<CarrierShipment>(e =>
        {
            e.HasIndex(x => x.ShipmentNo).IsUnique();
            e.HasIndex(x => x.PackageNo);
            e.HasIndex(x => x.TrackingNo);
            e.HasIndex(x => new { x.CarrierCd, x.Status, x.IsDeleted });
            e.HasIndex(x => x.CustomerCd);
        });

        modelBuilder.Entity<IotSensor>(e =>
        {
            e.HasIndex(x => x.SensorId).IsUnique();
            e.HasIndex(x => new { x.WarehouseCd, x.LocationCd });
            e.HasIndex(x => new { x.SensorType, x.IsEnabled });
        });

        modelBuilder.Entity<IotSensorReading>(e =>
        {
            e.HasIndex(x => new { x.SensorId, x.ReadAt });
            e.HasIndex(x => x.IsAlert);
        });

        modelBuilder.Entity<MobileTask>(e =>
        {
            e.HasIndex(x => x.MobileTaskNo).IsUnique();
            e.HasIndex(x => new { x.AssignedTo, x.Status, x.IsDeleted });
            e.HasIndex(x => new { x.TaskType, x.Status });
            e.HasIndex(x => new { x.Priority, x.Status });
            e.HasIndex(x => x.RelatedNo);
        });

        // ═══════════════════════════════════════════════════════════
        //  Phase 6 跨模块集成事件 + 受注ライフサイクル + 告警索引
        // ═══════════════════════════════════════════════════════════

        modelBuilder.Entity<IntegrationEvent>(e =>
        {
            // Worker 扫表主用：过滤 Failed + NextRetryAt 到期
            e.HasIndex(x => new { x.Status, x.NextRetryAt });
            // 端到端 trace：按 CorrelationId 串起整条业务链
            e.HasIndex(x => x.CorrelationId);
            // 按业务号查询历史 hook 调用
            e.HasIndex(x => new { x.SourceNo, x.HookName });
        });

        modelBuilder.Entity<Order>(e =>
        {
            // OrderStatus 是 Phase 6 新增；按 lifecycle status 检索受注（如「Cancellable 一覧」）
            e.HasIndex(x => new { x.OrderStatus, x.IsDeleted });
        });

        modelBuilder.Entity<Sys_OperLog>(e =>
        {
            // 告警 OperLog 快速过滤（IntegrationEvent DeadLetter 写入时 IsAlert=true）
            e.HasIndex(x => new { x.IsAlert, x.CreateDate });
        });

        // ═══════════════════════════════════════════════════════════
        //  章10 多租户：对所有 BaseTenantEntity 反射批量注册全局查询过滤（防漏命门，OA4-D1/D3）
        //  WHERE TenantId == CurrentTenantId —— 闭包到本上下文实例，EF 每次查询重读当前租户。
        // ═══════════════════════════════════════════════════════════
        foreach (var et in modelBuilder.Model.GetEntityTypes()
                     .Where(t => typeof(BaseTenantEntity).IsAssignableFrom(t.ClrType) && t.BaseType is null))
        {
            var p = Expression.Parameter(et.ClrType, "e");
            var body = Expression.Equal(
                Expression.Property(p, nameof(BaseTenantEntity.TenantId)),
                Expression.Property(Expression.Constant(this), nameof(CurrentTenantId)));
            modelBuilder.Entity(et.ClrType).HasQueryFilter(Expression.Lambda(body, p));
        }
    }

    /// <summary>写入盖章（章10 §4）：新增的 BaseTenantEntity 未显式设租户 → 盖当前租户。</summary>
    private void StampTenant()
    {
        foreach (var e in ChangeTracker.Entries<BaseTenantEntity>())
            if (e.State == EntityState.Added && e.Entity.TenantId == Guid.Empty)
                e.Entity.TenantId = CurrentTenantId;
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        StampTenant();   // SaveChanges() 经 base 路由至本重载，无需再覆盖无参版（避免重复盖章）
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        StampTenant();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }
}
