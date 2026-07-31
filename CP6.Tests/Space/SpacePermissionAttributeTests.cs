using System.Reflection;
using CP6.Core.Auth;
using CP6.Core.Services.Space.Observability;
using CP6.Core.Services.Sys;
using CP6.WebApi.Controllers.Space;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace CP6.Tests.Space;

/// <summary>
/// 反射守卫（波4 权限点接线）：扫 CP6.WebApi 程序集 Controllers.Space 命名空间全部 controller。
/// ① 每个变更端点（HttpPost/HttpPut/HttpDelete）必须带 [RequirePermission]，且 (menu,action)
///    落在映射白名单集合内（与 2026-07-07-space-wave4-crosscutting.md Global Constraints 映射表逐字一致）。
/// ② 每个只读端点（HttpGet，且非变更）必须**不带** [RequirePermission]（防误贴）。
///
/// 断言方式说明：RequirePermissionAttribute 的 menu/action 为 private field，实例反射不可读。
/// 故用 <see cref="CustomAttributeData"/> 读取该特性的构造参数 (menu, action)，实现白名单逐字校验
/// （非降级到存在性）。
///
/// 豁免清单（显式，只读语义的 POST）：
///   - CodeRuleController.Preview —— POST code-rule/preview 仅合成样例、不写库，按只读语义仅 [Authorize]。
///     豁免项按「不得带特性」校验（与 GET 同待遇）。
/// </summary>
public class SpacePermissionAttributeTests
{
    /// <summary>映射白名单 "menu:action"——计划 Global Constraints 映射表逐字。</summary>
    private static readonly HashSet<string> Whitelist = new()
    {
        "space-site:add", "space-site:edit", "space-site:delete",
        "space-floor:add", "space-floor:edit", "space-floor:delete",
        "space-code-rule:add", "space-code-rule:edit", "space-code-rule:delete",
        "space-code-rule:generate",
        "space-publish:publish", "space-publish:deactivate", "space-publish:adopt",
        "space-audit:read",
        "space:model:read", "space:model:edit", "space:source:upload",
        "space:model:generate-ai", "space:model:review-ai",
    };

    private static readonly Dictionary<string, string> AllowedReadPermissions =
        new()
        {
            ["LocationPublishController.ListEvents"] = "space-audit:read",
            ["SpaceAuditController.Query"] = "space-audit:read",
            ["SpaceAuditController.Timeline"] = "space-audit:read",
            ["SpaceDesignV1Controller.GetModel"] = "space:model:read",
            ["SpaceDesignV1Controller.GetVersions"] = "space:model:read",
            ["SpaceDesignV1Controller.GetVersion"] = "space:model:read",
            ["SpaceDesignV1Controller.GetScene"] = "space:model:read",
            ["SpaceDesignV1Controller.GetAssets"] = "space:model:read",
            ["SpaceDesignV1Controller.GetSources"] = "space:model:read",
            ["SpaceDesignV1Controller.GetJob"] = "space:model:read",
            ["SpaceDesignV1Controller.GetIssues"] = "space:model:read",
        };

    /// <summary>只读语义的 POST 豁免（Controller.Method）——按「不得带特性」校验。</summary>
    private static readonly HashSet<string> ReadOnlyPostExemptions = new()
    {
        "CodeRuleController.Preview",
    };

