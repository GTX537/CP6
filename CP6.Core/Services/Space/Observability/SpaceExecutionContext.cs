namespace CP6.Core.Services.Space.Observability;

public sealed record SpaceExecutionContext(
    Guid CorrelationId,
    string TraceId,
    Guid TenantId,
    string ActorType,
    string ActorId,
    string? ActorName,
    string? OrganizationContextId = null,
    Guid? JobId = null,
    Guid? RunId = null,
    Guid? PublishAttemptId = null) : ISpaceExecutionContext
{
    public const string UserActor = "User";
    public const string SystemActor = "System";

    public static SpaceExecutionContext ForUser(
        Guid tenantId,
        string actorId,
        string? actorName,
        Guid correlationId,
        string traceId,
        string? organizationContextId = null)
        => Validate(new SpaceExecutionContext(
            correlationId,
            traceId,
            tenantId,
            UserActor,
            actorId,
            actorName,
            OrganizationContextId: organizationContextId));

    public static SpaceExecutionContext ForSystem(
        Guid tenantId,
        string actorId,
        Guid correlationId,
        string traceId,
        Guid? jobId = null,
        Guid? runId = null,
        Guid? publishAttemptId = null)
        => Validate(new SpaceExecutionContext(
            correlationId,
            traceId,
            tenantId,
            SystemActor,
            actorId,
            actorId,
            OrganizationContextId: null,
            JobId: jobId,
            RunId: runId,
            PublishAttemptId: publishAttemptId));

    internal static SpaceExecutionContext Validate(SpaceExecutionContext value)
    {
        if (value.TenantId == Guid.Empty)
            throw new ArgumentException("TenantId is required");
        if (value.CorrelationId == Guid.Empty)
            throw new ArgumentException("CorrelationId is required");
        if (string.IsNullOrWhiteSpace(value.TraceId))
            throw new ArgumentException("TraceId is required");
        if (string.IsNullOrWhiteSpace(value.ActorId))
            throw new ArgumentException("ActorId is required");
        if (value.ActorType is not UserActor and not SystemActor)
            throw new ArgumentException("ActorType is invalid");

        return value;
    }
}
