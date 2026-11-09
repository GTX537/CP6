using System.Globalization;
using System.Runtime.ExceptionServices;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Common;
using CP6.Core.Services.Erp;
using CP6.Platform.Abstractions.Context;
using CP6.Platform.Contracts.Context;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace CP6.Core.Persistence.Tenancy;

internal sealed partial class TenantRmaReadStore : IAsyncDisposable
{
    internal const string Host = "main-rma-read";
    private const string QueryPurpose = "wms-rma:query";
    private const string DraftPurpose = "wms-rma:draft-write";
    private const string SubmitPurpose = "wms-rma:submit-write";
    private const string ClosePurpose = "wms-rma:close-write";
    private const string Issuer = "https://cp6.internal/main-rma-read";
    private const string Audience = "CP6.Main.RmaRead";

    private readonly TenantStorageCatalog _catalog;
    private readonly Guid _installation;
    private readonly IHttpContextAccessor _http;
    private readonly TenantConnectionManager _connections;
    private readonly TrustedContextExecutorV1 _executor;
    internal bool DraftEnabled { get; }
    internal bool SubmitEnabled { get; }
    internal bool OriginalOperationsEnabled { get; }

    internal TenantRmaReadStore(TenantStorageCatalog catalog, ITenantCredentialResolver credentials,
        IHttpContextAccessor http, Guid installation, bool draftEnabled = false, bool submitEnabled = false,
        bool originalOperationsEnabled = false)
    {
        _catalog = catalog;
        _installation = installation;
        _http = http;
        DraftEnabled = draftEnabled;
        SubmitEnabled = submitEnabled;
        OriginalOperationsEnabled = originalOperationsEnabled;
        var scope = new ContextScopeAccessorV1();
        _connections = new(Host, scope, catalog, credentials, new(32, 64, 2));
        _executor = new(new BindingAuthority(catalog, draftEnabled, submitEnabled, originalOperationsEnabled), scope);
    }

    // The existing RMA Owner has verified the current signed session and action.
    // It retains the authentication and shared outbound fences while this read runs.
    internal async Task<T> ReadAsync<T>(CP6Context authority, Guid actorId, string operationReference,
        Func<RmaTenantReadContext, CancellationToken, Task<T>> read, CancellationToken ct) where T : class
    {
        try
        {
            var tenant = authority.CurrentTenantId;
            var configured = _catalog.Resolve(tenant);
            var authorityIdentity = await RmaTenantStorageV1.RequireCoreReadBindingAsync(
                (NpgsqlConnection)authority.Database.GetDbConnection(),
                (NpgsqlTransaction)authority.Database.CurrentTransaction!.GetDbTransaction(),
                configured, _catalog.Principal(configured).Username, _installation, ct);

            var outcome = await InTenantScopeAsync(authority, actorId, operationReference, QueryPurpose,
                async (transaction, cancellation) =>
                {
                    var result = await transaction.ExecuteAsync<(T? Value, ExceptionDispatchInfo? Failure)>(
                        async (connection, dbTransaction, queryToken) =>
                        {
                            await using var readOnly = new NpgsqlCommand(
                                "SET TRANSACTION READ ONLY; SET LOCAL lock_timeout='10s'", connection, dbTransaction);
                            await readOnly.ExecuteNonQueryAsync(queryToken);
                            if (authorityIdentity != await RmaTenantStorageV1.CoreReadPrimaryAsync(connection, dbTransaction, queryToken))
                                throw new TenantStorageException(TenantStorageFailure.IdentityRejected);

                            using var guard = new TenantEfCommandGuard(transaction, connection, dbTransaction, tenant);
                            var options = RmaTenantReadContext.Options(connection).AddInterceptors(guard);
                            await using var db = new RmaTenantReadContext(options.Options, transaction.CurrentBinding);
                            await db.Database.UseTransactionAsync(dbTransaction, queryToken);
                            // Preserve expected Owner denial and not-found responses through the lease.
                            try { return (await read(db, queryToken), null); }
                            catch (OrderPriceCorrectionException error) { return (null, ExceptionDispatchInfo.Capture(error)); }
                        }, cancellation);
                    if (result.Failure is null)
                        await transaction.CommitAsync(cancellation);
                    return result;
                }, ct);
            outcome.Failure?.Throw();
            return outcome.Value!;
        }
        catch (TenantStorageException error)
        {
            throw new OrderPriceCorrectionException("RMA_TENANT_BINDING_UNAVAILABLE", 503, error);
        }
        catch (PostgresException error) when (error.SqlState is PostgresErrorCodes.UndefinedTable
            or PostgresErrorCodes.InvalidSchemaName or PostgresErrorCodes.InsufficientPrivilege)
        {
            throw new OrderPriceCorrectionException("RMA_TENANT_BINDING_UNAVAILABLE", 503, error);
        }
    }

