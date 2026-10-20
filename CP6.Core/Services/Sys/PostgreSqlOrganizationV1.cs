using CP6.Core.EFDbContext;
using CP6.Entity.DomainModels.Sys;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace CP6.Core.Services.Sys;

/// <summary>A missing, disabled, cyclic or malformed ancestor never becomes a permission root.</summary>
internal static class PostgreSqlOrganizationV1
{
    internal static string Table(CP6Context db)
    {
        var entity=db.Model.FindEntityType(typeof(Sys_Dept))!;
        return db.GetService<ISqlGenerationHelper>().DelimitIdentifier(entity.GetTableName()!,entity.GetSchema()??"public");
    }
    // Legacy PostgreSQL strings use unbounded bpchar. Equality/casts trim trailing spaces;
    // compare its actual stored bytes so a malformed path cannot become an authority root.
    internal static string HealthyCte(string table,string tenant) => $$"""
        healthy_departments AS (
          SELECT d.* FROM {{table}} d WHERE d."TenantId"={{tenant}} AND d."ParentId" IS NULL AND d."Enable"
            AND pg_catalog.convert_from(pg_catalog.bpcharsend(d."Path"),'UTF8')='/'||d."Id"::text||'/'
          UNION ALL
          SELECT d.* FROM {{table}} d JOIN healthy_departments p ON d."ParentId"=p."Id"
          WHERE d."TenantId"={{tenant}} AND d."Enable"
            AND pg_catalog.convert_from(pg_catalog.bpcharsend(d."Path"),'UTF8')=
                pg_catalog.convert_from(pg_catalog.bpcharsend(p."Path"),'UTF8')||d."Id"::text||'/')
        """;
    internal static IQueryable<Sys_Dept> Healthy(CP6Context db) => db.Sys_Depts.FromSqlRaw(
        "WITH RECURSIVE "+HealthyCte(Table(db),"@organization_tenant")+" SELECT * FROM healthy_departments",
        new NpgsqlParameter("organization_tenant",db.CurrentTenantId)).AsNoTracking();
}
