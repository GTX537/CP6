using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using CP6.Core.Services.Integration;
using CP6.Core.Services.Oa;
using Microsoft.EntityFrameworkCore;
using static CP6.Core.Services.Plm.EngineeringProductValuesV1;

namespace CP6.Core.Services.Plm;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record IterationBusinessGrantCommandV1(Guid OperationId,Guid SubmissionId,[property:JsonRequired]Guid? GrantId,[property:JsonRequired]Guid? ExpectedVersion,
    Guid DelegateId,[property:JsonRequired,JsonConverter(typeof(IterationGrantUtcConverterV1))]DateTimeOffset FromUtc,
    [property:JsonRequired,JsonConverter(typeof(IterationGrantUtcConverterV1))]DateTimeOffset ToUtc,string[] Actions,[property:JsonRequired]bool Enabled,string Reason);
public sealed class IterationGrantUtcConverterV1:JsonConverter<DateTimeOffset>
{
    public override DateTimeOffset Read(ref Utf8JsonReader reader,Type type,JsonSerializerOptions options)
    {
        var text=reader.TokenType==JsonTokenType.String?reader.GetString():null;
        if(text==null||!(text.EndsWith('Z')||text.EndsWith("+00:00",StringComparison.Ordinal))||!reader.TryGetDateTimeOffset(out var value))
            throw new JsonException("ITERATION_BUSINESS_GRANT_UTC_REQUIRED");
        return value;
    }
    public override void Write(Utf8JsonWriter writer,DateTimeOffset value,JsonSerializerOptions options)=>writer.WriteStringValue(value);
}
public sealed record IterationBusinessGrantV1(Guid GrantId,string Revision,Guid Version,Guid IterationId,Guid SubmissionId,
    Guid GrantorId,Guid DelegateId,DateTimeOffset FromUtc,DateTimeOffset ToUtc,string[] Actions,bool Enabled,
    Guid OperationId,DateTimeOffset RecordedAtUtc,string Reason);
public sealed record IterationBusinessGrantResultV1(Guid OperationId,IterationBusinessGrantV1 Grant);
public sealed record IterationBusinessGrantOperationV1(Guid OperationId,string State,IterationBusinessGrantResultV1? Result=null);

