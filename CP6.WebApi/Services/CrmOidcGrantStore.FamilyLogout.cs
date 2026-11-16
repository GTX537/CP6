using System.Data;
using System.Data.Common;
using System.Text.Json;
using CP6.Core.Persistence;
using CP6.Core.Services.CrmIdentity;
using CP6.Core.Services.Platform;
using Dapper;
using Microsoft.AspNetCore.DataProtection;
using Npgsql;

namespace CP6.WebApi.Services;

internal sealed class CrmOidcFamilyAdmissionClosedException : Exception;
internal sealed class CrmOidcFamilyLogoutUnavailableException : Exception;

internal sealed record CrmOidcFamilyGrantV1(Guid GrantId, DateTime AccessExpiresAtUtc, bool Revoked);

internal sealed record CrmOidcGrantRevocationV1(Guid GrantId, DateTime AccessExpiresAtUtc, int Version,
    string? PredecessorType, string? PredecessorHash, string SuccessorType, string SuccessorHash);

internal sealed class CrmOidcFamilyLogoutV1
{
    public Guid TenantId { get; init; }
    public Guid FamilyId { get; init; }
    public Guid SubjectId { get; init; }
    public Guid StoreId { get; init; }
    public string Issuer { get; init; } = "";
    public Guid? OriginalGrantId { get; init; }
    public string? ClientId { get; init; }
    public string? NonceHash { get; init; }
    public string RequestHash { get; init; } = "";
    public string GrantPlanJson { get; init; } = "";
    public string? WebClosePlanJson { get; init; }
    public DateTime? CompletedAtUtc { get; init; }
}

/// <summary>Seals the original grants using the registered CRM store, then appends their native Main C02 records.</summary>
internal interface ICrmOidcFamilySealV1
{
    Task<CrmOidcGrantRevocationV1[]> CaptureAsync(DbConnection source, DbTransaction transaction, Guid tenant, Guid subject,
        CrmOidcFamilyGrantV1[] grants, CancellationToken ct);

    Task<NpgsqlConnection> OpenAsync(Guid tenant, Guid store, CancellationToken ct);

    Task SealAndAppendAsync(NpgsqlConnection crm, DbConnection source, DbTransaction transaction, Guid tenant, Guid subject,
        Guid store, CrmOidcGrantRevocationV1[] grants, IReadOnlySet<Guid> alreadyRevoked, CancellationToken ct);
}

public sealed partial class SqlCrmOidcGrantStore
{
    private readonly ICrmOidcFamilySealV1? _familySeal;
    private readonly IDataProtector? _logoutTickets;

    private const string PgSaveGrant = """
        DELETE FROM public."CrmOidcGrant" WHERE "Id" IN (
          SELECT g."Id" FROM public."CrmOidcGrant" g
          WHERE g."AccessExpiresAtUtc"<clock_timestamp()-INTERVAL '24 hours'
            AND NOT EXISTS (SELECT 1 FROM public."Sys_RefreshTokens" r
              JOIN cp6_authentication_v1.crm_oidc_family_logouts_v1 p
                ON p.tenant_id=r."TenantId" AND p.family_id=r."BrowserSessionId" AND p.subject_id=r."UserId"
              WHERE p.completed_at_utc IS NULL AND g."SourceRefreshHash"=r."TokenHash" COLLATE "C"
                AND g."OrganizationId"=p.tenant_id AND g."SubjectId"=p.subject_id)
            AND NOT EXISTS (SELECT 1 FROM cp6_authentication_v1.crm_auth_source_retirements_v1 p
              WHERE p.completed_at_utc IS NULL AND p.tenant_id=g."OrganizationId" AND p.subject_id=g."SubjectId"
                AND (p.source_jti IS NULL OR p.source_jti=g."SourceJti"::text COLLATE "C"))
          LIMIT 100);
        INSERT INTO public."CrmOidcGrant"
          ("Id","CodeHash","ClientId","RedirectUri","Challenge","Nonce","SubjectId","OrganizationId","SourceJti","SourceRefreshHash","SecurityStamp",
           "ExpiresAtUtc","SourceExpiresAtUtc","AccessExpiresAtUtc","Consumed","Revoked")
        VALUES (@Id,@CodeHash,@ClientId,@RedirectUri,@Challenge,@Nonce,@SubjectId,@OrganizationId,@SourceJti,@SourceRefreshHash,@SecurityStamp,
           @ExpiresAtUtc,@SourceExpiresAtUtc,@AccessExpiresAtUtc,false,false)
        """;

