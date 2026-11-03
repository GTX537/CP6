using CP6.Core.Services.Oa;
using CP6.Core.Services.Sys;
using CP6.Entity.DomainModels.Wf;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Wf;

public partial class FlowEngine
{
    internal async Task<NativeSendBackOptionV2[]> SendBackOptionsV2(Guid taskId,FlowPublicationV2 publication,CancellationToken ct)
    {
        var task=await _db.Wf_FlowTasks.AsNoTracking().SingleAsync(t=>t.Id==taskId,ct);var schema=Deserialize(publication.Fields["SchemaJson"]!);
        var tokens=await _db.Wf_FlowTokens.AsNoTracking().Where(t=>t.InstanceId==task.InstanceId).ToArrayAsync(ct);var current=tokens.Single(t=>t.Id==task.TokenId);
        var result=new List<NativeSendBackOptionV2>{new("STARTER",null,"发起人")};
        if(task.StageIndex>0)result.Add(new("PREV_STAGE",null,"上一审批阶段"));
        foreach(var node in schema.Nodes.Where(n=>n.Id!=task.NodeId&&(IsType(n,"approval")||IsType(n,"start"))))
            if(IsUpstreamReachable(schema,node.Id,task.NodeId)&&SendBackScopeAnalyzer.Analyze(schema,tokens,current,task.NodeId,node.Id).Item1!=SendBackScope.SiblingBranch)
                result.Add(new("NODE",node.Id,node.Name??node.Id));
        return result.ToArray();
    }
    internal async Task StageNativeTransferV2(Wf_FlowTask task,Guid target,Guid actual,Guid? behalf,string reason,CancellationToken ct)
    {
        var src=await NativePendingFormTo(task,ct);
        src.Status=FlowFormToStatus.Transferred;src.ActualHandlerId=actual;src.OnBehalfOfId=behalf;src.HandledAt=DateTime.Now;src.Comment=reason;
        _db.Wf_FlowFormTos.Add(new(){Id=Guid.NewGuid(),NativeTaskId=task.Id,InstanceId=src.InstanceId,TokenId=src.TokenId,NodeId=src.NodeId,NodeCode=src.NodeCode,NodeName=src.NodeName,
            FromNodeId=src.FromNodeId,StageIndex=src.StageIndex,StageRound=src.StageRound,StepSeq=NextStepSeq(src.InstanceId),ExpectedHandlerId=target,Status=FlowFormToStatus.Pending,SentAt=DateTime.Now});
        task.AssigneeId=target;task.Modifier=actual.ToString("D");task.ModifyDate=DateTime.Now;
        AddHistory(task.InstanceId,task.NodeId,actual,"transfer",reason);
    }
    internal async Task<Wf_FlowTask> StagePersonalAddSignV2(Guid environment,Guid data,Guid root,FlowPublicationV2 publication,
        Wf_FlowTask task,Guid target,Guid actor,string source,string reason,string? stageCode,CancellationToken ct)
    {
        PersonalContext(environment,data,root,publication);
        return await StageNativeAddSign(publication,task,target,actor,source,reason,stageCode,ct);
    }
    private void AppOperationContext(FlowPublicationV2 publication,ApprovalSubmitCommandV2 original)
    {
        _approvalStartV2=true;_approvalOriginal=original;_approvalPublications.Add(publication.VersionId,publication);
    }
    internal Task<Wf_FlowTask> StageAppAddSignV2(FlowPublicationV2 publication,ApprovalSubmitCommandV2 original,Wf_FlowTask task,
        Guid target,Guid actor,string source,string reason,string? stageCode,CancellationToken ct)
    {AppOperationContext(publication,original);return StageNativeAddSign(publication,task,target,actor,source,reason,stageCode,ct);}
    private async Task<Wf_FlowTask> StageNativeAddSign(FlowPublicationV2 publication,Wf_FlowTask task,Guid target,Guid actor,string source,string reason,string? stageCode,CancellationToken ct)
    {
        var instance=await _db.Wf_FlowInstances.SingleAsync(i=>i.Id==task.InstanceId,ct);
        if(source=="before")task.Status=FlowTaskStatus.Suspended;
        var added=new Wf_FlowTask{Id=Guid.NewGuid(),InstanceId=task.InstanceId,NodeId=task.NodeId,TokenId=task.TokenId,StageIndex=task.StageIndex,
            StageRound=task.StageRound,AssigneeId=target,Countersign=task.Countersign,DueAt=task.DueAt,Status=FlowTaskStatus.Pending,AddSignSource=source};
        _db.Wf_FlowTasks.Add(added);RecordApprovalAssignment(instance,added,new(ApproverStrategy.Specified,null,null,target),[target],stageCode);
        var token=await _db.Wf_FlowTokens.SingleAsync(t=>t.Id==task.TokenId,ct);var node=FindNode(Deserialize(publication.Fields["SchemaJson"]!),task.NodeId)!;
        WriteFormToOnSend(instance,node,token,target,null,NextStepSeq(instance.Id),task.StageIndex,task.StageRound,added.Id);
        AddHistory(instance.Id,task.NodeId,actor,"addsign",reason);return added;
    }
    internal async Task<NativeSendBackPreviewV2> PreviewSendBackV2(Guid taskId, Guid root,
        FlowPublicationV2 publication, NativeSendBackTargetV2 target, CancellationToken ct)
    {
        var task = await _db.Wf_FlowTasks.AsNoTracking().SingleAsync(t => t.Id == taskId, ct);
        var instance = await _db.Wf_FlowInstances.AsNoTracking().SingleAsync(i => i.Id == task.InstanceId, ct);
        var schema = Deserialize(publication.Fields["SchemaJson"]!);
        var tokens = await _db.Wf_FlowTokens.AsNoTracking().Where(t => t.InstanceId == instance.Id).ToArrayAsync(ct);
        var current = tokens.Single(t => t.Id == task.TokenId);
        var affected = tokens.Select(t => t.Id).ToHashSet(); var scope = "INSTANCE";
        if (target.TargetKind == "NODE")
        {
            var node = FindNode(schema, target.TargetNodeId ?? "");
            if (node == null || node.Id == task.NodeId || !IsType(node, "approval") && !IsType(node, "start"))
                throw new ScopeAuthorizationExceptionV1("E-WF-012", 422);
            var (kind, strip) = SendBackScopeAnalyzer.Analyze(schema, tokens, current, task.NodeId, node.Id);
            if (kind == SendBackScope.SiblingBranch) throw new ScopeAuthorizationExceptionV1("E-WF-019", 422);
            if (!IsUpstreamReachable(schema, node.Id, task.NodeId)) throw new ScopeAuthorizationExceptionV1("E-WF-012", 422);
            if (kind == SendBackScope.SameBranch && strip != null)
            {
                scope = "TOKEN_SUBTREE"; affected = [strip.Id]; bool added;
                do { added = false; foreach (var t in tokens) if (t.ParentTokenId is Guid p && affected.Contains(p)) added |= affected.Add(t.Id); } while (added);
            }
        }
        else if (target.TargetKind == "PREV_STAGE" && target.TargetNodeId == null && task.StageIndex > 0)
        { scope = "PREV_STAGE"; affected = [current.Id]; }
        else if (target.TargetKind == "STARTER" && target.TargetNodeId == null)
        { scope = "ROOT_INSTANCE"; }
        else throw new ScopeAuthorizationExceptionV1("E-WF-012", 422);

        var members = new HashSet<Guid> { scope == "ROOT_INSTANCE" ? root : instance.Id };
        if (scope == "ROOT_INSTANCE")
        {
            var all = await _db.Wf_FlowInstances.AsNoTracking().Select(i => new { i.Id, i.ParentInstanceId }).ToArrayAsync(ct);
            bool added; do { added = false; foreach (var i in all) if (i.ParentInstanceId is Guid p && members.Contains(p)) added |= members.Add(i.Id); } while (added);
            tokens = await _db.Wf_FlowTokens.AsNoTracking().Where(t => members.Contains(t.InstanceId)).ToArrayAsync(ct);
            affected = tokens.Select(t => t.Id).ToHashSet();
        }
        else if(scope!="PREV_STAGE")
        {
            var children=await _db.Wf_FlowInstances.AsNoTracking().Where(i=>i.ParentInstanceId!=null&&
                (i.Status==FlowInstanceStatus.Running||i.Status==FlowInstanceStatus.Suspended||i.Status==FlowInstanceStatus.Draft))
                .Select(i=>new{i.Id,i.ParentInstanceId,i.ParentTokenId}).ToArrayAsync(ct);
            foreach(var child in children.Where(i=>i.ParentInstanceId==instance.Id&&i.ParentTokenId.HasValue&&affected.Contains(i.ParentTokenId.Value)))members.Add(child.Id);
            bool grew;do{grew=false;foreach(var child in children)if(child.ParentInstanceId is Guid parent&&parent!=instance.Id&&members.Contains(parent))grew|=members.Add(child.Id);}while(grew);
            var childIds=members.Where(id=>id!=instance.Id).ToArray();
            var childTokens=await _db.Wf_FlowTokens.AsNoTracking().Where(t=>childIds.Contains(t.InstanceId)).ToArrayAsync(ct);
            tokens=tokens.Concat(childTokens).ToArray();affected.UnionWith(childTokens.Select(t=>t.Id));
        }
        var tasks = await _db.Wf_FlowTasks.AsNoTracking().Where(t => members.Contains(t.InstanceId)
            && affected.Contains(t.TokenId!.Value) && (t.Status == FlowTaskStatus.Pending || t.Status == FlowTaskStatus.Suspended)).ToArrayAsync(ct);
        if (scope == "PREV_STAGE") tasks = tasks.Where(t => t.StageIndex == task.StageIndex && t.StageRound == task.StageRound).ToArray();
        var version = await _db.Database.SqlQuery<byte[]>($"SELECT \"ApprovalRowVersion\" AS \"Value\" FROM public.\"Wf_FlowTask\" WHERE \"Id\"={taskId} AND \"TenantId\"={_db.CurrentTenantId}").SingleAsync(ct);
        var preview = new NativeSendBackPreviewV2(taskId, instance.Id, target.TargetKind, target.TargetNodeId, scope,
            tasks.Select(t => t.Id).Order().ToArray(), tokens.Where(t => affected.Contains(t.Id) && t.Status == FlowTokenStatus.Active).Select(t => t.Id).Order().ToArray(),
            target.TargetKind == "STARTER", Convert.ToBase64String(version), Convert.ToBase64String(instance.RowVersion!), "");
        var taskIds=preview.AffectedTaskIds;
        var taskVersions=await _db.Database.SqlQuery<PreviewTaskVersion>($"SELECT \"Id\",\"AssigneeId\" AS \"Principal\",\"ApprovalRowVersion\" AS \"Version\" FROM public.\"Wf_FlowTask\" WHERE \"Id\"=ANY({taskIds}) AND \"TenantId\"={_db.CurrentTenantId} ORDER BY \"Id\"").ToArrayAsync(ct);
        var memberVersions=await _db.Wf_FlowInstances.AsNoTracking().Where(i=>members.Contains(i.Id)||i.Id==root).OrderBy(i=>i.Id).Select(i=>new{i.Id,i.RowVersion}).ToArrayAsync(ct);
        var lineage=tokens.Where(t=>affected.Contains(t.Id)).OrderBy(t=>t.Id).Select(t=>new{t.Id,t.ParentTokenId,t.ForkId,t.NodeId,t.Status,t.StagePlanJson}).ToArray();
        return preview with { PreviewDigest = ApprovalCanonicalV2.Hash(ApprovalCanonicalV2.Bytes(new{preview,taskVersions,memberVersions,lineage})) };
    }
    private sealed record PreviewTaskVersion(Guid Id,Guid Principal,byte[] Version);

