using CP6.Core.Services.Space.Observability;
using CP6.Space.Application;
using CP6.Space.Contracts;
using CP6.WebApi.Filters;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.Routing;
using System.Reflection;

namespace CP6.Tests.Space
{
    public sealed class SpaceAuditActionFilterTests
    {
        [Fact]
        public async Task Mutation_does_not_call_action_when_started_audit_fails()
        {
            var writer = new SequenceWriter(false);
            var filter = new SpaceAuditActionFilter(writer);
            var actionCalled = false;
            var context = MakeActionContext(
                HttpMethods.Post,
                "LocationPublish",
                "PublishFloor");

            await filter.OnActionExecutionAsync(context, () =>
            {
                actionCalled = true;
                return Task.FromResult(MakeExecuted(context, new OkResult()));
            });

            Assert.False(actionCalled);
            var result = Assert.IsType<ObjectResult>(context.Result);
            Assert.Equal(StatusCodes.Status503ServiceUnavailable, result.StatusCode);
            Assert.Equal("SPACE_AUDIT_UNAVAILABLE", Message(result));
            var started = Assert.Single(writer.Inputs);
            Assert.Equal(SpaceAuditOutcome.Started, started.Outcome);
            Assert.Equal("space.http.post", started.Action);
            Assert.Equal(
                "LocationPublish.PublishFloor",
                started.ResourceType);
        }

        [Fact]
        public async Task Successful_mutation_with_result_audit_failure_becomes_outcome_unknown()
        {
            var writer = new SequenceWriter(true, false);
            var filter = new SpaceAuditActionFilter(writer);
            var context = MakeActionContext(
                HttpMethods.Put,
                "LocationPublish",
                "Deactivate");

            var executed = await RunFilter(
                filter,
                context,
                new OkObjectResult(new { ok = true }));

            var result = Assert.IsType<ObjectResult>(executed.Result);
            Assert.Equal(StatusCodes.Status503ServiceUnavailable, result.StatusCode);
            Assert.Equal("SPACE_OPERATION_OUTCOME_UNKNOWN", Message(result));
            Assert.Equal(
                [SpaceAuditOutcome.Started, SpaceAuditOutcome.Succeeded],
                writer.Inputs.Select(x => x.Outcome));
        }

        [Fact]
        public async Task Mixed_case_post_is_still_pre_audited()
        {
            var writer = new SequenceWriter(true, true);
            var filter = new SpaceAuditActionFilter(writer);
            var context = MakeActionContext(
                "PoSt",
                "LocationPublish",
                "PublishFloor");
            var called = false;

            await filter.OnActionExecutionAsync(context, () =>
            {
                called = true;
                return Task.FromResult(
                    MakeExecuted(context, new OkResult()));
            });

            Assert.True(called);
            Assert.Equal(
                [SpaceAuditOutcome.Started, SpaceAuditOutcome.Succeeded],
                writer.Inputs.Select(x => x.Outcome));
            Assert.All(
                writer.Inputs,
                input => Assert.Equal("space.http.post", input.Action));
        }

        [Theory]
        [InlineData(StatusCodes.Status401Unauthorized)]
        [InlineData(StatusCodes.Status403Forbidden)]
        public async Task Unauthorized_and_forbidden_results_append_denied(int status)
        {
            var writer = new SequenceWriter(true, true);
            var filter = new SpaceAuditActionFilter(writer);
            var context = MakeActionContext(
                HttpMethods.Delete,
                "SpaceMaster",
                "Delete");

            await RunFilter(filter, context, new StatusCodeResult(status));

            Assert.Equal(SpaceAuditOutcome.Denied, writer.Inputs[1].Outcome);
            Assert.Equal(status.ToString(), writer.Inputs[1].Evidence!.Status);
        }

        [Fact]
        public async Task Status_code_interface_is_used_for_failure_classification()
        {
            var writer = new SequenceWriter(true, true);
            var filter = new SpaceAuditActionFilter(writer);
            var context = MakeActionContext(
                HttpMethods.Patch,
                "SpaceMaster",
                "Patch");

            await RunFilter(filter, context, new CustomStatusResult(429));

            Assert.Equal(SpaceAuditOutcome.Failed, writer.Inputs[1].Outcome);
            Assert.Equal("429", writer.Inputs[1].Evidence!.Status);
        }

