using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using CP6.Space.Contracts;
using CP6.Space.Domain;

namespace CP6.Space.Application;

public sealed class SpaceAiRetentionOptions
{
    public static TimeSpan MinimumRunPayloadRetention { get; } =
        TimeSpan.FromDays(90);

    public TimeSpan RunPayloadRetention { get; init; } =
        MinimumRunPayloadRetention;

    public TimeSpan UsageRetention { get; init; } =
        SpaceAiUsageRecord.MinimumRetention;

    public int BatchSize { get; init; } = 250;

    public void Validate()
    {
        if (RunPayloadRetention < MinimumRunPayloadRetention)
        {
            throw new InvalidOperationException(
                "AI generation payload retention cannot be shorter than 90 days.");
        }
        if (UsageRetention < SpaceAiUsageRecord.MinimumRetention)
        {
            throw new InvalidOperationException(
                "AI usage retention cannot be shorter than 365 days.");
        }
        if (BatchSize is < 1 or > 1000)
            throw new ArgumentOutOfRangeException(nameof(BatchSize));
    }
}

public sealed record SpaceAiRetentionJobPayload(
    [property: JsonPropertyName("schemaVersion")] string SchemaVersion,
    [property: JsonPropertyName("windowEndUtc")] DateTime WindowEndUtc,
    [property: JsonPropertyName("runPayloadCutoffUtc")]
        DateTime RunPayloadCutoffUtc,
    [property: JsonPropertyName("usageArchiveCutoffUtc")]
        DateTime UsageArchiveCutoffUtc,
    [property: JsonPropertyName("batchSize")] int BatchSize)
{
    public const string CurrentSchemaVersion = "1.0";

    public static SpaceAiRetentionJobPayload Create(
        SpaceAiRetentionOptions options,
        DateTime nowUtc)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        RequireUtc(nowUtc, nameof(nowUtc));
        var windowEnd = new DateTime(
            nowUtc.Year,
            nowUtc.Month,
            nowUtc.Day,
            0,
            0,
            0,
            DateTimeKind.Utc);
        return new SpaceAiRetentionJobPayload(
            CurrentSchemaVersion,
            windowEnd,
            windowEnd.Subtract(options.RunPayloadRetention),
            windowEnd.Subtract(options.UsageRetention),
            options.BatchSize).Validate(nowUtc);
    }

    public SpaceAiRetentionJobPayload Validate(DateTime notAfterUtc)
    {
        RequireUtc(notAfterUtc, nameof(notAfterUtc));
        RequireUtc(WindowEndUtc, nameof(WindowEndUtc));
        RequireUtc(
            RunPayloadCutoffUtc,
            nameof(RunPayloadCutoffUtc));
        RequireUtc(
            UsageArchiveCutoffUtc,
            nameof(UsageArchiveCutoffUtc));
        if (SchemaVersion != CurrentSchemaVersion ||
            WindowEndUtc > notAfterUtc ||
            WindowEndUtc.TimeOfDay != TimeSpan.Zero ||
            RunPayloadCutoffUtc >
                WindowEndUtc.Subtract(
                    SpaceAiRetentionOptions.MinimumRunPayloadRetention) ||
            UsageArchiveCutoffUtc >
                WindowEndUtc.Subtract(SpaceAiUsageRecord.MinimumRetention) ||
            BatchSize is < 1 or > 1000)
        {
            throw new SpaceAiRetentionPayloadException(
                "The AI retention Job payload is invalid.");
        }
        return this;
    }

    private static void RequireUtc(DateTime value, string parameterName)
    {
        if (value.Kind != DateTimeKind.Utc)
            throw new ArgumentException("Time must be UTC.", parameterName);
    }
}

public sealed record SpaceAiRetentionCleanupResult(
    int CandidateRuns,
    int RunPayloadsPurged,
    int ProposalPayloadsPurged,
    int DiagnosticPayloadsPurged,
    int StagingRowsDeleted,
    int UsageRowsArchived);

public interface ISpaceAiRetentionStore
{
    Task<SpaceAiRetentionCleanupResult> PurgeAsync(
        Guid tenantId,
        SpaceAiRetentionJobPayload payload,
        CancellationToken cancellationToken = default);
}

public interface ISpaceAiRetentionAuthorization
{
    bool IsRetentionServicePrincipal { get; }
}

