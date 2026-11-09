using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Npgsql;
using NpgsqlTypes;
using static CP6.Core.Persistence.Tenancy.TenantStorageProvisioner;

namespace CP6.Core.Persistence.Tenancy;

internal sealed record RmaTenantTargetV1(string Server, int Port, string Database, uint DatabaseOid, string SystemIdentifier)
{
    internal IPAddress Validate()
    {
        if (!IPAddress.TryParse(Server, out var address) || address.ToString() != Server
            || Port is < 1 or > 65535 || DatabaseOid == 0
            || string.IsNullOrWhiteSpace(Database) || Database.Any(char.IsControl) || Encoding.UTF8.GetByteCount(Database) > 63
            || !ulong.TryParse(SystemIdentifier, NumberStyles.None, CultureInfo.InvariantCulture, out var system)
            || system == 0 || system.ToString(CultureInfo.InvariantCulture) != SystemIdentifier)
            throw new TenantProvisioningException("input-rejected");
        return address;
    }
}

internal sealed record RmaTenantInstallRequestV1(RmaTenantTargetV1 Target, Guid InstallationId, TenantLoginV1[] Tenants);
internal sealed record RmaTenantMigrationRequestV1(RmaTenantTargetV1 Target, Guid InstallationId, Guid TenantId, Guid OperationId);

/// <summary>Explicit fresh RMA storage control. This profile never adopts an existing RMA authority.</summary>
internal static partial class RmaTenantStorageV1
{
    internal const string Profile = "rma-application-tenant-v1";
    internal const string UnknownOutcome = "outcome-unknown-retry-same-operation";
    private const string Lineage = "rma-application-v1";
    private const string MigrationId = "rma-tenant-storage-0001";
    private const string Control = "cp6_rma_tenant_v1";

    internal static async Task<string> InstallAsync(NpgsqlConnection connection, RmaTenantInstallRequestV1 request,
        CancellationToken ct)
    {
        if (request.InstallationId == Guid.Empty || request.Tenants is not { Length: >= 1 and <= 128 })
            throw new TenantProvisioningException("input-rejected");

        await connection.OpenAsync(ct);
        await using var tx = await connection.BeginTransactionAsync(ct);
        await RequireControlTarget(connection, tx, request.Target, ct);
        TenantLoginDirectoryV1.ValidateArguments(connection, tx, request.Tenants);
        var bindings = JsonSerializer.Serialize(request.Tenants.OrderBy(t => t.TenantId).ToArray());
        await Sql(connection, tx, $"SELECT pg_catalog.pg_advisory_xact_lock({TenantLoginDirectoryV1.DirectoryLockNamespace},4)", ct);
        await RejectOtherAuthority(connection, tx, ct);

        if (await Scalar(connection, tx, $"SELECT to_regnamespace('{Control}') IS NOT NULL", ct) is true)
        {
            await using var previous = new NpgsqlCommand($"""
                SELECT result_json FROM {Control}.installation
                WHERE singleton AND installation_id=@installation AND profile=@profile
                  AND target=@target AND bindings_json=@bindings
                """, connection, tx);
            previous.Parameters.AddWithValue("installation", request.InstallationId);
            previous.Parameters.AddWithValue("profile", Profile);
            previous.Parameters.AddWithValue("target", NpgsqlDbType.Jsonb, JsonSerializer.Serialize(request.Target));
            previous.Parameters.AddWithValue("bindings", bindings);
            if (await previous.ExecuteScalarAsync(ct) is not string receipt)
                throw new TenantProvisioningException("installation-conflict");
            return receipt;
        }

        if (await Scalar(connection, tx, TenantEmptyLayoutV1.Sql
                + " AND NOT EXISTS (SELECT FROM pg_catalog.pg_default_acl)", ct) is not true)
            throw new TenantProvisioningException("fresh-layout-required");

        await InstallDirectoryAsync(connection, tx, request.Target.Database, request.Tenants, fullCore: false, ct);

        var result = JsonSerializer.Serialize(new
        {
            Outcome = "INSTALLED",
            request.InstallationId,
            Profile,
            Lineage,
            request.Target.DatabaseOid,
            request.Target.SystemIdentifier,
            TenantCount = request.Tenants.Length
        });
        await InstallControlRowsAsync(connection, tx, request, Profile, bindings, result, ct);
        await Commit(tx, ct);
        return result;
    }

