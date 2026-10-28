using System.Globalization;
using CP6.Core.Services.Integration;
using CP6.Core.Services.Plm;
using CP6.Entity.DomainModels.Wms;
using Microsoft.EntityFrameworkCore;
using static CP6.Core.Services.Wms.WarehouseClockRuntimeV1;
using static CP6.Core.Services.Wms.LocationRuleValuesV1;
namespace CP6.Core.Services.Wms;

public sealed partial class WarehouseClockOwnerV1
{
    public Task<WarehouseClockEvidenceV1> ObservationAsync(Guid warehouse, Guid id, CancellationToken ct = default)
        => Authority.ExecuteAsync(OperationsOwnerV1.Warehouse, "query", false, async (a, t) =>
        {
            await Target(a, warehouse, false, t); var s = new WarehouseClockStoreV1(db); await s.Fence(false, t);
            return await s.Observation(warehouse, id, t);
        }, ct);
    public Task<RuleTimeAssessmentV1> EvaluateAsync(Guid location, Guid id, string? etag, RuleTimeRequestV1 input, CancellationToken ct = default)
    {
        if (input?.Proposal == null || input.Proposal.Owner != LocationRuleValuesV1.Owner || input.Proposal.Id != id
            || input.Kind is not ("CURRENT" or "PLANNED") || (input.Kind == "CURRENT" ? input.At != null : input.At == null))
            throw Error("RULE_TIME_REQUEST_INVALID");
        if (input.At != null) Instant(input.At);
        if (string.IsNullOrEmpty(etag)) throw Error("RULE_ETAG_REQUIRED", 428);
        var c = OperationsOwnerV1.Freeze(input);
        return Authority.ExecuteAsync<RuleTimeAssessmentV1>(OperationsOwnerV1.LocationRule, "query", false, async (a, t) =>
        {
            await runtime.Fence(t); a.AtCommit(runtime.Fence);
            var native = await a.RequireLocationRuleTarget(location, t);
            if (a.LocationRuleFields().Any(f => !f.CanRead)) throw Error("FIELD_READ_FORBIDDEN", 403);
            var warehouse = await db.Warehouses.AsNoTracking().Where(x => x.WarehouseCd == native.WarehouseCd).Select(x => x.Id).SingleAsync(t);
            var wa = await a.SourceAsync(OperationsOwnerV1.Warehouse, t); await wa.RequireMaster("warehouses", warehouse, t);
            var rules = new LocationRuleStoreV1(db); await rules.Fence(false, t); var h = await rules.HeadFor(location, id, t);
            var original = await rules.Read(h, null, t);
            if (h.Etag != etag || original.Reference != c.Proposal) throw Error("RULE_PREDECESSOR_CHANGED", 412);
            var s = new WarehouseClockStoreV1(db); await s.Fence(true, t);
            var policy = await s.Read(warehouse, null, t); var source = runtime.Source();
            a.AtCommit(async final =>
            {
                await a.RequireLocationRuleTarget(location, final); await wa.RequireMaster("warehouses", warehouse, final);
                await s.Fence(false, final);
                if (runtime.Source()?.Reference != source?.Reference) throw Error("CLOCK_SOURCE_CHANGED", 412);
                if ((await s.Read(warehouse, null, final))?.Reference != policy?.Reference) throw Error("CLOCK_POLICY_CHANGED", 412);
                if ((await rules.HeadFor(location, id, final)).Etag != etag) throw Error("RULE_PREDECESSOR_CHANGED", 412);
            });
            var reading = await s.Observe(warehouse, policy, source, runtime.Environment, t);
            if (reading.Evidence == null) return new(db.CurrentTenantId, a.ActorId, location, warehouse, original.Reference,
                etag, c, "UNKNOWN", reading.Reason, null, null, c.Kind == "PLANNED");
            var evidence = reading.Evidence; var draft = original.Original.Draft;
            var from = draft.ValidFrom == null ? (DateTimeOffset?)null : Instant(draft.ValidFrom);
            var to = draft.ValidTo == null ? (DateTimeOffset?)null : Instant(draft.ValidTo);
            var low = c.Kind == "PLANNED" ? Instant(c.At) : evidence.Original.Earliest;
            var high = c.Kind == "PLANNED" ? low : evidence.Original.Latest;
            var outcome = from.HasValue && high < from.Value ? "NOT_YET_VALID"
                : to.HasValue && low >= to.Value ? "EXPIRED"
                : from.HasValue && low < from.Value || to.HasValue && high >= to.Value ? "UNKNOWN" : "TIME_SATISFIED";
            var at = c.Kind == "PLANNED" ? c.At : evidence.Original.ObservedAt.ToString("O", CultureInfo.InvariantCulture);
            return new(db.CurrentTenantId, a.ActorId, location, warehouse, original.Reference, etag, c, outcome,
                outcome == "UNKNOWN" ? "CLOCK_INTERVAL_CROSSES_RULE_BOUNDARY" : outcome, evidence, at, c.Kind == "PLANNED");
        }, ct);
    }
}
