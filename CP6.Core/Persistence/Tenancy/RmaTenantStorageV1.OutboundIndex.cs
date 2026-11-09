using System.Text.Json;
using Npgsql;
using NpgsqlTypes;
using static CP6.Core.Persistence.Tenancy.TenantStorageProvisioner;

namespace CP6.Core.Persistence.Tenancy;

internal static partial class RmaTenantStorageV1
{
    private const string OutboundIndexMigration = "rma-tenant-storage-0002-outbound-index-v1";
    private const string OutboundIndexName = "rma_heads_outbound_id_v1";
    private const string IndexSteps = Control + ".online_index_steps_v1";
    private const string Building = "Building";
    private const string Dropping = "Dropping";
    private const string Applied = "Applied";

    private sealed record IndexPlan(string Schema, string Stage, string Create, string Drop, string Rename, string Hash);
    private sealed record IndexTarget(Guid InitialOperation, uint HeadsOid, short OutboundColumn);
    private sealed record IndexStep(string State, uint? IndexOid, string? Result);
    private sealed record CatalogIndex(uint Oid, bool Valid, bool Ready, bool Live)
    {
        internal bool Usable => Valid && Ready && Live;
    }
    private sealed record IndexPreparation(IndexTarget Target, CatalogIndex? Stage, string? Original);

    internal static async Task<string> MigrateCoreOutboundIndexAsync(NpgsqlConnection connection,
        RmaTenantMigrationRequestV1 request, CancellationToken ct)
    {
        if (request.InstallationId == Guid.Empty || request.TenantId == Guid.Empty || request.OperationId == Guid.Empty)
            throw new TenantProvisioningException("input-rejected");

        var schema = "t_" + request.TenantId.ToString("N");
        var stage = "rma_heads_outbound_build_" + request.OperationId.ToString("N");
        var qualifiedStage = Q(schema) + "." + Q(stage);
        var create = $"CREATE INDEX CONCURRENTLY {Q(stage)} ON {Q(schema)}.heads USING btree (outbound_id)";
        var drop = $"DROP INDEX CONCURRENTLY {qualifiedStage}";
        var rename = $"ALTER INDEX {qualifiedStage} RENAME TO {Q(OutboundIndexName)}";
        var plan = new IndexPlan(schema, stage, create, drop, rename, Sha256(string.Join('\n', create, drop, rename)));

        await connection.OpenAsync(ct);
        var prepared = await PrepareIndexAsync(connection, request, plan, ct);
        if (prepared.Original is { } original) return original;

        try
        {
            // Each concurrent DDL command owns its native implicit transactions on this dedicated session.
            await IndexSessionSql(connection, "SET lock_timeout='15s'; SET statement_timeout='60s'", ct);
            if (prepared.Stage is { Valid: false })
            {
                await IndexSessionSql(connection, plan.Drop, ct);
                await using var reset = await connection.BeginTransactionAsync(ct);
                if (await FindIndexAsync(connection, reset, plan, prepared.Target, plan.Stage, ct) is not null
                    || await FindIndexAsync(connection, reset, plan, prepared.Target, OutboundIndexName, ct) is not null)
                    throw new TenantProvisioningException("index-object-conflict");
                await ChangeIndexStepAsync(connection, reset, request, Dropping, Building, null, null, ct);
                await Commit(reset, ct);
            }

            if (prepared.Stage is not { Valid: true })
                await IndexSessionSql(connection, plan.Create, ct);

            return await CompleteIndexAsync(connection, request, plan, prepared, ct);
        }
        catch (Exception error) when (error is NpgsqlException or OperationCanceledException)
        {
            // The committed intent makes retries use the same operation even if PostgreSQL lost its reply.
            throw new TenantProvisioningException(UnknownOutcome);
        }
    }

