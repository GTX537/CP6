using System.Data.Common;
using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Persistence;
using CP6.Core.Services.Sys;
using Dapper;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace CP6.WebApi.Services;

public sealed partial class SqlCrmOidcGrantStore
{
    // The original auth endpoint supplies its proven source while it still holds the Main root.
    // Capture uses that transaction and only the accepted local intent survives its first commit.
    internal async Task<CrmOidcFamilyLogoutV1?> TryStageWebFamilyCloseAsync(CP6Context source,
        WebAuthenticationFamilyClosureV1 close, CancellationToken ct)
    {
        var db = source.Database.GetDbConnection();
        var tx = source.Database.CurrentTransaction!.GetDbTransaction();
        var accepted = await ReadAcceptedFamilyAsync(db, tx, close.TenantId, close.FamilyId, ct);
        if (accepted is not null)
        {
            if (accepted.SubjectId != close.UserId) throw new CrmOidcFamilyLogoutUnavailableException();
            return accepted.CompletedAtUtc is null ? accepted : null;
        }

        var store = _storeFence?.RegisteredStore(close.TenantId);
        if (store is null) return null;
        var grants = await ReadFamilyGrantsAsync(db, tx, close.TenantId, close.UserId, close.FamilyId, [], ct);
        if (grants.Length == 0) return null;
        if (_familySeal is null || _identity is null || !_identity.Options.Tenants.ContainsKey(close.TenantId))
            throw new CrmOidcFamilyLogoutUnavailableException();

        var issuer = _identity.Options.Issuer;
        var grantPlan = await _familySeal.CaptureAsync(db, tx, close.TenantId, close.UserId, grants, ct);
        await db.ExecuteAsync(new CommandDefinition("SET LOCAL synchronous_commit='on'", transaction: tx, cancellationToken: ct));
        await db.ExecuteAsync(new CommandDefinition("""
            INSERT INTO cp6_authentication_v1.crm_oidc_family_logouts_v1
              (tenant_id,family_id,subject_id,store_id,issuer,request_hash,grant_plan,web_close_plan)
            VALUES(@TenantId,@FamilyId,@UserId,@store,@issuer,@requestHash,CAST(@grantPlanJson AS jsonb),CAST(@webCloseJson AS jsonb))
            """, new { close.TenantId, close.FamilyId, close.UserId, store, issuer, requestHash = HashWebClose(close),
                grantPlanJson = JsonSerializer.Serialize(grantPlan), webCloseJson = JsonSerializer.Serialize(close) }, tx, cancellationToken: ct));
        return (await ReadAcceptedFamilyAsync(db, tx, close.TenantId, close.FamilyId, ct))!;
    }

    internal static WebAuthenticationFamilyClosureV1? ReadWebClose(CrmOidcFamilyLogoutV1 owner)
    {
        if (owner.WebClosePlanJson is null) return null;
        var close = JsonSerializer.Deserialize<WebAuthenticationFamilyClosureV1>(owner.WebClosePlanJson)!;
        if (close.TenantId != owner.TenantId || close.UserId != owner.SubjectId || close.FamilyId != owner.FamilyId ||
            HashWebClose(close) != owner.RequestHash)
            throw new CrmOidcFamilyLogoutUnavailableException();
        return close;
    }

    private static string HashWebClose(WebAuthenticationFamilyClosureV1 close)
        => CrmOidcCrypto.Hash(JsonSerializer.Serialize(close with { LogoutAudit = null }));

    private async Task ApplyWebCloseAsync(DbConnection db, DbTransaction tx, WebAuthenticationFamilyClosureV1 close, CancellationToken ct)
    {
        await using var context = await DatabaseSharedContext.CreateAsync<CP6Context>(_database, db, tx,
            DatabaseContextKind.Core, options => new(options), ct);
        try
        {
            await close.RequireNativeFamilyAsync(context, ct);
            if (!await close.CoversLiveFamilyAsync(context, ct)) throw new CrmOidcFamilyLogoutUnavailableException();
            await close.ApplyWithinRootAsync(context, ct);
        }
        catch (InvalidOperationException error) when (error.Message == "AUTH_SESSION_AUTHORITY_UNAVAILABLE")
        {
            throw new CrmOidcFamilyLogoutUnavailableException();
        }
    }
}
