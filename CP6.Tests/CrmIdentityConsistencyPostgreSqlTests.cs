using System.Text.Json;
using System.Text;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Common;
using CP6.Core.Services.CrmIdentity;
using CP6.Entity.DomainModels.Sys;
using CP6.Platform.EntityFramework;
using CP6.Platform.Messaging;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests;

[Collection("TenantManagementPostgres")]
public sealed class CrmIdentityConsistencyPostgreSqlTests(TenantManagementPostgresFixture server)
{
    [CurrentPermissionPostgresFact]
    public async Task Previously_tracked_snapshot_cannot_suppress_a_later_revocation()
    {
        var id = Guid.NewGuid();
        var tenant = new TenantContext { CurrentTenantId = id };
        var runtime = Runtime(id);
        await using (var seed = server.Context(tenant, runtime))
        {
            seed.Sys_Tenants.Add(new Sys_Tenant { Id = id, TenantCode = id.ToString("N"), TenantName = "identity fixture", Enable = false });
            await seed.SaveChangesAsync();
        }

        await using var stale = server.Context(tenant, runtime);
        var tracked = await stale.CrmIdentitySnapshots.SingleAsync(x => x.TenantId == id);
        Assert.Equal(1, tracked.Version);
        await using (var other = server.Context(tenant, runtime))
        {
            (await other.Sys_Tenants.SingleAsync(x => x.Id == id)).Enable = true;
            await other.SaveChangesAsync();
            Assert.Equal(2, (await other.CrmIdentitySnapshots.SingleAsync(x => x.TenantId == id)).Version);
        }

        // Load the business row only now: the stale object is the snapshot, not the tenant.
        var row = await stale.Sys_Tenants.SingleAsync(x => x.Id == id);
        Assert.True(row.Enable);
        row.Enable = false;
        await stale.SaveChangesAsync();

        await using var observer = server.Context(tenant);
        Assert.False((await observer.Sys_Tenants.SingleAsync(x => x.Id == id)).Enable);
        var actual = await observer.CrmIdentitySnapshots.SingleAsync(x => x.TenantId == id);
        Assert.False(JsonDocument.Parse(actual.PayloadJson).RootElement.GetProperty("enabled").GetBoolean());
        Assert.Equal(3, actual.Version);
        await using var priority = server.PriorityContext();
        Assert.Equal(new[] { 1, 3 }, await priority.Set<Cp6OutboxMessage>().Where(x => x.TenantId == id)
            .OrderBy(x => x.AggregateVersion).Select(x => x.AggregateVersion).ToArrayAsync());
    }

