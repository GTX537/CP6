using System.Reflection;
using System.Text.RegularExpressions;
using CP6.Core.Auth;
using CP6.WebApi.Controllers.Oa;
using CP6.WebApi.Controllers.Wf;
using Microsoft.AspNetCore.Mvc;

namespace CP6.Tests;

/// <summary>
/// 反射守卫（M-OA/WF 横切接线波 Task 4，fail-closed 防回潮闸）：扫 CP6.WebApi 程序集
/// **两个命名空间** Controllers.Oa（12）+ Controllers.Wf（5）= 17 controller，锁死
/// 「未来新增 OA/WF 写端点漏贴权限键即红」。与已合并的 <c>WmsPermissionAttributeTests</c> /
/// <c>ErpPermissionAttributeTests</c> / <c>MesPermissionAttributeTests</c> 同型三件套。
/// 真相源：docs/seeds/oawf-permission-keys.md（17 控制器扫描面 / 41 非GET端点 / 39 贴点 / 2 只读 POST 豁免；
/// 39 = 37 M-OA/WF·F-T2 + 2 波④ B-T2 batch-transfer/preview）。
/// **F-T2（WFS 波③ 事件触发，E-T1 交接票）**：FlowTriggerAdminController 由 Controllers.Integration 收编回
/// Controllers.Oa 纳入本守卫，贡献 6 变更端点（Edit×5 + View×1=CronPreview），计数 16→17 / 贴点 31→37 / 33→39 非GET。
///
/// ① discovery 守卫：断言扫到 17 个 controller（防命名空间/程序集变动导致「空扫空过」假绿）。
///    谓词覆盖 **两个** 命名空间（Oa ∪ Wf），计数断言 17 防单侧空扫。
/// ② fail-closed 核心闸：每个变更端点（HttpPost/HttpPut/HttpDelete）**要么**带 [RequirePermission]、
///    **要么**在显式只读 POST 豁免清单内；两者皆非即 offender 断言失败。且贴点数精确 == 39、
///    豁免命中数精确 == 2（41 = 39 + 2 收口）。将来谁新增 OA/WF 写端点忘贴权限，本用例立刻红。
/// ③ 键约定校验（防 typo）：读出每个 [RequirePermission] 的 (menu, action)，断言 menu 匹配
///    ^oa-[a-z0-9-]+$（连字符，禁下划线）。**注**：Wf 命名空间 5 控制器（Flow/Form/Task/AdvancedFlow/
///    Approval）无自己菜单行，其键锚定「消费页」OA 菜单，故 **全部键仍为 oa-* 前缀**（真相源 §一/§二）——
///    本波不存在 wf-* 键。action **逐词相等**落在真相源实际使用的 action 集合内。
/// ④ 豁免防腐：每条豁免必须确为「变更端点 且 未贴权限」——防豁免清单变陈旧（端点改名/被贴/被删）
///    却仍白名单遮蔽某个真·写端点丢键。
///
/// 断言方式：RequirePermissionAttribute 的 menu/action 为 private field，实例反射不可读，
/// 故用 <see cref="CustomAttributeData"/> 读构造参数 (menu, action)。
/// 继承说明：17 个 OA/WF 控制器中 1 个（Wf.TaskController）直接继承 ControllerBase，其余 16 个
/// （Oa 全 12 + Wf 的 AdvancedFlow/Approval/Flow/Form）经 <c>LocalizedControllerBase</c>（抽象基类，
/// 仅惰性暴露 Localizer 属性，**零 [HttpXxx] action 声明**）继承 ControllerBase；因各级基类均无端点
/// 声明，写端点均为子类手写声明方法，故 BindingFlags.DeclaredOnly 反射不会漏扫端点（逐类自查类头，
/// 见 oawf-t4-report.md 继承链核对）。若未来在共享基类（LocalizedControllerBase 或 ControllerBase
/// 派生链上）新增 [HttpXxx] 方法，DeclaredOnly 会静默漏扫该端点——届时须调整扫描策略（如改用非
/// DeclaredOnly 并按声明类型过滤）。
/// </summary>
public class OawfPermissionAttributeTests
{
    /// <summary>
    /// menu 键约定：**oa-** 前缀 + 小写字母数字连字符。Wf 命名空间控制器键锚定 OA 消费页菜单，
    /// 故亦为 oa-* 前缀（真相源 §一/§二：/api/wf/* 五控制器无自己菜单行）。
    /// </summary>
    private static readonly Regex MenuPattern = new("^oa-[a-z0-9-]+$", RegexOptions.Compiled);

