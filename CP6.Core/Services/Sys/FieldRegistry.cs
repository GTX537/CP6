namespace CP6.Core.Services.Sys;

/// <summary>
/// 字段权限资源注册表 —— PUB 章04 §7。各业务模块注册可做字段权限的资源及其可控字段。
/// 配置页据此渲染；保存时校验字段 ∈ 注册表。
/// </summary>
public static class FieldRegistry
{
    public record Field(string Name, string Label);

    private static readonly Dictionary<string, List<Field>> _map = new();

    /// <summary>注册某资源的可控字段（幂等覆盖）。</summary>
    public static void Register(string resourceKey, params Field[] fields) => _map[resourceKey] = fields.ToList();

    /// <summary>某资源的可控字段列表。</summary>
    public static IReadOnlyList<Field> Fields(string resourceKey) =>
        _map.TryGetValue(resourceKey, out var l) ? l : new List<Field>();

    /// <summary>字段是否在该资源注册表内。</summary>
    public static bool Has(string resourceKey, string field) =>
        _map.TryGetValue(resourceKey, out var l) && l.Any(f => f.Name == field);

    /// <summary>全部已注册资源键。</summary>
    public static IReadOnlyCollection<string> Resources => _map.Keys;
}
