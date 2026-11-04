using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using CP6.Core.Services.Integration;
using CP6.Core.Services.Oa;
using Microsoft.EntityFrameworkCore;
using static CP6.Core.Services.Wms.ToolInstanceStoreV1;

namespace CP6.Core.Services.Wms;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ToolBusinessGrantCommandV1(Guid OperationId,Guid SubmissionId,[property:JsonRequired]Guid? GrantId,[property:JsonRequired]Guid? ExpectedVersion,
    Guid DelegateId,[property:JsonRequired,JsonConverter(typeof(ToolGrantUtcConverterV1))]DateTimeOffset FromUtc,
    [property:JsonRequired,JsonConverter(typeof(ToolGrantUtcConverterV1))]DateTimeOffset ToUtc,string[] Actions,[property:JsonRequired]bool Enabled,string Reason);
public sealed class ToolGrantUtcConverterV1:JsonConverter<DateTimeOffset>
{
    public override DateTimeOffset Read(ref Utf8JsonReader reader,Type type,JsonSerializerOptions options)
    {
        var text=reader.TokenType==JsonTokenType.String?reader.GetString():null;
        if(text==null||!(text.EndsWith('Z')||text.EndsWith("+00:00",StringComparison.Ordinal))||!reader.TryGetDateTimeOffset(out var value))
            throw new JsonException("TOOL_BUSINESS_GRANT_UTC_REQUIRED");
        return value;
    }
    public override void Write(Utf8JsonWriter writer,DateTimeOffset value,JsonSerializerOptions options)=>writer.WriteStringValue(value);
}
public sealed record ToolBusinessGrantV1(Guid GrantId,string Revision,Guid Version,Guid ToolId,Guid SubmissionId,
    Guid GrantorId,Guid DelegateId,DateTimeOffset FromUtc,DateTimeOffset ToUtc,string[] Actions,bool Enabled,
    Guid OperationId,DateTimeOffset RecordedAtUtc,string Reason);
public sealed record ToolBusinessGrantResultV1(Guid OperationId,ToolBusinessGrantV1 Grant);
public sealed record ToolBusinessGrantOperationV1(Guid OperationId,string State,ToolBusinessGrantResultV1? Result=null);

