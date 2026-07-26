namespace CP6.Space.Application;

public interface ISpaceExecutionContext
{
    Guid TenantId { get; }
    Guid ActorId { get; }
}

public interface ISpaceClock
{
    DateTime UtcNow { get; }
}

public sealed class SystemSpaceClock : ISpaceClock
{
    public DateTime UtcNow => DateTime.UtcNow;
}
