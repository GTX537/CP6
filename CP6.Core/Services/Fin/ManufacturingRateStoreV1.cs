using System.Text.Json;
using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore;
using static CP6.Core.Services.Fin.ManufacturingFinanceScopeV1;

namespace CP6.Core.Services.Fin;

internal sealed class ManufacturingRateStoreV1(CP6Context db)
{
    private Guid Tenant => db.CurrentTenantId;

    internal async Task<RateRow> RateAsync(Guid ledger, Guid id, CancellationToken ct)
        => await db.Database.SqlQuery<RateRow>($"""
            SELECT id AS "Id",work_center_id AS "WorkCenterId",operation_id AS "OperationId",
                center_id AS "CenterId",center_version_id AS "CenterVersionId",
                policy_version_id AS "PolicyVersionId",basis_id AS "BasisId",maker AS "Maker",
                labor_rate AS "LaborRate",overhead_rate AS "OverheadRate",valid_from AS "From",to_exclusive AS "ToExclusive",
                state AS "State",revision AS "Revision",reviewer AS "Reviewer",review_qualification_id AS "ReviewQualificationId",
                review_original_actor AS "ReviewOriginalActor"
            FROM cp6_fin_mfg_v1.rate_versions WHERE tenant_id={Tenant} AND ledger_id={ledger} AND id={id}
            """).SingleOrDefaultAsync(ct) ?? throw Error("RATE_VERSION_NOT_FOUND", 404);

    internal async Task<bool> RequireInputsAsync(LedgerRow ledger, RateRow rate, CancellationToken ct)
    {
        var managedCenter = await db.Database.SqlQuery<bool?>($"""
            SELECT (v.submission_id IS NOT NULL) AS "Value" FROM cp6_fin_mfg_v1.current_center_versions v
              WHERE v.tenant_id={Tenant} AND v.ledger_id={ledger.Id} AND v.root_id={rate.CenterId}
              AND v.version_id={rate.CenterVersionId} AND (v.operation_id IS NULL OR v.operation_id={rate.OperationId})
              AND v.effective_dates @> daterange({rate.From},{rate.ToExclusive},'[)')
              AND NOT EXISTS(SELECT 1 FROM cp6_fin_mfg_v1.center_calendar competing
                WHERE competing.tenant_id=v.tenant_id AND competing.ledger_id=v.ledger_id AND competing.root_id=v.root_id
                  AND competing.version_id<>v.version_id AND competing.enabled
                  AND competing.effective_dates && datemultirange(daterange({rate.From},{rate.ToExclusive},'[)')))
            """).SingleOrDefaultAsync(ct);
        if (managedCenter == null)
            throw Error("COST_CENTER_VERSION_UNAVAILABLE", 409);

        if (!await db.Database.SqlQuery<bool>($"""
            SELECT EXISTS(SELECT 1 FROM cp6_fin_mfg_v1.policies p
              JOIN cp6_fin_mfg_v1.rate_bases b ON (b.tenant_id,b.ledger_id,b.policy_version_id)=(p.tenant_id,p.ledger_id,p.version_id)
              WHERE p.tenant_id={Tenant} AND p.ledger_id={ledger.Id} AND p.version_id={rate.PolicyVersionId}
                AND p.currency={ledger.Currency} AND p.enabled AND b.enabled AND b.id={rate.BasisId}
                AND b.work_center_id={rate.WorkCenterId} AND b.operation_id={rate.OperationId}
                AND daterange(p.valid_from,p.to_exclusive,'[)') @> daterange({rate.From},{rate.ToExclusive},'[)')
                AND daterange(b.valid_from,b.to_exclusive,'[)') @> daterange({rate.From},{rate.ToExclusive},'[)')
                AND (({rate.LaborRate}>0 AND {rate.OverheadRate}>0) OR (p.allow_zero_rate AND b.zero_evidenced))
            ) AS "Value"
            """).SingleAsync(ct))
            throw Error("APPROVED_RATE_BASIS_REQUIRED", 422);
        return managedCenter.Value;
    }

