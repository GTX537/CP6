using System.Text.Json.Serialization;
using CP6.Core.Services.Erp;

namespace CP6.Core.Services.Plm;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record EngineeringSourceRefV1(string Owner, Guid Id, string Version, string Digest);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record EngineeringMappingRefV1(string Owner, Guid Id, string Version, string Digest);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record EngineeringMappingLegalEntityV1(string Key, string Version, string Evidence);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record EngineeringMappingTechnicalScopeV1(EngineeringRevisionRefV1 RevisionRef, EngineeringRevisionRefV1 ConfigurationRef);
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
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public EngineeringMappingLegalEntityV1? LegalEntity { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public EngineeringMappingTechnicalScopeV1? TechnicalScope { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public EngineeringMappingRefV1? LegacyMappingRef { get; init; }

    internal bool RequiresScopeReview => LegalEntity != null || TechnicalScope != null || LegacyMappingRef != null;
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
    bool CanAdoptIdentitySite = false, string[]? Reasons = null, EngineeringOwnerConfirmationV1? Review = null)
{
    public string ManufacturingApplicability => "REQUIRED_FENCED";

    public EngineeringIdentitySiteScopeV1? ConfirmedIdentitySite =>
        State == "CONFIRMED" && EngineeringConfirmation != null && BusinessConfirmation != null
            && Candidate.BusinessRef is { } product && Candidate.SiteRef is { } site
            ? new(Reference, Candidate.EngineeringRef, product, site, Candidate.Purpose)
            : null;

    public EngineeringConfirmedLegalScopeV1? ConfirmedLegalScope =>
        Review != null && ConfirmedIdentitySite is { } identity && Candidate.LegalEntity is { } legal
            ? new(identity, legal, Candidate.TechnicalScope) : null;

    public EngineeringMappingTechnicalScopeV1? ConfirmedTechnicalScope =>
        Review != null && ConfirmedIdentitySite != null ? Candidate.TechnicalScope : null;

    public EngineeringLegacyMappingConfirmationV1? LegacyConfirmation =>
        Review != null && ConfirmedIdentitySite != null && Candidate.LegacyMappingRef is { } original
            ? new(original, Reference) : null;
}
public sealed record EngineeringIdentitySiteScopeV1(EngineeringMappingRefV1 MappingRef,
    EngineeringProductRefV1 EngineeringRef, EngineeringSourceRefV1 ProductRef, EngineeringSourceRefV1 SiteRef, string Purpose);
public sealed record EngineeringConfirmedLegalScopeV1(EngineeringIdentitySiteScopeV1 IdentitySite,
    EngineeringMappingLegalEntityV1 LegalEntity, EngineeringMappingTechnicalScopeV1? TechnicalScope);
public sealed record EngineeringLegacyMappingConfirmationV1(EngineeringMappingRefV1 OriginalMappingRef, EngineeringMappingRefV1 ConfirmedMappingRef);
public sealed record EngineeringBusinessChoiceV1(EngineeringSourceRefV1 Reference, string Code,
    string? Name = null, CommercialMaterialKeyV1? MaterialKey = null);
public sealed record EngineeringSiteChoiceV1(EngineeringSourceRefV1 Reference, string Code, string Name, bool Enabled);
internal sealed record EngineeringMappingStoredV1(Guid TenantId, Guid Id, string Version, Guid EngineeringId,
    string State, EngineeringMappingCandidateV1 Candidate, EngineeringOwnerConfirmationV1? EngineeringConfirmation,
    EngineeringOwnerConfirmationV1? BusinessConfirmation,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] EngineeringOwnerConfirmationV1? Review = null);
