using CP6.Core.Services.Erp;
using CP6.Core.Services.Oa;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests.Erp;
public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    [CurrentPermissionPostgresFact]
    public async Task LocationRuleWithdrawal_current_read_scope_and_original_commit_recovery_do_not_require_content_write()
    {
        var fault=new CommitFault();await using var h=await Seed(fault);var f=await RuleDecisionSetup(h);var id=f.Source.Candidate.Reference.Id;var owner=RuleReviews(h);
        var c=await owner.WithdrawalContextAsync(id,f.Submission);Assert.False(c.CanWithdraw);
        var input=new ApprovalUserWithdrawV2(Guid.NewGuid(),"Withdraw with independent action and current source read");
        Assert.Equal(403,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>owner.WithdrawAsync(id,f.Submission,"\""+c.ControlRowVersion+"\"",input))).Status);
        await EngineeringAction(h,"wms-location-rule","review-withdraw");
        h.Db.Sys_RoleFieldPerms.Add(new(){RoleId=72,ResourceKey="wms-location-rule",FieldName="Capacity",Access=3});await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();
        Assert.Equal("FIELD_READ_FORBIDDEN",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>owner.WithdrawalContextAsync(id,f.Submission))).Message);
        await h.Db.Sys_RoleFieldPerms.Where(x=>x.ResourceKey=="wms-location-rule"&&x.FieldName=="Capacity").ExecuteUpdateAsync(s=>s.SetProperty(p=>p.Access,2));
        c=await owner.WithdrawalContextAsync(id,f.Submission);Assert.True(c.CanWithdraw);
        await using var foreign=await Seed();await RuleDecisionSetup(foreign);
        Assert.Equal(404,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>RuleReviews(foreign).WithdrawalContextAsync(id,f.Submission))).Status);
        Assert.Equal(404,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>owner.WithdrawalContextAsync(id,Guid.NewGuid()))).Status);
        var unsubmitted=Guid.NewGuid();Assert.Equal("UNKNOWN",(await owner.WithdrawalOperationAsync(unsubmitted,false)).State);
        Assert.Equal("CLOSED",(await owner.WithdrawalOperationAsync(unsubmitted,true)).State);
        Assert.Equal("ORIGINAL_OPERATION_CLOSED",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>owner.WithdrawAsync(id,f.Submission,"\""+c.ControlRowVersion+"\"",input with{CommandId=unsubmitted}))).Message);
        fault.Armed=true;
        Assert.Equal("OPERATIONS_OUTCOME_UNKNOWN",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>owner.WithdrawAsync(id,f.Submission,"\""+c.ControlRowVersion+"\"",input))).Message);
        await h.Db.Sys_RoleActions.Where(x=>x.ActionCode=="review-withdraw").ExecuteDeleteAsync();
        var recovered=await owner.WithdrawalOperationAsync(input.CommandId,false);Assert.Equal("COMMITTED",recovered.State);
        Assert.Equal(recovered.Result,await owner.WithdrawAsync(id,f.Submission,"\""+c.ControlRowVersion+"\"",input));
        Assert.Equal(409,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>owner.WithdrawAsync(id,f.Submission,"\""+c.ControlRowVersion+"\"",input with{Reason="Different original input"}))).Status);
        await h.Db.Sys_RoleFieldPerms.Where(x=>x.ResourceKey=="wms-location-rule"&&x.FieldName=="Capacity").ExecuteUpdateAsync(s=>s.SetProperty(p=>p.Access,3));
        Assert.Equal("FIELD_READ_FORBIDDEN",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>owner.WithdrawalOperationAsync(input.CommandId,false))).Message);
        Assert.Equal(1,await h.Db.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM cp6_location_rule_review_control_v1.withdrawals WHERE tenant_id={h.Tenant}").SingleAsync());
    }
}
