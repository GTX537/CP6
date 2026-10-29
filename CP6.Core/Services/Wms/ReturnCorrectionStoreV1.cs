using System.Text.Json;
using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore;
using static CP6.Core.Services.Wms.RmaFormalStoreV1;
using Ranges = CP6.Core.Services.Wms.RmaRangeSetV1;

namespace CP6.Core.Services.Wms;

internal sealed class ReturnCorrectionStoreV1(CP6Context db)
{
    internal sealed class Row
    {
        public Guid Id { get; init; }
        public Guid App { get; init; }
        public Guid Line { get; init; }
        public Guid Target { get; init; }
        public string Axis { get; init; } =null!;
        public string State { get; init; } =null!;
        public byte[] Body { get; init; } =null!;
        public byte[]? Approval { get; init; }
        public byte[]? Preparation { get; init; }
        public byte[]? Result { get; init; }
        public byte[]? Proof { get; init; }
    }
    internal IQueryable<Row> Rows() =>db.Database.SqlQuery<Row>($"""
        SELECT proposal_id AS "Id",application_id AS "App",line_id AS "Line",target_id AS "Target",axis AS "Axis",state AS "State",
          body AS "Body",approval AS "Approval",preparation AS "Preparation",result AS "Result",proof AS "Proof"
        FROM cp6_return_correction_v1.proposals WHERE tenant_id={db.CurrentTenantId}
        """);
    internal static T Decode<T>(byte[] body) =>JsonSerializer.Deserialize<T>(body,RmaFormalStoreV1.Json)!;
    internal static FulfilmentRefV1 Head(Row r) =>RmaInboundAdapterV1.Ref("Main.Wms.ReturnCorrection.Head.v1",r.Id.ToString(),new { r.State,r.Body,r.Approval,r.Preparation,r.Result,r.Proof });
    internal async Task<FulfilmentRangeV1[]> Reversed(string axis,Guid target,CancellationToken ct)
        =>Ranges.Wire(Ranges.Union((await Rows().Where(x => x.Axis ==axis && x.Target ==target && x.State =="POSTED").ToArrayAsync(ct))
            .SelectMany(x => Ranges.Read(Decode<ReturnCorrectionCandidateV1>(x.Body).Ranges))));
    internal async Task<FulfilmentRangeV1[]> Fenced(Guid line,Guid? exclude,CancellationToken ct)
        =>Ranges.Wire(Ranges.Union((await Rows().Where(x => x.Line ==line && x.Id !=exclude && (x.State =="SUBMITTED" || x.State =="APPROVED")).ToArrayAsync(ct))
            .SelectMany(x => Ranges.Read(Decode<ReturnCorrectionCandidateV1>(x.Body).Ranges))));
    internal Task Insert(ReturnCorrectionCandidateV1 c,CancellationToken ct) =>db.Database.ExecuteSqlInterpolatedAsync($"""
        INSERT INTO cp6_return_correction_v1.proposals(tenant_id,proposal_id,application_id,line_id,target_id,axis,state,body)
        VALUES({db.CurrentTenantId},{c.Id},{c.ApplicationId},{c.Target.Source.LineId},{c.Target.Id},{c.Target.Axis},'DRAFT',{Encode(c)})
        """,ct);
    internal Task Version(ReturnCorrectionCandidateV1 c,CancellationToken ct) =>db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO cp6_return_correction_v1.versions(tenant_id,proposal_id,version,body) VALUES({db.CurrentTenantId},{c.Id},{c.Version},{Encode(c)})",ct);
    internal Task Edit(ReturnCorrectionCandidateV1 c,CancellationToken ct) =>db.Database.ExecuteSqlInterpolatedAsync($"UPDATE cp6_return_correction_v1.proposals SET body={Encode(c)} WHERE tenant_id={db.CurrentTenantId} AND proposal_id={c.Id}",ct);
    internal async Task Event(Guid id,string action,object value,CancellationToken ct)
        =>await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO cp6_return_correction_v1.events(tenant_id,event_id,proposal_id,action,body) VALUES({db.CurrentTenantId},{Guid.NewGuid()},{id},{action},{Encode(value)})",ct);
    internal Task Submit(Guid id,CancellationToken ct) =>db.Database.ExecuteSqlInterpolatedAsync($"UPDATE cp6_return_correction_v1.proposals SET state='SUBMITTED' WHERE tenant_id={db.CurrentTenantId} AND proposal_id={id}",ct);
    internal Task Approve(Guid id,ReturnCorrectionApprovalV1 approval,CancellationToken ct) =>db.Database.ExecuteSqlInterpolatedAsync($"UPDATE cp6_return_correction_v1.proposals SET state='APPROVED',approval={Encode(approval)} WHERE tenant_id={db.CurrentTenantId} AND proposal_id={id}",ct);
    internal Task Prepare(Guid id,ReturnCorrectionPreparationV1 value,CancellationToken ct) =>db.Database.ExecuteSqlInterpolatedAsync($"UPDATE cp6_return_correction_v1.proposals SET preparation={Encode(value)} WHERE tenant_id={db.CurrentTenantId} AND proposal_id={id}",ct);
    internal Task Withdraw(Guid id,FulfilmentRefV1 proof,CancellationToken ct) =>db.Database.ExecuteSqlInterpolatedAsync($"UPDATE cp6_return_correction_v1.proposals SET state='WITHDRAWN',proof={Encode(proof)} WHERE tenant_id={db.CurrentTenantId} AND proposal_id={id}",ct);
    internal async Task Apply(Guid id,ReturnCorrectionReceiptV1 result,CancellationToken ct)
    {
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE cp6_return_correction_v1.proposals SET state='POSTED',result={Encode(result)} WHERE tenant_id={db.CurrentTenantId} AND proposal_id={id}",ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO cp6_return_correction_v1.receipts(tenant_id,proposal_id,body) VALUES({db.CurrentTenantId},{id},{Encode(result)})",ct);
        await Event(id,"APPLIED",result,ct);
    }
}
