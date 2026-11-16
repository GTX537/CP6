using System.Data.Common;
using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Persistence;
using CP6.Core.Services.Sys;
using CP6.Entity.DomainModels.Sys;
using Dapper;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace CP6.WebApi.Services;

internal sealed record CrmAccessRetirementAuditV1(Guid TenantId, Guid UserId, string? UserName, string? Ip,
    string? UserAgent, string? Reason, bool BestEffort)
{
    internal Sys_SecurityLog Create()
    {
        var audit = SecurityAuditService.CreateRecord(SecurityEventType.Logout, UserId, UserName, null, Ip, UserAgent, Reason);
        audit.TenantId = TenantId;
        return audit;
    }
}

internal sealed record CrmAuthSourceRetirementV1
{
    public Guid OperationId { get; init; }
    public Guid TenantId { get; init; }
    public Guid SubjectId { get; init; }
    public Guid StoreId { get; init; }
    public string Issuer { get; init; } = "";
    public string? SourceJti { get; init; }
    public string RefreshHash { get; init; } = "";
    public string? ReplacementHash { get; init; }
    public string? AuditJson { get; init; }
    public string GrantPlanJson { get; init; } = "";
    public DateTime? CompletedAtUtc { get; init; }
}

public sealed partial class SqlCrmOidcGrantStore
{
    internal async Task<CrmAuthSourceRetirementV1?> TryStageAccessRetirementAsync(CP6Context source, Guid tenant, Guid subject,
        string jti, CrmAccessRetirementAuditV1 audit, CancellationToken ct)
    {
        var db = source.Database.GetDbConnection();
        var tx = source.Database.CurrentTransaction!.GetDbTransaction();
        var hash = await ReadAccessRefreshHashAsync(db, tx, tenant, subject, jti, ct);
        if (hash is null) return null;
        return await StageSourceRetirementAsync(db, tx, new()
        {
            TenantId = tenant, SubjectId = subject, SourceJti = jti, RefreshHash = hash,
            AuditJson = JsonSerializer.Serialize(audit)
        }, ct);
    }

    internal Task<CrmAuthSourceRetirementV1?> TryStageRefreshReuseAsync(CP6Context source, RefreshReuseSourceV1 proof, CancellationToken ct)
        => StageSourceRetirementAsync(source.Database.GetDbConnection(), source.Database.CurrentTransaction!.GetDbTransaction(),
            new() { TenantId = proof.TenantId, SubjectId = proof.UserId, RefreshHash = proof.RefreshHash,
                ReplacementHash = proof.ReplacementHash }, ct);

    private async Task<CrmAuthSourceRetirementV1?> StageSourceRetirementAsync(DbConnection db, DbTransaction tx,
        CrmAuthSourceRetirementV1 proposed, CancellationToken ct)
    {
        var accepted = await db.QuerySingleOrDefaultAsync<CrmAuthSourceRetirementV1>(new CommandDefinition(SourceRetirementSelect + "\n" + """
            WHERE tenant_id=@TenantId AND subject_id=@SubjectId
              AND (source_jti=@SourceJti OR (source_jti IS NULL AND @SourceJti IS NULL AND completed_at_utc IS NULL))
            FOR UPDATE
            """, proposed, tx, cancellationToken: ct));
        if (accepted is not null)
        {
            if (accepted.SourceJti is not null && accepted.RefreshHash != proposed.RefreshHash)
                throw new CrmOidcFamilyLogoutUnavailableException();
            return accepted;
        }

        var store = _storeFence?.RegisteredStore(proposed.TenantId);
        if (store is null) return null;
        var grants = await ReadSourceGrantsAsync(db, tx, proposed, [], ct);
        if (grants.Length == 0) return null;
        if (_familySeal is null || _identity is null || !_identity.Options.Tenants.ContainsKey(proposed.TenantId))
            throw new CrmOidcFamilyLogoutUnavailableException();
        if (proposed.SourceJti is null)
            await RequireOriginalReuseAsync(db, tx, proposed, requireDelivered: true, ct);

        var plan = await _familySeal.CaptureAsync(db, tx, proposed.TenantId, proposed.SubjectId, grants, ct);
        proposed = proposed with { OperationId = Guid.NewGuid(), StoreId = store.Value, Issuer = _identity.Options.Issuer,
            GrantPlanJson = JsonSerializer.Serialize(plan) };
        await db.ExecuteAsync(new CommandDefinition("SET LOCAL synchronous_commit='on'", transaction: tx, cancellationToken: ct));
        await db.ExecuteAsync(new CommandDefinition("""
            INSERT INTO cp6_authentication_v1.crm_auth_source_retirements_v1
              (operation_id,tenant_id,subject_id,store_id,issuer,source_jti,refresh_hash,replacement_hash,access_audit,grant_plan)
            VALUES (@OperationId,@TenantId,@SubjectId,@StoreId,@Issuer,@SourceJti,@RefreshHash,@ReplacementHash,
              CAST(@AuditJson AS jsonb),CAST(@GrantPlanJson AS jsonb))
            """, proposed, tx, cancellationToken: ct));
        return (await ReadSourceRetirementAsync(db, tx, proposed.OperationId, ct))!;
    }

