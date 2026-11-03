using System.Globalization;
using System.Text.Json.Serialization;
using CP6.Core.Services.Sys;

namespace CP6.Core.Services.Oa;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record NativeFormQueryV2(Guid? StarterId = null, Guid? HandlerId = null, string? FlowKey = null,
    string? Keyword = null, int? Status = null, string? From = null, string? To = null,
    int Page = 1, int PageSize = 20, string? FormKey = null, int? FormVersion = null, bool? HasInstance = null,
    string? SubmittedFrom = null, string? SubmittedTo = null, string? TimeZoneId = null, DateTimeOffset? SnapshotAsOf = null);
public sealed record NativeQueryDateWindowV2(DateTimeOffset? FromInclusiveUtc, DateTimeOffset? ToExclusiveUtc);
public sealed record NativeQueryContextV2(string TimeZoneId, string DateBoundary = "INCLUSIVE_CALENDAR_DATES_TO_HALF_OPEN_UTC");
public sealed record NativeFormQueryItemV2(Guid Id, Guid? FormDataId, Guid? InstanceId, string? FormKey, int? FormVersion,
    Guid? FormVersionId, string? FormName, string? FlowKey, int? FlowVersion, Guid? FlowVersionId, string? FlowName,
    string? Title, Guid StarterRef, DateTimeOffset SubmittedAtUtc, int? InstanceStatus, string SubmissionState,
    NativeBusinessReadV2? ApplicationSummary, string EvidenceState, string TimeSource, string DetailRef, DateTimeOffset QueryAvailableAtUtc);
public sealed record NativeFormQueryPageV2(NativeFormQueryItemV2[] Items, int Total, int Page, int PageSize,
    DateTimeOffset SnapshotAsOf, string? TimeZoneId, NativeQueryDateWindowV2 Window, NativeQueryDateWindowV2 SubmittedWindow,
    Guid ActualUserId, Guid EffectiveUserId, string AuthorizationProfile = "CURRENT_TASK_DELEGATION_V2");
public sealed record NativeFormQueryDetailV2(NativeFormQueryItemV2 Item, NativeFormContentV2? Form,
    NativeInstanceReadV2? Instance, bool CanEdit = false);

internal static class NativeQueryDatesV2
{
    internal static TimeZoneInfo Zone(string? id)
    {
        if (string.IsNullOrWhiteSpace(id)) throw new ScopeAuthorizationExceptionV1("TENANT_TIMEZONE_REQUIRED", 503);
        try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
        catch (Exception e) when (e is TimeZoneNotFoundException or InvalidTimeZoneException)
        { throw new ScopeAuthorizationExceptionV1("TENANT_TIMEZONE_INVALID", 503, e); }
    }
    internal static NativeQueryDateWindowV2 Window(TimeZoneInfo zone, string? from, string? to)
    {
        DateOnly? Parse(string? value) => value == null ? null
            : DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? date
            : throw new ScopeAuthorizationExceptionV1("QUERY_DATE_INVALID", 422);
        var first = Parse(from); var last = Parse(to);
        if (first > last || last == DateOnly.MaxValue) throw new ScopeAuthorizationExceptionV1("QUERY_DATE_INVALID", 422);
        DateTimeOffset Boundary(DateOnly date)
        {
            var local = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
            // Midnight transitions in some zones have no unique UTC instant. Require an explicit narrower range.
            if (zone.IsInvalidTime(local) || zone.IsAmbiguousTime(local)) throw new ScopeAuthorizationExceptionV1("QUERY_DATE_BOUNDARY_AMBIGUOUS", 422);
            return new(TimeZoneInfo.ConvertTimeToUtc(local, zone));
        }
        return new(first.HasValue ? Boundary(first.Value) : null, last.HasValue ? Boundary(last.Value.AddDays(1)) : null);
    }
}
