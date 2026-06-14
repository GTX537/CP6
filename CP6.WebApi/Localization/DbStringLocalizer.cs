using System.Globalization;
using CP6.Core.EFDbContext;
using CP6.Core.Utilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;

namespace CP6.WebApi.Localization;

/// <summary>
/// DB 支持的本地化器（i18n 优化 P1）。
///
/// 设计要点：
/// 1. 译文唯一事实源 = <c>Sys_Lang</c> 表。与前端 <c>/api/lang/{code}</c> 共用同一张表、
///    同一 Redis 缓存键 <c>lang:{code}</c>——两侧任一预热即共享，词条变更清缓存两侧同时失效。
/// 2. 当前请求 culture 由 RequestLocalization 设定（CurrentUICulture），按 culture 取对应语言列；
///    缺失回退源语言 ja，再缺回退 key 本身（不抛技术串）。完整回退链（ko→en→ja 等）留 P2。
/// 3. 复用现有 <see cref="CacheService"/>（Cache-Aside，1h TTL）；缓存未命中时用
///    <see cref="IServiceScopeFactory"/> 取 scoped <see cref="CP6Context"/>，故本类可安全注册为 Singleton。
/// </summary>
public class DbStringLocalizer : IStringLocalizer
{
    private static readonly string[] SupportedCodes = { "zh-CN", "zh-TW", "en", "ja", "ko" };
    private const string SourceLang = "ja";   // 源语言（系统原文），最终回退

    private readonly CacheService _cache;
    private readonly IServiceScopeFactory _scopeFactory;

    public DbStringLocalizer(CacheService cache, IServiceScopeFactory scopeFactory)
    {
        _cache = cache;
        _scopeFactory = scopeFactory;
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
        => GetDict(CurrentLangCode()).Select(kv => new LocalizedString(kv.Key, kv.Value, resourceNotFound: false));

    /// <summary>当前 culture 取译文；缺失回退源语言 ja；再缺返回 null（调用方回退 key 本身）。</summary>
    private string? Resolve(string key)
    {
        var code = CurrentLangCode();
        if (GetDict(code).TryGetValue(key, out var v) && !string.IsNullOrEmpty(v)) return v;
        if (code != SourceLang && GetDict(SourceLang).TryGetValue(key, out var jv) && !string.IsNullOrEmpty(jv)) return jv;
        return null;
    }

    /// <summary>把 CurrentUICulture 归一到 5 个受支持语言码；RequestLocalization 已 clamp，故通常已是其一。</summary>
    private static string CurrentLangCode()
    {
        var name = CultureInfo.CurrentUICulture.Name;
        foreach (var c in SupportedCodes)
            if (string.Equals(name, c, StringComparison.OrdinalIgnoreCase)) return c;
        // 容错：未 clamp 的派生 culture（zh-Hans / en-US 等）
        if (name.StartsWith("zh-Hant", StringComparison.OrdinalIgnoreCase) || name.Contains("TW") || name.Contains("HK")) return "zh-TW";
        if (name.StartsWith("zh", StringComparison.OrdinalIgnoreCase)) return "zh-CN";
        if (name.StartsWith("en", StringComparison.OrdinalIgnoreCase)) return "en";
        if (name.StartsWith("ko", StringComparison.OrdinalIgnoreCase)) return "ko";
        return SourceLang;
    }

    /// <summary>读取某语言全部词条字典（与 LangController 共用缓存键 lang:{code}）。</summary>
    private Dictionary<string, string> GetDict(string langCode)
    {
        var cacheKey = CacheService.LangKeyPrefix + langCode;
        return _cache.GetOrSetAsync(cacheKey, async () =>
        {
            using var scope = _scopeFactory.CreateScope();
            var ctx = scope.ServiceProvider.GetRequiredService<CP6Context>();
            var items = await ctx.Sys_Langs.AsNoTracking().ToListAsync();
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
