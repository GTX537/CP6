namespace CP6.Space.Domain;

public sealed record SpacePlanningSimulationRunData(
    Guid SiteId,
    Guid ModelId,
    Guid BranchId,
    Guid ScenarioVersionId,
    long ScenarioContentRevision,
    Guid DatasetId,
    string Name,
    string DefinitionVersion,
    string RequestHash,
    string DatasetRequestHash,
    string ResultHash,
    string GeometryBasis,
    string CurrencyCode,
    decimal DefaultQuantityCapacity,
    int DefaultConcurrentTaskCapacity,
    int LocationCapacityOverrideCount,
    int ThroughputWindowMinutes,
    decimal DistanceCostPerMeter,
    decimal LaborCostPerHour,
    decimal CongestionCostPerTaskHour,
    int TaskCount,
    int CompletedTaskCount,
    decimal CompletedQuantity,
    int DistanceEligibleTaskCount,
    decimal TotalDistanceMeters,
    decimal DistanceCoveragePercent,
    int PeakConcurrentTasks,
    long CongestionSeconds,
    long CongestionTaskSeconds,
    int OverloadedLocationCount,
    decimal PeakCapacityUtilizationPercent,
    decimal AverageCompletedTasksPerHour,
    decimal PeakCompletedTasksPerHour,
    decimal AverageCompletedQuantityPerHour,
    decimal PeakCompletedQuantityPerHour,
    decimal LaborHours,
    decimal DistanceCost,
    decimal LaborCost,
    decimal CongestionCost,
    decimal TotalCost);

public sealed class SpacePlanningSimulationRun : SpaceTenantEntity
{
    private SpacePlanningSimulationRun()
    {
    }

    public Guid SiteId { get; private set; }
    public Guid ModelId { get; private set; }
    public Guid BranchId { get; private set; }
    public Guid ScenarioVersionId { get; private set; }
    public long ScenarioContentRevision { get; private set; }
    public Guid DatasetId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string DefinitionVersion { get; private set; } = string.Empty;
    public string RequestHash { get; private set; } = string.Empty;
    public string DatasetRequestHash { get; private set; } = string.Empty;
    public string ResultHash { get; private set; } = string.Empty;
    public string GeometryBasis { get; private set; } = string.Empty;
    public string CurrencyCode { get; private set; } = string.Empty;
    public decimal DefaultQuantityCapacity { get; private set; }
    public int DefaultConcurrentTaskCapacity { get; private set; }
    public int LocationCapacityOverrideCount { get; private set; }
    public int ThroughputWindowMinutes { get; private set; }
    public decimal DistanceCostPerMeter { get; private set; }
    public decimal LaborCostPerHour { get; private set; }
    public decimal CongestionCostPerTaskHour { get; private set; }
    public int TaskCount { get; private set; }
    public int CompletedTaskCount { get; private set; }
    public decimal CompletedQuantity { get; private set; }
    public int DistanceEligibleTaskCount { get; private set; }
    public decimal TotalDistanceMeters { get; private set; }
    public decimal DistanceCoveragePercent { get; private set; }
    public int PeakConcurrentTasks { get; private set; }
    public long CongestionSeconds { get; private set; }
    public long CongestionTaskSeconds { get; private set; }
    public int OverloadedLocationCount { get; private set; }
    public decimal PeakCapacityUtilizationPercent { get; private set; }
    public decimal AverageCompletedTasksPerHour { get; private set; }
    public decimal PeakCompletedTasksPerHour { get; private set; }
    public decimal AverageCompletedQuantityPerHour { get; private set; }
    public decimal PeakCompletedQuantityPerHour { get; private set; }
    public decimal LaborHours { get; private set; }
    public decimal DistanceCost { get; private set; }
    public decimal LaborCost { get; private set; }
    public decimal CongestionCost { get; private set; }
    public decimal TotalCost { get; private set; }