    internal async Task<Wf_FlowInstance> StagePersonalSendBackV2(Guid environment, Guid data, Guid root,
        FlowPublicationV2 publication, Guid taskId, Guid actor, Guid? onBehalfOf, NativeSendBackTargetV2 target, string comment, CancellationToken ct)
    {
        PersonalContext(environment, data, root, publication);
        return await StageNativeSendBack(publication,taskId,root,actor,onBehalfOf,target,comment,ct);
    }
    internal Task<Wf_FlowInstance> StageAppSendBackV2(FlowPublicationV2 publication,ApprovalSubmitCommandV2 original,Guid taskId,
        Guid actor,Guid? onBehalfOf,NativeSendBackTargetV2 target,string comment,CancellationToken ct)
    {AppOperationContext(publication,original);return StageNativeSendBack(publication,taskId,ApprovalInstanceId(original),actor,onBehalfOf,target,comment,ct);}
    private async Task<Wf_FlowInstance> StageNativeSendBack(FlowPublicationV2 publication,Guid taskId,Guid root,Guid actor,Guid? onBehalfOf,
        NativeSendBackTargetV2 target,string comment,CancellationToken ct)
    {
        var task = await _db.Wf_FlowTasks.SingleAsync(t => t.Id == taskId, ct);
        var handled=await NativePendingFormTo(task,ct);
        var instance = await _db.Wf_FlowInstances.SingleAsync(i => i.Id == task.InstanceId, ct);
        var schema = Deserialize(publication.Fields["SchemaJson"]!);
        if (target.TargetKind == "STARTER")
        {
            var original = await _db.Wf_FlowInstances.SingleAsync(i => i.Id == root, ct);
            await SendBackToStarterAsync(original, task, actor, comment);
        }
        else if (target.TargetKind == "PREV_STAGE") await SendBackToPrevStageAsync(instance, schema, task, actor, comment);
        else await SendBackToNodeAsync(instance, schema, task, actor, target.TargetNodeId, comment);
        handled.ActualHandlerId=actor;handled.OnBehalfOfId=onBehalfOf;handled.HandledAt=DateTime.Now;handled.Comment=comment;
        foreach(var history in _db.ChangeTracker.Entries<Wf_FlowHistory>().Where(e=>e.State==EntityState.Added&&e.Entity.Action=="subFlowCascadeCancelled"))
            history.Entity.ActorId=actor;
        await DrainApprovalSubflows(ct);
        instance.ModifyDate = DateTime.Now;
        return await _db.Wf_FlowInstances.SingleAsync(i => i.Id == root, ct);
    }

    internal async Task<Wf_FlowInstance> StagePersonalResubmitV2(Guid environment, Guid data, Guid root,
        FlowPublicationV2 publication, Guid actor, string values, string? comment, CancellationToken ct)
    {
        PersonalContext(environment, data, root, publication);
        var instance = await _db.Wf_FlowInstances.SingleAsync(i => i.Id == root, ct);
        var schema = Deserialize(publication.Fields["SchemaJson"]!); var first = FirstNode(schema)!;
        instance.Status = FlowInstanceStatus.Running; instance.VarsJson = values;
        instance.Modifier = actor.ToString("D"); instance.ModifyDate = DateTime.Now;
        AddHistory(root, first.Id, actor, "resubmit", comment);
        await EnterNodeAsync(instance, schema, first, SpawnToken(instance, first));
        await DrainApprovalSubflows(ct); return instance;
    }
}
