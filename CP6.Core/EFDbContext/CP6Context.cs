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
    }
}
