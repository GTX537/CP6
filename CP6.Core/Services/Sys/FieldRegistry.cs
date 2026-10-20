namespace CP6.Core.Services.Sys;

/// <summary>
/// 字段权限资源注册表 —— PUB 章04 §7。各业务模块注册可做字段权限的资源及其可控字段。
/// 配置页据此渲染；保存时校验字段 ∈ 注册表。
/// </summary>
public static class FieldRegistry
{
    public record Field(string Name, string Label);

    /// <summary>启动期注册；宿主封存后只允许内容相同的幂等注册。</summary>
    public static void Register(string resourceKey, params Field[] fields) => AuthorizationCatalogV1.Store.RegisterFields(resourceKey, fields);

    /// <summary>某资源的可控字段列表。</summary>
    public static IReadOnlyList<Field> Fields(string resourceKey) =>
        AuthorizationCatalogV1.Capture().Fields.TryGetValue(resourceKey, out var l) ? l : Array.Empty<Field>();

    /// <summary>字段是否在该资源注册表内。</summary>
    public static bool Has(string resourceKey, string field) =>
        AuthorizationCatalogV1.Capture().Fields.TryGetValue(resourceKey, out var l) && l.Any(f => f.Name == field);

    /// <summary>全部已注册资源键。</summary>
    public static IReadOnlyCollection<string> Resources => Array.AsReadOnly(AuthorizationCatalogV1.Capture().Fields.Keys.ToArray());
}
