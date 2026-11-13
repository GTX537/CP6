using CP6.Core.Services.Platform;
using CP6.Platform.Abstractions.Context;
using CP6.Platform.Contracts.Context;
using CP6.Platform.EntityFramework.OutboxV2;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Integration.StockOutbox;

// Host configuration is a deployment-owned service registration, never a request body/header.
// No issuer or tenant is inferred when registration is absent.
internal sealed class StockOutboxHostOptionsV1
{
    public bool Enabled { get; set; }
    public string HostId { get; set; } = "";
    public string Issuer { get; set; } = "";
    public string Region { get; set; } = "";
    internal void Validate()
    {
        static bool Metadata(string s) => !string.IsNullOrWhiteSpace(s) && s.Length <= 96 && !s.Any(char.IsControl);
        if (!Metadata(HostId) || !Metadata(Region) || Issuer != Issuer.Trim()
            || !Uri.TryCreate(Issuer, UriKind.Absolute, out var uri) || !uri.IsWellFormedOriginalString()
            || uri.Scheme != Uri.UriSchemeHttps || string.IsNullOrEmpty(uri.Host)
            || uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0 || Issuer.Any(char.IsControl))
            throw new InvalidOperationException("STOCK_OUTBOX_HOST_REGISTRATION_REQUIRED");
    }
}

internal sealed class StockOutboxFactoryV1(DbContextOptions<StockOutboxContextV1> options)
    : IDbContextFactory<StockOutboxContextV1>
{
    public StockOutboxContextV1 CreateDbContext() => new(options);
}

