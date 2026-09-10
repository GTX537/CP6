using System.Text.Json;
using CP6.Core.Services.Common;
using CP6.Core.Services.CrmIdentity;
using CP6.Core.Services.Platform;
using CP6.Core.Services.Sys;
using CP6.Entity.DomainModels.Sys;
using CP6.WebApi.Services;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

sealed partial class IdentitySqlFixture
{
    public async Task ExternalTransactionRollbackAsync()
    {
        foreach (var asynchronous in new[] { false, true })
        {
            var tenant = await SeedAsync();
            await using var db = Create(tenant);
            await using var transaction = await db.Database.BeginTransactionAsync();
            (await db.Sys_RoleDataScopes.SingleAsync()).ScopeType = 6;
            await ExpectFailure(() => asynchronous ? db.SaveChangesAsync() : Task.FromResult(db.SaveChanges()), "C02_IDENTITY_CONTRACT_INVALID");
            // The caller can handle a rejected command and commit other work. The rejected command
            // must leave no business-only state behind in that still-active outer transaction.
            await transaction.CommitAsync();
            Require(await Scalar<int>("SELECT ScopeType FROM dbo.Sys_RoleDataScope WHERE TenantId=@tenant", new { tenant }) == 2 &&
                await CountOutbox(tenant) == 5, "failed identity command escaped caller-owned transaction rollback");
        }
        var guardedTenant = await SeedAsync();
        var mars = new SqlConnectionStringBuilder(connection) { MultipleActiveResultSets = true };
        await using var guarded = new CP6.Core.EFDbContext.CP6Context(new DbContextOptionsBuilder<CP6.Core.EFDbContext.CP6Context>()
            .UseSqlServer(mars.ConnectionString).Options, new TenantContext { CurrentTenantId = guardedTenant }, identity: Runtime(guardedTenant));
        await using var guardedTransaction = await guarded.Database.BeginTransactionAsync();
        (await guarded.Sys_RoleDataScopes.SingleAsync()).ScopeType = 6;
        await ExpectFailure(() => guarded.SaveChangesAsync(), "C02_REQUIRES_SQL_SAVEPOINTS");
        await guardedTransaction.CommitAsync();
        Require(await Scalar<int>("SELECT ScopeType FROM dbo.Sys_RoleDataScope WHERE TenantId=@tenant", new { tenant = guardedTenant }) == 2,
            "unsupported savepoint connection performed a partial command");
    }

    public async Task DepartmentMoveAsync()
    {
        var tenant = await SeedAsync();
        await using var db = Create(tenant);
        var root = await db.Sys_Depts.SingleAsync(x => x.ParentId == null);
        var child = await db.Sys_Depts.SingleAsync(x => x.ParentId != null);
        var target = Guid.NewGuid();
        db.Sys_Depts.Add(new Sys_Dept { Id = target, TenantId = tenant, DeptCode = "TARGET", DeptName = "Fixture target", Path = $"/{target:D}/" });
        await db.SaveChangesAsync();
        await new DeptService(db).MoveAsync(root.Id, target, "fixture");
        foreach (var department in new[] { root, child })
        {
            var row = await SnapshotAsync(tenant, $"department:{department.Id:D}");
            Require(row.Version == 2, "moved descendant version missing");
            using var data = JsonDocument.Parse(row.PayloadJson);
            Require(data.RootElement.GetProperty("path").GetString() == department.Path && department.Path.StartsWith($"/{target:D}/"), "descendant path snapshot differs");
        }
    }

    public async Task RoleChangesAsync()
    {
        var tenant = await SeedAsync();
        await using var db = Create(tenant);
        var user = await db.Sys_Users.SingleAsync();
        db.Sys_Roles.Add(new Sys_Role { RoleId = 8, TenantId = tenant, RoleName = "Extra fixture role" });
        db.Sys_UserRoles.AddRange(new Sys_UserRole { TenantId = tenant, UserId = user.Id, RoleId = 7 },
            new Sys_UserRole { TenantId = tenant, UserId = user.Id, RoleId = 8 });
        await db.SaveChangesAsync();
        using (var data = JsonDocument.Parse((await SnapshotAsync(tenant, $"user:{user.Id:D}")).PayloadJson))
            Require(data.RootElement.GetProperty("roles").EnumerateArray().Select(x => x.GetString()).SequenceEqual(new[] { "role:7", "role:8" }), "main and extra roles were not deduplicated");
        db.Sys_RoleActions.RemoveRange(await db.Sys_RoleActions.ToArrayAsync());
        await db.SaveChangesAsync();
        using (var data = JsonDocument.Parse((await SnapshotAsync(tenant, "permission:role:7")).PayloadJson))
            Require(data.RootElement.GetProperty("grants").GetArrayLength() == 0 && data.RootElement.GetProperty("enabled").GetBoolean(), "removing last action did not emit empty grants");
        (await db.Sys_Roles.SingleAsync(x => x.RoleId == 8)).Enable = false;
        await db.SaveChangesAsync();
        Require(await CountOutbox(tenant, true) == 1, "role disable did not receive priority");
        user.RoleId = null;
        db.Sys_UserRoles.RemoveRange(await db.Sys_UserRoles.ToArrayAsync());
        await db.SaveChangesAsync();
        using var removed = JsonDocument.Parse((await SnapshotAsync(tenant, $"user:{user.Id:D}")).PayloadJson);
        Require(removed.RootElement.GetProperty("roles").GetArrayLength() == 0, "empty membership was not published");
    }

