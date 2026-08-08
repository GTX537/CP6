using CP6.Entity.DomainModels.Wms;

namespace CP6.Core.Services.Wms;

public interface IMobileTaskNotifier
{
    Task NotifyAsync(Guid tenantId, string eventName, MobileTaskEvent payload, CancellationToken ct = default);
}

public sealed class MobileTaskEvent
{
    public string TaskNo { get; init; } = string.Empty;
    public string TaskType { get; init; } = MobileTaskType.Move;
    public int Status { get; init; }
    public string? AssignedTo { get; init; }
    public string? WarehouseCd { get; init; }
    public string? ProductCd { get; init; }
    public string RowVersion { get; init; } = string.Empty;
    public DateTime At { get; init; } = DateTime.UtcNow;
}

public sealed class NoOpMobileTaskNotifier : IMobileTaskNotifier
{
    public Task NotifyAsync(Guid tenantId, string eventName, MobileTaskEvent payload, CancellationToken ct = default)
        => Task.CompletedTask;
}
