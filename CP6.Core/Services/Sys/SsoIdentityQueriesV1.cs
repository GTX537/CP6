using System.Text;
using CP6.Core.EFDbContext;
using CP6.Entity.DomainModels.Sys;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Sys;

/// <summary>OIDC issuer and subject are exact protocol identifiers, independent of the historical user text domain.</summary>
public static class SsoIdentityQueriesV1
{
    public static async Task<Sys_User?> ExactAsync(CP6Context db, Guid tenant, string issuer, string subject, CancellationToken ct = default)
    {
        var issuerBytes = Encoding.UTF8.GetBytes(issuer); var subjectBytes = Encoding.UTF8.GetBytes(subject);
        // bpcharsend preserves stored trailing spaces; casting bpchar to text would discard them.
        var rows = await db.Sys_Users.FromSqlInterpolated($"""
            SELECT * FROM public."Sys_Users" WHERE "TenantId"={tenant}
              AND pg_catalog.bpcharsend("ExternalProvider")={issuerBytes}
              AND pg_catalog.bpcharsend("ExternalSubject")={subjectBytes}
            """).IgnoreQueryFilters().AsNoTracking().Take(2).ToArrayAsync(ct);
        if (rows.Length > 1) throw new InvalidOperationException("SSO_IDENTITY_CONFLICT");
        return rows.SingleOrDefault();
    }
}
