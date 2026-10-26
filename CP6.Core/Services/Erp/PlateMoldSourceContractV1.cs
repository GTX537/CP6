using System.Text.Json;
using System.Text.Json.Serialization;

namespace CP6.Core.Services.Erp;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record PlateMoldSourceRefV1(string Owner, Guid Id, string Version, string Digest);
public sealed record PlateMoldSourceScopeV1(Guid TenantId, Guid ActorId);
public sealed record PlateMoldSourceVersionV1(Guid TenantId, PlateMoldSourceRefV1 Reference,
    bool IsCurrent, bool SourcePresent, string ObservedNativeRowVersion, string CurrentNativeRowVersion,
    DateTimeOffset ObservedAt, JsonElement Source, string OriginalJson,
    string TechnicalApproval = "UNKNOWN", string PhysicalQualification = "UNKNOWN");
