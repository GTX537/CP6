using CP6.Core.EFDbContext;
using CP6.Core.Services.Sys;
using CP6.Entity.DomainModels.Pur;
using CP6.Entity.DomainModels.Sys;
using Microsoft.EntityFrameworkCore;
using Xunit.Abstractions;

namespace CP6.Tests.DatabaseCompatibility;

[Collection(CoreBusinessRelationalCollection.Name)]
public sealed class PermissionsRelationalTests(CoreBusinessRelationalFixture fixture, ITestOutputHelper output)
{
    [CoreBusinessFact]
    public async Task Production_data_scope_filters_real_purchase_requests_and_preserves_tenant_boundary()
    {
        output.WriteLine(fixture.SetupSummary);
        var tenant = Guid.NewGuid();
        var otherTenant = Guid.NewGuid();
        var root = Department("root");
        var child = Department("child");
        child.ParentId = root.Id;
        child.Path = root.Path + child.Id + "/";
        var outside = Department("outside");
        var mine = Request(root.Id, "alice");
        var subordinate = Request(child.Id, "bob");
        var unrelated = Request(outside.Id, "alice");
        var noDepartment = Request(null, "bob");
        var deleted = Request(root.Id, "alice");
        deleted.IsDeleted = true;
        await using (var seed = fixture.CreateContext(tenant))
        {
            AddTenant(seed, tenant);
            seed.Sys_Depts.AddRange(root, child, outside);
            seed.PurchaseRequests.AddRange(mine, subordinate, unrelated, noDepartment, deleted);
            await seed.SaveChangesAsync();
        }
        await using (var other = fixture.CreateContext(otherTenant))
        {
            AddTenant(other, otherTenant);
            var department = Department("other-root");
            other.Sys_Depts.Add(department);
            other.PurchaseRequests.Add(Request(department.Id, "alice"));
            await other.SaveChangesAsync();
        }

        await using var db = fixture.CreateContext(tenant);
        var resource = "pur-pr";
        var permission = new UserPermissionContext { UserName = "alice", DeptId = root.Id, DeptPath = root.Path };
        var filter = new DataScopeFilter(db);
        async Task AssertRowsAsync(int scope, params Guid[] expected)
        {
            permission.DataScopes[resource] = scope;
            var query = filter.Apply(db.PurchaseRequests.AsNoTracking().Where(r => !r.IsDeleted), resource, permission);
            // The production filter stays on the provider IQueryable until materialization.
            var rows = await query.ToArrayAsync();
            Assert.Equal(expected.Order().ToArray(), rows.Select(r => r.Id).Order().ToArray());
            Assert.All(rows, row => Assert.Equal(tenant, row.TenantId));
            Assert.DoesNotContain(rows, row => row.IsDeleted);
        }
        await AssertRowsAsync(1, mine.Id, unrelated.Id);
        await AssertRowsAsync(2, mine.Id);
        await AssertRowsAsync(3, mine.Id, subordinate.Id);
        permission.CustomDeptIds[resource] = [child.Id, outside.Id];
        await AssertRowsAsync(4, subordinate.Id, unrelated.Id);
        await AssertRowsAsync(5, mine.Id, subordinate.Id, unrelated.Id, noDepartment.Id);
        permission.DataScopes.Remove(resource);
        var defaultRows = await filter.Apply(db.PurchaseRequests.AsNoTracking().Where(r => !r.IsDeleted), "wp4-unconfigured", permission).ToArrayAsync();
        Assert.Equal(new[] { mine.Id, unrelated.Id }.Order().ToArray(), defaultRows.Select(r => r.Id).Order().ToArray());
        await using var otherCheck = fixture.CreateContext(otherTenant);
        Assert.Single(await otherCheck.PurchaseRequests.ToArrayAsync());
    }

