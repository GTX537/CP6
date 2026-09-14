using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Data.SqlClient;

namespace CP6.Crm.Cutover;

// Byte-identical protocol source is compiled independently by Core and CRM.
// Runtime coordination crosses the SQL/CLI boundary, never an in-process service reference.
public sealed record TargetRollbackAnchor(Guid OrganizationId, string TargetBindingSha256)
{
    public string Format => "CP6.CRM.TargetRollbackAnchor.v1";
    public string Digest() => TargetRollbackProtocol.Digest(this);
}

public sealed record TargetRollbackPermit(Guid RunId, Guid OrganizationId, string TargetBindingSha256,
    IReadOnlyList<string> SourceFreezeRequestSha256s, string ApprovalEvidenceSha256)
{
    public string Format => "CP6.CRM.TargetRollbackPermit.v1";
    public string Digest() => TargetRollbackProtocol.Digest(this);
}

public sealed record TargetRollbackIdentity(string ServerName, string MachineName, string DatabaseName,
    Guid BrokerGuid, Guid DatabaseGuid, Guid FamilyGuid, Guid RecoveryForkGuid);

public sealed record TargetRollbackReceipt(TargetRollbackPermit Permit, string PermitSha256,
    TargetRollbackIdentity Target, long ClosedGeneration, string ProtectionSha256, DateTimeOffset RecordedAtUtc)
{
    public string Format => "CP6.CRM.TargetRollbackReceipt.v1";
    public string State => "Aborted";
    public TargetRollbackAnchor Anchor => new(Permit.OrganizationId, Permit.TargetBindingSha256);
    public string Digest() => TargetRollbackProtocol.Digest(this);
}

public sealed class TargetRollbackException(string code) : Exception(code)
{
    public string Code { get; } = code;
}

/// <summary>Live SQL proof of a terminal CRM target, independent of either service's ORM.</summary>
public static class TargetRollbackProtocol
{
    public const string Marker = "CP6.CRM.TargetRollback.v1";
    public const string ControlTriggerName = "TargetRollback_ClosedForever";
    public const string ControlTriggerSql = "CREATE TRIGGER [crm_cutover].[TargetRollback_ClosedForever] ON [crm_cutover].[Control] AFTER INSERT,UPDATE,DELETE AS BEGIN SET NOCOUNT ON; THROW 51050, 'CRM_TARGET_ROLLBACK_TERMINAL', 1; END;";
    private static readonly JsonSerializerOptions Strict = new() { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, MaxDepth = 24 };

