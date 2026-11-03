namespace CP6.Core.Services.Oa;

public sealed record NativeDeadlinePolicyV2(Guid FlowVersionId, string NodeId, int? Hours, string? Action,
    Guid? EscalateTo, string EscalationReset, string Basis, string CalendarRevision, string TimeZoneId, int OffsetMinutes,
    string ClockSource = "POSTGRESQL_CLOCK_TIMESTAMP");
public sealed record NativeTimeoutClaimV2(Guid ExecutionId, Guid TaskId, long Revision, long Occurrence,
    long Epoch, Guid ClaimId, Guid ServiceId, Guid RegistrationRevision, DateTimeOffset LeaseUntil);
public sealed record NativeTimeoutResultV2(Guid ExecutionId, Guid TaskId, long Revision, long Occurrence,
    string State, string? Reason, Guid ServiceId, DateTimeOffset OccurredAt, Guid? CommandId = null, long ClaimEpoch = 0,
    string? OriginalTaskVersion = null, string? OriginalInstanceVersion = null);
public sealed record NativeDeadlineReadV2(Guid TaskId, long Revision, DateTimeOffset CreatedAt,
    DateTimeOffset? DueAt, NativeDeadlinePolicyV2 Policy, string State, long Occurrence,
    DateTimeOffset? NextDue, NativeTimeoutResultV2? LastResult, long? ClaimEpoch, DateTimeOffset? LeaseUntil);
public sealed record NativeTimeoutDiagnosticsV2(string RuntimeState,DateTimeOffset? LastScan,DateTimeOffset? LastEffect,
    int OverdueCount,DateTimeOffset? OldestDue,NativeDeadlineReadV2[] Deadlines,Guid[] MissingEvidenceTaskIds);

// Trusted host/deployment input, never supplied by an interactive request or task.
// A matching, currently enabled tenant registration is also required in the Owner.
public sealed class NativeTimeoutOptionsV2
{
    public bool Enabled { get; set; }
    public Guid ServiceId { get; set; }
    public string HostKey { get; set; } = "";
}
