using System.Text.Json;
using Npgsql;

namespace CP6.Core.Persistence.Tenancy;

/// <summary>Explicit provisioning of one supported messaging profile; never called by application startup.</summary>
internal sealed class TenantStorageProvisioner
{
    // A distinct database-local lock serializes first install before the directory exists.
    private const int ProvisioningLock = 1129330261;
    private readonly Func<TenantProvisioningCheckpoint, Task>? _checkpoint;
    internal TenantStorageProvisioner(Func<TenantProvisioningCheckpoint, Task>? checkpoint = null) => _checkpoint = checkpoint;

    internal async Task<TenantProvisioningResult> ProvisionAsync(TenantProvisioningRequest request,
        NpgsqlConnection connection, CancellationToken ct = default)
    {
        request.Validate();
        try
        {
            var settings = new NpgsqlConnectionStringBuilder(connection.ConnectionString);
            if (settings.Host != request.Host || settings.Port != request.Port || settings.Database != request.Database
                || settings.Username == request.RuntimeLogin || settings.SearchPath != "pg_catalog"
                || settings.Enlist || settings.IncludeErrorDetail || !string.IsNullOrWhiteSpace(settings.Options))
                throw new TenantProvisioningException("connection-rejected");
            if (connection.State != System.Data.ConnectionState.Open) await connection.OpenAsync(ct);
            var scripts = new TenantProvisioningMigrations(connection);
            var canonical = request.Canonical(scripts.Hash);
            await using var tx = await connection.BeginTransactionAsync(ct);
            async Task<int> Execute(string sql) => await Sql(connection, tx, sql, ct);
            await Execute("SET LOCAL search_path = pg_catalog; SET LOCAL lock_timeout = '15s'; SET LOCAL statement_timeout = '60s'");
            await Execute($"SELECT pg_catalog.pg_advisory_xact_lock({ProvisioningLock},1)");
            await using (var identity = new NpgsqlCommand("""
                SELECT current_database()=@db AND (SELECT oid FROM pg_catalog.pg_database WHERE datname=current_database())=@oid
                  AND (SELECT system_identifier::text FROM pg_catalog.pg_control_system())=@system
                  AND current_user=session_user AND (SELECT rolsuper FROM pg_catalog.pg_roles WHERE rolname=current_user)
                  AND (SELECT datdba=current_user::regrole FROM pg_catalog.pg_database WHERE datname=current_database())
                """, connection, tx))
            {
                identity.Parameters.AddWithValue("db",request.Database); identity.Parameters.AddWithValue("oid",NpgsqlTypes.NpgsqlDbType.Oid,request.DatabaseOid);
                identity.Parameters.AddWithValue("system",request.SystemIdentifier);
                if (await identity.ExecuteScalarAsync(ct) is not true) throw new TenantProvisioningException("target-identity-rejected");
            }
            var exists = await Scalar(connection,tx,"SELECT EXISTS(SELECT FROM pg_catalog.pg_namespace WHERE nspname='cp6_provisioning_v1')",ct) is true;
            var results = new List<TenantProvisioningResult>();
            if (exists)
            {
                await ValidateLedgerRoot(connection,tx,ct);
                await Execute($"SELECT pg_catalog.pg_advisory_xact_lock({TenantLoginDirectoryV1.DirectoryLockNamespace},1); LOCK TABLE cp6_tenancy_v1.login_tenant IN ACCESS EXCLUSIVE MODE");
                string seal;
                await using (var state = new NpgsqlCommand("SELECT fingerprint FROM cp6_provisioning_v1.installation WHERE singleton",connection,tx))
                    seal = await state.ExecuteScalarAsync(ct) as string ?? throw new TenantProvisioningException("incomplete-layout");
                if (seal != await TenantProvisioningCatalog.HashAsync(connection,tx,ct))
                    throw new TenantProvisioningException("layout-drift");
                await using (var command = new NpgsqlCommand("SELECT canonical,result FROM cp6_provisioning_v1.operations ORDER BY operation_id",connection,tx))
                await using (var rows = await command.ExecuteReaderAsync(ct))
                    while (await rows.ReadAsync(ct))
                    {
                        var savedResult = JsonSerializer.Deserialize<TenantProvisioningResult>(rows.GetString(1))
                            ?? throw new TenantProvisioningException("incomplete-layout");
                        if (savedResult.Database != request.Database || savedResult.DatabaseOid != request.DatabaseOid
                            || savedResult.SystemIdentifier != request.SystemIdentifier || savedResult.Profile != request.Profile
                            || savedResult.Migration != TenantMessagingProfile.CombinedMigrationVersion || savedResult.Scripts != scripts.Hash)
                            throw new TenantProvisioningException("version-rejected");
                        if (savedResult.OperationId == request.OperationId && rows.GetString(0) != canonical)
                            throw new TenantProvisioningException("operation-conflict");
                        results.Add(savedResult);
                    }
                if (results.Count == 0) throw new TenantProvisioningException("incomplete-layout");
                await TenantLoginDirectoryV1.ValidateAsync(connection,tx,results.Select(r => new TenantLoginV1(r.TenantId,r.RuntimeLogin)).ToArray(),ct);
                await ValidateHistory(connection,tx,results,ct);
                var previous = results.SingleOrDefault(r => r.OperationId == request.OperationId);
                if (previous is not null)
                {
                    await tx.CommitAsync(ct);
                    return previous;
                }
                if (results.Any(r => r.TenantId == request.TenantId || r.RuntimeLogin == request.RuntimeLogin))
                    throw new TenantProvisioningException("mapping-conflict");
            }
            else
            {
                if (await Scalar(connection,tx,TenantEmptyLayoutV1.Sql,ct) is not true) throw new TenantProvisioningException("unknown-layout");
            }

            var roleOid = await ValidateNewLogin(connection,tx,request,ct);
            var binding = new TenantLoginV1(request.TenantId,request.RuntimeLogin);
            if (!exists)
            {
                await Execute("SET LOCAL search_path = public");
                await Execute(scripts.Erp);
                await ErpTenantRowSecurityV1.InstallAsync(connection,tx,[binding],ct);
                await Execute(scripts.Identity);
                await IdentityTenantRowSecurityV1.InstallAsync(connection,tx,[binding],true,ct);
                await Execute("SET LOCAL search_path = pg_catalog");
                await Execute(LedgerSql);
            }
            else
            {
                await using (var insert = new NpgsqlCommand("INSERT INTO cp6_tenancy_v1.login_tenant VALUES (@oid,@role,@tenant)",connection,tx))
                {
                    insert.Parameters.AddWithValue("oid",NpgsqlTypes.NpgsqlDbType.Oid,roleOid); insert.Parameters.AddWithValue("role",request.RuntimeLogin);
                    insert.Parameters.AddWithValue("tenant",request.TenantId); await insert.ExecuteNonQueryAsync(ct);
                }
                await AppendRights(connection,tx,request.RuntimeLogin,results.Select(r => r.RuntimeLogin).Append(request.RuntimeLogin).ToArray(),ct);
            }
            var result = new TenantProvisioningResult(request.OperationId,request.TenantId,request.RuntimeLogin,roleOid,
                request.Schema,request.Database,request.DatabaseOid,request.SystemIdentifier,request.Profile,
                TenantMessagingProfile.CombinedMigrationVersion,scripts.Hash,DateTimeOffset.UtcNow);
            var json = JsonSerializer.Serialize(result);
            await Execute($"CREATE SCHEMA {Q(request.Schema)}; REVOKE ALL ON SCHEMA {Q(request.Schema)} FROM PUBLIC; GRANT USAGE ON SCHEMA {Q(request.Schema)} TO {Q(request.RuntimeLogin)}; "
                + $"CREATE TABLE {Q(request.Schema)}.initialization_history_v1 (operation_id uuid PRIMARY KEY, result text NOT NULL); REVOKE ALL ON {Q(request.Schema)}.initialization_history_v1 FROM PUBLIC");
            await using (var local = new NpgsqlCommand($"INSERT INTO {Q(request.Schema)}.initialization_history_v1 VALUES (@operation,@result)",connection,tx))
            {
                local.Parameters.AddWithValue("operation",request.OperationId); local.Parameters.AddWithValue("result",json);
                await local.ExecuteNonQueryAsync(ct);
            }
            await using (var save = new NpgsqlCommand("INSERT INTO cp6_provisioning_v1.operations VALUES (@operation,@canonical,@result)",connection,tx))
            {
                save.Parameters.AddWithValue("operation",request.OperationId); save.Parameters.AddWithValue("canonical",canonical);
                save.Parameters.AddWithValue("result",json); await save.ExecuteNonQueryAsync(ct);
            }
            results.Add(result);
            await TenantLoginDirectoryV1.ValidateAsync(connection,tx,results.Select(r => new TenantLoginV1(r.TenantId,r.RuntimeLogin)).ToArray(),ct);
            await ValidateHistory(connection,tx,results,ct);
            await using (var save = new NpgsqlCommand("INSERT INTO cp6_provisioning_v1.installation VALUES (true,@hash) ON CONFLICT (singleton) DO UPDATE SET fingerprint=excluded.fingerprint",connection,tx))
            {
                save.Parameters.AddWithValue("hash",await TenantProvisioningCatalog.HashAsync(connection,tx,ct));
                await save.ExecuteNonQueryAsync(ct);
            }
            if (_checkpoint is not null) await _checkpoint(TenantProvisioningCheckpoint.BeforeCommit);
            await tx.CommitAsync(ct);
            if (_checkpoint is not null) await _checkpoint(TenantProvisioningCheckpoint.AfterCommit);
            return result;
        }
        catch (TenantProvisioningException) { throw; }
        catch (PostgresException error) { throw new TenantProvisioningException("database-" + error.SqlState + "-retry-same-operation"); }
        catch (OperationCanceledException) { throw new TenantProvisioningException("interrupted-retry-same-operation"); }
        catch { throw new TenantProvisioningException("execution-uncertain-retry-same-operation"); }
    }

