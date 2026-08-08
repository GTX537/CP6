using System.Security.Cryptography;
using System.Text;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Common;
using CP6.Entity.DomainModels.Sys;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CP6.Core.Services.Sys;

/// <summary>
/// 刷新令牌服务实现（S 类认证加固 T4）。错误经 <see cref="InvalidOperationException"/> 携带 E-SEC 码
/// （Core 层惯例，控制器转 BizException 本地化）。
/// 安全设计：①库内只存 SHA-256 哈希（泄库不可还原原始令牌）；②轮换式（每次 refresh 吊销旧发新）；
/// ③重用检测（已吊销令牌再次提交即视为盗用，吊销该用户整条链）；④TokenHash 单列全局唯一，
/// refresh 时无租户上下文按 TokenHash + IgnoreQueryFilters 跨租户精确命中，再由令牌回设租户上下文。
/// </summary>
public class RefreshTokenService : IRefreshTokenService
{
    private readonly CP6Context _db;
    private readonly TokenOptions _t;
    private readonly ITenantContext _tenant;

    public RefreshTokenService(CP6Context db, IOptions<SecurityOptions> opt, ITenantContext tenant)
    {
        _db = db;
        _t = opt.Value.Token;
        _tenant = tenant;
    }

    private static string NewRaw() => Base64Url(RandomNumberGenerator.GetBytes(32));
    private static string HashOf(string raw) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw)));
    private static string Base64Url(byte[] b) => Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public Task<string> IssueAsync(Sys_User user, string? ip, string? ua)
        => IssueAsync(user, ip, ua, RefreshTokenClientContext.Web);

    public async Task<string> IssueAsync(
        Sys_User user, string? ip, string? ua, RefreshTokenClientContext client)
    {
        var raw = NewRaw();
        _db.Sys_RefreshTokens.Add(new Sys_RefreshToken
        {
            UserId = user.Id,
            TenantId = user.TenantId,   // 显式盖租户，与 RotateAsync 对称；不依赖 SaveChanges 隐式盖章的上下文时序
            TokenHash = HashOf(raw),
            ExpiresAt = DateTime.Now.AddDays(_t.RefreshTokenDays),
            CreatedIp = ip,
            UserAgent = ua,
            ClientKind = client.ClientKind,
            DeviceId = client.DeviceId,
            AppVersion = client.AppVersion
        });
        await _db.SaveChangesAsync();
        return raw;
    }

    public Task<(string newToken, Sys_User user)> RotateAsync(string rawToken, string? ip, string? ua)
        => RotateAsync(rawToken, ip, ua, RefreshTokenClientContext.Web);

    public async Task<(string newToken, Sys_User user)> RotateAsync(
        string rawToken, string? ip, string? ua, RefreshTokenClientContext client)
    {
        var hash = HashOf(rawToken);
        // 无租户上下文：按 TokenHash 跨租户查（全局唯一索引；IgnoreQueryFilters 白名单——令牌本身即凭证）
        var row = await _db.Sys_RefreshTokens.IgnoreQueryFilters().FirstOrDefaultAsync(r => r.TokenHash == hash);
        if (row == null || row.ExpiresAt <= DateTime.Now)
            throw new InvalidOperationException("E-SEC-007");   // 令牌无效或已过期

        if (row.RevokedAt != null)
        {
            // 重用检测：已吊销令牌被再次提交 → 视为盗用 → 吊销该用户整条链
            await RevokeAllForUserAsync(row.UserId);
            throw new InvalidOperationException("E-SEC-008");   // 检测到令牌重用
        }

        // Native refresh tokens are device-bound. AppVersion may legitimately
        // change during an upgrade, but client kind and stable device identity
        // must remain the same throughout the token family.
        if (!string.Equals(row.ClientKind, "Web", StringComparison.OrdinalIgnoreCase)
            && (!string.Equals(row.ClientKind, client.ClientKind, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(row.DeviceId, client.DeviceId, StringComparison.Ordinal)))
        {
            await RevokeAllForUserAsync(row.UserId);
            throw new InvalidOperationException("E-SEC-024");
        }

        // 由令牌的 TenantId 回设上下文，后续查询/盖章按其租户正确作用域
        _tenant.CurrentTenantId = row.TenantId;
        var user = await _db.Sys_Users.IgnoreQueryFilters().FirstAsync(u => u.Id == row.UserId);

        var raw2 = NewRaw();
        var hash2 = HashOf(raw2);
        var now = DateTime.Now;

        var replacement = new Sys_RefreshToken
        {
            UserId = user.Id,
            TenantId = row.TenantId,
            TokenHash = hash2,
            ExpiresAt = DateTime.Now.AddDays(_t.RefreshTokenDays),
            CreatedIp = ip,
            UserAgent = ua,
            ClientKind = client.ClientKind,
            DeviceId = client.DeviceId,
            AppVersion = client.AppVersion
        };

        if (_db.Database.IsRelational())
        {
            // The conditional revoke and replacement insert share one database
            // transaction. Exactly one concurrent caller can create the next
            // token; a failed insert rolls the revoke back as well.
            var tx = await _db.Database.BeginTransactionAsync();
            try
            {
                var affected = await _db.Sys_RefreshTokens.IgnoreQueryFilters()
                    .Where(r => r.Id == row.Id && r.RevokedAt == null)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(r => r.RevokedAt, now)
                        .SetProperty(r => r.ReplacedByTokenHash, hash2));
                if (affected != 1)
                {
                    await tx.RollbackAsync();
                    await tx.DisposeAsync();
                    tx = null!;
                    _db.ChangeTracker.Clear();
                    await RevokeAllForUserAsync(row.UserId);
                    throw new InvalidOperationException("E-SEC-008");
                }

                _db.Sys_RefreshTokens.Add(replacement);
                await _db.SaveChangesAsync();
                await tx.CommitAsync();
                return (raw2, user);
            }
            catch
            {
                if (tx != null) await tx.RollbackAsync();
                throw;
            }
            finally
            {
                if (tx != null) await tx.DisposeAsync();
            }
        }

        row.RevokedAt = now;
        row.ReplacedByTokenHash = hash2;
        _db.Sys_RefreshTokens.Add(replacement);
        await _db.SaveChangesAsync();
        return (raw2, user);
    }

    public async Task RevokeAsync(string rawToken)
    {
        var hash = HashOf(rawToken);
        var row = await _db.Sys_RefreshTokens.IgnoreQueryFilters().FirstOrDefaultAsync(r => r.TokenHash == hash);
        if (row is { RevokedAt: null })
        {
            row.RevokedAt = DateTime.Now;
            await _db.SaveChangesAsync();
        }
    }

    public async Task RevokeAllForUserAsync(Guid userId, bool saveChanges = true)
    {
        var rows = await _db.Sys_RefreshTokens.IgnoreQueryFilters()
            .Where(r => r.UserId == userId && r.RevokedAt == null).ToListAsync();
        if (rows.Count == 0) return;
        foreach (var r in rows) r.RevokedAt = DateTime.Now;
        // saveChanges:false → 变更留在跟踪器，由调用方与其它写入合并一次原子保存
        if (saveChanges) await _db.SaveChangesAsync();
    }

    public async Task RevokeAllForDeviceAsync(
        Guid tenantId,
        string deviceId,
        bool saveChanges = true)
    {
        var rows = await _db.Sys_RefreshTokens.IgnoreQueryFilters()
            .Where(r => r.TenantId == tenantId
                        && r.DeviceId == deviceId
                        && r.RevokedAt == null)
            .ToListAsync();
        if (rows.Count == 0) return;
        foreach (var row in rows) row.RevokedAt = DateTime.Now;
        if (saveChanges) await _db.SaveChangesAsync();
    }
}