    private static async Task<IndexPreparation> PrepareIndexAsync(NpgsqlConnection connection,
        RmaTenantMigrationRequestV1 request, IndexPlan plan, CancellationToken ct)
    {
        await using var tx = await connection.BeginTransactionAsync(ct);
        await RequireControlTarget(connection, tx, request.Target, ct);
        // The CLI connection is unpooled. Session locks survive the DDL transactions and end on disconnect.
        await Sql(connection, tx, $"SELECT pg_catalog.pg_advisory_lock_shared({TenantLoginDirectoryV1.DirectoryLockNamespace},1)", ct);
        await using (var acquire = new NpgsqlCommand(
            "SELECT pg_catalog.pg_try_advisory_lock(pg_catalog.hashtextextended(@key,0))", connection, tx))
        {
            acquire.Parameters.AddWithValue("key", $"CP6.Main.RmaApplication.OnlineMigration/v1/{request.TenantId:D}");
            if (await acquire.ExecuteScalarAsync(ct) is not true)
                throw new TenantProvisioningException("migration-in-progress");
        }

        var target = await RequireIndexTargetAsync(connection, tx, request, plan.Schema, ct);
        await EnsureIndexStepsAsync(connection, tx, ct);
        var prior = await ReadIndexStepAsync(connection, tx, request, plan, target, ct);
        var final = await FindIndexAsync(connection, tx, plan, target, OutboundIndexName, ct);
        var stage = await FindIndexAsync(connection, tx, plan, target, plan.Stage, ct);
        if (prior is { State: Applied })
        {
            if (prior.Result is null || stage is not null || final is not { Usable: true } || final.Oid != prior.IndexOid)
                throw new TenantProvisioningException("index-object-conflict");
            return new(target, null, prior.Result);
        }

        if (final is not null)
            throw new TenantProvisioningException("index-object-conflict");
        if (prior is null)
        {
            if (stage is not null) throw new TenantProvisioningException("index-object-conflict");
            await InsertIndexStepAsync(connection, tx, request, plan, target, ct);
        }
        else if (prior.State == Dropping)
        {
            if (stage is null)
                await ChangeIndexStepAsync(connection, tx, request, Dropping, Building, null, null, ct);
            else if (stage.Valid || stage.Oid != prior.IndexOid)
                throw new TenantProvisioningException("index-object-conflict");
        }
        else if (stage is { Valid: false })
        {
            if (!stage.Live) throw new TenantProvisioningException("index-object-conflict");
            await ChangeIndexStepAsync(connection, tx, request, Building, Dropping, stage.Oid, null, ct);
        }
        else if (stage is { Usable: false })
            throw new TenantProvisioningException("index-object-conflict");

        await Commit(tx, ct);
        return new(target, stage, null);
    }

    private static async Task<IndexTarget> RequireIndexTargetAsync(NpgsqlConnection connection, NpgsqlTransaction tx,
        RmaTenantMigrationRequestV1 request, string schema, CancellationToken ct)
    {
        if (await Scalar(connection, tx, $"""
                SELECT pg_catalog.to_regclass('{Control}.installation') IS NOT NULL
                   AND pg_catalog.to_regclass('{Control}.tenant_migrations') IS NOT NULL
                   AND pg_catalog.to_regclass('{CoreControl}.installation') IS NOT NULL
                   AND pg_catalog.to_regclass('cp6_tenancy_v1.login_tenant') IS NOT NULL
                """, ct) is not true)
            throw new TenantProvisioningException("binding-rejected");

        string login, storedHash, storedResult;
        Guid initial;
        await using (var binding = new NpgsqlCommand($"""
            SELECT r.rolname::text,m.operation_id,m.script_sha256,m.result_json
            FROM {Control}.tenant_migrations m
            JOIN cp6_tenancy_v1.login_tenant d ON d.tenant_id=m.tenant_id AND d.role_oid=m.role_oid AND d.role_name=m.role_name
            JOIN pg_catalog.pg_roles r ON r.oid=d.role_oid AND r.rolname=d.role_name
            WHERE m.tenant_id=@tenant AND m.lineage=@lineage AND m.state='Ready' AND m.migration_id=@initial
              AND {PlainLogin}
              AND EXISTS (SELECT FROM {Control}.installation i WHERE i.singleton AND i.installation_id=@installation
                            AND i.profile=@profile AND i.target=@target)
              AND EXISTS (SELECT FROM {CoreControl}.installation c WHERE c.singleton AND c.commission_receipt IS NOT NULL
                            AND c.request_json::jsonb->>'InstallationId'=@installation::text
                            AND c.request_json::jsonb->>'OperationId'=m.operation_id::text)
            """, connection, tx))
        {
            binding.Parameters.AddWithValue("tenant", request.TenantId);
            binding.Parameters.AddWithValue("lineage", Lineage);
            binding.Parameters.AddWithValue("initial", MigrationId);
            binding.Parameters.AddWithValue("installation", request.InstallationId);
            binding.Parameters.AddWithValue("profile", CoreProfile);
            binding.Parameters.AddWithValue("target", NpgsqlDbType.Jsonb, JsonSerializer.Serialize(request.Target));
            await using var row = await binding.ExecuteReaderAsync(ct);
            if (!await row.ReadAsync(ct)) throw new TenantProvisioningException("binding-rejected");
            login = row.GetString(0);
            initial = row.GetGuid(1);
            storedHash = row.GetString(2);
            storedResult = row.GetString(3);
        }

        var expected = Sha256(TenantDdl(request.TenantId, Q(schema), Q(login), writable: false));
        if (storedHash != expected) throw new TenantProvisioningException("migration-history-mismatch");
        await RequireTenantHistoryAsync(connection, tx, request with { OperationId = initial }, schema, expected, storedResult, ct);

        await using var heads = new NpgsqlCommand("""
            SELECT c.oid,a.attnum
            FROM pg_catalog.pg_class c
            JOIN pg_catalog.pg_namespace n ON n.oid=c.relnamespace
            JOIN pg_catalog.pg_attribute a ON a.attrelid=c.oid AND a.attname='outbound_id'
                AND a.attnum>0 AND a.attnotnull AND NOT a.attisdropped AND a.atttypid='pg_catalog.uuid'::regtype
            WHERE n.nspname=@schema AND c.relname='heads' AND c.relkind='r' AND c.relpersistence='p'
            """, connection, tx);
        heads.Parameters.AddWithValue("schema", schema);
        await using var found = await heads.ExecuteReaderAsync(ct);
        if (!await found.ReadAsync(ct)) throw new TenantProvisioningException("migration-history-mismatch");
        return new(initial, found.GetFieldValue<uint>(0), found.GetInt16(1));
    }

