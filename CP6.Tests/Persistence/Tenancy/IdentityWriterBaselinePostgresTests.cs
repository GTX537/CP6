using System.Text;
using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Common;
using CP6.Core.Services.CrmIdentity;
using CP6.Core.Services.CrmIdentity.Baselines;
using CP6.Core.Services.Sys;
using CP6.Entity.DomainModels.Sys;
using Microsoft.EntityFrameworkCore;
using Moq;
using Npgsql;
using static CP6.Tests.Persistence.Tenancy.IdentityDirectoryPostgresFixture;

namespace CP6.Tests.Persistence.Tenancy;

[Collection("IdentityDirectoryConfiguredHttpPostgres")]
public sealed class IdentityWriterBaselinePostgresTests(IdentityDirectoryConfiguredPostgresFixture postgres)
{
    [CurrentPermissionPostgresFact]
    public async Task Real_services_reach_complete_ordinary_PG_baseline_without_touching_other_tenant()
    {
        await using var host = await IdentityDirectoryConfiguredHttpHost.Start(postgres);
        using var collectorA = await Collector(host, A); using var collectorB = await Collector(host, B);
        var beforeB = await collectorB.CollectAsync();
        await using var writer = Writer(host, A); var current = Mock.Of<ICurrentPermissionContext>();
        var user = new Sys_User { UserName = "baseline-writer", Password = "fixture-only" };
        writer.Sys_Users.Add(user); await writer.SaveChangesAsync();
        await new UserRoleService(writer, current).SaveAsync(user.Id, [777], 777, "fixture");
        var department = await new DeptService(writer).CreateAsync(new() { DeptCode = "w22-baseline", DeptName = "fixture department" }, null, "fixture");
        await new DeptService(writer).SetUserOrgAsync(user.Id, new() { DeptId = department }, "fixture");
        writer.Sys_Menus.Add(new Sys_Menu { MenuId = 72301, MenuName = "baseline permission", MenuKey = "crm-w22-baseline" }); await writer.SaveChangesAsync();
        var permissions = new RolePermService(writer, current);
        await permissions.SaveRolePermAsync(777, new() { MenuIds = [72301], Actions = [new() { MenuId = 72301, ActionCode = "read" }] }, "fixture");
        var after = await collectorA.CollectAsync();
        await ExactStoredBytes(after, A);
        var projectedUser = Data<UserIdentityData>(after, $"user:{user.Id:D}"); Assert.Equal(new[] { "role:777" }, projectedUser.Roles); Assert.Equal(department, projectedUser.DeptId);
        Assert.Equal("crm-w22-baseline", Assert.Single(Data<PermissionIdentityData>(after, "permission:role:777").Grants).Resource);
        await permissions.SaveRolePermAsync(777, new(), "fixture");
        var removed = await collectorA.CollectAsync(); Assert.Empty(Data<PermissionIdentityData>(removed, "permission:role:777").Grants);
        Assert.NotEqual(after.Boundary, removed.Boundary); await ExactStoredBytes(removed, A);
        var afterB = await collectorB.CollectAsync(); Assert.Equal(beforeB.ContentSha256, afterB.ContentSha256); Assert.Equal(beforeB.Boundary, afterB.Boundary);
        Assert.Equal(0, host.OwnerQueries); Assert.Equal(0, host.Host.PoolSnapshot.Borrowers);
    }

    [CurrentPermissionPostgresFact]
    public async Task Fixed_menu_fanout_reaches_public_collector_after_real_role_service_commits()
    {
        await using var host = await IdentityDirectoryConfiguredHttpHost.Start(postgres);
        await using (var setup = Writer(host, B))
        {
            setup.Sys_Menus.Add(new Sys_Menu { MenuId = 72302, MenuName = "concurrent baseline", MenuKey = "crm-w22-before" }); await setup.SaveChangesAsync();
        }
        await using var role = Writer(host, B); await using var roleTx = await role.Database.BeginTransactionAsync();
        await new RolePermService(role, Mock.Of<ICurrentPermissionContext>()).SaveRolePermAsync(777,
            new() { MenuIds = [72302], Actions = [new() { MenuId = 72302, ActionCode = "read" }] }, "fixture");
        await using var menu = Writer(host, B); await menu.Database.OpenConnectionAsync();
        (await menu.Sys_Menus.SingleAsync(x => x.MenuId == 72302)).MenuKey = "crm-w22-after";
        using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(15)); var pending = menu.SaveChangesAsync(cancel.Token);
        try
        {
            var pid = ((NpgsqlConnection)menu.Database.GetDbConnection()).ProcessID; var until = DateTimeOffset.UtcNow.AddSeconds(8); var waiting = false;
            await using var observer = postgres.OwnerContext(B);
            while (DateTimeOffset.UtcNow < until && !pending.IsCompleted)
            {
                waiting = await observer.Database.SqlQuery<bool>($"SELECT EXISTS(SELECT FROM pg_stat_activity WHERE pid={pid} AND wait_event_type='Lock') AS \"Value\"").SingleAsync();
                if (waiting) break; await Task.Delay(20);
            }
            Assert.True(waiting); await roleTx.CommitAsync(); await pending;
        }
        finally { cancel.Cancel(); if (!pending.IsCompleted) await roleTx.RollbackAsync(); try { await pending; } catch (OperationCanceledException) { } }
        using var collector = await Collector(host, B); var baseline = await collector.CollectAsync();
        Assert.Equal("crm-w22-after", Assert.Single(Data<PermissionIdentityData>(baseline, "permission:role:777").Grants).Resource);
        await ExactStoredBytes(baseline, B); Assert.Equal(0, host.OwnerQueries); Assert.Equal(0, host.Host.PoolSnapshot.Borrowers);
    }

    private CP6Context Writer(IdentityDirectoryConfiguredHttpHost host, Guid tenant)
        => postgres.Server.Context(new TenantContext { CurrentTenantId = tenant }, host.Runtime);
    private async Task<IdentityBaselineCollector> Collector(IdentityDirectoryConfiguredHttpHost host, Guid tenant)
    {
        var token = await host.Token(tenant);
        var source = new IdentityBaselineSourceV1(tenant, host.Runtime.Options.Issuer, host.Runtime.Options.Tenants[tenant], host.Runtime.ContractBundleSha256, postgres.Identity.BaselineSha256);
        return new(host.Runtime, source, host.Client.BaseAddress!, _ => ValueTask.FromResult(token.Token), new(PageSize: 2, AllowLoopbackHttp: true));
    }
    private async Task ExactStoredBytes(VerifiedIdentityBaseline baseline, Guid tenant)
    {
        await using var db = postgres.OwnerContext(tenant); var rows = await db.CrmIdentitySnapshots.AsNoTracking().Where(x => x.TenantId == tenant).ToArrayAsync();
        Assert.Equal(rows.Length, baseline.TotalCount);
        foreach (var row in rows)
        {
            var actual = Assert.Single(baseline.Items, x => x.Item.AggregateId == row.AggregateId);
            Assert.Equal(Encoding.UTF8.GetBytes(row.PayloadJson), Convert.FromBase64String(actual.PayloadBase64));
            Assert.Equal(row.PayloadSha256, actual.Item.PayloadSha256); Assert.Equal(row.Version, actual.Item.Version);
        }
    }
    private static T Data<T>(VerifiedIdentityBaseline baseline, string aggregate)
        => JsonSerializer.Deserialize<T>(Convert.FromBase64String(Assert.Single(baseline.Items, x => x.Item.AggregateId == aggregate).PayloadBase64), IdentityEventContracts.Json)!;
}
