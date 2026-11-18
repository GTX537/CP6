using System.Security.Claims;
using Microsoft.AspNetCore.Http;
namespace CP6.Core.Services.Sys;
public class CurrentUserAccessor : ICurrentUserAccessor
{
    private readonly IHttpContextAccessor _http;
    public CurrentUserAccessor(IHttpContextAccessor http) => _http = http;
    public Guid? UserId =>
        Guid.TryParse(_http.HttpContext?.User?.FindFirstValue(ClaimTypes.NameIdentifier), out var g) ? g : null;
    public string? UserName => _http.HttpContext?.User?.FindFirstValue(ClaimTypes.Name);
    public Guid? ImpersonatorId => Guid.TryParse(_http.HttpContext?.User?.FindFirstValue("impersonator_id"), out var id) ? id : null;
    public string? AuditPermissionVersion => _http.HttpContext?.Items[AuditEvidenceV1.PermissionVersionItem] as string;
    public AuditEvidenceV1? NativeAuditEvidence => _http.HttpContext?.Items[AuditEvidenceV1.HttpItem] as AuditEvidenceV1;
}
