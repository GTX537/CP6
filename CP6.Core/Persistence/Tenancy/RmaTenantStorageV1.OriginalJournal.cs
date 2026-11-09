using System.Text.Json;
using CP6.Core.Services.Wms;
using Npgsql;
using NpgsqlTypes;
using static CP6.Core.Persistence.Tenancy.TenantStorageProvisioner;

namespace CP6.Core.Persistence.Tenancy;

internal static partial class RmaTenantStorageV1
{
    private const string JournalProfile = "rma-core-original-journal-v1";
    private const string JournalSchema = "cp6_rma_original_journal_v1";
    private const string JournalInstallation = JournalSchema + ".installation";
    private const string JournalExecutor = "cp6_rma_original_journal_executor_v1";
    private const string JournalStore = JournalSchema + ".store(uuid,uuid,uuid,text,bytea,text,bytea,uuid[],jsonb)";
    private const string JournalTable = "cp6_main_owner_v1.operations";

    private sealed record JournalOverlay(string Request, string Commission, string CatalogSha256, string Result);

    internal static async Task<string> InstallCoreOriginalJournalAsync(NpgsqlConnection connection,
        RmaCoreCommissionRequestV1 request, CancellationToken ct)
    {
        ValidateCoreRequest(request);
        await connection.OpenAsync(ct);
        await using var tx = await connection.BeginTransactionAsync(ct);
        await RequireControlTarget(connection, tx, request.Target, ct);
        TenantLoginDirectoryV1.ValidateArguments(connection, tx, request.Tenants);
        await Sql(connection, tx, $"""
            SELECT pg_catalog.pg_advisory_xact_lock({TenantLoginDirectoryV1.DirectoryLockNamespace},4);
            SELECT cp6_authentication_v1.acquire();
            SELECT pg_catalog.pg_advisory_xact_lock_shared({TenantLoginDirectoryV1.DirectoryLockNamespace},1);
            """, ct);

        var commission = await RequireJournalCommissionAsync(connection, tx, request, ct);
        var requestJson = CanonicalCoreRequest(request);
        var commissionJson = CanonicalCoreRequest(commission);
        var prior = await ReadJournalOverlayAsync(connection, tx, ct);
        if (prior is not null)
        {
            if (prior.Request != requestJson) throw new TenantProvisioningException("operation-conflict");
            await RequireJournalOverlayAsync(connection, tx, prior, commissionJson, ct);
            return prior.Result;
        }

        // Acquire the old auth root before the one shared DDL lock. The statement trigger stays in place.
        await Sql(connection, tx, $"LOCK TABLE {JournalTable} IN ACCESS EXCLUSIVE MODE", ct);
        await RequireUnextendedJournalAsync(connection, tx, ct);
        var logins = string.Join(", ", request.Tenants.OrderBy(tenant => tenant.TenantId).Select(tenant => Q(tenant.Login)));
        await Sql(connection, tx, $"""
            CREATE ROLE {JournalExecutor} NOLOGIN NOINHERIT NOSUPERUSER NOBYPASSRLS NOCREATEDB NOCREATEROLE NOREPLICATION
            """, ct);
        var executorOid = (uint)(await Scalar(connection, tx, $"""
            SELECT oid FROM pg_catalog.pg_roles WHERE rolname='{JournalExecutor}'
            """, ct))!;
        await Sql(connection, tx, JournalDdl(logins, executorOid), ct);

        var catalogSha256 = await JournalCatalogSha256Async(connection, tx, ct);
        var result = JsonSerializer.Serialize(new
        {
            Outcome = "INSTALLED",
            Profile = JournalProfile,
            request.InstallationId,
            request.OperationId,
            CommissionOperationId = commission.OperationId,
            request.Target.DatabaseOid,
            request.Target.SystemIdentifier,
            TenantCount = request.Tenants.Length,
            CatalogSha256 = catalogSha256,
            CompletedAt = DateTimeOffset.UtcNow
        });
        await using var save = new NpgsqlCommand($"""
            INSERT INTO {JournalInstallation}(singleton,request_json,commission_json,catalog_sha256,result_json)
            VALUES (true,@request,@commission,@hash,@result)
            """, connection, tx);
        save.Parameters.AddWithValue("request", requestJson);
        save.Parameters.AddWithValue("commission", commissionJson);
        save.Parameters.AddWithValue("hash", catalogSha256);
        save.Parameters.AddWithValue("result", result);
        await save.ExecuteNonQueryAsync(ct);
        await Commit(tx, ct);
        return result;
    }

