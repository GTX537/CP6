using System.Reflection;
using System.Text.RegularExpressions;
using CP6.Core.Auth;
using CP6.WebApi.Controllers.Pur;
using Microsoft.AspNetCore.Mvc;

namespace CP6.Tests;

/// <summary>
/// 反射守卫（M-PUR 横切接线波 Task 3，fail-closed 防回潮闸）：扫 CP6.WebApi 程序集
/// Controllers.Pur 命名空间全部 controller，锁死「未来新增 Pur 写端点漏贴权限键即红」。
/// 与已合并的 <c>MesPermissionAttributeTests</c> / <c>OawfPermissionAttributeTests</c> /
/// <c>ErpPermissionAttributeTests</c> / <c>WmsPermissionAttributeTests</c> 同型。
/// 真相源：docs/seeds/pur-permission-keys.md（8 控制器扫描面 / 24 非GET端点 / 24 贴点 / **0 只读 POST 豁免**）。
///
/// ① discovery 守卫：断言扫到 8 个 controller（防命名空间/程序集变动导致「空扫空过」假绿）。
/// ② fail-closed 核心闸：每个变更端点（HttpPost/HttpPut/HttpDelete）**必须**带 [RequirePermission]。
///    本波**豁免表 = 空**——真相源 §四那条只读 POST（Subcontract.Reconcile 防吞料对账）已按 view **贴点**
///    （`[RequirePermission("pur-subcontract","view")]`），走核心闸校键，不进旁路豁免。故贴点数精确 == 24、
///    豁免命中数精确 == 0（24 = 24 + 0 收口）。将来谁新增 Pur 写端点忘贴权限，本用例立刻红。
/// ③ 键约定校验（防 typo）：读出每个 [RequirePermission] 的 (menu, action)，断言 menu 匹配
///    ^pur-[a-z0-9-]+$（连字符，禁下划线，全仓约定），action **逐词相等**落在真相源实际使用的 action 集合内。
/// ④ 键面 oracle 双向相等：24 个 (menu-key, action) 元组与测试内**独立写死**的 oracle 集合双向相等
///    （测试内字面量誊自真相源 §一/§二，零引用 PurPermissionSeed.Actions/控制器常量），且资源键 ∈ 7 键白名单。
///
/// 断言方式：RequirePermissionAttribute 的 menu/action 为 private field，实例反射不可读，
/// 故用 <see cref="CustomAttributeData"/> 读构造参数 (menu, action)。
/// 继承说明：8 个 Pur 控制器**全部直接继承 ControllerBase**（无 LocalizedControllerBase 或任何中间抽象
/// 基类；已逐类自查类头——GoodsReceipt/PurchaseOrder/PurchaseRequest/PurReconcile/Rfq/Subcontract/
/// SupplierPrice/ThreeWayMatch 均 `: ControllerBase`）。ControllerBase 自身不声明任何 [HttpXxx] action，
/// 故所有写端点均为各子类手写声明方法，BindingFlags.DeclaredOnly 反射不会漏扫端点。
/// 若未来引入共享基类（如 LocalizedControllerBase）并在其上声明 [HttpXxx] 方法，DeclaredOnly 会静默
/// 漏扫该端点——届时须调整扫描策略（如改用非 DeclaredOnly 并按声明类型过滤）。
/// </summary>
public class PurPermissionAttributeTests
{
    /// <summary>menu 键约定：pur- 前缀 + 小写字母数字连字符（全仓连字符约定，禁下划线）。</summary>
    private static readonly Regex MenuPattern = new("^pur-[a-z0-9-]+$", RegexOptions.Compiled);

    /// <summary>资源键（menu-key）白名单，共 7（真相源 §二；708 pur-reconcile GET-only 承载 0 action 键，不在内）。</summary>
    private static readonly HashSet<string> MenuKeyWhitelist = new()
    {
        "pur-supplier-price", "pur-po", "pur-gr", "pur-match",
        "pur-pr", "pur-rfq", "pur-subcontract",
    };

    /// <summary>
    /// 真相源 §一 实际使用的 action 集合（从已贴的 24 个 [RequirePermission] 逐词读出，非凭空造）。
    /// **含 view**——本波 view 是**贴点** action（Subcontract.Reconcile 只读 POST 归 view 但已打属性，走核心闸），
    /// 与 MES/OA 波「view 未贴、不入集」不同。逐词相等，多一词/少一词即红；新 action 词须显式加入本集合。
    /// </summary>
    private static readonly HashSet<string> ActionVocabulary = new()
    {
        "add", "delete", "view",                    // 基粒度写（delete=Pur 域删除词，非 WMS 的 del）+ view（reconcile 只读 POST 归 view，已贴）
        "submit", "cancel", "qc", "reject",         // 状态键：PO 送审/取消、GR 检收应用、Match 拒绝（真相源 §3b）
        "convert", "issue", "cost", "release",      // 高危键：PR/RFQ 转单、外注发料/成本入账、Match 超容差放行（真相源 §三；add 亦复用于 gr/match 高危）
        "invite", "quote", "rank", "select", "writeback",  // Rfq 域个性化写（询价→比价全流程按操作分权，真相源 §五.2）
        "consign",                                  // 外注登记支給材（真相源 #21）
    };

