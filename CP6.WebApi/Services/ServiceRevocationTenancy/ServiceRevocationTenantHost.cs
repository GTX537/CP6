using CP6.Core.Persistence.Tenancy;
using CP6.Core.Services.CrmIdentity;
using CP6.Platform.Abstractions.Context;
using CP6.Platform.Contracts.Context;
using CP6.WebApi.Configuration.ServiceRevocationTenancy;
using Microsoft.Extensions.Primitives;
using Npgsql;

namespace CP6.WebApi.Services.ServiceRevocationTenancy;

internal enum ServiceRevocationCheckpoint { BeforeWrite, AfterWriteBeforeCommit, AfterCommit }

/// <summary>
/// Dedicated singleton context/pool lifetime. Configuration changes require stopping ingress, draining
/// every instance, installing a new version and restarting. No live reload or instantaneous revocation claim.
/// </summary>
public sealed class ServiceRevocationTenantHost : IAsyncDisposable
{
    internal const string Purpose = "service-token:self-revoke";
    internal const string Profile = "cp6-service-basic-revocation-v1";
    private readonly Frozen _configuration;
    private readonly CrmIdentityRuntime _runtime;
    private readonly TimeProvider _clock;
    private readonly ContextScopeAccessorV1 _accessor = new();
    private readonly TrustedContextExecutorV1 _executor;
    private readonly TenantConnectionManager _connections;
    private readonly SourceVerifier _verifier;
    private readonly BindingAuthority _authority;
    private readonly string _sourceEpoch = Guid.NewGuid().ToString("N");
    // Internal deterministic fault seam; never populated from requests, configuration or production DI.
    internal Func<ServiceRevocationCheckpoint, Task>? Checkpoint { get; set; }

    internal ServiceRevocationTenantHost(Frozen configuration, CrmIdentityRuntime runtime, TimeProvider clock)
    {
        _configuration = configuration; _clock = clock;
        // Writer reads its own frozen options; mutable legacy runtime remains untouched.
        _runtime = new(new CrmIdentityOptions { Enabled = true, Issuer = configuration.Issuer,
            Tenants = configuration.Clients.Values.Select(x => (x.Tenant, x.Region)).Distinct().ToDictionary(x => x.Tenant, x => x.Region),
            ProjectionReaderClientIds = ["bounded-service-revocation"], PasswordMaxAgeDays = 0 }, runtime.Validator, clock);
        _authority = new(this); _verifier = new(this);
        _executor = new(_authority, _accessor, clock);
        _connections = new(configuration.Host, _accessor, configuration.Catalog,
            new Credentials(configuration.Passwords), new(32, 64, 2));
    }

    internal async Task RevokeAsync(StringValues authorization, CrmOidcServiceClient authenticated, string jti, CancellationToken ct)
    {
        // Copy raw transport values once; callers cannot mutate an array after authentication.
        var input = new SourceInput(authorization.ToArray(), authenticated.ClientId, authenticated.TenantId);
        await _executor.ExecuteAsync(ContextEntryKindV1.Http, input, _verifier, async token =>
        {
            var trusted = _accessor.CurrentTrusted!;
            await using var lease = await _connections.OpenCurrentAsync(token).ConfigureAwait(false);
            await lease.InTransactionAsync(async (transaction, transactionToken) =>
            {
                if (Checkpoint is not null) await Checkpoint(ServiceRevocationCheckpoint.BeforeWrite).ConfigureAwait(false);
                async Task Fence(CancellationToken finalToken)
                {
                    var current = await _verifier.VerifyAsync(input, finalToken).ConfigureAwait(false);
                    if (!ReferenceEquals(_accessor.CurrentTrusted, trusted) || current is null
                        || _clock.GetUtcNow() >= trusted.Evidence.AuthenticatedAtUtc.AddSeconds(_configuration.Lifetime)
                        || current.Source.Actor != trusted.Evidence.Actor
                        || current.Source.SourceIdentityVersion != trusted.Evidence.SourceIdentityVersion
                        || await _authority.ResolveCurrentAsync(current.Operation, current.Source, finalToken).ConfigureAwait(false) is not { IsActive: true } registration
                        || registration.RegistrationVersion != trusted.Evidence.RegistrationVersion)
                        throw new TenantStorageException(TenantStorageFailure.BindingChanged);
                }
                await ServiceRevocationTransaction.RevokeAsync(transaction, _runtime, _configuration.Issuer,
                    input.Client, input.Tenant, jti,
                    new(_configuration.Installation, _configuration.DatabaseOid, _configuration.SystemIdentifier, _configuration.Server, _configuration.Port),
                    Fence, transactionToken).ConfigureAwait(false);
                if (Checkpoint is not null) await Checkpoint(ServiceRevocationCheckpoint.AfterWriteBeforeCommit).ConfigureAwait(false);
                await transaction.CommitAsync(transactionToken).ConfigureAwait(false);
                if (Checkpoint is not null) await Checkpoint(ServiceRevocationCheckpoint.AfterCommit).ConfigureAwait(false);
                return 0;
            }, cancellationToken: token).ConfigureAwait(false);
        }, ct).ConfigureAwait(false);
    }

