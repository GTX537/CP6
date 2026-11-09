using System.Globalization;
using System.Runtime.ExceptionServices;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Common;
using CP6.Core.Services.Erp;
using CP6.Core.Services.Integration;
using CP6.Entity.DomainModels.Wms;
using CP6.Platform.Abstractions.Context;
using CP6.Platform.Contracts.Context;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace CP6.Core.Persistence.Tenancy;

internal enum StockReadEntry { Query, AdjustCreationV2, MoveCreationV2 }
internal enum StockWriteEntry { ApplyV1, MoveV1, CloseV1, ApplyV2, MoveV2, ApplyLegacy }

/// <summary>Runs the Stock Owner's reads and explicitly enabled commands under a tenant LOGIN on the same primary.</summary>
internal sealed class TenantStockReadStore : IAsyncDisposable
{
    internal const string Host = "main-stock-read";
    private const string ReadPurpose = "wms-stock:query";
    private const string AdjustCreationPurpose = "wms-stock:creation-adjust-v2";
    private const string MoveCreationPurpose = "wms-stock:creation-move-v2";
    private const string AdjustPurpose = "wms-stock:adjust-v1";
    private const string LegacyAdjustPurpose = "wms-stock:adjust-legacy";
    private const string AdjustV2Purpose = "wms-stock:adjust-v2";
    private const string MovePurpose = "wms-stock:move-v1";
    private const string MoveV2Purpose = "wms-stock:move-v2";
    private const string ClosePurpose = "wms-stock:close-operation-v1";
    private const string Issuer = "https://cp6.internal/main-stock-read";
    private const string Audience = "CP6.Main.StockRead";
    private readonly TenantConnectionManager _connections;
    private readonly TrustedContextExecutorV1 _executor;
    private readonly IHttpContextAccessor _http;

    internal bool V1AdjustEnabled { get; }
    internal bool V1MoveEnabled { get; }
    internal bool V2AdjustEnabled { get; }
    internal bool V2MoveEnabled { get; }

    internal TenantStockReadStore(TenantStorageCatalog catalog, ITenantCredentialResolver credentials,
        IHttpContextAccessor http, bool v1AdjustEnabled = false, bool v1MoveEnabled = false, bool v2AdjustEnabled = false,
        bool v2MoveEnabled = false)
    {
        var accessor = new ContextScopeAccessorV1();
        _connections = new(Host, accessor, catalog, credentials, new(32, 64, 2));
        _executor = new(new BindingAuthority(catalog, v1AdjustEnabled, v1MoveEnabled, v2AdjustEnabled, v2MoveEnabled), accessor);
        _http = http;
        V1AdjustEnabled = v1AdjustEnabled;
        V1MoveEnabled = v1MoveEnabled;
        V2AdjustEnabled = v2AdjustEnabled;
        V2MoveEnabled = v2MoveEnabled;
    }

    // Called only after the native Stock Owner's current session and action decision.
    internal Task<T> ReadAsync<T>(CP6Context authority, Guid actorId, Func<CP6Context, CancellationToken, Task<T>> read,
        CancellationToken ct, bool requireJournalRls = false, StockReadEntry entry = StockReadEntry.Query)
        => WithTenantTransactionAsync(authority, actorId, entry switch
        {
            StockReadEntry.Query => ReadPurpose,
            StockReadEntry.AdjustCreationV2 => AdjustCreationPurpose,
            StockReadEntry.MoveCreationV2 => MoveCreationPurpose,
            _ => throw new TenantStorageException(TenantStorageFailure.BindingRejected)
        }, async (transaction, identity, cancellation) =>
        {
            var value = await transaction.ExecuteAsync(async (connection, dbTransaction, queryToken) =>
            {
                await using (var readOnly = new NpgsqlCommand("SET TRANSACTION READ ONLY", connection, dbTransaction))
                    await readOnly.ExecuteNonQueryAsync(queryToken);
                await RequireSameDatabase(identity, connection, dbTransaction, queryToken, requireJournalRls);
                var tenant = authority.CurrentTenantId;
                using var guard = new TenantEfCommandGuard(transaction, connection, dbTransaction, tenant);
                await using var db = new StockReadContext(Options(connection, guard), tenant);
                await db.Database.UseTransactionAsync(dbTransaction, queryToken);
                return await read(db, queryToken);
            }, cancellation);
            await transaction.CommitAsync(cancellation);
            return value;
        }, ct);

