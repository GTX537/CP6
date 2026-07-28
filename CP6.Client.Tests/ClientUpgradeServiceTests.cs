using CP6.Client.Api;
using CP6.Client.Core;
using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace CP6.Client.Tests;

public sealed class ClientUpgradeServiceTests
{
    private const string ValidHash =
        "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    [Fact]
    public void AllowsBusiness_WhenCurrentVersionMeetsMinimum()
    {
        var decision = ClientUpgradeService.Evaluate(
            Bootstrap(current: "1.2.0", latest: "1.2.0", minimum: "1.1.0"),
            Options("android", "1.2.0"));

        Assert.True(decision.BusinessAllowed);
        Assert.False(decision.UpgradeRequired);
        Assert.False(decision.CanDownload);
    }

    [Fact]
    public void EnforcesMinimumLocally_WhenServerFlagIsIncorrect()
    {
        var bootstrap = Bootstrap(
            current: "1.0.0",
            latest: "1.2.0",
            minimum: "1.1.0",
            upgradeRequired: false);
        bootstrap.DownloadUrl = "https://updates.cp6.example/CP6.Mobile.apk";
        bootstrap.Sha256 = ValidHash;

        var decision = ClientUpgradeService.Evaluate(
            bootstrap,
            Options("android", "1.0.0"));

        Assert.False(decision.BusinessAllowed);
        Assert.True(decision.UpgradeRequired);
        Assert.True(decision.CanDownload);
        Assert.Equal("https", decision.DownloadUri!.Scheme);
    }

    [Fact]
    public void EnforcesReleaseMinimum_ForPreReleaseClient()
    {
        var bootstrap = Bootstrap(
            current: "1.0.0-beta.10",
            latest: "1.0.0",
            minimum: "1.0.0",
            upgradeRequired: false);
        bootstrap.DownloadUrl =
            "https://updates.cp6.example/CP6.Mobile.apk";
        bootstrap.Sha256 = ValidHash;

        var decision = ClientUpgradeService.Evaluate(
            bootstrap,
            Options("android", "1.0.0-beta.10"));

        Assert.False(decision.BusinessAllowed);
        Assert.True(decision.UpgradeRequired);
    }

    [Fact]
    public void ComparesNumericPreReleaseIdentifiersSemantically()
    {
        var bootstrap = Bootstrap(
            current: "1.0.0-beta.2",
            latest: "1.0.0-beta.10",
            minimum: "1.0.0-beta.10",
            upgradeRequired: false);
        bootstrap.DownloadUrl =
            "https://updates.cp6.example/CP6.Mobile.apk";
        bootstrap.Sha256 = ValidHash;

        var decision = ClientUpgradeService.Evaluate(
            bootstrap,
            Options("android", "1.0.0-beta.2"));

        Assert.False(decision.BusinessAllowed);
        Assert.True(decision.UpgradeRequired);
    }

    [Theory]
    [InlineData("1.0")]
    [InlineData("01.0.0")]
    [InlineData("1.0.0-beta.01")]
    [InlineData("1.0.0+")]
    public void BlocksBusiness_WhenVersionIsNotValidSemVer(
        string currentVersion)
    {
        var decision = ClientUpgradeService.Evaluate(
            Bootstrap(
                current: currentVersion,
                latest: "1.0.0",
                minimum: "1.0.0"),
            Options("android", currentVersion));

        Assert.False(decision.BusinessAllowed);
        Assert.Equal(
            "E-CLIENT-BOOTSTRAP-CONTRACT",
            decision.ErrorCode);
    }

    [Theory]
    [InlineData("http://updates.cp6.example/CP6.Mobile.apk", ValidHash)]
    [InlineData("https://updates.cp6.example/CP6.Mobile.zip", ValidHash)]
    [InlineData("https://updates.cp6.example/CP6.Mobile.apk", "abcd")]
    [InlineData("https://user:pass@updates.cp6.example/CP6.Mobile.apk", ValidHash)]
    public void BlocksBusiness_WhenRequiredUpgradeMetadataIsUnsafe(
        string downloadUrl,
        string sha256)
    {
        var bootstrap = Bootstrap(
            current: "1.0.0",
            latest: "1.1.0",
            minimum: "1.1.0",
            upgradeRequired: true);
        bootstrap.DownloadUrl = downloadUrl;
        bootstrap.Sha256 = sha256;

        var decision = ClientUpgradeService.Evaluate(
            bootstrap,
            Options("android", "1.0.0"));

        Assert.False(decision.BusinessAllowed);
        Assert.True(decision.UpgradeRequired);
        Assert.False(decision.CanDownload);
        Assert.Equal("E-CLIENT-UPGRADE-METADATA", decision.ErrorCode);
    }