    private static async Task<RmaCoreCommissionRequestV1> RequireJournalCommissionAsync(NpgsqlConnection connection,
        NpgsqlTransaction tx, RmaCoreCommissionRequestV1 request, CancellationToken ct)
    {
        if (await Scalar(connection, tx, $"""
                SELECT pg_catalog.to_regclass('{CoreControl}.installation') IS NOT NULL
                   AND pg_catalog.to_regclass('{Control}.installation') IS NOT NULL
                   AND pg_catalog.to_regclass('{Control}.tenant_migrations') IS NOT NULL
                   AND pg_catalog.to_regclass('cp6_tenancy_v1.login_tenant') IS NOT NULL
                """, ct) is not true)
            throw new TenantProvisioningException("binding-rejected");

        var tenants = request.Tenants.OrderBy(tenant => tenant.TenantId).ToArray();
        await using var binding = new NpgsqlCommand($"""
            SELECT p.request_json FROM {CoreControl}.installation p CROSS JOIN {Control}.installation i
            WHERE p.singleton AND p.commission_receipt IS NOT NULL AND i.singleton
              AND i.installation_id=@installation AND i.profile=@profile AND i.target=@target AND i.bindings_json=@bindings
              AND (SELECT count(*) FROM cp6_tenancy_v1.login_tenant)=cardinality(@tenants)
              AND (SELECT count(*) FROM {Control}.tenant_migrations m
                   JOIN cp6_tenancy_v1.login_tenant d ON d.tenant_id=m.tenant_id AND d.role_oid=m.role_oid AND d.role_name=m.role_name
                   JOIN pg_catalog.pg_roles r ON r.oid=d.role_oid AND r.rolname=d.role_name
                   JOIN ROWS FROM (pg_catalog.unnest(@tenants),pg_catalog.unnest(@logins)) e(tenant,login)
                     ON e.tenant=m.tenant_id AND e.login=d.role_name::text
                   WHERE m.lineage=@lineage AND m.state='Ready' AND m.migration_id=@migration
                     AND {PlainLogin})=cardinality(@tenants)
            """, connection, tx);
        binding.Parameters.AddWithValue("installation", request.InstallationId);
        binding.Parameters.AddWithValue("profile", CoreProfile);
        binding.Parameters.AddWithValue("target", NpgsqlDbType.Jsonb, JsonSerializer.Serialize(request.Target));
        binding.Parameters.AddWithValue("bindings", JsonSerializer.Serialize(tenants));
        binding.Parameters.AddWithValue("tenants", tenants.Select(tenant => tenant.TenantId).ToArray());
        binding.Parameters.AddWithValue("logins", tenants.Select(tenant => tenant.Login).ToArray());
        binding.Parameters.AddWithValue("lineage", Lineage);
        binding.Parameters.AddWithValue("migration", MigrationId);
        if (await binding.ExecuteScalarAsync(ct) is not string original
            || JsonSerializer.Deserialize<RmaCoreCommissionRequestV1>(original) is not { } commission
            || original != CanonicalCoreRequest(request with { OperationId = commission.OperationId }))
            throw new TenantProvisioningException("binding-rejected");

        foreach (var tenant in tenants)
            await RequireCommissionedTenantAsync(connection, tx, commission, tenant, ct);
        return commission;
    }