    private static async Task<uint> ValidateNewLogin(NpgsqlConnection c,NpgsqlTransaction tx,TenantProvisioningRequest request,CancellationToken ct)
    {
        await using var command = new NpgsqlCommand("""
            SELECT r.oid FROM pg_catalog.pg_roles r WHERE r.rolname=@role AND r.rolcanlogin
              AND NOT (r.rolsuper OR r.rolbypassrls OR r.rolcreatedb OR r.rolcreaterole OR r.rolreplication)
              AND NOT EXISTS(SELECT FROM pg_catalog.pg_auth_members WHERE member=r.oid OR roleid=r.oid)
              AND NOT EXISTS(SELECT FROM pg_catalog.pg_database WHERE datdba=r.oid)
              AND NOT EXISTS(SELECT FROM pg_catalog.pg_namespace WHERE nspowner=r.oid)
              AND NOT EXISTS(SELECT FROM pg_catalog.pg_class WHERE relowner=r.oid)
              AND NOT EXISTS(SELECT FROM pg_catalog.pg_proc WHERE proowner=r.oid)
              AND NOT EXISTS(SELECT FROM pg_catalog.pg_db_role_setting WHERE setrole=r.oid)
              AND NOT EXISTS(SELECT FROM pg_catalog.pg_namespace n,LATERAL pg_catalog.aclexplode(n.nspacl) a WHERE a.grantee=r.oid)
              AND NOT EXISTS(SELECT FROM pg_catalog.pg_class c,LATERAL pg_catalog.aclexplode(c.relacl) a WHERE a.grantee=r.oid)
              AND NOT EXISTS(SELECT FROM pg_catalog.pg_attribute c,LATERAL pg_catalog.aclexplode(c.attacl) a WHERE a.grantee=r.oid)
              AND NOT EXISTS(SELECT FROM pg_catalog.pg_proc p,LATERAL pg_catalog.aclexplode(p.proacl) a WHERE a.grantee=r.oid)
              AND NOT EXISTS(SELECT FROM pg_catalog.pg_database d,LATERAL pg_catalog.aclexplode(d.datacl) a
                WHERE d.datname=current_database() AND a.grantee=r.oid AND (a.privilege_type<>'CONNECT' OR a.is_grantable))
            """,c,tx);
        command.Parameters.AddWithValue("role",request.RuntimeLogin);
        return await command.ExecuteScalarAsync(ct) is uint oid ? oid : throw new TenantProvisioningException("runtime-login-rejected");
    }