    public static SpacePlanningSimulationRun Create(
        Guid tenantId,
        Guid runId,
        SpacePlanningSimulationRunData value)
    {
        ArgumentNullException.ThrowIfNull(value);
        Identity(runId, nameof(runId));
        Identity(value.SiteId, nameof(value.SiteId));
        Identity(value.ModelId, nameof(value.ModelId));
        Identity(value.BranchId, nameof(value.BranchId));
        Identity(value.ScenarioVersionId, nameof(value.ScenarioVersionId));
        if (value.ScenarioContentRevision < 0)
            throw new ArgumentOutOfRangeException(nameof(value.ScenarioContentRevision));
        Identity(value.DatasetId, nameof(value.DatasetId));
        Positive(value.DefaultQuantityCapacity, nameof(value.DefaultQuantityCapacity));
        if (value.DefaultConcurrentTaskCapacity is < 1 or > 10_000)
            throw new ArgumentOutOfRangeException(nameof(value.DefaultConcurrentTaskCapacity));
        if (value.LocationCapacityOverrideCount is < 0 or > 10_000)
            throw new ArgumentOutOfRangeException(nameof(value.LocationCapacityOverrideCount));
        if (value.ThroughputWindowMinutes is < 1 or > 1_440)
            throw new ArgumentOutOfRangeException(nameof(value.ThroughputWindowMinutes));
        NonNegative(value.DistanceCostPerMeter, nameof(value.DistanceCostPerMeter));
        NonNegative(value.LaborCostPerHour, nameof(value.LaborCostPerHour));
        NonNegative(value.CongestionCostPerTaskHour, nameof(value.CongestionCostPerTaskHour));
        if (value.TaskCount < 1 ||
            value.CompletedTaskCount < 0 ||
            value.CompletedTaskCount > value.TaskCount ||
            value.DistanceEligibleTaskCount < 0 ||
            value.DistanceEligibleTaskCount > value.TaskCount ||
            value.PeakConcurrentTasks < 0 ||
            value.CongestionSeconds < 0 ||
            value.CongestionTaskSeconds < 0 ||
            value.OverloadedLocationCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(value));
        }
        NonNegative(value.TotalDistanceMeters, nameof(value.TotalDistanceMeters));
        NonNegative(value.CompletedQuantity, nameof(value.CompletedQuantity));
        Percentage(value.DistanceCoveragePercent, nameof(value.DistanceCoveragePercent));
        NonNegative(value.PeakCapacityUtilizationPercent, nameof(value.PeakCapacityUtilizationPercent));
        NonNegative(value.AverageCompletedTasksPerHour, nameof(value.AverageCompletedTasksPerHour));
        NonNegative(value.PeakCompletedTasksPerHour, nameof(value.PeakCompletedTasksPerHour));
        NonNegative(value.AverageCompletedQuantityPerHour, nameof(value.AverageCompletedQuantityPerHour));
        NonNegative(value.PeakCompletedQuantityPerHour, nameof(value.PeakCompletedQuantityPerHour));
        NonNegative(value.LaborHours, nameof(value.LaborHours));
        NonNegative(value.DistanceCost, nameof(value.DistanceCost));
        NonNegative(value.LaborCost, nameof(value.LaborCost));
        NonNegative(value.CongestionCost, nameof(value.CongestionCost));
        NonNegative(value.TotalCost, nameof(value.TotalCost));

        var result = new SpacePlanningSimulationRun
        {
            SiteId = value.SiteId,
            ModelId = value.ModelId,
            BranchId = value.BranchId,
            ScenarioVersionId = value.ScenarioVersionId,
            ScenarioContentRevision = value.ScenarioContentRevision,
            DatasetId = value.DatasetId,
            Name = Text(value.Name, 200, nameof(value.Name)),
            DefinitionVersion = Text(value.DefinitionVersion, 100, nameof(value.DefinitionVersion)),
            RequestHash = Hash(value.RequestHash, nameof(value.RequestHash)),
            DatasetRequestHash = Hash(value.DatasetRequestHash, nameof(value.DatasetRequestHash)),
            ResultHash = Hash(value.ResultHash, nameof(value.ResultHash)),
            GeometryBasis = Text(value.GeometryBasis, 100, nameof(value.GeometryBasis)),
            CurrencyCode = Currency(value.CurrencyCode),
            DefaultQuantityCapacity = value.DefaultQuantityCapacity,
            DefaultConcurrentTaskCapacity = value.DefaultConcurrentTaskCapacity,
            LocationCapacityOverrideCount = value.LocationCapacityOverrideCount,
            ThroughputWindowMinutes = value.ThroughputWindowMinutes,
            DistanceCostPerMeter = value.DistanceCostPerMeter,
            LaborCostPerHour = value.LaborCostPerHour,
            CongestionCostPerTaskHour = value.CongestionCostPerTaskHour,
            TaskCount = value.TaskCount,
            CompletedTaskCount = value.CompletedTaskCount,
            CompletedQuantity = value.CompletedQuantity,
            DistanceEligibleTaskCount = value.DistanceEligibleTaskCount,
            TotalDistanceMeters = value.TotalDistanceMeters,
            DistanceCoveragePercent = value.DistanceCoveragePercent,
            PeakConcurrentTasks = value.PeakConcurrentTasks,
            CongestionSeconds = value.CongestionSeconds,
            CongestionTaskSeconds = value.CongestionTaskSeconds,
            OverloadedLocationCount = value.OverloadedLocationCount,
            PeakCapacityUtilizationPercent = value.PeakCapacityUtilizationPercent,
            AverageCompletedTasksPerHour = value.AverageCompletedTasksPerHour,
            PeakCompletedTasksPerHour = value.PeakCompletedTasksPerHour,
            AverageCompletedQuantityPerHour = value.AverageCompletedQuantityPerHour,
            PeakCompletedQuantityPerHour = value.PeakCompletedQuantityPerHour,
            LaborHours = value.LaborHours,
            DistanceCost = value.DistanceCost,
            LaborCost = value.LaborCost,
            CongestionCost = value.CongestionCost,
            TotalCost = value.TotalCost,
        };
        result.SetTenant(tenantId);
        result.SetId(runId);
        return result;
    }

    private static void Identity(Guid value, string name)
    {
        if (value == Guid.Empty)
            throw new ArgumentException("Identity is required.", name);
    }

    private static void Positive(decimal value, string name)
    {
        if (value <= 0 || value > 99_999_999_999_999.9999m || decimal.Round(value, 4) != value)
            throw new ArgumentOutOfRangeException(name);
    }

    private static void NonNegative(decimal value, string name)
    {
        if (value < 0 || decimal.Round(value, 6) != value)
            throw new ArgumentOutOfRangeException(name);
    }

    private static void Percentage(decimal value, string name)
    {
        if (value < 0 || value > 100 || decimal.Round(value, 4) != value)
            throw new ArgumentOutOfRangeException(name);
    }

    private static string Hash(string value, string name)
    {
        var normalized = value?.Trim().ToLowerInvariant();
        if (normalized is null || normalized.Length != 64 || !normalized.All(Uri.IsHexDigit))
            throw new ArgumentException("A SHA-256 hash is required.", name);
        return normalized;
    }

    private static string Currency(string value)
    {
        var normalized = value?.Trim().ToUpperInvariant();
        if (normalized is null || normalized.Length != 3 || !normalized.All(char.IsAsciiLetter))
            throw new ArgumentException("A three-letter currency code is required.", nameof(value));
        return normalized;
    }

    private static string Text(string value, int maximumLength, string name)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized) ||
            normalized.Length > maximumLength ||
            normalized.Any(char.IsControl))
        {
            throw new ArgumentException($"A value of at most {maximumLength} characters is required.", name);
        }
        return normalized;
    }
}

