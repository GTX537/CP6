using System.Data;
using System.Data.Common;
using System.Security.Cryptography;
using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Erp;
using CP6.Core.Services.ErpIntegration;
using CP6.Core.Services.Integration;
using CP6.Core.Services.Sys;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using static CP6.Core.Services.Plm.EngineeringIntakeWireV1;
using static CP6.Core.Services.Plm.EngineeringIntakeStoreV1;

namespace CP6.Core.Services.Plm;

/// <summary>CRM 工程交接的 Main 受理入口。HTTP 与后续传输适配调用同一原键处理器。</summary>
public sealed partial class EngineeringIntakeContextService(CP6Context db, OperationsOwnerV1 owner, EngineeringRequirementService requirements,
    EngineeringIntakeOriginalsV1 originals, IDataProtectionProvider protection, IEngineeringIntakeAuthorityV1? authority = null)
{
    private readonly EngineeringIntakeStoreV1 store = new(db, protection);
    private static readonly TimeSpan ContextLifetime = TimeSpan.FromMinutes(5);
    private const string FenceDomain = "HO-ENG-MAIN-FENCE-1", DecisionDomain = "HO-ENG-MAIN-DECISION-1";

    private sealed record Current(Slot Slot, Received Original, Terminal Terminal, EngineeringRequirementSnapshotV1? Target,
        AdoptedSource? Adopted);
    private sealed record AdoptedSource(EngineeringSourceRefV1 Reference, EngineeringIntakeSourceV1 Original);
    private sealed class Deadlines
    {
        private readonly List<(DateTimeOffset Until, string Code, int Status)> values = [];
        internal void Add(DateTimeOffset until, string code, int status) => values.Add((until, code, status));
        internal void RequireActive(DateTimeOffset now)
        {
            foreach (var value in values)
                if (value.Until <= now) throw Failure(value.Code, value.Status);
        }
    }

    internal Task<EngineeringNextContextV1> QueryNextAsync(EngineeringIntakeCallerV1 caller, byte[] body, CancellationToken ct)
    {
        var query = Read<EngineeringNextQueryV1>(body, NextQuery);
        var scope = Scope(query.CaseNaturalKey);
        return RunAsync(caller, scope, EngineeringIntakeUseV1.Prepare, false, async (route, access, deadlines, token) =>
        {
            if (!await HasCoverageAsync(route, token)) throw AuthorityUnavailable();
            await store.HoldCaseAsync(query.CaseNaturalKey, token);
            var native = new EngineeringRequirementStoreV1(db);
            await native.Fence(false, token);
            var current = await ReadCurrentAsync(access, native, query.CaseNaturalKey, deadlines, token);
            var now = Milliseconds(await store.NowAsync(token));
            var until = Milliseconds(Earliest(now + ContextLifetime, route.ValidUntil, caller.ExpiresAt));
            var next = Describe(query, current, route, now, until);
            deadlines.Add(until, "AUTHORITY_FENCE_UNAVAILABLE", 503);
            return next with
            {
                PriorExecutionFence = next.PriorExecutionFence with { ProofRef = route.OwnerSigner.Sign(FenceDomain, next) },
                Decision = next.Decision with { ProofRef = route.OwnerSigner.Sign(DecisionDomain, next) }
            };
        }, ct);
    }

    internal Task<EngineeringIntakeReceiveV1> ReceiveAsync(EngineeringIntakeCallerV1 caller, byte[] body, CancellationToken ct)
    {
        var bytes = body.ToArray();
        var request = Read<EngineeringIntakeRequestV1>(bytes, Request);
        return RunAsync(caller, Scope(request.CaseNaturalKey), EngineeringIntakeUseV1.Receive, true, async (route, access, deadlines, token) =>
        {
            var covered = await HasCoverageAsync(route, token);
            // 当前源共同门已由路由参加者保持。自然槽和原键始终使用同一锁顺序。
            await store.HoldCaseAsync(request.CaseNaturalKey, token);
            await store.HoldRequestAsync(request.RequestKey, token);
            var original = await store.ReadRequestAsync(request.RequestKey, token);
            if (original is not null)
            {
                if (original.Envelope.CaseNaturalKey != request.CaseNaturalKey)
                    throw Failure("ENGINEERING_INTAKE_SOURCE_UNPROVEN", 503);
                await HoldOriginalAsync(access, original, deadlines, token);
                var replayStore = new EngineeringRequirementStoreV1(db);
                await replayStore.Fence(false, token);
                await ReadAdoptedSourceAsync(access, replayStore, original, await store.ReadTerminalAsync(original, token), deadlines, token);
                if (!original.Bytes.AsSpan().SequenceEqual(bytes)) throw Failure("REQUEST_KEY_CONFLICT", 409);
                return Acknowledge(request, "ALREADY_OBSERVED", original.ReceivedAt);
            }

            if (!covered) throw AuthorityUnavailable();
            if (request.Epoch != route.RequestEpoch) throw Failure("ENGINEERING_REQUEST_EPOCH_NOT_ALLOWED", 403);
            var bodyHash = Hash(bytes);
            var use = await authority!.RequireSourceUseAsync(db, route, request, bodyHash, token)
                ?? throw Failure("ENGINEERING_INTAKE_SOURCE_UNPROVEN", 503);
            if (use.DigestKey is null || use.DigestKey.Version != request.SourceBinding.DigestKeyVersion
                || !EngineeringIntakeProofKeyV1.Equal(request.SourceBinding.SourceDigest, SourceDigest(use.DigestKey, request.Source)))
                throw Failure("ENGINEERING_SOURCE_BINDING_INVALID", 403);
            if (use.MissingRequiredInputs is not { Length: <= 30 }
                || use.MissingRequiredInputs.Distinct(StringComparer.Ordinal).Count() != use.MissingRequiredInputs.Length
                || use.MissingRequiredInputs.Any(code => string.IsNullOrEmpty(code) || code.Length > 128 ||
                    code.Any(character => !(char.IsAsciiLetterOrDigit(character) || character == '_'))))
                throw AuthorityUnavailable();

            var native = new EngineeringRequirementStoreV1(db);
            await native.Fence(true, token);
            var current = await ReadCurrentAsync(access, native, request.CaseNaturalKey, deadlines, token);
            var now = await store.NowAsync(token);
            if (use.ValidUntil <= now) throw Failure("ENGINEERING_INTAKE_SOURCE_UNPROVEN", 503);
            RequireNext(request, current, route, now);
            if (await store.ConflictingIdentityAsync(request, current is null, token)) throw Failure("ENGINEERING_REQUEST_IDENTITY_CONFLICT", 409);
            deadlines.Add(request.NextAuthorization.ExpiresAt, "STALE_NEXT_CONTEXT", 409);
            deadlines.Add(use.ValidUntil, "ENGINEERING_INTAKE_SOURCE_UNPROVEN", 503);

            var receivedAt = Milliseconds(now);
            var receivedTrust = SignTrust(route, caller.ClientId, request.RequestKey, RequestContract, request.Producer, request.Epoch,
                bodyHash, receivedAt);
            await store.InsertRequestAsync(request, bytes, receivedTrust, token);
            var effect = use.MissingRequiredInputs.Length == 0
                ? await WriteRequirementAsync(access, native, route, request, SourceBytes(bytes), current, token)
                : (current?.Target is { } target ? Reference(target) : null, current?.Terminal.SourcePackageId, current?.Terminal.SourcePackageRevision);
            var resultAt = Milliseconds(await store.NowAsync(token));
            var result = CreateIntake(route, request, effect.Item1, use.MissingRequiredInputs, resultAt);
            var resultBytes = JsonSerializer.SerializeToUtf8Bytes(result, WireJson);
            var resultTrust = SignTrust(route, route.ResultCredentialSubject, request.RequestKey, IntakeContract,
                result.Producer, result.Epoch, Hash(resultBytes), resultAt);
            await store.CompleteAsync(request, current?.Slot, result, resultBytes, resultTrust, effect.Item2, effect.Item3, token);
            return Acknowledge(request, "DURABLY_RECEIVED", receivedAt);
        }, ct);
    }

    internal Task<EngineeringIntakeOriginalPageV1> QueryOriginalAsync(EngineeringIntakeCallerV1 caller, byte[] body, CancellationToken ct)
    {
        var query = Read<EngineeringIntakeOriginalQueryV1>(body, OriginalQuery);
        var scope = new EngineeringIntakeScopeV1(query.TenantId, query.Authority, query.LegalEntityKey, null, query.Request.CaseId);
        return RunAsync(caller, scope, EngineeringIntakeUseV1.ReadOriginal, false, async (route, access, deadlines, token) =>
        {
            var covered = await HasCoverageAsync(route, token);
            var original = await store.ReadRequestAsync(query.Request.RequestKey, token);
            EngineeringIntakeOriginalItemV1[] items = [];
            if (original is not null && Matches(query, original.Envelope))
            {
                await HoldOriginalAsync(access, original, deadlines, token);
                var terminal = await store.ReadTerminalAsync(original, token);
                var native = new EngineeringRequirementStoreV1(db);
                await native.Fence(false, token);
                await ReadAdoptedSourceAsync(access, native, original, terminal, deadlines, token);
                items = [terminal.Item];
            }
            var now = Milliseconds(await store.NowAsync(token));
            var observation = items.Length > 0 ? "FOUND" : covered ? "NOT_OBSERVED" : "UNPROVEN";
            var page = new EngineeringIntakeOriginalPageV1(query, observation, items, [], null,
                new(route.Scope.Authority, "ENGINEERING_ORIGINAL_QUERY", route.OwnerSigner.Version, ""), now, false);
            return page with
            {
                QueryEvidence = page.QueryEvidence with { ProofRef = route.OwnerSigner.Sign("HO-ENG-ORIGINAL-PAGE-1", page) }
            };
        }, ct);
    }

    private async Task<T> RunAsync<T>(EngineeringIntakeCallerV1 caller, EngineeringIntakeScopeV1 scope, EngineeringIntakeUseV1 use,
        bool write, Func<EngineeringIntakeRouteV1, OperationsAccessV1, Deadlines, CancellationToken, Task<T>> body, CancellationToken ct)
    {
        if (caller.TenantId == Guid.Empty || caller.TenantId != db.CurrentTenantId || scope.TenantId != caller.TenantId)
            throw Failure("ENGINEERING_INTAKE_TENANT_FORBIDDEN", 403);
        if (authority is null || !owner.Owns(db) || !db.Database.IsNpgsql() || db.Database.CurrentTransaction is not null
            || System.Transactions.Transaction.Current is not null || db.ChangeTracker.HasChanges())
            throw AuthorityUnavailable();
        IDbContextTransaction? transaction = null;
        var committing = false;
        try
        {
            transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
            await db.Database.ExecuteSqlRawAsync("SET LOCAL lock_timeout = '10s'", ct);
            if (write) await AuthenticationCompletionGateV1.AcquireAsync(db, ct);
            else await OperationsOwnerV1.HoldReadAuthorityFenceAsync(db, ct);
            var credentialUntil = await HoldCredentialAsync(caller, ct);
            var route = await authority.HoldRouteAsync(db, caller, scope, use, ct) ?? throw AuthorityUnavailable();
            ValidateRoute(route, scope);
            var deadlines = new Deadlines();
            deadlines.Add(Earliest(caller.ExpiresAt, credentialUntil, route.ValidUntil), "ENGINEERING_INTAKE_AUTHORITY_EXPIRED", 403);
            deadlines.RequireActive(await store.NowAsync(ct));
            var access = await owner.EngineeringIntakeAccessAsync(route.MainActorId, caller, ct);
            access.RequireRequirementFields(false);
            var result = await body(route, access, deadlines, ct);
            await owner.ValidateEngineeringIntakeAccessAsync(access, ct);
            deadlines.RequireActive(await store.NowAsync(ct));
            ct.ThrowIfCancellationRequested();
            committing = true;
            await transaction.CommitAsync(ct);
            return result;
        }
        catch (Exception error)
        {
            Exception? rollbackError = null;
            if (transaction is not null)
                try { await transaction.RollbackAsync(CancellationToken.None); }
                catch (Exception failure) { rollbackError = failure; }
            if (write && (committing || rollbackError is not null))
                throw new OrderPriceCorrectionException("ENGINEERING_INTAKE_OUTCOME_UNKNOWN", 503,
                    rollbackError is null ? error : new AggregateException(error, rollbackError));
            if (error is DbException or DbUpdateException or TimeoutException or CurrentPermissionUnavailableException
                or CommercialProtocolException or CryptographicException)
                throw new OrderPriceCorrectionException("AUTHORITY_FENCE_UNAVAILABLE", 503, error);
            throw;
        }
        finally
        {
            if (transaction is not null) await transaction.DisposeAsync();
        }
    }

    private async Task<DateTimeOffset> HoldCredentialAsync(EngineeringIntakeCallerV1 caller, CancellationToken ct)
    {
        var tenant = caller.TenantId;
        var jti = caller.TokenId.ToString("D");
        var rows = await db.Database.SqlQuery<DateTimeOffset>($"""
            SELECT "ExpiresAtUtc" AS "Value" FROM crm_identity."ServiceToken"
            WHERE "Issuer"={caller.Issuer} AND "Jti"={jti} AND "TenantId"={tenant} AND "ClientId"={caller.ClientId}
                AND "RevokedAtUtc" IS NULL AND "ExpiresAtUtc">clock_timestamp() FOR SHARE
            """).ToArrayAsync(ct);
        if (rows.Length != 1) throw Failure("ENGINEERING_INTAKE_CREDENTIAL_FORBIDDEN", 403);
        return rows[0];
    }

    private static void ValidateRoute(EngineeringIntakeRouteV1 route, EngineeringIntakeScopeV1 scope)
    {
        if (route.Scope != scope || route.MainActorId == Guid.Empty || route.OwnerSigner is null)
            throw AuthorityUnavailable();
        _ = CommercialWireV1.RequireKey(route.RouteVersion);
        _ = CommercialWireV1.RequireKey(route.RequestEpoch);
        _ = CommercialWireV1.RequireKey(route.ResultCredentialSubject);
        Stream(route.IntakeStream, IntakeContract);
        Stream(route.ProgressStream, ProgressContract);
    }

    private async Task<bool> HasCoverageAsync(EngineeringIntakeRouteV1 route, CancellationToken ct)
    {
        var coverage = await store.ReadCoverageAsync(route.Scope, ct);
        return coverage is not null && coverage.Generation == route.DirectoryGeneration && coverage.EvidenceKey == route.DirectoryEvidenceKey;
    }

    private async Task<Current?> ReadCurrentAsync(OperationsAccessV1 access, EngineeringRequirementStoreV1 native,
        EngineeringCaseNaturalKeyV1 natural, Deadlines deadlines, CancellationToken ct)
    {
        var slot = await store.ReadSlotAsync(natural, ct);
        if (slot is null) return null;
        var previous = await store.ReadRequestAsync(slot.CurrentRequestKey, ct) ?? throw OriginalUnavailable();
        if (previous.Envelope.CaseNaturalKey != natural || previous.Envelope.CaseId != slot.CaseId
            || previous.Envelope.SubmissionVersion != Version(slot.CurrentEs) || slot.ExecutionGeneration == Guid.Empty)
            throw OriginalUnavailable();
        await HoldOriginalAsync(access, previous, deadlines, ct);
        var terminal = await store.ReadTerminalAsync(previous, ct);
        var adopted = await ReadAdoptedSourceAsync(access, native, previous, terminal, deadlines, ct);
        if (terminal.Result.Engineering is not { } engineering) return new(slot, previous, terminal, null, null);
        var target = await native.Read(Guid.Parse(engineering.EngineeringRequestId), null, ct);
        return new(slot, previous, terminal, target, adopted);
    }

    private async Task HoldOriginalAsync(OperationsAccessV1 access, Received original, Deadlines deadlines, CancellationToken ct)
    {
        var until = await originals.HoldReadAsync(access, original.Source, ct);
        deadlines.Add(until, "ENGINEERING_INTAKE_SOURCE_UNPROVEN", 503);
    }

    private async Task<AdoptedSource?> ReadAdoptedSourceAsync(OperationsAccessV1 access, EngineeringRequirementStoreV1 native,
        Received observed, Terminal terminal, Deadlines deadlines, CancellationToken ct)
    {
        if (terminal.Result.Engineering is not { } engineering) return null;
        var target = Guid.Parse(engineering.EngineeringRequestId);
        var package = terminal.SourcePackageId ?? throw OriginalUnavailable();
        await access.RequireEngineeringRequirement(target, ct);
        await access.RequireEngineeringRequirement(package, ct);
        var packageVersion = await native.Read(package, terminal.SourcePackageRevision ?? throw OriginalUnavailable(), ct);
        if (packageVersion.Body.Source is not { Kind: "OWNER_ORIGINAL", Origin.Reference: { Owner: SourceOwner } origin })
            throw OriginalUnavailable();
        var accepted = origin.Id == observed.Envelope.RequestKey
            ? observed : await store.ReadRequestAsync(origin.Id, ct) ?? throw OriginalUnavailable();
        if (accepted.Envelope.CaseNaturalKey != observed.Envelope.CaseNaturalKey || accepted.Envelope.CaseId != observed.Envelope.CaseId
            || accepted.Envelope.SourceBinding.SourceVersion != origin.Version || accepted.SourceBodyHash != origin.Digest)
            throw OriginalUnavailable();
        if (accepted != observed)
            await HoldOriginalAsync(access, accepted, deadlines, ct);
        var recorded = await native.Read(target, CommercialWireV1.Revision(engineering.EngineeringRevision), ct);
        if (recorded.Body.Requirement?.Source != packageVersion.Reference) throw OriginalUnavailable();
        access.AtCommit(async token =>
        {
            await access.RequireEngineeringRequirement(target, token);
            await access.RequireEngineeringRequirement(package, token);
        });
        return new(packageVersion.Reference, accepted.Envelope.Source);
    }

    private static EngineeringNextContextV1 Describe(EngineeringNextQueryV1 query, Current? current, EngineeringIntakeRouteV1 route,
        DateTimeOffset issued, DateTimeOffset expires)
    {
        var expected = current is null ? new EngineeringNextQueryV1(query.CaseNaturalKey, null, "0", null, null, null)
            : new(query.CaseNaturalKey, current.Slot.CaseId, Version(current.Slot.CurrentEs), current.Slot.CurrentRequestKey,
                current.Terminal.Result.ReceiptId, current.Original.Envelope.SourceBinding);
        var blocker = expected != query ? "CURRENT_ES_CHANGED"
            : current?.Target is { } existing && (current.Adopted is null || existing.Body.Requirement?.Source != current.Adopted.Reference)
                ? "ENGINEERING_SOURCE_CHANGED"
            : current?.Slot.CurrentEs == long.MaxValue || current?.Slot.CaseRevision == long.MaxValue
                || current?.Target?.Reference.Version == Version(long.MaxValue) ? "ENGINEERING_GENERATION_EXHAUSTED" : null;
        var target = blocker is null && current?.Target is { } native ? Reference(native) : null;
        var fenceKey = expected != query ? "UNPROVEN"
            : current is null ? "ABSENT" : "CLOSED:" + current.Slot.CurrentRequestKey.ToString("D");
        var fenceGeneration = CommercialCanonicalV1.Digest("HO-ENG-EXECUTION-GENERATION-1", new
        {
            route.DirectoryGeneration, route.DirectoryEvidenceKey, route.RequestEpoch, execution = current?.Slot.ExecutionGeneration
        });
        return new(query, Version(current?.Slot.CaseRevision ?? 0), blocker is null,
            blocker is null ? target is null ? "CREATE" : "REVISE" : null, target,
            new(route.Scope.Authority, fenceKey, fenceGeneration, ""),
            new(route.Scope.Authority, "ENGINEERING_NEXT_CONTEXT", route.OwnerSigner.Version, ""),
            route.RouteVersion, route.IntakeStream, route.ProgressStream, issued, expires, blocker is null ? [] : [blocker]);
    }

    private static void RequireNext(EngineeringIntakeRequestV1 request, Current? current, EngineeringIntakeRouteV1 route, DateTimeOffset now)
    {
        var proposed = request.NextAuthorization;
        var expected = Describe(proposed.Query, current, route, proposed.IssuedAt, proposed.ExpiresAt);
        if (!expected.NextAllowed || request.Mode != expected.RequiredMode || request.Target != expected.Target
            || proposed.IssuedAt > now || proposed.ExpiresAt <= now || proposed.ExpiresAt <= proposed.IssuedAt
            || proposed.ExpiresAt - proposed.IssuedAt > ContextLifetime)
            throw Stale();
        var unsigned = proposed with
        {
            PriorExecutionFence = proposed.PriorExecutionFence with { ProofRef = "" },
            Decision = proposed.Decision with { ProofRef = "" }
        };
        if (!CommercialCanonicalV1.Data(unsigned).AsSpan().SequenceEqual(CommercialCanonicalV1.Data(expected))
            || !route.OwnerSigner.Verify(FenceDomain, expected, proposed.PriorExecutionFence.ProofRef)
            || !route.OwnerSigner.Verify(DecisionDomain, expected, proposed.Decision.ProofRef))
            throw Stale();
    }

    private static bool Matches(EngineeringIntakeOriginalQueryV1 query, EngineeringIntakeRequestV1 original) =>
        query.Authority == original.CaseNaturalKey.TargetAuthority && query.LegalEntityKey == original.CaseNaturalKey.LegalEntityKey
        && query.Request.CaseId == original.CaseId && query.Request.SubmissionVersion == original.SubmissionVersion
        && query.Request.SourceDigest == original.SourceBinding.SourceDigest;

    private static EngineeringIntakeRefV1 Reference(EngineeringRequirementSnapshotV1 target) =>
        new(target.Reference.Id.ToString("D"), target.Reference.Version);
    private static EngineeringIntakeReceiveV1 Acknowledge(EngineeringIntakeRequestV1 request, string state, DateTimeOffset received) =>
        new(request.RequestKey, request.SourceBinding.SourceDigest, state, "RECEIPT_ONLY", "UNKNOWN", received);

    private static DateTimeOffset Earliest(DateTimeOffset first, DateTimeOffset second, DateTimeOffset third) =>
        new[] { first, second, third }.Min();
    private static OrderPriceCorrectionException AuthorityUnavailable() => Failure("AUTHORITY_FENCE_UNAVAILABLE", 503);
    private static OrderPriceCorrectionException Stale() => Failure("STALE_NEXT_CONTEXT", 409);
}
