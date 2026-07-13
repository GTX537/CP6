// CP6.Core/Services/Wf/WfApiKeyHelper.cs
using System.Security.Cryptography;
using System.Text;

namespace CP6.Core.Services.Wf;

/// <summary>message 触发器 API key 基建（spec §3.4）：32 字节高熵随机，明文只在创建/重置响应显示一次，
/// 库内仅存 SHA-256 hex（泄库不可还原）；校验常量时间比较。复刻 RefreshTokenService/TwoFactorService 先例。</summary>
public static class WfApiKeyHelper
{
    public static string NewRawKey() => Base64Url(RandomNumberGenerator.GetBytes(32));

    public static string HashOf(string raw)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw)));

    public static bool Verify(string raw, string? storedHash)
    {
        if (string.IsNullOrEmpty(raw) || string.IsNullOrEmpty(storedHash)) return false;
        var candidate = HashOf(raw);
        var stored = storedHash.ToUpperInvariant();            // ToHexString 恒大写，防御性归一
        if (candidate.Length != stored.Length) return false;   // FixedTimeEquals 要求等长
        return CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(candidate), Encoding.ASCII.GetBytes(stored));
    }

    private static string Base64Url(byte[] b)
        => Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
