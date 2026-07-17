using System.Reflection;
using System.Runtime.CompilerServices;
using CP6.Core.Auth;
using CP6.WebApi.Seed;
using Microsoft.AspNetCore.Mvc;

namespace CP6.Tests;

/// <summary>
/// X-SWEEP T2 —「贴点⊆种子」互锁测试（跨波双票 sweep 第二票，六模块）。
///
/// 背景盲区（票源：M-ERP 完成记录票#3 起，逐波扩容 M-MES/M-OA/WF/M-PUR/M-PLAN·PUB 各票#3）：
///   既有各波 <c>*PermissionAttributeTests</c> 反射守卫锁「漏贴」（非 GET 无 [RequirePermission] 报红）；
///   既有各波 <c>*PermissionSeedTests</c> DB 播种守卫锁「种子偏离测试 oracle」。
///   **但存在缝隙**：新端点**贴了键、却忘了往 *PermissionSeed 加行**——两类闸皆不红
///   （反射测试只看有没贴属性；种子测试只把种子与其自身 oracle 比），只会在现网表现为
///   admin 403（PermissionAggregator 以 Sys_RoleActions join Sys_Menus 聚合 ActionKeys，
///   种子无该 (MenuId,ActionCode) 行 → admin 也拿不到键 → 403）。
///
/// 本测试直接把两侧对上：对每个模块——
///   ① 反射读该模块命名空间控制器全部变更端点（POST/PUT/PATCH/DELETE）的
///      <c>[RequirePermission("menu-key","action")]</c> 贴点元组；
///   ② 经**测试内字面量锚定表**（menu-key→MenuId，照真相源 docs/seeds/*-key-menu-anchor.md /
///      *-permission-keys.md 锚定小节誊写，零引用生产菜单常量）把 menu-key 换成 MenuId；
///   ③ 断言每个 (MenuId, action) 在该模块 *PermissionSeed 的 <c>Actions</c> 清单中**存在**
///      （⊆ 方向：种子可多于贴点——view 类种子键/词表补位属正常，不红）。
///
/// 对账对象是**种子类实际内容**（反射读其 private static Actions 字段）——这是本测试蓄意的对账面，
/// 与 <c>*PermissionAttributeTests</c> 的独立 oracle 相互独立（brief 要求：本票不复用 oracle）。
///
/// 种子对应面（brief §2，已核实）：
///   WMS→<see cref="WmsPermissionSeed"/> / ERP→<see cref="ErpPermissionSeed"/> /
///   MES→<see cref="MesPermissionSeed"/> / PUR→<see cref="PurPermissionSeed"/> /
///   PLAN+PUB→<see cref="PlanPubPermissionSeed"/> /
///   **OA/WF→种子并集** <see cref="OawfPermissionSeed"/> ∪ <see cref="InboxBatchTransferPermissionSeed"/>
///     ∪ <see cref="FlowTriggerPermissionSeed"/> ∪ <see cref="WorkCalendarConnectorPermissionSeed"/>
///     （后三个系 WFS 波④/③/⑤ 追加，贴点面横跨四种子）。
///
/// 首跑归因（brief §4）：全六模块首跑即绿——各波 *PermissionSeed 本就按「与去重贴点 1:1」构造，
/// 故不存在贴点∉种子的形态；六模块显式豁免表 <c>Exemptions</c> 全空（无一条既知缺陷需登记）。
/// M-OA/WF 票#4 的「2 view 豁免键无 MenuAction 行」= <c>oa-form-catalog:view</c>(Forecast.Preview) /
/// <c>oa-form-search:view</c>(Query.Search)：二者均**只读 POST 豁免、未贴 [RequirePermission]**
/// （见 OawfPermissionAttributeTests.ReadOnlyPostExemptions），故不产生贴点元组、天然不入本互锁面，
/// 无需豁免登记（贴点无→不参与⊆检查）。将来谁贴键忘种，本测试立即红。
///
/// 范围注记：<c>pub-*</c> 键族是拆分的——本面只覆盖 Controllers.Pub 下的 pub-codegen/pub-seq
/// （M-PLAN/PUB 波产物）；pub-dept/pub-role-perm/pub-data-scope/pub-field-perm 系 Controllers.Sys
/// 平台面（菜单 108-111，Sys 内联播种），蓄意不在六模块互锁面内，勿误读为全 pub-* 覆盖。
/// </summary>
public class PermissionSeedInterlockTests
{
    // ────────────────────────────── 反射基础设施 ──────────────────────────────

