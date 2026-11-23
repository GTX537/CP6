using System.Security.Cryptography;
using System.Text.Json;
using CP6.Core.Persistence;
using CP6.Core.Services.Sys;
using CP6.Core.Services.Wf;
using CP6.Entity.Contracts.Wire;
using CP6.Entity.DomainModels.Wf;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Oa;

public sealed partial class ConnectorControlRuntimeV1
{
    private sealed record StoredResult(Guid Id, string Service, string Schema, string Digest, string Mapping, byte[] Payload);
    internal sealed record PendingResultJob(Guid JobId, DateTime ReceivedAt);

    internal IQueryable<PendingResultJob>? PendingResultJobs() => fence.HasDeploymentEnvironment
        ? db.Database.SqlQuery<PendingResultJob>($"""
            SELECT job_id AS "JobId",received_at AS "ReceivedAt"
            FROM cp6_oa_conn_v1.pending_result_jobs
            WHERE tenant_id={db.CurrentTenantId} AND environment_id={fence.EnvironmentId}
            """)
        : null;

    private Task<OriginalRow?> ReadAdmission(Guid job, CancellationToken ct) => db.Database.SqlQuery<OriginalRow>($"""
        SELECT instance_id AS "InstanceId",token_id AS "TokenId",node_id AS "NodeId",flow_version_id AS "FlowVersionId",
          root_id AS "RootId",version_id AS "VersionId",source_kind AS "SourceKind",operation AS "Operation",
          profile_id AS "ProfileId",starter_id AS "StarterId",effect_key AS "EffectKey",
          transport_key AS "TransportKey",input_sha256 AS "InputSha256",query_until AS "QueryUntil",
          service_registration AS "ServiceRegistration"
        FROM cp6_oa_conn_v1.dispatch_admissions
        WHERE tenant_id={db.CurrentTenantId} AND environment_id={fence.EnvironmentId} AND job_id={job}
        """).SingleOrDefaultAsync(ct);

    private Task<StoredResult?> ReadResult(Guid job, CancellationToken ct) => db.Database.SqlQuery<StoredResult>($"""
        SELECT id AS "Id",service_registration AS "Service",schema_ref AS "Schema",payload_sha256 AS "Digest",
          mapping_sha256 AS "Mapping",payload AS "Payload"
        FROM cp6_oa_conn_v1.result_inbox
        WHERE tenant_id={db.CurrentTenantId} AND environment_id={fence.EnvironmentId} AND job_id={job}
        """).SingleOrDefaultAsync(ct);

    private Task<bool> ResultStoreReady(CancellationToken ct) => db.Database.SqlQueryRaw<bool>(
        "SELECT cp6_oa_conn_v1.result_ready() AS \"Value\"").SingleAsync(ct);

    private static string MappingDigest(RegisteredConnectorVersionV1 version, IReadOnlyList<ConnectorOutputFieldV1> mapping) =>
        Digest(JsonSerializer.SerializeToUtf8Bytes(new
        {
            version.ResponseMapping,
            Fields = mapping.OrderBy(field => field.Source, StringComparer.Ordinal)
        }));

