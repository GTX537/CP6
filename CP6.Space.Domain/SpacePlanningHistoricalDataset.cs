namespace CP6.Space.Domain;

public sealed record SpacePlanningHistoricalDatasetData(
    Guid SiteId,
    Guid ModelId,
    Guid BranchId,
    Guid ScenarioVersionId,
    string Name,
    DateTimeOffset HistoricalFromUtc,
    DateTimeOffset HistoricalToUtc,
    DateTimeOffset ReplayStartUtc,
    decimal ReplaySpeedFactor,
    int TaskCount,
    string SourceDatasetHash,
    string RequestHash,
    string DefinitionVersion,
    string DeidentificationVersion);

public sealed class SpacePlanningHistoricalDataset : SpaceTenantEntity
{
    private SpacePlanningHistoricalDataset()
    {
    }

    public Guid SiteId { get; private set; }
    public Guid ModelId { get; private set; }
    public Guid BranchId { get; private set; }
    public Guid ScenarioVersionId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public DateTimeOffset HistoricalFromUtc { get; private set; }
    public DateTimeOffset HistoricalToUtc { get; private set; }
    public DateTimeOffset ReplayStartUtc { get; private set; }
    public decimal ReplaySpeedFactor { get; private set; }
    public int TaskCount { get; private set; }
    public string SourceDatasetHash { get; private set; } = string.Empty;
    public string RequestHash { get; private set; } = string.Empty;
    public string DefinitionVersion { get; private set; } = string.Empty;
    public string DeidentificationVersion { get; private set; } = string.Empty;

    public static SpacePlanningHistoricalDataset Create(
        Guid tenantId,
        Guid datasetId,
        SpacePlanningHistoricalDatasetData value)
    {
        ArgumentNullException.ThrowIfNull(value);
        Identity(datasetId, nameof(datasetId));
        Identity(value.SiteId, nameof(value.SiteId));
        Identity(value.ModelId, nameof(value.ModelId));
        Identity(value.BranchId, nameof(value.BranchId));
        Identity(value.ScenarioVersionId, nameof(value.ScenarioVersionId));
        Utc(value.HistoricalFromUtc, nameof(value.HistoricalFromUtc));
        Utc(value.HistoricalToUtc, nameof(value.HistoricalToUtc));
        Utc(value.ReplayStartUtc, nameof(value.ReplayStartUtc));
        if (value.HistoricalFromUtc >= value.HistoricalToUtc)
        {
            throw new ArgumentException(
                "The historical window must be positive.",
                nameof(value));
        }
        if (value.ReplaySpeedFactor is <= 0 or > 1000)
        {
            throw new ArgumentOutOfRangeException(
                nameof(value.ReplaySpeedFactor));
        }
        if (decimal.Round(value.ReplaySpeedFactor, 4) !=
            value.ReplaySpeedFactor)
        {
            throw new ArgumentOutOfRangeException(
                nameof(value.ReplaySpeedFactor),
                "Replay speed supports at most four decimal places.");
        }
        if (value.TaskCount is < 1 or > 10_000)
            throw new ArgumentOutOfRangeException(nameof(value.TaskCount));

        var result = new SpacePlanningHistoricalDataset
        {
            SiteId = value.SiteId,
            ModelId = value.ModelId,
            BranchId = value.BranchId,
            ScenarioVersionId = value.ScenarioVersionId,
            Name = Text(value.Name, 200, nameof(value.Name)),
            HistoricalFromUtc = value.HistoricalFromUtc,
            HistoricalToUtc = value.HistoricalToUtc,
            ReplayStartUtc = value.ReplayStartUtc,
            ReplaySpeedFactor = value.ReplaySpeedFactor,
            TaskCount = value.TaskCount,
            SourceDatasetHash = Hash(
                value.SourceDatasetHash,
                nameof(value.SourceDatasetHash)),
            RequestHash = Hash(value.RequestHash, nameof(value.RequestHash)),
            DefinitionVersion = Text(
                value.DefinitionVersion,
                100,
                nameof(value.DefinitionVersion)),
            DeidentificationVersion = Text(
                value.DeidentificationVersion,
                100,
                nameof(value.DeidentificationVersion)),
        };
        result.SetTenant(tenantId);
        result.SetId(datasetId);
        return result;
    }

