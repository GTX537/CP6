using System.Globalization;
using System.Security.Claims;
using CP6.Core.Persistence.Tenancy;
using CP6.Core.Services.CrmIdentity;
using CP6.Platform.Abstractions.Context;
using CP6.Platform.Contracts.Context;
using CP6.WebApi.Configuration.IdentityDirectoryTenancy;
using CP6.WebApi.Controllers.Internal;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Npgsql;

namespace CP6.WebApi.Services.IdentityDirectoryTenancy;

internal sealed class IdentityDirectoryAccessException(bool authenticated) : Exception("C02_DIRECTORY_ACCESS_REJECTED")
{ internal bool Authenticated { get; } = authenticated; }

/// <summary>Dedicated frozen opt-in host. No Owner connection, automatic installation or live configuration reload.
/// Control changes require stopping ingress, draining all instances and a reviewed restart/upgrade.</summary>
public sealed class IdentityDirectoryTenantHost : IAsyncDisposable
{
    internal const string Purpose = "identity-directory:read", Profile = "cp6-identity-directory-jwt-v2";
    private const string Audience = "CP6.Services", Scope = "cp6.services", SourceReference = "/internal/crm/identity";
    private readonly Frozen _configuration;
    private readonly TimeProvider _clock;
    private readonly ContextScopeAccessorV1 _accessor = new();
    private readonly TrustedContextExecutorV1 _executor;
    private readonly TenantConnectionManager _connections;
    private readonly TenantIdentityDirectoryAuthorizationReader _authorization;
    private readonly TenantIdentityDirectoryReader _directory;
    private readonly SourceVerifier _verifier;
    private readonly string _epoch = Guid.NewGuid().ToString("N");

    internal IdentityDirectoryTenantHost(Frozen configuration, CrmIdentityRuntime runtime, IDataProtectionProvider protection, TimeProvider clock)
    {
        _configuration = configuration; _clock = clock;
        _executor = new(new BindingAuthority(this), _accessor, clock); _verifier = new(this);
        _connections = new(configuration.Host, _accessor, configuration.Catalog, new Credentials(configuration.Passwords), new(32, 64, 2));
        var ownRuntime = new CrmIdentityRuntime(new CrmIdentityOptions { Enabled = true, Issuer = configuration.Issuer,
            Tenants = configuration.Regions.ToDictionary(x => x.Key, x => x.Value), ProjectionReaderClientIds = configuration.Clients.Keys.ToArray() }, runtime.Validator, clock);
        _authorization = new(_connections, configuration.Identity); _directory = new(_connections, ownRuntime, protection, configuration.Identity);
    }

