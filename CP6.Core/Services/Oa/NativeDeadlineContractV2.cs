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
    int OverdueCount,DateTimeOffset? OldestDue,NativeDeadlineReadV2[] Deadlines,Guid[] MissingEvidenceTaskIds, NativeTimeoutStageV2[] Stages, NativeTimeoutRunV2[] Runs);

// Trusted host/deployment input, never supplied by an interactive request or task.
// A matching, currently enabled tenant registration is also required in the Owner.
public sealed class NativeTimeoutOptionsV2
{
    public bool Enabled { get; set; }
    public Guid ServiceId { get; set; }
    public string HostKey { get; set; } = "";
    public NativeTimeoutScheduleChangeV2? Schedule { get; set; }
}

public enum NativeTimeoutMissedRunV2 { Skip, CatchUpBounded, ContinueOriginal }

public sealed record NativeTimeoutScheduleDefinitionV2(bool Enabled, int IntervalSeconds,
    NativeTimeoutMissedRunV2 MissedRun, int CatchUpLimit, bool WorkdaysOnly, string TimeZoneId);
public sealed record NativeTimeoutScheduleChangeV2(Guid OperationId, Guid ExpectedVersion,
    string Reason, NativeTimeoutScheduleDefinitionV2 Definition);

public sealed record NativeTimeoutStageV2(string Layer, bool? Confirmed, DateTimeOffset? ObservedAt = null);
public sealed record NativeTimeoutObservationV2(long Sequence, string State, string? Reason, DateTimeOffset ObservedAt);
public sealed record NativeTimeoutUnitV2(Guid AttemptId, Guid OperationId, Guid TaskId, long Epoch,
    Guid? CommandId, Guid[] MessageIds, string State, NativeTimeoutObservationV2[] Observations);
public sealed record NativeTimeoutRunV2(Guid RunId, Guid ScheduleVersion, DateTimeOffset? SlotAt, DateTimeOffset EvaluatedAt,
    string CalendarVersion, string TimeZoneId, string TimeZoneVersion, string MissedRun, bool IsRecovery, string State,
    int CandidateCount, int AppliedCount, int AlreadyTerminalCount, int BlockedCount, int UnknownCount,
    int FailedCount, int InFlightCount, NativeTimeoutUnitV2[] Units);
public enum NativeTimeoutRecoveryActionV2 { ReturnOriginal, ContinueOriginal, Wait, Blocked, Isolate }
public sealed record NativeTimeoutRecoveryV2(Guid OperationId, long ExpectedRevision, string InputDigest,
    string OwnerOutcome, NativeTimeoutRecoveryActionV2 AllowedAction, string? Reason, NativeTimeoutResultV2? Result);

/// <summary>随实际 WebApi 宿主注册提供的诊断事实。</summary>
public sealed class NativeTimeoutHostRegistrationV2 { }
