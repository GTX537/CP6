using CP6.Core.Auth;
using CP6.Core.EFDbContext;
using CP6.Core.Utilities;
using CP6.Entity.DomainModels;
using CP6.WebApi.Localization;
using CP6.WebApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CP6.WebApi.Controllers.Sys;

[ApiController]
[Route("api/[controller]")]
public class LangController : ControllerBase
{
    private readonly CP6Context _context;
    private readonly CacheService _cache;
    private readonly LangPublishService _publish;
    private readonly IConfiguration _config;

    public LangController(CP6Context context, CacheService cache, LangPublishService publish, IConfiguration config)
    {
        _context = context;
        _cache = cache;
        _publish = publish;
        _config = config;
    }

    /// <summary>i18n 优化 P5：是否只下发已审校词条（serve reviewed-only 质量门）。默认 false=全部下发，零行为变化。</summary>
    private bool ReviewedOnly => _config.GetValue<bool>("I18n:ServeReviewedOnly");

    /// <summary>
    /// 按语言代码获取所有翻译（前端加载用，不需要登录）
    ///
    /// 缓存策略：Cache-Aside 模式
    /// - 第一次访问：查 DB → 写缓存 → 返回
    /// - 后续访问：直接返回缓存（不查 DB）
    /// - 数据变更：主动清除缓存（Add/Update/Delete 时）
    /// </summary>
    [HttpGet("{langCode}")]
    [AllowAnonymous]
    public async Task<IActionResult> GetByLang(string langCode)
    {
        var cacheKey = CacheService.LangKeyPrefix + langCode;

        // GetOrSetAsync = Cache-Aside 模式的完整实现
        var dict = await _cache.GetOrSetAsync(cacheKey, async () =>
        {
            // i18n 优化 P3：前端全局语言包只取全局值（TenantId=null），不泄露租户覆盖。
            // 缓存键 lang:{code} 与 DbStringLocalizer 全局字典共用，两侧一致。
            // i18n 优化 P5：可选只下发已审校（默认关）。
            var q = _context.Sys_Langs.Where(l => l.TenantId == null);
            if (ReviewedOnly) q = q.Where(l => l.Status == "reviewed");
            var items = await q.ToListAsync();
            var result = new Dictionary<string, string>();

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
                if (!string.IsNullOrEmpty(value))
                    result[item.LangKey] = value;
            }
            return result;
        }, TimeSpan.FromHours(1));  // 语言词条变化少，缓存1小时

