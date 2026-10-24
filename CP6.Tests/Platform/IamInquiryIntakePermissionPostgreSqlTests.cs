using System.Text.Json;
using CP6.Core.Services.Sys;
using CP6.Core.Services.CrmIdentity;
using CP6.Entity.DomainModels.Sys;
using CP6.WebApi.Services;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CP6.Tests.Platform;

public sealed partial class IamUserAssignmentsPostgreSqlTests
{
    private static AuthorizationCatalogSnapshotV1 IntakeCatalog(bool seal = true, bool complete = true)
    {
        var catalog = new AuthorizationCatalogStoreV1();
        catalog.RegisterFields("crm-intake", new[] { "contactName", "email", "phone", "company", "need", "riskCategories" }
            .Where(x => complete || x != "email").Select(x => new FieldRegistry.Field(x, x)));
        if (seal) catalog.Seal();
        return catalog.Capture();
    }

    private static async Task<InquirySiteFormScopeV1> GrantIntake(Harness h, int scope = 3)
    {
        var pair = (await GrantInquiry(h))[0];
        var menu = await h.Db.Sys_Menus.SingleOrDefaultAsync(x => x.MenuKey == "crm-lead");
        if (menu is null)
        {
            menu = new() { MenuId = Interlocked.Increment(ref policyMenuId), MenuKey = "crm-lead", MenuName = "Lead" };
            h.Db.Sys_Menus.Add(menu);
        }
        h.Db.Sys_RoleActions.AddRange(new[] { "query", "assign", "view-pii", "add", "edit" }
            .Select(x => new Sys_RoleAction { RoleId = 71, MenuId = menu.MenuId, ActionCode = x }));
        h.Db.Sys_RoleDataScopes.Add(new() { RoleId = 71, ResourceKey = "crm-lead", ScopeType = scope,
            CustomDeptIds = h.Child.ToString("D") + "," + Guid.NewGuid().ToString("D") });
        (await h.Db.Sys_Users.SingleAsync(x => x.Id == h.ActorId)).DeptId = h.Parent;
        await h.Db.SaveChangesAsync(); h.Db.ChangeTracker.Clear();
        return pair;
    }

    private static InquiryPermissionIssuerV1 IntakeIssuer(Harness h, InquiryPermissionSigningKeyV1 key,
        AuthorizationCatalogSnapshotV1? catalog = null) => new(h.Db, key, () => catalog ?? IntakeCatalog());

    [TenantManagementPostgresTheory]
    [InlineData("intake:list")][InlineData("intake:detail")][InlineData("intake:release")][InlineData("intake:reject")]
    public async Task Inquiry_intake_uses_current_human_supervisor_departments_and_complete_field_source(string purpose)
    {
        await using var h = await Seed(); var pair = await GrantIntake(h);
        using var key = InquiryPermissionSigningTests.Signer(InquiryPermissionSigningTests.Options());
        using var doc = JsonDocument.Parse(await Issue(IntakeIssuer(h, key), InquirySubject(h), pair, purpose)); var p = doc.RootElement;
        Assert.True(p.GetProperty("isSupervisor").GetBoolean());
        Assert.Equal(new[] { h.Parent, h.Child }.Order(), p.GetProperty("managedDepartmentIds").EnumerateArray().Select(x => x.GetGuid()).Order());
        Assert.Equal(new[] { "company", "contactName", "email", "need", "phone", "riskCategories" },
            p.GetProperty("fieldPermissions").EnumerateArray().Select(x => x.GetString()).Order(StringComparer.Ordinal));
        var actions = p.GetProperty("permissions").EnumerateArray().Select(x => x.GetString()).ToArray();
        Assert.Contains("crm-lead:query", actions); Assert.Contains("crm-lead:view-pii", actions);
        if (purpose == "intake:release") Assert.Contains("crm-lead:add", actions);
        if (purpose == "intake:reject") Assert.Contains("crm-lead:edit", actions);
        Assert.DoesNotContain("crm-lead:assign", actions); // Supervisor fact, not an unsupported wire action.
    }