    private async Task<T> InTenantScopeAsync<T>(CP6Context authority, Guid actorId, string operationReference, string purpose,
        Func<TenantStorageTransaction, CancellationToken, Task<T>> action, CancellationToken ct)
    {
        var tenant = authority.CurrentTenantId;
        var http = _http.HttpContext!;
        var user = http.User;
        var actor = new ContextActorV1(tenant, Issuer, actorId.ToString("D"), ContextActorKindV1.Human, actorId);
        var tokenId = user.FindFirst("jti")!.Value;
        var expires = DateTimeOffset.FromUnixTimeSeconds(long.Parse(user.FindFirst("exp")!.Value, CultureInfo.InvariantCulture));
        var original = user.FindFirst("impersonator_id")?.Value;
        string[] scopes = [purpose];
        var source = new AuthenticatedContextSourceV1(actor, actor, Audience, "main", DateTimeOffset.UtcNow,
            expires, "current-rma-owner:" + tokenId, tokenId, scopes,
            OriginalRequesterReference: original is null ? null : user.FindFirst("impersonator_tenant_id")!.Value + ":" + original);
        var operation = new ContextOperationV1(ContextEntryKindV1.Http, Host, RmaTenantStorageV1.CoreReadProfile,
            Audience, purpose, tenant, operationReference, http.TraceIdentifier, scopes);

        T result = default!;
        await _executor.ExecuteAsync(ContextEntryKindV1.Http, new ContextResolutionInputV1(operation, source),
            VerifiedRmaOwnerSource.Instance, async token =>
            {
                await using var lease = await _connections.OpenCurrentAsync(token);
                result = await lease.InTransactionAsync(action, cancellationToken: token);
            }, ct);
        return result;
    }

    public ValueTask DisposeAsync() => _connections.DisposeAsync();

    private sealed class VerifiedRmaOwnerSource : IContextSourceVerifierV1<ContextResolutionInputV1>
    {
        internal static readonly VerifiedRmaOwnerSource Instance = new();
        public ValueTask<ContextResolutionInputV1?> VerifyAsync(ContextResolutionInputV1 input, CancellationToken ct = default) =>
            ValueTask.FromResult<ContextResolutionInputV1?>(input);
    }

    private sealed class BindingAuthority(TenantStorageCatalog catalog, bool draftEnabled, bool submitEnabled,
        bool originalOperationsEnabled)
        : IContextBindingAuthorityV1
    {
        public ValueTask<ContextBindingRegistrationV1?> ResolveCurrentAsync(ContextOperationV1 operation,
            AuthenticatedContextSourceV1 source, CancellationToken ct = default)
        {
            var enabled = operation.Purpose switch
            {
                QueryPurpose => true,
                DraftPurpose => draftEnabled,
                SubmitPurpose => submitEnabled,
                ClosePurpose => originalOperationsEnabled,
                _ => false
            };
            if (!enabled) return ValueTask.FromResult<ContextBindingRegistrationV1?>(null);
            var binding = catalog.Resolve(operation.ExpectedTenantId);
            return ValueTask.FromResult<ContextBindingRegistrationV1?>(new(binding.TenantId, binding.TenantId, Host,
                RmaTenantStorageV1.CoreReadProfile, Issuer, "main", Audience, operation.Purpose, ContextEntryKindV1.Http,
                source.Actor, source.EffectiveActor, "rma-read:" + binding.TenantId.ToString("D"), binding.BindingRevision,
                source.AuthenticatedAtUtc, source.ExpiresAtUtc, true, [operation.Purpose]));
        }
    }
}

