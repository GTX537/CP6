using System.Text.Json;
using CP6.Core.Services.Platform;
using CP6.Core.Services.Sys;
using CP6.Entity.DTOs.Client;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace CP6.WebApi.Services;

public sealed partial class AuthenticationEntryServiceV1
{
    private sealed record ChallengeCredentialPlan(Guid Completion, Guid Preparation, Guid? Lease,
        DateTime ChangedAt, Guid Epoch, Guid Transition, AcceptedCredentialFactorV1? Factor);

    private sealed record ChallengeCredentialPhase<T>(T? Result, IamCrmUserSealV1? Pending) where T : class;

    private Task<AuthenticationEntryResultV1> CompleteEnrollmentAsync(AuthenticationInteractionV1 pending, Guid lease, string code,
        ClientContextDto? client, string? ip, string? ua, Action<AuthenticationEntryResultV1>? stage, CancellationToken ct, bool deferOutput)
        => WriteChallengeCredentialAsync(pending.Proof, "challenge-enroll-2fa", lease, code, async (authority, plan, token) =>
        {
            var user = authority.User;
            var verified = await store.ReadFactorAsync(authority.Proof.CompletionRef, lease, token);
            if (verified.AuthenticationVersion != authority.Proof.AuthenticationVersion || verified.ClientBinding != authority.Proof.ClientBinding
                || verified.UserId != user.Id || verified.TenantId != user.TenantId
                || !await store.PreparationMatchesAsync(user.Id, user.TwoFactorSecret, pending.PreparationHash, token))
                throw new InvalidOperationException("E-SEC-013");

            TwoFactorService.ApplyEnrollment(user, plan.ChangedAt, plan.Epoch);
            await db.SaveChangesAsync(token);
            var factor = verified with { AuthenticationVersion = AuthSessionVersion.For(user) };
            Audit(user, SecurityEventType.TwoFactorEnrolled, authority.Proof, ip, ua);
            var result = await IssueAsync(authority, factor, plan.Transition, client, ip, ua, token, deferOutput);
            await store.FinishAsync(authority.Proof.CompletionRef, lease, true, token);
            return result;
        }, stage, ct);

