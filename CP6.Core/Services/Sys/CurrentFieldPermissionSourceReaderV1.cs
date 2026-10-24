using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Entity.DomainModels.Sys;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using NpgsqlTypes;

namespace CP6.Core.Services.Sys;

internal sealed class CurrentFieldPermissionSourceReaderV1(
    CP6Context db, IHttpContextAccessor http, Func<AuthorizationCatalogSnapshotV1>? captureCatalog = null)
{
    /// <summary>
    /// One PostgreSQL statement snapshot, participating in the caller's transaction without
    /// changing isolation, retrying or committing it. Repeatable-read remains the caller's old
    /// snapshot; a later effect still needs the Owner's current authorization/fence.
    /// </summary>
    public Task<FieldPermissionSourceV1> ReadAsync(string resource, CancellationToken ct = default)
        => ReadCoreAsync(resource, null, ct);

    // Internal source-only entry for the Main OIDC producer after its actual current Bearer
    // callback and authentication root. No fabricated HttpContext/claims or public actor input.
    // The returned facts still confer no business authorization or commit fence.
    internal Task<FieldPermissionSourceV1> ReadTrustedSubjectAsync(string resource, Guid subject, CancellationToken ct)
    {
        if (subject == Guid.Empty || db.Database.CurrentTransaction is null)
            throw new CurrentPermissionUnavailableException();
        return ReadCoreAsync(resource, subject, ct);
    }

    private async Task<FieldPermissionSourceV1> ReadCoreAsync(string resource, Guid? trustedSubject, CancellationToken ct)
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(ct,
            http.HttpContext?.RequestAborted ?? CancellationToken.None);
        try
        {
            cancellation.Token.ThrowIfCancellationRequested();
            var subject = trustedSubject ?? CurrentPermissionContext.RequireCurrentUser(db, http);
            if (!db.Database.IsNpgsql() || string.IsNullOrWhiteSpace(resource))
                throw new CurrentPermissionUnavailableException();
            var catalog = (captureCatalog ?? AuthorizationCatalogV1.Capture)();
            if (!catalog.IsSealed || !catalog.Fields.TryGetValue(resource, out var definitions))
                throw new CurrentPermissionUnavailableException();
            var identifiers = db.GetService<ISqlGenerationHelper>();
            string Table<T>()
            {
                var entity = db.Model.FindEntityType(typeof(T)) ?? throw new CurrentPermissionUnavailableException();
                return identifiers.DelimitIdentifier(entity.GetTableName()!, entity.GetSchema() ?? "public");
            }

            // Tenant/subject/resource are parameters. Keep stored field names and explicit 1s
            // intact; filtering unregistered fields here would manufacture a permissive default.
            // Existing ordinary SQL writer triggers advance these revisions, including ABA.
            var sql = $$"""
                WITH field_subject AS MATERIALIZED (
                    SELECT u."Id", u."RoleId", u."TenantId", t."AuthenticationPolicyRevision", t."ExpireDate",
                        v.revision AS user_revision
                    FROM {{Table<Sys_User>()}} u
                    JOIN {{Table<Sys_Tenant>()}} t ON t."Id"=u."TenantId"
                    LEFT JOIN cp6_authentication_v1.identity_admission_revisions v
                        ON v.tenant_id=u."TenantId" AND v.object_key='user:'||u."Id"::text
                    WHERE u."TenantId"=@field_tenant AND u."Id"=@field_subject AND u."Enable" AND t."Enable"
                        AND (u."LockedUntil" IS NULL OR u."LockedUntil"<=@field_now)
                        AND (t."ExpireDate" IS NULL OR t."ExpireDate">@field_now)
                ), field_roles AS MATERIALIZED (
                    SELECT r."RoleId", v.revision
                    FROM {{Table<Sys_Role>()}} r
                    LEFT JOIN cp6_authentication_v1.identity_admission_revisions v
                        ON v.tenant_id=r."TenantId" AND v.object_key='role:'||r."RoleId"::text
                    WHERE r."TenantId"=@field_tenant AND r."Enable"
                        AND EXISTS(SELECT 1 FROM field_subject u WHERE r."RoleId"=u."RoleId"
                            OR EXISTS(SELECT 1 FROM {{Table<Sys_UserRole>()}} ur
                                WHERE ur."TenantId"=@field_tenant AND ur."UserId"=u."Id" AND ur."RoleId"=r."RoleId"))
                )
                SELECT pg_catalog.jsonb_build_object(
                    'TenantId',u."TenantId",'UserId',u."Id",'UserRevision',u.user_revision,
                    'PolicyRevision',u."AuthenticationPolicyRevision",'TenantExpiresAt',u."ExpireDate",
                    'Roles',COALESCE((SELECT pg_catalog.jsonb_agg(pg_catalog.jsonb_build_object(
                        'RoleId',r."RoleId",'Revision',r.revision) ORDER BY r."RoleId") FROM field_roles r),'[]'::jsonb),
                    'Rows',COALESCE((SELECT pg_catalog.jsonb_agg(pg_catalog.jsonb_build_object(
                        'Id',f."Id",'RoleId',f."RoleId",'RoleVersion',r.revision,
                        'Resource',f."ResourceKey",'Field',f."FieldName",'Access',f."Access") ORDER BY f."RoleId",f."Id")
                        FROM {{Table<Sys_RoleFieldPerm>()}} f JOIN field_roles r ON r."RoleId"=f."RoleId"
                        WHERE f."TenantId"=@field_tenant AND f."ResourceKey"=@field_resource),'[]'::jsonb)
                )::text AS "Value" FROM field_subject u
                """;
            var json = await db.Database.SqlQueryRaw<string>(sql,
                new NpgsqlParameter("field_tenant", db.CurrentTenantId),
                new NpgsqlParameter("field_subject", subject),
                new NpgsqlParameter("field_resource", resource),
                new NpgsqlParameter("field_now", NpgsqlDbType.Timestamp)
                { Value = DateTime.SpecifyKind(DateTime.Now, DateTimeKind.Unspecified) })
                .SingleOrDefaultAsync(cancellation.Token);
            var snapshot = json is null ? null : JsonSerializer.Deserialize<Snapshot>(json);
            if (snapshot is null || snapshot.TenantId != db.CurrentTenantId || snapshot.UserId != subject
                || snapshot.UserRevision is null || snapshot.UserRevision == Guid.Empty
                || snapshot.PolicyRevision is null || snapshot.PolicyRevision == Guid.Empty
                || snapshot.TenantExpiresAt <= DateTime.Now
                || snapshot.Roles is null || snapshot.Rows is null
                || snapshot.Roles.Any(r => r.Revision is null || r.Revision == Guid.Empty))
                throw new CurrentPermissionUnavailableException();
            var registryVersion = AuthenticationPolicyServiceV1.Hash(new { resource, definitions });
            var sourceVersion = AuthenticationPolicyServiceV1.Hash(snapshot);
            var source = new FieldPermissionSourceV1(snapshot.TenantId, subject, resource, registryVersion,
                sourceVersion, definitions.Select(f => f.Name),
                snapshot.Roles.Select(r => new FieldPermissionRoleV1(r.RoleId, r.Revision!.Value.ToString("D"), true)),
                snapshot.Rows, snapshot.TenantExpiresAt);
            // Validate source structure using the same accepted algorithm; no use is granted by
            // this check. The actual consumer must separately supply Owner-approved mappings.
            if (!source.Evaluate(source.Versions("source-validation-only"), "source-validation-only", [], []).IsAvailable)
                throw new CurrentPermissionUnavailableException();
            return source;
        }
        catch (CurrentPermissionUnavailableException) { throw; }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { throw; }
        catch (Exception error) { throw new CurrentPermissionUnavailableException(error); }
    }

    private sealed record Role(int RoleId, Guid? Revision);
    private sealed record Snapshot(Guid TenantId, Guid UserId, Guid? UserRevision, Guid? PolicyRevision, DateTime? TenantExpiresAt,
        Role[] Roles, FieldPermissionRowV1[] Rows);
}
