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
    }
}
