using System.Data;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using CP6.Core.EFDbContext;
using CP6.Entity.DomainModels.Sys;
using CP6.Platform.EntityFramework;
using Dapper;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace CP6.Core.Services.CrmIdentity;

/// <summary>All writes, including priority enqueue, enlist in the caller's business transaction.</summary>
public sealed class IdentitySnapshotWriter(CP6Context db, CrmIdentityRuntime runtime)
{
    public async Task WriteCapturedAsync(IdentityChangeCapture capture, CancellationToken cancellationToken = default)
    {
        RequireTransaction();
        foreach (var (tenant, aggregate) in capture.Targets.OrderBy(x => x.Tenant).ThenBy(x => x.Aggregate, StringComparer.Ordinal))
            await WriteCurrentAsync(tenant, aggregate, cancellationToken).ConfigureAwait(false);
        foreach (var tenant in capture.DisabledTenants.Order())
            await RevokeTenantTokensAsync(tenant, cancellationToken).ConfigureAwait(false);
        foreach (var (tenant, user) in capture.InvalidatedUsers.OrderBy(x => x.Tenant).ThenBy(x => x.User))
            if (!capture.DisabledTenants.Contains(tenant))
                await RevokeBrowserGrantsAsync(tenant, user, null, cancellationToken).ConfigureAwait(false);
        foreach (var (tenant, user, family) in capture.RevokedFamilies.OrderBy(x => x.Tenant).ThenBy(x => x.Family))
            if (!capture.DisabledTenants.Contains(tenant) && !capture.InvalidatedUsers.Contains((tenant, user)))
                await RevokeBrowserGrantsAsync(tenant, user, family, cancellationToken).ConfigureAwait(false);
    }

    public async Task WriteCurrentAsync(Guid tenant, string aggregate, CancellationToken cancellationToken = default)
    {
        RequireTransaction();
        if (!runtime.Options.Tenants.ContainsKey(tenant)) return;
        // Acquire the aggregate/range lock before reading its final business facts. A conflict rolls
        // back the entire command; versions are never allocated from a process counter.
        var previous = await LoadLockedAsync(tenant, aggregate, cancellationToken).ConfigureAwait(false);
        object data;
        string type;
        bool deleted;
        if (aggregate == $"tenant:{tenant:D}")
        {
            var row = await db.Sys_Tenants.AsNoTracking().SingleOrDefaultAsync(x => x.Id == tenant, cancellationToken).ConfigureAwait(false);
            data = new TenantIdentityData(tenant, row?.Enable == true, 0, Utc(row?.ExpireDate));
            type = IdentityEventContracts.TenantChanged;
            deleted = row is null;
        }
        else if (aggregate.StartsWith("user:", StringComparison.Ordinal) && Guid.TryParseExact(aggregate[5..], "D", out var userId))
        {
            var row = await db.Sys_Users.IgnoreQueryFilters().AsNoTracking()
                .SingleOrDefaultAsync(x => x.TenantId == tenant && x.Id == userId, cancellationToken).ConfigureAwait(false);
            var roles = await db.Sys_UserRoles.IgnoreQueryFilters().AsNoTracking()
                .Where(x => x.TenantId == tenant && x.UserId == userId).Select(x => x.RoleId).ToListAsync(cancellationToken).ConfigureAwait(false);
            if (row?.RoleId is int primary) roles.Add(primary);
            data = new UserIdentityData(tenant, userId, row?.DeptId, row?.Enable == true, 0,
                row is null ? [] : roles.Distinct().Order().Select(r => $"role:{r}").ToArray(), row?.ManagerId,
                row?.AuthenticationEpoch ?? Guid.Empty, row?.MustChangePassword == true, Utc(row?.PasswordChangedAt),
                runtime.Options.PasswordMaxAgeDays > 0 ? Utc(row?.PasswordChangedAt?.AddDays(runtime.Options.PasswordMaxAgeDays)) : null,
                Utc(row?.LockedUntil));
            type = IdentityEventContracts.UserChanged;
            deleted = row is null;
        }
        else if (aggregate.StartsWith("department:", StringComparison.Ordinal) && Guid.TryParseExact(aggregate[11..], "D", out var departmentId))
        {
            var row = await db.Sys_Depts.IgnoreQueryFilters().AsNoTracking()
                .SingleOrDefaultAsync(x => x.TenantId == tenant && x.Id == departmentId, cancellationToken).ConfigureAwait(false);
            data = new DepartmentIdentityData(tenant, departmentId, row?.ParentId, row?.Path ?? $"/{departmentId:D}/",
                row?.Enable == true, 0, row?.LeaderId);
            type = IdentityEventContracts.DepartmentChanged;
            deleted = row is null;
        }
        else if (aggregate.StartsWith("permission:role:", StringComparison.Ordinal) && int.TryParse(aggregate[16..], out var roleId) && roleId > 0)
        {
            data = await IdentityPermissionSnapshot.ReadAsync(db, tenant, roleId, cancellationToken).ConfigureAwait(false);
            type = IdentityEventContracts.PermissionChanged;
            deleted = !await db.Sys_Roles.IgnoreQueryFilters().AnyAsync(x => x.TenantId == tenant && x.RoleId == roleId, cancellationToken).ConfigureAwait(false);
        }
        else throw new InvalidOperationException("C02_UNKNOWN_IDENTITY_AGGREGATE");
        await WriteDataAsync(tenant, aggregate, type, data, deleted, previous, cancellationToken).ConfigureAwait(false);
    }

