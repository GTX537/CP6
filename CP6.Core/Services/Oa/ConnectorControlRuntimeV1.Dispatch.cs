using System.Security.Cryptography;
using System.Text.Json;
using CP6.Core.Persistence;
using CP6.Core.Services.Sys;
using CP6.Core.Services.Wf;
using CP6.Entity.DomainModels.Wf;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Oa;

public sealed partial class ConnectorControlRuntimeV1
{
    private sealed record OriginalRow(Guid InstanceId, Guid TokenId, string NodeId, Guid FlowVersionId, Guid RootId,
        Guid VersionId, string SourceKind, string Operation, Guid ProfileId, Guid StarterId,
        string EffectKey, string TransportKey, string InputSha256, DateTime? QueryUntil, string ServiceRegistration);

    private sealed record DispatchPlan(IConnectorDispatchAdmissionV1 Provider, RegisteredConnectorVersionV1 Version,
        ConnectorOriginalOperationV1 Original, ConnectorDispatchRequestV1? Send, DateTime DeadlineUtc, DateTime? QueryUntil);

    private sealed record DispatchObservation(string State, string? ProofRef, ConnectorSuccessfulResultV1? Result = null);

    internal async Task<ServiceTaskResult> Admit(ConnectorPinV1 pin, ServiceTaskContext job, CancellationToken ct)
    {
        if (!db.Database.IsNpgsql() || job.JobId == Guid.Empty || db.Database.CurrentTransaction != null)
            return ServiceTaskResult.Blocked("BLOCKED_CONNECTOR");

        DispatchPlan? plan;
        string? denial;
        try
        {
            if (await ReadResult(job.JobId, ct) is { } saved)
                return ServiceTaskResult.ApplyConnectorResult(saved.Id);
            (plan, denial) = await CommitPlan(pin, job, ct);
        }
        catch (ScopeAuthorizationExceptionV1 error)
        {
            return ServiceTaskResult.Blocked(error.Message is "SCOPE_MAPPING_DISABLED" or "FLOW_VERSION_UNAVAILABLE"
                ? error.Message : "DISPATCH_AUTHORITY_UNAVAILABLE");
        }
        catch (CurrentPermissionUnavailableException)
        {
            return ServiceTaskResult.Blocked("DISPATCH_AUTHORITY_UNAVAILABLE");
        }
        if (plan == null)
        {
            if (await ReadResult(job.JobId, ct) is { } saved)
                return ServiceTaskResult.ApplyConnectorResult(saved.Id);
            return ServiceTaskResult.Blocked(denial!);
        }

        // CommitPlan 已提交并离开 PostgreSQL 事务。先提交的准入只有此处可首次发送，重领租约只读原操作。
        var observation = await Observe(plan, ct);
        var result = await RecordObservation(plan, job, observation, ct);
        if (result is Guid id) return ServiceTaskResult.ApplyConnectorResult(id);
        if (observation.Result != null) return ServiceTaskResult.HoldDispatch("RESULT_QUARANTINED");
        var retryAt = DateTime.UtcNow.AddSeconds(30);
        var checkOriginal = observation.State != "ORIGINAL_OPERATION_OBSERVED" || plan.Provider is IConnectorResultApplicationV1;
        return ServiceTaskResult.HoldDispatch(observation.State, checkOriginal && retryAt < plan.QueryUntil ? retryAt : null);
    }

