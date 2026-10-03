using System.Security.Cryptography;
using System.Text;
using CP6.Core.Persistence;
using CP6.Core.Services.Common;
using CP6.Core.Services.CrmIdentity;
using CP6.Core.Services.Platform;
using CP6.Core.Services.Sys;
using CP6.Entity.DomainModels.Sys;
using CP6.Platform.EntityFramework;
using CP6.Platform.Messaging;
using CP6.WebApi.Services;
using Dapper;
using Microsoft.EntityFrameworkCore;
using Xunit.Abstractions;

namespace CP6.Tests.DatabaseCompatibility;

[Collection(Wp5ReportsRelationalCollection.Name)]
public sealed partial class GdprRelationalTests(Wp5ReportsRelationalFixture fixture, ITestOutputHelper output)
{
    private const string Issuer = "https://identity.cp6.test";

    [Wp5ReportsFact]
    public async Task Purge_preserves_identity_tombstones_and_other_tenant()
    {
        output.WriteLine(fixture.SetupSummary);
        var tenant = Guid.NewGuid();
        var otherTenant = Guid.NewGuid();
        var runtime = Runtime(tenant, otherTenant);
        var user = await SeedIdentityAsync(tenant, runtime);
        var otherUser = await SeedIdentityAsync(otherTenant, runtime);
        var grant = await GrantAndLogoutAsync(tenant, user);
        var otherGrant = await GrantAndLogoutAsync(otherTenant, otherUser);
        string[] otherSnapshotsBefore;
        await using (var before = fixture.CreateContext(otherTenant))
        {
            otherSnapshotsBefore = SnapshotBytes(await before.CrmIdentitySnapshots.AsNoTracking()
                .Where(x => x.TenantId == otherTenant).ToArrayAsync());
            Assert.Equal(5, await before.Set<Cp6OutboxMessage>().CountAsync(x => x.TenantId == tenant));
            Assert.Equal(5, await before.Set<Cp6OutboxMessage>().CountAsync(x => x.TenantId == otherTenant));
        }
        Assert.Equal(1, await CountGrantAsync(grant.Id));
        Assert.Equal(1, await CountLogoutAsync(grant.Id));
        Assert.Equal(1, await CountGrantAsync(otherGrant.Id));
        Assert.Equal(1, await CountLogoutAsync(otherGrant.Id));

        // Preserve the legacy native fixture's real purge path; these dependencies are unused by tenant purge.
        var tenantScope = new TenantContext { CurrentTenantId = tenant };
        await using (var db = fixture.CreateIdentityContext(tenantScope, runtime))
        {
            var service = new GdprService(db, null!, null!, null!, new SecurityAuditService(db), tenantScope, identity: runtime);
            await service.EraseTenantAsync(tenant, "purge");
        }

        await using (var verify = fixture.CreateContext(tenant))
        {
            Assert.False(await verify.Sys_Tenants.IgnoreQueryFilters().AnyAsync(x => x.Id == tenant));
            Assert.False(await verify.Sys_Users.IgnoreQueryFilters().AnyAsync(x => x.TenantId == tenant));
            Assert.False(await verify.Sys_Depts.IgnoreQueryFilters().AnyAsync(x => x.TenantId == tenant));
            Assert.False(await verify.Sys_Roles.IgnoreQueryFilters().AnyAsync(x => x.TenantId == tenant));
            Assert.False(await verify.Sys_RoleActions.IgnoreQueryFilters().AnyAsync(x => x.TenantId == tenant));
            Assert.False(await verify.Sys_RoleDataScopes.IgnoreQueryFilters().AnyAsync(x => x.TenantId == tenant));
            var snapshots = await verify.CrmIdentitySnapshots.AsNoTracking().Where(x => x.TenantId == tenant).ToArrayAsync();
            Assert.Equal(6, snapshots.Length);
            Assert.Equal(5, snapshots.Count(x => x.IsDeleted && x.Version == 2));
            var revoked = Assert.Single(snapshots, x => x.EventType == IdentityEventContracts.TokenRevoked);
            Assert.Equal(IdentityEventContracts.TokenAggregate(Issuer, grant.Id.ToString("D")), revoked.AggregateId);
            Assert.Equal(1, revoked.Version);
            Assert.DoesNotContain(snapshots, x => x.AggregateId.EndsWith(grant.SourceJti, StringComparison.Ordinal));
            Assert.All(snapshots, row =>
            {
                Assert.Equal(8, row.RowVersion.Length);
                Assert.Equal(IdentityEventContracts.Hash(Encoding.UTF8.GetBytes(row.PayloadJson)), row.PayloadSha256);
            });
            Assert.Equal(5, await verify.Set<Cp6OutboxMessage>().CountAsync(x => x.TenantId == tenant));
            var audit = Assert.Single(await verify.Sys_SecurityLogs.IgnoreQueryFilters()
                .Where(x => x.EventType == (int)SecurityEventType.GdprTenantErased && x.RequestTenantCode == TenantCode(tenant)).ToArrayAsync());
            Assert.Equal(TenantContext.DefaultTenant, audit.TenantId);
            Assert.Equal("purge:" + TenantCode(tenant), audit.Reason);
        }
        Assert.Equal(0, await CountGrantAsync(grant.Id));
        Assert.Equal(0, await CountLogoutAsync(grant.Id));

        await using (var priority = fixture.CreatePriorityContext())
        {
            var messages = await priority.Set<Cp6OutboxMessage>().AsNoTracking().Where(x => x.TenantId == tenant).ToArrayAsync();
            Assert.Equal(6, messages.Length);
            Assert.All(messages, message => Assert.True(runtime.Validator.IsValid(message.Payload)));
            Assert.Equal(0, await priority.Set<Cp6OutboxMessage>().CountAsync(x => x.TenantId == otherTenant));
        }
        await using (var other = fixture.CreateContext(otherTenant))
        {
            Assert.True((await other.Sys_Tenants.IgnoreQueryFilters().SingleAsync(x => x.Id == otherTenant)).Enable);
            var preservedUser = await other.Sys_Users.IgnoreQueryFilters().SingleAsync(x => x.Id == otherUser);
            Assert.Equal(otherTenant, preservedUser.TenantId);
            Assert.True(preservedUser.Enable);
            Assert.Equal("fixture-only-hash", preservedUser.Password);
            Assert.Equal(2, await other.Sys_Depts.CountAsync());
            Assert.Single(await other.Sys_Roles.ToArrayAsync());
            Assert.Single(await other.Sys_RoleActions.ToArrayAsync());
            Assert.Single(await other.Sys_RoleDataScopes.ToArrayAsync());
            Assert.Equal(otherSnapshotsBefore, SnapshotBytes(await other.CrmIdentitySnapshots.AsNoTracking()
                .Where(x => x.TenantId == otherTenant).ToArrayAsync()));
            Assert.Equal(5, await other.Set<Cp6OutboxMessage>().CountAsync(x => x.TenantId == otherTenant));
        }
        Assert.Equal(1, await CountGrantAsync(otherGrant.Id));
        Assert.Equal(1, await CountLogoutAsync(otherGrant.Id));
        await using var connection = fixture.CreateConnection();
        var revokedSql = fixture.Database.Provider == DatabaseProvider.PostgreSql
            ? "SELECT \"Revoked\" FROM public.\"CrmOidcGrant\" WHERE \"Id\"=@id"
            : "SELECT Revoked FROM dbo.CrmOidcGrant WHERE Id=@id";
        Assert.False(await connection.QuerySingleAsync<bool>(revokedSql, new { id = otherGrant.Id }));
    }

