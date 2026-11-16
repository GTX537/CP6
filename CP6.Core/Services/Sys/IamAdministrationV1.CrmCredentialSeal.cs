using System.Data.Common;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Platform;
using CP6.Entity.DomainModels.Sys;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Sys;

internal sealed record LegacyCredentialRequestV1(Guid? TargetUserId, Guid? ImpersonatorId, Guid? ImpersonatorTenantId,
    string? PasswordInputFingerprint, string? FactorMethod = null);

internal sealed record LegacyCredentialPlanV1(LegacyCredentialRequestV1 Request, Guid OriginalCredentialRevision,
    DateTime ChangedAt, string? PasswordSalt, Guid? ResetEpoch, Guid? PreparationRevision = null,
    AcceptedCredentialFactorV1? Factor = null);
internal sealed record LegacyCredentialSubjectV1(Guid UserId, string UserName, Guid? ResultPreparationRevision = null);
internal sealed record LegacyCredentialFactorChangeV1(string Kind, string? Method = null, string? Code = null,
    Func<Sys_User, Task>? Accept = null, Func<Sys_User, CancellationToken, Task>? Audit = null);

// The key and MAC are kept together only inside the protected request. The verifier and response never expose either.
internal sealed record AcceptedCredentialFactorV1(byte[] Key, byte[] Mac)
{
    internal static AcceptedCredentialFactorV1 Create(string code)
    {
        var key = RandomNumberGenerator.GetBytes(32);
        return new(key, HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(code)));
    }

    internal bool Matches(string? code) => code is not null &&
        CryptographicOperations.FixedTimeEquals(Mac, HMACSHA256.HashData(Key, Encoding.UTF8.GetBytes(code)));
}

