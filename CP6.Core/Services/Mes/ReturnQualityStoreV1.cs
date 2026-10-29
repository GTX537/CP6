using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Wms;
using Microsoft.EntityFrameworkCore;
using static CP6.Core.Services.Wms.RmaFormalStoreV1;

namespace CP6.Core.Services.Mes;

internal sealed class ReturnQualityStoreV1(CP6Context db)
{
    internal sealed class Head
    {
        public Guid Id { get; init; }
        public Guid Application { get; init; }
        public Guid Line { get; init; }
        public int Revision { get; init; }
        public byte[] Body { get; init; } = null!;
    }
    internal IQueryable<Head> Heads() => db.Database.SqlQuery<Head>($"""
        SELECT inspection_id AS "Id",application_id AS "Application",line_id AS "Line",revision AS "Revision",body AS "Body"
        FROM cp6_return_quality_v1.inspections WHERE tenant_id={db.CurrentTenantId}
        """);
    internal static T Decode<T>(byte[] value) => JsonSerializer.Deserialize<T>(value,RmaFormalStoreV1.Json)!;
    internal static FulfilmentRefV1 InitialHead(ReturnQualityInspectionV1 inspection)
        => RmaInboundAdapterV1.Ref("QA-NCR-01.ReturnHead",inspection.Id.ToString(),new { inspection.Reference,Revision = 0 });
    internal async Task<ReturnQualityViewV1> View(Head head, int? revision, CancellationToken ct)
    {
        var inspection = Decode<ReturnQualityInspectionV1>(head.Body); var version = revision ?? head.Revision;
        if (version < 0 || version > head.Revision) throw Error("QUALITY_VERSION_NOT_FOUND",404);
        var decision = version == 0 ? null : Decode<ReturnQualityDecisionRecordV1>(await db.Database.SqlQuery<byte[]>($"SELECT body AS \"Value\" FROM cp6_return_quality_v1.decisions WHERE tenant_id={db.CurrentTenantId} AND inspection_id={head.Id} AND revision={version}").SingleAsync(ct));
        var bytes = await db.Database.SqlQuery<byte[]>($"SELECT body AS \"Value\" FROM cp6_return_quality_v1.observations WHERE tenant_id={db.CurrentTenantId} AND inspection_id={head.Id} ORDER BY ordinal DESC LIMIT 1").SingleOrDefaultAsync(ct);
        var observation = bytes == null ? null : Decode<RmaQualityObservationV1>(bytes);
        return new(inspection,decision?.Result.CurrentHeadProof ?? InitialHead(inspection),decision,observation,
            observation != null && decision?.Result.DecisionRef == observation.Decision.DecisionRef && version == head.Revision && decision.Result.DecisionState == "ACTIVE");
    }
    internal Task Start(ReturnQualityInspectionV1 value, CancellationToken ct) => db.Database.ExecuteSqlInterpolatedAsync($"""
        INSERT INTO cp6_return_quality_v1.inspections(tenant_id,inspection_id,application_id,line_id,revision,body)
        VALUES({db.CurrentTenantId},{value.Id},{value.Source.ApplicationId},{value.Source.LineId},0,{Encode(value)})
        """,ct);
    internal async Task Save(Guid inspection, int revision, ReturnQualityDecisionRecordV1 value, CancellationToken ct)
    {
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO cp6_return_quality_v1.decisions(tenant_id,inspection_id,revision,body) VALUES({db.CurrentTenantId},{inspection},{revision},{Encode(value)})",ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE cp6_return_quality_v1.inspections SET revision={revision} WHERE tenant_id={db.CurrentTenantId} AND inspection_id={inspection}",ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO cp6_return_quality_v1.outbox(tenant_id,event_id,inspection_id,body) VALUES({db.CurrentTenantId},{Guid.NewGuid()},{inspection},{Encode(value)})",ct);
    }
    internal Task Observe(RmaQualityObservationV1 value, CancellationToken ct) => db.Database.ExecuteSqlInterpolatedAsync($"""
        INSERT INTO cp6_return_quality_v1.observations(tenant_id,inspection_id,body)
        VALUES({db.CurrentTenantId},{value.InspectionId},{Encode(value)})
        """,ct);
}