    /// <summary>
    /// 键面 oracle（独立写死，誊自真相源 §一表 24 行 / §二汇总）：24 个 "menu-key:action" 资源键。
    /// **零引用生产常量**（PurPermissionSeed.Actions/控制器 [RequirePermission] 字面量）——反向验证的第二道闸：
    /// 任一贴点被误删/误改，收集集与本 oracle 双向相等即破。
    /// </summary>
    private static readonly HashSet<string> ExpectedResourceKeys = new()
    {
        "pur-supplier-price:add", "pur-supplier-price:delete",              // #1-2
        "pur-po:add", "pur-po:submit", "pur-po:cancel",                    // #3-5
        "pur-gr:add", "pur-gr:qc",                                          // #6-7
        "pur-match:add", "pur-match:release", "pur-match:reject",          // #8-10
        "pur-pr:add", "pur-pr:submit", "pur-pr:convert",                   // #11-13
        "pur-rfq:add", "pur-rfq:invite", "pur-rfq:quote", "pur-rfq:rank",  // #14-17
        "pur-rfq:select", "pur-rfq:writeback", "pur-rfq:convert",          // #18-20
        "pur-subcontract:consign", "pur-subcontract:issue",               // #21-22
        "pur-subcontract:cost", "pur-subcontract:view",                   // #23-24 (view = reconcile 只读 POST 贴点)
    };

    private static IEnumerable<Type> PurControllers =>
        typeof(PurchaseOrderController).Assembly.GetTypes()
            .Where(t => t.Namespace == "CP6.WebApi.Controllers.Pur"
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
    public void PurControllers_AreDiscovered()
    {
        // 守卫：CP6.WebApi.Controllers.Pur 下继承 ControllerBase 的非抽象类共 8
        //      （含 PurReconcile GET-only 无真写控制器）。防命名空间/程序集变动导致空扫假绿。
        Assert.Equal(8, PurControllers.Count());
    }

    [Fact]
    public void EveryMutatingAction_IsGuarded_WithConventionalKey()
    {
        // 本波豁免表 = 空：每个变更端点必须带 [RequirePermission]（reconcile 已贴 view，不进旁路）。
        var offenders = new List<string>();
        var taggedCount = 0;

        foreach (var c in PurControllers)
        foreach (var m in ActionMethods(c).Where(IsMutating))
        {
            var key = Key(c, m);
            var perm = ReadPermission(m);

            if (perm == null)
            {
                offenders.Add($"{key}：变更端点缺 [RequirePermission]（本波无只读 POST 豁免旁路，reconcile 已贴 view）");
                continue;
            }

            taggedCount++;
            if (!MenuPattern.IsMatch(perm.Value.menu))
                offenders.Add($"{key}：menu '{perm.Value.menu}' 不符约定 ^pur-[a-z0-9-]+$");
            if (!MenuKeyWhitelist.Contains(perm.Value.menu))
                offenders.Add($"{key}：menu '{perm.Value.menu}' 不在 7 键白名单（真相源 §二）");
            if (!ActionVocabulary.Contains(perm.Value.action))
                offenders.Add($"{key}：action '{perm.Value.action}' 不在真相源 action 集（疑似 typo，须显式加入）");
        }

        Assert.True(offenders.Count == 0,
            "变更端点权限点缺失/键不合约定:\n" + string.Join("\n", offenders));

        // 收口断言：贴点精确 24 = 全 24 非GET端点，精确吻合真相源 §七（豁免 0）。
        Assert.Equal(24, taggedCount);
    }

    [Fact]
    public void ResourceKeys_MatchIndependentOracle_Exactly()
    {
        // 键面 oracle 双向相等：从 24 贴点收集的 (menu:action) 集合 == 测试内独立 oracle（24）。
        // 反向验证：误删/误改任一贴点 → 收集集 ≠ oracle → 破（与计数断言双重失败）。
        var collected = new HashSet<string>();
        var dupes = new List<string>();

        foreach (var c in PurControllers)
        foreach (var m in ActionMethods(c).Where(IsMutating))
        {
            var perm = ReadPermission(m);
            if (perm == null) continue;
            var rk = $"{perm.Value.menu}:{perm.Value.action}";
            if (!collected.Add(rk))
                dupes.Add($"{Key(c, m)} → {rk}（资源键跨端点重复，端点↔资源键须 1:1）");
        }

        Assert.True(dupes.Count == 0, "资源键重复:\n" + string.Join("\n", dupes));

        // 双向相等：oracle 里有但源码缺 = 漏贴/改键；源码有但 oracle 缺 = 新增未登记
        var missingInSource = ExpectedResourceKeys.Except(collected).ToList();
        var missingInOracle = collected.Except(ExpectedResourceKeys).ToList();
        Assert.True(missingInSource.Count == 0, "oracle 有但源码缺（漏贴/改键）:\n" + string.Join("\n", missingInSource));
        Assert.True(missingInOracle.Count == 0, "源码有但 oracle 缺（新增未登记）:\n" + string.Join("\n", missingInOracle));

        Assert.Equal(24, collected.Count);
        Assert.Equal(24, ExpectedResourceKeys.Count);

        // 资源键前缀 ∈ 7 键白名单，零下划线
        foreach (var rk in collected)
        {
            var menu = rk[..rk.IndexOf(':')];
            Assert.Contains(menu, MenuKeyWhitelist);
            Assert.DoesNotContain('_', rk);
        }
    }

    [Fact]
    public void NoReadOnlyGetAction_HasRequirePermission()
    {
        // 只读误贴防护：纯 HttpGet（且非变更）端点不应带 [RequirePermission]（本波未给 GET 贴）。
        var offenders = new List<string>();
        foreach (var c in PurControllers)
        foreach (var m in ActionMethods(c))
        {
            var readOnly = IsGet(m) && !IsMutating(m);
            if (readOnly && ReadPermission(m) != null)
                offenders.Add($"{Key(c, m)}：只读 GET 端点误贴 [RequirePermission]");
        }
        Assert.True(offenders.Count == 0, "只读端点误贴权限点:\n" + string.Join("\n", offenders));
    }
}