public sealed partial class IamAdministrationV1
{
    internal async Task<LegacyCredentialSubjectV1?> WriteLegacyCredentialAsync(LegacyCredentialRequestV1 request, string? permission,
        Func<CancellationToken, Task<Guid>> currentActor, Func<CancellationToken, Task> complete,
        Func<Sys_User, Task> validate, Func<Sys_User, LegacyCredentialPlanV1, Task> apply,
        Func<string>? createPasswordSalt, CancellationToken ct, LegacyCredentialFactorChangeV1? factor = null)
    {
        var self = request.TargetUserId is null;
        var kind = factor?.Kind ?? (self ? "self-change-password" : "admin-reset-2fa");
        var preparationResult = kind == "self-setup-2fa";
        var tenant = db.CurrentTenantId;
        var phase = await ExecuteAuthorityAsync<(Guid Actor, LegacyCredentialSubjectV1? Subject, IamCrmUserSealV1? Pending)>(permission, currentActor, async (current, token) =>
        {
            var target = request.TargetUserId ?? current.UserId;
            var user = await FindUser(target, token);
            if (user is null) return (current.UserId, null, null);
            var owner = new LegacyCredentialSubjectV1(user.Id, user.UserName);
            await validate(user);

            var original = await IamCrmUserSealV1.ReadAsync(db, tenant, Guid.Empty, token, target);
            if (original is not null)
            {
                await ReadPlan(original, current.UserId, user, token);
                return (current.UserId, owner, original);
            }

            var store = db.InquiryFence?.RegisteredStore(tenant);
            if (factor?.Accept is { } accept) await accept(user);
            var now = DateTime.Now;
            var plan = new LegacyCredentialPlanV1(request, user.CredentialRevision, now.AddTicks(-(now.Ticks % 10)),
                store.HasValue ? createPasswordSalt?.Invoke() : null, !self || factor is not null && !preparationResult ? Guid.NewGuid() : null,
                factor is null ? null : await ReadCredentialPreparationAsync(db, user.Id, token),
                factor?.Code is { } code ? AcceptedCredentialFactorV1.Create(code) : null);
            if (store is null)
            {
                await Apply(user, plan, token);
                await complete(token);
                return (current.UserId, await FinishUser(user, token), null);
            }

            var tx = db.Database.CurrentTransaction!;
            const string preview = "iam_legacy_credential_seal";
            await tx.CreateSavepointAsync(preview, token);
            var advances = await Capture(user, plan, token);
            if (advances.Length == 0)
            {
                await tx.ReleaseSavepointAsync(preview, token);
                await complete(token);
                return (current.UserId, await FinishUser(user, token), null);
            }

            await tx.RollbackToSavepointAsync(preview, token);
            await tx.ReleaseSavepointAsync(preview, token);
            db.ChangeTracker.Clear();
            await complete(token);
            await RequireSealDurability(token);
            var operation = Guid.NewGuid();
            await IamCrmUserSealV1.InsertAsync(db, tenant, target, current.UserId, kind, operation,
                fingerprint.Protect(JsonSerializer.Serialize(plan)), store.Value, advances, token);
            return (current.UserId, owner, await IamCrmUserSealV1.ReadAsync(db, tenant, operation, token) ?? throw Unknown());
        }, ct);
        if (phase.Pending is not { } accepted) return phase.Subject;

        var subject = accepted.UserId;
        var source = $"user:{subject:D}";
        var operationId = accepted.OperationId;
        var predecessors = accepted.ReadAdvances().ToDictionary(advance => advance.Aggregate, StringComparer.Ordinal);
        var fence = db.InquiryFence ?? throw Error(503, "AUTHORITY_FENCE_UNAVAILABLE");
        await using var crm = await fence.OpenSealAsync(tenant, accepted.StoreId, ct);

        // A login can fail without contacting the CRM Owner. Only a separate durable marker admits any remote seal.
        var alreadyCommitted = await ExecuteAuthorityAsync(permission, currentActor, async (current, token) =>
        {
            if (await ReadCommitted(current.UserId, token) is { } committed) return committed;
            var original = await RequirePending(current.UserId, token);
            await ReadPlan(original, current.UserId, await FindUser(subject, token), token);
            if (!original.SealStarted)
            {
                await RequireSealDurability(token);
                await original.MarkStartedAsync(db, tenant, token);
            }
            return null;
        }, ct);
        if (alreadyCommitted is not null) return Recovered(alreadyCommitted);

        try
        {
            return await ExecuteAuthorityAsync(permission, currentActor, async (current, token) =>
            {
                if (await ReadCommitted(current.UserId, token) is { } committed) return Recovered(committed);
                var original = await RequirePending(current.UserId, token);
                if (!original.SealStarted) throw Unknown();
                var user = await FindUser(subject, token);
                var plan = await ReadPlan(original, current.UserId, user, token);
                var advances = await Capture(user!, plan, token);
                if (advances.SingleOrDefault(advance => advance.Aggregate == source) != predecessors.GetValueOrDefault(source) ||
                    advances.Any(advance => predecessors.TryGetValue(advance.Aggregate, out var expected) && advance != expected))
                    throw Unknown();

                // The Main root still owns this user's current grants, including those issued after acceptance.
                if (advances.Length > 0)
                    await CrmInquiryTenantPauseV1.AdvanceConnectedIdentitiesAsync(crm, tenant, original.StoreId,
                        advances.Select(advance => (advance.Aggregate, advance.Version)).ToArray(), token);
                await complete(token);
                await RequireSealDurability(token);
                var result = await FinishUser(user!, token);
                await Store(current.UserId, null, new IamOperationV1(operationId, kind, "COMMITTED", subject,
                    result.ResultPreparationRevision?.ToString("D")), token);
                await original.RemoveAsync(db, tenant, token);
                return result;
            }, ct);
        }
        catch (IamAdministrationExceptionV1 error) when (error.Message == "IAM_OPERATION_UNKNOWN" && !ct.IsCancellationRequested)
        {
            // The initiating HTTP request already passed the gate. A durable receipt can resolve its lost database ACK
            // even when the committed change has invalidated its access cookie.
            try { if (await ReadCommitted(phase.Actor, ct) is { } committed) return Recovered(committed); }
            catch (DbException) { }
            throw;
        }

        Task<Sys_User?> FindUser(Guid subjectId, CancellationToken token) =>
            db.Sys_Users.SingleOrDefaultAsync(row => row.TenantId == tenant && row.Id == subjectId, token);

        async Task Apply(Sys_User user, LegacyCredentialPlanV1 plan, CancellationToken token)
        {
            await apply(user, plan);
            await db.SaveChangesAsync(token);
        }

        async Task<LegacyCredentialSubjectV1> FinishUser(Sys_User user, CancellationToken token)
        {
            if (factor?.Audit is { } audit) await audit(user, token);
            var revision = preparationResult ? await ReadCredentialPreparationAsync(db, user.Id, token) : (Guid?)null;
            return new(user.Id, user.UserName, revision);
        }

        Task<CrmUserSealAdvanceV1[]> Capture(Sys_User user, LegacyCredentialPlanV1 plan, CancellationToken token) =>
            CaptureCredentialAdvancesAsync(db, tenant, user.Id, () => Apply(user, plan, token));

        async Task<LegacyCredentialPlanV1> ReadPlan(IamCrmUserSealV1 original, Guid actor, Sys_User? user, CancellationToken token)
        {
            if (original.Kind != kind) throw Error(409, "IAM_CRM_USER_SEAL_PENDING");
            original.RequireOwner(actor, kind);
            var plan = JsonSerializer.Deserialize<LegacyCredentialPlanV1>(fingerprint.Unprotect(original.RequestProtected))!;
            if (plan.Request.ImpersonatorId != request.ImpersonatorId || plan.Request.ImpersonatorTenantId != request.ImpersonatorTenantId)
                throw Error(403, "IAM_OPERATION_OWNER_MISMATCH");
            if (plan.Request != request || plan.Factor is { } proof && !proof.Matches(factor?.Code))
                throw Error(409, "IAM_OPERATION_INPUT_CONFLICT");
            if (plan.OriginalCredentialRevision != user?.CredentialRevision ||
                plan.PreparationRevision is { } revision && revision != await ReadCredentialPreparationAsync(db, user!.Id, token))
                throw original.SealStarted ? Unknown() : Error(412, "IAM_REFERENCES_CHANGED");
            return plan;
        }

        async Task<IamCrmUserSealV1> RequirePending(Guid actor, CancellationToken token)
        {
            var original = await IamCrmUserSealV1.ReadAsync(db, tenant, operationId, token) ?? throw Unknown();
            original.RequireOwner(actor, kind);
            if (original.StoreId != accepted.StoreId || original.SourceSeals != accepted.SourceSeals ||
                original.RequestProtected != accepted.RequestProtected) throw Unknown();
            return original;
        }

        async Task<IamOperationV1?> ReadCommitted(Guid actor, CancellationToken token)
        {
            var receipt = await Operation(operationId, token);
            if (receipt is null) return null;
            RequireOwner(receipt, actor, kind);
            return receipt.State == "COMMITTED" ? JsonSerializer.Deserialize<IamOperationV1>(receipt.ResultJson) : null;
        }

        LegacyCredentialSubjectV1? Recovered(IamOperationV1 receipt) => preparationResult
            ? phase.Subject! with { ResultPreparationRevision = Guid.Parse(receipt.Version!) }
            : phase.Subject;

        static IamAdministrationExceptionV1 Unknown() => Error(503, "IAM_OPERATION_UNKNOWN");
    }

    internal static Task<Guid> ReadCredentialPreparationAsync(CP6Context db, Guid user, CancellationToken token) =>
        db.Database.SqlQuery<Guid>($"SELECT revision AS \"Value\" FROM cp6_authentication_v1.preparation_versions WHERE user_id={user}").SingleAsync(token);

    internal static async Task<CrmUserSealAdvanceV1[]> CaptureCredentialAdvancesAsync(CP6Context db, Guid tenant, Guid user, Func<Task> apply)
    {
        var advances = new List<CrmUserSealAdvanceV1>();
        db.CaptureExactCrmUserSeal = true;
        db.CaptureCrmUserSeal = (sourceTenant, sourceUser, advance) =>
        {
            if (sourceTenant != tenant || sourceUser != user) throw Error(503, "IAM_IDENTITY_CONTRACT_UNAVAILABLE");
            advances.Add(advance);
        };
        try { await apply(); }
        finally
        {
            db.CaptureCrmUserSeal = null;
            db.CaptureExactCrmUserSeal = false;
        }
        return advances.ToArray();
    }
}