    /// <summary>CP6.WebApi 程序集（控制器 + 种子同处此程序集；取任一种子类定位）。</summary>
    private static readonly Assembly WebApi = typeof(WmsPermissionSeed).Assembly;

    private static IEnumerable<Type> ControllersIn(params string[] namespaces) =>
        WebApi.GetTypes()
            .Where(t => t.Namespace != null && namespaces.Contains(t.Namespace)
                        && typeof(ControllerBase).IsAssignableFrom(t)
                        && !t.IsAbstract)
            .OrderBy(t => t.Name);

    // 不加 DeclaredOnly：中间基类若声明端点也须入扫（ControllerBase 自带方法无 Http* 特性，被 IsMutating 滤除）。
    private static IEnumerable<MethodInfo> ActionMethods(Type t) =>
        t.GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(m => !m.IsSpecialName);

    // 变更端点谓词：POST/PUT/PATCH/DELETE（含 PATCH——X-SWEEP T1 已在八波反射谓词齐补，本波同口径）。
    private static bool IsMutating(MethodInfo m) =>
        m.GetCustomAttributes<HttpPostAttribute>().Any()
        || m.GetCustomAttributes<HttpPutAttribute>().Any()
        || m.GetCustomAttributes<HttpPatchAttribute>().Any()
        || m.GetCustomAttributes<HttpDeleteAttribute>().Any();

    /// <summary>经 CustomAttributeData 读成员（方法或控制器类）上**全部** [RequirePermission] 的
    /// 构造参数 (menu, action)——特性 AllowMultiple，不许只取首个。</summary>
    private static IEnumerable<(string menu, string action)> ReadPermissions(MemberInfo m) =>
        CustomAttributeData.GetCustomAttributes(m)
            .Where(d => d.AttributeType == typeof(RequirePermissionAttribute))
            .Select(d => ((string)d.ConstructorArguments[0].Value!,
                          (string)d.ConstructorArguments[1].Value!));

    /// <summary>反射读种子类 private static readonly Actions 字段，投影 (MenuId, ActionCode)。
    /// 用 <see cref="ITuple"/> 逐元素取 [0]/[1]——对 (int,string) 或 (int,string,string) 等形状皆容忍。</summary>
    private static HashSet<(int MenuId, string Code)> SeedTuples(params Type[] seedTypes)
    {
        var set = new HashSet<(int, string)>();
        foreach (var st in seedTypes)
        {
            var field = st.GetField("Actions", BindingFlags.NonPublic | BindingFlags.Static)
                        ?? throw new InvalidOperationException($"{st.Name} 无 private static Actions 字段——种子形状变更须同步本测试");
            var arr = (Array)field.GetValue(null)!;
            foreach (var element in arr)
            {
                var tuple = (ITuple)element!;
                set.Add(((int)tuple[0]!, (string)tuple[1]!));
            }
        }
        return set;
    }

    // ────────────────────────────── 六模块描述子 ──────────────────────────────

    private sealed record ModuleFace(
        string Name,
        string[] Namespaces,
        Dictionary<string, int> Anchor,   // menu-key → MenuId（测试内字面量，誊自真相源锚定小节）
        Type[] SeedTypes,
        HashSet<string> Exemptions);      // 已知缺陷显式豁免（"menu-key:action"）——首跑全空

    private static ModuleFace Wms() => new(
        "WMS", new[] { "CP6.WebApi.Controllers.Wms" },
        new()
        {
            ["wms-warehouse"] = 401, ["wms-location"] = 402, ["wms-stock"] = 403, ["wms-stock-qc"] = 429,
            ["wms-inbound-order"] = 405, ["wms-inbound-receipt"] = 406, ["wms-outbound-order"] = 408,
            ["wms-stocktake"] = 415, ["wms-material-shortage"] = 417, ["wms-outbound-routing"] = 419,
            ["wms-qc-inspection"] = 421, ["wms-slotting"] = 422, ["wms-replenish"] = 423, ["wms-cross-dock"] = 424,
            ["wms-kitting"] = 425, ["wms-rma"] = 426, ["wms-lot-trace"] = 427, ["wms-expiry"] = 428,
            ["wms-paper-roll"] = 441, ["wms-remnant"] = 442, ["wms-plate-mold"] = 443, ["wms-ink"] = 444,
            ["wms-pallet"] = 445, ["wms-vmi"] = 446, ["wms-sample-stock"] = 447, ["wms-mobile"] = 461,
            ["wms-wcs-task"] = 462, ["wms-carrier"] = 463, ["wms-iot"] = 464, ["wms-stock-dwell"] = 483,
        },
        new[] { typeof(WmsPermissionSeed) },
        new());

