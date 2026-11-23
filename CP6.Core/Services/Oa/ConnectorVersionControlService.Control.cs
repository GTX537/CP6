using System.Text.Json;
using CP6.Core.Services.Sys;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Oa;

public sealed partial class ConnectorVersionControlService
{
    private sealed record Member(string Kind,Guid Id,string State);
    private sealed record Impact(string Heads,long Frontier,Member[] Members);
    private sealed record PreviewRow(Guid Id,Guid RootId,Guid VersionId,string Action,string Heads,string Manifest,DateTimeOffset Expires,string Coverage);
    private sealed record Subject(Guid Id,Guid RootId,Guid VersionId,string Kind,Guid? ImpactRef,Guid Maker,string Heads);
    private sealed record Decision(Guid Id,Guid SubjectId,Guid Reviewer,string Vote);
    private IQueryable<PreviewRow> Previews() => db.Database.SqlQuery<PreviewRow>($"""
        SELECT id AS "Id",root_id AS "RootId",version_id AS "VersionId",action AS "Action",heads::text AS "Heads",
            manifest::text AS "Manifest",expires_at AS "Expires",coverage AS "Coverage" FROM cp6_oa_conn_v1.previews
        WHERE tenant_id={Tenant} AND environment_id={Environment}
        """);
    private IQueryable<Subject> Subjects() => db.Database.SqlQuery<Subject>($"""
        SELECT id AS "Id",root_id AS "RootId",version_id AS "VersionId",kind AS "Kind",impact_ref AS "ImpactRef",
            maker AS "Maker",heads::text AS "Heads" FROM cp6_oa_conn_v1.subjects
        WHERE tenant_id={Tenant} AND environment_id={Environment}
        """);
    private IQueryable<Decision> Decisions() => db.Database.SqlQuery<Decision>($"""
        SELECT id AS "Id",subject_id AS "SubjectId",reviewer AS "Reviewer",decision AS "Vote" FROM cp6_oa_conn_v1.decisions
        WHERE tenant_id={Tenant} AND environment_id={Environment}
        """);
    private static bool ValidAction(string kind,string action) =>
        action is "DISABLE" or "RETIRE" || kind=="CONNECTOR" && action=="ACTIVATE" || kind=="TRIGGER" && action=="ENABLE";
    private async Task<string> ReviewHeads(Head root,Version v,ScopeAuthorizationSnapshotV1 auth,CancellationToken ct)
    {
        var profile=await ProfileFor(v.ProfileId,ct);
        return Fingerprint(new {root.Revision,v.Id,v.ProfileId,dependencies=await Heads(profile,VersionContent(v).DefinitionId,auth,ct)});
    }
    private async Task<Impact> CurrentImpact(Head root,Version version,ScopeAuthorizationSnapshotV1 auth,CancellationToken ct)
    {
        var frontier=await db.Database.SqlQuery<long>($"""
            SELECT revision AS "Value" FROM cp6_oa_conn_v1.frontiers WHERE tenant_id={Tenant} AND environment_id={Environment} AND root_id={root.Id}
            """).SingleAsync(ct);
        // 从实际队列和触发流水取成员。取得租约本身不能证明已派发。
        var members=await db.Database.SqlQuery<Member>($"""
            WITH fires AS (
                SELECT f."Id",f."InstanceId",f."Source" FROM public."Wf_TriggerFire" f
                WHERE {root.Kind}='TRIGGER' AND f."TenantId"={Tenant} AND f."TriggerId"={root.Id}
            ), native AS (
                SELECT kind AS "Kind",id AS "Id",state AS "State" FROM cp6_oa_conn_v1.runtime_members
                WHERE tenant_id={Tenant} AND environment_id={Environment} AND root_id={root.Id}
            )
            SELECT * FROM native
            UNION SELECT CASE WHEN "Source"=0 THEN 'SLOT' ELSE 'INVOCATION' END,"Id",
                CASE WHEN "InstanceId" IS NULL THEN 'RESERVED' ELSE 'INSTANCE_CREATED' END FROM fires
            UNION SELECT 'INSTANCE',"InstanceId",'CREATED' FROM fires WHERE "InstanceId" IS NOT NULL
            UNION SELECT 'JOB',j."Id", CASE
                WHEN j."LastError"='BLOCKED_CONNECTOR' THEN 'BLOCKED_CONNECTOR'
                WHEN {root.Kind}='CONNECTOR' AND j."Status" IN (0,1) AND
                    (cp6_oa_conn_v1.unknown_job(j."Kind",cp6_oa_conn_v1.action_ref(j."ActionRefJson"::text))
                     OR ((j."Status"=1 OR j."AttemptCount">0)
                         AND cp6_oa_conn_v1.action_ref(j."ActionRefJson"::text) #>> ARRAY['connectorPin','rootId'] IS NULL
                         AND NOT EXISTS(SELECT FROM native WHERE "Kind"='DISPATCH' AND "Id"=j."Id"))) THEN 'UNVERIFIED_LEGACY'
                WHEN j."Status"=0 THEN 'PENDING' WHEN j."Status"=1 THEN 'CLAIMED' ELSE 'TERMINAL' END
            FROM public."Wf_ServiceJob" j WHERE j."TenantId"={Tenant} AND (
                ({root.Kind}='TRIGGER' AND EXISTS(SELECT FROM fires WHERE "InstanceId"=j."InstanceId"))
                OR ({root.Kind}='CONNECTOR' AND (cp6_oa_conn_v1.job_matches({root.Id},{root.Name},cp6_oa_conn_v1.action_ref(j."ActionRefJson"::text))
                    OR (j."Status" IN (0,1) AND cp6_oa_conn_v1.unknown_job(j."Kind",cp6_oa_conn_v1.action_ref(j."ActionRefJson"::text))))))
            """).OrderBy(m=>m.Kind).ThenBy(m=>m.Id).ThenBy(m=>m.State).ToArrayAsync(ct);
        var exact=members.GroupBy(m=>(m.Kind,m.Id)).Select(g=>g.Count()==1?g.First():
            new Member(g.Key.Kind,g.Key.Id,"IMPACT_STATE_CONFLICT")).ToArray();
        return new(await ReviewHeads(root,version,auth,ct),frontier,exact);
    }
    private static object SafeManifest(Member[] members) => new {
        invocations=members.Count(m=>m.Kind=="INVOCATION"),slots=members.Count(m=>m.Kind=="SLOT"),
        instances=members.Count(m=>m.Kind=="INSTANCE"),jobs=members.Count(m=>m.Kind=="JOB"),
        inFlight=members.Count(m=>m.Kind=="DISPATCH" && m.State is "DISPATCH_ADMITTED" or "SENT" or "OUTCOME_UNKNOWN"),
        states=members.GroupBy(m=>(m.Kind,m.State)).Select(g=>new {g.Key.Kind,g.Key.State,count=g.Count()})};
    private static JsonElement ImpactView(PreviewRow preview,int page=1)
    {
        if(page is <1 or >1000000) throw Error("PAGE_INVALID",400);
        var members=JsonSerializer.Deserialize<Member[]>(preview.Manifest,Json)!;
        var offset=(page-1)*100;
        return Value(new {previewId=preview.Id,preview.RootId,preview.VersionId,preview.Action,preview.Expires,preview.Coverage,
            isExecutionPermit=false,manifest=SafeManifest(members),page,total=members.Length,
            entries=members.Skip(offset).Take(100).Select((m,i)=>new {locator=preview.Id.ToString("D")+"/"+(offset+i+1),m.Kind,m.State})});
    }
    private async Task<Applied> Preview(Head root,ConnWireV1 wire,UserPermissionContext actor,ScopeAuthorizationSnapshotV1 auth,CancellationToken ct)
    {
        var input=wire.Read<ConnPreviewInputV1>();
        if(!ValidAction(root.Kind,input.Action)) throw Error("CONTROL_ACTION_INVALID");
        var v=await Exact(root,input.VersionId,ct);
        Expected(wire.Expected,root.Revision);
        var impact=await CurrentImpact(root,v,auth,ct);
        var id=Guid.NewGuid();
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO cp6_oa_conn_v1.previews(tenant_id,environment_id,id,root_id,version_id,action,heads,manifest,expires_at,coverage)
            VALUES({Tenant},{Environment},{id},{root.Id},{v.Id},{input.Action},to_jsonb({Fingerprint(impact)}::text),
                CAST({JsonSerializer.Serialize(impact.Members,Json)} AS jsonb),clock_timestamp()+interval '10 minutes',
                {(impact.Members.Any(m=>m.State is "UNVERIFIED_LEGACY" or "IMPACT_STATE_CONFLICT")?"PARTIAL_UNPROVEN":"COMPLETE")})
            """,ct);
        return new(root.Id,v.Id,ImpactView(await Previews().SingleAsync(p=>p.Id==id,ct)));
    }
    private async Task RequireFresh(DateTimeOffset expires,CancellationToken ct)
    {
        if(!await db.Database.SqlQuery<bool>($"""SELECT clock_timestamp() < {expires} AS "Value" """).SingleAsync(ct))
            throw Error("IMPACT_CHANGED",412);
    }
    private async Task<PreviewRow> CheckPreview(Head root,Version v,Guid? id,string action,ScopeAuthorizationSnapshotV1 auth,CancellationToken ct)
    {
        var p=await Previews().SingleOrDefaultAsync(p=>p.Id==id && p.RootId==root.Id && p.VersionId==v.Id && p.Action==action,ct);
        if(p==null) throw Error("IMPACT_PREVIEW_REQUIRED",412);
        if(p.Coverage!="COMPLETE") throw Error("IMPACT_COVERAGE_INCOMPLETE",412);
        await RequireFresh(p.Expires,ct);
        if(Stored(p.Heads).GetString()!=Fingerprint(await CurrentImpact(root,v,auth,ct)))
            throw Error("IMPACT_CHANGED",412);
        return p;
    }
    public Task<JsonElement> PreviewAsync(Guid id,int page,CancellationToken ct) => Read(async(actor,token)=>
    {
        var p=await Previews().SingleOrDefaultAsync(p=>p.Id==id,token) ?? throw Error("RESOURCE_NOT_FOUND",404);
        var root=await Roots().SingleAsync(r=>r.Id==p.RootId,token);
        await Require(actor,Permission(root.Kind,p.Action=="ACTIVATE"?"activate":"control"),token);
        if(!await CanReadSource(await Exact(root,p.VersionId,token),actor,token)) throw Error("RESOURCE_NOT_FOUND",404);
        return ImpactView(p,page);
    },ct);
    private async Task<Applied> Submit(Head root,ConnWireV1 wire,UserPermissionContext actor,ScopeAuthorizationSnapshotV1 auth,CancellationToken ct)
    {
        var input=wire.Read<ConnReviewInputV1>();
        var v=await Exact(root,input.VersionId,ct);
        Expected(wire.Expected,root.Revision);
        string action=input.Kind switch {
            "TRIGGER_PUBLISH" when root.Kind=="TRIGGER"=>"PUBLISH",
            "TRIGGER_ENABLE" when root.Kind=="TRIGGER"=>"ENABLE",
            "CONNECTOR_ACTIVATE" when root.Kind=="CONNECTOR"=>"ACTIVATE",
            "TRIGGER_RETIRE" when root.Kind=="TRIGGER"=>"RETIRE",
            "CONNECTOR_RETIRE" when root.Kind=="CONNECTOR"=>"RETIRE",
            _=>throw Error("REVIEW_KIND_INVALID")
        };
        if(action=="PUBLISH")
        {
            if(input.ImpactPreviewRef!=null || v.State!="DRAFT") throw Error("REVIEW_KIND_INVALID");
        }
        else await CheckPreview(root,v,input.ImpactPreviewRef,action,auth,ct);
        var id=Guid.NewGuid();
        var heads=await ReviewHeads(root,v,auth,ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO cp6_oa_conn_v1.subjects(tenant_id,environment_id,id,root_id,version_id,kind,impact_ref,heads,maker)
            VALUES({Tenant},{Environment},{id},{root.Id},{v.Id},{input.Kind},{input.ImpactPreviewRef},to_jsonb({heads}::text),{actor.UserId})
            """,ct);
        return new(root.Id,v.Id,new {subjectRef=id,kind=input.Kind,state="PENDING_REVIEW",root.Revision});
    }
    private async Task<Applied> Decide(Head root,ConnWireV1 wire,UserPermissionContext actor,ScopeAuthorizationSnapshotV1 auth,CancellationToken ct)
    {
        var input=wire.Read<ConnDecideInputV1>();
        var s=await Subjects().SingleOrDefaultAsync(s=>s.Id==input.SubjectRef && s.RootId==root.Id,ct) ?? throw Error("RESOURCE_NOT_FOUND",404);
        var v=await Exact(root,s.VersionId,ct);
        Expected(wire.Expected,root.Revision);
        if(!await CanReadSource(v,actor,ct)) throw Error("RESOURCE_NOT_FOUND",404);
        if(input.Decision is not ("APPROVE" or "REJECT")) throw Error("REVIEW_DECISION_INVALID");
        if(actor.UserId==s.Maker || actor.UserId==v.Maker || actor.UserId==root.Owner) throw Error("SOD_VIOLATION",403);
        if(Stored(s.Heads).GetString()!=await ReviewHeads(root,v,auth,ct)) throw Error("REVIEW_STALE",412);
        if(s.ImpactRef!=null) await CheckPreview(root,v,s.ImpactRef,s.Kind.Split('_')[1],auth,ct);
        if(await Decisions().AnyAsync(d=>d.SubjectId==s.Id,ct)) throw Error("REVIEW_ALREADY_DECIDED",409);
        var id=Guid.NewGuid();
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO cp6_oa_conn_v1.decisions(tenant_id,environment_id,id,subject_id,reviewer,decision,authority_epoch)
            VALUES({Tenant},{Environment},{id},{s.Id},{actor.UserId},{input.Decision},{auth.Epoch})
            """,ct);
        return new(root.Id,v.Id,new {subjectRef=s.Id,decisionRef=id,decision=input.Decision,root.Revision});
    }
    public Task<JsonElement> ReviewsAsync(string kind,Guid? subject,Guid? root,int page,CancellationToken ct) => Read(async(actor,token)=>
    {
        CheckKind(kind);
        if(page is <1 or >100000) throw Error("PAGE_INVALID",400);
        await Require(actor,Permission(kind,"review"),token);
        var query=from s in Subjects() join r in Roots() on s.RootId equals r.Id where r.Kind==kind select s;
        if(root.HasValue) query=query.Where(s=>s.RootId==root.Value);
        if(subject.HasValue) query=query.Where(s=>s.Id==subject.Value);
        else
        {
            var decided=Decisions().Select(d=>d.SubjectId);
            query=query.Where(s=>!decided.Contains(s.Id));
        }
        var rows=await query.OrderBy(s=>s.Id).Skip((page-1)*100).Take(100).ToArrayAsync(token);
        if(subject.HasValue && rows.Length==0) throw Error("RESOURCE_NOT_FOUND",404);
        var result=new List<object>();
        foreach(var s in rows)
        {
            var r=await Root(kind,s.RootId,token);
            var version=await Exact(r,s.VersionId,token);
            if(!await CanReadSource(version,actor,token))
            {
                if(subject.HasValue) throw Error("RESOURCE_NOT_FOUND",404);
                continue;
            }
            result.Add(new {subjectRef=s.Id,s.RootId,s.VersionId,s.Kind,s.ImpactRef,s.Maker,r.Revision,
                canDecide=actor.UserId!=s.Maker && actor.UserId!=version.Maker && actor.UserId!=r.Owner,
                target=await Context(r,version,token),
                impact=s.ImpactRef==null?(JsonElement?)null:ImpactView(await Previews().SingleAsync(p=>p.Id==s.ImpactRef,token))});
        }
        return Value(result);
    },ct);
    private async Task<Applied> Control(Head root,string action,ConnWireV1 wire,UserPermissionContext actor,ScopeAuthorizationSnapshotV1 auth,CancellationToken ct)
    {
        var input=wire.Read<ConnControlInputV1>();
        var v=await Exact(root,input.VersionId,ct);
        Expected(wire.Expected,root.Revision);
        if(!await CanReadSource(v,actor,ct)) throw Error("RESOURCE_NOT_FOUND",404);
        bool publish=action=="PUBLISH";
        if(publish ? root.Kind!="TRIGGER" || input.ImpactPreviewRef!=null : !ValidAction(root.Kind,action))
            throw Error("CONTROL_ACTION_INVALID");
        PreviewRow? preview=null;
        if(!publish)
        {
            if(string.IsNullOrWhiteSpace(input.Reason) || input.Reason.Length>500) throw Error("CONTROL_REASON_REQUIRED");
            preview=await CheckPreview(root,v,input.ImpactPreviewRef,action,auth,ct);
        }
        if(action=="DISABLE")
        {
            if(input.DecisionRef!=null) throw Error("DISABLE_DECISION_INVALID",409);
        }
        else
        {
            var d=await Decisions().SingleOrDefaultAsync(d=>d.Id==input.DecisionRef,ct) ?? throw Error("APPROVAL_REQUIRED",409);
            var s=await Subjects().SingleAsync(s=>s.Id==d.SubjectId,ct);
            if(d.Vote!="APPROVE" || s.Kind!=root.Kind+"_"+action || s.RootId!=root.Id || s.VersionId!=v.Id || s.ImpactRef!=input.ImpactPreviewRef)
                throw Error("REVIEW_SUBJECT_MISMATCH",409);
            if(Stored(s.Heads).GetString()!=await ReviewHeads(root,v,auth,ct)) throw Error("REVIEW_STALE",412);
            if(!await db.Sys_Users.AsNoTracking().AnyAsync(u=>u.Id==d.Reviewer && u.Enable && (u.LockedUntil==null || u.LockedUntil<=DateTime.Now),ct))
                throw Error("REVIEWER_UNAVAILABLE",403);
            var reviewer=await PostgreSqlPermissionReader.ReadAsync(db,d.Reviewer,ct);
            await Require(reviewer,Permission(root.Kind,"review"),ct);
            await Require(reviewer,"conn.read",ct);
            if(!await CanReadSource(v,reviewer,ct)) throw Error("REVIEWER_UNAVAILABLE",403);
        }
        bool activate=action is "ENABLE" or "ACTIVATE";
        if(v.State=="RETIRED" && action!="RETIRE" || publish && v.Published || root.Kind=="TRIGGER" && activate && !v.Published)
            throw Error("VERSION_STATE_CONFLICT",409);
        if(activate) await CheckAdmission(root,v,await ProfileFor(v.ProfileId,ct),actor,ct);
        var next=action switch {"PUBLISH" or "DISABLE"=>"DISABLED","ENABLE" or "ACTIVATE"=>"ENABLED",_=>"RETIRED"};
        var changed=v.State!=next || publish && !v.Published;
        var revision=changed ? Guid.NewGuid() : root.Revision;
        if(changed)
        {
            if(activate && root.Active is Guid previous && previous!=v.Id)
                await db.Database.ExecuteSqlInterpolatedAsync($"""
                    UPDATE cp6_oa_conn_v1.version_control SET state='DISABLED' WHERE tenant_id={Tenant}
                        AND environment_id={Environment} AND root_id={root.Id} AND version_id={previous} AND state='ENABLED'
                    """,ct);
            Guid? active=activate ? v.Id : root.Active==v.Id ? null : root.Active;
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE cp6_oa_conn_v1.version_control SET published=(published OR {publish || activate}),state={next}
                    WHERE tenant_id={Tenant} AND environment_id={Environment} AND root_id={root.Id} AND version_id={v.Id};
                UPDATE cp6_oa_conn_v1.roots SET revision={revision},active_version={active}
                    WHERE tenant_id={Tenant} AND environment_id={Environment} AND id={root.Id}
                """,ct);
        }
        var inFlight=preview==null ? 0 : JsonSerializer.Deserialize<Member[]>(preview.Manifest,Json)!
            .Count(m=>m.Kind=="DISPATCH" && m.State is "DISPATCH_ADMITTED" or "SENT" or "OUTCOME_UNKNOWN");
        return new(root.Id,v.Id,new {action,state=next,revision,effect=changed?"APPLIED":"NO_CHANGE",input.DecisionRef,
            input.ImpactPreviewRef,input.Reason,notRevokedInFlight=inFlight,notRevokedInFlightManifestLocator=preview?.Id},preview?.Expires);
    }
}
