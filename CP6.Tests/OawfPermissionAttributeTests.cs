using System.Reflection;
using System.Text.RegularExpressions;
using CP6.Core.Auth;
using CP6.WebApi.Controllers.Oa;
using CP6.WebApi.Controllers.Wf;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Authorization;

namespace CP6.Tests;

// 当前入口登记见 docs/development/evidence/W679-01/DELIVERY.md。
// 这里只校验接线登记；实际 Owner 权限由行为回归覆盖。未知新端点仍失败。
public class OawfPermissionAttributeTests
{
    private static readonly Regex MenuPattern = new("^oa-[a-z0-9-]+$", RegexOptions.Compiled);

    private static readonly HashSet<string> ActionVocabulary = new()
    {
        "add", "edit", "del",
        "favorite",
        "read",
        "submit",
        "enable",
        "withdraw",
        "approve", "transfer", "sendback",
        "addsign",
        "delegate",
        "form-save",
        "batch-transfer",
        "FlowTrigger.View", "FlowTrigger.Edit",
        "Calendar.Edit", "Calendar.View",
        "Connector.Edit", "Connector.View",
    };

    private static readonly HashSet<string> ReadOnlyPostExemptions = new()
    {
        "ForecastController.Preview",
        "QueryController.Search",
    };

    // 明确到方法，不按整个控制器放行；命令重读由 Owner 区分当前读权与写权。
    private static readonly HashSet<string> BoundaryGuardedActions = new()
    {
        "ApprovalBindingController.Create", "ApprovalBindingController.Revise", "ApprovalBindingController.Disable",
        "DraftController.Create", "DraftController.Update", "DraftController.Rebase", "DraftController.Submit", "DraftController.Delete",
        "FlowAuthoringController.Preview", "FlowAuthoringController.Simulate", "FlowAuthoringController.Publish", "FlowAuthoringController.Clone", "FlowAuthoringController.Next", "FlowAuthoringController.Activate", "FlowAuthoringController.Save",
        "FlowAuthoringPolicyController.Save",
        "FlowPublicationPolicyController.Save",
        "FlowDraftController.Create",
        "FormAuthoringController.LegacySource", "FormAuthoringController.LegacySourceClose", "FormAuthoringController.Rules", "FormAuthoringController.Save", "FormAuthoringController.Preview", "FormAuthoringController.Publish", "FormAuthoringController.Clone", "FormAuthoringController.Next", "FormAuthoringController.Activate",
        "FormAuthoringPolicyController.Save",
        "FormPublicationPolicyController.Save",
        "FormFieldDispositionController.CreateRequest", "FormFieldDispositionController.Decide", "FormFieldDispositionController.Hold", "FormFieldDispositionController.Close",
        "FormFilesController.Upload", "FormFilesController.Read", "FormFilesController.Command", "FormFilesController.Close", "FormFilesController.Download", "FormFilesController.InboxRead", "FormFilesController.InboxDownload", "FormFilesController.QueryRead", "FormFilesController.QueryDownload",
        "FormSubmissionController.Submit",
        "NativeFormQueryController.Search",
        "NativeInboxController.TaskRead", "NativeInboxController.CcRead", "NativeInboxController.Close",
        "NativeTaskBatchesController.Preview", "NativeTaskBatchesController.Transfer",
        "NativeTaskOperationsController.Decide", "NativeTaskOperationsController.DecisionClose", "NativeTaskOperationsController.Preview", "NativeTaskOperationsController.SendBack", "NativeTaskOperationsController.Transfer", "NativeTaskOperationsController.AddSign", "NativeTaskOperationsController.Close",
        "PersonalFormController.AdoptLegacy", "PersonalFormController.Favorite", "PersonalFormController.FormPreview", "PersonalFormController.Options", "PersonalFormController.Create", "PersonalFormController.Submit", "PersonalFormController.DraftPreview", "PersonalFormController.Save", "PersonalFormController.Delete", "PersonalFormController.Restore", "PersonalFormController.SubmitDraft", "PersonalFormController.Preview", "PersonalFormController.Rebase", "PersonalFormController.RecoverSnapshot", "PersonalFormController.Close", "PersonalFormController.SendBackPreview", "PersonalFormController.SendBack", "PersonalFormController.Transfer", "PersonalFormController.AddSign", "PersonalFormController.EditReturned", "PersonalFormController.ResubmitReturned", "PersonalFormController.ResolveApprover", "PersonalFormController.Decide", "PersonalFormController.DecideSfs", "PersonalFormController.Configure",
        "TaskDecisionController.Decide", "TaskDecisionController.CloseCommand",
        "TaskDelegationController.Create", "TaskDelegationController.Revise", "TaskDelegationController.Revoke", "TaskDelegationController.Close",
    };

