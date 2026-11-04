using System.Text;
using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Integration;
using CP6.Core.Services.Sys;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Oa;

public sealed class FormFieldDispositionOwnerV1(CP6Context db,PersonalFormOwnerV2 forms,ScopeAuthorizationFenceV1 fence)
{
    internal const string Menu="oa-form-dispositions";
    private const string Contract="Main.Oa.FormFieldDisposition.commands.v1";
    private string Journal=>"Main.Oa.FormFieldDisposition.v1/"+fence.EnvironmentId.ToString("D");
    private static readonly JsonSerializerOptions Json=new(JsonSerializerDefaults.Web);
    private static string Encode(object value)=>JsonSerializer.Serialize(value,Json);
    private static ScopeAuthorizationExceptionV1 Error(string code,int status=409)=>new(code,status);
    private sealed record RequestRow(Guid Id,string Scope);
    private sealed record EventRow(Guid Version,Guid? PreviousVersion,string State,Guid ActorId,string Reason,DateTimeOffset RecordedAtUtc,DateTimeOffset? RetainUntilUtc);
    private sealed record HoldRow(Guid Version,bool Active);
    private Task<RequestRow?> Request(Guid id,CancellationToken ct)=>db.Database.SqlQuery<RequestRow>($"""
        SELECT id AS "Id",scope AS "Scope" FROM cp6_form_disposition_v1.requests
        WHERE tenant_id={db.CurrentTenantId} AND environment_id={fence.EnvironmentId} AND id={id}
        """).SingleOrDefaultAsync(ct);
    private async Task<(RequestRow Row,FormDispositionScopeV1 Scope)> Required(Guid id,CancellationToken ct)
    {
        var row=await Request(id,ct)??throw Error("FORM_DISPOSITION_NOT_FOUND",404);
        return(row,JsonSerializer.Deserialize<FormDispositionScopeV1>(row.Scope,Json)!);
    }
    private Task<EventRow[]> Events(Guid id,CancellationToken ct)=>db.Database.SqlQuery<EventRow>($"""
        SELECT version AS "Version",previous_version AS "PreviousVersion",state AS "State",actor_id AS "ActorId",
          reason AS "Reason",recorded_at AS "RecordedAtUtc",retain_until AS "RetainUntilUtc"
        FROM cp6_form_disposition_v1.events WHERE tenant_id={db.CurrentTenantId} AND environment_id={fence.EnvironmentId} AND request_id={id}
        ORDER BY sequence
        """).ToArrayAsync(ct);
    private Task<HoldRow?> Hold(Guid draft,CancellationToken ct)=>db.Database.SqlQuery<HoldRow>($"""
        SELECT version AS "Version",active AS "Active" FROM cp6_form_disposition_v1.holds
        WHERE tenant_id={db.CurrentTenantId} AND environment_id={fence.EnvironmentId} AND draft_id={draft} ORDER BY sequence DESC LIMIT 1
        """).SingleOrDefaultAsync(ct);
    private async Task<FormDispositionViewV1> View(Guid id,FormDispositionScopeV1 scope,EventRow latest,CancellationToken ct)
    {
        var hold=await Hold(scope.DraftId,ct);
        return new(id,scope.DraftId,scope.SourceVersionId,scope.TargetVersionId,latest.Version,latest.State,latest.RecordedAtUtc,
            hold==null?null:new(hold.Version,hold.Active),latest.State=="RETAINED"?new(id,latest.Version):null,latest.RetainUntilUtc);
    }
    private async Task SafeRead(FormDispositionScopeV1 scope,UserPermissionContext actor,CancellationToken ct)
    {
        if(scope.RequesterId==actor.UserId)await forms.DispositionRequesterRead(scope,actor,ct);
        else {await forms.Action(actor,"query",ct,Menu);await forms.DispositionRead(scope,actor,ct);}
    }
    private async Task Ready(ScopeAuthorizationLeaseV1 lease,CancellationToken ct)
    {
        await forms.DispositionWriter(lease,ct);
        if(!await db.Database.SqlQueryRaw<bool>("SELECT cp6_form_disposition_v1.ready() AS \"Value\"").SingleAsync(ct))
            throw Error("FORM_DISPOSITION_WRITER_UNAVAILABLE",503);
    }
    private async Task<T> Execute<T>(Func<UserPermissionContext,ScopeAuthorizationLeaseV1,CancellationToken,Task<T>> run,CancellationToken ct)
    {
        forms.Entry();await using var tx=await db.Database.BeginTransactionAsync(ct);var committing=false;
        try
        {
            var lease=await forms.ReadLease(ct);var actor=await forms.Actor(ct);var result=await run(actor,lease,ct);
            await forms.Final(actor,lease,ct,false);committing=true;await tx.CommitAsync(ct);return result;
        }
        catch(Exception e){await forms.Rollback(tx,committing,e);throw;}
    }
    private static void Input(Guid operation,string reason)
    {
        if(operation==Guid.Empty)throw Error("ORIGINAL_OPERATION_REQUIRED",400);
        Text(reason,500);
    }
    private static void Text(string value,int max)
    {
        if(string.IsNullOrWhiteSpace(value)||value.EnumerateRunes().Count()>max)throw Error("FORM_DISPOSITION_INPUT_INVALID",400);
        try{new UTF8Encoding(false,true).GetByteCount(value);}catch(EncoderFallbackException){throw Error("FORM_DISPOSITION_INPUT_INVALID",400);}
    }
    private async Task<FormDispositionOperationV1?> Original(Guid operation,UserPermissionContext actor,byte[]? input,CancellationToken ct)
    {
        var old=await new MainOwnerOperationJournalV1(db).Read(Journal,operation,ct);if(old==null)return null;
        if(old.Actor!=actor.UserId)throw Error("ORIGINAL_OPERATION_NOT_FOUND",404);
        var result=JsonSerializer.Deserialize<FormDispositionOperationV1>(old.Result)!;
        if(result.Request is {} view)await SafeRead((await Required(view.RequestId,ct)).Scope,actor,ct);
        else await Query(actor,ct);
        if(input!=null)
        {
            if(old.State=="CLOSED")throw Error("COMMAND_CLOSED");
            if(!MainOwnerOperationJournalV1.Matches(old,input))throw Error("COMMAND_BODY_CONFLICT");
        }
        return result;
    }
    private Task Query(UserPermissionContext actor,CancellationToken ct)=>forms.Action(actor,"query",ct,
        actor.MenuKeys.Contains(Menu)&&actor.ActionKeys.Contains(Menu+":query")?Menu:"oa-form-catalog");
    private Task Store(Guid operation,Guid actor,byte[]? input,FormDispositionOperationV1 result,object audit,CancellationToken ct)
        =>new MainOwnerOperationJournalV1(db).Store(Journal,Contract,operation,actor,input,result,
            result.Request is {} r?[r.RequestId,r.DraftId]:[],audit,ct);
    private Task Append(Guid request,EventRow value,Guid operation,CancellationToken ct)=>db.Database.ExecuteSqlInterpolatedAsync($"""
        INSERT INTO cp6_form_disposition_v1.events(tenant_id,environment_id,request_id,version,previous_version,state,actor_id,reason,recorded_at,retain_until,operation_id,journal)
        VALUES({db.CurrentTenantId},{fence.EnvironmentId},{request},{value.Version},{value.PreviousVersion},{value.State},{value.ActorId},{value.Reason},{value.RecordedAtUtc},{value.RetainUntilUtc},{operation},{Journal})
        """,ct);
    public Task<FormDispositionOperationV1> RequestAsync(FormDispositionRequestV1 input,CancellationToken ct=default)
    {
        Input(input.OperationId,input.Reason);
        if(input.DraftId==Guid.Empty||input.ExpectedDraftVersion==Guid.Empty||input.TargetVersionId==Guid.Empty)throw Error("FORM_DISPOSITION_INPUT_INVALID",400);
        var bytes=MainOwnerOperationJournalV1.Encode(new{kind="REQUEST",input});
        return Execute(async(actor,lease,t)=>
        {
            var old=await Original(input.OperationId,actor,bytes,t);if(old!=null)return old;
            await Ready(lease,t);var scope=await forms.DispositionSource(input,actor,t);var id=Guid.NewGuid();
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO cp6_form_disposition_v1.requests(tenant_id,environment_id,id,draft_id,history_id,scope)
                VALUES({db.CurrentTenantId},{fence.EnvironmentId},{id},{scope.DraftId},{scope.HistoryId},{Encode(scope)})
                """,t);
            var first=new EventRow(id,null,"REQUESTED",actor.UserId,input.Reason,forms.Clock(),null);
            await Append(id,first,input.OperationId,t);var result=new FormDispositionOperationV1(input.OperationId,"COMMITTED",await View(id,scope,first,t));
            await Store(input.OperationId,actor.UserId,bytes,result,new{kind="REQUEST",scope.HistoryId,scope.PolicyVersion,lease.Snapshot},t);return result;
        },ct);
    }
    public Task<FormDispositionDetailV1> ReadAsync(Guid request,CancellationToken ct=default)=>Execute(async(actor,_,t)=>
    {
        var (_,scope)=await Required(request,t);var events=await Events(request,t);var view=await View(request,scope,events[^1],t);
        if(scope.RequesterId==actor.UserId&&!actor.ActionKeys.Contains(Menu+":query"))
        {await forms.DispositionRequesterRead(scope,actor,t);return new FormDispositionDetailV1(view,null,null,null,null);}
        await forms.Action(actor,"query",t,Menu);var losses=await forms.DispositionRead(scope,actor,t);
        return new FormDispositionDetailV1(view,scope.HistoryId,scope.RetentionPolicyRef,losses,
            events.Select(x=>new FormDispositionEventV1(x.Version,x.PreviousVersion,x.State,x.ActorId,x.Reason,x.RecordedAtUtc,x.RetainUntilUtc)).ToArray());
    },ct);
    public Task<FormDispositionOperationV1> DecideAsync(Guid request,string action,FormDispositionCommandV1 input,CancellationToken ct=default)
    {
        Input(input.OperationId,input.Reason);
        if(input.ExpectedVersion==Guid.Empty||action is not ("approve" or "reject" or "retain" or "revoke"))throw Error("FORM_DISPOSITION_INPUT_INVALID",400);
        var bytes=MainOwnerOperationJournalV1.Encode(new{request,action,input});
        return Execute(async(actor,lease,t)=>
        {
            var old=await Original(input.OperationId,actor,bytes,t);if(old!=null)return old;
            await Ready(lease,t);await forms.Action(actor,action=="reject"?"approve":action,t,Menu);
            var (_,scope)=await Required(request,t);await forms.DispositionRead(scope,actor,t);
            var previous=(await Events(request,t))[^1];
            if(previous.Version!=input.ExpectedVersion)throw Error("FORM_DISPOSITION_VERSION_CONFLICT");
            var state=action switch
            {
                "approve" when previous.State=="REQUESTED"=>"APPROVED",
                "reject" when previous.State=="REQUESTED"=>"REJECTED",
                "retain" when previous.State=="APPROVED"=>"RETAINED",
                "revoke" when previous.State is "REQUESTED" or "APPROVED" or "RETAINED"=>"REVOKED",
                _=>throw Error("FORM_DISPOSITION_STATE_CONFLICT")
            };
            if(action is "approve" or "reject"&&actor.UserId==scope.RequesterId)throw Error("FORM_DISPOSITION_INDEPENDENT_APPROVER_REQUIRED",403);
            if(action is "approve" or "retain")
            {
                await forms.DispositionCurrent(scope,t);
                if((await Hold(scope.DraftId,t))?.Active==true)throw Error("FORM_DISPOSITION_ON_HOLD");
            }
            var at=forms.Clock();var next=new EventRow(Guid.NewGuid(),previous.Version,state,actor.UserId,input.Reason,at,
                state=="RETAINED"?at.AddDays(scope.RetentionDays):previous.RetainUntilUtc);
            await Append(request,next,input.OperationId,t);var result=new FormDispositionOperationV1(input.OperationId,"COMMITTED",await View(request,scope,next,t));
            await Store(input.OperationId,actor.UserId,bytes,result,new{action,scope.HistoryId,scope.PolicyVersion,lease.Snapshot},t);return result;
        },ct);
    }
    public Task<FormDispositionOperationV1> HoldAsync(Guid request,FormDispositionHoldCommandV1 input,CancellationToken ct=default)
    {
        Input(input.OperationId,input.Reason);Text(input.Basis,200);
        if(input.ExpectedVersion==Guid.Empty)throw Error("FORM_DISPOSITION_INPUT_INVALID",400);
        var bytes=MainOwnerOperationJournalV1.Encode(new{request,kind="HOLD",input});
        return Execute(async(actor,lease,t)=>
        {
            var old=await Original(input.OperationId,actor,bytes,t);if(old!=null)return old;
            await Ready(lease,t);await forms.Action(actor,"hold",t,Menu);var (_,scope)=await Required(request,t);await forms.DispositionRead(scope,actor,t);
            var previous=await Hold(scope.DraftId,t);if(previous?.Version!=input.ExpectedVersion)throw Error("FORM_DISPOSITION_HOLD_VERSION_CONFLICT");
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO cp6_form_disposition_v1.holds(tenant_id,environment_id,draft_id,version,previous_version,active,basis,reason,actor_id,operation_id,journal)
                VALUES({db.CurrentTenantId},{fence.EnvironmentId},{scope.DraftId},{Guid.NewGuid()},{input.ExpectedVersion},{input.Active},{input.Basis},{input.Reason},{actor.UserId},{input.OperationId},{Journal})
                """,t);
            var result=new FormDispositionOperationV1(input.OperationId,"COMMITTED",await View(request,scope,(await Events(request,t))[^1],t));
            await Store(input.OperationId,actor.UserId,bytes,result,new{kind="HOLD",scope.DraftId,input.Active,input.Basis,lease.Snapshot},t);return result;
        },ct);
    }
    public Task<FormDispositionOperationV1> OperationAsync(Guid operation,bool close=false,CancellationToken ct=default)
    {
        if(operation==Guid.Empty)throw Error("ORIGINAL_OPERATION_REQUIRED",400);
        return Execute(async(actor,lease,t)=>
        {
            var old=await Original(operation,actor,null,t);if(old!=null)return old;await Query(actor,t);
            var result=new FormDispositionOperationV1(operation,close?"CLOSED":"UNKNOWN",null);
            if(close){await Ready(lease,t);await Store(operation,actor.UserId,null,result,new{kind="CLOSE",lease.Snapshot},t);}return result;
        },ct);
    }
}
