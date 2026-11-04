using System.Net;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;
namespace CP6.Tests.Erp;
public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    [CurrentPermissionPostgresFact]
    public async Task PrivacyDisposition_legacy_confirmation_cannot_bypass_exact_plan_approval_and_hold()
    {
        await using var h=await Seed();await PrivacyAuthority(h);
        await using var host=await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!,SetHostSettings());var cookies=await Login(host,h);
        foreach(var confirm in new[]{"false","true"})
        foreach(var path in new[]{$"/api/platform/gdpr/erase/subject/{h.User}?confirm={confirm}",$"/api/platform/gdpr/erase/tenant/{h.Tenant}?confirm={confirm}"})
        {
            using var response=await host.Send(OperationsRequest(cookies,HttpMethod.Delete,path));
            Assert.Equal(HttpStatusCode.Gone,response.StatusCode);Assert.Contains("PRIVACY_APPROVED_PLAN_REQUIRED",await response.Content.ReadAsStringAsync());
        }
        Assert.True(await h.Db.Sys_Users.Where(x=>x.Id==h.User).Select(x=>x.Enable).SingleAsync());
    }
}
