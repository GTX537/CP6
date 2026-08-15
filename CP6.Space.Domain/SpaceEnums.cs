namespace CP6.Space.Domain;

public enum SpaceModelMode : short
{
    Legacy = 0,
    DesignV1 = 1,
}

public enum SpaceModelCutoverState : short
{
    LegacyOpen = 0,
    FreezeRequested = 1,
    Frozen = 2,
    Bootstrapping = 3,
    Verified = 4,
    DesignV1 = 5,
    FailedFrozen = 6,
}

public enum SpaceVersionStatus : short
{
    Draft = 0,
    Validating = 1,
    Ready = 2,
    Publishing = 3,
    Published = 4,
    Superseded = 5,
    ReconciliationRequired = 6,
    Initializing = 7,
    Failed = 8,
    Abandoned = 9,
}

public enum SpaceModelVersionPurpose : short
{
    Production = 0,
    PlanningScenario = 1,
}

public enum SpacePlanningTaskType : short
{
    Putaway = 0,
    Pick = 1,
    Replenishment = 2,
    Move = 3,
    Other = 4,
}

public enum SpacePlanningTaskOutcome : short
{
    Completed = 0,
    Cancelled = 1,
    Failed = 2,
}

public enum SpaceLifecycleState : short
{
    Active = 0,
    Disabled = 1,
    RemoveRequested = 2,
}

public enum SpaceLocationCodeOrigin : short
{
    Generated = 0,
    Imported = 1,
    Adopted = 2,
    Manual = 3,
}

public enum SpaceExternalBindingState : short
{
    Unbound = 0,
    Bound = 1,
    PendingRemoval = 2,
}

public enum SpaceLocationBindingMode : short
{
    WmsPrimary = 0,
    WmsAlias = 1,
}

public enum SpaceWmsAdoptionStatus : short
{
    Unbound = 0,
    Bound = 1,
    Diverged = 2,
    MissingInWms = 3,
}

public enum SpaceSourceType : short
{
    Dwg = 0,
    Dxf = 1,
    Pdf = 2,
    Png = 3,
    Jpg = 4,
    Excel = 5,
    Editor = 6,
    Template = 7,
}

public enum SpaceFileState : short
{
    Uploading = 0,
    Quarantined = 1,
    Scanning = 2,
    Clean = 3,
    Rejected = 4,
    Deleted = 5,
}

public enum SpaceFileRetentionClass : short
{
    Source = 0,
    Artifact = 1,
    Temporary = 2,
}

public enum SpaceSourceState : short
{
    Uploaded = 0,
    Scanning = 1,
    Ready = 2,
    Parsing = 3,
    PreviewReady = 4,
    Imported = 5,
    Rejected = 6,
}

public enum SpaceArtifactType : short
{
    CadIr = 0,
    LayerInventory = 1,
    PreviewSet = 2,
    Thumbnail = 3,
    ExcelErrorReport = 4,
    CanonicalSnapshot = 5,
    SceneChunk = 6,
    ExcelCadMatchPreview = 7,
}

public enum SpaceJobType : short
{
    FileScan = 0,
    CadConvert = 1,
    CadParse = 2,
    ExcelPreview = 3,
    Import = 4,
    Validate = 5,
    BuildScene = 6,
    Publish = 7,
    Reconcile = 8,
    CloneVersion = 9,
    ApplyGeneration = 10,
    AiRetentionCleanup = 11,
    HistoricalRepublish = 12,
    ExcelCadMatch = 13,
    ExcelCadApply = 14,
    InitializeVersion = 15,
}

public enum SpaceJobSubjectType : short
{
    File = 0,
    ModelSource = 1,
    ModelVersion = 2,
    PublishAttempt = 3,
    GenerationRun = 4,
    Tenant = 5,
    HistoricalRepublish = 6,
}

public enum SpaceJobStatus : short
{
    Queued = 0,
    Running = 1,
    Succeeded = 2,
    Failed = 3,
    Cancelled = 4,
    DeadLetter = 5,
}

public enum SpaceJobAttemptOutcome : short
{
    Running = 0,
    Succeeded = 1,
    Failed = 2,
    Abandoned = 3,
    Cancelled = 4,
}

public enum SpaceJobStepStatus : short
{
    Running = 0,
    Succeeded = 1,
    Failed = 2,
    Reused = 3,
}