    [TenantManagementPostgresTheory]
    [InlineData(1)][InlineData(2)][InlineData(3)][InlineData(4)][InlineData(5)]
    public async Task Inquiry_intake_projects_actual_scope_and_excludes_disabled_or_foreign_departments(int scope)
    {
        await using var h = await Seed(); var pair = await GrantIntake(h, scope);
        using var key = InquiryPermissionSigningTests.Signer(InquiryPermissionSigningTests.Options());
        if (scope == 1)
        {
            Assert.Equal(403, (await Assert.ThrowsAsync<InquiryPermissionExceptionV1>(() => Issue(IntakeIssuer(h, key), InquirySubject(h), pair, "intake:list"))).Status);
            return;
        }
        using var doc = JsonDocument.Parse(await Issue(IntakeIssuer(h, key), InquirySubject(h), pair, "intake:list"));
        var ids = doc.RootElement.GetProperty("managedDepartmentIds").EnumerateArray().Select(x => x.GetGuid()).ToArray();
        Assert.Equal((scope == 2 ? new[] { h.Parent } : scope == 4 ? [h.Child] : [h.Parent, h.Child]).Order(), ids.Order());
        await h.Db.Sys_Depts.Where(x => x.Id == h.Parent).ExecuteUpdateAsync(x => x.SetProperty(d => d.Enable, false));
        Assert.Equal(403, (await Assert.ThrowsAsync<InquiryPermissionExceptionV1>(() => Issue(IntakeIssuer(h, key), InquirySubject(h), pair, "intake:list"))).Status);
    }

    [CurrentPermissionPostgresFact]
    public async Task Inquiry_intake_field_MIN_keeps_explicit_hidden_without_per_role_default_and_readonly_can_be_read()
    {
        await using var h = await Seed(); var pair = await GrantIntake(h);
        h.Db.Sys_UserRoles.Add(new() { UserId = h.ActorId, RoleId = 72 });
        h.Db.Sys_RoleFieldPerms.Add(new() { RoleId = 71, ResourceKey = "crm-intake", FieldName = "email", Access = 3 });
        await h.Db.SaveChangesAsync(); h.Db.ChangeTracker.Clear();
        using var key = InquiryPermissionSigningTests.Signer(InquiryPermissionSigningTests.Options()); var service = IntakeIssuer(h, key);
        using (var hidden = JsonDocument.Parse(await Issue(service, InquirySubject(h), pair, "intake:detail")))
            Assert.DoesNotContain("email", hidden.RootElement.GetProperty("fieldPermissions").EnumerateArray().Select(x => x.GetString()));
        Assert.Equal("insufficient_field_scope", (await Assert.ThrowsAsync<InquiryPermissionExceptionV1>(() => Issue(service, InquirySubject(h), pair, "intake:release"))).Message);
        h.Db.Sys_RoleFieldPerms.Add(new() { RoleId = 72, ResourceKey = "crm-intake", FieldName = "email", Access = 2 });
        await h.Db.SaveChangesAsync(); h.Db.ChangeTracker.Clear();
        using var visible = JsonDocument.Parse(await Issue(service, InquirySubject(h), pair, "intake:release"));
        Assert.Contains("email", visible.RootElement.GetProperty("fieldPermissions").EnumerateArray().Select(x => x.GetString()));
        await h.Db.Sys_Roles.Where(x => x.RoleId == 72).ExecuteUpdateAsync(x => x.SetProperty(r => r.Enable, false));
        Assert.Equal(403, (await Assert.ThrowsAsync<InquiryPermissionExceptionV1>(() => Issue(service, InquirySubject(h), pair, "intake:reject"))).Status);
    }

    [TenantManagementPostgresTheory]
    [InlineData("query")][InlineData("assign")][InlineData("view-pii")][InlineData("add")][InlineData("edit")]
    public async Task Inquiry_intake_requires_actual_actions_without_borrowing_configuration_grants(string action)
    {
        await using var h = await Seed(); var pair = await GrantIntake(h);
        var menu = await h.Db.Sys_Menus.Where(x => x.MenuKey == "crm-lead").Select(x => x.MenuId).SingleAsync();
        await h.Db.Sys_RoleActions.Where(x => x.RoleId == 71 && x.MenuId == menu && x.ActionCode == action).ExecuteDeleteAsync();
        using var key = InquiryPermissionSigningTests.Signer(InquiryPermissionSigningTests.Options()); var service = IntakeIssuer(h, key);
        if (action == "view-pii")
        {
            using var list = JsonDocument.Parse(await Issue(service, InquirySubject(h), pair, "intake:list"));
            Assert.Empty(list.RootElement.GetProperty("fieldPermissions").EnumerateArray());
        }
        var purpose = action == "edit" ? "intake:reject" : "intake:release";
        Assert.Equal(403, (await Assert.ThrowsAsync<InquiryPermissionExceptionV1>(() => Issue(service, InquirySubject(h), pair, purpose))).Status);
    }

