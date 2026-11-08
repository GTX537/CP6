using System.Net;
using System.Text.Json;
using CP6.Core.Services.Platform;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;
namespace CP6.Tests.Erp;
public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    [CurrentPermissionPostgresFact]
    public async Task TenantPrivacyExport_direct_tenant_HTTP_stream_is_retired_and_new_read_is_independent_of_subject_export()
    {
        await using var h=await Seed();await PrivacyAuthority(h);
        await using var host=await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!,PrivacyExecutionEnvironment(h.Tenant));var cookies=await Login(host,h);
        using var legacy=await host.Send(BoundRequest(cookies,$"/api/platform/gdpr/export/tenant/{h.Tenant}"));Assert.Equal(HttpStatusCode.Gone,legacy.StatusCode);Assert.True(legacy.Headers.CacheControl!.NoStore);
        const string path="/api/platform/privacy-tenant-exports-v1/context";
        using var denied=await host.Send(BoundRequest(cookies,path));Assert.Equal(HttpStatusCode.Forbidden,denied.StatusCode);
        await EngineeringAction(h,"privacy-export","tenant-read");using var allowed=await host.Send(BoundRequest(cookies,path));Assert.True(allowed.StatusCode==HttpStatusCode.OK,await allowed.Content.ReadAsStringAsync());
        using var value=JsonDocument.Parse(await allowed.Content.ReadAsStringAsync());Assert.Equal("Main.Privacy.TenantExport.v1",value.RootElement.GetProperty("owner").GetString());
        var actions=value.RootElement.GetProperty("actions").EnumerateArray().Select(x=>x.GetString()).ToArray();Assert.Contains("tenant-read",actions);Assert.DoesNotContain("tenant-export",actions);Assert.DoesNotContain("tenant-download",actions);
    }
}
