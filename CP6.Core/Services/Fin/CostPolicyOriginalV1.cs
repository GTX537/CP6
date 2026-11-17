using System.Globalization;
using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Integration;
using Microsoft.EntityFrameworkCore;
using static CP6.Core.Services.Fin.ManufacturingFinanceScopeV1;
using static CP6.Core.Services.Fin.ManufacturingCostMasterStoreV1;

namespace CP6.Core.Services.Fin;

internal sealed class CostPolicyOriginalV1(CP6Context db, CostPolicySourcesV1? sources)
{
    internal async Task<CostPolicySourceOriginalV1[]> SourcesAsync(FinanceScopeV1 scope, OperationsAccessV1 access,
        ManufacturingCostPolicySubmitV1 body, bool adopt, CancellationToken ct)
        => body.Definition == null ? [] : await (sources ?? throw Error("COST_POLICY_SOURCE_OWNER_UNAVAILABLE", 503))
            .ReadAsync(scope, access, body.Definition, body.From, adopt, ct);

    internal async Task<CostPolicyAdoptionV1?> PublishAsync(LedgerRow ledger, VersionRow row,
        OperationsAccessV1 access, CancellationToken ct)
    {
        var body = row.Policy;
        if (body.Definition is not { } d) return null;
        var originals = await SourcesAsync(ledger.Scope(db.CurrentTenantId), access, body, true, ct);
        var qualification = await db.Database.SqlQuery<string>($"""
            SELECT to_jsonb(q)::text AS "Value" FROM cp6_fin_mfg_v1.qualification_decisions q
            WHERE (tenant_id,ledger_id,id)=({db.CurrentTenantId},{ledger.Id},{row.ReviewQualificationId!.Value})
            """).SingleAsync(ct);
        using var document = JsonDocument.Parse(qualification);
        var qualificationRef = new CostPolicyRefV1("Finance Costing", row.ReviewQualificationId.Value.ToString("D"), "1",
            FinanceCostCommandCanonicalV1.Hex(FinanceCostCommandCanonicalV1.Digest(document.RootElement)), "POLICY_QUALIFICATION");
        var policy = new CostPolicyV1("COST-POLICY-1", ledger.Scope(db.CurrentTenantId), row.RootId.ToString("D"),
            row.Sequence.ToString(CultureInfo.InvariantCulture), new(body.From, body.ToExclusive), d.Jurisdiction,
            d.Standard, ledger.Currency, body.CurrencyScale, d.UnitCostScale, body.Rounding, d.MaterialMethod,
            body.RateDateBasis, body.Valuation, d.WipMethod, d.AllocationMethod, d.Residual, body.Variance,
            d.TrialTreatment, d.ZeroTreatment, body.AllowZeroRate ? "ALLOW_WITH_EXPLICIT_BASIS" : "FORBID",
            d.CostCenterRequirement, d.CostCenterRuleRef, d.AllowedClasses, d.RequiredQualityKinds,
            d.CapitalizationRuleRef, d.ScrapRuleRef, d.ProvisionalRuleRef, d.GlAccountingPolicyRef,
            d.SourceContractRef, d.ApprovalBasisRef, row.Reviewer!.Value.ToString("D"), qualificationRef, "APPROVED", "1");
        var result = new CostPolicyAdoptionV1(policy, d.QualitySources, qualification, originals, JsonSerializer.Serialize(policy, Wire));
        var json = JsonSerializer.Serialize(result, Wire);
        if (await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO cp6_fin_mfg_v1.policy_originals (tenant_id,ledger_id,version_id,original)
            VALUES ({db.CurrentTenantId},{ledger.Id},{row.Id},CAST({json} AS jsonb))
            """, ct) != 1) throw Error("COST_POLICY_ORIGINAL_UNAVAILABLE", 503);
        return result;
    }

    internal async Task<CostPolicyAdoptionV1?> ReadAsync(Guid ledger, Guid version, CancellationToken ct)
    {
        var record = await db.Database.SqlQuery<Stored>($"""
            SELECT o.original::text AS "Original", (v.body->'definition' IS NOT NULL AND v.body->'definition'<>'null'::jsonb) AS "Full"
            FROM cp6_fin_mfg_v1.policies p
            LEFT JOIN cp6_fin_mfg_v1.cost_master_versions v ON (v.tenant_id,v.ledger_id,v.id)=(p.tenant_id,p.ledger_id,p.version_id)
            LEFT JOIN cp6_fin_mfg_v1.policy_originals o ON (o.tenant_id,o.ledger_id,o.version_id)=(p.tenant_id,p.ledger_id,p.version_id)
            WHERE (p.tenant_id,p.ledger_id,p.version_id)=({db.CurrentTenantId},{ledger},{version})
            """).SingleOrDefaultAsync(ct);
        if (record?.Original != null) return JsonSerializer.Deserialize<CostPolicyAdoptionV1>(record.Original, Wire)!;
        if (record?.Full == true) throw Error("COST_POLICY_ORIGINAL_UNAVAILABLE", 503);
        return null; // 仅旧合同保留其实际范围，不能生成完整政策。
    }

    internal async Task<CostPolicyAdoptionV1?> AdoptAsync(LedgerRow ledger, Guid version, OperationsAccessV1 access,
        string requestedClass, CancellationToken ct)
    {
        var original = await ReadAsync(ledger.Id, version, ct);
        if (original == null) return null;
        var policy = original.Policy;
        if (!policy.AllowedClasses.Contains(requestedClass, StringComparer.Ordinal))
            throw Error("COST_POLICY_CLASS_FORBIDDEN", 422);
        if (policy.MaterialMethod != "UNIT_PRICE" || policy.WipMethod != "EXACT_TRACE" || policy.AllocationMethod != "DIRECT_TRACE")
            throw Error("COST_POLICY_ADOPTION_REQUIRED", 409);
        return await HoldAsync(ledger, version, access, original, ct);
    }

    internal async Task<CostPolicyAdoptionV1?> HoldAsync(LedgerRow ledger, Guid version, OperationsAccessV1 access,
        CostPolicyAdoptionV1? original, CancellationToken ct)
    {
        original ??= await ReadAsync(ledger.Id, version, ct);
        if (original == null) return null;
        var row = await new ManufacturingCostMasterStoreV1(db).ReadAsync(ledger.Id, version, ct);
        var participant = sources ?? throw Error("COST_POLICY_SOURCE_OWNER_UNAVAILABLE", 503);
        await participant.RequireQualificationAsync(ledger, row, access, ct);
        var current = await participant.ReadAsync(ledger.Scope(db.CurrentTenantId), access, row.Policy.Definition!, ledger.BusinessDate, true, ct);
        return original with { Sources = current };
    }

    private sealed class Stored
    {
        public string? Original { get; set; }
        public bool? Full { get; set; }
    }
}
