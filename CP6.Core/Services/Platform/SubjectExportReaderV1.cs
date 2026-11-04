using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Entity.DomainModels.Sys;
using Microsoft.EntityFrameworkCore;
namespace CP6.Core.Services.Platform;
internal sealed record SubjectExportCaptureV1(byte[] Bytes,SubjectExportManifestV1 Manifest);
internal static class SubjectExportReaderV1
{
    private static SubjectExportSourceV1 Omitted(string owner,string source)=>new(owner,source,"EXCLUDED_WITH_REASON","OUTSIDE_REQUESTED_SCOPE","NOT_QUERIED",null,null,null,"NOT_CAPTURED",[]);
    internal static async Task<SubjectExportCaptureV1> CaptureAsync(CP6Context db,Guid userId,Guid? expectedTenant,string[] sources,CancellationToken ct)
    {
        var readStartedAtUtc = DateTime.UtcNow;
        var users = db.Sys_Users.IgnoreQueryFilters().AsNoTracking().Where(u => u.Id == userId);
        var user = await (sources.Contains("identity") ? users : users.Select(u => new Sys_User { Id=u.Id,TenantId=u.TenantId })).FirstOrDefaultAsync(ct)
            ?? throw new InvalidOperationException("E-SEC-032");

        if (expectedTenant.HasValue && user.TenantId != expectedTenant.Value) throw new PrivacyExportExceptionV1("PRIVACY_TARGET_BINDING_CHANGED", 412);
        var userReadCompletedAtUtc = DateTime.UtcNow;
        // The selected subject determines scope; the platform operator may belong to another tenant.
        var securityStarted = DateTime.UtcNow;
        var securityLogs = sources.Contains("security") ? await db.Sys_SecurityLogs.IgnoreQueryFilters().AsNoTracking()
            .Where(l => l.TenantId == user.TenantId && l.UserId == user.Id).OrderBy(l => l.CreatedAt).ThenBy(l => l.Id).ToListAsync(ct) : new List<Sys_SecurityLog>();
        var securityCompleted = DateTime.UtcNow;
        var rolesStarted = DateTime.UtcNow;
        var roleAssignments = sources.Contains("roles") ? await db.Sys_UserRoles.IgnoreQueryFilters().AsNoTracking()
            .Where(r => r.TenantId == user.TenantId && r.UserId == user.Id).OrderBy(r => r.RoleId).ThenBy(r => r.Id).ToListAsync(ct) : new List<Sys_UserRole>();
        var readCompletedAtUtc = DateTime.UtcNow;
        var manifest = SubjectExportCatalogV1.Build(db, user.TenantId, user.Id,
            sources.Contains("identity") ? SubjectExportCatalogV1.Included<Sys_User>("MAIN.IDENTITY", 1, readStartedAtUtc, userReadCompletedAtUtc, SubjectExportCatalogV1.UserFields, "STABLE_USER_ID") : Omitted("MAIN.IDENTITY", "Sys_User"),
            sources.Contains("security") ? SubjectExportCatalogV1.Included<Sys_SecurityLog>("MAIN.SECURITY", securityLogs.Count, securityStarted, securityCompleted, SubjectExportCatalogV1.SecurityFields) : Omitted("MAIN.SECURITY", "Sys_SecurityLog"),
            sources.Contains("roles") ? SubjectExportCatalogV1.Included<Sys_UserRole>("MAIN.AUTHORIZATION", roleAssignments.Count, rolesStarted, readCompletedAtUtc, SubjectExportCatalogV1.RoleFields) : Omitted("MAIN.AUTHORIZATION", "Sys_UserRole"));
        var package = new
        {
            user = sources.Contains("identity") ? SubjectExportCatalogV1.Project(user, SubjectExportCatalogV1.UserFields) : null,
            securityLogs = securityLogs.Select(row => SubjectExportCatalogV1.Project(row, SubjectExportCatalogV1.SecurityFields)).ToArray(),
            roleAssignments = roleAssignments.Select(row => SubjectExportCatalogV1.Project(row, SubjectExportCatalogV1.RoleFields)).ToArray(),
            // Historical array retained; a missing stable subject key must never be represented as proven absence.
            operLogs = Array.Empty<Dictionary<string, object?>>(),
            exportedAt = readCompletedAtUtc,
            manifest = JsonSerializer.SerializeToElement(manifest, SubjectExportCatalogV1.JsonOptions),
            scope = new
            {
                version = expectedTenant.HasValue ? "cp6.subject-export-scope.v3" : "cp6.subject-export-scope.v2",
                tenantId = user.TenantId,
                subjectId = user.Id,
                complete = false,
                consistency = "SEPARATE_READS",
                readStartedAtUtc,
                readCompletedAtUtc,
                user = new { status = sources.Contains("identity") ? "INCLUDED" : "EXCLUDED_WITH_REASON", selector = "STABLE_USER_ID" },
                securityLogs = new { status = sources.Contains("security") ? "INCLUDED" : "EXCLUDED_WITH_REASON", selector = "TARGET_TENANT_AND_STABLE_USER_ID" },
                roleAssignments = new { status = sources.Contains("roles") ? "INCLUDED" : "EXCLUDED_WITH_REASON", selector = "TARGET_TENANT_AND_STABLE_USER_ID" },
                operLogs = new { status = "UNKNOWN_SCOPE", reason = "STABLE_SUBJECT_ID_UNAVAILABLE" },
                otherOwners = new { status = "UNKNOWN_SCOPE", reason = "SEE_PER_SOURCE_MANIFEST" },
                fieldFiltering = "OWNER_EXPLICIT_PROJECTION; UNCLASSIFIED_CONTENT_EXCLUDED"
            }
        };

        return new(JsonSerializer.SerializeToUtf8Bytes(package,new JsonSerializerOptions{WriteIndented=true}),manifest);
    }
}
