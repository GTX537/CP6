using System.Text.Json.Serialization;

namespace CP6.Core.Services.Fin;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record GlCurrencyV1(string Mode, string[] Codes);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record GlAccountContentV1(string Name, int Type, int NormalSide, Guid? ParentRootId, string PostingClass,
    [property: JsonRequired] bool IsControl, string? SubLedgerType, [property: JsonRequired] bool RequirePartner, string? RoleKey, GlCurrencyV1? Currency,
    DateOnly From, DateOnly? ToExclusive);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record GlHeadsV1(long ControlRevision, long? TimelineRevision, Guid? ProposalId, long? ProposalRevision);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record GlCommandV1(Guid OperationRef, string Kind, long ExpectedScopeRevision, Guid? RootId, GlHeadsV1? ExpectedHeads,
    string? Code, GlAccountContentV1? Content, bool? Activate, Guid? PreviewId, Guid? DecisionId, string Reason, GlTimelineReferenceV1? TimelineSubject = null);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record GlPublishPreviewV1(long ExpectedScopeRevision, GlHeadsV1 ExpectedHeads,
    bool? Activate, DateOnly? ReplaceFrom, DateOnly? ReplaceToExclusive);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record GlTemplateLineV1(string Code, string? ParentCode, GlAccountContentV1 Content);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record GlTemplatePreviewInputV1(long ExpectedScopeRevision, bool StandardTemplate,
    DateOnly From, DateOnly? ToExclusive, GlTemplateLineV1?[]? Rows, Guid[]? LegacyIds = null);

public sealed record GlReceiptV1(Guid OperationRef, string Kind, long ScopeRevision, Guid? RootId, Guid? ProposalId,
    Guid? VersionId, Guid? DecisionId, Guid? ReleaseId, string? ControlState, Guid[]? RootIds = null, GlDraftReferenceV1[]? Drafts = null, string? ErrorCode = null, int? ErrorStatus = null,
    GlHeadsV1? Heads = null, GlTimelineReferenceV1? TimelineReference = null);
public sealed record GlDraftReferenceV1(Guid RootId, Guid ProposalId, Guid VersionId);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record GlTimelineReferenceV1(Guid RootId, long Revision, Guid ReleaseId, string Digest);

public sealed record GlTimelineSegmentV1(DateOnly From, DateOnly? ToExclusive, Guid VersionId, Guid ReleaseId);

public sealed record GlAccountEligibilityV1(string Status, string? Reason, Guid? RootId, Guid? VersionId)
{
    public bool IsExecutionPermit => false;
}

internal sealed record GlProposalV1(Guid Id, long Revision, string State, Guid VersionId, Guid[] Makers, bool Activate,
    Guid? DecisionId, Guid? SubmissionBasis);

internal sealed record GlVersionV1(Guid Id, long Number, string Digest, Guid Maker, GlAccountContentV1 Content);
internal sealed record GlQualificationV1(Guid Reference, Guid Issuer);
internal sealed record GlDecisionV1(Guid Id, string Kind, Guid Reviewer, GlQualificationV1 Qualification, Guid PolicyRevision,
    string BasisKey, GlHeadsV1 Heads, Guid? VersionId, string? Digest, DateOnly? From, DateOnly? ToExclusive,
    bool? Activate, string Reason, DateTimeOffset AppliedAt, GlTimelineReferenceV1? TimelineSubject = null);
internal sealed record GlReleasePlanV1(Guid RootId, GlHeadsV1 Heads, long ScopeRevision, Guid PolicyRevision, DateOnly BusinessDate,
    string GraphIdentity, Guid DecisionId, Guid VersionId, string Digest, Guid ReleaseId,
    GlTimelineSegmentV1[] Segments, GlTimelineSegmentV1[] Superseded, bool Activate, bool CanApply);
internal sealed record GlPreparedDraftV1(Guid RootId, string Code, GlAccountContentV1 Content);
internal sealed record GlTemplatePlanV1(long ScopeRevision, Guid PolicyRevision, DateOnly BusinessDate,
    GlPreparedDraftV1[] Drafts, Guid[]? LegacyIds, string? LegacyDigest);
