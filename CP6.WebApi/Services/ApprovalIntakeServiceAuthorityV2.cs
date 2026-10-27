using System.Security.Claims;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Common;
using CP6.Core.Services.Oa;
using CP6.Core.Services.Sys;
using CP6.WebApi.Controllers.Internal;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
namespace CP6.WebApi.Services;

public sealed class ApprovalIntakeOptionsV2
{
    public bool Enabled { get; set; }
    public string Version { get; set; }="";
    public Guid EnvironmentId { get; set; }
    public string EvidenceMode { get; set; }="REAL";
    public List<ApprovalIntakeClientV2> Clients { get; set; }=[];
}
public sealed class ApprovalIntakeClientV2
{
    public string ClientId { get; set; }="";
    public Guid TenantId { get; set; }
    public Guid PrincipalId { get; set; }
    public bool CanSubmit { get; set; }
}
// Deployment mapping only; credentials remain in the existing registered C02 service mechanism.
// Enabled controls this pending-intake feature, not the still-unadopted TxE/TxD/business runtime.
public sealed class ApprovalIntakeRegistryV2
{
    internal sealed record Client(string Id,Guid Tenant,Guid Principal,bool CanSubmit);
    internal readonly bool Enabled;
    internal readonly string Version;
    internal readonly Guid Environment;
    internal readonly string EvidenceMode;
    internal readonly IReadOnlyDictionary<string,Client> Clients;
    public ApprovalIntakeRegistryV2(ApprovalIntakeOptionsV2 options,CrmOidcOptions oidc,bool development=false)
    {
        Enabled=options.Enabled;Version=options.Version;Environment=options.EnvironmentId;EvidenceMode=options.EvidenceMode;
        Clients=options.Clients.ToDictionary(x=>x.ClientId,x=>new Client(x.ClientId,x.TenantId,x.PrincipalId,x.CanSubmit),StringComparer.Ordinal);
        if(!Enabled)return;
        if(!oidc.Enabled||Environment==Guid.Empty||string.IsNullOrWhiteSpace(Version)||Version.Length>100||Clients.Count==0
            ||EvidenceMode is not("REAL" or "DEMO")||EvidenceMode=="DEMO"&&!development
            ||Clients.Values.Select(x=>x.Principal).Distinct().Count()!=Clients.Count
            ||Clients.Values.Any(x=>x.Tenant==Guid.Empty||x.Principal==Guid.Empty||!oidc.ServiceClients.Any(c=>c.ClientId==x.Id&&c.TenantId==x.Tenant&&c.Enabled&&c.AllowedScopes.SequenceEqual(["cp6.services"]))))
            throw new InvalidOperationException("APP_SERVICE_REGISTRATION_INVALID");
    }
}