    [TenantManagementPostgresTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Concurrent_projection_writers_allocate_once_from_the_locked_version(bool existing)
    {
        var id = Guid.NewGuid(); var tenant = new TenantContext { CurrentTenantId = id }; var runtime = Runtime(id);
        await using (var seed = server.Context(tenant, existing ? runtime : null))
        {
            seed.Sys_Tenants.Add(new Sys_Tenant { Id = id, TenantCode = id.ToString("N"), TenantName = "concurrent", Enable = true });
            await seed.SaveChangesAsync();
        }
        await using var first = server.Context(tenant);
        await using var second = server.Context(tenant);
        await using var tx1 = await first.Database.BeginTransactionAsync();
        await using var tx2 = await second.Database.BeginTransactionAsync();
        Assert.NotEqual(((Npgsql.NpgsqlConnection)first.Database.GetDbConnection()).ProcessID,
            ((Npgsql.NpgsqlConnection)second.Database.GetDbConnection()).ProcessID);
        if (existing)
            await first.Database.ExecuteSqlInterpolatedAsync($"UPDATE public.\"Sys_Tenants\" SET \"Enable\"=false WHERE \"Id\"={id}");
        var aggregate = $"tenant:{id:D}";
        await new IdentitySnapshotWriter(first, runtime).WriteCurrentAsync(id, aggregate);
        using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var competing = new IdentitySnapshotWriter(second, runtime).WriteCurrentAsync(id, aggregate, cancel.Token);
        try
        {
            // The first transaction holds the missing-row or existing-row aggregate resource.
            await Assert.ThrowsAsync<TimeoutException>(() => competing.WaitAsync(TimeSpan.FromMilliseconds(200)));
            await first.SaveChangesAsync(); await tx1.CommitAsync();
            await competing; await second.SaveChangesAsync(); await tx2.CommitAsync();
        }
        finally
        {
            await cancel.CancelAsync();
            try { await competing; } catch (OperationCanceledException) { }
        }
        await using var observer = server.Context(tenant);
        var snapshot = await observer.CrmIdentitySnapshots.SingleAsync(x => x.TenantId == id);
        Assert.Equal(existing ? 2 : 1, snapshot.Version);
        Assert.Equal(!existing, Data(snapshot).GetProperty("enabled").GetBoolean());
        await AssertMessages(id, aggregate, Enumerable.Range(1, snapshot.Version).ToArray());
    }

    [CurrentPermissionPostgresFact]
    public async Task Repeated_staged_writes_preserve_deduplication_and_monotonic_versions()
    {
        var id = Guid.NewGuid(); var tenant = new TenantContext { CurrentTenantId = id }; var runtime = Runtime(id);
        await using var db = server.Context(tenant);
        db.Sys_Tenants.Add(new Sys_Tenant { Id = id, TenantCode = id.ToString("N"), TenantName = "staged" });
        await db.SaveChangesAsync();
        await using var tx = await db.Database.BeginTransactionAsync();
        var writer = new IdentitySnapshotWriter(db, runtime); var aggregate = $"tenant:{id:D}";
        await writer.WriteCurrentAsync(id, aggregate); await writer.WriteCurrentAsync(id, aggregate);
        Assert.Equal(1, db.CrmIdentitySnapshots.Local.Single().Version);
        await db.SaveChangesAsync();
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE public.\"Sys_Tenants\" SET \"Enable\"=false WHERE \"Id\"={id}");
        await writer.WriteCurrentAsync(id, aggregate); await writer.WriteCurrentAsync(id, aggregate);
        Assert.Equal(2, db.CrmIdentitySnapshots.Local.Single().Version);
        await db.SaveChangesAsync(); await tx.CommitAsync();
        await AssertMessages(id, aggregate, [1, 2]);
    }

    [CurrentPermissionPostgresFact]
    public async Task User_primary_membership_and_department_changes_publish_final_facts()
    {
        var id = Guid.NewGuid(); var tenant = new TenantContext { CurrentTenantId = id }; var runtime = Runtime(id);
        await using var db = server.Context(tenant, runtime);
        var oldDept = Guid.NewGuid(); var newDept = Guid.NewGuid(); var manager = Guid.NewGuid();
        var user = new Sys_User { UserName = "member", Password = "fixture-only", RoleId = 81, DeptId = oldDept };
        var other = new Sys_User { UserName = "other", Password = "fixture-only" };
        db.Sys_Users.AddRange(user, other);
        var membership = new Sys_UserRole { UserId = user.Id, RoleId = 82 };
        db.Sys_UserRoles.Add(membership); await db.SaveChangesAsync();
        var aggregate = $"user:{user.Id:D}";
        Assert.Equal(new[] { "role:81", "role:82" }, (await Snapshot<UserIdentityData>(db, id, aggregate)).Roles);
        user.RoleId = 83; user.DeptId = newDept; user.ManagerId = manager;
        // Moving a membership must refresh both the old and new user's projection.
        membership.UserId = other.Id; await db.SaveChangesAsync();
        var after = await Snapshot<UserIdentityData>(db, id, aggregate);
        Assert.Equal(2, after.Version); Assert.Equal(new[] { "role:83" }, after.Roles);
        Assert.Equal(newDept, after.DeptId); Assert.Equal(manager, after.ManagerId);
        Assert.Equal(new[] { "role:82" }, (await Snapshot<UserIdentityData>(db, id, $"user:{other.Id:D}")).Roles);
        db.Sys_UserRoles.Remove(membership); user.Enable = false; await db.SaveChangesAsync();
        Assert.Empty((await Snapshot<UserIdentityData>(db, id, $"user:{other.Id:D}")).Roles);
        Assert.False((await Snapshot<UserIdentityData>(db, id, aggregate)).Enabled);
        user.Email = "display-only@example.test"; await db.SaveChangesAsync();
        Assert.Equal(3, (await Snapshot<UserIdentityData>(db, id, aggregate)).Version);
        await AssertMessages(id, aggregate, [1, 2, 3]);
        await using var priority = server.PriorityContext();
        Assert.Contains(await priority.Set<Cp6OutboxMessage>().Where(x => x.TenantId == id).ToArrayAsync(),
            x => x.AggregateId == aggregate && x.AggregateVersion == 3);
    }

