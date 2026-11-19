using CP6.Client.Api;
using CP6.Client.Core;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
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
    [InlineData("1. 2.0")]
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
    public void DoesNotOfferAnOlderPackageWhenThePolicyFlagDrifts()
    {
        var bootstrap = Bootstrap("2.0.0", "1.2.0", "1.1.0", upgradeRequired: true);
        bootstrap.DownloadUrl = "https://updates.cp6.example/CP6.Mobile.apk";
        bootstrap.Sha256 = ValidHash;

        var decision = ClientUpgradeService.Evaluate(bootstrap, Options("android", "2.0.0"));

        Assert.False(decision.CanDownload);
        Assert.False(decision.BusinessAllowed);
        Assert.Equal("E-CLIENT-VERSION-POLICY", decision.ErrorCode);
    }

    [Fact]
    public void DoesNotOfferAnUpdate_WhenPolicyWasReadOverHttp()
    {
        var bootstrap = Bootstrap("1.0.0", "2.0.0", "2.0.0", upgradeRequired: true);
        bootstrap.DownloadUrl = "https://updates.cp6.example/CP6.Mobile.apk";
        bootstrap.Sha256 = ValidHash;
        var options = Options("android", "1.0.0");
        options.ApiBaseAddress = new Uri("http://api.cp6.example/");

        var decision = ClientUpgradeService.Evaluate(bootstrap, options);

        Assert.True(decision.UpgradeRequired);
        Assert.False(decision.CanDownload);
        Assert.Equal("E-CLIENT-UPGRADE-ORIGIN", decision.ErrorCode);
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
    public void RejectsAValidVersionFromAnotherChannel()
    {
        var bootstrap = Bootstrap("1.2.0", "1.2.0", "1.0.0");
        bootstrap.Channel = "pilot";
        var decision = ClientUpgradeService.Evaluate(bootstrap, Options("android", "1.2.0"));

        Assert.False(decision.BusinessAllowed);
        Assert.False(decision.CanDownload);
        Assert.Equal("E-CLIENT-CHANNEL-MISMATCH", decision.ErrorCode);
    }

    [Fact]
    public async Task RejectsManuallyConstructedUnsafeDownloadDecision()
    {
        var service = new ClientUpgradeService(
            null!,
            Options("android", "1.0.0"),
            null!,
            new ClientAccessGate(),
            NullLogger<ClientUpgradeService>.Instance);
        var decision = new ClientUpgradeDecision
        {
            UpgradeRequired = true,
            DownloadUri = new Uri("https://updates.cp6.example/CP6.Mobile.apk"),
            Sha256 = ValidHash,
            PublisherSha256 = ValidHash
        };

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.OpenDownloadAsync(decision));

        Assert.Equal("E-CLIENT-UPGRADE-METADATA", error.Message);
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
        using var stage = new UpgradeStaging();
        var installer = new RecordingInstaller();
        await using var provider = UpgradeServices(server, installer, stage.Path);
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
        Assert.Equal("2.0.0", installer.Version);
        Assert.Equal(ValidHash, installer.Publisher);
        Assert.Equal(StartupContractHandler.PackageBytes, File.ReadAllBytes(installer.PackagePath!));
        Assert.False((await upgrades.CheckAccessAsync()).BusinessAllowed);

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

    [Theory]
    [InlineData(HttpStatusCode.OK, "E-CLIENT-UPGRADE-HASH")]
    [InlineData(HttpStatusCode.Redirect, "E-CLIENT-UPGRADE-DOWNLOAD")]
    public async Task InvalidDownloadedArtifactCannotReachTheInstaller(HttpStatusCode status, string code)
    {
        using var stage = new UpgradeStaging();
        var installer = new RecordingInstaller();
        var server = new StartupContractHandler
        {
            MinimumVersion = "2.0.0", LatestVersion = "2.0.0", DownloadStatus = status, AlterDownload = true
        };
        await using var provider = UpgradeServices(server, installer, stage.Path);
        var upgrades = provider.GetRequiredService<ClientUpgradeService>();
        var decision = await upgrades.CheckAccessAsync();

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => upgrades.OpenDownloadAsync(decision));

        Assert.Equal(code, error.Message);
        Assert.Null(installer.PackagePath);
        Assert.True(!Directory.Exists(stage.Path) || Directory.GetFiles(stage.Path).Length == 0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task WindowsDescriptorRequiresAnIndependentApprovedPackageDigest(bool changePackage)
    {
        using var stage = new UpgradeStaging();
        var installer = new RecordingInstaller();
        var server = new StartupContractHandler
        {
            MinimumVersion = "2.0.0", LatestVersion = "2.0.0", DescribeWindowsPackage = true,
            AlterDownload = changePackage
        };
        await using var provider = UpgradeServices(server, installer, stage.Path);
        var upgrades = provider.GetRequiredService<ClientUpgradeService>();
        var release = await upgrades.CheckAccessAsync();

        if (changePackage)
        {
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => upgrades.OpenDownloadAsync(release));
            Assert.Equal("E-CLIENT-UPGRADE-HASH", error.Message);
            Assert.Null(installer.PackagePath);
        }
        else
        {
            await upgrades.OpenDownloadAsync(release);
            Assert.EndsWith(".msix", installer.PackagePath);
            Assert.Equal(File.ReadAllBytes(installer.PackagePath!), StartupContractHandler.PackageBytes);
        }
        Assert.Equal(new[] { "/CP6.Desktop.appinstaller", "/CP6.Desktop.msix" }, server.Downloads);
        Assert.DoesNotContain(Directory.GetFiles(stage.Path), path => path.EndsWith(".appinstaller"));
    }

    [Fact]
    public async Task MandatoryUpdateAllowsOnlyScopedOriginalTaskLookup()
    {
        var gate = new ClientAccessGate();
        gate.Update(new ClientUpgradeDecision { UpgradeRequired = true });
        using var handler = new ClientBusinessAccessHandler(gate) { InnerHandler = new OriginalLookupHandler() };
        using var transport = new HttpClient(handler) { BaseAddress = new Uri("https://api.cp6.example/") };
        var scoped = new Cp6ApiClient(transport, new(transport.BaseAddress, Guid.NewGuid(), Guid.NewGuid(), "test-device", 1));

        Assert.Equal("MOVE-1", (await scoped.GetOriginalTaskAsync("MOVE-1")).TaskNo);
        await Assert.ThrowsAsync<ClientBusinessAccessBlockedException>(() => scoped.GetTaskAsync("MOVE-1"));
        await Assert.ThrowsAsync<ClientBusinessAccessBlockedException>(() => scoped.CompleteMoveAsync("MOVE-1", new()));
        Assert.Throws<InvalidOperationException>(() => { _ = new Cp6ApiClient(transport).GetOriginalTaskAsync("MOVE-1"); });
    }

    private static ServiceProvider UpgradeServices(StartupContractHandler server, RecordingInstaller installer, string stage)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IRefreshTokenStore, MemoryTokenStore>();
        services.AddSingleton<ISystemBrowser, RecordingBrowser>();
        services.AddSingleton<IClientPackageInstaller>(installer);
        services.AddCp6ClientCore(Options("windows", "1.0.0", stage));
        foreach (var name in new[] { ClientServiceCollectionExtensions.BootstrapClient, ClientServiceCollectionExtensions.RawClient,
            ClientServiceCollectionExtensions.AuthenticatedClient, ClientServiceCollectionExtensions.UpgradeDownloadClient })
            services.AddHttpClient(name).ConfigurePrimaryHttpMessageHandler(() => server);
        return services.BuildServiceProvider();
    }

    private static ClientOptions Options(string platform, string version, string? stage = null) => new()
    {
        ApiBaseAddress = new Uri("https://api.cp6.example/"),
        Platform = platform,
        UpgradeStagingDirectory = stage ?? System.IO.Path.GetTempPath(),
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
        PublisherSha256 = ValidHash,
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

    private sealed class OriginalLookupHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            { Content = JsonContent.Create(new MobileTask { TaskNo = "MOVE-1" }) });
    }

    private sealed class RecordingInstaller : IClientPackageInstaller
    {
        public string? PackagePath { get; private set; }
        public string? Version { get; private set; }
        public string? Publisher { get; private set; }

        public Task OpenAsync(string packagePath, string version, string publisherSha256, CancellationToken ct)
        {
            PackagePath = packagePath;
            Version = version;
            Publisher = publisherSha256;
            return Task.CompletedTask;
        }
    }

    private sealed class UpgradeStaging : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"cp6-upgrade-{Guid.NewGuid():N}");

        public void Dispose()
        {
            if (!Directory.Exists(Path)) return;
            foreach (var file in Directory.GetFiles(Path)) File.Delete(file);
            Directory.Delete(Path);
        }
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
        public static byte[] PackageBytes { get; } = "WP02-20 模拟包"u8.ToArray();
        private static readonly byte[] Descriptor = """
            <AppInstaller xmlns="http://schemas.microsoft.com/appx/appinstaller/2018">
                <MainPackage Uri="https://updates.cp6.example/CP6.Desktop.msix" />
            </AppInstaller>
            """u8.ToArray();
        public HttpStatusCode DownloadStatus { get; init; } = HttpStatusCode.OK;
        public bool AlterDownload { get; init; }
        public bool DescribeWindowsPackage { get; init; }
        public List<string> Downloads { get; } = [];
        public string MinimumVersion { get; set; } = "1.0.0";
        public string LatestVersion { get; set; } = "1.0.0";
        public int BusinessCalls { get; private set; }
        public string? LastPlatform { get; private set; }
        public string? LastCurrentVersion { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (request.RequestUri!.Host == "updates.cp6.example")
            {
                Downloads.Add(request.RequestUri.AbsolutePath);
                return Task.FromResult(new HttpResponseMessage(DownloadStatus)
                {
                    RequestMessage = request,
                    Content = new ByteArrayContent(request.RequestUri.AbsolutePath.EndsWith(".appinstaller", StringComparison.Ordinal)
                        ? Descriptor
                        : AlterDownload ? [0] : PackageBytes)
                });
            }

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
                        DownloadUrl = DescribeWindowsPackage
                            ? "https://updates.cp6.example/CP6.Desktop.appinstaller"
                            : "https://updates.cp6.example/CP6.Desktop.msix",
                        Sha256 = Convert.ToHexString(SHA256.HashData(DescribeWindowsPackage ? Descriptor : PackageBytes)),
                        PackageSha256 = DescribeWindowsPackage ? Convert.ToHexString(SHA256.HashData(PackageBytes)) : null,
                        PublisherSha256 = ValidHash,
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