public sealed class ApprovalIntakeServiceAuthorityV2(CP6Context db,IHttpContextAccessor http,ApprovalIntakeRegistryV2 registry,CrmOidcOptions oidc)
    : IApprovalServiceAuthorityV2
{
    internal sealed record Facts(ApprovalIntakeRegistryV2.Client Client,Guid Jti,DateTimeOffset Expires);
    private static string? Single(ClaimsPrincipal p,string type)=>p.FindAll(type).ToArray() is {Length:1} claims?claims[0].Value:null;
    internal static async Task<Facts> Authenticate(HttpContext http,ApprovalIntakeRegistryV2 registry,CrmOidcOptions oidc,CancellationToken ct)
    {
        if(!registry.Enabled||await http.RequestServices.GetRequiredService<IAuthenticationSchemeProvider>().GetSchemeAsync(CrmIdentityController.Scheme)==null)
            throw new ScopeAuthorizationExceptionV1("OWNER_UNAVAILABLE",503);
        // Plain external HTTP never carries service credentials into this consumer. The existing
        // explicit development loopback option is the only local-test exception; forwarded headers alone do not authorize it.
        if(!http.Request.IsHttps&&!(oidc.AllowInsecureLoopback&&http.Connection.RemoteIpAddress is {} address&&System.Net.IPAddress.IsLoopback(address)))
            throw new ScopeAuthorizationExceptionV1("PRODUCER_NOT_ALLOWED",403);
        var result=await http.AuthenticateAsync(CrmIdentityController.Scheme);ct.ThrowIfCancellationRequested();
        var p=result.Principal;if(!result.Succeeded||p==null)throw new ScopeAuthorizationExceptionV1("AUTHENTICATION_REQUIRED",401);
        var id=Single(p,"client_id");var tenant=Single(p,"tenant_id");var token=Single(p,"jti");
        if(id==null||!registry.Clients.TryGetValue(id,out var client)||Single(p,"iss")!=oidc.Issuer||Single(p,"sub")!="service:"+id||Single(p,"scope")!="cp6.services"
            ||Single(p,"aud")!="CP6.Services"||tenant!=client.Tenant.ToString("D")||!Guid.TryParseExact(token,"D",out var jti)||jti==Guid.Empty||token!=jti.ToString("D")
            ||!long.TryParse(Single(p,"exp"),out var exp)||exp<=DateTimeOffset.UtcNow.ToUnixTimeSeconds())throw new ScopeAuthorizationExceptionV1("PRODUCER_NOT_ALLOWED",403);
        try{return new(client,jti,DateTimeOffset.FromUnixTimeSeconds(exp));}catch(ArgumentOutOfRangeException){throw new ScopeAuthorizationExceptionV1("PRODUCER_NOT_ALLOWED",403);}
    }
    internal static async Task BindTenant(HttpContext http,CancellationToken ct)
    {
        var registry=http.RequestServices.GetRequiredService<ApprovalIntakeRegistryV2>();var oidc=http.RequestServices.GetRequiredService<CrmOidcOptions>();
        var facts=await Authenticate(http,registry,oidc,ct);
        // Resolve the business DbContext only after this authenticated, registered mapping; never from body/header tenant input.
        http.RequestServices.GetRequiredService<ITenantContext>().CurrentTenantId=facts.Client.Tenant;
    }
    public async Task<ApprovalServiceIdentityV2> CurrentAsync(CancellationToken ct)
    {
        if(db.Database.CurrentTransaction==null||http.HttpContext==null)throw new ScopeAuthorizationExceptionV1("AUTHORITY_UNAVAILABLE",503);
        var facts=await Authenticate(http.HttpContext,registry,oidc,ct);var client=facts.Client;
        if(db.CurrentTenantId!=client.Tenant||!oidc.ServiceClients.Any(x=>x.ClientId==client.Id&&x.TenantId==client.Tenant&&x.Enabled&&x.AllowedScopes.SequenceEqual(["cp6.services"])))
            throw new ScopeAuthorizationExceptionV1("PRODUCER_NOT_ALLOWED",403);
        var token=facts.Jti.ToString("D");
        var locked=await db.Database.SqlQuery<Guid>($"""
            SELECT "TenantId" AS "Value" FROM crm_identity."ServiceToken"
            WHERE "Issuer"={oidc.Issuer} AND "Jti"={token} AND "TenantId"={client.Tenant} AND "ClientId"={client.Id}
              AND "RevokedAtUtc" IS NULL AND "ExpiresAtUtc">clock_timestamp() FOR SHARE
            """).ToArrayAsync(ct);
        if(locked.Length!=1||!await db.Sys_Tenants.AsNoTracking().AnyAsync(t=>t.Id==client.Tenant&&t.Enable&&(t.ExpireDate==null||t.ExpireDate>DateTime.Now),ct)
            ||facts.Expires<=DateTimeOffset.UtcNow)throw new ScopeAuthorizationExceptionV1("PRODUCER_NOT_ALLOWED",403);
        return new(registry.Environment,client.Tenant,client.Principal,client.Id,"CP6.Services",registry.Version,client.CanSubmit,registry.EvidenceMode);
    }
}
