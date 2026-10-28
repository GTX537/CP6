using System.Text.Json.Serialization;
using CP6.Core.Services.Erp;

namespace CP6.Core.Services.Mes;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ResourceCapabilityRefV1(string Owner, string Kind, Guid Id, string Version, string Digest);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ResourceCapabilityItemV1(string Key, string ValueKind, string? DecimalValue, string? EnumValue,
    UomProfileRefV1? Unit, string Dimension, string ValidFrom, string? ValidTo, string Basis);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ResourceCapabilityBodyV1(ResourceSourceRefV1 Source, UomProfileRefV1? TimeUnit,
    UomTimeBasisRefV1? TimeBasis, UomProfileRefV1? OutputUnit, ResourceCapabilityItemV1[] Capabilities, string Basis);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ResourceCapabilityCommandV1(Guid OperationId, string ExpectedVersion, ResourceCapabilityBodyV1 Body, string Reason);
public sealed record ResourceCapabilityOriginalV1(UomProfileRefV1 Reference, string OriginalJson);
internal sealed record ResourceCapabilityStoredV1(Guid TenantId, Guid Id, string Kind, string Version, Guid CreatorId,
    Guid ActorId, DateTimeOffset RecordedAt, ResourceCapabilityBodyV1 Body, string Reason, string ResourceOriginal,
    ResourceCapabilityOriginalV1[] Units, string? TimeBasisOriginal);
public sealed record ResourceCapabilitySnapshotV1(Guid TenantId, ResourceCapabilityRefV1 Reference, bool IsCurrent,
    ResourceCapabilityBodyV1 Body, Guid ActorId, DateTimeOffset RecordedAt, string OriginalJson,
    string[] Missing, string Qualification = "DECLARED_NOT_QUALIFIED");
public sealed record ResourceCapabilityEditorV1(ResourceSourceVersionV1 Resource, string ExpectedVersion, ResourceCapabilitySnapshotV1? Current);
