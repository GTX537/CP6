using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CP6.Client.Api;
using CP6.Client.Core;
using Microsoft.Extensions.DependencyInjection;

namespace CP6.Client.Tests;

public sealed class DeviceAndMoveRecoveryTests
{
    [Fact]
    public async Task Restart_reads_activation_by_original_operation_without_repeating_registration()
    {
        using var directory = new TestDirectory();
        var origin = new Uri("https://cp6.test/");
        var operation = Guid.NewGuid();
        var transport = new ActivationHandler(operation);
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IDeviceRequestSigner, RecordingDeviceSigner>();
        services.AddCp6ClientCore(new ClientOptions
        {
            ApiBaseAddress = origin,
            ActivationStateDirectory = directory.Path,
            Context = new ClientContext { ClientKind = "Windows", DeviceId = "registered", AppVersion = "1.0.0" },
        });
        services.AddHttpClient(ClientServiceCollectionExtensions.RawClient).ConfigurePrimaryHttpMessageHandler(() => transport);
        await using (var first = services.BuildServiceProvider())
            await Assert.ThrowsAsync<InvalidOperationException>(() => first.GetRequiredService<NativeDeviceActivationService>()
                .ActivateAsync(origin, "demo", "synthetic-no-storage", operation));

        transport.AllowRead = true;
        await using var second = services.BuildServiceProvider();
        var activation = second.GetRequiredService<NativeDeviceActivationService>();
        var result = await activation.ActivateAsync(origin, "demo", "synthetic-no-storage", operation);
        var context = await activation.AuthenticationContextAsync();
        Assert.Equal("registered", result.DeviceId);
        Assert.Equal("registered-device-v1", context.RequiredGate);
        Assert.Equal(operation, context.DeviceBinding?.ActivationOperationId);
        Assert.Equal(1, transport.Registrations);
        Assert.Equal(2, transport.StatusReads);
        Assert.All(Directory.GetFiles(directory.Path), path => Assert.DoesNotContain("synthetic-no-storage", File.ReadAllText(path)));
    }

