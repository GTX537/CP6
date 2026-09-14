using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.SqlClient;

namespace CP6.Crm.SourceFence;

public sealed record ActualSourceInspectionOptions(string ConnectionString, string ExpectedDatabaseName,
    Guid ExpectedDatabaseGuid, string ExpectedServerName, string? ExpectedScopeSha256 = null,
    int LockTimeoutMilliseconds = 5000, int CommandTimeoutSeconds = 30);

public sealed record ActualSourceIdentity(string ServerName, string MachineName, string DatabaseName,
    Guid BrokerGuid, Guid DatabaseGuid, Guid FamilyGuid, Guid RecoveryForkGuid, DateTime CreatedAtServerLocal,
    string OriginalLogin, string OriginalLoginSid);
public sealed record SourcePrincipal(string Name, string Type, string Sid, bool Disabled, bool? IsSysadmin);
public sealed record SourceSession(int SessionId, string OriginalLogin, string? HostName, string? ProgramName,
    string? CurrentDatabase, string Status);
public sealed record ActualSourceInspection(DateTimeOffset ObservationStartedUtc, DateTimeOffset ObservedAtUtc, ActualSourceIdentity Identity,
    IReadOnlyDictionary<string, long> SourceRows, string SchemaSha256, string SecuritySha256,
    string ProgramsSha256, string SqlAgentSha256, string MigrationHistorySha256, string ScopeSha256,
    IReadOnlyList<SourcePrincipal> ServerPrincipals, IReadOnlyList<SourcePrincipal> DatabasePrincipals,
    IReadOnlyList<SourceSession> Sessions, int SqlAgentJobCount, int EnabledSqlAgentJobCount,
    int OpaqueModuleCount, IReadOnlyList<string> Blockers)
{
    public string ScopeFormat => "CP6.C04A.ActualSourceInspection.v1";
    public bool ObserverIsSysadmin => true;
    public bool SourceTableInventoryVerified => true;
    public bool MutationAuthorized => false;
    public bool CompleteWriteFenceVerified => false;
    public bool FullSourceColumnSchemaVerified => false;
    public bool WriterInventoryComplete => false;
    public string AcceptanceScope => "point-in-time-local-source-inspection; no-approval-or-write-fence; C04A-open";
}

