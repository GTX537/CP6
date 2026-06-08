using CP6.Entity.DomainModels;

namespace CP6.Core.Services;

/// <summary>
/// Sends dead-letter alerts for exhausted integration events.
/// </summary>
public interface IDeadLetterNotifier
{
    /// <summary>
    /// Notifies operators that an integration event exhausted retry attempts.
    /// </summary>
    Task NotifyAsync(IntegrationEvent evt, CancellationToken ct = default);
}