    // 接收原件与工作流应用分两次提交。即使租约已被接管，旧 worker 仍可留下经过核验的原事实。
    private async Task<Guid?> ReceiveResult(DispatchPlan plan, ConnectorSuccessfulResultV1 result, int attempt, CancellationToken ct)
    {
        if (!await ResultStoreReady(ct)) throw new ScopeAuthorizationExceptionV1("RESULT_STORE_UNAVAILABLE");
        var identity = plan.Original.Identity;
        var source = await ReadAdmission(identity.JobId, ct)
            ?? throw new ScopeAuthorizationExceptionV1("RESULT_ADMISSION_REQUIRED");
        var raw = result.Payload.Length is >= 1 and <= WorkflowCommandJsonV1.MaxDocumentBytes
            ? result.Payload.ToArray() : null;
        var digest = raw == null
            ? Convert.ToHexString(SHA256.HashData(result.Payload.Span)).ToLowerInvariant() : Digest(raw);
        var mapping = (plan.Provider as IConnectorResultApplicationV1)?.OutputMapping(plan.Version);
        var existing = await ReadResult(identity.JobId, ct);
        string? denial = null;
        if (raw == null) denial = "RESULT_SIZE_INVALID";
        else if (result.Original != plan.Original || result.ServiceRegistration != source.ServiceRegistration ||
            !IsReference(result.ProofRef) || result.ResultSha256 != digest)
            denial = "RESULT_IDENTITY_MISMATCH";
        else if (existing != null && (existing.Digest != digest || !raw.AsSpan().SequenceEqual(existing.Payload) ||
            existing.Schema != result.OutputSchema))
            denial = "RESULT_CONFLICT";
        else if (result.OutputSchema != plan.Version.OutputSchema || string.IsNullOrWhiteSpace(plan.Version.ResponseMapping) ||
            ConnectorResultOutputV1.Map(raw, mapping) == null)
            denial = "RESULT_SCHEMA_INVALID";

        if (denial != null)
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO cp6_oa_conn_v1.result_quarantine(tenant_id,environment_id,job_id,payload_sha256,payload,reason)
                VALUES({identity.TenantId},{identity.EnvironmentId},{identity.JobId},{digest},{raw},{denial})
                ON CONFLICT DO NOTHING
                """, ct);
            // 隔离本次异体后，当前租约继续按原键处理已保存的首份有效结果。
            return existing?.Id;
        }
        if (existing != null) return existing.Id;

        var id = Guid.NewGuid();
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO cp6_oa_conn_v1.result_inbox(tenant_id,environment_id,job_id,id,service_registration,schema_ref,
              proof_ref,payload,payload_sha256,mapping_sha256,received_attempt)
            VALUES({identity.TenantId},{identity.EnvironmentId},{identity.JobId},{id},{source.ServiceRegistration},
              {result.OutputSchema},{result.ProofRef},{raw},{digest},{MappingDigest(plan.Version, mapping!)},{attempt})
            """, ct);
        var instance = await ResultInstanceLock(identity.InstanceId).AsNoTracking().SingleOrDefaultAsync(ct);
        var token = await ResultTokenLock(identity.TokenId).AsNoTracking().SingleOrDefaultAsync(ct);
        if (ResultIsStale(source, instance, token)) await MarkStale(identity.JobId, id, ct);
        return id;
    }

    private IQueryable<Wf_FlowInstance> ResultInstanceLock(Guid id) =>
        db.Wf_FlowInstances.FromSqlInterpolated($"""
            SELECT * FROM public."Wf_FlowInstance" WHERE "TenantId"={db.CurrentTenantId} AND "Id"={id} FOR UPDATE
            """);

    private IQueryable<Wf_FlowToken> ResultTokenLock(Guid id) =>
        db.Wf_FlowTokens.FromSqlInterpolated($"""
            SELECT * FROM public."Wf_FlowToken" WHERE "TenantId"={db.CurrentTenantId} AND "Id"={id} FOR UPDATE
            """);

    private static bool ResultIsStale(OriginalRow source, Wf_FlowInstance? instance, Wf_FlowToken? token) =>
        instance == null || instance.Status is FlowInstanceStatus.Approved or FlowInstanceStatus.Rejected or FlowInstanceStatus.Withdrawn ||
        token is not { Status: FlowTokenStatus.Active } || token.InstanceId != source.InstanceId || token.NodeId != source.NodeId;

    private Task MarkStale(Guid job, Guid result, CancellationToken ct) => db.Database.ExecuteSqlInterpolatedAsync($"""
        INSERT INTO cp6_oa_conn_v1.result_applications(tenant_id,environment_id,job_id,result_id,state)
        VALUES({db.CurrentTenantId},{fence.EnvironmentId},{job},{result},'STALE_TOKEN') ON CONFLICT DO NOTHING
        """, ct);

    internal async Task<bool> ApplyResultAsync(Wf_ServiceJob claimed, Guid resultId, FlowEngine engine,
        ApprovalTaskDecisionOwnerV2? app, CancellationToken ct)
    {
        db.ChangeTracker.Clear();
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        try
        {
            var authority = await fence.AcquireAsync(ct);
            if (!await ResultStoreReady(ct)) throw new ScopeAuthorizationExceptionV1("RESULT_STORE_UNAVAILABLE");
            var job = await WfServiceJobService.LockJobAsync(db, claimed.Id, ct);
            var now = await DatabaseUtcClock.ReadUtcNowAsync(db, ct);
            if (job == null || !WfServiceJobService.CurrentClaim(job, claimed.LockedBy, claimed.LockedAtUtc, claimed.AttemptCount, now))
                return false;

            var source = await ReadAdmission(job.Id, ct);
            var result = await ReadResult(job.Id, ct);
            if (source == null || result?.Id != resultId || result.Service != source.ServiceRegistration ||
                (source.InstanceId, source.TokenId, source.NodeId) != (job.InstanceId, job.TokenId, job.NodeId))
                throw new ScopeAuthorizationExceptionV1("RESULT_ADMISSION_MISMATCH");

            var instance = await ResultInstanceLock(job.InstanceId).SingleOrDefaultAsync(ct);
            var token = await ResultTokenLock(job.TokenId).SingleOrDefaultAsync(ct);
            if (ResultIsStale(source, instance, token))
            {
                await MarkStale(job.Id, result.Id, ct);
                job.Status = ServiceJobStatus.Cancelled;
            }
            else
            {
                await ApplyCurrentResult(authority, job, instance!, token!, source, result, engine, app, ct);
                job.Status = ServiceJobStatus.Succeeded;
                job.LastError = null;
            }

            job.CompletedAtUtc = now;
            await db.SaveChangesAsync(ct);
            await fence.ValidateAsync(authority, ct);
            if (job.Status == ServiceJobStatus.Succeeded && !await StarterActive(db.CurrentTenantId, source.StarterId, ct) ||
                job.LockExpiresAtUtc <= await DatabaseUtcClock.ReadUtcNowAsync(db, ct)) return false;
            await tx.CommitAsync(ct);
            return true;
        }
        finally
        {
            db.ChangeTracker.Clear();
        }
    }

    private async Task ApplyCurrentResult(ScopeAuthorizationLeaseV1 authority, Wf_ServiceJob job,
        Wf_FlowInstance instance, Wf_FlowToken token, OriginalRow source, StoredResult result, FlowEngine engine,
        ApprovalTaskDecisionOwnerV2? app, CancellationToken ct)
    {
        var tenant = db.CurrentTenantId;
        var environment = fence.EnvironmentId;
        var pin = new ConnectorPinV1(source.SourceKind, source.RootId, source.VersionId, source.Operation);
        if (instance.Status != FlowInstanceStatus.Running || instance.FlowDefVersionId != source.FlowVersionId ||
            instance.StarterId != source.StarterId || !await StarterActive(tenant, source.StarterId, ct) ||
            ServiceTaskActionRef.Parse(job.ActionRefJson ?? "{}").ConnectorPin != pin ||
            await db.Database.SqlQuery<bool>($"""
                SELECT EXISTS(SELECT FROM cp6_oa_conn_v1.dispatch_cancellations
                WHERE tenant_id={tenant} AND environment_id={environment} AND job_id={job.Id}) AS "Value"
                """).SingleAsync(ct))
            throw new ScopeAuthorizationExceptionV1("RESULT_APPLICATION_HELD", 409);

        var schema = await FlowEngine.LoadOriginalDispatchSchemaAsync(db, environment, instance, ct);
        var node = FlowEngine.FindNode(schema, job.NodeId);
        if (node?.ServiceConnectorPin != pin || ServiceTaskActionRef.Snapshot(node) != job.ActionRefJson)
            throw new ScopeAuthorizationExceptionV1("RESULT_PIN_UNAVAILABLE");

        var selection = await ReadVersion(pin, false, ct);
        if (selection?.Version is not { } version || version.ProfileId != source.ProfileId || version.OutputSchema != result.Schema)
            throw new ScopeAuthorizationExceptionV1("RESULT_APPLICATION_HELD", 409);
        var providers = dispatchers.OfType<IConnectorResultApplicationV1>()
            .Where(p => p.IsSimulation == version.Simulated && p.Matches(version)).Take(2).ToArray();
        if (providers.Length != 1 || providers[0].OutputMapping(version) is not { } mapping ||
            MappingDigest(version, mapping) != result.Mapping || ConnectorResultOutputV1.Map(result.Payload, mapping) is not { } output)
            throw new ScopeAuthorizationExceptionV1("RESULT_MAPPING_UNAVAILABLE");

        var identity = new ConnectorDispatchIdentityV1(tenant, environment, job.Id, instance.Id, token.Id, token.NodeId, source.FlowVersionId, pin);
        var original = new ConnectorOriginalOperationV1(identity, source.EffectKey, source.TransportKey, source.InputSha256, source.StarterId);
        if (!await providers[0].MayApplyAsync(db, version, original, authority.Snapshot, ct))
            throw new ScopeAuthorizationExceptionV1("RESULT_APPLICATION_FORBIDDEN", 403);

        var receipt = Guid.NewGuid();
        Guid? decision = null;
        if (instance.Creator == FlowEngine.ApprovalRuntimeV2 && app?.OwnsConnectorContext(db) == true)
            decision = await app.ApplyConnectorResumeAsync(instance, token, schema, original, result.Service, receipt, output, authority, ct);
        else if (instance.Creator is FlowEngine.ApprovalRuntimeV2 or FlowEngine.PersonalFormRuntimeV2 ||
            !string.IsNullOrEmpty(instance.BizType))
            throw new ScopeAuthorizationExceptionV1("RESULT_RECORDER_UNAVAILABLE");
        else
            await engine.ResumeServiceTokenAsync(instance.Id, token.Id, token.NodeId, output);

        var outputJson = JsonSerializer.Serialize(output);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO cp6_oa_conn_v1.result_applications(tenant_id,environment_id,job_id,result_id,receipt_id,decision_id,attempt,output_vars,state)
            VALUES({tenant},{environment},{job.Id},{result.Id},{receipt},{decision},{job.AttemptCount},{outputJson}::jsonb,'APPLIED');
            INSERT INTO cp6_oa_conn_v1.result_outbox(tenant_id,environment_id,job_id,receipt_id)
            VALUES({tenant},{environment},{job.Id},{receipt});
            UPDATE cp6_oa_conn_v1.runtime_members SET state='RESULT_RECEIVED'
            WHERE tenant_id={tenant} AND environment_id={environment} AND root_id={source.RootId}
              AND version_id={source.VersionId} AND kind='DISPATCH' AND id={job.Id}
            """, ct);
        if (!await providers[0].MayApplyAsync(db, version, original, authority.Snapshot, ct))
            throw new ScopeAuthorizationExceptionV1("RESULT_APPLICATION_FORBIDDEN", 403);
    }
}