    public async Task RevokeTokenAsync(Guid tenant, string jti, string subject, DateTimeOffset expiry,
        CancellationToken cancellationToken = default)
    {
        RequireTransaction();
        if (!runtime.Options.Tenants.ContainsKey(tenant)) return;
        var aggregate = IdentityEventContracts.TokenAggregate(runtime.Options.Issuer, jti);
        var previous = await LoadLockedAsync(tenant, aggregate, cancellationToken).ConfigureAwait(false);
        await WriteDataAsync(tenant, aggregate, IdentityEventContracts.TokenRevoked,
            new TokenRevokedIdentityData(tenant, runtime.Options.Issuer, jti, subject, expiry, 0),
            false, previous, cancellationToken).ConfigureAwait(false);
    }

    public async Task RevokeTenantTokensAsync(Guid tenant, CancellationToken cancellationToken = default)
    {
        RequireTransaction();
        if (!runtime.Options.Tenants.ContainsKey(tenant)) return;
        await RevokeBrowserGrantsAsync(tenant, null, null, cancellationToken).ConfigureAwait(false);
        var command = new CommandDefinition("""
            UPDATE crm_identity.ServiceToken SET RevokedAtUtc=SYSUTCDATETIME()
            OUTPUT inserted.Jti, inserted.ClientId, inserted.ExpiresAtUtc
            WHERE TenantId=@tenant AND Issuer=@issuer AND RevokedAtUtc IS NULL
            """, new { tenant, issuer = runtime.Options.Issuer }, db.Database.CurrentTransaction!.GetDbTransaction(), cancellationToken: cancellationToken);
        var tokens = await db.Database.GetDbConnection().QueryAsync<RevokedServiceToken>(command).ConfigureAwait(false);
        foreach (var token in tokens.OrderBy(x => x.Jti, StringComparer.Ordinal))
            await RevokeTokenAsync(tenant, token.Jti, "service:" + token.ClientId, token.ExpiresAtUtc, cancellationToken).ConfigureAwait(false);
    }

