using CP6.Entity.DomainModels.Sys;

namespace CP6.Core.Services.Sys;

/// <summary>
/// 字典服务 —— PUB 章05 §2.2。按 TypeCode 缓存字典项 + 值→标签翻译。
/// 供列表展示、Excel 导出（章07）等消费，避免每处自查字典表。
/// </summary>
public interface IDictService
{
    /// <summary>取某字典类型的启用项（按 OrderNo，带缓存）。</summary>
    Task<List<Sys_DictData>> GetItemsAsync(string typeCode);

    /// <summary>值 → 标签（命中返回 Label；未命中回原值；value 为 null 返回 null）。</summary>
    Task<string?> TranslateAsync(string typeCode, string? value);

    /// <summary>字典维护后失效该类型缓存。</summary>
    void InvalidateType(string typeCode);
}
