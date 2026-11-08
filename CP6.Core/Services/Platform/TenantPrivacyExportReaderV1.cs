using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Entity.DomainModels.Sys;
using Microsoft.EntityFrameworkCore;
namespace CP6.Core.Services.Platform;
internal sealed record TenantPrivacyExportCaptureV1(byte[] Bytes,TenantPrivacyExportManifestV1 Manifest);
internal static class TenantPrivacyExportReaderV1
{
    private static readonly string[] TenantFields=["Id","TenantCode","TenantName","Enable","ExpireDate","TimeZoneId"];
    private static SubjectExportSourceV1 Omitted(string owner,string source)=>new(owner,source,"EXCLUDED_WITH_REASON","OUTSIDE_REQUESTED_SCOPE","NOT_QUERIED",null,null,null,"NOT_CAPTURED",[]);
    internal static async Task<TenantPrivacyExportCaptureV1> Capture(CP6Context db,Guid target,string[] sources,CancellationToken ct)
    {
        var started=DateTime.UtcNow;var tenant=await db.Sys_Tenants.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==target,ct)??throw new PrivacyExportExceptionV1("PRIVACY_TENANT_TARGET_NOT_FOUND",404);var tenantAt=DateTime.UtcNow;
        var usersStart=DateTime.UtcNow;var users=sources.Contains("identity")?await db.Sys_Users.IgnoreQueryFilters().AsNoTracking().Where(x=>x.TenantId==target).OrderBy(x=>x.Id).ToListAsync(ct):new List<Sys_User>();var usersAt=DateTime.UtcNow;
        var securityStart=DateTime.UtcNow;var security=sources.Contains("security")?await db.Sys_SecurityLogs.IgnoreQueryFilters().AsNoTracking().Where(x=>x.TenantId==target).OrderBy(x=>x.Id).ToListAsync(ct):new List<Sys_SecurityLog>();var securityAt=DateTime.UtcNow;
        var rolesStart=DateTime.UtcNow;var roles=sources.Contains("roles")?await db.Sys_UserRoles.IgnoreQueryFilters().AsNoTracking().Where(x=>x.TenantId==target).OrderBy(x=>x.Id).ToListAsync(ct):new List<Sys_UserRole>();var rolesAt=DateTime.UtcNow;
        var directory=new List<SubjectExportSourceV1>{
            SubjectExportCatalogV1.Included<Sys_Tenant>("MAIN.TENANT_REGISTRY",1,started,tenantAt,TenantFields,"EXACT_TENANT_ID",true),
            sources.Contains("identity")?SubjectExportCatalogV1.Included<Sys_User>("MAIN.IDENTITY",users.Count,usersStart,usersAt,SubjectExportCatalogV1.UserFields,"TARGET_TENANT_ALL_ROWS",true):Omitted("MAIN.IDENTITY","Sys_User"),
            sources.Contains("security")?SubjectExportCatalogV1.Included<Sys_SecurityLog>("MAIN.SECURITY",security.Count,securityStart,securityAt,SubjectExportCatalogV1.SecurityFields,"TARGET_TENANT_ALL_ROWS",true):Omitted("MAIN.SECURITY","Sys_SecurityLog"),
            sources.Contains("roles")?SubjectExportCatalogV1.Included<Sys_UserRole>("MAIN.AUTHORIZATION",roles.Count,rolesStart,rolesAt,SubjectExportCatalogV1.RoleFields,"TARGET_TENANT_ALL_ROWS",true):Omitted("MAIN.AUTHORIZATION","Sys_UserRole")};
        SubjectExportCatalogV1.AppendRemainder(db,directory,true,typeof(Sys_Tenant));
        var manifest=new TenantPrivacyExportManifestV1("cp6.tenant-export-manifest.v1",target,"PARTIAL",false,"CURRENT_EF_MODEL_AND_NAMED_NON_EF_OWNERS_WITH_EXPLICIT_REMAINDER","SEPARATE_OWNER_READS",directory.ToArray());
        var package=new{tenant=SubjectExportCatalogV1.Project(tenant,TenantFields),users=users.Select(x=>SubjectExportCatalogV1.Project(x,SubjectExportCatalogV1.UserFields)).ToArray(),
            securityLogs=security.Select(x=>SubjectExportCatalogV1.Project(x,SubjectExportCatalogV1.SecurityFields)).ToArray(),roleAssignments=roles.Select(x=>SubjectExportCatalogV1.Project(x,SubjectExportCatalogV1.RoleFields)).ToArray(),
            exportedAt=rolesAt,manifest=JsonSerializer.SerializeToElement(manifest,SubjectExportCatalogV1.JsonOptions),
            scope=new{version="cp6.tenant-export-scope.v1",tenantId=target,complete=false,consistency="SEPARATE_OWNER_READS",readStartedAtUtc=started,readCompletedAtUtc=rolesAt,fieldFiltering="OWNER_EXPLICIT_PROJECTION; UNCLASSIFIED_CONTENT_EXCLUDED"}};
        return new(JsonSerializer.SerializeToUtf8Bytes(package,new JsonSerializerOptions{WriteIndented=true}),manifest);
    }
}