    private static void Identity(Guid value, string parameterName)
    {
        if (value == Guid.Empty)
            throw new ArgumentException("Identity is required.", parameterName);
    }

    private static void Utc(
        DateTimeOffset value,
        string parameterName)
    {
        if (value.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException(
                "A UTC timestamp is required.",
                parameterName);
        }
    }

    private static string Text(
        string value,
        int maximumLength,
        string parameterName)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized) ||
            normalized.Length > maximumLength ||
            normalized.Any(char.IsControl))
        {
            throw new ArgumentException(
                $"A value of at most {maximumLength} characters is required.",
                parameterName);
        }
        return normalized;
    }

    private static string Hash(string value, string parameterName)
    {
        var normalized = value?.Trim().ToLowerInvariant();
        if (normalized is null ||
            normalized.Length != 64 ||
            !normalized.All(Uri.IsHexDigit))
        {
            throw new ArgumentException(
                "A SHA-256 hash is required.",
                parameterName);
        }
        return normalized;
    }
}

public sealed record SpacePlanningHistoricalTaskData(
    int SequenceNo,
    string TaskToken,
    string? WorkerToken,
    SpacePlanningTaskType TaskType,
    SpacePlanningTaskOutcome Outcome,
    DateTimeOffset OriginalCreatedAtUtc,
    DateTimeOffset OriginalCompletedAtUtc,
    DateTimeOffset ReplayCreatedAtUtc,
    DateTimeOffset ReplayCompletedAtUtc,
    Guid? FromLocationLogicalId,
    Guid ToLocationLogicalId,
    decimal Quantity);

public sealed class SpacePlanningHistoricalTask : SpaceTenantEntity
{
    private SpacePlanningHistoricalTask()
    {
    }

    public Guid DatasetId { get; private set; }
    public int SequenceNo { get; private set; }
    public string TaskToken { get; private set; } = string.Empty;
    public string? WorkerToken { get; private set; }
    public SpacePlanningTaskType TaskType { get; private set; }
    public SpacePlanningTaskOutcome Outcome { get; private set; }
    public DateTimeOffset OriginalCreatedAtUtc { get; private set; }
    public DateTimeOffset OriginalCompletedAtUtc { get; private set; }
    public DateTimeOffset ReplayCreatedAtUtc { get; private set; }
    public DateTimeOffset ReplayCompletedAtUtc { get; private set; }
    public Guid? FromLocationLogicalId { get; private set; }
    public Guid ToLocationLogicalId { get; private set; }
    public decimal Quantity { get; private set; }

    public static SpacePlanningHistoricalTask Create(
        SpacePlanningHistoricalDataset dataset,
        SpacePlanningHistoricalTaskData value)
    {
        ArgumentNullException.ThrowIfNull(dataset);
        ArgumentNullException.ThrowIfNull(value);
        if (value.SequenceNo < 1)
            throw new ArgumentOutOfRangeException(nameof(value.SequenceNo));
        if (value.ToLocationLogicalId == Guid.Empty)
        {
            throw new ArgumentException(
                "A destination location is required.",
                nameof(value.ToLocationLogicalId));
        }
        if (value.FromLocationLogicalId == Guid.Empty)
        {
            throw new ArgumentException(
                "An optional source location cannot be empty.",
                nameof(value.FromLocationLogicalId));
        }
        if (value.Quantity <= 0)
            throw new ArgumentOutOfRangeException(nameof(value.Quantity));
        if (value.Quantity > 99_999_999_999_999.9999m ||
            decimal.Round(value.Quantity, 4) != value.Quantity)
        {
            throw new ArgumentOutOfRangeException(
                nameof(value.Quantity),
                "Quantity must fit decimal(18,4).");
        }
        if (!Enum.IsDefined(value.TaskType))
            throw new ArgumentOutOfRangeException(nameof(value.TaskType));
        if (!Enum.IsDefined(value.Outcome))
            throw new ArgumentOutOfRangeException(nameof(value.Outcome));
        ValidateWindow(
            value.OriginalCreatedAtUtc,
            value.OriginalCompletedAtUtc,
            dataset.HistoricalFromUtc,
            dataset.HistoricalToUtc,
            "original");
        ValidateWindow(
            value.ReplayCreatedAtUtc,
            value.ReplayCompletedAtUtc,
            dataset.ReplayStartUtc,
            SpaceReplayClock.Create(dataset).ReplayEndUtc,
            "replay");

        var result = new SpacePlanningHistoricalTask
        {
            DatasetId = dataset.Id,
            SequenceNo = value.SequenceNo,
            TaskToken = Token(value.TaskToken, nameof(value.TaskToken)),
            WorkerToken = string.IsNullOrWhiteSpace(value.WorkerToken)
                ? null
                : Token(value.WorkerToken, nameof(value.WorkerToken)),
            TaskType = value.TaskType,
            Outcome = value.Outcome,
            OriginalCreatedAtUtc = value.OriginalCreatedAtUtc,
            OriginalCompletedAtUtc = value.OriginalCompletedAtUtc,
            ReplayCreatedAtUtc = value.ReplayCreatedAtUtc,
            ReplayCompletedAtUtc = value.ReplayCompletedAtUtc,
            FromLocationLogicalId = value.FromLocationLogicalId,
            ToLocationLogicalId = value.ToLocationLogicalId,
            Quantity = value.Quantity,
        };
        result.SetTenant(dataset.TenantId);
        return result;
    }

