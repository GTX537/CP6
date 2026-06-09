using CP6.Entity.DomainModels;

namespace CP6.Core.Services.Integration;

/// <summary>
/// Dispatches failed integration events back to their original bridge hook.
/// </summary>
public interface IIntegrationEventDispatcher
{
    /// <summary>
    /// Re-invokes the original hook based on (SourceModule, TargetModule, HookName).
    /// Throws if the route is unknown. Returns the hook's success status.
    /// </summary>
    Task<bool> DispatchAsync(IntegrationEvent evt, CancellationToken ct = default);
}