    public static string Digest<T>(T value) => Hash(JsonSerializer.Serialize(value));
    public static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    public static bool IsHash(string? value) => value is { Length: 64 } && value.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
    public static void ValidatePermit(TargetRollbackPermit permit, string expected)
    {
        if (permit is null || permit.RunId == Guid.Empty || permit.OrganizationId == Guid.Empty || !IsHash(permit.TargetBindingSha256)
            || !IsHash(permit.ApprovalEvidenceSha256) || !IsHash(expected) || permit.SourceFreezeRequestSha256s is not { Count: > 0 and <= 16 }
            || permit.SourceFreezeRequestSha256s.Any(s => !IsHash(s))
            || !permit.SourceFreezeRequestSha256s.SequenceEqual(permit.SourceFreezeRequestSha256s.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))
            || permit.Digest() != expected) Fail("C04A_TARGET_ROLLBACK_PERMIT_MISMATCH");
    }

    public static async Task<SqlConnection> OpenLocalAsync(string value, CancellationToken token = default)
    {
        SqlConnection? connection = null;
        try
        {
            var builder = new SqlConnectionStringBuilder(value);
            var server = builder.DataSource;
            if (server.StartsWith("tcp:", StringComparison.OrdinalIgnoreCase) || server.StartsWith("lpc:", StringComparison.OrdinalIgnoreCase)) server = server[4..];
            server = server.Split('\\')[0].Split(',')[0];
            if (string.IsNullOrWhiteSpace(builder.InitialCatalog) || new[] { "master", "model", "msdb", "tempdb" }.Contains(builder.InitialCatalog, StringComparer.OrdinalIgnoreCase)
                || builder.AttachDBFilename.Length != 0 || builder.FailoverPartner.Length != 0 || builder.UserInstance || builder.ApplicationIntent != ApplicationIntent.ReadWrite
                || server != "." && server != "(local)" && server != "127.0.0.1" && server != "::1"
                    && !server.Equals("localhost", StringComparison.OrdinalIgnoreCase) && !server.Equals(Environment.MachineName, StringComparison.OrdinalIgnoreCase))
                Fail("C04A_TARGET_ROLLBACK_LOCAL_IDENTITY");
            builder.Enlist = false; builder.Pooling = false; builder.ApplicationName = "CP6.C04A.TargetRollbackVerifier";
            connection = new SqlConnection(builder.ConnectionString);
            await connection.OpenAsync(token);
            return connection;
        }
        catch (Exception error)
        {
            if (connection is not null) await connection.DisposeAsync();
            throw Sanitize(error, token);
        }
    }

    public static async Task<TargetRollbackReceipt> ReadAsync(string connectionString, string expectedReceiptSha256, CancellationToken token = default)
    {
        try
        {
            await using var connection = await OpenLocalAsync(connectionString, token);
            await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable, token);
            var receipt = await ReadWithinTransactionAsync(connection, transaction, expectedReceiptSha256, token);
            await transaction.CommitAsync(token);
            return receipt;
        }
        catch (Exception error) { throw Sanitize(error, token); }
    }

    // The caller retains these target locks until the source recovery has committed.
    public static async Task<TargetRollbackReceipt> ReadWithinTransactionAsync(SqlConnection connection, SqlTransaction transaction,
        string expectedReceiptSha256, CancellationToken token = default)
    {
        try
        {
            if (!IsHash(expectedReceiptSha256) || transaction.IsolationLevel != IsolationLevel.Serializable || !ReferenceEquals(transaction.Connection, connection))
                Fail("C04A_TARGET_ROLLBACK_TRANSACTION_REQUIRED");
            var session = new Session(connection, transaction, token);
            await session.ExecuteAsync("SET XACT_ABORT ON; SET LOCK_TIMEOUT 5000; SET ANSI_NULLS ON; SET QUOTED_IDENTIFIER ON;");
            var locked = await session.ScalarAsync<int>("DECLARE @r int; EXEC @r=sys.sp_getapplock @Resource=N'CP6.CRM.TargetWriteGate.v1',@LockMode=N'Shared',@LockOwner=N'Transaction',@LockTimeout=5000; SELECT @r;");
            if (locked < 0) Fail("C04A_TARGET_ROLLBACK_LOCK_TIMEOUT");
            var identity = await ReadIdentityAsync(connection, transaction, token);
            var serialized = await ReadStoredAsync(connection, transaction, token);
            if (serialized is null) Fail("C04A_TARGET_ROLLBACK_MISSING");
            var receipt = ParseReceipt(serialized!);
            if (receipt.Digest() != expectedReceiptSha256) Fail("C04A_TARGET_ROLLBACK_RECEIPT_MISMATCH");
            if (receipt.Target != identity) Fail("C04A_TARGET_ROLLBACK_IDENTITY_CHANGED");
            await LockTablesAsync(session);
            await VerifyTerminalAsync(session, receipt);
            var protection = await CaptureProtectionAsync(connection, transaction, false, token);
            if (protection != receipt.ProtectionSha256 || identity != await ReadIdentityAsync(connection, transaction, token)
                || serialized != await ReadStoredAsync(connection, transaction, token)) Fail("C04A_TARGET_ROLLBACK_PROTECTION_CHANGED");
            return receipt;
        }
        catch (Exception error) { throw Sanitize(error, token); }
    }

    public static async Task<TargetRollbackIdentity> ReadIdentityAsync(SqlConnection connection, SqlTransaction transaction, CancellationToken token)
    {
        var session = new Session(connection, transaction, token);
        if (await session.ScalarAsync<int>("SELECT CASE WHEN IS_SRVROLEMEMBER(N'sysadmin')=1 AND SUSER_SNAME()=ORIGINAL_LOGIN() THEN 1 ELSE 0 END;") != 1)
            Fail("C04A_TARGET_ROLLBACK_VISIBILITY");
        var rows = await session.RowsAsync("SELECT CAST(SERVERPROPERTY('ServerName') AS nvarchar(128)),CAST(SERVERPROPERTY('MachineName') AS nvarchar(128)),d.name,d.service_broker_guid,r.database_guid,r.family_guid,r.recovery_fork_guid,d.database_id,CAST(SERVERPROPERTY('IsClustered') AS int) FROM sys.databases d JOIN sys.database_recovery_status r ON r.database_id=d.database_id WHERE d.database_id=DB_ID();",
            r => (Identity: new TargetRollbackIdentity(r.GetString(0), r.GetString(1), r.GetString(2), r.GetGuid(3), r.GetGuid(4), r.GetGuid(5), r.GetGuid(6)), Id: r.GetInt32(7), Clustered: r.GetInt32(8)));
        if (rows.Count != 1 || rows[0].Id <= 4 || rows[0].Clustered != 0 || !rows[0].Identity.MachineName.Equals(Environment.MachineName, StringComparison.OrdinalIgnoreCase))
            Fail("C04A_TARGET_ROLLBACK_LOCAL_IDENTITY");
        return rows[0].Identity;
    }

    public static async Task<string?> ReadStoredAsync(SqlConnection connection, SqlTransaction transaction, CancellationToken token)
    {
        using var command = new SqlCommand("SELECT CONVERT(nvarchar(3750),value) FROM sys.extended_properties WHERE class=0 AND name=@marker;", connection, transaction) { CommandTimeout = 30 };
        command.Parameters.AddWithValue("@marker", Marker);
        return await command.ExecuteScalarAsync(token) as string;
    }

    public static TargetRollbackReceipt ParseReceipt(string serialized)
    {
        try
        {
            var receipt = JsonSerializer.Deserialize<TargetRollbackReceipt>(serialized, Strict);
            if (receipt is null || receipt.Permit is null || receipt.Target is null || serialized != JsonSerializer.Serialize(receipt) || receipt.ClosedGeneration < 0
                || !IsHash(receipt.ProtectionSha256) || receipt.RecordedAtUtc.Offset != TimeSpan.Zero)
                Fail("C04A_TARGET_ROLLBACK_RECEIPT_MISMATCH");
            ValidatePermit(receipt!.Permit, receipt.PermitSha256);
            return receipt;
        }
        catch (JsonException) { throw new TargetRollbackException("C04A_TARGET_ROLLBACK_RECEIPT_MISMATCH"); }
    }

    public static async Task<string> CaptureProtectionAsync(SqlConnection connection, SqlTransaction transaction, bool excludeTerminalTrigger, CancellationToken token)
    {
        var session = new Session(connection, transaction, token);
        var sections = new List<string>();
        foreach (var sql in SnapshotQueries)
            sections.Add(await session.JsonAsync(sql, ("@excludeTerminal", excludeTerminalTrigger)));
        return Digest(sections);
    }

    private static async Task LockTablesAsync(Session session)
    {
        var tables = await session.RowsAsync("SELECT s.name,t.name FROM sys.tables t JOIN sys.schemas s ON s.schema_id=t.schema_id WHERE s.name IN ('crm','crm_v1','crm_messages','crm_cutover') ORDER BY s.name,t.name;", r => (Schema: r.GetString(0), Table: r.GetString(1)));
        if (tables.Count < 44) Fail("C04A_TARGET_ROLLBACK_PROTECTION_CHANGED");
        // Table S locks block ordinary DML and incompatible DDL until source commit.
        foreach (var table in tables)
            _ = await session.ScalarAsync<long>($"SELECT COUNT_BIG(*) FROM {Quote(table.Schema)}.{Quote(table.Table)} WITH (TABLOCK,HOLDLOCK);");
    }

    private static async Task VerifyTerminalAsync(Session session, TargetRollbackReceipt receipt)
    {
        var controls = await session.RowsAsync("SELECT OrganizationId,TargetBindingSha256,State,Generation,FirstWriteAtUtc,FirstWriteTable FROM crm_cutover.Control WITH (TABLOCK,HOLDLOCK);",
            r => (Organization: r.GetGuid(0), Binding: r.GetString(1), State: r.GetString(2), Generation: r.GetInt64(3), Written: !r.IsDBNull(4) || !r.IsDBNull(5)));
        if (controls.Count != 1 || controls[0] != (receipt.Permit.OrganizationId, receipt.Permit.TargetBindingSha256, "Closed", receipt.ClosedGeneration, false))
            Fail("C04A_TARGET_ROLLBACK_NOT_CLOSED");
        if (await session.ScalarAsync<int>("SELECT COUNT(*) FROM crm_cutover.Audit WITH (TABLOCK,HOLDLOCK) WHERE Event='FirstWrite' OR PreviousState='Written' OR NextState='Written' OR FirstWriteTable IS NOT NULL;") != 0)
            Fail("C04A_TARGET_ROLLBACK_ALREADY_WRITTEN");
        if (await session.ScalarAsync<int>("""
            SELECT COUNT(*) FROM sys.triggers t JOIN sys.sql_modules m ON m.object_id=t.object_id
            WHERE t.object_id=OBJECT_ID(N'crm_cutover.TargetRollback_ClosedForever') AND t.parent_id=OBJECT_ID(N'crm_cutover.Control')
              AND t.is_disabled=0 AND t.is_not_for_replication=0 AND t.is_instead_of_trigger=0 AND m.definition=@definition
              AND m.uses_ansi_nulls=1 AND m.uses_quoted_identifier=1 AND m.is_schema_bound=0 AND m.execute_as_principal_id IS NULL
              AND (SELECT COUNT(*) FROM sys.trigger_events e WHERE e.object_id=t.object_id)=3
              AND (SELECT COUNT(*) FROM sys.trigger_events e WHERE e.object_id=t.object_id AND e.type_desc IN ('INSERT','UPDATE','DELETE') AND e.is_first=0 AND e.is_last=0)=3;
            """, ("@definition", ControlTriggerSql)) != 1) Fail("C04A_TARGET_ROLLBACK_PROTECTION_CHANGED");
        if (await session.ScalarAsync<int>("SELECT COUNT(*) FROM sys.database_permissions WHERE class=3 AND major_id=SCHEMA_ID(N'crm_cutover') AND grantee_principal_id=DATABASE_PRINCIPAL_ID(N'public') AND grantor_principal_id=1 AND state='D' AND permission_name IN ('INSERT','UPDATE','DELETE','ALTER');") != 4)
            Fail("C04A_TARGET_ROLLBACK_PROTECTION_CHANGED");
    }

    // Whole owned CRM schemas and their grants are fingerprinted; other schemas' data is not.
    private static readonly string[] SnapshotQueries =
    [
        "SELECT * FROM crm_cutover.Control ORDER BY Singleton FOR JSON PATH,INCLUDE_NULL_VALUES;",
        "SELECT * FROM crm_cutover.Audit ORDER BY Generation FOR JSON PATH,INCLUDE_NULL_VALUES;",
        "SELECT s.schema_id,s.name,s.principal_id FROM sys.schemas s WHERE s.name IN ('crm','crm_v1','crm_messages','crm_cutover') ORDER BY s.schema_id FOR JSON PATH,INCLUDE_NULL_VALUES;",
        "SELECT t.object_id,t.schema_id,t.name,t.principal_id,t.is_memory_optimized,t.temporal_type,t.is_filetable,c.column_id,c.name AS column_name,c.system_type_id,c.user_type_id,c.max_length,c.precision,c.scale,c.collation_name,c.is_nullable,c.is_identity,c.is_computed,c.default_object_id,c.generated_always_type,c.encryption_type FROM sys.tables t JOIN sys.columns c ON c.object_id=t.object_id WHERE SCHEMA_NAME(t.schema_id) IN ('crm','crm_v1','crm_messages','crm_cutover') ORDER BY t.object_id,c.column_id FOR JSON PATH,INCLUDE_NULL_VALUES;",
        "SELECT o.object_id,o.parent_object_id,o.name,o.type,dc.definition AS default_definition,cc.definition AS check_definition,cc.is_disabled,cc.is_not_trusted FROM sys.objects o LEFT JOIN sys.default_constraints dc ON dc.object_id=o.object_id LEFT JOIN sys.check_constraints cc ON cc.object_id=o.object_id WHERE SCHEMA_NAME(o.schema_id) IN ('crm','crm_v1','crm_messages','crm_cutover') AND o.type IN ('D','C','PK','UQ','F') ORDER BY o.object_id FOR JSON PATH,INCLUDE_NULL_VALUES;",
        "SELECT i.object_id,i.index_id,i.name,i.type,i.is_unique,i.is_disabled,i.filter_definition,ic.column_id,ic.key_ordinal,ic.is_descending_key,ic.is_included_column FROM sys.indexes i JOIN sys.tables t ON t.object_id=i.object_id LEFT JOIN sys.index_columns ic ON ic.object_id=i.object_id AND ic.index_id=i.index_id WHERE SCHEMA_NAME(t.schema_id) IN ('crm','crm_v1','crm_messages','crm_cutover') ORDER BY i.object_id,i.index_id,ic.index_column_id FOR JSON PATH,INCLUDE_NULL_VALUES;",
        "SELECT f.object_id,f.parent_object_id,f.referenced_object_id,f.is_disabled,f.is_not_trusted,f.delete_referential_action,f.update_referential_action,c.constraint_column_id,c.parent_column_id,c.referenced_column_id FROM sys.foreign_keys f JOIN sys.foreign_key_columns c ON c.constraint_object_id=f.object_id WHERE OBJECT_SCHEMA_NAME(f.parent_object_id) IN ('crm','crm_v1','crm_messages','crm_cutover') OR OBJECT_SCHEMA_NAME(f.referenced_object_id) IN ('crm','crm_v1','crm_messages','crm_cutover') ORDER BY f.object_id,c.constraint_column_id FOR JSON PATH,INCLUDE_NULL_VALUES;",
        "SELECT t.object_id,t.parent_id,t.name,t.type,t.is_disabled,t.is_not_for_replication,t.is_instead_of_trigger,m.definition,m.uses_ansi_nulls,m.uses_quoted_identifier,m.is_schema_bound,m.execute_as_principal_id,e.type_desc,e.is_first,e.is_last FROM sys.triggers t LEFT JOIN sys.sql_modules m ON m.object_id=t.object_id LEFT JOIN sys.trigger_events e ON e.object_id=t.object_id WHERE OBJECT_SCHEMA_NAME(t.parent_id) IN ('crm','crm_v1','crm_messages','crm_cutover') AND NOT (@excludeTerminal=1 AND t.object_id=OBJECT_ID(N'crm_cutover.TargetRollback_ClosedForever')) ORDER BY t.object_id,e.type FOR JSON PATH,INCLUDE_NULL_VALUES;",
        "SELECT * FROM sys.database_permissions WHERE class=3 AND SCHEMA_NAME(major_id) IN ('crm','crm_v1','crm_messages','crm_cutover') OR class=1 AND OBJECT_SCHEMA_NAME(major_id) IN ('crm','crm_v1','crm_messages','crm_cutover') ORDER BY class,major_id,minor_id,grantee_principal_id,grantor_principal_id,type FOR JSON PATH,INCLUDE_NULL_VALUES;",
        "SELECT principal_id,name,type,sid,owning_principal_id,authentication_type FROM sys.database_principals ORDER BY principal_id FOR JSON PATH,INCLUDE_NULL_VALUES;",
        "SELECT * FROM sys.database_role_members ORDER BY role_principal_id,member_principal_id FOR JSON PATH,INCLUDE_NULL_VALUES;"
    ];

    private static string Quote(string name) => "[" + name.Replace("]", "]]", StringComparison.Ordinal) + "]";
    [System.Diagnostics.CodeAnalysis.DoesNotReturn]
    private static void Fail(string code) => throw new TargetRollbackException(code);
    public static TargetRollbackException Sanitize(Exception error, CancellationToken token) => error as TargetRollbackException
        ?? new(token.IsCancellationRequested || error is OperationCanceledException ? "C04A_TARGET_ROLLBACK_CANCELLED"
            : error is SqlException { Number: -2 or 1222 or 1205 } ? "C04A_TARGET_ROLLBACK_LOCK_TIMEOUT" : "C04A_TARGET_ROLLBACK_STORAGE_FAILED");

    private sealed class Session(SqlConnection connection, SqlTransaction transaction, CancellationToken token)
    {
        private SqlCommand Command(string sql, (string Name, object Value)[] parameters)
        {
            var command = new SqlCommand(sql, connection, transaction) { CommandTimeout = 30 };
            foreach (var (name, value) in parameters) command.Parameters.AddWithValue(name, value);
            return command;
        }
        internal async Task ExecuteAsync(string sql)
        { using var command = Command(sql, []); await command.ExecuteNonQueryAsync(token); }
        internal async Task<T> ScalarAsync<T>(string sql, params (string Name, object Value)[] parameters)
        { using var command = Command(sql, parameters); return (T)(await command.ExecuteScalarAsync(token))!; }
        internal async Task<List<T>> RowsAsync<T>(string sql, Func<SqlDataReader,T> map)
        {
            using var command = Command(sql, []); using var reader = await command.ExecuteReaderAsync(token); var rows = new List<T>();
            while (await reader.ReadAsync(token)) rows.Add(map(reader)); return rows;
        }
        internal async Task<string> JsonAsync(string sql, params (string Name, object Value)[] parameters)
        {
            using var command = Command(sql, parameters); using var reader = await command.ExecuteReaderAsync(token); var json = new StringBuilder();
            while (await reader.ReadAsync(token)) json.Append(reader.GetString(0));
            return json.ToString();
        }
    }
}