    private static IEnumerable<Type> SpaceControllers =>
        typeof(SpaceMasterController).Assembly.GetTypes()
            .Where(t => t.Namespace == "CP6.WebApi.Controllers.Space"
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

    private static bool IsExempt(Type c, MethodInfo m) =>
        ReadOnlyPostExemptions.Contains($"{c.Name}.{m.Name}");

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
    public void SpaceControllers_AreDiscovered()
    {
        // 守卫：确保反射确实扫到 11 个 controller（防命名空间/程序集变动导致「空扫空过」）。
        Assert.Equal(11, SpaceControllers.Count());
    }

    [Fact]
    public void EveryMutatingAction_HasRequirePermission_InWhitelist()
    {
        var offenders = new List<string>();
        foreach (var c in SpaceControllers)
            foreach (var m in ActionMethods(c).Where(IsMutating))
            {
                if (IsExempt(c, m)) continue; // 豁免项在专门用例校验「不得带」
                var perm = ReadPermission(m);
                if (perm == null)
                {
                    offenders.Add($"{c.Name}.{m.Name}：变更端点缺 [RequirePermission]");
                    continue;
                }
                var key = $"{perm.Value.menu}:{perm.Value.action}";
                if (!Whitelist.Contains(key) || key == "space-audit:read")
                    offenders.Add($"{c.Name}.{m.Name}：键 '{key}' 不在映射白名单");
            }
        Assert.True(offenders.Count == 0, "变更端点权限点缺失/越界:\n" + string.Join("\n", offenders));
    }

    [Fact]
    public void ReadOnly_permissions_match_the_exact_audit_allowlist()
    {
        var offenders = new List<string>();
        foreach (var c in SpaceControllers)
            foreach (var m in ActionMethods(c))
            {
                var readOnly = (IsGet(m) && !IsMutating(m)) || IsExempt(c, m);
                if (!readOnly)
                    continue;

                var actionName = $"{c.Name}.{m.Name}";
                var actual = ReadPermission(m);
                if (AllowedReadPermissions.TryGetValue(
                        actionName,
                        out var expected))
                {
                    var key = actual is null
                        ? null
                        : $"{actual.Value.menu}:{actual.Value.action}";
                    if (key != expected)
                        offenders.Add(
                            $"{actionName}：期望 '{expected}'，实际 '{key ?? "<none>"}'");
                }
                else if (actual is not null)
                {
                    offenders.Add(
                        $"{actionName}：非审计 GET/只读豁免误贴 [RequirePermission]");
                }
            }

        var discovered = SpaceControllers
            .SelectMany(c => ActionMethods(c).Select(m => (c, m)))
            .Where(x => IsGet(x.m) && ReadPermission(x.m) is not null)
            .Select(x => $"{x.c.Name}.{x.m.Name}")
            .ToHashSet();
        if (!discovered.SetEquals(AllowedReadPermissions.Keys))
            offenders.Add("带权限 GET 集合与唯一允许清单不一致");

        Assert.True(
            offenders.Count == 0,
            "只读端点权限越界:\n" + string.Join("\n", offenders));
    }

    [Fact]
    public void Design_source_creation_requires_upload_and_model_edit()
    {
        var method = typeof(SpaceDesignV1Controller)
            .GetMethod(nameof(SpaceDesignV1Controller.CreateSource));
        Assert.NotNull(method);

        var permissions = CustomAttributeData
            .GetCustomAttributes(method!)
            .Where(data =>
                data.AttributeType == typeof(RequirePermissionAttribute))
            .Select(data =>
                $"{data.ConstructorArguments[0].Value}:" +
                $"{data.ConstructorArguments[1].Value}")
            .ToHashSet();

        Assert.True(permissions.SetEquals(
        [
            "space:source:upload",
            "space:model:edit",
        ]));
    }

    [Fact]
    public async Task Space_mutation_permission_denial_appends_one_safe_denied_event()
    {
        var writer = new CapturingAuditWriter(true);
        var context = AuthorizationContext(
            writer,
            method: "delete",
            path: "/api/space/floor/11111111-1111-1111-1111-111111111111",
            controller: "SpaceMaster",
            action: "DeleteFloor");

        await new RequirePermissionAttribute(
            "space-floor",
            "delete").OnAuthorizationAsync(context);

        var result = Assert.IsType<ObjectResult>(context.Result);
        Assert.Equal(StatusCodes.Status403Forbidden, result.StatusCode);
        var audit = Assert.Single(writer.Inputs);
        Assert.Equal("space.permission.check", audit.Action);
        Assert.Equal("SpaceAction", audit.ResourceType);
        Assert.Equal(context.HttpContext.Request.Path.Value, audit.ResourceId);
        Assert.Equal(SpaceAuditOutcome.Denied, audit.Outcome);
        Assert.Equal("SPACE_PERMISSION_DENIED", audit.ReasonCode);
        Assert.Equal("space-floor:delete", audit.Evidence!.PermissionCode);
        Assert.Equal("Denied", audit.Evidence.AuthorizationResult);
        Assert.Equal("Web", audit.ClientType);
        Assert.Equal("127.0.0.1", audit.IpAddress);
        Assert.Equal("space-permission-test", audit.UserAgent);
        Assert.False(Assert.Single(writer.Tokens).CanBeCanceled);
        Assert.DoesNotContain(
            "request-body-secret",
            audit.ToString(),
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Space_permission_denied_audit_failure_still_returns_403()
    {
        var writer = new CapturingAuditWriter(false);
        var context = AuthorizationContext(
            writer,
            method: HttpMethods.Post,
            path: "/api/space/floor",
            controller: "SpaceMaster",
            action: "CreateFloor");

        await new RequirePermissionAttribute(
            "space-floor",
            "add").OnAuthorizationAsync(context);

        var result = Assert.IsType<ObjectResult>(context.Result);
        Assert.Equal(StatusCodes.Status403Forbidden, result.StatusCode);
        Assert.Single(writer.Inputs);
        Assert.Equal(SpaceAuditOutcome.Denied, writer.Inputs[0].Outcome);
    }

    [Theory]
    [InlineData("GET", "/api/order")]
    [InlineData("POST", "/api/order/create")]
    public async Task Non_space_permission_denial_does_not_audit(
        string method,
        string path)
    {
        var writer = new CapturingAuditWriter(true);
        var context = AuthorizationContext(
            writer,
            method,
            path,
            controller: "Probe",
            action: "Write");

        await new RequirePermissionAttribute(
            "probe",
            "write").OnAuthorizationAsync(context);

        var result = Assert.IsType<ObjectResult>(context.Result);
        Assert.Equal(StatusCodes.Status403Forbidden, result.StatusCode);
        Assert.Empty(writer.Inputs);
    }

    private static AuthorizationFilterContext AuthorizationContext(
        ISpaceAuditWriter writer,
        string method,
        string path,
        string controller,
        string action)
    {
        var services = new ServiceCollection()
            .AddSingleton<IPermissionService>(new DeniedPermissionService())
            .AddSingleton(writer)
            .BuildServiceProvider();
        var http = new DefaultHttpContext
        {
            RequestServices = services,
        };
        http.Request.Method = method;
        http.Request.Path = path;
        http.Request.Headers.UserAgent = "space-permission-test";
        http.Connection.RemoteIpAddress =
            System.Net.IPAddress.Parse("127.0.0.1");
        var route = new RouteData();
        route.Values["controller"] = controller;
        route.Values["action"] = action;
        var actionContext = new ActionContext(
            http,
            route,
            new ActionDescriptor());
        return new AuthorizationFilterContext(
            actionContext,
            new List<IFilterMetadata>());
    }

    private sealed class DeniedPermissionService : IPermissionService
    {
        public Task<bool> HasActionAsync(string menu, string action) =>
            Task.FromResult(false);

        public Task<bool> HasMenuAsync(string menu) => Task.FromResult(false);
    }

    private sealed class CapturingAuditWriter : ISpaceAuditWriter
    {
        private readonly bool _result;

        public CapturingAuditWriter(bool result) => _result = result;

        public List<SpaceAuditEventInput> Inputs { get; } = [];
        public List<CancellationToken> Tokens { get; } = [];

        public Task<bool> TryAppendAsync(
            SpaceAuditEventInput input,
            CancellationToken ct = default)
        {
            Inputs.Add(input);
            Tokens.Add(ct);
            return Task.FromResult(_result);
        }
    }
}