    [CoreBusinessFact]
    public async Task Production_permission_aggregator_reads_role_unions_scopes_and_fields_from_native_tables()
    {
        output.WriteLine(fixture.SetupSummary);
        var tenant = Guid.NewGuid();
        var otherTenant = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var firstDept = Department("permission-first");
        var secondDept = Department("permission-second");
        var key = "wp4-permission-" + Guid.NewGuid().ToString("N");
        var widestKey = key + "-wide";
        var otherKey = key + "-other";
        int menuId, noKeyMenuId, otherMenuId;
        await using (var seed = fixture.CreateContext(tenant))
        {
            AddTenant(seed, tenant);
            menuId = (await seed.Sys_Menus.MaxAsync(m => (int?)m.MenuId) ?? 0) + 1;
            noKeyMenuId = menuId + 1;
            otherMenuId = menuId + 2;
            seed.Sys_Menus.AddRange(
                new Sys_Menu { MenuId = menuId, MenuName = "WP4 permission", MenuKey = key },
                new Sys_Menu { MenuId = noKeyMenuId, MenuName = "WP4 no key", MenuKey = null },
                new Sys_Menu { MenuId = otherMenuId, MenuName = "WP4 other tenant", MenuKey = otherKey });
            seed.Sys_Roles.AddRange(
                new Sys_Role { RoleId = 1, RoleName = "WP4 primary" },
                new Sys_Role { RoleId = 2, RoleName = "WP4 secondary" });
            seed.Sys_Depts.AddRange(firstDept, secondDept);
            seed.Sys_Users.Add(new Sys_User
            {
                Id = userId, UserName = "wp4-" + userId.ToString("N"), Password = "test-only", RoleId = 1, DeptId = firstDept.Id
            });
            // The primary role is also explicitly assigned; aggregation must deduplicate it.
            seed.Sys_UserRoles.AddRange(
                new Sys_UserRole { UserId = userId, RoleId = 1 },
                new Sys_UserRole { UserId = userId, RoleId = 2 });
            seed.Sys_RoleMenus.AddRange(
                new Sys_RoleMenu { RoleId = 1, MenuId = menuId },
                new Sys_RoleMenu { RoleId = 2, MenuId = noKeyMenuId });
            seed.Sys_RoleActions.AddRange(
                new Sys_RoleAction { RoleId = 1, MenuId = menuId, ActionCode = "query" },
                new Sys_RoleAction { RoleId = 2, MenuId = menuId, ActionCode = "export" },
                new Sys_RoleAction { RoleId = 1, MenuId = noKeyMenuId, ActionCode = "delete" });
            seed.Sys_RoleDataScopes.AddRange(
                new Sys_RoleDataScope { RoleId = 1, ResourceKey = key, ScopeType = 4, CustomDeptIds = firstDept.Id.ToString() },
                new Sys_RoleDataScope { RoleId = 2, ResourceKey = key, ScopeType = 4, CustomDeptIds = $"{secondDept.Id},{firstDept.Id}" },
                new Sys_RoleDataScope { RoleId = 1, ResourceKey = widestKey, ScopeType = 2 },
                new Sys_RoleDataScope { RoleId = 2, ResourceKey = widestKey, ScopeType = 5 });
            seed.Sys_RoleFieldPerms.AddRange(
                new Sys_RoleFieldPerm { RoleId = 1, ResourceKey = key, FieldName = "Cost", Access = 3 },
                new Sys_RoleFieldPerm { RoleId = 2, ResourceKey = key, FieldName = "Cost", Access = 2 });
            await seed.SaveChangesAsync();
        }
        await using (var other = fixture.CreateContext(otherTenant))
        {
            AddTenant(other, otherTenant);
            other.Sys_Roles.Add(new Sys_Role { RoleId = 1, RoleName = "WP4 other primary" });
            other.Sys_RoleMenus.Add(new Sys_RoleMenu { RoleId = 1, MenuId = otherMenuId });
            other.Sys_RoleActions.Add(new Sys_RoleAction { RoleId = 1, MenuId = otherMenuId, ActionCode = "delete" });
            other.Sys_RoleDataScopes.Add(new Sys_RoleDataScope { RoleId = 1, ResourceKey = otherKey, ScopeType = 5 });
            other.Sys_RoleFieldPerms.Add(new Sys_RoleFieldPerm { RoleId = 1, ResourceKey = otherKey, FieldName = "Cost", Access = 1 });
            await other.SaveChangesAsync();
        }

        await using var fresh = fixture.CreateContext(tenant);
        var permission = await new PermissionAggregator(fresh).BuildAsync(userId);
        Assert.Equal(new[] { 1, 2 }, permission.RoleIds.Order().ToArray());
        Assert.Equal(firstDept.Id, permission.DeptId);
        Assert.Equal(firstDept.Path, permission.DeptPath);
        Assert.Equal(new[] { key }, permission.MenuKeys.Order().ToArray());
        Assert.Equal(new[] { key + ":export", key + ":query" }, permission.ActionKeys.Order().ToArray());
        Assert.DoesNotContain(permission.ActionKeys, action => action.StartsWith(":", StringComparison.Ordinal));
        Assert.Equal(4, permission.DataScopes[key]);
        Assert.Equal(5, permission.DataScopes[widestKey]);
        Assert.Equal(new[] { firstDept.Id, secondDept.Id }.Order().ToArray(), permission.CustomDeptIds[key].Order().ToArray());
        Assert.Equal(2, permission.FieldPerms[key]["Cost"]);
        Assert.False(permission.MenuKeys.Contains(otherKey));
        Assert.False(permission.DataScopes.ContainsKey(otherKey));
        Assert.False(permission.FieldPerms.ContainsKey(otherKey));
    }

    private static Sys_Dept Department(string name)
    {
        var id = Guid.NewGuid();
        return new() { Id = id, DeptCode = "WP4-" + id.ToString("N"), DeptName = name, Path = $"/{id}/" };
    }

    private static PurchaseRequest Request(Guid? department, string creator) => new()
    {
        PrNo = "WP4" + Guid.NewGuid().ToString("N")[..17], DeptId = department,
        RequesterId = creator, Creator = creator, RequestDate = DateTime.UtcNow, Status = PrStatus.Draft, Source = PrSource.Manual
    };

    private static void AddTenant(CP6Context db, Guid tenant) => db.Sys_Tenants.Add(new()
    {
        Id = tenant, TenantCode = "wp4-perm-" + tenant.ToString("N"), TenantName = "WP4 permissions", Enable = true
    });
}
