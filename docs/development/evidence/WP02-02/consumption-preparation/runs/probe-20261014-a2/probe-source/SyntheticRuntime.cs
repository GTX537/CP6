using CP6.Platform.Abstractions;
using CP6.Platform.Abstractions.Context;
using CP6.Platform.AspNetCore.Context;
using CP6.Platform.Contracts;
using CP6.Platform.Contracts.Context;
using CP6.Platform.Messaging.Context;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

// Fixtures only. Neither adapter authenticates a real principal or consults a real authority.
static class SyntheticRuntime
{
    private static readonly Guid Tenant = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-10-14T00:00:00Z");
    public static ContextResolutionInputV1 Input(ContextEntryKindV1 kind = ContextEntryKindV1.Http)
    {
        var actor = new ContextActorV1(Tenant, "https://synthetic.invalid", "fixture-service", ContextActorKindV1.Service, null);
        return new(new(kind, "fixture-host", "fixture-profile", "fixture-audience", "fixture-read", Tenant,
            "fixture-source", "fixture-correlation", new[] { "read" }),
            new(actor, actor, "fixture-audience", "fixture-region", Now.AddMinutes(-1), Now.AddMinutes(1),
                "fixture-authentication", null, new[] { "read" }));
    }
    public static ContextBindingRegistrationV1 Registration(ContextOperationV1 o, AuthenticatedContextSourceV1 s) =>
        new(o.ExpectedTenantId, o.ExpectedTenantId, o.HostId, o.ProfileId, s.Actor.Issuer, s.Region,
            o.Audience, o.Purpose, o.EntryKind, s.Actor, s.EffectiveActor, "fixture-registration", "1",
            Now.AddMinutes(-1), Now.AddMinutes(1), true, new[] { "read" });
    public static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
    private static void Empty(ContextScopeAccessorV1 a) => Require(a.Current is null && a.CurrentTrusted is null, "scope leaked");
    public static async Task<object[]> Run()
    {
        var results = new List<object>();
        async Task Case(string name, Func<Task> run)
        {
            await run();
            results.Add(new { name, outcome = "PASS", kind = "SYNTHETIC_FIXTURE" });
        }
        await Case("trusted-type-has-no-public-constructor-or-setters", () =>
        {
            Require(typeof(TrustedRequestContextV1).GetConstructors().Length == 0, "trusted constructor is public");
            Require(typeof(TrustedRequestContextV1).GetProperties().All(p => p.SetMethod is null), "trusted setter exposed");
            return Task.CompletedTask;
        });
        await Case("DI-middleware-success-verifier-and-authority-masked-finally-clears-captured-context", async () =>
        {
            var services = new ServiceCollection();
            services.AddSingleton<TimeProvider>(new FixedTime());
            services.AddCp6TrustedRequestContextV1<SyntheticHttpVerifier, SyntheticAuthority>();
            using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
            using var scope = provider.CreateScope();
            var accessor = scope.ServiceProvider.GetRequiredService<ContextScopeAccessorV1>();
            Empty(accessor);
            ExecutionContext? captured = null;
            var entered = 0;
            var app = new ApplicationBuilder(scope.ServiceProvider);
            app.UseCp6TrustedRequestContextV1();
            app.Run(_ =>
            {
                entered++;
                var trusted = scope.ServiceProvider.GetRequiredService<ITrustedContextAccessorV1>().CurrentTrusted;
                Require(trusted is not null && trusted.Context.TenantId == Tenant, "missing trusted tenant");
                Require(trusted!.Evidence.CapabilityVersion == "ctx-binding-v1", "wrong capability");
                Require(scope.ServiceProvider.GetRequiredService<IRequestContextAccessor>().Current?.TenantId == Tenant,
                    "legacy request accessor not bridged");
                captured = ExecutionContext.Capture();
                return Task.CompletedTask;
            });
            await app.Build()(new DefaultHttpContext { RequestServices = scope.ServiceProvider });
            Require(entered == 1 && captured is not null, "handler count");
            Empty(accessor);
            ExecutionContext.Run(captured!, _ => Empty(accessor), null);
        });
        await Case("legacy-snapshot-does-not-mint-trusted-context", async () =>
        {
            var a = new ContextScopeAccessorV1();
            await a.ExecuteLegacyAsync(_ => ValueTask.FromResult<RequestContextSnapshot?>(
                new(Tenant, null, "fixture-service", "fixture-audience", "fixture-correlation", null, false)), _ =>
            {
                Require(a.Current is not null && a.CurrentTrusted is null, "legacy minted trust");
                return Task.CompletedTask;
            });
            Empty(a);
        });
        foreach (var scenario in new[] { "tenant-mismatch", "missing-source", "authority-unavailable", "cancel-before", "cancel-after-authority", "handler-throws" })
            await Case(scenario + "-denies-or-clears", async () =>
            {
                var a = new ContextScopeAccessorV1();
                using var cancellation = new CancellationTokenSource();
                var authority = new SyntheticAuthority(a) { Scenario = scenario, Cancellation = cancellation };
                var executor = new TrustedContextExecutorV1(authority, a, new FixedTime());
                var verifier = new SyntheticHttpVerifier(a) { Missing = scenario == "missing-source" };
                if (scenario == "cancel-before") cancellation.Cancel();
                var entered = false;
                Exception? caught = null;
                try
                {
                    await executor.ExecuteAsync(ContextEntryKindV1.Http, new DefaultHttpContext(), verifier, _ =>
                    {
                        entered = true;
                        Require(a.CurrentTrusted is not null, "untrusted handler invocation");
                        throw new FixtureHandlerException();
                    }, cancellation.Token);
                }
                catch (Exception e) { caught = e; }
                Require(caught is not null, "expected failure missing");
                if (scenario.StartsWith("cancel", StringComparison.Ordinal)) Require(caught is OperationCanceledException, "wrong cancellation");
                else if (scenario == "handler-throws") Require(caught is FixtureHandlerException, "handler error swallowed");
                else
                {
                    var expected = scenario == "missing-source" ? ContextResolutionFailureV1.AuthenticationRequired
                        : scenario == "authority-unavailable" ? ContextResolutionFailureV1.AuthorityUnavailable : ContextResolutionFailureV1.BindingRejected;
                    Require(caught is ContextResolutionExceptionV1 error && error.Failure == expected, "wrong rejection");
                }
                Require(entered == (scenario == "handler-throws"), "rejected source reached handler");
                Empty(a);
            });
        await Case("message-generic-executor-compiles-runs-clears", async () =>
        {
            var a = new ContextScopeAccessorV1();
            var executor = new MessageContextExecutorV1<string>(new(new SyntheticAuthority(a), a, new FixedTime()), new SyntheticMessageVerifier());
            await executor.ExecuteAsync("fixture-delivery", _ =>
            {
                Require(a.CurrentTrusted?.Evidence.EntryKind == ContextEntryKindV1.Message, "message binding missing");
                return Task.CompletedTask;
            });
            Empty(a);
        });
        return results.ToArray();
    }
    private sealed class FixedTime : TimeProvider { public override DateTimeOffset GetUtcNow() => Now; }
    private sealed class FixtureHandlerException : Exception;
    public sealed class SyntheticHttpVerifier(ContextScopeAccessorV1 accessor) : IContextSourceVerifierV1<HttpContext>
    {
        public bool Missing { get; init; }
        public async ValueTask<ContextResolutionInputV1?> VerifyAsync(HttpContext input, CancellationToken cancellationToken = default)
        {
            Empty(accessor);
            await Task.Yield();
            Empty(accessor);
            return Missing ? null : Input();
        }
    }
    public sealed class SyntheticAuthority(ContextScopeAccessorV1 accessor) : IContextBindingAuthorityV1
    {
        public string Scenario { get; init; } = "success";
        public CancellationTokenSource? Cancellation { get; init; }
        public async ValueTask<ContextBindingRegistrationV1?> ResolveCurrentAsync(ContextOperationV1 operation,
            AuthenticatedContextSourceV1 source, CancellationToken cancellationToken = default)
        {
            Empty(accessor);
            await Task.Yield();
            Empty(accessor);
            if (Scenario == "authority-unavailable") throw new ContextResolutionExceptionV1(ContextResolutionFailureV1.AuthorityUnavailable);
            if (Scenario == "cancel-after-authority") Cancellation!.Cancel();
            var registration = Registration(operation, source);
            return Scenario == "tenant-mismatch" ? registration with { TenantId = Guid.Empty } : registration;
        }
    }
    private sealed class SyntheticMessageVerifier : IContextSourceVerifierV1<string>
    {
        public ValueTask<ContextResolutionInputV1?> VerifyAsync(string input, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<ContextResolutionInputV1?>(Input(ContextEntryKindV1.Message));
    }
}
