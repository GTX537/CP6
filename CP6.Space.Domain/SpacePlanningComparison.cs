namespace CP6.Space.Domain;

public enum SpacePlanningRiskSeverity
{
    Information = 1,
    Warning = 2,
    Critical = 3,
}

public enum SpacePlanningDecisionOutcome
{
    Selected = 1,
    Deferred = 2,
    RejectedAll = 3,
}

public sealed record SpacePlanningComparisonData(
    Guid SiteId,
    Guid ModelId,
    Guid BasePublishedVersionId,
    Guid BaselineRunId,
    string Name,
    string DefinitionVersion,
    string RequestHash,
    string ComparisonHash,
    string SourceDatasetHash,
    string CurrencyCode,
    DateTimeOffset HistoricalFromUtc,
    DateTimeOffset HistoricalToUtc,
    int RunCount,
    decimal MinimumDistanceCoveragePercent,
    decimal MaximumPeakCapacityUtilizationPercent,
    decimal MaximumCongestionTaskHours,
    decimal? MaximumTotalCost);

public sealed class SpacePlanningComparison : SpaceTenantEntity
{
    private SpacePlanningComparison()
    {
    }

    public Guid SiteId { get; private set; }
    public Guid ModelId { get; private set; }
    public Guid BasePublishedVersionId { get; private set; }
    public Guid BaselineRunId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string DefinitionVersion { get; private set; } = string.Empty;
    public string RequestHash { get; private set; } = string.Empty;
    public string ComparisonHash { get; private set; } = string.Empty;
    public string SourceDatasetHash { get; private set; } = string.Empty;
    public string CurrencyCode { get; private set; } = string.Empty;
    public DateTimeOffset HistoricalFromUtc { get; private set; }
    public DateTimeOffset HistoricalToUtc { get; private set; }
    public int RunCount { get; private set; }
    public decimal MinimumDistanceCoveragePercent { get; private set; }
    public decimal MaximumPeakCapacityUtilizationPercent { get; private set; }
    public decimal MaximumCongestionTaskHours { get; private set; }
    public decimal? MaximumTotalCost { get; private set; }

    public static SpacePlanningComparison Create(
        Guid tenantId,
        Guid comparisonId,
        SpacePlanningComparisonData value)
    {
        ArgumentNullException.ThrowIfNull(value);
        Identity(comparisonId, nameof(comparisonId));
        Identity(value.SiteId, nameof(value.SiteId));
        Identity(value.ModelId, nameof(value.ModelId));
        Identity(value.BasePublishedVersionId, nameof(value.BasePublishedVersionId));
        Identity(value.BaselineRunId, nameof(value.BaselineRunId));
        if (value.RunCount is < 2 or > 10)
            throw new ArgumentOutOfRangeException(nameof(value.RunCount));
        Utc(value.HistoricalFromUtc, nameof(value.HistoricalFromUtc));
        Utc(value.HistoricalToUtc, nameof(value.HistoricalToUtc));
        if (value.HistoricalFromUtc >= value.HistoricalToUtc)
            throw new ArgumentException("The historical window must be positive.");
        Percentage(value.MinimumDistanceCoveragePercent,
            nameof(value.MinimumDistanceCoveragePercent));
        NonNegative(value.MaximumPeakCapacityUtilizationPercent,
            nameof(value.MaximumPeakCapacityUtilizationPercent), 4);
        NonNegative(value.MaximumCongestionTaskHours,
            nameof(value.MaximumCongestionTaskHours), 6);
        if (value.MaximumTotalCost.HasValue)
        {
            NonNegative(value.MaximumTotalCost.Value,
                nameof(value.MaximumTotalCost), 6);
        }

        var result = new SpacePlanningComparison
        {
            SiteId = value.SiteId,
            ModelId = value.ModelId,
            BasePublishedVersionId = value.BasePublishedVersionId,
            BaselineRunId = value.BaselineRunId,
            Name = Text(value.Name, 200, nameof(value.Name)),
            DefinitionVersion = Text(
                value.DefinitionVersion,
                100,
                nameof(value.DefinitionVersion)),
            RequestHash = Hash(value.RequestHash, nameof(value.RequestHash)),
            ComparisonHash = Hash(
                value.ComparisonHash,
                nameof(value.ComparisonHash)),
            SourceDatasetHash = Hash(
                value.SourceDatasetHash,
                nameof(value.SourceDatasetHash)),
            CurrencyCode = Currency(value.CurrencyCode),
            HistoricalFromUtc = value.HistoricalFromUtc,
            HistoricalToUtc = value.HistoricalToUtc,
            RunCount = value.RunCount,
            MinimumDistanceCoveragePercent =
                value.MinimumDistanceCoveragePercent,
            MaximumPeakCapacityUtilizationPercent =
                value.MaximumPeakCapacityUtilizationPercent,
            MaximumCongestionTaskHours = value.MaximumCongestionTaskHours,
            MaximumTotalCost = value.MaximumTotalCost,
        };
        result.SetTenant(tenantId);
        result.SetId(comparisonId);
        return result;
    }