        [Theory]
        [InlineData("Forbid", StatusCodes.Status403Forbidden)]
        [InlineData("Challenge", StatusCodes.Status401Unauthorized)]
        public async Task Authentication_results_append_denied_and_failed_audit_does_not_replace_result(
            string resultType,
            int expectedStatus)
        {
            var writer = new SequenceWriter(true, false);
            var filter = new SpaceAuditActionFilter(writer);
            var context = MakeActionContext(
                HttpMethods.Delete,
                "SpaceMaster",
                "Delete");
            IActionResult original = resultType == "Forbid"
                ? new ForbidResult()
                : new ChallengeResult();

            var executed = await RunFilter(filter, context, original);

            Assert.Same(original, executed.Result);
            Assert.Equal(SpaceAuditOutcome.Denied, writer.Inputs[1].Outcome);
            Assert.Equal(
                expectedStatus.ToString(),
                writer.Inputs[1].Evidence!.Status);
        }

        [Theory]
        [InlineData(StatusCodes.Status403Forbidden, "Denied")]
        [InlineData(StatusCodes.Status422UnprocessableEntity, "Failed")]
        public async Task Response_status_is_used_for_result_without_status_interface(
            int responseStatus,
            string expectedOutcome)
        {
            var writer = new SequenceWriter(true, false);
            var filter = new SpaceAuditActionFilter(writer);
            var context = MakeActionContext(
                HttpMethods.Patch,
                "SpaceMaster",
                "Patch");
            context.HttpContext.Response.StatusCode = responseStatus;
            var original = new EmptyResult();

            var executed = await RunFilter(filter, context, original);

            Assert.Same(original, executed.Result);
            Assert.Equal(expectedOutcome, writer.Inputs[1].Outcome);
            Assert.Equal(
                responseStatus.ToString(),
                writer.Inputs[1].Evidence!.Status);
        }

        [Fact]
        public async Task Executed_exception_without_result_records_status_500()
        {
            var writer = new SequenceWriter(true, true);
            var filter = new SpaceAuditActionFilter(writer);
            var context = MakeActionContext(
                HttpMethods.Post,
                "LocationPublish",
                "PublishFloor");

            await RunFilter(
                filter,
                context,
                result: null,
                exception: new InvalidOperationException("secret payload"));

            Assert.Equal(SpaceAuditOutcome.Failed, writer.Inputs[1].Outcome);
            Assert.Equal("500", writer.Inputs[1].Evidence!.Status);
        }