    internal async Task<IReadOnlyList<CrmAuthSourceRetirementV1>> ReadPendingSourceRetirementsAsync(Guid after, CancellationToken ct)
    {
        if (_familySeal is null || !IsPostgreSql) return [];
        await using var db = _connections.Create(_connectionString);
        return (await db.QueryAsync<CrmAuthSourceRetirementV1>(new CommandDefinition(SourceRetirementSelect + "\n" + """
            WHERE completed_at_utc IS NULL AND operation_id>@after ORDER BY operation_id LIMIT 32
            """, new { after }, cancellationToken: ct))).AsList();
    }

    internal async Task CompleteSourceRetirementForRequestAsync(CrmAuthSourceRetirementV1 accepted, CancellationToken ct)
    {
        try { await CompleteSourceRetirementAsync(accepted, ct); }
        catch (Exception error) when (error is CrmOidcFamilyLogoutUnavailableException or DbException or DbUpdateException)
        {
            // This request already owns the accepted proof. Only its exact durable terminal state resolves a lost commit.
            try
            {
                await using var db = _connections.Create(_connectionString);
                var readback = await ReadSourceRetirementAsync(db, null, accepted.OperationId, ct);
                if (readback is { CompletedAtUtc: not null } && SameSourceRetirement(accepted, readback)) return;
            }
            catch (DbException) { }
            throw new RefreshCompletionUnknownException(error);
        }
    }

    internal async Task CompleteSourceRetirementAsync(CrmAuthSourceRetirementV1 accepted, CancellationToken ct)
    {
        if (accepted.CompletedAtUtc.HasValue) return;
        var seal = _familySeal ?? throw new CrmOidcFamilyLogoutUnavailableException();
        if (_storeFence?.RegisteredStore(accepted.TenantId) != accepted.StoreId ||
            _identity is null || _identity.Options.Issuer != accepted.Issuer || !_identity.Options.Tenants.ContainsKey(accepted.TenantId))
            throw new CrmOidcFamilyLogoutUnavailableException();
        var expected = JsonSerializer.Deserialize<CrmOidcGrantRevocationV1[]>(accepted.GrantPlanJson)!;

        // First connection is outside the Main authentication root, including on recovery.
        await using var crm = await seal.OpenAsync(accepted.TenantId, accepted.StoreId, ct);
        await InAuthenticationRootAsync(async (db, tx) =>
        {
            var owner = await ReadSourceRetirementAsync(db, tx, accepted.OperationId, ct);
            if (owner is null || !SameSourceRetirement(accepted, owner))
                throw new CrmOidcFamilyLogoutUnavailableException();
            if (owner.CompletedAtUtc.HasValue) return true;

            var current = await ReadSourceGrantsAsync(db, tx, owner, expected.Select(grant => grant.GrantId).ToArray(), ct);
            if (!current.Select(grant => (grant.GrantId, grant.AccessExpiresAtUtc))
                .SequenceEqual(expected.Select(grant => (grant.GrantId, grant.AccessExpiresAtUtc))))
                throw new CrmOidcFamilyLogoutUnavailableException();

            await ApplySourceRetirementAsync(db, tx, owner, ct);
            var alreadyRevoked = current.Where(grant => grant.Revoked).Select(grant => grant.GrantId).ToHashSet();
            await seal.SealAndAppendAsync(crm, db, tx, owner.TenantId, owner.SubjectId, owner.StoreId, expected, alreadyRevoked, ct);
            await RevokeOwnGrantsAsync(db, tx, owner.TenantId, owner.SubjectId, expected, alreadyRevoked, ct);
            await db.ExecuteAsync(new CommandDefinition("""
                UPDATE cp6_authentication_v1.crm_auth_source_retirements_v1 SET completed_at_utc=clock_timestamp()
                WHERE operation_id=@OperationId AND completed_at_utc IS NULL
                """, owner, tx, cancellationToken: ct));
            return true;
        }, ct);
    }

    private async Task ApplySourceRetirementAsync(DbConnection db, DbTransaction tx, CrmAuthSourceRetirementV1 owner, CancellationToken ct)
    {
        await using var context = await DatabaseSharedContext.CreateAsync<CP6Context>(_database, db, tx,
            DatabaseContextKind.Core, options => new(options), ct);
        if (owner.SourceJti is { } jti)
        {
            if (await ReadAccessRefreshHashAsync(db, tx, owner.TenantId, owner.SubjectId, jti, ct) != owner.RefreshHash)
                throw new CrmOidcFamilyLogoutUnavailableException();
            var audit = JsonSerializer.Deserialize<CrmAccessRetirementAuditV1>(owner.AuditJson!)!;
            await new AuthenticationInteractionStoreV1(context).RevokeAccessWithinRootAsync(jti, audit.Create(), audit.BestEffort, ct);
            return;
        }

        await RequireOriginalReuseAsync(db, tx, owner, requireDelivered: false, ct);
        await new RefreshReuseSourceV1(owner.TenantId, owner.SubjectId, owner.RefreshHash, owner.ReplacementHash!)
            .ApplyWithinRootAsync(context, ct);
    }

