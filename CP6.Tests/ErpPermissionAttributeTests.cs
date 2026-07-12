using System.Reflection;
using System.Text.RegularExpressions;
using CP6.Core.Auth;
using CP6.WebApi.Controllers.Erp;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CP6.Tests;

/// <summary>
/// 反射守卫（M-ERP 横切接线波 Task 4，fail-closed 防回潮闸）：扫 CP6.WebApi 程序集
/// Controllers.Erp 命名空间全部 controller，锁死「未来新增 ERP 写端点漏贴权限键即红」。
/// 与已合并的 <c>WmsPermissionAttributeTests</c>（commit 0efb717）同型三件套 + ERP 特有两处扩展。
/// 真相源：docs/seeds/erp-permission-keys.md（15 控制器扫描面 / 46 写端点 / 35 贴点 / 11 只读 POST 豁免）。
///
/// ① discovery 守卫：断言扫到 15 个 controller（防命名空间/程序集变动导致「空扫空过」假绿）。
/// ② fail-closed 核心闸：每个变更端点（HttpPost/HttpPut/HttpDelete）**要么**带 [RequirePermission]、
///    **要么**在显式只读 POST 豁免清单内；两者皆非即 offender 断言失败。且贴点数精确 == 35、
///    豁免命中数精确 == 11（46 = 35 + 11 收口）。将来谁新增 ERP 写端点忘贴权限，本用例立刻红。
/// ③ 键约定校验（防 typo）：读出每个 [RequirePermission] 的 (menu, action)，断言 menu 匹配
///    ^erp-[a-z0-9-]+$（连字符，禁下划线），action **逐词相等**落在真相源实际使用的 action 集合内。
/// ④ 豁免防腐：每条豁免必须确为「变更端点 且 未贴权限」——防豁免清单变陈旧（端点改名/被贴/被删）
///    却仍白名单遮蔽某个真·写端点丢键。
/// ⑤ AllowAnonymous 锁：EstimateCalcController.Calculate 现挂 [AllowAnonymous]（主控裁决保留，
///    已记终审票待用户裁处）。断言其确实挂着——防有人删了 AllowAnonymous 却忘贴权限：Calculate 在
///    豁免清单内，核心闸不会拦，唯本断言把这条路锁死。
///
/// 断言方式：RequirePermissionAttribute 的 menu/action 为 private field，实例反射不可读，
/// 故用 <see cref="CustomAttributeData"/> 读构造参数 (menu, action)。
/// 继承说明：15 个 ERP 控制器中 6 个直接继承 ControllerBase，9 个经 LocalizedControllerBase
/// （抽象基类，仅暴露 Localizer 属性，零 [HttpXxx] action 声明）继承 ControllerBase；因各级基类
/// 均无端点声明，写端点均为子类手写声明方法，故 BindingFlags.DeclaredOnly 反射不会漏扫端点。
/// 若未来在共享基类（LocalizedControllerBase 或 ControllerBase 派生链上）新增 [HttpXxx] 方法，
/// DeclaredOnly 会静默漏扫该端点——届时须调整扫描策略（如改用非 DeclaredOnly 并按声明类型过滤）。
/// </summary>
public class ErpPermissionAttributeTests
{
    /// <summary>menu 键约定：erp- 前缀 + 小写字母数字连字符。</summary>
    private static readonly Regex MenuPattern = new("^erp-[a-z0-9-]+$", RegexOptions.Compiled);

    /// <summary>
    /// 真相源实际使用的 action 集合（从已贴的 35 个 [RequirePermission] grep 出的真实 action，非凭空造）。
    /// 只读 POST 豁免归 view 但**不贴键**（未打属性），故本集合**不含 view**——逐词相等，多一词/少一词即红。
    /// 新 action 词若出现须显式加入本集合，否则视为疑似 typo 报错。
    /// </summary>
    private static readonly HashSet<string> ActionVocabulary = new()
    {
        "add", "edit", "del",          // 四基粒度写（view 未贴，不入集）
        "cancel", "correct",           // 高危键（真相源 §三：erp-order:cancel / erp-order-price-correction:correct）
        "confirm", "issue",            // 状态键：見積確定管理 / 帳票発行
        "import",                      // 状态键：シート単価 Excel 取込
        "close", "split",              // 状态键：欠品残数 关闭 / 拆分新受注
    };

