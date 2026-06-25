using System.Security.Cryptography;
using System.Text;
using CP6.Core.EFDbContext;
using CP6.Entity.DomainModels.Sys;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Options;

namespace CP6.Core.Services.Sys;

/// <summary>
/// 2FA 编排服务实现（S 类 #2 T4）。组合 TotpService + IEmailSender + IDistributedCache + 审计。
/// 密钥/审计边界（评审#7）：BeginEnrollment 已 Enabled 抛 E-SEC-017；
/// LogAsync reason 不带 secret/otpauth URI。邮件 OTP 绑 otpKey 防跨会话(评审#3)。
/// </summary>
public class TwoFactorService : ITwoFactorService
{
    private readonly CP6Context _db;
    private readonly ITotpService _totp;
    private readonly IEmailSender _email;
    private readonly IDistributedCache _cache;
    private readonly ISecurityAuditService _audit;
    private readonly TwoFactorOptions _o;

    public TwoFactorService(
        CP6Context db,
        ITotpService totp,
        IEmailSender email,
        IDistributedCache cache,
        ISecurityAuditService audit,
        IOptions<SecurityOptions> sec)
    {
        _db = db;
        _totp = totp;
        _email = email;
        _cache = cache;
        _audit = audit;
        _o = sec.Value.TwoFactor;
    }

    public int ResolveTenantMode(Guid tenantId)
        => _db.Sys_Tenants.IgnoreQueryFilters()
            .Where(t => t.Id == tenantId)
            .Select(t => t.TwoFactorMode)
            .FirstOrDefault();

    public bool IsChallengeRequired(Sys_User user, int tenantMode)
        => user.TwoFactorEnabled || tenantMode == 2;

    public bool MustEnroll(Sys_User user, int tenantMode)
        => tenantMode == 2 && !user.TwoFactorEnabled;

    public string BeginEnrollment(Sys_User user)
    {
        if (user.TwoFactorEnabled)
            throw new InvalidOperationException("E-SEC-017");
        user.TwoFactorSecret = _totp.GenerateSecret();
        return _totp.BuildOtpAuthUri(user.TwoFactorSecret, user.UserName);
    }

    public async Task<bool> ConfirmEnrollmentAsync(Sys_User user, string code)
    {
        if (string.IsNullOrEmpty(user.TwoFactorSecret)) return false;
        if (!_totp.Verify(user.TwoFactorSecret, code)) return false;
        user.TwoFactorEnabled = true;
        user.TwoFactorEnrolledAt = DateTime.Now;
        await _audit.LogAsync(SecurityEventType.TwoFactorEnrolled, user.Id, user.UserName, null, null, null);
        return true;
    }

    public bool VerifyTotp(Sys_User user, string code)
        => !string.IsNullOrEmpty(user.TwoFactorSecret) && _totp.Verify(user.TwoFactorSecret, code);

    private static string OtpStoreKey(string otpKey) => $"sec:2fa:otp:{otpKey}";
    private static string OtpCooldownKey(string otpKey) => $"sec:2fa:otp-cd:{otpKey}";

    public async Task SendEmailOtpAsync(Sys_User user, string otpKey)
    {
        if (string.IsNullOrWhiteSpace(user.Email))
            throw new InvalidOperationException("E-SEC-015");
        if (!string.IsNullOrEmpty(await _cache.GetStringAsync(OtpCooldownKey(otpKey))))
            throw new InvalidOperationException("E-SEC-016");

        var otp = GenerateNumericOtp(_o.EmailOtpLength);
        var hash = Sha256Hex(otp);
        await _cache.SetStringAsync(OtpStoreKey(otpKey), hash, new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(Math.Max(1, _o.EmailOtpMinutes))
        });
        await _cache.SetStringAsync(OtpCooldownKey(otpKey), "1", new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(Math.Max(1, _o.EmailResendCooldownSeconds))
        });

        try
        {
            await _email.SendAsync(user.Email, "CP6 验证码", $"您的 CP6 验证码：{otp}（{_o.EmailOtpMinutes} 分钟内有效）");
        }
        catch
        {
            // 发送失败：清除已存的 OTP 以防错觉成功；冷却保留（评审：防尝试性脱密）
            await _cache.RemoveAsync(OtpStoreKey(otpKey));
            throw new InvalidOperationException("E-SEC-018");
        }

        await _audit.LogAsync(SecurityEventType.TwoFactorEmailOtpSent, user.Id, user.UserName, null, null, null);
    }

    public async Task<bool> VerifyEmailOtpAsync(string otpKey, string code)
    {
        if (string.IsNullOrWhiteSpace(code)) return false;
        var stored = await _cache.GetStringAsync(OtpStoreKey(otpKey));
        if (string.IsNullOrEmpty(stored)) return false;
        if (!FixedTimeEquals(stored, Sha256Hex(code))) return false;
        await _cache.RemoveAsync(OtpStoreKey(otpKey));
        return true;
    }

    public async Task ResetAsync(Sys_User user, string? reason = null)
    {
        user.TwoFactorEnabled = false;
        user.TwoFactorSecret = null;
        user.TwoFactorEnrolledAt = null;
        await _audit.LogAsync(SecurityEventType.TwoFactorReset, user.Id, user.UserName, null, null, null, reason);
    }

    private static string GenerateNumericOtp(int length)
    {
        length = Math.Max(4, Math.Min(10, length));
        Span<byte> buf = stackalloc byte[length];
        RandomNumberGenerator.Fill(buf);
        var sb = new StringBuilder(length);
        for (int i = 0; i < length; i++) sb.Append((char)('0' + (buf[i] % 10)));
        return sb.ToString();
    }

    private static string Sha256Hex(string s)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(s));
        return Convert.ToHexString(bytes);
    }

    private static bool FixedTimeEquals(string a, string b)
    {
        if (a.Length != b.Length) return false;
        return CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(a),
            Encoding.ASCII.GetBytes(b));
    }
}
