namespace CP6.Core.Services.Sys;

/// <summary>
/// 字段权限执行 —— PUB 章04 §4/§5。
/// MaskHidden：返回前把 Access=3（隐藏）字段反射置空（序列化掩码）。
/// StripReadOnly：保存前把 Access&gt;=2（只读/隐藏）字段用 DB 原值覆盖（拒写）。
/// </summary>
public interface IFieldPermService
{
    /// <summary>掩码隐藏字段（单对象或集合）—— 取当前用户上下文。</summary>
    Task MaskHiddenAsync(object? data, string resource);

    /// <summary>掩码隐藏字段（显式上下文，可单测）。</summary>
    void MaskHidden(object? data, string resource, UserPermissionContext ctx);

    /// <summary>拒写只读/隐藏字段：incoming 中受限字段用 original 覆盖 —— 取当前用户上下文。</summary>
    Task StripReadOnlyAsync(object incoming, object original, string resource);

    /// <summary>拒写只读/隐藏字段（显式上下文，可单测）。</summary>
    void StripReadOnly(object incoming, object original, string resource, UserPermissionContext ctx);
}
