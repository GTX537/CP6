using System.Reflection;
using System.Text.RegularExpressions;
using CP6.Core.Auth;
using CP6.WebApi.Controllers.Mes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CP6.Tests;

/// <summary>MES endpoint authorization guard. Adopted read Owners require independent query/export actions;
/// non-GET routes have no read-only permission exemption. Existing unadopted GET contracts remain unchanged.</summary>
public class MesPermissionAttributeTests
{
    /// <summary>menu 键约定：mes- 前缀 + 小写字母数字连字符。</summary>
    private static readonly Regex MenuPattern = new("^mes-[a-z0-9-]+$", RegexOptions.Compiled);

    private static readonly HashSet<string> ActionVocabulary = new()
    {
        "add", "edit", "del", "issue", "start", "suspend", "complete", "report",
        "status", "downtime", "recalculate", "reschedule", "arrange", "query", "export"
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
        // Prevent a namespace/assembly change from silently producing an empty scan.
        Assert.Equal(11, MesControllers.Count());
    }

    [Fact]
    public void EveryNonGetAction_IsGuarded_WithConventionalKey()
    {
        var methods=MesControllers.SelectMany(c=>ActionMethods(c).Where(IsMutating).Select(m=>(c,m))).ToArray();
        Assert.NotEmpty(methods);
        foreach(var (c,m) in methods)
        {
            var permission=ReadPermission(m);
            Assert.True(permission!=null,$"{Key(c,m)} requires its own action");
            Assert.Matches(MenuPattern,permission!.Value.menu);
            Assert.Contains(permission.Value.action,ActionVocabulary);
        }
    }

    [Fact]
    public void AchievementReadPosts_RequireIndependentQueryAndExport()
    {
        Assert.Equal(("mes-plan-achievement","query"),ReadPermission(typeof(PlanAchievementController).GetMethod("Summary")!));
        Assert.Equal(("mes-plan-achievement","export"),ReadPermission(typeof(PlanAchievementController).GetMethod("ExportCsv")!));
    }

    [Fact]
    public void AdoptedOwnerReads_RequireIndependentQuery_WhileLegacyUnadoptedReadsRemainUnchanged()
    {
        // Adopted service Owners require explicit query; untouched legacy endpoints retain their historical contract.
        var offenders = new List<string>();
        foreach (var c in MesControllers)
        foreach (var m in ActionMethods(c))
        {
            var readOnly = IsGet(m) && !IsMutating(m);
            if(c==typeof(WorkOrderController) && readOnly)
            {
                Assert.Equal(("mes-work-order","query"),ReadPermission(m));
                continue;
            }
            if(c==typeof(ProductionResultController) && readOnly)
            {
                Assert.Equal(("mes-production-result","query"),ReadPermission(m));
                continue;
            }
            if(c==typeof(MesDashboardController) && readOnly)
            {
                Assert.Equal(("mes-dashboard","query"),ReadPermission(m));
                continue;
            }
            if(c==typeof(DefectRecordController) && readOnly)
            {
                Assert.Equal(("mes-defect","query"),ReadPermission(m));
                continue;
            }
            if(c==typeof(QualityInspectionController) && readOnly)
            {
                Assert.Equal(("mes-quality-inspection","query"),ReadPermission(m));
                continue;
            }
            if(c==typeof(MachineController) && readOnly)
            {
                Assert.Equal(("mes-machine","query"),ReadPermission(m));
                continue;
            }
            if(c==typeof(OeeController) && readOnly)
            {
                Assert.Equal(("mes-oee","query"),ReadPermission(m));
                continue;
            }
            if(c==typeof(PlanningBoardController) && readOnly)
            {
                Assert.Equal(("mes-planning-board","query"),ReadPermission(m));
                continue;
            }
            if (readOnly && ReadPermission(m) != null)
                offenders.Add($"{Key(c, m)}：只读 GET 端点误贴 [RequirePermission]");
        }
        Assert.True(offenders.Count == 0, "只读端点误贴权限点:\n" + string.Join("\n", offenders));
    }
}
