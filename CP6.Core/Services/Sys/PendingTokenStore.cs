using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Options;

namespace CP6.Core.Services.Sys;

public class PendingTokenStore : IPendingTokenStore
{
    private readonly IDistributedCache _cache;
    private readonly TwoFactorOptions _o;

    public PendingTokenStore(IDistributedCache cache, IOptions<SecurityOptions> sec)
    {
        _cache = cache;
        _o = sec.Value.TwoFactor;
    }

    private static string Key(string jti) => $"sec:2fa:pending:{jti}";

    private record Payload(Guid UserId, Guid TenantId, string Purpose);

    public string Create(Guid userId, Guid tenantId, string purpose)
    {
        var jti = Guid.NewGuid().ToString("N");
        var json = JsonSerializer.Serialize(new Payload(userId, tenantId, purpose));
        _cache.SetString(Key(jti), json, new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(Math.Max(1, _o.PendingTokenMinutes))
        });
        return jti;
    }

    public (Guid userId, Guid tenantId, string purpose)? Get(string pendingJti)
    {
        if (string.IsNullOrEmpty(pendingJti)) return null;
        var json = _cache.GetString(Key(pendingJti));
        if (string.IsNullOrEmpty(json)) return null;
        var p = JsonSerializer.Deserialize<Payload>(json);
        return p is null ? null : (p.UserId, p.TenantId, p.Purpose);
    }

    public void Consume(string pendingJti)
    {
        if (string.IsNullOrEmpty(pendingJti)) return;
        _cache.Remove(Key(pendingJti));
    }
}
