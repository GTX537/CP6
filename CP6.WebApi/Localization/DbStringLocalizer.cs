using System.Globalization;
using CP6.Core.EFDbContext;
using CP6.Core.Utilities;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;

namespace CP6.WebApi.Localization;

/// <summary>
/// DB 支持的本地化器（i18n 优化 P1 / P3）。
///
/// 设计要点：
/// 1. 译文唯一事实源 = <c>Sys_Lang</c> 表。全局值与前端 <c>/api/lang/{code}</c> 共用缓存键 <c>lang:{code}</c>。
/// 2. 解析回退链（P3）：租户覆盖(TenantId=X) → 全局(null) → 回退语言 → 源语言 ja → key 本身。
///    每个语言档先查租户覆盖再查全局；语言档顺序由 <see cref="FallbackChain"/> 给出（与前端一致）。
/// 3. 复用现有 <see cref="CacheService"/>（Cache-Aside，1h TTL）；缓存未命中时用
///    <see cref="IServiceScopeFactory"/> 取 scoped <see cref="CP6Context"/>，故本类可安全注册为 Singleton。
/// 4. 当前租户取 JWT 'tenant' claim（系统单租户时恒 null，仅用全局值；多租户上线即生效）。
/// </summary>
public class DbStringLocalizer : IStringLocalizer
{
    private static readonly string[] SupportedCodes = { "zh-CN", "zh-TW", "en", "ja", "ko" };
    private const string SourceLang = "ja";

    // 回退语言链（与前端 i18n/index.ts fallbackChain 一致），含自身在首位、源语言 ja 兜底。
    private static readonly Dictionary<string, string[]> FallbackChain = new()
    {
        ["zh-CN"] = new[] { "zh-CN", "zh-TW", "ja" },
        ["zh-TW"] = new[] { "zh-TW", "zh-CN", "ja" },
        ["en"] = new[] { "en", "ja" },
        ["ko"] = new[] { "ko", "en", "ja" },
        ["ja"] = new[] { "ja" },
    };

    private readonly CacheService _cache;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IHttpContextAccessor _http;

    public DbStringLocalizer(CacheService cache, IServiceScopeFactory scopeFactory, IHttpContextAccessor http)
    {
        _cache = cache;
        _scopeFactory = scopeFactory;
        _http = http;
    }

    public LocalizedString this[string name]
    {
        get
        {
            var value = Resolve(name);
            return new LocalizedString(name, value ?? name, resourceNotFound: value == null);
        }
    }

    public LocalizedString this[string name, params object[] arguments]
    {
        get
        {
            var raw = Resolve(name);
            var value = string.Format(raw ?? name, arguments);
            return new LocalizedString(name, value, resourceNotFound: raw == null);
        }
    }

    public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures)
        => GetGlobalDict(CurrentLangCode()).Select(kv => new LocalizedString(kv.Key, kv.Value, resourceNotFound: false));

    /// <summary>回退链解析：每语言档先租户覆盖、再全局；档顺序当前→回退→ja；全缺返回 null。</summary>
    private string? Resolve(string key)
    {
        var tenant = CurrentTenantId();
        foreach (var lang in FallbackChain.TryGetValue(CurrentLangCode(), out var chain) ? chain : new[] { SourceLang })
        {
            if (tenant != null && GetTenantDict(lang, tenant.Value).TryGetValue(key, out var tv) && !string.IsNullOrEmpty(tv)) return tv;
            if (GetGlobalDict(lang).TryGetValue(key, out var gv) && !string.IsNullOrEmpty(gv)) return gv;
        }
        return null;
    }

    /// <summary>把 CurrentUICulture 归一到 5 个受支持语言码；RequestLocalization 已 clamp，故通常已是其一。</summary>
    private static string CurrentLangCode()
    {
        var name = CultureInfo.CurrentUICulture.Name;
        foreach (var c in SupportedCodes)
            if (string.Equals(name, c, StringComparison.OrdinalIgnoreCase)) return c;
        if (name.StartsWith("zh-Hant", StringComparison.OrdinalIgnoreCase) || name.Contains("TW") || name.Contains("HK")) return "zh-TW";
        if (name.StartsWith("zh", StringComparison.OrdinalIgnoreCase)) return "zh-CN";
        if (name.StartsWith("en", StringComparison.OrdinalIgnoreCase)) return "en";
        if (name.StartsWith("ko", StringComparison.OrdinalIgnoreCase)) return "ko";
        return SourceLang;
    }

    /// <summary>当前租户 ID（JWT 'tenant' claim）；无/不可解析 → null（仅用全局值）。</summary>
    private int? CurrentTenantId()
    {
        var raw = _http.HttpContext?.User?.FindFirst("tenant")?.Value;
        return int.TryParse(raw, out var id) ? id : (int?)null;
    }

    /// <summary>某语言的全局词条字典（TenantId 为 null）。与 LangController 共用缓存键 lang:{code}。</summary>
    private Dictionary<string, string> GetGlobalDict(string langCode)
        => LoadDict(CacheService.LangKeyPrefix + langCode, langCode, tenantId: null);

    /// <summary>某语言、某租户的覆盖词条字典（TenantId = tenantId）。缓存键 lang:t{id}:{code}。</summary>
    private Dictionary<string, string> GetTenantDict(string langCode, int tenantId)
        => LoadDict($"{CacheService.LangKeyPrefix}t{tenantId}:{langCode}", langCode, tenantId);

    private Dictionary<string, string> LoadDict(string cacheKey, string langCode, int? tenantId)
    {
        return _cache.GetOrSetAsync(cacheKey, async () =>
        {
            using var scope = _scopeFactory.CreateScope();
            var ctx = scope.ServiceProvider.GetRequiredService<CP6Context>();
            var items = await ctx.Sys_Langs.AsNoTracking()
                .Where(l => l.TenantId == tenantId)
                .ToListAsync();
            var result = new Dictionary<string, string>(items.Count);
            foreach (var item in items)
            {
                var value = langCode switch
                {
                    "zh-CN" => item.ZhCN,
                    "zh-TW" => item.ZhTW,
                    "en" => item.En,
                    "ja" => item.Ja,
                    "ko" => item.Ko,
                    _ => item.ZhCN
                };
                if (!string.IsNullOrEmpty(value)) result[item.LangKey] = value!;
            }
            return result;
        }, TimeSpan.FromHours(1)).GetAwaiter().GetResult();
    }
}

/// <summary>
/// 工厂：让 <c>IStringLocalizer&lt;T&gt;</c> 也复用同一个 DB 本地化器（本系统词条不按资源类型分桶，统一一张 Sys_Lang）。
/// </summary>
public class DbStringLocalizerFactory : IStringLocalizerFactory
{
    private readonly DbStringLocalizer _localizer;
    public DbStringLocalizerFactory(DbStringLocalizer localizer) => _localizer = localizer;
    public IStringLocalizer Create(Type resourceSource) => _localizer;
    public IStringLocalizer Create(string baseName, string location) => _localizer;
}
