using System.Text.Json;
using CP6.Core.Services.Integration;
using CP6.Core.Services.Oa;
using Microsoft.EntityFrameworkCore;
using static CP6.Core.Services.Wms.ToolInstanceStoreV1;
namespace CP6.Core.Services.Wms;

public sealed record ToolWithdrawalResultV1(Guid OperationId,Guid ToolId,string State,ApprovalWithdrawalFactV2? Fact,
    Guid? TransportCommandId,Guid? PreviousCommandId=null);
public sealed record ToolWithdrawalOperationV1(Guid OperationId,string State,ToolWithdrawalResultV1? Result=null);
public sealed record ToolWithdrawalAuthorizationV1(Guid CommandId,Guid? PreviousCommandId,string State,Guid ActualHandlerId,
    Guid RequesterId,Guid? GrantId,string? GrantRevision);
public sealed partial class ToolDispositionApprovalOwnerV1
{
    internal const string WithdrawalOwner="Main.Wms.ToolWithdrawal.v1",WithdrawalAuthorization="Main.Wms.ToolWithdrawalAuthorization.v1";
    private const string WithdrawalJournal="Main.Wms.ToolWithdrawal.operations.v1",WithdrawalContract="Main.Wms.ToolWithdrawal.commands.v1";
    private sealed record WithdrawalStored(Guid Tool,Guid Submission,Guid Actor,Guid Operation,byte[] Payload,string Digest,string Authority,byte[] After,string? Mode);
    private Task<WithdrawalStored?> Withdrawal(Guid command,CancellationToken ct)=>db.Database.SqlQuery<WithdrawalStored>($"""
        SELECT tool_id AS "Tool",submission_id AS "Submission",actor_id AS "Actor",operation_id AS "Operation",payload AS "Payload",digest AS "Digest",authority_json AS "Authority",after_version AS "After",authority_json::jsonb->>'evidenceMode' AS "Mode"
        FROM cp6_tool_approval_control_v1.withdrawals WHERE environment_id={Environment} AND tenant_id={db.CurrentTenantId} AND command_id={command}
        """).SingleOrDefaultAsync(ct);
    private Task<ToolWithdrawalAuthorizationV1[]> WithdrawalAuthorizations(Guid submission,CancellationToken ct)=>db.Database.SqlQuery<ToolWithdrawalAuthorizationV1>($"""
        SELECT w.command_id AS "CommandId",w.previous_command_id AS "PreviousCommandId",coalesce(c.state,'PENDING') AS "State",w.actor_id AS "ActualHandlerId",
          (convert_from(w.payload,'UTF8')::jsonb->'fact'->'actor'->>'requesterId')::uuid AS "RequesterId",
          (convert_from(w.payload,'UTF8')::jsonb->'fact'->'actor'->>'grantId')::uuid AS "GrantId",
          convert_from(w.payload,'UTF8')::jsonb->'fact'->'actor'->>'grantRevision' AS "GrantRevision"
        FROM cp6_tool_approval_control_v1.withdrawals w LEFT JOIN cp6_approval_withdraw_v2.commands c
          ON c.environment_id=w.environment_id AND c.tenant_id=w.tenant_id AND c.owner={ToolDispositionStoreV1.DispositionOwner} AND c.id=w.command_id
        WHERE w.environment_id={Environment} AND w.tenant_id={db.CurrentTenantId} AND w.submission_id={submission} ORDER BY w.after_version
        """).ToArrayAsync(ct);
    public sealed record WithdrawalContextV1(Guid EnvironmentId,Guid ToolId,Guid SubmissionId,string State,string ControlRowVersion,bool CanWithdraw,
        ToolWithdrawalAuthorizationV1[] Authorizations,CP6.Core.Services.Wms.ToolApplicationDeliveryViewV2? Delivery);
    public Task<WithdrawalContextV1> WithdrawalContextAsync(Guid id,Guid submission,CancellationToken ct=default)
        =>Authority.ExecuteAsync(OperationsOwnerV1.ToolInstance,"query",false,async(a,t)=>{
            var c=await WithdrawalContextOwned(a,id,submission,false,t);var allowed=false;
            try
            {
                var actor=await BusinessActor(a.ActorId,"dispose-withdraw",t);
                await BusinessResponsibility(actor,c.Context,c.Submission,"dispose-withdraw",t);allowed=true;
            }
            catch(CP6.Core.Services.Erp.OrderPriceCorrectionException e) when(e.Status==403) { }
            return new WithdrawalContextV1(Environment,id,submission,c.Control.State,c.Control.RowVersion,allowed&&c.Control.State=="IN_REVIEW",
                await WithdrawalAuthorizations(submission,t),await ToolWithdrawalDeliveryV1.ForSubmission(db,Environment,submission,t));
        },ct);
    public Task<ToolWithdrawalResultV1> WithdrawAsync(Guid id,Guid submission,string? ifMatch,ApprovalUserWithdrawV2 input,CancellationToken ct=default,Guid? behalf=null)
        =>WithdrawCore(id,submission,ifMatch,input,null,behalf,ct);
    public Task<ToolWithdrawalResultV1> ReauthorizeWithdrawalAsync(Guid id,Guid submission,Guid previous,string? ifMatch,
        ApprovalUserWithdrawV2 input,CancellationToken ct=default,Guid? behalf=null)
        =>WithdrawCore(id,submission,ifMatch,input,previous,behalf,ct);
    private Task<ToolWithdrawalResultV1> WithdrawCore(Guid id,Guid submission,string? ifMatch,ApprovalUserWithdrawV2 input,
        Guid? previous,Guid? behalf,CancellationToken ct)
    {
        if(input==null || id==Guid.Empty || submission==Guid.Empty || input.CommandId==Guid.Empty)throw Error("WITHDRAW_COMMAND_INVALID",400);
        Text(input.Reason,500);
        var command=OperationsOwnerV1.Freeze(input);var journal=new MainOwnerOperationJournalV1(db);MainOwnerOperationJournalV1.Original? original=null;
        var bytes=previous==null&&behalf==null?MainOwnerOperationJournalV1.Encode(new{contract=WithdrawalContract,environment=Environment,id,submission,ifMatch,command})
            :MainOwnerOperationJournalV1.Encode(new{contract=WithdrawalContract,environment=Environment,id,submission,ifMatch,command,previous,behalf});
        return Authority.ExecuteAsync(OperationsOwnerV1.ToolInstance,"dispose-withdraw",true,async(a,t)=>{
            var c=await WithdrawalContextOwned(a,id,submission,original==null,t);
            if(original!=null)return await DiscloseWithdrawal(c.Context,command.CommandId,original,bytes,t);
            var acceptedMode=await db.Database.SqlQuery<string>($"""
                SELECT selection_json::jsonb->'registration'->>'evidenceMode' AS "Value" FROM cp6_approval_intake_v2.submissions
                WHERE environment_id={Environment} AND tenant_id={db.CurrentTenantId} AND owner={ToolDispositionStoreV1.DispositionOwner} AND id={submission}
                """).SingleOrDefaultAsync(t);
            var grant=await BusinessAct(a,c.Context,c.Submission,"dispose-withdraw",behalf,t);
            ToolWithdrawalResultV1 result;
            {
                if(string.IsNullOrEmpty(ifMatch))throw Error("IF_MATCH_REQUIRED",428);
                if(ifMatch!="\""+c.Control.RowVersion+"\"")throw Error("TOOL_APPROVAL_CONTROL_CHANGED",412);
                if(previous==null&&c.Control.State!="IN_REVIEW")
                    throw Error("TOOL_APPROVAL_ROUND_CHANGED",409);
                if(previous.HasValue)await RequireBlockedWithdrawal(id,submission,previous.Value,c,t);
                var envelope=c.Context.Store.Envelope(c.Submission);
                // Tool owns the frozen submission identity; intake requires that same canonical
                // identity for its first acceptance. Transport may still be pending here.
                var before=c.Control.RowVersion;
                var after=await new ToolApprovalControlStoreV1(db,Environment).Transition(c.Control,"WITHDRAWN",t,previous.HasValue);
                var at=Now();var reference=new ApprovalRefV2(WithdrawalOwner,Guid.NewGuid().ToString("D"),"1");
                var actor=new ApprovalWithdrawalActorV2("SUBMITTER_WITHDRAWAL",command.CommandId,grant?.GrantorId??a.ActorId,a.ActorId,grant?.GrantorId,grant?.GrantId,grant?.Revision,reference,
                    new(WithdrawalAuthorization,command.CommandId.ToString("D"),"1"),at);
                var fact=new ApprovalWithdrawalFactV2("oa.app.withdrawal-fact/1.0",envelope.Scope,ToolDispositionStoreV1.DispositionOwner,reference,command.CommandId,
                    ApprovalCanonicalV2.Digest(ApprovalCanonicalV2.Bytes(command)),submission,envelope.Subject,envelope.Policy.Ref,before,after.RowVersion,at,actor,command.Reason);
                var transport=new ApprovalWithdrawCommandV2("oa.app.withdraw/2.0",envelope.Scope,ToolDispositionStoreV1.DispositionOwner,Guid.NewGuid(),Guid.NewGuid(),submission,fact,
                    ApprovalCanonicalV2.Digest(ApprovalCanonicalV2.Bytes(fact)));
                var payload=ApprovalCanonicalV2.Bytes(transport);var digest=ApprovalCanonicalV2.Hash(payload);
                var audit=ApprovalBindingOwnerV2.Serialize(new{profile="Main.Wms.ToolWithdrawal/1",evidenceMode=acceptedMode,ifMatch,command,actor,previous,grantBody=grant==null?null:JsonSerializer.Serialize(grant,PersonalFormOwnerV2.Json),authorities=Witnesses(a),before,after=after.RowVersion});
                await db.Database.ExecuteSqlInterpolatedAsync($"""
                    INSERT INTO cp6_tool_approval_control_v1.withdrawals(environment_id,tenant_id,tool_id,submission_id,id,operation_id,command_id,outbox_id,actor_id,
                      before_version,after_version,payload,digest,authority_json,previous_command_id)
                    VALUES({Environment},{db.CurrentTenantId},{id},{submission},{Guid.Parse(reference.Id)},{command.CommandId},{transport.CommandId},{transport.OwnerOutboxId},{a.ActorId},
                      {c.Control.Version},{after.Version},{payload},{digest},{audit},{previous});
                    INSERT INTO cp6_tool_approval_control_v1.withdraw_outbox(environment_id,tenant_id,id,command_id,submission_id,payload,digest)
                    VALUES({Environment},{db.CurrentTenantId},{transport.OwnerOutboxId},{transport.CommandId},{submission},{payload},{digest})
                    """,t);
                result=new(command.CommandId,id,"WITHDRAWN",fact,transport.CommandId,previous);
                a.AtCommit(token=>a.EngineeringFinalObservation(async(observation,finalToken)=>{
                    await BusinessResponsibility(observation,c.Context,c.Submission,"dispose-withdraw",finalToken);
                    await BusinessAct(observation,c.Context with{Access=observation},c.Submission,"dispose-withdraw",behalf,finalToken,grant);return true;
                },token));
            }
            await journal.Store(WithdrawalJournal,WithdrawalContract,command.CommandId,a.ActorId,bytes,result,[id],new{command,ifMatch,result,authorities=Witnesses(a)},t);
            return result;
        },ct,actionAtFence:async t=>{original=await journal.Read(WithdrawalJournal,command.CommandId,t);return original==null?"dispose-withdraw":"query";});
    }
    private async Task RequireBlockedWithdrawal(Guid id,Guid submission,Guid previous,WithdrawalContext c,CancellationToken ct)
    {
        var old=await Withdrawal(previous,ct);
        if(old==null||old.Tool!=id||old.Submission!=submission)throw Error("RESOURCE_NOT_FOUND",404);
        if(c.Control.State!="WITHDRAWN"||!c.Control.Version.AsSpan().SequenceEqual(old.After))
            throw Error("TOOL_APPROVAL_ROUND_CHANGED",409);
        if(!await db.Database.SqlQuery<bool>($"""
            SELECT EXISTS(SELECT FROM cp6_approval_withdraw_v2.commands WHERE environment_id={Environment} AND tenant_id={db.CurrentTenantId}
              AND owner={ToolDispositionStoreV1.DispositionOwner} AND id={previous} AND submission_id={submission} AND state='BLOCKED') AS "Value"
            """).SingleAsync(ct))throw Error("WITHDRAW_BLOCKED_COMMAND_REQUIRED",409);
        if(await db.Database.SqlQuery<bool>($"""
            SELECT EXISTS(SELECT FROM cp6_approval_decision_v2.decisions WHERE environment_id={Environment} AND tenant_id={db.CurrentTenantId}
              AND submission_id={submission}) AS "Value"
            """).SingleAsync(ct))throw Error("WITHDRAW_WORKFLOW_ALREADY_DECIDED",409);
    }
    internal async Task<string> WithdrawalEvidence(ApprovalSubmitCommandV2 original,ApprovalWithdrawCommandV2 command,bool current,CancellationToken ct)
    {
        var (_,row,envelope)=await Frozen(original,ct);var stored=await Withdrawal(command.CommandId,ct)??throw Error("WITHDRAW_FACT_MISMATCH",422);
        if(!ApprovalCanonicalV2.Bytes(command).AsSpan().SequenceEqual(stored.Payload)
            || stored.Tool!=row.Tool || stored.Submission!=row.Id || command.Fact.Subject!=envelope.Subject || command.Fact.PolicyRef!=envelope.Policy.Ref
            || command.Fact.OwnerWithdrawalRef.Owner!=WithdrawalOwner || command.Fact.Actor.WithdrawAuthorizationRef!=new ApprovalRefV2(WithdrawalAuthorization,stored.Operation.ToString("D"),"1"))
            throw Error("WITHDRAW_FACT_MISMATCH",422);
        if(!current)return stored.Authority;
        var acceptedMode=await db.Database.SqlQuery<string>($"""
            SELECT selection_json::jsonb->'registration'->>'evidenceMode' AS "Value" FROM cp6_approval_intake_v2.submissions
            WHERE environment_id={Environment} AND tenant_id={db.CurrentTenantId} AND owner={ToolDispositionStoreV1.DispositionOwner} AND id={row.Id}
            """).SingleOrDefaultAsync(ct);
        if(stored.Mode!=null&&stored.Mode!=acceptedMode)throw Error("TOOL_EVIDENCE_MODE_CHANGED",422);
        var actor=command.Fact.Actor;
        if(actor.ActualHandlerId!=stored.Actor)throw Error("WITHDRAW_AUTHORITY_UNAVAILABLE",403);
        Task<OperationsOwnerV1.Authority> Current(string r,string action,CancellationToken t)=>Authority.ToolWithdrawalAuthorityAsync(r,action,stored.Actor,t);
        var authority=await Current(OperationsOwnerV1.ToolInstance,"dispose-withdraw",ct);
        var access=new OperationsAccessV1(db,owner,authority,storedSubjectSource:Current);var c=await Prepare(access,row.Tool,false,ct);
        var control=await new ToolApprovalControlStoreV1(db,Environment).Read(row.Id,ct);
        if(control.State!="WITHDRAWN"||!control.Version.AsSpan().SequenceEqual(stored.After))
            throw Error("TOOL_WITHDRAWAL_SUPERSEDED",409);
        await BusinessResponsibility(access,c,row,"dispose-withdraw",ct);
        ToolBusinessGrantV1? grant=null;
        if(actor.GrantId.HasValue)
        {
            grant=(await CurrentBusinessGrants(row.Tool,row.Id,ct)).SingleOrDefault(g=>g.GrantId==actor.GrantId&&g.Revision==actor.GrantRevision)
                ??throw Error("TOOL_BUSINESS_GRANT_DENIED",403);
        }
        var currentGrant=await BusinessAct(access,c,row,"dispose-withdraw",actor.OnBehalfOfId,ct,grant);
        await ValidateParticipant(access,stored.Actor,ct);
        return ApprovalBindingOwnerV2.Serialize(new{stored.Authority,currentAuthorities=Witnesses(access),actor,currentGrant});
    }
    private async Task<ToolWithdrawalResultV1> DiscloseWithdrawal(Context c,Guid operation,MainOwnerOperationJournalV1.Original original,byte[]? bytes,CancellationToken ct)
    {
        if(original.Actor!=c.Access.ActorId || original.Contract!=WithdrawalContract)throw Error("ORIGINAL_OPERATION_FORBIDDEN",403);
        if(original.State=="CLOSED")throw Error("ORIGINAL_OPERATION_CLOSED",409);
        if(bytes!=null && !MainOwnerOperationJournalV1.Matches(original,bytes))throw Error("ORIGINAL_OPERATION_INPUT_CONFLICT",409);
        var r=JsonSerializer.Deserialize<ToolWithdrawalResultV1>(original.Result);
        if(original.State!="COMMITTED" || r==null || r.OperationId!=operation || r.ToolId!=c.Disposition.ToolId || !original.Objects.SequenceEqual(new[]{r.ToolId}))
            throw Error("ORIGINAL_OPERATION_UNAVAILABLE",503);
        if(r.State=="WITHDRAWN" && r.TransportCommandId is Guid transport)
        {
            var stored=await Withdrawal(transport,ct)??throw Error("ORIGINAL_OPERATION_UNAVAILABLE",503);
            await BusinessResponsibility(c.Access,c,c.Submissions.Single(x=>x.Id==stored.Submission),"query",ct);
        }
        else throw Error("ORIGINAL_OPERATION_UNAVAILABLE",503);
        return r;
    }
    public Task<ToolWithdrawalOperationV1> WithdrawalOperationAsync(Guid operation,bool close,CancellationToken ct=default)
        =>Authority.ExecuteAsync(OperationsOwnerV1.ToolInstance,"query",close,async(a,t)=>{
            if(operation==Guid.Empty)throw Error("ORIGINAL_OPERATION_REQUIRED",400);
            await scope.Root(t);a.RequireToolFieldRead("Remarks");var journal=new MainOwnerOperationJournalV1(db);var original=await journal.Read(WithdrawalJournal,operation,t);
            if(original!=null)
            {
                if(original.Actor!=a.ActorId || original.Contract!=WithdrawalContract)throw Error("ORIGINAL_OPERATION_FORBIDDEN",403);
                if(original.State=="CLOSED")return new ToolWithdrawalOperationV1(operation,"CLOSED");
                if(original.Objects.Length!=1)throw Error("ORIGINAL_OPERATION_UNAVAILABLE",503);
                return new ToolWithdrawalOperationV1(operation,"COMMITTED",await DiscloseWithdrawal(await Prepare(a,original.Objects[0],false,t),operation,original,null,t));
            }
            if(!close)return new ToolWithdrawalOperationV1(operation,"UNKNOWN");
            var closed=new ToolWithdrawalOperationV1(operation,"CLOSED");
            await journal.Store(WithdrawalJournal,WithdrawalContract,operation,a.ActorId,null,closed,[],new{action="close-uncommitted-tool-withdrawal",a.ActorId},t);return closed;
        },ct,readOnlyAuthorityFence:!close);
}
