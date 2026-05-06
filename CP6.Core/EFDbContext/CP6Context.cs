using CP6.Entity.DomainModels;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.EFDbContext;

/// <summary>
/// 数据库上下文 - 管理所有实体与数据库表的映射
/// 每新增一个实体，就在这里加一个 DbSet
/// </summary>
public class CP6Context : DbContext
{
    public CP6Context(DbContextOptions<CP6Context> options) : base(options)
    {
    }

    /// <summary>
    /// 文章表
    /// </summary>
    public DbSet<Article> Articles { get; set; }

    /// <summary>
    /// 用户表
    /// </summary>
    public DbSet<Sys_User> Sys_Users { get; set; }

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

    // ───── MSBBPA130 シート単価 ─────
    public DbSet<SheetUnitPrice> SheetUnitPrices { get; set; }
    public DbSet<SheetUnitPriceEstimate> SheetUnitPriceEstimates { get; set; }

    // ───── MSBBPA140/150 木型・版型管理マスタ ─────
    public DbSet<PlateMold> PlateMolds { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

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
    }
}
