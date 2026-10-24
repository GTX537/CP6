using System.Data.Common;
using System.Text.Json;
using CP6.Core.Services.Common;
using CP6.Core.Services.CrmIdentity;
using CP6.Platform.EntityFramework;
using CP6.Platform.Messaging;
using CP6.WebApi.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace CP6.Tests.Platform;

public sealed partial class IamUserAssignmentsPostgreSqlTests
{
    private static CrmIdentityRuntime ServiceRuntime(Guid tenant)
    {
        var options=new CrmIdentityOptions { Enabled=true,Issuer="https://iam-writer.example.test",ProjectionReaderClientIds=["fixture-reader"] };
        options.Tenants.Add(tenant,"us");return new(options,new IdentityEventValidator(Cp6ContractBundle.Load(Path.Combine(AppContext.BaseDirectory,"contracts/events/platform")),options.Issuer));
    }
    [CurrentPermissionPostgresFact]
    public async Task Service_C02_publishes_actual_frozen_source_then_disable_and_removal_as_current_priority_facts()
    {
        await using var h=await Seed();await GrantIntake(h);var runtime=ServiceRuntime(h.Tenant);await new IdentityBootstrapService(h.Db,runtime).InitializeAsync(h.Tenant);
        h.Db.ChangeTracker.Clear();var subject=Guid.NewGuid();var epoch=Guid.NewGuid();var config=ServiceRegistration(h,subject);
        config.Source.IdentityPublicationEnabled=true;config.Source.IdentityPublicationRevision=1;config.Source.Registrations[0].AuthenticationEpoch=epoch;
        async Task Publish(InquiryServicePrincipalRegistryV1 source)
        {var tenant=new TenantContext { CurrentTenantId=Guid.NewGuid() };var before=tenant.CurrentTenantId;await using var db=server.Context(tenant,runtime);
         await new InquiryServiceIdentityPublisherV1(db,tenant,source,runtime).PublishAsync(h.Tenant);Assert.Equal(before,tenant.CurrentTenantId);Assert.True(source.IsPublished(h.Tenant));}
        var registry=new InquiryServicePrincipalRegistryV1(config.Source,config.Oidc);await Publish(registry);
        var aggregate="service-principal:"+subject.ToString("D");var snapshot=await h.Db.CrmIdentitySnapshots.AsNoTracking().SingleAsync(x=>x.AggregateId==aggregate);
        Assert.Equal(1,snapshot.Version);Assert.False(snapshot.IsDeleted);var data=JsonSerializer.Deserialize<ServicePrincipalIdentityData>(snapshot.PayloadJson,IdentityEventContracts.Json)!;
        Assert.Equal(subject,data.PrincipalId);Assert.Equal(epoch,data.AuthenticationEpoch);Assert.Equal(new[]{"role:71"},data.Roles);Assert.Equal(h.Parent,data.DepartmentId);
        Assert.True(await h.Db.Set<Cp6OutboxMessage>().AnyAsync(x=>x.AggregateId==aggregate&&x.AggregateVersion==1));Assert.False(await h.Db.Sys_Users.AnyAsync(x=>x.Id==subject));
        await Publish(registry);Assert.Equal(1,await h.Db.CrmIdentitySnapshots.Where(x=>x.AggregateId==aggregate).Select(x=>x.Version).SingleAsync());
        var export=Environment.GetEnvironmentVariable("CP6_SERVICE_IDENTITY_OUTPUT");
        if(!string.IsNullOrWhiteSpace(export))
        {
            var rows=await h.Db.CrmIdentitySnapshots.AsNoTracking().Where(x=>x.TenantId==h.Tenant).OrderBy(x=>x.AggregateId).ToArrayAsync();
            await File.WriteAllTextAsync(export,JsonSerializer.Serialize(new { tenantId=h.Tenant,subjectId=subject,epoch,clientId=ServiceClient,
                issuer=runtime.Options.Issuer,bundleSha256=runtime.ContractBundleSha256,rows=rows.Select(x=>new {x.AggregateId,x.EventType,x.Version,x.PayloadSha256,x.IsDeleted,data=JsonDocument.Parse(x.PayloadJson).RootElement}) },IdentityEventContracts.Json));
        }
        config.Source.Version="owned-test-v2";config.Source.IdentityPublicationRevision=2;config.Source.Registrations[0].AuthenticationEpoch=Guid.NewGuid();config.Oidc.ServiceClients[0].Enabled=false;
        await Publish(new(config.Source,config.Oidc));var disabled=await h.Db.CrmIdentitySnapshots.AsNoTracking().SingleAsync(x=>x.AggregateId==aggregate);
        Assert.Equal(2,disabled.Version);Assert.False(disabled.IsDeleted);Assert.False(JsonDocument.Parse(disabled.PayloadJson).RootElement.GetProperty("enabled").GetBoolean());
        config.Source.Version="owned-test-v3";config.Source.IdentityPublicationRevision=3;config.Source.Registrations.Clear();await Publish(new(config.Source,config.Oidc));
        var removed=await h.Db.CrmIdentitySnapshots.AsNoTracking().SingleAsync(x=>x.AggregateId==aggregate);Assert.Equal(3,removed.Version);Assert.True(removed.IsDeleted);
        Assert.Equal("C02_SERVICE_REGISTRATION_REVISION_STALE",(await Assert.ThrowsAsync<InvalidOperationException>(()=>Publish(registry))).Message);
        Assert.True(await h.Db.CrmIdentitySnapshots.Where(x=>x.AggregateId==aggregate).Select(x=>x.IsDeleted).SingleAsync());
        using(var key=InquiryPermissionSigningTests.Signer(InquiryPermissionSigningTests.Options()))
        {
            var tenant=new TenantContext { CurrentTenantId=Guid.NewGuid() };await using var db=server.Context(tenant,runtime);
            var issuer=new InquiryServicePermissionIssuerV1(db,tenant,registry,key,()=>IntakeCatalog());
            Assert.Equal("inquiry_service_identity_not_current",(await Assert.ThrowsAsync<InquiryPermissionExceptionV1>(()=>issuer.IssueAsync(new("intake:release",Guid.NewGuid(),Guid.NewGuid()),ServiceBasic(),default))).Message);
        }
        await new IdentityBootstrapService(h.Db,runtime).InitializeAsync(h.Tenant); // Service facts are never reinterpreted as Sys_User.
    }
    [CurrentPermissionPostgresFact]
    public async Task Service_C02_requires_new_epoch_and_version_and_actual_commit_before_permission_issuance()
    {
        await using var h=await Seed();var pair=await GrantIntake(h);var runtime=ServiceRuntime(h.Tenant);await new IdentityBootstrapService(h.Db,runtime).InitializeAsync(h.Tenant);h.Db.ChangeTracker.Clear();
        var config=ServiceRegistration(h,Guid.NewGuid());config.Source.IdentityPublicationEnabled=true;config.Source.IdentityPublicationRevision=1;config.Source.Registrations[0].AuthenticationEpoch=Guid.NewGuid();
        var registry=new InquiryServicePrincipalRegistryV1(config.Source,config.Oidc);var tenant=new TenantContext{CurrentTenantId=Guid.NewGuid()};
        using var key=InquiryPermissionSigningTests.Signer(InquiryPermissionSigningTests.Options());
        await using(var db=server.Context(tenant,runtime))
        {
            var issuer=new InquiryServicePermissionIssuerV1(db,tenant,registry,key,()=>IntakeCatalog());
            Assert.Equal("inquiry_service_identity_not_published",(await Assert.ThrowsAsync<InquiryPermissionExceptionV1>(()=>issuer.IssueAsync(new("intake:release",pair.SiteId,pair.FormId),ServiceBasic(),default))).Message);
            await new InquiryServiceIdentityPublisherV1(db,tenant,registry,runtime).PublishAsync(h.Tenant);
        }
        await using(var db=server.Context(tenant,runtime))
        {
            var issuer=new InquiryServicePermissionIssuerV1(db,tenant,registry,key,()=>IntakeCatalog());
            using var result=JsonDocument.Parse(await issuer.IssueAsync(new("intake:release",pair.SiteId,pair.FormId),ServiceBasic(),default));
            Assert.Equal(config.Source.Registrations[0].SubjectId,result.RootElement.GetProperty("subjectId").GetGuid());
        }
        config.Source.Version="changed-without-epoch";config.Source.IdentityPublicationRevision=2;var invalid=new InquiryServicePrincipalRegistryV1(config.Source,config.Oidc);
        await using(var db=server.Context(tenant,runtime))
            Assert.Equal("C02_SERVICE_REGISTRATION_REQUIRES_NEW_EPOCH_AND_VERSION",(await Assert.ThrowsAsync<InvalidOperationException>(()=>new InquiryServiceIdentityPublisherV1(db,tenant,invalid,runtime).PublishAsync(h.Tenant))).Message);
        Assert.False(invalid.IsPublished(h.Tenant));
    }
    private sealed class ServicePublicationFault:DbTransactionInterceptor
    {
        public override ValueTask<InterceptionResult> TransactionCommittingAsync(DbTransaction tx,TransactionEventData data,InterceptionResult result,CancellationToken ct=default)
            =>throw new InvalidOperationException("service-source-before-commit");
    }
    [CurrentPermissionPostgresFact]
    public async Task Service_C02_failed_commit_rolls_back_snapshot_and_outbox_and_does_not_open_source_gate()
    {
        await using var h=await Seed();var runtime=ServiceRuntime(h.Tenant);await new IdentityBootstrapService(h.Db,runtime).InitializeAsync(h.Tenant);h.Db.ChangeTracker.Clear();
        var config=ServiceRegistration(h,Guid.NewGuid());config.Source.IdentityPublicationEnabled=true;config.Source.IdentityPublicationRevision=1;config.Source.Registrations[0].AuthenticationEpoch=Guid.NewGuid();
        var registry=new InquiryServicePrincipalRegistryV1(config.Source,config.Oidc);var tenant=new TenantContext{CurrentTenantId=Guid.NewGuid()};var before=tenant.CurrentTenantId;
        await using(var db=server.Context(tenant,runtime,new ServicePublicationFault()))
            await Assert.ThrowsAsync<InvalidOperationException>(()=>new InquiryServiceIdentityPublisherV1(db,tenant,registry,runtime).PublishAsync(h.Tenant));
        var aggregate="service-principal:"+config.Source.Registrations[0].SubjectId.ToString("D");
        Assert.False(registry.IsPublished(h.Tenant));Assert.Equal(before,tenant.CurrentTenantId);
        Assert.False(await h.Db.CrmIdentitySnapshots.AnyAsync(x=>x.AggregateId==aggregate));Assert.False(await h.Db.Set<Cp6OutboxMessage>().AnyAsync(x=>x.AggregateId==aggregate));
    }
}