    /// <summary>
    /// 真相源实际使用的 action 集合（从已贴的 37 个 [RequirePermission] 逐词读出的真实 action，非凭空造）。
    /// 只读 POST 豁免归 view 但**不贴键**（未打属性），故本集合**不含 view**——逐词相等，多一词/少一词即红。
    /// 新 action 词若出现须显式加入本集合，否则视为疑似 typo 报错。
    /// </summary>
    private static readonly HashSet<string> ActionVocabulary = new()
    {
        "add", "edit", "del",                       // 四基粒度写（view 未贴，不入集）
        "favorite",                                 // 低危个性化写：oa-form-catalog:favorite 收藏表单
        "read",                                     // 低危写：oa-inbox:read 标记已读（归并 Inbox/CC/Notification，§五归并4）
        "submit",                                   // 状态键：oa-form-catalog:submit 起流程/提交（归并 Draft/Flow/Approval/Form，§五归并3）
        "enable",                                   // 状态键：oa-flow-admin:enable 流程启停干预（§五归并6）
        "withdraw",                                 // 状态键：oa-inbox:withdraw 撤回申请
        "approve", "transfer", "sendback",          // 高危键：审批办理/转交/退回（§三，归并 Inbox+Flow/AdvancedFlow）
        "addsign",                                  // 高危键：oa-inbox:addsign 加签改审批链（§三）
        "delegate",                                 // 高危键：oa-settings:delegate 委派授权（合一 OA #5/#6 + AdvancedFlow #26，§三/§六注4）
        "form-save",                                // 高危键：oa-designer:form-save 表单定义保存（旧栈 Form.SaveDef，§三）
        // WFS 波④ 信箱体验 B-T2：InboxController 新增 batch-transfer/preview 两个 POST，同贴 oa-inbox:batch-transfer。
        "batch-transfer",                           // 高危键：oa-inbox:batch-transfer 在途批量改派（spec OA.Inbox.BatchTransfer；preview 同键，C8）
        // WFS 波③ 事件触发 F-T2（E-T1 交接票）：FlowTriggerAdminController 由 Integration 收编回 Controllers.Oa，
        // 纳入本 fail-closed 守卫扫描面。Edit=增改/启停/试发/重置key（5 端点），View=cron 预览（1 端点，POST）。
        "FlowTrigger.View", "FlowTrigger.Edit",     // oa-flow-admin:FlowTrigger.*（点式 action，与 spec §6 权限点名逐字一致）
    };

    /// <summary>
    /// 只读 POST 豁免清单（真相源 §四，共 2 条 —— 均逐条读 Service 实现证得无写副作用；归 view，不贴权限键）。
    /// 键 = "ControllerName.MethodName"。每条带真相源编号 + 豁免依据。
    /// </summary>
    private static readonly HashSet<string> ReadOnlyPostExemptions = new()
    {
        "ForecastController.Preview",   // §四#1 ForecastService.ForecastAsync 仅 Wf_FlowDefs 读定义→内存遍历 schema 算预计审批路径，不产生实例、全类无 Add/Update/Remove/SaveChanges；POST 仅为传 varsJson 复杂体
        "QueryController.Search",       // §四#2 InboxService.QueryAsync 仅 Wf_FlowInstances 多条件筛选 + join，Take(500).ToListAsync 投影 DTO，无写；POST 仅为传 FormQueryFilter 复杂体
    };

    private static IEnumerable<Type> OawfControllers =>
        typeof(InboxController).Assembly.GetTypes()
            .Where(t => (t.Namespace == "CP6.WebApi.Controllers.Oa"
                         || t.Namespace == "CP6.WebApi.Controllers.Wf")
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
    public void OawfControllers_AreDiscovered()
    {
        // 守卫：Controllers.Oa（12，含 F-T2 收编的 FlowTriggerAdminController）+ Controllers.Wf（5）下
        //      继承 ControllerBase 的非抽象类共 17（含 Forecast/Query 两个全豁免·真写=0 控制器）。防单侧空扫假绿。
        Assert.Equal(17, OawfControllers.Count());
    }

    [Fact]
    public void EveryMutatingAction_IsGuarded_WithConventionalKeyOrExemption()
    {
        var offenders = new List<string>();
        var taggedCount = 0;
        var exemptHit = new HashSet<string>();

        foreach (var c in OawfControllers)
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
                offenders.Add($"{key}：menu '{perm.Value.menu}' 不符约定 ^oa-[a-z0-9-]+$");
            if (!ActionVocabulary.Contains(perm.Value.action))
                offenders.Add($"{key}：action '{perm.Value.action}' 不在真相源 action 集（疑似 typo，须显式加入）");
        }

        Assert.True(offenders.Count == 0,
            "变更端点权限点缺失/键不合约定/豁免冲突:\n" + string.Join("\n", offenders));

        // 收口断言：贴点 39 + 豁免命中 2 = 全 41 非GET端点，精确吻合真相源 §七 + F-T2 收编 6 端点 + B-T2 新增 2 端点。
        Assert.Equal(39, taggedCount);
        Assert.Equal(2, exemptHit.Count);
    }

    [Fact]
    public void ReadOnlyPostExemptions_AreAllStillUntaggedMutatingEndpoints()
    {
        // 豁免防腐：清单每一条都必须实存、确为变更端点、且当前未贴权限键。
        // 防清单变陈旧（端点被改名/删除/后来贴了键）却仍白名单遮蔽某真·写端点丢键。
        var byKey = OawfControllers
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
        foreach (var c in OawfControllers)
        foreach (var m in ActionMethods(c))
        {
            var readOnly = IsGet(m) && !IsMutating(m);
            if (readOnly && ReadPermission(m) != null)
                offenders.Add($"{Key(c, m)}：只读 GET 端点误贴 [RequirePermission]");
        }
        Assert.True(offenders.Count == 0, "只读端点误贴权限点:\n" + string.Join("\n", offenders));
    }
}
