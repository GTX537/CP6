using CP6.Core.EFDbContext;
using CP6.Core.Utilities;
using CP6.Entity.DomainModels;
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

    public LangController(CP6Context context, CacheService cache)
    {
        _context = context;
        _cache = cache;
    }

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
            var items = await _context.Sys_Langs.ToListAsync();
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
    public async Task<IActionResult> Add([FromBody] Sys_Lang entity)
    {
        if (await _context.Sys_Langs.AnyAsync(l => l.LangKey == entity.LangKey))
            return BadRequest(new { message = "词条Key已存在" });

        _context.Sys_Langs.Add(entity);
        await _context.SaveChangesAsync();

        // 缓存失效：词条变更后，清除所有语言缓存
        await InvalidateLangCache();

        return Ok(entity);
    }

    [HttpPut]
    [Authorize]
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

        await _context.SaveChangesAsync();

        await InvalidateLangCache();

        return Ok(existing);
    }

    [HttpDelete]
    [Authorize]
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
        foreach (var code in langCodes)
        {
            await _cache.RemoveAsync(CacheService.LangKeyPrefix + code);
        }
    }
}