    [TenantManagementPostgresTheory]
    [InlineData("unsealed")][InlineData("partial")][InlineData("unknown-field")][InlineData("invalid-access")]
    public async Task Inquiry_intake_incomplete_or_invalid_source_never_becomes_default_allow(string failure)
    {
        await using var h = await Seed(); var pair = await GrantIntake(h);
        if (failure is "unknown-field" or "invalid-access")
        {
            h.Db.Sys_RoleFieldPerms.Add(new() { RoleId = 71, ResourceKey = "crm-intake",
                FieldName = failure == "unknown-field" ? "Email" : "email", Access = failure == "invalid-access" ? 0 : 1 });
            await h.Db.SaveChangesAsync(); h.Db.ChangeTracker.Clear();
        }
        using var key = InquiryPermissionSigningTests.Signer(InquiryPermissionSigningTests.Options());
        await Assert.ThrowsAsync<CurrentPermissionUnavailableException>(() => Issue(IntakeIssuer(h, key,
            IntakeCatalog(failure != "unsealed", failure != "partial")), InquirySubject(h), pair, "intake:detail"));
    }

    [TenantManagementPostgresTheory]
    [InlineData("too-many")][InlineData("wire-id")]
    public async Task Inquiry_intake_never_truncates_or_remaps_a_scope_the_frozen_wire_cannot_represent(string failure)
    {
        await using var h = await Seed(); var pair = await GrantIntake(h, 5);
        var extra = failure == "too-many" ? 255 : 1;
        for (var i = 0; i < extra; i++)
        {
            var id = failure == "wire-id" ? Guid.Parse("11111111-1111-1111-8111-111111111111") : Guid.NewGuid();
            h.Db.Sys_Depts.Add(new() { Id = id, DeptCode = "E" + i, DeptName = "extra", Path = $"/{id}/" });
        }
        await h.Db.SaveChangesAsync(); h.Db.ChangeTracker.Clear();
        using var key = InquiryPermissionSigningTests.Signer(InquiryPermissionSigningTests.Options());
        Assert.Equal(422, (await Assert.ThrowsAsync<InquiryPermissionExceptionV1>(() => Issue(IntakeIssuer(h, key), InquirySubject(h), pair, "intake:list"))).Status);
    }

    [TenantManagementPostgresTheory]
    [InlineData("field")][InlineData("department")]
    public async Task Inquiry_intake_waiting_on_actual_root_reads_committed_field_or_department_revocation(string kind)
    {
        await using var h = await Seed(); var pair = await GrantIntake(h);
        h.Db.Sys_RoleFieldPerms.Add(new() { RoleId = 71, ResourceKey = "crm-intake", FieldName = "email", Access = 1 });
        await h.Db.SaveChangesAsync(); h.Db.ChangeTracker.Clear();
        using var key = InquiryPermissionSigningTests.Signer(InquiryPermissionSigningTests.Options());
        await using var writer = new NpgsqlConnection(h.Db.Database.GetConnectionString()); await writer.OpenAsync();
        await using var tx = await writer.BeginTransactionAsync();
        await using var sql = new NpgsqlCommand("SELECT cp6_authentication_v1.acquire(); " + (kind == "field"
            ? "UPDATE public.\"Sys_RoleFieldPerm\" SET \"Access\"=3 WHERE \"TenantId\"=@tenant AND \"RoleId\"=71"
            : "UPDATE public.\"Sys_Dept\" SET \"Enable\"=false WHERE \"TenantId\"=@tenant AND \"Id\"=@department"), writer, tx);
        sql.Parameters.AddWithValue("tenant", h.Tenant); sql.Parameters.AddWithValue("department", h.Parent); await sql.ExecuteNonQueryAsync();
        await h.Db.Database.OpenConnectionAsync(); var reader = (NpgsqlConnection)h.Db.Database.GetDbConnection();
        var pending = Issue(IntakeIssuer(h, key), InquirySubject(h), pair, "intake:release");
        await using var observer = new NpgsqlConnection(h.Db.Database.GetConnectionString()); await observer.OpenAsync();
        await using var blocked = new NpgsqlCommand("SELECT @writer=ANY(pg_blocking_pids(@reader))", observer);
        blocked.Parameters.AddWithValue("writer", writer.ProcessID); blocked.Parameters.AddWithValue("reader", reader.ProcessID);
        var waiting = false;
        for (var n = 0; n < 200 && !waiting; n++) { waiting = (bool)(await blocked.ExecuteScalarAsync())!; if (!waiting) await Task.Delay(10); }
        Assert.True(waiting, "Actual PostgreSQL blocker required"); await tx.CommitAsync();
        Assert.Equal(403, (await Assert.ThrowsAsync<InquiryPermissionExceptionV1>(async () => await pending)).Status);
    }
}
