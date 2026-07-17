using System.Reflection;
using System.Text.RegularExpressions;
using CP6.Core.Auth;
using CP6.WebApi.Controllers.Plan;
using CP6.WebApi.Controllers.Pub;
using Microsoft.AspNetCore.Mvc;

namespace CP6.Tests;

/// <summary>
/// 反射守卫（M-PLAN/PUB 横切接线波 Task 3，fail-closed 防回潮闸）：扫 CP6.WebApi 程序集
/// **两个命名空间** Controllers.Plan（2）+ Controllers.Pub（3）= 5 controller，锁死
/// 「未来新增 Plan/Pub 写端点漏贴权限键即红」。与已合并的 <c>WmsPermissionAttributeTests</c> /
/// <c>ErpPermissionAttributeTests</c> / <c>MesPermissionAttributeTests</c> / <c>OawfPermissionAttributeTests</c> /
/// <c>PurPermissionAttributeTests</c> 同型（本波结构最贴 OawfPermissionAttributeTests：双命名空间 + 非空豁免表）。
/// 真相源：docs/seeds/planpub-permission-keys.md（§一表 14 非GET端点 / §七计数：11 贴点 + 3 组件豁免 = 14；
/// 4 menu-key：plan-mrp/plan-item-policy/pub-codegen/pub-seq）。
///
/// ① discovery 守卫：断言扫到 5 个 controller（防命名空间/程序集变动导致「空扫空过」假绿）。
///    谓词覆盖 **两个** 命名空间（Plan ∪ Pub），计数断言 5 防单侧空扫。
/// ② fail-closed 核心闸：每个变更端点（HttpPost/HttpPut/HttpDelete）**要么**带 [RequirePermission]、
///    **要么**在显式组件豁免清单内（Attachment upload/delete/rebind，§五.4 横切组件无菜单可锚→登入豁免）；
///    两者皆非即 offender 断言失败。且贴点数精确 == 11、豁免命中数精确 == 3（14 = 11 + 3 收口）。
///    **CodeGen.PreviewInline 已贴 pub-codegen:view（§四.4a 只读 POST 归 view 贴点，非旁路）→ 走核心闸、不进豁免表。**
///    将来谁新增 Plan/Pub 写端点忘贴权限，本用例立刻红。
/// ③ 键约定校验（防 typo）：读出每个 [RequirePermission] 的 (menu, action)，断言 menu 匹配
///    ^(plan|pub)-[a-z0-9-]+$（连字符，禁下划线，全仓约定），且 ∈ 4 键白名单；action **逐词相等**
///    落在真相源实际使用的 action 集合内。
/// ④ 键面 oracle 双向相等：11 个 (menu-key, action) 元组与测试内**独立写死**的 oracle 集合双向相等
///    （测试内字面量誊自真相源 §一/§二/§七，零引用 PlanPubPermissionSeed.Actions/控制器常量）。
/// ⑤ 豁免防腐：每条组件豁免必须确为「变更端点 且 未贴权限」——防豁免清单变陈旧（端点改名/被贴/被删）。
/// ⑥ HttpPut 显式覆盖：pub-seq:edit=SeqController.Update 是本波唯一 PUT，钉死 IsMutating 含 HttpPut
///    （T1 concern：漏扫 PUT 则漏贴不报红；对应 M-PUR 跨波 sweep 票背景）。
///
/// 断言方式：RequirePermissionAttribute 的 menu/action 为 private field，实例反射不可读，
/// 故用 <see cref="CustomAttributeData"/> 读构造参数 (menu, action)。
/// 继承说明（逐类自查类头，见 planpub-t3-report.md 核对）：5 控制器中 3 个
/// （Plan.MrpController / Plan.ItemPlanningPolicyController / Pub.CodeGenController）直接 `: ControllerBase`；
/// 2 个（Pub.SeqController / Pub.AttachmentController）经 <c>LocalizedControllerBase</c>（**abstract** 基类，
/// 位于 CP6.WebApi.Controllers 命名空间——不在本扫描面且 IsAbstract 被 !t.IsAbstract 排除；**零 [HttpXxx] action 声明**，
/// 仅暴露 Localizer）继承 ControllerBase。因各级基类均无端点声明，写端点均为子类手写声明方法，
/// 故 BindingFlags.DeclaredOnly 反射不会漏扫端点。若未来在共享基类（LocalizedControllerBase 或 ControllerBase
/// 派生链上）新增 [HttpXxx] 方法，DeclaredOnly 会静默漏扫该端点——届时须调整扫描策略（如改用非 DeclaredOnly 并按声明类型过滤）。
/// </summary>
public class PlanPubPermissionAttributeTests
{
    /// <summary>menu 键约定：plan-/pub- 前缀 + 小写字母数字连字符（全仓连字符约定，禁下划线）。</summary>
    private static readonly Regex MenuPattern = new("^(plan|pub)-[a-z0-9-]+$", RegexOptions.Compiled);

