using System.Text.Json;
using CP6.Core.Services.Platform;
using Npgsql;
using NpgsqlTypes;
using static CP6.Core.Persistence.Tenancy.TenantStorageProvisioner;

namespace CP6.Core.Persistence.Tenancy;

internal static partial class RmaTenantStorageV1
{
    internal const string CoreReadSharedMigrationV2 = "20261113090000_RmaTenantModelV2Shared";
    private const string ModelSchema = "cp6_rma_model_v2";
    private const string ModelHistory = ModelSchema + ".tenant_migrations";
    private const string ModelWriters = ModelSchema + ".writer_transactions";

    private static string CoreReadModelV2Predicate(TenantStorageBindingV1 binding)
    {
        if (binding.ModelVersion != CoreReadProfileV2) return "";
        if (binding.MigrationVersion != CoreReadMigrationV2)
            throw new TenantStorageException(TenantStorageFailure.BindingRejected);

        return $"""
            AND EXISTS (SELECT FROM public."__EFMigrationsHistory" WHERE "MigrationId"='{CoreReadSharedMigrationV2}')
            AND EXISTS (
                SELECT FROM {ModelHistory} v
                JOIN {Q(binding.SchemaName)}.rma_migration_history_v1 h ON h.tenant_id=v.tenant_id
                WHERE v.tenant_id=m.tenant_id AND v.installation_id=i.installation_id
                  AND h.lineage=m.lineage AND h.migration_id=@migration
                  AND (h.operation_id,h.script_sha256,h.result_json)=(v.operation_id,v.script_sha256,v.result_json))
            """;
    }

    internal static async Task<string> MigrateCoreModelV2Async(NpgsqlConnection connection,
        RmaTenantMigrationRequestV1 request, CancellationToken ct)
    {
        if (request.InstallationId == Guid.Empty || request.TenantId == Guid.Empty || request.OperationId == Guid.Empty)
            throw new TenantProvisioningException("input-rejected");
        request.Target.Validate();

        await connection.OpenAsync(ct);
        await using var tx = await connection.BeginTransactionAsync(ct);
        await RequireControlTarget(connection, tx, request.Target, ct);
        if (connection.PostgreSqlVersion.Major < 15)
            throw new TenantProvisioningException("rma-model-shared-version-required");
        if (await Scalar(connection, tx, "SELECT coalesce(shobj_description(oid,'pg_database'),'') FROM pg_database WHERE datname=current_database()", ct)
            is string marker && marker.StartsWith(PrivacyRestoreNativeV1.MarkerPrefix, StringComparison.Ordinal))
            throw new TenantProvisioningException("target-identity-rejected");
        await Sql(connection, tx, $"""
            SELECT pg_catalog.pg_advisory_xact_lock({TenantLoginDirectoryV1.DirectoryLockNamespace},4);
            SELECT cp6_authentication_v1.acquire();
            SELECT pg_catalog.pg_advisory_xact_lock_shared({TenantLoginDirectoryV1.DirectoryLockNamespace},1);
            """, ct);

        if (await Scalar(connection, tx, $"""
            SELECT EXISTS (SELECT FROM public."__EFMigrationsHistory" WHERE "MigrationId"='{CoreReadSharedMigrationV2}')
                AND to_regclass('{ModelHistory}') IS NOT NULL
                AND to_regclass('{ModelWriters}') IS NOT NULL
            """, ct) is not true)
            throw new TenantProvisioningException("rma-model-shared-version-required");

        var schema = "t_" + request.TenantId.ToString("N");
        var (commission, tenant) = await RequireModelCommissionAsync(connection, tx, request, ct);
        await RequireCommissionedTenantAsync(connection, tx, commission, tenant, ct);
        var journal = await ReadJournalOverlayAsync(connection, tx, ct)
            ?? throw new TenantProvisioningException("draft-journal-required");
        await RequireJournalOverlayAsync(connection, tx, journal, CanonicalCoreRequest(commission), ct);
        var draft = await ReadDraftOverlayAsync(connection, tx, request.InstallationId, ct);
        var submit = await ReadSubmitOverlayAsync(connection, tx, request.InstallationId, ct)
            ?? throw new TenantProvisioningException("submit-draft-required");
        var schemas = commission.Tenants.Select(row => "t_" + row.TenantId.ToString("N")).ToArray();
        RequireSubmitTransition(submit, draft, await DraftCatalogSha256Async(connection, tx, schemas, ct));

        var ddl = ModelV2Ddl(Q(schema), Q(tenant.Login));
        var hash = Sha256(ddl);
        await using var receipt = new NpgsqlCommand($"""
            SELECT installation_id,operation_id,script_sha256,result_json FROM {ModelHistory}
            WHERE tenant_id=@tenant
            """, connection, tx);
        receipt.Parameters.AddWithValue("tenant", request.TenantId);
        string? original = null;
        await using (var row = await receipt.ExecuteReaderAsync(ct))
        {
            if (await row.ReadAsync(ct))
            {
                if (row.GetGuid(0) != request.InstallationId || row.GetGuid(1) != request.OperationId || row.GetString(2) != hash)
                    throw new TenantProvisioningException("operation-conflict");
                original = row.GetString(3);
            }
        }
        if (original is not null)
        {
            await RequireModelHistoryAsync(connection, tx, request, schema, hash, original, ct);
            return original;
        }

        await EnsureCoreModelV2WritersAsync(connection, tx, ct);
        await Sql(connection, tx, ddl, ct);
        var result = JsonSerializer.Serialize(new
        {
            Outcome = "READY", request.InstallationId, request.OperationId, request.TenantId, Schema = schema,
            Profile = CoreReadProfileV2, Lineage, MigrationId = CoreReadMigrationV2, SharedMigration = CoreReadSharedMigrationV2,
            ScriptSha256 = hash, request.Target.DatabaseOid, request.Target.SystemIdentifier, CompletedAt = DateTimeOffset.UtcNow
        });
        await using var save = new NpgsqlCommand($"""
            INSERT INTO {Q(schema)}.rma_migration_history_v1
                (tenant_id,lineage,migration_id,operation_id,script_sha256,result_json)
                VALUES (@tenant,@lineage,@migration,@operation,@hash,@result);
            INSERT INTO {ModelHistory}(tenant_id,installation_id,operation_id,script_sha256,result_json)
                VALUES (@tenant,@installation,@operation,@hash,@result);
            """, connection, tx);
        ModelV2Parameters(save, request, hash, result);
        await save.ExecuteNonQueryAsync(ct);
        await Commit(tx, ct);
        return result;
    }

