using System.Reflection;
using System.Text.RegularExpressions;
using CP6.Core.Auth;
using CP6.WebApi.Controllers.Wms;
using Microsoft.AspNetCore.Mvc;

namespace CP6.Tests.Wms;

/// <summary>
/// 反射守卫（M-WMS 权限点接线 Task 4，fail-closed 防回潮闸）：扫 CP6.WebApi 程序集
/// Controllers.Wms 命名空间全部 controller。
///
/// ① 存在性闸（核心）：每个变更端点（HttpPost/HttpPut/HttpDelete）**必须**带
///    [RequirePermission]，否则记为 offender 断言失败。将来谁新增 WMS 写端点忘贴权限，
///    本用例立刻红。**无豁免清单**——本波 125 个写端点全部已贴（含 StockDwell.Summary
///    这类语义 view 但走 HttpPost 的端点）。
/// ② 键约定校验（防 typo，非逐字白名单）：读出每个 [RequirePermission] 的 (menu, action)，
///    断言 menu 匹配 ^wms-[a-z0-9-]+$、action 落在本波动作词汇集合内。**不做逐字 112 条白名单**
///    （逐字正确性已由 T3a 审查逐条核过；此处只防结构性回潮/typo，避免与 T1 文档、T3b 种子三重复制）。
/// ③ 只读误贴防护：纯 HttpGet（且非变更）端点不应带 [RequirePermission]（本波未给 GET 贴）。
///
/// 断言方式说明：RequirePermissionAttribute 的 menu/action 为 private field，实例反射不可读。
/// 故用 <see cref="CustomAttributeData"/> 读取该特性的构造参数 (menu, action)。
///
/// 继承说明：本波全部 32 个 WMS 控制器均直接继承 ControllerBase、写端点均为手写声明方法
/// （无 CodeGen BaseCrudController 继承端点），故 DeclaredOnly 反射不会漏扫。
/// </summary>
public class WmsPermissionAttributeTests
{
    /// <summary>menu 键约定：wms- 前缀 + 小写字母数字连字符。</summary>
    private static readonly Regex MenuPattern = new("^wms-[a-z0-9-]+$", RegexOptions.Compiled);

    /// <summary>
    /// 本波动作词汇集合——从实际贴的 [RequirePermission] 属性 grep 出的真实 action 集
    /// （非凭空造）。新 action 词若出现须显式加入本集合，否则视为疑似 typo 报错。
    /// </summary>
    private static readonly HashSet<string> ActionVocabulary = new()
    {
        "view", "add", "edit", "del",
        "adjust", "move",
        "confirm", "cancel", "submit", "approve",
        "allocate", "pick", "ship", "post",
        "count", "generate", "execute",
        "resolve", "dismiss",
        "ingest", "simulate",
        "open", "mix",
        "recall",
        "dispatch", "start", "complete", "fail",
        "receive",
        "dispose",
        "calculate",
        "reserve", "use",
        "event", "judge",
        "scan",
        "maintenance",
        "set",
        "consume", "slit",
        "lend", "return", "expire",
        "analyze", "inspect", "close",
    };

    private static IEnumerable<Type> WmsControllers =>
        typeof(StockController).Assembly.GetTypes()
            .Where(t => t.Namespace == "CP6.WebApi.Controllers.Wms"
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
    public void WmsControllers_AreDiscovered()
    {
        // 守卫：确保反射确实扫到 32 个 controller（防命名空间/程序集变动导致「空扫空过」）。
        // 32 = CP6.WebApi.Controllers.Wms 下继承 ControllerBase 的非抽象类
        //      （含 3 个纯 GET 控制器 WmsDashboard/ReportCenter/Shipping）。
        Assert.Equal(32, WmsControllers.Count());
    }

    [Fact]
    public void EveryMutatingAction_HasRequirePermission_WithConventionalKey()
    {
        var offenders = new List<string>();
        foreach (var c in WmsControllers)
        foreach (var m in ActionMethods(c).Where(IsMutating))
        {
            var perm = ReadPermission(m);
            if (perm == null)
            {
                // fail-closed 存在性闸：变更端点缺 [RequirePermission] 即红。
                offenders.Add($"{c.Name}.{m.Name}：变更端点缺 [RequirePermission]");
                continue;
            }
            if (!MenuPattern.IsMatch(perm.Value.menu))
                offenders.Add($"{c.Name}.{m.Name}：menu '{perm.Value.menu}' 不符约定 ^wms-[a-z0-9-]+$");
            if (!ActionVocabulary.Contains(perm.Value.action))
                offenders.Add($"{c.Name}.{m.Name}：action '{perm.Value.action}' 不在本波动作词汇集（疑似 typo，须显式加入）");
        }
        Assert.True(offenders.Count == 0, "变更端点权限点缺失/键不合约定:\n" + string.Join("\n", offenders));
    }

    [Fact]
    public void NoReadOnlyGetAction_HasRequirePermission()
    {
        var offenders = new List<string>();
        foreach (var c in WmsControllers)
        foreach (var m in ActionMethods(c))
        {
            var readOnly = IsGet(m) && !IsMutating(m);
            if (readOnly && ReadPermission(m) != null)
                offenders.Add($"{c.Name}.{m.Name}：只读 GET 端点误贴 [RequirePermission]");
        }
        Assert.True(offenders.Count == 0, "只读端点误贴权限点:\n" + string.Join("\n", offenders));
    }
}