    private async Task<(DispatchPlan? Plan, string? Denial)> CommitPlan(ConnectorPinV1 pin, ServiceTaskContext job, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var authority = await fence.AcquireAsync(ct);
        if (!await db.Database.SqlQueryRaw<bool>("SELECT cp6_oa_conn_v1.dispatch_ready() AS \"Value\"").SingleAsync(ct))
            return (null, "DISPATCH_AUTHORITY_UNAVAILABLE");
        var tenant = db.CurrentTenantId;
        var environment = fence.EnvironmentId;
        // 旧入口可能正在持原 Job 锁等待外部响应。当前事务只尝试接管，避免持 WF_AUTH 等待网络。
        if (!await db.Database.SqlQuery<bool>($"""
            SELECT pg_try_advisory_xact_lock(cp6_oa_conn_v1.job_lock({tenant},{job.JobId})) AS "Value"
            """).SingleAsync(ct)) return (null, "LEGACY_CALL_IN_FLIGHT");
        var prior = await ReadAdmission(job.JobId, ct);
        if (prior != null && await ReadResult(job.JobId, ct) != null) return (null, "RESULT_RECEIVED");
        var selection = await ReadVersion(pin, prior != null, ct);
        if (selection == null) return (null, "BLOCKED_CONNECTOR");
        var version = selection.Version;
        if (prior == null && await db.Database.SqlQuery<bool>($"""
            SELECT EXISTS(SELECT FROM cp6_oa_conn_v1.runtime_members WHERE tenant_id={tenant}
                AND environment_id={environment} AND kind='DISPATCH' AND id={job.JobId}) AS "Value"
            """).SingleAsync(ct)) return (null, "DISPATCH_RECOVERY_REQUIRED");

        var claim = await WfServiceJobService.LockJob(db, job.JobId).AsNoTracking().SingleOrDefaultAsync(ct);
        var now = await DatabaseUtcClock.ReadUtcNowAsync(db, ct);
        if (claim == null || !WfServiceJobService.CurrentClaim(claim, job.ClaimWorker, job.ClaimedAtUtc, job.AttemptNo, now) ||
            claim.InstanceId != job.InstanceId || claim.TokenId != job.TokenId || claim.NodeId != job.NodeId ||
            claim.ActionRefJson != job.ActionRefJson || ServiceTaskActionRef.Parse(claim.ActionRefJson!).ConnectorPin != pin)
            return (null, "DISPATCH_CLAIM_CHANGED");

        var instance = await db.Wf_FlowInstances.FromSqlInterpolated($"""
            SELECT * FROM public."Wf_FlowInstance" WHERE "TenantId"={tenant} AND "Id"={claim.InstanceId} FOR UPDATE
            """).AsNoTracking().SingleOrDefaultAsync(ct);
        var token = await db.Wf_FlowTokens.FromSqlInterpolated($"""
            SELECT * FROM public."Wf_FlowToken" WHERE "TenantId"={tenant} AND "Id"={claim.TokenId} FOR UPDATE
            """).AsNoTracking().SingleOrDefaultAsync(ct);
        if (instance is not { Status: FlowInstanceStatus.Running, FlowDefVersionId: Guid flowVersion } || instance.StarterId != job.StarterId ||
            token is not { Status: FlowTokenStatus.Active } || token.InstanceId != instance.Id || token.NodeId != claim.NodeId)
            return (null, "DISPATCH_PIN_UNAVAILABLE");
        if (!await StarterActive(tenant, instance.StarterId, ct))
            return (null, "DISPATCH_AUTHORITY_UNAVAILABLE");

        var identity = new ConnectorDispatchIdentityV1(tenant, environment, claim.Id, instance.Id, token.Id, token.NodeId, flowVersion, pin);
        var participants = dispatchers.Where(d => d.IsSimulation == version.Simulated && d.Matches(version)).Take(2).ToArray();
        if (participants.Length != 1) return (null, "DISPATCH_PARTICIPANT_UNPROVEN");
        var provider = participants[0];
        if (provider is IConnectorResultApplicationV1 && !await ResultStoreReady(ct))
            return (null, "RESULT_STORE_UNAVAILABLE");
        var guarantees = provider.Guarantees(version);
        var deadline = claim.LockExpiresAtUtc!.Value;
        ConnectorOriginalOperationV1 original;
        ConnectorDispatchRequestV1? request = null;
        DateTime? queryUntil;
        if (prior != null)
        {
            if ((prior.InstanceId, prior.TokenId, prior.NodeId, prior.FlowVersionId, prior.RootId, prior.VersionId,
                 prior.SourceKind, prior.Operation, prior.ProfileId, prior.StarterId) !=
                (instance.Id, token.Id, token.NodeId, flowVersion, pin.RootId, pin.VersionId,
                 pin.SourceKind, pin.Operation, version.ProfileId, instance.StarterId))
                return (null, "DISPATCH_ORIGINAL_MISMATCH");
            if (prior.QueryUntil is not DateTime expiry || now >= expiry ||
                guarantees.OriginalQueryRetention is not { Ticks: > 0 } || string.IsNullOrWhiteSpace(version.ResultQueryContract))
                return (null, "DISPATCH_RECOVERY_REQUIRED");
            if (provider is not IConnectorResultApplicationV1 && await db.Database.SqlQuery<bool>($"""
                SELECT EXISTS(SELECT FROM cp6_oa_conn_v1.dispatch_observations WHERE tenant_id={tenant} AND environment_id={environment}
                  AND job_id={claim.Id} AND state='ORIGINAL_OPERATION_OBSERVED') AS "Value"
                """).SingleAsync(ct)) return (null, "ORIGINAL_OPERATION_OBSERVED");
            original = new(identity, prior.EffectKey, prior.TransportKey, prior.InputSha256, prior.StarterId);
            if (!await provider.MayQueryOriginalAsync(db, version, original, authority.Snapshot, ct))
                return (null, "DISPATCH_QUERY_FORBIDDEN");
            queryUntil = expiry;
        }
        else
        {
            var schema = await FlowEngine.LoadOriginalDispatchSchemaAsync(db, environment, instance, ct);
            var node = FlowEngine.FindNode(schema, token.NodeId);
            if (node == null || node.ServiceConnectorPin != pin || ServiceTaskActionRef.Snapshot(node) != claim.ActionRefJson)
                return (null, "DISPATCH_PIN_UNAVAILABLE");

            var mutation = version.EffectClass != "READ_ONLY";
            if (mutation && (version.RequiresCurrentAuthorizationAtApply == null ||
                version.RequiresCurrentAuthorizationAtApply == true && !guarantees.ChecksCurrentAuthorizationAtApply))
                return (null, "REMOTE_AUTHORITY_FENCE_UNPROVEN");
            var idempotencyUntil = Deadline(now, version.IdempotencyContract, guarantees.IdempotencyRetention);
            queryUntil = Deadline(now, version.ResultQueryContract, guarantees.OriginalQueryRetention);
            var preparation = await provider.PrepareAsync(db, version, new(identity, instance.StarterId,
                authority.Snapshot, instance.VarsJson ?? "{}"), ct);
            if (preparation == null || !IsReference(preparation.ServiceRegistration) || !IsReference(preparation.DisclosureDecision) ||
                mutation && (!IsReference(preparation.OwnerDecision) || string.IsNullOrWhiteSpace(version.AdmissionBasis) ||
                    idempotencyUntil == null && !preparation.AllowsUncertainFirstCall) ||
                preparation.MappedInput.Length is < 1 or > 65536)
                return (null, "DISPATCH_QUALIFICATION_REQUIRED");

            var input = preparation.MappedInput.ToArray();
            var effectKey = Key("effect", identity);
            var transportKey = Key("transport", identity);
            original = new(identity, effectKey, transportKey, Digest(input), instance.StarterId);
            request = new(original, input, authority.Snapshot, preparation.ServiceRegistration, preparation.DisclosureDecision, preparation.OwnerDecision);
            await InsertAdmission(selection, request, preparation.AllowsUncertainFirstCall, claim, now, idempotencyUntil, queryUntil, ct);
        }

        await fence.ValidateAsync(authority, ct);
        var final = await DatabaseUtcClock.ReadUtcNowAsync(db, ct);
        if (!await StarterActive(tenant, instance.StarterId, ct) ||
            deadline <= final.AddSeconds(version.MaxCallDurationSec) || request == null && queryUntil <= final)
            return (null, "DISPATCH_CLAIM_CHANGED");
        await tx.CommitAsync(ct);
        return (new(provider, version, original, request, deadline, queryUntil), null);
    }