    private const string PgCleanupTickets = """
        DELETE FROM public."CrmOidcLogout" WHERE "TicketHash" IN (
          SELECT "TicketHash" FROM public."CrmOidcLogout" WHERE "ExpiresAtUtc"<clock_timestamp() LIMIT 100);
        """;

    internal SqlCrmOidcGrantStore(DatabaseOptions database, string connectionString, CrmIdentityRuntime? identity, CrmInquiryTenantPauseV1? fence,
        ICrmOidcFamilySealV1? familySeal, IDataProtectionProvider protection) : this(database, connectionString, identity)
    {
        _storeFence = fence;
        _familySeal = familySeal;
        _logoutTickets = protection.CreateProtector("CP6.CrmOidc.FamilyLogoutContinuation.v1");
    }

    internal bool RequiresNativeFamilyLogout(Guid tenant) => IsPostgreSql && _storeFence?.IsRegistered(tenant) == true;

    private Task SavePostgreSqlGrantAsync(CrmOidcGrant grant) => InAuthenticationRootAsync(async (db, tx) =>
    {
        var family = await ReadFamilyAsync(db, tx, grant, CancellationToken.None);
        if (family?.Pending == true || RequiresNativeFamilyLogout(grant.OrganizationId) && (family is null || family.LoggedOut))
            throw new CrmOidcFamilyAdmissionClosedException();
        if (await db.ExecuteScalarAsync<bool>("""
            SELECT EXISTS (SELECT 1 FROM cp6_authentication_v1.crm_auth_source_retirements_v1 p
              WHERE p.completed_at_utc IS NULL AND p.tenant_id=@OrganizationId AND p.subject_id=@SubjectId
                AND (p.source_jti IS NULL OR p.source_jti=@SourceJti))
              OR EXISTS (SELECT 1 FROM cp6_authentication_v1.access_sessions a
                JOIN cp6_authentication_v1.session_evidence s ON s.refresh_hash=a.refresh_hash
                WHERE a.access_jti=@SourceJti AND a.refresh_hash=@SourceRefreshHash
                  AND (a.revoked_at IS NOT NULL OR s.rejected_at IS NOT NULL))
            """, grant, tx)) throw new CrmOidcFamilyAdmissionClosedException();
        await db.ExecuteAsync(PgSaveGrant, grant, tx);
        return true;
    }, CancellationToken.None);

