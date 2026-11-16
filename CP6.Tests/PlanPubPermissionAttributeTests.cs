using System.Reflection;
using System.Text.RegularExpressions;
using CP6.Core.Auth;
using CP6.WebApi.Controllers.Plan;
using CP6.WebApi.Controllers.Pub;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Authorization;

namespace CP6.Tests;

// 当前入口登记见 docs/development/evidence/W679-01/DELIVERY.md。
// 这里只校验接线登记；实际 Owner 权限由行为回归覆盖。未知新端点仍失败。
public class PlanPubPermissionAttributeTests
{
    private static readonly Regex MenuPattern = new("^(plan|pub)-[a-z0-9-]+$", RegexOptions.Compiled);

    private static readonly HashSet<string> MenuKeyWhitelist = new()
    {
        "plan-mrp", "plan-item-policy", "pub-codegen", "pub-seq",
    };

    private static readonly HashSet<string> ActionVocabulary = new()
    {
        "run", "convert", "save",
        "confirm", "ignore",
        "add", "delete", "edit",
        "view",
    };

    private static readonly HashSet<string> ExpectedResourceKeys = new()
    {
        "plan-mrp:run", "plan-mrp:confirm", "plan-mrp:convert", "plan-mrp:ignore",
        "plan-item-policy:add", "plan-item-policy:delete",
        "pub-codegen:save", "pub-codegen:view",
        "pub-seq:add", "pub-seq:edit", "pub-seq:delete",
    };

    private static readonly HashSet<string> ComponentExemptions = new()
    {
        "AttachmentController.Upload",
        "AttachmentController.Delete",
        "AttachmentController.Rebind",
    };

    // 明确到方法，不按整个控制器放行；命令重读由 Owner 区分当前读权与写权。
    private static readonly HashSet<string> BoundaryGuardedActions = new()
    {
        "CodeGenMetadataControllerV1.Save", "CodeGenMetadataControllerV1.Original", "CodeGenMetadataControllerV1.Adopt",
        "FileContentController.Upload", "FileContentController.Command", "FileContentController.Close",
        "NumberRuleControllerV1.Version", "NumberRuleControllerV1.Change", "NumberRuleControllerV1.Original",
    };

    // 已关闭且不提供业务结果的旧入口，与仍有当前权限读取的 Owner 分开登记。
    private static readonly HashSet<string> RetiredActions = new()
    {

    };

    private static IEnumerable<Type> PlanPubControllers =>
        typeof(MrpController).Assembly.GetTypes()
            .Where(t => (t.Namespace == "CP6.WebApi.Controllers.Plan"
                         || t.Namespace == "CP6.WebApi.Controllers.Pub")
                        && typeof(ControllerBase).IsAssignableFrom(t)
                        && !t.IsAbstract)
            .OrderBy(t => t.Name);

    private static IEnumerable<MethodInfo> ActionMethods(Type t) =>
        t.GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(m => m.GetCustomAttributes<HttpMethodAttribute>().Any());

    private static bool IsMutating(MethodInfo m) =>
        m.GetCustomAttributes<HttpPostAttribute>().Any()
        || m.GetCustomAttributes<HttpPutAttribute>().Any()
        || m.GetCustomAttributes<HttpPatchAttribute>().Any()
        || m.GetCustomAttributes<HttpDeleteAttribute>().Any();

    private static bool IsGet(MethodInfo m) => m.GetCustomAttributes<HttpGetAttribute>().Any();

