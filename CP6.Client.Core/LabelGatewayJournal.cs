using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CP6.Client.Core;

public enum LabelGatewayStage
{
    ClaimPending = 1,
    ReadyToPrint = 2,
    PrintStarted = 3,
    CompletionPending = 4,
    Completed = 5,
    RecoveryRequired = 6
}

public sealed record LabelGatewayJournalEntry
{
    [JsonRequired] public int Version { get; init; } = 1;
    [JsonRequired] public required string ScopeKey { get; init; }
    [JsonRequired] public required string JobNo { get; init; }
    [JsonRequired] public Guid CreationOperationId { get; init; }
    [JsonRequired] public Guid ClaimOperationId { get; init; }
    [JsonRequired] public Guid CompletionOperationId { get; init; }
    [JsonRequired] public required string OriginalRowVersion { get; init; }
    [JsonRequired] public required string CompletionMessage { get; init; }
    [JsonRequired] public LabelGatewayStage Stage { get; init; }
    public string? ClaimedRowVersion { get; init; }
    public string? ContentDigest { get; init; }
    public string? ErrorCode { get; init; }
}

public interface ILabelGatewayJournal
{
    Task<IAsyncDisposable?> TryAcquireAsync(LabelGatewayIdentity identity, CancellationToken ct = default);
    Task<IReadOnlyList<LabelGatewayJournalEntry>> ReadOpenAsync(LabelGatewayIdentity identity, CancellationToken ct = default);
    Task<LabelGatewayJournalEntry?> ReadAsync(LabelGatewayIdentity identity, string jobNo, CancellationToken ct = default);
    Task SaveAsync(LabelGatewayIdentity identity, LabelGatewayJournalEntry entry, CancellationToken ct = default);
}

public sealed class FileLabelGatewayJournal(string directory) : ILabelGatewayJournal
{
    private readonly string _root = Path.GetFullPath(directory);

    public Task<IAsyncDisposable?> TryAcquireAsync(LabelGatewayIdentity identity, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested(); var folder = Folder(identity); Directory.CreateDirectory(folder);
        try
        {
            IAsyncDisposable lease = new FileStream(Path.Combine(folder, "worker.lock"), FileMode.OpenOrCreate,
                FileAccess.ReadWrite, FileShare.None, 1, FileOptions.WriteThrough);
            return Task.FromResult<IAsyncDisposable?>(lease);
        }
        catch (IOException error) when ((error.HResult & 0xffff) is 32 or 33)
        { return Task.FromResult<IAsyncDisposable?>(null); }
    }

    public async Task<IReadOnlyList<LabelGatewayJournalEntry>> ReadOpenAsync(LabelGatewayIdentity identity, CancellationToken ct = default)
    {
        var active = Path.Combine(Folder(identity), "active");
        if (!Directory.Exists(active)) return Array.Empty<LabelGatewayJournalEntry>();
        var result = new List<LabelGatewayJournalEntry>();
        foreach (var path in Directory.EnumerateFiles(active, "*.json").Order(StringComparer.Ordinal))
        {
            var entry = await ReadFile(identity, path, ct);
            var closed = EntryPath(identity, entry.JobNo, true);
            if (File.Exists(closed)) _ = await ReadFile(identity, closed, ct);
            else if (entry.Stage != LabelGatewayStage.Completed) result.Add(entry);
        }
        return result;
    }

    public async Task<LabelGatewayJournalEntry?> ReadAsync(LabelGatewayIdentity identity, string jobNo, CancellationToken ct = default)
    {
        var closed = EntryPath(identity, jobNo, true); var active = EntryPath(identity, jobNo, false);
        var path = File.Exists(closed) ? closed : File.Exists(active) ? active : null;
        if (path == null) return null;
        var entry = await ReadFile(identity, path, ct);
        if (entry.JobNo != jobNo) throw new LabelGatewayRecoveryException("RecoveryJournalInvalid", jobNo);
        return entry;
    }

    public async Task SaveAsync(LabelGatewayIdentity identity, LabelGatewayJournalEntry entry, CancellationToken ct = default)
    {
        Validate(identity, entry);
        var completed = entry.Stage == LabelGatewayStage.Completed;
        var path = EntryPath(identity, entry.JobNo, completed); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var file = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                4096, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(file, entry, cancellationToken: ct);
                await file.FlushAsync(ct); file.Flush(flushToDisk: true);
            }
            File.Move(temp, path, overwrite: true);
            if (completed)
            {
                var active = EntryPath(identity, entry.JobNo, false);
                if (File.Exists(active)) File.Delete(active);
            }
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }

    private async Task<LabelGatewayJournalEntry> ReadFile(LabelGatewayIdentity identity, string path, CancellationToken ct)
    {
        try
        {
            await using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            var entry = await JsonSerializer.DeserializeAsync<LabelGatewayJournalEntry>(input, cancellationToken: ct)
                ?? throw new JsonException();
            Validate(identity, entry);
            var completed = Path.GetFileName(Path.GetDirectoryName(path)) == "completed";
            if (!Path.GetFileName(path).Equals(Path.GetFileName(EntryPath(identity, entry.JobNo, completed)), StringComparison.Ordinal)
                || completed != (entry.Stage == LabelGatewayStage.Completed))
                throw new LabelGatewayRecoveryException("RecoveryJournalInvalid", entry.JobNo);
            return entry;
        }
        catch (JsonException error) { throw new LabelGatewayRecoveryException("RecoveryJournalInvalid", inner: error); }
    }

    private static void Validate(LabelGatewayIdentity identity, LabelGatewayJournalEntry entry)
    {
        if (entry.Version != 1 || entry.ScopeKey != identity.ScopeKey || string.IsNullOrWhiteSpace(entry.JobNo)
            || entry.CreationOperationId == Guid.Empty || entry.ClaimOperationId == Guid.Empty || entry.CompletionOperationId == Guid.Empty
            || entry.ClaimOperationId == entry.CompletionOperationId || string.IsNullOrWhiteSpace(entry.OriginalRowVersion)
            || string.IsNullOrWhiteSpace(entry.CompletionMessage) || !Enum.IsDefined(entry.Stage)
            || entry.Stage is LabelGatewayStage.ReadyToPrint or LabelGatewayStage.PrintStarted or LabelGatewayStage.CompletionPending or LabelGatewayStage.Completed
                && (string.IsNullOrWhiteSpace(entry.ClaimedRowVersion) || string.IsNullOrWhiteSpace(entry.ContentDigest)))
            throw new LabelGatewayRecoveryException("RecoveryJournalInvalid", entry.JobNo);
    }

    private string Folder(LabelGatewayIdentity identity) => Path.Combine(_root, identity.ScopeKey);
    private string EntryPath(LabelGatewayIdentity identity, string jobNo, bool completed)
        => Path.Combine(Folder(identity), completed ? "completed" : "active",
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(jobNo))) + ".json");
}
