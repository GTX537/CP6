using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using CP6.Core.Services.Wms;
namespace CP6.WebApi.Services;

public sealed class ToolApprovalDeliveryOptionsV1
{
    public bool Enabled { get; set; }
    public string Version { get; set; }="";
    public string DestinationOrigin { get; set; }="";
    public List<ToolApprovalDeliveryClientV1> Clients { get; set; }=[];
}
public sealed class ToolApprovalDeliveryClientV1
{
    public Guid TenantId { get; set; }
    public string ClientId { get; set; }="";
    // Supplied only by the host secret provider. Never included in route receipts or logs.
    public string ClientSecret { get; set; }="";
}
public sealed class ToolApprovalDeliveryRegistryV1
{
    private readonly Dictionary<Guid,(ToolApprovalDeliveryRouteV1 Route,string Secret)> _routes=[];
    private readonly CrmOidcOptions _oidc;
    private readonly ApprovalIntakeRegistryV2 _intake;
    public ToolApprovalDeliveryRegistryV1(ToolApprovalDeliveryOptionsV1 options,CrmOidcOptions oidc,ApprovalIntakeRegistryV2 intake,bool development=false)
    {
        _oidc=oidc;_intake=intake;if(!options.Enabled)return;
        bool Origin(string value)=>Uri.TryCreate(value,UriKind.Absolute,out var u) && u.AbsolutePath=="/" && u.Query=="" && u.Fragment=="" && u.UserInfo==""
            && !value.EndsWith('/') && (u.Scheme=="https" || development && oidc.AllowInsecureLoopback && u.Scheme=="http"
                && IPAddress.TryParse(u.Host,out var ip) && IPAddress.IsLoopback(ip));
        if(!intake.Enabled || !oidc.Enabled || !Origin(oidc.Issuer) || !Origin(options.DestinationOrigin)
            || string.IsNullOrWhiteSpace(options.Version) || options.Version.Length>100 || options.Clients.Count==0)throw new InvalidOperationException("DELIVERY_REGISTRATION_INVALID");
        foreach(var client in options.Clients)
        {
            var registered=oidc.ServiceClients.SingleOrDefault(c=>c.ClientId==client.ClientId);
            if(!intake.Clients.TryGetValue(client.ClientId,out var mapping) || mapping.Tenant!=client.TenantId
                || registered==null || !registered.Enabled || registered.TenantId!=client.TenantId || !registered.AllowedScopes.SequenceEqual(["cp6.services"])
                || string.IsNullOrEmpty(client.ClientSecret) || client.ClientSecret.Length>1024 || !CrmOidcCrypto.EqualsSecret(client.ClientSecret,registered.SecretSha256)
                || _routes.ContainsKey(client.TenantId))throw new InvalidOperationException("DELIVERY_REGISTRATION_INVALID");
            _routes.Add(client.TenantId,(new(intake.Environment,client.TenantId,mapping.Principal,client.ClientId,oidc.Issuer,options.DestinationOrigin,options.Version,intake.EvidenceMode),client.ClientSecret));
        }
    }
    public ToolApprovalDeliveryRouteV1? Route(Guid tenant)
    {
        if(!_routes.TryGetValue(tenant,out var value) || !_intake.Enabled || !_oidc.Enabled)return null;
        var registered=_oidc.ServiceClients.SingleOrDefault(c=>c.ClientId==value.Route.Client);
        return registered!=null && registered.Enabled && registered.TenantId==tenant && registered.AllowedScopes.SequenceEqual(["cp6.services"])
            && CrmOidcCrypto.EqualsSecret(value.Secret,registered.SecretSha256) ? value.Route:null;
    }
    internal string Secret(ToolApprovalDeliveryRouteV1 route)=>Route(route.Tenant)==route?_routes[route.Tenant].Secret:throw new InvalidOperationException("DELIVERY_ROUTE_CHANGED");
}