    internal static async Task<string> MigrateAsync(NpgsqlConnection connection, RmaTenantMigrationRequestV1 request,
        CancellationToken ct)
    {
        if (request.InstallationId == Guid.Empty || request.TenantId == Guid.Empty || request.OperationId == Guid.Empty)
            throw new TenantProvisioningException("input-rejected");

        await connection.OpenAsync(ct);
        await using var tx = await connection.BeginTransactionAsync(ct);
        await RequireControlTarget(connection, tx, request.Target, ct);
        await RejectOtherAuthority(connection, tx, ct);
        if (await Scalar(connection, tx, $"SELECT to_regnamespace('{Control}') IS NOT NULL", ct) is not true)
            throw new TenantProvisioningException("installation-required");

        // Pin the directory without serializing different tenants. Only the selected migration row is locked exclusively.
        await Sql(connection, tx, $"SELECT pg_catalog.pg_advisory_xact_lock_shared({TenantLoginDirectoryV1.DirectoryLockNamespace},1)", ct);
        string login;
        Guid? previous;
        string? storedMigration = null;
        string? storedHash = null;
        string? storedResult = null;
        await using (var gate = new NpgsqlCommand($"""
            SELECT r.rolname::text,m.operation_id,m.migration_id,m.script_sha256,m.result_json
            FROM {Control}.tenant_migrations m
            JOIN cp6_tenancy_v1.login_tenant d ON d.tenant_id=m.tenant_id AND d.role_oid=m.role_oid AND d.role_name=m.role_name
            JOIN pg_catalog.pg_roles r ON r.oid=d.role_oid AND r.rolname=d.role_name
            WHERE m.tenant_id=@tenant AND m.lineage=@lineage AND {PlainLogin}
              AND EXISTS (SELECT FROM {Control}.installation
                          WHERE singleton AND installation_id=@installation AND profile=@profile AND target=@target)
            FOR UPDATE OF m
            """, connection, tx))
        {
            gate.Parameters.AddWithValue("tenant", request.TenantId);
            gate.Parameters.AddWithValue("lineage", Lineage);
            gate.Parameters.AddWithValue("installation", request.InstallationId);
            gate.Parameters.AddWithValue("profile", Profile);
            gate.Parameters.AddWithValue("target", NpgsqlDbType.Jsonb, JsonSerializer.Serialize(request.Target));
            await using var row = await gate.ExecuteReaderAsync(ct);
            if (!await row.ReadAsync(ct))
                throw new TenantProvisioningException("binding-rejected");
            login = row.GetString(0);
            previous = row.IsDBNull(1) ? null : row.GetGuid(1);
            if (previous.HasValue)
            {
                storedMigration = row.GetString(2);
                storedHash = row.GetString(3);
                storedResult = row.GetString(4);
            }
        }

        var schema = "t_" + request.TenantId.ToString("N");
        var ddl = TenantDdl(request.TenantId, Q(schema), Q(login));
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(ddl))).ToLowerInvariant();
        if (previous.HasValue)
        {
            if (previous != request.OperationId)
                throw new TenantProvisioningException("operation-conflict");
            if (storedMigration != MigrationId || storedHash != hash)
                throw new TenantProvisioningException("migration-history-mismatch");

            await RequireTenantHistoryAsync(connection, tx, request, schema, hash, storedResult!, ct);
            return storedResult!;
        }

        var result = await ApplyTenantAsync(connection, tx, request, schema, ddl, hash, Profile, ct);
        await Commit(tx, ct);
        return result;
    }

    private static async Task InstallDirectoryAsync(NpgsqlConnection connection, NpgsqlTransaction tx,
        string databaseName, TenantLoginV1[] tenants, bool fullCore, CancellationToken ct)
    {
        await ServiceTokenProfileV1.ValidateFreshPublicAsync(connection, tx, ct);
        foreach (var binding in tenants)
        {
            await using var login = new NpgsqlCommand($"""
                SELECT {PlainLogin}
                  AND NOT EXISTS (SELECT FROM pg_catalog.pg_database WHERE datdba=r.oid)
                  AND NOT EXISTS (SELECT FROM pg_catalog.pg_namespace WHERE nspowner=r.oid)
                  AND NOT EXISTS (SELECT FROM pg_catalog.pg_class WHERE relowner=r.oid)
                  AND NOT EXISTS (SELECT FROM pg_catalog.pg_proc WHERE proowner=r.oid)
                FROM pg_catalog.pg_roles r WHERE r.rolname=@role
                """, connection, tx);
            login.Parameters.AddWithValue("role", binding.Login);
            if (await login.ExecuteScalarAsync(ct) is not true)
                throw new TenantProvisioningException("runtime-login-rejected");
            // regrole resolves a PostgreSQL identifier, so preserve quoted login names.
            await ServiceTokenProfileV1.ValidateFreshLoginAsync(connection, tx, Q(binding.Login), ct);
        }

        var roles = string.Join(", ", tenants.Select(t => Q(t.Login)));
        var database = Q(databaseName);
        await Sql(connection, tx, $"""
            REVOKE ALL ON DATABASE {database} FROM PUBLIC;
            GRANT CONNECT ON DATABASE {database} TO {roles};
            REVOKE ALL ON SCHEMA public FROM PUBLIC;
            """, ct);
        await TenantLoginDirectoryV1.InstallAsync(connection, tx, tenants, ct);
        await Sql(connection, tx, $"""
            GRANT USAGE ON SCHEMA cp6_tenancy_v1 TO {roles};
            GRANT EXECUTE ON FUNCTION cp6_tenancy_v1.current_tenant_id() TO {roles};
            """, ct);
        if (fullCore)
            await RequireCoreRoleIsolationAsync(connection, tx, tenants, ct);
        else
            await TenantLoginDirectoryV1.ValidateAsync(connection, tx, tenants, ct);
    }

    private static async Task InstallControlRowsAsync(NpgsqlConnection connection, NpgsqlTransaction tx,
        RmaTenantInstallRequestV1 request, string profile, string bindings, string result, CancellationToken ct)
    {
        await Sql(connection, tx, $"""
            CREATE SCHEMA {Control};
            REVOKE ALL ON SCHEMA {Control} FROM PUBLIC;
            CREATE TABLE {Control}.installation (
                singleton boolean PRIMARY KEY CHECK (singleton),
                installation_id uuid NOT NULL, profile text NOT NULL,
                target jsonb NOT NULL, bindings_json text NOT NULL, result_json text NOT NULL
            );
            CREATE TABLE {Control}.tenant_migrations (
                tenant_id uuid NOT NULL, lineage text NOT NULL,
                role_oid oid NOT NULL, role_name name NOT NULL,
                state text NOT NULL DEFAULT 'Provisioning',
                operation_id uuid, migration_id text, script_sha256 text, result_json text,
                PRIMARY KEY (tenant_id, lineage),
                CHECK ((state='Provisioning' AND operation_id IS NULL AND migration_id IS NULL
                            AND script_sha256 IS NULL AND result_json IS NULL)
                    OR (state='Ready' AND operation_id IS NOT NULL AND migration_id IS NOT NULL
                            AND script_sha256 IS NOT NULL AND result_json IS NOT NULL))
            );
            REVOKE ALL ON {Control}.installation, {Control}.tenant_migrations FROM PUBLIC;
            """, ct);
        await using var save = new NpgsqlCommand($"""
            INSERT INTO {Control}.installation VALUES (true,@installation,@profile,@target,@bindings,@result);
            INSERT INTO {Control}.tenant_migrations (tenant_id,lineage,role_oid,role_name)
            SELECT tenant_id,@lineage,role_oid,role_name FROM cp6_tenancy_v1.login_tenant;
            """, connection, tx);
        save.Parameters.AddWithValue("installation", request.InstallationId);
        save.Parameters.AddWithValue("profile", profile);
        save.Parameters.AddWithValue("target", NpgsqlDbType.Jsonb, JsonSerializer.Serialize(request.Target));
        save.Parameters.AddWithValue("bindings", bindings);
        save.Parameters.AddWithValue("result", result);
        save.Parameters.AddWithValue("lineage", Lineage);
        await save.ExecuteNonQueryAsync(ct);
    }

    private static async Task<string> ApplyTenantAsync(NpgsqlConnection connection, NpgsqlTransaction tx,
        RmaTenantMigrationRequestV1 request, string schema, string ddl, string hash, string profile, CancellationToken ct)
    {
        var result = JsonSerializer.Serialize(new
        {
            Outcome = "READY",
            request.InstallationId,
            request.OperationId,
            request.TenantId,
            Schema = schema,
            Profile = profile,
            Lineage,
            MigrationId,
            ScriptSha256 = hash,
            request.Target.DatabaseOid,
            request.Target.SystemIdentifier,
            CompletedAt = DateTimeOffset.UtcNow
        });

        await Sql(connection, tx, ddl, ct);
        await using var local = new NpgsqlCommand($"""
            INSERT INTO {Q(schema)}.rma_migration_history_v1 (tenant_id,lineage,migration_id,operation_id,script_sha256,result_json)
            VALUES (@tenant,@lineage,@migration,@operation,@hash,@result)
            """, connection, tx);
        MigrationParameters(local, request, hash, result);
        await local.ExecuteNonQueryAsync(ct);

        await using var ready = new NpgsqlCommand($"""
            UPDATE {Control}.tenant_migrations
            SET state='Ready', operation_id=@operation, migration_id=@migration, script_sha256=@hash, result_json=@result
            WHERE tenant_id=@tenant AND lineage=@lineage AND state='Provisioning'
            """, connection, tx);
        MigrationParameters(ready, request, hash, result);
        if (await ready.ExecuteNonQueryAsync(ct) != 1)
            throw new TenantProvisioningException("binding-rejected");
        return result;
    }

    private static async Task RequireTenantHistoryAsync(NpgsqlConnection connection, NpgsqlTransaction tx,
        RmaTenantMigrationRequestV1 request, string schema, string hash, string result, CancellationToken ct)
    {
        await using var history = new NpgsqlCommand($"""
            SELECT EXISTS (SELECT FROM {Q(schema)}.rma_migration_history_v1
                           WHERE tenant_id=@tenant AND lineage=@lineage AND migration_id=@migration
                             AND operation_id=@operation AND script_sha256=@hash AND result_json=@result)
              AND (SELECT count(*) FROM pg_catalog.pg_class c JOIN pg_catalog.pg_namespace n ON n.oid=c.relnamespace
                   WHERE n.nspname=@schema AND n.nspowner=current_user::regrole AND c.relowner=current_user::regrole
                     AND c.relkind='r' AND c.relname IN ('heads','versions','rma_migration_history_v1'))=3
            """, connection, tx);
        MigrationParameters(history, request, hash, result);
        history.Parameters.AddWithValue("schema", schema);
        if (await history.ExecuteScalarAsync(ct) is not true)
            throw new TenantProvisioningException("migration-history-mismatch");
    }

    private static async Task RequireControlTarget(NpgsqlConnection connection, NpgsqlTransaction tx,
        RmaTenantTargetV1 target, CancellationToken ct)
    {
        await Sql(connection, tx, "SET LOCAL search_path=pg_catalog; SET LOCAL lock_timeout='15s'; SET LOCAL statement_timeout='60s'", ct);
        await using var command = new NpgsqlCommand("""
            SELECT CASE WHEN r.rolsuper AND session_user=current_user AND d.datdba=r.oid THEN
                d.datname=@database AND d.oid=@oid AND host(inet_server_addr())=@server AND inet_server_port()=@port
                AND NOT pg_is_in_recovery() AND (SELECT system_identifier::text FROM pg_control_system())=@system
                ELSE false END
            FROM pg_catalog.pg_roles r, pg_catalog.pg_database d
            WHERE r.rolname=current_user AND d.datname=current_database()
            """, connection, tx);
        command.Parameters.AddWithValue("database", target.Database);
        command.Parameters.AddWithValue("oid", NpgsqlDbType.Oid, target.DatabaseOid);
        command.Parameters.AddWithValue("server", target.Server);
        command.Parameters.AddWithValue("port", target.Port);
        command.Parameters.AddWithValue("system", target.SystemIdentifier);
        if (await command.ExecuteScalarAsync(ct) is not true)
            throw new TenantProvisioningException("target-identity-rejected");
    }

    private static async Task RejectOtherAuthority(NpgsqlConnection connection, NpgsqlTransaction tx, CancellationToken ct)
    {
        if (await Scalar(connection, tx, """
                SELECT to_regnamespace('cp6_rma_application_v1') IS NOT NULL
                    OR to_regnamespace('cp6_provisioning_v1') IS NOT NULL
                """, ct) is true)
            throw new TenantProvisioningException("fresh-layout-required");
    }

    private static void MigrationParameters(NpgsqlCommand command, RmaTenantMigrationRequestV1 request,
        string hash, string result)
    {
        command.Parameters.AddWithValue("tenant", request.TenantId);
        command.Parameters.AddWithValue("lineage", Lineage);
        command.Parameters.AddWithValue("migration", MigrationId);
        command.Parameters.AddWithValue("operation", request.OperationId);
        command.Parameters.AddWithValue("hash", hash);
        command.Parameters.AddWithValue("result", result);
    }

    private static async Task Sql(NpgsqlConnection connection, NpgsqlTransaction tx, string sql, CancellationToken ct)
    {
        await using var command = new NpgsqlCommand(sql, connection, tx);
        await command.ExecuteNonQueryAsync(ct);
    }

    private static async Task<object?> Scalar(NpgsqlConnection connection, NpgsqlTransaction tx, string sql, CancellationToken ct)
    {
        await using var command = new NpgsqlCommand(sql, connection, tx);
        return await command.ExecuteScalarAsync(ct);
    }

    private static async Task Commit(NpgsqlTransaction transaction, CancellationToken ct)
    {
        try
        {
            await transaction.CommitAsync(ct);
        }
        catch (Exception error) when (error is NpgsqlException or OperationCanceledException or InvalidOperationException)
        {
            throw new TenantProvisioningException(UnknownOutcome);
        }
    }

    private const string PlainLogin = """
        r.rolcanlogin AND NOT (r.rolsuper OR r.rolbypassrls OR r.rolcreatedb OR r.rolcreaterole OR r.rolreplication)
        AND NOT EXISTS (SELECT FROM pg_catalog.pg_auth_members WHERE member=r.oid OR roleid=r.oid)
        AND NOT EXISTS (SELECT FROM pg_catalog.pg_db_role_setting WHERE setrole=r.oid)
        """;

    // Forward-only DDL for the new profile. The historical shared Core migration remains frozen.
    private static string TenantDdl(Guid tenant, string schema, string login, bool writable = true)
    {
        var objects = $"""
            CREATE SCHEMA {schema};
            REVOKE ALL ON SCHEMA {schema} FROM PUBLIC;
            CREATE TABLE {schema}.heads (
                tenant_id uuid NOT NULL CHECK (tenant_id='{tenant:D}'::uuid),
                id uuid NOT NULL, actor_id uuid NOT NULL, outbound_id uuid NOT NULL,
                version integer NOT NULL CHECK (version>0), PRIMARY KEY (tenant_id,id)
            );
            CREATE TABLE {schema}.versions (
                tenant_id uuid NOT NULL CHECK (tenant_id='{tenant:D}'::uuid),
                id uuid NOT NULL, version integer NOT NULL CHECK (version>0),
                state text NOT NULL CHECK (state IN ('DRAFT','SUBMITTED')), payload jsonb NOT NULL,
                PRIMARY KEY (tenant_id,id,version),
                FOREIGN KEY (tenant_id,id) REFERENCES {schema}.heads(tenant_id,id)
            );
            ALTER TABLE {schema}.heads ADD CONSTRAINT rma_application_head_version
                FOREIGN KEY (tenant_id,id,version) REFERENCES {schema}.versions(tenant_id,id,version)
                DEFERRABLE INITIALLY DEFERRED;
            CREATE FUNCTION {schema}.rma_application_immutable_v1() RETURNS trigger
                LANGUAGE plpgsql SET search_path=pg_catalog AS $immutable$
                BEGIN RAISE EXCEPTION 'RMA_APPLICATION_VERSION_IMMUTABLE' USING ERRCODE='23514'; END $immutable$;
            CREATE TRIGGER rma_application_immutable BEFORE UPDATE OR DELETE ON {schema}.versions
                FOR EACH ROW EXECUTE FUNCTION {schema}.rma_application_immutable_v1();
            CREATE TRIGGER rma_application_no_truncate BEFORE TRUNCATE ON {schema}.versions
                FOR EACH STATEMENT EXECUTE FUNCTION {schema}.rma_application_immutable_v1();
            CREATE TABLE {schema}.rma_migration_history_v1 (
                tenant_id uuid NOT NULL CHECK (tenant_id='{tenant:D}'::uuid),
                lineage text NOT NULL, migration_id text NOT NULL,
                operation_id uuid NOT NULL, script_sha256 text NOT NULL, result_json text NOT NULL,
                PRIMARY KEY (lineage,migration_id)
            );
            REVOKE ALL ON {schema}.heads, {schema}.versions, {schema}.rma_migration_history_v1 FROM PUBLIC;
            REVOKE ALL ON FUNCTION {schema}.rma_application_immutable_v1() FROM PUBLIC;
            GRANT USAGE ON SCHEMA {schema} TO {login};
            """;
        var grants = writable ? $"""
            GRANT SELECT, INSERT ON {schema}.heads, {schema}.versions TO {login};
            GRANT UPDATE (version) ON {schema}.heads TO {login};
            """ : $"GRANT SELECT ON {schema}.heads, {schema}.versions TO {login};";
        return (objects + "\n" + grants).ReplaceLineEndings("\n");
    }
}
