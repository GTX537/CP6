using CP6.Core.EFDbContext;
using CP6.Entity.DomainModels.Sys;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CP6.Core.Services.Sys;

/// <summary>
/// 密码策略服务实现。错误经 <see cref="InvalidOperationException"/> 携带 E-SEC 错误码
/// （Core 层惯例，见 Mes/Plan 既有服务）——WebApi 边界转 BizException 本地化。
/// </summary>
public class PasswordPolicyService : IPasswordPolicyService
{
    private readonly CP6Context _db;
    private readonly PasswordPolicyOptions _p;
    private readonly IPasswordHasher _hasher;

    public PasswordPolicyService(CP6Context db, IOptions<SecurityOptions> opt, IPasswordHasher hasher)
    {
        _db = db;
        _p = opt.Value.Password;
        _hasher = hasher;
    }

    public void Validate(string plain)
    {
        if (string.IsNullOrEmpty(plain) || plain.Length < _p.MinLength) throw new InvalidOperationException("E-SEC-004");
        if (_p.RequireUpper && !plain.Any(char.IsUpper)) throw new InvalidOperationException("E-SEC-004");
        if (_p.RequireLower && !plain.Any(char.IsLower)) throw new InvalidOperationException("E-SEC-004");
        if (_p.RequireDigit && !plain.Any(char.IsDigit)) throw new InvalidOperationException("E-SEC-004");
        if (_p.RequireSymbol && plain.All(char.IsLetterOrDigit)) throw new InvalidOperationException("E-SEC-004");
    }

    public async Task CheckHistoryAsync(Guid userId, string plain)
    {
        if (_p.HistoryCount <= 0) return;
        // 改密无 tenant 边界保证（同租户）；历史按用户取，IgnoreQueryFilters 防作用域漏读
        var recent = await _db.Sys_PasswordHistories.IgnoreQueryFilters()
            .Where(h => h.UserId == userId).OrderByDescending(h => h.ChangedAt)
            .Take(_p.HistoryCount).Select(h => h.PasswordHash).ToListAsync();
        if (recent.Any(h => _hasher.Verify(plain, h))) throw new InvalidOperationException("E-SEC-005");
    }

    public async Task RecordHistoryAsync(Guid userId, string oldHash)
    {
        if (_p.HistoryCount <= 0) return;   // 历史关闭：不记录
        var history = await _db.Sys_PasswordHistories.IgnoreQueryFilters()
            .Where(h => h.UserId == userId).OrderByDescending(h => h.ChangedAt).ToListAsync();
        // 即将再 Add 1 条 → 现存只保留最近 (HistoryCount-1) 条，多余删除
        foreach (var stale in history.Skip(Math.Max(0, _p.HistoryCount - 1)))
            _db.Sys_PasswordHistories.Remove(stale);
        _db.Sys_PasswordHistories.Add(new Sys_PasswordHistory { UserId = userId, PasswordHash = oldHash, ChangedAt = DateTime.Now });
        // 不 SaveChanges：与改密其它写入合并到调用方一次保存
    }

    public bool IsExpired(Sys_User user)
        => _p.ExpiryDays > 0 && user.PasswordChangedAt is { } at && (DateTime.Now - at).TotalDays > _p.ExpiryDays;
}
