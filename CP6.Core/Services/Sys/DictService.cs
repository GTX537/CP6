using CP6.Core.EFDbContext;
using CP6.Entity.DomainModels.Sys;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace CP6.Core.Services.Sys;

/// <summary>字典服务实现 —— PUB 章05。IMemoryCache 按 TypeCode 缓存（字段为 Value/Label，非 DictValue/DictLabel）。</summary>
public class DictService : IDictService
{
    private readonly CP6Context _db;
    private readonly IMemoryCache _cache;

    public DictService(CP6Context db, IMemoryCache cache)
    {
        _db = db;
        _cache = cache;
    }

    public async Task<List<Sys_DictData>> GetItemsAsync(string typeCode) =>
        (await _cache.GetOrCreateAsync(Key(typeCode), async e =>
        {
            e.SlidingExpiration = TimeSpan.FromMinutes(30);
            return await _db.Sys_DictDatas
                .Where(d => d.TypeCode == typeCode && d.Enable)
                .OrderBy(d => d.OrderNo)
                .ToListAsync();
        }))!;

    public async Task<string?> TranslateAsync(string typeCode, string? value)
    {
        if (value == null) return null;
        var items = await GetItemsAsync(typeCode);
        return items.FirstOrDefault(d => d.Value == value)?.Label ?? value;   // 未命中回原值
    }

    public void InvalidateType(string typeCode) => _cache.Remove(Key(typeCode));

    private static string Key(string typeCode) => $"dict:{typeCode}";
}
