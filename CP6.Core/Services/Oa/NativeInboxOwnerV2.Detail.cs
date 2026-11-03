using CP6.Core.Services.Sys;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Oa;

public sealed partial class NativeInboxOwnerV2
{
    private async Task<(Member Member,Content Content)> Visible(Guid instance,ReadIdentity actor,CancellationToken ct)
    {
        if(!(await Participating(actor.EffectiveId,ct)).Contains(instance))throw Error("RESOURCE_NOT_FOUND",404);
        var member=await Members().SingleOrDefaultAsync(m=>m.InstanceId==instance,ct)??throw Error("RESOURCE_NOT_FOUND",404);
        if(!actor.CanRead(member))throw Error("RESOURCE_NOT_FOUND",404);
        var content=await Authorize(member,actor,ct);actor.Use(member);return(member,content);
    }
    private sealed record TaskVersion(Guid Id,byte[] Version);
    public async Task<NativeInstanceReadV2> DetailAsync(Guid instance,Guid? behalf=null,CancellationToken ct=default)
    {
        forms.Entry();await using var tx=await db.Database.BeginTransactionAsync(ct);var(actor,lease)=await Begin(behalf,ct);
        var(member,content)=await Visible(instance,actor,ct);
        var result=await Detail(instance,member,content,actor,lease,ct);
        await FinalRead(actor,lease,ct);return result;
    }
    private async Task<NativeInstanceReadV2> Detail(Guid instance,Member member,Content content,ReadIdentity actor,ScopeAuthorizationLeaseV1 lease,CancellationToken ct)
    {
        var members=(await Members().Where(m=>m.Root==member.Root).ToArrayAsync(ct)).Where(actor.CanRead).ToArray();
        foreach(var m in members)actor.Use(m);var ids=members.Select(m=>m.InstanceId).ToArray();
        var instances=await db.Wf_FlowInstances.AsNoTracking().Where(i=>ids.Contains(i.Id)).ToArrayAsync(ct);
        var tasks=await db.Wf_FlowTasks.AsNoTracking().Where(t=>ids.Contains(t.InstanceId)).OrderBy(t=>t.CreateDate).ThenBy(t=>t.Id).ToArrayAsync(ct);
        var cc=await db.Wf_FlowCcs.AsNoTracking().Where(c=>ids.Contains(c.InstanceId)&&c.RecipientId==actor.EffectiveId).ToArrayAsync(ct);
        var versions=await db.Database.SqlQuery<TaskVersion>($"""
            SELECT "Id","ApprovalRowVersion" AS "Version" FROM public."Wf_FlowTask"
            WHERE "TenantId"={db.CurrentTenantId} AND "InstanceId"=ANY({ids})
            """).ToDictionaryAsync(t=>t.Id,t=>t.Version,ct);
        var reads=await Reads(ids,ct);
        var history=await db.Wf_FlowHistories.AsNoTracking().Where(h=>ids.Contains(h.InstanceId)).OrderBy(h=>h.CreateDate).ThenBy(h=>h.Id)
            .Select(h=>new NativeWorkflowEventV2(h.Id,h.InstanceId,h.NodeId,h.Action,h.ActorId,
                h.ActorId==Guid.Empty?"SYSTEM":"RECORDED_ACTOR",h.Comment,h.CreateDate)).ToArrayAsync(ct);
        var mayRequest=lease.Snapshot.Enabled && actor.Actual.ActionKeys.Contains("oa-inbox:approve")&&actor.Effective.ActionKeys.Contains("oa-inbox:approve");
        var result=new NativeInstanceReadV2(instance,member.Root,member.Kind,actor.UserId,actor.EffectiveId,
            instances.OrderBy(i=>i.Id==member.Root?0:1).ThenBy(i=>i.Id).Select(i=>new NativeInstancePinV2(i.Id,i.ParentInstanceId,
                members.Single(m=>m.InstanceId==i.Id).FlowVersion,i.FormDefVersionId,i.FlowKey,i.Status,Convert.ToBase64String(i.RowVersion!))).ToArray(),
            tasks.Select(t=>new NativeTaskReadV2(t.Id,t.InstanceId,t.NodeId,t.TokenId,t.StageIndex,t.StageRound,t.AssigneeId,t.Status,
                Convert.ToBase64String(versions[t.Id]),reads.SingleOrDefault(r=>r.Kind=="TASK"&&r.TargetId==t.Id),
                mayRequest&&t.AssigneeId==actor.EffectiveId&&t.Status==0&&instances.Single(i=>i.Id==t.InstanceId).Status==0
                    &&(!actor.Delegated||actor.Grant(members.Single(m=>m.InstanceId==t.InstanceId),"TASK_APPROVE")!=null||actor.Grant(members.Single(m=>m.InstanceId==t.InstanceId),"TASK_REJECT")!=null))).ToArray(),
            cc.Select(c=>new NativeCcReadV2(c.Id,c.InstanceId,c.RecipientId,c.AtNodeId,reads.SingleOrDefault(r=>r.Kind=="CC"&&r.TargetId==c.Id))).ToArray(),
            history,await Votes(ids,ct),content.Form,content.Business);
        return result;
    }
}
