namespace CP6.Core.Services.Sys;

/// <summary>
/// 数据范围资源注册表 —— PUB 章03 §8。各业务模块启动时注册可做数据权限的资源
/// （资源键 + 显示名 + 支持的范围类型 + 默认范围）。配置页据此渲染、保存时校验。
/// </summary>
public static class DataScopeRegistry
{
    public record Resource(string Key, string Name, int[] Supports, int Default);

    /// <summary>启动期注册；宿主封存后只允许内容相同的幂等注册。</summary>
    public static void Register(string key, string name, int[] supports, int @default)
        => AuthorizationCatalogV1.Store.RegisterScope(key, name, supports, @default);

    /// <summary>全部已注册资源（配置页用）。</summary>
    public static IReadOnlyCollection<Resource> All => Array.AsReadOnly(AuthorizationCatalogV1.Capture().Scopes.Values
        .Select(x => new Resource(x.Key, x.Name, x.Supports.ToArray(), x.Default)).ToArray());

    /// <summary>资源未在角色配置中出现时的默认范围（未注册资源回落 1=本人，最严）。</summary>
    public static int DefaultScope(string key) => AuthorizationCatalogV1.Capture().Scopes.TryGetValue(key, out var r) ? r.Default : 1;

    /// <summary>校验某范围类型是否被该资源支持（未注册资源放行，由上层决定）。</summary>
    public static bool Supports(string key, int scopeType)
        => !AuthorizationCatalogV1.Capture().Scopes.TryGetValue(key, out var r) || r.Supports.Contains(scopeType);
}