    private async Task<T> WriteChallengeCredentialAsync<T>(AuthenticationProofV1 proof, string kind, Guid? lease, string? code,
        Func<AuthenticationAuthorityV1, ChallengeCredentialPlan, CancellationToken, Task<T>> write,
        Action<T>? stage, CancellationToken ct) where T : class
    {
        var tenantId = proof.TenantId;
        var userId = proof.UserId;
        var fence = db.InquiryFence;
        var protector = protection?.CreateProtector("CP6.Authentication.ChallengeCredential.V1");
        var phase = await gate.ExecuteAsync(proof, async (authority, token) =>
        {
            var existing = await IamCrmUserSealV1.ReadAsync(db, tenantId, Guid.Empty, token, userId);
            if (existing is not null)
            {
                await ReadPlan(existing, token);
                return new ChallengeCredentialPhase<T>(null, existing);
            }

            var now = DateTime.Now;
            var plan = new ChallengeCredentialPlan(proof.CompletionRef,
                await IamAdministrationV1.ReadCredentialPreparationAsync(db, userId, token), lease,
                now.AddTicks(-(now.Ticks % 10)), Guid.NewGuid(), Guid.NewGuid(),
                code is null ? null : AcceptedCredentialFactorV1.Create(code));
            if (fence?.RegisteredStore(tenantId) is not { } crmStore)
            {
                var result = await write(authority, plan, token);
                stage?.Invoke(result);
                return new ChallengeCredentialPhase<T>(result, null);
            }

            var transaction = db.Database.CurrentTransaction!;
            const string preview = "authentication_challenge_credential";
            await transaction.CreateSavepointAsync(preview, token);
            var (candidate, advances) = await Capture(authority, plan, token);
            if (advances.Length == 0)
            {
                await transaction.ReleaseSavepointAsync(preview, token);
                stage?.Invoke(candidate);
                return new ChallengeCredentialPhase<T>(candidate, null);
            }

            await transaction.RollbackToSavepointAsync(preview, token);
            await transaction.ReleaseSavepointAsync(preview, token);
            db.ChangeTracker.Clear();
            await IamAdministrationV1.RequireSealDurabilityAsync(db, token);
            var operation = Guid.NewGuid();
            await IamCrmUserSealV1.InsertAsync(db, tenantId, userId, userId, kind, operation,
                (protector ?? throw Unavailable()).Protect(JsonSerializer.Serialize(plan)), crmStore, advances, token, proof.CompletionRef);
            var accepted = await IamCrmUserSealV1.ReadAsync(db, tenantId, operation, token) ?? throw Unavailable();
            return new ChallengeCredentialPhase<T>(null, accepted);
        }, ct);
        if (phase.Result is { } finished) return finished;

        var pending = phase.Pending!;
        var acceptedAdvances = pending.ReadAdvances().ToLookup(advance => advance.Aggregate, StringComparer.Ordinal);
        try
        {
            await using var crm = await (fence ?? throw Unavailable()).OpenSealAsync(tenantId, pending.StoreId, ct);
            await gate.ExecuteAsync(proof, async (_, token) =>
            {
                var original = await RequireOriginal(token);
                await ReadPlan(original, token);
                if (!original.SealStarted)
                {
                    await IamAdministrationV1.RequireSealDurabilityAsync(db, token);
                    await original.MarkStartedAsync(db, tenantId, token);
                }
                return true;
            }, ct);
            return await gate.ExecuteAsync(proof, async (authority, token) =>
            {
                var original = await RequireOriginal(token);
                if (!original.SealStarted) throw Unavailable();
                var plan = await ReadPlan(original, token);
                var (completed, advances) = await Capture(authority, plan, token);
                var userSource = $"user:{userId:D}";
                if (!advances.Where(advance => advance.Aggregate == userSource).SequenceEqual(acceptedAdvances[userSource])
                    || advances.Any(advance => advance.Aggregate != userSource && acceptedAdvances.Contains(advance.Aggregate)
                        && !acceptedAdvances[advance.Aggregate].Contains(advance)))
                    throw Unavailable();
                stage?.Invoke(completed);
                if (advances.Length > 0)
                    await CrmInquiryTenantPauseV1.AdvanceConnectedIdentitiesAsync(crm, tenantId, original.StoreId,
                        advances.Select(advance => (advance.Aggregate, advance.Version)).ToArray(), token);
                await IamAdministrationV1.RequireSealDurabilityAsync(db, token);
                await IamAdministrationV1.StoreOperationAsync(db, userId, null,
                    new IamOperationV1(original.OperationId, kind, "COMMITTED", userId), token);
                await original.RemoveAsync(db, tenantId, token);
                return completed;
            }, ct);
        }
        catch (TenantManagementExceptionV1 unavailable)
        {
            throw new RefreshCompletionUnknownException(unavailable);
        }

        async Task<IamCrmUserSealV1> RequireOriginal(CancellationToken token)
        {
            var current = await IamCrmUserSealV1.ReadAsync(db, tenantId, pending.OperationId, token) ?? throw Unavailable();
            if (current.StoreId != pending.StoreId || current.SourceSeals != pending.SourceSeals || current.RequestProtected != pending.RequestProtected)
                throw Unavailable();
            return current;
        }

        async Task<ChallengeCredentialPlan> ReadPlan(IamCrmUserSealV1 current, CancellationToken token)
        {
            if (current.Kind != kind || current.CompletionId != proof.CompletionRef || current.ActorId != userId || current.UserId != userId)
                throw Unavailable();
            var plan = JsonSerializer.Deserialize<ChallengeCredentialPlan>((protector ?? throw Unavailable()).Unprotect(current.RequestProtected))!;
            if (plan.Completion != proof.CompletionRef || plan.Lease != lease || plan.Factor is { } factor && !factor.Matches(code)
                || plan.Preparation != await IamAdministrationV1.ReadCredentialPreparationAsync(db, userId, token))
                throw new InvalidOperationException("E-SEC-013");
            return plan;
        }

        async Task<(T Result, CrmUserSealAdvanceV1[] Advances)> Capture(
            AuthenticationAuthorityV1 authority, ChallengeCredentialPlan plan, CancellationToken token)
        {
            T result = null!;
            var advances = await IamAdministrationV1.CaptureCredentialAdvancesAsync(db, tenantId, userId, async () =>
            {
                result = await write(authority, plan, token);
                await db.SaveChangesAsync(token);
            });
            return (result, advances);
        }

        static InvalidOperationException Unavailable() => new("AUTH_SESSION_AUTHORITY_UNAVAILABLE");
    }
}
