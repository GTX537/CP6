namespace CP6.Persistence.PostgreSql;

internal static class PostgreSqlFreshSeedV1
{
    // These insert-only source migration rows precede the ordinary application
    // language seed. The remaining corrected Japanese texts live in that seed.
    public const string BudgetLanguageSql = """
        INSERT INTO public."Sys_Langs"("TenantId","LangKey","Status","ZhCN","ZhTW","En","Ja","Ko")
        VALUES(NULL,'预算编制','reviewed','预算编制','預算編制','Budget Planning','予算編成','예산 편성'),
              (NULL,'执行分析','reviewed','执行分析','執行分析','Budget vs Actual','予実分析','예실 분석');
        """;
}