public sealed record SpacePlanningSimulationLocationResultData(
    Guid LocationLogicalId,
    int TaskCount,
    int CompletedTaskCount,
    decimal TotalQuantity,
    int DistanceEligibleTaskCount,
    decimal TotalDistanceMeters,
    decimal QuantityCapacity,
    int ConcurrentTaskCapacity,
    int PeakConcurrentTasks,
    decimal PeakConcurrentQuantity,
    decimal CapacityUtilizationPercent,
    long CongestionSeconds,
    long CongestionTaskSeconds);

public sealed class SpacePlanningSimulationLocationResult : SpaceTenantEntity
{
    private SpacePlanningSimulationLocationResult()
    {
    }

    public Guid RunId { get; private set; }
    public Guid ScenarioVersionId { get; private set; }
    public Guid LocationLogicalId { get; private set; }
    public int TaskCount { get; private set; }
    public int CompletedTaskCount { get; private set; }
    public decimal TotalQuantity { get; private set; }
    public int DistanceEligibleTaskCount { get; private set; }
    public decimal TotalDistanceMeters { get; private set; }
    public decimal QuantityCapacity { get; private set; }
    public int ConcurrentTaskCapacity { get; private set; }
    public int PeakConcurrentTasks { get; private set; }
    public decimal PeakConcurrentQuantity { get; private set; }
    public decimal CapacityUtilizationPercent { get; private set; }
    public long CongestionSeconds { get; private set; }
    public long CongestionTaskSeconds { get; private set; }

    public static SpacePlanningSimulationLocationResult Create(
        SpacePlanningSimulationRun run,
        SpacePlanningSimulationLocationResultData value)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(value);
        if (value.LocationLogicalId == Guid.Empty ||
            value.TaskCount < 1 ||
            value.CompletedTaskCount < 0 ||
            value.CompletedTaskCount > value.TaskCount ||
            value.DistanceEligibleTaskCount < 0 ||
            value.DistanceEligibleTaskCount > value.TaskCount ||
            value.ConcurrentTaskCapacity < 1 ||
            value.PeakConcurrentTasks < 0 ||
            value.CongestionSeconds < 0 ||
            value.CongestionTaskSeconds < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(value));
        }
        if (value.TotalQuantity <= 0 ||
            value.QuantityCapacity <= 0 ||
            value.PeakConcurrentQuantity < 0 ||
            value.TotalDistanceMeters < 0 ||
            value.CapacityUtilizationPercent < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(value));
        }

        var result = new SpacePlanningSimulationLocationResult
        {
            RunId = run.Id,
            ScenarioVersionId = run.ScenarioVersionId,
            LocationLogicalId = value.LocationLogicalId,
            TaskCount = value.TaskCount,
            CompletedTaskCount = value.CompletedTaskCount,
            TotalQuantity = value.TotalQuantity,
            DistanceEligibleTaskCount = value.DistanceEligibleTaskCount,
            TotalDistanceMeters = value.TotalDistanceMeters,
            QuantityCapacity = value.QuantityCapacity,
            ConcurrentTaskCapacity = value.ConcurrentTaskCapacity,
            PeakConcurrentTasks = value.PeakConcurrentTasks,
            PeakConcurrentQuantity = value.PeakConcurrentQuantity,
            CapacityUtilizationPercent = value.CapacityUtilizationPercent,
            CongestionSeconds = value.CongestionSeconds,
            CongestionTaskSeconds = value.CongestionTaskSeconds,
        };
        result.SetTenant(run.TenantId);
        return result;
    }
}
