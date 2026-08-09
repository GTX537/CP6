using System.Net;
using System.Net.Http.Json;
using CP6.Client.Api;
using CP6.Client.Core;

namespace CP6.Client.Tests;

public sealed class WmsTaskRecoveryTests
{
    [Fact]
    public async Task ClaimTimeout_ProbesStateWithoutReplayingWrite()
    {
        var claimCalls = 0;
        var getCalls = 0;
        var service = Service(async (request, ct) =>
        {
            if (request.Method == HttpMethod.Post
                && request.RequestUri!.AbsolutePath.EndsWith("/claim"))
            {
                claimCalls++;
                throw new OperationCanceledException("simulated timeout");
            }

            getCalls++;
            return Json(new MobileTask
            {
                TaskNo = "MOV-1",
                Status = 1,
                AssignedTo = "alice"
            });
        }, "alice");

        var result = await service.ClaimAsync(Mobile("MOV-1"));

        Assert.Equal(1, result.Status);
        Assert.Equal("alice", result.AssignedTo);
        Assert.Equal(1, claimCalls);
        Assert.Equal(1, getCalls);
    }

    [Fact]
    public async Task CompleteTimeout_ConfirmsSameOperationWithoutReplayingWrite()
    {
        var operationId = Guid.NewGuid();
        var completeCalls = 0;
        var service = Service(async (request, ct) =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/scan"))
                return Json(new ScanResult { Matched = true });
            if (path.EndsWith("/complete"))
            {
                completeCalls++;
                throw new HttpRequestException("connection lost");
            }

            return Json(new MobileTask
            {
                TaskNo = "MOV-2",
                Status = 2,
                CompletionOperationId = operationId
            });
        });

        var result = await service.CompleteAsync(
            Mobile("MOV-2"), operationId, 5);

        Assert.Equal(2, result.Status);
        Assert.Equal(operationId, result.CompletionOperationId);
        Assert.Equal(1, completeCalls);
    }

    [Fact]
    public async Task CompleteTimeout_WithPendingState_ReturnsExplicitRecovery()
    {
        var operationId = Guid.NewGuid();
        var completeCalls = 0;
        var service = Service(async (request, ct) =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/scan"))
                return Json(new ScanResult { Matched = true });
            if (path.EndsWith("/complete"))
            {
                completeCalls++;
                throw new OperationCanceledException("simulated timeout");
            }

            return Json(new MobileTask
            {
                TaskNo = "MOV-3",
                Status = 1
            });
        });

        var error = await Assert.ThrowsAsync<RequestOutcomeUnknownException>(
            () => service.CompleteAsync(
                Mobile("MOV-3"), operationId, 5));

        Assert.Equal("MOV-3", error.TaskNo);
        Assert.Equal("complete", error.Command);
        Assert.Equal(operationId, error.OperationId);
        Assert.Equal(
            "RELOAD_TASK_STATE_THEN_RETRY_SAME_OPERATION",
            error.RecoveryAction);
        Assert.Equal(1, completeCalls);
    }

    [Fact]
    public async Task ScanTimeout_PreservesClientScanNumberForManualRetry()
    {
        const string scanNo = "device-7-scan-42";
        var scanCalls = 0;
        var service = Service(async (request, ct) =>
        {
            scanCalls++;
            if (scanCalls == 1)
                throw new OperationCanceledException("simulated timeout");
            return Json(new ScanResult
            {
                TaskNo = "MOV-4",
                Step = "Product",
                Matched = true,
                RecoveryAction = "CONTINUE"
            });
        });

        var error = await Assert.ThrowsAsync<RequestOutcomeUnknownException>(
            () => service.ScanAsync(
                Mobile("MOV-4"), "Product", "P-100", scanNo));

        Assert.Equal("scan", error.Command);
        Assert.Equal(scanNo, error.ClientScanNo);
        Assert.Equal(
            "RESCAN_WITH_SAME_CLIENT_SCAN_NO",
            error.RecoveryAction);
        Assert.Equal(1, scanCalls);

        var retry = await service.ScanAsync(
            Mobile("MOV-4"), "Product", "P-100", error.ClientScanNo);

        Assert.True(retry.Matched);
        Assert.Equal(2, scanCalls);
    }

    [Fact]
    public async Task CallerCancellation_IsNotReportedAsUnknownOutcome()
    {
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        var service = Service((request, ct) =>
            Task.FromCanceled<HttpResponseMessage>(ct));

        var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => service.ClaimAsync(
                Mobile("MOV-5"), cancelled.Token));

        Assert.IsNotType<RequestOutcomeUnknownException>(error);
    }

    private static WmsTaskService Service(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send,
        string userName = "operator")
    {
        var client = new HttpClient(new DelegateHandler(send))
        {
            BaseAddress = new Uri("https://cp6.test/")
        };
        return new WmsTaskService(
            new SingleClientFactory(client),
            new ClientOptions
            {
                ApiBaseAddress = client.BaseAddress,
                Platform = "android",
                Context = new ClientContext
                {
                    ClientKind = "Android",
                    DeviceId = "device-7",
                    AppVersion = "1.0.0"
                }
            },
            new FixedSession(userName));
    }

    private static MobileTask Mobile(string taskNo)
        => new()
        {
            TaskNo = taskNo,
            Status = 1,
            AssignedTo = "operator",
            ToLocationCd = "B-02",
            Qty = 5,
            ExecutionVersion = 1,
            RowVersion = Convert.ToBase64String([1])
        };

    private static HttpResponseMessage Json<T>(T value)
        => new(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(value)
        };

    private sealed class DelegateHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
            => send(request, cancellationToken);
    }

    private sealed class SingleClientFactory(HttpClient client)
        : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class FixedSession(string userName)
        : IClientSessionService
    {
        public event EventHandler<TokenSession?>? SessionChanged
        {
            add { }
            remove { }
        }
        public TokenSession? Current { get; } = new()
        {
            AccessToken = "access",
            RefreshToken = "refresh",
            Profile = new ClientProfile { UserName = userName }
        };
        public string? AccessToken => Current?.AccessToken;
        public Task<bool> RestoreAsync(CancellationToken ct = default)
            => Task.FromResult(true);
        public Task<NativeAuthResult> LoginAsync(
            string userName,
            string password,
            string? tenantCode,
            CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<NativeAuthResult> QuickSwitchAsync(
            string tenantCode,
            string badgeNo,
            string pin,
            CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<TwoFactorSetup> SetupTwoFactorAsync(
            string challenge,
            CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task RequestEmailOtpAsync(
            string challenge,
            CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<NativeAuthResult> VerifyTwoFactorAsync(
            string challenge,
            string code,
            string? method,
            bool enroll,
            CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task AdoptAsync(
            TokenSession session,
            CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task RefreshMergedAsync(
            string? observedAccessToken = null,
            CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task LogoutAsync(CancellationToken ct = default)
            => throw new NotSupportedException();
    }
}
