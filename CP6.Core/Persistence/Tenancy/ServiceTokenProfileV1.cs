using Npgsql;

namespace CP6.Core.Persistence.Tenancy;

internal static class ServiceTokenProfileV1
{
    internal const string Profile = "service-token-v1";
    internal const string MigrationVersion = "service-token-tenant-rls-v1";
    internal const string Schema = "cp6_service_token_v1";

    // PostgreSQL records initdb exceptions in pg_init_privs; absent entries use acldefault.
    // Compare effective privileges, not ACL array text (GRANT/REVOKE can materialize a NULL ACL).
    // Scope is pg_catalog / pg_* objects; the legacy catalog and seal remain unchanged.
    private const string SystemAclObjectsSql = """
        SELECT 'pg_class'::regclass::oid AS classoid,c.oid AS objoid,0 AS objsubid,
          c.relacl AS acl,pg_catalog.acldefault((CASE WHEN c.relkind='S' THEN 's' ELSE 'r' END)::"char",c.relowner) AS defaults
        FROM pg_catalog.pg_class c JOIN pg_catalog.pg_namespace n ON n.oid=c.relnamespace
        WHERE n.nspname ~ '^pg_'
        UNION ALL
        SELECT 'pg_class'::regclass::oid,c.oid,a.attnum,a.attacl,pg_catalog.acldefault('c',c.relowner)
        FROM pg_catalog.pg_attribute a JOIN pg_catalog.pg_class c ON c.oid=a.attrelid
          JOIN pg_catalog.pg_namespace n ON n.oid=c.relnamespace
        WHERE a.attnum>0 AND NOT a.attisdropped AND (n.nspname ~ '^pg_')
        UNION ALL
        SELECT 'pg_proc'::regclass::oid,p.oid,0,p.proacl,pg_catalog.acldefault('f',p.proowner)
        FROM pg_catalog.pg_proc p JOIN pg_catalog.pg_namespace n ON n.oid=p.pronamespace
        WHERE n.nspname ~ '^pg_'
        UNION ALL
        SELECT 'pg_namespace'::regclass::oid,n.oid,0,n.nspacl,pg_catalog.acldefault('n',n.nspowner)
        FROM pg_catalog.pg_namespace n WHERE n.nspname ~ '^pg_'
        UNION ALL
        SELECT 'pg_type'::regclass::oid,t.oid,0,t.typacl,pg_catalog.acldefault('T',t.typowner)
        FROM pg_catalog.pg_type t JOIN pg_catalog.pg_namespace n ON n.oid=t.typnamespace
        WHERE n.nspname ~ '^pg_'
        """;

    internal static string CatalogSql => $$"""
        WITH system_objects AS ({{SystemAclObjectsSql}}), effective AS (
          SELECT o.classoid,o.objoid,o.objsubid,a.grantee,a.privilege_type,bool_or(a.is_grantable) AS grantable
          FROM system_objects o CROSS JOIN LATERAL pg_catalog.aclexplode(coalesce(o.acl,o.defaults)) a
          WHERE a.grantee=0 OR a.grantee IN(SELECT role_oid FROM cp6_tenancy_v1.login_tenant)
          GROUP BY o.classoid,o.objoid,o.objsubid,a.grantee,a.privilege_type)
        SELECT jsonb_build_array(({{TenantProvisioningCatalog.Sql}}),
          (SELECT coalesce(jsonb_agg(jsonb_build_array(classoid,objoid,objsubid,grantee,privilege_type,grantable)
            ORDER BY classoid,objoid,objsubid,grantee,privilege_type),'[]') FROM effective))::text
        """;

    internal static async Task ValidateFreshPublicAsync(NpgsqlConnection connection,NpgsqlTransaction tx,CancellationToken ct)
    {
        await using var command=new NpgsqlCommand($$"""
            WITH system_objects AS ({{SystemAclObjectsSql}})
            SELECT NOT EXISTS(
              SELECT FROM system_objects o
              LEFT JOIN pg_catalog.pg_init_privs i ON i.classoid=o.classoid AND i.objoid=o.objoid
                AND i.objsubid=o.objsubid AND i.privtype='i'
              CROSS JOIN LATERAL pg_catalog.aclexplode(coalesce(o.acl,o.defaults)) a
              WHERE a.grantee=0 AND NOT EXISTS(
                SELECT FROM pg_catalog.aclexplode(coalesce(i.initprivs,o.defaults)) original
                WHERE original.grantee=0 AND original.privilege_type=a.privilege_type
                  AND (NOT a.is_grantable OR original.is_grantable)))
            """,connection,tx);
        if(await command.ExecuteScalarAsync(ct).ConfigureAwait(false) is not true) throw TenantStorageBindingV1.Rejected();
    }

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
