namespace CP6.Space.Contracts;

public sealed record SpaceAiApprovedProviderDto(
    string Alias,
    string Kind);

public sealed record SpaceAiPolicyDto(
    int Version,
    string DataPolicy,
    IReadOnlyList<Guid> AllowedSiteIds,
    IReadOnlyList<string> AllowedProviderAliases,
    int MaxConcurrentRuns,
    bool ExternalProviderEnabled,
    long? DailyBudgetMinor,
    long? MonthlyBudgetMinor,
    string? Currency,
    IReadOnlyList<SpaceAiApprovedProviderDto> ApprovedProviders,
    DateTime? UpdatedAtUtc,
    Guid? UpdatedBy);

public sealed record UpdateSpaceAiPolicyRequest(
    int ExpectedVersion,
    string DataPolicy,
    IReadOnlyList<Guid> AllowedSiteIds,
    IReadOnlyList<string> AllowedProviderAliases,
    int MaxConcurrentRuns,
    bool ExternalProviderEnabled,
    long? DailyBudgetMinor,
    long? MonthlyBudgetMinor,
    string? Currency);

public sealed record UpdateSpaceAiPolicyResponse(
    SpaceAiPolicyDto Policy,
    bool IdempotentReplay);

public sealed record SpaceAiUsageQuery(
    DateTime? FromUtc = null,
    DateTime? ToUtc = null,
    string? ProviderAlias = null,
    string? Outcome = null,
    int Page = 1,
    int PageSize = 25);

public sealed record SpaceAiUsageItemDto(
    Guid Id,
    Guid RunId,
    string ProviderAlias,
    string ProviderModel,
    long InputUnits,
    long OutputUnits,
    long EstimatedCostMinor,
    long? ActualCostMinor,
    string? Currency,
    long LatencyMs,
    string Outcome,
    DateTime RecordedAtUtc);

public sealed record SpaceAiBudgetBalanceDto(
    long? LimitMinor,
    long ConsumedMinor,
    long? RemainingMinor,
    string? Currency);

public sealed record SpaceAiUsageSummaryDto(
    long TotalRuns,
    long InputUnits,
    long OutputUnits,
    long EstimatedCostMinor,
    long ActualCostMinor,
    bool HasUnpricedUsage,
    SpaceAiBudgetBalanceDto DailyBudget,
    SpaceAiBudgetBalanceDto MonthlyBudget);

public sealed record SpaceAiUsagePageDto(
    IReadOnlyList<SpaceAiUsageItemDto> Items,
    long Total,
    int Page,
    int PageSize,
    SpaceAiUsageSummaryDto Summary);
