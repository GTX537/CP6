using CP6.Core.Services.CrmIdentity;
using CP6.WebApi.Configuration;
using CP6.WebApi.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CP6.Tests;

public sealed class CrmIdentityConfigurationTests
{
    private static readonly Guid First = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid Second = Guid.Parse("22222222-2222-4222-8222-222222222222");

    [Fact]
    public void Actual_registration_preserves_both_configured_tenants_and_reader_mapping()
    {
        var values = new Dictionary<string, string?>
        {
            ["CrmIdentity:Enabled"] = "true", ["CrmIdentity:Tenants:" + First] = "us",
            ["CrmIdentity:Tenants:" + Second] = "eu", ["CrmIdentity:ProjectionReaderClientIds:0"] = "reader-one",
            ["CrmIdentity:ProjectionReaderClientIds:1"] = "reader-two",
            ["ConnectionStrings:DefaultConnection"] = "Server=localhost;Database=fixture;Integrated Security=True;MultipleActiveResultSets=False",
            ["Security:Password:ExpiryDays"] = "90"
        };
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        var oidc = new CrmOidcOptions
        {
            Enabled = true, Issuer = "https://identity.cp6.test",
            Organizations = [new() { TenantId = First, Region = "us", CrmEnabled = true }, new() { TenantId = Second, Region = "eu", CrmEnabled = true }],
            ServiceClients = [new() { ClientId = "reader-one", TenantId = First, Enabled = true, AllowedScopes = ["cp6.services"] },
                new() { ClientId = "reader-two", TenantId = Second, Enabled = true, AllowedScopes = ["cp6.services"] }]
        };
        var services = new ServiceCollection();
        services.AddCrmIdentityEvents(configuration, oidc);
        using var provider = services.BuildServiceProvider();
        var runtime = provider.GetRequiredService<CrmIdentityRuntime>();
        Assert.Equal(2, runtime.Options.Tenants.Count);
        Assert.Equal("us", runtime.Options.Tenants[First]);
        Assert.Equal("eu", runtime.Options.Tenants[Second]);
        Assert.Equal(90, runtime.Options.PasswordMaxAgeDays);
        Assert.Equal(oidc.Issuer, runtime.Options.Issuer);
        oidc.ServiceClients[1].TenantId = Guid.NewGuid();
        Assert.Equal("C02_REQUIRES_EXPLICIT_MAPPED_READERS", Assert.Throws<InvalidOperationException>(() =>
            new ServiceCollection().AddCrmIdentityEvents(configuration, oidc)).Message);
    }

    [Theory]
    [InlineData("not-a-tenant", "us")]
    [InlineData("00000000-0000-0000-0000-000000000000", "us")]
    [InlineData("11111111111141118111111111111111", "us")]
    [InlineData("11111111-1111-4111-8111-111111111111", "")]
    public void Invalid_tenant_entries_are_rejected_at_configuration_boundary(string tenant, string region)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        { ["CrmIdentity:Enabled"] = "true", ["CrmIdentity:Tenants:" + tenant] = region }).Build();
        Assert.Equal("C02_REQUIRES_VALID_TENANT_MAP", Assert.Throws<InvalidOperationException>(() => CrmIdentityConfiguration.BindOptions(configuration)).Message);
    }

    [Fact]
    public void Empty_enabled_map_fails_but_disabled_feature_preserves_existing_startup()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["CrmIdentity:Enabled"] = "true" }).Build();
        Assert.Throws<InvalidOperationException>(() => CrmIdentityConfiguration.BindOptions(configuration));
        configuration["CrmIdentity:Enabled"] = "false";
        var services = new ServiceCollection();
        services.AddCrmIdentityEvents(configuration, new());
        Assert.Empty(services);
    }

    [Fact]
    public void Later_provider_overrides_region_without_losing_other_tenants()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            { ["CrmIdentity:Enabled"] = "true", ["CrmIdentity:Tenants:" + First] = "us", ["CrmIdentity:Tenants:" + Second] = "eu" })
            .AddInMemoryCollection(new Dictionary<string, string?> { ["CrmIdentity:Tenants:" + First] = "local" }).Build();
        var options = CrmIdentityConfiguration.BindOptions(configuration);
        Assert.Equal("local", options.Tenants[First]);
        Assert.Equal("eu", options.Tenants[Second]);
    }
}
