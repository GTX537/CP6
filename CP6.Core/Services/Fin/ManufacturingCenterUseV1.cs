using System.Text.Json;
using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore;
using static CP6.Core.Services.Fin.ManufacturingFinanceScopeV1;

namespace CP6.Core.Services.Fin;

// Finance Costing is the only participant of this registered in-process COST_ATTRIBUTION path.
internal static class ManufacturingCenterUseV1
{
    internal const string RateRelease = "RATE_RELEASE";
    internal const string CostCollection = "COST_COLLECTION";

    internal sealed record Attribution(Guid RateId, Guid RootId, Guid VersionId, Guid OperationId)
    {
        internal Item For(Guid id) => new(id, RateId, RootId, VersionId, OperationId);
    }

    internal sealed record Item(Guid ItemId, Guid RateId, Guid RootId, Guid VersionId, Guid OperationId);
    internal sealed record Use(Guid LedgerId, Guid ActorId, DateOnly BusinessDate, string Currency, Item[] Items);

    internal static async Task<Guid> EnlistAsync(CP6Context db, Use use, string resultKind, Guid resultId,
        long resultRevision, CancellationToken ct)
    {
        if (db.Database.CurrentTransaction == null)
            throw Error("FINANCE_CENTER_USE_TRANSACTION_REQUIRED", 503);
        if (use.Items.Length > 1000 || use.Items.Select(item => item.RootId).Distinct().Count() > 64)
            throw Error("DEPENDENCY_LIMIT", 422);

        var tenant = db.CurrentTenantId;
        var effect = Guid.NewGuid();
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO cp6_fin_mfg_v1.center_use_effects
                (tenant_id,ledger_id,id,result_kind,result_id,result_revision,actor_id,business_date,currency,item_count)
            VALUES ({tenant},{use.LedgerId},{effect},{resultKind},{resultId},{resultRevision},
                {use.ActorId},{use.BusinessDate},{use.Currency},{use.Items.Length})
            """, ct);

        var ordinal = 0;
        foreach (var items in use.Items.OrderBy(item => item.ItemId.ToString("D"), StringComparer.Ordinal).Chunk(64))
        {
            ordinal++;
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO cp6_fin_mfg_v1.center_use_intents (tenant_id,ledger_id,effect_id,batch_no,items)
                VALUES ({tenant},{use.LedgerId},{effect},{ordinal},CAST({JsonSerializer.Serialize(items)} AS jsonb))
                """, ct);
            var recorded = await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO cp6_fin_mfg_v1.center_use_receipts (tenant_id,ledger_id,effect_id,batch_no,adopted)
                SELECT i.tenant_id,i.ledger_id,i.effect_id,i.batch_no,
                    jsonb_agg(jsonb_build_object('Item',x.item,'Version',to_jsonb(v),'Approval',to_jsonb(s)) ORDER BY x.ordinal)
                FROM cp6_fin_mfg_v1.center_use_intents i
                JOIN cp6_fin_mfg_v1.center_use_effects e ON (e.tenant_id,e.ledger_id,e.id)=(i.tenant_id,i.ledger_id,i.effect_id)
                CROSS JOIN LATERAL jsonb_array_elements(i.items) WITH ORDINALITY x(item,ordinal)
                JOIN cp6_fin_mfg_v1.rate_versions r ON (r.tenant_id,r.ledger_id,r.id)=(i.tenant_id,i.ledger_id,(x.item->>'RateId')::uuid)
                JOIN cp6_fin_mfg_v1.current_center_versions v ON (v.tenant_id,v.ledger_id,v.root_id,v.version_id,v.operation_id)=
                    (i.tenant_id,i.ledger_id,(x.item->>'RootId')::uuid,(x.item->>'VersionId')::uuid,(x.item->>'OperationId')::uuid)
                JOIN cp6_fin_mfg_v1.center_submissions s ON (s.tenant_id,s.ledger_id,s.id)=(v.tenant_id,v.ledger_id,v.submission_id)
                WHERE (i.tenant_id,i.ledger_id,i.effect_id,i.batch_no)=({tenant},{use.LedgerId},{effect},{ordinal})
                  AND e.writer_xid=pg_current_xact_id_if_assigned()
                  AND (r.center_id,r.center_version_id,r.operation_id)=(v.root_id,v.version_id,v.operation_id)
                  AND CASE e.result_kind WHEN 'RATE_RELEASE' THEN
                      r.id=e.result_id AND r.state='APPROVED' AND v.effective_dates @> daterange(r.valid_from,r.to_exclusive,'[)')
                      ELSE r.state='RELEASED' AND v.effective_dates @> e.business_date END
                GROUP BY i.tenant_id,i.ledger_id,i.effect_id,i.batch_no
                HAVING count(*)=jsonb_array_length(i.items)
                """, ct);
            if (recorded != 1)
                throw Error("FINANCE_CENTER_USE_SOURCE_UNAVAILABLE", 409);
        }
        return effect;
    }
}
