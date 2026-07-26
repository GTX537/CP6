using System.Security.Cryptography;
using System.Text;
using CP6.Space.Domain;

namespace CP6.Space.Application;

public sealed record SpaceJobEnqueueRequest(
    SpaceJobType JobType,
    SpaceJobSubjectType SubjectType,
    Guid SubjectId,
    string InputHash,
    string ProcessorVersion,
    string? VariantKey = null,
    short Priority = 0,
    int MaxAttempts = 5,
    string PayloadJson = "{}");

public sealed record SpaceJobEnqueueResult(
    SpaceJob Job,
    bool Reused);

public sealed class SpaceJobCoordinator
{
    private readonly ISpaceExecutionContext _execution;
    private readonly ISpaceClock _clock;
    private readonly ISpaceJobQueue _queue;

    public SpaceJobCoordinator(
        ISpaceExecutionContext execution,
        ISpaceClock clock,
        ISpaceJobQueue queue)
    {
        _execution = execution;
        _clock = clock;
        _queue = queue;
    }

    public async Task<SpaceJobEnqueueResult> EnqueueAsync(
        SpaceJobEnqueueRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        EnsureExecutionContext();
        var businessKey = SpaceJobBusinessKey.Create(request);
        var existing = await _queue.FindActiveAsync(
            _execution.TenantId,
            request.JobType,
            businessKey,
            cancellationToken);
        if (existing is not null)
            return new SpaceJobEnqueueResult(existing, Reused: true);

        var job = SpaceJob.CreateQueued(
            _execution.TenantId,
            request.JobType,
            request.SubjectType,
            request.SubjectId,
            businessKey,
            request.InputHash,
            request.Priority,
            request.MaxAttempts,
            _execution.ActorId,
            _clock.UtcNow,
            RequireCorrelationId(_execution),
            request.PayloadJson);
        return await _queue.AddOrGetActiveAsync(job, cancellationToken);
    }

    public async Task RequestCancellationAsync(
        Guid jobId,
        CancellationToken cancellationToken = default)
    {
        EnsureExecutionContext();
        var job = await RequireJobAsync(jobId, cancellationToken);
        job.RequestCancellation(_execution.ActorId, _clock.UtcNow);
        await _queue.SaveChangesAsync(cancellationToken);
    }

    public async Task<SpaceJob> RetryAsync(
        Guid jobId,
        SpaceJobEnqueueRequest retryRequest,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(retryRequest);
        EnsureExecutionContext();
        var original = await RequireJobAsync(jobId, cancellationToken);
        if (retryRequest.JobType != original.JobType ||
            retryRequest.SubjectType != original.SubjectType ||
            retryRequest.SubjectId != original.SubjectId)
        {
            throw new SpaceJobNotRetryableException(
                "An explicit retry cannot change the Job type or subject.");
        }

        var businessKey = SpaceJobBusinessKey.Create(retryRequest);
        var active = await _queue.FindActiveAsync(
            _execution.TenantId,
            original.JobType,
            businessKey,
            cancellationToken);
        if (active is not null)
            return active;

        var retry = original.CreateExplicitRetry(
            businessKey,
            retryRequest.InputHash,
            _execution.ActorId,
            _clock.UtcNow,
            RequireCorrelationId(_execution),
            retryRequest.PayloadJson);
        return (await _queue.AddOrGetActiveAsync(retry, cancellationToken)).Job;
    }

    private async Task<SpaceJob> RequireJobAsync(
        Guid jobId,
        CancellationToken cancellationToken)
    {
        if (jobId == Guid.Empty)
            throw new ArgumentException("Job ID is required.", nameof(jobId));
        return await _queue.FindByIdAsync(
                   _execution.TenantId,
                   jobId,
                   cancellationToken)
               ?? throw new KeyNotFoundException("The Space Job was not found.");
    }

    private void EnsureExecutionContext()
    {
        if (_execution.TenantId == Guid.Empty || _execution.ActorId == Guid.Empty)
            throw new SpaceTenantScopeException(
                "A verified Space tenant and actor are required.");
    }

    private static Guid RequireCorrelationId(ISpaceExecutionContext execution)
    {
        if (execution is ISpaceCorrelationContext correlation &&
            correlation.CorrelationId != Guid.Empty)
        {
            return correlation.CorrelationId;
        }

        return Guid.NewGuid();
    }
}

public static class SpaceJobBusinessKey
{
    public static string Create(SpaceJobEnqueueRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.SubjectId == Guid.Empty)
            throw new ArgumentException("Job subject is required.", nameof(request));
        var inputHash = NormalizeHash(request.InputHash);
        var processorVersion = NormalizePart(
            request.ProcessorVersion,
            100,
            nameof(request.ProcessorVersion));
        var variant = string.IsNullOrWhiteSpace(request.VariantKey)
            ? string.Empty
            : NormalizePart(request.VariantKey, 500, nameof(request.VariantKey));
        var canonical = string.Join(
            "\n",
            ((short)request.JobType).ToString(System.Globalization.CultureInfo.InvariantCulture),
            ((short)request.SubjectType).ToString(System.Globalization.CultureInfo.InvariantCulture),
            request.SubjectId.ToString("N"),
            inputHash,
            processorVersion,
            variant);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)))
            .ToLowerInvariant();
    }

    private static string NormalizeHash(string value)
    {
        if (value is null || value.Length != 64 || !value.All(Uri.IsHexDigit))
            throw new ArgumentException("A SHA-256 input hash is required.", nameof(value));
        return value.ToLowerInvariant();
    }

    private static string NormalizePart(
        string value,
        int maxLength,
        string parameterName)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized) || normalized.Length > maxLength)
            throw new ArgumentException(
                $"A value between 1 and {maxLength} characters is required.",
                parameterName);
        return normalized;
    }
}
