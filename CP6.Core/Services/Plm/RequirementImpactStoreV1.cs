using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore;
using static CP6.Core.Services.Plm.EngineeringProductValuesV1;
namespace CP6.Core.Services.Plm;

internal sealed class RequirementImpactStoreV1(CP6Context db)
{
    internal async Task Ready(CancellationToken ct)
    {
        if(!await db.Database.SqlQueryRaw<bool>("SELECT cp6_requirement_impact_v1.ready() AS \"Value\"").SingleAsync(ct))
            throw Error("REQUIREMENT_IMPACT_STORE_UNAVAILABLE",503);
    }
    private sealed class RequestRow
    {
        public Guid Id {get;init;} public Guid CandidateId {get;init;} public long Revision {get;init;}
        public string Digest {get;init;}=""; public string Action {get;init;}="";public Guid Actor {get;init;}
        public string Reason {get;init;}="";public DateTimeOffset At {get;init;}public Guid? Previous {get;init;}
        public RequirementImpactRequestV1 Value()=>new(Id,new(RequirementSetValuesV1.Owner,CandidateId,Revision.ToString(System.Globalization.CultureInfo.InvariantCulture),Digest),Action,Actor,Reason,At,Previous);
    }
    internal async Task<RequirementImpactRequestV1[]> Requests(CancellationToken ct)=>
        (await db.Database.SqlQuery<RequestRow>($"""
            SELECT DISTINCT ON(r.candidate_id,r.candidate_revision) r.id AS "Id",r.candidate_id AS "CandidateId",r.candidate_revision AS "Revision",
              v.body_digest AS "Digest",r.action AS "Action",r.actor_id AS "Actor",r.reason AS "Reason",r.recorded_at AS "At",r.previous_id AS "Previous"
            FROM cp6_requirement_impact_v1.requests r JOIN cp6_requirement_set_v1.versions v
              ON(v.tenant_id=r.tenant_id AND v.object_id=r.candidate_id AND v.revision=r.candidate_revision)
            WHERE r.tenant_id={db.CurrentTenantId} ORDER BY r.candidate_id,r.candidate_revision,r.sequence DESC
            """).ToArrayAsync(ct)).Select(r=>r.Value()).ToArray();
    internal async Task AddRequest(RequirementImpactRequestCommandV1 c,Guid actor,Guid? previous,CancellationToken ct)=>
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO cp6_requirement_impact_v1.requests(tenant_id,id,candidate_id,candidate_revision,action,previous_id,actor_id,reason)
            VALUES({db.CurrentTenantId},{c.OperationId},{c.Candidate.Id},{Revision(c.Candidate.Version)},{c.Action},{previous},{actor},{c.Reason})
            """,ct);
    private sealed class CaseRow
    {
        public Guid Id {get;init;}public Guid RequestId {get;init;}public Guid CandidateId {get;init;}public long CandidateRevision {get;init;}
        public string CandidateDigest {get;init;}=""; public Guid IterationId {get;init;} public long IterationRevision {get;init;}
        public string IterationDigest {get;init;}="";public long AdoptedRevision {get;init;}public string AdoptedDigest {get;init;}="";
        public long InputVersion {get;init;}public long PlanVersion {get;init;}public string Stage {get;init;}="";
        public Guid Actor {get;init;}public DateTimeOffset At {get;init;}
        public RequirementImpactCaseV1 Value(IterationImpactAssessmentV1? assessment)=>new(Id,RequestId,
            new(RequirementSetValuesV1.Owner,CandidateId,Str(CandidateRevision),CandidateDigest),
            new(EngineeringIterationValuesV1.Owner,IterationId,Str(IterationRevision),IterationDigest),
            new(RequirementSetValuesV1.Owner,CandidateId,Str(AdoptedRevision),AdoptedDigest),Str(InputVersion),Str(PlanVersion),Stage,Actor,At,assessment);
    }
    private static string Str(long value)=>value.ToString(System.Globalization.CultureInfo.InvariantCulture);
    private sealed class AssessmentRow
    {
        public Guid Id {get;init;}public Guid CaseId {get;init;} public Guid IterationId {get;init;}public long Revision {get;init;}
        public string Digest {get;init;}="";public long InputVersion {get;init;}public long PlanVersion {get;init;}
        public string Decision {get;init;}="";public Guid Actor {get;init;}public string Reason {get;init;}="";
        public DateTimeOffset At {get;init;}public Guid? Previous {get;init;}
        public IterationImpactAssessmentV1 Value()=>new(Id,CaseId,new(EngineeringIterationValuesV1.Owner,IterationId,Str(Revision),Digest),
            Str(InputVersion),Str(PlanVersion),Decision,Actor,Reason,At,Previous);
    }
    internal async Task<IterationImpactAssessmentV1[]> Assessments(Guid caseId,CancellationToken ct)=>
        (await db.Database.SqlQuery<AssessmentRow>($"""
            SELECT a.id AS "Id",a.case_id AS "CaseId",a.iteration_id AS "IterationId",a.iteration_revision AS "Revision",v.body_digest AS "Digest",
              a.input_version AS "InputVersion",a.plan_version AS "PlanVersion",a.decision AS "Decision",a.actor_id AS "Actor",a.reason AS "Reason",
              a.recorded_at AS "At",a.previous_id AS "Previous"
            FROM cp6_requirement_impact_v1.assessments a JOIN cp6_engineering_iteration_v1.versions v
              ON(v.tenant_id=a.tenant_id AND v.object_id=a.iteration_id AND v.revision=a.iteration_revision)
            WHERE a.tenant_id={db.CurrentTenantId} AND a.case_id={caseId} ORDER BY a.sequence DESC
            """).ToArrayAsync(ct)).Select(r=>r.Value()).ToArray();
    internal async Task<RequirementImpactCaseV1[]> Cases(CancellationToken ct)
    {
        var rows=await db.Database.SqlQuery<CaseRow>($"""
            SELECT c.id AS "Id",c.request_id AS "RequestId",c.candidate_id AS "CandidateId",c.candidate_revision AS "CandidateRevision",s.body_digest AS "CandidateDigest",
              c.iteration_id AS "IterationId",c.iteration_revision AS "IterationRevision",i.body_digest AS "IterationDigest",c.adopted_revision AS "AdoptedRevision",
              p.body_digest AS "AdoptedDigest",c.input_version AS "InputVersion",c.plan_version AS "PlanVersion",c.stage AS "Stage",c.actor_id AS "Actor",c.recorded_at AS "At"
            FROM cp6_requirement_impact_v1.cases c
            JOIN cp6_requirement_set_v1.versions s ON(s.tenant_id=c.tenant_id AND s.object_id=c.candidate_id AND s.revision=c.candidate_revision)
            JOIN cp6_requirement_set_v1.versions p ON(p.tenant_id=c.tenant_id AND p.object_id=c.candidate_id AND p.revision=c.adopted_revision)
            JOIN cp6_engineering_iteration_v1.versions i ON(i.tenant_id=c.tenant_id AND i.object_id=c.iteration_id AND i.revision=c.iteration_revision)
            WHERE c.tenant_id={db.CurrentTenantId} ORDER BY c.recorded_at,c.id
            """).ToArrayAsync(ct);
        var result=new List<RequirementImpactCaseV1>();
        foreach(var r in rows)
        {var history=await Assessments(r.Id,ct);result.Add(r.Value(history.FirstOrDefault()) with{AssessmentHistory=history});}
        return result.ToArray();
    }
    internal async Task<Guid> AddCase(RequirementImpactRequestV1 request,EngineeringIterationSnapshotV1 iteration,Guid actor,Guid operation,string journal,CancellationToken ct)
    {
        var work=iteration.Body.Iteration!;var adopted=work.Input.Proof.RequirementSet;var id=Guid.NewGuid();
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO cp6_requirement_impact_v1.cases(tenant_id,id,request_id,candidate_id,candidate_revision,iteration_id,iteration_revision,
              input_version,adopted_revision,plan_version,stage,actor_id,operation_id,journal)
            VALUES({db.CurrentTenantId},{id},{request.Id},{request.Candidate.Id},{Revision(request.Candidate.Version)},{iteration.Reference.Id},
              {Revision(iteration.Reference.Version)},{Revision(work.InputVersion)},{Revision(adopted.Version)},{Revision(work.PlanVersion)},
              {work.Stage},{actor},{operation},{journal})
            """,ct);return id;
    }
    internal async Task AddAssessment(IterationImpactAssessmentCommandV1 c,IterationWorkV1 work,Guid actor,CancellationToken ct)=>
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO cp6_requirement_impact_v1.assessments(tenant_id,id,case_id,previous_id,iteration_id,iteration_revision,input_version,plan_version,decision,actor_id,reason)
            VALUES({db.CurrentTenantId},{c.OperationId},{c.CaseId},{c.ExpectedAssessmentId},{c.Iteration.Id},{Revision(c.Iteration.Version)},
              {Revision(work.InputVersion)},{Revision(work.PlanVersion)},{c.Decision},{actor},{c.Reason})
            """,ct);
    internal static string State(EngineeringIterationSnapshotV1 iteration,RequirementImpactCaseV1? item)
    {
        if(item==null)return "NOT_REQUESTED";
        var w=iteration.Body.Iteration!;
        if(w.Input.Proof.RequirementSet==item.Candidate)return "ADOPTED";
        if(w.InputVersion!=item.InputVersion)return "INPUT_REPLACED";
        if(item.Assessment==null)return "PENDING";
        if(item.Assessment.PlanVersion!=w.PlanVersion)return "REASSESSMENT_REQUIRED";
        return item.Assessment.Decision;
    }
    internal static RequirementImpactConsumerV1 Consumer(EngineeringIterationSnapshotV1 v,RequirementImpactCaseV1? c)=>new(v.Reference,v.Body.Name,
        v.Body.Iteration!.Input.Proof.RequirementSet,v.Body.Iteration.InputVersion,v.Body.Iteration.PlanVersion,v.Body.Iteration.Stage,State(v,c),c);
}
