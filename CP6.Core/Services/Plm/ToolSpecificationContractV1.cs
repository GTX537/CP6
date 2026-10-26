using System.Text.Json.Serialization;
using CP6.Core.Services.Erp;

namespace CP6.Core.Services.Plm;
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ToolRequirementsV1(EngineeringProductRefV1 ProductRef, string ProductApplicability,
    string ProcessRequirements, string ConfigurationApplicability, string TechnicalRequirements, string DocumentRequirements);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ToolSpecificationCommandV1(Guid OperationId, string Action, PlateMoldSourceRefV1 Source,
    string ExpectedNativeRowVersion, PlateMoldTechnicalControlV1 ExpectedTechnicalControl, ToolRequirementsV1 Requirements, string Reason);
internal sealed record ToolSpecificationStoredV1(Guid TenantId, Guid Id, string Version, Guid ActorId,
    PlateMoldSourceRefV1 Source, string NativeRowVersion, ToolRequirementsV1 Requirements, string Reason, DateTimeOffset RecordedAt);
public sealed record ToolSpecificationV1(EngineeringSourceRefV1 Reference, PlateMoldSourceRefV1 Source,
    string NativeRowVersion, ToolRequirementsV1 Requirements, string Reason, Guid ActorId, DateTimeOffset RecordedAt,
    string OriginalJson, string State = "DRAFT", string Approval = "NOT_REQUESTED", string Freeze = "NOT_REQUESTED", string PhysicalQualification = "UNKNOWN");
public sealed record ToolSpecificationViewV1(PlateMoldSourceVersionV1 Source, PlateMoldTechnicalControlV1 CurrentControl,
    ToolSpecificationV1? Specification, bool IsCurrent, bool SourceMatches, bool ProductMatches);
public sealed record ToolSpecificationResultV1(Guid OperationId, string Kind, Guid ObjectId, string Version, string Digest);
public sealed record ToolSpecificationOperationV1(Guid OperationId, string State, ToolSpecificationResultV1? Result = null);