    internal async Task<string> LogoutRegisteredFamilyAsync(CrmOidcGrant original, string redirect, string requestState,
        string continuationRedirect, CancellationToken ct)
    {
        if (_familySeal is null || _logoutTickets is null || _identity is null || !_identity.Options.Tenants.ContainsKey(original.OrganizationId))
            throw new CrmOidcFamilyLogoutUnavailableException();
        var issuer = _identity.Options.Issuer;
        var store = _storeFence!.RegisteredStore(original.OrganizationId)!.Value;
        var nonceHash = CrmOidcCrypto.Hash(original.Nonce);
        var requestHash = CrmOidcCrypto.Hash(JsonSerializer.Serialize(new
        {
            issuer, original.OrganizationId, original.SubjectId, original.Id, original.ClientId, nonceHash, redirect, requestState
        }));

        var owner = await InAuthenticationRootAsync(async (db, tx) =>
        {
            var family = await ReadFamilyAsync(db, tx, original, ct) ?? throw new CrmOidcFamilyLogoutUnavailableException();
            var accepted = await ReadAcceptedFamilyAsync(db, tx, original.OrganizationId, family.Id, ct);
            if (accepted is not null)
            {
                if (accepted.SubjectId != original.SubjectId || accepted.Issuer != issuer ||
                    accepted.CompletedAtUtc is null && (accepted.StoreId != store || accepted.RequestHash != requestHash))
                    throw new CrmOidcFamilyLogoutUnavailableException();
                return accepted;
            }

            // An earlier lookup may have crossed the existing 24h hint-retention boundary.
            if (!await db.ExecuteScalarAsync<bool>(new CommandDefinition("""
                SELECT EXISTS(SELECT 1 FROM public."CrmOidcGrant" WHERE "Id"=@Id AND "Consumed"
                 AND "AccessExpiresAtUtc">clock_timestamp()-INTERVAL '24 hours')
                """, original, tx, cancellationToken: ct))) throw new CrmOidcFamilyLogoutUnavailableException();
            var grants = await ReadFamilyGrantsAsync(db, tx, original.OrganizationId, original.SubjectId, family.Id, [], ct);
            var plan = await _familySeal.CaptureAsync(db, tx, original.OrganizationId, original.SubjectId, grants, ct);
            var grantPlanJson = JsonSerializer.Serialize(plan);
            await db.ExecuteAsync(new CommandDefinition("""
                INSERT INTO cp6_authentication_v1.crm_oidc_family_logouts_v1
                  (tenant_id,family_id,subject_id,store_id,issuer,original_grant_id,client_id,nonce_hash,request_hash,grant_plan)
                VALUES(@OrganizationId,@Id,@SubjectId,@store,@issuer,@OriginalGrantId,@ClientId,@nonceHash,@requestHash,CAST(@grantPlanJson AS jsonb))
                """, new { original.OrganizationId, family.Id, original.SubjectId, store, issuer, OriginalGrantId = original.Id,
                    original.ClientId, nonceHash, requestHash, grantPlanJson }, tx, cancellationToken: ct));
            return (await ReadAcceptedFamilyAsync(db, tx, original.OrganizationId, family.Id, ct))!;
        }, ct);

        if (owner.CompletedAtUtc is null) await CompleteAcceptedFamilyAsync(owner, ct);
        return await SaveNativeContinuationAsync(original.Id, continuationRedirect, requestHash, ct);
    }

    internal async Task<IReadOnlyList<CrmOidcFamilyLogoutV1>> ReadPendingFamilyLogoutsAsync(Guid after, CancellationToken ct)
    {
        if (_familySeal is null || !IsPostgreSql) return [];
        await using var db = _connections.Create(_connectionString);
        return (await db.QueryAsync<CrmOidcFamilyLogoutV1>(new CommandDefinition(FamilyLogoutSelect + """
             WHERE completed_at_utc IS NULL AND family_id>@after ORDER BY family_id LIMIT 32
            """, new { after }, cancellationToken: ct))).AsList();
    }

