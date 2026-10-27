using System.Text.Json.Serialization;
using CP6.Core.Services.Erp;
using CP6.Core.Services.Wms;

namespace CP6.Core.Services.Plm;
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ToolAssociationScopeV1(string ProductApplicability, string ProcessApplicability,
    string ConfigurationApplicability, string Purpose, DateTimeOffset EffectiveFrom, DateTimeOffset? EffectiveTo);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ToolAssociationProposalCommandV1(Guid OperationId, Guid ProposalId, EngineeringSourceRefV1 Technical,
    ToolInstanceRefV1 Physical, ToolAssociationScopeV1 Scope, string Reason, Guid? Supersedes = null);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ToolAssociationDecisionCommandV1(Guid OperationId, Guid ProposalId, Guid RoundId,
    string SubjectDigest, string ExpectedControlRevision, string ExpectedGeneration, string Decision, string Evidence);
public sealed record ToolAssociationCandidateV1(EngineeringSourceRefV1 Technical, PlateMoldSourceRefV1 Native,
    string BaseCode, string Number, int BusinessRevision, ToolRequirementsV1 Requirements,
    string TechnicalState = "DRAFT", string Approval = "NOT_REQUESTED");
public sealed record ToolAssociationSubjectV1(Guid TenantId, Guid ProposalId, string ProposalRevision,
    EngineeringSourceRefV1 Technical, PlateMoldSourceRefV1 Native, string BaseCode, string Number, int BusinessRevision,
    ToolInstanceRefV1 Physical, EngineeringProductRefV1 Product, ToolAssociationScopeV1 Scope, string Reason,
    Guid? Supersedes, Guid ActorId, DateTimeOffset RecordedAt);
public sealed record ToolAssociationDecisionRefV1(string Owner, Guid Id, string Version, string Digest);
internal sealed record ToolAssociationAuthorityV1(string Resource, string Action, string PermissionVersion, string OrganizationVersion);
internal sealed record ToolAssociationDecisionStoredV1(Guid TenantId, Guid ProposalId, Guid RoundId, string Generation,
    string SubjectDigest, string Role, string Owner, Guid Id, string Decision, string Evidence, Guid ActorId,
    ToolAssociationAuthorityV1[] Authorities, DateTimeOffset RecordedAt);
internal sealed record ToolAssociationProjectionV1(Guid TenantId, Guid ProposalId, string SubjectDigest,
    string Revision, string ControlRevision, string Generation, Guid RoundId, string State, bool EverConfirmed,
    ToolAssociationDecisionRefV1? Engineering, ToolAssociationDecisionRefV1? Physical, DateTimeOffset UpdatedAt);
public sealed record ToolAssociationViewV1(ToolAssociationSubjectV1 Subject, string SubjectDigest, string Revision,
    string ControlRevision, string Generation, Guid RoundId, string State, string LastKnownState, bool EverConfirmed,
    string CurrentKnowledge, string[] UnresolvedRoles, ToolAssociationDecisionRefV1? Engineering,
    ToolAssociationDecisionRefV1? Physical, DateTimeOffset AsOf, bool InEffectiveInterval, bool NotExecutionPermit = true);
public sealed record ToolAssociationResultV1(Guid OperationId, string Kind, Guid ObjectId, Guid ProposalId,
    string Version, string Digest, bool Applied, string Outcome, Guid RoundId, string Generation, bool NotExecutionPermit = true);
public sealed record ToolAssociationOperationV1(Guid OperationId, string State, ToolAssociationResultV1? Result = null);
