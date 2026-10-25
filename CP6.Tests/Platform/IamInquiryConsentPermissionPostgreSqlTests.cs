using System.Text.Json;
using CP6.Core.Services.Sys;
using CP6.Core.Services.CrmIdentity;
using CP6.Entity.DomainModels.Sys;
using CP6.WebApi.Services;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests.Platform;

public sealed partial class IamUserAssignmentsPostgreSqlTests
{
    private static AuthorizationCatalogSnapshotV1 ConsentCatalog(bool complete = true)
    {
        var catalog = new AuthorizationCatalogStoreV1();
        if (complete) catalog.RegisterFields("crm-privacy", [new("consent", "Consent")]);
        catalog.Seal(); return catalog.Capture();
    }
    private static async Task<InquirySiteFormScopeV1> GrantConsent(Harness h, int scope = 3)
    {
        var pair = await GrantIntake(h);
        // Intentionally remove all PII/intake write grants: privacy is a separate purpose.
        await h.Db.Sys_RoleActions.Where(x => x.RoleId == 71 && (x.ActionCode == "view-pii" || x.ActionCode == "add" || x.ActionCode == "edit")).ExecuteDeleteAsync();
        var menu = await h.Db.Sys_Menus.SingleOrDefaultAsync(x => x.MenuKey == "crm-privacy");
        if (menu is null) { menu = new() { MenuId = Interlocked.Increment(ref policyMenuId), MenuKey = "crm-privacy", MenuName = "Privacy" }; h.Db.Sys_Menus.Add(menu); }
        h.Db.Sys_RoleActions.Add(new() { RoleId = 71, MenuId = menu.MenuId, ActionCode = "consent-withdraw" });
        h.Db.Sys_RoleDataScopes.Add(new() { RoleId = 71, ResourceKey = "crm-privacy", ScopeType = scope,
            CustomDeptIds = h.Child.ToString("D") });
        await h.Db.SaveChangesAsync(); h.Db.ChangeTracker.Clear(); return pair;
    }
    [TenantManagementPostgresTheory]
    [InlineData(1)][InlineData(2)][InlineData(3)][InlineData(4)][InlineData(5)]
    public async Task Inquiry_consent_source_requires_own_action_and_intersects_actual_department_scope(int scope)
    {
        await using var h = await Seed(); var pair = await GrantConsent(h, scope);
        using var key = InquiryPermissionSigningTests.Signer(InquiryPermissionSigningTests.Options());
        var service = new InquiryPermissionIssuerV1(h.Db, key, () => ConsentCatalog());
        if (scope == 1)
        { Assert.Equal(403, (await Assert.ThrowsAsync<InquiryPermissionExceptionV1>(() => Issue(service, InquirySubject(h), pair, "privacy:consent-withdraw"))).Status); return; }
        using var doc = JsonDocument.Parse(await Issue(service, InquirySubject(h), pair, "privacy:consent-withdraw")); var p = doc.RootElement;
        Assert.Equal(new[] { "crm-lead:query", "crm-privacy:consent-withdraw" }, p.GetProperty("permissions").EnumerateArray().Select(x => x.GetString()));
        Assert.Equal(new[] { "consent" }, p.GetProperty("fieldPermissions").EnumerateArray().Select(x => x.GetString()));
        Assert.True(p.GetProperty("isSupervisor").GetBoolean());
        Assert.Equal((scope == 2 ? new[] { h.Parent } : scope == 4 ? [h.Child] : [h.Parent, h.Child]).Order(),
            p.GetProperty("managedDepartmentIds").EnumerateArray().Select(x => x.GetGuid()).Order());
        await h.Db.Sys_Depts.Where(x => x.Id == h.Parent).ExecuteUpdateAsync(x => x.SetProperty(v => v.Enable, false));
        Assert.Equal(403, (await Assert.ThrowsAsync<InquiryPermissionExceptionV1>(() => Issue(service, InquirySubject(h), pair, "privacy:consent-withdraw"))).Status);
    }
    [TenantManagementPostgresTheory]
    [InlineData("query")][InlineData("assign")][InlineData("consent-withdraw")][InlineData("site-form")][InlineData("role")]
    public async Task Inquiry_consent_current_revocation_cannot_be_replaced_by_intake_permissions(string change)
    {
        await using var h = await Seed(); var pair = await GrantConsent(h);
        using var key = InquiryPermissionSigningTests.Signer(InquiryPermissionSigningTests.Options());
        var service = new InquiryPermissionIssuerV1(h.Db, key, () => ConsentCatalog());
        await Issue(service, InquirySubject(h), pair, "privacy:consent-withdraw");
        if (change == "site-form") { var s = InquiryScopes(h); await s.Service.ChangeAsync(71, await InquiryCommand(h, s.Service, 71), h.Actor, default); }
        else if (change == "role") await h.Db.Sys_Roles.Where(x => x.RoleId == 71).ExecuteUpdateAsync(x => x.SetProperty(v => v.Enable, false));
        else await h.Db.Sys_RoleActions.Where(x => x.RoleId == 71 && x.ActionCode == change).ExecuteDeleteAsync();
        Assert.Equal(403, (await Assert.ThrowsAsync<InquiryPermissionExceptionV1>(() => Issue(service, InquirySubject(h), pair, "privacy:consent-withdraw"))).Status);
    }
    [CurrentPermissionPostgresFact]
    public async Task Inquiry_consent_field_requires_write_access_with_stored_MIN_and_exact_registered_mapping()
    {
        await using var h = await Seed(); var pair = await GrantConsent(h);
        using var key = InquiryPermissionSigningTests.Signer(InquiryPermissionSigningTests.Options());
        var service = new InquiryPermissionIssuerV1(h.Db, key, () => ConsentCatalog());
        h.Db.Sys_UserRoles.Add(new() { UserId = h.ActorId, RoleId = 72 });
        h.Db.Sys_RoleFieldPerms.Add(new() { RoleId = 71, ResourceKey = "crm-privacy", FieldName = "consent", Access = 3 });
        await h.Db.SaveChangesAsync(); h.Db.ChangeTracker.Clear();
        Assert.Equal("insufficient_field_scope", (await Assert.ThrowsAsync<InquiryPermissionExceptionV1>(() => Issue(service, InquirySubject(h), pair, "privacy:consent-withdraw"))).Message);
        h.Db.Sys_RoleFieldPerms.Add(new() { RoleId = 72, ResourceKey = "crm-privacy", FieldName = "consent", Access = 2 });
        await h.Db.SaveChangesAsync(); h.Db.ChangeTracker.Clear();
        Assert.Equal("insufficient_field_scope", (await Assert.ThrowsAsync<InquiryPermissionExceptionV1>(() => Issue(service, InquirySubject(h), pair, "privacy:consent-withdraw"))).Message);
        await h.Db.Sys_RoleFieldPerms.Where(x => x.RoleId == 72 && x.ResourceKey == "crm-privacy").ExecuteUpdateAsync(x => x.SetProperty(v => v.Access, 1));
        await Issue(service, InquirySubject(h), pair, "privacy:consent-withdraw");
        await h.Db.Sys_Roles.Where(x => x.RoleId == 72).ExecuteUpdateAsync(x => x.SetProperty(v => v.Enable, false));
        Assert.Equal(403, (await Assert.ThrowsAsync<InquiryPermissionExceptionV1>(() => Issue(service, InquirySubject(h), pair, "privacy:consent-withdraw"))).Status);
        await Assert.ThrowsAsync<CurrentPermissionUnavailableException>(() => Issue(new(h.Db, key, () => ConsentCatalog(false)), InquirySubject(h), pair, "privacy:consent-withdraw"));
    }
    [CurrentPermissionPostgresFact]
    public Task Inquiry_consent_actual_HTTP_login_OIDC_signing_and_current_revocation()
        => InquiryProducerHttp("privacy:consent-withdraw", humanContext: true);
}
