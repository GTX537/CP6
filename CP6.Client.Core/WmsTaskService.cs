using CP6.Client.Api;

namespace CP6.Client.Core;

public sealed class WmsTaskService
{
    private readonly Cp6ApiClient _api;
    private readonly ClientOptions _options;
    private readonly IClientSessionService _sessions;

    public WmsTaskService(
        IHttpClientFactory clients,
        ClientOptions options,
        IClientSessionService sessions)
    {
        _api = new Cp6ApiClient(clients.CreateClient(ClientServiceCollectionExtensions.AuthenticatedClient));
        _options = options;
        _sessions = sessions;
    }

    public Task<PagedResult<MobileTask>> GetAllAsync(
        string? assignedTo = null,
        int? status = null,
        bool openOnly = false,
        int page = 1,
        int pageSize = 50,
        CancellationToken ct = default)
        => _api.GetTasksAsync(
            assignedTo: assignedTo,
            includeUnassigned: false,
            status: status,
            openOnly: openOnly,
            page: page,
            pageSize: pageSize,
            ct: ct);

    public Task<PagedResult<MobileTask>> GetMineAndUnassignedAsync(
        string userName,
        CancellationToken ct = default)
        => _api.GetTasksAsync(userName, includeUnassigned: true, openOnly: true, ct: ct);

    public Task<MobileTask> GetAsync(string taskNo, CancellationToken ct = default)
        => _api.GetTaskAsync(taskNo, ct);

    public Task<MobileTask> CreateAsync(CreateMoveTaskRequest request, CancellationToken ct = default)
    {
        if (request.OperationId == Guid.Empty) request.OperationId = Guid.NewGuid();
        return _api.CreateMoveTaskAsync(request, ct);
    }

    public Task<MobileTask> AssignAsync(MobileTask task, string userName, CancellationToken ct = default)
        => _api.AssignTaskAsync(task.TaskNo, new AssignTaskRequest
        {
            AssignedTo = userName,
            RowVersion = task.RowVersion,
            DeviceId = _options.Context.DeviceId,
            ExecutionVersion = task.ExecutionVersion,
        }, ct);

    public Task<MobileTask> StartAsync(MobileTask task, CancellationToken ct = default)
        => _api.StartTaskAsync(task.TaskNo, new StartTaskRequest
        {
            RowVersion = task.RowVersion,
            DeviceId = _options.Context.DeviceId,
            ExecutionVersion = task.ExecutionVersion,
        }, ct);

    public async Task<MobileTask> ClaimAsync(MobileTask task, CancellationToken ct = default)
    {
        var operationId = Guid.NewGuid();
        try
        {
            return await _api.ClaimTaskAsync(task.TaskNo, new ClaimTaskRequest
            {
                OperationId = operationId,
                DeviceId = _options.Context.DeviceId,
                RowVersion = task.RowVersion,
                ExecutionVersion = task.ExecutionVersion,
            }, ct);
        }
        catch (Exception ex) when (IsUnknownTransportFailure(ex, ct))
        {
            var current = await ProbeTaskAsync(
                task.TaskNo, "claim", operationId, ex, ct);
            if (current.Status == 1
                && string.Equals(
                    current.AssignedTo,
                    _sessions.Current?.Profile.UserName,
                    StringComparison.OrdinalIgnoreCase))
                return current;
            throw Unknown(
                task.TaskNo, "claim", operationId,
                "RELOAD_TASK_STATE_THEN_RETRY", ex);
        }
    }

    public Task<TaskScanProfile> GetScanProfileAsync(
        MobileTask task,
        CancellationToken ct = default)
        => _api.GetScanProfileAsync(task.TaskNo, ct);

    public Task<ScanResult> ScanAsync(
        MobileTask task,
        string step,
        string barcode,
        string? clientScanNo = null,
        CancellationToken ct = default)
        => SendScanAsync(task, new ScanRequest
        {
            RowVersion = task.RowVersion,
            DeviceId = _options.Context.DeviceId,
            ExecutionVersion = task.ExecutionVersion,
            Step = step,
            RawBarcode = barcode,
            ClientScanNo = string.IsNullOrWhiteSpace(clientScanNo)
                ? Guid.NewGuid().ToString("N")
                : clientScanNo,
        }, ct);

    public async Task<MobileTask> CompleteAsync(
        MobileTask task,
        Guid operationId,
        decimal quantity,
        string? partialReason = null,
        string? remarks = null,
        CancellationToken ct = default)
    {
        var quantityScan = await SendScanAsync(task, new ScanRequest
        {
            RowVersion = task.RowVersion,
            DeviceId = _options.Context.DeviceId,
            ExecutionVersion = task.ExecutionVersion,
            Step = "Quantity",
            RawBarcode = quantity.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ClientScanNo = $"qty-{operationId:N}",
        }, ct);
        if (quantityScan.Matched != true)
            throw new InvalidOperationException(quantityScan.ErrorCode ?? "WM-V2-SCAN-MISMATCH");
        try
        {
            return await _api.CompleteMoveAsync(task.TaskNo, new CompleteMoveRequest
            {
                OperationId = operationId,
                RowVersion = task.RowVersion,
                ScannedQty = quantity,
                ToLocationCd = task.ToLocationCd ?? string.Empty,
                DeviceId = _options.Context.DeviceId,
                ExecutionVersion = task.ExecutionVersion,
                PartialReason = partialReason,
                Remarks = remarks,
            }, ct);
        }
        catch (Exception ex) when (IsUnknownTransportFailure(ex, ct))
        {
            var current = await ProbeTaskAsync(
                task.TaskNo, "complete", operationId, ex, ct);
            if (current.CompletionOperationId == operationId
                && current.Status is 2 or 3)
                return current;
            throw Unknown(
                task.TaskNo, "complete", operationId,
                "RELOAD_TASK_STATE_THEN_RETRY_SAME_OPERATION", ex);
        }
    }