    internal sealed record SourceInput(string?[] Authorization, string Client, Guid Tenant);
    internal sealed class SourceVerifier(ServiceRevocationTenantHost host) : IContextSourceVerifierV1<SourceInput>
    {
        public ValueTask<ContextResolutionInputV1?> VerifyAsync(SourceInput input, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!CrmOidcServiceTokens.TryDecodeBasic(new StringValues(input.Authorization), out var id, out var secret)
                || !host._configuration.Clients.TryGetValue(id, out var client)
                || client.Tenant != input.Tenant || id != input.Client || !CrmOidcCrypto.EqualsSecret(secret, client.SecretHash))
                return ValueTask.FromResult<ContextResolutionInputV1?>(null);
            var now = host._clock.GetUtcNow();
            var actor = new ContextActorV1(client.Tenant, host._configuration.Issuer, "service:" + id, ContextActorKindV1.Service, null);
            return ValueTask.FromResult<ContextResolutionInputV1?>(new(
                new(ContextEntryKindV1.Http, host._configuration.Host, Profile, "CP6.Services", Purpose, client.Tenant,
                    "/connect/service-revocations", Guid.NewGuid().ToString("D"), [Purpose]),
                new(actor, actor, "CP6.Services", client.Region, now, now.AddSeconds(host._configuration.Lifetime),
                    "registered-basic:" + id, null, [Purpose], host._sourceEpoch + ":" + client.Version)));
        }
    }
    internal sealed class BindingAuthority(ServiceRevocationTenantHost host) : IContextBindingAuthorityV1
    {
        public ValueTask<ContextBindingRegistrationV1?> ResolveCurrentAsync(ContextOperationV1 operation,
            AuthenticatedContextSourceV1 source, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var id = source.Actor.Subject.StartsWith("service:", StringComparison.Ordinal) ? source.Actor.Subject[8..] : "";
            var configuration = host._configuration;
            if (!configuration.Clients.TryGetValue(id, out var client) || !client.Allow
                || operation.HostId != configuration.Host || operation.ProfileId != Profile || operation.Purpose != Purpose
                || operation.EntryKind != ContextEntryKindV1.Http || operation.SourceReference != "/connect/service-revocations"
                || operation.ExpectedTenantId != client.Tenant || operation.Audience != "CP6.Services"
                || !operation.RequiredScopes.SequenceEqual([Purpose]) || !source.Scopes.SequenceEqual([Purpose])
                || source.Actor != new ContextActorV1(client.Tenant, configuration.Issuer, "service:" + id, ContextActorKindV1.Service, null)
                || source.EffectiveActor != source.Actor || source.Region != client.Region || source.Audience != "CP6.Services"
                || source.SourceIdentityVersion != host._sourceEpoch + ":" + client.Version || source.AuthenticationReference != "registered-basic:" + id
                || source.TokenId is not null || source.OriginalRequesterReference is not null
                || source.AuthenticatedAtUtc < configuration.From || source.AuthenticatedAtUtc > host._clock.GetUtcNow()
                || source.ExpiresAtUtc <= source.AuthenticatedAtUtc || source.ExpiresAtUtc <= host._clock.GetUtcNow()
                || source.ExpiresAtUtc > source.AuthenticatedAtUtc.AddSeconds(configuration.Lifetime)
                || host._clock.GetUtcNow() < configuration.From || host._clock.GetUtcNow() >= configuration.Until)
                return ValueTask.FromResult<ContextBindingRegistrationV1?>(null);
            configuration.Catalog.Resolve(client.Tenant);
            return ValueTask.FromResult<ContextBindingRegistrationV1?>(new(client.Tenant, client.Tenant,
                configuration.Host, Profile, configuration.Issuer, client.Region, "CP6.Services", Purpose,
                ContextEntryKindV1.Http, source.Actor, source.Actor, "service-revocation:" + id,
                configuration.Revision + ":" + client.Version, configuration.From, configuration.Until, true, [Purpose]));
        }
    }
    internal sealed record Client(Guid Tenant, string Region, string SecretHash, string Version, bool Allow);
    internal sealed record Frozen(string Host, string Revision, string Issuer, DateTimeOffset From, DateTimeOffset Until,
        int Lifetime, string Server, int Port, uint DatabaseOid, string SystemIdentifier, Guid Installation,
        IReadOnlyDictionary<string, Client> Clients, IReadOnlyDictionary<Guid, string> Passwords, TenantStorageCatalog Catalog);
    internal static Frozen Freeze(ServiceRevocationOptions options, CrmOidcOptions oidc, CrmIdentityOptions identity)
    {
        var bindings = options.Tenants.Select(t => new TenantStorageBindingV1(t.TenantId, options.HostId, "issuance-db",
            TenantStorageBindingV1.SupportedLayout, ServiceRevocationTransaction.Profile, options.Revision, t.CredentialVersion,
            t.Login, ["crm_identity_priority"], TenantStorageState.Ready, ServiceRevocationTransaction.MigrationVersion));
        var catalog = new TenantStorageCatalog(options.HostId, [ServiceRevocationTransaction.Profile], bindings,
            [new("issuance-db", options.Server, options.Port, options.Database, Enum.Parse<SslMode>(options.SslMode))],
            options.Tenants.Select(t => new TenantPrincipal(t.Login, t.Login)));
        return new(options.HostId, options.Revision, oidc.Issuer, options.ValidFromUtc, options.ValidUntilUtc,
            options.SourceLifetimeSeconds, options.Server, options.Port, options.DatabaseOid, options.SystemIdentifier,
            options.InstallationId, options.Clients.ToDictionary(c => c.ClientId, c =>
            {
                var source = oidc.ServiceClients.Single(s => s.ClientId == c.ClientId);
                return new Client(source.TenantId, identity.Tenants[source.TenantId], source.SecretSha256, c.RegistrationVersion, c.AllowSelfRevoke);
            }, StringComparer.Ordinal), options.Tenants.ToDictionary(t => t.TenantId, t => t.Password), catalog);
    }
    private sealed class Credentials(IReadOnlyDictionary<Guid, string> passwords) : ITenantCredentialResolver
    {
        public ValueTask<string> ResolvePasswordAsync(TenantStorageBindingV1 binding, CancellationToken ct)
        { ct.ThrowIfCancellationRequested(); return ValueTask.FromResult(passwords[binding.TenantId]); }
    }
    internal (int Pools, int ReservedConnections, int Borrowers, int DrainingPools) PoolSnapshot => _connections.Snapshot();
    public ValueTask DisposeAsync() => _connections.DisposeAsync();
}
