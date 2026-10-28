using System.Globalization;
using System.Text;
using System.Text.Json;
using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using static CP6.Core.Services.Plm.EngineeringProductValuesV1;
using static CP6.Core.Services.Plm.EngineeringOutputRelationsValuesV1;

namespace CP6.Core.Services.Plm;

internal sealed class OutputRelationsHeadV1
{
    public Guid Id { get; init; }
    public Guid CreatorId { get; init; }
    public Guid RevisionId { get; init; }
    public Guid ProductId { get; init; }
    public long Revision { get; init; }
    public long Control { get; init; }
    public string Name { get; init; }="";
}
internal sealed class EngineeringOutputRelationsStoreV1(CP6Context db,Guid environment)
{
    private IDbContextTransaction? fenced;
    private void Transaction(bool ready=true)
    {
        if(!db.Database.IsNpgsql() || db.Database.CurrentTransaction==null || db.CurrentTenantId==Guid.Empty || environment==Guid.Empty
            || ready && !ReferenceEquals(fenced,db.Database.CurrentTransaction))throw Error("AUTHORITY_FENCE_UNAVAILABLE",503);
    }
    internal async Task Fence(bool write,CancellationToken ct)
    {
        Transaction(false);await new EngineeringRevisionStoreV1(db).Fence(false,ct);
        await db.Database.ExecuteSqlRawAsync(write
            ? "LOCK TABLE cp6_output_relations_v1.heads,cp6_output_relations_v1.versions,cp6_output_relations_v1.controls,cp6_output_relations_v1.records IN SHARE ROW EXCLUSIVE MODE"
            : "LOCK TABLE cp6_output_relations_v1.heads,cp6_output_relations_v1.versions,cp6_output_relations_v1.controls,cp6_output_relations_v1.records IN SHARE MODE",ct);
        if(!await db.Database.SqlQueryRaw<bool>("SELECT cp6_output_relations_v1.ready() AS \"Value\"").SingleAsync(ct))throw Error("OUTPUT_RELATIONS_STORE_UNAVAILABLE",503);
        fenced=db.Database.CurrentTransaction;
    }
    internal static IQueryable<OutputRelationsHeadV1> Heads(CP6Context db,Guid environment)=>db.Database.SqlQuery<OutputRelationsHeadV1>($"""
        SELECT relation_id AS "Id",creator_id AS "CreatorId",engineering_revision_id AS "RevisionId",product_id AS "ProductId",
          draft_revision AS "Revision",control_revision AS "Control",name AS "Name" FROM cp6_output_relations_v1.heads
        WHERE environment_id={environment} AND tenant_id={db.CurrentTenantId}
        """);
    private sealed record Raw(byte[] Body,string Digest);
    private static T Parse<T>(Raw raw)
    {
        try{if(Hash(raw.Body)!=raw.Digest)throw new JsonException();return JsonSerializer.Deserialize<T>(raw.Body,EngineeringProductStoreV1.Json)??throw new JsonException();}
        catch(JsonException){throw Error("OUTPUT_RELATIONS_ORIGINAL_UNAVAILABLE",503);}
    }
    internal async Task<OutputRelationsVersionV1> Read(Guid id,long? version,CancellationToken ct)
    {
        Transaction();var head=await Heads(db,environment).SingleOrDefaultAsync(x=>x.Id==id,ct)??throw Error("OBJECT_FORBIDDEN",403);
        var selected=version??head.Revision;
        var raw=await db.Database.SqlQuery<Raw>($"""
            SELECT body AS "Body",digest AS "Digest" FROM cp6_output_relations_v1.versions
            WHERE environment_id={environment} AND tenant_id={db.CurrentTenantId} AND relation_id={id} AND revision={selected}
            """).SingleOrDefaultAsync(ct)??throw Error("OUTPUT_RELATIONS_VERSION_NOT_FOUND",404);
        var value=Parse<OutputRelationsOriginalV1>(raw);
        if(value.EnvironmentId!=environment || value.TenantId!=db.CurrentTenantId || value.Id!=id || value.Version!=selected.ToString(CultureInfo.InvariantCulture)
            || value.CreatorId!=head.CreatorId || value.Product.Id!=head.ProductId || value.RevisionOrigin.Id!=head.RevisionId
            || value.WireOriginal!=Encoding.UTF8.GetString(Bytes(value.Wire)))throw Error("OUTPUT_RELATIONS_ORIGINAL_UNAVAILABLE",503);
        return new(new(EngineeringOutputRelationsValuesV1.Owner,id,value.Version,raw.Digest),value,Encoding.UTF8.GetString(raw.Body));
    }
    internal async Task<OutputRelationsVersionV1> Version(Guid id,EngineeringOutputRelationsBodyV1 body,EngineeringManufacturingSnapshotV1 manufacturing,
        EngineeringBomSnapshotV1 mbom,EngineeringRoutingSnapshotV1 routing,string fingerprint,EngineeringSourcePinV1[] sources,Guid actor,string mode,Guid operation,bool create,CancellationToken ct)
    {
        Transaction();var head=await Heads(db,environment).SingleOrDefaultAsync(x=>x.Id==id,ct);
        if(create!=(head==null))throw Error("OUTPUT_RELATIONS_IDENTITY_CONFLICT",409);
        var product=manufacturing.Body.ProductRef;var parent=manufacturing.Body.RevisionOrigin;
        if(head!=null && (head.ProductId!=product.Id || head.RevisionId!=parent.Id))throw Error("OUTPUT_RELATIONS_PARENT_IMMUTABLE",409);
        var revision=head==null?1:checked(head.Revision+1);var creator=head?.CreatorId??actor;var version=revision.ToString(CultureInfo.InvariantCulture);
        var wire=Wire(environment,db.CurrentTenantId,id,version,manufacturing,mbom,routing,body);
        if(head==null)await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO cp6_output_relations_v1.heads(environment_id,tenant_id,relation_id,creator_id,product_id,engineering_revision_id,name,draft_revision,control_revision)
            VALUES({environment},{db.CurrentTenantId},{id},{creator},{product.Id},{parent.Id},{body.Name},1,0)
            """,ct);
        else await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE cp6_output_relations_v1.heads SET draft_revision={revision},name={body.Name}
            WHERE environment_id={environment} AND tenant_id={db.CurrentTenantId} AND relation_id={id}
            """,ct);
        var raw=Bytes(new OutputRelationsOriginalV1(environment,db.CurrentTenantId,id,version,creator,actor,mode,body,parent,product,wire,
            Encoding.UTF8.GetString(Bytes(wire)),fingerprint,manufacturing.OriginalJson,sources,head?.Revision.ToString(CultureInfo.InvariantCulture),DateTimeOffset.UtcNow));
        if(raw.Length>33554432)throw Error("OUTPUT_RELATIONS_SIZE_LIMIT",400);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO cp6_output_relations_v1.versions(environment_id,tenant_id,relation_id,revision,body,digest,operation_id,actor_id)
            VALUES({environment},{db.CurrentTenantId},{id},{revision},{raw},{Hash(raw)},{operation},{actor})
            """,ct);
        return await Read(id,revision,ct);
    }
    internal async Task<OutputRelationsRecordV1<OutputRelationsControlV1>> Control(Guid id,CancellationToken ct)
    {
        Transaction();var raw=await db.Database.SqlQuery<Raw>($"""
            SELECT c.body AS "Body",c.digest AS "Digest" FROM cp6_output_relations_v1.controls c JOIN cp6_output_relations_v1.heads h USING(environment_id,tenant_id,relation_id)
            WHERE c.environment_id={environment} AND c.tenant_id={db.CurrentTenantId} AND c.relation_id={id} AND c.revision=h.control_revision
            """).SingleOrDefaultAsync(ct)??throw Error("OUTPUT_RELATIONS_CONTROL_UNAVAILABLE",503);
        var value=Parse<OutputRelationsControlV1>(raw);
        if(value.RelationId!=id)throw Error("OUTPUT_RELATIONS_CONTROL_UNAVAILABLE",503);
        return new(new(ControlOwner,id,value.Revision.ToString(CultureInfo.InvariantCulture),raw.Digest),value);
    }
    internal async Task<EngineeringSourceRefV1> SaveControl(OutputRelationsControlV1 value,Guid operation,CancellationToken ct)
    {
        Transaction();var raw=Bytes(value);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO cp6_output_relations_v1.controls(environment_id,tenant_id,relation_id,revision,body,digest,operation_id,actor_id)
            VALUES({environment},{db.CurrentTenantId},{value.RelationId},{value.Revision},{raw},{Hash(raw)},{operation},{value.ActorId});
            UPDATE cp6_output_relations_v1.heads SET control_revision={value.Revision}
            WHERE environment_id={environment} AND tenant_id={db.CurrentTenantId} AND relation_id={value.RelationId} AND control_revision={value.Revision-1};
            """,ct);
        var saved=await Control(value.RelationId,ct);if(saved.Reference.Digest!=Hash(raw))throw Error("OUTPUT_RELATIONS_CONTROL_INCOMPLETE",503);return saved.Reference;
    }
    internal static string RecordOwner(string kind)=>kind switch{"POLICY"=>"Main.Plm.OutputRelationsPolicy.v1","QUALIFICATION"=>"Main.Plm.OutputRelationsQualification.v1",_=>throw Error("OUTPUT_RELATIONS_RECORD_INVALID",503)};
    internal async Task<OutputRelationsRecordV1<T>> SaveRecord<T>(Guid relation,string kind,Guid id,T value,Guid operation,Guid actor,CancellationToken ct)
    {
        Transaction();var raw=Bytes(value);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO cp6_output_relations_v1.records(environment_id,tenant_id,relation_id,id,kind,body,digest,operation_id,actor_id)
            VALUES({environment},{db.CurrentTenantId},{relation},{id},{kind},{raw},{Hash(raw)},{operation},{actor})
            """,ct);
        return new(new(RecordOwner(kind),id,"1",Hash(raw)),value);
    }
    internal async Task<OutputRelationsRecordV1<T>?> Record<T>(Guid relation,string kind,Guid? id,CancellationToken ct)
    {
        Transaction();if(id==null)return null;
        var raw=await db.Database.SqlQuery<Raw>($"""
            SELECT body AS "Body",digest AS "Digest" FROM cp6_output_relations_v1.records
            WHERE environment_id={environment} AND tenant_id={db.CurrentTenantId} AND relation_id={relation} AND kind={kind} AND id={id}
            """).SingleOrDefaultAsync(ct)??throw Error("OUTPUT_RELATIONS_RECORD_UNAVAILABLE",503);
        return new(new(RecordOwner(kind),id.Value,"1",raw.Digest),Parse<T>(raw));
    }
}
