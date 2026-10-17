using System.Text;
using CP6.Core.Persistence;
using CP6.Tests.Persistence.Tenancy;
using CP6.WebApi.Configuration;
using CP6.WebApi.Services;
using CP6.WebApi.Services.ServiceRevocationTenancy;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CP6.Tests;

public sealed class ServiceTokenConfigurationTests
{
    private static ServiceTokenPostgresFixture Fixture()
    {
        var fixture = new ServiceTokenPostgresFixture();
        fixture.Installation = new(Guid.NewGuid(),"127.0.0.1",TenantStoragePostgresFixture.Port,
            ServiceRevocationPostgresFixture.Database,42,"123","unused",
            [new(ServiceRevocationPostgresFixture.A,"tenant_a"),new(ServiceRevocationPostgresFixture.B,"tenant_b")]);
        return fixture;
    }

    [Theory]
    [InlineData("CrmIdentity:ServiceRevocationTenancy:Enabled","true")]
    [InlineData("CrmIdentity:ServiceTokenTenancy:ModelVersion","service-revocation-v1")]
    [InlineData("CrmIdentity:ServiceTokenTenancy:Server","localhost")]
    [InlineData("CrmIdentity:ServiceTokenTenancy:DatabaseOid","0")]
    [InlineData("CrmIdentity:ServiceTokenTenancy:Enabled","false")]
    [InlineData("CrmIdentity:ServiceTokenTenancy:Tenants:0:Password","")]
    public void Wrong_or_ambiguous_profile_configuration_is_rejected(string key,string value)
    {
        var fixture=Fixture(); var values=fixture.Configuration(DateTimeOffset.UtcNow); values[key]=value;
        var configuration=new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        Assert.ThrowsAny<Exception>(()=>new ServiceCollection().AddCrmIdentityEvents(configuration,fixture.Options(),new(DatabaseProvider.PostgreSql)));
    }

    [Fact]
    public async Task Frozen_issuance_qualification_and_revocation_permissions_are_separate()
    {
        var fixture=Fixture(); var clock=new RevocationClock();
        var options=fixture.Options(); options.ServiceClients[0].AllowedScopes=[];
        var values=fixture.Configuration(clock.GetUtcNow());
        var configuration=new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        var services=new ServiceCollection(); services.AddSingleton<TimeProvider>(clock);
        services.AddCrmIdentityEvents(configuration,options,new(DatabaseProvider.PostgreSql));
        await using var provider=services.BuildServiceProvider();
        var host=provider.GetRequiredService<ServiceRevocationTenantHost>();
        var verifier=new ServiceRevocationTenantHost.SourceVerifier(host);
        var input=new ServiceRevocationTenantHost.SourceInput(
            ["Basic "+Convert.ToBase64String(Encoding.UTF8.GetBytes("client-a:"+fixture.ServiceSecret))],"client-a",ServiceRevocationPostgresFixture.A);
        Assert.NotNull(await verifier.VerifyAsync(input)); Assert.Null(await verifier.VerifyAsync(input with { Issue=true }));
        options.ServiceClients[0].AllowedScopes=["cp6.services"];
        Assert.Null(await verifier.VerifyAsync(input with { Issue=true })); // Startup snapshot, no live reload.
        Assert.Equal(0,host.PoolSnapshot.Pools);
        options.Organizations[0].CrmEnabled=false;
        Assert.Throws<InvalidOperationException>(()=>new ServiceCollection().AddCrmIdentityEvents(configuration,options,new(DatabaseProvider.PostgreSql)));
    }
}
