using System.Reflection;
using System.Text.RegularExpressions;
using CP6.Core.Auth;
using CP6.WebApi.Controllers.Mes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CP6.Tests;

/// <summary>
/// 反射守卫（M-MES 横切接线波 Task 4，fail-closed 防回潮闸）：扫 CP6.WebApi 程序集
/// Controllers.Mes 命名空间全部 controller，锁死「未来新增 MES 写端点漏贴权限键即红」。
/// 与已合并的 <c>ErpPermissionAttributeTests</c> / <c>WmsPermissionAttributeTests</c> 同型三件套。
/// 真相源：docs/seeds/mes-permission-keys.md（11 控制器扫描面 / 30 非GET端点 / 28 贴点 / 2 只读 POST 豁免）。
///
/// ① discovery 守卫：断言扫到 11 个 controller（防命名空间/程序集变动导致「空扫空过」假绿）。
/// ② fail-closed 核心闸：每个变更端点（HttpPost/HttpPut/HttpDelete）**要么**带 [RequirePermission]、
///    **要么**在显式只读 POST 豁免清单内；两者皆非即 offender 断言失败。且贴点数精确 == 28、
///    豁免命中数精确 == 2（30 = 28 + 2 收口）。将来谁新增 MES 写端点忘贴权限，本用例立刻红。
/// ③ 键约定校验（防 typo）：读出每个 [RequirePermission] 的 (menu, action)，断言 menu 匹配
///    ^mes-[a-z0-9-]+$（连字符，禁下划线），action **逐词相等**落在真相源实际使用的 action 集合内。
/// ④ 豁免防腐：每条豁免必须确为「变更端点 且 未贴权限」——防豁免清单变陈旧（端点改名/被贴/被删）
///    却仍白名单遮蔽某个真·写端点丢键。
///
/// 断言方式：RequirePermissionAttribute 的 menu/action 为 private field，实例反射不可读，
/// 故用 <see cref="CustomAttributeData"/> 读构造参数 (menu, action)。
/// 继承说明：11 个 MES 控制器**全部直接继承 ControllerBase**（无 LocalizedControllerBase 或任何
/// 中间抽象基类；已逐类自查类头，见 mes-t4-report.md 继承链核对）。ControllerBase 自身不声明任何
/// [HttpXxx] action，故所有写端点均为各子类手写声明方法，BindingFlags.DeclaredOnly 反射不会漏扫端点。
/// 若未来引入共享基类（如 LocalizedControllerBase）并在其上声明 [HttpXxx] 方法，DeclaredOnly 会静默
/// 漏扫该端点——届时须调整扫描策略（如改用非 DeclaredOnly 并按声明类型过滤）。
/// </summary>
public class MesPermissionAttributeTests
{
    /// <summary>menu 键约定：mes- 前缀 + 小写字母数字连字符。</summary>
    private static readonly Regex MenuPattern = new("^mes-[a-z0-9-]+$", RegexOptions.Compiled);

    /// <summary>
    /// 真相源实际使用的 action 集合（从已贴的 28 个 [RequirePermission] 逐词读出的真实 action，非凭空造）。
    /// 只读 POST 豁免归 view 但**不贴键**（未打属性），故本集合**不含 view**——逐词相等，多一词/少一词即红。
    /// 新 action 词若出现须显式加入本集合，否则视为疑似 typo 报错。
    /// </summary>
    private static readonly HashSet<string> ActionVocabulary = new()
    {
        "add", "edit", "del",                       // 四基粒度写（view 未贴，不入集）
        "issue",                                    // 状态键：mes-work-order:issue 指図発行
        "start", "suspend", "complete", "report",   // 状态/高危键：mes-production-result:* 报工五态（complete 高危·反冲，§三）
        "status", "downtime",                       // 状态键：mes-machine:status 設備状態 / mes-machine:downtime 停止記録（含 close 归并）
        "recalculate",                              // 状态键：mes-oee:recalculate OEE 再計算
        "reschedule", "arrange",                    // 状态键：mes-planning-board:* 計画変更/自動配置
                                                    // （process-cost-rate:edit 高危复用 edit，§三；work-center/process-cost-rate 复用 edit/del）
    };

    /// <summary>
    /// 只读 POST 豁免清单（真相源 §四，共 2 条 —— 均逐条读 Service 实现证得无写副作用；归 view，不贴权限键）。
    /// 键 = "ControllerName.MethodName"。每条带真相源编号 + 豁免依据。
    /// </summary>
    private static readonly HashSet<string> ReadOnlyPostExemptions = new()
    {
        "PlanAchievementController.Summary",    // §四#1 PlanAchievementService.GetSummaryAsync 仅 WorkOrders.AsNoTracking 读→内存 GroupBy 达成率 DTO，全类无 Add/Update/Remove/SaveChanges；POST 仅为传复杂查询体
        "PlanAchievementController.ExportCsv",  // §四#2 调 GetSummaryAsync 后拼 CSV bytes，纯读导出，无写
    };

