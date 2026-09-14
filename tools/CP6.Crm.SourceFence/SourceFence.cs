using System.Data;
using Microsoft.Data.SqlClient;

namespace CP6.Crm.SourceFence;

public sealed record SourceFenceOptions(string ConnectionString, string ExpectedDatabaseName,
    Guid ExpectedDatabaseGuid, int LockTimeoutMilliseconds = 5000, int CommandTimeoutSeconds = 30);

public sealed record SourceFenceStatus(string State, Guid? RunId, long Generation,
    IReadOnlyDictionary<string, long> SourceRows, bool GuardInventoryVerified,
    bool CompleteWriteFenceVerified = false,
    string AcceptanceScope = "local-rehearsal-only; privileged-identities-and-live-writer-inventory-pending; C04A-open")
{
    public bool SourceTableInventoryVerified => true;
    public bool EmptyProfileVerified => SourceRows.Values.All(count => count == 0);
    public bool FullSourceColumnSchemaVerified => false;
    public bool TargetWriteIntegrationVerified => false;
    public bool ForwardOnlyIsActualTargetWriteProof => false;
}

public sealed class SourceFenceException(string code) : Exception(code)
{
    public string Code { get; } = code;
}

public sealed class SourceFence(SourceFenceOptions options)
{
    internal static readonly string[] Tables =
    [
        "Crm_Account", "Crm_Activity", "Crm_Collaborator", "Crm_Contact", "Crm_ErpLink",
        "Crm_IntakeConfig", "Crm_IntakeMember", "Crm_Lead", "Crm_MediaAsset", "Crm_MergeRecord",
        "Crm_Opportunity", "Crm_PageRevision", "Crm_PageTranslation", "Crm_PublicForm",
        "Crm_PublicRoute", "Crm_PublicSubmission", "Crm_Site", "Crm_SitePage", "Crm_SourceTouch", "Crm_StageHistory"
    ];
    private static readonly HashSet<string> OwnedPermissions = ["INSERT", "UPDATE", "DELETE", "ALTER"];
    private const string Marker = "CP6.C04A.SourceFence.SchemaVersion";
    private const string AuditTrigger = "CREATE TRIGGER [crm_source_control].[C04A_Audit_AppendOnly] ON [crm_source_control].[Audit] AFTER UPDATE, DELETE AS BEGIN SET NOCOUNT ON; THROW 51044, 'C04A_AUDIT_APPEND_ONLY', 1; END;";

    public Task<SourceFenceStatus> PreflightAsync(CancellationToken cancellationToken = default) => ExecuteAsync("Preflight", Guid.Empty, 0, cancellationToken);
    public Task<SourceFenceStatus> StatusAsync(CancellationToken cancellationToken = default) => ExecuteAsync("Status", Guid.Empty, 0, cancellationToken);
    public Task<SourceFenceStatus> FreezeAsync(Guid runId, long expectedGeneration, CancellationToken cancellationToken = default) => ExecuteAsync("Freeze", runId, expectedGeneration, cancellationToken);
    public Task<SourceFenceStatus> ReopenAsync(Guid runId, long expectedGeneration, CancellationToken cancellationToken = default) => ExecuteAsync("Reopen", runId, expectedGeneration, cancellationToken);
    public Task<SourceFenceStatus> SealForwardOnlyAsync(Guid runId, long expectedGeneration, CancellationToken cancellationToken = default) => ExecuteAsync("SealForwardOnly", runId, expectedGeneration, cancellationToken);

