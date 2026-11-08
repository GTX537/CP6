using System.Net;
using CP6.Tests.Platform;
using CP6.Entity.DomainModels.Sys;
using CP6.Core.Services.Platform;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
namespace CP6.Tests.Erp;
public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private static readonly string[] SecurityFieldActions=["security-read","security-policy","security-prepare","security-approve","security-revoke","security-execute"];
    private static async Task<(Guid Log,Guid Policy,Harness Approver)> SecurityFieldSetup(Harness h)
    {
        var setup=await PrivacyDispositionSetup(h);var menu=await h.Db.Sys_Menus.SingleAsync(x=>x.MenuKey=="privacy-disposition");
        foreach(var action in SecurityFieldActions){await EngineeringAction(h,"privacy-disposition",action);h.Db.Sys_RoleActions.Add(new(){RoleId=73,MenuId=menu.MenuId,ActionCode=action});}
        await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();var log=await SecurityFieldLog(h,setup.Target);var policy=Guid.NewGuid();
        await PrivacyDispositionOwner(h).ConfigureSecurityPolicyAsync(new(Guid.NewGuid(),policy,h.Tenant,null,Guid.NewGuid(),true,PrivacyReviewTime(-3600)),PrivacySession(h));return(log,policy,setup.Approver);
    }
    private static async Task<SecurityFieldPlanV1> SecurityFieldApproved(Harness h,(Guid Log,Guid Policy,Harness Approver) setup)
    {
        var o=PrivacyDispositionOwner(h);var s=PrivacySession(h);var preview=await o.SecurityPreviewAsync(new(setup.Log,setup.Policy),s);Assert.Empty(preview.Blockers);
        var requested=await o.RequestSecurityDispositionAsync(new(Guid.NewGuid(),Guid.NewGuid(),new(setup.Log,setup.Policy),preview.ScopeDigest),s);var id=requested.PlanId!.Value;
        var decision=new SecurityFieldDecisionV1(Guid.NewGuid(),requested.Version!.Value,"APPROVE",Guid.NewGuid());
        Assert.Equal(403,(await Assert.ThrowsAsync<PrivacyDispositionExceptionV1>(()=>o.DecideSecurityDispositionAsync(id,decision,s))).Status);
        await PrivacyDispositionOwner(setup.Approver).DecideSecurityDispositionAsync(id,decision,PrivacySession(setup.Approver));return await o.SecurityPlanAsync(id,s);
    }
    [CurrentPermissionPostgresFact]
    public async Task SecurityFieldDisposition_actual_clear_preserves_event_identity_and_unknown_content_and_original_survives_execution_revocation()
    {
        await using var h=await Seed();var setup=await SecurityFieldSetup(h);var o=PrivacyDispositionOwner(h);var s=PrivacySession(h);var before=await h.Db.Sys_SecurityLogs.AsNoTracking().SingleAsync(x=>x.Id==setup.Log);
        var preview=await o.SecurityPreviewAsync(new(setup.Log,setup.Policy),s);Assert.DoesNotContain("simulated-sensitive-name",JsonSerializer.Serialize(preview));Assert.DoesNotContain("198.51.100.7",JsonSerializer.Serialize(preview));Assert.Equal(DateTimeKind.Unspecified,preview.Scope.OriginalCreatedAt.Kind);
        var plan=await SecurityFieldApproved(h,setup);var input=new SecurityFieldExecuteV1(Guid.NewGuid(),plan.Version,plan.ScopeDigest,Guid.NewGuid());var audit=await h.Db.Sys_FieldAuditLogs.CountAsync();
        var result=await o.ExecuteSecurityDispositionAsync(plan.PlanId,input,s);Assert.Equal("APPLIED",result.Execution!.OwnerOutcome);Assert.Equal("PARTIAL",result.Execution.AggregateState);Assert.Equal(2,result.Execution.ClearedValueCount);
        var row=await h.Db.Sys_SecurityLogs.AsNoTracking().SingleAsync(x=>x.Id==setup.Log);Assert.Null(row.UserName);Assert.Null(row.ClientIp);Assert.Equal(before.UserId,row.UserId);Assert.Equal(before.TenantId,row.TenantId);Assert.Equal(before.EventType,row.EventType);Assert.Equal(before.CreatedAt,row.CreatedAt);
        Assert.Equal(before.Reason,row.Reason);Assert.Equal(before.UserAgent,row.UserAgent);Assert.Equal(before.ContextJson,row.ContextJson);Assert.Equal(before.Creator,row.Creator);Assert.Equal(before.Modifier,row.Modifier);Assert.Equal(before.CreateDate,row.CreateDate);Assert.Equal(before.ModifyDate,row.ModifyDate);Assert.Equal(audit,await h.Db.Sys_FieldAuditLogs.CountAsync());
        Assert.Equal(result.Execution.AfterVersion,await h.Db.Database.SqlQuery<Guid>($"SELECT privacy_revision AS \"Value\" FROM public.\"Sys_SecurityLogs\" WHERE \"Id\"={setup.Log}").SingleAsync());Assert.NotEqual(result.Execution.BeforeVersion,result.Execution.AfterVersion);
        var error=await Assert.ThrowsAsync<Npgsql.PostgresException>(()=>h.Db.Sys_SecurityLogs.Where(x=>x.Id==setup.Log).ExecuteUpdateAsync(x=>x.SetProperty(r=>r.UserName,"must-not-reappear")));Assert.Contains("DISPOSED_IDENTITY",error.MessageText);
        await h.Db.Sys_RoleActions.Where(x=>x.RoleId==72&&x.ActionCode=="security-execute").ExecuteDeleteAsync();
        Assert.Equal(JsonSerializer.Serialize(result),JsonSerializer.Serialize(await o.ExecuteSecurityDispositionAsync(plan.PlanId,input,s)));Assert.Equal(JsonSerializer.Serialize(result),JsonSerializer.Serialize(await o.SecurityOperationAsync(input.OperationId,false,s)));
        Assert.Equal(409,(await Assert.ThrowsAsync<PrivacyDispositionExceptionV1>(()=>o.ExecuteSecurityDispositionAsync(plan.PlanId,input with{ConfirmationRef=Guid.NewGuid()},s))).Status);
        Assert.DoesNotContain("simulated-sensitive-name",JsonSerializer.Serialize(result));Assert.DoesNotContain("unclassified-context-remains",JsonSerializer.Serialize(result));
    }
    [CurrentPermissionPostgresFact]
    public async Task SecurityFieldDisposition_native_ABA_and_explicit_UTC_policy_CAS_are_authoritative_without_guessing_old_event_time()
    {
        await using var h=await Seed();var setup=await SecurityFieldSetup(h);var o=PrivacyDispositionOwner(h);var s=PrivacySession(h);var target=new SecurityFieldTargetV1(setup.Log,setup.Policy);var preview=await o.SecurityPreviewAsync(target,s);
        await h.Db.Sys_SecurityLogs.Where(x=>x.Id==setup.Log).ExecuteUpdateAsync(x=>x.SetProperty(r=>r.UserName,"temporary-name"));await h.Db.Sys_SecurityLogs.Where(x=>x.Id==setup.Log).ExecuteUpdateAsync(x=>x.SetProperty(r=>r.UserName,"simulated-sensitive-name"));
        Assert.Equal(412,(await Assert.ThrowsAsync<PrivacyDispositionExceptionV1>(()=>o.RequestSecurityDispositionAsync(new(Guid.NewGuid(),Guid.NewGuid(),target,preview.ScopeDigest),s))).Status);
        var policy=(await o.SecurityPolicyAsync(setup.Policy,s))!;Assert.Equal(TimeSpan.Zero,policy.NotBeforeUtc.Offset);
        Assert.Equal(412,(await Assert.ThrowsAsync<PrivacyDispositionExceptionV1>(()=>o.ConfigureSecurityPolicyAsync(new(Guid.NewGuid(),setup.Policy,h.Tenant,null,Guid.NewGuid(),true,PrivacyReviewTime()),s))).Status);
        var blocked=await o.ConfigureSecurityPolicyAsync(new(Guid.NewGuid(),setup.Policy,h.Tenant,policy.Version,Guid.NewGuid(),true,PrivacyReviewTime()),s);preview=await o.SecurityPreviewAsync(target,s);Assert.Contains("POLICY_NOT_BEFORE_NOT_REACHED",preview.Blockers);
        Assert.Equal(409,(await Assert.ThrowsAsync<PrivacyDispositionExceptionV1>(()=>o.RequestSecurityDispositionAsync(new(Guid.NewGuid(),Guid.NewGuid(),target,preview.ScopeDigest),s))).Status);
        var at=PrivacyReviewTime(-60).ToOffset(TimeSpan.FromHours(2));await o.ConfigureSecurityPolicyAsync(new(Guid.NewGuid(),setup.Policy,h.Tenant,blocked.Version,Guid.NewGuid(),true,at),s);
        Assert.Equal(at.ToUniversalTime(),(await o.SecurityPolicyAsync(setup.Policy,s))!.NotBeforeUtc);
        await h.Db.Sys_SecurityLogs.Where(x=>x.Id==setup.Log).ExecuteUpdateAsync(x=>x.SetProperty(r=>r.CreatedAt,new DateTime(2099,1,1,0,0,0,DateTimeKind.Unspecified)));
        Assert.Empty((await o.SecurityPreviewAsync(target,s)).Blockers);
        var anonymous=await SecurityFieldLog(h,null);Assert.Equal("PRIVACY_SECURITY_FIELDS_UNATTRIBUTABLE_EVENT",(await Assert.ThrowsAsync<PrivacyDispositionExceptionV1>(()=>o.SecurityPreviewAsync(new(anonymous,setup.Policy),s))).Message);
    }
    private static async Task<Guid> SecurityFieldLog(Harness h,Guid? subject)
    {
        var row=new Sys_SecurityLog{UserId=subject,UserName="simulated-sensitive-name",ClientIp="198.51.100.7",UserAgent="unclassified-device-remains",Reason="unclassified-reason-remains",ContextJson="{\"otherContent\":\"unclassified-context-remains\"}",EventType=1,CreatedAt=new DateTime(2026,10,31,12,0,0,DateTimeKind.Unspecified)};
        h.Db.Sys_SecurityLogs.Add(row);await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();return row.Id;
    }
    [CurrentPermissionPostgresFact]
    public async Task SecurityFieldDisposition_actual_HTTP_read_is_independent_and_does_not_grant_execution()
    {
        await using var h=await Seed();await PrivacyDispositionSetup(h);
        await using var host=await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!,PrivacyExecutionEnvironment(h.Tenant));var cookies=await Login(host,h);
        const string path="/api/platform/privacy-dispositions-v1/security-fields/context";
        using var denied=await host.Send(BoundRequest(cookies,path));Assert.Equal(HttpStatusCode.Forbidden,denied.StatusCode);
        await EngineeringAction(h,"privacy-disposition","security-read");
        using var allowed=await host.Send(BoundRequest(cookies,path));Assert.True(allowed.StatusCode==HttpStatusCode.OK,await allowed.Content.ReadAsStringAsync());
        using var value=System.Text.Json.JsonDocument.Parse(await allowed.Content.ReadAsStringAsync());Assert.Equal("Main.SecurityLog.Fields.v1",value.RootElement.GetProperty("owner").GetString());
        var actions=value.RootElement.GetProperty("actions").EnumerateArray().Select(x=>x.GetString()).ToArray();Assert.Contains("security-read",actions);Assert.DoesNotContain("security-execute",actions);
    }
    [CurrentPermissionPostgresFact]
    public async Task SecurityFieldDisposition_native_unowned_clear_is_rejected_without_rewriting_unclassified_content()
    {
        await using var h=await Seed();await PrivacyAuthority(h);var id=await SecurityFieldLog(h,h.User);
        var error=await Assert.ThrowsAsync<Npgsql.PostgresException>(()=>h.Db.Sys_SecurityLogs.Where(x=>x.Id==id).ExecuteUpdateAsync(x=>x.SetProperty(r=>r.UserName,(string?)null).SetProperty(r=>r.ClientIp,(string?)null)));
        Assert.Equal("23514",error.SqlState);Assert.Contains("PRIVACY_SECURITY_FIELDS_DISPOSITION_REQUIRED",error.MessageText);
        var row=await h.Db.Sys_SecurityLogs.AsNoTracking().SingleAsync(x=>x.Id==id);Assert.Equal("simulated-sensitive-name",row.UserName);Assert.Equal("198.51.100.7",row.ClientIp);Assert.Equal("unclassified-device-remains",row.UserAgent);
    }
}
