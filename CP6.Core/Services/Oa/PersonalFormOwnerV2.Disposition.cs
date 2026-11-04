using CP6.Core.Services.Sys;
using CP6.Core.Services.Wf;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Oa;

public sealed partial class PersonalFormOwnerV2
{
    internal Task DispositionWriter(ScopeAuthorizationLeaseV1 lease,CancellationToken ct)=>WriteReady(lease,ct);
    internal async Task<FormDispositionScopeV1> DispositionSource(FormDispositionRequestV1 input,UserPermissionContext actor,CancellationToken ct)
    {
        await Action(actor,"edit",ct);var row=await Owned(input.DraftId,actor.UserId,ct);
        Active(row);Expected(row,input.ExpectedDraftVersion.ToString("D"));
        var policy=await Policy(row.Head,ct)??throw Error("FORM_USE_POLICY_REQUIRED",403);
        if(!policy.Enabled)throw Error("FORM_USE_POLICY_DISABLED",403);
        var source=await Publication(row.Head,row.Version,ct);var target=await Publication(row.Head,input.TargetVersionId,ct);
        _=Mask(actor,source,policy);
        await DispositionTarget(row.Head,row.Version,input.TargetVersionId,ct);
        if(DispositionLosses(source,target,row.Data).Length==0)throw Error("FORM_DISPOSITION_NO_LOSS",422);
        return await DispositionScope(row,source,target,policy,ct);
    }
    private async Task<FormDispositionScopeV1> DispositionScope(DraftRow row,FormPublicationV2 source,FormPublicationV2 target,PersonalFormPolicyV2 policy,CancellationToken ct)
    {
        var history=await db.Database.SqlQuery<Guid>($"""
            SELECT id AS "Value" FROM cp6_personal_form_v2.history WHERE draft_id={row.Id}
              AND (after_json::jsonb->>'row_version')::uuid={row.Token}
            """).SingleAsync(ct);
        return new FormDispositionScopeV1(fence.EnvironmentId,db.CurrentTenantId,row.Id,row.Owner,row.Head,history,row.Token,
            row.Version,target.VersionId,source.SchemaDigest!,target.SchemaDigest!,policy.Version,policy.RetentionPolicyRef,policy.RetentionDays);
    }
    internal async Task DispositionCurrent(FormDispositionScopeV1 scope,CancellationToken ct)
    {
        var row=await Drafts().SingleAsync(x=>x.Id==scope.DraftId,ct);Active(row);
        if(row.Token!=scope.DraftVersion)throw Error("FORM_DISPOSITION_SOURCE_CHANGED",409);
        await DispositionTarget(scope.HeadId,scope.SourceVersionId,scope.TargetVersionId,ct);
        await DispositionPolicyCurrent(scope,ct);
    }
    internal async Task DispositionPolicyCurrent(FormDispositionScopeV1 scope,CancellationToken ct)
    {
        var policy=await Policy(scope.HeadId,ct);
        if(policy==null||!policy.Enabled||policy.Version!=scope.PolicyVersion)throw Error("FORM_DISPOSITION_POLICY_CHANGED",409);
    }
    internal async Task<FormDispositionLossV1[]> DispositionProcessor(FormDispositionScopeV1 scope,Guid id,PersonalFormPolicyV2 policy,CancellationToken ct)
    {
        if(!await db.Sys_Users.AsNoTracking().AnyAsync(x=>x.Id==id&&x.Enable&&(x.LockedUntil==null||x.LockedUntil<=DateTime.Now),ct))
            throw Error("FORM_DISPOSITION_PROCESSOR_UNAVAILABLE",403);
        var processor=await PostgreSqlPermissionReader.ReadAsync(db,id,ct);
        await Action(processor,"retain",ct,FormFieldDispositionOwnerV1.Menu);
        return await DispositionRead(scope,processor,ct,policy);
    }
    private async Task DispositionTarget(Guid head,Guid source,Guid target,CancellationToken ct)
    {
        var latest=await db.Database.SqlQuery<Guid>($"""
            SELECT id AS "Value" FROM cp6_form_authoring_v2.versions WHERE tenant_id={db.CurrentTenantId}
              AND environment_id={fence.EnvironmentId} AND head_id={head} ORDER BY version_number DESC LIMIT 1
            """).SingleAsync(ct);
        if(latest!=target||latest==source)throw Error("REBASE_TARGET_CHANGED",409);
    }
    private static FormDispositionLossV1[] DispositionLosses(FormPublicationV2 source,FormPublicationV2 target,string data)
    {
        static Dictionary<string,string> Readable(string schema)=>PersonalFormValuesV2.Schema(schema).Fields.ToDictionary(x=>x.Name,_=>"readonly",StringComparer.Ordinal);
        var losses=new List<FormDispositionLossV1>();
        PersonalFormRebaseV2.Build(source.Fields["SchemaJson"]!,target.Fields["SchemaJson"]!,data,
            Readable(source.Fields["SchemaJson"]!),Readable(target.Fields["SchemaJson"]!),
            (field,path,kind,value)=>losses.Add(new(field,path,kind,value.ToJsonString())));
        return losses.ToArray();
    }
    internal async Task<FormDispositionLossV1[]> DispositionRead(FormDispositionScopeV1 scope,UserPermissionContext actor,CancellationToken ct,PersonalFormPolicyV2? policy=null)
    {
        var source=await Publication(scope.HeadId,scope.SourceVersionId,ct);var target=await Publication(scope.HeadId,scope.TargetVersionId,ct);
        policy??=await Policy(scope.HeadId,ct)??throw Error("FORM_USE_POLICY_REQUIRED",403);
        var sourceMask=Mask(actor,source,policy);var targetMask=Mask(actor,target,policy);
        var data=await db.Database.SqlQuery<string>($"""
            SELECT after_json::jsonb->>'data_json' AS "Value" FROM cp6_personal_form_v2.history WHERE id={scope.HistoryId}
            """).SingleAsync(ct);
        var losses=DispositionLosses(source,target,data);
        if(losses.Any(x=>sourceMask[x.Field]=="hidden"||targetMask.GetValueOrDefault(x.Field,sourceMask[x.Field])=="hidden"))
            throw Error("FORM_DISPOSITION_READ_REQUIRED",403);
        return losses;
    }
    internal async Task DispositionRequesterRead(FormDispositionScopeV1 scope,UserPermissionContext actor,CancellationToken ct)
    {
        await Action(actor,"query",ct);await Owned(scope.DraftId,actor.UserId,ct);
        await Mask(actor,scope.HeadId,await Publication(scope.HeadId,scope.SourceVersionId,ct),ct);
    }
}