    private async Task<SourceFenceStatus> ExecuteAsync(string operation, Guid runId, long expectedGeneration, CancellationToken token)
    {
        try
        {
            var mutation = operation is "Freeze" or "Reopen" or "SealForwardOnly";
            ValidateOptions();
            if (mutation && (runId == Guid.Empty || expectedGeneration < 0 || expectedGeneration == long.MaxValue))
                Fail("C04A_INVALID_COMMAND");
            await using var connection = new SqlConnection(options.ConnectionString);
            await connection.OpenAsync(token);
            await VerifyIdentityAsync(connection, token);
            await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable, token);
            var result = await ExecuteWithinTransactionAsync(connection, transaction, operation, runId, expectedGeneration, false, token);
            await transaction.CommitAsync(token);
            return result;
        }
        catch (SourceFenceException) { throw; }
        catch (OperationCanceledException) { throw new SourceFenceException("C04A_CANCELLED"); }
        catch (SqlException error)
        {
            if (token.IsCancellationRequested) throw new SourceFenceException("C04A_CANCELLED");
            throw new SourceFenceException(error.Number switch
            {
                -2 or 1222 => "C04A_LOCK_TIMEOUT",
                1205 => "C04A_LOCK_UNAVAILABLE",
                _ => "C04A_SQL_FAILURE"
            });
        }
        catch (ArgumentException) { throw new SourceFenceException("C04A_INVALID_OPTIONS"); }
        catch (InvalidOperationException) { throw new SourceFenceException("C04A_SQL_FAILURE"); }
    }

    // Internal reuse keeps identity / authorization checks in each public entry point.
    // The caller owns commit; the actual-source binding and guards commit together.
    internal async Task<SourceFenceStatus> ExecuteWithinTransactionAsync(SqlConnection connection, SqlTransaction transaction,
        string operation, Guid runId, long expectedGeneration, bool actualControl, CancellationToken token)
    {
        var mutation = operation is "Freeze" or "Reopen" or "SealForwardOnly";
        var session = new Session(connection, transaction, options.CommandTimeoutSeconds, token);
        await session.ExecuteAsync($"SET XACT_ABORT ON; SET LOCK_TIMEOUT {options.LockTimeoutMilliseconds}; SET ANSI_NULLS ON; SET QUOTED_IDENTIFIER ON;");
        var acquired = await session.ScalarAsync<int>("DECLARE @result int; EXEC @result=sys.sp_getapplock @Resource=N'CP6.C04A.SourceFence.v1', @LockMode=@mode, @LockOwner=N'Transaction', @LockTimeout=@timeout, @DbPrincipal=N'public'; SELECT @result;",
            ("@mode", mutation ? "Exclusive" : "Shared"), ("@timeout", options.LockTimeoutMilliseconds));
        if (acquired < 0) Fail(acquired == -1 ? "C04A_LOCK_TIMEOUT" : "C04A_LOCK_UNAVAILABLE");
        if (!actualControl && await session.ScalarAsync<int>("SELECT COUNT(*) FROM sys.extended_properties WHERE class=0 AND name=N'CP6.C04A.ActualSourceFreeze.v1';") != 0)
            Fail("C04A_ACTUAL_CONTROL_REQUIRED");
        await VerifySourceInventoryAsync(session);

        // These locks drain every earlier writer and are retained through all DDL,
        // permission, state and audit changes. No filtered counts / soft-delete filters.
        var rows = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (var table in Tables)
            rows.Add(table, await session.ScalarAsync<long>($"SELECT COUNT_BIG(*) FROM [dbo].[{table}] WITH ({(mutation ? "TABLOCKX" : "TABLOCK")}, HOLDLOCK);"));

        var metadata = await ReadAndVerifyMetadataAsync(session);
        await VerifySourceGuardsAsync(session, metadata.State is "Frozen" or "ForwardOnly");
        // A replay acknowledges the immediately preceding transition only.
        // Reopened writers may already have resumed before its acknowledgement
        // is retried; those rows do not invalidate a completed reopen.
        var immediateReplay = mutation && metadata.Last is { } last && last.RunId == runId && last.Command == operation &&
            last.ExpectedGeneration == expectedGeneration && metadata.Generation == expectedGeneration + 1;
        var reopenedReplay = immediateReplay && operation == "Reopen" && metadata.State == "Reopened";
        if (!reopenedReplay && (operation != "Status" || metadata.State is "Frozen" or "ForwardOnly") && rows.Values.Any(count => count != 0))
            Fail("C04A_SOURCE_NONEMPTY");
        if (!mutation)
        {
            return Status(metadata, rows);
        }

        // Only the immediately preceding identical command can be replayed.
        if (immediateReplay)
        {
            return Status(metadata, rows);
        }
        if (metadata.Generation != expectedGeneration) Fail("C04A_CONFLICT");
        if (metadata.State == "ForwardOnly") Fail("C04A_FORWARD_ONLY");
        var nextState = operation switch
        {
            "Freeze" when metadata.State is "Uninitialized" or "Reopened" => "Frozen",
            "Reopen" when metadata.State == "Frozen" && metadata.RunId == runId => "Reopened",
            "SealForwardOnly" when metadata.State == "Frozen" && metadata.RunId == runId => "ForwardOnly",
            _ => throw new SourceFenceException("C04A_CONFLICT")
        };
        if (operation == "Freeze" && metadata.Events.Any(entry => entry.RunId == runId)) Fail("C04A_RUN_REUSED");
        if (metadata.State == "Uninitialized") await InitializeAsync(session);
        if (operation == "Freeze")
        {
            foreach (var table in Tables)
            {
                await session.ExecuteAsync(TriggerDefinition(table));
                await session.ExecuteAsync($"DENY INSERT, UPDATE, DELETE, ALTER ON OBJECT::[dbo].[{table}] TO [public] AS [dbo];");
            }
        }
        else if (operation == "Reopen")
        {
            foreach (var table in Tables)
            {
                await session.ExecuteAsync($"DROP TRIGGER [dbo].[C04A_Fence_{table}];");
                // Only the four public object denies owned by this tool are removed.
                // Initial conflicts and any drift were rejected before reaching here.
                await session.ExecuteAsync($"REVOKE INSERT, UPDATE, DELETE, ALTER ON OBJECT::[dbo].[{table}] FROM [public] AS [dbo];");
            }
        }
        var generation = checked(expectedGeneration + 1);
        await session.ExecuteAsync("INSERT [crm_source_control].[Audit] (Generation, RunId, Command, PreviousState, NextState, ExpectedGeneration, OccurredAt) VALUES (@generation,@run,@command,@previous,@next,@expected,SYSUTCDATETIME()); UPDATE [crm_source_control].[Control] SET RunId=@run, Generation=@generation, State=@next WHERE Singleton=1; IF @@ROWCOUNT=0 INSERT [crm_source_control].[Control] (Singleton,RunId,Generation,State) VALUES (1,@run,@generation,@next);",
            ("@generation", generation), ("@run", runId), ("@command", operation), ("@previous", metadata.State), ("@next", nextState), ("@expected", expectedGeneration));
        var after = await ReadAndVerifyMetadataAsync(session);
        await VerifySourceGuardsAsync(session, nextState is "Frozen" or "ForwardOnly");
        return Status(after, rows);
    }

    private static SourceFenceStatus Status(Metadata metadata, Dictionary<string, long> rows) =>
        new(metadata.State, metadata.RunId, metadata.Generation, rows, metadata.State is "Frozen" or "ForwardOnly");

    private void ValidateOptions()
    {
        if (string.IsNullOrWhiteSpace(options.ExpectedDatabaseName) || options.ExpectedDatabaseGuid == Guid.Empty ||
            options.LockTimeoutMilliseconds is < 100 or > 60000 || options.CommandTimeoutSeconds is < 1 or > 300)
            Fail("C04A_INVALID_OPTIONS");
        if (!options.ExpectedDatabaseName.StartsWith("CP6_C04A_Rehearsal_", StringComparison.Ordinal) ||
            options.ExpectedDatabaseName.Length <= "CP6_C04A_Rehearsal_".Length)
            Fail("C04A_DATABASE_IDENTITY");
        var builder = new SqlConnectionStringBuilder(options.ConnectionString);
        if (!string.Equals(builder.InitialCatalog, options.ExpectedDatabaseName, StringComparison.Ordinal) ||
            !string.IsNullOrEmpty(builder.AttachDBFilename) || builder.ApplicationIntent != ApplicationIntent.ReadWrite ||
            !string.IsNullOrEmpty(builder.FailoverPartner))
            Fail("C04A_DATABASE_IDENTITY");
        var server = builder.DataSource;
        if (server.StartsWith("tcp:", StringComparison.OrdinalIgnoreCase)) server = server[4..];
        server = server.Split('\\')[0].Split(',')[0];
        if (server != "." && server != "(local)" && server != "127.0.0.1" && server != "::1" &&
            !server.Equals("localhost", StringComparison.OrdinalIgnoreCase) && !server.Equals(Environment.MachineName, StringComparison.OrdinalIgnoreCase))
            Fail("C04A_LOCAL_REHEARSAL_REQUIRED");
    }

    private async Task VerifyIdentityAsync(SqlConnection connection, CancellationToken token)
    {
        using var command = new SqlCommand("SELECT DB_NAME(), service_broker_guid, CAST(SERVERPROPERTY('MachineName') AS nvarchar(128)), database_id, CAST(SERVERPROPERTY('IsClustered') AS int) FROM sys.databases WHERE database_id=DB_ID();", connection)
        { CommandTimeout = options.CommandTimeoutSeconds };
        using var reader = await command.ExecuteReaderAsync(token);
        if (!await reader.ReadAsync(token) || reader.GetString(0) != options.ExpectedDatabaseName || reader.GetGuid(1) != options.ExpectedDatabaseGuid || reader.GetInt32(3) <= 4)
            Fail("C04A_DATABASE_IDENTITY");
        if (!reader.GetString(2).Equals(Environment.MachineName, StringComparison.OrdinalIgnoreCase) || reader.GetInt32(4) != 0)
            Fail("C04A_LOCAL_REHEARSAL_REQUIRED");
    }

    private static async Task VerifySourceInventoryAsync(Session session)
    {
        var found = await session.ReadAsync("SELECT s.name,t.name,t.is_memory_optimized,t.temporal_type,t.is_filetable FROM sys.tables t JOIN sys.schemas s ON t.schema_id=s.schema_id WHERE LOWER(t.name) LIKE N'crm[_]%';",
            row => (Schema: row.GetString(0), Table: row.GetString(1), Unsupported: row.GetBoolean(2) || row.GetByte(3) != 0 || row.GetBoolean(4)));
        if (found.Count != Tables.Length || found.Any(table => table.Schema != "dbo" || table.Unsupported) ||
            !found.Select(table => table.Table).OrderBy(name => name, StringComparer.Ordinal).SequenceEqual(Tables, StringComparer.Ordinal))
            Fail("C04A_SOURCE_INVENTORY");
    }

    private static string TriggerDefinition(string table) => $"CREATE TRIGGER [dbo].[C04A_Fence_{table}] ON [dbo].[{table}] AFTER INSERT, UPDATE, DELETE AS BEGIN SET NOCOUNT ON; THROW 51041, 'C04A_SOURCE_FROZEN', 1; END;";

    private static async Task VerifySourceGuardsAsync(Session session, bool frozen)
    {
        foreach (var table in Tables)
        {
            var triggers = await session.ReadAsync("SELECT tr.name,tr.is_disabled,tr.is_not_for_replication,tr.is_instead_of_trigger,tr.type,m.definition,m.uses_ansi_nulls,m.uses_quoted_identifier,m.execute_as_principal_id FROM sys.triggers tr LEFT JOIN sys.sql_modules m ON m.object_id=tr.object_id WHERE tr.parent_id=OBJECT_ID(@table);",
                row => new Trigger(row.GetString(0), row.GetBoolean(1), row.GetBoolean(2), row.GetBoolean(3), row.GetString(4), row.IsDBNull(5) ? null : row.GetString(5), !row.IsDBNull(6) && row.GetBoolean(6), !row.IsDBNull(7) && row.GetBoolean(7), !row.IsDBNull(8)), ("@table", "dbo." + table));
            if ((!frozen && triggers.Count != 0) || (frozen && (triggers.Count != 1 || !triggers[0].Matches("C04A_Fence_" + table, TriggerDefinition(table)))))
                Fail("C04A_GUARD_DRIFT");
            var permissions = await session.ReadAsync("SELECT permission_name,state,minor_id,grantor_principal_id FROM sys.database_permissions WHERE class=1 AND major_id=OBJECT_ID(@table) AND grantee_principal_id=DATABASE_PRINCIPAL_ID(N'public');",
                row => (Permission: row.GetString(0), State: row.GetString(1), Column: row.GetInt32(2), Grantor: row.GetInt32(3)), ("@table", "dbo." + table));
            var owned = permissions.Where(permission => OwnedPermissions.Contains(permission.Permission)).ToArray();
            if (frozen)
            {
                if (owned.Length != 4 || owned.Any(permission => permission.State != "D" || permission.Column != 0 || permission.Grantor != 1) ||
                    !owned.Select(permission => permission.Permission).ToHashSet(StringComparer.Ordinal).SetEquals(OwnedPermissions))
                    Fail("C04A_GUARD_DRIFT");
            }
            else if (owned.Length != 0) Fail("C04A_PERMISSION_CONFLICT");
        }
        // A same-name guard orphaned onto an unexpected object is also drift.
        var guardCount = await session.ScalarAsync<int>("SELECT COUNT(*) FROM sys.triggers WHERE name LIKE N'C04A[_]Fence[_]%';");
        if (guardCount != (frozen ? Tables.Length : 0)) Fail("C04A_GUARD_DRIFT");
    }

    private static async Task InitializeAsync(Session session)
    {
        await session.ExecuteAsync("CREATE SCHEMA [crm_source_control] AUTHORIZATION [dbo];");
        await session.ExecuteAsync("""
            CREATE TABLE [crm_source_control].[Control] (
              Singleton bit NOT NULL CONSTRAINT PK_C04A_Control PRIMARY KEY CONSTRAINT CK_C04A_Control_Singleton CHECK (Singleton=1),
              RunId uniqueidentifier NOT NULL, Generation bigint NOT NULL, State nvarchar(16) NOT NULL);
            CREATE TABLE [crm_source_control].[Audit] (
              Generation bigint NOT NULL CONSTRAINT PK_C04A_Audit PRIMARY KEY,
              RunId uniqueidentifier NOT NULL, Command nvarchar(32) NOT NULL,
              PreviousState nvarchar(16) NOT NULL, NextState nvarchar(16) NOT NULL,
              ExpectedGeneration bigint NOT NULL, OccurredAt datetime2(7) NOT NULL);
            DENY INSERT, UPDATE, DELETE, ALTER ON SCHEMA::[crm_source_control] TO [public] AS [dbo];
            """);
        await session.ExecuteAsync(AuditTrigger);
        await session.ExecuteAsync("EXEC sys.sp_addextendedproperty @name=@marker,@value=N'1';", ("@marker", Marker));
    }

    private static async Task<Metadata> ReadAndVerifyMetadataAsync(Session session)
    {
        var schema = await session.ScalarAsync<int>("SELECT COUNT(*) FROM sys.schemas WHERE name=N'crm_source_control' AND principal_id=1;");
        var marker = await session.ReadAsync("SELECT CONVERT(nvarchar(128),value) FROM sys.extended_properties WHERE class=0 AND name=@marker;", row => row.GetString(0), ("@marker", Marker));
        if (schema == 0 && marker.Count == 0)
        {
            if (await session.ScalarAsync<int>("SELECT COUNT(*) FROM sys.schemas WHERE name=N'crm_source_control';") != 0) Fail("C04A_METADATA_DRIFT");
            return new("Uninitialized", null, 0, []);
        }
        if (schema != 1 || marker.Count != 1 || marker[0] != "1") Fail("C04A_METADATA_DRIFT");
        var objects = await session.ReadAsync("SELECT name,type FROM sys.objects WHERE schema_id=SCHEMA_ID(N'crm_source_control') AND type IN ('U','V','P','FN','IF','TF');", row => (Name: row.GetString(0), Type: row.GetString(1).Trim()));
        if (objects.Count != 2 || !objects.Contains(("Control", "U")) || !objects.Contains(("Audit", "U"))) Fail("C04A_METADATA_DRIFT");
        var columns = await session.ReadAsync("SELECT t.name,c.name,TYPE_NAME(c.user_type_id),c.max_length,c.is_nullable,c.is_identity,c.is_computed FROM sys.tables t JOIN sys.columns c ON t.object_id=c.object_id WHERE t.schema_id=SCHEMA_ID(N'crm_source_control') ORDER BY t.name,c.column_id;",
            row => $"{row.GetString(0)}:{row.GetString(1)}:{row.GetString(2)}:{row.GetInt16(3)}:{row.GetBoolean(4)}:{row.GetBoolean(5)}:{row.GetBoolean(6)}");
        string[] expected =
        [
            "Audit:Generation:bigint:8:False:False:False", "Audit:RunId:uniqueidentifier:16:False:False:False",
            "Audit:Command:nvarchar:64:False:False:False", "Audit:PreviousState:nvarchar:32:False:False:False",
            "Audit:NextState:nvarchar:32:False:False:False", "Audit:ExpectedGeneration:bigint:8:False:False:False",
            "Audit:OccurredAt:datetime2:8:False:False:False", "Control:Singleton:bit:1:False:False:False",
            "Control:RunId:uniqueidentifier:16:False:False:False", "Control:Generation:bigint:8:False:False:False",
            "Control:State:nvarchar:32:False:False:False"
        ];
        if (!columns.SequenceEqual(expected, StringComparer.Ordinal)) Fail("C04A_METADATA_DRIFT");
        var keys = await session.ReadAsync("SELECT t.name,k.name,c.name,ic.key_ordinal,i.is_unique,i.is_disabled FROM sys.key_constraints k JOIN sys.tables t ON t.object_id=k.parent_object_id JOIN sys.indexes i ON i.object_id=t.object_id AND i.index_id=k.unique_index_id JOIN sys.index_columns ic ON ic.object_id=i.object_id AND ic.index_id=i.index_id JOIN sys.columns c ON c.object_id=ic.object_id AND c.column_id=ic.column_id WHERE t.schema_id=SCHEMA_ID(N'crm_source_control') ORDER BY t.name,ic.key_ordinal;",
            row => $"{row.GetString(0)}:{row.GetString(1)}:{row.GetString(2)}:{row.GetByte(3)}:{row.GetBoolean(4)}:{row.GetBoolean(5)}");
        if (!keys.SequenceEqual(new[] { "Audit:PK_C04A_Audit:Generation:1:True:False", "Control:PK_C04A_Control:Singleton:1:True:False" }, StringComparer.Ordinal))
            Fail("C04A_METADATA_DRIFT");
        var checks = await session.ReadAsync("SELECT name,definition,is_disabled,is_not_trusted FROM sys.check_constraints WHERE schema_id=SCHEMA_ID(N'crm_source_control');",
            row => (Name: row.GetString(0), Definition: new string(row.GetString(1).Where(character => !"()[] ".Contains(character)).ToArray()), Disabled: row.GetBoolean(2), Untrusted: row.GetBoolean(3)));
        if (checks.Count != 1 || checks[0] != ("CK_C04A_Control_Singleton", "Singleton=1", false, false) ||
            await session.ScalarAsync<int>("SELECT COUNT(*) FROM sys.objects WHERE schema_id=SCHEMA_ID(N'crm_source_control') AND type IN ('F','D');") != 0)
            Fail("C04A_METADATA_DRIFT");
        var guards = await session.ReadAsync("SELECT tr.name,tr.is_disabled,tr.is_not_for_replication,tr.is_instead_of_trigger,tr.type,m.definition,m.uses_ansi_nulls,m.uses_quoted_identifier,m.execute_as_principal_id FROM sys.triggers tr LEFT JOIN sys.sql_modules m ON m.object_id=tr.object_id WHERE tr.parent_id IN (OBJECT_ID(N'crm_source_control.Audit'),OBJECT_ID(N'crm_source_control.Control'));",
            row => new Trigger(row.GetString(0), row.GetBoolean(1), row.GetBoolean(2), row.GetBoolean(3), row.GetString(4), row.IsDBNull(5) ? null : row.GetString(5), !row.IsDBNull(6) && row.GetBoolean(6), !row.IsDBNull(7) && row.GetBoolean(7), !row.IsDBNull(8)));
        if (guards.Count != 1 || !guards[0].Matches("C04A_Audit_AppendOnly", AuditTrigger)) Fail("C04A_METADATA_DRIFT");
        var denies = await session.ReadAsync("SELECT permission_name,state,grantor_principal_id FROM sys.database_permissions WHERE class=3 AND major_id=SCHEMA_ID(N'crm_source_control') AND grantee_principal_id=DATABASE_PRINCIPAL_ID(N'public');",
            row => (Permission: row.GetString(0), State: row.GetString(1), Grantor: row.GetInt32(2)));
        if (denies.Count != 4 || denies.Any(deny => deny.State != "D" || deny.Grantor != 1) ||
            !denies.Select(deny => deny.Permission).ToHashSet(StringComparer.Ordinal).SetEquals(OwnedPermissions)) Fail("C04A_METADATA_DRIFT");
        // Column permissions can override table/schema DENY in SQL Server. The owned
        // metadata schema never grants column writes and rejects injected overrides.
        if (await session.ScalarAsync<int>("SELECT COUNT(*) FROM sys.database_permissions p JOIN sys.objects o ON p.major_id=o.object_id WHERE p.class=1 AND o.schema_id=SCHEMA_ID(N'crm_source_control') AND p.minor_id<>0 AND p.permission_name IN (N'INSERT',N'UPDATE',N'DELETE',N'ALTER');") != 0)
            Fail("C04A_METADATA_DRIFT");
        var controls = await session.ReadAsync("SELECT Singleton,RunId,Generation,State FROM crm_source_control.Control;", row => (Singleton: row.GetBoolean(0), Run: row.GetGuid(1), Generation: row.GetInt64(2), State: row.GetString(3)));
        var events = await session.ReadAsync("SELECT Generation,RunId,Command,PreviousState,NextState,ExpectedGeneration FROM crm_source_control.Audit ORDER BY Generation;", row => new AuditEvent(row.GetInt64(0), row.GetGuid(1), row.GetString(2), row.GetString(3), row.GetString(4), row.GetInt64(5)));
        if (controls.Count != 1 || !controls[0].Singleton || events.Count == 0) Fail("C04A_METADATA_DRIFT");
        var state = "Uninitialized"; Guid? run = null; long generation = 0;
        var usedRuns = new HashSet<Guid>();
        foreach (var entry in events)
        {
            if (entry.RunId == Guid.Empty || entry.Generation != generation + 1 || entry.ExpectedGeneration != generation || entry.PreviousState != state)
                Fail("C04A_METADATA_DRIFT");
            var valid = entry.Command switch
            {
                "Freeze" => state is "Uninitialized" or "Reopened" && entry.NextState == "Frozen" && usedRuns.Add(entry.RunId),
                "Reopen" => state == "Frozen" && entry.NextState == "Reopened" && run == entry.RunId,
                "SealForwardOnly" => state == "Frozen" && entry.NextState == "ForwardOnly" && run == entry.RunId,
                _ => false
            };
            if (!valid) Fail("C04A_METADATA_DRIFT");
            state = entry.NextState; run = entry.RunId; generation = entry.Generation;
        }
        if (controls[0].Run != run || controls[0].Generation != generation || controls[0].State != state) Fail("C04A_METADATA_DRIFT");
        return new(state, run, generation, events);
    }

    private static void Fail(string code) => throw new SourceFenceException(code);

    private sealed record Trigger(string Name, bool Disabled, bool NotForReplication, bool InsteadOf,
        string Type, string? Definition, bool AnsiNulls, bool QuotedIdentifier, bool ExecuteAs)
    {
        internal bool Matches(string name, string definition) => Name == name && !Disabled && !NotForReplication && !InsteadOf &&
            Type.Trim() == "TR" && Definition == definition && AnsiNulls && QuotedIdentifier && !ExecuteAs;
    }
    private sealed record AuditEvent(long Generation, Guid RunId, string Command, string PreviousState, string NextState, long ExpectedGeneration);
    private sealed record Metadata(string State, Guid? RunId, long Generation, List<AuditEvent> Events)
    {
        internal AuditEvent? Last => Events.LastOrDefault();
    }

    private sealed class Session(SqlConnection connection, SqlTransaction transaction, int timeout, CancellationToken token)
    {
        private SqlCommand Command(string sql, (string Name, object Value)[] parameters)
        {
            var command = new SqlCommand(sql, connection, transaction) { CommandTimeout = timeout };
            foreach (var parameter in parameters) command.Parameters.AddWithValue(parameter.Name, parameter.Value);
            return command;
        }
        internal async Task ExecuteAsync(string sql, params (string Name, object Value)[] parameters)
        {
            using var command = Command(sql, parameters);
            await command.ExecuteNonQueryAsync(token);
        }
        internal async Task<T> ScalarAsync<T>(string sql, params (string Name, object Value)[] parameters)
        {
            using var command = Command(sql, parameters);
            return (T)(await command.ExecuteScalarAsync(token))!;
        }
        internal async Task<List<T>> ReadAsync<T>(string sql, Func<SqlDataReader, T> map, params (string Name, object Value)[] parameters)
        {
            using var command = Command(sql, parameters);
            using var reader = await command.ExecuteReaderAsync(token);
            var result = new List<T>();
            while (await reader.ReadAsync(token)) result.Add(map(reader));
            return result;
        }
    }
}