    [CurrentPermissionPostgresFact]
    public async Task Department_move_leader_disable_and_delete_keep_a_versioned_tombstone()
    {
        var id = Guid.NewGuid(); var tenant = new TenantContext { CurrentTenantId = id };
        await using var db = server.Context(tenant, Runtime(id));
        var dept = new Sys_Dept { Id = Guid.NewGuid(), DeptCode = id.ToString("N"), DeptName = "department" };
        dept.Path = $"/{dept.Id:D}/"; db.Sys_Depts.Add(dept); await db.SaveChangesAsync();
        dept.ParentId = Guid.NewGuid(); dept.LeaderId = Guid.NewGuid(); dept.Path = $"/{dept.ParentId:D}/{dept.Id:D}/";
        await db.SaveChangesAsync(); var aggregate = $"department:{dept.Id:D}";
        var after = await Snapshot<DepartmentIdentityData>(db, id, aggregate);
        Assert.Equal((dept.ParentId, dept.Path, dept.LeaderId, 2), (after.ParentId, after.Path, after.LeaderId, after.Version));
        dept.Enable = false; await db.SaveChangesAsync();
        Assert.False((await Snapshot<DepartmentIdentityData>(db, id, aggregate)).Enabled);
        db.Sys_Depts.Remove(dept); await db.SaveChangesAsync();
        var tombstone = await db.CrmIdentitySnapshots.AsNoTracking().SingleAsync(x => x.TenantId == id && x.AggregateId == aggregate);
        Assert.True(tombstone.IsDeleted); Assert.Equal(4, tombstone.Version);
        Assert.True(Data(tombstone).GetProperty("deleted").GetBoolean());
        await AssertMessages(id, aggregate, [1, 2, 3, 4]);
    }

    [CurrentPermissionPostgresFact]
    public async Task Permission_capture_tracks_actions_scopes_fields_menu_keys_and_tenant_boundaries()
    {
        var id = Guid.NewGuid(); var foreign = Guid.NewGuid(); var runtime = Runtime(id, foreign);
        var tenant = new TenantContext { CurrentTenantId = id };
        await using var db = server.Context(tenant, runtime);
        var resource = "crm-" + id.ToString("N");
        var menu = new Sys_Menu { MenuId = 71003, MenuName = "identity", MenuKey = resource };
        var role = new Sys_Role { RoleId = 91, RoleName = "reader" };
        var action = new Sys_RoleAction { RoleId = 91, MenuId = menu.MenuId, ActionCode = "read" };
        var scope = new Sys_RoleDataScope { RoleId = 91, ResourceKey = resource, ScopeType = 1 };
        var field = new Sys_RoleFieldPerm { RoleId = 91, ResourceKey = resource, FieldName = "cost", Access = 3 };
        db.Sys_Menus.Add(menu); db.Sys_Roles.Add(role); db.Sys_RoleActions.Add(action);
        db.Sys_RoleDataScopes.Add(scope); db.Sys_RoleFieldPerms.Add(field); await db.SaveChangesAsync();
        await using (var other = server.Context(new TenantContext { CurrentTenantId = foreign }, runtime))
        {
            other.Sys_Roles.Add(new Sys_Role { RoleId = 91, RoleName = "foreign" });
            other.Sys_RoleActions.Add(new Sys_RoleAction { RoleId = 91, MenuId = menu.MenuId, ActionCode = "foreign" });
            other.Sys_RoleFieldPerms.Add(new Sys_RoleFieldPerm { RoleId = 91, ResourceKey = resource, FieldName = "foreign", Access = 1 });
            await other.SaveChangesAsync();
        }
        var aggregate = "permission:role:91"; var dept = Guid.NewGuid();
        action.ActionCode = "write"; scope.ScopeType = 4; scope.CustomDeptIds = $"{dept:D},{dept:D}"; field.Access = 2;
        await db.SaveChangesAsync();
        var permission = await Snapshot<PermissionIdentityData>(db, id, aggregate);
        var grant = Assert.Single(permission.Grants);
        Assert.Equal(("write", 4), (grant.Action, grant.Scope)); Assert.Equal(new[] { dept }, grant.DeptIds);
        Assert.Equal(new[] { new IdentityFieldPermission("cost", 2) }, grant.Fields);
        var foreignData = await Snapshot<PermissionIdentityData>(db, foreign, aggregate);
        Assert.Equal("foreign", Assert.Single(foreignData.Grants).Action);
        Assert.Equal(1, foreignData.Version);
        // A shared menu-key edit expands both configured tenants' actual roles before saving.
        menu.MenuKey = resource + "-renamed"; await db.SaveChangesAsync();
        Assert.Equal(menu.MenuKey, Assert.Single((await Snapshot<PermissionIdentityData>(db, id, aggregate)).Grants).Resource);
        Assert.Equal(menu.MenuKey, Assert.Single((await Snapshot<PermissionIdentityData>(db, foreign, aggregate)).Grants).Resource);
        Assert.Equal(2, (await Snapshot<PermissionIdentityData>(db, foreign, aggregate)).Version);
        role.Enable = false; await db.SaveChangesAsync();
        permission = await Snapshot<PermissionIdentityData>(db, id, aggregate);
        Assert.False(permission.Enabled); Assert.Empty(permission.Grants); Assert.Equal(4, permission.Version);
        Assert.True((await Snapshot<PermissionIdentityData>(db, foreign, aggregate)).Enabled);
        await AssertMessages(id, aggregate, [1, 2, 3, 4]);
    }

