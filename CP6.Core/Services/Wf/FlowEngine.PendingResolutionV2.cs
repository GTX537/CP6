using System.Text.Json;
using CP6.Core.Services.Oa;
using CP6.Entity.DomainModels.Wf;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Wf;

internal sealed record PendingApproverSlotV2(Guid InstanceId,Guid TokenId,string NodeId,int StageIndex,int StageRound,
    string? StageCode,string Countersign,Guid FlowVersionId,ApproverRecoveryPolicyV2? Policy,string Reason);

public partial class FlowEngine
{
    private readonly List<PendingApproverSlotV2> _pendingApproverSlots=[];
    internal void SuspendApproverV2(Wf_FlowInstance instance,FlowNode node,Wf_FlowToken token,int stage,int round,
        string? stageCode,string countersign,ApproverRecoveryPolicyV2? policy,string reason)
    {
        Suspend(instance,node,reason);
        if(ManagedRuntime)_pendingApproverSlots.Add(new(instance.Id,token.Id,node.Id,stage,round,stageCode,countersign,instance.FlowDefVersionId!.Value,policy,reason));
    }
    internal async Task SavePendingApproversV2(Guid command,string kind,CancellationToken ct)
    {
        var environment=ManagedEnvironment;var root=ManagedRoot;
        var cycle=_personalFormRuntime?await _db.Database.SqlQuery<Guid>($"SELECT id AS \"Value\" FROM cp6_personal_form_v2.execution_cycles WHERE root_id={root} ORDER BY cycle_number DESC LIMIT 1").SingleAsync(ct):root;
        foreach(var slot in _pendingApproverSlots)
        {
            var policyRef=$"FLOW/{slot.FlowVersionId:D}/NODE/{Uri.EscapeDataString(slot.NodeId)}/STAGE/{slot.StageIndex}/APPROVER_RECOVERY";
            await _db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO cp6_native_tasks_v2.pending_resolutions(id,tenant_id,environment_id,root_id,instance_id,execution_cycle_id,
                  token_id,node_id,stage_index,stage_round,flow_version,route_policy_ref,slot_json,source_kind,source_command,instance_version)
                SELECT {Guid.NewGuid()},{_db.CurrentTenantId},{environment},{root},{slot.InstanceId},{cycle},{slot.TokenId},{slot.NodeId},
                  {slot.StageIndex},{slot.StageRound},{slot.FlowVersionId},{policyRef},{JsonSerializer.Serialize(slot,PersonalFormOwnerV2.Json)},
                  {kind},{command},"RowVersion" FROM public."Wf_FlowInstance" WHERE "Id"={slot.InstanceId} AND "TenantId"={_db.CurrentTenantId}
                """,ct);
        }
        _pendingApproverSlots.Clear();
        // Cancelled slots cannot be revived by a stale administrator context.
        await _db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE cp6_native_tasks_v2.pending_resolutions p SET state='CANCELLED',resolution_kind={kind},resolution_command={command}
            WHERE p.tenant_id={_db.CurrentTenantId} AND p.environment_id={environment} AND p.root_id={root} AND p.state='PENDING'
              AND EXISTS(SELECT FROM public."Wf_FlowToken" t WHERE t."Id"=p.token_id AND t."Status"<>0)
            """,ct);
    }
    internal async Task<IReadOnlyList<PersonalFormAssignmentV2>> StagePersonalResolveApproverV2(Guid environment,Guid data,Guid root,
        FlowPublicationV2 publication,PendingApproverSlotV2 slot,Guid[] targets,Guid actor,string reason,CancellationToken ct)
    {
        PersonalContext(environment,data,root,publication);
        var instance=await _db.Wf_FlowInstances.SingleAsync(i=>i.Id==slot.InstanceId,ct);
        await CreateResolutionTasks(slot,targets,actor,reason,instance,publication,ct);
        return _personalAssignments;
    }
    private async Task CreateResolutionTasks(PendingApproverSlotV2 slot,Guid[] targets,Guid actor,string reason,
        Wf_FlowInstance instance,FlowPublicationV2 publication,CancellationToken ct)
    {
        var token=await _db.Wf_FlowTokens.SingleAsync(t=>t.Id==slot.TokenId,ct);
        var node=FindNode(Deserialize(publication.Fields["SchemaJson"]!),slot.NodeId)!;var step=NextStepSeq(instance.Id);
        WriteSnapshot(instance,node,token,step);
        var rule=new ApproverRule(ApproverStrategy.Group,null,null,null){Members=slot.Policy!.EligibleRoleIds.Select(r=>new ApproverRule(ApproverStrategy.Role,null,r,null)).ToList()};
        foreach(var target in targets)
        {
            var task=new Wf_FlowTask{Id=Guid.NewGuid(),InstanceId=slot.InstanceId,NodeId=slot.NodeId,TokenId=slot.TokenId,StageIndex=slot.StageIndex,
                StageRound=slot.StageRound,AssigneeId=target,Status=FlowTaskStatus.Pending,Countersign=slot.Countersign,DueAt=NodeDueAt(node)};
            _db.Wf_FlowTasks.Add(task);RecordApprovalAssignment(instance,task,rule,targets,slot.StageCode);
            WriteFormToOnSend(instance,node,token,target,null,step,slot.StageIndex,slot.StageRound,task.Id);
        }
        AddHistory(instance.Id,slot.NodeId,actor,"resolve-approver",reason);instance.Modifier=actor.ToString("D");instance.ModifyDate=DateTime.Now;
    }
    internal async Task StageAppResolveApproverV2(FlowPublicationV2 publication,ApprovalSubmitCommandV2 original,
        PendingApproverSlotV2 slot,Guid[] targets,Guid actor,string reason,CancellationToken ct)
    {
        AppOperationContext(publication,original);
        await CreateResolutionTasks(slot,targets,actor,reason,await _db.Wf_FlowInstances.SingleAsync(i=>i.Id==slot.InstanceId,ct),publication,ct);
    }
}
