using System.Reflection;
using System.Text.RegularExpressions;
using CP6.Core.Auth;
using CP6.WebApi.Controllers.Wms;
using Microsoft.AspNetCore.Mvc;

namespace CP6.Tests.Wms;

/// <summary>
/// Reflection guard for every WMS controller:
/// mutating actions must be permission-protected, permission keys must follow the
/// vocabulary contract, and read-only management/reporting actions may use
/// their dedicated least-privilege action instead of broad view access.
/// </summary>
public class WmsPermissionAttributeTests
{
    private static readonly Regex MenuPattern =
        new("^wms-[a-z0-9-]+$", RegexOptions.Compiled);

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
        "assign", "claim",
        "pause", "release", "takeover", "exception",
        "barcode-manage", "device-manage", "analytics",
        "serial-manage", "lpn-manage",
        "label-manage", "label-print",
    };

    private static readonly HashSet<string> ReadOnlyActionVocabulary = new()
    {
        "view",
        "barcode-manage",
        "device-manage",
        "analytics",
        "label-manage",
        "label-print",
    };

    private static IEnumerable<Type> WmsControllers =>
        typeof(StockController).Assembly.GetTypes()
            .Where(t => t.Namespace == "CP6.WebApi.Controllers.Wms"
                        && typeof(ControllerBase).IsAssignableFrom(t)
                        && !t.IsAbstract)
            .OrderBy(t => t.Name);

    private static IEnumerable<MethodInfo> ActionMethods(Type type) =>
        type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(method => !method.IsSpecialName);

    private static bool IsMutating(MethodInfo method) =>
        method.GetCustomAttributes<HttpPostAttribute>().Any()
        || method.GetCustomAttributes<HttpPutAttribute>().Any()
        || method.GetCustomAttributes<HttpPatchAttribute>().Any()
        || method.GetCustomAttributes<HttpDeleteAttribute>().Any();

    private static bool IsGet(MethodInfo method) =>
        method.GetCustomAttributes<HttpGetAttribute>().Any();

    private static (string menu, string action)? ReadPermission(MethodInfo method)
    {
        var data = CustomAttributeData.GetCustomAttributes(method)
            .FirstOrDefault(attribute => attribute.AttributeType == typeof(RequirePermissionAttribute));
        if (data is null)
        {
            return null;
        }

        var arguments = data.ConstructorArguments;
        return ((string)arguments[0].Value!, (string)arguments[1].Value!);
    }

    [Fact]
    public void WmsControllers_AreDiscovered()
    {
        Assert.Equal(43, WmsControllers.Count());
    }

    [Fact]
    public void EveryMutatingAction_HasRequirePermission_WithConventionalKey()
    {
        var offenders = new List<string>();
        foreach (var controller in WmsControllers)
        foreach (var method in ActionMethods(controller).Where(IsMutating))
        {
            var permission = ReadPermission(method);
            if (permission is null)
            {
                offenders.Add(
                    $"{controller.Name}.{method.Name}: mutating action is missing RequirePermission");
                continue;
            }

            if (!MenuPattern.IsMatch(permission.Value.menu))
            {
                offenders.Add(
                    $"{controller.Name}.{method.Name}: menu '{permission.Value.menu}' does not match ^wms-[a-z0-9-]+$");
            }

            if (!ActionVocabulary.Contains(permission.Value.action))
            {
                offenders.Add(
                    $"{controller.Name}.{method.Name}: action '{permission.Value.action}' is not in the permission vocabulary");
            }
        }

        Assert.True(
            offenders.Count == 0,
            "Mutating WMS actions have missing or invalid permissions.\n"
            + string.Join("\n", offenders));
    }

    [Fact]
    public void ReadOnlyGetAction_UsesAReadOrManagementPermissionWhenPresent()
    {
        var offenders = new List<string>();
        foreach (var controller in WmsControllers)
        foreach (var method in ActionMethods(controller))
        {
            var readOnly = IsGet(method) && !IsMutating(method);
            var permission = ReadPermission(method);
            if (readOnly
                && permission is not null
                && !ReadOnlyActionVocabulary.Contains(permission.Value.action))
            {
                offenders.Add(
                    $"{controller.Name}.{method.Name}: GET permission '{permission.Value.action}' is not an approved read permission");
            }
        }

        Assert.True(
            offenders.Count == 0,
            "Read-only GET actions must declare an approved read or management permission.\n"
            + string.Join("\n", offenders));
    }
}
