using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CP6.Core.Migrations
{
    /// <summary>
    /// A5 i18n 数据修正迁移（无 schema 变更）。
    /// 把日语词条里误用的中文"方案"→标准日语"予算案"（11 处既有 key），并补 2 个中文自然语言 key
    /// （预算编制 / 执行分析，视图 t() 直接使用但全局曾缺失）。
    /// <para>
    /// 为何用数据迁移：项目 i18n seed 为 insert-only（已存在 LangKey 跳过、不更新），既有库无法靠重启 reseed 反映改值；
    /// 改为迁移后 db.Database.Migrate() 启动时对所有环境自动生效。
    /// </para>
    /// <para>
    /// 两种库均安全：①既有库——UPDATE 修旧值、INSERT 由 NOT EXISTS 守卫补缺键；
    /// ②全新库——本迁移先于种子执行，UPDATE 命中 0 行（行尚未 seed），2 个新 key 的 INSERT 先建、随后 seed 跳过（幂等），
    /// 其余 budget.*/E-A5-* 由 seed 以已修正源码插入。仅作用 TenantId IS NULL 的全局默认行。
    /// </para>
    /// </summary>
    public partial class A5BudgetI18nFix : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ── 11 处 Ja "方案" → "予算案"（仅全局默认行 TenantId IS NULL）──
            migrationBuilder.Sql("UPDATE Sys_Langs SET Ja = N'予算案管理、バージョン編成・承認、期間金額入力' WHERE LangKey = N'budget.workbench.subtitle' AND TenantId IS NULL;");
            migrationBuilder.Sql("UPDATE Sys_Langs SET Ja = N'予算案番号' WHERE LangKey = N'budget.field.budgetNo' AND TenantId IS NULL;");
            migrationBuilder.Sql("UPDATE Sys_Langs SET Ja = N'予算案名称' WHERE LangKey = N'budget.field.budgetName' AND TenantId IS NULL;");
            migrationBuilder.Sql("UPDATE Sys_Langs SET Ja = N'予算案新規' WHERE LangKey = N'budget.btn.createBudget' AND TenantId IS NULL;");
            migrationBuilder.Sql("UPDATE Sys_Langs SET Ja = N'予算案を作成しました' WHERE LangKey = N'budget.msg.created' AND TenantId IS NULL;");
            migrationBuilder.Sql("UPDATE Sys_Langs SET Ja = N'予算案を無効化しました' WHERE LangKey = N'budget.msg.deactivated' AND TenantId IS NULL;");
            migrationBuilder.Sql("UPDATE Sys_Langs SET Ja = N'この予算案を無効化しますか？' WHERE LangKey = N'budget.msg.deactivateConfirm' AND TenantId IS NULL;");
            migrationBuilder.Sql("UPDATE Sys_Langs SET Ja = N'会計年度 {0} の予算案が既に存在します' WHERE LangKey = N'E-A5-BUDGET-001' AND TenantId IS NULL;");
            migrationBuilder.Sql("UPDATE Sys_Langs SET Ja = N'予算案が見つかりません' WHERE LangKey = N'E-A5-BUDGET-404' AND TenantId IS NULL;");
            migrationBuilder.Sql("UPDATE Sys_Langs SET Ja = N'予算案' WHERE LangKey = N'budget.panel.budgets' AND TenantId IS NULL;");
            migrationBuilder.Sql("UPDATE Sys_Langs SET Ja = N'予算案がありません。新規作成してください' WHERE LangKey = N'budget.msg.noBudgets' AND TenantId IS NULL;");

            // ── 2 个中文自然语言 key（视图 page-header t() 用，全局曾缺失→曾渲染中文裸字）──
            migrationBuilder.Sql(
@"IF NOT EXISTS (SELECT 1 FROM Sys_Langs WHERE LangKey = N'预算编制' AND TenantId IS NULL)
    INSERT INTO Sys_Langs (TenantId, LangKey, Status, ZhCN, ZhTW, En, Ja, Ko)
    VALUES (NULL, N'预算编制', N'reviewed', N'预算编制', N'預算編制', N'Budget Planning', N'予算編成', N'예산 편성');");
            migrationBuilder.Sql(
@"IF NOT EXISTS (SELECT 1 FROM Sys_Langs WHERE LangKey = N'执行分析' AND TenantId IS NULL)
    INSERT INTO Sys_Langs (TenantId, LangKey, Status, ZhCN, ZhTW, En, Ja, Ko)
    VALUES (NULL, N'执行分析', N'reviewed', N'执行分析', N'執行分析', N'Budget vs Actual', N'予実分析', N'예실 분석');");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // 前向数据修正，不回滚（回滚将重新引入日语中文混用的瑕疵，无意义）。
        }
    }
}