    private static async Task EnsureIndexStepsAsync(NpgsqlConnection connection, NpgsqlTransaction tx, CancellationToken ct)
    {
        // Bootstrap the extra control table without holding a common lock across a tenant's build.
        await Sql(connection, tx, $"SELECT pg_catalog.pg_advisory_xact_lock({TenantLoginDirectoryV1.DirectoryLockNamespace},5)", ct);
        var shape = await Scalar(connection, tx, $"""
            SELECT CASE WHEN n.nspowner<>current_user::regrole THEN -1
                        WHEN c.oid IS NULL THEN 0
                        WHEN c.relowner=current_user::regrole AND c.relkind='r' AND c.relpersistence='p'
                             AND NOT c.relrowsecurity AND NOT c.relforcerowsecurity
                             AND NOT EXISTS (SELECT FROM pg_catalog.aclexplode(c.relacl) a WHERE a.grantee<>c.relowner)
                          THEN 1 ELSE -1 END
            FROM pg_catalog.pg_namespace n
            LEFT JOIN pg_catalog.pg_class c ON c.relnamespace=n.oid AND c.relname='online_index_steps_v1'
            WHERE n.nspname='{Control}'
            """, ct);
        if (shape is 1) return;
        if (shape is not 0) throw new TenantProvisioningException("migration-history-mismatch");

        await Sql(connection, tx, $"""
            CREATE TABLE {IndexSteps} (
                tenant_id uuid NOT NULL,
                migration_id text NOT NULL,
                installation_id uuid NOT NULL,
                initial_operation_id uuid NOT NULL,
                operation_id uuid NOT NULL,
                table_oid oid NOT NULL,
                script_sha256 text NOT NULL,
                state text NOT NULL,
                index_oid oid,
                result_json text,
                PRIMARY KEY (tenant_id,migration_id),
                CHECK ((state='{Building}' AND index_oid IS NULL AND result_json IS NULL)
                    OR (state='{Dropping}' AND index_oid IS NOT NULL AND result_json IS NULL)
                    OR (state='{Applied}' AND index_oid IS NOT NULL AND result_json IS NOT NULL))
            );
            REVOKE ALL ON {IndexSteps} FROM PUBLIC;
            """, ct);
    }