    private async Task<bool> StarterActive(Guid tenant, Guid starter, CancellationToken ct) =>
        await db.Sys_Tenants.AsNoTracking().AnyAsync(t => t.Id == tenant && t.Enable &&
            (t.ExpireDate == null || t.ExpireDate > DateTime.Now), ct) &&
        await db.Sys_Users.AsNoTracking().AnyAsync(u => u.Id == starter && u.Enable &&
            (u.LockedUntil == null || u.LockedUntil <= DateTime.Now), ct);

    private static bool IsReference(string? value) => !string.IsNullOrWhiteSpace(value) && value.Length <= 256 && !value.Any(char.IsControl);
    private static string Digest(byte[] value) => Convert.ToHexString(SHA256.HashData(value)).ToLowerInvariant();
    private static string Key(string purpose, ConnectorDispatchIdentityV1 identity) =>
        Digest(JsonSerializer.SerializeToUtf8Bytes(new { contract = "OA-CONN-DISPATCH/1", purpose, identity }, ConnectorVersionControlService.Json));

    private static DateTime? Deadline(DateTime now, string? contract, TimeSpan? retention) =>
        !string.IsNullOrWhiteSpace(contract) && retention is { Ticks: > 0 } duration && duration < DateTime.MaxValue - now
            ? now + duration : null;