    /// <summary>资源键（menu-key）白名单，共 4（真相源 §二；Attachment 无菜单不铸键，不在内）。</summary>
    private static readonly HashSet<string> MenuKeyWhitelist = new()
    {
        "plan-mrp", "plan-item-policy", "pub-codegen", "pub-seq",
    };

    /// <summary>
    /// 真相源 §一 实际使用的 action 集合（从已贴的 11 个 [RequirePermission] 逐词读出，非凭空造）。
    /// **含 view**——本波 view 是**贴点** action（CodeGen.PreviewInline 只读 POST 归 view 但已打属性，走核心闸），
    /// 与 MES/OA 波「view 未贴、不入集」不同、与 Pur 波「view 是贴点」同。
    /// 逐词相等，多一词/少一词即红；新 action 词须显式加入本集合。
    /// </summary>
    private static readonly HashSet<string> ActionVocabulary = new()
    {
        "run", "convert", "save",          // 高危键（真相源 §三）：MRP 全量重算 / 转单建承诺 / 代码生成写盘覆盖
        "confirm", "ignore",               // 状态键（真相源 §3b）：计划订单确认进供给 / 忽略不计供给
        "add", "delete", "edit",           // 基粒度写：plan-item-policy add/delete + pub-seq add/edit/delete（Plan/Pub 域用 delete 非 del）
        "view",                            // 只读 POST 归 view（pub-codegen:view = PreviewInline 贴点，§四.4a）
    };

    /// <summary>
    /// 键面 oracle（独立写死，誊自真相源 §一表 / §七计数）：11 个 "menu-key:action" 资源键。
    /// **零引用生产常量**（PlanPubPermissionSeed.Actions/控制器 [RequirePermission] 字面量）——反向验证的第二道闸：
    /// 任一贴点被误删/误改，收集集与本 oracle 双向相等即破。
    /// </summary>
    private static readonly HashSet<string> ExpectedResourceKeys = new()
    {
        "plan-mrp:run", "plan-mrp:confirm", "plan-mrp:convert", "plan-mrp:ignore",   // #1-4
        "plan-item-policy:add", "plan-item-policy:delete",                            // #5-6
        "pub-codegen:save", "pub-codegen:view",                                       // #7-8 (view = PreviewInline 只读 POST 贴点)
        "pub-seq:add", "pub-seq:edit", "pub-seq:delete",                              // #12-14 (edit = Update[HttpPut])
    };

    /// <summary>
    /// 组件豁免清单（真相源 §四.4b / §五.4，共 3 条 —— Attachment 横切组件无独立页面/菜单行可锚，
    /// 铸键即死键，故登入 fail-closed 反射测试的显式豁免表，不铸键、不入种子）。
    /// 键 = "ControllerName.MethodName"。每条带真相源编号 + 豁免依据。
    /// </summary>
    private static readonly HashSet<string> ComponentExemptions = new()
    {
        "AttachmentController.Upload",   // §四.4b#1 统一附件上传，横切组件嵌入各业务页、无 RoutePath/菜单行；随宿主业务菜单 EnforceBizPermission 门控
        "AttachmentController.Delete",   // §四.4b#2 引用计数后物理删附件（删除属高危形态，但无菜单可锚→组件豁免，§六 follow-up 建议扩 biz 权限回查至此）
        "AttachmentController.Rebind",   // §四.4b#3 草稿转正 draftToken 附件回填 BizId，业务单据保存后随宿主页授权
    };

