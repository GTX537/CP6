using CP6.Core.EFDbContext;
using CP6.Core.Services.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CP6.Core.Services.Wf;

/// <summary>租户时钟（WFS infra ⑥，E-T2）。为引擎的本地时刻解释提供「当前租户时区」单一来源：
/// <list type="number">
///   <item>当前租户 <see cref="CP6.Entity.DomainModels.Sys.Sys_Tenant.TimeZoneId"/>（可解析则用）；</item>
///   <item>缺省 → app 默认 <see cref="WfsInfraOptions.DefaultTimeZone"/>（可解析则用）；</item>
///   <item>再缺省 → 服务器本地 <see cref="TimeZoneInfo.Local"/>（存量行为，字节等价）。</item>
/// </list>
/// 不可解析的 id 记警告并逐级回落——运行期永不因坏配置抛异常（保存时的 E-WF-028 才拒绝落库）。</summary>
public interface ITenantClock
{
    /// <summary>解析当前租户时区（见类型注释的三级回落链）。</summary>
    TimeZoneInfo GetTenantTimeZone();
}

/// <summary><see cref="ITenantClock"/> 实现（scoped）。<b>per-scope 缓存</b>一份时区——同请求/同 worker 租户 scope 内
/// 多次消费只查一次租户表、只解析一次 id。</summary>
public sealed class TenantClock : ITenantClock
{
    private readonly CP6Context _db;
    private readonly ITenantContext _tenant;
    private readonly WfsInfraOptions _opts;
    private readonly ILogger<TenantClock>? _logger;
    private TimeZoneInfo? _cached;   // per-scope 缓存（服务 scoped，一次解析复用）

    public TenantClock(CP6Context db, ITenantContext tenant, WfsInfraOptions opts, ILogger<TenantClock>? logger = null)
    {
        _db = db;
        _tenant = tenant;
        _opts = opts;
        _logger = logger;
    }

    public TimeZoneInfo GetTenantTimeZone() => _cached ??= Resolve();

    private TimeZoneInfo Resolve()
    {
        // ① 当前租户 TimeZoneId。Sys_Tenant 为共享表（不参与行级过滤）→ 直查 Id 即得，无需 IgnoreQueryFilters。
        var tid = _tenant.CurrentTenantId;
        var tzId = _db.Sys_Tenants.AsNoTracking()
            .Where(t => t.Id == tid)
            .Select(t => t.TimeZoneId)
            .FirstOrDefault();
        if (TryResolve(tzId, out var tz)) return tz;

        // ② app 默认（Wfs:DefaultTimeZone）。
        if (TryResolve(_opts.DefaultTimeZone, out tz)) return tz;

        // ③ 服务器本地（存量行为，字节等价）。
        return TimeZoneInfo.Local;
    }

    /// <summary>尝试解析时区 id（.NET 8 <see cref="TimeZoneInfo.FindSystemTimeZoneById"/> 跨平台容纳 IANA/Windows 双制式）。
    /// 空/不可解析 → false（记警告，交由上层回落），永不抛。</summary>
    private bool TryResolve(string? id, out TimeZoneInfo tz)
    {
        tz = TimeZoneInfo.Local;
        if (string.IsNullOrWhiteSpace(id)) return false;
        try
        {
            tz = TimeZoneInfo.FindSystemTimeZoneById(id);
            return true;
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            _logger?.LogWarning(ex, "时区 id 无法解析:{TimeZoneId}，回落下一级来源", id);
            return false;
        }
    }
}
