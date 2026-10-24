using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Sys;
using CP6.Entity.DomainModels.Sys;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using NpgsqlTypes;

namespace CP6.WebApi.Services;

/// <summary>R3V15 maintenance-only action mapping. Actual registered service and current IAM
/// role facts are required; human supervisor, intake fields and business-row department scope
/// are not maintenance authority. The CRM Owner must still check each row deadline and hold.</summary>
internal static class InquiryRetentionPermissionSourceV1
{
    internal const string Purpose = "privacy:retention-worker";
    internal const string Action = "crm-privacy:retention-run";
    internal static async Task<InquiryPermissionFactsV1> ReadAsync(CP6Context db,
        InquiryServicePrincipalRegistryV1.Registration registration, CancellationToken ct)
    {
        if (db.CurrentTenantId != registration.Tenant || db.Database.CurrentTransaction is null)
            throw new CurrentPermissionUnavailableException();
        var identifiers = db.GetService<ISqlGenerationHelper>();
        string Table<T>()
        {
            var entity = db.Model.FindEntityType(typeof(T)) ?? throw new CurrentPermissionUnavailableException();
            return identifiers.DelimitIdentifier(entity.GetTableName()!, entity.GetSchema() ?? "public");
        }
        // bpchar source bytes must match exactly; padded or case-changed action keys never grant.
        var sql = $$"""
            WITH roles AS MATERIALIZED (
                SELECT r."RoleId", v.revision
                FROM {{Table<Sys_Role>()}} r
                LEFT JOIN cp6_authentication_v1.identity_admission_revisions v
                  ON v.tenant_id=r."TenantId" AND v.object_key='role:'||r."RoleId"::text
                WHERE r."TenantId"=@tenant AND r."Enable" AND r."RoleId"=ANY(@roles)
            )
            SELECT jsonb_build_object(
                'InvalidSource',EXISTS(SELECT 1 FROM roles WHERE revision IS NULL OR revision='00000000-0000-0000-0000-000000000000'::uuid),
                'Granted',EXISTS(SELECT 1 FROM {{Table<Sys_RoleAction>()}} a
                    JOIN roles r ON r."RoleId"=a."RoleId"
                    JOIN {{Table<Sys_Menu>()}} m ON m."MenuId"=a."MenuId"
                    WHERE a."TenantId"=@tenant
                      AND convert_from(bpcharsend(m."MenuKey"),'UTF8')='crm-privacy'
                      AND convert_from(bpcharsend(a."ActionCode"),'UTF8')='retention-run'))::text AS "Value"
            """;
        var raw = await db.Database.SqlQueryRaw<string>(sql,new NpgsqlParameter("tenant",registration.Tenant),
            new NpgsqlParameter("roles",NpgsqlDbType.Array|NpgsqlDbType.Integer){Value=registration.Roles.ToArray()}).SingleAsync(ct);
        var facts = JsonSerializer.Deserialize<Source>(raw) ?? throw new CurrentPermissionUnavailableException();
        if (facts.InvalidSource) throw new CurrentPermissionUnavailableException();
        if (!facts.Granted) throw new InquiryPermissionExceptionV1(403,"insufficient_scope");
        // The service's registered department remains an identity dependency, not a row scope.
        var departments = await db.Sys_Depts.AsNoTracking().ToDictionaryAsync(x=>x.Id,ct);
        if (!IamUserAdministrationV1.DepartmentAvailable(registration.Department,departments))
            throw new InquiryPermissionExceptionV1(403,"insufficient_scope");
        return new([Action],[],[],false);
    }
    private sealed record Source(bool InvalidSource,bool Granted);
}
