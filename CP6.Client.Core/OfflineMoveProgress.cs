using CP6.Client.Api;

namespace CP6.Client.Core;

public sealed class OfflineMoveProgress
{
    public MobileTask Task { get; set; } = new();
    public TaskScanProfile ScanProfile { get; set; } = new();
    public MoveScanStep Step { get; set; }
    public decimal ConfirmedQuantity { get; set; }
    public string? PartialReason { get; set; }
    public Guid CompletionOperationId { get; set; }
    public DateTimeOffset SavedAt { get; set; }
}

public interface IOfflineMoveProgressStore
{
    Task<OfflineMoveProgress?> ReadAsync(CancellationToken ct = default);
    Task WriteAsync(OfflineMoveProgress progress, CancellationToken ct = default);
    Task ClearAsync(CancellationToken ct = default);
}
