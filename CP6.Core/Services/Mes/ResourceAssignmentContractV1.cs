using System.Text.Json.Serialization;

namespace CP6.Core.Services.Mes;

public sealed record ResourceAssignmentRefV1(string Owner, Guid Id, string Version, string Digest);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ResourceAssignmentIntervalV1(ResourceSourceRefV1 WorkCenter, string ValidFrom, string? ValidTo);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ResourceAssignmentPlanV1(ResourceSourceRefV1 Machine, ResourceAssignmentIntervalV1[] Intervals);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ResourceAssignmentCommandV1(Guid OperationId, string ExpectedVersion, ResourceAssignmentPlanV1 Plan, string Reason);

internal sealed record ResourceAssignmentOriginalV1(Guid TenantId, Guid MachineId, string Version, Guid ActorId,
    DateTimeOffset RecordedAt, ResourceAssignmentPlanV1 Plan, string Reason);

public sealed record ResourceAssignmentVersionV1(Guid TenantId, ResourceAssignmentRefV1 Reference, bool IsCurrent,
    ResourceAssignmentPlanV1 Plan, string OriginalJson, ResourceSourceRefV1? CurrentWorkCenter = null);