    private static async Task<IndexStep?> ReadIndexStepAsync(NpgsqlConnection connection, NpgsqlTransaction tx,
        RmaTenantMigrationRequestV1 request, IndexPlan plan, IndexTarget target, CancellationToken ct)
    {
        await using var command = new NpgsqlCommand($"""
            SELECT operation_id,installation_id,initial_operation_id,table_oid,script_sha256,state,index_oid,result_json
            FROM {IndexSteps}
            WHERE tenant_id=@tenant AND migration_id=@migration
            """, connection, tx);
        IndexStepKey(command, request);
        await using var row = await command.ExecuteReaderAsync(ct);
        if (!await row.ReadAsync(ct)) return null;
        if (row.GetGuid(0) != request.OperationId) throw new TenantProvisioningException("operation-conflict");
        if (row.GetGuid(1) != request.InstallationId || row.GetGuid(2) != target.InitialOperation
            || row.GetFieldValue<uint>(3) != target.HeadsOid || row.GetString(4) != plan.Hash)
            throw new TenantProvisioningException("migration-history-mismatch");

        var state = row.GetString(5);
        if (state is not (Building or Dropping or Applied))
            throw new TenantProvisioningException("migration-history-mismatch");
        return new(state, row.IsDBNull(6) ? null : row.GetFieldValue<uint>(6),
            row.IsDBNull(7) ? null : row.GetString(7));
    }

    private static async Task InsertIndexStepAsync(NpgsqlConnection connection, NpgsqlTransaction tx,
        RmaTenantMigrationRequestV1 request, IndexPlan plan, IndexTarget target, CancellationToken ct)
    {
        await using var command = new NpgsqlCommand($"""
            INSERT INTO {IndexSteps}(tenant_id,migration_id,installation_id,initial_operation_id,operation_id,table_oid,script_sha256,state)
            VALUES (@tenant,@migration,@installation,@initial,@operation,@table,@hash,'{Building}')
            """, connection, tx);
        IndexStepKey(command, request);
        command.Parameters.AddWithValue("installation", request.InstallationId);
        command.Parameters.AddWithValue("initial", target.InitialOperation);
        command.Parameters.AddWithValue("operation", request.OperationId);
        command.Parameters.AddWithValue("table", NpgsqlDbType.Oid, target.HeadsOid);
        command.Parameters.AddWithValue("hash", plan.Hash);
        await command.ExecuteNonQueryAsync(ct);
    }

    private static async Task ChangeIndexStepAsync(NpgsqlConnection connection, NpgsqlTransaction tx,
        RmaTenantMigrationRequestV1 request, string previous, string state, uint? indexOid, string? result, CancellationToken ct)
    {
        await using var command = new NpgsqlCommand($"""
            UPDATE {IndexSteps} SET state=@state,index_oid=@oid,result_json=@result
            WHERE tenant_id=@tenant AND migration_id=@migration AND operation_id=@operation AND state=@previous
            """, connection, tx);
        IndexStepKey(command, request);
        command.Parameters.AddWithValue("operation", request.OperationId);
        command.Parameters.AddWithValue("previous", previous);
        command.Parameters.AddWithValue("state", state);
        command.Parameters.AddWithValue("oid", NpgsqlDbType.Oid, (object?)indexOid ?? DBNull.Value);
        command.Parameters.AddWithValue("result", NpgsqlDbType.Text, (object?)result ?? DBNull.Value);
        if (await command.ExecuteNonQueryAsync(ct) != 1)
            throw new TenantProvisioningException("migration-history-mismatch");
    }

    private static void IndexStepKey(NpgsqlCommand command, RmaTenantMigrationRequestV1 request)
    {
        command.Parameters.AddWithValue("tenant", request.TenantId);
        command.Parameters.AddWithValue("migration", OutboundIndexMigration);
    }