    internal static void Identity(Guid value, string name)
    {
        if (value == Guid.Empty)
            throw new ArgumentException("Identity is required.", name);
    }

    internal static string Hash(string value, string name)
    {
        var normalized = value?.Trim().ToLowerInvariant();
        if (normalized is null ||
            normalized.Length != 64 ||
            !normalized.All(Uri.IsHexDigit))
        {
            throw new ArgumentException("A SHA-256 hash is required.", name);
        }
        return normalized;
    }

    internal static string Text(string value, int maximumLength, string name)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized) ||
            normalized.Length > maximumLength ||
            normalized.Any(char.IsControl))
        {
            throw new ArgumentException(
                $"A value of at most {maximumLength} characters is required.",
                name);
        }
        return normalized;
    }

    private static string Currency(string value)
    {
        var normalized = value?.Trim().ToUpperInvariant();
        if (normalized is null ||
            normalized.Length != 3 ||
            !normalized.All(char.IsAsciiLetter))
        {
            throw new ArgumentException(
                "A three-letter currency code is required.",
                nameof(value));
        }
        return normalized;
    }

    private static void Utc(DateTimeOffset value, string name)
    {
        if (value.Offset != TimeSpan.Zero)
            throw new ArgumentException("A UTC timestamp is required.", name);
    }

    private static void Percentage(decimal value, string name)
    {
        if (value < 0 || value > 100 || decimal.Round(value, 4) != value)
            throw new ArgumentOutOfRangeException(name);
    }

    private static void NonNegative(decimal value, string name, int scale)
    {
        if (value < 0 || decimal.Round(value, scale) != value)
            throw new ArgumentOutOfRangeException(name);
    }
}

public sealed record SpacePlanningComparisonEntryData(
    int SequenceNo,
    Guid RunId,
    Guid BranchId,
    Guid ScenarioVersionId,
    long ScenarioContentRevision,
    string RunName,
    string RunResultHash,
    bool IsBaseline,
    decimal DistanceCoveragePercent,
    decimal TotalDistanceMeters,
    long CongestionTaskSeconds,
    int OverloadedLocationCount,
    decimal PeakCapacityUtilizationPercent,
    decimal AverageCompletedTasksPerHour,
    decimal PeakCompletedTasksPerHour,
    decimal TotalCost,
    decimal DistanceDeltaMeters,
    long CongestionTaskSecondsDelta,
    int OverloadedLocationCountDelta,
    decimal PeakCapacityUtilizationDeltaPercentagePoints,
    decimal AverageCompletedTasksPerHourDelta,
    decimal TotalCostDelta,
    int RiskCount);

public sealed class SpacePlanningComparisonEntry : SpaceTenantEntity
{
    private SpacePlanningComparisonEntry()
    {
    }

    public Guid ComparisonId { get; private set; }
    public Guid SiteId { get; private set; }
    public int SequenceNo { get; private set; }
    public Guid RunId { get; private set; }
    public Guid BranchId { get; private set; }
    public Guid ScenarioVersionId { get; private set; }
    public long ScenarioContentRevision { get; private set; }
    public string RunName { get; private set; } = string.Empty;
    public string RunResultHash { get; private set; } = string.Empty;
    public bool IsBaseline { get; private set; }
    public decimal DistanceCoveragePercent { get; private set; }
    public decimal TotalDistanceMeters { get; private set; }
    public long CongestionTaskSeconds { get; private set; }
    public int OverloadedLocationCount { get; private set; }
    public decimal PeakCapacityUtilizationPercent { get; private set; }
    public decimal AverageCompletedTasksPerHour { get; private set; }
    public decimal PeakCompletedTasksPerHour { get; private set; }
    public decimal TotalCost { get; private set; }
    public decimal DistanceDeltaMeters { get; private set; }
    public long CongestionTaskSecondsDelta { get; private set; }
    public int OverloadedLocationCountDelta { get; private set; }
    public decimal PeakCapacityUtilizationDeltaPercentagePoints
        { get; private set; }
    public decimal AverageCompletedTasksPerHourDelta { get; private set; }
    public decimal TotalCostDelta { get; private set; }
    public int RiskCount { get; private set; }

