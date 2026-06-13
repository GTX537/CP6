namespace CP6.Core.Services.Sys;

/// <summary>
/// 数据范围资源注册表 —— PUB 章03 §8。各业务模块启动时注册可做数据权限的资源
/// （资源键 + 显示名 + 支持的范围类型 + 默认范围）。配置页据此渲染、保存时校验。
/// </summary>
public static class DataScopeRegistry
{
    public record Resource(string Key, string Name, int[] Supports, int Default);

    private static readonly Dictionary<string, Resource> _map = new();

    /// <summary>注册一个数据权限资源（幂等覆盖）。</summary>
    public static void Register(string key, string name, int[] supports, int @default)
        => _map[key] = new Resource(key, name, supports, @default);

    /// <summary>全部已注册资源（配置页用）。</summary>
    public static IReadOnlyCollection<Resource> All => _map.Values;

    /// <summary>资源未在角色配置中出现时的默认范围（未注册资源回落 1=本人，最严）。</summary>
    public static int DefaultScope(string key) => _map.TryGetValue(key, out var r) ? r.Default : 1;

    /// <summary>校验某范围类型是否被该资源支持（未注册资源放行，由上层决定）。</summary>
    public static bool Supports(string key, int scopeType)
        => !_map.TryGetValue(key, out var r) || r.Supports.Contains(scopeType);
}
