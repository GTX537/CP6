using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace CP6.Client.Api;

/// <summary>
/// Typed client generated from CP6's client-facing OpenAPI surface.  Keep route
/// literals here so native clients do not hand-build URLs throughout the UI.
/// </summary>
public sealed class Cp6ApiClient
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _http;

    public Cp6ApiClient(HttpClient http) => _http = http;

    public Task<NativeAuthResult> LoginAsync(NativeLoginRequest request, CancellationToken ct = default)
        => SendAsync<NativeAuthResult>(HttpMethod.Post, "api/client-auth/login", request, ct);

    public Task<NativeAuthResult> VerifyTwoFactorAsync(NativeTwoFactorRequest request, CancellationToken ct = default)
        => SendAsync<NativeAuthResult>(HttpMethod.Post, "api/client-auth/2fa/verify", request, ct);

    public Task<TwoFactorSetup> SetupTwoFactorAsync(NativeChallengeRequest request, CancellationToken ct = default)
        => SendAsync<TwoFactorSetup>(HttpMethod.Post, "api/client-auth/2fa/setup", request, ct);

    public Task<NativeAuthResult> EnrollTwoFactorAsync(NativeTwoFactorRequest request, CancellationToken ct = default)
        => SendAsync<NativeAuthResult>(HttpMethod.Post, "api/client-auth/2fa/enroll", request, ct);

    public Task RequestEmailOtpAsync(NativeChallengeRequest request, CancellationToken ct = default)
        => SendNoContentAsync(HttpMethod.Post, "api/client-auth/2fa/email-otp", request, ct);

    public Task<NativeSsoStartResponse> StartSsoAsync(NativeSsoStartRequest request, CancellationToken ct = default)
        => SendAsync<NativeSsoStartResponse>(HttpMethod.Post, "api/client-auth/sso/start", request, ct);

    public Task<NativeAuthResult> ExchangeSsoAsync(NativeSsoExchangeRequest request, CancellationToken ct = default)
        => SendAsync<NativeAuthResult>(HttpMethod.Post, "api/client-auth/sso/exchange", request, ct);

    public Task<TokenSession> RefreshAsync(NativeRefreshRequest request, CancellationToken ct = default)
        => SendAsync<TokenSession>(HttpMethod.Post, "api/client-auth/refresh", request, ct);

    public Task LogoutAsync(NativeLogoutRequest request, CancellationToken ct = default)
        => SendNoContentAsync(HttpMethod.Post, "api/client-auth/logout", request, ct);

    public Task<NativeAuthResult> QuickSwitchAsync(
        QuickSwitchRequest request,
        CancellationToken ct = default)
        => SendAsync<NativeAuthResult>(HttpMethod.Post, "api/client-auth/quick-switch", request, ct);

    public Task<ActivatedClientDevice> ActivateDeviceAsync(
        ActivateClientDeviceRequest request,
        CancellationToken ct = default)
        => SendAsync<ActivatedClientDevice>(HttpMethod.Post, "api/client/devices/activate", request, ct);

    public Task<ClientDevice> HeartbeatAsync(
        ClientDeviceHeartbeatRequest request,
        CancellationToken ct = default)
        => SendAsync<ClientDevice>(HttpMethod.Post, "api/client/devices/heartbeat", request, ct);

    public Task<TaskAnalytics> GetTaskAnalyticsAsync(CancellationToken ct = default)
        => SendAsync<TaskAnalytics>(
            HttpMethod.Get, "api/v2/wms/task-analytics", null, ct);

    public Task<PagedResult<ClientDevice>> GetClientDevicesAsync(
        int page = 1,
        int pageSize = 100,
        CancellationToken ct = default)
        => SendAsync<PagedResult<ClientDevice>>(
            HttpMethod.Get,
            $"api/v2/admin/client-devices?page={page}&pageSize={pageSize}",
            null,
            ct);

    public Task<ClientDevice> UpdateClientDeviceAsync(
        string deviceId,
        UpdateClientDeviceRequest request,
        CancellationToken ct = default)
        => SendAsync<ClientDevice>(
            HttpMethod.Patch,
            $"api/v2/admin/client-devices/{Uri.EscapeDataString(deviceId)}",
            request,
            ct);

    public Task<PagedResult<BarcodeAlias>> GetBarcodeAliasesAsync(
        int page = 1,
        int pageSize = 100,
        CancellationToken ct = default)
        => SendAsync<PagedResult<BarcodeAlias>>(
            HttpMethod.Get,
            $"api/v2/wms/barcodes?page={page}&pageSize={pageSize}",
            null,
            ct);

    public Task<BarcodeAlias> UpsertBarcodeAliasAsync(
        UpsertBarcodeAliasRequest request,
        CancellationToken ct = default)
        => SendAsync<BarcodeAlias>(
            HttpMethod.Post, "api/v2/wms/barcodes", request, ct);

    public Task<PagedResult<LabelJob>> GetLabelJobsAsync(
        string? status = null,
        string? warehouseCd = null,
        int page = 1,
        int pageSize = 25,
        CancellationToken ct = default)
    {
        var query = new List<string> { $"page={page}", $"pageSize={pageSize}" };
        if (!string.IsNullOrWhiteSpace(status))
            query.Add($"status={Uri.EscapeDataString(status)}");
        if (!string.IsNullOrWhiteSpace(warehouseCd))
            query.Add($"warehouseCd={Uri.EscapeDataString(warehouseCd)}");
        return SendAsync<PagedResult<LabelJob>>(
            HttpMethod.Get, $"api/v2/wms/label-jobs?{string.Join("&", query)}", null, ct);
    }

    public Task<LabelJob> ClaimLabelJobAsync(
        string jobNo,
        LabelJobCommand request,
        CancellationToken ct = default)
        => SendAsync<LabelJob>(
            HttpMethod.Post,
            $"api/v2/wms/label-jobs/{Uri.EscapeDataString(jobNo)}/claim",
            request,
            ct);

    public Task<LabelJob> CompleteLabelJobAsync(
        string jobNo,
        LabelJobCommand request,
        bool success,
        CancellationToken ct = default)
        => SendAsync<LabelJob>(
            HttpMethod.Post,
            $"api/v2/wms/label-jobs/{Uri.EscapeDataString(jobNo)}/{(success ? "complete" : "fail")}",
            request,
            ct);

    public Task<ClientBootstrap> BootstrapAsync(
        string platform,
        string currentVersion,
        CancellationToken ct = default)
        => SendAsync<ClientBootstrap>(
            HttpMethod.Get,
            $"api/client/bootstrap?platform={Uri.EscapeDataString(platform)}&currentVersion={Uri.EscapeDataString(currentVersion)}",
            null,
            ct);

    public Task<PagedResult<MobileTask>> GetTasksAsync(
        string? assignedTo = null,
        bool includeUnassigned = false,
        int? status = null,
        bool openOnly = false,
        int page = 1,
        int pageSize = 50,
        CancellationToken ct = default)
    {
        var query = new List<string>
        {
            $"includeUnassigned={includeUnassigned.ToString().ToLowerInvariant()}",
            $"openOnly={openOnly.ToString().ToLowerInvariant()}",
            $"page={page}",
            $"pageSize={pageSize}",
        };
        if (!string.IsNullOrWhiteSpace(assignedTo))
            query.Add($"assignedTo={Uri.EscapeDataString(assignedTo)}");
        if (status.HasValue)
            query.Add($"status={status.Value}");
        return SendAsync<PagedResult<MobileTask>>(
            HttpMethod.Get, $"api/v2/wms/tasks?{string.Join("&", query)}", null, ct);
    }

    public Task<MobileTask> GetTaskAsync(string taskNo, CancellationToken ct = default)
        => SendAsync<MobileTask>(HttpMethod.Get, TaskPath(taskNo), null, ct);

    public Task<MobileTask> CreateMoveTaskAsync(CreateMoveTaskRequest request, CancellationToken ct = default)
        => SendAsync<MobileTask>(HttpMethod.Post, "api/v2/wms/tasks", request, ct);

    public Task<MobileTask> AssignTaskAsync(string taskNo, AssignTaskRequest request, CancellationToken ct = default)
        => SendAsync<MobileTask>(HttpMethod.Post, $"{TaskPath(taskNo)}/assign", request, ct);

    public Task<MobileTask> ClaimTaskAsync(string taskNo, ClaimTaskRequest request, CancellationToken ct = default)
        => SendAsync<MobileTask>(HttpMethod.Post, $"{TaskPath(taskNo)}/claim", request, ct);

    public Task<MobileTask> StartTaskAsync(string taskNo, StartTaskRequest request, CancellationToken ct = default)
        => SendAsync<MobileTask>(HttpMethod.Post, $"{TaskPath(taskNo)}/start", request, ct);

    public Task<TaskScanProfile> GetScanProfileAsync(string taskNo, CancellationToken ct = default)
        => SendAsync<TaskScanProfile>(HttpMethod.Get, $"{TaskPath(taskNo)}/scan-profile", null, ct);

    public Task<ScanResult> ScanAsync(string taskNo, ScanRequest request, CancellationToken ct = default)
        => SendAsync<ScanResult>(HttpMethod.Post, $"{TaskPath(taskNo)}/scan", request, ct);

    public Task<MobileTask> CompleteMoveAsync(string taskNo, CompleteMoveRequest request, CancellationToken ct = default)
        => SendAsync<MobileTask>(HttpMethod.Post, $"{TaskPath(taskNo)}/complete", request, ct);

    public Task<MobileTask> CancelTaskAsync(string taskNo, CancelTaskRequest request, CancellationToken ct = default)
        => SendAsync<MobileTask>(HttpMethod.Post, $"{TaskPath(taskNo)}/cancel", request, ct);

    public Task<MobileTask> PauseTaskAsync(string taskNo, PauseTaskRequest request, CancellationToken ct = default)
        => SendAsync<MobileTask>(HttpMethod.Post, $"{TaskPath(taskNo)}/pause", request, ct);

    public Task<MobileTask> ReleaseTaskAsync(string taskNo, PauseTaskRequest request, CancellationToken ct = default)
        => SendAsync<MobileTask>(HttpMethod.Post, $"{TaskPath(taskNo)}/release", request, ct);

    public Task<MobileTask> TakeoverTaskAsync(string taskNo, TakeoverTaskRequest request, CancellationToken ct = default)
        => SendAsync<MobileTask>(HttpMethod.Post, $"{TaskPath(taskNo)}/takeover", request, ct);

    public Task<MobileTask> RaiseTaskExceptionAsync(string taskNo, RaiseTaskExceptionRequest request, CancellationToken ct = default)
        => SendAsync<MobileTask>(HttpMethod.Post, $"{TaskPath(taskNo)}/exception", request, ct);

    public Task<LangManifest> GetLanguageManifestAsync(CancellationToken ct = default)
        => SendAsync<LangManifest>(HttpMethod.Get, "api/lang/manifest", null, ct);

    public Task<Dictionary<string, string>> GetLanguagePackAsync(
        string version,
        string language,
        CancellationToken ct = default)
        => SendAsync<Dictionary<string, string>>(
            HttpMethod.Get,
            $"api/lang/published/{Uri.EscapeDataString(version)}/{Uri.EscapeDataString(language)}",
            null,
            ct);

    private static string TaskPath(string taskNo)
        => $"api/v2/wms/tasks/{Uri.EscapeDataString(taskNo)}";

    private async Task<T> SendAsync<T>(
        HttpMethod method,
        string path,
        object? body,
        CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, path);
        if (body != null) request.Content = JsonContent.Create(body, options: Json);
        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!response.IsSuccessStatusCode)
            throw await ApiException.CreateAsync(response, ct);
        return await response.Content.ReadFromJsonAsync<T>(Json, ct)
               ?? throw new ApiException(response.StatusCode, "E-CLIENT-EMPTY", "Server returned an empty response.");
    }

    private async Task SendNoContentAsync(
        HttpMethod method,
        string path,
        object body,
        CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, path)
        {
            Content = JsonContent.Create(body, options: Json),
        };
        using var response = await _http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
            throw await ApiException.CreateAsync(response, ct);
    }
}

public sealed class ApiException : HttpRequestException
{
    public ApiException(HttpStatusCode statusCode, string? code, string message)
        : base(message, null, statusCode)
    {
        Code = code;
    }

    public string? Code { get; }

    internal static async Task<ApiException> CreateAsync(
        HttpResponseMessage response,
        CancellationToken ct)
    {
        try
        {
            var error = await response.Content.ReadFromJsonAsync<ApiError>(
                new JsonSerializerOptions(JsonSerializerDefaults.Web), ct);
            return new ApiException(
                response.StatusCode,
                error?.Code ?? error?.Message,
                error?.Message ?? error?.Title ?? response.ReasonPhrase ?? "Request failed.");
        }
        catch
        {
            return new ApiException(
                response.StatusCode,
                null,
                response.ReasonPhrase ?? "Request failed.");
        }
    }
}
