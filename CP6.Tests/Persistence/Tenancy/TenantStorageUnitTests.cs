using CP6.Core.Persistence.Tenancy;
using CP6.Platform.Abstractions.Context;
using CP6.Platform.Contracts;
using CP6.Platform.Contracts.Context;
using Npgsql;

namespace CP6.Tests.Persistence.Tenancy;

public sealed class TenantStorageUnitTests
{
    [Fact]
    public async Task Missing_or_legacy_context_never_resolves_credentials()
    {
        var context = new TenantStorageContext();
        var credentials = new TenantStorageCredentials();
        await using var manager = new TenantConnectionManager("main", context.Accessor,
            TenantStorageContext.Catalog(), credentials, new(2, 2, 1));
        await Rejected(TenantStorageFailure.TrustedContextRequired, () => manager.OpenCurrentAsync());
        await context.Accessor.ExecuteLegacyAsync(_ => ValueTask.FromResult<RequestContextSnapshot?>(new(
            TenantStorageContext.A, null, "legacy", "synthetic-audience", "legacy-correlation", null, false)), async _ =>
            await Rejected(TenantStorageFailure.TrustedContextRequired, () => manager.OpenCurrentAsync()));
        Assert.Equal(0, credentials.Calls);
    }

    [Fact]
    public void Binding_derives_exact_schema_and_freezes_caller_collections()
    {
        var queues = new List<string> { "queue-one" };
        var binding = TenantStorageContext.Binding(queues: queues);
        var bindings = new List<TenantStorageBindingV1> { binding };
        var catalog = new TenantStorageCatalog("main", ["fixture-v1"], bindings,
            [new("db", "localhost", 5432, "fixture")], [new("principal-a", "tenant_a")]);
        queues[0] = "mutated";
        bindings.Clear();
        Assert.Equal("t_11111111111141118111111111111111", binding.SchemaName);
        Assert.Equal(["queue-one"], binding.QueueDomainRefs);
        Assert.Same(binding, catalog.Resolve(TenantStorageContext.A));
        Assert.Throws<NotSupportedException>(() => ((IList<string>)binding.QueueDomainRefs).Add("injected"));
        Assert.Throws<TenantStorageException>(() => TenantStorageContext.Binding(Guid.Empty));
    }