    private async Task<ScanResult> SendScanAsync(
        MobileTask task,
        ScanRequest request,
        CancellationToken ct)
    {
        try
        {
            return await _api.ScanAsync(task.TaskNo, request, ct);
        }
        catch (Exception ex) when (IsUnknownTransportFailure(ex, ct))
        {
            throw Unknown(
                task.TaskNo,
                "scan",
                request.OperationId,
                "RESCAN_WITH_SAME_CLIENT_SCAN_NO",
                ex,
                request.ClientScanNo);
        }
    }

    private async Task<MobileTask> ProbeTaskAsync(
        string taskNo,
        string command,
        Guid operationId,
        Exception commandFailure,
        CancellationToken ct)
    {
        try
        {
            return await _api.GetTaskAsync(taskNo, ct);
        }
        catch (Exception probeFailure)
            when (IsUnknownTransportFailure(probeFailure, ct))
        {
            throw Unknown(
                taskNo,
                command,
                operationId,
                "RELOAD_TASK_STATE",
                new AggregateException(commandFailure, probeFailure));
        }
    }

    private static bool IsUnknownTransportFailure(
        Exception exception,
        CancellationToken callerToken)
        => exception switch
        {
            HttpRequestException { StatusCode: null } => true,
            OperationCanceledException
                when !callerToken.IsCancellationRequested => true,
            TimeoutException => true,
            System.Text.Json.JsonException => true,
            _ => false
        };

    private static RequestOutcomeUnknownException Unknown(
        string taskNo,
        string command,
        Guid operationId,
        string recoveryAction,
        Exception inner,
        string? clientScanNo = null)
        => new(
            taskNo,
            command,
            operationId,
            recoveryAction,
            inner,
            clientScanNo);

    public Task<MobileTask> PauseAsync(
        MobileTask task,
        string reason,
        CancellationToken ct = default)
        => _api.PauseTaskAsync(task.TaskNo, new PauseTaskRequest
        {
            RowVersion = task.RowVersion,
            DeviceId = _options.Context.DeviceId,
            ExecutionVersion = task.ExecutionVersion,
            Reason = reason,
        }, ct);

    public Task<MobileTask> ReleaseAsync(
        MobileTask task,
        string reason,
        CancellationToken ct = default)
        => _api.ReleaseTaskAsync(task.TaskNo, new PauseTaskRequest
        {
            RowVersion = task.RowVersion,
            DeviceId = _options.Context.DeviceId,
            ExecutionVersion = task.ExecutionVersion,
            Reason = reason,
        }, ct);

    public Task<MobileTask> TakeoverAsync(
        MobileTask task,
        string assignedTo,
        string reason,
        CancellationToken ct = default)
        => _api.TakeoverTaskAsync(task.TaskNo, new TakeoverTaskRequest
        {
            RowVersion = task.RowVersion,
            DeviceId = _options.Context.DeviceId,
            ExecutionVersion = task.ExecutionVersion,
            AssignedTo = assignedTo,
            Reason = reason,
        }, ct);

    public Task<MobileTask> RaiseExceptionAsync(
        MobileTask task,
        string reasonCode,
        string description,
        CancellationToken ct = default)
        => _api.RaiseTaskExceptionAsync(task.TaskNo, new RaiseTaskExceptionRequest
        {
            RowVersion = task.RowVersion,
            DeviceId = _options.Context.DeviceId,
            ExecutionVersion = task.ExecutionVersion,
            ReasonCode = reasonCode,
            Description = description,
        }, ct);

    public Task<MobileTask> CancelAsync(
        MobileTask task,
        string reason = "",
        CancellationToken ct = default)
        => _api.CancelTaskAsync(task.TaskNo, new CancelTaskRequest
        {
            RowVersion = task.RowVersion,
            DeviceId = _options.Context.DeviceId,
            ExecutionVersion = task.ExecutionVersion,
            Reason = reason,
        }, ct);
}

public sealed class RequestOutcomeUnknownException : InvalidOperationException
{
    public RequestOutcomeUnknownException(
        string taskNo,
        string command,
        Guid operationId,
        string recoveryAction,
        Exception inner,
        string? clientScanNo = null)
        : base("E-CLIENT-REQUEST-OUTCOME-UNKNOWN", inner)
    {
        TaskNo = taskNo;
        Command = command;
        OperationId = operationId;
        RecoveryAction = recoveryAction;
        ClientScanNo = clientScanNo;
    }

    public string TaskNo { get; }
    public string Command { get; }
    public Guid OperationId { get; }
    public string RecoveryAction { get; }
    public string? ClientScanNo { get; }
}