    private static IEnumerable<Type> MesControllers =>
        typeof(WorkOrderController).Assembly.GetTypes()
            .Where(t => t.Namespace == "CP6.WebApi.Controllers.Mes"
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

    private static string Key(Type c, MethodInfo m) => $"{c.Name}.{m.Name}";

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
    public void MesControllers_AreDiscovered()
    {
        // 守卫：CP6.WebApi.Controllers.Mes 下继承 ControllerBase 的非抽象类共 11
        //      （含 MesDashboard GET-only + PlanAchievement 全豁免 两个无真写控制器）。防空扫假绿。
        Assert.Equal(11, MesControllers.Count());
    }

    [Fact]
    public void EveryMutatingAction_IsGuarded_WithConventionalKeyOrExemption()
    {
        var offenders = new List<string>();
        var taggedCount = 0;
        var exemptHit = new HashSet<string>();

        foreach (var c in MesControllers)
        foreach (var m in ActionMethods(c).Where(IsMutating))
        {
            var key = Key(c, m);
            var perm = ReadPermission(m);

            if (perm == null)
            {
                // 未贴权限：唯有在显式只读 POST 豁免清单内才放行。
                if (ReadOnlyPostExemptions.Contains(key))
                    exemptHit.Add(key);
                else
                    offenders.Add($"{key}：变更端点缺 [RequirePermission] 且不在只读 POST 豁免清单");
                continue;
            }

            // 已贴权限却又列入豁免 = 语义冲突（豁免应无键）。
            if (ReadOnlyPostExemptions.Contains(key))
                offenders.Add($"{key}：既贴 [RequirePermission] 又在豁免清单，二者互斥");

            taggedCount++;
            if (!MenuPattern.IsMatch(perm.Value.menu))
                offenders.Add($"{key}：menu '{perm.Value.menu}' 不符约定 ^mes-[a-z0-9-]+$");
            if (!ActionVocabulary.Contains(perm.Value.action))
                offenders.Add($"{key}：action '{perm.Value.action}' 不在真相源 action 集（疑似 typo，须显式加入）");
        }

        Assert.True(offenders.Count == 0,
            "变更端点权限点缺失/键不合约定/豁免冲突:\n" + string.Join("\n", offenders));

        // 收口断言：贴点 28 + 豁免命中 2 = 全 30 非GET端点，精确吻合真相源 §七。
        Assert.Equal(28, taggedCount);
        Assert.Equal(2, exemptHit.Count);
    }

    [Fact]
    public void ReadOnlyPostExemptions_AreAllStillUntaggedMutatingEndpoints()
    {
        // 豁免防腐：清单每一条都必须实存、确为变更端点、且当前未贴权限键。
        // 防清单变陈旧（端点被改名/删除/后来贴了键）却仍白名单遮蔽某真·写端点丢键。
        var byKey = MesControllers
            .SelectMany(c => ActionMethods(c).Select(m => (key: Key(c, m), method: m)))
            .ToDictionary(x => x.key, x => x.method);

        var stale = new List<string>();
        foreach (var ex in ReadOnlyPostExemptions)
        {
            if (!byKey.TryGetValue(ex, out var m))
            {
                stale.Add($"{ex}：豁免清单条目在源码中已不存在（须清理豁免清单）");
                continue;
            }
            if (!IsMutating(m))
                stale.Add($"{ex}：豁免条目已非变更端点（须从豁免清单移除）");
            if (ReadPermission(m) != null)
                stale.Add($"{ex}：豁免条目现已贴 [RequirePermission]（须从豁免清单移除，交核心闸校验键）");
        }

        Assert.True(stale.Count == 0, "只读 POST 豁免清单已陈旧:\n" + string.Join("\n", stale));
        Assert.Equal(2, ReadOnlyPostExemptions.Count);
    }

    [Fact]
    public void NoReadOnlyGetAction_HasRequirePermission()
    {
        // 只读误贴防护：纯 HttpGet（且非变更）端点不应带 [RequirePermission]（本波未给 GET 贴）。
        var offenders = new List<string>();
        foreach (var c in MesControllers)
        foreach (var m in ActionMethods(c))
        {
            var readOnly = IsGet(m) && !IsMutating(m);
            if (readOnly && ReadPermission(m) != null)
                offenders.Add($"{Key(c, m)}：只读 GET 端点误贴 [RequirePermission]");
        }
        Assert.True(offenders.Count == 0, "只读端点误贴权限点:\n" + string.Join("\n", offenders));
    }
}
