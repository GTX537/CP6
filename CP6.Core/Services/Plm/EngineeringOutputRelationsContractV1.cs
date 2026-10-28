using System.Text.Json.Serialization;
using CP6.Core.Services.Oa;

namespace CP6.Core.Services.Plm;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record OutputInputProofV1(Guid OccurrenceId, EngineeringSourceRefV1 Specification);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record OutputOccurrenceProofV1(Guid OccurrenceId, EngineeringSourceRefV1 Specification,
    EngineeringSourceRefV1 RelationDocument, EngineeringSourceRefV1? NoInputDocument);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record EngineeringOutputRelationsBodyV1(string Name, EngineeringSourceRefV1 Manufacturing,
    OutputInputProofV1[] Inputs, OutputOccurrenceProofV1[] Outputs);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record OutputRelationsPolicyInputV1(int RoleId, DateTimeOffset EffectiveFrom, DateTimeOffset EffectiveTo, string Basis);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record EngineeringOutputRelationsCommandV1(Guid OperationId, Guid RelationId, string Action,
    string? ExpectedEtag, string Reason, EngineeringOutputRelationsBodyV1? Body = null, OutputRelationsPolicyInputV1? Policy = null);
public sealed record OutputRelationsScopeV1(string EnvironmentId, Guid TenantId);
public sealed record OutputRelationsInputV1(string InputOccurrenceId, IterationReviewNativeRefV1 ItemRevisionRef, IterationReviewNativeRefV1 SpecRef);
public sealed record OutputRelationsOperationV1(string OperationOccurrenceId, IterationReviewNativeRefV1 OperationRef, IterationReviewNativeRefV1 ConfigurationRef);
public sealed record OutputRelationsOutputV1(string OutputOccurrenceId, IterationReviewNativeRefV1 ProductIdentity, IterationReviewNativeRefV1 ItemRevisionRef,
    IterationReviewNativeRefV1 SpecRef, string GeneratingOperationOccurrenceId, IterationReviewNativeRefV1 ConfigurationRef,
    string[] InputOccurrenceIds, IterationReviewNativeRefV1 RelationEvidence, IterationReviewNativeRefV1? GenerationWithoutInputBasis);
public sealed record OutputRelationsWireV1(string Schema, OutputRelationsScopeV1 Scope, Guid RelationId, string Version,
    IterationReviewNativeRefV1 ConfigurationRef, OutputRelationsInputV1[] Inputs, OutputRelationsOperationV1[] Operations,
    OutputRelationsOutputV1[] Outputs, string BodyDigest);
public sealed record OutputRelationsOriginalV1(Guid EnvironmentId, Guid TenantId, Guid Id, string Version, Guid CreatorId,
    Guid AuthorId, string EvidenceMode, EngineeringOutputRelationsBodyV1 Body, EngineeringRevisionRefV1 RevisionOrigin,
    EngineeringProductRefV1 Product, OutputRelationsWireV1 Wire, string WireOriginal, string SourceFingerprint,
    string ManufacturingOriginal, EngineeringSourcePinV1[] Sources, string? PreviousVersion, DateTimeOffset RecordedAt);
public sealed record OutputRelationsVersionV1(EngineeringSourceRefV1 Reference, OutputRelationsOriginalV1 Original, string OriginalJson);
public sealed record OutputRelationsControlV1(Guid RelationId, long Revision, string DraftVersion, string State,
    Guid? PolicyId, Guid? QualificationId, Guid ActorId, string Reason, DateTimeOffset RecordedAt);
public sealed record OutputRelationsPolicyV1(Guid Id, Guid RelationId, Guid ActorId, OutputRelationsPolicyInputV1 Input, DateTimeOffset RecordedAt);
public sealed record OutputRelationsQualificationV1(Guid Id, Guid RelationId, Guid ActorId, EngineeringSourceRefV1 Relation,
    EngineeringSourceRefV1 Policy, string SourceFingerprint, string RelationDigest, int RoleId, string PermissionVersion,
    string OrganizationVersion, string Statement, DateTimeOffset RecordedAt);
public sealed record OutputRelationsRecordV1<T>(EngineeringSourceRefV1 Reference, T Body);
public sealed record OutputRelationsViewV1(OutputRelationsVersionV1 Version, EngineeringSourceRefV1 ControlReference,
    OutputRelationsControlV1 Control, string Etag, OutputRelationsRecordV1<OutputRelationsPolicyV1>? Policy,
    OutputRelationsRecordV1<OutputRelationsQualificationV1>? Qualification, string[] Missing, string[] DownstreamMissing,
    string Purpose = "OUTPUT_RELATIONS_ONLY");
public sealed record OutputRelationsResultV1(Guid OperationId, Guid ObjectId, string Action, EngineeringSourceRefV1 Relation,
    EngineeringSourceRefV1 Control, string State);
public sealed record OutputRelationsOperationResultV1(Guid OperationId, string State, OutputRelationsResultV1? Result = null);