    [Fact]
    public void BlocksBusiness_WhenBootstrapContractDoesNotMatchClient()
    {
        var bootstrap = Bootstrap(
            current: "1.0.0",
            latest: "1.1.0",
            minimum: "1.0.0");
        bootstrap.Platform = "windows";

        var decision = ClientUpgradeService.Evaluate(
            bootstrap,
            Options("android", "1.0.0"));

        Assert.False(decision.BusinessAllowed);
        Assert.Equal("E-CLIENT-BOOTSTRAP-CONTRACT", decision.ErrorCode);
    }

    [Fact]
    public async Task AuthenticatedPipeline_BlocksUntilBootstrapAllowsBusiness()
    {
        var gate = new ClientAccessGate();
        using var handler = new ClientBusinessAccessHandler(gate)
        {
            InnerHandler = new SuccessHandler()
        };
        using var client = new HttpClient(handler);

        var blocked = await Assert.ThrowsAsync<ClientBusinessAccessBlockedException>(
            () => client.GetAsync("https://api.cp6.example/api/v2/wms/tasks"));
        Assert.Equal("E-CLIENT-BOOTSTRAP-REQUIRED", blocked.Code);

        gate.Update(new ClientUpgradeDecision { BusinessAllowed = true });
        using var response = await client.GetAsync(
            "https://api.cp6.example/api/v2/wms/tasks");
        Assert.True(response.IsSuccessStatusCode);
    }

    [Fact]
    public async Task OpensOnlyValidatedUpgradeDecision()
    {
        var browser = new RecordingBrowser();
        var service = new ClientUpgradeService(
            null!,
            Options("windows", "1.0.0"),
            browser,
            new ClientAccessGate(),
            NullLogger<ClientUpgradeService>.Instance);
        var decision = new ClientUpgradeDecision
        {
            UpgradeRequired = true,
            DownloadUri = new Uri(
                "https://updates.cp6.example/CP6.Desktop.appinstaller"),
            Sha256 = ValidHash
        };

        await service.OpenDownloadAsync(decision);

        Assert.Equal(decision.DownloadUri, browser.Opened);
    }

    [Fact]
    public async Task RejectsManuallyConstructedUnsafeDownloadDecision()
    {
        var browser = new RecordingBrowser();
        var service = new ClientUpgradeService(
            null!,
            Options("android", "1.0.0"),
            browser,
            new ClientAccessGate(),
            NullLogger<ClientUpgradeService>.Instance);
        var decision = new ClientUpgradeDecision
        {
            UpgradeRequired = true,
            DownloadUri = new Uri("http://updates.cp6.example/CP6.Mobile.apk"),
            Sha256 = ValidHash
        };

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.OpenDownloadAsync(decision));