    private static async Task<(RmaCoreCommissionRequestV1 Commission, TenantLoginV1 Tenant)> RequireModelCommissionAsync(
        NpgsqlConnection connection, NpgsqlTransaction tx, RmaTenantMigrationRequestV1 request, CancellationToken ct)
    {
        await using var command = new NpgsqlCommand($"""
            SELECT p.request_json,r.rolname::text FROM {CoreControl}.installation p CROSS JOIN {Control}.installation i
            JOIN {Control}.tenant_migrations m ON m.tenant_id=@tenant
            JOIN cp6_tenancy_v1.login_tenant d ON (d.tenant_id,d.role_oid,d.role_name)=(m.tenant_id,m.role_oid,m.role_name)
            JOIN pg_roles r ON (r.oid,r.rolname)=(d.role_oid,d.role_name)
            WHERE p.singleton AND p.commission_receipt IS NOT NULL AND i.singleton
                AND i.installation_id=@installation AND i.profile=@profile AND i.target=@target
                AND m.lineage=@lineage AND m.state='Ready' AND m.migration_id=@initial AND {PlainLogin}
            FOR SHARE OF i,p FOR UPDATE OF m
            """, connection, tx);
        command.Parameters.AddWithValue("tenant", request.TenantId);
        command.Parameters.AddWithValue("installation", request.InstallationId);
        command.Parameters.AddWithValue("profile", CoreProfile);
        command.Parameters.AddWithValue("target", NpgsqlDbType.Jsonb, JsonSerializer.Serialize(request.Target));
        command.Parameters.AddWithValue("lineage", Lineage);
        command.Parameters.AddWithValue("initial", MigrationId);
        await using var row = await command.ExecuteReaderAsync(ct);
        if (!await row.ReadAsync(ct)
            || JsonSerializer.Deserialize<RmaCoreCommissionRequestV1>(row.GetString(0)) is not { } commission
            || commission.Target != request.Target || commission.InstallationId != request.InstallationId
            || commission.Tenants.SingleOrDefault(t => t.TenantId == request.TenantId) is not { } tenant
            || row.GetString(1) != tenant.Login)
            throw new TenantProvisioningException("binding-rejected");
        return (commission, tenant);
    }