    private static async Task AppendRights(NpgsqlConnection c,NpgsqlTransaction tx,string login,string[] all,CancellationToken ct)
    {
        var roles = string.Join(',',all.Select(Q)); var role = Q(login);
        foreach (var (schema,tables,storage) in new[] { ("erp_integration",ErpTenantRowSecurityV1.Tables,"cp6_storage_erp"),
            ("crm_identity_priority",IdentityTenantRowSecurityV1.Tables,"cp6_storage_identity") })
        {
            foreach (var table in tables)
            {
                var target=Q(schema)+"."+Q(table);
                await Sql(c,tx,$"LOCK TABLE {target} IN ACCESS EXCLUSIVE MODE; "
                    + string.Join(' ',new[] {"select","insert","update","delete"}.Select(p=>$"ALTER POLICY tenant_{p}_v1 ON {target} TO {roles};"))
                    + $" GRANT SELECT,INSERT{(table is "DeliveryReplayAudit" or "InboxReplayAudit" ? "" : ",UPDATE")} ON {target} TO {role}",ct);
            }
            await Sql(c,tx,$"GRANT USAGE ON SCHEMA {Q(schema)},{Q(storage)} TO {role}; GRANT USAGE ON SEQUENCE {Q(storage)}.rowversion_v1 TO {role}; GRANT EXECUTE ON FUNCTION {Q(storage)}.set_rowversion_v1() TO {role}",ct);
        }
        await Sql(c,tx,$"GRANT CONNECT ON DATABASE {Q(c.Database)} TO {role}; GRANT USAGE ON SCHEMA public,cp6_tenancy_v1 TO {role}; GRANT EXECUTE ON FUNCTION cp6_tenancy_v1.current_tenant_id(),public.cp6_cp936_length_v1(text) TO {role}",ct);
    }

