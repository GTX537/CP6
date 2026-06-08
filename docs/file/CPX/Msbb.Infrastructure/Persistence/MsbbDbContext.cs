using Microsoft.EntityFrameworkCore;
using Msbb.Domain.Entities;

namespace Msbb.Infrastructure.Persistence
{
    public class MsbbDbContext : DbContext
    {
        public MsbbDbContext(DbContextOptions<MsbbDbContext> options) : base(options)
        {
        }

        // 告诉 EF Core 这里的类要生成数据库表
        public DbSet<SysMenu> SysMenus { get; set; }
        public DbSet<SysLog> SysLogs { get; set; }
        public DbSet<GeneralMaster> GeneralMasters { get; set; }


        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // 配置复合主键 (确保与之前定义一致)
            modelBuilder.Entity<SysMenu>()
                .HasKey(c => new { c.MajorCategoryNO, c.FunctionNO });

            modelBuilder.Entity<GeneralMaster>()
                .HasKey(g => new { g.ClassCode, g.Code });

            // --- 菜单数据初始化 (SysMenu Seed Data) ---
            modelBuilder.Entity<SysMenu>().HasData(
                // 大分类 10: 見积・报价
                new SysMenu { MajorCategoryNO = 10, FunctionNO = 10, MajorCategoryName = "見積・报价", FunctionName = "見積計算書入力", FunctionID = "MSBBPA010", Url = "/estimate/input", DisplayOrder = 10, InsUsrID = "SYSTEM", InsDate = DateTime.Now },
                new SysMenu { MajorCategoryNO = 10, FunctionNO = 20, MajorCategoryName = "見積・报价", FunctionName = "見積計算書一覧照会", FunctionID = "MSBBPA020", Url = "/estimate/list", DisplayOrder = 20, InsUsrID = "SYSTEM", InsDate = DateTime.Now },
                new SysMenu { MajorCategoryNO = 10, FunctionNO = 30, MajorCategoryName = "見積・报价", FunctionName = "御見積書登録・発行", FunctionID = "MSBBPA030", Url = "/quote/input", DisplayOrder = 30, InsUsrID = "SYSTEM", InsDate = DateTime.Now },
                new SysMenu { MajorCategoryNO = 10, FunctionNO = 40, MajorCategoryName = "見積・报价", FunctionName = "御見積書一覧照会", FunctionID = "MSBBPA040", Url = "/quote/list", DisplayOrder = 40, InsUsrID = "SYSTEM", InsDate = DateTime.Now },

                // 大分类 20: 受注管理
                new SysMenu { MajorCategoryNO = 20, FunctionNO = 10, MajorCategoryName = "受注管理", FunctionName = "受注入力", FunctionID = "MSBBPA070", Url = "/order/input", DisplayOrder = 10, InsUsrID = "SYSTEM", InsDate = DateTime.Now },
                new SysMenu { MajorCategoryNO = 20, FunctionNO = 20, MajorCategoryName = "受注管理", FunctionName = "受注一覧照会", FunctionID = "MSBBPA080", Url = "/order/list", DisplayOrder = 20, InsUsrID = "SYSTEM", InsDate = DateTime.Now },
                new SysMenu { MajorCategoryNO = 20, FunctionNO = 30, MajorCategoryName = "受注管理", FunctionName = "単価訂正", FunctionID = "MSBBPA090", Url = "/order/price-correction", DisplayOrder = 30, InsUsrID = "SYSTEM", InsDate = DateTime.Now },

                // 大分类 30: マスタ管理
                new SysMenu { MajorCategoryNO = 30, FunctionNO = 10, MajorCategoryName = "マスタ管理", FunctionName = "製品マスタ", FunctionID = "MSBBPA050", Url = "/master/product-input", DisplayOrder = 10, InsUsrID = "SYSTEM", InsDate = DateTime.Now },
                new SysMenu { MajorCategoryNO = 30, FunctionNO = 20, MajorCategoryName = "マスタ管理", FunctionName = "製品マスタ一覧照会", FunctionID = "MSBBPA060", Url = "/master/product-list", DisplayOrder = 20, InsUsrID = "SYSTEM", InsDate = DateTime.Now },
                new SysMenu { MajorCategoryNO = 30, FunctionNO = 30, MajorCategoryName = "マスタ管理", FunctionName = "取引先マスタ", FunctionID = "MSBBPA110", Url = "/master/bp-input", DisplayOrder = 30, InsUsrID = "SYSTEM", InsDate = DateTime.Now },
                new SysMenu { MajorCategoryNO = 30, FunctionNO = 40, MajorCategoryName = "マスタ管理", FunctionName = "取引先マスタ一覧照会", FunctionID = "MSBBPA120", Url = "/master/bp-list", DisplayOrder = 40, InsUsrID = "SYSTEM", InsDate = DateTime.Now },
                new SysMenu { MajorCategoryNO = 30, FunctionNO = 50, MajorCategoryName = "マスタ管理", FunctionName = "シート単価マスタ", FunctionID = "MSBBPA130", Url = "/master/sheet-price", DisplayOrder = 50, InsUsrID = "SYSTEM", InsDate = DateTime.Now },
                new SysMenu { MajorCategoryNO = 30, FunctionNO = 60, MajorCategoryName = "マスタ管理", FunctionName = "版型・木型マスタ", FunctionID = "MSBBPA140", Url = "/master/die-input", DisplayOrder = 60, InsUsrID = "SYSTEM", InsDate = DateTime.Now },
                new SysMenu { MajorCategoryNO = 30, FunctionNO = 70, MajorCategoryName = "マスタ管理", FunctionName = "版型・木型マスタ一覧照会", FunctionID = "MSBBPA150", Url = "/master/die-list", DisplayOrder = 70, InsUsrID = "SYSTEM", InsDate = DateTime.Now },

                // 大分类 99: 帳票出力
                new SysMenu { MajorCategoryNO = 99, FunctionNO = 10, MajorCategoryName = "帳票出力", FunctionName = "FSCチェックシート出力", FunctionID = "MSBBPA100", Url = "/report/fsc-output", DisplayOrder = 10, InsUsrID = "SYSTEM", InsDate = DateTime.Now }
            );

            // --- 通用字典数据初始化 (GeneralMaster Seed Data) ---
            modelBuilder.Entity<GeneralMaster>().HasData(
                // 常见单位 (UNITS)
                new GeneralMaster { ClassCode = "UNIT", Code = "01", Name = "個", DisplayOrder = 1, InsUsrID = "SYSTEM" },
                new GeneralMaster { ClassCode = "UNIT", Code = "02", Name = "枚", DisplayOrder = 2, InsUsrID = "SYSTEM" },
                new GeneralMaster { ClassCode = "UNIT", Code = "03", Name = "式", DisplayOrder = 3, InsUsrID = "SYSTEM" },

                // 状态 FLG (DATA_STATUS) - 参阅 #ステータスと転送FLGの扱い方.xlsx
                new GeneralMaster { ClassCode = "DATA_STATUS", Code = "0", Name = "未登録/新規", DisplayOrder = 10, InsUsrID = "SYSTEM" },
                new GeneralMaster { ClassCode = "DATA_STATUS", Code = "1", Name = "承認待ち", DisplayOrder = 20, InsUsrID = "SYSTEM" },
                new GeneralMaster { ClassCode = "DATA_STATUS", Code = "9", Name = "承認済/確定", DisplayOrder = 30, InsUsrID = "SYSTEM" }
            );
        }
    }
}