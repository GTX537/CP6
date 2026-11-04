using System.Net;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;
namespace CP6.Tests.Erp;
public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    [CurrentPermissionPostgresFact]
    public async Task PrivacyExport_legacy_direct_subject_download_cannot_bypass_purpose_and_current_artifact_authority()
    {
        await using var h=await Seed();
        await h.Db.Sys_Users.Where(x=>x.Id==h.User).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.IsPlatformAdmin,true));
        await using var host=await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!,SetHostSettings());
        var cookies=await Login(host,h);
        using var response=await host.Send(OperationsRequest(cookies,HttpMethod.Get,$"/api/platform/gdpr/export/subject/{h.User}"));
        Assert.Equal(HttpStatusCode.Gone,response.StatusCode);
        Assert.Contains("PRIVACY_USE_DURABLE_EXPORT",await response.Content.ReadAsStringAsync());
        Assert.True(response.Headers.CacheControl!.NoStore);
    }
}
