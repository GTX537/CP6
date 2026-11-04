using CP6.Core.Services.Integration;
using CP6.Core.Services.Sys;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Oa;
public sealed partial class FormFieldDispositionOwnerV1
{
    private Task<FormDispositionConsumptionV1?> Consumption(Guid id,CancellationToken ct)=>db.Database.SqlQuery<FormDispositionConsumptionV1>($"""
        SELECT command_ref AS "CommandRef",command_id AS "CommandId",actor_id AS "ActualActorId",processor_id AS "ProcessorId",
          draft_version AS "DraftVersion",recorded_at AS "RecordedAtUtc" FROM cp6_form_disposition_v1.consumptions
        WHERE tenant_id={db.CurrentTenantId} AND environment_id={fence.EnvironmentId} AND request_id={id}
        """).SingleOrDefaultAsync(ct);
    private async Task<FormDispositionOperationV1> CreateRequest(FormDispositionRequestV1 input,byte[] bytes,FormDispositionScopeV1 scope,
        UserPermissionContext actor,ScopeAuthorizationLeaseV1 lease,CancellationToken t)
    {
        var id=Guid.NewGuid();
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO cp6_form_disposition_v1.requests(tenant_id,environment_id,id,draft_id,history_id,scope)
                VALUES({db.CurrentTenantId},{fence.EnvironmentId},{id},{scope.DraftId},{scope.HistoryId},{Encode(scope)})
                """,t);
            var first=new EventRow(id,null,"REQUESTED",actor.UserId,input.Reason,forms.Clock(),null);
            await Append(id,first,input.OperationId,t);var result=new FormDispositionOperationV1(input.OperationId,"COMMITTED",await View(id,scope,first,t));
            await Store(input.OperationId,actor.UserId,bytes,result,new{kind="REQUEST",scope.HistoryId,scope.PolicyVersion,lease.Snapshot},t);return result;
    }
    // Participates in the caller's existing native FORM transaction and authorization fence.
    internal async Task<Guid> RequiredForRebase(FormDispositionScopeV1 scope,UserPermissionContext actor,ScopeAuthorizationLeaseV1 lease,CancellationToken ct)
    {
        await StorageReady(ct);
        var existing=await db.Database.SqlQuery<Guid>($"""
            SELECT r.id AS "Value" FROM cp6_form_disposition_v1.requests r
            JOIN LATERAL(SELECT state FROM cp6_form_disposition_v1.events e WHERE e.tenant_id=r.tenant_id AND e.environment_id=r.environment_id AND e.request_id=r.id ORDER BY sequence DESC LIMIT 1)e ON true
            WHERE r.tenant_id={db.CurrentTenantId} AND r.environment_id={fence.EnvironmentId} AND r.draft_id={scope.DraftId}
              AND r.scope::jsonb={Encode(scope)}::jsonb AND e.state IN('REQUESTED','APPROVED','RETAINED')
              AND NOT EXISTS(SELECT FROM cp6_form_disposition_v1.consumptions c WHERE c.tenant_id=r.tenant_id AND c.environment_id=r.environment_id AND c.request_id=r.id)
            ORDER BY r.id LIMIT 1
            """).SingleOrDefaultAsync(ct);
        if(existing!=Guid.Empty)return existing;
        var input=new FormDispositionRequestV1(Guid.NewGuid(),scope.DraftId,scope.DraftVersion,scope.TargetVersionId,"Controlled processing required by exact draft rebase preview");
        return (await CreateRequest(input,MainOwnerOperationJournalV1.Encode(new{kind="REQUEST",input}),scope,actor,lease,ct)).Request!.RequestId;
    }
    internal async Task<FormDispositionRebaseV1> ForRebase(FormDispositionRefV1 reference,Guid draft,Guid token,Guid target,
        UserPermissionContext actor,PersonalFormPolicyV2 policy,CancellationToken ct)
    {
        if(reference.RequestId==Guid.Empty||reference.ReceiptVersion==Guid.Empty)throw Error("FORM_DISPOSITION_INPUT_INVALID",400);
        await StorageReady(ct);var(_,scope)=await Required(reference.RequestId,ct);
        if(scope.RequesterId!=actor.UserId||scope.DraftId!=draft)throw Error("FORM_DISPOSITION_NOT_FOUND",404);
        if(scope.DraftVersion!=token||scope.TargetVersionId!=target)throw Error("FORM_DISPOSITION_SCOPE_CHANGED");
        if(await Consumption(reference.RequestId,ct)!=null)throw Error("FORM_DISPOSITION_ALREADY_CONSUMED");
        var retained=(await Events(reference.RequestId,ct))[^1];
        if(retained.Version!=reference.ReceiptVersion||retained.State!="RETAINED")throw Error("FORM_DISPOSITION_VERSION_CONFLICT");
        if(retained.RetainUntilUtc<=forms.Clock())throw Error("FORM_DISPOSITION_RETENTION_EXPIRED");
        if((await Hold(draft,ct))?.Active==true)throw Error("FORM_DISPOSITION_ON_HOLD");
        if(!policy.Enabled||policy.Version!=scope.PolicyVersion)throw Error("FORM_DISPOSITION_POLICY_CHANGED");
        var losses=await forms.DispositionProcessor(scope,retained.ActorId,policy,ct);
        return new(reference,scope,retained.ActorId,losses,retained.RetainUntilUtc!.Value);
    }
    internal Task Consume(FormDispositionRebaseV1 authority,Guid command,string commandId,Guid actor,Guid token,CancellationToken ct)
        =>db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO cp6_form_disposition_v1.consumptions(tenant_id,environment_id,request_id,receipt_version,command_ref,command_id,actor_id,processor_id,draft_version)
            VALUES({db.CurrentTenantId},{fence.EnvironmentId},{authority.Reference.RequestId},{authority.Reference.ReceiptVersion},
              {command},{commandId},{actor},{authority.Processor},{token})
            """,ct);
}
