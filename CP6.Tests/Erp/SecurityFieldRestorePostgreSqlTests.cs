using CP6.Core.Services.Platform;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;
namespace CP6.Tests.Erp;
public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private static async Task<SecurityFieldOperationV1> SecurityFieldDispose(Harness h,(Guid Log,Guid Policy,Harness Approver) setup)
    {
        var plan=await SecurityFieldApproved(h,setup);return await PrivacyDispositionOwner(h).ExecuteSecurityDispositionAsync(plan.PlanId,new(Guid.NewGuid(),plan.Version,plan.ScopeDigest,Guid.NewGuid()),PrivacySession(h));
    }
    [CurrentPermissionPostgresFact]
    public async Task SecurityFieldDisposition_restore_native_backup_reapplies_exact_fields_and_fences_absent_event_without_erasing_other_content()
    {
        await using var h=await Seed();var setup=await SecurityFieldSetup(h);await RestorePermissions(h);
        await using var candidate=await PrivacyRestoreCandidateFixture.Create(h.Db.Database.GetConnectionString()!);var disposed=await SecurityFieldDispose(h,setup);
        var late=await SecurityFieldLog(h,disposed.Execution!.SubjectId);await SecurityFieldDispose(h,(late,setup.Policy,setup.Approver));
        var options=RestoreRegistration(candidate);var runtime=await RestoreRuntime(h);var o=PrivacyRestorer(h,options,runtime);var s=PrivacySession(h);
        var preview=await o.RestorePreviewAsync(candidate.Id,s);Assert.True(preview.Scope.SecurityFieldTombstoneCount>=2);
        var plan=await ApprovedRestore(h,setup.Approver,options,runtime,candidate.Id);var result=await o.ExecuteRestoreAsync(plan.PlanId,RestoreCommand(plan),s);
        var unit=Assert.Single(result.Units!,x=>x.LogId==setup.Log);Assert.Equal("Main.SecurityLog.Fields.v1",unit.Owner);Assert.Equal("REAPPLIED",unit.Outcome);Assert.Equal(2,unit.ClearedValueCount);Assert.Equal("ABSENT_FENCED",Assert.Single(result.Units!,x=>x.LogId==late).Outcome);
        await using var target=PrivacyRestoreNativeV1.Context(candidate.Connection,h.Tenant);var row=await target.Sys_SecurityLogs.AsNoTracking().SingleAsync(x=>x.Id==setup.Log);Assert.Null(row.UserName);Assert.Null(row.ClientIp);Assert.Equal("unclassified-device-remains",row.UserAgent);Assert.Equal("unclassified-reason-remains",row.Reason);Assert.Contains("unclassified-context-remains",row.ContextJson!);
        Assert.Equal(disposed.Execution.SubjectId,row.UserId);Assert.Equal(unit.AfterVersion,await target.Database.SqlQuery<Guid>($"SELECT privacy_revision AS \"Value\" FROM public.\"Sys_SecurityLogs\" WHERE \"Id\"={setup.Log}").SingleAsync());
        await using(var admitted=await PrivacyRestoreAdmissionV1.AcquireAsync(target,new(){ControlConnectionString=h.Db.Database.GetConnectionString()!},default))Assert.NotNull(admitted);
        var error=await Assert.ThrowsAsync<Npgsql.PostgresException>(()=>target.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO public."Sys_SecurityLogs"("Id","TenantId","UserId","UserName","EventType","CreatedAt","CreateDate")
            VALUES({late},{h.Tenant},{row.UserId},'must-not-reappear',1,{row.CreatedAt.ToString("yyyy-MM-ddTHH:mm:ss.ffffff",System.Globalization.CultureInfo.InvariantCulture)}::timestamp,LOCALTIMESTAMP)
            """));Assert.Contains("DISPOSED_IDENTITY",error.MessageText);
    }
}
