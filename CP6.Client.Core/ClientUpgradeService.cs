using CP6.Client.Api;
using Microsoft.Extensions.Logging;

namespace CP6.Client.Core;

public sealed record ClientUpgradeDecision
{
    public bool BusinessAllowed { get; init; }
    public bool UpgradeRequired { get; init; }
    public string CurrentVersion { get; init; } = string.Empty;
    public string LatestVersion { get; init; } = string.Empty;
    public string MinimumVersion { get; init; } = string.Empty;
    public Uri? DownloadUri { get; init; }
    public string? Sha256 { get; init; }
    public string? PublisherSha256 { get; init; }
    public string? PackageSha256 { get; init; }
    internal Uri? PolicySource { get; init; }
    public string? ErrorCode { get; init; }
    public bool CanDownload =>
        UpgradeRequired &&
        DownloadUri is not null &&
        !string.IsNullOrWhiteSpace(Sha256) &&
        !string.IsNullOrWhiteSpace(PublisherSha256) &&
        ErrorCode is null;

    public static ClientUpgradeDecision Unavailable(string currentVersion) => new()
    {
        BusinessAllowed = false,
        CurrentVersion = currentVersion,
        ErrorCode = "E-CLIENT-BOOTSTRAP-UNAVAILABLE"
    };
}

public sealed class ClientAccessGate
{
    private ClientUpgradeDecision? _current;

    public ClientUpgradeDecision? Current => Volatile.Read(ref _current);

    internal void Update(ClientUpgradeDecision decision) =>
        Volatile.Write(ref _current, decision);

    public void EnsureBusinessAllowed()
    {
        var decision = Current;
        if (decision?.BusinessAllowed == true)
            return;

        throw new ClientBusinessAccessBlockedException(
            decision?.ErrorCode ??
            (decision?.UpgradeRequired == true
                ? "E-CLIENT-UPGRADE-REQUIRED"
                : "E-CLIENT-BOOTSTRAP-REQUIRED"));
    }
}

public sealed class ClientBusinessAccessBlockedException(string code)
    : InvalidOperationException(code)
{
    public string Code { get; } = code;
}

public sealed class ClientBusinessAccessHandler(ClientAccessGate gate) : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        if (gate.Current?.UpgradeRequired != true || !Cp6ApiClient.IsOriginalTaskLookup(request))
            gate.EnsureBusinessAllowed();
        return base.SendAsync(request, cancellationToken);
    }
}

public sealed class ClientUpgradeService
{
    private readonly ClientBootstrapService _bootstrap;
    private readonly ClientOptions _options;
    private readonly ClientUpgradePackage _package;
    private readonly ClientAccessGate _gate;
    private readonly ILogger<ClientUpgradeService> _logger;

    public ClientUpgradeService(
        ClientBootstrapService bootstrap,
        ClientOptions options,
        ClientUpgradePackage package,
        ClientAccessGate gate,
        ILogger<ClientUpgradeService> logger)
    {
        _bootstrap = bootstrap;
        _options = options;
        _package = package;
        _gate = gate;
        _logger = logger;
    }

    public async Task<ClientUpgradeDecision> CheckAccessAsync(CancellationToken ct = default)
    {
        ClientUpgradeDecision decision;
        var source = _options.ApiBaseAddress;
        try
        {
            decision = Evaluate(await _bootstrap.CheckAsync(ct), _options);
            if (!_options.ApiBaseAddress.Equals(source))
                decision = ClientUpgradeDecision.Unavailable(_options.Context.AppVersion);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (ApiException ex) when (ex.Code is "E-CLIENT-VERSION-POLICY" or "E-CLIENT-CHANNEL-MISMATCH" or "E-CLIENT-VERSION-INVALID")
        {
            decision = ClientUpgradeDecision.Unavailable(_options.Context.AppVersion) with { ErrorCode = ex.Code };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                "Client bootstrap check failed: {ErrorType}",
                ex.GetType().Name);
            decision = ClientUpgradeDecision.Unavailable(_options.Context.AppVersion);
        }

        _gate.Update(decision);
        return decision;
    }

    public Task OpenDownloadAsync(
        ClientUpgradeDecision decision,
        CancellationToken ct = default)
    {
        if (!IsCurrentDownload(decision))
            throw new InvalidOperationException(
                decision.ErrorCode ?? "E-CLIENT-UPGRADE-METADATA");

        return _package.OpenAsync(decision, () => IsCurrentDownload(decision), ct);
    }

    private bool IsCurrentDownload(ClientUpgradeDecision decision) =>
        decision.CanDownload && _gate.Current == decision &&
        _options.ApiBaseAddress.Equals(decision.PolicySource);