    private static string ModelV2Ddl(string schema, string login) => $"""
        CREATE VIEW {schema}.application_heads_v2 WITH (security_invoker=true) AS
            SELECT tenant_id,id,actor_id,outbound_id,version FROM {schema}.heads;
        CREATE VIEW {schema}.application_versions_v2 WITH (security_invoker=true) AS
            SELECT tenant_id,id,version,state,payload FROM {schema}.versions;
        REVOKE ALL ON {schema}.application_heads_v2,{schema}.application_versions_v2 FROM PUBLIC;
        GRANT SELECT ON {schema}.application_heads_v2,{schema}.application_versions_v2 TO {login};
        GRANT USAGE ON SCHEMA {ModelSchema} TO {login};
        GRANT EXECUTE ON FUNCTION {ModelDraft}, {ModelSubmit}, {ModelClose} TO {login};
        CREATE TRIGGER rma_model_v2_writer BEFORE INSERT OR UPDATE ON {schema}.heads
            FOR EACH ROW EXECUTE FUNCTION {ModelSchema}.require_writer();
        CREATE TRIGGER rma_model_v2_writer BEFORE INSERT ON {schema}.versions
            FOR EACH ROW EXECUTE FUNCTION {ModelSchema}.require_writer();
        ALTER TABLE {schema}.heads ENABLE ALWAYS TRIGGER rma_model_v2_writer;
        ALTER TABLE {schema}.versions ENABLE ALWAYS TRIGGER rma_model_v2_writer;
        """.ReplaceLineEndings("\n");

    private static void ModelV2Parameters(NpgsqlCommand command, RmaTenantMigrationRequestV1 request,
        string hash, string result)
    {
        command.Parameters.AddWithValue("tenant", request.TenantId);
        command.Parameters.AddWithValue("lineage", Lineage);
        command.Parameters.AddWithValue("migration", CoreReadMigrationV2);
        command.Parameters.AddWithValue("operation", request.OperationId);
        command.Parameters.AddWithValue("installation", request.InstallationId);
        command.Parameters.AddWithValue("hash", hash);
        command.Parameters.AddWithValue("result", result);
    }

    private static async Task RequireModelHistoryAsync(NpgsqlConnection connection, NpgsqlTransaction tx,
        RmaTenantMigrationRequestV1 request, string schema, string hash, string original, CancellationToken ct)
    {
        await using var command = new NpgsqlCommand($"""
            SELECT EXISTS (SELECT FROM {Q(schema)}.rma_migration_history_v1
                WHERE tenant_id=@tenant AND lineage=@lineage AND migration_id=@migration AND operation_id=@operation
                    AND script_sha256=@hash AND result_json=@result)
              AND (SELECT count(*) FROM pg_class WHERE relkind='v' AND relowner=current_user::regrole
                AND reloptions @> ARRAY['security_invoker=true']
                AND oid IN (to_regclass(@heads_view),to_regclass(@versions_view)))=2
              AND EXISTS(SELECT FROM pg_trigger WHERE tgname='rma_model_v2_journal_writer' AND tgenabled='A'
                AND tgrelid='{JournalTable}'::regclass AND tgtype=7
                AND tgfoid=to_regprocedure('{ModelSchema}.require_journal_writer()'))
              AND (SELECT count(*) FROM pg_trigger WHERE tgname='rma_model_v2_writer' AND tgenabled='A'
                AND tgfoid='{ModelSchema}.require_writer()'::regprocedure
                AND ((tgrelid=to_regclass(@heads) AND tgtype=23) OR (tgrelid=to_regclass(@versions) AND tgtype=7)))=2
            """, connection, tx);
        ModelV2Parameters(command, request, hash, original);
        command.Parameters.AddWithValue("heads_view", schema + ".application_heads_v2");
        command.Parameters.AddWithValue("versions_view", schema + ".application_versions_v2");
        command.Parameters.AddWithValue("heads", schema + ".heads");
        command.Parameters.AddWithValue("versions", schema + ".versions");
        if (await command.ExecuteScalarAsync(ct) is not true)
            throw new TenantProvisioningException("migration-history-mismatch");
    }
}
