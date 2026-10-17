using Npgsql;

namespace CP6.Core.Persistence.Tenancy;

internal static class ServiceTokenProfileV1
{
    internal const string Profile = "service-token-v1";
    internal const string MigrationVersion = "service-token-tenant-rls-v1";
    internal const string Schema = "cp6_service_token_v1";

    // New profile extends the seal to explicit runtime grants on system objects. The legacy seal
    // remains byte-for-byte unchanged; unrelated grants in another database are outside this layout.
    internal static string CatalogSql => $$"""
        SELECT jsonb_build_array(({{TenantProvisioningCatalog.Sql}}),
          (SELECT coalesce(jsonb_agg(jsonb_build_array(c.oid,c.relacl) ORDER BY c.oid),'[]')
           FROM pg_catalog.pg_class c WHERE c.relnamespace='pg_catalog'::regnamespace
             AND EXISTS(SELECT FROM pg_catalog.aclexplode(c.relacl) a
               WHERE a.grantee IN(SELECT role_oid FROM cp6_tenancy_v1.login_tenant))),
          (SELECT coalesce(jsonb_agg(jsonb_build_array(p.oid,p.proacl) ORDER BY p.oid),'[]')
           FROM pg_catalog.pg_proc p WHERE p.pronamespace='pg_catalog'::regnamespace
             AND EXISTS(SELECT FROM pg_catalog.aclexplode(p.proacl) a
               WHERE a.grantee IN(SELECT role_oid FROM cp6_tenancy_v1.login_tenant))),
          (SELECT coalesce(jsonb_agg(jsonb_build_array(c.attrelid,c.attnum,c.attacl) ORDER BY c.attrelid,c.attnum),'[]')
           FROM pg_catalog.pg_attribute c JOIN pg_catalog.pg_class r ON r.oid=c.attrelid
           WHERE r.relnamespace='pg_catalog'::regnamespace AND EXISTS(SELECT FROM pg_catalog.aclexplode(c.attacl) a
             WHERE a.grantee IN(SELECT role_oid FROM cp6_tenancy_v1.login_tenant))),
          (SELECT coalesce(jsonb_agg(jsonb_build_array(n.oid,n.nspacl) ORDER BY n.oid),'[]')
           FROM pg_catalog.pg_namespace n WHERE n.nspname ~ '^pg_' AND EXISTS(SELECT FROM pg_catalog.aclexplode(n.nspacl) a
             WHERE a.grantee IN(SELECT role_oid FROM cp6_tenancy_v1.login_tenant))))::text
        """;

    internal static async Task<string> HashAsync(NpgsqlConnection connection,NpgsqlTransaction tx,CancellationToken ct)
    {
        await using var command=new NpgsqlCommand(CatalogSql,connection,tx);
        var value=(string)(await command.ExecuteScalarAsync(ct).ConfigureAwait(false))!;
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(value)));
    }

    internal static async Task ValidateFreshLoginAsync(NpgsqlConnection connection,NpgsqlTransaction tx,string login,CancellationToken ct)
    {
        await using var command=new NpgsqlCommand("""
            SELECT NOT EXISTS(SELECT FROM pg_catalog.pg_namespace n,LATERAL pg_catalog.aclexplode(n.nspacl) a WHERE a.grantee=@login::regrole)
              AND NOT EXISTS(SELECT FROM pg_catalog.pg_class c,LATERAL pg_catalog.aclexplode(c.relacl) a WHERE a.grantee=@login::regrole)
              AND NOT EXISTS(SELECT FROM pg_catalog.pg_attribute c,LATERAL pg_catalog.aclexplode(c.attacl) a WHERE a.grantee=@login::regrole)
              AND NOT EXISTS(SELECT FROM pg_catalog.pg_proc p,LATERAL pg_catalog.aclexplode(p.proacl) a WHERE a.grantee=@login::regrole)
              AND NOT EXISTS(SELECT FROM pg_catalog.pg_database d,LATERAL pg_catalog.aclexplode(d.datacl) a
                WHERE d.datname=current_database() AND a.grantee=@login::regrole AND (a.privilege_type<>'CONNECT' OR a.is_grantable))
              AND NOT EXISTS(SELECT FROM pg_catalog.pg_default_acl WHERE defaclrole=@login::regrole)
            """,connection,tx);
        command.Parameters.AddWithValue("login",login);
        if(await command.ExecuteScalarAsync(ct).ConfigureAwait(false) is not true) throw TenantStorageBindingV1.Rejected();
    }

    // No tenant argument, dynamic SQL or session setting supplied by callers. session_user is the
    // authenticated login even inside SECURITY DEFINER; the directory pins its mapping until commit.
    internal const string EligibilitySql = """
        CREATE FUNCTION cp6_service_token_v1.current_tenant_eligibility()
        RETURNS TABLE(enabled boolean, expires_at timestamp without time zone)
        LANGUAGE plpgsql VOLATILE SECURITY DEFINER SET search_path=pg_catalog,pg_temp
        AS $eligibility$
        DECLARE tenant uuid;
        BEGIN
          tenant := cp6_tenancy_v1.current_tenant_id();
          RETURN QUERY SELECT t."Enable",t."ExpireDate" FROM public."Sys_Tenants" t
            WHERE t."Id"=tenant FOR SHARE;
        END $eligibility$;
        REVOKE ALL ON FUNCTION cp6_service_token_v1.current_tenant_eligibility() FROM PUBLIC;
        """;

    internal static async Task RequireActiveAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        TimeProvider clock, CancellationToken ct)
    {
        await using var command = new NpgsqlCommand(
            "SELECT enabled,expires_at FROM cp6_service_token_v1.current_tenant_eligibility()", connection, transaction);
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        // Read the clock after acquiring the lock, including when another writer made us wait.
        if (!await reader.ReadAsync(ct).ConfigureAwait(false) || !reader.GetBoolean(0)
            || !reader.IsDBNull(1) && reader.GetDateTime(1) <= clock.GetUtcNow().UtcDateTime.ToLocalTime())
            throw new TenantStorageException(TenantStorageFailure.TenantInactive);
    }
}

internal sealed record ServiceTokenInstallRequest(Guid InstallationId, string Server, int Port, string Database,
    uint DatabaseOid, string SystemIdentifier, TenantLoginV1[] Tenants);