    internal async Task CompleteAcceptedFamilyAsync(CrmOidcFamilyLogoutV1 accepted, CancellationToken ct)
    {
        if (accepted.CompletedAtUtc.HasValue) return;
        var seal = _familySeal ?? throw new CrmOidcFamilyLogoutUnavailableException();
        if (_storeFence?.RegisteredStore(accepted.TenantId) != accepted.StoreId || _identity is null ||
            _identity.Options.Issuer != accepted.Issuer || !_identity.Options.Tenants.ContainsKey(accepted.TenantId))
            throw new CrmOidcFamilyLogoutUnavailableException();

        var expected = JsonSerializer.Deserialize<CrmOidcGrantRevocationV1[]>(accepted.GrantPlanJson)!;
        // A refused first CRM connection holds neither the Main root nor a browser-family row lock.
        await using var crm = expected.Length == 0 ? null : await seal.OpenAsync(accepted.TenantId, accepted.StoreId, ct);
        await InAuthenticationRootAsync(async (db, tx) =>
        {
            var owner = await ReadAcceptedFamilyAsync(db, tx, accepted.TenantId, accepted.FamilyId, ct);
            if (owner is null || owner.RequestHash != accepted.RequestHash || owner.StoreId != accepted.StoreId ||
                owner.GrantPlanJson != accepted.GrantPlanJson || owner.WebClosePlanJson != accepted.WebClosePlanJson)
                throw new CrmOidcFamilyLogoutUnavailableException();
            if (owner.CompletedAtUtc.HasValue) return true;

            var webClose = ReadWebClose(owner);
            if (webClose is null) await RequireOriginalOidcSourceAsync(db, tx, owner, ct);

            var current = await ReadFamilyGrantsAsync(db, tx, owner.TenantId, owner.SubjectId, owner.FamilyId,
                expected.Select(g => g.GrantId).ToArray(), ct);
            if (!current.Select(g => (g.GrantId, g.AccessExpiresAtUtc)).SequenceEqual(expected.Select(g => (g.GrantId, g.AccessExpiresAtUtc))))
                throw new CrmOidcFamilyLogoutUnavailableException();

            if (webClose is not null) await ApplyWebCloseAsync(db, tx, webClose, ct);
            var alreadyRevoked = current.Where(g => g.Revoked).Select(g => g.GrantId).ToHashSet();
            if (crm is not null) await seal.SealAndAppendAsync(crm, db, tx, owner.TenantId, owner.SubjectId, owner.StoreId,
                expected, alreadyRevoked, ct);
            await RevokeOwnGrantsAsync(db, tx, owner.TenantId, owner.SubjectId, expected, alreadyRevoked, ct);
            await db.ExecuteAsync(new CommandDefinition("""
                UPDATE public."Sys_BrowserSessions"
                SET "LoggedOutAtUtc"=COALESCE("LoggedOutAtUtc",clock_timestamp()) WHERE "Id"=@FamilyId;
                UPDATE public."Sys_RefreshTokens" SET "RevokedAt"=COALESCE("RevokedAt",clock_timestamp()::timestamp)
                WHERE "BrowserSessionId"=@FamilyId AND "UserId"=@SubjectId AND "TenantId"=@TenantId;
                UPDATE cp6_authentication_v1.crm_oidc_family_logouts_v1 SET completed_at_utc=clock_timestamp()
                WHERE tenant_id=@TenantId AND family_id=@FamilyId AND completed_at_utc IS NULL;
                """, owner, tx, cancellationToken: ct));
            return true;
        }, ct);
    }

    private static async Task RevokeOwnGrantsAsync(DbConnection db, DbTransaction tx, Guid tenant, Guid subject,
        CrmOidcGrantRevocationV1[] expected, IReadOnlySet<Guid> alreadyRevoked, CancellationToken ct)
    {
        var pending = expected.Where(g => !alreadyRevoked.Contains(g.GrantId)).Select(g => g.GrantId).ToArray();
        var updated = await db.ExecuteAsync(new CommandDefinition("""
            UPDATE public."CrmOidcGrant" SET "Revoked"=true
            WHERE "Id"=ANY(@pending) AND "OrganizationId"=@tenant AND "SubjectId"=@subject AND NOT "Revoked"
            """, new { pending, tenant, subject }, tx, cancellationToken: ct));
        if (updated != pending.Length) throw new CrmOidcFamilyLogoutUnavailableException();
    }

    private static async Task RequireOriginalOidcSourceAsync(DbConnection db, DbTransaction tx, CrmOidcFamilyLogoutV1 owner, CancellationToken ct)
    {
        var original = await db.QuerySingleOrDefaultAsync<CrmOidcGrant>(new CommandDefinition("""
            SELECT * FROM public."CrmOidcGrant"
            WHERE "Id"=@OriginalGrantId AND "OrganizationId"=@TenantId AND "SubjectId"=@SubjectId AND "Consumed"
            """, owner, tx, cancellationToken: ct));
        if (original is null || original.ClientId != owner.ClientId || CrmOidcCrypto.Hash(original.Nonce) != owner.NonceHash ||
            (await ReadFamilyAsync(db, tx, original, ct))?.Id != owner.FamilyId)
            throw new CrmOidcFamilyLogoutUnavailableException();
    }