/// <summary>One fresh tenant job. This private verifier and the same-transaction Owner share actual authority;
/// a serialized ctx-v1 envelope or lack of HttpContext cannot authorize the worker.</summary>
internal sealed class StockOutboxHostV1(IDbContextFactory<StockOutboxContextV1> factory,
    StockOutboxHostOptionsV1 options, Guid tenant, bool privacyRestoreConfigured, bool routeControl = false) : IOutboxOwnerV2,
    IContextSourceVerifierV1<object>, IContextBindingAuthorityV1
{
    private readonly object _invocation = new();
    private readonly ContextScopeAccessorV1 _accessor = new();
    private DateTimeOffset _expires;
    private const string Audience = "Main.StockOutbox";
    internal ITrustedContextAccessorV1 Trusted => _accessor;
    internal string WorkerId => options.HostId + ":stock-hint";
    private ContextActorV1 Actor => new(tenant, options.Issuer, WorkerId, ContextActorKindV1.Service, null);
    private static string Version(StockOutboxAuthorityV1 a, Guid revision) => $"{a.Version}:{revision:N}";
    private sealed class TenantState
    {
        public bool Enable { get; set; }
        public DateTime? ExpireDate { get; set; }
        public Guid AuthenticationPolicyRevision { get; set; }
    }
    private async Task<(StockOutboxAuthorityV1 Authority, Guid Revision)> Current(DbContext db, CancellationToken ct)
    {
        var root = StockOutboxProtocolV1.Root(tenant);
        var a = await db.Set<StockOutboxAuthorityV1>().AsNoTracking().SingleOrDefaultAsync(x => x.RootId == root.Id, ct);
        var t = await db.Database.SqlQuery<TenantState>($"SELECT \"Enable\",\"ExpireDate\",\"AuthenticationPolicyRevision\" FROM public.\"Sys_Tenants\" WHERE \"Id\"={tenant}")
            .SingleOrDefaultAsync(ct);
        if (tenant == Guid.Empty || t is null || !t.Enable || (t.ExpireDate != null && t.ExpireDate <= DateTime.Now)
            || a is null || a.TenantId != tenant || a.PolicyVersion != StockOutboxProtocolV1.PolicyVersion
            || a.PublisherReference != StockOutboxProtocolV1.Publisher)
            throw new OutboxProtocolExceptionV2("STOCK_OUTBOX_CURRENT_AUTHORITY_REQUIRED");
        return (a, t.AuthenticationPolicyRevision);
    }
    internal Task Execute(Func<CancellationToken, Task> body, CancellationToken ct)
    {
        options.Validate();
        if (!options.Enabled) throw new OutboxProtocolExceptionV2("STOCK_OUTBOX_HOST_DISABLED");
        return new BackgroundContextExecutorV1<object>(new TrustedContextExecutorV1(this, _accessor), this)
            .ExecuteAsync(_invocation, body, ct);
    }
    async ValueTask<ContextResolutionInputV1?> IContextSourceVerifierV1<object>.VerifyAsync(object input, CancellationToken ct)
    {
        if (!ReferenceEquals(input, _invocation)) return null;
        await using var db = await factory.CreateDbContextAsync(ct);
        var (a, revision) = await Current(db, ct);
        var now = DateTimeOffset.UtcNow; _expires = now.AddMinutes(5);
        return new(new(ContextEntryKindV1.Background, options.HostId, StockOutboxProtocolV1.Profile, Audience,
            StockOutboxProtocolV1.Purpose, tenant, "Main.TenantScopeRunner.StockOutbox.v1", Guid.NewGuid().ToString("D"), [StockOutboxProtocolV1.Scope]),
            new(Actor, Actor, Audience, options.Region, now, _expires, "Main.StockOutbox.HostRegistration.v1", null,
                [StockOutboxProtocolV1.Scope], Version(a, revision)));
    }
    async ValueTask<ContextBindingRegistrationV1?> IContextBindingAuthorityV1.ResolveCurrentAsync(
        ContextOperationV1 operation, AuthenticatedContextSourceV1 source, CancellationToken ct)
    {
        if (source.Actor != Actor || source.EffectiveActor != Actor || operation.ExpectedTenantId != tenant
            || operation.HostId != options.HostId || operation.ProfileId != StockOutboxProtocolV1.Profile
            || operation.EntryKind != ContextEntryKindV1.Background || operation.Audience != Audience
            || operation.Purpose != StockOutboxProtocolV1.Purpose) return null;
        await using var db = await factory.CreateDbContextAsync(ct);
        var (a, revision) = await Current(db, ct);
        if (source.SourceIdentityVersion != Version(a, revision)) return null;
        return new(tenant, tenant, options.HostId, StockOutboxProtocolV1.Profile, options.Issuer, options.Region,
            Audience, StockOutboxProtocolV1.Purpose, ContextEntryKindV1.Background, Actor, Actor,
            StockOutboxProtocolV1.Root(tenant).Id, Version(a, revision), source.AuthenticatedAtUtc, _expires,
            true, [StockOutboxProtocolV1.Scope]);
    }
    public async ValueTask<OutboxRegistrationV2> ResolveAsync(DbContext db, TrustedRequestContextV1 trusted,
        OutboxRootKeyV2 root, CancellationToken ct)
    {
        var e = trusted.Evidence;
        if (!ReferenceEquals(trusted, _accessor.CurrentTrusted) || !options.Enabled || DateTimeOffset.UtcNow >= _expires
            || root != StockOutboxProtocolV1.Root(tenant) || trusted.Context.TenantId != tenant
            || e.HostId != options.HostId || e.Issuer != options.Issuer || e.ProfileId != StockOutboxProtocolV1.Profile
            || e.Purpose != StockOutboxProtocolV1.Purpose || e.EntryKind != ContextEntryKindV1.Background
            || e.Actor != Actor || e.EffectiveActor != Actor || !e.GrantedScopes.Contains(StockOutboxProtocolV1.Scope))
            throw new OutboxProtocolExceptionV2("STOCK_OUTBOX_TRUSTED_HOST_REQUIRED");
        var (a, revision) = await Current(db, ct);
        if (e.RegistrationVersion != Version(a, revision)) throw new OutboxProtocolExceptionV2("STOCK_OUTBOX_AUTHORITY_CHANGED");
        return new(root, StockOutboxContextV1.Schema, WorkerId, StockOutboxProtocolV1.Publisher,
            "20261023003000_MainStockOutboxV1", OutboxEncodingV2.Mapping, "Main.StockOutbox.Writer.v1",
            "Main.StockOutbox.NewOriginalsOnly.v1", [], StockOutboxProtocolV1.RouteLock(root), StockOutboxProtocolV1.Policy,
            [new("public", "Sys_Tenants", "Id", tenant), new(StockOutboxContextV1.Schema, "Authority", "RootId", root.Id)]);
    }

    internal static async Task RequireDispatchDatabaseAsync(DbContext db, bool privacyRestoreConfigured, CancellationToken ct)
    {
        // Stock dispatch requires an ordinary queue database even after privacy admission opens.
        if (privacyRestoreConfigured || (await PrivacyRestoreNativeV1.Marker(db, ct))
            .StartsWith(PrivacyRestoreNativeV1.MarkerPrefix, StringComparison.Ordinal))
            throw new OutboxProtocolExceptionV2("STOCK_OUTBOX_RESTORE_DISPATCH_FORBIDDEN");
    }

    public async ValueTask AuthorizeAsync(OutboxTransactionV2 tx, OutboxAccessV2 access, CancellationToken ct)
    {
        await RequireDispatchDatabaseAsync(tx.Context, privacyRestoreConfigured, ct);
        await ResolveAsync(tx.Context, _accessor.CurrentTrusted ?? throw new OutboxProtocolExceptionV2("STOCK_OUTBOX_TRUSTED_HOST_REQUIRED"), tx.Registration.Root, ct);
        var (authority, _) = await Current(tx.Context, ct);
        if (access == OutboxAccessV2.Retention || (access == OutboxAccessV2.Switch && !routeControl)
            || (routeControl && access is not (OutboxAccessV2.ReadReceipt or OutboxAccessV2.Switch))
            || (access != OutboxAccessV2.ReadReceipt && !authority.Enabled))
            throw new OutboxProtocolExceptionV2("STOCK_OUTBOX_WRITE_NOT_AUTHORIZED");
        if (access == OutboxAccessV2.Switch)
            await RequireTerminalQueueAsync(tx, ct);
    }

    private static async Task RequireTerminalQueueAsync(OutboxTransactionV2 tx, CancellationToken ct)
    {
        var root = tx.Registration.Root.Id;
        // Held under the package's tenant, authority and route locks. DispatchRun and the
        // original completion receipts cover in-flight work, including a lost send response.
        var ready = await tx.Context.Database.SqlQuery<bool>($"""
            SELECT (NOT EXISTS (SELECT FROM cp6_stock_outbox_v1."DispatchRun"
                WHERE "RootId"={root} AND "CompletedAtUtc" IS NULL)
              AND NOT EXISTS (SELECT FROM cp6_stock_outbox_v1."Cp6_OutboxMessage" n
                LEFT JOIN cp6_stock_outbox_v1."Cp6_OutboxBindingV2" b ON b."NativeId"=n."Id"
                WHERE n."TenantId"={tx.TenantId} AND
                  (b."RootId" IS DISTINCT FROM {root} OR n."Status" NOT IN (2,3)
                   OR n."LeaseOwner" IS NOT NULL OR n."LeaseToken" IS NOT NULL OR n."LeaseExpiresAtUtc" IS NOT NULL
                   OR b."CurrentClaimId" IS NOT NULL OR b."ClaimEpoch" IS NOT NULL
                   OR NOT EXISTS (SELECT FROM cp6_stock_outbox_v1."Cp6_OutboxMutationReceiptV2" m
                     WHERE m."RootId"={root} AND m."NativeId"=n."Id" AND m."Output"::jsonb->>'Transition'=
                       CASE n."Status" WHEN 2 THEN 'Published' WHEN 3 THEN 'DeadLettered' END)))
              AND NOT EXISTS (SELECT FROM cp6_stock_outbox_v1."Cp6_OutboxSendAttemptV2" s
                WHERE s."RootId"={root} AND NOT EXISTS (
                  SELECT FROM cp6_stock_outbox_v1."Cp6_OutboxMutationReceiptV2" m
                  WHERE (m."RootId",m."MutationId")=(s."RootId",s."MutationId")
                    AND m."Output"::jsonb->>'Transition' IN ('Published','RetryScheduled','DeadLettered')))) AS "Value"
            """).SingleAsync(ct);
        if (!ready) throw new OutboxProtocolExceptionV2("STOCK_OUTBOX_SWITCH_UNRESOLVED");
    }

    public ValueTask<OutboxHandoverV2?> VerifyHandoverAsync(OutboxTransactionV2 tx, Guid id, CancellationToken ct)
        => ValueTask.FromResult<OutboxHandoverV2?>(routeControl
            ? new(id.ToString("D"), StockOutboxProtocolV1.Publisher, [], true)
            : null);
    public ValueTask<OutboxRetentionEvidenceV1?> RetentionEligibilityAsync(OutboxTransactionV2 tx, OutboxRetentionCandidateV1 candidate, CancellationToken ct)
        => ValueTask.FromResult<OutboxRetentionEvidenceV1?>(null);
}
