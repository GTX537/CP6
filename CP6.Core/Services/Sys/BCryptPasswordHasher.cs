using Microsoft.Extensions.Options;

namespace CP6.Core.Services.Sys;

/// <summary>BCrypt 密码哈希。工作因子经 SecurityOptions.Password.BcryptWorkFactor 配置（默认 11），便于安全生命周期升级。</summary>
public class BCryptPasswordHasher : IPasswordHasher
{
    private readonly int _workFactor;
    public BCryptPasswordHasher(int workFactor = 11) => _workFactor = workFactor;
    public BCryptPasswordHasher(IOptions<SecurityOptions> options) : this(options.Value.Password.BcryptWorkFactor) { }

    public string Hash(string plain) => BCrypt.Net.BCrypt.HashPassword(plain, workFactor: _workFactor);
    public bool Verify(string plain, string hash) { try { return BCrypt.Net.BCrypt.Verify(plain, hash); } catch { return false; } }
    // 前缀判定 BCrypt 变体；长度 >= 60 容忍未来变体（标准 BCrypt 输出恰 60 字符）
    public bool IsHashed(string value)
        => !string.IsNullOrEmpty(value) && value.Length >= 60
           && (value.StartsWith("$2a$") || value.StartsWith("$2b$") || value.StartsWith("$2y$"));
}