public sealed class ClosedSpaceAiRetentionAuthorization :
    ISpaceAiRetentionAuthorization
{
    public bool IsRetentionServicePrincipal => false;
}

public sealed class SpaceAiRetentionCoordinator(
    ISpaceExecutionContext execution,
    ISpaceClock clock,
    ISpaceAiRetentionAuthorization authorization,
    SpaceJobCoordinator jobs,
    SpaceAiRetentionOptions options)
{
    public async Task<SpaceJobEnqueueResult> QueueAsync(
        CancellationToken cancellationToken = default)
    {
        if (execution.IsExternal)
        {
            throw new SpaceProblemException(
                SpaceErrorCodes.ExternalSubjectDenied,
                403,
                "External principals cannot queue AI retention cleanup.",
                recoveryAction: "use-retention-service-principal");
        }
        if (execution.TenantId == Guid.Empty || execution.ActorId == Guid.Empty)
        {
            throw new SpaceTenantScopeException(
                "A verified Space tenant and actor are required.");
        }
        if (!authorization.IsRetentionServicePrincipal)
        {
            throw new UnauthorizedAccessException(
                "AI retention cleanup requires its restricted service principal.");
        }

        var now = clock.UtcNow;
        var payload = SpaceAiRetentionJobPayload.Create(options, now);
        var json = SpaceAiRetentionPayloadCodec.Serialize(payload);
        return await jobs.EnqueueAsync(
            new SpaceJobEnqueueRequest(
                SpaceJobType.AiRetentionCleanup,
                SpaceJobSubjectType.Tenant,
                execution.TenantId,
                SpaceAiRetentionPayloadCodec.Hash(json),
                SpaceAiRetentionJobProcessor.ProcessorVersionValue,
                VariantKey: payload.WindowEndUtc.ToString(
                    "yyyyMMdd",
                    CultureInfo.InvariantCulture),
                MaxAttempts: 5,
                PayloadJson: json),
            cancellationToken);
    }
}

public interface ISpaceAiRetentionJobStepExecutor
{
    Task<SpaceJobStepOutput> ExecuteAsync(
        SpaceJobStepExecution execution,
        CancellationToken cancellationToken = default);
}

public static class SpaceAiRetentionJobSteps
{
    public const string PurgeExpiredPayloads = nameof(PurgeExpiredPayloads);
    public static IReadOnlyList<string> All { get; } = [PurgeExpiredPayloads];
}

public sealed class SpaceAiRetentionJobProcessor(
    ISpaceAiRetentionJobStepExecutor executor) : ISpaceJobProcessor
{
    public const string ProcessorVersionValue = "space-ai-retention-v1";

    public SpaceJobType JobType => SpaceJobType.AiRetentionCleanup;
    public SpaceJobSubjectType SubjectType => SpaceJobSubjectType.Tenant;
    public string ProcessorVersion => ProcessorVersionValue;
    public IReadOnlyList<string> StepCodes => SpaceAiRetentionJobSteps.All;

    public Task<SpaceJobStepOutput> ExecuteStepAsync(
        SpaceJobStepExecution execution,
        CancellationToken cancellationToken = default) =>
        execution.StepCode == SpaceAiRetentionJobSteps.PurgeExpiredPayloads
            ? executor.ExecuteAsync(execution, cancellationToken)
            : throw new SpaceJobProcessingException(
                SpaceJobFailureKind.Input,
                SpaceErrorCodes.AiRetentionInvalid,
                "The AI retention Job step is not supported.");
}

public sealed class SpaceAiRetentionPayloadException : InvalidOperationException
{
    public SpaceAiRetentionPayloadException(string message) : base(message)
    {
    }
}

public sealed class SpaceAiRetentionBusyException : InvalidOperationException
{
    public SpaceAiRetentionBusyException(string message) : base(message)
    {
    }
}

public static class SpaceAiRetentionPayloadCodec
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    public static string Serialize<T>(T value) =>
        JsonSerializer.Serialize(value, Options);

    public static SpaceAiRetentionJobPayload ParsePayload(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<SpaceAiRetentionJobPayload>(
                       json,
                       Options)
                   ?? throw new JsonException("Payload is required.");
        }
        catch (JsonException exception)
        {
            throw new SpaceAiRetentionPayloadException(
                $"The AI retention Job payload is invalid: {exception.Message}");
        }
    }

    public static string Hash(string value) =>
        Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(value)))
            .ToLowerInvariant();
}
