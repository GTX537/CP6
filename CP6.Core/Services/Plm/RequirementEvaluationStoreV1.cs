using System.Globalization;
using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Oa;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using static CP6.Core.Services.Plm.EngineeringProductValuesV1;
using static CP6.Core.Services.Plm.RequirementEvaluationValuesV1;
namespace CP6.Core.Services.Plm;
internal sealed class RequirementEvaluationStoreV1(CP6Context db,Guid environment,string mode)
{
    private IDbContextTransaction? fenced;
    internal static byte[] Bytes<T>(T value)=>ApprovalCanonicalV2.Bytes(value);
    internal static string Hash(byte[] value)=>ApprovalCanonicalV2.Hash(value);
    internal static DateTimeOffset Now()=>DateTimeOffset.FromUnixTimeMilliseconds(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
    internal static string RecordOwner(string kind)=>kind switch{
        "POLICY"=>"Main.Plm.RequirementReviewPolicy.v1","PACKET"=>"Main.Plm.RequirementReviewPacket.v1",
        "VOTE"=>"Main.Plm.RequirementReviewVote.v1","APPROVAL"=>"Main.Plm.RequirementApproval.v1",_=>throw Error("REQUIREMENT_REVIEW_RECORD_INVALID",503)};
    private void Transaction(bool ready=true)
    {if(!db.Database.IsNpgsql() || db.CurrentTenantId==Guid.Empty || environment==Guid.Empty || db.Database.CurrentTransaction==null
        || ready&&!ReferenceEquals(fenced,db.Database.CurrentTransaction))throw Error("AUTHORITY_FENCE_UNAVAILABLE",503);}
    internal async Task Fence(bool write,CancellationToken ct)
    {
        Transaction(false);
        await db.Database.ExecuteSqlRawAsync(write
            ?"LOCK TABLE cp6_requirement_evaluation_v1.heads,cp6_requirement_evaluation_v1.controls,cp6_requirement_evaluation_v1.records IN SHARE ROW EXCLUSIVE MODE"
            :"LOCK TABLE cp6_requirement_evaluation_v1.heads,cp6_requirement_evaluation_v1.controls,cp6_requirement_evaluation_v1.records IN SHARE MODE",ct);
        if(!await db.Database.SqlQueryRaw<bool>("SELECT cp6_requirement_evaluation_v1.ready() AS \"Value\"").SingleAsync(ct))throw Error("REQUIREMENT_REVIEW_STORE_UNAVAILABLE",503);
        fenced=db.Database.CurrentTransaction;
    }
    private sealed record Raw(byte[] Body,string Digest);
    private static T Parse<T>(Raw r)
    {
        try{if(Hash(r.Body)!=r.Digest)throw new JsonException();return JsonSerializer.Deserialize<T>(r.Body,ApprovalBindingOwnerV2.Json)??throw new JsonException();}
        catch(JsonException){throw Error("REQUIREMENT_REVIEW_ORIGINAL_UNAVAILABLE",503);}
    }
    private sealed record Head(long Revision,string Mode);
    internal async Task<RequirementEvaluationRecordV1<RequirementEvaluationControlV1>?> Control(Guid id,CancellationToken ct,long? revision=null)
    {
        Transaction();var h=await db.Database.SqlQuery<Head>($"""
            SELECT revision AS "Revision",evidence_mode AS "Mode" FROM cp6_requirement_evaluation_v1.heads
            WHERE environment_id={environment} AND tenant_id={db.CurrentTenantId} AND requirement_id={id}
            """).SingleOrDefaultAsync(ct);if(h==null)return null;
        if(h.Mode!=mode)throw Error("REQUIREMENT_REVIEW_MODE_MISMATCH",403);var selected=revision??h.Revision;
        var r=await db.Database.SqlQuery<Raw>($"""
            SELECT body AS "Body",digest AS "Digest" FROM cp6_requirement_evaluation_v1.controls
            WHERE environment_id={environment} AND tenant_id={db.CurrentTenantId} AND requirement_id={id} AND revision={selected}
            """).SingleOrDefaultAsync(ct)??throw Error(revision==null?"REQUIREMENT_REVIEW_CONTROL_UNAVAILABLE":"REQUIREMENT_REVIEW_CONTROL_NOT_FOUND",revision==null?503:404);
        var v=Parse<RequirementEvaluationControlV1>(r);
        if(v.RequirementId!=id || v.Revision!=selected)throw Error("REQUIREMENT_REVIEW_CONTROL_UNAVAILABLE",503);
        return new(new(ControlOwner,id,v.Revision.ToString(CultureInfo.InvariantCulture),r.Digest),v);
    }
    internal async Task EnsureHead(Guid id,CancellationToken ct)
    {
        Transaction();await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO cp6_requirement_evaluation_v1.heads(environment_id,tenant_id,requirement_id,evidence_mode,revision)
            VALUES({environment},{db.CurrentTenantId},{id},{mode},0) ON CONFLICT DO NOTHING
            """,ct);
    }
    internal async Task<EngineeringSourceRefV1> SaveControl(RequirementEvaluationControlV1 v,Guid operation,CancellationToken ct)
    {
        Transaction();var bytes=Bytes(v);
        if(await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO cp6_requirement_evaluation_v1.controls(environment_id,tenant_id,requirement_id,revision,body,digest,operation_id,actor_id)
            VALUES({environment},{db.CurrentTenantId},{v.RequirementId},{v.Revision},{bytes},{Hash(bytes)},{operation},{v.ActorId})
            """,ct)!=1)throw Error("REQUIREMENT_REVIEW_WRITE_INCOMPLETE",503);
        if(await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE cp6_requirement_evaluation_v1.heads SET revision={v.Revision}
            WHERE environment_id={environment} AND tenant_id={db.CurrentTenantId} AND requirement_id={v.RequirementId} AND revision={v.Revision-1}
            """,ct)!=1)throw Error("REQUIREMENT_REVIEW_CHANGED",412);
        var saved=await Control(v.RequirementId,ct);
        if(saved?.Reference.Digest!=Hash(bytes))throw Error("REQUIREMENT_REVIEW_WRITE_INCOMPLETE",503);return saved.Reference;
    }
    internal async Task<RequirementEvaluationRecordV1<T>> Record<T>(Guid requirement,string kind,Guid id,T value,Guid operation,Guid actor,CancellationToken ct)
    {
        Transaction();var bytes=Bytes(value);if(bytes.Length>67108864)throw Error("REQUIREMENT_REVIEW_PACKET_SIZE_LIMIT",400);
        Guid? packet=value is RequirementEvaluationVoteV1 vote?vote.PacketId:null;
        if(await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO cp6_requirement_evaluation_v1.records(environment_id,tenant_id,requirement_id,id,kind,body,digest,operation_id,actor_id,packet_id)
            VALUES({environment},{db.CurrentTenantId},{requirement},{id},{kind},{bytes},{Hash(bytes)},{operation},{actor},{packet})
            """,ct)!=1)throw Error("REQUIREMENT_REVIEW_WRITE_INCOMPLETE",503);
        var saved=await Record<T>(requirement,kind,id,ct);
        if(saved?.Reference.Digest!=Hash(bytes))throw Error("REQUIREMENT_REVIEW_WRITE_INCOMPLETE",503);return saved;
    }
    internal async Task<RequirementEvaluationRecordV1<T>?> Record<T>(Guid requirement,string kind,Guid? id,CancellationToken ct)
    {
        Transaction();if(id==null)return null;
        var r=await db.Database.SqlQuery<Raw>($"""
            SELECT body AS "Body",digest AS "Digest" FROM cp6_requirement_evaluation_v1.records
            WHERE environment_id={environment} AND tenant_id={db.CurrentTenantId} AND requirement_id={requirement} AND id={id} AND kind={kind}
            """).SingleOrDefaultAsync(ct)??throw Error("REQUIREMENT_REVIEW_RECORD_UNAVAILABLE",503);
        return new(new(RecordOwner(kind),id.Value,"1",r.Digest),Parse<T>(r));
    }
    internal async Task<RequirementEvaluationRecordV1<RequirementEvaluationVoteV1>[]> Votes(Guid requirement,Guid? packet,CancellationToken ct,long? through=null)
    {
        Transaction();if(packet==null)return [];var limit=through??long.MaxValue;
        var ids=await db.Database.SqlQuery<Guid>($"""
            SELECT r.id AS "Value" FROM cp6_requirement_evaluation_v1.records r WHERE r.environment_id={environment} AND r.tenant_id={db.CurrentTenantId}
            AND r.requirement_id={requirement} AND r.kind='VOTE' AND r.packet_id={packet}
            AND EXISTS(SELECT FROM cp6_requirement_evaluation_v1.controls c WHERE c.environment_id=r.environment_id AND c.tenant_id=r.tenant_id
                AND c.requirement_id=r.requirement_id AND c.operation_id=r.operation_id AND c.revision<={limit}) ORDER BY r.id
            """).ToArrayAsync(ct);
        var result=new List<RequirementEvaluationRecordV1<RequirementEvaluationVoteV1>>();
        foreach(var id in ids)result.Add((await Record<RequirementEvaluationVoteV1>(requirement,"VOTE",id,ct))!);return result.ToArray();
    }
    internal static async Task GuardRevision(CP6Context db,Guid id,CancellationToken ct)
    {
        await db.Database.ExecuteSqlRawAsync("LOCK TABLE cp6_requirement_evaluation_v1.heads,cp6_requirement_evaluation_v1.controls,cp6_requirement_evaluation_v1.records IN SHARE MODE",ct);
        if(!await db.Database.SqlQueryRaw<bool>("SELECT cp6_requirement_evaluation_v1.ready() AS \"Value\"").SingleAsync(ct))throw Error("REQUIREMENT_REVIEW_STORE_UNAVAILABLE",503);
        if(await db.Database.SqlQuery<int>($"""
            SELECT 1 AS "Value" FROM cp6_requirement_evaluation_v1.heads h JOIN cp6_requirement_evaluation_v1.controls c
            USING(environment_id,tenant_id,requirement_id,revision)
            WHERE h.tenant_id={db.CurrentTenantId} AND h.requirement_id={id} AND convert_from(c.body,'UTF8')::jsonb->>'state'='IN_REVIEW'
            """).AnyAsync(ct))throw Error("REQUIREMENT_REVIEW_FROZEN",409);
    }
}