    [Fact]
    public async Task New_operator_cannot_load_or_remove_previous_draft_and_original_operator_reads_same_result()
    {
        using var directory = new TestDirectory();
        var origin = new Uri("https://cp6.test/");
        var tenant = Guid.NewGuid();
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var operation = Guid.NewGuid();
        var task = new MobileTask { TaskNo = "MOVE-RECOVERY", WarehouseCd = "W1", AssignedTo = "first", Status = 1, ExecutionVersion = 7 };
        var owner = new MoveOwnerHandler(task, operation);
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IRefreshTokenStore, InMemoryRefresh>();
        services.AddCp6ClientCore(new ClientOptions
        {
            ApiBaseAddress = origin,
            Context = new ClientContext { ClientKind = "Android", DeviceId = "shared", AppVersion = "1.0.0" },
        });
        services.AddHttpClient(ClientServiceCollectionExtensions.AuthenticatedClient).ConfigurePrimaryHttpMessageHandler(() => owner);
        await using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<ClientAccessGate>().Update(new ClientUpgradeDecision { BusinessAllowed = true });
        var sessions = provider.GetRequiredService<IClientSessionService>();
        var tasks = provider.GetRequiredService<WmsTaskService>();
        var store = new FileOfflineMoveProgressStore(directory.Path);
        await sessions.AdoptAsync(Session(first, "first"));
        var scope = tasks.CaptureScope();
        var stored = new OfflineMoveProgress { Task = task, CompletionOperationId = operation, CompletionAttempted = true,
            Step = MoveScanStep.ReadyToComplete, ConfirmedQuantity = 1m, PartialReason = "原输入",
            OfflineScans = [new("scan-original", "SourceLocation", "FROM")] };
        await store.WriteAsync(scope, stored);

        var staleOwner = tasks.InScope(scope);
        await sessions.AdoptAsync(Session(second, "second"));
        var replacement = tasks.CaptureScope();
        Assert.Null(await store.ReadAsync(replacement, task.TaskNo));
        Assert.Null(await store.ReadAsync(scope with { TenantId = Guid.NewGuid() }, task.TaskNo));
        Assert.Null(await store.ReadAsync(scope with { DeviceId = "another-device" }, task.TaskNo));
        await store.ClearAsync(replacement, task.TaskNo, operation);
        await store.ClearAsync(scope, task.TaskNo, Guid.NewGuid());
        await Assert.ThrowsAsync<ClientInteractionChangedException>(() => staleOwner.GetAsync(task.TaskNo));
        Assert.Equal(0, owner.Reads);

        await sessions.AdoptAsync(Session(first, "first"));
        var renewed = tasks.CaptureScope();
        var recovered = await store.ReadAsync(renewed, task.TaskNo);
        Assert.NotNull(recovered);
        Assert.Equal(operation, recovered.CompletionOperationId);
        Assert.Equal("scan-original", Assert.Single(recovered.OfflineScans).ClientScanNo);
        Assert.Equal("原输入", recovered.PartialReason);
        var current = await tasks.InScope(renewed).GetAsync(recovered.Task.TaskNo);
        Assert.True(WmsTaskService.IsOriginalCompletion(current, recovered.CompletionOperationId));
        Assert.False(tasks.CanResumeMove(recovered.Task, current));
        Assert.Equal(1, owner.Reads);
        Assert.Equal(0, owner.Writes);

        TokenSession Session(Guid actor, string name)
        {
            var raw = Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(new { sub = actor, tenant_id = tenant }))
                .TrimEnd('=').Replace('+', '-').Replace('/', '_');
            return new TokenSession { AccessToken = "header." + raw + ".test", RefreshToken = "refresh-" + name,
                Profile = new ClientProfile { UserName = name } };
        }
    }

    [Fact]
    public async Task Authorized_original_owner_reuses_offline_scan_and_reads_the_one_committed_result()
    {
        using var directory = new TestDirectory();
        var operation = Guid.NewGuid();
        var task = new MobileTask { TaskNo = "MOVE-RESUME", WarehouseCd = "W1", AssignedTo = "operator", Status = 1,
            ExecutionVersion = 7, ToLocationCd = "TO", Qty = 1m };
        var owner = new ResumableMoveHandler(task, operation);
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IRefreshTokenStore, InMemoryRefresh>();
        services.AddCp6ClientCore(new ClientOptions
        {
            ApiBaseAddress = new Uri("https://cp6.test/"),
            Context = new ClientContext { ClientKind = "Android", DeviceId = "shared", AppVersion = "1.0.0" },
        });
        services.AddHttpClient(ClientServiceCollectionExtensions.AuthenticatedClient).ConfigurePrimaryHttpMessageHandler(() => owner);
        await using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<ClientAccessGate>().Update(new ClientUpgradeDecision { BusinessAllowed = true });
        var sessions = provider.GetRequiredService<IClientSessionService>();
        var tasks = provider.GetRequiredService<WmsTaskService>();
        var actor = Guid.NewGuid();
        var tenant = Guid.NewGuid();
        var raw = Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(new { sub = actor, tenant_id = tenant }))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
        TokenSession Session() => new() { AccessToken = "header." + raw + ".test", RefreshToken = "refresh-demo",
            Profile = new ClientProfile { UserName = "operator" } };

        await sessions.AdoptAsync(Session());
        var store = new FileOfflineMoveProgressStore(directory.Path);
        await store.WriteAsync(tasks.CaptureScope(), new OfflineMoveProgress
        {
            Task = task, CompletionOperationId = operation, ConfirmedQuantity = 1m,
            OfflineScans = [new("original-offline-scan", "SourceLocation", "FROM")],
        });
        await sessions.AdoptAsync(Session());
        var renewedScope = tasks.CaptureScope();
        var resumed = await new FileOfflineMoveProgressStore(directory.Path).ReadAsync(renewedScope, task.TaskNo);
        Assert.NotNull(resumed);
        var current = await tasks.InScope(renewedScope).GetAsync(resumed.Task.TaskNo);
        Assert.True(tasks.CanResumeMove(resumed.Task, current));
        foreach (var queued in resumed.OfflineScans)
            Assert.True((await tasks.InScope(renewedScope).ScanAsync(current, queued.Step, queued.Value, queued.ClientScanNo)).Matched);
        var completed = await tasks.InScope(renewedScope).CompleteAsync(current, resumed.CompletionOperationId, resumed.ConfirmedQuantity);
        Assert.True(WmsTaskService.IsOriginalCompletion(completed, operation));
        Assert.Equal(new[] { "original-offline-scan", $"qty-{operation:N}" }, owner.ScanKeys);
        Assert.Equal(1, owner.Completions);
        Assert.Equal(2, owner.Reads);
    }

    private sealed class ResumableMoveHandler(MobileTask task, Guid operation) : HttpMessageHandler
    {
        public List<string> ScanKeys { get; } = [];
        public int Completions { get; private set; }
        public int Reads { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (request.Method == HttpMethod.Get)
            {
                Reads++;
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(task) };
            }

            if (path.EndsWith("/scan", StringComparison.Ordinal))
            {
                var scan = await request.Content!.ReadFromJsonAsync<ScanRequest>(ct);
                ScanKeys.Add(scan!.ClientScanNo);
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new ScanResult { Matched = true }) };
            }

            Assert.EndsWith("/complete", path);
            var input = await request.Content!.ReadFromJsonAsync<CompleteMoveRequest>(ct);
            Assert.Equal(operation, input!.OperationId);
            Assert.Equal(task.ExecutionVersion, input.ExecutionVersion);
            Completions++;
            task.Status = 2;
            task.CompletionOperationId = operation;
            throw new HttpRequestException("模拟原作业已提交，确认丢失。");
        }
    }

    private sealed class ActivationHandler(Guid operation) : HttpMessageHandler
    {
        public int Registrations { get; private set; }
        public int StatusReads { get; private set; }
        public bool AllowRead { get; set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var body = await request.Content!.ReadAsStringAsync(ct);
            using var requestBody = JsonDocument.Parse(body);
            Assert.Equal(operation, requestBody.RootElement.GetProperty("activationOperationId").GetGuid());
            if (request.RequestUri!.AbsolutePath.EndsWith("/activate", StringComparison.Ordinal))
            {
                Registrations++;
                throw new HttpRequestException("模拟服务已提交，传输确认丢失。");
            }
            Assert.EndsWith("/activate/status", request.RequestUri.AbsolutePath);
            Assert.False(requestBody.RootElement.TryGetProperty("activationToken", out _));
            Assert.DoesNotContain("synthetic-no-storage", body);
            StatusReads++;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(AllowRead
                    ? new DeviceActivationRecovery("registered", new() { DeviceId = "registered", TenantCode = "demo" })
                    : new DeviceActivationRecovery("unavailable", null)),
            };
        }
    }

    private sealed class MoveOwnerHandler(MobileTask task, Guid completion) : HttpMessageHandler
    {
        public int Reads { get; private set; }
        public int Writes { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (request.Method != HttpMethod.Get) Writes++;
            Assert.Equal(HttpMethod.Get, request.Method);
            Reads++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new MobileTask
                {
                    TaskNo = task.TaskNo, AssignedTo = task.AssignedTo, WarehouseCd = task.WarehouseCd,
                    Status = 2, CompletionOperationId = completion, ExecutionVersion = task.ExecutionVersion,
                }),
            });
        }
    }

    private sealed class RecordingDeviceSigner : IDeviceRequestSigner
    {
        public Task<string> GetOrCreatePublicKeyAsync(CancellationToken ct = default) => Task.FromResult("test-key");
        public Task<string> SignAsync(byte[] payload, CancellationToken ct = default) => Task.FromResult("test-proof");
    }

    private sealed class InMemoryRefresh : IRefreshTokenStore
    {
        public Task<string?> ReadAsync(CancellationToken ct = default) => Task.FromResult<string?>(null);
        public Task WriteAsync(string refreshToken, CancellationToken ct = default) => Task.CompletedTask;
        public Task ClearAsync(CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class TestDirectory : IDisposable
    {
        public string Path { get; } = Directory.CreateTempSubdirectory("cp6-client-recovery-").FullName;
        public void Dispose()
        {
            foreach (var path in Directory.GetFiles(Path)) File.Delete(path);
            Directory.Delete(Path);
        }
    }
}