    private CrmIdentityRuntime Runtime(params Guid[] tenants)
    {
        var options = new CrmIdentityOptions { Enabled = true, Issuer = Issuer, ProjectionReaderClientIds = ["reader-a"] };
        foreach (var tenant in tenants) options.Tenants.Add(tenant, "us");
        var bundle = Cp6ContractBundle.Load(Path.Combine(AppContext.BaseDirectory, "contracts/events/platform"));
        return new CrmIdentityRuntime(options, new IdentityEventValidator(bundle, Issuer));
    }

    private async Task<Guid> SeedIdentityAsync(Guid tenant, CrmIdentityRuntime runtime)
    {
        var root = Guid.NewGuid();
        var child = Guid.NewGuid();
        var user = Guid.NewGuid();
        await using var db = fixture.CreateIdentityContext(tenant, runtime);
        db.Sys_Tenants.Add(new Sys_Tenant { Id = tenant, TenantCode = TenantCode(tenant), TenantName = "WP5 GDPR native fixture" });
        db.Sys_Roles.Add(new Sys_Role { TenantId = tenant, RoleId = 7, RoleName = "WP5 GDPR role" });
        db.Sys_Depts.AddRange(new Sys_Dept { Id = root, TenantId = tenant, DeptCode = "ROOT", DeptName = "Root", Path = $"/{root:D}/" },
            new Sys_Dept { Id = child, TenantId = tenant, DeptCode = "CHILD", DeptName = "Child", ParentId = root, Path = $"/{root:D}/{child:D}/" });
        db.Sys_Users.Add(new Sys_User { Id = user, TenantId = tenant, UserName = TenantCode(tenant), Password = "fixture-only-hash", RoleId = 7, DeptId = root });
        if (!await db.Sys_Menus.AnyAsync(x => x.MenuId == 9901))
            db.Sys_Menus.Add(new Sys_Menu { MenuId = 9901, MenuKey = "crm-lead", MenuName = "Fixture leads" });
        db.Sys_RoleActions.Add(new Sys_RoleAction { TenantId = tenant, RoleId = 7, MenuId = 9901, ActionCode = "query" });
        db.Sys_RoleDataScopes.Add(new Sys_RoleDataScope { TenantId = tenant, RoleId = 7, ResourceKey = "crm-lead", ScopeType = 2 });
        await db.SaveChangesAsync();
        var snapshots = await db.CrmIdentitySnapshots.AsNoTracking().Where(x => x.TenantId == tenant).ToArrayAsync();
        Assert.Equal(5, snapshots.Length);
        Assert.All(snapshots, row => { Assert.Equal(1, row.Version); Assert.False(row.IsDeleted); Assert.Equal(8, row.RowVersion.Length); });
        return user;
    }

