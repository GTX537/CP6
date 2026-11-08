using System.Net;
using System.Text.Json;
using CP6.Core.Services.Platform;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;
namespace CP6.Tests.Erp;
public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private static Dictionary<string,string> PrivacyExecutionEnvironment(Guid tenant)
    {
        var environment=IamUserAssignmentsPostgreSqlTests.IdentityEnvironment(tenant);
        foreach(var pair in SetHostSettings())environment[pair.Key]=pair.Value;
        return environment;
    }
    private static async Task<(Guid Plan,Guid Version,string Digest)> PrivacyApprovedPlan(Harness h,Guid target,Guid policy,Harness approver)
    {
        var o=PrivacyDispositionOwner(h);var original=await o.RequestAsync(await PrivacyDispositionCommand(h,target,policy),PrivacySession(h));
        await PrivacyDispositionOwner(approver).DecideAsync(original.PlanId!.Value,new(Guid.NewGuid(),original.Version!.Value,"APPROVE",Guid.NewGuid()),PrivacySession(approver));
        var current=await o.PlanAsync(original.PlanId!.Value,PrivacySession(h));return(current.PlanId,current.Version,current.ScopeDigest);
    }
    [CurrentPermissionPostgresFact]
    public async Task PrivacyExecution_actual_HTTP_applies_only_approved_identity_profile_and_read_recovers_after_execute_revocation()
    {
        await using var h=await Seed();var setup=await PrivacyDispositionSetup(h);await EngineeringAction(h,"privacy-disposition","execute");
        var plan=await PrivacyApprovedPlan(h,setup.Target,setup.Policy,setup.Approver);
        await using var host=await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!,PrivacyExecutionEnvironment(h.Tenant));var cookies=await Login(host,h);
        var operation=Guid.NewGuid();var command=new{operationId=operation,expectedVersion=plan.Version,expectedScopeDigest=plan.Digest,confirmationRef=Guid.NewGuid()};
        using var response=await host.Send(OperationsRequest(cookies,HttpMethod.Post,$"/api/platform/privacy-dispositions-v1/plans/{plan.Plan}/execute",command));
        var content=await response.Content.ReadAsStringAsync();Assert.True(response.StatusCode==HttpStatusCode.OK,content);Assert.True(response.Headers.CacheControl!.NoStore);
        Assert.Contains("PARTIAL",content);Assert.DoesNotContain("simulated@example.invalid",content);Assert.DoesNotContain("original profile remains",content);
        var user=await h.Db.Sys_Users.AsNoTracking().SingleAsync(x=>x.Id==setup.Target);Assert.Null(user.Email);Assert.Null(user.NickName);Assert.False(user.Enable);Assert.StartsWith("anon_",user.UserName);
        await h.Db.Sys_RoleActions.Where(x=>x.RoleId==72&&x.ActionCode=="execute").ExecuteDeleteAsync();
        using var original=await host.Send(OperationsRequest(cookies,HttpMethod.Get,$"/api/platform/privacy-dispositions-v1/operations/{operation}"));
        Assert.Equal(HttpStatusCode.OK,original.StatusCode);Assert.Equal(JsonDocument.Parse(content).RootElement.GetProperty("execution").GetRawText(),JsonDocument.Parse(await original.Content.ReadAsStringAsync()).RootElement.GetProperty("execution").GetRawText());
    }
}