public enum SpacePublishAttemptStatus : short
{
    Requested = 0,
    Preflighting = 1,
    ApplyingWms = 2,
    VerifyingWms = 3,
    ActivatingRuntime = 4,
    Completed = 5,
    FailedNoEffect = 6,
    WaitingRetry = 7,
    ReconciliationRequired = 8,
    ManualIntervention = 9,
}

public enum SpacePublishStep : short
{
    Requested = 0,
    Preflight = 1,
    ApplyWms = 2,
    VerifyWms = 3,
    ActivateRuntime = 4,
    Complete = 5,
    Reconcile = 6,
}

public enum SpacePublishBatchStatus : short
{
    Pending = 0,
    Applying = 1,
    Applied = 2,
    FailedNoEffect = 3,
    Partial = 4,
    Uncertain = 5,
    Verified = 6,
}

public enum SpaceWmsReceiptOutcome : short
{
    Applied = 0,
    NotApplied = 1,
    Unknown = 2,
}

public enum SpaceReconciliationClassification : short
{
    WmsPartial = 0,
    WmsUncertain = 1,
    WmsReadBackMismatch = 2,
    RuntimeActivationFailed = 3,
}

public enum SpaceReconciliationStatus : short
{
    Open = 0,
    Investigating = 1,
    Resolved = 2,
    Escalated = 3,
}

public enum SpacePublishAuditEventType : short
{
    Queued = 0,
    ProcessingStarted = 1,
    PreflightPassed = 2,
    WmsApplyStarted = 3,
    WmsApplyObserved = 4,
    WmsVerified = 5,
    RuntimeActivationStarted = 6,
    Completed = 7,
    FailedNoEffect = 8,
    RetryScheduled = 9,
    ManualRetryRequested = 10,
    ReconciliationRequired = 11,
    ManualInterventionRequired = 12,
    ReconciliationResolved = 13,
    RetryableFailureObserved = 14,
    HistoricalRepublishQueued = 15,
}

public enum SpaceHistoricalRepublishStatus : short
{
    Requested = 0,
    SnapshotCloned = 1,
    ValidationPassed = 2,
    ValidationBlocked = 3,
    PublishQueued = 4,
}

public enum SpaceJobFailureKind : short
{
    Transient = 0,
    Resource = 1,
    Input = 2,
    Security = 3,
    Bug = 4,
}

public enum SpaceIssueSeverity : short
{
    Info = 0,
    Warning = 1,
    Blocking = 2,
}

public enum SpaceIssueStatus : short
{
    Open = 0,
    Resolved = 1,
    Acknowledged = 2,
}

public enum SpaceValidationStatus : short
{
    Queued = 0,
    Running = 1,
    Passed = 2,
    Blocked = 3,
    Failed = 4,
}

public enum SpaceIssueResolutionKind : short
{
    None = 0,
    CommandBatch = 1,
    ProposalDecision = 2,
    ProposalRejection = 3,
}

public enum SpaceAiPolicySnapshot : short
{
    Disabled = 0,
    MetadataOnly = 1,
    StructuredFeatures = 2,
}

public enum SpaceGenerationRunStatus : short
{
    Queued = 0,
    Preparing = 1,
    Inferring = 2,
    Validating = 3,
    AwaitingReview = 4,
    Applying = 5,
    Succeeded = 6,
    Failed = 7,
    Cancelled = 8,
    Stale = 9,
}

public enum SpaceGenerationProposalStatus : short
{
    Proposed = 0,
    Accepted = 1,
    Rejected = 2,
    Modified = 3,
    Applied = 4,
    Obsolete = 5,
}

public enum SpaceGenerationStagingValidationStatus : short
{
    Prepared = 0,
    Validated = 1,
}

public enum SpaceProposalDecisionType : short
{
    Accept = 1,
    Reject = 2,
    Modify = 3,
}

public enum SpaceLockedFactMatchMethod : short
{
    SameSourceIdentity = 1,
}

public enum SpaceConfidenceBand : short
{
    Low = 0,
    Medium = 1,
    High = 2,
}

public enum SpaceAiUsageOutcome : short
{
    Unknown = 0,
    Succeeded = 1,
    Failed = 2,
}

public enum SpaceAiBudgetReservationStatus : short
{
    Reserved = 0,
    Submitted = 1,
    Reported = 2,
    Released = 3,
    Reconciled = 4,
}