    private async Task InsertAdmission(VersionSelection selection, ConnectorDispatchRequestV1 request, bool allowsUncertainFirstCall,
        Wf_ServiceJob claim, DateTime now, DateTime? idempotencyUntil, DateTime? queryUntil, CancellationToken ct)
    {
        var o = request.Original;
        var i = o.Identity;
        var p = i.Pin;
        var a = request.Authority;
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO cp6_oa_conn_v1.runtime_members(tenant_id,environment_id,root_id,version_id,kind,id,state)
            VALUES({i.TenantId},{i.EnvironmentId},{p.RootId},{p.VersionId},'DISPATCH',{i.JobId},'DISPATCH_ADMITTED');
            INSERT INTO cp6_oa_conn_v1.dispatch_admissions(tenant_id,environment_id,job_id,instance_id,token_id,node_id,flow_version_id,
              root_id,version_id,source_kind,operation,profile_id,profile_revision,control_revision,starter_id,permission_domain,
              authority_epoch,authority_revision,service_registration,disclosure_decision,owner_decision,allows_uncertain_first_call,simulated,
              worker_id,claim_at,first_attempt,effect_key,transport_key,input_bytes,input_sha256,admitted_at,idempotency_until,query_until)
            VALUES({i.TenantId},{i.EnvironmentId},{i.JobId},{i.InstanceId},{i.TokenId},{i.NodeId},{i.FlowVersionId},
              {p.RootId},{p.VersionId},{p.SourceKind},{p.Operation},{selection.Version.ProfileId},{selection.ProfileRevision},{selection.RootRevision},
              {o.StarterId},{a.PermissionDomain},{a.Epoch},{a.RowVersion},{request.ServiceRegistration},{request.DisclosureDecision},
              {request.OwnerDecision},{allowsUncertainFirstCall},{selection.Version.Simulated},{claim.LockedBy},{claim.LockedAtUtc},
              {claim.AttemptCount},{o.EffectKey},{o.TransportKey},{request.FrozenInput.ToArray()},{o.InputSha256},{now},{idempotencyUntil},{queryUntil})
            """, ct);
    }

    private static async Task<DispatchObservation> Observe(DispatchPlan plan, CancellationToken ct)
    {
        var deadline = plan.Send == null && plan.QueryUntil < plan.DeadlineUtc ? plan.QueryUntil!.Value : plan.DeadlineUtc;
        var remaining = deadline - DateTime.UtcNow;
        if (remaining <= TimeSpan.Zero) return new("OUTCOME_UNKNOWN", null);
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var max = TimeSpan.FromSeconds(plan.Version.MaxCallDurationSec);
        budget.CancelAfter(remaining < max ? remaining : max);
        try
        {
            budget.Token.ThrowIfCancellationRequested();
            var pending = plan.Send == null
                ? plan.Provider.QueryOriginalAsync(plan.Version, plan.Original, budget.Token)
                : plan.Provider.SendAsync(plan.Version, plan.Send, budget.Token);
            var observed = await pending.WaitAsync(budget.Token);
            var original = plan.Original;
            var exact = observed.EffectKey == original.EffectKey && observed.TransportKey == original.TransportKey &&
                observed.InputSha256 == original.InputSha256;
            return observed.State switch
            {
                ConnectorTransportStateV1.Acknowledged when plan.Send != null && exact =>
                    new("HTTP_ACK", null, observed.SuccessfulResult),
                ConnectorTransportStateV1.NotObserved when plan.Send == null && exact => new("NOT_OBSERVED", null),
                ConnectorTransportStateV1.OriginalOperationObserved when plan.Send == null && exact && IsReference(observed.OriginalProofRef)
                    => new("ORIGINAL_OPERATION_OBSERVED", observed.OriginalProofRef, observed.SuccessfulResult),
                _ => new("OUTCOME_UNKNOWN", null),
            };
        }
        catch (Exception error) when (error is HttpRequestException or IOException or TimeoutException ||
            error is OperationCanceledException && !ct.IsCancellationRequested)
        {
            return new("OUTCOME_UNKNOWN", null);
        }
    }

    private async Task<Guid?> RecordObservation(DispatchPlan plan, ServiceTaskContext job,
        DispatchObservation observation, CancellationToken ct)
    {
        var i = plan.Original.Identity;
        var source = plan.Send == null ? "ORIGINAL_QUERY" : "SEND";
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await AuthenticationCompletionGateV1.AcquireAsync(db, ct);
        var inserted = await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO cp6_oa_conn_v1.dispatch_observations(tenant_id,environment_id,job_id,worker_id,claim_at,attempt,source,state,proof_ref)
            VALUES({i.TenantId},{i.EnvironmentId},{i.JobId},{job.ClaimWorker},{job.ClaimedAtUtc},{job.AttemptNo},{source},
                {observation.State},{observation.ProofRef}) ON CONFLICT DO NOTHING
            """, ct);
        if (inserted == 1 && source == "SEND")
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE cp6_oa_conn_v1.runtime_members SET state={(observation.State == "HTTP_ACK" ? "SENT" : "OUTCOME_UNKNOWN")}
                WHERE tenant_id={i.TenantId} AND environment_id={i.EnvironmentId} AND root_id={i.Pin.RootId}
                  AND kind='DISPATCH' AND id={i.JobId} AND state='DISPATCH_ADMITTED'
                """, ct);
        var result = observation.Result is { } originalResult
            ? await ReceiveResult(plan, originalResult, job.AttemptNo, ct) : null;
        await tx.CommitAsync(ct);
        return result;
    }
}
