using System.Collections;
using System.Reflection;

namespace CP6.Core.Services.Sys;

/// <summary>字段权限执行实现 —— PUB 章04。反射掩码 + 拒写。</summary>
public class FieldPermService : IFieldPermService
{
    private readonly ICurrentPermissionContext _cur;
    public FieldPermService(ICurrentPermissionContext cur) => _cur = cur;

    public async Task MaskHiddenAsync(object? data, string resource) =>
        MaskHidden(data, resource, await _cur.GetAsync());

    public async Task StripReadOnlyAsync(object incoming, object original, string resource) =>
        StripReadOnly(incoming, original, resource, await _cur.GetAsync());

    public void MaskHidden(object? data, string resource, UserPermissionContext ctx)
    {
        if (data == null) return;
        if (!ctx.FieldPerms.TryGetValue(resource, out var perms)) return;
        var hidden = perms.Where(kv => kv.Value == 3).Select(kv => kv.Key).ToHashSet();
        if (hidden.Count == 0) return;

        foreach (var item in AsItems(data))
        {
            var type = item.GetType();
            foreach (var field in hidden)
            {
                var prop = type.GetProperty(field, BindingFlags.Public | BindingFlags.Instance);
                if (prop == null || !prop.CanWrite) continue;
                // 引用类型/可空值类型 → null；非可空值类型 → default
                var isNonNullableValue = prop.PropertyType.IsValueType
                                         && Nullable.GetUnderlyingType(prop.PropertyType) == null;
                prop.SetValue(item, isNonNullableValue ? Activator.CreateInstance(prop.PropertyType) : null);
            }
        }
    }

    public void StripReadOnly(object incoming, object original, string resource, UserPermissionContext ctx)
    {
        if (incoming == null || original == null) return;
        if (!ctx.FieldPerms.TryGetValue(resource, out var perms)) return;
        var locked = perms.Where(kv => kv.Value >= 2).Select(kv => kv.Key).ToHashSet();   // 只读(2)+隐藏(3) 均拒写
        if (locked.Count == 0) return;

        var type = incoming.GetType();
        foreach (var field in locked)
        {
            var prop = type.GetProperty(field, BindingFlags.Public | BindingFlags.Instance);
            if (prop == null || !prop.CanRead || !prop.CanWrite) continue;
            prop.SetValue(incoming, prop.GetValue(original));   // 用原值覆盖（拒写）
        }
    }

    /// <summary>把单对象/集合统一为可遍历的对象序列（string 视为单值）。</summary>
    private static IEnumerable<object> AsItems(object data)
    {
        if (data is IEnumerable e && data is not string)
        {
            foreach (var x in e)
                if (x != null) yield return x;
        }
        else
        {
            yield return data;
        }
    }
}