    public async Task CredentialRevocationAsync()
    {
        foreach (var twoFactor in new[] { false, true })
        {
            var tenant = await SeedAsync();
            var grant = await GrantAsync(tenant);
            await using var db = Create(tenant);
            var user = await db.Sys_Users.SingleAsync();
            if (twoFactor) { user.TwoFactorEnabled = true; user.TwoFactorSecret = "fixture-two-factor-secret"; }
            else { user.Password = "fixture-new-credential-hash"; user.PasswordChangedAt = DateTime.Now; }
            await db.SaveChangesAsync();
            Require(await Scalar<bool>("SELECT Revoked FROM dbo.CrmOidcGrant WHERE Id=@Id", grant), "credential change left original CRM token active");
            var row = await SnapshotAsync(tenant, IdentityEventContracts.TokenAggregate(Issuer, grant.Id.ToString("D")));
            Require(row.Version == 1 && !row.PayloadJson.Contains("fixture-"), "credential secret entered revocation event");
        }
    }

    public async Task RefreshFamilyAsync()
    {
        var tenant = await SeedAsync();
        var grant = await GrantAsync(tenant);
        var family = await BindFamilyAsync(tenant, grant);
        var replacement = Guid.NewGuid().ToString("N");
        await using (var db = Create(tenant))
        {
            var source = await db.Sys_RefreshTokens.SingleAsync(x => x.TokenHash == grant.SourceRefreshHash);
            source.RevokedAt = DateTime.Now;
            source.ReplacedByTokenHash = replacement;
            db.Sys_RefreshTokens.Add(new Sys_RefreshToken { TenantId = tenant, UserId = grant.SubjectId, BrowserSessionId = family,
                TokenHash = replacement, ExpiresAt = DateTime.Now.AddHours(1) });
            await db.SaveChangesAsync();
        }
        Require(!await Scalar<bool>("SELECT Revoked FROM dbo.CrmOidcGrant WHERE Id=@Id", grant), "normal rotation revoked the CRM grant");
        await using (var db = Create(tenant))
        {
            (await db.Sys_RefreshTokens.SingleAsync(x => x.TokenHash == replacement)).RevokedAt = DateTime.Now;
            await db.SaveChangesAsync();
        }
        Require(await Scalar<bool>("SELECT Revoked FROM dbo.CrmOidcGrant WHERE Id=@Id", grant) && await CountOutbox(tenant, true) == 1,
            "terminal replacement revoke did not find the original family grant");
    }

    public async Task DirectFamilyAsync()
    {
        var tenant = await SeedAsync();
        var grant = await GrantAsync(tenant);
        var family = await BindFamilyAsync(tenant, grant);
        var store = new SqlCrmOidcGrantStore(connection, Runtime(tenant));
        await Execute($"ALTER TABLE crm_identity_priority.Cp6_OutboxMessage WITH NOCHECK ADD CONSTRAINT C02_FamilyReject CHECK (TenantId <> '{tenant:D}');");
        try
        {
            await ExpectFailure(() => store.RevokeSourceFamilyAsync(grant), "C02_FamilyReject");
            Require(!await Scalar<bool>("SELECT Revoked FROM dbo.CrmOidcGrant WHERE Id=@Id", grant), "failed family command committed grant revocation");
            Require(await Scalar<int>("SELECT COUNT(*) FROM dbo.Sys_BrowserSessions WHERE Id=@family AND LoggedOutAtUtc IS NOT NULL", new { family }) == 0, "failed family command committed logout");
            Require(await CountOutbox(tenant, true) == 0, "failed family command committed priority message");
        }
        finally { await Execute("ALTER TABLE crm_identity_priority.Cp6_OutboxMessage DROP CONSTRAINT C02_FamilyReject"); }
        await store.RevokeSourceFamilyAsync(grant);
        await store.RevokeSourceFamilyAsync(grant);
        Require(await CountOutbox(tenant, true) == 1 && await Scalar<int>("SELECT COUNT(*) FROM dbo.Sys_RefreshTokens WHERE BrowserSessionId=@family AND RevokedAt IS NOT NULL", new { family }) == 1,
            "successful family command did not commit exactly once");
    }

