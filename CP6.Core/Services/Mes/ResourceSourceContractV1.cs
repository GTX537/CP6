using System.Text.Json;
using System.Text.Json.Serialization;

namespace CP6.Core.Services.Mes;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ResourceSourceRefV1(string Owner, string Kind, Guid Id, string Version, string Digest);
public sealed record ResourceSourceScopeV1(Guid TenantId, Guid ActorId);
public sealed record ResourceSourceAxesV1(string Master, string Capability, string Assignment, string Control);
public sealed record ResourceSourceVersionV1(Guid TenantId, ResourceSourceRefV1 Reference, ResourceSourceAxesV1 Axes,
    bool IsCurrent, bool SourcePresent, bool SourceEnabled, JsonElement Source, string OriginalJson,
    string Qualification = "NOT_ATTESTED");
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ResourceSourcePatchV1(string Field, string? Value);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ResourceSourceCommandV1(Guid OperationId, ResourceSourceRefV1 Expected, ResourceSourcePatchV1[] Patches);
public sealed record ResourceSourceResultV1(Guid OperationId, string Kind, Guid ObjectId, string Version, string Digest, bool Changed);
public sealed record ResourceSourceOperationV1(Guid OperationId, string State, ResourceSourceResultV1? Result = null);
