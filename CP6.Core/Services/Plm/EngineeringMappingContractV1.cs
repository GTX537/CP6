using System.Text.Json.Serialization;

namespace CP6.Core.Services.Plm;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record EngineeringSourceRefV1(string Owner, Guid Id, string Version, string Digest);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record EngineeringMappingRefV1(string Owner, Guid Id, string Version, string Digest);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record EngineeringMappingCandidateV1
{
    public EngineeringProductRefV1 EngineeringRef { get; init; } = null!;
    public EngineeringSourceRefV1? BusinessRef { get; init; }
    public EngineeringSourceRefV1? SiteRef { get; init; }
    public string Purpose { get; init; } = "";
    public string Evidence { get; init; } = "";
    public string Conflicts { get; init; } = "";
    public bool? ConflictsResolved { get; init; }
}
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record EngineeringMappingCommandV1
{
    public Guid OperationId { get; init; }
    public Guid MappingId { get; init; }
    public string Action { get; init; } = "";
    public string ExpectedVersion { get; init; } = "";
    public EngineeringMappingCandidateV1? Candidate { get; init; }
}
public sealed record EngineeringOwnerConfirmationV1(string Owner, string Resource, string Action,
    Guid ActorId, string PermissionVersion, string RegistryVersion, string OrganizationVersion);
public sealed record EngineeringMappingVersionV1(Guid TenantId, EngineeringMappingRefV1 Reference,
    string State, bool IsCurrent, EngineeringMappingCandidateV1 Candidate,
    EngineeringOwnerConfirmationV1? EngineeringConfirmation, EngineeringOwnerConfirmationV1? BusinessConfirmation,
    string OriginalJson, string Coverage = "IDENTITY_SITE_ONLY",
    string TechnicalApplicability = "REQUIRED_FENCED", string LegalEntityApplicability = "REQUIRED_FENCED",
    bool CanAdoptIdentitySite = false, string[]? Reasons = null);
public sealed record EngineeringBusinessChoiceV1(EngineeringSourceRefV1 Reference, string Code);
public sealed record EngineeringSiteChoiceV1(EngineeringSourceRefV1 Reference, string Code, string Name, bool Enabled);
internal sealed record EngineeringMappingStoredV1(Guid TenantId, Guid Id, string Version, Guid EngineeringId,
    string State, EngineeringMappingCandidateV1 Candidate, EngineeringOwnerConfirmationV1? EngineeringConfirmation,
    EngineeringOwnerConfirmationV1? BusinessConfirmation);