        return Ok(dict);
    }

    /// <summary>
    /// i18n 优化 P4 懒加载：按命名空间取词条。
    /// ns = 单命名空间（如 wms）→ 首段==ns 的 key；ns = _core → 非大模块(wms/sales/erp/mes)的全部(含无点日文 key)。
    /// 与全局 feed 一样只取全局值(TenantId=null)，复用缓存（键 lang:ns:{ns}:{code}）。
    /// </summary>
    [HttpGet("{lang}/ns/{ns}")]
    [AllowAnonymous]
    public async Task<IActionResult> GetByLangNamespace(string lang, string ns)
    {
        var cacheKey = $"{CacheService.LangKeyPrefix}ns:{ns}:{lang}";
        var dict = await _cache.GetOrSetAsync(cacheKey, async () =>
        {
            var q = _context.Sys_Langs.AsNoTracking().Where(l => l.TenantId == null);
            if (ReviewedOnly) q = q.Where(l => l.Status == "reviewed");   // i18n 优化 P5：可选审校门控
            var items = await q.ToListAsync();
            IEnumerable<Sys_Lang> filtered = ns == "_core"
                ? items.Where(i => !LangColumn.LazyNamespaces.Contains(LangColumn.Namespace(i.LangKey)))
                : items.Where(i => string.Equals(LangColumn.Namespace(i.LangKey), ns, StringComparison.OrdinalIgnoreCase));
            return LangColumn.ToDict(filtered, lang);
        }, TimeSpan.FromHours(1));
        return Ok(dict);
    }

    /// <summary>i18n 优化 P4 发布模式：当前已发布版本指针（未发布过返回 version=null）。前端拉它再取版本化静态包。</summary>
    [HttpGet("manifest")]
    [AllowAnonymous]
    public IActionResult GetManifest()
    {
        Response.Headers.CacheControl = "no-cache";   // 版本指针必须实时
        var m = _publish.GetManifest();
        return Ok(m ?? new LangManifest { Version = "" });
    }

    /// <summary>i18n 优化 P4 发布模式：取某版本某语言的已发布静态包（不可变长缓存，便于浏览器/CDN 缓存）。</summary>
    [HttpGet("published/{version}/{lang}")]
    [AllowAnonymous]
    public IActionResult GetPublished(string version, string lang)
    {
        var json = _publish.ReadPublished(version, lang);
        if (json == null) return NotFound();
        Response.Headers.CacheControl = "public, max-age=31536000, immutable";   // 版本化 URL → 永久缓存
        return Content(json, "application/json; charset=utf-8");
    }

    /// <summary>i18n 优化 P4 发布模式：触发一次发布（导出版本化静态包 + 更新 manifest）。</summary>
    [HttpPost("publish")]
    [Authorize]
    [RequirePermission("lang", "publish")]
    public async Task<IActionResult> Publish()
    {
        var version = await _publish.PublishAsync(User?.Identity?.Name, DateTime.Now);
        return Ok(new { code = 0, message = "OK", data = new { version } });
    }

    /// <summary>i18n 优化 P4 发布模式：回滚 manifest 指向某历史版本（旧静态包仍在）。</summary>
    [HttpPost("publish/rollback")]
    [Authorize]
    [RequirePermission("lang", "publish")]
    public IActionResult Rollback([FromBody] RollbackRequest req)
    {
        var ok = _publish.Rollback(req.Version, User?.Identity?.Name, DateTime.Now);
        return ok ? Ok(new { code = 0, message = "OK", data = new { version = req.Version } })
                  : BadRequest(new { code = 400, message = "version not found" });
    }

    public class RollbackRequest { public string Version { get; set; } = ""; }

    /// <summary>i18n 优化 P5：审校动作 —— 标记为已审校（draft→reviewed）+ 记审计 + 清缓存。
    /// ids 为空/缺省 = 审校全部 draft 词条。</summary>
    [HttpPost("review")]
    [Authorize]
    [RequirePermission("lang", "review")]
    public async Task<IActionResult> Review([FromBody] int[]? ids)
    {
        var rows = (ids != null && ids.Length > 0)
            ? await _context.Sys_Langs.Where(l => ids.Contains(l.Id)).ToListAsync()
            : await _context.Sys_Langs.Where(l => l.Status != "reviewed").ToListAsync();
        var now = DateTime.Now;
        var by = User?.Identity?.Name;
        foreach (var r in rows)
        {
            r.Status = "reviewed";
            r.UpdatedBy = by;
            r.UpdatedAt = now;
        }
        var count = await _context.SaveChangesAsync();
        await InvalidateLangCache();
        return Ok(new { code = 0, message = "OK", data = new { count = rows.Count } });
    }

    /// <summary>
    /// 获取所有词条列表（管理页面用，不缓存）
    /// </summary>
    [HttpGet]
    [Authorize]
    public async Task<IActionResult> GetList(int page = 1, int pageSize = 20, string? keyword = null)
    {
        var query = _context.Sys_Langs.AsQueryable();
        if (!string.IsNullOrEmpty(keyword))
            query = query.Where(l => l.LangKey.Contains(keyword)
                || (l.ZhCN != null && l.ZhCN.Contains(keyword))
                || (l.En != null && l.En.Contains(keyword)));

        var total = await query.CountAsync();
        var data = await query
            .OrderBy(l => l.LangKey)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return Ok(new { rows = data, total });
    }

    [HttpPost]
    [Authorize]
    [RequirePermission("lang", "update")]
    public async Task<IActionResult> Add([FromBody] Sys_Lang entity)
    {
        // i18n 优化 P1 样例：抛错误码而非日文/中文字面量，由 BizExceptionMiddleware 按请求 culture 本地化。
        // P3：唯一性按 (TenantId, LangKey)，允许同一 key 的全局值 + 各租户覆盖值并存。
        if (await _context.Sys_Langs.AnyAsync(l => l.TenantId == entity.TenantId && l.LangKey == entity.LangKey))
            throw new CP6.WebApi.Localization.BizException("lang.keyExists");

        // i18n 优化 P3：手动新增默认 draft（待审校）+ 记审计。
        entity.Status = "draft";
        entity.UpdatedBy = User?.Identity?.Name;
        entity.UpdatedAt = DateTime.Now;
        _context.Sys_Langs.Add(entity);
        await _context.SaveChangesAsync();

        // 缓存失效：词条变更后，清除所有语言缓存
        await InvalidateLangCache();

        return Ok(entity);
    }

    [HttpPut]
    [Authorize]
    [RequirePermission("lang", "update")]
    public async Task<IActionResult> Update([FromBody] Sys_Lang entity)
    {
        var existing = await _context.Sys_Langs.FindAsync(entity.Id);
        if (existing == null) return NotFound();

        existing.LangKey = entity.LangKey;
        existing.ZhCN = entity.ZhCN;
        existing.ZhTW = entity.ZhTW;
        existing.En = entity.En;
        existing.Ja = entity.Ja;
        existing.Ko = entity.Ko;
        // i18n 优化 P3：手动编辑视为未审校 → 置 draft（审校通过置 reviewed 留 P5 审校动作）+ 记审计。
        // TenantId 不在编辑中改（不重挂租户归属）。
        existing.Status = "draft";
        existing.UpdatedBy = User?.Identity?.Name;
        existing.UpdatedAt = DateTime.Now;

        await _context.SaveChangesAsync();

        await InvalidateLangCache();

        return Ok(existing);
    }

    [HttpDelete]
    [Authorize]
    [RequirePermission("lang", "delete")]
    public async Task<IActionResult> Delete([FromBody] int[] ids)
    {
        var entities = await _context.Sys_Langs.Where(l => ids.Contains(l.Id)).ToListAsync();
        _context.Sys_Langs.RemoveRange(entities);
        var count = await _context.SaveChangesAsync();

        await InvalidateLangCache();

        return Ok(new { count });
    }

    /// <summary>
    /// 清除所有语言缓存（5种语言全清）
    /// </summary>
    private async Task InvalidateLangCache()
    {
        var langCodes = new[] { "zh-CN", "zh-TW", "en", "ja", "ko" };
        // i18n 优化 P4：连带清理懒加载分包缓存（_core + 大模块命名空间），否则编辑后分包仍旧。
        var namespaces = new[] { "_core", "wms", "sales", "erp", "mes" };
        foreach (var code in langCodes)
        {
            await _cache.RemoveAsync(CacheService.LangKeyPrefix + code);
            foreach (var ns in namespaces)
                await _cache.RemoveAsync($"{CacheService.LangKeyPrefix}ns:{ns}:{code}");
        }
    }
}