    public static SpacePlanningComparisonEntry Create(
        SpacePlanningComparison comparison,
        SpacePlanningComparisonEntryData value)
    {
        ArgumentNullException.ThrowIfNull(comparison);
        ArgumentNullException.ThrowIfNull(value);
        SpacePlanningComparison.Identity(value.RunId, nameof(value.RunId));
        SpacePlanningComparison.Identity(value.BranchId, nameof(value.BranchId));
        SpacePlanningComparison.Identity(
            value.ScenarioVersionId,
            nameof(value.ScenarioVersionId));
        if (value.SequenceNo is < 1 or > 10 ||
            value.ScenarioContentRevision < 0 ||
            value.CongestionTaskSeconds < 0 ||
            value.OverloadedLocationCount < 0 ||
            value.RiskCount is < 0 or > 10)
        {
            throw new ArgumentOutOfRangeException(nameof(value));
        }
        if (value.DistanceCoveragePercent is < 0 or > 100 ||
            value.TotalDistanceMeters < 0 ||
            value.PeakCapacityUtilizationPercent < 0 ||
            value.AverageCompletedTasksPerHour < 0 ||
            value.PeakCompletedTasksPerHour < 0 ||
            value.TotalCost < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(value));
        }

        var result = new SpacePlanningComparisonEntry
        {
            ComparisonId = comparison.Id,
            SiteId = comparison.SiteId,
            SequenceNo = value.SequenceNo,
            RunId = value.RunId,
            BranchId = value.BranchId,
            ScenarioVersionId = value.ScenarioVersionId,
            ScenarioContentRevision = value.ScenarioContentRevision,
            RunName = SpacePlanningComparison.Text(
                value.RunName,
                200,
                nameof(value.RunName)),
            RunResultHash = SpacePlanningComparison.Hash(
                value.RunResultHash,
                nameof(value.RunResultHash)),
            IsBaseline = value.IsBaseline,
            DistanceCoveragePercent = value.DistanceCoveragePercent,
            TotalDistanceMeters = value.TotalDistanceMeters,
            CongestionTaskSeconds = value.CongestionTaskSeconds,
            OverloadedLocationCount = value.OverloadedLocationCount,
            PeakCapacityUtilizationPercent =
                value.PeakCapacityUtilizationPercent,
            AverageCompletedTasksPerHour =
                value.AverageCompletedTasksPerHour,
            PeakCompletedTasksPerHour = value.PeakCompletedTasksPerHour,
            TotalCost = value.TotalCost,
            DistanceDeltaMeters = value.DistanceDeltaMeters,
            CongestionTaskSecondsDelta = value.CongestionTaskSecondsDelta,
            OverloadedLocationCountDelta =
                value.OverloadedLocationCountDelta,
            PeakCapacityUtilizationDeltaPercentagePoints =
                value.PeakCapacityUtilizationDeltaPercentagePoints,
            AverageCompletedTasksPerHourDelta =
                value.AverageCompletedTasksPerHourDelta,
            TotalCostDelta = value.TotalCostDelta,
            RiskCount = value.RiskCount,
        };
        result.SetTenant(comparison.TenantId);
        return result;
    }
}

public sealed class SpacePlanningComparisonRisk : SpaceTenantEntity
{
    private SpacePlanningComparisonRisk()
    {
    }

    public Guid ComparisonId { get; private set; }
    public Guid EntryId { get; private set; }
    public Guid RunId { get; private set; }
    public string Code { get; private set; } = string.Empty;
    public SpacePlanningRiskSeverity Severity { get; private set; }

