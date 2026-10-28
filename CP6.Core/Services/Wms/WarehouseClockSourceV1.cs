using System.Globalization;
using System.Text.RegularExpressions;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Plm;
using CP6.Core.Services.Sys;
using Microsoft.Extensions.Options;
using Microsoft.EntityFrameworkCore;
using static CP6.Core.Services.Wms.LocationRuleValuesV1;
namespace CP6.Core.Services.Wms;

public sealed class WarehouseClockRuntimeV1(CP6Context db, ScopeAuthorizationFenceV1 scope,
    IOptions<WarehouseClockOptionsV1> options, bool development = false)
{
    internal const string Provider = "POSTGRESQL_CLOCK_TIMESTAMP_V1", SourceOwner = "Main.PostgreSql.ClockAuthority.v1";
    internal Guid Environment => scope.Owns(db) ? scope.EnvironmentId : throw Error("CLOCK_CONTEXT_MISMATCH", 503);
    internal async Task Fence(CancellationToken ct)
    {
        _ = Environment;
        await scope.Root(ct);
        var bound=await db.Database.SqlQueryRaw<Guid?>("SELECT environment_id AS \"Value\" FROM cp6_warehouse_clock_v1.binding WHERE singleton").SingleOrDefaultAsync(ct);
        if(bound!=null&&bound!=Environment)throw Error("CLOCK_ENVIRONMENT_MISMATCH",503);
    }
    internal WarehouseClockSourceV1? Source()
    {
        var c = options.Value; var a = c.Authority;
        if (!c.Enabled || a == null || !c.Tenants.Contains(db.CurrentTenantId)) return null;
        if (a.EnvironmentId != Environment || a.Id == Guid.Empty || a.Provider != Provider
            || a.EvidenceMode is not ("REAL" or "DEMO") || a.EvidenceMode == "DEMO" && !development
            || !Text(a.AttestationRef, 1000) || a.MaxErrorMilliseconds is < 1 or > 86400000
            || a.MaxReadMilliseconds is < 1 or > 30000) return null;
        try
        {
            EngineeringProductValuesV1.Revision(a.Version);
            if (Instant(a.ValidFrom) >= Instant(a.ValidTo)) return null;
        }
        catch (CP6.Core.Services.Erp.OrderPriceCorrectionException) { return null; }
        var bytes = EngineeringProductStoreV1.Encode(a);
        return new(new(SourceOwner, a.Id, a.Version, EngineeringProductStoreV1.Digest(bytes)), a);
    }
    internal static DateTimeOffset Instant(string? value)
    {
        if (value == null || !Regex.IsMatch(value, @"\A[0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9]{2}:[0-9]{2}:[0-9]{2}(?:\.[0-9]{1,7})?(?:Z|[+-][0-9]{2}:[0-9]{2})\z",
                RegexOptions.CultureInvariant | RegexOptions.NonBacktracking)
            || !DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var result))
            throw Error("CLOCK_INSTANT_INVALID");
        return result;
    }
    internal static WarehouseClockDefinitionV1 Validate(WarehouseClockDefinitionV1? d)
    {
        if (d?.Source == null || d.Source.Owner != SourceOwner || d.Source.Id == Guid.Empty
            || !Regex.IsMatch(d.Source.Digest ?? "", @"\A[0-9a-f]{64}\z", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking))
            throw Error("CLOCK_SOURCE_REFERENCE_INVALID");
        EngineeringProductValuesV1.Revision(d.Source.Version);
        if (Instant(d.ValidFrom) >= Instant(d.ValidTo)) throw Error("CLOCK_POLICY_INTERVAL_INVALID");
        return d; // Keep original offsets and all seven decimal places.
    }
}