public sealed class ToolApprovalDeliveryTransportV1 : IToolApprovalDeliveryTransportV1,IDisposable
{
    private readonly ToolApprovalDeliveryRegistryV1 _registry;
    private readonly HttpClient _client;
    public ToolApprovalDeliveryTransportV1(ToolApprovalDeliveryRegistryV1 registry)
        :this(registry,new SocketsHttpHandler { AllowAutoRedirect=false,UseCookies=false,ConnectTimeout=TimeSpan.FromSeconds(10) }) { }
    // Internal injection permits a test to pin its own temporary TLS certificate; the production constructor always validates platform trust.
    internal ToolApprovalDeliveryTransportV1(ToolApprovalDeliveryRegistryV1 registry,HttpMessageHandler handler)
    {
        _registry=registry;
        _client=new(handler){Timeout=TimeSpan.FromSeconds(20)};
    }
    public ToolApprovalDeliveryRouteV1? Route(Guid tenant)=>_registry.Route(tenant);
    private static async Task<byte[]> Bounded(HttpContent content,CancellationToken ct)
    {
        if(content.Headers.ContentLength>1048576)throw new HttpRequestException("DELIVERY_RESPONSE_TOO_LARGE");
        await using var stream=await content.ReadAsStreamAsync(ct);using var result=new MemoryStream();var buffer=new byte[8192];int n;
        while((n=await stream.ReadAsync(buffer,ct))!=0)
        {if(result.Length+n>1048576)throw new HttpRequestException("DELIVERY_RESPONSE_TOO_LARGE");await result.WriteAsync(buffer.AsMemory(0,n),ct);}
        return result.ToArray();
    }
    public Task<ToolApprovalDeliveryResponseV1> SendAsync(ToolApprovalDeliveryRouteV1 route,Guid command,byte[] original,bool query,CancellationToken ct)
        => Exchange(route,"/internal/oa/app/v2/"+(query?"commands/"+command.ToString("D"):"submissions"),original,query,ct);
    internal Task<ToolApprovalDeliveryResponseV1> SendReceiptAsync(ToolApprovalDeliveryRouteV1 route,Guid receiptEvent,byte[] original,bool query,CancellationToken ct)
        => Exchange(route,query?CP6.Core.Services.Oa.ApprovalReceiptIngestionV2.QueryRoute+receiptEvent.ToString("D"):CP6.Core.Services.Oa.ApprovalReceiptIngestionV2.Route,original,query,ct);
    private async Task<ToolApprovalDeliveryResponseV1> Exchange(ToolApprovalDeliveryRouteV1 route,string fixedPath,byte[] original,bool query,CancellationToken ct)
    {
        // Both fixed origins are deployment registrations. A response body/queryRef can never redirect credentials.
        if(Route(route.Tenant)!=route)return new(403,[]);
        using var deadline=CancellationTokenSource.CreateLinkedTokenSource(ct);deadline.CancelAfter(TimeSpan.FromSeconds(20));ct=deadline.Token;
        using var tokenRequest=new HttpRequestMessage(HttpMethod.Post,route.Issuer+"/connect/token");
        var basic=Uri.EscapeDataString(route.Client)+":"+Uri.EscapeDataString(_registry.Secret(route));
        tokenRequest.Headers.Authorization=new("Basic",Convert.ToBase64String(Encoding.UTF8.GetBytes(basic)));
        tokenRequest.Content=new FormUrlEncodedContent(new Dictionary<string,string>{{"grant_type","client_credentials"},{"scope","cp6.services"}});
        using var tokenResponse=await _client.SendAsync(tokenRequest,HttpCompletionOption.ResponseHeadersRead,ct);
        if(tokenResponse.StatusCode!=HttpStatusCode.OK)
            return new((int)tokenResponse.StatusCode is 429 or 503 or 504?(int)tokenResponse.StatusCode:403,[]);
        string token;
        try
        {
            using var document=CP6.Entity.Contracts.Wire.WorkflowCommandJsonV1.Parse(await Bounded(tokenResponse.Content,ct));var root=document.RootElement;
            token=root.GetProperty("access_token").GetString()??"";
            if(root.GetProperty("token_type").GetString()!="Bearer" || root.GetProperty("scope").GetString()!="cp6.services"
                || root.GetProperty("expires_in").GetInt32() is <=0 or >300 || token.Length is <1 or >32768 || token.Any(char.IsWhiteSpace))return new(403,[]);
        }
        catch(Exception e)when(e is JsonException or KeyNotFoundException or InvalidOperationException or FormatException){return new(403,[]);}
        if(Route(route.Tenant)!=route)return new(403,[]);
        using var request=new HttpRequestMessage(query?HttpMethod.Get:HttpMethod.Post,route.Destination+fixedPath);
        request.Headers.Authorization=new AuthenticationHeaderValue("Bearer",token);
        if(!query){request.Content=new ByteArrayContent(original);request.Content.Headers.ContentType=new("application/json");}
        using var response=await _client.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,ct);
        return new((int)response.StatusCode,await Bounded(response.Content,ct));
    }
    public void Dispose()=>_client.Dispose();
}
