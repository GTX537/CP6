using CP6.Core.Services.Common;
using CP6.Core.Services.CrmIdentity;
using CP6.Entity.DomainModels.Sys;
using CP6.WebApi.Services;
using Microsoft.EntityFrameworkCore;
namespace CP6.Tests.Platform;
public sealed partial class IamUserAssignmentsPostgreSqlTests
{
    [TenantManagementPostgresTheory]
    [InlineData("query")][InlineData("view-pii")][InlineData("assign")][InlineData("export")]
    [InlineData("privacy-scope")][InlineData("lead-scope")][InlineData("artifact-field")][InlineData("pii-field")][InlineData("catalog")]
    public async Task Inquiry_export_service_v2_requires_current_actions_both_scopes_and_all_fields(string change)
    {
        await using var h=await Seed();var pair=await GrantPrivacy(h,5);var options=InquiryPermissionSigningTests.Options();
        using var key=InquiryPermissionSigningTests.Signer(options);var runtime=ContextRuntime(h.Tenant,options.Issuer);
        await new IdentityBootstrapService(h.Db,runtime).InitializeAsync(h.Tenant);h.Db.ChangeTracker.Clear();
        var config=ServiceRegistration(h,Guid.NewGuid());config.Source.IdentityPublicationEnabled=true;
        config.Source.IdentityPublicationRevision=1;config.Source.Registrations[0].AuthenticationEpoch=Guid.NewGuid();
        var registry=new InquiryServicePrincipalRegistryV1(config.Source,config.Oidc);var tenant=new TenantContext();
        await using var db=server.Context(tenant,runtime);
        await new InquiryServiceIdentityPublisherV1(db,tenant,registry,runtime).PublishAsync(h.Tenant);db.ChangeTracker.Clear();
        var issuer=new InquiryServicePermissionIssuerV1(db,tenant,registry,key,()=>PrivacyCatalog(),runtime);
        var request=new InquiryServiceContextRequestV1("privacy:export",pair.SiteId,pair.FormId,ServiceNonce());
        await issuer.IssueExportContextV2Async(request,ServiceBasic(),default);
        Assert.Equal(400,(await Assert.ThrowsAsync<InquiryPermissionExceptionV1>(()=>issuer.IssueContextAsync(request,ServiceBasic(),default))).Status);
        if(change is "query" or "view-pii" or "assign" or "export")
            await h.Db.Sys_RoleActions.Where(x=>x.RoleId==71&&x.ActionCode==change).ExecuteDeleteAsync();
        else if(change is "privacy-scope" or "lead-scope")
            await h.Db.Sys_RoleDataScopes.Where(x=>x.RoleId==71&&x.ResourceKey==(change=="privacy-scope"?"crm-privacy":"crm-lead")).ExecuteUpdateAsync(x=>x.SetProperty(v=>v.ScopeType,1));
        else if(change is "artifact-field" or "pii-field")
            await h.Db.Sys_RoleFieldPerms.Where(x=>x.RoleId==71&&x.ResourceKey==(change=="artifact-field"?"crm-privacy":"crm-intake")&&x.FieldName==(change=="artifact-field"?"exportArtifact":"email")).ExecuteUpdateAsync(x=>x.SetProperty(v=>v.Access,3));
        else issuer=new(db,tenant,registry,key,()=>IntakeCatalog(),runtime);
        var error=await Record.ExceptionAsync(()=>issuer.IssueExportContextV2Async(request,ServiceBasic(),default));
        Assert.NotNull(error);
        if(change=="catalog")Assert.IsType<CP6.Core.Services.Sys.CurrentPermissionUnavailableException>(error);
        else Assert.Equal(403,Assert.IsType<InquiryPermissionExceptionV1>(error).Status);
        Assert.Null(db.Database.CurrentTransaction);
    }
}