    // Called inside the existing issuer root after validating the live native bearer grant.
    internal static Task<bool> HasPendingAuthenticationRetirementAsync(DbConnection db, DbTransaction tx, Guid tenant, Guid grantId, CancellationToken ct)
        => db.ExecuteScalarAsync<bool>(new CommandDefinition("""
            SELECT EXISTS (SELECT 1 FROM public."CrmOidcGrant" g WHERE g."Id"=@grantId AND g."OrganizationId"=@tenant
              AND (EXISTS (SELECT 1 FROM public."Sys_RefreshTokens" r
                    JOIN cp6_authentication_v1.crm_oidc_family_logouts_v1 p
                      ON p.tenant_id=r."TenantId" AND p.family_id=r."BrowserSessionId" AND p.subject_id=r."UserId"
                    WHERE g."SourceRefreshHash"=r."TokenHash" COLLATE "C" AND p.tenant_id=g."OrganizationId"
                      AND p.subject_id=g."SubjectId" AND p.completed_at_utc IS NULL)
                OR EXISTS (SELECT 1 FROM cp6_authentication_v1.crm_auth_source_retirements_v1 p
                    WHERE p.tenant_id=g."OrganizationId" AND p.subject_id=g."SubjectId" AND p.completed_at_utc IS NULL
                      AND (p.source_jti IS NULL OR p.source_jti=g."SourceJti"::text COLLATE "C"))))
            """, new { tenant, grantId }, tx, cancellationToken: ct));

    private async Task<string> SaveNativeContinuationAsync(Guid grantId, string redirect, string requestHash, CancellationToken ct)
        => await InAuthenticationRootAsync(async (db, tx) =>
        {
            var existing = await db.QuerySingleOrDefaultAsync<string>(new CommandDefinition("""
                SELECT "ResponseTicketProtected" FROM public."CrmOidcLogout"
                WHERE "SourceRequestHash"=@requestHash AND "GrantId"=@grantId AND "ExpiresAtUtc">clock_timestamp()
                FOR UPDATE
                """, new { grantId, requestHash }, tx, cancellationToken: ct));
            if (existing is not null) return _logoutTickets!.Unprotect(existing);

            var raw = CrmOidcCrypto.RandomToken();
            await db.ExecuteAsync(new CommandDefinition(PgCleanupTickets + """
                DELETE FROM public."CrmOidcLogout" WHERE "SourceRequestHash"=@requestHash AND "ExpiresAtUtc"<=clock_timestamp();
                INSERT INTO public."CrmOidcLogout"
                  ("TicketHash","GrantId","RedirectUri","ExpiresAtUtc","SourceRequestHash","ResponseTicketProtected")
                VALUES(@TicketHash,@grantId,@redirect,clock_timestamp()+INTERVAL '1 minute',@requestHash,@Protected)
                """, new { TicketHash = CrmOidcCrypto.Hash(raw), grantId, redirect, requestHash, Protected = _logoutTickets!.Protect(raw) },
                tx, cancellationToken: ct));
            return raw;
        }, ct);

