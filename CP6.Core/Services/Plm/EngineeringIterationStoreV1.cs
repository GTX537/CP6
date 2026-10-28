using System.Globalization;
using System.Text;
using System.Text.Json;
using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using static CP6.Core.Services.Plm.EngineeringProductValuesV1;

namespace CP6.Core.Services.Plm;
internal sealed class EngineeringIterationHeadV1
{
    public Guid Id { get; init; }
    public Guid CreatorId { get; init; }
    public long Revision { get; init; }
    public Guid? ProjectId {get;init;}
    public Guid[] MemberIds {get;init;}=[];
    public string Kind { get; init; } = "";
    public string Name { get; init; } = "";
}
internal sealed class EngineeringIterationStoreV1(CP6Context db)
{
    internal CP6Context Context=>db;
    private IDbContextTransaction? fenced;
    private void Transaction(bool check = true)
    {
        if (!db.Database.IsNpgsql() || db.CurrentTenantId==Guid.Empty || db.Database.CurrentTransaction==null
            || check && !ReferenceEquals(fenced,db.Database.CurrentTransaction))throw Error("AUTHORITY_FENCE_UNAVAILABLE",503);
    }
    internal async Task Fence(bool write,CancellationToken ct)
    {
        Transaction(false);
        await db.Database.ExecuteSqlRawAsync(write
            ? "LOCK TABLE cp6_engineering_iteration_v1.heads,cp6_engineering_iteration_v1.versions IN SHARE ROW EXCLUSIVE MODE"
            : "LOCK TABLE cp6_engineering_iteration_v1.heads,cp6_engineering_iteration_v1.versions IN SHARE MODE",ct);
        if(!await db.Database.SqlQueryRaw<bool>("SELECT cp6_engineering_iteration_v1.ready() AS \"Value\"").SingleAsync(ct))throw Error("ITERATION_STORE_UNAVAILABLE",503);
        fenced=db.Database.CurrentTransaction;
    }
    internal static IQueryable<EngineeringIterationHeadV1> Heads(CP6Context context)=>context.Database.SqlQuery<EngineeringIterationHeadV1>($"""
        SELECT object_id AS "Id",creator_id AS "CreatorId",revision AS "Revision",kind AS "Kind",name AS "Name",project_id AS "ProjectId",member_ids AS "MemberIds"
        FROM cp6_engineering_iteration_v1.heads WHERE tenant_id={context.CurrentTenantId}
        """);
    private sealed class Original { public byte[] Body {get;init;}=[]; public string Digest {get;init;}=""; }
    internal async Task<EngineeringIterationSnapshotV1> Read(Guid id,long? version,CancellationToken ct)
    {
        Transaction();var head=await Heads(db).SingleOrDefaultAsync(x=>x.Id==id,ct)??throw Error("OBJECT_FORBIDDEN",403);
        var selected=version??head.Revision;
        var raw=await db.Database.SqlQuery<Original>($"""
            SELECT body_bytes AS "Body",body_digest AS "Digest" FROM cp6_engineering_iteration_v1.versions
            WHERE tenant_id={db.CurrentTenantId} AND object_id={id} AND revision={selected}
            """).SingleOrDefaultAsync(ct)??throw Error("ITERATION_VERSION_NOT_FOUND",404);
        EngineeringIterationStoredV1? v;
        try{v=JsonSerializer.Deserialize<EngineeringIterationStoredV1>(raw.Body,EngineeringProductStoreV1.Json);}
        catch(JsonException){throw Error("ITERATION_ORIGINAL_UNAVAILABLE",503);}
        if(v==null || v.Body==null || v.Id!=id || v.TenantId!=db.CurrentTenantId || v.CreatorId!=head.CreatorId || v.Etag==Guid.Empty
            || v.Body.Kind!=head.Kind || v.Version!=selected.ToString(CultureInfo.InvariantCulture) || EngineeringProductStoreV1.Digest(raw.Body)!=raw.Digest)
            throw Error("ITERATION_ORIGINAL_UNAVAILABLE",503);
        return new(db.CurrentTenantId,new(EngineeringIterationValuesV1.Owner,id,v.Version,raw.Digest),v.Etag,selected==head.Revision,
            v.Body,v.CreatorId,v.ActorId,v.ParentVersion,v.Reason,v.RecordedAt,Encoding.UTF8.GetString(raw.Body),[]);
    }
    internal async Task<EngineeringIterationSnapshotV1> Append(EngineeringIterationSaveV1 c,Guid actor,CancellationToken ct)
    {
        Transaction();var project=c.Body.Iteration?.ProjectId;var members=c.Body.Project?.Definition.Members??[];
        var h=await Heads(db).SingleOrDefaultAsync(x=>x.Id==c.ObjectId,ct);long next;Guid creator;
        if(c.Action=="CREATE")
        {
            if(h!=null)throw Error("ITERATION_IDENTITY_CONFLICT",409);next=1;creator=actor;
            if(await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO cp6_engineering_iteration_v1.heads(tenant_id,object_id,creator_id,kind,name,revision,project_id,member_ids,number)
                VALUES({db.CurrentTenantId},{c.ObjectId},{actor},{c.Body.Kind},{c.Body.Name},1,{project},{members},{c.Body.Number})
                """,ct)!=1)throw Error("ITERATION_WRITE_UNAVAILABLE",503);
        }
        else
        {
            if(h==null || h.Revision!=Revision(c.ExpectedVersion))throw Error("ITERATION_VERSION_CHANGED",412);
            var old=await Read(c.ObjectId,null,ct);if(old.Etag!=c.ExpectedEtag)throw Error("ITERATION_VERSION_CHANGED",412);
            if(h.Kind!=c.Body.Kind)throw Error("ITERATION_KIND_IMMUTABLE",409);
            if(h.Revision==long.MaxValue)throw Error("ITERATION_VERSION_EXHAUSTED",503);next=h.Revision+1;creator=h.CreatorId;
            if(await db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE cp6_engineering_iteration_v1.heads SET revision={next},name={c.Body.Name},member_ids={members}
                WHERE tenant_id={db.CurrentTenantId} AND object_id={c.ObjectId} AND revision={h.Revision}
                """,ct)!=1)throw Error("ITERATION_VERSION_CHANGED",412);
        }
        var raw=EngineeringProductStoreV1.Encode(new EngineeringIterationStoredV1(db.CurrentTenantId,c.ObjectId,next.ToString(CultureInfo.InvariantCulture),creator,
            Guid.NewGuid(),c.Body,c.OperationId,c.Action,actor,c.ExpectedVersion,c.Reason,DateTimeOffset.UtcNow));
        if(raw.Length>2097152)throw Error("ITERATION_SIZE_LIMIT",400);
        var digest=EngineeringProductStoreV1.Digest(raw);
        if(await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO cp6_engineering_iteration_v1.versions(tenant_id,object_id,revision,body_bytes,body_digest,actor_id,operation_id)
            VALUES({db.CurrentTenantId},{c.ObjectId},{next},{raw},{digest},{actor},{c.OperationId})
            """,ct)!=1)throw Error("ITERATION_WRITE_UNAVAILABLE",503);
        var saved=await Read(c.ObjectId,next,ct);
        if(saved.Reference.Digest!=digest || saved.OriginalJson!=Encoding.UTF8.GetString(raw))throw Error("ITERATION_WRITE_UNAVAILABLE",503);
        return saved;
    }
}
