using System.Text.Json;
using CP6.Core.Services.Integration;
using CP6.Core.Services.Oa;
using Microsoft.EntityFrameworkCore;
using static CP6.Core.Services.Plm.EngineeringProductValuesV1;
namespace CP6.Core.Services.Plm;

public sealed record IterationWithdrawalResultV1(Guid OperationId,Guid IterationId,string State,ApprovalWithdrawalFactV2? Fact,
    Guid? TransportCommandId,ApprovalApplicationReceiptV2? AppliedReceipt);
public sealed record IterationWithdrawalOperationV1(Guid OperationId,string State,IterationWithdrawalResultV1? Result=null);
public sealed partial class IterationReviewOwnerV1
{
    internal const string WithdrawalOwner="Main.Plm.IterationWithdrawal.v1",WithdrawalAuthorization="Main.Plm.IterationWithdrawalAuthorization.v1";
    private const string WithdrawalJournal="Main.Plm.IterationWithdrawal.operations.v1",WithdrawalContract="Main.Plm.IterationWithdrawal.commands.v1";
    private sealed record WithdrawalStored(Guid Iteration,Guid Submission,Guid Actor,Guid Operation,byte[] Payload,string Digest,string Authority);
    private Task<WithdrawalStored?> Withdrawal(Guid command,CancellationToken ct)=>db.Database.SqlQuery<WithdrawalStored>($"""
        SELECT iteration_id AS "Iteration",submission_id AS "Submission",actor_id AS "Actor",operation_id AS "Operation",payload AS "Payload",digest AS "Digest",authority_json AS "Authority"
        FROM cp6_iteration_review_control_v1.withdrawals WHERE environment_id={Environment} AND tenant_id={db.CurrentTenantId} AND command_id={command}
        """).SingleOrDefaultAsync(ct);
    private async Task WithdrawResponsibility(OperationsAccessV1 a,Context c,IterationReviewStoreV1.SubmissionRow row,CancellationToken ct)
    {
        // Explicit current withdraw authority is required even for the original starter.
        if(a.ActorId!=row.Actor && a.ActorId!=c.Iteration.Body.Iteration!.Plan.OwnerId
            && a.ActorId!=(await iterations.CandidateProject(c.Iteration.Body.Iteration!,ct)).OwnerId)throw Error("ITERATION_REVIEW_WITHDRAW_RESPONSIBILITY_REQUIRED",403);
        await DisclosePackage(a,row,false,ct);a.RequireEngineeringIterationFields(true);
    }
    public Task<IterationWithdrawalResultV1> WithdrawAsync(Guid id,Guid submission,string? ifMatch,ApprovalUserWithdrawV2 input,CancellationToken ct=default)
    {
        if(input==null || id==Guid.Empty || submission==Guid.Empty || input.CommandId==Guid.Empty)throw Error("WITHDRAW_COMMAND_INVALID",400);
        Text(input.Reason,true,1000);if(input.Reason.EnumerateRunes().Count()>500)throw Error("WITHDRAW_REASON_INVALID",400);
        var command=OperationsOwnerV1.Freeze(input);var journal=new MainOwnerOperationJournalV1(db);MainOwnerOperationJournalV1.Original? original=null;
        var bytes=MainOwnerOperationJournalV1.Encode(new{contract=WithdrawalContract,environment=Environment,id,submission,ifMatch,command});
        return Authority.ExecuteAsync(OperationsOwnerV1.EngineeringIteration,"review-withdraw",true,async(a,t)=>{
            var c=await ApplicationContextAsync(a,id,submission,original==null,t);
            if(original!=null)return await DiscloseWithdrawal(c.Context,command.CommandId,original,bytes,t);
            await WithdrawResponsibility(a,c.Context,c.Submission,t);
            IterationWithdrawalResultV1 result;
            if(c.Receipt?.Application=="APPLIED")result=new(command.CommandId,id,"CONFLICT",null,null,c.Receipt);
            else
            {
                if(string.IsNullOrEmpty(ifMatch))throw Error("IF_MATCH_REQUIRED",428);
                if(c.Control==null || ifMatch!="\""+c.Control.RowVersion+"\"")throw Error("ITERATION_REVIEW_CONTROL_CHANGED",412);
                if(c.Control.Submission!=submission || c.Control.State!="IN_REVIEW" || c.Context.Iteration.Body.Iteration!.CurrentReview?.SubmissionId!=submission)
                    throw Error("ITERATION_REVIEW_ROUND_CHANGED",409);
                var envelope=await c.Context.Store.Envelope(c.Submission,t);
                if(!await db.Database.SqlQuery<bool>($"""
                    SELECT EXISTS(SELECT FROM cp6_approval_intake_v2.submissions WHERE environment_id={Environment} AND tenant_id={db.CurrentTenantId}
                      AND owner={IterationReviewStoreV1.Owner} AND id={submission}) AS "Value"
                    """).SingleAsync(t))throw Error("WITHDRAW_SUBMISSION_NOT_ACCEPTED",422);
                var before=c.Control.RowVersion;
                var changed=await iterations.ReviewTransition(a,c.Context.Iteration,submission,command.CommandId,"review-withdraw","IN_DESIGN",true,command.Reason,t);
                var after=await new IterationReviewControlStoreV1(db,Environment).Transition(c.Control,"WITHDRAWN",t);
                var at=Now();var reference=new ApprovalRefV2(WithdrawalOwner,Guid.NewGuid().ToString("D"),"1");
                var actor=new ApprovalWithdrawalActorV2("SUBMITTER_WITHDRAWAL",command.CommandId,a.ActorId,a.ActorId,null,null,null,reference,
                    new(WithdrawalAuthorization,command.CommandId.ToString("D"),"1"),at);
                var fact=new ApprovalWithdrawalFactV2("oa.app.withdrawal-fact/1.0",envelope.Scope,IterationReviewStoreV1.Owner,reference,command.CommandId,
                    ApprovalCanonicalV2.Digest(ApprovalCanonicalV2.Bytes(command)),submission,envelope.Subject,envelope.Policy.Ref,before,after.RowVersion,at,actor,command.Reason);
                var transport=new ApprovalWithdrawCommandV2("oa.app.withdraw/2.0",envelope.Scope,IterationReviewStoreV1.Owner,Guid.NewGuid(),Guid.NewGuid(),submission,fact,
                    ApprovalCanonicalV2.Digest(ApprovalCanonicalV2.Bytes(fact)));
                var parsed=ApprovalWithdrawalWireV2.Parse(ApprovalCanonicalV2.Bytes(transport));
                var audit=Json(new{profile="Main.Plm.IterationWithdrawal/1",ifMatch,command,actor,authorities=Witnesses(a),before,after=after.RowVersion,changed.Reference});
                await db.Database.ExecuteSqlInterpolatedAsync($"""
                    INSERT INTO cp6_iteration_review_control_v1.withdrawals(environment_id,tenant_id,iteration_id,submission_id,id,operation_id,command_id,outbox_id,actor_id,
                      before_version,after_version,iteration_revision,payload,digest,authority_json)
                    VALUES({Environment},{db.CurrentTenantId},{id},{submission},{Guid.Parse(reference.Id)},{command.CommandId},{transport.CommandId},{transport.OwnerOutboxId},{a.ActorId},
                      {c.Control.Version},{after.Version},{Revision(changed.Reference.Version)},{parsed.Canonical},{parsed.Hash},{audit});
                    INSERT INTO cp6_iteration_review_control_v1.withdraw_outbox(environment_id,tenant_id,id,command_id,submission_id,payload,digest)
                    VALUES({Environment},{db.CurrentTenantId},{transport.OwnerOutboxId},{transport.CommandId},{submission},{parsed.Canonical},{parsed.Hash})
                    """,t);
                result=new(command.CommandId,id,"WITHDRAWN",fact,transport.CommandId,null);
                a.AtCommit(token=>a.EngineeringFinalObservation(async(observation,finalToken)=>{
                    await WithdrawResponsibility(observation,c.Context with{Access=observation},c.Submission,finalToken);return true;
                },token));
            }
            await journal.Store(WithdrawalJournal,WithdrawalContract,command.CommandId,a.ActorId,bytes,result,[id],new{command,ifMatch,result,authorities=Witnesses(a)},t);
            return result;
        },ct,actionAtFence:async t=>{original=await journal.Read(WithdrawalJournal,command.CommandId,t);return original==null?"review-withdraw":"query";});
    }
    internal async Task<string> WithdrawalEvidence(ApprovalSubmitCommandV2 original,ApprovalWithdrawCommandV2 command,bool current,CancellationToken ct)
    {
        var (_,row,envelope)=await Frozen(original,ct);var stored=await Withdrawal(command.CommandId,ct)??throw Error("WITHDRAW_FACT_MISMATCH",422);
        var parsed=ApprovalWithdrawalWireV2.Parse(stored.Payload);
        if(parsed.Hash!=stored.Digest || !parsed.Canonical.AsSpan().SequenceEqual(stored.Payload) || Json(parsed.Value)!=Json(command)
            || stored.Iteration!=row.Iteration || stored.Submission!=row.Id || command.Fact.Subject!=envelope.Subject || command.Fact.PolicyRef!=envelope.Policy.Ref
            || command.Fact.OwnerWithdrawalRef.Owner!=WithdrawalOwner || command.Fact.Actor.WithdrawAuthorizationRef!=new ApprovalRefV2(WithdrawalAuthorization,stored.Operation.ToString("D"),"1"))
            throw Error("WITHDRAW_FACT_MISMATCH",422);
        if(!current)return stored.Authority;
        var actor=command.Fact.Actor;
        if(actor.ActualHandlerId!=stored.Actor || actor.RequesterId!=stored.Actor || actor.GrantId!=null || actor.OnBehalfOfId!=null)
            throw Error("WITHDRAW_AUTHORITY_UNAVAILABLE",403); // This native Owner has no delegated withdrawal producer; never invent a grant.
        Task<OperationsOwnerV1.Authority> Current(string r,string action,CancellationToken t)=>Authority.IterationReviewAuthorityAsync(r,action,stored.Actor,t);
        var authority=await Current(OperationsOwnerV1.EngineeringIteration,"review-withdraw",ct);
        var access=new OperationsAccessV1(db,owner,authority,storedSubjectSource:Current);var c=await Prepare(access,row.Iteration,false,ct);
        await WithdrawResponsibility(access,c,row,ct);await ValidateParticipant(access,stored.Actor,ct);
        return Json(new{stored.Authority,currentAuthorities=Witnesses(access),actor});
    }
    private async Task<IterationWithdrawalResultV1> DiscloseWithdrawal(Context c,Guid operation,MainOwnerOperationJournalV1.Original original,byte[]? bytes,CancellationToken ct)
    {
        if(original.Actor!=c.Access.ActorId || original.Contract!=WithdrawalContract)throw Error("ORIGINAL_OPERATION_FORBIDDEN",403);
        if(original.State=="CLOSED")throw Error("ORIGINAL_OPERATION_CLOSED",409);
        if(bytes!=null && !MainOwnerOperationJournalV1.Matches(original,bytes))throw Error("ORIGINAL_OPERATION_INPUT_CONFLICT",409);
        var r=JsonSerializer.Deserialize<IterationWithdrawalResultV1>(original.Result);
        if(original.State!="COMMITTED" || r==null || r.OperationId!=operation || r.IterationId!=c.Iteration.Reference.Id || !original.Objects.SequenceEqual(new[]{r.IterationId}))
            throw Error("ORIGINAL_OPERATION_UNAVAILABLE",503);
        if(r.State=="WITHDRAWN" && r.TransportCommandId is Guid transport)
        {
            var stored=await Withdrawal(transport,ct)??throw Error("ORIGINAL_OPERATION_UNAVAILABLE",503);
            if(Json(ApprovalWithdrawalWireV2.Parse(stored.Payload).Value.Fact)!=Json(r.Fact!))throw Error("ORIGINAL_OPERATION_UNAVAILABLE",503);
            await DisclosePackage(c.Access,c.Submissions.Single(x=>x.Id==stored.Submission),false,ct);
        }
        else if(r.State=="CONFLICT" && r.AppliedReceipt!=null)
        {
            var store=new IterationReviewApplicationStoreV1(db,scope);await store.Ready(ct);
            if(Json(await store.Exact(r.AppliedReceipt.ReceiptId,ct))!=Json(r.AppliedReceipt))throw Error("ORIGINAL_OPERATION_UNAVAILABLE",503);
        }
        else throw Error("ORIGINAL_OPERATION_UNAVAILABLE",503);
        return r;
    }
    public Task<IterationWithdrawalOperationV1> WithdrawalOperationAsync(Guid operation,bool close,CancellationToken ct=default)
        =>Authority.ExecuteAsync(OperationsOwnerV1.EngineeringIteration,"query",close,async(a,t)=>{
            if(operation==Guid.Empty)throw Error("ORIGINAL_OPERATION_REQUIRED",400);
            await scope.Root(t);a.RequireEngineeringIterationFields(false);var journal=new MainOwnerOperationJournalV1(db);var original=await journal.Read(WithdrawalJournal,operation,t);
            if(original!=null)
            {
                if(original.Actor!=a.ActorId || original.Contract!=WithdrawalContract)throw Error("ORIGINAL_OPERATION_FORBIDDEN",403);
                if(original.State=="CLOSED")return new IterationWithdrawalOperationV1(operation,"CLOSED");
                if(original.Objects.Length!=1)throw Error("ORIGINAL_OPERATION_UNAVAILABLE",503);
                return new IterationWithdrawalOperationV1(operation,"COMMITTED",await DiscloseWithdrawal(await Prepare(a,original.Objects[0],false,t),operation,original,null,t));
            }
            if(!close)return new IterationWithdrawalOperationV1(operation,"UNKNOWN");
            var closed=new IterationWithdrawalOperationV1(operation,"CLOSED");
            await journal.Store(WithdrawalJournal,WithdrawalContract,operation,a.ActorId,null,closed,[],new{action="close-uncommitted-iteration-withdrawal",a.ActorId},t);return closed;
        },ct,readOnlyAuthorityFence:!close);
}
