using Xunit;

namespace CP6.Tests.Infra;

/// <summary>
/// 环境变量门控的 <see cref="FactAttribute"/>：仅当 <c>CP6_TEST_SQLSERVER</c>
/// 环境变量提供 SQL Server 连接串时才运行；缺失则 Skip（CI 默认恒绿）。
///
/// SQLite 无法覆盖三条真库语义——过滤唯一索引（HasFilter NULL 排除）、
/// 两阶段换码（经 NULL 中转）、原生 rowversion 乐观锁并发——故这些集成测试
/// 需真实 SQL Server。本机验证：
///   $env:CP6_TEST_SQLSERVER = "Server=127.0.0.1,1433;Database=master;User Id=sa;Password=***;TrustServerCertificate=True"
/// 连接串中 Database 段无所谓，测试自建唯一名临时库（CP6Test_{Guid:N}）并在结束时删除。
/// </summary>
public sealed class SqlServerFactAttribute : FactAttribute
{
    public const string EnvVar = "CP6_TEST_SQLSERVER";

    public SqlServerFactAttribute()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable(EnvVar)))
            Skip = $"设 {EnvVar}=<连接串> 以运行真库集成测试";
    }
}