    private static ModuleFace Erp() => new(
        "ERP", new[] { "CP6.WebApi.Controllers.Erp" },
        new()
        {
            ["erp-estimate-calc"] = 202, ["erp-quotation"] = 204, ["erp-product"] = 206, ["erp-order"] = 208,
            ["erp-order-price-correction"] = 209, ["erp-fsc-checklist"] = 210, ["erp-business-partner"] = 212,
            ["erp-sheet-unit-price"] = 213, ["erp-plate-mold"] = 215, ["erp-backorder"] = 218, ["erp-fx-rate"] = 220,
        },
        new[] { typeof(ErpPermissionSeed) },
        new());

    private static ModuleFace Mes() => new(
        "MES", new[] { "CP6.WebApi.Controllers.Mes" },
        new()
        {
            ["mes-planning-board"] = 301, ["mes-work-order"] = 302, ["mes-production-result"] = 304,
            ["mes-quality-inspection"] = 306, ["mes-defect"] = 308, ["mes-machine"] = 310, ["mes-oee"] = 311,
            ["mes-work-center"] = 314, ["mes-process-cost-rate"] = 315,
        },
        new[] { typeof(MesPermissionSeed) },
        new());

    private static ModuleFace Pur() => new(
        "PUR", new[] { "CP6.WebApi.Controllers.Pur" },
        new()
        {
            ["pur-supplier-price"] = 701, ["pur-po"] = 702, ["pur-gr"] = 703, ["pur-match"] = 704,
            ["pur-pr"] = 706, ["pur-rfq"] = 705, ["pur-subcontract"] = 707,
        },
        new[] { typeof(PurPermissionSeed) },
        new());

    private static ModuleFace PlanPub() => new(
        "PLAN/PUB", new[] { "CP6.WebApi.Controllers.Plan", "CP6.WebApi.Controllers.Pub" },
        new()
        {
            ["plan-mrp"] = 731, ["plan-item-policy"] = 732, ["pub-codegen"] = 113, ["pub-seq"] = 112,
        },
        new[] { typeof(PlanPubPermissionSeed) },
        new());

    // OA/WF：贴点横跨 Oa+Wf 两命名空间；断言面取四种子并集（后三个系 WFS 波④/③/⑤ 追加）。
    private static ModuleFace Oawf() => new(
        "OA/WF", new[] { "CP6.WebApi.Controllers.Oa", "CP6.WebApi.Controllers.Wf" },
        new()
        {
            ["oa-inbox"] = 733, ["oa-flow-admin"] = 734, ["oa-form-catalog"] = 735, ["oa-settings"] = 737,
            ["oa-designer"] = 738, ["oa-approver-map"] = 739, ["oa-work-calendar"] = 743,
        },
        new[]
        {
            typeof(OawfPermissionSeed), typeof(InboxBatchTransferPermissionSeed),
            typeof(FlowTriggerPermissionSeed), typeof(WorkCalendarConnectorPermissionSeed),
        },
        new());

    // ────────────────────────────── 互锁核心 ──────────────────────────────