    internal async Task<T> WriteAsync<T>(CP6Context authority, Guid actorId, StockWriteEntry entry,
        Func<StockMovementWriteContextV1, CancellationToken, Task<T>> stage, CancellationToken ct)
    {
        var purpose = entry switch
        {
            StockWriteEntry.ApplyV1 => AdjustPurpose,
            StockWriteEntry.MoveV1 => MovePurpose,
            StockWriteEntry.CloseV1 => ClosePurpose,
            StockWriteEntry.ApplyV2 => AdjustV2Purpose,
            StockWriteEntry.MoveV2 => MoveV2Purpose,
            StockWriteEntry.ApplyLegacy => LegacyAdjustPurpose,
            _ => throw new TenantStorageException(TenantStorageFailure.BindingRejected)
        };
        var committing = false;
        var rollbackUnknown = false;
        try
        {
            var outcome = await WithTenantTransactionAsync(authority, actorId, purpose, async (transaction, identity, cancellation) =>
            {
                try
                {
                    var attempt = await transaction.ExecuteAsync<(T Result, ExceptionDispatchInfo? Failure)>(async (connection, dbTransaction, token) =>
                    {
                        await RequireSameDatabase(identity, connection, dbTransaction, token, requireJournalRls: true);
                        var tenant = authority.CurrentTenantId;
                        using var guard = new TenantEfCommandGuard(transaction, connection, dbTransaction, tenant);
                        await using var writer = new StockMovementWriteContextV1(Options(connection, guard), tenant);
                        await writer.Database.UseTransactionAsync(dbTransaction, token);
                        try { return (await stage(writer, token), null); }
                        catch (Exception error) when (OperationsOwnerV1.IsExpectedOwnerFailure(error, token))
                        { return (default!, ExceptionDispatchInfo.Capture(error)); }
                    }, cancellation);
                    if (attempt.Failure is null)
                    {
                        transaction.SetCommitFence(token =>
                        {
                            token.ThrowIfCancellationRequested();
                            committing = true;
                            return Task.CompletedTask;
                        });
                        await transaction.CommitAsync(cancellation);
                    }
                    return attempt;
                }
                finally
                {
                    if (!committing)
                    {
                        try { await transaction.RollbackAsync(CancellationToken.None); }
                        catch (TenantStorageException) { rollbackUnknown = true; throw; }
                    }
                }
            }, ct);
            outcome.Failure?.Throw();
            return outcome.Result;
        }
        catch (Exception error) when (committing || rollbackUnknown)
        {
            throw new OrderPriceCorrectionException("OPERATIONS_OUTCOME_UNKNOWN", 503, error);
        }
    }

    private async Task<T> WithTenantTransactionAsync<T>(CP6Context authority, Guid actorId, string purpose,
        Func<TenantStorageTransaction, string, CancellationToken, Task<T>> action, CancellationToken ct)
    {
        var http = _http.HttpContext!;
        var tenant = authority.CurrentTenantId;
        var actor = new ContextActorV1(tenant, Issuer, actorId.ToString("D"), ContextActorKindV1.Human, actorId);
        var expires = DateTimeOffset.FromUnixTimeSeconds(long.Parse(http.User.FindFirst("exp")!.Value, CultureInfo.InvariantCulture));
        var tokenId = http.User.FindFirst("jti")!.Value;
        // The Owner has verified both sides of an impersonated session. The storage tenant is the
        // effective subject's tenant. Keep the original requester reference in the resolved evidence.
        var original = http.User.FindFirst("impersonator_id")?.Value;
        string[] scopes = [purpose];
        var source = new AuthenticatedContextSourceV1(actor, actor, Audience, "main",
            DateTimeOffset.UtcNow, expires, "current-stock-owner:" + tokenId, tokenId, scopes,
            OriginalRequesterReference: original is null ? null : http.User.FindFirst("impersonator_tenant_id")!.Value + ":" + original);
        var reference = purpose switch
        {
            ReadPurpose => "api/wms/stock",
            AdjustCreationPurpose => "api/wms/stock/creation-context/adjust",
            MoveCreationPurpose => "api/wms/stock/creation-context/move",
            AdjustPurpose => "api/wms/stock/commands/v1/apply",
            LegacyAdjustPurpose => "api/wms/stock/apply",
            AdjustV2Purpose => "api/wms/stock/commands/v2/apply",
            MovePurpose => "api/wms/stock/commands/v1/move",
            MoveV2Purpose => "api/wms/stock/commands/v2/move",
            ClosePurpose => "api/wms/stock/operations/v1/close",
            _ => throw new TenantStorageException(TenantStorageFailure.BindingRejected)
        };
        var operation = new ContextOperationV1(ContextEntryKindV1.Http, Host, StockReadStorageV1.ModelVersion,
            Audience, purpose, tenant, reference, http.TraceIdentifier, scopes);

        var identity = await StockReadStorageV1.ReadDatabaseIdentityAsync(
            (NpgsqlConnection)authority.Database.GetDbConnection(),
            (NpgsqlTransaction)authority.Database.CurrentTransaction!.GetDbTransaction(), ct);
        T result = default!;
        await _executor.ExecuteAsync(ContextEntryKindV1.Http, new ContextResolutionInputV1(operation, source),
            VerifiedOwnerSource.Instance, async token =>
            {
                await using var lease = await _connections.OpenCurrentAsync(token);
                result = await lease.InTransactionAsync((transaction, cancellation) => action(transaction, identity, cancellation),
                    cancellationToken: token);
            }, ct);
        return result;
    }