    internal async Task InsertAsync(Guid ledger, RateRow rate, CancellationToken ct)
    {
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO cp6_fin_mfg_v1.rate_heads (tenant_id,ledger_id,work_center_id,operation_id)
            VALUES ({Tenant},{ledger},{rate.WorkCenterId},{rate.OperationId}) ON CONFLICT DO NOTHING
            """, ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO cp6_fin_mfg_v1.rate_versions
                (tenant_id,ledger_id,id,work_center_id,operation_id,center_id,center_version_id,policy_version_id,basis_id,
                labor_rate,overhead_rate,valid_from,to_exclusive,maker)
            VALUES ({Tenant},{ledger},{rate.Id},{rate.WorkCenterId},{rate.OperationId},{rate.CenterId},
                {rate.CenterVersionId},{rate.PolicyVersionId},{rate.BasisId},{rate.LaborRate},{rate.OverheadRate},
                {rate.From},{rate.ToExclusive},{rate.Maker})
            """, ct);
    }

    internal Task<int> SubmitAsync(Guid ledger, RateRow rate, CancellationToken ct)
        => db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE cp6_fin_mfg_v1.rate_versions SET state='SUBMITTED', revision=revision+1, submitted_at=clock_timestamp()
            WHERE tenant_id={Tenant} AND ledger_id={ledger} AND id={rate.Id} AND revision={rate.Revision}
            """, ct);

    internal Task<int> ApproveAsync(Guid ledger, RateRow rate, Guid reviewer, Guid qualification, CancellationToken ct)
        => db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE cp6_fin_mfg_v1.rate_versions SET state='APPROVED', revision=revision+1,
                reviewer={reviewer},review_qualification_id={qualification},reviewed_at=clock_timestamp(),review_original_actor=true
            WHERE tenant_id={Tenant} AND ledger_id={ledger} AND id={rate.Id} AND revision={rate.Revision}
            """, ct);

    internal Task<long> HeadAsync(Guid ledger, RateRow rate, CancellationToken ct)
        => db.Database.SqlQuery<long>($"""
            SELECT revision AS "Value" FROM cp6_fin_mfg_v1.rate_heads
            WHERE tenant_id={Tenant} AND ledger_id={ledger} AND work_center_id={rate.WorkCenterId} AND operation_id={rate.OperationId}
            """).SingleAsync(ct);

    internal Task<ManufacturingRateSegmentV1[]> SegmentsAsync(Guid ledger, RateRow rate, CancellationToken ct)
        => db.Database.SqlQuery<ManufacturingRateSegmentV1>($"""
            SELECT rate_id AS "RateId",valid_from AS "From",to_exclusive AS "ToExclusive"
            FROM cp6_fin_mfg_v1.timeline
            WHERE tenant_id={Tenant} AND ledger_id={ledger} AND work_center_id={rate.WorkCenterId} AND operation_id={rate.OperationId}
            ORDER BY valid_from
            """).ToArrayAsync(ct);

    internal Task InsertPreviewAsync(Guid ledger, RateRow rate, Guid subject, Guid token, long head, CancellationToken ct)
        => db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO cp6_fin_mfg_v1.release_previews
                (token,tenant_id,ledger_id,rate_id,subject_id,rate_revision,timeline_revision,expires_at)
            VALUES ({token},{Tenant},{ledger},{rate.Id},{subject},{rate.Revision},{head},clock_timestamp()+interval '10 minutes')
            """, ct);

    internal async Task<(bool Released, Guid? SourceProductId)> RequirePreviewAsync(Guid ledger, Guid id, Guid subject,
        Guid preview, CancellationToken ct)
    {
        var row = await db.Database.SqlQuery<PreviewRow>($"""
            SELECT p.rate_revision AS "RateRevision", p.timeline_revision AS "TimelineRevision",
                r.revision AS "CurrentRateRevision", h.revision AS "CurrentTimelineRevision",
                p.expires_at>clock_timestamp() AS "Live",
                (e.preview_token IS NOT NULL) AS "Released",e.source_product_id AS "SourceProductId"
            FROM cp6_fin_mfg_v1.release_previews p
            JOIN cp6_fin_mfg_v1.rate_versions r ON (r.tenant_id,r.ledger_id,r.id)=(p.tenant_id,p.ledger_id,p.rate_id)
            JOIN cp6_fin_mfg_v1.rate_heads h ON (h.tenant_id,h.ledger_id,h.work_center_id,h.operation_id)
                =(r.tenant_id,r.ledger_id,r.work_center_id,r.operation_id)
            LEFT JOIN cp6_fin_mfg_v1.releases e ON (e.tenant_id,e.ledger_id,e.rate_id,e.preview_token)
                =(p.tenant_id,p.ledger_id,p.rate_id,p.token)
            WHERE p.tenant_id={Tenant} AND p.ledger_id={ledger} AND p.rate_id={id}
              AND p.subject_id={subject} AND p.token={preview}
            """).SingleOrDefaultAsync(ct) ?? throw Error("RATE_RELEASE_PREVIEW_NOT_FOUND", 404);
        if (row.Released)
            return (true, row.SourceProductId);
        if (!row.Live || row.RateRevision != row.CurrentRateRevision || row.TimelineRevision != row.CurrentTimelineRevision)
            throw Error("RATE_RELEASE_PREVIEW_STALE", 412);
        return (false, null);
    }

    internal async Task ReleaseAsync(Guid ledger, RateRow rate, Guid subject, Guid qualification,
        Guid preview, long head, ManufacturingRateSegmentV1[] before, ManufacturingRateSegmentV1[] after,
        Guid sourceProductId, Guid? centerUse, CancellationToken ct)
    {
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE cp6_fin_mfg_v1.rate_versions SET state='RELEASED', revision=revision+1,
                publisher={subject},release_qualification_id={qualification}
            WHERE tenant_id={Tenant} AND ledger_id={ledger} AND id={rate.Id} AND revision={rate.Revision};
            DELETE FROM cp6_fin_mfg_v1.timeline
            WHERE tenant_id={Tenant} AND ledger_id={ledger} AND work_center_id={rate.WorkCenterId} AND operation_id={rate.OperationId};
            UPDATE cp6_fin_mfg_v1.rate_heads SET revision=revision+1
            WHERE tenant_id={Tenant} AND ledger_id={ledger} AND work_center_id={rate.WorkCenterId} AND operation_id={rate.OperationId};
            """, ct);
        foreach (var segment in after)
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO cp6_fin_mfg_v1.timeline
                    (tenant_id,ledger_id,work_center_id,operation_id,rate_id,valid_from,to_exclusive)
                VALUES ({Tenant},{ledger},{rate.WorkCenterId},{rate.OperationId},{segment.RateId},{segment.From},{segment.ToExclusive})
                """, ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO cp6_fin_mfg_v1.releases
                (tenant_id,ledger_id,rate_id,preview_token,timeline_revision,before_segments,after_segments,source_product_id,center_use_effect_id)
            VALUES ({Tenant},{ledger},{rate.Id},{preview},{head+1},CAST({JsonSerializer.Serialize(before)} AS jsonb),
                CAST({JsonSerializer.Serialize(after)} AS jsonb),{sourceProductId},{centerUse})
            """, ct);
    }

    internal sealed class RateRow
    {
        public Guid Id { get; set; }
        public Guid WorkCenterId { get; set; }
        public Guid OperationId { get; set; }
        public Guid CenterId { get; set; }
        public Guid CenterVersionId { get; set; }
        public Guid PolicyVersionId { get; set; }
        public Guid BasisId { get; set; }
        public Guid Maker { get; set; }
        public decimal LaborRate { get; set; }
        public decimal OverheadRate { get; set; }
        public DateOnly From { get; set; }
        public DateOnly? ToExclusive { get; set; }
        public string State { get; set; } = "DRAFT";
        public long Revision { get; set; } = 1;
        public Guid? Reviewer { get; set; }
        public Guid? ReviewQualificationId { get; set; }
        public bool ReviewOriginalActor { get; set; }
    }

    internal sealed class PreviewRow
    {
        public long RateRevision { get; set; }
        public long TimelineRevision { get; set; }
        public long CurrentRateRevision { get; set; }
        public long CurrentTimelineRevision { get; set; }
        public bool Live { get; set; }
        public bool Released { get; set; }
        public Guid? SourceProductId { get; set; }
    }
}