    private static string Key(Type c, MethodInfo m) => $"{c.Name}.{m.Name}";

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
        Assert.Contains(typeof(MrpController), PlanPubControllers);
        Assert.Contains(typeof(CP6.WebApi.Controllers.Pub.CodeGenController), PlanPubControllers);
    }

    [Fact]
    public void EveryMutatingAction_IsGuarded_WithConventionalKeyOrExemption()
    {
        var offenders = new List<string>();
        var boundaryHit = new HashSet<string>();
        var retiredHit = new HashSet<string>();
        var exemptHit = new HashSet<string>();

        foreach (var c in PlanPubControllers)
        foreach (var m in ActionMethods(c).Where(IsMutating))
        {
            var key = Key(c, m);
            var perm = ReadPermission(m);

            if (BoundaryGuardedActions.Contains(key) || RetiredActions.Contains(key))
            {
                Assert.True(c.IsDefined(typeof(AuthorizeAttribute), true), $"{key}：必须要求登录");
                Assert.False(c.IsDefined(typeof(AllowAnonymousAttribute), true) || m.IsDefined(typeof(AllowAnonymousAttribute), true), $"{key}：不得匿名访问");
                Assert.Null(perm);
                Assert.DoesNotContain(key, ComponentExemptions);
                Assert.False(BoundaryGuardedActions.Contains(key) && RetiredActions.Contains(key), $"{key}：边界分类冲突");
                (BoundaryGuardedActions.Contains(key) ? boundaryHit : retiredHit).Add(key);
                continue;
            }
            if (perm == null)
            {
                if (ComponentExemptions.Contains(key))
                    exemptHit.Add(key);
                else
                    offenders.Add($"{key}：变更端点缺 [RequirePermission] 且未登记实际边界");
                continue;
            }

            if (ComponentExemptions.Contains(key))
                offenders.Add($"{key}：既贴 [RequirePermission] 又在豁免清单，二者互斥");

            if (!MenuPattern.IsMatch(perm.Value.menu))
                offenders.Add($"{key}：menu '{perm.Value.menu}' 不符约定 ^(plan|pub)-[a-z0-9-]+$");
            if (!MenuKeyWhitelist.Contains(perm.Value.menu))
                offenders.Add($"{key}：menu '{perm.Value.menu}' 不在 4 键白名单（真相源 §二）");
            if (!ActionVocabulary.Contains(perm.Value.action))
                offenders.Add($"{key}：action '{perm.Value.action}' 不在真相源 action 集（疑似 typo，须显式加入）");
        }

        Assert.True(offenders.Count == 0,
            "变更端点权限点缺失/键不合约定/豁免冲突:\n" + string.Join("\n", offenders));

        Assert.True(BoundaryGuardedActions.SetEquals(boundaryHit), "Owner 入口登记已过时");
        Assert.True(RetiredActions.SetEquals(retiredHit), "关闭入口登记已过时");
        Assert.True(ComponentExemptions.SetEquals(exemptHit), "旧入口登记已过时");
    }

    [Fact]
    public void ResourceKeys_MatchIndependentOracle_Exactly()
    {
        var collected = new HashSet<string>();

        foreach (var c in PlanPubControllers)
        foreach (var m in ActionMethods(c))
        {
            var perm = ReadPermission(m);
            if (perm == null) continue;
            var rk = $"{perm.Value.menu}:{perm.Value.action}";
            collected.Add(rk);
        }

        var missingInSource = ExpectedResourceKeys.Except(collected).ToList();
        var missingInOracle = collected.Except(ExpectedResourceKeys).ToList();
        Assert.True(missingInSource.Count == 0, "oracle 有但源码缺（漏贴/改键）:\n" + string.Join("\n", missingInSource));
        Assert.True(missingInOracle.Count == 0, "源码有但 oracle 缺（新增未登记）:\n" + string.Join("\n", missingInOracle));

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
    }

    [Fact]
    public void HttpPut_Endpoint_IsScannedAndGuarded()
    {
        var seq = PlanPubControllers.FirstOrDefault(t => t.Name == "SeqController");
        Assert.NotNull(seq);
        var update = seq!.GetMethod("Update", BindingFlags.Public | BindingFlags.Instance);
        Assert.NotNull(update);
        Assert.True(update!.GetCustomAttributes<HttpPutAttribute>().Any(), "SeqController.Update 应为 [HttpPut]");
        Assert.True(IsMutating(update), "IsMutating 谓词须含 HttpPut，否则 PUT 端点漏扫→漏贴不报红");
        Assert.Equal(("pub-seq", "edit"), ReadPermission(update));
    }

    [Fact]
    public void NoPatchEndpoints_InScope()
    {
        var patchCount = PlanPubControllers
            .SelectMany(ActionMethods)
            .Count(m => m.GetCustomAttributes<HttpPatchAttribute>().Any());
        Assert.Equal(0, patchCount);
    }

    [Fact]
    public void ReadPermissions_MatchCurrentDeclaredBindings()
    {
        // GET 可合法要求 query/view；核对已采用映射，不禁止读取权限。
        var expected = new Dictionary<string, (string menu, string action)>
        {
            ["CodeGenController.Preview"] = ("pub-codegen", "view"),
        };
        var actual = PlanPubControllers.SelectMany(c => ActionMethods(c)
            .Where(m => IsGet(m) && !IsMutating(m) && ReadPermission(m) != null)
            .Select(m => (key: Key(c, m), permission: ReadPermission(m)!.Value)))
            .ToDictionary(x => x.key, x => x.permission);
        Assert.Equal(expected.OrderBy(x => x.Key), actual.OrderBy(x => x.Key));
    }
}
