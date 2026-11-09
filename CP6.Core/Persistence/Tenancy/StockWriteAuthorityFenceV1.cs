using Npgsql;

namespace CP6.Core.Persistence.Tenancy;

/// <summary>Explicitly extends the isolated stock read layout with a caller-transaction fence.</summary>
internal static class StockWriteAuthorityFenceV1
{
    internal const string Contract = "stock-write-fence-v1";

    internal static async Task InstallAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        Guid readInstallationId, IReadOnlyCollection<TenantLoginV1> writers, CancellationToken ct = default)
    {
        var roles = await PrepareInstallAsync(connection, transaction, readInstallationId, writers, requireFence: false, ct);
        // CREATE rejects prior or unknown objects. The dedicated owner can only enter the native root.
        await using var install = new NpgsqlCommand($$"""
            CREATE ROLE cp6_stock_write_fence_executor_v1
                NOLOGIN NOINHERIT NOSUPERUSER NOBYPASSRLS NOCREATEDB NOCREATEROLE NOREPLICATION;
            CREATE SCHEMA cp6_stock_write_fence_v1;
            REVOKE ALL ON SCHEMA cp6_stock_write_fence_v1 FROM PUBLIC;
            GRANT USAGE ON SCHEMA cp6_authentication_v1, cp6_platform_admin_guard_v1, cp6_tenancy_v1
                TO cp6_stock_write_fence_executor_v1;
            GRANT EXECUTE ON FUNCTION cp6_authentication_v1.acquire(), cp6_platform_admin_guard_v1.acquire(),
                cp6_tenancy_v1.current_tenant_id() TO cp6_stock_write_fence_executor_v1;
            GRANT SELECT (singleton, revision), UPDATE (revision) ON cp6_platform_admin_guard_v1.root
                TO cp6_stock_write_fence_executor_v1;

            CREATE FUNCTION cp6_stock_write_fence_v1.acquire(expected_tenant uuid)
            RETURNS boolean LANGUAGE plpgsql VOLATILE SECURITY DEFINER SET search_path = pg_catalog, pg_temp AS $cp6$
            BEGIN
                PERFORM cp6_authentication_v1.acquire();
                IF cp6_tenancy_v1.current_tenant_id() IS DISTINCT FROM expected_tenant THEN
                    RAISE EXCEPTION 'Stock write tenant binding rejected' USING ERRCODE = '42501';
                END IF;
                RETURN true;
            END $cp6$;
            ALTER FUNCTION cp6_stock_write_fence_v1.acquire(uuid) OWNER TO cp6_stock_write_fence_executor_v1;
            REVOKE ALL ON FUNCTION cp6_stock_write_fence_v1.acquire(uuid) FROM PUBLIC;
            GRANT USAGE ON SCHEMA cp6_stock_write_fence_v1 TO {{roles}};
            GRANT EXECUTE ON FUNCTION cp6_stock_write_fence_v1.acquire(uuid) TO {{roles}};
            """, connection, transaction);
        await install.ExecuteNonQueryAsync(ct);
    }

    internal static async Task<string> PrepareInstallAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        Guid readInstallationId, IReadOnlyCollection<TenantLoginV1> writers, bool requireFence, CancellationToken ct)
    {
        var bindings = writers.ToArray();
        TenantLoginDirectoryV1.ValidateArguments(connection, transaction, bindings);
        if (readInstallationId == Guid.Empty)
            throw new TenantStorageException(TenantStorageFailure.ConfigurationRejected);

        await using (var pinDirectory = new NpgsqlCommand($"""
            SET LOCAL search_path = pg_catalog;
            SELECT pg_catalog.pg_advisory_xact_lock_shared({TenantLoginDirectoryV1.DirectoryLockNamespace}, 1);
            """, connection, transaction))
            await pinDirectory.ExecuteNonQueryAsync(ct);

        await using var preflight = new NpgsqlCommand("""
            SELECT EXISTS (
                SELECT FROM pg_catalog.pg_roles control
                WHERE control.rolname=session_user AND current_user=session_user AND control.rolsuper
                  AND NOT EXISTS (
                    SELECT FROM pg_catalog.pg_default_acl d, LATERAL pg_catalog.aclexplode(d.defaclacl) a
                    WHERE d.defaclrole=control.oid AND a.grantee<>control.oid
                      AND NOT (d.defaclobjtype='f' AND a.grantee=0 AND a.privilege_type='EXECUTE')))
              AND EXISTS (
                SELECT FROM cp6_stock_read_v1.installation
                WHERE singleton AND installation_id=@installation AND model_version=@model AND migration_version=@migration)
              AND NOT EXISTS (
                SELECT FROM ROWS FROM (pg_catalog.unnest(@tenants), pg_catalog.unnest(@logins)) AS expected(tenant_id, login_name)
                WHERE NOT EXISTS (
                    SELECT FROM cp6_tenancy_v1.login_tenant m
                    JOIN pg_catalog.pg_roles r ON r.oid=m.role_oid AND r.rolname=m.role_name
                    WHERE m.tenant_id=expected.tenant_id AND r.rolname::text=expected.login_name AND r.rolcanlogin
                      AND NOT (r.rolsuper OR r.rolbypassrls OR r.rolcreatedb OR r.rolcreaterole OR r.rolreplication)
                      AND NOT EXISTS (SELECT FROM pg_catalog.pg_auth_members WHERE member=r.oid OR roleid=r.oid)
                      AND NOT EXISTS (SELECT FROM pg_catalog.pg_database WHERE datdba=r.oid)
                      AND (NOT @require_fence OR (
                        pg_catalog.has_schema_privilege(r.oid, pg_catalog.to_regnamespace('cp6_stock_write_fence_v1'), 'USAGE')
                        AND pg_catalog.has_function_privilege(r.oid, pg_catalog.to_regprocedure('cp6_stock_write_fence_v1.acquire(uuid)'), 'EXECUTE')))))
            """, connection, transaction);
        preflight.Parameters.AddWithValue("installation", readInstallationId);
        preflight.Parameters.AddWithValue("model", StockReadStorageV1.ModelVersion);
        preflight.Parameters.AddWithValue("migration", StockReadStorageV1.MigrationVersion);
        preflight.Parameters.AddWithValue("tenants", bindings.Select(binding => binding.TenantId).ToArray());
        preflight.Parameters.AddWithValue("logins", bindings.Select(binding => binding.Login).ToArray());
        preflight.Parameters.AddWithValue("require_fence", requireFence);
        if (await preflight.ExecuteScalarAsync(ct) is not true)
            throw new TenantStorageException(TenantStorageFailure.ConfigurationRejected);

        using var identifiers = new NpgsqlCommandBuilder();
        return string.Join(", ", bindings.Select(binding => identifiers.QuoteIdentifier(binding.Login)));
    }
}
