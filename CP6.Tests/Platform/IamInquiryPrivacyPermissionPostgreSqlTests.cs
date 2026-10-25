using System.Text.Json;
using CP6.Core.Services.Sys;
using CP6.Core.Services.CrmIdentity;
using CP6.Entity.DomainModels.Sys;
using CP6.WebApi.Services;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests.Platform;
public sealed partial class IamUserAssignmentsPostgreSqlTests
{
    private static AuthorizationCatalogSnapshotV1 PrivacyCatalog()
    {
        var catalog=new AuthorizationCatalogStoreV1();
        catalog.RegisterFields("crm-intake",new[]{"contactName","email","phone","company","need","riskCategories"}.Select(x=>new FieldRegistry.Field(x,x)));
        catalog.RegisterFields("crm-privacy",[new("consent","Consent"),new("exportArtifact","Export artifact")]);
        catalog.Seal();return catalog.Capture();
    }
    private static async Task<InquirySiteFormScopeV1> GrantPrivacy(Harness h,int scope=3)
    {
        var pair=await GrantIntake(h);
        var menu=await h.Db.Sys_Menus.SingleOrDefaultAsync(x=>x.MenuKey=="crm-privacy");
        if(menu is null){menu=new(){MenuId=Interlocked.Increment(ref policyMenuId),MenuKey="crm-privacy",MenuName="Privacy"};h.Db.Sys_Menus.Add(menu);}
        h.Db.Sys_RoleActions.Add(new(){RoleId=71,MenuId=menu.MenuId,ActionCode="export"});
        h.Db.Sys_RoleDataScopes.Add(new(){RoleId=71,ResourceKey="crm-privacy",ScopeType=scope,CustomDeptIds=h.Child.ToString("D")});
        foreach(var field in new[]{"contactName","email","phone","company","need","riskCategories"})
            h.Db.Sys_RoleFieldPerms.Add(new(){RoleId=71,ResourceKey="crm-intake",FieldName=field,Access=2});
        h.Db.Sys_RoleFieldPerms.Add(new(){RoleId=71,ResourceKey="crm-privacy",FieldName="exportArtifact",Access=2});
        await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();return pair;
    }
    [TenantManagementPostgresTheory]
    [InlineData("privacy:export")][InlineData("privacy:download")]
    public async Task Inquiry_privacy_current_source_requires_independent_action_and_seven_fields(string purpose)
    {
        await using var h=await Seed();var pair=await GrantPrivacy(h);
        using var key=InquiryPermissionSigningTests.Signer(InquiryPermissionSigningTests.Options());
        var service=new InquiryPermissionIssuerV1(h.Db,key,()=>PrivacyCatalog());
        using var doc=JsonDocument.Parse(await Issue(service,InquirySubject(h),pair,purpose));var p=doc.RootElement;
        Assert.Equal(purpose,p.GetProperty("purpose").GetString());
        Assert.Equal(new[]{"crm-lead:query","crm-lead:view-pii","crm-privacy:export"},p.GetProperty("permissions").EnumerateArray().Select(x=>x.GetString()));
        Assert.Equal(new[]{"contactName","email","phone","company","need","riskCategories","exportArtifact"},p.GetProperty("fieldPermissions").EnumerateArray().Select(x=>x.GetString()));
        Assert.True(p.GetProperty("isSupervisor").GetBoolean());
        await h.Db.Sys_RoleActions.Where(x=>x.RoleId==71&&x.ActionCode=="export").ExecuteDeleteAsync();
        Assert.Equal(403,(await Assert.ThrowsAsync<InquiryPermissionExceptionV1>(()=>Issue(service,InquirySubject(h),pair,purpose))).Status);
    }
    [CurrentPermissionPostgresFact]
    public async Task Inquiry_privacy_expanded_catalog_preserves_independent_consent_without_PII_grants()
    {
        await using var h=await Seed();var pair=await GrantConsent(h);
        h.Db.Sys_RoleFieldPerms.Add(new(){RoleId=71,ResourceKey="crm-privacy",FieldName="exportArtifact",Access=3});
        await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();
        using var key=InquiryPermissionSigningTests.Signer(InquiryPermissionSigningTests.Options());
        using var doc=JsonDocument.Parse(await Issue(new(h.Db,key,()=>PrivacyCatalog()),InquirySubject(h),pair,"privacy:consent-withdraw"));
        Assert.Equal(new[]{"consent"},doc.RootElement.GetProperty("fieldPermissions").EnumerateArray().Select(x=>x.GetString()));
    }