    private async Task WriteDataAsync(Guid tenant, string aggregate, string type, object value, bool deleted,
        CrmIdentitySnapshot? previous, CancellationToken cancellationToken)
    {
        var data = JsonSerializer.SerializeToNode(value, value.GetType(), IdentityEventContracts.Json)!.AsObject();
        data["deleted"] = deleted;
        data["version"] = previous?.Version ?? 0;
        var comparable = data.ToJsonString(IdentityEventContracts.Json);
        if (previous is not null && previous.IsDeleted == deleted && previous.EventType == type &&
            previous.PayloadSha256 == IdentityEventContracts.Hash(Encoding.UTF8.GetBytes(comparable))) return;
        var version = checked((previous?.Version ?? 0) + 1);
        data["version"] = version;
        var payload = data.ToJsonString(IdentityEventContracts.Json);
        var command = Guid.NewGuid().ToString("N");
        var envelope = IdentityEventContracts.Create(tenant, aggregate, version, type, data,
            runtime.Options.Tenants[tenant], command, command, runtime.Clock);
        if (!runtime.Validator.Validate(envelope).IsValid) throw new InvalidOperationException("C02_IDENTITY_CONTRACT_INVALID");
        var snapshot = previous ?? new CrmIdentitySnapshot { TenantId = tenant, AggregateId = aggregate };
        snapshot.Version = version;
        snapshot.EventType = type;
        snapshot.PayloadJson = payload;
        snapshot.PayloadSha256 = IdentityEventContracts.Hash(Encoding.UTF8.GetBytes(payload));
        snapshot.IsDeleted = deleted;
        snapshot.UpdatedAtUtc = runtime.Clock.GetUtcNow();
        if (previous is null) db.CrmIdentitySnapshots.Add(snapshot);

        var priority = type == IdentityEventContracts.TokenRevoked || data["enabled"]?.GetValue<bool>() == false;
        if (!priority)
        {
            new Cp6OutboxStore<CP6Context>(db, runtime.Validator, runtime.Clock).Enqueue(envelope);
            return;
        }
        await using var queue = new IdentityMessagingContext(new DbContextOptionsBuilder<IdentityMessagingContext>()
            .UseSqlServer(db.Database.GetDbConnection()).Options);
        await queue.Database.UseTransactionAsync(db.Database.CurrentTransaction!.GetDbTransaction(), cancellationToken).ConfigureAwait(false);
        new Cp6OutboxStore<IdentityMessagingContext>(queue, runtime.Validator, runtime.Clock).Enqueue(envelope);
        await queue.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    private Task<CrmIdentitySnapshot?> LoadLockedAsync(Guid tenant, string aggregate, CancellationToken cancellationToken)
    {
        var tracked = db.CrmIdentitySnapshots.Local.SingleOrDefault(x => x.TenantId == tenant && x.AggregateId == aggregate);
        if (tracked is not null) return Task.FromResult<CrmIdentitySnapshot?>(tracked);
        return db.CrmIdentitySnapshots.FromSqlInterpolated($"SELECT * FROM crm_identity.Snapshot WITH (UPDLOCK,HOLDLOCK) WHERE TenantId={tenant} AND AggregateId={aggregate}")
            .SingleOrDefaultAsync(cancellationToken);
    }

    private async Task RevokeBrowserGrantsAsync(Guid tenant, Guid? user, Guid? family, CancellationToken cancellationToken)
    {
        var command = new CommandDefinition("""
            UPDATE g SET Revoked=1
            OUTPUT inserted.Id, inserted.SubjectId, inserted.OrganizationId, inserted.AccessExpiresAtUtc
            FROM dbo.CrmOidcGrant g
            WHERE g.OrganizationId=@tenant AND (@user IS NULL OR g.SubjectId=@user) AND g.Revoked=0
              AND (@family IS NULL OR EXISTS (SELECT 1 FROM dbo.Sys_RefreshTokens r
                WHERE r.BrowserSessionId=@family AND r.UserId=g.SubjectId AND r.TenantId=g.OrganizationId
                  AND r.TokenHash COLLATE Latin1_General_100_BIN2=g.SourceRefreshHash))
            """, new { tenant, user, family }, db.Database.CurrentTransaction!.GetDbTransaction(),
            cancellationToken: cancellationToken);
        var grants = await db.Database.GetDbConnection().QueryAsync<RevokedGrant>(command).ConfigureAwait(false);
        foreach (var grant in grants.OrderBy(x => x.Id))
            await RevokeTokenAsync(grant.OrganizationId, grant.Id.ToString("D"), $"user:{grant.SubjectId:D}",
                new DateTimeOffset(DateTime.SpecifyKind(grant.AccessExpiresAtUtc, DateTimeKind.Utc)), cancellationToken).ConfigureAwait(false);
    }

    private void RequireTransaction()
    {
        if (!db.Database.IsSqlServer() || db.Database.CurrentTransaction is null)
            throw new InvalidOperationException("C02_REQUIRES_CALLER_SQL_TRANSACTION");
    }

    private static DateTimeOffset? Utc(DateTime? value) => value is DateTime date ? new DateTimeOffset(date).ToUniversalTime() : null;
    private sealed class RevokedGrant
    {
        public Guid Id { get; set; }
        public Guid SubjectId { get; set; }
        public Guid OrganizationId { get; set; }
        public DateTime AccessExpiresAtUtc { get; set; }
    }
    private sealed class RevokedServiceToken
    {
        public string Jti { get; set; } = "";
        public string ClientId { get; set; } = "";
        public DateTimeOffset ExpiresAtUtc { get; set; }
    }
}
