using System.Text;
using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Sys;
using CP6.Core.Services.Wf;
using CP6.Entity.DomainModels.Wf;
using Microsoft.EntityFrameworkCore;
using static CP6.Core.Services.Oa.ApprovalSubmissionWireV2;

namespace CP6.Core.Services.Oa;

public sealed record ApprovalStartRunV2(string State, Guid? SubmissionId = null, Guid? InstanceId = null);

/// <summary>TxE: native engine graph, exact start completion and audit in one physical transaction.
/// Immutable intake/command acknowledgements remain historical; current execution is queried separately.</summary>
public sealed class ApprovalStartOwnerV2(CP6Context db, ScopeAuthorizationFenceV1 scope)
{
    private sealed record Pending(Guid Id, string Owner, byte[] Payload, string Digest, Guid Head, Guid Version, string Ack);
    private sealed record Source(string Snapshot);
    private async Task Tenant(CancellationToken ct)
    {
        if (!await db.Sys_Tenants.AsNoTracking().AnyAsync(t => t.Id == db.CurrentTenantId && t.Enable
            && (t.ExpireDate == null || t.ExpireDate > DateTime.Now), ct)) throw Error("TENANT_UNAVAILABLE", 403);
    }
    internal static async Task Ready(CP6Context db, CancellationToken ct)
    {
        if (!await db.Database.SqlQueryRaw<bool>("SELECT cp6_approval_start_v2.ready() AS \"Value\"").SingleAsync(ct))
            throw Error("APP_START_STORE_UNAVAILABLE", 503);
    }
    internal static Task<string?> Current(CP6Context db, Guid environment, string owner, Guid submission, CancellationToken ct)
        => db.Database.SqlQuery<string>($"""
            SELECT result_json AS "Value" FROM cp6_approval_start_v2.starts
            WHERE tenant_id={db.CurrentTenantId} AND environment_id={environment} AND owner={owner} AND submission_id={submission}
            """).SingleOrDefaultAsync(ct);

