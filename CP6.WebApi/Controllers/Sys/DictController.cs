using CP6.Core.EFDbContext;
using CP6.Core.Utilities;
using CP6.Entity.DomainModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CP6.WebApi.Controllers.Sys;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class DictController : ControllerBase
{
    private readonly CP6Context _context;
    private readonly CacheService _cache;

    public DictController(CP6Context context, CacheService cache)
    {
        _context = context;
        _cache = cache;
    }

    // ========== 字典类型 ==========

    [HttpGet("types")]
    public async Task<IActionResult> GetTypes(int page = 1, int pageSize = 10, string? keyword = null)
    {
        var query = _context.Sys_DictTypes.AsQueryable();
        if (!string.IsNullOrEmpty(keyword))
            query = query.Where(t => t.TypeCode.Contains(keyword) || t.TypeName.Contains(keyword));

        var total = await query.CountAsync();
        var data = await query
            .OrderBy(t => t.OrderNo)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return Ok(new { rows = data, total });
    }

    [HttpPost("types")]
    public async Task<IActionResult> AddType([FromBody] Sys_DictType entity)
    {
        if (await _context.Sys_DictTypes.AnyAsync(t => t.TypeCode == entity.TypeCode))
            return BadRequest(new { message = "类型编码已存在" });

        entity.CreateDate = DateTime.Now;
        _context.Sys_DictTypes.Add(entity);
        await _context.SaveChangesAsync();
        return Ok(entity);
    }

    [HttpPut("types")]
    public async Task<IActionResult> UpdateType([FromBody] Sys_DictType entity)
    {
        var existing = await _context.Sys_DictTypes.FindAsync(entity.Id);
        if (existing == null) return NotFound();

        existing.TypeCode = entity.TypeCode;
        existing.TypeName = entity.TypeName;
        existing.Enable = entity.Enable;
        existing.OrderNo = entity.OrderNo;

        await _context.SaveChangesAsync();
        return Ok(existing);
    }

    [HttpDelete("types")]
    public async Task<IActionResult> DeleteTypes([FromBody] int[] ids)
    {
        var types = await _context.Sys_DictTypes.Where(t => ids.Contains(t.Id)).ToListAsync();
        var typeCodes = types.Select(t => t.TypeCode).ToList();

        // 同时删除该类型下的字典数据
        var datas = _context.Sys_DictDatas.Where(d => typeCodes.Contains(d.TypeCode));
        _context.Sys_DictDatas.RemoveRange(datas);
        _context.Sys_DictTypes.RemoveRange(types);

        var count = await _context.SaveChangesAsync();

        // 缓存失效：删除相关字典缓存
        foreach (var code in typeCodes)
            await _cache.RemoveAsync(CacheService.DictKeyPrefix + code);

        return Ok(new { count });
    }

    // ========== 字典数据 ==========

    [HttpGet("data")]
    public async Task<IActionResult> GetData(string typeCode, int page = 1, int pageSize = 20, string? keyword = null)
    {
        var query = _context.Sys_DictDatas.Where(d => d.TypeCode == typeCode);
        if (!string.IsNullOrEmpty(keyword))
            query = query.Where(d => d.Label.Contains(keyword) || d.Value.Contains(keyword));

        var total = await query.CountAsync();
        var data = await query
            .OrderBy(d => d.OrderNo)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return Ok(new { rows = data, total });
    }

    [HttpPost("data")]
    public async Task<IActionResult> AddData([FromBody] Sys_DictData entity)
    {
        entity.CreateDate = DateTime.Now;
        _context.Sys_DictDatas.Add(entity);
        await _context.SaveChangesAsync();

        // 缓存失效：字典数据变更后清除该类型缓存
        await _cache.RemoveAsync(CacheService.DictKeyPrefix + entity.TypeCode);

        return Ok(entity);
    }

    [HttpPut("data")]
    public async Task<IActionResult> UpdateData([FromBody] Sys_DictData entity)
    {
        var existing = await _context.Sys_DictDatas.FindAsync(entity.Id);
        if (existing == null) return NotFound();

        existing.Value = entity.Value;
        existing.Label = entity.Label;
        existing.OrderNo = entity.OrderNo;
        existing.Enable = entity.Enable;

        await _context.SaveChangesAsync();

        await _cache.RemoveAsync(CacheService.DictKeyPrefix + existing.TypeCode);

        return Ok(existing);
    }

    [HttpDelete("data")]
    public async Task<IActionResult> DeleteData([FromBody] int[] ids)
    {
        var entities = await _context.Sys_DictDatas.Where(d => ids.Contains(d.Id)).ToListAsync();
        var typeCodes = entities.Select(d => d.TypeCode).Distinct().ToList();

        _context.Sys_DictDatas.RemoveRange(entities);
        var count = await _context.SaveChangesAsync();

        // 缓存失效
        foreach (var code in typeCodes)
            await _cache.RemoveAsync(CacheService.DictKeyPrefix + code);

        return Ok(new { count });
    }

    // ========== 公共接口：按类型编码获取选项列表（带缓存） ==========

    /// <summary>
    /// 获取指定类型的字典选项（给下拉框用）
    ///
    /// Cache-Aside 模式：
    /// 1. 查缓存 dict:gender → 有则直接返回
    /// 2. 缓存没有 → 查 DB → 写缓存 → 返回
    /// 3. 字典数据变更时 → 自动清除 dict:gender 缓存
    /// </summary>
    [HttpGet("options/{typeCode}")]
    public async Task<IActionResult> GetOptions(string typeCode)
    {
        var cacheKey = CacheService.DictKeyPrefix + typeCode;

        var options = await _cache.GetOrSetAsync(cacheKey, async () =>
        {
            return await _context.Sys_DictDatas
                .Where(d => d.TypeCode == typeCode && d.Enable)
                .OrderBy(d => d.OrderNo)
                .Select(d => new DictOptionDto(d.Value, d.Label))
                .ToListAsync();
        }, TimeSpan.FromMinutes(30));

        return Ok(options);
    }

    // Dapper 和缓存都需要有明确类型做映射
    public record DictOptionDto(string Value, string Label);
}
