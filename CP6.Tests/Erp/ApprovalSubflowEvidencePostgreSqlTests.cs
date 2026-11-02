using System.Text.Json;
using CP6.Core.Services.Oa;
using CP6.Core.Services.Sys;
using CP6.Core.Services.Wf;
using CP6.Entity.DomainModels.Wf;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests.Erp;
public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private static string SubflowPruneSchema(int mode, bool sufficient = false)
    {
        var nodes = new List<object>
        {
            new { id="s", type="start" }, new { id="p", type="parallelSplit", onBranchReject="prune" },
            new { id="call", type="subFlow", subFlowKey="child" },
            new { id="survivor", type="approval", approverStrategy="Role", approverRoleId=73, countersign=sufficient?"all":"any" },
            new { id="j", type="parallelJoin" }, new { id="e", type="end" }
        };
        var edges = new List<object> { new {from="s",to="p"}, new {from="p",to="call"}, new {from="p",to="survivor"}, new {from="survivor",to="j"}, new {from="j",to="e"} };
        if(mode==0) edges.Add(new {from="call",to="j"});
        else
        {
            nodes.Add(new {id="a",type="approval",approverStrategy="Role",approverRoleId=73});
            if(mode==1) { edges.Add(new {from="call",to="a"}); edges.Add(new {from="a",to="j"}); }
            else
            {
                nodes.Add(new {id="inner",type="parallelSplit",onBranchReject="prune"});
                nodes.Add(new {id="b",type="approval",approverStrategy="Role",approverRoleId=73});
                nodes.Add(new {id="innerJoin",type="parallelJoin"});
                edges.AddRange([new {from="call",to="inner"},new {from="inner",to="a"},new {from="inner",to="b"},new {from="a",to="innerJoin"},new {from="b",to="innerJoin"},new {from="innerJoin",to="j"}]);
            }
        }
        return JsonSerializer.Serialize(new {start="s",nodes,edges});
    }

    [TenantManagementPostgresTheory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)]
    public async Task App_subflow_pruned_child_or_parent_votes_do_not_supply_missing_human_approvals(int mode)
    {
        await using var h=await Seed();var f=await SubflowSetup(h,SubflowPruneSchema(mode));var start=await Starter(h).RunAsync(h.Tenant);
        var child=await h.Db.Wf_FlowInstances.AsNoTracking().SingleAsync(x=>x.ParentInstanceId==start.InstanceId);
        async Task<ApprovalTaskDecisionResultV2> Act(Guid instance,string node,Guid actorId,string decision="approve")
        {
            var task=await h.Db.Wf_FlowTasks.AsNoTracking().SingleAsync(x=>x.InstanceId==instance && x.NodeId==node && x.AssigneeId==actorId && x.Status==0);
            var actor=DecisionActor(h,actorId);return await Vote(actor,await SubflowInput(actor,task.Id,decision));
        }
        await Act(child.Id,"a",f.First);
        await Act(child.Id,"a",f.Second,mode==0?"reject":"approve");
        if(mode>0) await Act(start.InstanceId!.Value,"a",f.Second,"reject");
        if(mode==2) await Act(start.InstanceId!.Value,"b",f.Second,"reject");
        var count=await h.Db.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM cp6_approval_decision_v2.votes WHERE tenant_id={h.Tenant}").SingleAsync();
        Assert.Equal("DECISION_EVIDENCE_INCOMPLETE",(await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>Act(start.InstanceId!.Value,"survivor",f.Second))).Message);
        Assert.Equal(FlowInstanceStatus.Running,(await h.Db.Wf_FlowInstances.AsNoTracking().SingleAsync(x=>x.Id==start.InstanceId)).Status);
        Assert.Equal(count,await h.Db.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM cp6_approval_decision_v2.votes WHERE tenant_id={h.Tenant}").SingleAsync());
        Assert.Equal(0,await h.Db.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM cp6_approval_decision_v2.decisions WHERE tenant_id={h.Tenant}").SingleAsync());
    }

    [CurrentPermissionPostgresFact]
    public async Task App_subflow_prune_keeps_audit_votes_but_only_successful_duties_satisfy_root_policy()
    {
        await using var h=await Seed();var f=await SubflowSetup(h,SubflowPruneSchema(0,sufficient:true));var start=await Starter(h).RunAsync(h.Tenant);
        var child=await h.Db.Wf_FlowInstances.AsNoTracking().SingleAsync(x=>x.ParentInstanceId==start.InstanceId);
        foreach(var actorId in new[]{f.First,f.Second})
        {
            var task=await h.Db.Wf_FlowTasks.AsNoTracking().SingleAsync(x=>x.InstanceId==child.Id && x.AssigneeId==actorId && x.Status==0);
            var actor=DecisionActor(h,actorId);await Vote(actor,await SubflowInput(actor,task.Id,actorId==f.First?"approve":"reject"));
        }
        foreach(var actorId in new[]{f.First,f.Second})
        {
            var task=await h.Db.Wf_FlowTasks.AsNoTracking().SingleAsync(x=>x.InstanceId==start.InstanceId && x.AssigneeId==actorId && x.Status==0);
            var actor=DecisionActor(h,actorId);await Vote(actor,await SubflowInput(actor,task.Id));
        }
        var json=await h.Db.Database.SqlQuery<string>($"SELECT evidence_json AS \"Value\" FROM cp6_approval_decision_v2.decisions WHERE tenant_id={h.Tenant}").SingleAsync();
        using var evidence=JsonDocument.Parse(json);var votes=evidence.RootElement.GetProperty("votes").EnumerateArray().ToArray();Assert.Equal(4,votes.Length);
        var satisfied=evidence.RootElement.GetProperty("satisfiedRequirementIds").EnumerateArray().Select(x=>x.GetString()).ToArray();
        Assert.All(votes.Where(x=>x.GetProperty("instanceId").GetGuid()==child.Id),x=>Assert.DoesNotContain(x.GetProperty("requirementId").GetString(),satisfied));
        Assert.Equal(FlowInstanceStatus.Approved,(await h.Db.Wf_FlowInstances.AsNoTracking().SingleAsync(x=>x.Id==start.InstanceId)).Status);
    }
}