    private async Task<CrmOidcGrant> GrantAndLogoutAsync(Guid tenant, Guid user)
    {
        var grant = new CrmOidcGrant
        {
            Id = Guid.NewGuid(), ClientId = "CP6.Web", CodeHash = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)),
            RedirectUri = "https://fixture.cp6.test/callback", Challenge = "fixture-challenge", Nonce = "fixture-nonce",
            SubjectId = user, OrganizationId = tenant, SourceJti = Guid.NewGuid().ToString("D"),
            SourceRefreshHash = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)), SecurityStamp = "fixture-stamp",
            ExpiresAtUtc = DateTime.UtcNow.AddMinutes(1), SourceExpiresAtUtc = DateTime.UtcNow.AddMinutes(5),
            AccessExpiresAtUtc = DateTime.UtcNow.AddMinutes(5)
        };
        var store = new SqlCrmOidcGrantStore(fixture.Database, fixture.ConnectionString);
        await store.SaveAsync(grant);
        await store.SaveLogoutAsync(new CrmOidcLogout
        {
            TicketHash = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)), GrantId = grant.Id,
            RedirectUri = grant.RedirectUri, ExpiresAtUtc = DateTime.UtcNow.AddMinutes(5)
        });
        return grant;
    }

    private async Task<int> CountGrantAsync(Guid id)
    {
        await using var connection = fixture.CreateConnection();
        return await connection.QuerySingleAsync<int>(fixture.Database.Provider == DatabaseProvider.PostgreSql
            ? "SELECT COUNT(*) FROM public.\"CrmOidcGrant\" WHERE \"Id\"=@id"
            : "SELECT COUNT(*) FROM dbo.CrmOidcGrant WHERE Id=@id", new { id });
    }

    private async Task<int> CountLogoutAsync(Guid id)
    {
        await using var connection = fixture.CreateConnection();
        return await connection.QuerySingleAsync<int>(fixture.Database.Provider == DatabaseProvider.PostgreSql
            ? "SELECT COUNT(*) FROM public.\"CrmOidcLogout\" WHERE \"GrantId\"=@id"
            : "SELECT COUNT(*) FROM dbo.CrmOidcLogout WHERE GrantId=@id", new { id });
    }

    private static string[] SnapshotBytes(IEnumerable<CrmIdentitySnapshot> rows) => rows.OrderBy(x => x.AggregateId, StringComparer.Ordinal)
        .Select(x => $"{x.AggregateId}|{x.Version}|{x.IsDeleted}|{x.PayloadJson}|{x.PayloadSha256}|{Convert.ToHexString(x.RowVersion)}").ToArray();

    private static string TenantCode(Guid tenant) => "wp5-" + tenant.ToString("N");
}
