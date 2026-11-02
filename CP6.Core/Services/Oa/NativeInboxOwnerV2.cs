using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Sys;
using CP6.Core.Services.Wf;
using CP6.Entity.DomainModels.Wf;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Oa;

/// <summary>Current read authority, independent of historical participation and decision eligibility.
/// Native reads never mutate task versions or use legacy detail/list fallbacks.</summary>
public sealed partial class NativeInboxOwnerV2(CP6Context db,ScopeAuthorizationFenceV1 fence,
    PersonalFormOwnerV2 forms,ApprovalBusinessEvidenceV2 businesses)
{
    private static readonly JsonSerializerOptions Json=PersonalFormOwnerV2.Json;
    private static ScopeAuthorizationExceptionV1 Error(string code,int status=503)=>new(code,status);
    private static T Parse<T>(string value)=>JsonSerializer.Deserialize<T>(value,Json)!;
    private sealed record Member(Guid InstanceId,Guid Root,string Kind,Guid FlowVersion,byte[]? Payload,string? DetailRoute);
    private sealed record Content(NativeFormContentV2? Form,NativeBusinessReadV2? Business);
    private sealed record Dataset(Member[] Members,Wf_FlowInstance[] Instances,Wf_FlowTask[] Tasks,Wf_FlowCc[] Cc,
        Dictionary<Guid,Content> Content,NativeReadFactV2[] Reads,NativeVoteReadV2[] Votes);
    private IQueryable<Member> Members()=>db.Database.SqlQuery<Member>($"""
        SELECT x.instance_id AS "InstanceId",x.root_instance_id AS "Root",'APP' AS "Kind",x.flow_version AS "FlowVersion",s.payload AS "Payload",r.body::jsonb->>'detailRoute' AS "DetailRoute"
        FROM cp6_approval_runtime_v2.instances x JOIN cp6_approval_intake_v2.submissions s
          ON s.tenant_id=x.tenant_id AND s.environment_id=x.environment_id AND s.owner=x.owner AND s.id=x.submission_id
        JOIN cp6_approval_binding_v2.revisions r ON r.tenant_id=s.tenant_id AND r.environment_id=s.environment_id AND r.binding_id=s.binding_id AND r.id=s.binding_revision
        WHERE x.tenant_id={db.CurrentTenantId} AND x.environment_id={fence.EnvironmentId}
        UNION ALL
        SELECT instance_id,root_id,'SFS',flow_version,NULL::bytea,NULL::text FROM cp6_personal_form_v2.instances
        WHERE tenant_id={db.CurrentTenantId} AND environment_id={fence.EnvironmentId}
        """);
    private async Task<(UserPermissionContext Actor,ScopeAuthorizationLeaseV1 Lease)> Begin(Guid? behalf,CancellationToken ct)
    {
        var lease=await forms.ReadLease(ct);var actor=await forms.Actor(ct);await forms.Action(actor,"query",ct,"oa-inbox");
        // New read contract does not reinterpret legacy empty/FlowKey grants as TASK_READ scopes.
        // The accepted legacy decision endpoints retain their own current grant contract.
        if(behalf.HasValue && behalf!=actor.UserId)throw Error("DELEGATED_READ_OWNER_NOT_ADOPTED",422);
        return(actor,lease);
    }
    private async Task<Content> Authorize(Member member,UserPermissionContext actor,CancellationToken ct)
    {
        if(member.Kind=="SFS")return new(await forms.ReadSubmissionAsync(member.Root,actor,ct),null);
        // Immutable local intake already owns envelope validation. Do not hash/reparse its wire proof again.
        var original=JsonSerializer.Deserialize<ApprovalSubmitCommandV2>(member.Payload!,Json)!;
        try{await businesses.Decision(original,null,false,ct);}
        catch(CP6.Core.Services.Erp.OrderPriceCorrectionException e){throw new ScopeAuthorizationExceptionV1(e.Message,e.Status,e);}
        var subject=original.Subject;
        var route=subject.BusinessOwner switch
        {
            "Main.Plm.ControlledDocument.v1"=>$"/plm/controlled-document/{Uri.EscapeDataString(subject.SubjectId)}?submission={original.SubmissionId}",
            _=>member.DetailRoute?.Replace("{bizId}",Uri.EscapeDataString(subject.SubjectId),StringComparison.Ordinal)
        };
        return new(null,new(subject.BusinessOwner,subject.BizType,subject.SubjectKind,subject.SubjectId,
            subject.BusinessRevision,original.SubmissionId,route));
    }
    private async Task<NativeReadFactV2[]> Reads(Guid[] instances,CancellationToken ct)=>await db.Database.SqlQuery<NativeReadFactV2>($"""
        SELECT kind AS "Kind",target_id AS "TargetId",principal_id AS "PrincipalId",actual_id AS "ActualUserId",
          first_read_at AS "FirstReadAtUtc",first_command_key AS "FirstCommandId"
        FROM cp6_workflow_read_v2.facts WHERE tenant_id={db.CurrentTenantId} AND environment_id={fence.EnvironmentId} AND instance_id=ANY({instances})
        """).ToArrayAsync(ct);
    private async Task<NativeVoteReadV2[]> Votes(Guid[] instances,CancellationToken ct)
    {
        var app=await db.Database.SqlQuery<string>($"""
            SELECT body AS "Value" FROM cp6_approval_decision_v2.votes
            WHERE tenant_id={db.CurrentTenantId} AND environment_id={fence.EnvironmentId} AND instance_id=ANY({instances})
            """).ToArrayAsync(ct);
        var result=app.Select(x=>Parse<ApprovalVoteV2>(x)).Select(v=>new NativeVoteReadV2(v.Id,v.TaskId,v.InstanceId,v.Approved,
            v.Comment,v.Authority.PrincipalId,v.Authority.ActualHandlerId,v.Authority.OnBehalfOfId,v.OccurredAt)).ToList();
        var ordinary=await db.Database.SqlQuery<FormVote>($"""
            SELECT v.id AS "Id",v.task_id AS "Task",a.instance_id AS "Instance",v.body AS "Body"
            FROM cp6_personal_form_v2.votes v JOIN cp6_personal_form_v2.assignments a ON a.task_id=v.task_id
            JOIN cp6_personal_form_v2.instances x ON x.instance_id=a.instance_id
            WHERE x.tenant_id={db.CurrentTenantId} AND x.environment_id={fence.EnvironmentId} AND x.instance_id=ANY({instances})
            """).ToArrayAsync(ct);
        foreach(var v in ordinary)
        {
            using var doc=JsonDocument.Parse(v.Body);var b=doc.RootElement;
            result.Add(new(v.Id,v.Task,v.Instance,b.GetProperty("approved").GetBoolean(),b.GetProperty("comment").GetString()!,
                b.GetProperty("principal").GetGuid(),b.GetProperty("actual").GetGuid(),
                b.GetProperty("onBehalfOf").ValueKind==JsonValueKind.Null?null:b.GetProperty("onBehalfOf").GetGuid(),b.GetProperty("occurred").GetDateTimeOffset()));
        }
        return result.OrderBy(x=>x.OccurredAtUtc).ThenBy(x=>x.VoteId).ToArray();
    }
    private sealed record FormVote(Guid Id,Guid Task,Guid Instance,string Body);
    private async Task<Guid[]> Participating(Guid actor,CancellationToken ct)
    {
        var native=Members().Select(x=>x.InstanceId);
        // Principal assignment and recorded actual votes are distinct participation facts.
        var actual=await db.Database.SqlQuery<Guid>($"""
            SELECT instance_id AS "Value" FROM cp6_approval_decision_v2.votes
            WHERE tenant_id={db.CurrentTenantId} AND environment_id={fence.EnvironmentId} AND actual_id={actor}
            UNION
            SELECT a.instance_id FROM cp6_personal_form_v2.votes v JOIN cp6_personal_form_v2.assignments a ON a.task_id=v.task_id
            JOIN cp6_personal_form_v2.instances x ON x.instance_id=a.instance_id
            WHERE x.tenant_id={db.CurrentTenantId} AND x.environment_id={fence.EnvironmentId} AND v.body::jsonb->>'actual'={actor.ToString("D")}
            """).ToArrayAsync(ct);
        var ids=db.Wf_FlowInstances.Where(i=>i.StarterId==actor).Select(i=>i.Id)
            .Union(db.Wf_FlowTasks.Where(t=>t.AssigneeId==actor).Select(t=>t.InstanceId))
            .Union(db.Wf_FlowCcs.Where(c=>c.RecipientId==actor).Select(c=>c.InstanceId));
        return (await ids.Where(id=>native.Contains(id)).ToArrayAsync(ct)).Union(actual).ToArray();
    }
    private async Task<Dataset> Load(UserPermissionContext actor,CancellationToken ct)
    {
        var participating=await Participating(actor.UserId,ct);
        var members=await Members().Where(m=>participating.Contains(m.InstanceId)).ToArrayAsync(ct);
        var content=new Dictionary<Guid,Content>();var allowed=new List<Member>();
        foreach(var root in members.GroupBy(m=>m.Root))
        {
            try{content.Add(root.Key,await Authorize(root.First(),actor,ct));allowed.AddRange(root);}
            catch(ScopeAuthorizationExceptionV1 e)when(e.Status is 403 or 404){ /* Current denial excludes the row before all aggregates/search. */ }
        }
        members=allowed.ToArray();var ids=members.Select(m=>m.InstanceId).ToArray();
        return new(members,await db.Wf_FlowInstances.AsNoTracking().Where(i=>ids.Contains(i.Id)).ToArrayAsync(ct),
            await db.Wf_FlowTasks.AsNoTracking().Where(t=>ids.Contains(t.InstanceId)).ToArrayAsync(ct),
            await db.Wf_FlowCcs.AsNoTracking().Where(c=>ids.Contains(c.InstanceId)).ToArrayAsync(ct),content,await Reads(ids,ct),await Votes(ids,ct));
    }
    private static NativeInboxItemV2[] Rows(Dataset data,Guid actor,string kind)
    {
        var result=new List<NativeInboxItemV2>();
        foreach(var i in data.Instances)
        {
            var m=data.Members.Single(m=>m.InstanceId==i.Id);var source=data.Content[m.Root];
            NativeInboxItemV2 Row(Guid[] tasks,Guid[] cc)=>new(i.Id,m.Root,m.Kind,i.FlowKey,i.Status,source.Form?.Title,
                source.Business?.BusinessType,tasks,cc,tasks.Count(t=>!data.Reads.Any(r=>r.Kind=="TASK"&&r.TargetId==t))
                    +cc.Count(c=>!data.Reads.Any(r=>r.Kind=="CC"&&r.TargetId==c)),i.CreateDate);
            if(kind=="running")
            {if(i.Status is 0 or 4)result.Add(Row([],[]));continue;}
            if(kind=="cc")
            {result.AddRange(data.Cc.Where(c=>c.InstanceId==i.Id&&c.RecipientId==actor).Select(c=>Row([],[c.Id])));continue;}
            foreach(var t in data.Tasks.Where(t=>t.InstanceId==i.Id))
            {
                var mine=t.AssigneeId==actor;var handled=data.Votes.Any(v=>v.TaskId==t.Id&&v.ActualUserId==actor);
                if(kind=="pending"&&mine&&t.Status==0&&i.Status==0 || kind=="done"&&(mine||handled)&&t.Status is 1 or 2 or 3)
                    result.Add(Row([t.Id],[]));
            }
        }
        return result.ToArray();
    }
    public async Task<NativeInboxPageV2> ListAsync(string kind,int page=1,int pageSize=20,string rowMode="expanded",string search="",Guid? behalf=null,CancellationToken ct=default)
    {
        if(kind is not("pending" or "cc" or "running" or "done") || rowMode is not("expanded" or "merged")
            || page<1 || page>100000 || pageSize<1 || pageSize>100 || search.Length>200)throw Error("INBOX_QUERY_INVALID",400);
        forms.Entry();await using var tx=await db.Database.BeginTransactionAsync(ct);var(actor,lease)=await Begin(behalf,ct);var data=await Load(actor,ct);
        var rows=Rows(data,actor.UserId,kind).Where(r=>search.Length==0||r.FlowKey.Contains(search,StringComparison.OrdinalIgnoreCase)
            ||r.Title?.Contains(search,StringComparison.OrdinalIgnoreCase)==true||r.BusinessType?.Contains(search,StringComparison.OrdinalIgnoreCase)==true).ToArray();
        var groups=rows.GroupBy(r=>r.InstanceId).ToArray();
        var visible=rowMode=="expanded"?rows:groups.Select(g=>g.First() with{TaskIds=g.SelectMany(x=>x.TaskIds).ToArray(),CcIds=g.SelectMany(x=>x.CcIds).ToArray(),UnreadCount=g.Sum(x=>x.UnreadCount)}).ToArray();
        var result=new NativeInboxPageV2(visible.OrderByDescending(x=>x.CreatedAt).ThenBy(x=>x.InstanceId).ThenBy(x=>x.TaskIds.FirstOrDefault()).ThenBy(x=>x.CcIds.FirstOrDefault())
            .Skip((page-1)*pageSize).Take(pageSize).ToArray(),rows.Length,groups.Length,page,pageSize,rowMode,actor.UserId,actor.UserId);
        await forms.Final(actor,lease,ct,false);return result;
    }
    public async Task<NativeInboxCountsV2> CountsAsync(Guid? behalf=null,CancellationToken ct=default)
    {
        forms.Entry();await using var tx=await db.Database.BeginTransactionAsync(ct);var(actor,lease)=await Begin(behalf,ct);var data=await Load(actor,ct);
        var pending=Rows(data,actor.UserId,"pending");var cc=Rows(data,actor.UserId,"cc");
        var result=new NativeInboxCountsV2(pending.Length,cc.Length,Rows(data,actor.UserId,"running").Length,Rows(data,actor.UserId,"done").Length,
            pending.Sum(x=>x.UnreadCount),cc.Sum(x=>x.UnreadCount));await forms.Final(actor,lease,ct,false);return result;
    }
}
