using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CP6.Client.Api;

namespace CP6.Client.Core;

public interface ILabelPrinter
{
    Task PrintAsync(LabelJob job, CancellationToken ct = default);
}

public sealed class LabelGatewayService
{
    private readonly IHttpClientFactory _clients;
    private readonly ClientOptions _options;
    private readonly ILabelPrinter _printer;
    private readonly ILabelGatewayJournal _journal;
    private readonly ILabelGatewayContext _context;
    private readonly SemaphoreSlim _processing = new(1, 1);
    private CancellationTokenSource? _loopCts;
    private Task? _loop;

    public LabelGatewayService(
        IHttpClientFactory clients,
        ClientOptions options,
        ILabelPrinter printer)
        : this(clients, options, printer, new FileLabelGatewayJournal(options.LabelJournalDirectory), new MissingLabelGatewayContext()) { }

    public LabelGatewayService(IHttpClientFactory clients, ClientOptions options, ILabelPrinter printer,
        ILabelGatewayJournal journal, ILabelGatewayContext context)
    {
        _clients = clients; _options = options; _printer = printer; _journal = journal; _context = context;
    }

    public event EventHandler<string>? StateChanged;
    public bool IsRunning => _loop is { IsCompleted: false };

    public Task StartAsync(CancellationToken ct = default)
    {
        if (IsRunning) return Task.CompletedTask;
        _loopCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        StateChanged?.Invoke(this, "Running");
        _loop = RunLoopAsync(_loopCts.Token);
        return Task.CompletedTask;
    }

    public async Task StopAsync()
    {
        if (_loopCts == null) return;
        await _loopCts.CancelAsync();
        if (_loop != null)
        {
            try { await _loop; }
            catch (OperationCanceledException) { }
        }
        _loopCts.Dispose();
        _loopCts = null;
        _loop = null;
        StateChanged?.Invoke(this, "Stopped");
    }

