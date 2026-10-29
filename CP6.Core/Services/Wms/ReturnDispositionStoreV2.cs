using System.Text.Json;
using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore;
using static CP6.Core.Services.Wms.RmaFormalStoreV1;

namespace CP6.Core.Services.Wms;

internal sealed class ReturnDispositionStoreV2(CP6Context db)
{
    internal sealed class Row
    {
        public Guid Id { get; init; }
        public Guid App { get; init; }
        public Guid Line { get; init; }
        public Guid Actor { get; init; }
        public string Kind { get; init; } = null!;
        public string State { get; init; } = null!;
        public byte[] Body { get; init; } = null!;
        public byte[]? Result { get; init; }
        public byte[]? Proof { get; init; }
    }
    internal IQueryable<Row> Rows() => db.Database.SqlQuery<Row>($"""
        SELECT work_id AS "Id",application_id AS "App",line_id AS "Line",actor_id AS "Actor",kind AS "Kind",state AS "State",body AS "Body",result AS "Result",proof AS "Proof"
        FROM cp6_return_disposition_v2.works WHERE tenant_id={db.CurrentTenantId}
        """);
    internal static T Decode<T>(byte[] bytes) => JsonSerializer.Deserialize<T>(bytes,RmaFormalStoreV1.Json)!;
    internal static FulfilmentRefV1 Head(Row row) => new("Main.ReturnDisposition.Work.v2",row.Id.ToString(),row.State =="PREPARED" ? "1" : "2",
        Digest(new { row.Id,row.Kind,row.State,Preparation = Decode<ReturnDispositionPreparationV2>(row.Body),Result = row.Result,Proof = row.Proof }));
    internal static ReturnDispositionViewV2 View(Row row,bool qualityCurrent) => new(Decode<ReturnDispositionPreparationV2>(row.Body),Head(row),row.State,
        row.Result ==null ? null : Decode<ReturnDispositionReceiptV2>(row.Result),row.Proof ==null ? null : Decode<FulfilmentRefV1>(row.Proof),qualityCurrent);
    internal Task Prepare(ReturnDispositionPreparationV2 p,CancellationToken ct) => db.Database.ExecuteSqlInterpolatedAsync($"""
        INSERT INTO cp6_return_disposition_v2.works(tenant_id,work_id,application_id,line_id,actor_id,kind,state,body)
        VALUES({db.CurrentTenantId},{p.WorkId},{p.ApplicationId},{p.Source.LineId},{p.ActorId},{p.Definition.Kind},'PREPARED',{Encode(p)})
        """,ct);
    internal async Task Apply(Guid id,ReturnDispositionReceiptV2 result,CancellationToken ct)
    {
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE cp6_return_disposition_v2.works SET state='APPLIED',result={Encode(result)} WHERE tenant_id={db.CurrentTenantId} AND work_id={id}",ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO cp6_return_disposition_v2.receipts(tenant_id,work_id,owner,body) VALUES({db.CurrentTenantId},{id},{result.Reference.Owner},{Encode(result)})",ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO cp6_return_disposition_v2.outbox(tenant_id,event_id,work_id,body) VALUES({db.CurrentTenantId},{Guid.NewGuid()},{id},{Encode(result)})",ct);
    }
    internal Task Seal(Guid id,FulfilmentRefV1 proof,CancellationToken ct) => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE cp6_return_disposition_v2.works SET state='SEALED_NO_EFFECT',proof={Encode(proof)} WHERE tenant_id={db.CurrentTenantId} AND work_id={id}",ct);
}
