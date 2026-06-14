using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.WebApi.Localization;
using Microsoft.EntityFrameworkCore;

namespace CP6.WebApi.Services;

/// <summary>
/// i18n 优化 P4 发布模式：把 Sys_Lang（全局值）导出成版本化静态 JSON，供前端不可变长缓存读取。
///
/// 产物（ContentRoot/wwwroot/i18n 下）：
/// - {version}/{lang}.json   每语言全量词条（不可变，文件名带版本号）
/// - manifest.json           当前版本指针 { version, langs, publishedAt }
///
/// 切换版本只改 manifest（回滚=指回旧 version，旧文件保留）。运行时前端读静态文件，零 DB 读。
/// 经 API 端点 GET /api/lang/published/{ver}/{lang} 下发并带 immutable 缓存头（也便于 CDN 前置）。
/// </summary>
public class LangPublishService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IWebHostEnvironment _env;

    public LangPublishService(IServiceScopeFactory scopeFactory, IWebHostEnvironment env)
    {
        _scopeFactory = scopeFactory;
        _env = env;
    }

    private string RootDir => Path.Combine(_env.ContentRootPath, "wwwroot", "i18n");
    private string ManifestPath => Path.Combine(RootDir, "manifest.json");

    /// <summary>执行一次发布，返回新版本号（vYYYYMMDDHHmmss）。</summary>
    public async Task<string> PublishAsync(string? publishedBy, DateTime now)
    {
        var version = "v" + now.ToString("yyyyMMddHHmmss");
        var verDir = Path.Combine(RootDir, version);
        Directory.CreateDirectory(verDir);

        using var scope = _scopeFactory.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<CP6Context>();
        var items = await ctx.Sys_Langs.AsNoTracking().Where(l => l.TenantId == null).ToListAsync();

        foreach (var lang in LangColumn.Codes)
        {
            var dict = LangColumn.ToDict(items, lang);
            var json = JsonSerializer.Serialize(dict);
            await File.WriteAllTextAsync(Path.Combine(verDir, $"{lang}.json"), json);
        }

        var manifest = new LangManifest
        {
            Version = version,
            Langs = LangColumn.Codes,
            PublishedAt = now,
            PublishedBy = publishedBy,
            Count = items.Count,
        };
        await File.WriteAllTextAsync(ManifestPath, JsonSerializer.Serialize(manifest));
        return version;
    }

    /// <summary>读取当前 manifest（未发布过返回 null）。</summary>
    public LangManifest? GetManifest()
    {
        if (!File.Exists(ManifestPath)) return null;
        try { return JsonSerializer.Deserialize<LangManifest>(File.ReadAllText(ManifestPath)); }
        catch { return null; }
    }

    /// <summary>读取某版本某语言的已发布 JSON 原文（不存在返回 null）。</summary>
    public string? ReadPublished(string version, string lang)
    {
        // 防目录穿越：版本号/语言码白名单化
        if (!IsSafeSegment(version) || !LangColumn.Codes.Contains(lang)) return null;
        var path = Path.Combine(RootDir, version, $"{lang}.json");
        return File.Exists(path) ? File.ReadAllText(path) : null;
    }

    /// <summary>把 manifest 指回某历史版本（回滚）。该版本目录须存在。</summary>
    public bool Rollback(string version, string? operatedBy, DateTime now)
    {
        if (!IsSafeSegment(version) || !Directory.Exists(Path.Combine(RootDir, version))) return false;
        var m = GetManifest() ?? new LangManifest();
        m.Version = version;
        m.PublishedAt = now;
        m.PublishedBy = operatedBy;
        m.Langs = LangColumn.Codes;
        File.WriteAllText(ManifestPath, JsonSerializer.Serialize(m));
        return true;
    }

    private static bool IsSafeSegment(string s) =>
        !string.IsNullOrEmpty(s) && s.All(c => char.IsLetterOrDigit(c) || c == '-' || c == '_');
}

public class LangManifest
{
    public string Version { get; set; } = "";
    public string[] Langs { get; set; } = Array.Empty<string>();
    public DateTime PublishedAt { get; set; }
    public string? PublishedBy { get; set; }
    public int Count { get; set; }
}