    internal async Task<T> ReadAsync<T>(HttpContext http, Func<IIdentitySnapshotReader, Guid, Task<T>> action, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var authentication = await http.AuthenticateAsync(CrmIdentityController.Scheme).ConfigureAwait(false);
        if (!authentication.Succeeded || authentication.Principal is null) throw new IdentityDirectoryAccessException(false);
        if (!TryFacts(authentication.Principal, out var facts)) throw new IdentityDirectoryAccessException(true);
        var now = _clock.GetUtcNow();
        var skewExpiry = facts.Expires > DateTimeOffset.MaxValue.AddSeconds(-60) ? DateTimeOffset.MaxValue : facts.Expires.AddSeconds(60);
        var input = new SourceInput(http, facts, now, Min(Min(now.AddSeconds(_configuration.Lifetime), skewExpiry), _configuration.Until));
        if (now < _configuration.From || now >= _configuration.Until || input.Expires <= now) throw Unavailable();
        T? result = default; var completed = false;
        try
        {
            await _executor.ExecuteAsync(ContextEntryKindV1.Http, input, _verifier, async token =>
            {
                CheckCurrent(input);
                if (!await _authorization.IsAuthorizedAsync(facts.Tenant, _configuration.Issuer, facts.Client, facts.Jti, _clock.GetUtcNow(), token).ConfigureAwait(false))
                    throw new IdentityDirectoryAccessException(true);
                CheckCurrent(input);
                result = await action(_directory, facts.Tenant).ConfigureAwait(false);
                CheckCurrent(input); token.ThrowIfCancellationRequested(); completed = true;
            }, ct).ConfigureAwait(false);
        }
        catch (ContextResolutionExceptionV1) { throw Unavailable(); }
        return completed ? result! : throw Unavailable();
    }
    private void CheckCurrent(SourceInput input)
    {
        var now = _clock.GetUtcNow();
        if (now < _configuration.From || now >= _configuration.Until || now < input.AuthenticatedAt || now >= input.Expires ||
            _accessor.CurrentTrusted?.Context.TenantId != input.Facts.Tenant) throw Unavailable();
    }
    private bool TryFacts(ClaimsPrincipal principal, out ServiceFacts facts)
    {
        facts = null!;
        var clientId = Single(principal, "client_id");
        if (clientId is null || !_configuration.Clients.TryGetValue(clientId, out var client) ||
            Single(principal, "iss") != _configuration.Issuer || Single(principal, "sub") != "service:" + clientId || Single(principal, "scope") != Scope ||
            !Guid.TryParseExact(Single(principal, "tenant_id"), "D", out var tenant) || tenant == Guid.Empty || tenant != client.Tenant ||
            Single(principal, "tenant_id") != tenant.ToString("D") || !Guid.TryParseExact(Single(principal, "jti"), "D", out var jti) || jti == Guid.Empty ||
            Single(principal, "jti") != jti.ToString("D") || !long.TryParse(Single(principal, "exp"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var exp)) return false;
        try { facts = new(tenant, clientId, jti, DateTimeOffset.FromUnixTimeSeconds(exp)); return true; }
        catch (ArgumentOutOfRangeException) { return false; }
    }
    private sealed record ServiceFacts(Guid Tenant, string Client, Guid Jti, DateTimeOffset Expires);
    private sealed record SourceInput(HttpContext Http, ServiceFacts Facts, DateTimeOffset AuthenticatedAt, DateTimeOffset Expires);
    private sealed class SourceVerifier(IdentityDirectoryTenantHost host) : IContextSourceVerifierV1<SourceInput>
    {
        public async ValueTask<ContextResolutionInputV1?> VerifyAsync(SourceInput input, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            var authentication = await input.Http.AuthenticateAsync(CrmIdentityController.Scheme).ConfigureAwait(false);
            if (!authentication.Succeeded || authentication.Principal is null || !HttpMethods.IsGet(input.Http.Request.Method) ||
                !host.TryFacts(authentication.Principal, out var facts) || facts != input.Facts) return null;
            var client = host._configuration.Clients[facts.Client];
            var actor = new ContextActorV1(facts.Tenant, host._configuration.Issuer, "service:" + facts.Client, ContextActorKindV1.Service, null);
            return new(new(ContextEntryKindV1.Http, host._configuration.Host, Profile, Audience, Purpose, facts.Tenant,
                SourceReference, input.Http.TraceIdentifier, [Scope]),
                new(actor, actor, Audience, client.Region, input.AuthenticatedAt, input.Expires,
                    "validated-directory-jwt:" + facts.Jti.ToString("D"), facts.Jti.ToString("D"), [Scope], host._epoch + ":" + client.Version));
        }
    }
    private sealed class BindingAuthority(IdentityDirectoryTenantHost host) : IContextBindingAuthorityV1
    {
        public ValueTask<ContextBindingRegistrationV1?> ResolveCurrentAsync(ContextOperationV1 operation, AuthenticatedContextSourceV1 source, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested(); var now = host._clock.GetUtcNow(); var config = host._configuration;
            var id = source.Actor.Subject.StartsWith("service:", StringComparison.Ordinal) ? source.Actor.Subject[8..] : "";
            if (!config.Clients.TryGetValue(id, out var client) || operation.HostId != config.Host || operation.ProfileId != Profile ||
                operation.Purpose != Purpose || operation.EntryKind != ContextEntryKindV1.Http || operation.SourceReference != SourceReference ||
                operation.Audience != Audience || operation.ExpectedTenantId != client.Tenant || !operation.RequiredScopes.SequenceEqual([Scope]) ||
                source.Actor != new ContextActorV1(client.Tenant, config.Issuer, "service:" + id, ContextActorKindV1.Service, null) ||
                source.EffectiveActor != source.Actor || source.Region != client.Region || source.Audience != Audience || !source.Scopes.SequenceEqual([Scope]) ||
                source.SourceIdentityVersion != host._epoch + ":" + client.Version || source.OriginalRequesterReference is not null ||
                !Guid.TryParseExact(source.TokenId, "D", out var token) || token == Guid.Empty || source.TokenId != token.ToString("D") ||
                source.AuthenticationReference != "validated-directory-jwt:" + source.TokenId || source.AuthenticatedAtUtc < config.From ||
                source.AuthenticatedAtUtc > now || source.ExpiresAtUtc <= now || source.ExpiresAtUtc <= source.AuthenticatedAtUtc ||
                source.ExpiresAtUtc > source.AuthenticatedAtUtc.AddSeconds(config.Lifetime) || source.ExpiresAtUtc > config.Until ||
                now < config.From || now >= config.Until) return ValueTask.FromResult<ContextBindingRegistrationV1?>(null);
            config.Catalog.Resolve(client.Tenant);
            return ValueTask.FromResult<ContextBindingRegistrationV1?>(new(client.Tenant, client.Tenant, config.Host, Profile, config.Issuer,
                client.Region, Audience, Purpose, ContextEntryKindV1.Http, source.Actor, source.Actor, "identity-directory:" + id,
                config.Revision + ":" + client.Version, config.From, config.Until, true, [Scope]));
        }
    }
    internal sealed record Client(Guid Tenant, string Region, string Version);
    internal sealed record Frozen(string Host, string Revision, string Issuer, DateTimeOffset From, DateTimeOffset Until, int Lifetime,
        IdentityDirectoryDatabaseIdentity Identity, IReadOnlyDictionary<string, Client> Clients, IReadOnlyDictionary<Guid, string> Passwords,
        IReadOnlyDictionary<Guid, string> Regions, TenantStorageCatalog Catalog);
    internal static Frozen Freeze(IdentityDirectoryOptions options, CrmOidcOptions oidc, CrmIdentityOptions identity)
    {
        var bindings = options.Tenants.Select(t => new TenantStorageBindingV1(t.TenantId, options.HostId, "directory-db",
            TenantStorageBindingV1.SupportedLayout, IdentityDirectoryAuthorizationV2.ModelVersion, options.Revision, t.CredentialVersion,
            t.Login, [], TenantStorageState.Ready, IdentityDirectoryAuthorizationV2.MigrationVersion));
        var catalog = new TenantStorageCatalog(options.HostId, [IdentityDirectoryAuthorizationV2.ModelVersion], bindings,
            [new("directory-db", options.Server, options.Port, options.Database, Enum.Parse<SslMode>(options.SslMode))],
            options.Tenants.Select(t => new TenantPrincipal(t.Login, t.Login)));
        return new(options.HostId, options.Revision, oidc.Issuer, options.ValidFromUtc, options.ValidUntilUtc, options.SourceLifetimeSeconds,
            new(options.InstallationId, options.Server, options.Port, options.Database, options.DatabaseOid, options.SystemIdentifier, options.Fingerprint),
            options.Clients.ToDictionary(c => c.ClientId, c => { var registered = oidc.ServiceClients.Single(s => s.ClientId == c.ClientId);
                return new Client(registered.TenantId, identity.Tenants[registered.TenantId], c.RegistrationVersion); }, StringComparer.Ordinal),
            options.Tenants.ToDictionary(t => t.TenantId, t => t.Password), options.Tenants.ToDictionary(t => t.TenantId, t => identity.Tenants[t.TenantId]), catalog);
    }
    private sealed class Credentials(IReadOnlyDictionary<Guid, string> passwords) : ITenantCredentialResolver
    {
        public ValueTask<string> ResolvePasswordAsync(TenantStorageBindingV1 binding, CancellationToken ct)
        { ct.ThrowIfCancellationRequested(); return ValueTask.FromResult(passwords[binding.TenantId]); }
    }
    private static string? Single(ClaimsPrincipal principal, string type) => principal.FindAll(type).ToArray() is { Length: 1 } claims ? claims[0].Value : null;
    private static DateTimeOffset Min(DateTimeOffset a, DateTimeOffset b) => a < b ? a : b;
    private static IdentityReadException Unavailable() => new("C02_IDENTITY_STORE_UNAVAILABLE");
    internal (int Pools, int ReservedConnections, int Borrowers, int DrainingPools) PoolSnapshot => _connections.Snapshot();
    public ValueTask DisposeAsync() => _connections.DisposeAsync();
}