    /// <summary>
    /// 对单模块跑「贴点⊆种子」互锁：收集所有变更端点的 [RequirePermission] 贴点，经锚定表换 MenuId，
    /// 逐个断言 (MenuId, action) ∈ 种子并集。返回 offender 列表 + 收集到的贴点计数（供空扫守卫）。
    /// </summary>
    private static (List<string> offenders, int decoratorTupleCount) RunInterlock(ModuleFace f)
    {
        var seed = SeedTuples(f.SeedTypes);
        var offenders = new List<string>();
        var collected = new HashSet<(string menu, string action)>();

        foreach (var c in ControllersIn(f.Namespaces))
        {
            // 类级贴点对控制器内全部端点生效——与方法级并集（当前全仓零类级，此路系防将来形态）。
            var classPerms = ReadPermissions(c).ToArray();

            foreach (var m in ActionMethods(c).Where(IsMutating))
            foreach (var (menu, action) in ReadPermissions(m).Concat(classPerms))
            {
                // 完全未贴键的变更端点 = 组件/只读POST豁免，由 *PermissionAttributeTests 管，不参与⊆
                collected.Add((menu, action));

                // 锚定表须能解析该 menu-key（新键漏登记锚定表即红——防静默跳过）。
                if (!f.Anchor.TryGetValue(menu, out var menuId))
                {
                    offenders.Add($"{c.Name}.{m.Name}：menu-key '{menu}' 不在测试锚定表——新键须补锚定表（menu-key→MenuId）");
                    continue;
                }

                if (!seed.Contains((menuId, action)))
                {
                    // ⊆ 破：贴了键但种子无对应行 → 现网 admin 403。唯有显式豁免（既知缺陷）才放行。
                    if (!f.Exemptions.Contains($"{menu}:{action}"))
                        offenders.Add(
                            $"{c.Name}.{m.Name}：贴点 ({menu}={menuId}, {action}) 在 {string.Join("∪", f.SeedTypes.Select(t => t.Name))} 中无对应 (MenuId,ActionCode) 行"
                            + "——种子漏行，admin 将 403（若属既知缺陷须登记豁免表并附票号）");
                }
            }
        }

        return (offenders, collected.Count);
    }

    private static void AssertInterlock(ModuleFace f)
    {
        var (offenders, decoratorTupleCount) = RunInterlock(f);

        // 空扫守卫：命名空间/程序集变动导致零贴点被扫 → 假绿。每模块至少数条去重贴点。
        Assert.True(decoratorTupleCount > 0,
            $"[{f.Name}] 未扫到任何 [RequirePermission] 贴点——疑似命名空间/程序集变动致空扫假绿");

        Assert.True(offenders.Count == 0,
            $"[{f.Name}] 「贴点⊆种子」互锁破（贴键漏种，现网 admin 403）:\n" + string.Join("\n", offenders));
    }

    [Fact] public void Wms_Decorators_AreSubsetOf_Seed() => AssertInterlock(Wms());
    [Fact] public void Erp_Decorators_AreSubsetOf_Seed() => AssertInterlock(Erp());
    [Fact] public void Mes_Decorators_AreSubsetOf_Seed() => AssertInterlock(Mes());
    [Fact] public void Pur_Decorators_AreSubsetOf_Seed() => AssertInterlock(Pur());
    [Fact] public void PlanPub_Decorators_AreSubsetOf_Seed() => AssertInterlock(PlanPub());
    [Fact] public void Oawf_Decorators_AreSubsetOf_SeedUnion() => AssertInterlock(Oawf());

    [Fact]
    public void AllExemptions_AreEmpty_FirstRunAllGreen()
    {
        // 首跑全绿现状 pin：六模块显式豁免表当前全空（无既知「贴点∉种子」缺陷）。
        // 将来若某模块因既知缺陷登记豁免，本断言即提示更新此现状快照（并要求豁免附票号）。
        foreach (var f in new[] { Wms(), Erp(), Mes(), Pur(), PlanPub(), Oawf() })
            Assert.True(f.Exemptions.Count == 0,
                $"[{f.Name}] 豁免表非空：{string.Join(",", f.Exemptions)}——须核对票号依据");
    }

    [Fact]
    public void SeedActionsField_IsReadable_ForAllModuleSeeds()
    {
        // 反射前置守卫：九个种子类的 Actions 字段均可反射读且非空——防种子类改名/改私有字段名致本测试静默空过。
        var seeds = new[]
        {
            typeof(WmsPermissionSeed), typeof(ErpPermissionSeed), typeof(MesPermissionSeed),
            typeof(PurPermissionSeed), typeof(PlanPubPermissionSeed), typeof(OawfPermissionSeed),
            typeof(InboxBatchTransferPermissionSeed), typeof(FlowTriggerPermissionSeed),
            typeof(WorkCalendarConnectorPermissionSeed),
        };
        foreach (var st in seeds)
            Assert.True(SeedTuples(st).Count > 0, $"{st.Name}.Actions 反射读取为空——种子形状变更须同步本测试");
    }
}
