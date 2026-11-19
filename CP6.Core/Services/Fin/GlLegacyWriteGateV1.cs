using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Fin;

/// <summary>旧表只有租户和方案。该范围一旦启用版本账套，旧表保留作历史来源。</summary>
public static class GlLegacyWriteGateV1
{
    public const string Required = "GL_VERSION_WORKFLOW_REQUIRED";

    public static IQueryable<string> VersionedSchemes(CP6Context db, Guid tenant) => db.Database.SqlQuery<string>($"""
        SELECT scheme AS "Value" FROM cp6_fin_gl_v1.books WHERE tenant_id={tenant}
        """);

    public static async Task RequireUnversioned(CP6Context db, Guid tenant, string? scheme)
    {
        if (!db.Database.IsNpgsql() || string.IsNullOrWhiteSpace(scheme)) return;
        var normalized = scheme.Trim().ToUpperInvariant();
        if (await VersionedSchemes(db, tenant).AnyAsync(value => value == normalized))
            throw new InvalidOperationException(Required);
    }
}