    // 已关闭且不提供业务结果的旧入口，与仍有当前权限读取的 Owner 分开登记。
    private static readonly HashSet<string> RetiredActions = new()
    {
        "DefinitionController.Retired",
        "DesignerController.Retired",
        "FlowAdminController.Retired",
        "FlowController.DefinitionRetired",
        "FormController.DefinitionRetired",
    };

    private static IEnumerable<Type> OawfControllers =>
        typeof(InboxController).Assembly.GetTypes()
            .Where(t => (t.Namespace == "CP6.WebApi.Controllers.Oa"
                         || t.Namespace == "CP6.WebApi.Controllers.Wf")
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
    public void OawfControllers_AreDiscovered()
    {
        Assert.Contains(typeof(InboxController), OawfControllers);
        Assert.Contains(typeof(CP6.WebApi.Controllers.Wf.TaskController), OawfControllers);
    }

    [Fact]
    public void EveryMutatingAction_IsGuarded_WithConventionalKeyOrExemption()
    {
        var offenders = new List<string>();
        var boundaryHit = new HashSet<string>();
        var retiredHit = new HashSet<string>();
        var exemptHit = new HashSet<string>();

        foreach (var c in OawfControllers)
        foreach (var m in ActionMethods(c).Where(IsMutating))
        {
            var key = Key(c, m);
            var perm = ReadPermission(m);

            if (BoundaryGuardedActions.Contains(key) || RetiredActions.Contains(key))
            {
                Assert.True(c.IsDefined(typeof(AuthorizeAttribute), true), $"{key}：必须要求登录");
                Assert.False(c.IsDefined(typeof(AllowAnonymousAttribute), true) || m.IsDefined(typeof(AllowAnonymousAttribute), true), $"{key}：不得匿名访问");
                Assert.Null(perm);
                Assert.DoesNotContain(key, ReadOnlyPostExemptions);
                Assert.False(BoundaryGuardedActions.Contains(key) && RetiredActions.Contains(key), $"{key}：边界分类冲突");
                (BoundaryGuardedActions.Contains(key) ? boundaryHit : retiredHit).Add(key);
                continue;
            }
            if (perm == null)
            {
                if (ReadOnlyPostExemptions.Contains(key))
                    exemptHit.Add(key);
                else
                    offenders.Add($"{key}：变更端点缺 [RequirePermission] 且未登记实际边界");
                continue;
            }

            if (ReadOnlyPostExemptions.Contains(key))
                offenders.Add($"{key}：既贴 [RequirePermission] 又在豁免清单，二者互斥");

            if (!MenuPattern.IsMatch(perm.Value.menu))
                offenders.Add($"{key}：menu '{perm.Value.menu}' 不符约定 ^oa-[a-z0-9-]+$");
            if (!ActionVocabulary.Contains(perm.Value.action))
                offenders.Add($"{key}：action '{perm.Value.action}' 不在真相源 action 集（疑似 typo，须显式加入）");
        }

        Assert.True(offenders.Count == 0,
            "变更端点权限点缺失/键不合约定/豁免冲突:\n" + string.Join("\n", offenders));

        Assert.True(BoundaryGuardedActions.SetEquals(boundaryHit), "Owner 入口登记已过时");
        Assert.True(RetiredActions.SetEquals(retiredHit), "关闭入口登记已过时");
        Assert.True(ReadOnlyPostExemptions.SetEquals(exemptHit), "旧入口登记已过时");
    }

    [Fact]
    public void ReadOnlyPostExemptions_AreAllStillUntaggedMutatingEndpoints()
    {
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
    }

    [Fact]
    public void ReadPermissions_MatchCurrentDeclaredBindings()
    {
        // GET 可合法要求 query/view；核对已采用映射，不禁止读取权限。
        var expected = new Dictionary<string, (string menu, string action)>
        {
        };
        var actual = OawfControllers.SelectMany(c => ActionMethods(c)
            .Where(m => IsGet(m) && !IsMutating(m) && ReadPermission(m) != null)
            .Select(m => (key: Key(c, m), permission: ReadPermission(m)!.Value)))
            .ToDictionary(x => x.key, x => x.permission);
        Assert.Equal(expected.OrderBy(x => x.Key), actual.OrderBy(x => x.Key));
    }
}
