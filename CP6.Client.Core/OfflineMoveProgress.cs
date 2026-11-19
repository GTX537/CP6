using System.Security.Cryptography;
using System.Text.Json;
using CP6.Client.Api;

namespace CP6.Client.Core;

public sealed record OfflineMoveScan(string ClientScanNo, string Step, string Value);

public sealed class OfflineMoveProgress
{
    public MobileTask Task { get; set; } = new();
    public TaskScanProfile ScanProfile { get; set; } = new();
    public MoveScanStep Step { get; set; }
    public decimal ConfirmedQuantity { get; set; }
    public string? PartialReason { get; set; }
    public Guid CompletionOperationId { get; set; }
    public bool CompletionAttempted { get; set; }
    public string? PendingClientScanNo { get; set; }
    public string? PendingScanStep { get; set; }
    public string? PendingScanValue { get; set; }
    public List<OfflineMoveScan> OfflineScans { get; set; } = [];
    public DateTimeOffset SavedAt { get; set; }
}

public interface IOfflineMoveProgressStore
{
    Task<OfflineMoveProgress?> ReadAsync(ClientRequestScope scope, string taskNo, CancellationToken ct = default);
    Task WriteAsync(ClientRequestScope scope, OfflineMoveProgress progress, CancellationToken ct = default);
    Task ClearAsync(ClientRequestScope scope, string taskNo, Guid originalOperation, CancellationToken ct = default);
}

public sealed class FileOfflineMoveProgressStore(string directory) : IOfflineMoveProgressStore
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly SemaphoreSlim _mutations = new(1, 1);

    public async Task<OfflineMoveProgress?> ReadAsync(ClientRequestScope scope, string taskNo, CancellationToken ct = default)
    {
        try
        {
            await using var stream = new FileStream(EntryPath(scope, taskNo), FileMode.Open, FileAccess.Read,
                FileShare.Read | FileShare.Delete, 4096, FileOptions.Asynchronous);
            return await JsonSerializer.DeserializeAsync<OfflineMoveProgress>(stream, Json, ct);
        }
        catch (FileNotFoundException) { return null; }
        catch (DirectoryNotFoundException) { return null; }
    }

    public async Task WriteAsync(ClientRequestScope scope, OfflineMoveProgress progress, CancellationToken ct = default)
    {
        if (progress.CompletionOperationId == Guid.Empty) throw new ClientInteractionChangedException();
        var path = EntryPath(scope, progress.Task.TaskNo);
        await _mutations.WaitAsync(ct);
        try
        {
            var previous = await ReadAsync(scope, progress.Task.TaskNo, ct);
            if (previous != null && (previous.CompletionOperationId != progress.CompletionOperationId
                || previous.CompletionAttempted && !progress.CompletionAttempted))
                throw new InvalidOperationException("原草稿已有待核对的操作，请重新读取当前草稿。");

            Directory.CreateDirectory(directory);
            var temporary = path + "." + Guid.NewGuid().ToString("N");
            try
            {
                await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write,
                    FileShare.None, 4096, FileOptions.Asynchronous))
                    await JsonSerializer.SerializeAsync(stream, progress, Json, ct);
                File.Move(temporary, path, true);
            }
            finally { File.Delete(temporary); }
        }
        finally { _mutations.Release(); }
    }

    public async Task ClearAsync(ClientRequestScope scope, string taskNo, Guid originalOperation, CancellationToken ct = default)
    {
        await _mutations.WaitAsync(ct);
        try
        {
            var previous = await ReadAsync(scope, taskNo, ct);
            if (previous?.CompletionOperationId == originalOperation)
                File.Delete(EntryPath(scope, taskNo));
        }
        finally { _mutations.Release(); }
    }

    private string EntryPath(ClientRequestScope scope, string taskNo)
    {
        if (scope.TenantId == Guid.Empty || scope.ActorId == Guid.Empty
            || string.IsNullOrWhiteSpace(scope.DeviceId) || string.IsNullOrWhiteSpace(taskNo))
            throw new ClientInteractionChangedException();

        // 同一主体重新认证可以找回原键。旧版单文件缺少归属，继续隔离。
        var address = new object[] { scope.Source.AbsoluteUri, scope.TenantId, scope.ActorId, scope.DeviceId, taskNo };
        var hash = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(address)));
        return Path.Combine(directory, hash + ".json");
    }
}
