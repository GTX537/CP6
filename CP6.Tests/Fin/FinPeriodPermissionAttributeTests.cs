using System.Reflection;
using System.Text.RegularExpressions;
using CP6.Core.Auth;
using CP6.WebApi.Controllers.Fin;
using Microsoft.AspNetCore.Mvc;

namespace CP6.Tests.Fin;

/// <summary>
/// F1 波G G.1 fail-closed 反射守卫（照 <c>WmsPermissionAttributeTests</c> 泛化）：
/// 扫 <see cref="PeriodController"/>（本包新增两高危端点 year-close/reopen-year 所在控制器）全部变更端点，
/// 断言每个 HttpPost/Put/Delete **必须**带 <c>[RequirePermission]</c>，menu 符 <c>^fin-[a-z0-9-]+$</c>、
/// action 落在本控制器动作词汇集内。将来漏贴权限 = 测试红。
///
/// 范围仅 PeriodController（G.1 brief：勿扩大到全 Fin 存量——存量归 M-FIN 模块波）。
/// </summary>
public class FinPeriodPermissionAttributeTests
{
    private static readonly Regex MenuPattern = new("^fin-[a-z0-9-]+$", RegexOptions.Compiled);

    /// <summary>PeriodController 动作词汇集（close/reopen 存量 + year-close/reopen-year 本包新增）。</summary>
    private static readonly HashSet<string> ActionVocabulary = new()
    {
        "close", "reopen", "year-close", "reopen-year",
    };

    private static IEnumerable<MethodInfo> ActionMethods(Type t) =>
        t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => !m.IsSpecialName);

    private static bool IsMutating(MethodInfo m) =>
        m.GetCustomAttributes<HttpPostAttribute>().Any()
        || m.GetCustomAttributes<HttpPutAttribute>().Any()
        || m.GetCustomAttributes<HttpDeleteAttribute>().Any();

    private static (string menu, string action)? ReadPermission(MethodInfo m)
    {
        var data = CustomAttributeData.GetCustomAttributes(m)
            .FirstOrDefault(d => d.AttributeType == typeof(RequirePermissionAttribute));
        if (data == null) return null;
        var args = data.ConstructorArguments;
        return ((string)args[0].Value!, (string)args[1].Value!);
    }

    [Fact]
    public void PeriodController_EveryMutatingAction_HasRequirePermission_WithConventionalKey()
    {
        var offenders = new List<string>();
        foreach (var m in ActionMethods(typeof(PeriodController)).Where(IsMutating))
        {
            var perm = ReadPermission(m);
            if (perm == null)
            {
                offenders.Add($"{nameof(PeriodController)}.{m.Name}：变更端点缺 [RequirePermission]");
                continue;
            }
            if (!MenuPattern.IsMatch(perm.Value.menu))
                offenders.Add($"{m.Name}：menu '{perm.Value.menu}' 不符约定 ^fin-[a-z0-9-]+$");
            if (!ActionVocabulary.Contains(perm.Value.action))
                offenders.Add($"{m.Name}：action '{perm.Value.action}' 不在 PeriodController 动作词汇集（疑似 typo）");
        }
        Assert.True(offenders.Count == 0, "PeriodController 变更端点权限缺失/键不合约定:\n" + string.Join("\n", offenders));
    }

    [Fact]
    public void PeriodController_NewYearEndpoints_UseFinPeriodKey()
    {
        // 显式锚定两个本包新增高危端点的键（防误改）。
        var yc = ReadPermission(typeof(PeriodController).GetMethod(nameof(PeriodController.YearClose))!);
        var ry = ReadPermission(typeof(PeriodController).GetMethod(nameof(PeriodController.ReopenYear))!);
        Assert.Equal(("fin-period", "year-close"), yc);
        Assert.Equal(("fin-period", "reopen-year"), ry);
    }
}
