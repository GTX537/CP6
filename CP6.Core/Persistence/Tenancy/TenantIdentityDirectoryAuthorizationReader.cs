using CP6.Core.Services.CrmIdentity;
using Npgsql;
using NpgsqlTypes;

namespace CP6.Core.Persistence.Tenancy;

/// <summary>Host-only current authorization facts. The host must authenticate and bind issuer/client/jti
/// before calling. One SQL snapshot checks tenant and recorded token; this is not an Owner write fence.</summary>
internal sealed class TenantIdentityDirectoryAuthorizationReader(TenantConnectionManager manager,
    IdentityDirectoryDatabaseIdentity? installedIdentity = null)
{
    internal async Task<bool> IsAuthorizedAsync(Guid tenant, string issuer, string client, Guid jti,
        DateTimeOffset now, CancellationToken ct = default)
    {
        if (tenant == Guid.Empty || jti == Guid.Empty || string.IsNullOrWhiteSpace(issuer) || issuer.Length > 512 ||
            string.IsNullOrWhiteSpace(client) || client.Length > 128) return false;
        try
        {
            await using var lease = await manager.OpenCurrentAsync(ct).ConfigureAwait(false);
            var binding = lease.CurrentBinding;
            if (binding.TenantId != tenant || binding.ModelVersion != IdentityDirectoryAuthorizationV2.ModelVersion ||
                binding.MigrationVersion != IdentityDirectoryAuthorizationV2.MigrationVersion || binding.QueueDomainRefs.Count != 0)
                throw new TenantStorageException(TenantStorageFailure.BindingRejected);
            return await lease.InTransactionAsync(async (transaction, token) =>
            {
                var authorized = await transaction.ExecuteAsync(async (connection, tx, cancellation) =>
                {
                    using var guard = new TenantEfCommandGuard(transaction, connection, tx, tenant);
                    // Validate and pin the protected LOGIN mapping before the authorization SQL snapshot.
                    await guard.ValidateCurrentAsync(cancellation).ConfigureAwait(false);
                    if (installedIdentity is not null)
                        await installedIdentity.ValidateAsync(connection, tx, cancellation).ConfigureAwait(false);
                    await using var command = new NpgsqlCommand("""
                        SELECT EXISTS(SELECT FROM public."Sys_Tenants" t
                          JOIN crm_identity."ServiceToken" s ON s."TenantId"=t."Id"
                          WHERE t."Id"=@tenant AND t."Enable" AND (t."ExpireDate" IS NULL OR t."ExpireDate">@localNow)
                            AND s."Issuer"=@issuer AND s."ClientId"=@client AND s."Jti"=@jti
                            AND s."RevokedAtUtc" IS NULL AND s."ExpiresAtUtc">@skewBoundary)
                        """, connection, tx);
                    command.Parameters.AddWithValue("tenant", tenant); command.Parameters.AddWithValue("issuer", issuer);
                    command.Parameters.AddWithValue("client", client); command.Parameters.AddWithValue("jti", jti.ToString("D"));
                    // Preserve the existing Sys_Tenants wall-clock contract and service token's 60s skew.
                    command.Parameters.AddWithValue("localNow", NpgsqlDbType.Timestamp, DateTime.SpecifyKind(now.LocalDateTime, DateTimeKind.Unspecified));
                    command.Parameters.AddWithValue("skewBoundary", now.ToUniversalTime().AddSeconds(-60));
                    return await command.ExecuteScalarAsync(cancellation).ConfigureAwait(false) is true;
                }, token).ConfigureAwait(false);
                await transaction.CommitAsync(token).ConfigureAwait(false);
                return authorized;
            }, cancellationToken: ct).ConfigureAwait(false);
        }
        catch (TenantStorageException) { throw new IdentityReadException("C02_IDENTITY_STORE_UNAVAILABLE"); }
    }
}
