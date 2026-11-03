using System.Text.Json;
using CP6.Core.Services.Oa;
using CP6.Entity.DomainModels.Wf;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Wf;

internal sealed record PersonalFormAssignmentV2(Guid TaskId, Guid InstanceId, Guid TokenId, Guid FlowVersionId,
    string NodeId, int StageIndex, int StageRound, string? StageCode, string? Countersign,
    Guid PrincipalId, Guid[] Candidates, ApproverRule Rule, DateTimeOffset AssignedAt,
    Guid? CreatedPrincipalId=null,long AssignmentRevision=0);

public partial class FlowEngine
{
    internal const string PersonalFormRuntimeV2 = "oa.form/2.0";
    private bool _personalFormRuntime, _personalFormDecision;
    private Guid _personalEnvironment, _personalRoot, _personalData;
    private readonly List<PersonalFormAssignmentV2> _personalAssignments = [];
    internal bool ManagedRuntime => _approvalStartV2 || _personalFormRuntime;
    private bool ManagedDecision => _approvalDecisionV2 || _personalFormDecision;
    private Guid ManagedEnvironment => _personalFormRuntime ? _personalEnvironment : Guid.Parse(_approvalOriginal!.Scope.EnvironmentId);
    private Guid ManagedRoot => _personalFormRuntime ? _personalRoot : ApprovalInstanceId(_approvalOriginal!);

    private void PersonalContext(Guid environment, Guid data, Guid root, FlowPublicationV2 publication)
    {
        _personalFormRuntime = true; _personalEnvironment = environment; _personalData = data; _personalRoot = root;
        _approvalPublications.Add(publication.VersionId, publication);
    }

    // Transaction ownership belongs to PersonalFormOwnerV2. No SaveChanges, external callback,
    // notifier or background wakeup occurs inside this adapter.
    internal async Task<Wf_FlowInstance> StagePersonalStartV2(Guid environment, Guid data, Guid root,
        FlowPublicationV2 publication, Guid actor, string values, CancellationToken ct)
    {
        PersonalContext(environment, data, root, publication);
        var schema = Deserialize(publication.Fields["SchemaJson"]!); var first = FirstNode(schema)!;
        var instance = new Wf_FlowInstance
        {
            Id = root, FlowDefVersionId = publication.VersionId, FlowKey = publication.Key,
            FormDefVersionId = ApprovalFormPin(publication), FormDataId = data,
            BizType = "SFS", BizId = data.ToString("D"), StarterId = actor, VarsJson = values,
            CurrentNode = first.Id, Status = FlowInstanceStatus.Running, Creator = PersonalFormRuntimeV2
        };
        _db.Wf_FlowInstances.Add(instance);
        _approvalChildren.Add((instance, publication, ""));
        AddHistory(root, first.Id, actor, "submit", "Submitted pinned personal form");
        await EnterNodeAsync(instance, schema, first, SpawnToken(instance, first));
        await DrainApprovalSubflows(ct); return instance;
    }

    internal async Task<Wf_FlowInstance> StagePersonalDecisionV2(Guid environment, Guid data, Guid root,
        FlowPublicationV2 publication, PersonalFormAssignmentV2 assignment, Guid actor, Guid? onBehalfOf,
        bool approve, string comment, CancellationToken ct)
    {
        PersonalContext(environment, data, root, publication); _personalFormDecision = true;
        await ActOnceAsync(assignment.TaskId, actor, approve, comment, onBehalfOf);
        await DrainApprovalSubflows(ct);
        return await _db.Wf_FlowInstances.SingleAsync(i => i.Id == root, ct);
    }

    internal async Task<IReadOnlyList<PersonalFormAssignmentV2>> SavePersonalRuntime(Guid command, CancellationToken ct)
    {
        if (_approvalChildren.Any(x => x.Instance.Id == _personalRoot))
            await _db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO cp6_personal_form_v2.execution_cycles(id,root_id,cycle_number,command_id)
                VALUES({_personalRoot},{_personalRoot},1,{command})
                """,ct);
        foreach (var (instance, publication, node) in _approvalChildren)
            await _db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO cp6_personal_form_v2.instances(instance_id,tenant_id,environment_id,form_data_id,root_id,
                  parent_id,parent_token_id,parent_node,sub_index,flow_head,flow_version)
                VALUES({instance.Id},{_db.CurrentTenantId},{_personalEnvironment},{_personalData},{_personalRoot},
                  {instance.ParentInstanceId},{instance.ParentTokenId},{(instance.ParentInstanceId == null ? null : node)},
                  {instance.SubIndex},{publication.DefinitionId},{publication.VersionId})
                """, ct);
        foreach (var assignment in _personalAssignments)
            await _db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO cp6_personal_form_v2.assignments(task_id,instance_id,command_id,body)
                VALUES({assignment.TaskId},{assignment.InstanceId},{command},{JsonSerializer.Serialize(assignment,PersonalFormOwnerV2.Json)})
                """, ct);
        _approvalChildren.Clear();
        await _db.SaveChangesAsync(ct);
        await SavePendingApproversV2(command,"SFS_COMMAND",ct);
        return _personalAssignments;
    }
}
