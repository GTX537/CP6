using System.Text.Json.Serialization;
using CP6.Core.Services.Pub;

namespace CP6.Core.Services.Plm;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record DocumentSourceV1(string Kind, string OwnerName, string Organization, string SourceVersion,
    string? SourceDate, string AcquisitionBasis, string AuthorityLanguage, string AuthorityBasis);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record DocumentContentV1(Guid OccurrenceId, string Role, FileContentRefV1 File,
    Guid? DerivedFromOccurrenceId, string Language, string DerivationBasis);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ControlledDocumentBodyV1(string Name, string RevisionLabel, EngineeringRevisionRefV1 RevisionOrigin,
    EngineeringProductRefV1 ProductRef, DocumentSourceV1 Source, DocumentContentV1[] Contents);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ControlledDocumentCommandV1(Guid OperationId, Guid DocumentId, string Action,
    string? ExpectedVersion, Guid? ExpectedEtag, ControlledDocumentBodyV1 Body, string Reason);
internal sealed record ControlledDocumentStoredV1(Guid TenantId, Guid Id, string Version, Guid CreatorId,
    Guid Etag, ControlledDocumentBodyV1 Body, string Action, string Reason, string? ParentVersion,
    Guid ActorId, DateTimeOffset RecordedAt);
public sealed record DocumentContentObservationV1(Guid OccurrenceId, string Availability, string FileName, string StateVersion);
public sealed record ControlledDocumentSnapshotV1(Guid TenantId, EngineeringSourceRefV1 Reference, Guid Etag,
    bool IsCurrent, ControlledDocumentBodyV1 Body, string Action, string Reason, string? ParentVersion,
    Guid ActorId, DateTimeOffset RecordedAt, string OriginalJson, string[]? Missing = null,
    DocumentContentObservationV1[]? ContentStatus = null, string State = "DRAFT", string Readiness = "REVIEW_REQUIRED");
public sealed record ControlledDocumentResultV1(Guid OperationId, string Kind, Guid ObjectId, string Version, string Digest);
public sealed record ControlledDocumentOperationV1(Guid OperationId, string State, ControlledDocumentResultV1? Result = null);
public sealed record ControlledDocumentDiffV1(EngineeringSourceRefV1 Before, EngineeringSourceRefV1 After, EngineeringWorkingChangeV1[] Changes);
