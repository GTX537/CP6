using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CP6.Core.Migrations
{
    /// <inheritdoc />
    public partial class WidenProductCategorySml : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 小分類码为 5 字符（如 A0101），原列 nvarchar(4) 会截断 → 拓宽到 6。
            // 用裸 SQL 直接 ALTER COLUMN：SQL Server 允许直接增大可变长度列的长度
            // （即使该列参与了非聚集索引），避免 EF 自动 DROP/CREATE 索引（DB 存在索引漂移）。
            migrationBuilder.Sql("ALTER TABLE [T_ProductMaster] ALTER COLUMN [ProductCatSml] nvarchar(6) NULL;");
            migrationBuilder.Sql("ALTER TABLE [T_OrderDetail] ALTER COLUMN [ProductCatSml] nvarchar(6) NULL;");
            migrationBuilder.Sql("ALTER TABLE [T_EstimateCalc] ALTER COLUMN [ProductCategorySml] nvarchar(6) NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("ALTER TABLE [T_ProductMaster] ALTER COLUMN [ProductCatSml] nvarchar(4) NULL;");
            migrationBuilder.Sql("ALTER TABLE [T_OrderDetail] ALTER COLUMN [ProductCatSml] nvarchar(4) NULL;");
            migrationBuilder.Sql("ALTER TABLE [T_EstimateCalc] ALTER COLUMN [ProductCategorySml] nvarchar(4) NULL;");
        }
    }
}
