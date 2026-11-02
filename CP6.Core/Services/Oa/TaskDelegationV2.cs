using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Sys;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Oa;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record TaskDelegationScopeV2(string[] ActionKinds,string[]? FlowKeys=null,string[]? BizTypes=null);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record TaskDelegationInputV2(Guid DelegateId,DateTimeOffset ValidFromUtc,DateTimeOffset ValidToUtc,
    TaskDelegationScopeV2 Scope,string Remark);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record TaskDelegationRevokeV2(string Reason);
public sealed record TaskDelegationRevisionV2(Guid GrantId,string Revision,Guid Version,Guid GrantorId,Guid DelegateId,
    DateTimeOffset FromUtc,DateTimeOffset ToUtc,TaskDelegationScopeV2 Scope,bool Enabled,string RevocationEpoch,
    DateTimeOffset GrantedAtUtc,Guid GrantedBy,DateTimeOffset? RevokedAtUtc,Guid? RevokedBy,string Reason,string SourceRef);
public sealed record TaskDelegationCommandV2(string CommandId,string State,TaskDelegationRevisionV2? Result,string QueryRef);
public sealed record TaskDelegationListV2(TaskDelegationRevisionV2[] Granted,TaskDelegationRevisionV2[] Received,
    Guid ActualUserId,DateTimeOffset ObservedAtUtc,string LegacyState="LEGACY_SCOPE_TIME_UNVERIFIED");
public sealed record TaskDelegationPrincipalV2(Guid UserId,string Name,TaskDelegationRevisionV2[] Grants);
public sealed record TaskDelegationTargetV2(Guid Id,string Name);

/// <summary>Participant in the caller's existing WF_AUTH transaction. One complete grant is selected;
/// it never grants business application/withdrawal or changes a task's actual responsibility.</summary>
internal sealed class TaskDelegationReaderV2(CP6Context db,Guid environment)
{
    internal static readonly string[] Actions=["TASK_READ","TASK_APPROVE","TASK_REJECT","TASK_SENDBACK","TASK_TRANSFER"];
    internal static readonly JsonSerializerOptions Json=PersonalFormOwnerV2.Json;
    private static ScopeAuthorizationExceptionV1 Error(string code,int status=403)=>new(code,status);
    internal static void Validate(TaskDelegationScopeV2 scope)
    {
        static bool Keys(string[]? keys)=>keys is {Length:>0 and <=100} && keys.All(k=>!string.IsNullOrWhiteSpace(k)&&k.Length<=100)
            && keys.Distinct(StringComparer.Ordinal).Count()==keys.Length;
        if(scope==null || !Keys(scope.ActionKinds) || scope.ActionKinds.Any(a=>!Actions.Contains(a,StringComparer.Ordinal))
            || !scope.ActionKinds.Contains("TASK_READ",StringComparer.Ordinal)
            || scope.FlowKeys==null&&scope.BizTypes==null || scope.FlowKeys!=null&&!Keys(scope.FlowKeys) || scope.BizTypes!=null&&!Keys(scope.BizTypes))
            throw Error("GRANT_SCOPE_INVALID",400);
    }
    internal static bool Covers(TaskDelegationScopeV2 scope,string action,string flow,string? biz)
        =>scope.ActionKinds.Contains(action,StringComparer.Ordinal)
            && (scope.FlowKeys==null||scope.FlowKeys.Contains(flow,StringComparer.Ordinal))
            && (scope.BizTypes==null||biz!=null&&scope.BizTypes.Contains(biz,StringComparer.Ordinal));
    internal static bool Overlaps(TaskDelegationScopeV2 a,TaskDelegationScopeV2 b)
        =>a.ActionKinds.Intersect(b.ActionKinds,StringComparer.Ordinal).Any()
            && (a.FlowKeys==null||b.FlowKeys==null||a.FlowKeys.Intersect(b.FlowKeys,StringComparer.Ordinal).Any())
            && (a.BizTypes==null||b.BizTypes==null||a.BizTypes.Intersect(b.BizTypes,StringComparer.Ordinal).Any());
    internal async Task Ready(CancellationToken ct)
    {
        if(!await db.Database.SqlQueryRaw<bool>("SELECT cp6_task_delegation_v2.ready() AS \"Value\"").SingleAsync(ct))throw Error("GRANT_AUTHORITY_UNAVAILABLE",503);
    }
    internal Task<DateTimeOffset> Now(CancellationToken ct)=>db.Database.SqlQueryRaw<DateTimeOffset>("SELECT clock_timestamp() AS \"Value\"").SingleAsync(ct);
    internal async Task<TaskDelegationRevisionV2[]> Current(CancellationToken ct)
    {
        var rows=await db.Database.SqlQuery<string>($"""
            SELECT r.body AS "Value" FROM cp6_task_delegation_v2.heads h JOIN cp6_task_delegation_v2.revisions r
              ON (r.tenant_id,r.environment_id,r.grant_id,r.revision)=(h.tenant_id,h.environment_id,h.id,h.revision)
            WHERE h.tenant_id={db.CurrentTenantId} AND h.environment_id={environment}
            """).ToArrayAsync(ct);
        return rows.Select(x=>JsonSerializer.Deserialize<TaskDelegationRevisionV2>(x,Json)!).ToArray();
    }
    internal async Task<TaskDelegationRevisionV2?> Match(Guid actual,Guid principal,string action,string flow,string? biz,CancellationToken ct)
    {
        var now=await Now(ct);
        return (await Current(ct)).Where(g=>g.Enabled&&g.GrantorId==principal&&g.DelegateId==actual&&g.FromUtc<=now&&now<g.ToUtc
            && Covers(g.Scope,action,flow,biz)).OrderBy(g=>g.GrantId.ToString("D"),StringComparer.Ordinal).FirstOrDefault();
    }
    internal async Task<TaskDelegationRevisionV2> Require(Guid actual,Guid principal,string action,string flow,string? biz,CancellationToken ct)
    {
        await Ready(ct);
        var grant=await Match(actual,principal,action,flow,biz,ct);
        if(grant!=null)return grant;
        if(await db.Wf_FlowDelegates.AsNoTracking().AnyAsync(x=>x.GrantorId==principal&&x.DelegateId==actual,ct))
            throw Error("GRANT_SCOPE_UNVERIFIED");
        throw Error("GRANT_SCOPE_DENIED");
    }
    internal async Task ValidateExact(TaskDelegationRevisionV2 grant,string action,string flow,string? biz,CancellationToken ct)
    {
        // Immutable selected revision remains the authority. A newly added lower-id grant cannot
        // silently replace it; expiry is checked again at the actual final decision point.
        await Ready(ct);var current=(await Current(ct)).SingleOrDefault(g=>g.GrantId==grant.GrantId);var now=await Now(ct);
        if(current==null||current.Version!=grant.Version||!current.Enabled||now<current.FromUtc||now>=current.ToUtc
            ||!Covers(current.Scope,action,flow,biz))throw Error("GRANT_AUTHORITY_CHANGED");
    }
    internal static long Revision(string value)=>long.Parse(value,NumberStyles.None,CultureInfo.InvariantCulture);
}
