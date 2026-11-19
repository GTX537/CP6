using CP6.Core.Services.Sys;
using CP6.Core.Services.Erp;
using CP6.WebApi.Controllers.Erp;
using CP6.Core.Auth;
using CP6.Core.EFDbContext;
using CP6.Core.Utilities;
using CP6.Entity.DomainModels;
using CP6.WebApi.Services;
using System.Data.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CP6.WebApi.Controllers.Sys;

[ApiController]
[OrderOwnerResult]
[Route("api/[controller]")]
[Authorize]
public class DictController : LocalizedControllerBase
{
    private readonly CP6Context _context;
    private readonly CacheService _cache;
    private readonly DictionaryMutationOwnerV1? _owner;

    public DictController(CP6Context context, CacheService cache, DictionaryMutationOwnerV1? owner = null)
    {
        _context = context;
        _cache = cache;
        _owner = owner;
    }

    private Task<IActionResult> Mutate(string action, Func<CancellationToken, Task<IActionResult>> body)
    {
        if (!_context.Database.IsNpgsql()) return body(CancellationToken.None);
        if (_owner?.Owns(_context) != true)
            throw new OrderPriceCorrectionException("DICT_AUTHORITY_UNAVAILABLE", 503);
        return _owner.ExecuteAsync(action, body, HttpContext.RequestAborted);
    }

    [HttpGet("owner-v1/options/{typeCode}")]
    public async Task<IActionResult> ReadDictionary(string typeCode, [FromServices] DictionaryCommandsV1 commands,
        [FromQuery] string? historicalValue = null)
        => Ok(await commands.Options(typeCode, historicalValue, HttpContext.RequestAborted));

    [HttpPost("owner-v1/commands")]
    public async Task<IActionResult> ApplyDictionary(DictionaryCommandV1 input, [FromServices] DictionaryCommandsV1 commands)
        => Ok(await commands.Apply(input, HttpContext.RequestAborted));

