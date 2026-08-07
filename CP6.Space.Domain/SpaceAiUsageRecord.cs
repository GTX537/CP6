namespace CP6.Space.Domain;

public sealed class SpaceAiUsageRecord : SpaceTenantEntity
{
    private SpaceAiUsageRecord()
    {
    }

    public Guid RunId { get; private set; }
    public string ProviderCode { get; private set; } = string.Empty;
    public string ProviderModel { get; private set; } = string.Empty;
    public string ProviderRequestIdHash { get; private set; } = string.Empty;
    public long InputUnits { get; private set; }
    public long OutputUnits { get; private set; }
    public long EstimatedCostMinor { get; private set; }
    public long? ActualCostMinor { get; private set; }
    public string? Currency { get; private set; }
    public long LatencyMs { get; private set; }
    public SpaceAiUsageOutcome Outcome { get; private set; }
    public DateTime RecordedAtUtc { get; private set; }
    public DateTime? ArchivedAtUtc { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    public static TimeSpan MinimumRetention { get; } = TimeSpan.FromDays(365);

    public static SpaceAiUsageRecord Create(
        Guid tenantId,
        Guid runId,
        string providerCode,
        string providerModel,
        string providerRequestIdHash,
        long inputUnits,
        long outputUnits,
        long estimatedCostMinor,
        long? actualCostMinor,
        string? currency,
        long latencyMs,
        SpaceAiUsageOutcome outcome,
        DateTime recordedAtUtc)
    {
        if (runId == Guid.Empty)
            throw new ArgumentException("Run is required.", nameof(runId));
        if (inputUnits < 0)
            throw new ArgumentOutOfRangeException(nameof(inputUnits));
        if (outputUnits < 0)
            throw new ArgumentOutOfRangeException(nameof(outputUnits));
        if (estimatedCostMinor < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(estimatedCostMinor));
        }
        if (actualCostMinor < 0)
            throw new ArgumentOutOfRangeException(nameof(actualCostMinor));
        if (latencyMs < 0)
            throw new ArgumentOutOfRangeException(nameof(latencyMs));
        if (!Enum.IsDefined(outcome))
            throw new ArgumentOutOfRangeException(nameof(outcome));
        SpaceGenerationRun.RequireUtc(
            recordedAtUtc,
            nameof(recordedAtUtc));

        var normalizedCurrency = NormalizeCurrency(currency);
        if ((estimatedCostMinor > 0 || actualCostMinor > 0) &&
            normalizedCurrency is null)
        {
            throw new ArgumentException(
                "Currency is required when cost is recorded.",
                nameof(currency));
        }

        var usage = new SpaceAiUsageRecord
        {
            RunId = runId,
            ProviderCode = SpaceGenerationRun.RequireText(
                providerCode,
                64,
                nameof(providerCode)),
            ProviderModel = SpaceGenerationRun.RequireText(
                providerModel,
                128,
                nameof(providerModel)),
            ProviderRequestIdHash = SpaceGenerationRun.RequireHash(
                providerRequestIdHash,
                nameof(providerRequestIdHash)),
            InputUnits = inputUnits,
            OutputUnits = outputUnits,
            EstimatedCostMinor = estimatedCostMinor,
            ActualCostMinor = actualCostMinor,
            Currency = normalizedCurrency,
            LatencyMs = latencyMs,
            Outcome = outcome,
            RecordedAtUtc = recordedAtUtc,
        };
        usage.SetTenant(tenantId);
        return usage;
    }

    public bool ArchiveForRetention(DateTime archivedAtUtc)
    {
        SpaceGenerationRun.RequireUtc(
            archivedAtUtc,
            nameof(archivedAtUtc));
        if (ArchivedAtUtc.HasValue)
            return false;
        if (archivedAtUtc < RecordedAtUtc.Add(MinimumRetention))
        {
            throw new SpaceAiCapacityStateException(
                "AI usage cannot be archived before its minimum retention expires.");
        }
        ArchivedAtUtc = archivedAtUtc;
        return true;
    }

    private static string? NormalizeCurrency(string? currency)
    {
        if (currency is null)
            return null;
        var normalized = currency.Trim().ToUpperInvariant();
        if (normalized.Length != 3 ||
            !normalized.All(character =>
                character is >= 'A' and <= 'Z'))
        {
            throw new ArgumentException(
                "Currency must be an ISO 4217 alpha code.",
                nameof(currency));
        }
        return normalized;
    }
}
