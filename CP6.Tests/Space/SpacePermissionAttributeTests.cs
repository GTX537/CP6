using System.Reflection;
using System.Text;
using System.Text.Json;
using CP6.Core.Auth;
using CP6.Core.Services.Space.Observability;
using CP6.Core.Services.Sys;
using CP6.WebApi.Controllers.Space;
using CP6.WebApi.Filters;
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
        "space:model:read", "space:model:edit", "space:model:validate",
        "space:model:lease:takeover",
        "space:model:provider:manage",
        "space:model:publish", "space:model:rollback",
        "space:source:upload",
        "space:model:generate-ai", "space:model:review-ai",
        "space:integration:manage",
        "space:external:read", "space:external:manage",
        "space:operations:diagnostics:read",
        "space:operations:recommendations:read",
        "space:operations:recommendations:generate",
        "space:operations:dispatch:read",
        "space:operations:dispatch:submit",
        "space:operations:dispatch:cancel",
        "space:operations:dispatch:retry",
        "space:operations:dispatch:compensate",
        "space:planning:scenario:read",
        "space:planning:scenario:create",
        "space:planning:dataset:read",
        "space:planning:dataset:create",
        "space:planning:simulation:read",
        "space:planning:simulation:create",
        "space:planning:comparison:read",
        "space:planning:comparison:create",
        "space:planning:decision:read",
        "space:planning:decision:create",
        "space:planning:exchange:read",
        "space-ai-admin:read", "space-ai-admin:manage",
        "space-control-tower:manage",
    };

    private static readonly Dictionary<string, string> AllowedReadPermissions =
        new()
        {
            ["LocationPublishController.ListEvents"] = "space-audit:read",
            ["SpaceAuditController.Query"] = "space-audit:read",
            ["SpaceAuditController.Timeline"] = "space-audit:read",
            ["SpaceDesignV1Controller.GetModel"] = "space:model:read",
            ["SpaceDesignV1Controller.GetPublishedScene"] =
                "space:model:read",
            ["SpaceDesignV1Controller.GetVersions"] = "space:model:read",
            ["SpaceDesignV1Controller.GetVersion"] = "space:model:read",
            ["SpaceDesignV1Controller.GetScene"] = "space:model:read",
            ["SpaceEditLeaseController.GetEditLease"] = "space:model:edit",
            ["SpaceCadParseController.GetPreparationStatus"] =
                "space:source:upload",
            ["SpaceCadProviderController.GetCapability"] =
                "space:model:read",
            ["SpaceCadParseController.GetMappingProfiles"] =
                "space:source:upload",
            ["SpaceCadParseController.GetParse"] = "space:model:read",
            ["SpaceCadParseController.GetReviewWorkspace"] =
                "space:model:read",
            ["SpaceDesignV1Controller.GetAssets"] = "space:model:read",
            ["SpaceDesignV1Controller.GetSources"] = "space:model:read",
            ["SpaceDesignV1Controller.GetFile"] = "space:model:read",
            ["SpaceDesignV1Controller.GetUnderlayContent"] = "space:model:read",
            ["SpacePublishController.GetHistoricalRepublish"] = "space:model:read",
            ["SpaceDesignV1Controller.DownloadStandardExcelTemplate"] =
                "space:model:read",
            ["SpaceExcelMappingController.GetProfiles"] = "space:model:read",
            ["SpaceExcelMappingController.GetProfile"] = "space:model:read",
            ["SpaceExcelPreflightController.GetPreflight"] = "space:model:read",
            ["SpaceExcelPreflightController.DownloadErrorReport"] =
                "space:model:read",
            ["SpaceExcelCadMatchController.GetMatch"] =
                "space:model:read",
            ["SpaceExcelCadMatchController.GetConfirmation"] =
                "space:model:read",
            ["SpaceValidationController.GetValidation"] =
                "space:model:read",
            ["SpaceRackGenerationProfileController.GetRackGenerationProfiles"] =
                "space:model:read",
            ["SpaceRackGenerationProfileController.GetRackGenerationProfileVersion"] =
                "space:model:read",
            ["SpacePublishPreviewController.GetPublishPreview"] =
                "space:model:read",
            ["SpacePublishController.GetPublishAttempt"] =
                "space:model:read",
            ["SpacePublishActivityController.GetPublishAttempts"] =
                "space:model:read",
            ["SpaceDesignV1Controller.GetUnderlayCalibration"] = "space:model:read",
            ["SpaceDesignV1Controller.GetJob"] = "space:model:read",
            ["SpaceDesignV1Controller.GetIssues"] = "space:model:read",
            ["SpaceDesignV1Controller.GetWmsAdoptionLocations"] =
                "space:model:read",
            ["SpaceWmsRuntimeController.GetInventory"] = "space:model:read",
            ["SpaceWmsRuntimeController.LocateInventory"] = "space:model:read",
            ["SpaceWmsRuntimeController.GetWarehouseOverview"] =
                "space:model:read",
            ["SpaceWmsRuntimeController.GetTasks"] = "space:model:read",
            ["SpaceWmsRuntimeController.GetTaskPath"] = "space:model:read",
            ["SpacePersonnelRuntimeController.GetCurrentPersonnel"] =
                "space:model:read",
            ["SpacePersonnelRuntimeController.GetPersonnelTrajectory"] =
                "space-audit:read",
            ["SpaceDeviceEventsController.GetDeviceMappings"] =
                "space:model:read",
            ["SpaceDeviceRuntimeController.GetCurrentDevices"] =
                "space:model:read",
            ["SpaceAiAtomicApplyController.GetGenerationRun"] =
                "space:model:review-ai",
            ["SpaceOperationsDiagnosticController.Get"] =
                "space:operations:diagnostics:read",
            ["SpacePutawayRecommendationController.Get"] =
                "space:operations:recommendations:read",
            ["SpaceDispatchRecommendationController.Get"] =
                "space:operations:recommendations:read",
            ["SpaceDispatchApprovalController.Get"] =
                "space:operations:dispatch:read",
            ["SpaceDispatchApprovalController.GetExecution"] =
                "space:operations:dispatch:read",
            ["SpaceDispatchApprovalController.GetEvaluation"] =
                "space:operations:dispatch:read",
            ["SpaceExternalOrganizationController.GetOrganizations"] =
                "space:external:read",
            ["SpaceExternalOrganizationController.GetOrganization"] =
                "space:external:read",
            ["SpaceExternalOrganizationController.GetMemberships"] =
                "space:external:read",
            ["SpaceExternalOrganizationController.GetGrants"] =
                "space:external:read",
            ["SpaceExternalOrganizationController.GetGrant"] =
                "space:external:read",
            ["SpaceFieldPolicyController.GetFieldPolicies"] =
                "space:external:read",
            ["SpaceFieldPolicyController.GetFieldPolicy"] =
                "space:external:read",
            ["SpaceAiAdministrationController.GetPolicy"] =
                "space-ai-admin:read",
            ["SpaceAiAdministrationController.GetUsage"] =
                "space-ai-admin:read",
            ["SpaceAiProposalDecisionController.GetProposalReview"] =
                "space:model:review-ai",
            ["SpaceAiProposalDecisionController.GetGenerationProposals"] =
                "space:model:review-ai",
            ["SpaceAiProposalDecisionController.GetGenerationProposalIssues"] =
                "space:model:review-ai",
            ["SpaceAiProposalDecisionController.GetProposalDecisions"] =
                "space:model:review-ai",
            ["SpacePlanningScenarioController.GetBranch"] =
                "space:planning:scenario:read",
            ["SpacePlanningScenarioController.GetBranches"] =
                "space:planning:scenario:read",
            ["SpacePlanningDatasetController.GetHistoricalDataset"] =
                "space:planning:dataset:read",
            ["SpacePlanningDatasetController.GetHistoricalDatasets"] =
                "space:planning:dataset:read",
            ["SpacePlanningSimulationController.GetSimulationRun"] =
                "space:planning:simulation:read",
            ["SpacePlanningSimulationController.GetSimulationRuns"] =
                "space:planning:simulation:read",
            ["SpacePlanningComparisonController.GetComparison"] =
                "space:planning:comparison:read",
            ["SpacePlanningComparisonController.GetComparisons"] =
                "space:planning:comparison:read",
            ["SpacePlanningComparisonController.GetDecision"] =
                "space:planning:decision:read",
            ["SpacePlanningComparisonController.GetDecisions"] =
                "space:planning:decision:read",
            ["SpacePlanningExchangeController.DownloadGlb"] =
                "space:planning:exchange:read",
            ["SpaceAnalyticsController.ControlTower"] =
                "space-control-tower:view",
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
        // 守卫：确保反射确实扫到全部 controller（防命名空间/程序集变动导致「空扫空过」）。
        Assert.Equal(45, SpaceControllers.Count());
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

    [Theory]
    [InlineData(nameof(SpaceDesignV1Controller.CreateSource))]
    [InlineData(nameof(SpaceDesignV1Controller.UploadUnderlay))]
    [InlineData(nameof(SpaceDesignV1Controller.AttachUnderlay))]
    [InlineData(nameof(SpaceDesignV1Controller.CalibrateUnderlay))]
    public void Design_source_mutations_require_upload_and_model_edit(
        string methodName)
    {
        var method = typeof(SpaceDesignV1Controller)
            .GetMethod(methodName);
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

    [Theory]
    [InlineData(nameof(SpaceCadParseController.GetPreparationStatus))]
    [InlineData(nameof(SpaceCadParseController.GetMappingProfiles))]
    [InlineData(nameof(SpaceCadParseController.PreviewPreparation))]
    [InlineData(nameof(SpaceCadParseController.StartParse))]
    public void Cad_preparation_requires_upload_and_model_edit(
        string methodName)
    {
        var method = typeof(SpaceCadParseController).GetMethod(methodName);
        Assert.NotNull(method);
        var permissions = CustomAttributeData
            .GetCustomAttributes(method!)
            .Where(data => data.AttributeType == typeof(RequirePermissionAttribute))
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
    public void Lease_takeover_requires_edit_and_takeover_permissions()
    {
        var method = typeof(SpaceEditLeaseController).GetMethod(
            nameof(SpaceEditLeaseController.TakeoverEditLease));
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
            "space:model:edit",
            "space:model:lease:takeover",
        ]));
    }

    [Fact]
    public void Cad_provider_configuration_requires_dedicated_permission()
    {
        var method = typeof(SpaceCadProviderController).GetMethod(
            nameof(SpaceCadProviderController.ReplaceProviderConfiguration));
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
            "space:model:provider:manage",
        ]));
    }

    [Theory]
    [InlineData(nameof(SpaceExcelPreflightController.UploadExcelSource))]
    [InlineData(nameof(SpaceExcelPreflightController.StartPreflight))]
    public void Excel_preflight_mutations_require_upload_and_model_edit(
        string methodName)
    {
        var method = typeof(SpaceExcelPreflightController)
            .GetMethod(methodName);
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
    public void Wms_refresh_requires_integration_manage_and_model_edit()
    {
        var method = typeof(SpaceDesignV1Controller)
            .GetMethod(nameof(SpaceDesignV1Controller.RefreshWmsAdoption));
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
            "space:integration:manage",
            "space:model:edit",
        ]));
    }

    [Theory]
    [InlineData(
        nameof(SpaceValidationController.CreateValidation),
        "space:model:validate",
        "space.validation.start",
        false)]
    [InlineData(
        nameof(SpaceValidationController.GetValidation),
        "space:model:read",
        "space.validation.read",
        true)]
    public void Validation_endpoints_have_stable_permission_and_audit_metadata(
        string methodName,
        string permissionCode,
        string action,
        bool auditRead)
    {
        var method = typeof(SpaceValidationController).GetMethod(methodName);
        Assert.NotNull(method);
        var permission = Assert.Single(
            method!.GetCustomAttributes<RequirePermissionAttribute>());
        var data = Assert.Single(
            CustomAttributeData.GetCustomAttributes(method),
            value =>
                value.AttributeType == typeof(RequirePermissionAttribute));
        Assert.Equal(
            permissionCode,
            $"{data.ConstructorArguments[0].Value}:" +
            $"{data.ConstructorArguments[1].Value}");

        var audit = Assert.Single(
            method.GetCustomAttributes<SpaceAuditOperationAttribute>());
        Assert.Equal(action, audit.Action);
        Assert.Equal(permissionCode, audit.PermissionCode);
        Assert.Equal(auditRead, audit.AuditRead);
    }

    [Theory]
    [InlineData(
        nameof(SpaceRackGenerationProfileController.GetRackGenerationProfiles),
        "space:model:read",
        "space.rack-generation-profile.list",
        true)]
    [InlineData(
        nameof(SpaceRackGenerationProfileController.GetRackGenerationProfileVersion),
        "space:model:read",
        "space.rack-generation-profile-version.read",
        true)]
    [InlineData(
        nameof(SpaceRackGenerationProfileController.CreateRackGenerationProfile),
        "space:model:edit",
        "space.rack-generation-profile.create",
        false)]
    public void Rack_generation_profile_endpoints_have_stable_security_metadata(
        string methodName,
        string permissionCode,
        string action,
        bool auditRead)
    {
        var method = typeof(SpaceRackGenerationProfileController)
            .GetMethod(methodName);
        Assert.NotNull(method);
        var permission = Assert.Single(
            method!.GetCustomAttributes<RequirePermissionAttribute>());
        var permissionData = Assert.Single(
            CustomAttributeData.GetCustomAttributes(method),
            value =>
                value.AttributeType == typeof(RequirePermissionAttribute));
        Assert.Equal(
            permissionCode,
            $"{permissionData.ConstructorArguments[0].Value}:" +
            $"{permissionData.ConstructorArguments[1].Value}");
        Assert.True(permission.UseProblemDetails);

        var audit = Assert.Single(
            method.GetCustomAttributes<SpaceAuditOperationAttribute>());
        Assert.Equal(action, audit.Action);
        Assert.Equal(permissionCode, audit.PermissionCode);
        Assert.Equal(auditRead, audit.AuditRead);
    }

    [Theory]
    [InlineData(
        nameof(SpacePublishController.StartHistoricalRepublish),
        "space:model:rollback",
        "space.publish.republish",
        false)]
    [InlineData(
        nameof(SpacePublishController.GetHistoricalRepublish),
        "space:model:read",
        "space.publish.republish.read",
        true)]
    [InlineData(
        nameof(SpacePublishController.CreatePublishAttempt),
        "space:model:publish",
        "space.publish.start",
        false)]
    [InlineData(
        nameof(SpacePublishController.GetPublishAttempt),
        "space:model:read",
        "space.publish.read",
        true)]
    public void Publish_endpoints_have_stable_permission_and_audit_metadata(
        string methodName,
        string permissionCode,
        string action,
        bool auditRead)
    {
        var method = typeof(SpacePublishController).GetMethod(methodName);
        Assert.NotNull(method);
        var permission = Assert.Single(
            CustomAttributeData.GetCustomAttributes(method!),
            value => value.AttributeType == typeof(RequirePermissionAttribute));
        Assert.Equal(
            permissionCode,
            $"{permission.ConstructorArguments[0].Value}:" +
            $"{permission.ConstructorArguments[1].Value}");

        var audit = Assert.Single(
            method!.GetCustomAttributes<SpaceAuditOperationAttribute>());
        Assert.Equal(action, audit.Action);
        Assert.Equal(permissionCode, audit.PermissionCode);
        Assert.Equal(auditRead, audit.AuditRead);
    }

    [Theory]
    [InlineData(nameof(SpaceAiProposalDecisionController.CreateProposalDecision))]
    [InlineData(nameof(SpaceAiProposalDecisionController.CreateProposalBatchDecision))]
    public void Ai_proposal_decisions_require_review_and_model_edit(
        string methodName)
    {
        var method = typeof(SpaceAiProposalDecisionController)
            .GetMethod(methodName);
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
            "space:model:review-ai",
            "space:model:edit",
        ]));
    }

    [Fact]
    public void Ai_proposal_apply_requires_review_and_model_edit()
    {
        var method = typeof(SpaceAiAtomicApplyController)
            .GetMethod(nameof(
                SpaceAiAtomicApplyController.ApplyGenerationProposals));
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
            "space:model:review-ai",
            "space:model:edit",
        ]));
    }

    [Theory]
    [InlineData(
        nameof(SpaceAiAtomicApplyController.GetGenerationRun),
        "space.ai-generation-run.read",
        true)]
    [InlineData(
        nameof(SpaceAiAtomicApplyController.ApplyGenerationProposals),
        "space.ai-proposal.apply",
        false)]
    public void Ai_apply_endpoints_have_stable_audit_metadata(
        string methodName,
        string action,
        bool auditRead)
    {
        var method = typeof(SpaceAiAtomicApplyController)
            .GetMethod(methodName);
        Assert.NotNull(method);
        var audit = Assert.Single(
            method!.GetCustomAttributes<SpaceAuditOperationAttribute>());

        Assert.Equal(action, audit.Action);
        Assert.Equal("GenerationRun", audit.ResourceType);
        Assert.Equal("runId", audit.ResourceIdArgument);
        Assert.Equal("space:model:review-ai", audit.PermissionCode);
        Assert.Equal(auditRead, audit.AuditRead);
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
    [InlineData(nameof(SpaceWmsRuntimeController.GetInventory))]
    [InlineData(nameof(SpaceWmsRuntimeController.GetWarehouseOverview))]
    [InlineData(nameof(SpaceWmsRuntimeController.GetTasks))]
    public void Runtime_reads_opt_in_to_problem_details(string methodName)
    {
        var method = typeof(SpaceWmsRuntimeController).GetMethod(methodName);
        Assert.NotNull(method);
        var permission = Assert.Single(
            CustomAttributeData.GetCustomAttributes(method!),
            data =>
                data.AttributeType ==
                typeof(RequirePermissionAttribute));

        var optIn = Assert.Single(
            permission.NamedArguments,
            argument => argument.MemberName == "UseProblemDetails");
        Assert.True((bool)optIn.TypedValue.Value!);
    }

    [Fact]
    public async Task Runtime_permission_denial_serializes_space_problem_details()
    {
        var writer = new CapturingAuditWriter(true);
        var path =
            "/api/space/design/v1/sites/" +
            "11111111-1111-1111-1111-111111111111/runtime/inventory";
        var context = AuthorizationContext(
            writer,
            method: HttpMethods.Get,
            path,
            controller: "SpaceWmsRuntime",
            action: nameof(SpaceWmsRuntimeController.GetInventory));
        context.HttpContext.TraceIdentifier = "trace-fallback";
        context.HttpContext.Response.Headers["X-Trace-ID"] = "trace-123";
        context.HttpContext.Request.Headers["X-Correlation-ID"] = "corr-456";
        var method = typeof(SpaceWmsRuntimeController).GetMethod(
            nameof(SpaceWmsRuntimeController.GetInventory));
        var permission = Assert.Single(
            method!.GetCustomAttributes<RequirePermissionAttribute>());

        await permission.OnAuthorizationAsync(context);

        var result = Assert.IsType<ObjectResult>(context.Result);
        var body = await ExecuteAsync(context, result);
        Assert.Equal(StatusCodes.Status403Forbidden, result.StatusCode);
        Assert.Equal(
            StatusCodes.Status403Forbidden,
            context.HttpContext.Response.StatusCode);
        Assert.StartsWith(
            "application/problem+json",
            context.HttpContext.Response.ContentType,
            StringComparison.OrdinalIgnoreCase);
        using var document = JsonDocument.Parse(body);
        var problem = document.RootElement;
        Assert.Equal(
            "https://cp6.example/problems/space-permission-denied",
            problem.GetProperty("type").GetString());
        Assert.Equal(
            "The Space request was denied.",
            problem.GetProperty("title").GetString());
        Assert.Equal(403, problem.GetProperty("status").GetInt32());
        Assert.Equal(
            "Request access to use this Space operation.",
            problem.GetProperty("detail").GetString());
        Assert.Equal(path, problem.GetProperty("instance").GetString());
        Assert.Equal(
            "SPACE_PERMISSION_DENIED",
            problem.GetProperty("code").GetString());
        Assert.Equal("trace-123", problem.GetProperty("traceId").GetString());
        Assert.Equal(
            "corr-456",
            problem.GetProperty("correlationId").GetString());
        var recovery = problem.GetProperty("recovery");
        Assert.Equal(
            "request-access",
            recovery.GetProperty("action").GetString());
        Assert.False(recovery.GetProperty("retryable").GetBoolean());
        Assert.DoesNotContain(
            "space:model:read",
            body,
            StringComparison.OrdinalIgnoreCase);
        Assert.Single(writer.Inputs);
        Assert.False(Assert.Single(writer.Tokens).CanBeCanceled);
    }

    [Fact]
    public async Task Legacy_permission_denial_keeps_code_and_message_json()
    {
        var writer = new CapturingAuditWriter(true);
        var context = AuthorizationContext(
            writer,
            method: HttpMethods.Get,
            path: "/api/order",
            controller: "Probe",
            action: "Read");

        await new RequirePermissionAttribute(
            "probe",
            "read").OnAuthorizationAsync(context);

        var result = Assert.IsType<ObjectResult>(context.Result);
        var body = await ExecuteAsync(context, result);
        Assert.Equal(StatusCodes.Status403Forbidden, result.StatusCode);
        Assert.StartsWith(
            "application/json",
            context.HttpContext.Response.ContentType,
            StringComparison.OrdinalIgnoreCase);
        using var document = JsonDocument.Parse(body);
        Assert.Equal(403, document.RootElement.GetProperty("code").GetInt32());
        Assert.Contains(
            "probe:read",
            document.RootElement.GetProperty("message").GetString(),
            StringComparison.Ordinal);
        Assert.False(document.RootElement.TryGetProperty("recovery", out _));
        Assert.Empty(writer.Inputs);
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
            .AddLogging()
            .AddSingleton<IPermissionService>(new DeniedPermissionService())
            .AddSingleton(writer);
        services.AddControllers();
        var provider = services.BuildServiceProvider();
        var http = new DefaultHttpContext
        {
            RequestServices = provider,
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

    private static async Task<string> ExecuteAsync(
        AuthorizationFilterContext context,
        ObjectResult result)
    {
        var body = new MemoryStream();
        context.HttpContext.Response.Body = body;
        await result.ExecuteResultAsync(context);
        return Encoding.UTF8.GetString(body.ToArray());
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