    private static async Task RequireUnextendedJournalAsync(NpgsqlConnection connection, NpgsqlTransaction tx, CancellationToken ct)
    {
        await RequireLegacyFencesAsync(connection, tx, ct);
        if (await Scalar(connection, tx, $"""
                SELECT c.relkind='r' AND c.relowner=pg_catalog.to_regrole(pg_catalog.quote_ident(CURRENT_USER))
                    AND NOT c.relrowsecurity AND NOT c.relforcerowsecurity
                    AND NOT EXISTS (SELECT FROM pg_catalog.pg_policy WHERE polrelid=c.oid)
                    AND NOT EXISTS (SELECT FROM pg_catalog.aclexplode(c.relacl) a WHERE a.grantee<>c.relowner)
                    AND NOT EXISTS (SELECT FROM pg_catalog.pg_attribute a, LATERAL pg_catalog.aclexplode(a.attacl) acl
                                    WHERE a.attrelid=c.oid AND acl.grantee<>c.relowner)
                    AND NOT EXISTS (SELECT FROM pg_catalog.pg_default_acl d, LATERAL pg_catalog.aclexplode(d.defaclacl) a
                                    WHERE d.defaclrole=c.relowner AND d.defaclnamespace=0 AND a.grantee<>c.relowner
                                      AND NOT (d.defaclobjtype='f' AND a.grantee=0 AND a.privilege_type='EXECUTE'))
                    AND EXISTS (SELECT FROM public."__EFMigrationsHistory"
                                WHERE "MigrationId"='20261109014500_LocationWithdrawalJournalDispatchV1')
                    AND EXISTS (SELECT FROM pg_catalog.pg_trigger t
                                WHERE t.tgrelid=c.oid AND t.tgname='authority_gate' AND NOT t.tgisinternal
                                  AND t.tgtype=62 AND t.tgenabled IN ('O','A') AND t.tgqual IS NULL
                                  AND t.tgfoid=pg_catalog.to_regprocedure('cp6_authentication_v1.before_change()'))
                    AND EXISTS (SELECT FROM pg_catalog.pg_trigger t
                                WHERE t.tgrelid=c.oid AND t.tgname='immutable' AND NOT t.tgisinternal
                                  AND t.tgtype=58 AND t.tgenabled IN ('O','A') AND t.tgqual IS NULL
                                  AND t.tgfoid=pg_catalog.to_regprocedure('cp6_main_owner_v1.immutable()'))
                    AND EXISTS (SELECT FROM pg_catalog.pg_trigger t WHERE t.tgrelid=c.oid AND t.tgname='rma_core_legacy_disabled'
                                AND pg_catalog.translate(pg_catalog.pg_get_triggerdef(t.oid),'()','')=
                                  'CREATE TRIGGER rma_core_legacy_disabled BEFORE INSERT ON {JournalTable} FOR EACH ROW WHEN new.owner = ''{RmaOwner}''::text EXECUTE FUNCTION {CoreControl}.reject_legacy')
                FROM pg_catalog.pg_class c WHERE c.oid=pg_catalog.to_regclass('{JournalTable}')
                """, ct) is not true)
            throw new TenantProvisioningException("journal-overlay-mismatch");
        if (await Scalar(connection, tx, $"SELECT NOT EXISTS (SELECT FROM {JournalTable} WHERE owner='{RmaOwner}')", ct) is not true)
            throw new TenantProvisioningException("shared-rma-not-empty");
    }

