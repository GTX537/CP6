using CP6.Core.EFDbContext;
using CP6.Entity.DomainModels.Sys;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.CrmIdentity;

public interface ICrmServiceTokenRecordStore
{
    Task RecordAsync(string issuer, string clientId, Guid tenantId, string jti, DateTimeOffset expiry, CancellationToken cancellationToken);
    Task RevokeAsync(string issuer, string clientId, Guid tenantId, string jti, CancellationToken cancellationToken);
}

public sealed class CrmServiceTokenRecordStore(CP6Context db, CrmIdentityRuntime runtime) : ICrmServiceTokenRecordStore
{
    public async Task RecordAsync(string issuer, string clientId, Guid tenantId, string jti,
        DateTimeOffset expiry, CancellationToken cancellationToken)
    {
        if (!runtime.Options.Tenants.ContainsKey(tenantId)) return;
        if (issuer != runtime.Options.Issuer) throw new InvalidOperationException("C02_SERVICE_ISSUER_MISMATCH");
        db.CrmServiceTokenRecords.Add(new CrmServiceTokenRecord
        {
            Issuer = issuer, ClientId = clientId, TenantId = tenantId, Jti = jti, ExpiresAtUtc = expiry
        });
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task RevokeAsync(string issuer, string clientId, Guid tenantId, string jti, CancellationToken cancellationToken)
    {
        if (!runtime.Options.Tenants.ContainsKey(tenantId)) return;
        if (issuer != runtime.Options.Issuer) throw new InvalidOperationException("C02_SERVICE_ISSUER_MISMATCH");
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var record = await db.CrmServiceTokenRecords.FromSqlInterpolated($"SELECT * FROM crm_identity.ServiceToken WITH (UPDLOCK,HOLDLOCK) WHERE Issuer={issuer} AND Jti={jti} AND ClientId={clientId} AND TenantId={tenantId}")
            .SingleOrDefaultAsync(cancellationToken);
        if (record is not null && record.RevokedAtUtc is null)
        {
            record.RevokedAtUtc = runtime.Clock.GetUtcNow();
            await new IdentitySnapshotWriter(db, runtime).RevokeTokenAsync(tenantId, jti,
                "service:" + clientId, record.ExpiresAtUtc, cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
    }
}