    [Theory]
    [InlineData("duplicate-tenant")]
    [InlineData("principal-alias")]
    [InlineData("database-alias")]
    [InlineData("wrong-host")]
    public void Catalog_rejects_ambiguous_or_cross_tenant_assignments(string conflict)
    {
        var a = TenantStorageContext.Binding();
        var b = TenantStorageContext.Binding(TenantStorageContext.B, host: conflict == "wrong-host" ? "other" : "main");
        var bindings = conflict == "duplicate-tenant" ? new[] { a, a } : new[] { a, b };
        var databases = new List<TenantDatabase> { new("db", "localhost", 5432, "fixture") };
        if (conflict == "database-alias") databases.Add(new("alias", "localhost", 5432, "fixture"));
        Assert.Equal(TenantStorageFailure.ConfigurationRejected, Assert.Throws<TenantStorageException>(() =>
            new TenantStorageCatalog("main", ["fixture-v1"], bindings, databases,
                [new("principal-a", "tenant_a"), new("principal-b", conflict == "principal-alias" ? "tenant_a" : "tenant_b")])).Failure);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public async Task Non_ready_storage_is_rejected_before_credentials(int state)
    {
        var context = new TenantStorageContext();
        var credentials = new TenantStorageCredentials();
        await using var manager = new TenantConnectionManager("main", context.Accessor,
            TenantStorageContext.Catalog(TenantStorageContext.Binding(state: (TenantStorageState)state)), credentials, new(1, 1, 1));
        await context.Run(TenantStorageContext.A, _ => Rejected(TenantStorageFailure.BindingRejected, () => manager.OpenCurrentAsync()));
        Assert.Equal(0, credentials.Calls);
    }

    [Theory]
    [InlineData("host")]
    [InlineData("model")]
    [InlineData("layout")]
    [InlineData("tenant")]
    public async Task Wrong_host_unknown_version_or_unregistered_tenant_never_open(string mismatch)
    {
        var context = new TenantStorageContext { Host = mismatch == "host" ? "other-host" : "main" };
        var credentials = new TenantStorageCredentials();
        var binding = TenantStorageContext.Binding(model: mismatch == "model" ? "unknown-model" : "fixture-v1",
            layout: mismatch == "layout" ? "unknown-layout" : TenantStorageBindingV1.SupportedLayout);
        await using var manager = new TenantConnectionManager("main", context.Accessor,
            TenantStorageContext.Catalog(binding), credentials, new(1, 1, 1));
        await context.Run(mismatch == "tenant" ? TenantStorageContext.B : TenantStorageContext.A,
            _ => Rejected(TenantStorageFailure.BindingRejected, () => manager.OpenCurrentAsync()));
        Assert.Equal(0, credentials.Calls);
    }

    [Fact]
    public async Task Captured_context_ends_while_credential_lookup_waits_and_cannot_reactivate()
    {
        var context = new TenantStorageContext();
        var resolve = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var credentials = new TenantStorageCredentials { Resolve = async (_, ct) => await resolve.Task.WaitAsync(ct) };
        await using var manager = new TenantConnectionManager("main", context.Accessor, TenantStorageContext.Catalog(), credentials, new(1, 1, 1));
        Task<TenantConnectionLease>? pending = null;
        TrustedRequestContextV1? cached = null;
        await context.Run(TenantStorageContext.A, _ =>
        {
            cached = context.Accessor.CurrentTrusted;
            pending = manager.OpenCurrentAsync();
            return Task.CompletedTask;
        });
        Assert.NotNull(cached);
        Assert.Null(context.Accessor.CurrentTrusted);
        resolve.SetResult("synthetic-password");
        await Rejected(TenantStorageFailure.ContextChanged, () => pending!);
        Assert.Equal((0, 0, 0, 0), manager.Snapshot());
    }

    [Fact]
    public async Task Catalog_rotation_during_credentials_lookup_invalidates_pending_acquisition()
    {
        var context = new TenantStorageContext();
        var resolve = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var credentials = new TenantStorageCredentials { Resolve = async (_, ct) => await resolve.Task.WaitAsync(ct) };
        await using var manager = new TenantConnectionManager("main", context.Accessor, TenantStorageContext.Catalog(), credentials, new(1, 1, 1));
        await context.Run(TenantStorageContext.A, async _ =>
        {
            var pending = manager.OpenCurrentAsync();
            manager.ReplaceCatalog(TenantStorageContext.Catalog(TenantStorageContext.Binding(credential: "v2", revision: "r2")));
            Assert.Equal((1, 1, 1, 1), manager.Snapshot());
            resolve.SetResult("synthetic-password");
            await Rejected(TenantStorageFailure.BindingChanged, () => pending);
            Assert.Equal((0, 0, 0, 0), manager.Snapshot());
        });
    }

    [Fact]
    public async Task Resolver_exception_and_cancellation_release_capacity_without_secret_details()
    {
        var context = new TenantStorageContext();
        var credentials = new TenantStorageCredentials { Resolve = (_, _) => throw new InvalidOperationException("private-credential-secret") };
        await using var manager = new TenantConnectionManager("main", context.Accessor, TenantStorageContext.Catalog(), credentials, new(1, 1, 1));
        await context.Run(TenantStorageContext.A, async _ =>
        {
            var error = await Assert.ThrowsAsync<TenantStorageException>(() => manager.OpenCurrentAsync());
            Assert.Equal(TenantStorageFailure.CredentialsUnavailable, error.Failure);
            Assert.Null(error.InnerException);
            Assert.DoesNotContain("private-credential-secret", error.ToString());
            Assert.Equal((0, 0, 0, 0), manager.Snapshot());
            var waiting = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            credentials.Resolve = async (_, ct) =>
            {
                try { return await waiting.Task.WaitAsync(ct); }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw new OperationCanceledException("private-cancel-secret",
                        new InvalidOperationException("private-inner-secret"), ct);
                }
            };
            using var cancellation = new CancellationTokenSource();
            var pending = manager.OpenCurrentAsync(cancellation.Token);
            cancellation.Cancel();
            var canceled = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
            Assert.Equal(cancellation.Token, canceled.CancellationToken);
            Assert.Null(canceled.InnerException);
            Assert.DoesNotContain("private-cancel-secret", canceled.ToString());
            Assert.DoesNotContain("private-inner-secret", canceled.ToString());
            Assert.Equal((0, 0, 0, 0), manager.Snapshot());
        });
    }