    [TenantManagementPostgresTheory]
    [InlineData(1)][InlineData(2)][InlineData(3)][InlineData(4)][InlineData(5)]
    public async Task Inquiry_privacy_intersects_both_current_resource_scopes_and_healthy_departments(int scope)
    {
        await using var h=await Seed();var pair=await GrantPrivacy(h,scope);
        using var key=InquiryPermissionSigningTests.Signer(InquiryPermissionSigningTests.Options());var issuer=new InquiryPermissionIssuerV1(h.Db,key,()=>PrivacyCatalog());
        if(scope==1){Assert.Equal(403,(await Assert.ThrowsAsync<InquiryPermissionExceptionV1>(()=>Issue(issuer,InquirySubject(h),pair,"privacy:export"))).Status);return;}
        using var doc=JsonDocument.Parse(await Issue(issuer,InquirySubject(h),pair,"privacy:export"));
        Assert.Equal((scope==2?new[]{h.Parent}:scope==4?[h.Child]:[h.Parent,h.Child]).Order(),doc.RootElement.GetProperty("managedDepartmentIds").EnumerateArray().Select(x=>x.GetGuid()).Order());
        // An independent privacy grant cannot replace the lead scope.
        await h.Db.Sys_RoleDataScopes.Where(x=>x.RoleId==71&&x.ResourceKey=="crm-lead").ExecuteUpdateAsync(x=>x.SetProperty(v=>v.ScopeType,1));
        Assert.Equal(403,(await Assert.ThrowsAsync<InquiryPermissionExceptionV1>(()=>Issue(issuer,InquirySubject(h),pair,"privacy:download"))).Status);
    }
    [TenantManagementPostgresTheory]
    [InlineData("query")][InlineData("view-pii")][InlineData("assign")][InlineData("export")][InlineData("site-form")][InlineData("role")][InlineData("department")]
    public async Task Inquiry_privacy_current_revocation_is_not_replaced_by_intake_grants(string change)
    {
        await using var h=await Seed();var pair=await GrantPrivacy(h);
        using var key=InquiryPermissionSigningTests.Signer(InquiryPermissionSigningTests.Options());var issuer=new InquiryPermissionIssuerV1(h.Db,key,()=>PrivacyCatalog());
        await Issue(issuer,InquirySubject(h),pair,"privacy:export");
        if(change=="site-form"){var scopes=InquiryScopes(h);await scopes.Service.ChangeAsync(71,await InquiryCommand(h,scopes.Service,71),h.Actor,default);}
        else if(change=="role")await h.Db.Sys_Roles.Where(x=>x.RoleId==71).ExecuteUpdateAsync(x=>x.SetProperty(v=>v.Enable,false));
        else if(change=="department")await h.Db.Sys_Depts.Where(x=>x.Id==h.Parent).ExecuteUpdateAsync(x=>x.SetProperty(v=>v.Enable,false));
        else await h.Db.Sys_RoleActions.Where(x=>x.RoleId==71&&x.ActionCode==change).ExecuteDeleteAsync();
        foreach(var purpose in new[]{"privacy:export","privacy:download"})
            Assert.Equal(403,(await Assert.ThrowsAsync<InquiryPermissionExceptionV1>(()=>Issue(issuer,InquirySubject(h),pair,purpose))).Status);
    }
    [TenantManagementPostgresTheory]
    [InlineData("contactName")][InlineData("email")][InlineData("phone")][InlineData("company")][InlineData("need")][InlineData("riskCategories")][InlineData("exportArtifact")]
    public async Task Inquiry_privacy_hidden_fields_require_same_use_and_stored_MIN_without_per_role_defaults(string field)
    {
        await using var h=await Seed();var pair=await GrantPrivacy(h);
        h.Db.Sys_UserRoles.Add(new(){UserId=h.ActorId,RoleId=72});await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();
        await h.Db.Sys_RoleFieldPerms.Where(x=>x.RoleId==71&&x.FieldName==field).ExecuteUpdateAsync(x=>x.SetProperty(v=>v.Access,3));
        using var key=InquiryPermissionSigningTests.Signer(InquiryPermissionSigningTests.Options());var issuer=new InquiryPermissionIssuerV1(h.Db,key,()=>PrivacyCatalog());
        foreach(var purpose in new[]{"privacy:export","privacy:download"})Assert.Equal("insufficient_field_scope",(await Assert.ThrowsAsync<InquiryPermissionExceptionV1>(()=>Issue(issuer,InquirySubject(h),pair,purpose))).Message);
        h.Db.Sys_RoleFieldPerms.Add(new(){RoleId=72,ResourceKey=field=="exportArtifact"?"crm-privacy":"crm-intake",FieldName=field,Access=2});
        await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();
        foreach(var purpose in new[]{"privacy:export","privacy:download"})await Issue(issuer,InquirySubject(h),pair,purpose);
        await h.Db.Sys_Roles.Where(x=>x.RoleId==72).ExecuteUpdateAsync(x=>x.SetProperty(v=>v.Enable,false));
        Assert.Equal(403,(await Assert.ThrowsAsync<InquiryPermissionExceptionV1>(()=>Issue(issuer,InquirySubject(h),pair,"privacy:download"))).Status);
    }
    [CurrentPermissionPostgresFact]
    public async Task Inquiry_privacy_requires_expanded_catalog_and_final_original_bearer()
    {
        await using var h=await Seed();var pair=await GrantPrivacy(h);
        using var key=InquiryPermissionSigningTests.Signer(InquiryPermissionSigningTests.Options());
        await Assert.ThrowsAsync<CurrentPermissionUnavailableException>(()=>Issue(new(h.Db,key,()=>ConsentCatalog()),InquirySubject(h),pair,"privacy:export"));
        var issuer=new InquiryPermissionIssuerV1(h.Db,key,()=>PrivacyCatalog());var reads=0;var subject=InquirySubject(h);
        Assert.Equal(401,(await Assert.ThrowsAsync<InquiryPermissionExceptionV1>(()=>issuer.IssueAsync(new("privacy:download",pair.SiteId,pair.FormId),(_,_)=>Task.FromResult<InquiryPermissionSubjectV1?>(++reads==1?subject:subject with{SessionId=Guid.NewGuid()}),default))).Status);
    }
    [CurrentPermissionPostgresFact]
    public Task Inquiry_privacy_export_actual_HTTP_login_OIDC_signing_and_current_revocation()=>InquiryProducerHttp("privacy:export",humanContext:true);
    [CurrentPermissionPostgresFact]
    public Task Inquiry_privacy_download_actual_HTTP_login_OIDC_signing_and_current_revocation()=>InquiryProducerHttp("privacy:download",humanContext:true);

