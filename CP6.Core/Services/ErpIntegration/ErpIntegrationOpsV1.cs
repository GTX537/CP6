using System.Data;
using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Integration;
using CP6.Core.Services.Sys;
using CP6.Platform.EntityFramework;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using static CP6.Core.Services.ErpIntegration.IntegrationOpsContractV1;

namespace CP6.Core.Services.ErpIntegration;

public sealed partial class ErpIntegrationOpsV1(IDbContextFactory<ErpIntegrationContext> factory, ErpIntegrationRuntime runtime,
    IHttpContextAccessor http, IntegrationOpsDirectoryV1 directory, ErpIntegrationOpsSourceV1 source,
    ErpInboxReplayService inbox, ErpDeliveryReplayService delivery, IEnumerable<IErpIntegrationEffectOwnerV1> owners)
{
    internal const string Journal = "Main.ERP.C03.IntegrationReplay";
    private const string LegacyContract = "ErpReplayRequest.legacy.v1";
    internal const string Blocked = "BLOCKED_WAITING_AUTHORITY";
    private readonly IntegrationOpsAuthorityV1 authority = new(runtime, http);
    private readonly IErpIntegrationEffectOwnerV1? effectOwner = owners.SingleOrDefault();

    public sealed record Admission(IntegrationReplayReceiptV1 Receipt, bool New);
    public sealed record OperationLookup(Guid ReplayOperationId, string Status, IntegrationReplayReceiptV1? Receipt,
        string? CurrentExecution = null);
    private sealed record EligibilityTicket(Guid Tenant, Guid Actor, Guid Operation, string Epoch, IntegrationReplayEvidenceV1 Evidence,
        string Version, IntegrationRequestRefV1 Original, DateTimeOffset Expires);
    private sealed record Legacy(IntegrationReplayReceiptV1 Receipt, string Reason);

    public async Task RequireLegacyListReadAsync(Guid tenant, CancellationToken ct)
    {
        await using var queue = await factory.CreateDbContextAsync(ct);
        await using var db = await IntegrationOpsAuthorityV1.BindAsync(queue, tenant, ct);
        if (!(await authority.ReadAsync(db, ct)).Evidence)
            throw Error("INTEGRATION_READ_DENIED", 403);
    }

    public async Task<IntegrationFailureV1> DetailAsync(Guid tenant, string failureRef, CancellationToken ct)
    {
        await using var queue = await factory.CreateDbContextAsync(ct);
        await using var db = await IntegrationOpsAuthorityV1.BindAsync(queue, tenant, ct);
        var access = await authority.ReadAsync(db, ct);
        var locator = Locate(tenant, failureRef);
        var native = await source.ReadNativeAsync(queue, db, locator.Queue, ct);
        return directory.Project(tenant, native.Record, access.Evidence, source.RouteEpoch) with
        {
            FailureRef = failureRef, OwnerOutcome = native.OwnerOutcome, EvidenceState = "VERIFIED",
            OriginalMessageRef = access.Evidence ? native.MessageId : null,
            OriginalRequestRef = access.Evidence ? native.OriginalRequest : null,
            EnvelopeRef = access.Evidence ? native.EnvelopeRef : null,
            ResultVersion = access.Evidence ? native.ResultVersion : null,
            ReplayEvidence = access.Evidence ? native.Evidence : null
        };
    }

    public async Task<IntegrationEligibilityV1> EligibilityAsync(Guid tenant, string failureRef, CancellationToken ct)
    {
        await using var queue = await factory.CreateDbContextAsync(ct);
        await using var db = await IntegrationOpsAuthorityV1.BindAsync(queue, tenant, ct);
        var access = await authority.ReadAsync(db, ct);
        var locator = Locate(tenant, failureRef);
        var expires = runtime.Clock.GetUtcNow().AddMinutes(2);
        var native = await source.ReadNativeAsync(queue, db, locator.Queue, ct);
        if (!CanSchedule(access, native.Evidence.AllowedAction))
            return new("Blocked", "INTEGRATION_ACTION_DENIED", null, expires, source.RouteEpoch, null, null);
        var operation = Guid.NewGuid();
        var ticket = directory.Encode("Eligibility", new EligibilityTicket(tenant, access.Actor, operation, source.RouteEpoch, native.Evidence,
            native.Record.NativeVersion!, native.OriginalRequest, expires));
        if (!native.Eligible)
            return new("Blocked", "NATIVE_NOT_ELIGIBLE", ticket, expires, source.RouteEpoch, native.Evidence, native.Record.NativeVersion, operation);
        var waiting = native.Evidence.AllowedAction != Deliver;
        return new(waiting ? "PendingOwner" : "Eligible", waiting && effectOwner is null ? Blocked : null,
            ticket, expires, source.RouteEpoch, native.Evidence, native.Record.NativeVersion, operation);
    }

    public Task<Admission> ScheduleAsync(Guid tenant, string failureRef, IntegrationReplayRequestV1 input, CancellationToken ct)
    {
        var version = Validate(input, legacy: false);
        return ScheduleCoreAsync(tenant, input, version, failureRef, ct);
    }

    public Task<Admission> ScheduleLegacyAsync(Guid tenant, string type, string id, ErpReplayRequest input, CancellationToken ct)
    {
        if (input is null || input.RowVersion is not { Length: 8 }) throw Error("REPLAY_INPUT_INVALID", 400);
        var request = new IntegrationReplayRequestV1(input.OperationId, Action(type)!, new(Owner, type, id),
            Convert.ToBase64String(input.RowVersion), Digest(type, input.PayloadSha256), input.ReasonCode, "", LegacyContract);
        return ScheduleCoreAsync(tenant, request, Validate(request, legacy: true), null, ct);
    }

    private async Task<Admission> ScheduleCoreAsync(Guid tenant, IntegrationReplayRequestV1 input, byte[] version,
        string? failureRef, CancellationToken ct)
    {
        await using var queue = await factory.CreateDbContextAsync(ct);
        await using var tx = await queue.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
        await using var db = await IntegrationOpsAuthorityV1.BindAsync(queue, tenant, ct);
        var access = await authority.ReadAsync(db, ct);
        var legacy = failureRef is null;
        if (!legacy && Locate(tenant, failureRef!).Queue != input.NativeQueueRef)
            throw Error("ORIGINAL_IDENTITY_CONFLICT");
        var original = MainOwnerOperationJournalV1.Encode(input);
        var operation = input.ReplayOperationId;
        var prior = await ReadPreviousAsync(db, queue, input, original, access, legacy, ct);
        if (prior is not null) return new(prior, false);

        // 先取当前授权根，再等待原动作或原队列，读写权限缓存不参与决定。
        await db.Database.ExecuteSqlRawAsync("SET LOCAL lock_timeout = '10s'", ct);
        var epoch = await AuthenticationCompletionGateV1.AcquireAsync(db, ct);
        access = await authority.ReadAsync(db, ct);
        prior = await ReadPreviousAsync(db, queue, input, original, access, legacy, ct);
        if (prior is not null) return new(prior, false);
        if (!CanSchedule(access, input.AllowedAction)) throw Error("INTEGRATION_ACTION_DENIED", 403);
        var native = await source.ReadNativeAsync(queue, db, input.NativeQueueRef, ct);
        CheckNative(input, native);
        if (!legacy) CheckTicket(tenant, access.Actor, input, native);
        if (!native.Eligible) throw Error("NATIVE_NOT_ELIGIBLE");

        var intent = Intent(tenant, access.Actor, input, native, source.RouteEpoch);
        var hold = input.AllowedAction == Deliver ? null : await HoldAsync(db, intent, false, ct);
        var blocked = input.AllowedAction != Deliver && hold is null;
        await ErpSqlLock.AcquireAsync(queue, $"int-ops:operation:{tenant:D}:{operation:D}", ct);
        prior = await ReadPreviousAsync(db, queue, input, original, access, legacy, ct);
        if (prior is not null) return new(prior, false);
        var target = input.NativeQueueRef.NativeId;
        if (!blocked)
        {
            // 对应原生处理者共享这些锁。等待完成后再读最后资格、版本和租约。
            if (input.AllowedAction == Replay)
            {
                await ErpInboxReplayService.LockAsync(queue, target, ct);
                await ErpRequestHandler.LockAggregateAsync(queue, tenant, native.RequestKind!.Value, native.AggregateId!.Value, ct);
            }
            else if (input.AllowedAction == Deliver)
                await ErpDeliveryReplayService.LockResultAsync(queue, tenant, Guid.Parse(target), ct);
            else
                await ErpDeliveryReplayService.LockBridgeAsync(queue, tenant, target, ct);
            if (intent.RouteEpoch != source.RouteEpoch) throw Error("ELIGIBILITY_CHANGED");
            var locked = await source.ReadNativeAsync(queue, db, input.NativeQueueRef, ct);
            CheckNative(input, locked);
            if (!locked.Eligible || locked.OriginalRequest != native.OriginalRequest)
                throw Error("NATIVE_NOT_ELIGIBLE");
            if (!legacy) CheckTicket(tenant, access.Actor, input, locked);
        }
        var time = runtime.Clock.GetUtcNow();
        string? versionAfter = null;
        if (!blocked)
        {
            var typed = new ErpReplayRequest(operation, version, input.ExpectedDigest.Value, input.ReasonCode);
            if (input.AllowedAction == Replay)
                await inbox.StageAsync(queue, tenant, target, typed, access.Actor.ToString("D"), time, ct);
            else
            {
                var kind = input.AllowedAction == Deliver ? "result" : "bridge";
                await delivery.StageAsync(queue, db, tenant, kind, target, typed, access.Actor.ToString("D"), time, ct);
            }
            await queue.SaveChangesAsync(ct);
            versionAfter = (await source.ReadNativeAsync(queue, db, input.NativeQueueRef, ct)).Record.NativeVersion;
        }
        var receipt = new IntegrationReplayReceiptV1(operation, input.AllowedAction, input.NativeQueueRef, input.ExpectedDigest,
            native.OriginalRequest, native.MessageId, time, access.Actor, blocked ? null : operation.ToString("D"),
            input.ExpectedNativeVersion, versionAfter, blocked ? "Blocked" : "Scheduled", native.OwnerOutcome,
            blocked ? Blocked : null, operation, Receipt, source.RouteEpoch);
        var audit = new
        {
            tenant, owner = Owner, replayOperator = access.Actor, actorKind = "USER", originalActor = (Guid?)null,
            workerActor = (Guid?)null, access.PolicyVersion, authorizationRootEpoch = epoch, authorizationHold = hold,
            decision = blocked ? Blocked : "SCHEDULED", routeEpoch = source.RouteEpoch, input.ReasonCode,
            input.AllowedAction, input.NativeQueueRef, input.ExpectedNativeVersion, input.ExpectedDigest,
            native.OriginalRequest, native.MessageId, native.Record.AttemptCount, native.Record.NativeStatus,
            nativeStatusAfter = blocked ? native.Record.NativeStatus : "SCHEDULED", scheduledAt = blocked ? null : (DateTimeOffset?)time,
            contract = Receipt, retentionClass = "OWNER_ORIGINAL", supportRef = operation
        };
        await new MainOwnerOperationJournalV1(db).Store(Journal, Receipt, operation, access.Actor, original, receipt,
            [native.OriginalRequest.RequestId], audit, ct);
        await tx.CommitAsync(ct);
        return new(Mask(receipt, access), true);
    }

    private async Task<IntegrationReplayReceiptV1?> ReadPreviousAsync(CP6Context db, ErpIntegrationContext queue,
        IntegrationReplayRequestV1 input, byte[] original, IntegrationOpsAuthorityV1.Access access, bool legacy, CancellationToken ct)
    {
        var previous = await MainOwnerOperationJournalV1.Read(db, db.CurrentTenantId, Journal, input.ReplayOperationId, ct);
        if (previous is not null)
        {
            if (previous.Actor != access.Actor || !MainOwnerOperationJournalV1.Matches(previous, original))
                throw Error("REPLAY_OPERATION_CONFLICT");
            return Mask(ReadReceipt(previous), access);
        }
        var old = await LegacyAsync(queue, db.CurrentTenantId, input.ReplayOperationId, ct);
        if (old is null) return null;
        if (!legacy || old.Receipt.AcceptedBy != access.Actor || old.Receipt.NativeQueueRef != input.NativeQueueRef ||
            old.Receipt.ExpectedDigest != input.ExpectedDigest || old.Receipt.NativeVersionBefore != input.ExpectedNativeVersion ||
            old.Reason != input.ReasonCode)
            throw Error("REPLAY_OPERATION_CONFLICT");
        return Mask(old.Receipt, access);
    }

    public async Task<OperationLookup> ReadReplayAsync(Guid tenant, Guid operation, CancellationToken ct)
    {
        await using var queue = await factory.CreateDbContextAsync(ct);
        await using var tx = await queue.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
        await using var db = await IntegrationOpsAuthorityV1.BindAsync(queue, tenant, ct);
        var access = await authority.ReadAsync(db, ct);
        var previous = await MainOwnerOperationJournalV1.Read(db, tenant, Journal, operation, ct);
        var receipt = previous is null ? (await LegacyAsync(queue, tenant, operation, ct))?.Receipt : ReadReceipt(previous);
        if (receipt is null) return new(operation, "NOT_OBSERVED", null);
        var execution = receipt.Status == "Blocked" ? "Blocked" : await ObserveExecutionAsync(queue, db, receipt, ct);
        return new(operation, receipt.Status, Mask(receipt, access), execution);
    }

    private async Task<string> ObserveExecutionAsync(ErpIntegrationContext queue, CP6Context db,
        IntegrationReplayReceiptV1 receipt, CancellationToken ct)
    {
        if (receipt.NativeQueueRef is not { } key || receipt.RouteEpoch != source.RouteEpoch) return "UNKNOWN";
        try
        {
            var native = await source.ReadNativeAsync(queue, db, key, ct);
            if (native.Evidence.ExpectedDigest != receipt.ExpectedDigest || native.OriginalRequest != receipt.OriginalRequestRef)
                return "UNKNOWN";
            return native.Record.NativeStatus switch
            {
                "Published" or "Processed" or "COMPLETED" => "Completed",
                "DEAD_LETTERED" when native.Record.SafeCode == Blocked => "Denied",
                "DEAD_LETTERED" => "Exhausted",
                "Processing" or "Pending" or "PENDING" => "Scheduled",
                _ => "UNKNOWN"
            };
        }
        catch (Exception error) when (error is IntegrationOpsExceptionV1 || IsUnavailable(error))
        {
            return "UNKNOWN";
        }
    }

    public async Task<object> ReadAuditAsync(Guid tenant, Guid operation, CancellationToken ct)
    {
        await using var queue = await factory.CreateDbContextAsync(ct);
        await using var db = await IntegrationOpsAuthorityV1.BindAsync(queue, tenant, ct);
        var access = await authority.ReadAsync(db, ct);
        if (!access.Audit) throw Error("INTEGRATION_AUDIT_DENIED", 403);
        var data = await db.Database.SqlQuery<string>($"""
            SELECT audit_json::text AS "Value" FROM cp6_main_owner_v1.operations
            WHERE tenant_id={tenant} AND owner={Journal} AND operation_id={operation}
            """).SingleOrDefaultAsync(ct);
        if (data is null)
        {
            var history = await LegacyAsync(queue, tenant, operation, ct) ?? throw Error("FAILURE_NOT_FOUND", 404);
            return new { operation, proofStatus = "LEGACY_UNKNOWN", receipt = Mask(history.Receipt, access) };
        }
        using var document = JsonDocument.Parse(data);
        var audit = document.RootElement;
        return new
        {
            operation, proofStatus = audit.GetProperty("decision").GetString(),
            allowedAction = audit.GetProperty("AllowedAction").GetString(),
            routeEpoch = audit.GetProperty("routeEpoch").GetString(),
            originalActor = (Guid?)null, workerActor = (Guid?)null,
            replayOperator = access.Evidence ? audit.GetProperty("replayOperator").GetGuid() : (Guid?)null,
            nativeQueueRef = access.Evidence ? audit.GetProperty("NativeQueueRef").Deserialize<IntegrationQueueRefV1>() : null,
            expectedDigest = access.Evidence ? audit.GetProperty("ExpectedDigest").Deserialize<IntegrationDigestV1>() : null,
            expectedNativeVersion = access.Evidence ? audit.GetProperty("ExpectedNativeVersion").GetString() : null,
            reasonCode = audit.GetProperty("ReasonCode").GetString(),
            schedulingTime = audit.GetProperty("scheduledAt").Clone(),
            authorizationRootEpoch = audit.GetProperty("authorizationRootEpoch").GetInt64().ToString(System.Globalization.CultureInfo.InvariantCulture)
        };
    }

    public async Task<object> ReadOwnerResultAsync(Guid tenant, Guid requestId, CancellationToken ct)
    {
        await using var queue = await factory.CreateDbContextAsync(ct);
        await using var db = await IntegrationOpsAuthorityV1.BindAsync(queue, tenant, ct);
        var access = await authority.ReadAsync(db, ct);
        var request = await queue.Requests.AsNoTracking().SingleOrDefaultAsync(x =>
            x.TenantId == tenant && x.RequestId == requestId, ct);
        if (request is null) return new { ownerId = Owner, outcome = "NOT_OBSERVED" };
        var result = await queue.Set<Cp6OutboxMessage>().AsNoTracking().Where(x => x.TenantId == tenant &&
            x.TopicName == ErpEventContracts.ResultTopic && x.AggregateId == request.AggregateId.ToString() &&
            x.AggregateVersion == request.ResultVersion).OrderBy(x => x.CreatedAtUtc).ThenBy(x => x.Id)
            .Select(x => x.Id).FirstOrDefaultAsync(ct);
        ErpIntegrationOpsSourceV1.Native? evidence = null;
        if (result != Guid.Empty)
            evidence = await source.ReadNativeAsync(queue, db, new(Owner, Outbox, result.ToString("D")), ct);
        return new
        {
            ownerId = Owner, outcome = ErpIntegrationOpsSourceV1.Outcome(request), request.Terminal,
            originalRequestRef = access.Evidence ? ErpIntegrationOpsSourceV1.Original(requestId, request.RequestVersion) : null,
            resultVersion = access.Evidence ? ErpIntegrationOpsSourceV1.Number(request.ResultVersion) : null,
            originalResultNativeRef = access.Evidence ? evidence?.Evidence.NativeQueueRef : null,
            resultDigest = access.Evidence ? evidence?.Evidence.ExpectedDigest : null,
            evidenceState = evidence is null ? "ORIGINAL_EVIDENCE_UNAVAILABLE" : "VERIFIED"
        };
    }

    private IntegrationOpsDirectoryV1.Locator Locate(Guid tenant, string token)
    {
        var locator = directory.ReadLocator(tenant, token);
        if (locator.Queue.OwnerId != Owner || locator.FailureKey !=
            ErpIntegrationOpsSourceV1.FailureKey(locator.Queue.NativeType, locator.Queue.NativeId))
            throw Error("FAILURE_NOT_FOUND", 404);
        return locator;
    }

    private static byte[] Validate(IntegrationReplayRequestV1 input, bool legacy)
    {
        var type = input?.NativeQueueRef?.NativeType;
        var expected = input?.ExpectedDigest;
        if (input is null || input.ReplayOperationId == Guid.Empty || input.NativeQueueRef is null ||
            input.NativeQueueRef.OwnerId != Owner || input.NativeQueueRef.NativeId is not { Length: > 0 and <= 128 } ||
            input.NativeQueueRef.NativeId.Any(char.IsControl) || Action(type) is null ||
            type == Bridge && input.NativeQueueRef.NativeId.Length > 20 ||
            type == Outbox && (!Guid.TryParseExact(input.NativeQueueRef.NativeId, "D", out var id) ||
                id == Guid.Empty || id.ToString("D") != input.NativeQueueRef.NativeId) ||
            input.ExpectedNativeVersion is not { Length: 12 } || expected is null || !IsHash(expected.Value) ||
            input.ReasonCode is not ("dependency-recovered" or "contract-verified") ||
            input.InputContractVersion != (legacy ? LegacyContract : Contract) ||
            !legacy && input.EligibilityRef is not { Length: > 0 and <= 8192 })
            throw Error("REPLAY_INPUT_INVALID", 400);
        if (input.AllowedAction != Action(type) || expected != Digest(type!, expected.Value))
            throw Error("DIGEST_DOMAIN_ACTION_MISMATCH");
        Span<byte> bytes = stackalloc byte[8];
        if (!Convert.TryFromBase64String(input.ExpectedNativeVersion, bytes, out var count) || count != bytes.Length ||
            Convert.ToBase64String(bytes) != input.ExpectedNativeVersion)
            throw Error("REPLAY_INPUT_INVALID", 400);
        return bytes.ToArray();
    }

    private static bool CanSchedule(IntegrationOpsAuthorityV1.Access access, string action) =>
        access.Evidence && (action == Deliver ? access.Deliver : access.Replay);

    private void CheckTicket(Guid tenant, Guid actor, IntegrationReplayRequestV1 input, ErpIntegrationOpsSourceV1.Native native)
    {
        var ticket = directory.Decode<EligibilityTicket>("Eligibility", input.EligibilityRef, "ELIGIBILITY_INVALID", 409);
        if (ticket.Tenant != tenant || ticket.Actor != actor || ticket.Operation != input.ReplayOperationId || ticket.Epoch != source.RouteEpoch ||
            ticket.Evidence != native.Evidence || ticket.Version != input.ExpectedNativeVersion ||
            ticket.Original != native.OriginalRequest || ticket.Expires <= runtime.Clock.GetUtcNow())
            throw Error("ELIGIBILITY_CHANGED");
    }

    private static void CheckNative(IntegrationReplayRequestV1 input, ErpIntegrationOpsSourceV1.Native native)
    {
        if (native.Evidence.ExpectedDigest != input.ExpectedDigest) throw Error("ORIGINAL_IDENTITY_CONFLICT");
        if (native.Record.NativeVersion != input.ExpectedNativeVersion) throw Error("NATIVE_VERSION_CHANGED");
    }

    private async Task<IntegrationEffectHoldV1?> HoldAsync(CP6Context db, IntegrationEffectIntentV1 intent, bool executing,
        CancellationToken ct)
    {
        if (effectOwner is null) return null;
        var tx = db.Database.CurrentTransaction ?? throw Error(Blocked, 503);
        var proof = await effectOwner.EvaluateAndHoldAsync(db, intent, executing, ct);
        return proof is not null && proof.TransactionId == tx.TransactionId && proof.RouteEpoch == intent.RouteEpoch &&
            proof.FenceRef is { Length: > 0 and <= 256 } && proof.SourceProtectionRef is { Length: > 0 and <= 256 } &&
            proof.QualificationRef is { Length: > 0 and <= 256 } ? proof : null;
    }

    private static IntegrationEffectIntentV1 Intent(Guid tenant, Guid actor, IntegrationReplayRequestV1 input,
        ErpIntegrationOpsSourceV1.Native native, string epoch) => new(tenant, actor, input.ReplayOperationId,
        epoch, input.AllowedAction, input.NativeQueueRef, native.OriginalRequest, input.ExpectedDigest, input.ExpectedNativeVersion);

    internal static IntegrationReplayReceiptV1 ReadReceipt(MainOwnerOperationJournalV1.Original previous) =>
        previous.Contract == Receipt && JsonSerializer.Deserialize<IntegrationReplayReceiptV1>(previous.Result) is { } receipt
            ? receipt : throw Error("ORIGINAL_EVIDENCE_UNAVAILABLE", 503);

    private static IntegrationReplayReceiptV1 Mask(IntegrationReplayReceiptV1 receipt, IntegrationOpsAuthorityV1.Access access) =>
        access.Evidence ? receipt : receipt with
        {
            NativeQueueRef = null, ExpectedDigest = null, OriginalRequestRef = null, OriginalMessageRef = null, AcceptedBy = null,
            NativeVersionBefore = null, NativeVersionAfter = null
        };

    private static async Task<Legacy?> LegacyAsync(ErpIntegrationContext queue, Guid tenant, Guid operation, CancellationToken ct)
    {
        var request = await queue.ReplayAudits.AsNoTracking().SingleOrDefaultAsync(
            x => x.TenantId == tenant && x.OperationId == operation, ct);
        var response = await queue.Set<ErpDeliveryReplayAudit>().AsNoTracking().SingleOrDefaultAsync(
            x => x.TenantId == tenant && x.OperationId == operation, ct);
        if (request is not null && response is not null) throw Error("REPLAY_OPERATION_CONFLICT");
        if (request is not null)
            return LegacyReceipt(Inbox, request.MessageId, request.MessageId, request.ActorId, request.ReasonCode,
                request.PayloadSha256, request.InputRowVersion, request.ReplayedAtUtc);
        if (response is not null)
            return LegacyReceipt(response.Kind == "result" ? Outbox : Bridge, response.TargetId, response.MessageId, response.ActorId,
                response.ReasonCode, response.PayloadSha256, response.InputRowVersion, response.ReplayedAtUtc);
        return null;

        Legacy LegacyReceipt(string type, string id, string? message, string actor, string reason, string hash,
            byte[] version, DateTimeOffset at) => new(new(operation, Action(type)!, new(Owner, type, id), Digest(type, hash),
            null, message, at, Guid.TryParse(actor, out var actorId) ? actorId : null, operation.ToString("D"),
            Convert.ToBase64String(version), null, "Scheduled", "LEGACY_UNKNOWN", null, operation, Receipt), reason);
    }
}