    public async Task ProcessOnceAsync(CancellationToken ct = default)
    {
        await _processing.WaitAsync(ct);
        try
        {
            var identity = _context.Capture();
            await using var lease = await _journal.TryAcquireAsync(identity, ct);
            if (lease == null) throw new LabelGatewayRecoveryException("GatewayBusy");
            var api = new Cp6ApiClient(_clients.CreateClient(ClientServiceCollectionExtensions.AuthenticatedClient),
                new ClientRequestScope(identity.Source, identity.TenantId, identity.ActorId, identity.DeviceId));
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var entry in await _journal.ReadOpenAsync(identity, ct))
            {
                seen.Add(entry.JobNo);
                await Resume(api, identity, entry, ct);
            }
            var candidates = new List<LabelJob>();
            for (var page = 1; candidates.Count < 25; page++)
            {
                RequireSameScope(identity);
                var jobs = await api.GetLabelJobsAsync(status: "PENDING", page: page, pageSize: 200, ct: ct);
                RequireSameScope(identity);
                candidates.AddRange(jobs.Items.Where(j => !seen.Contains(j.JobNo)
                    && (string.IsNullOrWhiteSpace(j.RequestedDeviceId) || j.RequestedDeviceId == identity.DeviceId)).Take(25 - candidates.Count));
                if (jobs.Items.Count == 0 || (long)page * 200 >= jobs.Total) break;
            }
            foreach (var pending in candidates)
            {
                ct.ThrowIfCancellationRequested(); if (!seen.Add(pending.JobNo)) continue;
                var entry = await _journal.ReadAsync(identity, pending.JobNo, ct);
                if (entry?.Stage == LabelGatewayStage.Completed)
                    throw new LabelGatewayRecoveryException("CompletedJobReturnedPending", pending.JobNo);
                if (entry == null)
                {
                    if (pending.OperationId == Guid.Empty || string.IsNullOrWhiteSpace(pending.RowVersion))
                        throw new LabelGatewayRecoveryException("LabelResponseInvalid", pending.JobNo);
                    entry = new LabelGatewayJournalEntry { ScopeKey = identity.ScopeKey, JobNo = pending.JobNo, CreationOperationId = pending.OperationId,
                        ClaimOperationId = Guid.NewGuid(), CompletionOperationId = Guid.NewGuid(), OriginalRowVersion = pending.RowVersion,
                        CompletionMessage = "Gateway print call returned", Stage = LabelGatewayStage.ClaimPending };
                    await _journal.SaveAsync(identity, entry, ct);
                }
                await Resume(api, identity, entry, ct);
            }
        }
        catch (LabelGatewayRecoveryException error) { StateChanged?.Invoke(this, error.Code); throw; }
        finally { _processing.Release(); }
    }

    private async Task Resume(Cp6ApiClient api, LabelGatewayIdentity identity, LabelGatewayJournalEntry entry, CancellationToken ct)
    {
        if (entry.Stage is LabelGatewayStage.PrintStarted or LabelGatewayStage.RecoveryRequired)
        {
            if (entry.Stage == LabelGatewayStage.PrintStarted)
                await _journal.SaveAsync(identity, entry with { Stage = LabelGatewayStage.RecoveryRequired, ErrorCode = "PrintOutcomeUnknown" }, CancellationToken.None);
            throw new LabelGatewayRecoveryException("PrintOutcomeUnknown", entry.JobNo);
        }
        if (entry.Stage is LabelGatewayStage.ClaimPending or LabelGatewayStage.ReadyToPrint)
        {
            LabelJob claimed;
            try
            {
                RequireSameScope(identity);
                claimed = await api.ClaimLabelJobAsync(entry.JobNo, new LabelJobCommand { OperationId = entry.ClaimOperationId,
                    RowVersion = entry.OriginalRowVersion, DeviceId = identity.DeviceId }, ct);
                RequireSameScope(identity); RequireJob(claimed, entry, identity, "PRINTING");
            }
            catch (Exception error) when (error is HttpRequestException or OperationCanceledException or JsonException)
            { throw new LabelGatewayRecoveryException("ClaimRecoveryPending", entry.JobNo, error); }
            var digest = Digest(claimed);
            if (entry.ContentDigest != null && (entry.ContentDigest != digest || entry.ClaimedRowVersion != claimed.RowVersion))
                throw new LabelGatewayRecoveryException("OriginalLabelResultChanged", entry.JobNo);
            entry = entry with { Stage = LabelGatewayStage.ReadyToPrint, ClaimedRowVersion = claimed.RowVersion, ContentDigest = digest };
            await _journal.SaveAsync(identity, entry, ct);
            ct.ThrowIfCancellationRequested(); RequireSameScope(identity);
            entry = entry with { Stage = LabelGatewayStage.PrintStarted };
            await _journal.SaveAsync(identity, entry, ct);
            ct.ThrowIfCancellationRequested(); RequireSameScope(identity);
            try { await _printer.PrintAsync(claimed, ct); }
            catch (Exception error)
            {
                // This external boundary may have produced output even when cancellation or an error is reported.
                await _journal.SaveAsync(identity, entry with { Stage = LabelGatewayStage.RecoveryRequired, ErrorCode = "PrintOutcomeUnknown" }, CancellationToken.None);
                throw new LabelGatewayRecoveryException("PrintOutcomeUnknown", entry.JobNo, error);
            }
            entry = entry with { Stage = LabelGatewayStage.CompletionPending };
            await _journal.SaveAsync(identity, entry, CancellationToken.None);
        }
        if (entry.Stage == LabelGatewayStage.CompletionPending)
        {
            try
            {
                ct.ThrowIfCancellationRequested(); RequireSameScope(identity);
                var result = await api.CompleteLabelJobAsync(entry.JobNo, new LabelJobCommand { OperationId = entry.CompletionOperationId,
                    RowVersion = entry.ClaimedRowVersion!, DeviceId = identity.DeviceId, ResultMessage = entry.CompletionMessage }, success: true, ct);
                RequireSameScope(identity); RequireJob(result, entry, identity, "COMPLETED");
            }
            catch (Exception error) when (error is HttpRequestException or OperationCanceledException or JsonException)
            { throw new LabelGatewayRecoveryException("CompletionRecoveryPending", entry.JobNo, error); }
            await _journal.SaveAsync(identity, entry with { Stage = LabelGatewayStage.Completed, ErrorCode = null }, CancellationToken.None);
        }
    }

    private void RequireSameScope(LabelGatewayIdentity identity)
    {
        if (_context.Capture().ScopeKey != identity.ScopeKey || !_options.ApiBaseAddress.Equals(identity.Source) || _options.Context.DeviceId != identity.DeviceId)
            throw new LabelGatewayRecoveryException("RecoveryScopeChanged");
    }

    private static void RequireJob(LabelJob result, LabelGatewayJournalEntry entry, LabelGatewayIdentity identity, string status)
    {
        if (result.JobNo != entry.JobNo || result.OperationId != entry.CreationOperationId || result.Status != status
            || result.RequestedDeviceId != identity.DeviceId || string.IsNullOrWhiteSpace(result.RowVersion))
            throw new LabelGatewayRecoveryException("LabelResponseInvalid", entry.JobNo);
    }

    private static string Digest(LabelJob job) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(
        new { job.JobNo, job.OperationId, job.WarehouseCd, job.TemplateName, job.Format, job.TemplateBody, job.PayloadJson, job.PrinterName, job.RequestedDeviceId }))));

    private async Task RunLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await ProcessOnceAsync(ct);
                StateChanged?.Invoke(this, "Online");
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
            catch (LabelGatewayRecoveryException error)
            { StateChanged?.Invoke(this, error.Code); }
            catch (HttpRequestException)
            {
                StateChanged?.Invoke(this, "Retrying");
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            { StateChanged?.Invoke(this, "RecoveryStorageUnavailable"); return; }
            await Task.Delay(TimeSpan.FromSeconds(5), ct);
        }
    }
}
