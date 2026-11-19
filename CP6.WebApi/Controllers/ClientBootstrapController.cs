using CP6.Client.Core;
using CP6.Core.Services.Sys;
using CP6.Entity.DTOs.Client;
using CP6.WebApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace CP6.WebApi.Controllers;

[ApiController]
[Route("api/client")]
public sealed class ClientBootstrapController : ControllerBase
{
    private readonly SecurityOptions _security;
    private readonly LangPublishService _languages;

    public ClientBootstrapController(
        IOptions<SecurityOptions> security,
        LangPublishService languages)
    {
        _security = security.Value;
        _languages = languages;
    }

    [HttpGet("bootstrap")]
    [AllowAnonymous]
    public async Task<ActionResult<ClientBootstrapDto>> Get(
        [FromQuery] string platform,
        [FromQuery] string currentVersion,
        [FromQuery] string? channel = null)
    {
        var release = platform.Equals("android", StringComparison.OrdinalIgnoreCase)
            ? _security.NativeClient.Android
            : platform.Equals("windows", StringComparison.OrdinalIgnoreCase)
                ? _security.NativeClient.Windows
                : null;
        if (release is null) return BadRequest(new { message = "不支持该客户端平台。" });
        if (!TryGetMinimum(release, out var minimum))
            return StatusCode(503, new { code = "E-CLIENT-VERSION-POLICY", message = "客户端版本政策无效。" });
        if (!string.Equals(channel ?? "stable", release.Channel, StringComparison.Ordinal))
            return BadRequest(new { code = "E-CLIENT-CHANNEL-MISMATCH", message = "客户端与发行渠道不匹配。" });
        if (!ClientSemanticVersion.TryParse(currentVersion, out var current))
            return BadRequest(new { code = "E-CLIENT-VERSION-INVALID", message = "当前客户端版本无效。" });

        var manifest = await _languages.GetManifestAsync();
        return new ClientBootstrapDto
        {
            ApiVersion = "1",
            RequiredGates = [DeviceProofV1.RegisteredDeviceGate],
            ServerUtc = DateTimeOffset.UtcNow,
            Platform = platform.ToLowerInvariant(),
            Channel = release.Channel,
            CurrentVersion = currentVersion,
            LatestVersion = release.LatestVersion,
            MinimumVersion = release.MinimumVersion,
            UpgradeRequired = current.CompareTo(minimum) < 0,
            DownloadUrl = release.DownloadUrl,
            Sha256 = release.Sha256,
            PublisherSha256 = release.PublisherSha256,
            PackageSha256 = release.PackageSha256,
            LanguageManifestVersion = manifest?.Version ?? string.Empty
        };
    }

    internal static bool TryGetMinimum(ClientReleaseOptions release, out ClientSemanticVersion minimum)
    {
        minimum = null!;
        return release.Channel is { Length: > 0 and <= 32 } &&
               release.Channel.All(c => c is >= 'a' and <= 'z' || char.IsAsciiDigit(c) || c == '-') &&
               ClientSemanticVersion.TryParse(release.MinimumVersion, out minimum) &&
               ClientSemanticVersion.TryParse(release.LatestVersion, out var latest) &&
               minimum.CompareTo(latest) <= 0;
    }

    internal static int Compare(string left, string right)
    {
        if (!ClientSemanticVersion.TryParse(left, out var parsedLeft))
            return -1;
        if (!ClientSemanticVersion.TryParse(right, out var parsedRight))
            return 1;
        return parsedLeft.CompareTo(parsedRight);
    }
}
