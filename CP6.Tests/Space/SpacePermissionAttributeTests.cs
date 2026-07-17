using System.Reflection;
using CP6.Core.Auth;
using CP6.WebApi.Controllers.Space;
using Microsoft.AspNetCore.Mvc;

namespace CP6.Tests.Space;

/// <summary>
/// 反射守卫（波4 权限点接线）：扫 CP6.WebApi 程序集 Controllers.Space 命名空间全部 controller。
/// ① 每个变更端点（HttpPost/HttpPut/HttpDelete）必须带 [RequirePermission]，且 (menu,action)
///    落在映射白名单集合内（与 2026-07-07-space-wave4-crosscutting.md Global Constraints 映射表逐字一致）。
/// ② 每个只读端点（HttpGet，且非变更）必须**不带** [RequirePermission]（防误贴）。
///
/// 断言方式说明：RequirePermissionAttribute 的 menu/action 为 private field，实例反射不可读。
/// 故用 <see cref="CustomAttributeData"/> 读取该特性的构造参数 (menu, action)，实现白名单逐字校验
/// （非降级到存在性）。
///
/// 豁免清单（显式，只读语义的 POST）：
///   - CodeRuleController.Preview —— POST code-rule/preview 仅合成样例、不写库，按只读语义仅 [Authorize]。
///     豁免项按「不得带特性」校验（与 GET 同待遇）。
/// </summary>
public class SpacePermissionAttributeTests
{
    /// <summary>映射白名单 "menu:action"——计划 Global Constraints 映射表逐字。</summary>
    private static readonly HashSet<string> Whitelist = new()
    {
        "space-site:add", "space-site:edit", "space-site:delete",
        "space-floor:add", "space-floor:edit", "space-floor:delete",
        "space-code-rule:add", "space-code-rule:edit", "space-code-rule:delete",
        "space-code-rule:generate",
        "space-publish:publish", "space-publish:deactivate", "space-publish:adopt",
    };

    /// <summary>只读语义的 POST 豁免（Controller.Method）——按「不得带特性」校验。</summary>
    private static readonly HashSet<string> ReadOnlyPostExemptions = new()
    {
        "CodeRuleController.Preview",
    };

    private static IEnumerable<Type> SpaceControllers =>
        typeof(SpaceMasterController).Assembly.GetTypes()
            .Where(t => t.Namespace == "CP6.WebApi.Controllers.Space"
                        && typeof(ControllerBase).IsAssignableFrom(t)
                        && !t.IsAbstract)
            .OrderBy(t => t.Name);

    private static IEnumerable<MethodInfo> ActionMethods(Type t) =>
        t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => !m.IsSpecialName);

    private static bool IsMutating(MethodInfo m) =>
        m.GetCustomAttributes<HttpPostAttribute>().Any()
        || m.GetCustomAttributes<HttpPutAttribute>().Any()
        || m.GetCustomAttributes<HttpPatchAttribute>().Any()   // X-SWEEP T1：补 PATCH，杜绝未来 [HttpPatch] 写端点静默逃出扫描面
        || m.GetCustomAttributes<HttpDeleteAttribute>().Any();

    private static bool IsGet(MethodInfo m) => m.GetCustomAttributes<HttpGetAttribute>().Any();

    private static bool IsExempt(Type c, MethodInfo m) =>
        ReadOnlyPostExemptions.Contains($"{c.Name}.{m.Name}");

    /// <summary>经 CustomAttributeData 读构造参数 (menu, action)；无特性返回 null。</summary>
    private static (string menu, string action)? ReadPermission(MethodInfo m)
    {
        var data = CustomAttributeData.GetCustomAttributes(m)
            .FirstOrDefault(d => d.AttributeType == typeof(RequirePermissionAttribute));
        if (data == null) return null;
        var args = data.ConstructorArguments;
        return ((string)args[0].Value!, (string)args[1].Value!);
    }

    [Fact]
    public void SpaceControllers_AreDiscovered()
    {
        // 守卫：确保反射确实扫到 9 个 controller（防命名空间/程序集变动导致「空扫空过」）。
        Assert.Equal(9, SpaceControllers.Count());
    }

    [Fact]
    public void EveryMutatingAction_HasRequirePermission_InWhitelist()
    {
        var offenders = new List<string>();
        foreach (var c in SpaceControllers)
        foreach (var m in ActionMethods(c).Where(IsMutating))
        {
            if (IsExempt(c, m)) continue; // 豁免项在专门用例校验「不得带」
            var perm = ReadPermission(m);
            if (perm == null)
            {
                offenders.Add($"{c.Name}.{m.Name}：变更端点缺 [RequirePermission]");
                continue;
            }
            var key = $"{perm.Value.menu}:{perm.Value.action}";
            if (!Whitelist.Contains(key))
                offenders.Add($"{c.Name}.{m.Name}：键 '{key}' 不在映射白名单");
        }
        Assert.True(offenders.Count == 0, "变更端点权限点缺失/越界:\n" + string.Join("\n", offenders));
    }

    [Fact]
    public void NoReadOnlyAction_HasRequirePermission()
    {
        var offenders = new List<string>();
        foreach (var c in SpaceControllers)
        foreach (var m in ActionMethods(c))
        {
            var readOnly = (IsGet(m) && !IsMutating(m)) || IsExempt(c, m);
            if (readOnly && ReadPermission(m) != null)
                offenders.Add($"{c.Name}.{m.Name}：只读端点误贴 [RequirePermission]");
        }
        Assert.True(offenders.Count == 0, "只读端点误贴权限点:\n" + string.Join("\n", offenders));
    }
}
