using System.Data;
using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Options;

namespace CP6.Core.Services.Sys;

public sealed class ScopeAuthorizationOptionsV1
{
    // Trusted deployment input only. Empty is deliberately unavailable, never a generated fallback.
    public Guid EnvironmentId { get; set; }
}
public sealed class ScopeAuthorizationExceptionV1(string code, int status = 503, Exception? inner = null)
    : InvalidOperationException(code, inner)
{
    public int Status { get; } = status;
}
public sealed record ScopeAuthorizationSnapshotV1(Guid TenantId, Guid EnvironmentId, string LogicalDomain,
    Guid PermissionDomain, bool Enabled, long Epoch, Guid RowVersion, string WriterProfile);

/// <summary>A coordination proof, not an authorization decision. The consumer must check current
/// actor, resource, fields, SOD and effective grants inside this same transaction and save their evidence.</summary>
public sealed class ScopeAuthorizationLeaseV1
{
    internal ScopeAuthorizationLeaseV1(CP6Context db, Guid transaction, ScopeAuthorizationSnapshotV1 snapshot)
    { Database = db; Transaction = transaction; Snapshot = snapshot; }
    internal CP6Context Database { get; }
    internal Guid Transaction { get; }
    public ScopeAuthorizationSnapshotV1 Snapshot { get; }
}

/// <summary>Native authentication root → actual Scope fence → command/business rows.
/// This provider does not commit, start nested transactions, or turn native IAM coverage into full WF_AUTH adoption.</summary>
public sealed class ScopeAuthorizationFenceV1(CP6Context db, IOptions<ScopeAuthorizationOptionsV1> options)
{
    public const string WorkflowDomain = "WF_AUTH", NativeWriterProfile = "MAIN_NATIVE_IAM_WF_V1";
    internal Guid EnvironmentId => options.Value.EnvironmentId is var id && id != Guid.Empty
        ? id : throw new ScopeAuthorizationExceptionV1("SCOPE_ENVIRONMENT_UNAVAILABLE");
    internal void RequireTransaction()
    {
        if (!db.Database.IsNpgsql() || db.CurrentTenantId == Guid.Empty
            || db.Database.CurrentTransaction?.GetDbTransaction() is not Npgsql.NpgsqlTransaction tx
            || !ReferenceEquals(tx.Connection, db.Database.GetDbConnection()) || tx.IsolationLevel != IsolationLevel.ReadCommitted
            || System.Transactions.Transaction.Current != null)
            throw new ScopeAuthorizationExceptionV1("SCOPE_TRANSACTION_REQUIRED");
    }
    internal async Task Root(CancellationToken ct)
    {
        RequireTransaction(); _ = EnvironmentId;
        await AuthenticationCompletionGateV1.AcquireAsync(db, ct);
        if (!await db.Database.SqlQueryRaw<bool>("SELECT cp6_scope_authorization_v1.writers_ready() AS \"Value\"").SingleAsync(ct))
            throw new ScopeAuthorizationExceptionV1("SCOPE_WRITER_COVERAGE_UNAVAILABLE");
        var bound = await db.Database.SqlQueryRaw<Guid?>("SELECT environment_id AS \"Value\" FROM cp6_scope_authorization_v1.environment WHERE singleton").SingleOrDefaultAsync(ct);
        if (bound != null && bound != EnvironmentId) throw new ScopeAuthorizationExceptionV1("SCOPE_ENVIRONMENT_MISMATCH");
    }
    internal async Task<ScopeAuthorizationSnapshotV1?> ReadLocked(CancellationToken ct)
    {
        // Root held before every call, including registration when no row exists yet.
        var rows = await db.Database.SqlQuery<ScopeAuthorizationSnapshotV1>($"""
            SELECT tenant_id AS "TenantId",environment_id AS "EnvironmentId",logical_domain AS "LogicalDomain",
              permission_domain AS "PermissionDomain",enabled AS "Enabled",epoch AS "Epoch",row_version AS "RowVersion",
              writer_profile AS "WriterProfile" FROM cp6_scope_authorization_v1.fences
            WHERE tenant_id={db.CurrentTenantId} AND environment_id={EnvironmentId} AND logical_domain='WF_AUTH' FOR UPDATE
            """).ToListAsync(ct);
        return rows.SingleOrDefault();
    }
    public async Task<ScopeAuthorizationLeaseV1> AcquireAsync(CancellationToken ct = default)
    {
        await Root(ct);
        var snapshot = await ReadLocked(ct) ?? throw new ScopeAuthorizationExceptionV1("SCOPE_MAPPING_REQUIRED");
        if (!snapshot.Enabled || snapshot.WriterProfile != NativeWriterProfile)
            throw new ScopeAuthorizationExceptionV1("SCOPE_MAPPING_DISABLED");
        return new(db, db.Database.CurrentTransaction!.TransactionId, snapshot);
    }
    public async Task ValidateAsync(ScopeAuthorizationLeaseV1 lease, CancellationToken ct = default)
    {
        RequireTransaction();
        if (!ReferenceEquals(lease.Database, db) || lease.Transaction != db.Database.CurrentTransaction!.TransactionId
            || lease.Snapshot.TenantId != db.CurrentTenantId || lease.Snapshot.EnvironmentId != EnvironmentId)
            throw new ScopeAuthorizationExceptionV1("SCOPE_TRANSACTION_MISMATCH");
        await Root(ct);
        if (await ReadLocked(ct) != lease.Snapshot)
            throw new ScopeAuthorizationExceptionV1("SCOPE_AUTHORITY_CHANGED", 409);
    }
}