public sealed partial class ToolDispositionApprovalOwnerV1
{
    private const string BusinessGrantJournal="Main.Wms.ToolBusinessGrant.operations.v1",BusinessGrantContract="Main.Wms.ToolBusinessGrant.commands.v1";
    private async Task BusinessGrantsReady(CancellationToken ct)
    {
        if(!await db.Database.SqlQueryRaw<bool>("SELECT cp6_tool_approval_control_v1.business_grants_ready() AS \"Value\"").SingleAsync(ct))
            throw Error("TOOL_BUSINESS_GRANT_UNAVAILABLE",503);
    }
    private async Task<ToolBusinessGrantV1[]> CurrentBusinessGrants(Guid tool,Guid submission,CancellationToken ct)
    {
        await BusinessGrantsReady(ct);
        var rows=await db.Database.SqlQuery<string>($"""
            SELECT DISTINCT ON(id) body AS "Value" FROM cp6_tool_approval_control_v1.business_grants
            WHERE environment_id={Environment} AND tenant_id={db.CurrentTenantId} AND tool_id={tool} AND submission_id={submission}
            ORDER BY id,revision DESC
            """).ToArrayAsync(ct);
        return rows.Select(r=>JsonSerializer.Deserialize<ToolBusinessGrantV1>(r,PersonalFormOwnerV2.Json)!).ToArray();
    }
    private async Task<OperationsAccessV1> BusinessActor(Guid actor,string action,CancellationToken ct)
    {
        Task<OperationsOwnerV1.Authority> Current(string resource,string required,CancellationToken token)
            =>Authority.ToolWithdrawalAuthorityAsync(resource,required,actor,token);
        return new(db,owner,await Current(OperationsOwnerV1.ToolInstance,action,ct),storedSubjectSource:Current);
    }
    private async Task BusinessResponsibility(OperationsAccessV1 access,Context c,ToolDispositionApprovalStoreV1.SubmissionRow row,string action,CancellationToken ct)
    {
        // Withdrawal changes the approval control, not tool content or physical restrictions.
        // Reuse the current caller's context. A principal or final observation has its own
        // authority and therefore reads the source through that participant's boundary.
        var source=ReferenceEquals(access,c.Access)?c:await Prepare(access,row.Tool,false,ct);access.RequireToolFieldRead("Remarks");
        if(source.Disposition.Request?.Reference.Id!=row.Request)throw Error("TOOL_APPROVAL_SOURCE_CHANGED",409);
    }
    // Business authorization is scoped to this actual Owner's exact submitted tool. TASK grants
    // are never consulted here, and the principal is obtained only through this Owner's grant.
    private async Task<ToolBusinessGrantV1?> BusinessAct(OperationsAccessV1 actual,Context c,ToolDispositionApprovalStoreV1.SubmissionRow row,
        string action,Guid? behalf,CancellationToken ct,ToolBusinessGrantV1? exact=null)
    {
        var principal=behalf??actual.ActorId;
        if(principal==actual.ActorId)
        {
            // The caller already read this current business context with the actual actor.
            return null;
        }
        var grants=await CurrentBusinessGrants(row.Tool,row.Id,ct);
        var now=await db.Database.SqlQueryRaw<DateTimeOffset>("SELECT clock_timestamp() AS \"Value\"").SingleAsync(ct);
        var grant=grants.Where(g=>g.GrantorId==principal&&g.DelegateId==actual.ActorId&&g.Enabled&&g.FromUtc<=now&&now<g.ToUtc
            &&g.Actions.Contains(action,StringComparer.Ordinal)&&(exact==null||g.GrantId==exact.GrantId&&g.Version==exact.Version))
            .OrderBy(g=>g.GrantId.ToString("D"),StringComparer.Ordinal).FirstOrDefault()??throw Error("TOOL_BUSINESS_GRANT_DENIED",403);
        var assigned=await BusinessActor(principal,action,ct);await BusinessResponsibility(assigned,c,row,action,ct);
        await ValidateParticipant(assigned,principal,ct);return grant;
    }
    public Task<ToolBusinessGrantV1[]> BusinessGrantsAsync(Guid id,Guid submission,CancellationToken ct=default)
        =>Authority.ExecuteAsync(OperationsOwnerV1.ToolInstance,"query",false,async(a,t)=>{
            var c=await WithdrawalContextOwned(a,id,submission,false,t);
            return (await CurrentBusinessGrants(id,submission,t)).Where(g=>g.GrantorId==a.ActorId||g.DelegateId==a.ActorId).ToArray();
        },ct);
    public sealed record BusinessDelegateV1(Guid Id,string Name);
    public Task<BusinessDelegateV1[]> BusinessDelegatesAsync(Guid id,CancellationToken ct=default)
        =>Authority.ExecuteAsync(OperationsOwnerV1.ToolInstance,"dispose-delegate",false,async(a,t)=>{
            await Prepare(a,id,false,t);
            return await db.Sys_Users.AsNoTracking().Where(u=>u.Enable&&u.Id!=a.ActorId&&(u.LockedUntil==null||u.LockedUntil<=DateTime.Now))
                .OrderBy(u=>u.UserName).ThenBy(u=>u.Id).Select(u=>new BusinessDelegateV1(u.Id,u.UserName)).ToArrayAsync(t);
        },ct);
    public Task<ToolBusinessGrantResultV1> GrantBusinessAsync(Guid id,ToolBusinessGrantCommandV1 input,Guid? behalf=null,CancellationToken ct=default)
    {
        if(input==null||id==Guid.Empty||input.OperationId==Guid.Empty||input.SubmissionId==Guid.Empty||input.DelegateId==Guid.Empty
            ||input.FromUtc.Offset!=TimeSpan.Zero||input.ToUtc.Offset!=TimeSpan.Zero||input.ToUtc<=input.FromUtc
            ||input.Actions is not{Length:1}||input.Actions[0]!="dispose-withdraw"||input.GrantId.HasValue!=input.ExpectedVersion.HasValue
            ||input.GrantId==Guid.Empty||input.ExpectedVersion==Guid.Empty||!input.Enabled&&!input.GrantId.HasValue)
            throw Error("TOOL_BUSINESS_GRANT_INPUT_INVALID",400);
        Text(input.Reason,500);var command=OperationsOwnerV1.Freeze(input);var journal=new MainOwnerOperationJournalV1(db);
        MainOwnerOperationJournalV1.Original? original=null;
        var bytes=MainOwnerOperationJournalV1.Encode(new{contract=BusinessGrantContract,environment=Environment,id,behalf,command});
        return Authority.ExecuteAsync(OperationsOwnerV1.ToolInstance,"dispose-delegate",true,async(a,t)=>{
            if(behalf.HasValue&&behalf!=a.ActorId)throw Error("TOOL_BUSINESS_REDELEGATION_FORBIDDEN",403);
            if(original!=null)
            {
                await WithdrawalContextOwned(a,id,command.SubmissionId,false,t);
                return DiscloseBusinessGrant(a,command.OperationId,original,bytes);
            }
            var lease=await scope.AcquireAsync(t);
            if(command.DelegateId==a.ActorId||command.Enabled&&!await db.Sys_Users.AsNoTracking().AnyAsync(u=>u.Id==command.DelegateId&&u.Enable&&(u.LockedUntil==null||u.LockedUntil<=DateTime.Now),t))
                throw Error("TOOL_BUSINESS_GRANT_TARGET_UNAVAILABLE",422);
            var current=await CurrentBusinessGrants(id,command.SubmissionId,t);
            var old=command.GrantId.HasValue?current.SingleOrDefault(g=>g.GrantId==command.GrantId&&g.GrantorId==a.ActorId):null;
            if(command.GrantId.HasValue&&old==null)throw Error("RESOURCE_NOT_FOUND",404);
            if(old!=null&&old.Version!=command.ExpectedVersion)throw Error("TOOL_BUSINESS_GRANT_CHANGED",412);
            if(command.Enabled)
            {
                if(current.Any(g=>g.GrantId!=command.GrantId&&g.Enabled&&g.FromUtc<command.ToUtc&&command.FromUtc<g.ToUtc
                    &&(g.DelegateId==a.ActorId||g.GrantorId==command.DelegateId)&&g.Actions.Intersect(command.Actions,StringComparer.Ordinal).Any()))
                    throw Error("TOOL_BUSINESS_REDELEGATION_FORBIDDEN",403);
            }
            else if(old!.DelegateId!=command.DelegateId||old.FromUtc!=command.FromUtc||old.ToUtc!=command.ToUtc||!old.Actions.SequenceEqual(command.Actions))
                throw Error("TOOL_BUSINESS_REVOKE_INPUT_CHANGED",409);
            var revision=old==null?1:checked(long.Parse(old.Revision,CultureInfo.InvariantCulture)+1);
            var grant=new ToolBusinessGrantV1(command.GrantId??Guid.NewGuid(),revision.ToString(CultureInfo.InvariantCulture),Guid.NewGuid(),id,
                command.SubmissionId,a.ActorId,command.DelegateId,command.FromUtc,command.ToUtc,command.Actions,command.Enabled,command.OperationId,Now(),command.Reason);
            var result=new ToolBusinessGrantResultV1(command.OperationId,grant);
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO cp6_tool_approval_control_v1.business_grants(environment_id,tenant_id,id,revision,version,tool_id,submission_id,grantor_id,operation_id,body)
                VALUES({Environment},{db.CurrentTenantId},{grant.GrantId},{revision},{grant.Version},{id},{grant.SubmissionId},{a.ActorId},{command.OperationId},{JsonSerializer.Serialize(grant,PersonalFormOwnerV2.Json)})
                """,t);
            // The grant's own INSERT advances WF_AUTH. Observe business participants after that
            // in the same transaction, so their final leases describe the resulting authority epoch.
            // Any failed business qualification rolls back this tentative revision and epoch.
            var c=await WithdrawalContextOwned(a,id,command.SubmissionId,false,t);
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
        },ct,actionAtFence:async t=>{original=await journal.Read(BusinessGrantJournal,command.OperationId,t);return original==null?"dispose-delegate":"query";});
    }
    private static ToolBusinessGrantResultV1 DiscloseBusinessGrant(OperationsAccessV1 a,Guid operation,MainOwnerOperationJournalV1.Original original,byte[]? bytes)
    {
        if(original.Actor!=a.ActorId||original.Contract!=BusinessGrantContract)throw Error("ORIGINAL_OPERATION_FORBIDDEN",403);
        if(original.State=="CLOSED")throw Error("ORIGINAL_OPERATION_CLOSED",409);
        if(bytes!=null&&!MainOwnerOperationJournalV1.Matches(original,bytes))throw Error("ORIGINAL_OPERATION_INPUT_CONFLICT",409);
        return JsonSerializer.Deserialize<ToolBusinessGrantResultV1>(original.Result)!;
    }
    public Task<ToolBusinessGrantOperationV1> BusinessGrantOperationAsync(Guid operation,bool close,CancellationToken ct=default)
        =>Authority.ExecuteAsync(OperationsOwnerV1.ToolInstance,"query",close,async(a,t)=>{
            if(operation==Guid.Empty)throw Error("ORIGINAL_OPERATION_REQUIRED",400);
            await scope.Root(t);var journal=new MainOwnerOperationJournalV1(db);var original=await journal.Read(BusinessGrantJournal,operation,t);
            if(original!=null)
            {
                if(original.Actor!=a.ActorId||original.Contract!=BusinessGrantContract)throw Error("ORIGINAL_OPERATION_FORBIDDEN",403);
                if(original.State=="CLOSED")return new ToolBusinessGrantOperationV1(operation,"CLOSED");
                var result=DiscloseBusinessGrant(a,operation,original,null);
                await WithdrawalContextOwned(a,result.Grant.ToolId,result.Grant.SubmissionId,false,t);
                return new ToolBusinessGrantOperationV1(operation,"COMMITTED",result);
            }
            if(!close)return new ToolBusinessGrantOperationV1(operation,"UNKNOWN");
            var closed=new ToolBusinessGrantOperationV1(operation,"CLOSED");
            await journal.Store(BusinessGrantJournal,BusinessGrantContract,operation,a.ActorId,null,closed,[],new{action="close-uncommitted-business-grant",a.ActorId},t);return closed;
        },ct,readOnlyAuthorityFence:!close);
}