        Assert.Equal("E-CLIENT-UPGRADE-METADATA", error.Message);
        Assert.Null(browser.Opened);
    }

    [Fact]
    public async Task CoreRegistration_ResolvesGeneratedApiAndUpgradeServices()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IRefreshTokenStore, MemoryTokenStore>();
        services.AddSingleton<ISystemBrowser, RecordingBrowser>();
        services.AddCp6ClientCore(Options("windows", "1.0.0"));

        await using var provider = services.BuildServiceProvider();

        Assert.NotNull(provider.GetRequiredService<Cp6ApiClient>());
        Assert.NotNull(provider.GetRequiredService<ClientUpgradeService>());
    }

    [Fact]
    public async Task StartupContract_FailClosesThenOpensAuthenticatedPipeline()
    {
        var server = new StartupContractHandler
        {
            MinimumVersion = "2.0.0",
            LatestVersion = "2.0.0"
        };
        var browser = new RecordingBrowser();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IRefreshTokenStore, MemoryTokenStore>();
        services.AddSingleton<ISystemBrowser>(browser);
        services.AddCp6ClientCore(Options("windows", "1.0.0"));
        services.AddHttpClient(ClientServiceCollectionExtensions.RawClient)
            .ConfigurePrimaryHttpMessageHandler(() => server);
        services.AddHttpClient(
                ClientServiceCollectionExtensions.AuthenticatedClient)
            .ConfigurePrimaryHttpMessageHandler(() => server);

        await using var provider = services.BuildServiceProvider();
        var upgrades =
            provider.GetRequiredService<ClientUpgradeService>();
        var authenticated = provider
            .GetRequiredService<IHttpClientFactory>()
            .CreateClient(
                ClientServiceCollectionExtensions.AuthenticatedClient);

        var blocked = await upgrades.CheckAccessAsync();
        Assert.True(blocked.UpgradeRequired);
        Assert.False(blocked.BusinessAllowed);
        await Assert.ThrowsAsync<ClientBusinessAccessBlockedException>(
            () => authenticated.GetAsync("api/v2/wms/tasks"));
        Assert.Equal(0, server.BusinessCalls);
        await upgrades.OpenDownloadAsync(blocked);
        Assert.Equal(blocked.DownloadUri, browser.Opened);

        server.MinimumVersion = "1.0.0";
        server.LatestVersion = "1.0.0";
        var allowed = await upgrades.CheckAccessAsync();
        using var response = await authenticated.GetAsync(
            "api/v2/wms/tasks");

        Assert.True(allowed.BusinessAllowed);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, server.BusinessCalls);
        Assert.Equal("windows", server.LastPlatform);
        Assert.Equal("1.0.0", server.LastCurrentVersion);
    }

    private static ClientOptions Options(string platform, string version) => new()
    {
        ApiBaseAddress = new Uri("https://api.cp6.example/"),
        Platform = platform,
        Context = new ClientContext
        {
            ClientKind = platform,
            DeviceId = "device-1",
            AppVersion = version,
            PlatformVersion = "test"
        }
    };

    private static ClientBootstrap Bootstrap(
        string current,
        string latest,
        string minimum,
        bool upgradeRequired = false) => new()
    {
        ApiVersion = "1",
        ServerUtc = DateTimeOffset.UtcNow,
        Platform = "android",
        CurrentVersion = current,
        LatestVersion = latest,
        MinimumVersion = minimum,
        UpgradeRequired = upgradeRequired,
        LanguageManifestVersion = "test"
    };

    private sealed class SuccessHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK));
    }

    private sealed class RecordingBrowser : ISystemBrowser
    {
        public Uri? Opened { get; private set; }

        public Task OpenAsync(Uri uri, CancellationToken ct = default)
        {
            Opened = uri;
            return Task.CompletedTask;
        }
    }

    private sealed class MemoryTokenStore : IRefreshTokenStore
    {
        public Task<string?> ReadAsync(CancellationToken ct = default) =>
            Task.FromResult<string?>(null);

        public Task WriteAsync(
            string refreshToken,
            CancellationToken ct = default) => Task.CompletedTask;

        public Task ClearAsync(CancellationToken ct = default) =>
            Task.CompletedTask;
    }

    private sealed class StartupContractHandler : HttpMessageHandler
    {
        public string MinimumVersion { get; set; } = "1.0.0";
        public string LatestVersion { get; set; } = "1.0.0";
        public int BusinessCalls { get; private set; }
        public string? LastPlatform { get; private set; }
        public string? LastCurrentVersion { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (request.RequestUri!.AbsolutePath ==
                "/api/client/bootstrap")
            {
                var query = ParseQuery(request.RequestUri.Query);
                LastPlatform = query["platform"];
                LastCurrentVersion = query["currentVersion"];
                return Task.FromResult(new HttpResponseMessage(
                    HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new ClientBootstrap
                    {
                        ApiVersion = "1",
                        ServerUtc = DateTimeOffset.UtcNow,
                        Platform = LastPlatform,
                        CurrentVersion = LastCurrentVersion,
                        LatestVersion = LatestVersion,
                        MinimumVersion = MinimumVersion,
                        UpgradeRequired =
                            ClientBootstrapService.CompareVersions(
                                LastCurrentVersion,
                                MinimumVersion) < 0,
                        DownloadUrl =
                            "https://updates.cp6.example/CP6.Desktop.msix",
                        Sha256 = ValidHash,
                        LanguageManifestVersion = "contract-test"
                    })
                });
            }

            BusinessCalls++;
            return Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK));
        }

        private static Dictionary<string, string> ParseQuery(
            string query) =>
            query.TrimStart('?')
                .Split('&', StringSplitOptions.RemoveEmptyEntries)
                .Select(part => part.Split('=', 2))
                .ToDictionary(
                    part => Uri.UnescapeDataString(part[0]),
                    part => Uri.UnescapeDataString(part[1]),
                    StringComparer.OrdinalIgnoreCase);
    }
}
