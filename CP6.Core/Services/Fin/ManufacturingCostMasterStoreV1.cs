using System.Text.Json;
using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore;
using static CP6.Core.Services.Fin.ManufacturingFinanceScopeV1;

namespace CP6.Core.Services.Fin;

internal sealed class ManufacturingCostMasterStoreV1(CP6Context db)
{
    internal static readonly JsonSerializerOptions Wire = new(JsonSerializerDefaults.Web);
    private Guid Tenant => db.CurrentTenantId;

    internal async Task<VersionRow> ReadAsync(Guid ledger, Guid id, CancellationToken ct)
        => await db.Database.SqlQuery<VersionRow>($"""
            SELECT v.id AS "Id",v.kind AS "Kind",v.root_id AS "RootId",v.sequence AS "Sequence",
                v.predecessor_id AS "PredecessorId",v.maker AS "Maker",v.product_id AS "ProductId",v.body::text AS "Body",
                a.id AS "ApprovalId",a.reviewer AS "Reviewer",a.qualification_id AS "ReviewQualificationId",r.id AS "ReleaseId"
            FROM cp6_fin_mfg_v1.cost_master_versions v
            LEFT JOIN cp6_fin_mfg_v1.cost_master_approvals a ON (a.tenant_id,a.ledger_id,a.version_id)=(v.tenant_id,v.ledger_id,v.id)
            LEFT JOIN cp6_fin_mfg_v1.cost_master_releases r ON (r.tenant_id,r.ledger_id,r.version_id)=(v.tenant_id,v.ledger_id,v.id)
            WHERE (v.tenant_id,v.ledger_id,v.id)=({Tenant},{ledger},{id})
            """).SingleOrDefaultAsync(ct) ?? throw Error("COST_MASTER_VERSION_NOT_FOUND", 404);

    internal Task InsertAsync(Guid ledger, VersionRow row, CancellationToken ct)
        => WriteOneAsync($"""
            INSERT INTO cp6_fin_mfg_v1.cost_master_versions
                (tenant_id,ledger_id,id,kind,root_id,sequence,predecessor_id,maker,product_id,body)
            VALUES ({Tenant},{ledger},{row.Id},{row.Kind},{row.RootId},{row.Sequence},{row.PredecessorId},
                {row.Maker},{row.ProductId},CAST({row.Body} AS jsonb))
            """, ct);

    internal Task RecordApprovalAsync(Guid ledger, Guid id, Guid receipt, Guid actor, Guid qualification,
        string reason, CancellationToken ct)
        => WriteOneAsync($"""
            INSERT INTO cp6_fin_mfg_v1.cost_master_approvals
                (tenant_id,ledger_id,version_id,id,reviewer,qualification_id,reason)
            VALUES ({Tenant},{ledger},{id},{receipt},{actor},{qualification},{reason})
            """, ct);

    internal Task RecordReleaseAsync(Guid ledger, Guid id, Guid receipt, Guid actor, Guid qualification,
        string reason, CancellationToken ct)
        => WriteOneAsync($"""
            INSERT INTO cp6_fin_mfg_v1.cost_master_releases
                (tenant_id,ledger_id,version_id,id,publisher,qualification_id,reason)
            VALUES ({Tenant},{ledger},{id},{receipt},{actor},{qualification},{reason})
            """, ct);

    internal Task<bool> HasSuccessorAsync(Guid ledger, Guid predecessor, CancellationToken ct)
        => db.Database.SqlQuery<bool>($"""
            SELECT EXISTS(SELECT 1 FROM cp6_fin_mfg_v1.cost_master_versions v
                JOIN cp6_fin_mfg_v1.cost_master_releases r ON (r.tenant_id,r.ledger_id,r.version_id)=(v.tenant_id,v.ledger_id,v.id)
                WHERE (v.tenant_id,v.ledger_id,v.predecessor_id)=({Tenant},{ledger},{predecessor})) AS "Value"
            """).SingleAsync(ct);

    internal Task PublishPolicyAsync(LedgerRow ledger, VersionRow row, ManufacturingCostPolicySubmitV1 body,
        CancellationToken ct)
        => WriteOneAsync($"""
            INSERT INTO cp6_fin_mfg_v1.policies
                (tenant_id,ledger_id,version_id,approval_id,currency,currency_scale,rounding,rate_date_basis,
                    valuation,variance,allow_zero_rate,valid_from,to_exclusive)
            VALUES ({Tenant},{ledger.Id},{row.Id},{row.ApprovalId!.Value},{ledger.Currency},{body.CurrencyScale},{body.Rounding},
                {body.RateDateBasis},{body.Valuation},{body.Variance},{body.AllowZeroRate},{body.From},{body.ToExclusive})
            """, ct);

    internal Task PublishBasisAsync(Guid ledger, VersionRow row, ManufacturingRateBasisSubmitV1 body, CancellationToken ct)
        => WriteOneAsync($"""
            INSERT INTO cp6_fin_mfg_v1.rate_bases
                (tenant_id,ledger_id,id,policy_version_id,approval_id,work_center_id,operation_id,
                    valid_from,to_exclusive,zero_evidenced)
            VALUES ({Tenant},{ledger},{row.Id},{body.PolicyVersionId},{row.ApprovalId!.Value},{body.WorkCenterId},
                {body.OperationProfileId},{body.From},{body.ToExclusive},{body.ZeroJustification != null})
            """, ct);

    internal Task<bool> AllowsBasisAsync(LedgerRow ledger, ManufacturingRateBasisSubmitV1 body, CancellationToken ct)
        => db.Database.SqlQuery<bool>($"""
            SELECT EXISTS(SELECT 1 FROM cp6_fin_mfg_v1.policies p
                WHERE (p.tenant_id,p.ledger_id,p.version_id)=({Tenant},{ledger.Id},{body.PolicyVersionId})
                    AND p.enabled AND p.currency={ledger.Currency} AND ({body.ZeroJustification == null} OR p.allow_zero_rate)
                    AND daterange(p.valid_from,p.to_exclusive,'[)') @> daterange({body.From},{body.ToExclusive},'[)')) AS "Value"
            """).SingleAsync(ct);

    private async Task WriteOneAsync(FormattableString sql, CancellationToken ct)
    {
        if (await db.Database.ExecuteSqlInterpolatedAsync(sql, ct) != 1)
            throw Error("AUTHORITY_UNAVAILABLE", 503);
    }

    internal sealed class VersionRow
    {
        public Guid Id { get; set; }
        public string Kind { get; set; } = null!;
        public Guid RootId { get; set; }
        public long Sequence { get; set; }
        public Guid? PredecessorId { get; set; }
        public Guid Maker { get; set; }
        public Guid? ProductId { get; set; }
        public string Body { get; set; } = null!;
        public Guid? ApprovalId { get; set; }
        public Guid? Reviewer { get; set; }
        public Guid? ReviewQualificationId { get; set; }
        public Guid? ReleaseId { get; set; }

        internal string Revision => ReleaseId.HasValue ? "3" : ApprovalId.HasValue ? "2" : "1";
        internal string State => ReleaseId.HasValue ? "RELEASED" : ApprovalId.HasValue ? "APPROVED" : "SUBMITTED";
        internal ManufacturingCostPolicySubmitV1 Policy => JsonSerializer.Deserialize<ManufacturingCostPolicySubmitV1>(Body, Wire)!;
        internal ManufacturingRateBasisSubmitV1 Basis => JsonSerializer.Deserialize<ManufacturingRateBasisSubmitV1>(Body, Wire)!;
    }
}
