using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using CP6.Core.Services.Sys;
using CP6.Core.Services.Wf;

namespace CP6.Core.Services.Oa;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ApprovalBindingRuleV2(Guid RuleId, int Priority, string When, string FlowKey, Guid? FlowDefVersionId);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ApprovalBindingWriteV2(Guid CommandId, string BizType, string SubjectKind, string SelectionMode,
    string FlowKey, Guid? FlowDefVersionId, DateTimeOffset EffectiveFrom, DateTimeOffset? EffectiveTo,
    string RuleMode, ApprovalBindingRuleV2[] Rules, string? DetailRoute, string Remark);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ApprovalBindingDisableV2(Guid CommandId, string Reason);
public sealed record ApprovalBindingIntervalV2(DateTimeOffset From, DateTimeOffset? To, Guid? RevisionId, string State);
public sealed record ApprovalBindingTimelineV2(Guid TimelineRevisionId, Guid BindingId, string TimelineNo,
    Guid? PreviousTimelineRevisionId, DateTimeOffset PublishedAt, Guid PublishedBy, ApprovalBindingIntervalV2[] Intervals);
public sealed record ApprovalBindingViewV2(Guid BindingId, Guid RevisionId, int Revision, string State, string Owner,
    string RowVersion, Guid CreatedBy, DateTimeOffset CreatedAt, Guid TimelineRevisionId,
    ApprovalBindingIntervalV2? EffectiveInterval, ApprovalBindingIntervalV2[] ScheduledIntervals,
    Guid CommandId, string BizType, string SubjectKind, string SelectionMode, string FlowKey, Guid? FlowDefVersionId,
    DateTimeOffset EffectiveFrom, DateTimeOffset? EffectiveTo, string RuleMode, ApprovalBindingRuleV2[] Rules,
    string? DetailRoute, string Remark);
public sealed record ApprovalBindingResultV2(Guid CommandId, string State, ApprovalBindingViewV2 Result, string QueryRef);
public sealed record ApprovalBindingListV2<T>(T[] Items, string? NextCursor, DateTimeOffset AsOf);
public sealed record ApprovalBindingTypeV2(string BizType, string Owner, string[] SubjectKinds, string SnapshotSchema);
public sealed record ApprovalBindingFlowV2(string FlowKey, Guid VersionId, string Version, string Status, bool HeadEnabled);
public sealed record ApprovalBindingOptionsV2(ApprovalBindingTypeV2[] BusinessTypes, ApprovalBindingFlowV2[] FlowVersions, DateTimeOffset AsOf);
public sealed record ApprovalBindingCapabilitiesV2(string Schema, string PermissionEpoch, bool BindingRead, bool BindingConfigure,
    bool CallbackRead, bool CallbackReplay, bool EvidenceRead, string[] SupportedSchemas);

/// <summary>OA-APP §16.1: immutable schedules; no implicit return to the former revision.</summary>
internal static class ApprovalBindingTimelineBuilderV2
{
    internal static ApprovalBindingIntervalV2[] Revise(ApprovalBindingIntervalV2[] old, DateTimeOffset at,
        DateTimeOffset? until, Guid revision, bool disable = false)
    {
        var prefix = old.Where(x => x.From < at).Select(x => x.To == null || x.To > at ? x with { To = at } : x).ToList();
        if (disable) { prefix.Add(new(at, null, null, "DISABLED")); return prefix.ToArray(); }
        var future = old.Where(x => x.From > at).ToArray(); var next = future.FirstOrDefault()?.From;
        if (next != null && (until == null || until > next)) throw new ScopeAuthorizationExceptionV1("FUTURE_SWITCH_CONFLICT", 409);
        prefix.Add(new(at, until, revision, "ACTIVE"));
        if (until != null && (next == null || until < next)) prefix.Add(new(until.Value, next, null, "DISABLED"));
        prefix.AddRange(future); return prefix.ToArray();
    }
    internal static string Version(long value)
    { Span<byte> bytes = stackalloc byte[8]; BinaryPrimitives.WriteInt64BigEndian(bytes, value); return Convert.ToBase64String(bytes); }
    internal static bool Matches(string? etag, long value) => etag == "\"" + Version(value) + "\"";
}