    private static IEnumerable<Type> PlanPubControllers =>
        typeof(MrpController).Assembly.GetTypes()
            .Where(t => (t.Namespace == "CP6.WebApi.Controllers.Plan"
                         || t.Namespace == "CP6.WebApi.Controllers.Pub")
                        && typeof(ControllerBase).IsAssignableFrom(t)
                        && !t.IsAbstract)
            .OrderBy(t => t.Name);

    private static IEnumerable<MethodInfo> ActionMethods(Type t) =>
        t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => !m.IsSpecialName);

    // 变更端点谓词：HttpPost/HttpPut/HttpPatch/HttpDelete。**含 HttpPut**（SeqController.Update=pub-seq:edit
    // 唯一 PUT，见 HttpPut_Endpoint_IsScannedAndGuarded 显式钉死）。**含 HttpPatch**——X-SWEEP T1 跨波 sweep 票
    // 已于本 sweep 落地（八波反射谓词齐补 PATCH），杜绝未来 [HttpPatch] 写端点静默逃出扫描面（fail-open）。
    // 本波 5 控制器现仍零 PATCH 端点（NoPatchEndpoints_InScope 现状 pin）。
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
    public void PlanPubControllers_AreDiscovered()
    {
        // 守卫：Controllers.Plan（2：Mrp/ItemPlanningPolicy）+ Controllers.Pub（3：CodeGen/Seq/Attachment）
        //      下继承 ControllerBase 的非抽象类共 5。防命名空间/程序集变动导致单侧空扫假绿。
        Assert.Equal(5, PlanPubControllers.Count());
    }

    [Fact]
    public void EveryMutatingAction_IsGuarded_WithConventionalKeyOrExemption()
    {
        var offenders = new List<string>();
        var taggedCount = 0;
        var exemptHit = new HashSet<string>();

        foreach (var c in PlanPubControllers)
        foreach (var m in ActionMethods(c).Where(IsMutating))
        {
            var key = Key(c, m);
            var perm = ReadPermission(m);

            if (perm == null)
            {
                // 未贴权限：唯有在显式组件豁免清单内才放行（Attachment 无菜单可锚）。
                if (ComponentExemptions.Contains(key))
                    exemptHit.Add(key);
                else
                    offenders.Add($"{key}：变更端点缺 [RequirePermission] 且不在组件豁免清单");
                continue;
            }

            // 已贴权限却又列入豁免 = 语义冲突（豁免应无键）。
            if (ComponentExemptions.Contains(key))
                offenders.Add($"{key}：既贴 [RequirePermission] 又在豁免清单，二者互斥");

            taggedCount++;
            if (!MenuPattern.IsMatch(perm.Value.menu))
                offenders.Add($"{key}：menu '{perm.Value.menu}' 不符约定 ^(plan|pub)-[a-z0-9-]+$");
            if (!MenuKeyWhitelist.Contains(perm.Value.menu))
                offenders.Add($"{key}：menu '{perm.Value.menu}' 不在 4 键白名单（真相源 §二）");
            if (!ActionVocabulary.Contains(perm.Value.action))
                offenders.Add($"{key}：action '{perm.Value.action}' 不在真相源 action 集（疑似 typo，须显式加入）");
        }

        Assert.True(offenders.Count == 0,
            "变更端点权限点缺失/键不合约定/豁免冲突:\n" + string.Join("\n", offenders));

        // 收口断言：贴点 11 + 组件豁免命中 3 = 全 14 非GET端点，精确吻合真相源 §七。
        Assert.Equal(11, taggedCount);
        Assert.Equal(3, exemptHit.Count);
    }

    [Fact]
    public void ResourceKeys_MatchIndependentOracle_Exactly()
    {
        // 键面 oracle 双向相等：从 11 贴点收集的 (menu:action) 集合 == 测试内独立 oracle（11）。
        // 反向验证：误删/误改任一贴点 → 收集集 ≠ oracle → 破（与计数断言双重失败）。
        var collected = new HashSet<string>();
        var dupes = new List<string>();

        foreach (var c in PlanPubControllers)
        foreach (var m in ActionMethods(c).Where(IsMutating))
        {
            var perm = ReadPermission(m);
            if (perm == null) continue;   // 组件豁免端点（Attachment）——无键，不入资源键集
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

        Assert.Equal(11, collected.Count);
        Assert.Equal(11, ExpectedResourceKeys.Count);

        // 资源键前缀 ∈ 4 键白名单，零下划线
        foreach (var rk in collected)
        {
            var menu = rk[..rk.IndexOf(':')];
            Assert.Contains(menu, MenuKeyWhitelist);
            Assert.DoesNotContain('_', rk);
        }
    }

    [Fact]
    public void ComponentExemptions_AreAllStillUntaggedMutatingEndpoints()
    {
        // 豁免防腐：清单每一条都必须实存、确为变更端点、且当前未贴权限键。
        // 防清单变陈旧（端点被改名/删除/后来贴了键）却仍白名单遮蔽某真·写端点丢键。
        var byKey = PlanPubControllers
            .SelectMany(c => ActionMethods(c).Select(m => (key: Key(c, m), method: m)))
            .ToDictionary(x => x.key, x => x.method);

        var stale = new List<string>();
        foreach (var ex in ComponentExemptions)
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

        Assert.True(stale.Count == 0, "组件豁免清单已陈旧:\n" + string.Join("\n", stale));
        Assert.Equal(3, ComponentExemptions.Count);
    }

    [Fact]
    public void HttpPut_Endpoint_IsScannedAndGuarded()
    {
        // T1 concern 点名：pub-seq:edit=SeqController.Update 是本波唯一 PUT。若 IsMutating 谓词漏含 HttpPut
        // （M-PUR 跨波 sweep 票背景），PUT 端点漏扫→漏贴不报红。本用例显式钉死：
        //   Update 确为 [HttpPut]、被 IsMutating 认定为变更端点、且已贴 (pub-seq, edit)。
        var seq = PlanPubControllers.FirstOrDefault(t => t.Name == "SeqController");
        Assert.NotNull(seq);
        var update = seq!.GetMethod("Update", BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
        Assert.NotNull(update);
        Assert.True(update!.GetCustomAttributes<HttpPutAttribute>().Any(), "SeqController.Update 应为 [HttpPut]");
        Assert.True(IsMutating(update), "IsMutating 谓词须含 HttpPut，否则 PUT 端点漏扫→漏贴不报红");
        Assert.Equal(("pub-seq", "edit"), ReadPermission(update));
    }

    [Fact]
    public void NoPatchEndpoints_InScope()
    {
        // 现状 pin（跨波 sweep 票已落地）：X-SWEEP T1 已将 HttpPatch 补入 IsMutating 谓词（八波齐补），
        // 谓词现含 PATCH，未来任何 [HttpPatch] 写端点漏贴权限会被核心闸抓红（不再 fail-open）。
        // 本自检从『钉票』转为『现状 pin』：断言本波扫描面 PATCH 端点数当前 == 0（全仓零 PATCH 端点的事实快照）。
        // 若未来 Plan/Pub 引入 PATCH 端点，本断言即红——提示更新此现状快照（谓词已就位，无需再动 IsMutating）。
        var patchCount = PlanPubControllers
            .SelectMany(ActionMethods)
            .Count(m => m.GetCustomAttributes<HttpPatchAttribute>().Any());
        Assert.Equal(0, patchCount);
    }

    [Fact]
    public void NoReadOnlyGetAction_HasRequirePermission()
    {
        // 只读误贴防护：纯 HttpGet（且非变更）端点不应带 [RequirePermission]（本波未给 GET 贴）。
        var offenders = new List<string>();
        foreach (var c in PlanPubControllers)
        foreach (var m in ActionMethods(c))
        {
            var readOnly = IsGet(m) && !IsMutating(m);
            if (readOnly && ReadPermission(m) != null)
                offenders.Add($"{Key(c, m)}：只读 GET 端点误贴 [RequirePermission]");
        }
        Assert.True(offenders.Count == 0, "只读端点误贴权限点:\n" + string.Join("\n", offenders));
    }
}
