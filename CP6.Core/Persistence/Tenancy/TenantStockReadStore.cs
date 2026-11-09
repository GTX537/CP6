using System.Globalization;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Common;
using CP6.Entity.DomainModels.Wms;
using CP6.Platform.Abstractions.Context;
using CP6.Platform.Contracts.Context;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace CP6.Core.Persistence.Tenancy;

/// <summary>Executes the stock Owner's read under a tenant LOGIN while the original Owner
/// transaction holds the authority and business graph fences in the same primary database.</summary>
internal sealed class TenantStockReadStore : IAsyncDisposable
{
    internal const string Host = "main-stock-read";
    private const string Purpose = "wms-stock:query";
    private const string Issuer = "https://cp6.internal/main-stock-read";
    private const string Audience = "CP6.Main.StockRead";
    private static readonly string[] Scopes = [Purpose];
    private readonly TenantConnectionManager _connections;
    private readonly TrustedContextExecutorV1 _executor;
    private readonly IHttpContextAccessor _http;

    internal TenantStockReadStore(TenantStorageCatalog catalog, ITenantCredentialResolver credentials, IHttpContextAccessor http)
    {
        var accessor = new ContextScopeAccessorV1();
        _connections = new(Host, accessor, catalog, credentials, new(32, 64, 2));
        _executor = new(new BindingAuthority(catalog), accessor);
        _http = http;
    }

    // Called only inside StockOperationsService after the native Owner's current session and query decision.
    internal async Task<T> ReadAsync<T>(CP6Context authority, Guid actorId, Func<CP6Context, CancellationToken, Task<T>> read,
        CancellationToken ct)
    {
        var http = _http.HttpContext!;
        var tenant = authority.CurrentTenantId;
        var actor = new ContextActorV1(tenant, Issuer, actorId.ToString("D"), ContextActorKindV1.Human, actorId);
        var expires = DateTimeOffset.FromUnixTimeSeconds(long.Parse(http.User.FindFirst("exp")!.Value, CultureInfo.InvariantCulture));
        var tokenId = http.User.FindFirst("jti")!.Value;
        // The Owner has verified both sides of an impersonated session. The storage tenant is the
        // effective subject's tenant. Keep the original requester reference in the resolved evidence.
        var original = http.User.FindFirst("impersonator_id")?.Value;
        var source = new AuthenticatedContextSourceV1(actor, actor, Audience, "main",
            DateTimeOffset.UtcNow, expires, "current-stock-owner:" + tokenId, tokenId, Scopes,
            OriginalRequesterReference: original is null ? null : http.User.FindFirst("impersonator_tenant_id")!.Value + ":" + original);
        var operation = new ContextOperationV1(ContextEntryKindV1.Http, Host, StockReadStorageV1.ModelVersion,
            Audience, Purpose, tenant, "api/wms/stock", http.TraceIdentifier, Scopes);

        var identity = await StockReadStorageV1.ReadDatabaseIdentityAsync(
            (NpgsqlConnection)authority.Database.GetDbConnection(),
            (NpgsqlTransaction)authority.Database.CurrentTransaction!.GetDbTransaction(), ct);
        T result = default!;
        await _executor.ExecuteAsync(ContextEntryKindV1.Http, new ContextResolutionInputV1(operation, source),
            VerifiedOwnerSource.Instance, async token =>
            {
                await using var lease = await _connections.OpenCurrentAsync(token);
                result = await lease.InTransactionAsync(async (transaction, cancellation) =>
                {
                    var value = await transaction.ExecuteAsync(async (connection, dbTransaction, queryToken) =>
                    {
                        await using (var readOnly = new NpgsqlCommand("SET TRANSACTION READ ONLY", connection, dbTransaction))
                            await readOnly.ExecuteNonQueryAsync(queryToken);
                        if (identity != await StockReadStorageV1.ReadDatabaseIdentityAsync(connection, dbTransaction, queryToken, requireRowSecurity: true))
                            throw new TenantStorageException(TenantStorageFailure.IdentityRejected);

                        using var guard = new TenantEfCommandGuard(transaction, connection, dbTransaction, tenant);
                        var options = new DbContextOptionsBuilder<CP6Context>()
                            .UseNpgsql(connection, contextOwnsConnection: false).AddInterceptors(guard);
                        DatabaseContextOptions.ConfigurePostgreSql(options);
                        await using var db = new StockReadContext(options.Options, tenant);
                        await db.Database.UseTransactionAsync(dbTransaction, queryToken);
                        return await read(db, queryToken);
                    }, cancellation);
                    await transaction.CommitAsync(cancellation);
                    return value;
                }, cancellationToken: token);
            }, ct);
        return result;
    }

    public ValueTask DisposeAsync() => _connections.DisposeAsync();

    private sealed class VerifiedOwnerSource : IContextSourceVerifierV1<ContextResolutionInputV1>
    {
        internal static readonly VerifiedOwnerSource Instance = new();
        public ValueTask<ContextResolutionInputV1?> VerifyAsync(ContextResolutionInputV1 input, CancellationToken ct = default)
            => ValueTask.FromResult<ContextResolutionInputV1?>(input);
    }

    private sealed class BindingAuthority(TenantStorageCatalog catalog) : IContextBindingAuthorityV1
    {
        public ValueTask<ContextBindingRegistrationV1?> ResolveCurrentAsync(ContextOperationV1 operation,
            AuthenticatedContextSourceV1 source, CancellationToken ct = default)
        {
            var binding = catalog.Resolve(operation.ExpectedTenantId);
            return ValueTask.FromResult<ContextBindingRegistrationV1?>(new(binding.TenantId, binding.TenantId, Host,
                StockReadStorageV1.ModelVersion, Issuer, "main", Audience, Purpose, ContextEntryKindV1.Http,
                source.Actor, source.EffectiveActor, "stock-read:" + binding.TenantId.ToString("D"), binding.BindingRevision,
                source.AuthenticatedAtUtc, source.ExpiresAtUtc, true, Scopes));
        }
    }

    // Fixed schema for the original global keys. Separate model cache by context type.
    private sealed class StockReadContext(DbContextOptions<CP6Context> options, Guid tenant)
        : CP6Context(options, new TenantContext { CurrentTenantId = tenant })
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Entity<Stock>().ToTable("T_Stock", "public");
            modelBuilder.Entity<StockTransaction>().ToTable("T_StockTransaction", "public");
            modelBuilder.Entity<WmsRoleScope>().ToTable("T_WmsRoleScope", "public");
            modelBuilder.Entity<Location>().ToTable("T_Location", "public");
        }
    }
}
