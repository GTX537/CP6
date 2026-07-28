namespace CP6.Entity.DTOs.Client;

/// <summary>原生客户端在每次登录/刷新时提交的非敏感设备画像。</summary>
public sealed class ClientContextDto
{
    public string ClientKind { get; set; } = string.Empty;
    public string DeviceId { get; set; } = string.Empty;
    public string AppVersion { get; set; } = string.Empty;
    public string? PlatformVersion { get; set; }
}

public sealed class NativeLoginRequest
{
    public string UserName { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string? TenantCode { get; set; }
    public ClientContextDto Client { get; set; } = new();
}

public class NativeChallengeRequest
{
    public string ChallengeToken { get; set; } = string.Empty;
    public ClientContextDto Client { get; set; } = new();
}

public sealed class NativeTwoFactorRequest : NativeChallengeRequest
{
    public string Code { get; set; } = string.Empty;
    public string? Method { get; set; }
}

public sealed class NativeRefreshRequest
{
    public string RefreshToken { get; set; } = string.Empty;
    public ClientContextDto Client { get; set; } = new();
}

public sealed class NativeLogoutRequest
{
    public string? RefreshToken { get; set; }
    public ClientContextDto Client { get; set; } = new();
}

public sealed class ClientMenuDto
{
    public int Id { get; set; }
    public string MenuName { get; set; } = string.Empty;
    public string? RoutePath { get; set; }
    public string? Icon { get; set; }
    public int? ParentId { get; set; }
    public int OrderNo { get; set; }
}

public sealed class ClientProfileDto
{
    public string UserName { get; set; } = string.Empty;
    public string? NickName { get; set; }
    public int? RoleId { get; set; }
    public List<ClientMenuDto> Menus { get; set; } = new();
    public bool MustChangePassword { get; set; }
    public bool IsPlatformAdmin { get; set; }
}

public sealed class TokenSessionDto
{
    public string AccessToken { get; set; } = string.Empty;
    public DateTimeOffset AccessExpiresAt { get; set; }
    public string RefreshToken { get; set; } = string.Empty;
    public DateTimeOffset RefreshExpiresAt { get; set; }
    public ClientProfileDto Profile { get; set; } = new();
}

/// <summary>State: authenticated / twoFactorRequired / enrollmentRequired。</summary>
public sealed class NativeAuthResult
{
    public string State { get; set; } = string.Empty;
    public string? ChallengeToken { get; set; }
    public TokenSessionDto? Session { get; set; }
}

public sealed class NativeSsoStartRequest
{
    public string TenantCode { get; set; } = string.Empty;
    public string RedirectUri { get; set; } = string.Empty;
    public string CodeChallenge { get; set; } = string.Empty;
    public ClientContextDto Client { get; set; } = new();
}

public sealed class NativeSsoStartResponse
{
    public string AuthorizeUrl { get; set; } = string.Empty;
}

public sealed class NativeSsoExchangeRequest
{
    public string GrantCode { get; set; } = string.Empty;
    public string CodeVerifier { get; set; } = string.Empty;
    public ClientContextDto Client { get; set; } = new();
}

public sealed class ClientBootstrapDto
{
    public string ApiVersion { get; set; } = "1";
    public DateTimeOffset ServerUtc { get; set; }
    public string Platform { get; set; } = string.Empty;
    public string CurrentVersion { get; set; } = string.Empty;
    public string LatestVersion { get; set; } = string.Empty;
    public string MinimumVersion { get; set; } = string.Empty;
    public bool UpgradeRequired { get; set; }
    public string? DownloadUrl { get; set; }
    public string? Sha256 { get; set; }
    public string LanguageManifestVersion { get; set; } = string.Empty;
}
