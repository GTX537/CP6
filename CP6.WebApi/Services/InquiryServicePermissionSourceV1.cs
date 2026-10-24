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

/// <summary>Actual IAM facts for a host-registered service, read under Main's authentication root.
/// It never creates a user/session or accepts role membership from an HTTP request.</summary>
internal static class InquiryServicePermissionSourceV1
{
    internal const string MappingVersion = "MainInquiryServiceIntakeReadFieldsV1";
    internal static async Task<InquiryPermissionFactsV1> ReadAsync(CP6Context db,
        InquiryServicePrincipalRegistryV1.Registration registration, InquiryPermissionRequestV1 request,
        AuthorizationCatalogSnapshotV1 catalog, CancellationToken ct)
    {
        if (db.CurrentTenantId != registration.Tenant || db.Database.CurrentTransaction is null
            || !catalog.IsSealed || !catalog.Fields.TryGetValue(InquiryIntakePermissionSourceV1.Resource, out var definitions)
            || !definitions.Select(x => x.Name).Order(StringComparer.Ordinal)
                .SequenceEqual(InquiryIntakePermissionSourceV1.Fields.Order(StringComparer.Ordinal)))
            throw new CurrentPermissionUnavailableException();
        var identifiers = db.GetService<ISqlGenerationHelper>();
        string Table<T>()
        {
            var entity = db.Model.FindEntityType(typeof(T)) ?? throw new CurrentPermissionUnavailableException();
            return identifiers.DelimitIdentifier(entity.GetTableName()!, entity.GetSchema() ?? "public");
        }
        // Retain raw bpchar bytes for action keys, and all field rows (including unknown or
        // explicitly hidden fields). A missing/invalid source must not become a default grant.
        var sql = $$"""
            WITH roles AS MATERIALIZED (
                SELECT r."RoleId",v.revision
                FROM {{Table<Sys_Role>()}} r
                LEFT JOIN cp6_authentication_v1.identity_admission_revisions v
                    ON v.tenant_id=r."TenantId" AND v.object_key='role:'||r."RoleId"::text
                WHERE r."TenantId"=@tenant AND r."Enable" AND r."RoleId"=ANY(@roles)
            )
            SELECT pg_catalog.jsonb_build_object(
                'Roles',COALESCE((SELECT jsonb_agg(jsonb_build_object('RoleId',r."RoleId",'Revision',r.revision)
                    ORDER BY r."RoleId") FROM roles r),'[]'::jsonb),
                'Actions',COALESCE((SELECT jsonb_agg(
                    pg_catalog.convert_from(pg_catalog.bpcharsend(m."MenuKey"),'UTF8')||':'||
                    pg_catalog.convert_from(pg_catalog.bpcharsend(a."ActionCode"),'UTF8'))
                    FROM {{Table<Sys_RoleAction>()}} a JOIN roles r ON r."RoleId"=a."RoleId"
                    JOIN {{Table<Sys_Menu>()}} m ON m."MenuId"=a."MenuId"
                    WHERE a."TenantId"=@tenant AND m."MenuKey" IS NOT NULL),'[]'::jsonb),
                'Scopes',COALESCE((SELECT jsonb_agg(jsonb_build_object('ScopeType',s."ScopeType",'CustomDeptIds',s."CustomDeptIds"))
                    FROM {{Table<Sys_RoleDataScope>()}} s JOIN roles r ON r."RoleId"=s."RoleId"
                    WHERE s."TenantId"=@tenant AND s."ResourceKey"='crm-lead'),'[]'::jsonb),
                'Fields',COALESCE((SELECT jsonb_agg(jsonb_build_object(
                    'Id',f."Id",'RoleId',f."RoleId",'RoleVersion',r.revision,'Resource',f."ResourceKey",
                    'Field',f."FieldName",'Access',f."Access") ORDER BY f."RoleId",f."Id")
                    FROM {{Table<Sys_RoleFieldPerm>()}} f JOIN roles r ON r."RoleId"=f."RoleId"
                    WHERE f."TenantId"=@tenant AND f."ResourceKey"='crm-intake'),'[]'::jsonb)
            )::text AS "Value"
            """;
        var json = await db.Database.SqlQueryRaw<string>(sql, new NpgsqlParameter("tenant", registration.Tenant),
            new NpgsqlParameter("roles", NpgsqlDbType.Array | NpgsqlDbType.Integer) { Value = registration.Roles.ToArray() }).SingleAsync(ct);
        var snapshot = JsonSerializer.Deserialize<Snapshot>(json) ?? throw new CurrentPermissionUnavailableException();
        if (snapshot.Roles.Any(x => x.Revision is null || x.Revision == Guid.Empty) || snapshot.Scopes.Any(x => x.ScopeType is < 1 or > 5))
            throw new CurrentPermissionUnavailableException();
        string[] required = ["crm-lead:query", "crm-lead:view-pii", "crm-lead:add"];
        if (snapshot.Roles.Length == 0 || required.Append("crm-lead:assign").Any(x => !snapshot.Actions.Contains(x, StringComparer.Ordinal)))
            throw new InquiryPermissionExceptionV1(403, "insufficient_scope");
        var permitted = false;
        foreach (var role in snapshot.Roles)
        {
            var pairs = await IamInquiryScopeAdministrationV1.ReadSourceAsync(db, registration.Tenant, role.RoleId, ct);
            if (pairs.Any(x => x.SiteId == request.SiteId && x.FormId == request.FormId)) { permitted = true; break; }
        }
        if (!permitted) throw new InquiryPermissionExceptionV1(403, "insufficient_scope");
        var organization = await db.Sys_Depts.AsNoTracking().ToDictionaryAsync(x => x.Id, ct);
        var healthy = organization.Values.Where(x => IamUserAdministrationV1.DepartmentAvailable(x.Id, organization)).ToArray();
        var own = healthy.SingleOrDefault(x => x.Id == registration.Department);
        if (own is null) throw new InquiryPermissionExceptionV1(403, "insufficient_scope");
        var scope = snapshot.Scopes.Select(x => x.ScopeType).DefaultIfEmpty(1).Max();
        var custom = snapshot.Scopes.Where(x => x.ScopeType == 4).SelectMany(x => PermissionAggregator.ParseGuids(x.CustomDeptIds)).ToHashSet();
        var departments = healthy.Where(x => scope switch
        {
            2 => x.Id == own.Id, 3 => x.Path.StartsWith(own.Path, StringComparison.Ordinal),
            4 => custom.Contains(x.Id), 5 => true, _ => false
        }).Select(x => x.Id).Order().ToArray();
        if (departments.Length == 0) throw new InquiryPermissionExceptionV1(403, "insufficient_scope");
        if (departments.Length > 256) throw new InquiryPermissionExceptionV1(422, "inquiry_projection_scope_not_representable");
        var source = new FieldPermissionSourceV1(registration.Tenant, registration.Subject, InquiryIntakePermissionSourceV1.Resource,
            AuthenticationPolicyServiceV1.Hash(definitions), AuthenticationPolicyServiceV1.Hash(new { registration.Version, Snapshot = json }),
            definitions.Select(x => x.Name), snapshot.Roles.Select(x => new FieldPermissionRoleV1(x.RoleId, x.Revision!.Value.ToString("D"), true)),
            snapshot.Fields);
        var fields = source.Evaluate(source.Versions(MappingVersion), MappingVersion, [], []);
        if (!fields.IsAvailable) throw new CurrentPermissionUnavailableException();
        if (InquiryIntakePermissionSourceV1.Fields.Any(x => fields.Fields[x].Access is not (1 or 2)))
            throw new InquiryPermissionExceptionV1(403, "insufficient_field_scope");
        return new(required, InquiryIntakePermissionSourceV1.Fields.ToArray(), departments, true);
    }
    private sealed record Role(int RoleId, Guid? Revision);
    private sealed record Scope(int ScopeType, string? CustomDeptIds);
    private sealed record Snapshot(Role[] Roles, string[] Actions, Scope[] Scopes, FieldPermissionRowV1[] Fields);
}
