using System.Text.Json;
using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore;
using static CP6.Core.Services.Wms.Replenish.ReplenishPlanningValuesV1;
namespace CP6.Core.Services.Wms.Replenish;

internal sealed class ReplenishPlanningStoreV1(CP6Context db,Guid environment)
{
    internal sealed record Document(Guid Id,string Kind,Guid CreatorId,Guid? DepartmentId,string ScopeKey,byte[] Body);
    internal sealed record SavedCommand(string Action,string InputDigest,Guid ResultId,byte[] Body);
    internal Task Fence(bool write,CancellationToken ct)=>db.Database.ExecuteSqlRawAsync(
        "LOCK TABLE cp6_replenish_v1.current_observations,cp6_replenish_v1.documents IN "+(write?"SHARE ROW EXCLUSIVE":"SHARE")+" MODE",ct);
    internal static T Decode<T>(byte[] body)=>JsonSerializer.Deserialize<T>(body,RepJson)!;
    internal IQueryable<Document> Documents()=>db.Database.SqlQuery<Document>($"""
        SELECT id AS "Id",kind AS "Kind",creator_id AS "CreatorId",department_id AS "DepartmentId",scope_key AS "ScopeKey",body AS "Body"
        FROM cp6_replenish_v1.documents WHERE tenant_id={db.CurrentTenantId} AND environment_id={environment}
        """);
    internal async Task<Document> Read(Guid id,string? kind,CancellationToken ct)
        =>await Documents().Where(x=>x.Id==id&&(kind==null||x.Kind==kind)).SingleOrDefaultAsync(ct)??throw Error("REP_NOT_VISIBLE",404);
    internal async Task<Document> CurrentOrder(Guid id,CancellationToken ct)
    {
        var snapshot=await db.Database.SqlQuery<Guid?>($"""
            SELECT v.snapshot_id AS "Value" FROM cp6_replenish_v1.order_heads h
            JOIN cp6_replenish_v1.order_versions v USING(tenant_id,environment_id,order_id,revision)
            WHERE h.tenant_id={db.CurrentTenantId} AND h.environment_id={environment} AND h.order_id={id}
            """).SingleOrDefaultAsync(ct)??throw Error("REP_NOT_VISIBLE",404);
        return await Read(snapshot,null,ct);
    }
    internal async Task SaveOrderVersion(Guid id,Guid snapshot,RepOrderV1 order,Guid actor,Guid? department,CancellationToken ct)
    {
        await Save(snapshot,id==snapshot?"order":"order-revision",actor,department,Hash(order.Input.Scope),order,ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO cp6_replenish_v1.order_versions VALUES({db.CurrentTenantId},{environment},{id},{order.Revision},{snapshot});
            INSERT INTO cp6_replenish_v1.order_heads VALUES({db.CurrentTenantId},{environment},{id},{order.Revision})
            ON CONFLICT(tenant_id,environment_id,order_id) DO UPDATE SET revision=EXCLUDED.revision;
            """,ct);
    }
    internal Task<SavedCommand?> Command(Guid actor,string key,CancellationToken ct)=>db.Database.SqlQuery<SavedCommand>($"""
        SELECT action AS "Action",input_digest AS "InputDigest",result_id AS "ResultId",result_body AS "Body" FROM cp6_replenish_v1.commands
        WHERE tenant_id={db.CurrentTenantId} AND environment_id={environment} AND actor_id={actor} AND command_key={key}
        """).SingleOrDefaultAsync(ct);
    internal async Task SaveCommand(Guid actor,string key,string action,string digest,Guid result,object resultBody,CancellationToken ct)
        =>await db.Database.ExecuteSqlInterpolatedAsync($"""
          INSERT INTO cp6_replenish_v1.commands VALUES({db.CurrentTenantId},{environment},{actor},{key},{action},{digest},{result},{Bytes(resultBody)})
          """,ct);
    internal async Task Save(Guid id,string kind,Guid actor,Guid? department,string scope,object body,CancellationToken ct)
        =>await db.Database.ExecuteSqlInterpolatedAsync($"""
          INSERT INTO cp6_replenish_v1.documents VALUES({db.CurrentTenantId},{environment},{id},{kind},{actor},{department},{scope},{Bytes(body)})
          """,ct);
    internal async Task<RepObservationV1> Observation(string key,CancellationToken ct)
    {
        var body=await db.Database.SqlQuery<byte[]>($"""
            SELECT o.body AS "Value" FROM cp6_replenish_v1.current_observations h
            JOIN cp6_replenish_v1.observations o USING(tenant_id,environment_id,scope_key,cut_key)
            WHERE h.tenant_id={db.CurrentTenantId} AND h.environment_id={environment} AND h.scope_key={key}
            """).SingleOrDefaultAsync(ct)??throw Error("REP_OBSERVATION_UNAVAILABLE",503);
        return Decode<RepObservationV1>(body);
    }
    internal async Task<RepObservationV1> ObservationAt(string key,RepRefV1 reference,CancellationToken ct)
    {
        var body=await db.Database.SqlQuery<byte[]>($"""
            SELECT body AS "Value" FROM cp6_replenish_v1.observations
            WHERE tenant_id={db.CurrentTenantId} AND environment_id={environment} AND scope_key={key} AND cut_key={Hash(reference)}
            """).SingleOrDefaultAsync(ct)??throw Error("REP_SOURCE_CUT_NOT_FOUND",412);return Decode<RepObservationV1>(body);
    }
    // Trusted upstream ingestion boundary. Caller owns the transaction; public REP commands cannot publish rules/stock.
    internal async Task Publish(RepObservationV1 observation,CancellationToken ct=default)
    {
        if(db.Database.CurrentTransaction==null)throw Error("REP_SOURCE_TRANSACTION_REQUIRED",503);
        ValidateObservation(observation);await Fence(true,ct);var key=Hash(observation.Scope);var body=Bytes(observation);var cut=Hash(observation.CutRef);
        var old=await db.Database.SqlQuery<byte[]>($"""
            SELECT body AS "Value" FROM cp6_replenish_v1.observations
            WHERE tenant_id={db.CurrentTenantId} AND environment_id={environment} AND scope_key={key} AND cut_key={cut}
            """).SingleOrDefaultAsync(ct);
        if(old!=null){if(!old.AsSpan().SequenceEqual(body))throw Error("REP_SOURCE_CUT_CONFLICT",409);return;}
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO cp6_replenish_v1.observations(tenant_id,environment_id,scope_key,cut_key,body)
            VALUES({db.CurrentTenantId},{environment},{key},{cut},{body});
            INSERT INTO cp6_replenish_v1.current_observations VALUES({db.CurrentTenantId},{environment},{key},{cut})
            ON CONFLICT(tenant_id,environment_id,scope_key) DO UPDATE SET cut_key=EXCLUDED.cut_key;
            """,ct);
    }
}