// Deliberately separate from mutation entries: this public API only inspects.
// This type exposes no DDL, permission change, approval, freeze or reopen operation.
public sealed class ActualSourceInspector(ActualSourceInspectionOptions options)
{
    public async Task<ActualSourceInspection> InspectAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var builder = ValidateOptions();
            await using var connection = new SqlConnection(builder.ConnectionString);
            await connection.OpenAsync(cancellationToken);
            await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
            var report = await CaptureWithinTransactionAsync(connection, transaction, false, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return report;
        }
        catch (SourceFenceException) { throw; }
        catch (OperationCanceledException) { throw new SourceFenceException("C04A_CANCELLED"); }
        catch (SqlException error)
        {
            throw new SourceFenceException(cancellationToken.IsCancellationRequested ? "C04A_CANCELLED" : error.Number switch
            { -2 or 1222 => "C04A_LOCK_TIMEOUT", 1205 => "C04A_LOCK_UNAVAILABLE", _ => "C04A_SQL_FAILURE" });
        }
        catch (ArgumentException) { throw new SourceFenceException("C04A_INVALID_OPTIONS"); }
        catch (InvalidOperationException) { throw new SourceFenceException("C04A_SQL_FAILURE"); }
    }

    internal async Task<ActualSourceInspection> CaptureWithinTransactionAsync(SqlConnection connection, SqlTransaction transaction,
        bool exclusive, CancellationToken cancellationToken)
    {
        var started = DateTimeOffset.UtcNow;
        var session = new InspectionSession(connection, options.CommandTimeoutSeconds, cancellationToken) { Transaction = transaction };
        // Server and msdb catalogs can silently omit rows for lesser identities.
        // Refuse such observations instead of reporting an incomplete empty inventory.
        if (await session.ScalarAsync<int>("SELECT CASE WHEN IS_SRVROLEMEMBER(N'sysadmin')=1 AND SUSER_SNAME()=ORIGINAL_LOGIN() THEN 1 ELSE 0 END;") != 1)
            Fail("C04A_INSPECTION_VISIBILITY");
        var identity = await ReadIdentityAsync(session);
        await session.ExecuteAsync($"SET XACT_ABORT ON; SET LOCK_TIMEOUT {options.LockTimeoutMilliseconds};");
        var acquired = await session.ScalarAsync<int>("DECLARE @r int; EXEC @r=sys.sp_getapplock @Resource=N'CP6.C04A.SourceFence.v1',@LockMode=@mode,@LockOwner=N'Transaction',@LockTimeout=@timeout; SELECT @r;", ("@timeout", options.LockTimeoutMilliseconds), ("@mode", exclusive ? "Exclusive" : "Shared"));
        if (acquired < 0) Fail(acquired == -1 ? "C04A_LOCK_TIMEOUT" : "C04A_LOCK_UNAVAILABLE");
        var tables = await session.RowsAsync("SELECT s.name,t.name,t.is_memory_optimized,t.temporal_type,t.is_filetable FROM sys.tables t JOIN sys.schemas s ON s.schema_id=t.schema_id WHERE LOWER(t.name) LIKE N'crm[_]%';",
            row => (Schema: row.GetString(0), Name: row.GetString(1), Unsupported: row.GetBoolean(2) || row.GetByte(3) != 0 || row.GetBoolean(4)));
        if (tables.Count != SourceFence.Tables.Length || tables.Any(t => t.Schema != "dbo" || t.Unsupported) ||
            !tables.Select(t => t.Name).OrderBy(n => n, StringComparer.Ordinal).SequenceEqual(SourceFence.Tables))
            Fail("C04A_SOURCE_INVENTORY");
        var rows = new SortedDictionary<string, long>(StringComparer.Ordinal);
        foreach (var table in SourceFence.Tables)
            rows.Add(table, await session.ScalarAsync<long>($"SELECT COUNT_BIG(*) FROM dbo.[{table}] WITH ({(exclusive ? "TABLOCKX" : "TABLOCK")},HOLDLOCK);"));

        var schema = await session.JsonAsync(SchemaSql);
        var security = await session.JsonAsync(SecuritySql);
        var programs = await session.JsonAsync(ProgramsSql);
        var jobs = await session.JsonAsync(JobsSql);
        var migrations = await session.JsonAsync(MigrationSql);
        var serverPrincipals = await session.RowsAsync("SELECT name,type_desc,CONVERT(varchar(172),sid,2),is_disabled,IS_SRVROLEMEMBER(N'sysadmin',name) FROM sys.server_principals ORDER BY principal_id;",
            row => new SourcePrincipal(row.GetString(0), row.GetString(1), row.GetString(2), row.GetBoolean(3), row.IsDBNull(4) ? null : row.GetInt32(4) == 1));
        var databasePrincipals = await session.RowsAsync("SELECT name,type_desc,COALESCE(CONVERT(varchar(172),sid,2),'') FROM sys.database_principals ORDER BY principal_id;",
            row => new SourcePrincipal(row.GetString(0), row.GetString(1), row.GetString(2), false, null));
        var sessions = await session.RowsAsync("SELECT session_id,original_login_name,host_name,program_name,DB_NAME(database_id),status FROM sys.dm_exec_sessions WHERE is_user_process=1 AND session_id<>@@SPID ORDER BY session_id;",
            row => new SourceSession(row.GetInt16(0), row.GetString(1), row.IsDBNull(2) ? null : row.GetString(2), row.IsDBNull(3) ? null : row.GetString(3), row.IsDBNull(4) ? null : row.GetString(4), row.GetString(5)));
        var opaque = await session.ScalarAsync<int>("SELECT COUNT(*) FROM sys.sql_modules WHERE definition IS NULL;");
        var jobCount = await session.ScalarAsync<int>("SELECT COUNT(*) FROM msdb.dbo.sysjobs;");
        var enabledJobs = await session.ScalarAsync<int>("SELECT COUNT(*) FROM msdb.dbo.sysjobs WHERE enabled=1;");

        // Source S locks prevent concurrent source writes/DDL. Server permissions,
        // other modules and Agent can change independently: detect observed drift.
        // Two reads are not a continuous fence or proof against an ABA change.
        if (security != await session.JsonAsync(SecuritySql) || programs != await session.JsonAsync(ProgramsSql) ||
            jobs != await session.JsonAsync(JobsSql) || migrations != await session.JsonAsync(MigrationSql) ||
            identity != await ReadIdentityAsync(session))
            Fail("C04A_INSPECTION_DRIFT");
        var schemaHash = Hash(schema); var securityHash = Hash(security); var programsHash = Hash(programs);
        var jobsHash = Hash(jobs); var migrationsHash = Hash(migrations);
        var scope = Hash(JsonSerializer.Serialize(new
        {
            format = "CP6.C04A.ActualSourceInspection.v1", identity, sourceRows = rows,
            schemaSha256 = schemaHash, securitySha256 = securityHash, programsSha256 = programsHash,
            sqlAgentSha256 = jobsHash, migrationHistorySha256 = migrationsHash
        }));
        if (options.ExpectedScopeSha256 is { } expected && !string.Equals(scope, expected, StringComparison.OrdinalIgnoreCase))
            Fail("C04A_SCOPE_CHANGED");
        var blockers = new List<string> { "C04A_APPROVAL_NOT_BOUND", "C04A_EXTERNAL_WRITERS_UNVERIFIED", "C04A_PRIVILEGED_IDENTITIES_UNISOLATED", "C04A_TARGET_FIRST_WRITE_UNBOUND" };
        if (rows.Values.Any(count => count != 0)) blockers.Add("C04A_SOURCE_NONEMPTY");
        if (opaque != 0) blockers.Add("C04A_OPAQUE_MODULES");
        if (enabledJobs != 0) blockers.Add("C04A_ENABLED_AGENT_JOBS_UNCLASSIFIED");
        return new(started, DateTimeOffset.UtcNow, identity, rows, schemaHash, securityHash, programsHash, jobsHash,
            migrationsHash, scope, serverPrincipals, databasePrincipals, sessions, jobCount, enabledJobs, opaque, blockers);
    }

    internal async Task<(string Security, string Programs)> CaptureUnownedMetadataAsync(SqlConnection connection,
        SqlTransaction transaction, bool excludeVerifiedFence, CancellationToken token)
    {
        var security = SecuritySql;
        var programs = ProgramsSql;
        if (excludeVerifiedFence)
        {
            var tableIds = string.Join(',', SourceFence.Tables.Select(t => $"OBJECT_ID(N'dbo.{t}')"));
            var guardIds = string.Join(',', SourceFence.Tables.Select(t => $"COALESCE(OBJECT_ID(N'dbo.C04A_Fence_{t}'),-1)"))
                + ",COALESCE(OBJECT_ID(N'crm_source_control.C04A_Audit_AppendOnly'),-1)";
            security = security.Replace("FROM sys.database_permissions ORDER BY", $"FROM sys.database_permissions WHERE NOT (grantee_principal_id=DATABASE_PRINCIPAL_ID(N'public') AND grantor_principal_id=1 AND minor_id=0 AND state='D' AND permission_name IN ('INSERT','UPDATE','DELETE','ALTER') AND ((class=1 AND major_id IN ({tableIds})) OR (class=3 AND major_id=SCHEMA_ID(N'crm_source_control')))) ORDER BY", StringComparison.Ordinal)
                .Replace("FROM sys.schemas ORDER BY", "FROM sys.schemas WHERE name<>N'crm_source_control' ORDER BY", StringComparison.Ordinal);
            programs = programs.Replace("WHERE m.object_id IS NOT NULL OR o.type IN ('PC','FS','FT','TA') ORDER BY",
                $"WHERE (m.object_id IS NOT NULL OR o.type IN ('PC','FS','FT','TA')) AND o.object_id NOT IN ({guardIds}) ORDER BY", StringComparison.Ordinal)
                .Replace("m.object_id=t.object_id ORDER BY t.object_id", $"m.object_id=t.object_id WHERE t.object_id NOT IN ({guardIds}) ORDER BY t.object_id", StringComparison.Ordinal);
        }
        var session = new InspectionSession(connection, options.CommandTimeoutSeconds, token) { Transaction = transaction };
        return (Hash(await session.JsonAsync(security)), Hash(await session.JsonAsync(programs)));
    }

    internal SqlConnectionStringBuilder ValidateOptions()
    {
        if (string.IsNullOrWhiteSpace(options.ExpectedDatabaseName) || string.IsNullOrWhiteSpace(options.ExpectedServerName) ||
            options.ExpectedDatabaseGuid == Guid.Empty || options.LockTimeoutMilliseconds is < 100 or > 60000 ||
            options.CommandTimeoutSeconds is < 1 or > 300 || options.ExpectedScopeSha256 is { } hash &&
            (hash.Length != 64 || !hash.All(Uri.IsHexDigit))) Fail("C04A_INVALID_OPTIONS");
        var builder = new SqlConnectionStringBuilder(options.ConnectionString);
        if (new[] { "master", "model", "msdb", "tempdb" }.Contains(options.ExpectedDatabaseName, StringComparer.OrdinalIgnoreCase) ||
            builder.InitialCatalog != options.ExpectedDatabaseName || builder.AttachDBFilename.Length != 0 ||
            builder.FailoverPartner.Length != 0 || builder.ApplicationIntent != ApplicationIntent.ReadWrite)
            Fail("C04A_DATABASE_IDENTITY");
        var server = builder.DataSource;
        if (server.StartsWith("tcp:", StringComparison.OrdinalIgnoreCase) || server.StartsWith("lpc:", StringComparison.OrdinalIgnoreCase)) server = server[4..];
        server = server.Split('\\')[0].Split(',')[0];
        if (server != "." && server != "(local)" && server != "127.0.0.1" && server != "::1" &&
            !server.Equals("localhost", StringComparison.OrdinalIgnoreCase) && !server.Equals(Environment.MachineName, StringComparison.OrdinalIgnoreCase))
            Fail("C04A_LOCAL_SOURCE_REQUIRED");
        builder.Enlist = false;
        builder.Pooling = false;
        builder.ApplicationName = "CP6.C04A.ActualSourceInspector";
        return builder;
    }

    private async Task<ActualSourceIdentity> ReadIdentityAsync(InspectionSession session)
    {
        var identities = await session.RowsAsync("SELECT CAST(SERVERPROPERTY('ServerName') AS nvarchar(128)),CAST(SERVERPROPERTY('MachineName') AS nvarchar(128)),d.name,d.service_broker_guid,r.database_guid,r.family_guid,r.recovery_fork_guid,d.create_date,ORIGINAL_LOGIN(),CONVERT(varchar(172),SUSER_SID(ORIGINAL_LOGIN()),2),d.database_id,CAST(SERVERPROPERTY('IsClustered') AS int) FROM sys.databases d JOIN sys.database_recovery_status r ON r.database_id=d.database_id WHERE d.database_id=DB_ID();",
            row => (Identity: new ActualSourceIdentity(row.GetString(0), row.GetString(1), row.GetString(2), row.GetGuid(3), row.GetGuid(4), row.GetGuid(5), row.GetGuid(6), row.GetDateTime(7), row.GetString(8), row.GetString(9)), DatabaseId: row.GetInt32(10), Clustered: row.GetInt32(11)));
        if (identities.Count != 1) Fail("C04A_DATABASE_IDENTITY");
        var found = identities[0];
        if (found.DatabaseId <= 4 || found.Identity.ServerName != options.ExpectedServerName ||
            found.Identity.DatabaseName != options.ExpectedDatabaseName || found.Identity.BrokerGuid != options.ExpectedDatabaseGuid)
            Fail("C04A_DATABASE_IDENTITY");
        if (!found.Identity.MachineName.Equals(Environment.MachineName, StringComparison.OrdinalIgnoreCase) || found.Clustered != 0)
            Fail("C04A_LOCAL_SOURCE_REQUIRED");
        return found.Identity;
    }

    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    private static void Fail(string code) => throw new SourceFenceException(code);

    private const string SchemaSql = """
        SELECT t.name, t.object_id, t.lob_data_space_id,t.filestream_data_space_id,t.lock_escalation,t.is_replicated,t.is_tracked_by_cdc,
        (SELECT c.column_id,c.name,c.system_type_id,c.user_type_id,c.max_length,c.precision,c.scale,c.collation_name,c.is_nullable,c.is_identity,c.is_computed,c.is_rowguidcol,c.is_sparse,c.generated_always_type,c.encryption_type,
            dc.name AS default_name,dc.definition AS default_definition,cc.definition AS computed_definition,cc.is_persisted,CONVERT(nvarchar(128),ic.seed_value) AS identity_seed,CONVERT(nvarchar(128),ic.increment_value) AS identity_increment
            FROM sys.columns c LEFT JOIN sys.default_constraints dc ON dc.object_id=c.default_object_id LEFT JOIN sys.computed_columns cc ON cc.object_id=c.object_id AND cc.column_id=c.column_id LEFT JOIN sys.identity_columns ic ON ic.object_id=c.object_id AND ic.column_id=c.column_id
            WHERE c.object_id=t.object_id ORDER BY c.column_id FOR JSON PATH,INCLUDE_NULL_VALUES) AS columns_json,
        (SELECT i.index_id,i.name,i.type,i.is_unique,i.is_primary_key,i.is_unique_constraint,i.is_disabled,i.has_filter,i.filter_definition,i.data_space_id,
            (SELECT column_id,key_ordinal,is_descending_key,is_included_column,partition_ordinal FROM sys.index_columns WHERE object_id=i.object_id AND index_id=i.index_id ORDER BY index_column_id FOR JSON PATH,INCLUDE_NULL_VALUES) AS columns_json
            FROM sys.indexes i WHERE i.object_id=t.object_id ORDER BY i.index_id FOR JSON PATH,INCLUDE_NULL_VALUES) AS indexes_json,
        (SELECT f.name,f.parent_object_id,f.referenced_object_id,f.delete_referential_action,f.update_referential_action,f.is_disabled,f.is_not_trusted,f.is_not_for_replication,
            (SELECT constraint_column_id,parent_column_id,referenced_column_id FROM sys.foreign_key_columns WHERE constraint_object_id=f.object_id ORDER BY constraint_column_id FOR JSON PATH,INCLUDE_NULL_VALUES) AS columns_json
            FROM sys.foreign_keys f WHERE f.parent_object_id=t.object_id OR f.referenced_object_id=t.object_id ORDER BY f.object_id FOR JSON PATH,INCLUDE_NULL_VALUES) AS foreign_keys_json,
        (SELECT name,definition,is_disabled,is_not_trusted,is_not_for_replication FROM sys.check_constraints WHERE parent_object_id=t.object_id ORDER BY object_id FOR JSON PATH,INCLUDE_NULL_VALUES) AS checks_json
        FROM sys.tables t JOIN sys.schemas s ON s.schema_id=t.schema_id WHERE s.name=N'dbo' AND LOWER(t.name) LIKE N'crm[_]%' ORDER BY t.name FOR JSON PATH,INCLUDE_NULL_VALUES;
        """;
    private const string SecuritySql = """
        SELECT
        (SELECT name,owner_sid,is_trustworthy_on,is_db_chaining_on,containment FROM sys.databases WHERE database_id=DB_ID() FOR JSON PATH,INCLUDE_NULL_VALUES) AS database_json,
        (SELECT principal_id,name,type,sid,is_disabled,default_database_name,credential_id,owning_principal_id FROM sys.server_principals ORDER BY principal_id FOR JSON PATH,INCLUDE_NULL_VALUES) AS server_principals_json,
        (SELECT * FROM sys.server_role_members ORDER BY role_principal_id,member_principal_id FOR JSON PATH,INCLUDE_NULL_VALUES) AS server_roles_json,
        (SELECT * FROM sys.server_permissions ORDER BY class,major_id,grantee_principal_id,grantor_principal_id,type FOR JSON PATH,INCLUDE_NULL_VALUES) AS server_permissions_json,
        (SELECT principal_id,name,type,sid,default_schema_name,owning_principal_id,authentication_type FROM sys.database_principals ORDER BY principal_id FOR JSON PATH,INCLUDE_NULL_VALUES) AS database_principals_json,
        (SELECT * FROM sys.database_role_members ORDER BY role_principal_id,member_principal_id FOR JSON PATH,INCLUDE_NULL_VALUES) AS database_roles_json,
        (SELECT * FROM sys.database_permissions ORDER BY class,major_id,minor_id,grantee_principal_id,grantor_principal_id,type FOR JSON PATH,INCLUDE_NULL_VALUES) AS database_permissions_json,
        (SELECT schema_id,name,principal_id FROM sys.schemas ORDER BY schema_id FOR JSON PATH,INCLUDE_NULL_VALUES) AS schema_owners_json,
        (SELECT object_id,principal_id FROM sys.objects WHERE principal_id IS NOT NULL ORDER BY object_id FOR JSON PATH,INCLUDE_NULL_VALUES) AS object_owners_json,
        (SELECT class,major_id,thumbprint,crypt_type,crypt_property FROM sys.crypt_properties ORDER BY class,major_id,thumbprint,crypt_type FOR JSON PATH,INCLUDE_NULL_VALUES) AS module_signatures_json
        FOR JSON PATH,INCLUDE_NULL_VALUES;
        """;
    private const string ProgramsSql = """
        SELECT
        (SELECT o.object_id,o.name,o.type,o.schema_id,m.definition,m.uses_ansi_nulls,m.uses_quoted_identifier,m.is_schema_bound,m.execute_as_principal_id FROM sys.objects o LEFT JOIN sys.sql_modules m ON m.object_id=o.object_id WHERE m.object_id IS NOT NULL OR o.type IN ('PC','FS','FT','TA') ORDER BY o.object_id FOR JSON PATH,INCLUDE_NULL_VALUES) AS modules_json,
        (SELECT t.object_id,t.name,t.parent_class,t.parent_id,t.is_disabled,t.is_not_for_replication,t.is_instead_of_trigger,m.definition,m.execute_as_principal_id FROM sys.triggers t LEFT JOIN sys.sql_modules m ON m.object_id=t.object_id ORDER BY t.object_id FOR JSON PATH,INCLUDE_NULL_VALUES) AS triggers_json,
        (SELECT object_id,name,schema_id,base_object_name FROM sys.synonyms ORDER BY object_id FOR JSON PATH,INCLUDE_NULL_VALUES) AS synonyms_json,
        (SELECT assembly_id,file_id,content FROM sys.assembly_files ORDER BY assembly_id,file_id FOR JSON PATH,INCLUDE_NULL_VALUES) AS assemblies_json,
        (SELECT object_id,assembly_id,assembly_class,assembly_method,execute_as_principal_id FROM sys.assembly_modules ORDER BY object_id FOR JSON PATH,INCLUDE_NULL_VALUES) AS assembly_modules_json,
        (SELECT object_id,name,schema_id,is_activation_enabled,activation_procedure,execute_as_principal_id,is_receive_enabled FROM sys.service_queues ORDER BY object_id FOR JSON PATH,INCLUDE_NULL_VALUES) AS service_queues_json
        FOR JSON PATH,INCLUDE_NULL_VALUES;
        """;
    private const string JobsSql = """
        SELECT j.job_id,j.name,j.enabled,j.owner_sid,j.start_step_id,
        (SELECT step_id,step_name,subsystem,command,database_name,database_user_name,proxy_id,on_success_action,on_success_step_id,on_fail_action,on_fail_step_id,retry_attempts,retry_interval FROM msdb.dbo.sysjobsteps WHERE job_id=j.job_id ORDER BY step_id FOR JSON PATH,INCLUDE_NULL_VALUES) AS steps_json,
        (SELECT s.schedule_id,s.name,s.enabled,s.freq_type,s.freq_interval,s.freq_subday_type,s.freq_subday_interval,s.freq_relative_interval,s.freq_recurrence_factor,s.active_start_date,s.active_end_date,s.active_start_time,s.active_end_time FROM msdb.dbo.sysjobschedules js JOIN msdb.dbo.sysschedules s ON s.schedule_id=js.schedule_id WHERE js.job_id=j.job_id ORDER BY s.schedule_id FOR JSON PATH,INCLUDE_NULL_VALUES) AS schedules_json
        FROM msdb.dbo.sysjobs j ORDER BY j.job_id FOR JSON PATH,INCLUDE_NULL_VALUES;
        """;
    private const string MigrationSql = """
        IF OBJECT_ID(N'dbo.__EFMigrationsHistory',N'U') IS NULL SELECT N'[]';
        ELSE SELECT MigrationId,ProductVersion FROM dbo.__EFMigrationsHistory ORDER BY MigrationId FOR JSON PATH,INCLUDE_NULL_VALUES;
        """;

    private sealed class InspectionSession(SqlConnection connection, int timeout, CancellationToken token)
    {
        internal SqlTransaction? Transaction { get; set; }
        private SqlCommand Command(string sql, (string Name, object Value)[] parameters)
        {
            var command = new SqlCommand(sql, connection, Transaction) { CommandTimeout = timeout };
            foreach (var (name, value) in parameters) command.Parameters.AddWithValue(name, value);
            return command;
        }
        internal async Task ExecuteAsync(string sql)
        {
            using var command = Command(sql, []);
            await command.ExecuteNonQueryAsync(token);
        }
        internal async Task<T> ScalarAsync<T>(string sql, params (string Name, object Value)[] parameters)
        {
            using var command = Command(sql, parameters);
            return (T)(await command.ExecuteScalarAsync(token))!;
        }
        internal async Task<List<T>> RowsAsync<T>(string sql, Func<SqlDataReader, T> map)
        {
            using var command = Command(sql, []);
            using var reader = await command.ExecuteReaderAsync(token);
            var rows = new List<T>();
            while (await reader.ReadAsync(token)) rows.Add(map(reader));
            return rows;
        }
        internal async Task<string> JsonAsync(string sql) => string.Concat(await RowsAsync(sql, row => row.GetString(0)));
    }
}