    [CurrentPermissionPostgresFact]
    public async Task Foreign_user_and_department_facts_cannot_enter_a_tenant_snapshot()
    {
        var id = Guid.NewGuid(); var foreign = Guid.NewGuid(); var runtime = Runtime(id, foreign);
        var user = new Sys_User { UserName = "foreign-user", Password = "fixture-only", RoleId = 98, ManagerId = Guid.NewGuid() };
        var dept = new Sys_Dept { Id = Guid.NewGuid(), ParentId = Guid.NewGuid(), DeptCode = "foreign", DeptName = "foreign" };
        dept.Path = $"/{dept.ParentId:D}/{dept.Id:D}/"; user.DeptId = dept.Id;
        await using (var seed = server.Context(new TenantContext { CurrentTenantId = foreign }, runtime))
        {
            seed.Sys_Depts.Add(dept); seed.Sys_Users.Add(user); await seed.SaveChangesAsync();
        }
        await using var db = server.Context(new TenantContext { CurrentTenantId = id });
        var localUser = new Sys_User { UserName = "local-user", Password = "fixture-only", RoleId = 99 };
        db.Sys_Users.Add(localUser); await db.SaveChangesAsync();
        // A same-user-ID membership carrying the wrong tenant must not enter the local projection.
        await using (var seed = server.Context(new TenantContext { CurrentTenantId = foreign }))
        {
            seed.Sys_UserRoles.Add(new Sys_UserRole { UserId = localUser.Id, RoleId = 98 }); await seed.SaveChangesAsync();
        }
        await using var tx = await db.Database.BeginTransactionAsync();
        var writer = new IdentitySnapshotWriter(db, runtime);
        await writer.WriteCurrentAsync(id, $"user:{user.Id:D}");
        await writer.WriteCurrentAsync(id, $"user:{localUser.Id:D}");
        await writer.WriteCurrentAsync(id, $"department:{dept.Id:D}");
        await db.SaveChangesAsync(); await tx.CommitAsync();
        var userData = await Snapshot<UserIdentityData>(db, id, $"user:{user.Id:D}");
        Assert.False(userData.Enabled); Assert.Null(userData.DeptId); Assert.Null(userData.ManagerId); Assert.Empty(userData.Roles);
        Assert.Equal(new[] { "role:99" }, (await Snapshot<UserIdentityData>(db, id, $"user:{localUser.Id:D}")).Roles);
        var deptData = await Snapshot<DepartmentIdentityData>(db, id, $"department:{dept.Id:D}");
        Assert.False(deptData.Enabled); Assert.Null(deptData.ParentId); Assert.Equal($"/{dept.Id:D}/", deptData.Path);
        Assert.True((await Snapshot<UserIdentityData>(db, foreign, $"user:{user.Id:D}")).Enabled);
        await AssertMessages(id, $"user:{user.Id:D}", [1]);
        await AssertMessages(id, $"department:{dept.Id:D}", [1]);
    }