    private static Task<string?> ReadAccessRefreshHashAsync(DbConnection db, DbTransaction tx, Guid tenant, Guid subject,
        string jti, CancellationToken ct) => db.QuerySingleOrDefaultAsync<string>(new CommandDefinition("""
            SELECT a.refresh_hash FROM cp6_authentication_v1.access_sessions a
            JOIN public."Sys_RefreshTokens" r ON r."TokenHash"=a.refresh_hash
            WHERE a.access_jti=@jti AND r."TenantId"=@tenant AND r."UserId"=@subject AND r."ClientKind"='Web'
            """, new { tenant, subject, jti }, tx, cancellationToken: ct));

    private static async Task RequireOriginalReuseAsync(DbConnection db, DbTransaction tx, CrmAuthSourceRetirementV1 owner,
        bool requireDelivered, CancellationToken ct)
    {
        if (!await db.ExecuteScalarAsync<bool>(new CommandDefinition("""
            SELECT EXISTS(SELECT 1 FROM public."Sys_RefreshTokens" p
              JOIN public."Sys_RefreshTokens" r ON r."TokenHash"=p."ReplacedByTokenHash"
              JOIN cp6_authentication_v1.session_evidence s ON s.refresh_hash=r."TokenHash"
              WHERE p."TokenHash"=CAST(@RefreshHash AS bpchar) AND r."TokenHash"=CAST(@ReplacementHash AS bpchar)
                AND s.parent_refresh_hash=p."TokenHash" AND p."TenantId"=@TenantId AND p."UserId"=@SubjectId
                AND r."TenantId"=p."TenantId" AND r."UserId"=p."UserId" AND r."ClientKind"=p."ClientKind"
                AND r."DeviceId" IS NOT DISTINCT FROM p."DeviceId" AND p."RevokedAt" IS NOT NULL
                AND (NOT @requireDelivered OR (NOT r."CompletionPending" AND r."CompletionRejectedAtUtc" IS NULL AND s.rejected_at IS NULL)))
            """, new { owner.RefreshHash, owner.ReplacementHash, owner.TenantId, owner.SubjectId, requireDelivered },
            tx, cancellationToken: ct))) throw new CrmOidcFamilyLogoutUnavailableException();
    }

    private static async Task<CrmOidcFamilyGrantV1[]> ReadSourceGrantsAsync(DbConnection db, DbTransaction tx,
        CrmAuthSourceRetirementV1 source, Guid[] includeRevoked, CancellationToken ct)
    {
        var rows = (await db.QueryAsync<SourceGrant>(new CommandDefinition("""
            SELECT "Id" AS "GrantId","AccessExpiresAtUtc","Revoked","SourceRefreshHash"
            FROM public."CrmOidcGrant" WHERE "OrganizationId"=@TenantId AND "SubjectId"=@SubjectId
              AND (@SourceJti IS NULL OR "SourceJti"=CAST(@SourceJti AS bpchar))
              AND (NOT "Revoked" OR "Id"=ANY(@includeRevoked))
            ORDER BY "Id" FOR UPDATE
            """, new { source.TenantId, source.SubjectId, source.SourceJti, includeRevoked }, tx, cancellationToken: ct))).AsList();
        if (source.SourceJti is not null && rows.Any(grant => grant.SourceRefreshHash != source.RefreshHash))
            throw new CrmOidcFamilyLogoutUnavailableException();
        return rows.Select(grant => new CrmOidcFamilyGrantV1(grant.GrantId, grant.AccessExpiresAtUtc, grant.Revoked)).ToArray();
    }

    private sealed record SourceGrant(Guid GrantId, DateTime AccessExpiresAtUtc, bool Revoked, string SourceRefreshHash);

    private const string SourceRetirementSelect = """
        SELECT operation_id AS "OperationId",tenant_id AS "TenantId",subject_id AS "SubjectId",store_id AS "StoreId",
          issuer AS "Issuer",source_jti AS "SourceJti",refresh_hash AS "RefreshHash",replacement_hash AS "ReplacementHash",
          access_audit::text AS "AuditJson",grant_plan::text AS "GrantPlanJson",completed_at_utc AS "CompletedAtUtc"
        FROM cp6_authentication_v1.crm_auth_source_retirements_v1
        """;

    private static Task<CrmAuthSourceRetirementV1?> ReadSourceRetirementAsync(DbConnection db, DbTransaction? tx, Guid id,
        CancellationToken ct) => db.QuerySingleOrDefaultAsync<CrmAuthSourceRetirementV1>(new CommandDefinition(
            SourceRetirementSelect + " WHERE operation_id=@id" + (tx is null ? "" : " FOR UPDATE"), new { id }, tx, cancellationToken: ct));

    private static bool SameSourceRetirement(CrmAuthSourceRetirementV1 accepted, CrmAuthSourceRetirementV1 current)
        => accepted == current with { CompletedAtUtc = accepted.CompletedAtUtc };
}