    private static string JournalDdl(string logins, uint executorOid) => $$"""
        CREATE SCHEMA {{JournalSchema}};
        REVOKE ALL ON SCHEMA {{JournalSchema}} FROM PUBLIC;
        CREATE TABLE {{JournalInstallation}} (
            singleton boolean PRIMARY KEY CHECK(singleton),
            request_json text NOT NULL,
            commission_json text NOT NULL,
            catalog_sha256 text NOT NULL,
            result_json text NOT NULL
        );
        REVOKE ALL ON {{JournalInstallation}} FROM PUBLIC;

        ALTER TABLE {{JournalTable}} ENABLE ROW LEVEL SECURITY;
        ALTER TABLE {{JournalTable}} FORCE ROW LEVEL SECURITY;
        CREATE POLICY cp6_rma_journal_control_v1 ON {{JournalTable}} TO CURRENT_USER USING (true) WITH CHECK (true);
        CREATE POLICY cp6_rma_journal_read_v1 ON {{JournalTable}} FOR SELECT TO {{logins}}
            USING (tenant_id=(SELECT cp6_tenancy_v1.current_tenant_id()) AND owner='{{RmaOwner}}');
        CREATE POLICY cp6_rma_journal_append_v1 ON {{JournalTable}} FOR INSERT TO {{JournalExecutor}}
            WITH CHECK (tenant_id=(SELECT cp6_tenancy_v1.current_tenant_id()) AND owner='{{RmaOwner}}'
                AND contract='{{RmaApplicationOwnerV1.Contract}}');

        GRANT USAGE ON SCHEMA cp6_main_owner_v1, {{JournalSchema}} TO {{logins}};
        GRANT SELECT (tenant_id, owner, operation_id, actor_id, contract, state, input_bytes, input_sha256, result_bytes, object_ids)
            ON {{JournalTable}} TO {{logins}};
        GRANT USAGE ON SCHEMA cp6_main_owner_v1, cp6_authentication_v1, cp6_platform_admin_guard_v1, cp6_tenancy_v1
            TO {{JournalExecutor}};
        GRANT EXECUTE ON FUNCTION cp6_authentication_v1.acquire(), cp6_platform_admin_guard_v1.acquire(),
            cp6_tenancy_v1.current_tenant_id() TO {{JournalExecutor}};
        GRANT SELECT (singleton, revision), UPDATE (revision) ON cp6_platform_admin_guard_v1.root TO {{JournalExecutor}};
        GRANT INSERT (tenant_id, owner, operation_id, actor_id, contract, state, input_bytes, input_sha256, result_bytes, object_ids, audit_json)
            ON {{JournalTable}} TO {{JournalExecutor}};

        CREATE FUNCTION {{JournalSchema}}.store(tenant uuid, operation uuid, actor uuid, status text, input bytea,
            digest text, result bytea, objects uuid[], audit jsonb) RETURNS void
        LANGUAGE plpgsql VOLATILE SECURITY DEFINER SET search_path = pg_catalog, pg_temp
        AS $cp6$
        BEGIN
            PERFORM cp6_authentication_v1.acquire();
            INSERT INTO {{JournalTable}}(tenant_id, owner, operation_id, actor_id, contract, state,
                input_bytes, input_sha256, result_bytes, object_ids, audit_json)
            VALUES ($1,'{{RmaOwner}}',$2,$3,'{{RmaApplicationOwnerV1.Contract}}',$4,$5,$6,$7,$8,$9);
        END
        $cp6$;
        ALTER FUNCTION {{JournalStore}} OWNER TO {{JournalExecutor}};
        REVOKE ALL ON FUNCTION {{JournalStore}} FROM PUBLIC;
        GRANT EXECUTE ON FUNCTION {{JournalStore}} TO {{logins}};

        CREATE OR REPLACE TRIGGER rma_core_legacy_disabled BEFORE INSERT ON {{JournalTable}} FOR EACH ROW
        WHEN (NEW.owner='{{RmaOwner}}' AND pg_catalog.to_regrole(pg_catalog.quote_ident(CURRENT_USER))::pg_catalog.oid IS DISTINCT FROM {{executorOid}}::pg_catalog.oid)
        EXECUTE FUNCTION {{CoreControl}}.reject_legacy();
        ALTER TABLE {{JournalTable}} ENABLE ALWAYS TRIGGER rma_core_legacy_disabled;
        """;

