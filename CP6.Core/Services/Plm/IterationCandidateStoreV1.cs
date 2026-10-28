using System.Globalization;
using System.Text;
using System.Text.Json;
using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using static CP6.Core.Services.Plm.EngineeringProductValuesV1;
namespace CP6.Core.Services.Plm;
internal sealed class IterationCandidateHeadV1
{
    public Guid Id {get;init;} public Guid IterationId {get;init;} public Guid CreatorId {get;init;}
    public long Revision {get;init;} public string Title {get;init;}="";
}
internal sealed class IterationCandidateStoreV1(CP6Context db)
{
    private IDbContextTransaction? transaction;
    private void Ready(bool check=true)
    {
        if(!db.Database.IsNpgsql() || db.CurrentTenantId==Guid.Empty || db.Database.CurrentTransaction==null
            || check && !ReferenceEquals(transaction,db.Database.CurrentTransaction))throw Error("AUTHORITY_FENCE_UNAVAILABLE",503);
    }
    internal async Task Fence(bool write,CancellationToken ct)
    {
        Ready(false);
        await db.Database.ExecuteSqlRawAsync(write
            ?"LOCK TABLE cp6_iteration_candidate_v1.heads,cp6_iteration_candidate_v1.versions IN SHARE ROW EXCLUSIVE MODE"
            :"LOCK TABLE cp6_iteration_candidate_v1.heads,cp6_iteration_candidate_v1.versions IN SHARE MODE",ct);
        if(!await db.Database.SqlQueryRaw<bool>("SELECT cp6_iteration_candidate_v1.ready() AS \"Value\"").SingleAsync(ct))throw Error("ITERATION_CANDIDATE_STORE_UNAVAILABLE",503);
        transaction=db.Database.CurrentTransaction;
    }
    internal IQueryable<IterationCandidateHeadV1> Heads()=>db.Database.SqlQuery<IterationCandidateHeadV1>($"""
        SELECT candidate_id AS "Id",iteration_id AS "IterationId",creator_id AS "CreatorId",revision AS "Revision",title AS "Title"
        FROM cp6_iteration_candidate_v1.heads WHERE tenant_id={db.CurrentTenantId}
        """);
    private sealed class Raw {public byte[] Body {get;init;}=[];public string Digest {get;init;}="";}
    internal async Task<IterationCandidateSnapshotV1> Read(Guid iteration,Guid id,long? version,CancellationToken ct)
    {
        Ready();var head=await Heads().SingleOrDefaultAsync(x=>x.Id==id && x.IterationId==iteration,ct)??throw Error("OBJECT_FORBIDDEN",403);
        var selected=version??head.Revision;
        var raw=await db.Database.SqlQuery<Raw>($"""
            SELECT body_bytes AS "Body",body_digest AS "Digest" FROM cp6_iteration_candidate_v1.versions
            WHERE tenant_id={db.CurrentTenantId} AND candidate_id={id} AND revision={selected}
            """).SingleOrDefaultAsync(ct)??throw Error("ITERATION_CANDIDATE_VERSION_NOT_FOUND",404);
        IterationCandidateStoredV1? v;
        try{v=JsonSerializer.Deserialize<IterationCandidateStoredV1>(raw.Body,EngineeringProductStoreV1.Json);}
        catch(JsonException){throw Error("ITERATION_CANDIDATE_ORIGINAL_UNAVAILABLE",503);}
        if(v==null || v.Id!=id || v.IterationId!=iteration || v.TenantId!=db.CurrentTenantId || v.CreatorId!=head.CreatorId
            || v.Version!=selected.ToString(CultureInfo.InvariantCulture) || v.Etag==Guid.Empty || v.Definition==null
            || EngineeringProductStoreV1.Digest(raw.Body)!=raw.Digest)throw Error("ITERATION_CANDIDATE_ORIGINAL_UNAVAILABLE",503);
        return new(new(IterationCandidateValuesV1.Owner,id,v.Version,raw.Digest),selected==head.Revision,v,Encoding.UTF8.GetString(raw.Body),"UNKNOWN",[]);
    }
    internal async Task<IterationCandidateSnapshotV1> Save(IterationCandidateStoredV1 value,CancellationToken ct)
    {
        Ready();var revision=Revision(value.Version);var bytes=EngineeringProductStoreV1.Encode(value);
        if(bytes.Length>16777216)throw Error("ITERATION_CANDIDATE_SIZE_LIMIT",400);
        if(revision==1)
        {
            if(await Heads().AnyAsync(x=>x.Id==value.Id,ct))throw Error("ITERATION_CANDIDATE_IDENTITY_CONFLICT",409);
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO cp6_iteration_candidate_v1.heads(tenant_id,candidate_id,iteration_id,creator_id,revision,title)
                VALUES({db.CurrentTenantId},{value.Id},{value.IterationId},{value.CreatorId},{revision},{value.Definition.Title})
                """,ct);
        }
        else if(await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE cp6_iteration_candidate_v1.heads SET revision={revision},title={value.Definition.Title}
            WHERE tenant_id={db.CurrentTenantId} AND candidate_id={value.Id} AND iteration_id={value.IterationId} AND revision={revision-1}
            """,ct)!=1)throw Error("ITERATION_CANDIDATE_VERSION_CHANGED",412);
        var digest=EngineeringProductStoreV1.Digest(bytes);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO cp6_iteration_candidate_v1.versions(tenant_id,candidate_id,iteration_id,revision,body_bytes,body_digest,actor_id,operation_id)
            VALUES({db.CurrentTenantId},{value.Id},{value.IterationId},{revision},{bytes},{digest},{value.ActorId},{value.OperationId})
            """,ct);
        return await Read(value.IterationId,value.Id,revision,ct);
    }
}