        [Fact]
        public async Task Thrown_action_exception_appends_sanitized_failure_and_rethrows()
        {
            var writer = new SequenceWriter(true, true);
            var filter = new SpaceAuditActionFilter(writer);
            var context = MakeActionContext(
                HttpMethods.Post,
                "LocationPublish",
                "PublishFloor");
            var expected = new InvalidOperationException(
                "secret response body bearer-token");

            var actual = await Assert.ThrowsAsync<InvalidOperationException>(
                () => filter.OnActionExecutionAsync(
                    context,
                    () => Task.FromException<ActionExecutedContext>(expected)));

            Assert.Same(expected, actual);
            var failure = writer.Inputs[1];
            Assert.Equal(SpaceAuditOutcome.Failed, failure.Outcome);
            Assert.Equal("SPACE_ACTION_FAILED", failure.ReasonCode);
            Assert.Equal(
                nameof(InvalidOperationException),
                failure.Evidence!.ExceptionType);
            Assert.Matches("^[0-9A-F]{64}$", failure.Evidence.ErrorFingerprint);
            Assert.DoesNotContain(
                "secret",
                failure.Evidence.ToString(),
                StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task Executed_exception_appends_sanitized_failure()
        {
            var writer = new SequenceWriter(true, true);
            var filter = new SpaceAuditActionFilter(writer);
            var context = MakeActionContext(
                HttpMethods.Post,
                "LocationPublish",
                "PublishFloor");
            var exception = new InvalidOperationException("secret payload");

            await RunFilter(
                filter,
                context,
                new ObjectResult(null) { StatusCode = 500 },
                exception);

            var failure = writer.Inputs[1];
            Assert.Equal(SpaceAuditOutcome.Failed, failure.Outcome);
            Assert.Equal("SPACE_ACTION_FAILED", failure.ReasonCode);
            Assert.DoesNotContain(
                "secret",
                failure.Evidence!.ToString(),
                StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task Final_audit_ignores_request_abort_after_successful_side_effect()
        {
            using var abort = new CancellationTokenSource();
            var writer = new SequenceWriter(true, true);
            var filter = new SpaceAuditActionFilter(writer);
            var context = MakeActionContext(
                HttpMethods.Post,
                "LocationPublish",
                "PublishFloor",
                abort.Token);

            await filter.OnActionExecutionAsync(context, () =>
            {
                abort.Cancel();
                return Task.FromResult(
                    MakeExecuted(context, new OkObjectResult(new { ok = true })));
            });

            Assert.Equal(2, writer.Tokens.Count);
            Assert.Equal(abort.Token, writer.Tokens[0]);
            Assert.False(writer.Tokens[1].CanBeCanceled);
            Assert.Equal(SpaceAuditOutcome.Succeeded, writer.Inputs[1].Outcome);
        }

        [Fact]
        public async Task Request_abort_during_action_still_attempts_failed_audit_without_aborted_token()
        {
            using var abort = new CancellationTokenSource();
            var writer = new SequenceWriter(true, true);
            var filter = new SpaceAuditActionFilter(writer);
            var context = MakeActionContext(
                HttpMethods.Post,
                "LocationPublish",
                "PublishFloor",
                abort.Token);
            var expected = new OperationCanceledException(abort.Token);

            var actual = await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => filter.OnActionExecutionAsync(context, () =>
                {
                    abort.Cancel();
                    return Task.FromException<ActionExecutedContext>(expected);
                }));

            Assert.Same(expected, actual);
            Assert.False(writer.Tokens[1].CanBeCanceled);
            Assert.Equal(SpaceAuditOutcome.Failed, writer.Inputs[1].Outcome);
        }

        [Fact]
        public async Task Filter_never_reads_or_serializes_action_arguments()
        {
            var writer = new SequenceWriter(true, true);
            var filter = new SpaceAuditActionFilter(writer);
            var context = MakeActionContext(
                HttpMethods.Post,
                "LocationPublish",
                "PublishFloor");
            context.ActionArguments["request"] = new
            {
                Secret = "request-body-secret",
                Authorization = "bearer-token",
            };

            await RunFilter(filter, context, new OkResult());

            var auditText = string.Join(
                "|",
                writer.Inputs.Select(x => x.ToString()));
            Assert.DoesNotContain(
                "request-body-secret",
                auditText,
                StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(
                "bearer-token",
                auditText,
                StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task Portal_view_appends_clipped_authorization_evidence_fail_open()
        {
            var writer = new SequenceWriter(false);
            var filter = new SpaceAuditActionFilter(writer);
            var siteId = Guid.NewGuid();
            var authorizationVersion = new string('A', 64);
            var controller = new CP6.WebApi.Controllers.Space
                .SpaceExternalPortalController(null!);
            var method = typeof(CP6.WebApi.Controllers.Space
                    .SpaceExternalPortalController)
                .GetMethod(nameof(CP6.WebApi.Controllers.Space
                    .SpaceExternalPortalController.GetPortalPublishedScene))!;
            var context = MakeActionContext(
                HttpMethods.Get,
                "SpaceExternalPortal",
                "GetPortalPublishedScene",
                controller: controller,
                actionMethod: method);
            context.ActionArguments["siteId"] = siteId;
            var original = new ObjectResult(new SpacePortalPublishedSceneDto(
                siteId,
                Guid.NewGuid(),
                authorizationVersion,
                []));

            var executed = await RunFilter(filter, context, original);

            Assert.Same(original, executed.Result);
            var audit = Assert.Single(writer.Inputs);
            Assert.Equal("space.external.portal.view", audit.Action);
            Assert.Equal("PublishedScene", audit.ResourceType);
            Assert.Equal(siteId.ToString(), audit.ResourceId);
            Assert.Equal(siteId, audit.SiteId);
            Assert.Equal(SpaceAuditOutcome.Succeeded, audit.Outcome);
            Assert.Equal("Allowed", audit.Evidence!.AuthorizationResult);
            Assert.Equal(0, audit.Evidence.ItemCount);
            Assert.Equal(
                authorizationVersion,
                audit.Evidence.AuthorizationVersion);
            Assert.False(Assert.Single(writer.Tokens).CanBeCanceled);
        }

        [Fact]
        public async Task Portal_scope_denial_is_audited_without_leaking_exception_detail()
        {
            var writer = new SequenceWriter(true);
            var filter = new SpaceAuditActionFilter(writer);
            var controller = new CP6.WebApi.Controllers.Space
                .SpaceExternalPortalController(null!);
            var method = typeof(CP6.WebApi.Controllers.Space
                    .SpaceExternalPortalController)
                .GetMethod(nameof(CP6.WebApi.Controllers.Space
                    .SpaceExternalPortalController.GetPortalSites))!;
            var context = MakeActionContext(
                HttpMethods.Get,
                "SpaceExternalPortal",
                "GetPortalSites",
                controller: controller,
                actionMethod: method);
            var error = new SpaceProblemException(
                SpaceErrorCodes.ExternalMembershipInactive,
                StatusCodes.Status404NotFound,
                "Not found",
                "secret organization detail");

            var executed = await RunFilter(
                filter,
                context,
                result: null,
                exception: error);

            Assert.Same(error, executed.Exception);
            var audit = Assert.Single(writer.Inputs);
            Assert.Equal(SpaceAuditOutcome.Denied, audit.Outcome);
            Assert.Equal(
                SpaceErrorCodes.ExternalMembershipInactive,
                audit.ReasonCode);
            Assert.Equal("Denied", audit.Evidence!.AuthorizationResult);
            Assert.DoesNotContain(
                "secret organization detail",
                audit.ToString(),
                StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task Authorization_mutation_uses_stable_domain_action_and_safe_route_id()
        {
            var writer = new SequenceWriter(true, true);
            var filter = new SpaceAuditActionFilter(writer);
            var membershipId = Guid.NewGuid();
            var controller = new CP6.WebApi.Controllers.Space
                .SpaceExternalOrganizationController(null!, null!);
            var method = typeof(CP6.WebApi.Controllers.Space
                    .SpaceExternalOrganizationController)
                .GetMethod(nameof(CP6.WebApi.Controllers.Space
                    .SpaceExternalOrganizationController.UpdateMembership))!;
            var context = MakeActionContext(
                HttpMethods.Put,
                "SpaceExternalOrganization",
                "UpdateMembership",
                controller: controller,
                actionMethod: method);
            context.ActionArguments["membershipId"] = membershipId;
            context.ActionArguments["request"] = new
            {
                Secret = "must-not-be-audited",
            };

            await RunFilter(filter, context, new OkResult());

            Assert.Equal(2, writer.Inputs.Count);
            Assert.All(writer.Inputs, input =>
            {
                Assert.Equal("space.external.membership.update", input.Action);
                Assert.Equal("ExternalMembership", input.ResourceType);
                Assert.Equal(membershipId.ToString(), input.ResourceId);
                Assert.Equal(
                    "space:external:manage",
                    input.Evidence!.PermissionCode);
                Assert.DoesNotContain(
                    "must-not-be-audited",
                    input.ToString(),
                    StringComparison.OrdinalIgnoreCase);
            });
        }

        [Fact]
        public void External_access_endpoints_have_complete_stable_audit_metadata()
        {
            var portal = typeof(CP6.WebApi.Controllers.Space
                .SpaceExternalPortalController);
            var organizations = typeof(CP6.WebApi.Controllers.Space
                .SpaceExternalOrganizationController);
            var policies = typeof(CP6.WebApi.Controllers.Space
                .SpaceFieldPolicyController);
            var expected = new (Type Type, string Method, string Action)[]
            {
                (portal, "GetPortalOrganizations", "space.external.portal.session"),
                (portal, "GetPortalSites", "space.external.organization.select"),
                (portal, "GetPortalPublishedScene", "space.external.portal.view"),
                (portal, "GetPortalStock", "space.external.portal.view"),
                (portal, "GetPortalTasks", "space.external.portal.view"),
                (organizations, "CreateOrganization", "space.external.organization.create"),
                (organizations, "UpdateOrganization", "space.external.organization.update"),
                (organizations, "CreateMembership", "space.external.membership.create"),
                (organizations, "UpdateMembership", "space.external.membership.update"),
                (organizations, "CreateGrant", "space.external.grant.create"),
                (organizations, "UpdateGrant", "space.external.grant.update"),
                (policies, "CreateFieldPolicy", "space.external.field-policy.create"),
                (policies, "UpdateFieldPolicy", "space.external.field-policy.update"),
            };

            foreach (var item in expected)
            {
                var attribute = item.Type.GetMethod(item.Method)!
                    .GetCustomAttribute<SpaceAuditOperationAttribute>();
                Assert.NotNull(attribute);
                Assert.Equal(item.Action, attribute.Action);
            }
        }

        [Theory]
        [InlineData("GET")]
        [InlineData("HEAD")]
        public async Task Safe_space_methods_bypass_audit(string method)
        {
            var writer = new SequenceWriter();
            var filter = new SpaceAuditActionFilter(writer);
            var context = MakeActionContext(method, "SpaceMaster", "Get");
            var called = false;

            await filter.OnActionExecutionAsync(context, () =>
            {
                called = true;
                return Task.FromResult(MakeExecuted(context, new OkResult()));
            });

            Assert.True(called);
            Assert.Empty(writer.Inputs);
        }

        [Fact]
        public async Task Mutation_outside_exact_space_controller_namespace_bypasses_audit()
        {
            var writer = new SequenceWriter();
            var filter = new SpaceAuditActionFilter(writer);
            var context = MakeActionContext(
                HttpMethods.Post,
                "Other",
                "Write",
                controller: new NonSpaceProbeController());
            var called = false;

            await filter.OnActionExecutionAsync(context, () =>
            {
                called = true;
                return Task.FromResult(MakeExecuted(context, new OkResult()));
            });

            Assert.True(called);
            Assert.Empty(writer.Inputs);
        }

        private static ActionExecutingContext MakeActionContext(
            string method,
            string controllerName,
            string action,
            CancellationToken requestAborted = default,
            object? controller = null,
            MethodInfo? actionMethod = null)
        {
            var http = new DefaultHttpContext();
            http.Request.Method = method;
            http.Request.Path = $"/api/space/{action}";
            http.RequestAborted = requestAborted;
            var descriptor = new ControllerActionDescriptor
            {
                MethodInfo = actionMethod!,
                RouteValues = new Dictionary<string, string?>
                {
                    ["controller"] = controllerName,
                    ["action"] = action,
                },
            };
            var actionContext = new ActionContext(
                http,
                new RouteData(),
                descriptor);
            return new ActionExecutingContext(
                actionContext,
                new List<IFilterMetadata>(),
                new Dictionary<string, object?>(),
                controller ??
                    new CP6.WebApi.Controllers.Space.AuditFilterProbeController());
        }

        private static ActionExecutedContext MakeExecuted(
            ActionExecutingContext context,
            IActionResult? result,
            Exception? exception = null) =>
            new(
                context,
                new List<IFilterMetadata>(),
                context.Controller)
            {
                Result = result,
                Exception = exception,
            };

        private static async Task<ActionExecutedContext> RunFilter(
            SpaceAuditActionFilter filter,
            ActionExecutingContext context,
            IActionResult? result,
            Exception? exception = null)
        {
            ActionExecutedContext? captured = null;
            await filter.OnActionExecutionAsync(context, () =>
            {
                captured = MakeExecuted(context, result, exception);
                return Task.FromResult(captured);
            });
            return captured!;
        }

        private static string Message(ObjectResult result) =>
            result.Value!
                .GetType()
                .GetProperty("message")!
                .GetValue(result.Value)!
                .ToString()!;

        private sealed class SequenceWriter : ISpaceAuditWriter
        {
            private readonly Queue<bool> _results;

            public SequenceWriter(params bool[] results) =>
                _results = new Queue<bool>(results);

            public List<SpaceAuditEventInput> Inputs { get; } = [];
            public List<CancellationToken> Tokens { get; } = [];

            public Task<bool> TryAppendAsync(
                SpaceAuditEventInput input,
                CancellationToken ct = default)
            {
                Inputs.Add(input);
                Tokens.Add(ct);
                return Task.FromResult(
                    _results.Count == 0 || _results.Dequeue());
            }
        }

        private sealed class CustomStatusResult : IActionResult, IStatusCodeActionResult
        {
            public CustomStatusResult(int statusCode) => StatusCode = statusCode;

            public int? StatusCode { get; }

            public Task ExecuteResultAsync(ActionContext context) =>
                Task.CompletedTask;
        }

        private sealed class NonSpaceProbeController : ControllerBase;
    }
}

namespace CP6.WebApi.Controllers.Space
{
    internal sealed class AuditFilterProbeController : ControllerBase;
}
