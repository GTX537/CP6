using CP6.Core.EFDbContext;
using CP6.Core.Services.Common;
using CP6.Core.Services.Erp;
using CP6.Core.Services.ErpIntegration;
using CP6.Core.Services.Integration;
using CP6.WebApi.Configuration;
using CP6.WebApi.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CP6.ErpIntegration.SqlTests;

[Collection(SqlDatabaseCollection.Name)]
public sealed class ProductionRegistrationSqlTests(SqlDatabaseFixture database)
{
    [Fact]
    public async Task Production_registration_uses_event_tenant_fx_when_request_scope_has_default_tenant()
    {
        var s = new ErpScenario(database);
        await s.InitializeAsync();
        await s.RegisterPartnerAsync(currency: "USD");
        string connection;
        await using (var db = s.Db()) connection = db.Database.GetConnectionString()!;
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = connection,
            ["CrmIdentity:Enabled"] = "true",
            [$"CrmIdentity:Tenants:{s.Tenant:D}"] = "local",
            ["ErpIntegration:Enabled"] = "true",
            [$"ErpIntegration:Tenants:{s.Tenant:D}"] = "local",
            ["ErpIntegration:ReaderClientIds:0"] = "c03-di-reader",
            ["ErpIntegration:DaprAppToken"] = new string('t', 48),
            ["ErpIntegration:DaprApiToken"] = new string('a', 48)
        }).Build();
        var oidc = new CrmOidcOptions
        {
            Enabled = true, Issuer = "https://identity.cp6.test",
            Organizations = [new() { TenantId = s.Tenant, Slug = "c03-sql-di", Region = "local", CrmEnabled = true }],
            ServiceClients = [new() { ClientId = "c03-di-reader", TenantId = s.Tenant, Enabled = true, AllowedScopes = ["cp6.services"] }]
        };
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddSingleton<TimeProvider>(s.Clock);
        // Anonymous sidecar ingress does not establish an authenticated request tenant.
        services.AddScoped<ITenantContext, TenantContext>();
        services.AddDbContext<CP6Context>(options => options.UseSqlServer(connection));
        services.AddScoped<IFxRateService, FxRateService>();
        services.AddSingleton<IPowerEggWorkflowService>(s.ExternalCalls);
        services.AddSingleton<IWmsBridgeHook>(s.ExternalCalls);
        services.AddSingleton<IMesBridgeHook>(s.ExternalCalls);
        services.AddErpIntegration(configuration, oidc);
        // Resolve the actual production registration; no test replacement of its handler factory.
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        await using var scope = provider.CreateAsyncScope();
        var requestTenant = scope.ServiceProvider.GetRequiredService<ITenantContext>();
        var defaultDb = scope.ServiceProvider.GetRequiredService<CP6Context>();
        Assert.Equal(TenantContext.DefaultTenant, requestTenant.CurrentTenantId);
        Assert.Equal(TenantContext.DefaultTenant, defaultDb.CurrentTenantId);
        var handler = scope.ServiceProvider.GetRequiredService<ErpRequestHandler>();
        await s.ConsumeAsync(s.Envelope(s.PartnerRequest()), handler);
        var request = await s.NewAcceptedQuotationAsync("USD");
        await using (var db = s.Db())
            await new FxRateService(db).CreateAsync(new()
            {
                CurrencyCd = "USD", RateDate = s.Clock.GetUtcNow().UtcDateTime.Date, Rate = 150m
            }, "erp-staff");
        var defaultFx = await scope.ServiceProvider.GetRequiredService<IFxRateService>()
            .ResolveForCustomerAsync(s.PartnerKey, s.Clock.GetUtcNow().UtcDateTime.Date);
        Assert.Equal("JPY", defaultFx.CurrencyCd);
        Assert.Equal(1m, defaultFx.Rate);
        var envelope = s.Envelope(request);
        await s.ConsumeAsync(envelope, handler);
        var journal = await s.JournalAsync(request.OpportunityId);
        Assert.True(journal.Succeeded, journal.ResultDataJson);
        Assert.Equal(TenantContext.DefaultTenant, requestTenant.CurrentTenantId);
        Assert.Equal(TenantContext.DefaultTenant, defaultDb.CurrentTenantId);
        await using var verify = s.Db();
        var order = await verify.Orders.SingleAsync();
        Assert.Equal(s.Tenant, order.TenantId);
        Assert.Equal(request.RequestId, order.CrmRequestId);
        Assert.Equal("USD", order.CurrencyCd);
        Assert.Equal(150m, order.FxRate);
        Assert.Equal(300m, (await verify.OrderDetails.SingleAsync()).Amount);
        var result = Assert.Single(await s.ResultsAsync(ErpEventContracts.OrderCreated));
        Assert.Equal("USD", result.GetProperty("data").GetProperty("currency").GetString());
        Assert.Equal(300m, result.GetProperty("data").GetProperty("bookedAmount").GetDecimal());
        Assert.Equal(order.WebOrderNo, result.GetProperty("data").GetProperty("orderKey").GetString());
        Assert.Equal(envelope.MessageId, result.GetProperty("causationid").GetString());
        Assert.Empty(await defaultDb.Orders.ToArrayAsync());
        Assert.Equal(0, s.ExternalCalls.Count);
    }
}
