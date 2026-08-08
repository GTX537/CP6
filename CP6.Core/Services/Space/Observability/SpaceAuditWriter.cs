using System.Text.Json;
using CP6.Entity.DomainModels.Space;
using Microsoft.Extensions.Logging;

namespace CP6.Core.Services.Space.Observability;

public sealed class SpaceAuditWriter : ISpaceAuditWriter
{
    private const int EvidenceLimit = 8192;
    private const string TruncatedEvidence = """{"status":"EvidenceTruncated"}""";

    private readonly ISpaceAuditDbContextFactory _factory;
    private readonly ISpaceExecutionContextAccessor _execution;
    private readonly ILogger<SpaceAuditWriter> _logger;

    public SpaceAuditWriter(
        ISpaceAuditDbContextFactory factory,
        ISpaceExecutionContextAccessor execution,
        ILogger<SpaceAuditWriter> logger)
    {
        _factory = factory;
        _execution = execution;
        _logger = logger;
    }

    public async Task<bool> TryAppendAsync(
        SpaceAuditEventInput input,
        CancellationToken ct = default)
    {
        ISpaceExecutionContext? executionContext = null;
        try
        {
            ArgumentNullException.ThrowIfNull(input);
            executionContext = input.Outcome == SpaceAuditOutcome.Started
                ? _execution.RequireCurrent()
                : _execution.RequireOutcomeCurrent();
            ValidateContext(executionContext);
            ValidateInput(input);

            var evidence = input.Evidence is null
                ? null
                : JsonSerializer.Serialize(input.Evidence);
            if (evidence?.Length > EvidenceLimit)
                evidence = TruncatedEvidence;

            await using var db = _factory.CreateDbContext();
            if (db.CurrentTenantId != executionContext.TenantId)
                throw new InvalidOperationException(
                    "SPACE_AUDIT_TENANT_CONTEXT_MISMATCH");

            db.SpaceAuditEvents.Add(Materialize(
                input,
                executionContext,
                DateTime.UtcNow,
                evidence));
            await db.SaveChangesAsync(ct);
            return true;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            var safe = SpaceErrorSanitizer.Classify(
                ex,
                "SPACE_AUDIT_WRITE_FAILED");
            _logger.LogError(
                "Space audit append failed {ReasonCode} {ErrorType} {Fingerprint} {CorrelationId}",
                safe.ReasonCode,
                safe.ExceptionType,
                safe.Fingerprint,
                executionContext?.CorrelationId ?? Guid.Empty);
            return false;
        }
    }

    internal static Space_AuditEvent Materialize(
        SpaceAuditEventInput input,
        ISpaceExecutionContext executionContext,
        DateTime now,
        string? serializedEvidence = null,
        Guid? auditId = null)
    {
        ValidateContext(executionContext);
        ValidateInput(input);
        var evidence = serializedEvidence;
        if (evidence is null && input.Evidence is not null)
            evidence = JsonSerializer.Serialize(input.Evidence);
        if (evidence?.Length > EvidenceLimit)
            evidence = TruncatedEvidence;

        return new Space_AuditEvent
        {
            Id = auditId ?? Guid.NewGuid(),
            TenantId = executionContext.TenantId,
            OccurredAtUtc = now,
            ActorType = executionContext.ActorType,
            ActorId = Limit(executionContext.ActorId, 100)!,
            ActorName = Limit(executionContext.ActorName, 100),
            OrganizationContextId = Limit(
                executionContext.OrganizationContextId,
                100),
            Action = Limit(input.Action, 100)!,
            ResourceType = Limit(input.ResourceType, 64)!,
            ResourceId = Limit(input.ResourceId, 128),
            SiteId = input.SiteId,
            VersionId = input.VersionId,
            FloorId = input.FloorId,
            Outcome = input.Outcome,
            ReasonCode = input.ReasonCode,
            AuthorizationEvidenceJson = evidence,
            BeforeHash = input.BeforeHash,
            AfterHash = input.AfterHash,
            CorrelationId = executionContext.CorrelationId,
            TraceId = Limit(executionContext.TraceId, 64)!,
            JobId = executionContext.JobId,
            RunId = executionContext.RunId,
            PublishAttemptId = executionContext.PublishAttemptId,
            AttemptNo = input.AttemptNo,
            ClientType = Limit(input.ClientType, 32),
            IpAddress = Limit(input.IpAddress, 64),
            UserAgent = Limit(input.UserAgent, 256),
            Creator = Limit(executionContext.ActorId, 100),
            CreateDate = now,
        };
    }

    private static void ValidateInput(SpaceAuditEventInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        if (string.IsNullOrWhiteSpace(input.Action))
            throw new ArgumentException("SPACE_AUDIT_ACTION_REQUIRED");
        if (string.IsNullOrWhiteSpace(input.ResourceType))
            throw new ArgumentException("SPACE_AUDIT_RESOURCE_TYPE_REQUIRED");
        if (!SpaceAuditOutcome.IsValid(input.Outcome))
            throw new ArgumentException("SPACE_AUDIT_OUTCOME_INVALID");
        if (input.ReasonCode is not null &&
            (!SpaceErrorSanitizer.IsStableReasonCode(input.ReasonCode) ||
             input.ReasonCode.Length > 100))
        {
            throw new ArgumentException("SPACE_AUDIT_REASON_CODE_INVALID");
        }
        if (!IsSha256(input.BeforeHash) ||
            !IsSha256(input.AfterHash) ||
            !IsSha256(input.Evidence?.ErrorFingerprint))
            throw new ArgumentException("SPACE_AUDIT_HASH_INVALID");
    }

    private static void ValidateContext(ISpaceExecutionContext context)
    {
        if (context.TenantId == Guid.Empty ||
            context.CorrelationId == Guid.Empty ||
            string.IsNullOrWhiteSpace(context.TraceId) ||
            string.IsNullOrWhiteSpace(context.ActorId) ||
            context.ActorType is not
                SpaceExecutionContext.UserActor and not
                SpaceExecutionContext.SystemActor)
        {
            throw new InvalidOperationException(
                "SPACE_AUDIT_EXECUTION_CONTEXT_INVALID");
        }
    }

    private static bool IsSha256(string? value)
    {
        if (value is null)
            return true;
        if (value.Length != 64)
            return false;

        foreach (var character in value)
        {
            if (character is not (>= '0' and <= '9') &&
                character is not (>= 'a' and <= 'f') &&
                character is not (>= 'A' and <= 'F'))
            {
                return false;
            }
        }

        return true;
    }

    private static string? Limit(string? value, int maxLength) =>
        string.IsNullOrEmpty(value)
            ? value
            : value.Length <= maxLength
                ? value
                : value[..maxLength];
}
