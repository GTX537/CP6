using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CP6.Core.Services.Wf;
using CP6.Core.Services.Wf.Executors;
using CP6.Entity.DomainModels.Wf;
using Xunit;
using static CP6.Tests.WfsInfraTestHarness;

namespace CP6.Tests;

public class ConnectorResolutionTests
{
    private sealed class FakeAppConnector : IWfConnector
    {
        public string Name { get; }
        public string DisplayName => Name;
        public FakeAppConnector(string name) => Name = name;
        public Task<ServiceTaskResult> CallAsync(string p, string? j, ServiceTaskContext c)
            => Task.FromResult(ServiceTaskResult.Ok(new Dictionary<string, object?> { ["src"] = "app" }));
    }

    [Fact]
    public async Task Resolve_TenantRowPreferred_OverAppRegistration()
    {
        using var conn = NewSqliteWithSchema();
        using (var db = Ctx(conn))
        {
            db.Wf_Connectors.Add(new Wf_Connector { Name = "erpEcho", DisplayName = "租户 Echo",
                BaseUrl = "https://tenant", TimeoutSec = 120, Enabled = true });
            await db.SaveChangesAsync();
        }
        using var db2 = Ctx(conn);
        // resolver：先查租户表（命中 erpEcho）→ 不回落 app 的 FakeAppConnector("erpEcho")
        var resolver = new TenantConnectorResolver(db2, new IWfConnector[] { new FakeAppConnector("erpEcho") },
            Microsoft.AspNetCore.DataProtection.DataProtectionProvider.Create("CP6.Tests"), StubClientFactory());
        var c = await resolver.ResolveAsync("erpEcho", CancellationToken.None);
        Assert.IsType<DbWfConnector>(c);   // 租户行优先
    }

    [Fact]
    public async Task Resolve_FallsBackToApp_WhenNoTenantRow()
    {
        using var conn = NewSqliteWithSchema();
        using var db = Ctx(conn);
        var resolver = new TenantConnectorResolver(db, new IWfConnector[] { new FakeAppConnector("erpEcho") },
            Microsoft.AspNetCore.DataProtection.DataProtectionProvider.Create("CP6.Tests"), StubClientFactory());
        var c = await resolver.ResolveAsync("erpEcho", CancellationToken.None);
        Assert.IsType<FakeAppConnector>(c);   // 无租户行 → app 兜底
    }

    [Fact]
    public async Task Resolve_DisabledTenantRow_FallsBackToApp()
    {
        using var conn = NewSqliteWithSchema();
        using (var db = Ctx(conn))
        {
            db.Wf_Connectors.Add(new Wf_Connector { Name = "erpEcho", DisplayName = "禁用",
                BaseUrl = "https://x", TimeoutSec = 30, Enabled = false });
            await db.SaveChangesAsync();
        }
        using var db2 = Ctx(conn);
        var resolver = new TenantConnectorResolver(db2, new IWfConnector[] { new FakeAppConnector("erpEcho") },
            Microsoft.AspNetCore.DataProtection.DataProtectionProvider.Create("CP6.Tests"), StubClientFactory());
        var c = await resolver.ResolveAsync("erpEcho", CancellationToken.None);
        Assert.IsType<FakeAppConnector>(c);   // 禁用行不解析 → app 兜底
    }

    [Fact]
    public async Task Resolve_Unknown_ReturnsNull()
    {
        using var conn = NewSqliteWithSchema();
        using var db = Ctx(conn);
        var resolver = new TenantConnectorResolver(db, Array.Empty<IWfConnector>(),
            Microsoft.AspNetCore.DataProtection.DataProtectionProvider.Create("CP6.Tests"), StubClientFactory());
        Assert.Null(await resolver.ResolveAsync("ghost", CancellationToken.None));
    }

    [Fact]
    public async Task Catalog_MergesBothSources_TenantRowDedups()
    {
        using var conn = NewSqliteWithSchema();
        using (var db = Ctx(conn))
        {
            db.Wf_Connectors.Add(new Wf_Connector { Name = "erpEcho", DisplayName = "租户 Echo", BaseUrl = "https://t", TimeoutSec = 120, Enabled = true });
            db.Wf_Connectors.Add(new Wf_Connector { Name = "erpProd", DisplayName = "ERP 生产", BaseUrl = "https://p", TimeoutSec = 120, Enabled = true });
            await db.SaveChangesAsync();
        }
        using var db2 = Ctx(conn);
        var resolver = new TenantConnectorResolver(db2, new IWfConnector[] { new FakeAppConnector("erpEcho"), new FakeAppConnector("appOnly") },
            Microsoft.AspNetCore.DataProtection.DataProtectionProvider.Create("CP6.Tests"), StubClientFactory());
        var names = (await resolver.ListCatalogAsync(CancellationToken.None)).Select(x => x.Name).OrderBy(x => x).ToList();
        Assert.Equal(new[] { "appOnly", "erpEcho", "erpProd" }, names);   // erpEcho 去重（租户行优先），app-only 项保留
    }

    private static System.Net.Http.IHttpClientFactory StubClientFactory()
        => new StubHttpClientFactory();

    private sealed class StubHttpClientFactory : System.Net.Http.IHttpClientFactory
    {
        public System.Net.Http.HttpClient CreateClient(string name) => new();
    }
}
