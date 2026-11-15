using System.Data.Common;
using CP6.Core.EFDbContext;
using CP6.Core.Persistence;
using CP6.Core.Services.CrmIdentity;
using CP6.Core.Services.Platform;
using CP6.Core.Services.Sys;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CP6.WebApi.Services;

internal sealed class CrmOidcFamilySealV1(DatabaseOptions database, CrmIdentityRuntime identity, CrmInquiryTenantPauseV1 fence)
    : ICrmOidcFamilySealV1
{
    public async Task<CrmOidcGrantRevocationV1[]> CaptureAsync(DbConnection source, DbTransaction transaction, Guid tenant, Guid subject,
        CrmOidcFamilyGrantV1[] grants, CancellationToken ct)
    {
        if (grants.Length == 0) return [];
        const string preview = "crm_oidc_family_logout_v1";
        await transaction.SaveAsync(preview, ct);
        await using var context = await CreateContextAsync(source, transaction, ct);
        var advances = await AppendRevocationsAsync(context, tenant, subject,
            grants.Select(g => (g.GrantId, g.AccessExpiresAtUtc)), ct);
        // Only the exact plan survives acceptance. CRM must seal it before the native write set can commit.
        await transaction.RollbackAsync(preview, ct);
        await transaction.ReleaseAsync(preview, ct);
        return advances;
    }

    public async Task<NpgsqlConnection> OpenAsync(Guid tenant, Guid store, CancellationToken ct)
    {
        try { return await fence.OpenSealAsync(tenant, store, ct); }
        catch (TenantManagementExceptionV1) { throw new CrmOidcFamilyLogoutUnavailableException(); }
    }

    public async Task SealAndAppendAsync(NpgsqlConnection crm, DbConnection source, DbTransaction transaction, CrmOidcFamilyLogoutV1 owner,
        CrmOidcGrantRevocationV1[] grants, IReadOnlySet<Guid> alreadyRevoked, CancellationToken ct)
    {
        await using var context = await CreateContextAsync(source, transaction, ct);
        foreach (var existing in grants.Where(g => alreadyRevoked.Contains(g.GrantId)))
        {
            var aggregate = Aggregate(existing.GrantId);
            if (!await context.CrmIdentitySnapshots.AsNoTracking().AnyAsync(snapshot => snapshot.TenantId == owner.TenantId &&
                    snapshot.AggregateId == aggregate && snapshot.Version == existing.Version && snapshot.EventType == existing.SuccessorType &&
                    snapshot.PayloadSha256 == existing.SuccessorHash && !snapshot.IsDeleted, ct))
                throw new CrmOidcFamilyLogoutUnavailableException();
        }

        var pending = grants.Where(g => !alreadyRevoked.Contains(g.GrantId)).ToArray();
        if (pending.Length == 0) return;
        var current = await AppendRevocationsAsync(context, owner.TenantId, owner.SubjectId,
            pending.Select(g => (g.GrantId, g.AccessExpiresAtUtc)), ct);
        if (!current.SequenceEqual(pending)) throw new CrmOidcFamilyLogoutUnavailableException();
        try
        {
            await CrmInquiryTenantPauseV1.AdvanceConnectedIdentitiesAsync(crm, owner.TenantId, owner.StoreId,
                current.Select(g => (Aggregate(g.GrantId), g.Version)).ToArray(), ct);
        }
        catch (TenantManagementExceptionV1) { throw new CrmOidcFamilyLogoutUnavailableException(); }
    }

    private async Task<CrmOidcGrantRevocationV1[]> AppendRevocationsAsync(CP6Context context, Guid tenant, Guid subject,
        IEnumerable<(Guid GrantId, DateTime AccessExpiresAtUtc)> grants, CancellationToken ct)
    {
        var writer = new IdentitySnapshotWriter(context, identity);
        var captured = new Dictionary<string, CrmUserSealAdvanceV1>(StringComparer.Ordinal);
        context.CaptureCrmUserSeal = (sourceTenant, sourceSubject, advance) =>
        {
            if (sourceTenant != tenant || sourceSubject != subject) throw new CrmOidcFamilyLogoutUnavailableException();
            captured.Add(advance.Aggregate, advance);
        };
        var plan = new List<CrmOidcGrantRevocationV1>();
        foreach (var (id, expiry) in grants)
        {
            await writer.RevokeTokenAsync(tenant, id.ToString("D"), $"user:{subject:D}",
                new DateTimeOffset(DateTime.SpecifyKind(expiry, DateTimeKind.Utc)), ct);
            if (!captured.Remove(Aggregate(id), out var advance) || captured.Count != 0)
                throw new CrmOidcFamilyLogoutUnavailableException();
            plan.Add(new(id, expiry, advance.Version, advance.PredecessorType, advance.PredecessorHash, advance.SuccessorType, advance.IntentHash));
        }
        await context.SaveChangesAsync(ct);
        return plan.ToArray();
    }

    private string Aggregate(Guid grant) => IdentityEventContracts.TokenAggregate(identity.Options.Issuer, grant.ToString("D"));

    private Task<CP6Context> CreateContextAsync(DbConnection source, DbTransaction transaction, CancellationToken ct)
        => DatabaseSharedContext.CreateAsync<CP6Context>(database, source, transaction, DatabaseContextKind.Core, options => new(options), ct);
}
