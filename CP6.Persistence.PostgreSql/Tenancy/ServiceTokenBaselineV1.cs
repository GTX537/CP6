namespace CP6.Persistence.PostgreSql.Tenancy;

/// <summary>Fresh-only five-table profile. Reuses the original frozen Core operations without stamping EF history.</summary>
public static class ServiceTokenBaselineV1
{
    public static string GenerateSql() => ServiceRevocationBaselineV1.GenerateSql(serviceTokens: true);
}
