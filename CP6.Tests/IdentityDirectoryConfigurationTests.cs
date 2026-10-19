using CP6.Core.Persistence;
using CP6.Tests.Persistence.Tenancy;
using CP6.WebApi.Configuration;
using CP6.WebApi.Services;
using CP6.WebApi.Services.IdentityDirectoryTenancy;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CP6.Tests;

public sealed class IdentityDirectoryConfigurationTests
{
    internal const string Prefix = "CrmIdentity:IdentityDirectoryTenancy:";
    internal static Dictionary<string, string?> Values(DateTimeOffset now) => new()
    {
        ["ConnectionStrings:DefaultConnection"] = "Host=127.0.0.1;Port=55435;Database=directory;Username=fixture_owner",
        ["CrmIdentity:Enabled"] = "true", ["CrmIdentity:Tenants:" + TenantStorageContext.A] = "us",
        ["CrmIdentity:ProjectionReaderClientIds:0"] = "reader-a",
        [Prefix + "Enabled"] = "true", [Prefix + "HostId"] = "main", [Prefix + "Revision"] = "r1",
        [Prefix + "ValidFromUtc"] = now.AddHours(-1).ToString("O"), [Prefix + "ValidUntilUtc"] = now.AddHours(1).ToString("O"),
        [Prefix + "Server"] = "127.0.0.1", [Prefix + "Port"] = "55435", [Prefix + "Database"] = "directory",
        [Prefix + "DatabaseOid"] = "42", [Prefix + "SystemIdentifier"] = "123",
        [Prefix + "InstallationId"] = "33333333-3333-4333-8333-333333333333", [Prefix + "Fingerprint"] = new string('A', 64),
        [Prefix + "SslMode"] = "Disable", [Prefix + "Tenants:0:TenantId"] = TenantStorageContext.A.ToString("D"),
        [Prefix + "Tenants:0:Login"] = "directory_a", [Prefix + "Tenants:0:Password"] = "fixture",
        [Prefix + "Tenants:0:CredentialVersion"] = "v1", [Prefix + "Clients:0:ClientId"] = "reader-a",
        [Prefix + "Clients:0:RegistrationVersion"] = "r1"
    };
    internal static CrmOidcOptions Options() => new()
    {
        Enabled = true, Issuer = "https://directory-isolation.example.test",
        Organizations = [new() { TenantId = TenantStorageContext.A, Region = "us", CrmEnabled = true }],
        ServiceClients = [new() { ClientId = "reader-a", TenantId = TenantStorageContext.A, Enabled = true, AllowedScopes = ["cp6.services"] }]
    };

    [Fact]
    public void Explicit_directory_configuration_cannot_be_silently_ignored()
    {
        var values = Values(DateTimeOffset.UtcNow); values[Prefix + "Unexpected"] = "reject-this";
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        Assert.Throws<InvalidOperationException>(() => new ServiceCollection().AddCrmIdentityEvents(configuration, Options(), new(DatabaseProvider.PostgreSql)));
    }

    [Theory]
    [InlineData("Enabled", "false")]
    [InlineData("Server", "localhost")]
    [InlineData("Database", "wrong-database")]
    [InlineData("DatabaseOid", "0")]
    [InlineData("SystemIdentifier", "00123")]
    [InlineData("Fingerprint", "bad-receipt")]
    [InlineData("SourceLifetimeSeconds", "0")]
    [InlineData("SourceLifetimeSeconds", "301")]
    [InlineData("Tenants:0:Login", "fixture_owner")]
    [InlineData("Tenants:0:Password", "")]
    [InlineData("Tenants:0:CredentialVersion", "")]
    [InlineData("Tenants:1:TenantId", "11111111-1111-4111-8111-111111111111")]
    [InlineData("Clients:0:ClientId", "not-registered")]
    [InlineData("Clients:0:RegistrationVersion", "")]
    public void Invalid_identity_credentials_registration_or_bounds_fail_closed(string name, string value)
    {
        var values = Values(DateTimeOffset.UtcNow); values[Prefix + name] = value;
        var error = Assert.Throws<InvalidOperationException>(() => new ServiceCollection().AddCrmIdentityEvents(
            new ConfigurationBuilder().AddInMemoryCollection(values).Build(), Options(), new(DatabaseProvider.PostgreSql)));
        Assert.Equal("C02_DIRECTORY_CONFIGURATION_REJECTED", error.Message); Assert.Null(error.InnerException);
    }

    [Theory]
    [InlineData("ServiceTokenTenancy")]
    [InlineData("ServiceRevocationTenancy")]
    public void Incompatible_default_database_profiles_are_rejected(string profile)
    {
        var values = Values(DateTimeOffset.UtcNow); values["CrmIdentity:" + profile + ":Enabled"] = "true";
        Assert.Equal("C02_DIRECTORY_CONFIGURATION_REJECTED", Assert.Throws<InvalidOperationException>(() => new ServiceCollection().AddCrmIdentityEvents(
            new ConfigurationBuilder().AddInMemoryCollection(values).Build(), Options(), new(DatabaseProvider.PostgreSql))).Message);
    }

    [Fact]
    public async Task Explicit_registration_is_singleton_and_absent_or_disabled_configuration_keeps_legacy_path()
    {
        var values = Values(DateTimeOffset.UtcNow);
        var services = new ServiceCollection(); services.AddSingleton<IDataProtectionProvider>(new EphemeralDataProtectionProvider());
        services.AddCrmIdentityEvents(new ConfigurationBuilder().AddInMemoryCollection(values).Build(), Options(), new(DatabaseProvider.PostgreSql));
        await using var provider = services.BuildServiceProvider();
        var host = provider.GetRequiredService<IdentityDirectoryTenantHost>();
        Assert.Same(host, provider.GetRequiredService<IdentityDirectoryTenantHost>()); Assert.Equal(0, host.PoolSnapshot.Pools);
        var legacy = values.Where(x => !x.Key.StartsWith(Prefix, StringComparison.Ordinal)).ToDictionary(x => x.Key, x => x.Value);
        foreach (var disabled in new[] { false, true })
        {
            if (disabled) legacy[Prefix + "Enabled"] = "false";
            var old = new ServiceCollection(); old.AddCrmIdentityEvents(new ConfigurationBuilder().AddInMemoryCollection(legacy).Build(), Options(), new(DatabaseProvider.PostgreSql));
            await using var oldProvider = old.BuildServiceProvider(); Assert.Null(oldProvider.GetService<IdentityDirectoryTenantHost>());
        }
    }
}