    private static async Task<JournalOverlay?> ReadJournalOverlayAsync(NpgsqlConnection connection,
        NpgsqlTransaction tx, CancellationToken ct)
    {
        if (await Scalar(connection, tx, $"SELECT pg_catalog.to_regnamespace('{JournalSchema}') IS NULL", ct) is true)
            return null;
        if (await Scalar(connection, tx, $"""
                SELECT n.nspowner=pg_catalog.to_regrole(pg_catalog.quote_ident(CURRENT_USER)) AND c.relowner=n.nspowner
                    AND c.relkind='r' AND NOT c.relrowsecurity
                    AND NOT EXISTS (SELECT FROM pg_catalog.aclexplode(c.relacl) a WHERE a.grantee<>c.relowner)
                    AND NOT EXISTS (SELECT FROM pg_catalog.pg_attribute a, LATERAL pg_catalog.aclexplode(a.attacl) acl
                                    WHERE a.attrelid=c.oid AND acl.grantee<>c.relowner)
                FROM pg_catalog.pg_class c JOIN pg_catalog.pg_namespace n ON n.oid=c.relnamespace
                WHERE c.oid=pg_catalog.to_regclass('{JournalInstallation}')
                """, ct) is not true)
            throw new TenantProvisioningException("journal-overlay-mismatch");

        await using var command = new NpgsqlCommand($"""
            SELECT request_json, commission_json, catalog_sha256, result_json
            FROM {JournalInstallation} WHERE singleton
            """, connection, tx);
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) throw new TenantProvisioningException("journal-overlay-mismatch");
        return new JournalOverlay(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3));
    }

    private static async Task RequireJournalOverlayAsync(NpgsqlConnection connection, NpgsqlTransaction tx,
        JournalOverlay overlay, string commission, CancellationToken ct)
    {
        if (overlay.Commission != commission || overlay.CatalogSha256 != await JournalCatalogSha256Async(connection, tx, ct))
            throw new TenantProvisioningException("journal-overlay-mismatch");
    }

    private static async Task<bool> HasAdoptedOriginalJournalAsync(NpgsqlConnection connection, NpgsqlTransaction tx,
        RmaCoreCommissionRequestV1 request, CancellationToken ct)
    {
        var overlay = await ReadJournalOverlayAsync(connection, tx, ct);
        if (overlay is null) return false;
        await RequireJournalOverlayAsync(connection, tx, overlay, CanonicalCoreRequest(request), ct);
        return true;
    }

    private static async Task<string> JournalCatalogSha256Async(NpgsqlConnection connection, NpgsqlTransaction tx,
        CancellationToken ct)
    {
        // Pin the adopted permission boundary and the original trigger definitions for control-side recovery.
        var catalog = await Scalar(connection, tx, $"""
            SELECT pg_catalog.jsonb_build_object(
                'relation', pg_catalog.jsonb_build_array(c.oid,c.relowner,c.relacl,j.nspacl,s.nspacl),
                'columns', (SELECT pg_catalog.jsonb_agg(pg_catalog.jsonb_build_array(a.attnum,a.attname,a.attacl) ORDER BY a.attnum)
                            FROM pg_catalog.pg_attribute a WHERE a.attrelid=c.oid AND a.attnum>0 AND NOT a.attisdropped),
                'policies', (SELECT pg_catalog.jsonb_agg(pg_catalog.jsonb_build_array(q.oid,q.polname,q.polcmd,q.polpermissive,q.polroles,
                                 pg_catalog.pg_get_expr(q.polqual,c.oid),pg_catalog.pg_get_expr(q.polwithcheck,c.oid)) ORDER BY q.polname)
                             FROM pg_catalog.pg_policy q WHERE q.polrelid=c.oid),
                'triggers', (SELECT pg_catalog.jsonb_agg(pg_catalog.jsonb_build_array(t.oid,t.tgenabled,pg_catalog.pg_get_triggerdef(t.oid))
                                              ORDER BY t.tgname)
                             FROM pg_catalog.pg_trigger t WHERE t.tgrelid=c.oid
                               AND t.tgname IN ('authority_gate','immutable','rma_core_legacy_disabled')),
                'store', pg_catalog.jsonb_build_array(p.oid,p.proowner,p.proacl,pg_catalog.pg_get_functiondef(p.oid))
            )::text
            FROM pg_catalog.pg_class c JOIN pg_catalog.pg_namespace j ON j.oid=c.relnamespace
            JOIN pg_catalog.pg_proc p ON p.oid=pg_catalog.to_regprocedure('{JournalStore}')
            JOIN pg_catalog.pg_namespace s ON s.oid=p.pronamespace
            JOIN pg_catalog.pg_roles r ON r.oid=p.proowner
            WHERE c.oid=pg_catalog.to_regclass('{JournalTable}')
              AND c.relowner=pg_catalog.to_regrole(pg_catalog.quote_ident(CURRENT_USER)) AND s.nspowner=pg_catalog.to_regrole(pg_catalog.quote_ident(CURRENT_USER))
              AND c.relkind='r' AND c.relrowsecurity AND c.relforcerowsecurity AND p.prosecdef
              AND r.rolname='{JournalExecutor}' AND NOT (r.rolcanlogin OR r.rolsuper OR r.rolbypassrls
                  OR r.rolinherit OR r.rolcreatedb OR r.rolcreaterole OR r.rolreplication)
              AND NOT EXISTS (SELECT FROM pg_catalog.pg_auth_members m WHERE m.roleid=r.oid OR m.member=r.oid)
            """, ct);
        return catalog is string definition ? Sha256(definition) : throw new TenantProvisioningException("journal-overlay-mismatch");
    }
}
