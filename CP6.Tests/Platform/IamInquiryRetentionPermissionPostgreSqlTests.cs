using System.Text.Json;
using System.Text.Json.Nodes;
using CP6.Core.Services.Common;
using CP6.Core.Services.CrmIdentity;
using CP6.Entity.DomainModels.Sys;
using CP6.WebApi.Services;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CP6.Tests.Platform;

public sealed partial class IamUserAssignmentsPostgreSqlTests
{
    private static async Task<InquirySiteFormScopeV1> GrantRetention(Harness h)
    {
        var menu=await h.Db.Sys_Menus.SingleOrDefaultAsync(x=>x.MenuKey=="crm-privacy");
        if(menu is null){menu=new Sys_Menu{MenuId=Interlocked.Increment(ref policyMenuId),MenuKey="crm-privacy",MenuName="Privacy"};h.Db.Sys_Menus.Add(menu);}
        h.Db.Sys_RoleActions.Add(new(){RoleId=71,MenuId=menu.MenuId,ActionCode="retention-run"});
        await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();
        // Maintenance has no per-row department or site/form assignment prerequisite.
        return new(Guid.NewGuid(),Guid.NewGuid());
    }
    [TenantManagementPostgresTheory]
    [InlineData("none")][InlineData("action")][InlineData("role")][InlineData("department")][InlineData("tenant")]
    [InlineData("other-role")][InlineData("padded-action")][InlineData("padded-menu")]
    public async Task Inquiry_retention_context_requires_only_actual_maintenance_action_and_current_service(string failure)
    {
        await using var h=await Seed();var pair=await GrantRetention(h);var options=InquiryPermissionSigningTests.Options();
        using var key=InquiryPermissionSigningTests.Signer(options);var runtime=ContextRuntime(h.Tenant,options.Issuer);
        await new IdentityBootstrapService(h.Db,runtime).InitializeAsync(h.Tenant);h.Db.ChangeTracker.Clear();
        var config=ServiceRegistration(h,Guid.NewGuid());config.Source.IdentityPublicationEnabled=true;config.Source.IdentityPublicationRevision=1;
        config.Source.Registrations[0].AuthenticationEpoch=Guid.NewGuid();var registry=new InquiryServicePrincipalRegistryV1(config.Source,config.Oidc);
        var tenant=new TenantContext{CurrentTenantId=Guid.NewGuid()};var prior=tenant.CurrentTenantId;
        await using var db=server.Context(tenant,runtime);await new InquiryServiceIdentityPublisherV1(db,tenant,registry,runtime).PublishAsync(h.Tenant);db.ChangeTracker.Clear();
        var issuer=new InquiryServicePermissionIssuerV1(db,tenant,registry,key,()=>throw new InvalidOperationException("No intake field authority for maintenance"),runtime);
        var request=new InquiryServiceContextRequestV1("privacy:retention-worker",pair.SiteId,pair.FormId,ServiceNonce());
        Task<string> Send()=>issuer.IssueContextAsync(request,ServiceBasic(),default);
        var json=await Send();var outer=JsonNode.Parse(json)!.AsObject();Assert.True(ContextSignature(outer,options.PublicKeyBase64));
        using(var inner=JsonDocument.Parse(outer["permissionJson"]!.GetValue<string>()))
        {
            var p=inner.RootElement;Assert.Equal(request.Purpose,p.GetProperty("purpose").GetString());
            Assert.Equal(new[]{"crm-privacy:retention-run"},p.GetProperty("permissions").EnumerateArray().Select(x=>x.GetString()));
            Assert.False(p.GetProperty("isSupervisor").GetBoolean());Assert.Empty(p.GetProperty("managedDepartmentIds").EnumerateArray());Assert.Empty(p.GetProperty("fieldPermissions").EnumerateArray());
        }
        Assert.Equal(prior,tenant.CurrentTenantId);Assert.Empty(db.ChangeTracker.Entries());
        Assert.Equal(400,(await Assert.ThrowsAsync<InquiryPermissionExceptionV1>(()=>issuer.IssueAsync(new(request.Purpose,pair.SiteId,pair.FormId),ServiceBasic(),default))).Status);
        var human=new InquiryPermissionIssuerV1(h.Db,key,()=>IntakeCatalog(),runtime);
        Assert.Equal(400,(await Assert.ThrowsAsync<InquiryPermissionExceptionV1>(()=>human.IssueHumanContextAsync(new(request.Purpose,pair.SiteId,pair.FormId,request.RequestNonce),(_,_)=>Task.FromResult<InquiryPermissionSubjectV1?>(InquirySubject(h)),default))).Status);
        if(failure=="none")return;
        var menu=await h.Db.Sys_Menus.Where(x=>x.MenuKey=="crm-privacy").Select(x=>x.MenuId).SingleAsync();
        switch(failure)
        {
            case "action":await h.Db.Sys_RoleActions.Where(x=>x.RoleId==71&&x.MenuId==menu).ExecuteDeleteAsync();break;
            case "role":await h.Db.Sys_Roles.Where(x=>x.RoleId==71).ExecuteUpdateAsync(x=>x.SetProperty(v=>v.Enable,false));break;
            case "department":await h.Db.Sys_Depts.Where(x=>x.Id==h.Parent).ExecuteUpdateAsync(x=>x.SetProperty(v=>v.Enable,false));break;
            case "tenant":await h.Db.Sys_Tenants.Where(x=>x.Id==h.Tenant).ExecuteUpdateAsync(x=>x.SetProperty(v=>v.Enable,false));break;
            case "other-role":await h.Db.Sys_RoleActions.Where(x=>x.RoleId==71&&x.MenuId==menu).ExecuteDeleteAsync();h.Db.Sys_RoleActions.Add(new(){RoleId=72,MenuId=menu,ActionCode="retention-run"});await h.Db.SaveChangesAsync();break;
            case "padded-action":await h.Db.Sys_RoleActions.Where(x=>x.RoleId==71&&x.MenuId==menu).ExecuteUpdateAsync(x=>x.SetProperty(v=>v.ActionCode,"retention-run "));break;
            case "padded-menu":await h.Db.Sys_Menus.Where(x=>x.MenuId==menu).ExecuteUpdateAsync(x=>x.SetProperty(v=>v.MenuKey,"crm-privacy "));break;
        }
        try{Assert.Equal(403,(await Assert.ThrowsAsync<InquiryPermissionExceptionV1>(Send)).Status);Assert.Equal(prior,tenant.CurrentTenantId);}
        finally{if(failure=="padded-menu")await h.Db.Sys_Menus.Where(x=>x.MenuId==menu).ExecuteUpdateAsync(x=>x.SetProperty(v=>v.MenuKey,"crm-privacy"));}
    }
    [CurrentPermissionPostgresFact]
    public async Task Inquiry_retention_waits_for_actual_Main_authority_and_rejects_committed_action_revocation()
    {
        await using var h=await Seed();var pair=await GrantRetention(h);var options=InquiryPermissionSigningTests.Options();
        using var key=InquiryPermissionSigningTests.Signer(options);var runtime=ContextRuntime(h.Tenant,options.Issuer);
        await new IdentityBootstrapService(h.Db,runtime).InitializeAsync(h.Tenant);h.Db.ChangeTracker.Clear();
        var config=ServiceRegistration(h,Guid.NewGuid());config.Source.IdentityPublicationEnabled=true;config.Source.IdentityPublicationRevision=1;config.Source.Registrations[0].AuthenticationEpoch=Guid.NewGuid();
        var registry=new InquiryServicePrincipalRegistryV1(config.Source,config.Oidc);var tenant=new TenantContext();await using var db=server.Context(tenant,runtime);
        await new InquiryServiceIdentityPublisherV1(db,tenant,registry,runtime).PublishAsync(h.Tenant);db.ChangeTracker.Clear();
        var issuer=new InquiryServicePermissionIssuerV1(db,tenant,registry,key,()=>IntakeCatalog(),runtime);
        await using var writer=new NpgsqlConnection(h.Db.Database.GetConnectionString());await writer.OpenAsync();await using var tx=await writer.BeginTransactionAsync();
        await using(var update=new NpgsqlCommand("SELECT cp6_authentication_v1.acquire(); DELETE FROM public.\"Sys_RoleAction\" WHERE \"TenantId\"=@tenant AND \"RoleId\"=71 AND \"ActionCode\"='retention-run'",writer,tx))
        {update.Parameters.AddWithValue("tenant",h.Tenant);await update.ExecuteNonQueryAsync();}
        await db.Database.OpenConnectionAsync();var reader=(NpgsqlConnection)db.Database.GetDbConnection();
        var pending=issuer.IssueContextAsync(new("privacy:retention-worker",pair.SiteId,pair.FormId,ServiceNonce()),ServiceBasic(),default);
        await using var observer=new NpgsqlConnection(h.Db.Database.GetConnectionString());await observer.OpenAsync();
        await using var blocked=new NpgsqlCommand("SELECT @writer=ANY(pg_blocking_pids(@reader))",observer);blocked.Parameters.AddWithValue("writer",writer.ProcessID);blocked.Parameters.AddWithValue("reader",reader.ProcessID);
        var waiting=false;for(var n=0;n<200&&!waiting;n++){waiting=(bool)(await blocked.ExecuteScalarAsync())!;if(!waiting)await Task.Delay(10);}
        Assert.True(waiting);await tx.CommitAsync();Assert.Equal(403,(await Assert.ThrowsAsync<InquiryPermissionExceptionV1>(async()=>await pending)).Status);
    }
}
