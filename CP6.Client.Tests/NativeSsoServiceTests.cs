using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CP6.Client.Api;
using CP6.Client.Core;

namespace CP6.Client.Tests;

public sealed class NativeSsoServiceTests
{
    [Fact]
    public async Task Start_UsesPlatformOwnedRedirectUri()
    {
        var transport = new SsoHandler();
        var browser = new RecordingBrowser();
        var service = Create(
            platform: "windows",
            transport,
            browser,
            new MemoryPkceVerifierStore(),
            new RecordingSessionService());

        await service.StartAsync("TENANT-A");

        Assert.Equal(
            new Uri("https://identity.cp6.example/authorize"),
            browser.Opened);
        Assert.NotNull(transport.StartRequest);
        Assert.Equal(
            "cp6-desktop://auth/callback",
            transport.StartRequest!.RedirectUri);
        Assert.Equal("Windows", transport.StartRequest.Client.ClientKind);
    }

    [Fact]
    public async Task ForeignCallback_IsRejectedWithoutConsumingVerifier()
    {
        var verifierStore = new MemoryPkceVerifierStore();
        await verifierStore.WriteAsync("stored-verifier");
        var transport = new SsoHandler();
        var service = Create(
            platform: "windows",
            transport,
            new RecordingBrowser(),
            verifierStore,
            new RecordingSessionService());

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CompleteAsync(new Uri(
                $"cp6-mobile://auth/callback?grantCode={GrantCode()}")));

        Assert.Equal("E-CLIENT-SSO-CALLBACK", error.Message);
        Assert.Equal(
            "stored-verifier",
            await verifierStore.TakeAsync());
        Assert.Equal(0, transport.ExchangeCalls);
    }

    [Theory]
    [InlineData("cp6-desktop://auth/other?grantCode={0}")]
    [InlineData("cp6-desktop://auth/callback?error=denied")]
    [InlineData("cp6-desktop://auth/callback?grantCode={0}&grantCode={0}")]
    [InlineData("cp6-desktop://auth/callback?grantCode=not-valid!")]
    public async Task MalformedCallback_IsRejectedBeforePkceConsumption(
        string callbackTemplate)
    {
        var verifierStore = new MemoryPkceVerifierStore();
        await verifierStore.WriteAsync("stored-verifier");
        var service = Create(
            platform: "windows",
            new SsoHandler(),
            new RecordingBrowser(),
            verifierStore,
            new RecordingSessionService());
        var callback = string.Format(
            callbackTemplate,
            GrantCode());

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CompleteAsync(new Uri(callback)));

        Assert.Equal("E-CLIENT-SSO-CALLBACK", error.Message);
        Assert.Equal(
            "stored-verifier",
            await verifierStore.TakeAsync());
    }

    [Fact]
    public async Task ValidCallback_ExchangesAndAdoptsSession()
    {
        var verifierStore = new MemoryPkceVerifierStore();
        await verifierStore.WriteAsync("stored-verifier");
        var sessions = new RecordingSessionService();
        var transport = new SsoHandler();
        var service = Create(
            platform: "android",
            transport,
            new RecordingBrowser(),
            verifierStore,
            sessions);

        var result = await service.CompleteAsync(new Uri(
            $"cp6-mobile://auth/callback?grantCode={GrantCode()}"));

        Assert.Equal("authenticated", result.State);
        Assert.NotNull(sessions.Adopted);
        Assert.Null(await verifierStore.TakeAsync());
        Assert.Equal(1, transport.ExchangeCalls);
        Assert.Equal(
            "stored-verifier",
            transport.ExchangeRequest!.CodeVerifier);
    }

    private static NativeSsoService Create(
        string platform,
        HttpMessageHandler handler,
        ISystemBrowser browser,
        IPkceVerifierStore verifiers,
        IClientSessionService sessions)
    {
        var options = new ClientOptions
        {
            ApiBaseAddress = new Uri("https://api.cp6.example/"),
            Platform = platform,
            Context = new ClientContext
            {
                ClientKind = platform.Equals(
                    "windows",
                    StringComparison.OrdinalIgnoreCase)
                    ? "Windows"
                    : "Android",
                DeviceId = "device-1",
                AppVersion = "1.0.0",
                PlatformVersion = "test"
            }
        };
        var client = new HttpClient(handler)
        {
            BaseAddress = options.ApiBaseAddress
        };
        return new NativeSsoService(
            new FixedHttpClientFactory(client),
            options,
            browser,
            sessions,
            verifiers);
    }

    private static string GrantCode() => new('a', 43);

    private sealed class SsoHandler : HttpMessageHandler
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

        public NativeSsoStartRequest? StartRequest { get; private set; }
        public NativeSsoExchangeRequest? ExchangeRequest { get; private set; }
        public int ExchangeCalls { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (request.RequestUri!.AbsolutePath.EndsWith(
                    "/sso/start",
                    StringComparison.Ordinal))
            {
                StartRequest = JsonSerializer.Deserialize<NativeSsoStartRequest>(
                    await request.Content!.ReadAsStringAsync(
                        cancellationToken),
                    JsonOptions);
                return Json(new NativeSsoStartResponse
                {
                    AuthorizeUrl =
                        "https://identity.cp6.example/authorize"
                });
            }

            if (request.RequestUri.AbsolutePath.EndsWith(
                    "/sso/exchange",
                    StringComparison.Ordinal))
            {
                ExchangeCalls++;
                ExchangeRequest =
                    JsonSerializer.Deserialize<NativeSsoExchangeRequest>(
                        await request.Content!.ReadAsStringAsync(
                            cancellationToken),
                        JsonOptions);
                return Json(new NativeAuthResult
                {
                    State = "authenticated",
                    Session = new TokenSession
                    {
                        AccessToken = "access",
                        RefreshToken = "refresh",
                        Profile = new ClientProfile
                        {
                            UserName = "operator"
                        }
                    }
                });
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }

        private static HttpResponseMessage Json<T>(T value) =>
            new(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(value)
            };
    }

    private sealed class FixedHttpClientFactory(HttpClient client)
        : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class RecordingBrowser : ISystemBrowser
    {
        public Uri? Opened { get; private set; }

        public Task OpenAsync(
            Uri uri,
            CancellationToken ct = default)
        {
            Opened = uri;
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingSessionService : IClientSessionService
    {
        public event EventHandler<TokenSession?>? SessionChanged;
        public TokenSession? Adopted { get; private set; }
        public TokenSession? Current => Adopted;
        public string? AccessToken => Adopted?.AccessToken;

        public Task AdoptAsync(
            TokenSession session,
            CancellationToken ct = default)
        {
            Adopted = session;
            SessionChanged?.Invoke(this, session);
            return Task.CompletedTask;
        }

        public Task<bool> RestoreAsync(CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<NativeAuthResult> LoginAsync(
            string userName,
            string password,
            string? tenantCode,
            CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<NativeAuthResult> QuickSwitchAsync(
            string tenantCode,
            string badgeNo,
            string pin,
            CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<TwoFactorSetup> SetupTwoFactorAsync(
            string challenge,
            CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task RequestEmailOtpAsync(
            string challenge,
            CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<NativeAuthResult> VerifyTwoFactorAsync(
            string challenge,
            string code,
            string? method,
            bool enroll,
            CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task RefreshMergedAsync(
            string? observedAccessToken = null,
            CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task LogoutAsync(CancellationToken ct = default) =>
            throw new NotSupportedException();
    }
}