    private static void ValidateWindow(
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        DateTimeOffset windowFromUtc,
        DateTimeOffset windowToUtc,
        string name)
    {
        if (fromUtc.Offset != TimeSpan.Zero ||
            toUtc.Offset != TimeSpan.Zero ||
            fromUtc < windowFromUtc ||
            toUtc > windowToUtc ||
            fromUtc > toUtc)
        {
            throw new ArgumentException(
                $"The {name} task window is invalid.");
        }
    }

    private static string Token(string value, string parameterName)
    {
        var normalized = value?.Trim().ToLowerInvariant();
        if (normalized is null ||
            normalized.Length != 64 ||
            !normalized.All(Uri.IsHexDigit))
        {
            throw new ArgumentException(
                "A de-identified SHA-256 token is required.",
                parameterName);
        }
        return normalized;
    }
}

public sealed record SpaceReplayClock(
    DateTimeOffset HistoricalFromUtc,
    DateTimeOffset HistoricalToUtc,
    DateTimeOffset ReplayStartUtc,
    decimal SpeedFactor,
    DateTimeOffset ReplayEndUtc)
{
    public static SpaceReplayClock Create(
        SpacePlanningHistoricalDataset dataset)
    {
        ArgumentNullException.ThrowIfNull(dataset);
        return Create(
            dataset.HistoricalFromUtc,
            dataset.HistoricalToUtc,
            dataset.ReplayStartUtc,
            dataset.ReplaySpeedFactor);
    }

    public static SpaceReplayClock Create(
        DateTimeOffset historicalFromUtc,
        DateTimeOffset historicalToUtc,
        DateTimeOffset replayStartUtc,
        decimal speedFactor)
    {
        if (historicalFromUtc.Offset != TimeSpan.Zero ||
            historicalToUtc.Offset != TimeSpan.Zero ||
            replayStartUtc.Offset != TimeSpan.Zero ||
            historicalFromUtc >= historicalToUtc ||
            speedFactor is <= 0 or > 1000 ||
            decimal.Round(speedFactor, 4) != speedFactor)
        {
            throw new ArgumentException("The replay clock definition is invalid.");
        }
        var replayDuration = Scale(
            historicalToUtc - historicalFromUtc,
            speedFactor);
        if (replayDuration <= TimeSpan.Zero)
            throw new ArgumentException("The replay duration must be positive.");
        return new SpaceReplayClock(
            historicalFromUtc,
            historicalToUtc,
            replayStartUtc,
            speedFactor,
            replayStartUtc.Add(replayDuration));
    }

    public DateTimeOffset Map(DateTimeOffset historicalUtc)
    {
        if (historicalUtc.Offset != TimeSpan.Zero ||
            historicalUtc < HistoricalFromUtc ||
            historicalUtc > HistoricalToUtc)
        {
            throw new ArgumentOutOfRangeException(nameof(historicalUtc));
        }
        return ReplayStartUtc.Add(
            Scale(historicalUtc - HistoricalFromUtc, SpeedFactor));
    }

    private static TimeSpan Scale(TimeSpan value, decimal speedFactor)
    {
        var ticks = decimal.ToInt64(decimal.Round(
            value.Ticks / speedFactor,
            0,
            MidpointRounding.ToEven));
        return TimeSpan.FromTicks(ticks);
    }
}
