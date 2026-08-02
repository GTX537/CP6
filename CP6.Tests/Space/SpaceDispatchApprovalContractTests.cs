using System.Reflection;
using CP6.Core.Auth;
using CP6.Space.Application;
using CP6.Space.Contracts;
using CP6.Space.Infrastructure;
using CP6.WebApi.Controllers.Space;
using CP6.WebApi.Filters;
using CP6.WebApi.OpenApi;
using Microsoft.AspNetCore.Mvc;

namespace CP6.Tests.Space;

public sealed class SpaceDispatchApprovalContractTests
{
    [Fact]
    public void Endpoints_are_internal_audited_and_not_design_v1()
    {
        var controller = typeof(SpaceDispatchApprovalController);
        var route = Assert.Single(controller.GetCustomAttributes<RouteAttribute>());
        Assert.Equal(
            "api/space/operations/v1/sites/{siteId:guid}/dispatch-recommendations/{recommendationId:guid}/approval-requests",
            route.Template);
        Assert.Empty(controller.GetCustomAttributes<SpaceDesignV1ContractAttribute>());

        AssertEndpoint(
            nameof(SpaceDispatchApprovalController.Submit),
            "operations:dispatch:submit",
            "space.operations.dispatch-approval.submit",
            auditRead: false);
        AssertEndpoint(
            nameof(SpaceDispatchApprovalController.Get),
            "operations:dispatch:read",
            "space.operations.dispatch-approval.read",
            auditRead: true);
        AssertEndpoint(
            nameof(SpaceDispatchApprovalController.Cancel),
            "operations:dispatch:cancel",
            "space.operations.dispatch-approval.cancel",
            auditRead: false);
        AssertEndpoint(
            nameof(SpaceDispatchApprovalController.GetExecution),
            "operations:dispatch:read",
            "space.operations.dispatch-execution.read",
            auditRead: true,
            resourceType: "DispatchExecution");
        AssertEndpoint(
            nameof(SpaceDispatchApprovalController.GetEvaluation),
            "operations:dispatch:read",
            "space.operations.dispatch-evaluation.read",
            auditRead: true,
            resourceType: "DispatchOutcomeEvaluation");
        AssertEndpoint(
            nameof(SpaceDispatchApprovalController.Retry),
            "operations:dispatch:retry",
            "space.operations.dispatch-execution.retry",
            auditRead: false,
            resourceType: "DispatchExecutionAction",
            resourceIdArgument: "actionId");
        AssertEndpoint(
            nameof(SpaceDispatchApprovalController.Compensate),
            "operations:dispatch:compensate",
            "space.operations.dispatch-execution.compensate",
            auditRead: false,
            resourceType: "DispatchExecutionAction",
            resourceIdArgument: "actionId");
    }

    [Fact]
    public void Public_contract_accepts_only_bounded_selection_and_exposes_no_user_mapping()
    {
        Assert.Equal(
            ["Reason", "SelectedRanks"],
            Properties<SubmitSpaceDispatchApprovalRequest>().Order());

        var selection = Properties<SpaceDispatchApprovalSelectionDto>();
        Assert.Contains("Rank", selection);
        Assert.Contains("TaskId", selection);
        Assert.Contains("PersonExternalId", selection);
        Assert.DoesNotContain("UserId", selection);
        Assert.DoesNotContain("PersonUserId", selection);
        Assert.DoesNotContain("AssignedTo", selection);

        var approval = Properties<SpaceDispatchApprovalRequestDto>();
        Assert.Contains("Selections", approval);
        Assert.Contains("Receipts", approval);
        Assert.DoesNotContain("SelectionJson", approval);
        Assert.DoesNotContain("RecommendationRequestHash", approval);

        Assert.Equal(
            ["Reason"],
            Properties<SubmitSpaceDispatchExecutionActionRequest>());
        var executionTask = Properties<SpaceDispatchExecutionTaskDto>();
        Assert.Contains("WmsStatus", executionTask);
        Assert.Contains("State", executionTask);
        Assert.DoesNotContain("AssignedTo", executionTask);
        Assert.DoesNotContain("UserId", executionTask);
        Assert.DoesNotContain("PersonUserId", executionTask);
    }

    [Fact]
    public void Workflow_status_adapter_and_service_surface_are_frozen()
    {
        Assert.Equal("SPACE_DISPATCH_ASSIGNMENT", SpaceDispatchApprovalService.ApprovalBizType);
        Assert.Equal("cp6-mobile-task-assignment-v1", Cp6SpaceDispatchTaskAdapter.AdapterVersion);
        Assert.Equal(
            ["Applied", "Cancelled", "Compensated", "FailedNoEffect", "PendingApproval", "Rejected", "Stale"],
            typeof(SpaceDispatchApprovalStatus)
                .GetFields(BindingFlags.Public | BindingFlags.Static)
                .Select(value => (string)value.GetRawConstantValue()!)
                .Order());

        var methods = typeof(ISpaceDispatchApprovalService)
            .GetMethods()
            .OrderBy(value => value.Name)
            .ToArray();
        Assert.Equal(3, methods.Length);
        Assert.Equal(
            ["CancelAsync", "GetAsync", "SubmitAsync"],
            methods.Select(value => value.Name));

        var executionMethods = typeof(ISpaceDispatchExecutionService)
            .GetMethods()
            .OrderBy(value => value.Name)
            .ToArray();
        Assert.Equal(
            ["CompensateAsync", "GetExecutionAsync", "RetryAsync"],
            executionMethods.Select(value => value.Name));
    }

    private static void AssertEndpoint(
        string methodName,
        string permissionAction,
        string auditAction,
        bool auditRead,
        string resourceType = "DispatchApprovalRequest",
        string resourceIdArgument = "approvalRequestId")
    {
        var method = typeof(SpaceDispatchApprovalController).GetMethod(methodName);
        Assert.NotNull(method);
        var permission = Assert.Single(
            CustomAttributeData.GetCustomAttributes(method!),
            value => value.AttributeType == typeof(RequirePermissionAttribute));
        Assert.Equal("space", permission.ConstructorArguments[0].Value);
        Assert.Equal(permissionAction, permission.ConstructorArguments[1].Value);
        Assert.True((bool)Assert.Single(
            permission.NamedArguments,
            value => value.MemberName == "UseProblemDetails").TypedValue.Value!);

        var audit = Assert.Single(method!.GetCustomAttributes<SpaceAuditOperationAttribute>());
        Assert.Equal(auditAction, audit.Action);
        Assert.Equal(resourceType, audit.ResourceType);
        Assert.Equal(resourceIdArgument, audit.ResourceIdArgument);
        Assert.Equal("siteId", audit.SiteIdArgument);
        Assert.Equal($"space:{permissionAction}", audit.PermissionCode);
        Assert.Equal(auditRead, audit.AuditRead);
    }

    private static HashSet<string> Properties<T>() =>
        typeof(T).GetProperties()
            .Select(value => value.Name)
            .ToHashSet(StringComparer.Ordinal);
}