    private static async Task<CatalogIndex?> FindIndexAsync(NpgsqlConnection connection, NpgsqlTransaction tx,
        IndexPlan plan, IndexTarget target, string name, CancellationToken ct)
    {
        await using var command = new NpgsqlCommand("""
            SELECT c.oid,i.indisvalid,i.indisready,i.indislive,
                   COALESCE(c.relkind='i' AND c.relpersistence='p' AND c.relowner=current_user::regrole
                       AND c.reloptions IS NULL AND t.oid=@table AND t.relnamespace=n.oid AND t.relname='heads'
                       AND t.relowner=c.relowner AND t.relkind='r'
                       AND a.attname='outbound_id' AND a.atttypid='pg_catalog.uuid'::regtype AND NOT a.attisdropped
                       AND am.amname='btree' AND o.opcname='uuid_ops' AND o.opcnamespace='pg_catalog'::regnamespace
                       AND o.opcmethod=c.relam AND o.opcdefault
                       AND i.indnatts=1 AND i.indnkeyatts=1 AND i.indkey[0]=@column
                       AND i.indcollation[0]=0::oid AND i.indoption[0]=0
                       AND NOT (i.indisunique OR i.indisprimary OR i.indisexclusion OR i.indisclustered OR i.indisreplident)
                       AND i.indpred IS NULL AND i.indexprs IS NULL
                       AND NOT EXISTS (SELECT FROM pg_catalog.pg_constraint p WHERE p.conindid=c.oid),false)
            FROM pg_catalog.pg_class c
            JOIN pg_catalog.pg_namespace n ON n.oid=c.relnamespace
            LEFT JOIN pg_catalog.pg_index i ON i.indexrelid=c.oid
            LEFT JOIN pg_catalog.pg_class t ON t.oid=i.indrelid
            LEFT JOIN pg_catalog.pg_attribute a ON a.attrelid=t.oid AND a.attnum=@column
            LEFT JOIN pg_catalog.pg_am am ON am.oid=c.relam
            LEFT JOIN pg_catalog.pg_opclass o ON o.oid=i.indclass[0]
            WHERE n.nspname=@schema AND c.relname=@name
            """, connection, tx);
        command.Parameters.AddWithValue("schema", plan.Schema);
        command.Parameters.AddWithValue("name", name);
        command.Parameters.AddWithValue("table", NpgsqlDbType.Oid, target.HeadsOid);
        command.Parameters.AddWithValue("column", target.OutboundColumn);
        await using var row = await command.ExecuteReaderAsync(ct);
        if (!await row.ReadAsync(ct)) return null;
        if (!row.GetBoolean(4)) throw new TenantProvisioningException("index-object-conflict");
        return new(row.GetFieldValue<uint>(0), row.GetBoolean(1), row.GetBoolean(2), row.GetBoolean(3));
    }

    private static async Task<string> CompleteIndexAsync(NpgsqlConnection connection,
        RmaTenantMigrationRequestV1 request, IndexPlan plan, IndexPreparation prepared, CancellationToken ct)
    {
        await using var tx = await connection.BeginTransactionAsync(ct);
        await Sql(connection, tx, $"LOCK TABLE {Q(plan.Schema)}.heads IN ACCESS SHARE MODE", ct);
        var staged = await FindIndexAsync(connection, tx, plan, prepared.Target, plan.Stage, ct);
        if (staged is not { Usable: true }
            || prepared.Stage is { Valid: true } previous && previous.Oid != staged.Oid
            || await FindIndexAsync(connection, tx, plan, prepared.Target, OutboundIndexName, ct) is not null)
            throw new TenantProvisioningException("index-object-conflict");

        // Rename locks the actual index selected by name. Bind that object before saving completion.
        await Sql(connection, tx, plan.Rename, ct);
        var renamed = await FindIndexAsync(connection, tx, plan, prepared.Target, OutboundIndexName, ct);
        if (renamed is not { Usable: true } || renamed.Oid != staged.Oid)
            throw new TenantProvisioningException("index-object-conflict");

        var result = JsonSerializer.Serialize(new
        {
            Outcome = "APPLIED",
            request.InstallationId,
            request.TenantId,
            request.OperationId,
            Profile = CoreProfile,
            Lineage,
            MigrationId = OutboundIndexMigration,
            ScriptSha256 = plan.Hash,
            plan.Schema,
            TableOid = prepared.Target.HeadsOid,
            IndexName = OutboundIndexName,
            IndexOid = renamed.Oid,
            request.Target.DatabaseOid,
            request.Target.SystemIdentifier,
            CompletedAt = DateTimeOffset.UtcNow
        });
        await ChangeIndexStepAsync(connection, tx, request, Building, Applied, renamed.Oid, result, ct);
        await Commit(tx, ct);
        return result;
    }

    private static async Task IndexSessionSql(NpgsqlConnection connection, string sql, CancellationToken ct)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(ct);
    }
}
