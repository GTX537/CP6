using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Options;

namespace CP6.Core.Services.Sys;

/// <summary>
/// SSO state 一次性存储（S 类 #3 T3）。基于 IDistributedCache。
/// key=sec:sso:state:{state}；value=JSON(SsoState)；TTL=SsoOptions.StateMinutes。
/// </summary>
public class SsoStateStore : ISsoStateStore
{
    private readonly IDistributedCache _cache;
    private readonly SecurityOptions _sec;

    public SsoStateStore(IDistributedCache cache, IOptions<SecurityOptions> sec)
    {
        _cache = cache;
        _sec = sec.Value;
    }

    private static string Key(string state) => $"sec:sso:state:{state}";

    public string Create(Guid tenantId, string nonce, string codeVerifier, string? returnUrl)
    {
        var state = Guid.NewGuid().ToString("N");
        var json = JsonSerializer.Serialize(new SsoState(tenantId, nonce, codeVerifier, returnUrl));
        _cache.SetString(Key(state), json, new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(Math.Max(1, _sec.Sso.StateMinutes))
        });
        return state;
    }

    public SsoState? Get(string state)
    {
        if (string.IsNullOrEmpty(state)) return null;
        var json = _cache.GetString(Key(state));
        return string.IsNullOrEmpty(json) ? null : JsonSerializer.Deserialize<SsoState>(json);
    }

    public void Consume(string state)
    {
        if (string.IsNullOrEmpty(state)) return;
        _cache.Remove(Key(state));
    }
}
