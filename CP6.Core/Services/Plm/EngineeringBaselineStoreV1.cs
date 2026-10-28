using System.Text;
using System.Text.Json;
using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using static CP6.Core.Services.Plm.EngineeringProductValuesV1;
using static CP6.Core.Services.Plm.EngineeringBaselineValuesV1;
namespace CP6.Core.Services.Plm;

internal sealed class EngineeringBaselineHeadV1
{
    public Guid Id{get;init;} public Guid CreatorId{get;init;} public Guid RevisionId{get;init;}
    public Guid CandidateId{get;init;} public string CandidateVersion{get;init;}="";
    public Guid ConfigurationId{get;init;} public string ConfigurationVersion{get;init;}="";
    public Guid OperationId{get;init;} public Guid IntentId{get;init;}
}
internal sealed class EngineeringBaselineStoreV1(CP6Context db,Guid environment)
{
    private IDbContextTransaction? fenced;
    private void Transaction(bool ready=true)
    {if(!db.Database.IsNpgsql() || db.CurrentTenantId==Guid.Empty || environment==Guid.Empty || db.Database.CurrentTransaction==null
        || ready && !ReferenceEquals(fenced,db.Database.CurrentTransaction))throw Error("AUTHORITY_FENCE_UNAVAILABLE",503);}
    internal async Task Fence(bool write,CancellationToken ct)
    {
        Transaction(false);await new EngineeringRevisionStoreV1(db).Fence(write,ct);
        await db.Database.ExecuteSqlRawAsync(write?"LOCK TABLE cp6_engineering_baseline_v1.baselines,cp6_engineering_baseline_v1.members,cp6_engineering_baseline_v1.intents,cp6_engineering_baseline_v1.successors IN SHARE ROW EXCLUSIVE MODE"
            :"LOCK TABLE cp6_engineering_baseline_v1.baselines,cp6_engineering_baseline_v1.members,cp6_engineering_baseline_v1.intents,cp6_engineering_baseline_v1.successors IN SHARE MODE",ct);
        if(!await db.Database.SqlQueryRaw<bool>("SELECT cp6_engineering_baseline_v1.ready() AS \"Value\"").SingleAsync(ct))throw Error("ENGINEERING_BASELINE_STORE_UNAVAILABLE",503);
        fenced=db.Database.CurrentTransaction;
    }
    internal static IQueryable<EngineeringBaselineHeadV1> Heads(CP6Context db,Guid environment)=>db.Database.SqlQuery<EngineeringBaselineHeadV1>($"""
        SELECT baseline_id AS "Id",actor_id AS "CreatorId",revision_id AS "RevisionId",candidate_id AS "CandidateId",candidate_version::text AS "CandidateVersion",
          configuration_id AS "ConfigurationId",configuration_version::text AS "ConfigurationVersion",operation_id AS "OperationId",intent_id AS "IntentId"
        FROM cp6_engineering_baseline_v1.baselines WHERE environment_id={environment} AND tenant_id={db.CurrentTenantId}
        """);
    private sealed class Raw
    {public byte[] Body{get;init;}=[];public string Digest{get;init;}="";public byte[]? Binding{get;init;} public string? BindingDigest{get;init;}}
    internal async Task<EngineeringBaselineViewV1> Read(Guid id,CancellationToken ct)
    {
        Transaction();var raw=await db.Database.SqlQuery<Raw>($"""
            SELECT body AS "Body",digest AS "Digest",binding AS "Binding",binding_digest AS "BindingDigest" FROM cp6_engineering_baseline_v1.baselines
            WHERE environment_id={environment} AND tenant_id={db.CurrentTenantId} AND baseline_id={id}
            """).SingleOrDefaultAsync(ct)??throw Error("OBJECT_FORBIDDEN",403);
        if(Hash(raw.Body)!=raw.Digest || raw.Binding!=null && Hash(raw.Binding)!=raw.BindingDigest)throw Error("ENGINEERING_BASELINE_ORIGINAL_UNAVAILABLE",503);
        var o=JsonSerializer.Deserialize<EngineeringBaselineOriginalV1>(raw.Body,EngineeringProductStoreV1.Json)??throw Error("ENGINEERING_BASELINE_ORIGINAL_UNAVAILABLE",503);
        if(o.Id!=id || o.TenantId!=db.CurrentTenantId || o.EnvironmentId!=environment || o.Version!="1" || o.Schema!=Schema)throw Error("ENGINEERING_BASELINE_ORIGINAL_UNAVAILABLE",503);
        var binding=raw.Binding==null?null:JsonSerializer.Deserialize<EngineeringBaselineBindingV1>(raw.Binding,EngineeringProductStoreV1.Json);
        return new(new(EngineeringBaselineValuesV1.Owner,id,"1",raw.Digest),o,Encoding.UTF8.GetString(raw.Body),binding,raw.Binding==null?null:Encoding.UTF8.GetString(raw.Binding),"NOT_CHECKED",[]);
    }
    internal async Task<EngineeringFreezeResultV1> Save(EngineeringBaselineOriginalV1 original,OutputRelationsVersionV1? relation,CancellationToken ct)
    {
        // The approved native review package is bounded at 16 MiB. Preserve its nested originals,
        // the selected candidate and separate technical evidence without imposing a smaller aggregate limit.
        Transaction();var bytes=Bytes(original);if(bytes.Length>268435456)throw Error("ENGINEERING_BASELINE_SIZE_LIMIT",400);
        var digest=Hash(bytes);var reference=new EngineeringSourceRefV1(EngineeringBaselineValuesV1.Owner,original.Id,"1",digest);
        var binding=relation==null?null:Binding(reference,original,relation);var b=binding==null?null:Bytes(binding);var c=original.Command;var intent=Guid.NewGuid();
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO cp6_engineering_baseline_v1.baselines(environment_id,tenant_id,baseline_id,actor_id,revision_id,revision_version,candidate_id,candidate_version,
              configuration_id,configuration_version,operation_id,intent_id,body,digest,binding,binding_digest)
            VALUES({environment},{db.CurrentTenantId},{original.Id},{original.ActorId},{c.Revision.Id},{Revision(c.Revision.Version)},{c.EngineeringCandidate.Id},{Revision(c.EngineeringCandidate.Version)},
              {c.Configuration.Id},{Revision(c.Configuration.Version)},{c.OperationId},{intent},{bytes},{digest},{b},{(b==null?null:Hash(b))})
            """,ct);
        var components=original.Revision.Draft.Components;
        for(var i=0;i<components.Length;i++)
        {
            var member=Bytes(components[i]);await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO cp6_engineering_baseline_v1.members(environment_id,tenant_id,baseline_id,ordinal,occurrence_id,body,digest)
                VALUES({environment},{db.CurrentTenantId},{original.Id},{i},{components[i].OccurrenceId},{member},{Hash(member)})
                """,ct);
        }
        var result=new EngineeringFreezeResultV1(c.OperationId,reference,c.OperationId,"ACCEPTED",original.Manifest.Digest,binding?.BodyDigest,intent);
        var payload=Bytes(new{schema="Main.Plm.EngineeringBaselineFrozen/1",scope=new{environmentId=environment,tenantId=db.CurrentTenantId},result});
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO cp6_engineering_baseline_v1.intents(environment_id,tenant_id,baseline_id,id,payload,digest)
            VALUES({environment},{db.CurrentTenantId},{original.Id},{intent},{payload},{Hash(payload)})
            """,ct);return result;
    }
    internal async Task SaveSuccessor(EngineeringBaselineSuccessorV1 c,EngineeringBaselineSuccessorResultV1 result,Guid actor,CancellationToken ct)
    {
        Transaction();var raw=Bytes(result);await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO cp6_engineering_baseline_v1.successors(environment_id,tenant_id,baseline_id,operation_id,actor_id,revision_id,revision_version,body,digest)
            VALUES({environment},{db.CurrentTenantId},{c.Baseline.Id},{c.OperationId},{actor},{result.Revision.Id},{Revision(result.Revision.Version)},{raw},{Hash(raw)})
            """,ct);
    }
    internal async Task<EngineeringSourceRefV1[]> Successors(Guid id,CancellationToken ct)
    {
        Transaction();var rows=await db.Database.SqlQuery<Raw>($"""
            SELECT body AS "Body",digest AS "Digest",NULL::bytea AS "Binding",NULL::text AS "BindingDigest" FROM cp6_engineering_baseline_v1.successors
            WHERE environment_id={environment} AND tenant_id={db.CurrentTenantId} AND baseline_id={id} ORDER BY revision_version
            """).ToArrayAsync(ct);
        return rows.Select(x=>{if(Hash(x.Body)!=x.Digest)throw Error("ENGINEERING_BASELINE_ORIGINAL_UNAVAILABLE",503);
            var r=JsonSerializer.Deserialize<EngineeringBaselineSuccessorResultV1>(x.Body,EngineeringProductStoreV1.Json)!;
            return new EngineeringSourceRefV1(r.Revision.Owner,r.Revision.Id,r.Revision.Version,r.Revision.Digest);}).ToArray();
    }
}