    private async Task<T> InAuthenticationRootAsync<T>(Func<DbConnection, DbTransaction, Task<T>> action, CancellationToken ct)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                await using var db = _connections.Create(_connectionString);
                await db.OpenAsync(ct);
                // Refresh, logout, and fresh OIDC codes share this order: authentication root then browser family.
                await using var tx = await db.BeginTransactionAsync(IsolationLevel.Serializable, ct);
                await db.ExecuteScalarAsync<long>(new CommandDefinition("SELECT cp6_authentication_v1.acquire()", transaction: tx, cancellationToken: ct));
                await db.ExecuteAsync(new CommandDefinition("SET LOCAL synchronous_commit='on'", transaction: tx, cancellationToken: ct));
                var result = await action(db, tx);
                await tx.CommitAsync(ct);
                return result;
            }
            catch (Exception exception) when (attempt < 2 && DatabaseFailureClassifier.Classify(exception).CanRetryTransaction) { }
        }
    }

    private static Task<BrowserFamily?> ReadFamilyAsync(DbConnection db, DbTransaction tx, CrmOidcGrant grant, CancellationToken ct)
        => db.QuerySingleOrDefaultAsync<BrowserFamily>(new CommandDefinition("""
            SELECT f."Id", f."LoggedOutAtUtc" IS NOT NULL AS "LoggedOut",
              EXISTS(SELECT 1 FROM cp6_authentication_v1.crm_oidc_family_logouts_v1 p
                WHERE p.tenant_id=f."TenantId" AND p.family_id=f."Id" AND p.completed_at_utc IS NULL) AS "Pending"
            FROM public."Sys_RefreshTokens" r JOIN public."Sys_BrowserSessions" f ON f."Id"=r."BrowserSessionId"
            WHERE r."TokenHash"=CAST(@SourceRefreshHash AS bpchar) AND r."ClientKind"=CAST('Web' AS bpchar)
              AND r."UserId"=@SubjectId AND r."TenantId"=@OrganizationId AND f."UserId"=@SubjectId AND f."TenantId"=@OrganizationId
            FOR UPDATE OF f
            """, grant, tx, cancellationToken: ct));

    private static async Task<CrmOidcFamilyGrantV1[]> ReadFamilyGrantsAsync(DbConnection db, DbTransaction tx, Guid tenant,
        Guid subject, Guid family, Guid[] includeRevoked, CancellationToken ct)
        => (await db.QueryAsync<CrmOidcFamilyGrantV1>(new CommandDefinition("""
            SELECT g."Id" AS "GrantId",g."AccessExpiresAtUtc",g."Revoked"
            FROM public."CrmOidcGrant" g JOIN public."Sys_RefreshTokens" r ON g."SourceRefreshHash"=r."TokenHash" COLLATE "C"
            WHERE r."BrowserSessionId"=@family AND r."UserId"=@subject AND r."TenantId"=@tenant
              AND g."OrganizationId"=@tenant AND g."SubjectId"=@subject AND (NOT g."Revoked" OR g."Id"=ANY(@includeRevoked))
            ORDER BY g."Id" FOR UPDATE OF g
            """, new { tenant, subject, family, includeRevoked }, tx, cancellationToken: ct))).ToArray();

    private const string FamilyLogoutSelect = """
        SELECT tenant_id AS "TenantId", family_id AS "FamilyId", subject_id AS "SubjectId", store_id AS "StoreId",
          issuer AS "Issuer", original_grant_id AS "OriginalGrantId", client_id AS "ClientId", nonce_hash AS "NonceHash",
          request_hash AS "RequestHash", grant_plan::text AS "GrantPlanJson", web_close_plan::text AS "WebClosePlanJson",
          completed_at_utc AS "CompletedAtUtc"
        FROM cp6_authentication_v1.crm_oidc_family_logouts_v1
        """;

    private static Task<CrmOidcFamilyLogoutV1?> ReadAcceptedFamilyAsync(DbConnection db, DbTransaction tx,
        Guid tenant, Guid family, CancellationToken ct)
        => db.QuerySingleOrDefaultAsync<CrmOidcFamilyLogoutV1>(new CommandDefinition(FamilyLogoutSelect + """
             WHERE tenant_id=@tenant AND family_id=@family FOR UPDATE
            """, new { tenant, family }, tx, cancellationToken: ct));

    private sealed record BrowserFamily(Guid Id, bool LoggedOut, bool Pending);
}