    [TenantManagementPostgresTheory]
    [InlineData("legacy-catalog")][InlineData("extra-catalog")][InlineData("unknown-stored")][InlineData("invalid-access")]
    public async Task Inquiry_privacy_incomplete_or_unknown_sources_fail_closed(string change)
    {
        await using var h=await Seed();var pair=await GrantPrivacy(h);var catalog=PrivacyCatalog();
        if(change is "legacy-catalog" or "extra-catalog")
        {
            var store=new AuthorizationCatalogStoreV1();store.RegisterFields("crm-intake",new[]{"contactName","email","phone","company","need","riskCategories"}.Select(x=>new FieldRegistry.Field(x,x)));
            store.RegisterFields("crm-privacy",(change=="legacy-catalog"?new[]{"consent"}:["consent","exportArtifact","unknown"]).Select(x=>new FieldRegistry.Field(x,x)));
            store.Seal();catalog=store.Capture();
        }
        else if(change=="unknown-stored"){h.Db.Sys_RoleFieldPerms.Add(new(){RoleId=71,ResourceKey="crm-privacy",FieldName="unknown",Access=1});await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();}
        else await h.Db.Sys_RoleFieldPerms.Where(x=>x.RoleId==71&&x.FieldName=="exportArtifact").ExecuteUpdateAsync(x=>x.SetProperty(v=>v.Access,0));
        using var key=InquiryPermissionSigningTests.Signer(InquiryPermissionSigningTests.Options());
        await Assert.ThrowsAsync<CurrentPermissionUnavailableException>(()=>Issue(new(h.Db,key,()=>catalog),InquirySubject(h),pair,"privacy:export"));
    }
    [CurrentPermissionPostgresFact]
    public async Task Inquiry_privacy_never_truncates_intersected_departments()
    {
        await using var h=await Seed();var pair=await GrantPrivacy(h,5);
        await h.Db.Sys_RoleDataScopes.Where(x=>x.RoleId==71&&x.ResourceKey=="crm-lead").ExecuteUpdateAsync(x=>x.SetProperty(v=>v.ScopeType,5));
        for(var i=0;i<255;i++){var id=Guid.NewGuid();h.Db.Sys_Depts.Add(new(){Id=id,DeptCode="P"+i,DeptName="extra",Path=$"/{id}/"});}
        await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();
        using var key=InquiryPermissionSigningTests.Signer(InquiryPermissionSigningTests.Options());
        Assert.Equal(422,(await Assert.ThrowsAsync<InquiryPermissionExceptionV1>(()=>Issue(new(h.Db,key,()=>PrivacyCatalog()),InquirySubject(h),pair,"privacy:download"))).Status);
    }
}