    /// <summary>
    /// 只读 POST 豁免清单（真相源 §四，共 11 条 —— 均逐条读 Service 实现证得无写副作用；归 view，不贴权限键）。
    /// 键 = "ControllerName.MethodName"。每条带真相源编号 + 豁免依据。
    /// </summary>
    private static readonly HashSet<string> ReadOnlyPostExemptions = new()
    {
        "OrderController.CalcLeadTime",         // §四#1 纯营业日逆算，Task.FromResult，无 _db 触碰
        "OrderController.CalcProductCategory",  // §四#2 仅 ProductMasters.AsNoTracking 读，无 SaveChanges
        "OrderController.CalcMaterials",        // §四#3 BOM 展开纯读投影 DTO
        "OrderController.ExportReport",         // §四#4 受注伝票导出，AsNoTracking 读→拼文本 bytes
        "EstimateCalcController.Calculate",     // §四#5 计算引擎仅写内存 DTO List，无 _db.Add/SaveChanges；⚠另挂 [AllowAnonymous]（见 Calculate_RetainsAllowAnonymous）
        "PlateMoldController.Label",            // §四#6 ラベル CSV 生成，AsNoTracking 读
        "CreditNoteController.Search",          // §四#7 CreditNoteService 全类无写，纯分页查询
        "OtdReportController.Summary",          // §四#8 OtdReportService 全类无写，纯汇总读
        "OtdReportController.ExportCsv",        // §四#9 同上，纯读导出
        "UnshippedOrderController.Search",      // §四#10 UnshippedOrderService 全类无写，纯分页查询
        "UnshippedOrderController.ExportCsv",   // §四#11 同上，纯读导出
    };

    private static IEnumerable<Type> ErpControllers =>
        typeof(OrderController).Assembly.GetTypes()
            .Where(t => t.Namespace == "CP6.WebApi.Controllers.Erp"
                        && typeof(ControllerBase).IsAssignableFrom(t)
                        && !t.IsAbstract)
            .OrderBy(t => t.Name);

    private static IEnumerable<MethodInfo> ActionMethods(Type t) =>
        t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => !m.IsSpecialName);

    private static bool IsMutating(MethodInfo m) =>
        m.GetCustomAttributes<HttpPostAttribute>().Any()
        || m.GetCustomAttributes<HttpPutAttribute>().Any()
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
    public void ErpControllers_AreDiscovered()
    {
        // 守卫：CP6.WebApi.Controllers.Erp 下继承 ControllerBase 的非抽象类共 15
        //      （含 MasterData / OrderTrace 两个 GET-only 控制器）。防空扫假绿。
        Assert.Equal(15, ErpControllers.Count());
    }

    [Fact]
    public void EveryMutatingAction_IsGuarded_WithConventionalKeyOrExemption()
    {
        var offenders = new List<string>();
        var taggedCount = 0;
        var exemptHit = new HashSet<string>();

        foreach (var c in ErpControllers)
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
                offenders.Add($"{key}：menu '{perm.Value.menu}' 不符约定 ^erp-[a-z0-9-]+$");
            if (!ActionVocabulary.Contains(perm.Value.action))
                offenders.Add($"{key}：action '{perm.Value.action}' 不在真相源 action 集（疑似 typo，须显式加入）");
        }

        Assert.True(offenders.Count == 0,
            "变更端点权限点缺失/键不合约定/豁免冲突:\n" + string.Join("\n", offenders));

        // 收口断言：贴点 35 + 豁免命中 11 = 全 46 变更端点，精确吻合真相源 §七。
        Assert.Equal(35, taggedCount);
        Assert.Equal(11, exemptHit.Count);
    }

    [Fact]
    public void ReadOnlyPostExemptions_AreAllStillUntaggedMutatingEndpoints()
    {
        // 豁免防腐：清单每一条都必须实存、确为变更端点、且当前未贴权限键。
        // 防清单变陈旧（端点被改名/删除/后来贴了键）却仍白名单遮蔽某真·写端点丢键。
        var byKey = ErpControllers
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
        Assert.Equal(11, ReadOnlyPostExemptions.Count);
    }

    [Fact]
    public void Calculate_RetainsAllowAnonymous()
    {
        // 主控裁决：EstimateCalcController.Calculate 保留 [AllowAnonymous]（已记终审票待用户裁处）。
        // 该端点在豁免清单内，核心闸不会拦其缺键；此断言独立锁死「删 AllowAnonymous 却忘贴权限」这条路：
        // 一旦有人去掉匿名开放，本用例即红，逼其重新决策（复原匿名 或 移出豁免+贴 RequirePermission）。
        var m = typeof(EstimateCalcController).GetMethod(nameof(EstimateCalcController.Calculate));
        Assert.NotNull(m);
        Assert.True(m!.GetCustomAttributes<AllowAnonymousAttribute>().Any(),
            "EstimateCalcController.Calculate 应保留 [AllowAnonymous]；若已撤销，须同步给该端点贴 [RequirePermission] 并移出只读 POST 豁免清单。");
    }

    [Fact]
    public void NoReadOnlyGetAction_HasRequirePermission()
    {
        // 只读误贴防护：纯 HttpGet（且非变更）端点不应带 [RequirePermission]（本波未给 GET 贴）。
        var offenders = new List<string>();
        foreach (var c in ErpControllers)
        foreach (var m in ActionMethods(c))
        {
            var readOnly = IsGet(m) && !IsMutating(m);
            if (readOnly && ReadPermission(m) != null)
                offenders.Add($"{Key(c, m)}：只读 GET 端点误贴 [RequirePermission]");
        }
        Assert.True(offenders.Count == 0, "只读端点误贴权限点:\n" + string.Join("\n", offenders));
    }
}
