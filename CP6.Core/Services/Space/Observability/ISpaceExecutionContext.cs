namespace CP6.Core.Services.Space.Observability;

public interface ISpaceExecutionContext
{
    Guid CorrelationId { get; }
    string TraceId { get; }
    Guid TenantId { get; }
    string ActorType { get; }
    string ActorId { get; }
    string? ActorName { get; }
    string? OrganizationContextId { get; }
    Guid? JobId { get; }
    Guid? RunId { get; }
    Guid? PublishAttemptId { get; }
}

public interface ISpaceExecutionContextAccessor
{
    ISpaceExecutionContext? Current { get; }
    ISpaceExecutionContext? OutcomeCurrent { get; }
    ISpaceExecutionContext RequireCurrent();
    ISpaceExecutionContext RequireOutcomeCurrent();
}

public interface ISpaceExecutionContextManager
{
    IDisposable Push(SpaceExecutionContext context);
    IDisposable PushDerived(SpaceExecutionContext context);

    void Enrich(
        Guid? jobId = null,
        Guid? runId = null,
        Guid? publishAttemptId = null,
        string? traceId = null);
}