    public async Task<ApprovalStartRunV2> RunAsync(Guid tenant, CancellationToken ct = default)
    {
        if (!db.Database.IsNpgsql() || db.Database.CurrentTransaction != null || db.ChangeTracker.HasChanges()
            || System.Transactions.Transaction.Current != null) throw Error("APP_START_TRANSACTION_REQUIRED", 503);
        if (tenant == Guid.Empty || tenant != db.CurrentTenantId) throw Error("APP_START_SCOPE_MISMATCH", 403);
        await using var tx = await db.Database.BeginTransactionAsync(ct); var committing = false;
        try
        {
            var lease = await scope.AcquireAsync(ct); await Ready(db, ct);
            await Tenant(ct);
            // All selection is scoped. Concurrent connections serialize on the native Scope and original row.
            var pending = await db.Database.SqlQuery<Pending>($"""
                SELECT s.id AS "Id",s.owner AS "Owner",s.payload AS "Payload",s.payload_digest AS "Digest",
                  s.flow_head AS "Head",s.flow_version AS "Version",s.ack_json AS "Ack"
                FROM cp6_approval_intake_v2.start_outbox o JOIN cp6_approval_intake_v2.submissions s
                  ON s.tenant_id=o.tenant_id AND s.environment_id=o.environment_id AND s.owner=o.owner AND s.id=o.submission_id
                WHERE o.tenant_id={tenant} AND o.environment_id={scope.EnvironmentId}
                  AND NOT EXISTS(SELECT FROM cp6_approval_start_v2.starts d WHERE d.tenant_id=o.tenant_id
                    AND d.environment_id=o.environment_id AND d.owner=o.owner AND d.submission_id=o.submission_id)
                ORDER BY s.received_at,s.id LIMIT 1 FOR UPDATE OF o SKIP LOCKED
                """).SingleOrDefaultAsync(ct);
            if (pending == null) return new("IDLE");
            var parsed = Parse(pending.Payload); var e = parsed.Value;
            if (parsed.Full != pending.Digest || !parsed.Canonical.AsSpan().SequenceEqual(pending.Payload)
                || e.SubmissionId != pending.Id || e.Subject.BusinessOwner != pending.Owner || e.Scope.TenantId != tenant
                || e.Scope.EnvironmentId != scope.EnvironmentId.ToString()) throw Error("APP_START_ORIGINAL_INVALID", 503);
            var source = await db.Database.SqlQuery<Source>($"""
                SELECT snapshot_json AS "Snapshot" FROM cp6_workflow_authoring_v2.versions
                WHERE tenant_id={tenant} AND environment_id={scope.EnvironmentId} AND head_id={pending.Head} AND id={pending.Version}
                """).SingleAsync(ct);
            var publication = JsonSerializer.Deserialize<FlowPublicationV2>(source.Snapshot, ApprovalBindingOwnerV2.Json)
                ?? throw Error("FLOW_VERSION_UNAVAILABLE", 503);
            if (publication.DefinitionId != pending.Head || publication.VersionId != pending.Version
                || !publication.Fields.TryGetValue("SchemaJson", out var schema) || schema == null
                || publication.SchemaDigest != ApprovalCanonicalV2.Hash(Encoding.UTF8.GetBytes(schema)))
                throw Error("FLOW_VERSION_UNAVAILABLE", 503);
            var instanceId = FlowEngine.ApprovalInstanceId(e);
            if (await db.Wf_FlowInstances.IgnoreQueryFilters().AsNoTracking().AnyAsync(x => x.Id == instanceId, ct)
                || await db.Wf_FlowTasks.IgnoreQueryFilters().AsNoTracking().AnyAsync(x => x.InstanceId == instanceId, ct)
                || await db.Wf_FlowTokens.IgnoreQueryFilters().AsNoTracking().AnyAsync(x => x.InstanceId == instanceId, ct)
                || await db.Wf_FlowHistories.IgnoreQueryFilters().AsNoTracking().AnyAsync(x => x.InstanceId == instanceId, ct)
                || await db.Wf_FlowFormTos.IgnoreQueryFilters().AsNoTracking().AnyAsync(x => x.InstanceId == instanceId, ct)
                || await db.Wf_FlowDatas.IgnoreQueryFilters().AsNoTracking().AnyAsync(x => x.InstanceId == instanceId, ct)
                || await db.Wf_FlowCcs.IgnoreQueryFilters().AsNoTracking().AnyAsync(x => x.InstanceId == instanceId, ct)
                || await db.Wf_ServiceJobs.IgnoreQueryFilters().AsNoTracking().AnyAsync(x => x.InstanceId == instanceId, ct))
                throw Error("APP_ENGINE_ID_CONFLICT", 409); // Never adopt unrelated/orphan runtime rows.
            // Exact accepted pin: later disable/publication does not select a different definition.
            var engine = new FlowEngine(db, new ApproverResolver(db, exactConditions: true));
            var instance = await engine.StageApprovalStartV2(publication, e,
                Encoding.UTF8.GetString(Convert.FromBase64String(e.Snapshot.CanonicalBytesBase64)), ct);
            await db.SaveChangesAsync(ct); // Participates in this outer native transaction; never a separate commit.
            var original = JsonSerializer.Deserialize<ApprovalSubmissionAckV2>(pending.Ack, ApprovalBindingOwnerV2.Json)!;
            var state = instance.Status == FlowInstanceStatus.Suspended ? "SUSPENDED" : "RUNNING";
            var result = original with { InstanceId = instance.Id, State = state, RowVersion = ApprovalBindingTimelineBuilderV2.Version(2) };
            var resultJson = ApprovalBindingOwnerV2.Serialize(result);
            var audit = ApprovalBindingOwnerV2.Serialize(new { engineStartId = e.SubmissionId, instance.Id, pending.Owner,
                pending.Digest, pending.Head, pending.Version, publication.SchemaDigest, state,
                originalStarter = e.StarterId, scope = lease.Snapshot, profile = "NATIVE_TXE_V2" });
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO cp6_approval_start_v2.starts(tenant_id,environment_id,owner,submission_id,engine_start_id,instance_id,
                  flow_head,flow_version,state,result_json,audit_json)
                VALUES({tenant},{scope.EnvironmentId},{pending.Owner},{e.SubmissionId},{e.SubmissionId},{instance.Id},
                  {pending.Head},{pending.Version},{state},{resultJson},{audit})
                """, ct);
            await scope.ValidateAsync(lease, ct); await Ready(db, ct); await Tenant(ct); ct.ThrowIfCancellationRequested();
            committing = true; await tx.CommitAsync(ct); return new(state, e.SubmissionId, instance.Id);
        }
        catch (Exception error)
        {
            try { await tx.RollbackAsync(CancellationToken.None); }
            catch (Exception rollback) { throw new ScopeAuthorizationExceptionV1("START_UNKNOWN", 504, new AggregateException(error, rollback)); }
            finally { db.ChangeTracker.Clear(); }
            if (committing) throw new ScopeAuthorizationExceptionV1("START_UNKNOWN", 504, error);
            throw;
        }
    }
}