    public static ClientUpgradeDecision Evaluate(
        ClientBootstrap bootstrap,
        ClientOptions options)
    {
        var currentVersion = options.Context.AppVersion;
        if (!string.Equals(bootstrap.ApiVersion, "1", StringComparison.Ordinal) ||
            !string.Equals(bootstrap.Platform, options.Platform, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(bootstrap.CurrentVersion, currentVersion, StringComparison.Ordinal) ||
            !ClientSemanticVersion.TryParse(
                currentVersion,
                out var current) ||
            !ClientSemanticVersion.TryParse(
                bootstrap.LatestVersion,
                out var latest) ||
            !ClientSemanticVersion.TryParse(
                bootstrap.MinimumVersion,
                out var minimum) ||
            minimum.CompareTo(latest) > 0)
        {
            return Blocked(
                currentVersion,
                bootstrap,
                "E-CLIENT-BOOTSTRAP-CONTRACT");
        }

        if (!string.Equals(bootstrap.Channel, options.ReleaseChannel, StringComparison.Ordinal))
            return Blocked(currentVersion, bootstrap, "E-CLIENT-CHANNEL-MISMATCH");

        var upgradeRequired =
            bootstrap.UpgradeRequired ||
            current.CompareTo(minimum) < 0;
        if (!upgradeRequired)
        {
            return new ClientUpgradeDecision
            {
                BusinessAllowed = true,
                CurrentVersion = currentVersion,
                LatestVersion = bootstrap.LatestVersion,
                MinimumVersion = bootstrap.MinimumVersion
            };
        }

        if (latest.CompareTo(current) <= 0)
            return Blocked(currentVersion, bootstrap, "E-CLIENT-VERSION-POLICY", upgradeRequired: true);

        if (IsInsecureSource(options.ApiBaseAddress))
            return Blocked(currentVersion, bootstrap, "E-CLIENT-UPGRADE-ORIGIN", upgradeRequired: true);

        if (!TryValidateDownload(
                bootstrap.DownloadUrl,
                bootstrap.Sha256,
                options.Platform,
                out var downloadUri) ||
            !IsDigest(bootstrap.PublisherSha256) ||
            Path.GetExtension(downloadUri!.AbsolutePath).Equals(".appinstaller", StringComparison.OrdinalIgnoreCase) &&
                !IsDigest(bootstrap.PackageSha256))
        {
            return Blocked(
                currentVersion,
                bootstrap,
                "E-CLIENT-UPGRADE-METADATA",
                upgradeRequired: true);
        }

        return new ClientUpgradeDecision
        {
            BusinessAllowed = false,
            UpgradeRequired = true,
            CurrentVersion = currentVersion,
            LatestVersion = bootstrap.LatestVersion,
            MinimumVersion = bootstrap.MinimumVersion,
            DownloadUri = downloadUri,
            Sha256 = bootstrap.Sha256,
            PublisherSha256 = bootstrap.PublisherSha256,
            PackageSha256 = bootstrap.PackageSha256,
            PolicySource = options.ApiBaseAddress
        };
    }

    private static ClientUpgradeDecision Blocked(
        string currentVersion,
        ClientBootstrap bootstrap,
        string errorCode,
        bool upgradeRequired = false) => new()
    {
        BusinessAllowed = false,
        UpgradeRequired = upgradeRequired,
        CurrentVersion = currentVersion,
        LatestVersion = bootstrap.LatestVersion,
        MinimumVersion = bootstrap.MinimumVersion,
        ErrorCode = errorCode
    };

    internal static bool TryValidateDownload(
        string? downloadUrl,
        string? sha256,
        string platform,
        out Uri? uri)
    {
        uri = null;
        if (!Uri.TryCreate(downloadUrl, UriKind.Absolute, out var parsed) ||
            parsed.Scheme != Uri.UriSchemeHttps ||
            string.IsNullOrWhiteSpace(parsed.Host) ||
            !string.IsNullOrWhiteSpace(parsed.UserInfo) ||
            !string.IsNullOrWhiteSpace(parsed.Fragment) ||
            !IsDigest(sha256))
        {
            return false;
        }

        var extension = Path.GetExtension(parsed.AbsolutePath);
        var allowed = platform.Equals("windows", StringComparison.OrdinalIgnoreCase)
            ? extension.Equals(".msix", StringComparison.OrdinalIgnoreCase) ||
              extension.Equals(".appinstaller", StringComparison.OrdinalIgnoreCase)
            : platform.Equals("android", StringComparison.OrdinalIgnoreCase) &&
              extension.Equals(".apk", StringComparison.OrdinalIgnoreCase);
        if (!allowed)
            return false;

        uri = parsed;
        return true;
    }

    private static bool IsDigest(string? value) => value is { Length: 64 } && value.All(Uri.IsHexDigit);

    private static bool IsInsecureSource(Uri source) =>
        !source.IsAbsoluteUri || source.Scheme != Uri.UriSchemeHttps || source.UserInfo.Length != 0;
}