    [Fact]
    public async Task Concurrent_initialization_reserves_full_pool_capacity_before_opening()
    {
        var context = new TenantStorageContext();
        var waiting = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var credentials = new TenantStorageCredentials { Resolve = async (_, ct) => await waiting.Task.WaitAsync(ct) };
        await using var manager = new TenantConnectionManager("main", context.Accessor, TenantStorageContext.Catalog(), credentials, new(5, 3, 2));
        await context.Run(TenantStorageContext.A, async _ =>
        {
            using var cancellation = new CancellationTokenSource();
            var pending = manager.OpenCurrentAsync(cancellation.Token);
            Assert.Equal((1, 2, 1, 0), manager.Snapshot());
            await context.Run(TenantStorageContext.B, _ => Rejected(TenantStorageFailure.CapacityExceeded, () => manager.OpenCurrentAsync()));
            Assert.Equal(1, credentials.Calls);
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
            Assert.Equal((0, 0, 0, 0), manager.Snapshot());
        });
    }

    internal static async Task Rejected(TenantStorageFailure failure, Func<Task<TenantConnectionLease>> action)
        => Assert.Equal(failure, (await Assert.ThrowsAsync<TenantStorageException>(action)).Failure);
}

internal sealed class TenantStorageContext : IContextSourceVerifierV1<Guid>, IContextBindingAuthorityV1
{
    internal static readonly Guid A = Guid.Parse("11111111-1111-4111-8111-111111111111");
    internal static readonly Guid B = Guid.Parse("22222222-2222-4222-8222-222222222222");
    public ContextScopeAccessorV1 Accessor { get; } = new();
    public string Host { get; set; } = "main";
    public Task Run(Guid tenant, Func<CancellationToken, Task> action) =>
        new TrustedContextExecutorV1(this, Accessor).ExecuteAsync(ContextEntryKindV1.Background, tenant, this, action);

    public ValueTask<ContextResolutionInputV1?> VerifyAsync(Guid tenant, CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        var actor = new ContextActorV1(tenant, "https://synthetic.invalid", "synthetic-service", ContextActorKindV1.Service, null);
        return ValueTask.FromResult<ContextResolutionInputV1?>(new(
            new(ContextEntryKindV1.Background, Host, "synthetic-profile", "synthetic-audience", "storage-test", tenant,
                "synthetic-operation", "synthetic-correlation", ["read"]),
            new(actor, actor, "synthetic-audience", "local", now.AddMinutes(-1), now.AddMinutes(5), "synthetic-authentication", null, ["read"])));
    }

    public ValueTask<ContextBindingRegistrationV1?> ResolveCurrentAsync(ContextOperationV1 operation,
        AuthenticatedContextSourceV1 source, CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        return ValueTask.FromResult<ContextBindingRegistrationV1?>(new(source.Actor.TenantId, source.Actor.TenantId,
            operation.HostId, operation.ProfileId, source.Actor.Issuer, source.Region, operation.Audience, operation.Purpose,
            operation.EntryKind, source.Actor, source.EffectiveActor, "synthetic-registration", "v1", now.AddMinutes(-1),
            now.AddMinutes(5), true, ["read"]));
    }

    internal static TenantStorageBindingV1 Binding(Guid? tenant = null, string credential = "v1",
        TenantStorageState state = TenantStorageState.Ready, string model = "fixture-v1", string host = "main",
        string revision = "r1", string? principal = null, IEnumerable<string>? queues = null,
        string layout = TenantStorageBindingV1.SupportedLayout) =>
        new(tenant ?? A, host, "db", layout, model, revision, credential,
            principal ?? ((tenant ?? A) == A ? "principal-a" : "principal-b"), queues ?? ["synthetic-queue"], state, "fixture-m1");

    internal static TenantStorageCatalog Catalog(params TenantStorageBindingV1[] bindings) => new("main", ["fixture-v1"],
        bindings.Length == 0 ? [Binding(), Binding(B)] : bindings,
        [new("db", "127.0.0.1", 1, "synthetic_database", SslMode.Disable)],
        [new("principal-a", "tenant_a"), new("principal-b", "tenant_b")]);
}

internal sealed class TenantStorageCredentials : ITenantCredentialResolver
{
    private int _calls;
    internal int Calls => Volatile.Read(ref _calls);
    internal Func<TenantStorageBindingV1, CancellationToken, ValueTask<string>> Resolve { get; set; } =
        (_, _) => ValueTask.FromResult("synthetic-password");
    public ValueTask<string> ResolvePasswordAsync(TenantStorageBindingV1 binding, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _calls);
        return Resolve(binding, cancellationToken);
    }
}
