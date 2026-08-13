namespace CP6.Space.Application;

public interface ISpaceExecutionContext
{
    Guid TenantId { get; }
    Guid ActorId { get; }
    string? ActorDisplayName => null;
    string RequestSource => "unknown";
    bool IsExternal => false;
    Guid? OrganizationContextId => null;
}

public interface ISpaceCorrelationContext
{
    Guid CorrelationId { get; }
}

public interface ISpaceClock
{
    DateTime UtcNow { get; }
}

public sealed class SystemSpaceClock : ISpaceClock
{
    public DateTime UtcNow => DateTime.UtcNow;
}
