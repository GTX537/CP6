using CP6.Entity.DomainModels;

namespace CP6.WebApi.Localization;

/// <summary>
/// i18n 优化 P4：多语言词条列/命名空间共享小工具（懒加载端点与发布管线共用）。
/// </summary>
public static class LangColumn
{
    public static readonly string[] Codes = { "zh-CN", "zh-TW", "en", "ja", "ko" };

    /// <summary>按路由懒加载的大模块命名空间（其余归 _core，启动即载）。前端 i18n/index.ts 须保持一致。</summary>
    public static readonly HashSet<string> LazyNamespaces = new(StringComparer.OrdinalIgnoreCase) { "wms", "sales", "erp", "mes" };

    /// <summary>取某语言对应列值。</summary>
    public static string? Pick(Sys_Lang it, string lang) => lang switch
    {
        "zh-CN" => it.ZhCN,
        "zh-TW" => it.ZhTW,
        "en" => it.En,
        "ja" => it.Ja,
        "ko" => it.Ko,
        _ => it.ZhCN
    };

    /// <summary>key 的命名空间 = 首个点前的片段；无点（日文/纯码 key）返回空串。</summary>
    public static string Namespace(string key)
    {
        var i = key.IndexOf('.');
        return i < 0 ? string.Empty : key[..i];
    }

    /// <summary>把词条行集合投影成某语言的 {key:value} 字典（跳过空值）。</summary>
    public static Dictionary<string, string> ToDict(IEnumerable<Sys_Lang> items, string lang)
    {
        var dict = new Dictionary<string, string>();
        foreach (var it in items)
        {
            var v = Pick(it, lang);
            if (!string.IsNullOrEmpty(v)) dict[it.LangKey] = v!;
        }
        return dict;
    }
}