    private static async Task RequireSameDatabase(string identity, NpgsqlConnection connection, NpgsqlTransaction transaction,
        CancellationToken ct, bool requireJournalRls = false)
    {
        if (identity != await StockReadStorageV1.ReadDatabaseIdentityAsync(connection, transaction, ct,
                requireRowSecurity: true, requireJournalRls: requireJournalRls))
            throw new TenantStorageException(TenantStorageFailure.IdentityRejected);
    }

    private static DbContextOptions<CP6Context> Options(NpgsqlConnection connection, TenantEfCommandGuard guard)
    {
        var options = new DbContextOptionsBuilder<CP6Context>().UseNpgsql(connection, contextOwnsConnection: false).AddInterceptors(guard);
        DatabaseContextOptions.ConfigurePostgreSql(options);
        return options.Options;
    }

    public ValueTask DisposeAsync() => _connections.DisposeAsync();

    private sealed class VerifiedOwnerSource : IContextSourceVerifierV1<ContextResolutionInputV1>
    {
        internal static readonly VerifiedOwnerSource Instance = new();
        public ValueTask<ContextResolutionInputV1?> VerifyAsync(ContextResolutionInputV1 input, CancellationToken ct = default)
            => ValueTask.FromResult<ContextResolutionInputV1?>(input);
    }

    private sealed class BindingAuthority(TenantStorageCatalog catalog, bool v1AdjustEnabled, bool v1MoveEnabled,
        bool v2AdjustEnabled, bool v2MoveEnabled)
        : IContextBindingAuthorityV1
    {
        public ValueTask<ContextBindingRegistrationV1?> ResolveCurrentAsync(ContextOperationV1 operation,
            AuthenticatedContextSourceV1 source, CancellationToken ct = default)
        {
            var enabled = operation.Purpose switch
            {
                ReadPurpose => true,
                AdjustPurpose or ClosePurpose or LegacyAdjustPurpose => v1AdjustEnabled,
                MovePurpose => v1MoveEnabled,
                AdjustCreationPurpose or AdjustV2Purpose => v2AdjustEnabled,
                MoveCreationPurpose or MoveV2Purpose => v2MoveEnabled,
                _ => false
            };
            if (!enabled)
                return ValueTask.FromResult<ContextBindingRegistrationV1?>(null);
            var binding = catalog.Resolve(operation.ExpectedTenantId);
            return ValueTask.FromResult<ContextBindingRegistrationV1?>(new(binding.TenantId, binding.TenantId, Host,
                StockReadStorageV1.ModelVersion, Issuer, "main", Audience, operation.Purpose, ContextEntryKindV1.Http,
                source.Actor, source.EffectiveActor, "stock-read:" + binding.TenantId.ToString("D"), binding.BindingRevision,
                source.AuthenticatedAtUtc, source.ExpiresAtUtc, true, [operation.Purpose]));
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