    private static async Task ValidateHistory(NpgsqlConnection c,NpgsqlTransaction tx,List<TenantProvisioningResult> results,CancellationToken ct)
    {
        foreach (var (table,version) in new[] { ("__EFMigrationsHistory_ErpIntegration",TenantProvisioningMigrations.ErpVersion),
            ("__EFMigrationsHistory_IdentityPriority",TenantProvisioningMigrations.IdentityVersion) })
        {
            await using var command = new NpgsqlCommand($"SELECT count(*)=1 AND bool_and(\"MigrationId\"=@version AND \"ProductVersion\"='8.0.30') FROM public.{Q(table)}",c,tx);
            command.Parameters.AddWithValue("version",version);
            if (await command.ExecuteScalarAsync(ct) is not true) throw new TenantProvisioningException("history-rejected");
        }
        foreach (var result in results)
        {
            await using var command = new NpgsqlCommand($"SELECT count(*)=1 AND bool_and(operation_id=@operation AND result=@result) FROM {Q(result.Schema)}.initialization_history_v1",c,tx);
            command.Parameters.AddWithValue("operation",result.OperationId); command.Parameters.AddWithValue("result",JsonSerializer.Serialize(result));
            if (await command.ExecuteScalarAsync(ct) is not true) throw new TenantProvisioningException("history-rejected");
        }
    }

    private static async Task ValidateLedgerRoot(NpgsqlConnection c,NpgsqlTransaction tx,CancellationToken ct)
    {
        if (await Scalar(c,tx,"""
            SELECT (SELECT count(*) FROM pg_catalog.pg_namespace WHERE nspname='cp6_provisioning_v1'
              AND nspowner=current_user::regrole AND NOT EXISTS(SELECT FROM pg_catalog.aclexplode(nspacl) a WHERE a.grantee<>nspowner))=1
              AND (SELECT count(*) FROM pg_catalog.pg_class c JOIN pg_catalog.pg_namespace n ON n.oid=c.relnamespace
                WHERE n.nspname='cp6_provisioning_v1' AND c.relname IN ('installation','operations') AND c.relkind='r'
                  AND c.relowner=current_user::regrole AND NOT c.relrowsecurity AND NOT c.relforcerowsecurity
                  AND NOT EXISTS(SELECT FROM pg_catalog.aclexplode(c.relacl) a WHERE a.grantee<>c.relowner)
                  AND NOT EXISTS(SELECT FROM pg_catalog.pg_attribute t,LATERAL pg_catalog.aclexplode(t.attacl) a WHERE t.attrelid=c.oid AND a.grantee<>c.relowner)
                  AND NOT EXISTS(SELECT FROM pg_catalog.pg_trigger WHERE tgrelid=c.oid AND NOT tgisinternal)
                  AND NOT EXISTS(SELECT FROM pg_catalog.pg_rewrite WHERE ev_class=c.oid))=2
            """,ct) is not true) throw new TenantProvisioningException("ledger-rejected");
    }

    private const string LedgerSql = """
        CREATE SCHEMA cp6_provisioning_v1;
        REVOKE ALL ON SCHEMA cp6_provisioning_v1 FROM PUBLIC;
        CREATE TABLE cp6_provisioning_v1.installation(singleton boolean PRIMARY KEY CHECK(singleton),fingerprint text NOT NULL);
        CREATE TABLE cp6_provisioning_v1.operations(operation_id uuid PRIMARY KEY,canonical text NOT NULL,result text NOT NULL);
        REVOKE ALL ON ALL TABLES IN SCHEMA cp6_provisioning_v1 FROM PUBLIC;
        """;
    internal static string Q(string value) => new NpgsqlCommandBuilder().QuoteIdentifier(value);
    private static async Task<int> Sql(NpgsqlConnection c,NpgsqlTransaction tx,string sql,CancellationToken ct)
    { await using var command = new NpgsqlCommand(sql,c,tx); return await command.ExecuteNonQueryAsync(ct); }
    private static async Task<object?> Scalar(NpgsqlConnection c,NpgsqlTransaction tx,string sql,CancellationToken ct)
    { await using var command = new NpgsqlCommand(sql,c,tx); return await command.ExecuteScalarAsync(ct); }
}
