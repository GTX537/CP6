namespace CP6.Space.Contracts;

/// <summary>
/// Stable Design API error codes frozen by the Space MVP baseline.
/// HTTP Problem Details mapping is introduced with E01-S05.
/// </summary>
public static class SpaceErrorCodes
{
    public const string AuthenticationRequired = "SPACE_AUTHENTICATION_REQUIRED";
    public const string TenantScopeDenied = "SPACE_TENANT_SCOPE_DENIED";
    public const string ExternalSubjectDenied = "SPACE_EXTERNAL_SUBJECT_DENIED";
    public const string PermissionDenied = "SPACE_PERMISSION_DENIED";
    public const string DesignApiDisabled = "SPACE_DESIGN_API_DISABLED";
    public const string ModelNotFound = "SPACE_MODEL_NOT_FOUND";
    public const string VersionNotFound = "SPACE_VERSION_NOT_FOUND";
    public const string LogicalIdNotFound = "SPACE_LOGICAL_ID_NOT_FOUND";
    public const string SourceNotFound = "SPACE_SOURCE_NOT_FOUND";
    public const string JobNotFound = "SPACE_JOB_NOT_FOUND";
    public const string IssueNotFound = "SPACE_ISSUE_NOT_FOUND";
    public const string VersionConflict = "SPACE_VERSION_CONFLICT";
    public const string VersionStateInvalid = "SPACE_VERSION_STATE_INVALID";
    public const string SourceConflict = "SPACE_SOURCE_CONFLICT";
    public const string AssetScopeDenied = "SPACE_ASSET_SCOPE_DENIED";
    public const string AssetConflict = "SPACE_ASSET_CONFLICT";
    public const string IdempotencyConflict = "SPACE_IDEMPOTENCY_KEY_REUSED";
    public const string IdempotencyKeyRequired = "SPACE_IDEMPOTENCY_KEY_REQUIRED";
    public const string CursorInvalid = "SPACE_CURSOR_INVALID";
    public const string CursorScopeMismatch = "SPACE_CURSOR_SCOPE_MISMATCH";
    public const string RequestInvalid = "SPACE_REQUEST_INVALID";
    public const string ConcurrencyConflict = "SPACE_CONCURRENCY_CONFLICT";
    public const string ExcelMappingProfileInvalid =
        "SPACE_MAPPING_PROFILE_INVALID";
    public const string ExcelMappingProfileNotFound =
        "SPACE_MAPPING_PROFILE_NOT_FOUND";
    public const string ExcelMappingProfileConflict =
        "SPACE_MAPPING_PROFILE_CONFLICT";
    public const string ExcelMappingProfileReadOnly =
        "SPACE_MAPPING_PROFILE_READ_ONLY";
    public const string ExcelPreflightInvalid =
        "SPACE_EXCEL_PREFLIGHT_INVALID";
    public const string ExcelPreflightNotFound =
        "SPACE_EXCEL_PREFLIGHT_NOT_FOUND";
    public const string ExcelWorkbookInvalid =
        "SPACE_EXCEL_WORKBOOK_INVALID";
    public const string FileTooLarge = "SPACE_FILE_TOO_LARGE";
    public const string FileTypeMismatch = "SPACE_FILE_TYPE_MISMATCH";
    public const string FileQuarantined = "SPACE_FILE_QUARANTINED";
    public const string FileMalwareDetected = "SPACE_FILE_MALWARE_DETECTED";
    public const string FileArchiveBomb = "SPACE_FILE_ARCHIVE_BOMB";
    public const string FileEncryptedUnsupported =
        "SPACE_FILE_ENCRYPTED_UNSUPPORTED";
    public const string FileActiveContent = "SPACE_FILE_ACTIVE_CONTENT";
    public const string FileCorrupt = "SPACE_FILE_CORRUPT";
    public const string FileNotFound = "SPACE_FILE_NOT_FOUND";
    public const string UnderlaySourceInvalid = "SPACE_UNDERLAY_SOURCE_INVALID";
    public const string UnderlayCalibrationInvalid =
        "SPACE_UNDERLAY_CALIBRATION_INVALID";
    public const string UnderlayCalibrationOutOfTolerance =
        "SPACE_UNDERLAY_CALIBRATION_OUT_OF_TOLERANCE";
    public const string FloorRevisionConflict = "SPACE_FLOOR_REVISION_CONFLICT";
    public const string CommandSchemaUnsupported =
        "SPACE_COMMAND_SCHEMA_UNSUPPORTED";
    public const string CommandConflict = "SPACE_COMMAND_CONFLICT";
    public const string SourceUnsafe = "SPACE_SOURCE_UNSAFE";
    public const string JobLeaseLost = "SPACE_JOB_LEASE_LOST";
    public const string JobNotRetryable = "SPACE_JOB_NOT_RETRYABLE";
    public const string JobProcessorUnavailable =
        "SPACE_JOB_PROCESSOR_UNAVAILABLE";
    public const string JobProcessorFailed =
        "SPACE_JOB_PROCESSOR_FAILED";
    public const string JobTimeout = "SPACE_JOB_TIMEOUT";
    public const string ParseFailed = "SPACE_PARSE_FAILED";
    public const string AiDisabled = "SPACE_AI_DISABLED";
    public const string AiQuotaExceeded = "SPACE_AI_QUOTA_EXCEEDED";
    public const string AiProviderUnavailable =
        "SPACE_AI_PROVIDER_UNAVAILABLE";
    public const string AiOutputInvalid =
        "SPACE_AI_OUTPUT_INVALID";
    public const string RackProfileRequired =
        "SPACE_RACK_PROFILE_REQUIRED";
    public const string AiSourcePolicyDenied =
        "SPACE_AI_SOURCE_POLICY_DENIED";
    public const string AiPolicyInvalid = "SPACE_AI_POLICY_INVALID";
    public const string AiPolicyConflict = "SPACE_AI_POLICY_CONFLICT";
    public const string AiProviderAliasNotApproved =
        "SPACE_AI_PROVIDER_ALIAS_NOT_APPROVED";
    public const string AiUsageQueryInvalid =
        "SPACE_AI_USAGE_QUERY_INVALID";
    public const string WmsUnavailable = "SPACE_WMS_UNAVAILABLE";
    public const string WmsRuntimeContractViolation =
        "SPACE_WMS_RUNTIME_CONTRACT_VIOLATION";
    public const string WmsAdoptionNotFound =
        "SPACE_WMS_ADOPTION_NOT_FOUND";
    public const string WmsAdoptionDuplicate =
        "SPACE_WMS_ADOPTION_DUPLICATE";
    public const string WmsAdoptionMissing =
        "SPACE_WMS_ADOPTION_MISSING";
    public const string WmsLocationUnbound =
        "SPACE_WMS_LOCATION_UNBOUND";
    public const string WmsLocationCodeDuplicate =
        "SPACE_WMS_LOCATION_CODE_DUPLICATE";
    public const string WmsBindingGeometryMissing =
        "SPACE_WMS_BINDING_GEOMETRY_MISSING";
    public const string PersonnelEventInvalid =
        "SPACE_PERSONNEL_EVENT_INVALID";
    public const string PersonnelEventConflict =
        "SPACE_PERSONNEL_EVENT_CONFLICT";
    public const string PersonnelSiteNotFound =
        "SPACE_PERSONNEL_SITE_NOT_FOUND";
    public const string PersonnelQueryInvalid =
        "SPACE_PERSONNEL_QUERY_INVALID";
    public const string PersonnelNotFound =
        "SPACE_PERSONNEL_NOT_FOUND";
    public const string OperationsDiagnosticsInternalOnly =
        "SPACE_OPERATIONS_DIAGNOSTICS_INTERNAL_ONLY";
    public const string OperationsDiagnosticsEvidenceLimit =
        "SPACE_OPERATIONS_DIAGNOSTICS_EVIDENCE_LIMIT";
    public const string PutawayRecommendationsInternalOnly =
        "SPACE_PUTAWAY_RECOMMENDATIONS_INTERNAL_ONLY";
    public const string PutawayRecommendationConflict =
        "SPACE_PUTAWAY_RECOMMENDATION_CONFLICT";
    public const string PutawayRecommendationNotFound =
        "SPACE_PUTAWAY_RECOMMENDATION_NOT_FOUND";
    public const string PutawayRecommendationEvidenceLimit =
        "SPACE_PUTAWAY_RECOMMENDATION_EVIDENCE_LIMIT";
    public const string DispatchRecommendationsInternalOnly =
        "SPACE_DISPATCH_RECOMMENDATIONS_INTERNAL_ONLY";
    public const string DispatchRecommendationConflict =
        "SPACE_DISPATCH_RECOMMENDATION_CONFLICT";
    public const string DispatchRecommendationNotFound =
        "SPACE_DISPATCH_RECOMMENDATION_NOT_FOUND";
    public const string DispatchRecommendationEvidenceLimit =
        "SPACE_DISPATCH_RECOMMENDATION_EVIDENCE_LIMIT";
    public const string DispatchRecommendationPairLimit =
        "SPACE_DISPATCH_RECOMMENDATION_PAIR_LIMIT";
    public const string DispatchApprovalInternalOnly =
        "SPACE_DISPATCH_APPROVAL_INTERNAL_ONLY";
    public const string DispatchApprovalInvalid =
        "SPACE_DISPATCH_APPROVAL_INVALID";
    public const string DispatchApprovalConflict =
        "SPACE_DISPATCH_APPROVAL_CONFLICT";
    public const string DispatchApprovalActive =
        "SPACE_DISPATCH_APPROVAL_ACTIVE";
    public const string DispatchApprovalNotFound =
        "SPACE_DISPATCH_APPROVAL_NOT_FOUND";
    public const string DispatchApprovalCancelForbidden =
        "SPACE_DISPATCH_APPROVAL_CANCEL_FORBIDDEN";
    public const string DispatchApprovalNotPending =
        "SPACE_DISPATCH_APPROVAL_NOT_PENDING";
    public const string DispatchApprovalFlowUnavailable =
        "SPACE_DISPATCH_APPROVAL_FLOW_UNAVAILABLE";
    public const string DispatchExecutionInvalid =
        "SPACE_DISPATCH_EXECUTION_INVALID";
    public const string DispatchExecutionConflict =
        "SPACE_DISPATCH_EXECUTION_CONFLICT";
    public const string DispatchExecutionRetryUnavailable =
        "SPACE_DISPATCH_EXECUTION_RETRY_UNAVAILABLE";
    public const string DispatchExecutionRetryLimit =
        "SPACE_DISPATCH_EXECUTION_RETRY_LIMIT";
    public const string DispatchExecutionCompensationUnavailable =
        "SPACE_DISPATCH_EXECUTION_COMPENSATION_UNAVAILABLE";
    public const string DispatchExecutionEvidenceInvalid =
        "SPACE_DISPATCH_EXECUTION_EVIDENCE_INVALID";
    public const string DispatchEvaluationEvidenceInvalid =
        "SPACE_DISPATCH_EVALUATION_EVIDENCE_INVALID";
    public const string DeviceEventInvalid =
        "SPACE_DEVICE_EVENT_INVALID";
    public const string DeviceEventConflict =
        "SPACE_DEVICE_EVENT_CONFLICT";
    public const string DeviceMappingNotFound =
        "SPACE_DEVICE_MAPPING_NOT_FOUND";
    public const string DeviceMappingConflict =
        "SPACE_DEVICE_MAPPING_CONFLICT";
    public const string DeviceMappingStale =
        "SPACE_DEVICE_MAPPING_STALE";
    public const string DeviceElementNotFound =
        "SPACE_DEVICE_ELEMENT_NOT_FOUND";
    public const string DeviceSiteNotFound =
        "SPACE_DEVICE_SITE_NOT_FOUND";
    public const string DeviceQueryInvalid =
        "SPACE_DEVICE_QUERY_INVALID";
    public const string WmsBindingCodeMismatch =
        "SPACE_WMS_BINDING_CODE_MISMATCH";
    public const string WmsLocationMissing =
        "SPACE_WMS_LOCATION_MISSING";
    public const string ExternalOrganizationNotFound =
        "SPACE_EXTERNAL_ORGANIZATION_NOT_FOUND";
    public const string ExternalOrganizationConflict =
        "SPACE_EXTERNAL_ORGANIZATION_CONFLICT";
    public const string ExternalMembershipNotFound =
        "SPACE_EXTERNAL_MEMBERSHIP_NOT_FOUND";
    public const string ExternalMembershipConflict =
        "SPACE_EXTERNAL_MEMBERSHIP_CONFLICT";
    public const string ExternalGrantNotFound =
        "SPACE_EXTERNAL_GRANT_NOT_FOUND";
    public const string ExternalGrantConflict =
        "SPACE_EXTERNAL_GRANT_CONFLICT";
    public const string ExternalGrantScopeInvalid =
        "SPACE_EXTERNAL_GRANT_SCOPE_INVALID";
    public const string FieldPolicyNotFound =
        "SPACE_FIELD_POLICY_NOT_FOUND";
    public const string FieldPolicyConflict =
        "SPACE_FIELD_POLICY_CONFLICT";
    public const string FieldPolicyInvalid =
        "SPACE_FIELD_POLICY_INVALID";
    public const string FieldPolicyDenied =
        "SPACE_FIELD_POLICY_DENIED";
    public const string ExternalPortalSubjectRequired =
        "SPACE_EXTERNAL_PORTAL_SUBJECT_REQUIRED";
    public const string ExternalPortalReadOnly =
        "SPACE_EXTERNAL_PORTAL_READ_ONLY";
    public const string ExternalOrganizationContextRequired =
        "SPACE_EXTERNAL_ORGANIZATION_CONTEXT_REQUIRED";
    public const string ExternalMembershipInactive =
        "SPACE_EXTERNAL_MEMBERSHIP_INACTIVE";
    public const string ExternalGrantInactive =
        "SPACE_EXTERNAL_GRANT_INACTIVE";
    public const string ExternalScopeDenied =
        "SPACE_EXTERNAL_SCOPE_DENIED";
    public const string ExternalScopeAllowed =
        "SPACE_EXTERNAL_SCOPE_ALLOWED";
    public const string InternalScopeAllowed =
        "SPACE_INTERNAL_SCOPE_ALLOWED";
    public const string ExternalReferenceNotFound =
        "SPACE_EXTERNAL_REFERENCE_NOT_FOUND";
    public const string ExternalAccessStateInvalid =
        "SPACE_EXTERNAL_ACCESS_STATE_INVALID";
    public const string AuditUnavailable = "SPACE_AUDIT_UNAVAILABLE";
    public const string PlanningScenarioInternalOnly =
        "SPACE_PLANNING_SCENARIO_INTERNAL_ONLY";
    public const string PlanningScenarioConflict =
        "SPACE_PLANNING_SCENARIO_CONFLICT";
    public const string PlanningScenarioNotFound =
        "SPACE_PLANNING_SCENARIO_NOT_FOUND";
    public const string PlanningScenarioBaseInvalid =
        "SPACE_PLANNING_SCENARIO_BASE_INVALID";
    public const string PlanningScenarioProductionDenied =
        "SPACE_PLANNING_SCENARIO_PRODUCTION_DENIED";
    public const string PlanningDatasetConflict =
        "SPACE_PLANNING_DATASET_CONFLICT";
    public const string PlanningDatasetNotFound =
        "SPACE_PLANNING_DATASET_NOT_FOUND";
    public const string PlanningDatasetBranchNotReady =
        "SPACE_PLANNING_DATASET_BRANCH_NOT_READY";
    public const string PlanningDatasetDeidentificationRequired =
        "SPACE_PLANNING_DATASET_DEIDENTIFICATION_REQUIRED";
    public const string PlanningDatasetInvalid =
        "SPACE_PLANNING_DATASET_INVALID";
    public const string PlanningDatasetLocationInvalid =
        "SPACE_PLANNING_DATASET_LOCATION_INVALID";
    public const string PlanningSimulationConflict =
        "SPACE_PLANNING_SIMULATION_CONFLICT";
    public const string PlanningSimulationNotFound =
        "SPACE_PLANNING_SIMULATION_NOT_FOUND";
    public const string PlanningSimulationBranchNotReady =
        "SPACE_PLANNING_SIMULATION_BRANCH_NOT_READY";
    public const string PlanningSimulationDatasetInvalid =
        "SPACE_PLANNING_SIMULATION_DATASET_INVALID";
    public const string PlanningSimulationRequestInvalid =
        "SPACE_PLANNING_SIMULATION_REQUEST_INVALID";
    public const string PlanningSimulationGeometryInvalid =
        "SPACE_PLANNING_SIMULATION_GEOMETRY_INVALID";
    public const string PlanningComparisonConflict =
        "SPACE_PLANNING_COMPARISON_CONFLICT";
    public const string PlanningComparisonNotFound =
        "SPACE_PLANNING_COMPARISON_NOT_FOUND";
    public const string PlanningComparisonRequestInvalid =
        "SPACE_PLANNING_COMPARISON_REQUEST_INVALID";
    public const string PlanningComparisonEvidenceInvalid =
        "SPACE_PLANNING_COMPARISON_EVIDENCE_INVALID";
    public const string PlanningDecisionConflict =
        "SPACE_PLANNING_DECISION_CONFLICT";
    public const string PlanningDecisionNotFound =
        "SPACE_PLANNING_DECISION_NOT_FOUND";
    public const string PlanningExchangeUnavailable =
        "SPACE_PLANNING_EXCHANGE_UNAVAILABLE";
    public const string PlanningExchangeGeometryInvalid =
        "SPACE_PLANNING_EXCHANGE_GEOMETRY_INVALID";
    public const string PlanningDecisionInvalid =
        "SPACE_PLANNING_DECISION_INVALID";
}
