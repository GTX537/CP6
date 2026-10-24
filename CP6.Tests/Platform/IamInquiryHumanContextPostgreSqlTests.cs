using System.Text.Json;
using System.Text.Json.Nodes;
using CP6.Core.Services.CrmIdentity;
using CP6.Core.Services.Common;
using CP6.Core.Services.Sys;
using CP6.Entity.DomainModels.Sys;
using CP6.WebApi.Services;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests.Platform;

public sealed partial class IamUserAssignmentsPostgreSqlTests
{
    [CurrentPermissionPostgresFact]
    public async Task Inquiry_human_context_binds_all_committed_roles_nonce_and_unchanged_permission_and_field_revision()
    {
        await using var h=await Seed();var pair=await GrantIntake(h);var subject=InquirySubject(h);
        h.Db.Sys_UserRoles.Add(new(){UserId=h.ActorId,RoleId=72});await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();
        (await h.Db.Sys_Users.SingleAsync(x=>x.Id==h.ActorId)).AuthenticationEpoch=Guid.Parse("11111111-1111-7111-8111-111111111111");await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();
        var options=InquiryPermissionSigningTests.Options();using var key=InquiryPermissionSigningTests.Signer(options);
        var runtime=ContextRuntime(h.Tenant,options.Issuer);await new IdentityBootstrapService(h.Db,runtime).InitializeAsync(h.Tenant);h.Db.ChangeTracker.Clear();
        var service=new InquiryPermissionIssuerV1(h.Db,key,()=>IntakeCatalog(),runtime);
        var request=new InquiryHumanContextRequestV1("intake:detail",pair.SiteId,pair.FormId,ServiceNonce());
        Task<string> IssueHuman(InquiryHumanContextRequestV1? r=null)=>service.IssueHumanContextAsync(r??request,(_,_)=>Task.FromResult<InquiryPermissionSubjectV1?>(subject),default);
        var json=await IssueHuman();var outer=JsonNode.Parse(json)!.AsObject();Assert.True(ContextSignature(outer,options.PublicKeyBase64,InquiryPermissionSigningKeyV1.HumanContextVersion));
        var user=await h.Db.CrmIdentitySnapshots.AsNoTracking().SingleAsync(x=>x.AggregateId=="user:"+h.ActorId.ToString("D"));
        Assert.Equal(user.PayloadSha256,outer["userSource"]!["payloadSha256"]!.GetValue<string>());
        Assert.Equal(request.RequestNonce,outer["requestNonce"]!.GetValue<string>());
        Assert.Equal(2,outer["roleSources"]!.AsArray().Count);Assert.Contains(outer["roleSources"]!.AsArray(),x=>x!["aggregateId"]!.GetValue<string>()=="permission:role:72");
        var inner=JsonNode.Parse(outer["permissionJson"]!.GetValue<string>())!;
        Assert.Equal("crm-iam-projection-v1",inner["schemaVersion"]!.GetValue<string>());Assert.Null(inner["userSource"]);
        Assert.Equal(6,inner["fieldPermissions"]!.AsArray().Count);
        if(Environment.GetEnvironmentVariable("CP6_HUMAN_CONTEXT_OUTPUT") is {Length:>0} output)
            await File.WriteAllTextAsync(output,JsonSerializer.Serialize(new{contextJson=json,request,publicKeyBase64=options.PublicKeyBase64,
                activeFromUtc=options.ActiveFromUtc,issuer=options.Issuer,tenantId=h.Tenant,subjectId=h.ActorId,
                epoch=outer["authenticationEpoch"]!.GetValue<string>(),bundle=runtime.ContractBundleSha256}));
        var old=outer["roleSources"]!.AsArray().Single(x=>x!["aggregateId"]!.GetValue<string>()=="permission:role:72")!["version"]!.GetValue<int>();
        await using(var db=server.Context(new TenantContext{CurrentTenantId=h.Tenant},runtime))
        await using(var tx=await db.Database.BeginTransactionAsync())
        {
            await AuthenticationCompletionGateV1.AcquireAsync(db,default);
            db.Sys_RoleFieldPerms.Add(new(){RoleId=72,ResourceKey="crm-intake",FieldName="email",Access=3});
            await db.SaveChangesAsync();await tx.CommitAsync();
        }
        var fresh=JsonNode.Parse(await IssueHuman())!;
        Assert.True(fresh["roleSources"]!.AsArray().Single(x=>x!["aggregateId"]!.GetValue<string>()=="permission:role:72")!["version"]!.GetValue<int>()>old);
        Assert.DoesNotContain(JsonNode.Parse(fresh["permissionJson"]!.GetValue<string>())!["fieldPermissions"]!.AsArray(),x=>x!.GetValue<string>()=="email");
        foreach(var kind in new[]{"nonce","purpose","site"})
        {
            var bad=kind=="nonce"?request with{RequestNonce="bad"}:kind=="purpose"?request with{Purpose="public:receive"}:request with{SiteId=Guid.NewGuid()};
            Assert.Equal(kind=="site"?403:400,(await Assert.ThrowsAsync<InquiryPermissionExceptionV1>(()=>IssueHuman(bad))).Status);
        }
        outer["roleSources"]!.AsArray().RemoveAt(1);Assert.False(ContextSignature(outer,options.PublicKeyBase64,InquiryPermissionSigningKeyV1.HumanContextVersion));
    }
    [CurrentPermissionPostgresFact]
    public async Task Inquiry_human_context_requires_published_current_source_final_session_and_successful_commit()
    {
        var fault=new CommitFault();await using var h=await Seed(fault);var pair=await GrantIntake(h);var subject=InquirySubject(h);
        (await h.Db.Sys_Users.SingleAsync(x=>x.Id==h.ActorId)).AuthenticationEpoch=Guid.NewGuid();await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();
        var options=InquiryPermissionSigningTests.Options();using var key=InquiryPermissionSigningTests.Signer(options);var runtime=ContextRuntime(h.Tenant,options.Issuer);
        var issuer=new InquiryPermissionIssuerV1(h.Db,key,()=>IntakeCatalog(),runtime);
        var request=new InquiryHumanContextRequestV1("intake:list",pair.SiteId,pair.FormId,ServiceNonce());
        Task<string> Send()=>issuer.IssueHumanContextAsync(request,(_,_)=>Task.FromResult<InquiryPermissionSubjectV1?>(subject),default);
        Assert.Equal(503,(await Assert.ThrowsAsync<InquiryPermissionExceptionV1>(Send)).Status);
        await new IdentityBootstrapService(h.Db,runtime).InitializeAsync(h.Tenant);h.Db.ChangeTracker.Clear();await Send();
        var reads=0;
        Assert.Equal(401,(await Assert.ThrowsAsync<InquiryPermissionExceptionV1>(()=>issuer.IssueHumanContextAsync(request,(_,_)=>Task.FromResult<InquiryPermissionSubjectV1?>(++reads==1?subject:subject with{SessionId=Guid.NewGuid()}),default))).Status);
        fault.Armed=true;await Assert.ThrowsAsync<IOException>(Send);Assert.Null(h.Db.Database.CurrentTransaction);
        await h.Db.Sys_Users.Where(x=>x.Id==h.ActorId).ExecuteUpdateAsync(x=>x.SetProperty(u=>u.AuthenticationEpoch,Guid.NewGuid()));
        Assert.Equal(503,(await Assert.ThrowsAsync<InquiryPermissionExceptionV1>(Send)).Status);
    }
    [CurrentPermissionPostgresFact]
    public Task Inquiry_human_context_actual_HTTP_login_OIDC_signing_and_current_revocation()=>InquiryProducerHttp("intake:release",humanContext:true);
    [CurrentPermissionPostgresFact]
    public Task Inquiry_intake_read_context_actual_HTTP_signs_optional_current_decision_facts()=>InquiryProducerHttp("intake:detail",humanContext:true);

}