    [CurrentPermissionPostgresFact]
    public async Task Repeatable_read_does_not_turn_an_old_snapshot_into_a_new_committed_version()
    {
        var id = Guid.NewGuid(); var tenant = new TenantContext { CurrentTenantId = id }; var runtime = Runtime(id);
        await using (var seed = server.Context(tenant, runtime))
        {
            seed.Sys_Tenants.Add(new Sys_Tenant { Id = id, TenantCode = id.ToString("N"), TenantName = "repeatable" });
            await seed.SaveChangesAsync();
        }
        await using var stale = server.Context(tenant);
        await using var tx = await stale.Database.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead);
        Assert.Equal(1, (await stale.CrmIdentitySnapshots.SingleAsync(x => x.TenantId == id)).Version);
        await using (var other = server.Context(tenant, runtime))
        {
            (await other.Sys_Tenants.SingleAsync(x => x.Id == id)).Enable = false;
            await other.SaveChangesAsync();
        }
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new IdentitySnapshotWriter(stale, runtime).WriteCurrentAsync(id, $"tenant:{id:D}"));
        Assert.Equal("40001", Assert.IsType<Npgsql.PostgresException>(error.InnerException).SqlState);
        await tx.RollbackAsync(); // The caller, not the writer, owns any whole-command retry.
        await using var observer = server.Context(tenant);
        var current = await Snapshot<TenantIdentityData>(observer, id, $"tenant:{id:D}");
        Assert.False(current.Enabled); Assert.Equal(2, current.Version);
        await AssertMessages(id, $"tenant:{id:D}", [1, 2]);
    }

    private static JsonElement Data(CrmIdentitySnapshot snapshot)
        => JsonSerializer.Deserialize<JsonElement>(snapshot.PayloadJson);

    private static async Task<T> Snapshot<T>(CP6Context db, Guid tenant, string aggregate)
        => JsonSerializer.Deserialize<T>((await db.CrmIdentitySnapshots.AsNoTracking()
            .SingleAsync(x => x.TenantId == tenant && x.AggregateId == aggregate)).PayloadJson, IdentityEventContracts.Json)!;

    private async Task AssertMessages(Guid tenant, string aggregate, int[] versions)
    {
        await using var db = server.Context(new TenantContext { CurrentTenantId = tenant });
        await using var priority = server.PriorityContext();
        var messages = (await db.Set<Cp6OutboxMessage>().Where(x => x.TenantId == tenant && x.AggregateId == aggregate).ToArrayAsync())
            .Concat(await priority.Set<Cp6OutboxMessage>().Where(x => x.TenantId == tenant && x.AggregateId == aggregate).ToArrayAsync())
            .OrderBy(x => x.AggregateVersion).ToArray();
        Assert.Equal(versions, messages.Select(x => x.AggregateVersion).ToArray());
        var runtime = Runtime(tenant);
        Assert.All(messages, message => Assert.True(runtime.Validator.IsValid(message.Payload)));
        var snapshot = await db.CrmIdentitySnapshots.SingleAsync(x => x.TenantId == tenant && x.AggregateId == aggregate);
        Assert.Equal(IdentityEventContracts.Hash(Encoding.UTF8.GetBytes(snapshot.PayloadJson)), snapshot.PayloadSha256);
        using var envelope = JsonDocument.Parse(messages[^1].Payload);
        Assert.Equal(snapshot.PayloadJson, envelope.RootElement.GetProperty("data").GetRawText());
    }

    private static CrmIdentityRuntime Runtime(params Guid[] tenants)
    {
        const string issuer = "https://identity-consistency.example.test";
        var options = new CrmIdentityOptions { Enabled = true, Issuer = issuer, ProjectionReaderClientIds = ["fixture-reader"] };
        foreach (var id in tenants) options.Tenants.Add(id, "us");
        var bundle = Cp6ContractBundle.Load(Path.Combine(AppContext.BaseDirectory, "contracts/events/platform"));
        return new CrmIdentityRuntime(options, new IdentityEventValidator(bundle, issuer));
    }
}
