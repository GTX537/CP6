using System.Diagnostics;
using System.Text.Json;
using CP6.Core.Persistence;
using CP6.Core.Services.Plm;
using Microsoft.EntityFrameworkCore;
using static CP6.Core.Services.Wms.WarehouseClockRuntimeV1;
using static CP6.Core.Services.Wms.LocationRuleValuesV1;
namespace CP6.Core.Services.Wms;

internal sealed partial class WarehouseClockStoreV1
{
    internal const string EvidenceOwner = "Main.Wms.WarehouseClockObservation.v1";
    internal async Task<WarehouseClockReadingV1> Observe(Guid warehouse, WarehouseClockPolicyV1? policy,
        WarehouseClockSourceV1? source, Guid environment, CancellationToken ct)
    {
        static WarehouseClockReadingV1 Unknown(string reason) => new("UNKNOWN", reason, null);
        if (policy == null) return Unknown("CLOCK_POLICY_REQUIRED");
        if (!policy.Original.Definition.Enabled) return Unknown("CLOCK_POLICY_DISABLED");
        if (source == null) return Unknown("CLOCK_SOURCE_UNAVAILABLE");
        if (policy.Original.EnvironmentId != environment || policy.Original.Definition.Source != source.Reference
            || policy.Original.Source != source) return Unknown("CLOCK_SOURCE_CHANGED");
        // Called under the store's write fence. MAX is scoped to this tenant/environment, across
        // all warehouses and source revisions: changing a policy cannot erase a seen rollback.
        var previous = await db.Database.SqlQuery<DateTime?>($"""
            SELECT max(observed_at) AS "Value" FROM cp6_warehouse_clock_v1.observations
            WHERE tenant_id={db.CurrentTenantId} AND environment_id={environment}
            """).SingleAsync(ct);
        var start = Stopwatch.GetTimestamp();
        var raw = await DatabaseUtcClock.ReadUtcNowAsync(db, ct);
        var elapsed = Stopwatch.GetElapsedTime(start);
        if (elapsed > TimeSpan.FromMilliseconds(source.Authority.MaxReadMilliseconds)) return Unknown("CLOCK_READ_TOO_SLOW");
        if (previous != null && raw < previous.Value) return Unknown("CLOCK_MOVED_BACKWARD");
        var observed = new DateTimeOffset(raw);
        DateTimeOffset earliest, latest;
        try
        {
            earliest = observed.AddMilliseconds(-source.Authority.MaxErrorMilliseconds);
            latest = observed.AddMilliseconds(source.Authority.MaxErrorMilliseconds).Add(elapsed);
        }
        catch (ArgumentOutOfRangeException) { return Unknown("CLOCK_RANGE_UNAVAILABLE"); }
        if (earliest < Instant(source.Authority.ValidFrom) || latest >= Instant(source.Authority.ValidTo))
            return Unknown("CLOCK_SOURCE_OUTSIDE_VALIDITY");
        if (earliest < Instant(policy.Original.Definition.ValidFrom) || latest >= Instant(policy.Original.Definition.ValidTo))
            return Unknown("CLOCK_POLICY_OUTSIDE_VALIDITY");
        var id = Guid.NewGuid();
        var fact = new WarehouseClockObservationV1(db.CurrentTenantId, environment, warehouse, id,
            policy.Reference, source.Reference, source.Authority.EvidenceMode, observed, earliest, latest, elapsed.Ticks);
        var body = EngineeringProductStoreV1.Encode(fact); var digest = EngineeringProductStoreV1.Digest(body);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO cp6_warehouse_clock_v1.observations(tenant_id,environment_id,warehouse_id,id,policy_id,policy_revision,source_digest,observed_at,body,digest)
            VALUES({db.CurrentTenantId},{environment},{warehouse},{id},{policy.Reference.Id},
              {EngineeringProductValuesV1.Revision(policy.Reference.Version)},{source.Reference.Digest},{raw},{body},{digest})
            """, ct);
        return new("OBSERVED", "CLOCK_OBSERVED", new(new(EvidenceOwner, id, "1", digest), fact));
    }
    internal async Task<WarehouseClockEvidenceV1> Observation(Guid warehouse, Guid id, CancellationToken ct)
    {
        var r = await db.Database.SqlQuery<Row>($"""
            SELECT body AS "Body",digest AS "Digest",id AS "Id",1::bigint AS "Revision"
            FROM cp6_warehouse_clock_v1.observations WHERE tenant_id={db.CurrentTenantId} AND warehouse_id={warehouse} AND id={id}
            """).SingleOrDefaultAsync(ct) ?? throw Error("CLOCK_OBSERVATION_NOT_FOUND", 404);
        var o = JsonSerializer.Deserialize<WarehouseClockObservationV1>(r.Body, EngineeringProductStoreV1.Json);
        if (o == null || o.TenantId != db.CurrentTenantId || o.Id != id || o.WarehouseId != warehouse
            || EngineeringProductStoreV1.Digest(r.Body) != r.Digest) throw Error("CLOCK_OBSERVATION_UNAVAILABLE", 503);
        return new(new(EvidenceOwner, id, "1", r.Digest), o);
    }
}
