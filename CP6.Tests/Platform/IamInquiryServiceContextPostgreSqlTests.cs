using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using CP6.Core.Services.Common;
using CP6.Core.Services.CrmIdentity;
using CP6.WebApi.Services;
using CP6.Platform.Messaging;
using CP6.Platform.EntityFramework;
using CP6.Core.Services.Sys;
using CP6.Entity.DomainModels.Sys;
using Microsoft.EntityFrameworkCore;
using NSec.Cryptography;

namespace CP6.Tests.Platform;

public sealed partial class IamUserAssignmentsPostgreSqlTests
{
    private static string ServiceNonce() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+','-').Replace('/','_');
    private static CrmIdentityRuntime ContextRuntime(Guid tenant, string issuer)
    {
        var options=new CrmIdentityOptions { Enabled=true,Issuer=issuer,ProjectionReaderClientIds=["fixture-reader"] };
        options.Tenants.Add(tenant,"us");return new(options,new IdentityEventValidator(Cp6ContractBundle.Load(Path.Combine(AppContext.BaseDirectory,"contracts/events/platform")),issuer));
    }
    private static bool ContextSignature(JsonObject body, string publicKey, string domain="cp6-inquiry-service-context-v1")
    {
        var signature=body["signature"]!.GetValue<string>();
        static JsonNode? Sort(JsonNode? value)=>value switch
        {
            JsonObject o=>new JsonObject(o.OrderBy(x=>x.Key,StringComparer.Ordinal).Select(x=>new KeyValuePair<string,JsonNode?>(x.Key,Sort(x.Value)))),
            JsonArray a=>new JsonArray(a.Select(Sort).ToArray()),_=>value?.DeepClone()
        };
        var unsigned=body.DeepClone().AsObject();unsigned.Remove("signature");var ordered=Sort(unsigned)!;
        var bytes=Encoding.UTF8.GetBytes(domain+"\n"+ordered.ToJsonString());
        return SignatureAlgorithm.Ed25519.Verify(PublicKey.Import(SignatureAlgorithm.Ed25519,Convert.FromBase64String(publicKey),KeyBlobFormat.RawPublicKey),bytes,
            Convert.FromBase64String(signature.Replace('-','+').Replace('_','/')+"=="));
    }
    [CurrentPermissionPostgresFact]
    public async Task Inquiry_service_context_binds_actual_committed_source_nonce_and_unchanged_permission_under_one_authentication()
    {
        await using var h=await Seed();var pair=await GrantIntake(h);var options=InquiryPermissionSigningTests.Options();
        using var key=InquiryPermissionSigningTests.Signer(options);var runtime=ContextRuntime(h.Tenant,options.Issuer);
        await new IdentityBootstrapService(h.Db,runtime).InitializeAsync(h.Tenant);h.Db.ChangeTracker.Clear();
        var config=ServiceRegistration(h,Guid.NewGuid());config.Source.IdentityPublicationEnabled=true;
        config.Source.IdentityPublicationRevision=long.MaxValue-1;config.Source.Registrations[0].AuthenticationEpoch=Guid.NewGuid();
        var registry=new InquiryServicePrincipalRegistryV1(config.Source,config.Oidc);var tenant=new TenantContext{CurrentTenantId=Guid.NewGuid()};var before=tenant.CurrentTenantId;
        await using var db=server.Context(tenant,runtime);var issuer=new InquiryServicePermissionIssuerV1(db,tenant,registry,key,()=>IntakeCatalog(),runtime);
        var request=new InquiryServiceContextRequestV1("intake:release",pair.SiteId,pair.FormId,ServiceNonce());
        Assert.Equal("inquiry_service_identity_not_published",(await Assert.ThrowsAsync<InquiryPermissionExceptionV1>(()=>issuer.IssueContextAsync(request,ServiceBasic(),default))).Message);
        await new InquiryServiceIdentityPublisherV1(db,tenant,registry,runtime).PublishAsync(h.Tenant);db.ChangeTracker.Clear();
        var json=await issuer.IssueContextAsync(request,ServiceBasic(),default);var body=JsonNode.Parse(json)!.AsObject();
        Assert.True(ContextSignature(body,options.PublicKeyBase64));Assert.Equal(before,tenant.CurrentTenantId);Assert.Null(db.Database.CurrentTransaction);
        Assert.Equal(request.RequestNonce,body["requestNonce"]!.GetValue<string>());Assert.Equal(ServiceClient,body["clientId"]!.GetValue<string>());
        Assert.Equal(config.Source.IdentityPublicationRevision,body["registrationRevision"]!.GetValue<long>());
        var source=await h.Db.CrmIdentitySnapshots.AsNoTracking().SingleAsync(x=>x.AggregateId=="service-principal:"+config.Source.Registrations[0].SubjectId.ToString("D"));
        Assert.Equal(source.Version,body["sourceVersion"]!.GetValue<int>());Assert.Equal(source.PayloadSha256,body["sourcePayloadSha256"]!.GetValue<string>());
        Assert.Equal(runtime.ContractBundleSha256,body["contractBundleSha256"]!.GetValue<string>());
        var projection=JsonNode.Parse(body["permissionJson"]!.GetValue<string>())!.AsObject();
        Assert.Equal("crm-iam-projection-v1",projection["schemaVersion"]!.GetValue<string>());Assert.False(projection.ContainsKey("clientId"));
        Assert.Equal(body["subjectId"]!.GetValue<string>(),projection["subjectId"]!.GetValue<string>());
        body["authenticationEpoch"]=Guid.NewGuid().ToString("D");Assert.False(ContextSignature(body,options.PublicKeyBase64));
        foreach(var nonce in new[]{"",new string('!',43),ServiceNonce()+"="})
            Assert.Equal(400,(await Assert.ThrowsAsync<InquiryPermissionExceptionV1>(()=>issuer.IssueContextAsync(request with { RequestNonce=nonce },ServiceBasic(),default))).Status);
        Assert.Equal(401,(await Assert.ThrowsAsync<InquiryPermissionExceptionV1>(()=>issuer.IssueContextAsync(request,ServiceBasic("wrong"),default))).Status);
        var export=Environment.GetEnvironmentVariable("CP6_SERVICE_CONTEXT_OUTPUT");
        if(!string.IsNullOrWhiteSpace(export))
        {
            var rows=await h.Db.CrmIdentitySnapshots.AsNoTracking().Where(x=>x.TenantId==h.Tenant).OrderBy(x=>x.AggregateId).ToArrayAsync();
            await File.WriteAllTextAsync(export,JsonSerializer.Serialize(new { contextJson=json,request,publicKey=options.PublicKeyBase64,keyId=options.KeyId,
                keyFromUtc=options.ActiveFromUtc,issuer=options.Issuer,tenantId=h.Tenant,departmentId=h.Parent,subjectId=config.Source.Registrations[0].SubjectId,
                rows=rows.Select(x=>new{x.AggregateId,x.EventType,x.Version,x.PayloadSha256,x.IsDeleted,data=JsonDocument.Parse(x.PayloadJson).RootElement}) },IdentityEventContracts.Json));
        }
        config.Source.Version="owned-test-context-v2";config.Source.IdentityPublicationRevision=long.MaxValue;config.Source.Registrations[0].AuthenticationEpoch=Guid.NewGuid();
        var updated=new InquiryServicePrincipalRegistryV1(config.Source,config.Oidc);
        await new InquiryServiceIdentityPublisherV1(db,tenant,updated,runtime).PublishAsync(h.Tenant);db.ChangeTracker.Clear();
        Assert.Equal("inquiry_service_identity_not_current",(await Assert.ThrowsAsync<InquiryPermissionExceptionV1>(()=>issuer.IssueContextAsync(request,ServiceBasic(),default))).Message);
        var fresh=new InquiryServicePermissionIssuerV1(db,tenant,updated,key,()=>IntakeCatalog(),runtime);
        var latest=JsonNode.Parse(await fresh.IssueContextAsync(request,ServiceBasic(),default))!.AsObject();
        Assert.Equal(long.MaxValue,latest["registrationRevision"]!.GetValue<long>());Assert.True(ContextSignature(latest,options.PublicKeyBase64));
        var oldRoleVersion=latest["roleSources"]!.AsArray().Single(x=>x!["aggregateId"]!.GetValue<string>()=="permission:role:71")!["version"]!.GetValue<int>();
        var actorTenant=new TenantContext { CurrentTenantId=h.Tenant };
        await using(var change=server.Context(actorTenant,runtime))
        await using(var transaction=await change.Database.BeginTransactionAsync())
        {
            await AuthenticationCompletionGateV1.AcquireAsync(change,default);
            change.Sys_RoleFieldPerms.Add(new Sys_RoleFieldPerm { RoleId=71,ResourceKey="crm-intake",FieldName="email",Access=3 });
            await change.SaveChangesAsync();await transaction.CommitAsync();
        }
        var invalidated=await h.Db.CrmIdentitySnapshots.AsNoTracking().SingleAsync(x=>x.TenantId==h.Tenant&&x.AggregateId=="permission:role:71");
        Assert.True(invalidated.Version>oldRoleVersion,"Actual intake field change must invalidate the bound C02 role source even without a crm-intake action.");
        Assert.Equal(403,(await Assert.ThrowsAsync<InquiryPermissionExceptionV1>(()=>fresh.IssueContextAsync(request,ServiceBasic(),default))).Status);
        var publicOnly=JsonNode.Parse(await fresh.IssueContextAsync(request with { Purpose="public:receive" },ServiceBasic(),default))!.AsObject();
        Assert.Null(publicOnly["permissionJson"]);Assert.Equal("public:receive",publicOnly["purpose"]!.GetValue<string>());
        Assert.True(ContextSignature(publicOnly,options.PublicKeyBase64));
        Assert.Equal(invalidated.Version,publicOnly["roleSources"]!.AsArray().Single(x=>x!["aggregateId"]!.GetValue<string>()=="permission:role:71")!["version"]!.GetValue<int>());
        foreach(var state in new[]{"revoked","future","expired"})
        {
            var blocked=InquiryPermissionSigningTests.Options();blocked.Revoked=state=="revoked";
            if(state=="future")blocked.ActiveFromUtc=DateTimeOffset.UtcNow.AddHours(1);
            if(state=="expired"){blocked.ActiveFromUtc=DateTimeOffset.UtcNow.AddHours(-2);blocked.ActiveToUtc=DateTimeOffset.UtcNow.AddHours(-1);}
            using var closed=InquiryPermissionSigningTests.Signer(blocked);
            var invalidSigner=new InquiryServicePermissionIssuerV1(db,tenant,updated,closed,()=>IntakeCatalog(),runtime);
            Assert.Equal("inquiry_signer_unavailable",(await Assert.ThrowsAsync<InquiryPermissionExceptionV1>(()=>invalidSigner.IssueContextAsync(request with{Purpose="public:receive"},ServiceBasic(),default))).Message);
        }
    }
    [CurrentPermissionPostgresFact]
    public async Task Inquiry_service_context_never_returns_after_unknown_commit_and_cannot_sign_unpublished_or_foreign_issuer_source()
    {
        await using var h=await Seed();var pair=await GrantIntake(h);var options=InquiryPermissionSigningTests.Options();
        using var key=InquiryPermissionSigningTests.Signer(options);var runtime=ContextRuntime(h.Tenant,options.Issuer);
        await new IdentityBootstrapService(h.Db,runtime).InitializeAsync(h.Tenant);h.Db.ChangeTracker.Clear();
        var config=ServiceRegistration(h,Guid.NewGuid());var tenant=new TenantContext{CurrentTenantId=Guid.NewGuid()};var before=tenant.CurrentTenantId;var fault=new CommitFault();
        await using var db=server.Context(tenant,runtime,fault);var request=new InquiryServiceContextRequestV1("intake:release",pair.SiteId,pair.FormId,ServiceNonce());
        var disabled=new InquiryServicePermissionIssuerV1(db,tenant,new(config.Source,config.Oidc),key,()=>IntakeCatalog(),runtime);
        Assert.Equal("inquiry_service_context_unavailable",(await Assert.ThrowsAsync<InquiryPermissionExceptionV1>(()=>disabled.IssueContextAsync(request,ServiceBasic(),default))).Message);
        config.Source.IdentityPublicationEnabled=true;config.Source.IdentityPublicationRevision=1;config.Source.Registrations[0].AuthenticationEpoch=Guid.NewGuid();
        var registry=new InquiryServicePrincipalRegistryV1(config.Source,config.Oidc);
        await new InquiryServiceIdentityPublisherV1(db,tenant,registry,runtime).PublishAsync(h.Tenant);db.ChangeTracker.Clear();
        var issuer=new InquiryServicePermissionIssuerV1(db,tenant,registry,key,()=>IntakeCatalog(),runtime);
        fault.Armed=true;await Assert.ThrowsAsync<IOException>(()=>issuer.IssueContextAsync(request,ServiceBasic(),default));
        Assert.Equal(before,tenant.CurrentTenantId);Assert.Null(db.Database.CurrentTransaction);
        using var cancellation=new CancellationTokenSource();cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>issuer.IssueContextAsync(request,ServiceBasic(),cancellation.Token));
        var foreign=new InquiryServicePermissionIssuerV1(db,tenant,registry,key,()=>IntakeCatalog(),ContextRuntime(h.Tenant,"https://iam.other.test"));
        Assert.Equal("inquiry_service_context_unavailable",(await Assert.ThrowsAsync<InquiryPermissionExceptionV1>(()=>foreign.IssueContextAsync(request,ServiceBasic(),default))).Message);
    }
    [CurrentPermissionPostgresFact]
    public async Task Inquiry_field_policy_source_revision_and_outbox_follow_commit_and_rollback_without_fake_actions()
    {
        await using var h=await Seed();await GrantIntake(h);var runtime=ContextRuntime(h.Tenant,InquiryPermissionSigningTests.Issuer);
        await new IdentityBootstrapService(h.Db,runtime).InitializeAsync(h.Tenant);h.Db.ChangeTracker.Clear();
        const string aggregate="permission:role:71";
        var before=await h.Db.CrmIdentitySnapshots.AsNoTracking().SingleAsync(x=>x.TenantId==h.Tenant&&x.AggregateId==aggregate);
        var effects=await h.Db.Set<Cp6OutboxMessage>().CountAsync(x=>x.AggregateId==aggregate);
        foreach(var commit in new[]{false,true})
        {
            await using var db=server.Context(new TenantContext{CurrentTenantId=h.Tenant},runtime);await using var tx=await db.Database.BeginTransactionAsync();
            await AuthenticationCompletionGateV1.AcquireAsync(db,default);
            db.Sys_RoleFieldPerms.Add(new Sys_RoleFieldPerm{RoleId=71,ResourceKey="crm-intake",FieldName="email",Access=3});
            await db.SaveChangesAsync();
            var staged=await db.CrmIdentitySnapshots.AsNoTracking().SingleAsync(x=>x.TenantId==h.Tenant&&x.AggregateId==aggregate);
            Assert.Equal(before.Version+1,staged.Version);Assert.NotEqual(before.PayloadSha256,staged.PayloadSha256);
            if(commit)await tx.CommitAsync();else await tx.RollbackAsync();
            var current=await h.Db.CrmIdentitySnapshots.AsNoTracking().SingleAsync(x=>x.TenantId==h.Tenant&&x.AggregateId==aggregate);
            Assert.Equal(before.Version+(commit?1:0),current.Version);
            Assert.Equal(effects+(commit?1:0),await h.Db.Set<Cp6OutboxMessage>().CountAsync(x=>x.AggregateId==aggregate));
            using var data=JsonDocument.Parse(current.PayloadJson);
            Assert.DoesNotContain(data.RootElement.GetProperty("grants").EnumerateArray(),g=>g.GetProperty("resource").GetString()=="crm-intake");
        }
        await using(var db=server.Context(new TenantContext{CurrentTenantId=h.Tenant},runtime))await using(var tx=await db.Database.BeginTransactionAsync())
        {
            await AuthenticationCompletionGateV1.AcquireAsync(db,default);
            db.Sys_RoleFieldPerms.Remove(await db.Sys_RoleFieldPerms.SingleAsync(x=>x.RoleId==71&&x.ResourceKey=="crm-intake"&&x.FieldName=="email"));
            await db.SaveChangesAsync();await tx.CommitAsync();
        }
        Assert.Equal(before.Version+2,(await h.Db.CrmIdentitySnapshots.AsNoTracking().SingleAsync(x=>x.TenantId==h.Tenant&&x.AggregateId==aggregate)).Version);
    }

}