public sealed partial class IterationReviewOwnerV1
{
    private const string BusinessGrantJournal="Main.Plm.IterationBusinessGrant.operations.v1",BusinessGrantContract="Main.Plm.IterationBusinessGrant.commands.v1";
    private async Task BusinessGrantsReady(CancellationToken ct)
    {
        if(!await db.Database.SqlQueryRaw<bool>("SELECT cp6_iteration_review_control_v1.business_grants_ready() AS \"Value\"").SingleAsync(ct))
            throw Error("ITERATION_BUSINESS_GRANT_UNAVAILABLE",503);
    }
    private async Task<IterationBusinessGrantV1[]> CurrentBusinessGrants(Guid iteration,Guid submission,CancellationToken ct)
    {
        await BusinessGrantsReady(ct);
        var rows=await db.Database.SqlQuery<string>($"""
            SELECT DISTINCT ON(id) body AS "Value" FROM cp6_iteration_review_control_v1.business_grants
            WHERE environment_id={Environment} AND tenant_id={db.CurrentTenantId} AND iteration_id={iteration} AND submission_id={submission}
            ORDER BY id,revision DESC
            """).ToArrayAsync(ct);
        return rows.Select(r=>JsonSerializer.Deserialize<IterationBusinessGrantV1>(r,PersonalFormOwnerV2.Json)!).ToArray();
    }
    private async Task<OperationsAccessV1> BusinessActor(Guid actor,string action,CancellationToken ct)
    {
        Task<OperationsOwnerV1.Authority> Current(string resource,string required,CancellationToken token)
            =>Authority.IterationReviewAuthorityAsync(resource,required,actor,token);
        return new(db,owner,await Current(OperationsOwnerV1.EngineeringIteration,action,ct),storedSubjectSource:Current);
    }
    private async Task BusinessResponsibility(OperationsAccessV1 access,Context c,IterationReviewStoreV1.SubmissionRow row,string action,CancellationToken ct)
    {
        await iterations.ReviewSource(access,row.Iteration,false,ct);access.RequireEngineeringIterationFields(true);
        if(action=="review-withdraw")await WithdrawResponsibility(access,c with{Access=access},row,ct);
        else await DisclosePackage(access,row,false,ct);
    }
    // Business authorization is scoped to this actual Owner's exact submitted iteration. TASK grants
    // are never consulted here, and the principal is obtained only through this Owner's grant.
    private async Task<IterationBusinessGrantV1?> BusinessAct(OperationsAccessV1 actual,Context c,IterationReviewStoreV1.SubmissionRow row,
        string action,Guid? behalf,CancellationToken ct,IterationBusinessGrantV1? exact=null)
    {
        var principal=behalf??actual.ActorId;
        if(principal==actual.ActorId)
        {
            // The caller already read this current business context with the actual actor.
            if(action=="review-withdraw")await WithdrawResponsibility(actual,c,row,ct);
            else actual.RequireEngineeringIterationFields(true);
            return null;
        }
        var grants=await CurrentBusinessGrants(row.Iteration,row.Id,ct);
        var now=await db.Database.SqlQueryRaw<DateTimeOffset>("SELECT clock_timestamp() AS \"Value\"").SingleAsync(ct);
        var grant=grants.Where(g=>g.GrantorId==principal&&g.DelegateId==actual.ActorId&&g.Enabled&&g.FromUtc<=now&&now<g.ToUtc
            &&g.Actions.Contains(action,StringComparer.Ordinal)&&(exact==null||g.GrantId==exact.GrantId&&g.Version==exact.Version))
            .OrderBy(g=>g.GrantId.ToString("D"),StringComparer.Ordinal).FirstOrDefault()??throw Error("ITERATION_BUSINESS_GRANT_DENIED",403);
        actual.RequireEngineeringIterationFields(true);
        var assigned=await BusinessActor(principal,action,ct);await BusinessResponsibility(assigned,c,row,action,ct);
        await ValidateParticipant(assigned,principal,ct);return grant;
    }
    public Task<IterationBusinessGrantV1[]> BusinessGrantsAsync(Guid id,Guid submission,CancellationToken ct=default)
        =>Authority.ExecuteAsync(OperationsOwnerV1.EngineeringIteration,"query",false,async(a,t)=>{
            var c=await ApplicationContextAsync(a,id,submission,false,t);
            return (await CurrentBusinessGrants(id,submission,t)).Where(g=>g.GrantorId==a.ActorId||g.DelegateId==a.ActorId).ToArray();
        },ct);
    public Task<IterationBusinessGrantResultV1> GrantBusinessAsync(Guid id,IterationBusinessGrantCommandV1 input,Guid? behalf=null,CancellationToken ct=default)
    {
        if(input==null||id==Guid.Empty||input.OperationId==Guid.Empty||input.SubmissionId==Guid.Empty||input.DelegateId==Guid.Empty
            ||input.FromUtc.Offset!=TimeSpan.Zero||input.ToUtc.Offset!=TimeSpan.Zero||input.ToUtc<=input.FromUtc
            ||input.Actions is not{Length:>0 and <=2}||input.Actions.Distinct(StringComparer.Ordinal).Count()!=input.Actions.Length
            ||input.Actions.Any(x=>x is not("review-withdraw" or "review-apply"))||input.GrantId.HasValue!=input.ExpectedVersion.HasValue
            ||input.GrantId==Guid.Empty||input.ExpectedVersion==Guid.Empty||!input.Enabled&&!input.GrantId.HasValue)
            throw Error("ITERATION_BUSINESS_GRANT_INPUT_INVALID",400);
        Text(input.Reason,true,1000);var command=OperationsOwnerV1.Freeze(input);var journal=new MainOwnerOperationJournalV1(db);
        MainOwnerOperationJournalV1.Original? original=null;
        var bytes=MainOwnerOperationJournalV1.Encode(new{contract=BusinessGrantContract,environment=Environment,id,behalf,command});
        return Authority.ExecuteAsync(OperationsOwnerV1.EngineeringIteration,"review-delegate",true,async(a,t)=>{
            if(behalf.HasValue&&behalf!=a.ActorId)throw Error("ITERATION_BUSINESS_REDELEGATION_FORBIDDEN",403);
            if(original!=null)
            {
                await ApplicationContextAsync(a,id,command.SubmissionId,false,t);
                return DiscloseBusinessGrant(a,command.OperationId,original,bytes);
            }
            var lease=await scope.AcquireAsync(t);
            if(command.DelegateId==a.ActorId||command.Enabled&&!await db.Sys_Users.AsNoTracking().AnyAsync(u=>u.Id==command.DelegateId&&u.Enable&&(u.LockedUntil==null||u.LockedUntil<=DateTime.Now),t))
                throw Error("ITERATION_BUSINESS_GRANT_TARGET_UNAVAILABLE",422);
            var current=await CurrentBusinessGrants(id,command.SubmissionId,t);
            var old=command.GrantId.HasValue?current.SingleOrDefault(g=>g.GrantId==command.GrantId&&g.GrantorId==a.ActorId):null;
            if(command.GrantId.HasValue&&old==null)throw Error("RESOURCE_NOT_FOUND",404);
            if(old!=null&&old.Version!=command.ExpectedVersion)throw Error("ITERATION_BUSINESS_GRANT_CHANGED",412);
            if(command.Enabled)
            {
                if(current.Any(g=>g.GrantId!=command.GrantId&&g.Enabled&&g.FromUtc<command.ToUtc&&command.FromUtc<g.ToUtc
                    &&(g.DelegateId==a.ActorId||g.GrantorId==command.DelegateId)&&g.Actions.Intersect(command.Actions,StringComparer.Ordinal).Any()))
                    throw Error("ITERATION_BUSINESS_REDELEGATION_FORBIDDEN",403);
            }
            else if(old!.DelegateId!=command.DelegateId||old.FromUtc!=command.FromUtc||old.ToUtc!=command.ToUtc||!old.Actions.SequenceEqual(command.Actions))
                throw Error("ITERATION_BUSINESS_REVOKE_INPUT_CHANGED",409);
            var revision=old==null?1:checked(long.Parse(old.Revision,CultureInfo.InvariantCulture)+1);
            var grant=new IterationBusinessGrantV1(command.GrantId??Guid.NewGuid(),revision.ToString(CultureInfo.InvariantCulture),Guid.NewGuid(),id,
                command.SubmissionId,a.ActorId,command.DelegateId,command.FromUtc,command.ToUtc,command.Actions,command.Enabled,command.OperationId,Now(),command.Reason);
            var result=new IterationBusinessGrantResultV1(command.OperationId,grant);
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO cp6_iteration_review_control_v1.business_grants(environment_id,tenant_id,id,revision,version,iteration_id,submission_id,grantor_id,operation_id,body)
                VALUES({Environment},{db.CurrentTenantId},{grant.GrantId},{revision},{grant.Version},{id},{grant.SubmissionId},{a.ActorId},{command.OperationId},{JsonSerializer.Serialize(grant,PersonalFormOwnerV2.Json)})
                """,t);
            // The grant's own INSERT advances WF_AUTH. Observe business participants after that
            // in the same transaction, so their final leases describe the resulting authority epoch.
            // Any failed business qualification rolls back this tentative revision and epoch.
            var c=await ApplicationContextAsync(a,id,command.SubmissionId,false,t);
            if(command.Enabled)foreach(var action in command.Actions)
            {
                var own=await BusinessActor(a.ActorId,action,t);await BusinessResponsibility(own,c.Context,c.Submission,action,t);
                a.AtCommit(token=>ValidateParticipant(own,a.ActorId,token));
            }
            await journal.Store(BusinessGrantJournal,BusinessGrantContract,command.OperationId,a.ActorId,bytes,result,[id],new{command,grant,authorities=Witnesses(a)},t);
            a.AtCommit(async token=>{
                var final=await scope.ReadLocked(token)??throw Error("SCOPE_MAPPING_REQUIRED",503);
                if(final.Epoch!=lease.Snapshot.Epoch+1||(final with{Epoch=lease.Snapshot.Epoch,RowVersion=lease.Snapshot.RowVersion})!=lease.Snapshot)
                    throw Error("SCOPE_AUTHORITY_CHANGED",409);
                await BusinessGrantsReady(token);
            });return result;
        },ct,actionAtFence:async t=>{original=await journal.Read(BusinessGrantJournal,command.OperationId,t);return original==null?"review-delegate":"query";});
    }
    private static IterationBusinessGrantResultV1 DiscloseBusinessGrant(OperationsAccessV1 a,Guid operation,MainOwnerOperationJournalV1.Original original,byte[]? bytes)
    {
        if(original.Actor!=a.ActorId||original.Contract!=BusinessGrantContract)throw Error("ORIGINAL_OPERATION_FORBIDDEN",403);
        if(original.State=="CLOSED")throw Error("ORIGINAL_OPERATION_CLOSED",409);
        if(bytes!=null&&!MainOwnerOperationJournalV1.Matches(original,bytes))throw Error("ORIGINAL_OPERATION_INPUT_CONFLICT",409);
        return JsonSerializer.Deserialize<IterationBusinessGrantResultV1>(original.Result)!;
    }
    public Task<IterationBusinessGrantOperationV1> BusinessGrantOperationAsync(Guid operation,bool close,CancellationToken ct=default)
        =>Authority.ExecuteAsync(OperationsOwnerV1.EngineeringIteration,"query",close,async(a,t)=>{
            if(operation==Guid.Empty)throw Error("ORIGINAL_OPERATION_REQUIRED",400);
            await scope.Root(t);var journal=new MainOwnerOperationJournalV1(db);var original=await journal.Read(BusinessGrantJournal,operation,t);
            if(original!=null)
            {
                if(original.Actor!=a.ActorId||original.Contract!=BusinessGrantContract)throw Error("ORIGINAL_OPERATION_FORBIDDEN",403);
                if(original.State=="CLOSED")return new IterationBusinessGrantOperationV1(operation,"CLOSED");
                var result=DiscloseBusinessGrant(a,operation,original,null);
                await ApplicationContextAsync(a,result.Grant.IterationId,result.Grant.SubmissionId,false,t);
                return new IterationBusinessGrantOperationV1(operation,"COMMITTED",result);
            }
            if(!close)return new IterationBusinessGrantOperationV1(operation,"UNKNOWN");
            var closed=new IterationBusinessGrantOperationV1(operation,"CLOSED");
            await journal.Store(BusinessGrantJournal,BusinessGrantContract,operation,a.ActorId,null,closed,[],new{action="close-uncommitted-business-grant",a.ActorId},t);return closed;
        },ct,readOnlyAuthorityFence:!close);
}