    [HttpGet("owner-v1/operations/{operation:guid}")]
    public async Task<IActionResult> RecoverDictionary(Guid operation, [FromServices] DictionaryCommandsV1 commands)
    {
        try { return Ok(await commands.Recover(HttpContext, operation)); }
        catch (IamAdministrationExceptionV1 error)
        { throw new OrderPriceCorrectionException(error.Message, error.Status, error); }
        catch (DbException error)
        { throw new OrderPriceCorrectionException("DICT_AUTHORITY_UNAVAILABLE", 503, error); }
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
    [RequirePermission("dict", "add")]
    public async Task<IActionResult> AddType([FromBody] Sys_DictType entity)
    {
        return await Mutate("add", async token =>
        {
            if (await _context.Sys_DictTypes.AnyAsync(t => t.TypeCode == entity.TypeCode, token))
                return BadRequest(new { message = Localizer["类型编码已存在"] });
            entity.CreateDate = DateTime.Now;
            _context.Sys_DictTypes.Add(entity);
            await _context.SaveChangesAsync(token);
            return Ok(entity);
        });
    }

    [HttpPut("types")]
    [RequirePermission("dict", "edit")]
    public async Task<IActionResult> UpdateType([FromBody] Sys_DictType entity)
    {
        return await Mutate("edit", async token =>
        {
            var existing = await _context.Sys_DictTypes.FindAsync(new object?[] { entity.Id }, token);
            if (existing == null) return NotFound();
            if (_context.Database.IsNpgsql() && await _context.Sys_DictTypes.AnyAsync(t => t.Id != entity.Id && t.TypeCode == entity.TypeCode, token))
                return BadRequest(new { message = Localizer["类型编码已存在"] });
            if (_context.Database.IsNpgsql() && existing.TypeCode != entity.TypeCode)
                throw new OrderPriceCorrectionException("DICT_STABLE_TYPE_CODE", 409);
            existing.TypeCode = entity.TypeCode;
            existing.TypeName = entity.TypeName;
            existing.Enable = entity.Enable;
            existing.OrderNo = entity.OrderNo;
            await _context.SaveChangesAsync(token);
            return Ok(existing);
        });
    }

    [HttpDelete("types")]
    [RequirePermission("dict", "delete")]
    public async Task<IActionResult> DeleteTypes([FromBody] int[] ids)
    {
        var typeCodes = new List<string>();
        var result = await Mutate("delete", async token =>
        {
            var types = await _context.Sys_DictTypes.Where(t => ids.Contains(t.Id)).ToListAsync(token);
            typeCodes = types.Select(t => t.TypeCode).ToList();
            if (_context.Database.IsNpgsql())
                await new DictionaryCatalogV1(_context).RequireUnreferenced(types.Select(type => type.Id).ToArray(), token);
            var datas = _context.Sys_DictDatas.Where(d => typeCodes.Contains(d.TypeCode));
            _context.Sys_DictDatas.RemoveRange(datas);
            _context.Sys_DictTypes.RemoveRange(types);
            var count = await _context.SaveChangesAsync(token);
            return Ok(new { count });
        });
        foreach (var code in typeCodes)
            await _cache.RemoveAsync(CacheService.DictKeyPrefix + code);
        return result;
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
    [RequirePermission("dict", "add")]
    public async Task<IActionResult> AddData([FromBody] Sys_DictData entity)
    {
        var result = await Mutate("add", async token =>
        {
            if (_context.Database.IsNpgsql())
            {
                var count = await _context.Sys_DictTypes.CountAsync(type => type.TypeCode == entity.TypeCode, token);
                if (count != 1) throw new OrderPriceCorrectionException("DICT_TYPE_UNAVAILABLE", 409);
                var values = await _context.Sys_DictDatas.Where(item => item.TypeCode == entity.TypeCode)
                    .Select(item => item.Value).ToListAsync(token);
                if (values.Contains(entity.Value, StringComparer.Ordinal))
                    throw new OrderPriceCorrectionException("DICT_VALUE_EXISTS", 409);
            }
            entity.CreateDate = DateTime.Now;
            _context.Sys_DictDatas.Add(entity);
            await _context.SaveChangesAsync(token);
            return Ok(entity);
        });
        await _cache.RemoveAsync(CacheService.DictKeyPrefix + entity.TypeCode);
        return result;
    }

    [HttpPut("data")]
    [RequirePermission("dict", "edit")]
    public async Task<IActionResult> UpdateData([FromBody] Sys_DictData entity)
    {
        string? typeCode = null;
        var result = await Mutate("edit", async token =>
        {
            var existing = await _context.Sys_DictDatas.FindAsync(new object?[] { entity.Id }, token);
            if (existing == null) return NotFound();
            if (_context.Database.IsNpgsql() && existing.Value != entity.Value)
                throw new OrderPriceCorrectionException("DICT_STABLE_VALUE", 409);
            existing.Value = entity.Value;
            existing.Label = entity.Label;
            existing.OrderNo = entity.OrderNo;
            existing.Enable = entity.Enable;
            typeCode = existing.TypeCode;
            await _context.SaveChangesAsync(token);
            return Ok(existing);
        });
        if (typeCode != null)
            await _cache.RemoveAsync(CacheService.DictKeyPrefix + typeCode);
        return result;
    }

    [HttpDelete("data")]
    [RequirePermission("dict", "delete")]
    public async Task<IActionResult> DeleteData([FromBody] int[] ids)
    {
        var typeCodes = new List<string>();
        var result = await Mutate("delete", async token =>
        {
            var entities = await _context.Sys_DictDatas.Where(d => ids.Contains(d.Id)).ToListAsync(token);
            typeCodes = entities.Select(d => d.TypeCode).Distinct().ToList();
            if (_context.Database.IsNpgsql())
            {
                var typeIds = await _context.Sys_DictTypes.Where(type => typeCodes.Contains(type.TypeCode))
                    .Select(type => type.Id).ToArrayAsync(token);
                await new DictionaryCatalogV1(_context).RequireUnreferenced(typeIds, token);
            }
            _context.Sys_DictDatas.RemoveRange(entities);
            var count = await _context.SaveChangesAsync(token);
            return Ok(new { count });
        });
        foreach (var code in typeCodes)
            await _cache.RemoveAsync(CacheService.DictKeyPrefix + code);
        return result;
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
        if (_context.Database.IsNpgsql())
            return Ok(await new DictionaryCatalogV1(_context).Active(typeCode)
                .OrderBy(item => item.OrderNo).ThenBy(item => item.Id)
                .Select(item => new DictOptionDto(item.Value, item.Label)).ToListAsync(HttpContext.RequestAborted));

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