internal static partial class RmaTenantStorageV1
{
    internal const string CoreReadProfile = CoreProfile;
    internal const string CoreReadMigration = MigrationId;
    private const string PrimaryIdentity = """
        pg_catalog.concat_ws('/', pg_catalog.host(pg_catalog.inet_server_addr()), pg_catalog.inet_server_port(),
            d.oid, extract(epoch FROM pg_catalog.pg_postmaster_start_time()))
        """;

    internal static async Task<string> RequireCoreReadBindingAsync(NpgsqlConnection connection, NpgsqlTransaction tx,
        TenantStorageBindingV1 binding, string login, Guid installation, CancellationToken ct)
    {
        // Ready is a native, locked control row. The configured Ready designation alone grants no access.
        await using var command = new NpgsqlCommand($"""
            SELECT {PrimaryIdentity}
            FROM {Control}.installation i
            CROSS JOIN {CoreControl}.installation p
            CROSS JOIN pg_catalog.pg_database d
            JOIN {Control}.tenant_migrations m ON m.tenant_id=@tenant
            JOIN cp6_tenancy_v1.login_tenant l ON l.tenant_id=m.tenant_id AND l.role_oid=m.role_oid AND l.role_name=m.role_name
            JOIN pg_catalog.pg_roles r ON r.oid=l.role_oid AND r.rolname=l.role_name
            WHERE i.singleton AND p.singleton AND i.installation_id=@installation AND i.profile=@model
                AND p.commission_receipt::jsonb->>'InstallationId'=i.installation_id::text
                AND p.commission_receipt::jsonb->>'Profile'=i.profile
                AND m.lineage=@lineage AND m.state='Ready' AND m.migration_id=@migration AND r.rolname::text=@login
                AND d.datname=pg_catalog.current_database() AND NOT pg_catalog.pg_is_in_recovery()
                AND i.target->>'Database'=d.datname::text AND i.target->>'DatabaseOid'=d.oid::text
                AND i.target->>'Server'=pg_catalog.host(pg_catalog.inet_server_addr())
                AND i.target->>'Port'=pg_catalog.inet_server_port()::text
                AND i.target->>'SystemIdentifier'=(SELECT system_identifier::text FROM pg_catalog.pg_control_system())
            FOR SHARE OF i,p,m
            """, connection, tx);
        command.Parameters.AddWithValue("tenant", binding.TenantId);
        command.Parameters.AddWithValue("installation", installation);
        command.Parameters.AddWithValue("model", binding.ModelVersion);
        command.Parameters.AddWithValue("lineage", Lineage);
        command.Parameters.AddWithValue("migration", binding.MigrationVersion);
        command.Parameters.AddWithValue("login", login);
        return await command.ExecuteScalarAsync(ct) as string
            ?? throw new TenantStorageException(TenantStorageFailure.IdentityRejected);
    }

    internal static async Task<string> CoreReadPrimaryAsync(NpgsqlConnection connection, NpgsqlTransaction tx, CancellationToken ct)
    {
        await using var command = new NpgsqlCommand($"""
            SELECT {PrimaryIdentity}
            FROM pg_catalog.pg_database d
            WHERE d.datname=pg_catalog.current_database()
                AND pg_catalog.inet_server_addr() IS NOT NULL AND NOT pg_catalog.pg_is_in_recovery()
            """, connection, tx);
        return await command.ExecuteScalarAsync(ct) as string
            ?? throw new TenantStorageException(TenantStorageFailure.IdentityRejected);
    }
}