    public async Task TenantDisableAsync()
    {
        var tenant = await SeedAsync();
        var grant = await GrantAsync(tenant);
        var jti = Guid.NewGuid().ToString("D");
        await using var db = Create(tenant);
        await new CrmServiceTokenRecordStore(db, Runtime(tenant)).RecordAsync(Issuer, "reader-a", tenant, jti, DateTimeOffset.UtcNow.AddMinutes(5), default);
        (await db.Sys_Tenants.SingleAsync(x => x.Id == tenant)).Enable = false;
        await db.SaveChangesAsync();
        Require(await CountOutbox(tenant, true) == 3 && await Scalar<bool>("SELECT Revoked FROM dbo.CrmOidcGrant WHERE Id=@Id", grant), "tenant disable did not revoke both token classes");
        Require(await Scalar<int>("SELECT COUNT(*) FROM crm_identity.ServiceToken WHERE TenantId=@tenant AND RevokedAtUtc IS NOT NULL", new { tenant }) == 1,
            "tenant service token record remained active");
    }

    public async Task GdprPurgeAsync()
    {
        var tenant = await SeedAsync();
        var grant = await GrantAsync(tenant);
        await using var db = Create(tenant);
        var context = new TenantContext { CurrentTenantId = tenant };
        var service = new GdprService(db, null!, null!, null!, new SecurityAuditService(db), context, identity: Runtime(tenant));
        await service.EraseTenantAsync(tenant, "purge");
        Require(await Scalar<int>("SELECT COUNT(*) FROM dbo.Sys_Tenants WHERE Id=@tenant", new { tenant }) == 0 &&
            await Scalar<int>("SELECT COUNT(*) FROM dbo.Sys_Users WHERE TenantId=@tenant", new { tenant }) == 0, "tenant rows survived purge");
        Require(await Scalar<int>("SELECT COUNT(*) FROM dbo.CrmOidcGrant WHERE OrganizationId=@tenant", new { tenant }) == 0, "purge retained original grant material");
        var snapshots = await db.CrmIdentitySnapshots.AsNoTracking().Where(x => x.TenantId == tenant).ToArrayAsync();
        Require(snapshots.Count(x => x.IsDeleted && x.Version == 2) == 5 && snapshots.Count(x => x.EventType == IdentityEventContracts.TokenRevoked) == 1,
            "purge lost tombstones or original token identity");
        Require(await CountOutbox(tenant, true) == 6, "purge deleted undelivered revocations");
    }

    public async Task ConcurrentBootstrapAsync()
    {
        var tenant = await SeedAsync();
        var manager = Guid.NewGuid();
        async Task Bootstrap()
        {
            await using var db = Create(tenant);
            await new IdentityBootstrapService(db, Runtime(tenant)).InitializeAsync(tenant);
        }
        async Task Change()
        {
            await using var db = Create(tenant);
            (await db.Sys_Users.SingleAsync()).ManagerId = manager;
            await db.SaveChangesAsync();
        }
        // SQL chooses a deadlock victim; retry only that whole rolled-back transaction in a new
        // context, matching the bootstrap worker's ownership/retry boundary.
        await Task.WhenAll(RetryDeadlock(Bootstrap), RetryDeadlock(Bootstrap), RetryDeadlock(Change));
        var row = await Scalar<string>("SELECT PayloadJson FROM crm_identity.Snapshot WHERE TenantId=@tenant AND AggregateId LIKE N'user:%'", new { tenant });
        using var data = JsonDocument.Parse(row);
        Require(data.RootElement.GetProperty("managerId").GetGuid() == manager && data.RootElement.GetProperty("version").GetInt32() == 2 && await CountOutbox(tenant) == 6,
            "bootstrap overwrote or duplicated concurrent user state");
    }

    private static async Task RetryDeadlock(Func<Task> command)
    {
        for (var attempt = 0; ; attempt++)
        {
            try { await command(); return; }
            catch (Exception ex) when (attempt < 4 && IsDeadlock(ex)) { await Task.Delay(50 * (attempt + 1)); }
        }
    }
    private static bool IsDeadlock(Exception ex) => ex is SqlException { Number: 1205 } || ex.InnerException is not null && IsDeadlock(ex.InnerException);
    private async Task<CrmIdentitySnapshot> SnapshotAsync(Guid tenant, string aggregate)
    {
        await using var db = Create(tenant);
        return await db.CrmIdentitySnapshots.SingleAsync(x => x.TenantId == tenant && x.AggregateId == aggregate);
    }
    private async Task<Guid> BindFamilyAsync(Guid tenant, CrmOidcGrant grant)
    {
        var family = Guid.NewGuid();
        await using var db = Create(tenant);
        db.Sys_BrowserSessions.Add(new Sys_BrowserSession { TenantId = tenant, Id = family, UserId = grant.SubjectId, AuthenticationVersion = "fixture-version" });
        db.Sys_RefreshTokens.Add(new Sys_RefreshToken { TenantId = tenant, UserId = grant.SubjectId, BrowserSessionId = family,
            TokenHash = grant.SourceRefreshHash, ExpiresAt = DateTime.Now.AddHours(1) });
        await db.SaveChangesAsync();
        return family;
    }
}