    public static SpacePlanningComparisonRisk Create(
        SpacePlanningComparison comparison,
        SpacePlanningComparisonEntry entry,
        string code,
        SpacePlanningRiskSeverity severity)
    {
        ArgumentNullException.ThrowIfNull(comparison);
        ArgumentNullException.ThrowIfNull(entry);
        if (entry.ComparisonId != comparison.Id ||
            entry.TenantId != comparison.TenantId ||
            !Enum.IsDefined(severity))
        {
            throw new ArgumentException("Comparison risk evidence is invalid.");
        }
        var result = new SpacePlanningComparisonRisk
        {
            ComparisonId = comparison.Id,
            EntryId = entry.Id,
            RunId = entry.RunId,
            Code = SpacePlanningComparison.Text(code, 100, nameof(code)),
            Severity = severity,
        };
        result.SetTenant(comparison.TenantId);
        return result;
    }
}

public sealed record SpacePlanningDecisionRecordData(
    Guid SiteId,
    Guid ComparisonId,
    Guid? SelectedRunId,
    Guid? SupersedesDecisionId,
    SpacePlanningDecisionOutcome Outcome,
    string Rationale,
    string ComparisonHash,
    string RequestHash,
    string DefinitionVersion);

public sealed class SpacePlanningDecisionRecord : SpaceTenantEntity
{
    private SpacePlanningDecisionRecord()
    {
    }

    public Guid SiteId { get; private set; }
    public Guid ComparisonId { get; private set; }
    public Guid? SelectedRunId { get; private set; }
    public Guid? SupersedesDecisionId { get; private set; }
    public SpacePlanningDecisionOutcome Outcome { get; private set; }
    public string Rationale { get; private set; } = string.Empty;
    public string ComparisonHash { get; private set; } = string.Empty;
    public string RequestHash { get; private set; } = string.Empty;
    public string DefinitionVersion { get; private set; } = string.Empty;

    public static SpacePlanningDecisionRecord Create(
        Guid tenantId,
        Guid decisionId,
        SpacePlanningDecisionRecordData value)
    {
        ArgumentNullException.ThrowIfNull(value);
        SpacePlanningComparison.Identity(decisionId, nameof(decisionId));
        SpacePlanningComparison.Identity(value.SiteId, nameof(value.SiteId));
        SpacePlanningComparison.Identity(
            value.ComparisonId,
            nameof(value.ComparisonId));
        if (!Enum.IsDefined(value.Outcome))
            throw new ArgumentOutOfRangeException(nameof(value.Outcome));
        if (value.Outcome == SpacePlanningDecisionOutcome.Selected)
        {
            if (!value.SelectedRunId.HasValue ||
                value.SelectedRunId.Value == Guid.Empty)
            {
                throw new ArgumentException(
                    "A selected outcome requires a simulation run.");
            }
        }
        else if (value.SelectedRunId.HasValue)
        {
            throw new ArgumentException(
                "Only a selected outcome may reference a simulation run.");
        }
        if (value.SupersedesDecisionId == Guid.Empty ||
            value.SupersedesDecisionId == decisionId)
        {
            throw new ArgumentException("Superseded decision evidence is invalid.");
        }

        var rationale = NormalizeRationale(value.Rationale);
        var result = new SpacePlanningDecisionRecord
        {
            SiteId = value.SiteId,
            ComparisonId = value.ComparisonId,
            SelectedRunId = value.SelectedRunId,
            SupersedesDecisionId = value.SupersedesDecisionId,
            Outcome = value.Outcome,
            Rationale = rationale,
            ComparisonHash = SpacePlanningComparison.Hash(
                value.ComparisonHash,
                nameof(value.ComparisonHash)),
            RequestHash = SpacePlanningComparison.Hash(
                value.RequestHash,
                nameof(value.RequestHash)),
            DefinitionVersion = SpacePlanningComparison.Text(
                value.DefinitionVersion,
                100,
                nameof(value.DefinitionVersion)),
        };
        result.SetTenant(tenantId);
        result.SetId(decisionId);
        return result;
    }

    private static string NormalizeRationale(string value)
    {
        var normalized = value?.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Trim();
        if (string.IsNullOrWhiteSpace(normalized) ||
            normalized.Length > 2_000 ||
            normalized.Any(character =>
                char.IsControl(character) && character is not '\n' and not '\t'))
        {
            throw new ArgumentException(
                "A rationale of at most 2000 characters is required.",
                nameof(value));
        }
        return normalized;
    }
}
