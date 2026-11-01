using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;
using static CP6.Core.Services.Wms.RmaFormalStoreV1;

namespace CP6.Core.Services.Wms;

public sealed class RmaCommercialHttpGatewayV1(HttpClient http, IOptions<RmaCommercialGatewayOptionsV1> options) : IRmaCommercialGatewayV1
{
    public async Task<RmaCommercialObservationV1> ObserveAsync(RmaCommercialOwnerRequestV1 request, bool submit, CancellationToken ct)
    {
        if (!Uri.TryCreate(options.Value.BaseUrl,UriKind.Absolute,out var root) || root.Scheme is not ("http" or "https")
            || !root.IsLoopback && (root.Scheme!="https" || string.IsNullOrWhiteSpace(options.Value.BearerToken)))
            throw Error("RMA_COMMERCIAL_OWNER_NOT_CONFIGURED",503);
        using var message = new HttpRequestMessage(HttpMethod.Post,new Uri(root,submit ? "requests" : "queries"));
        message.Headers.Add("Idempotency-Key",request.Request.TenantId.ToString("N")+":"+request.Request.RequestId.ToString("N"));
        if(!string.IsNullOrWhiteSpace(options.Value.BearerToken)) message.Headers.Authorization=new("Bearer",options.Value.BearerToken);
        message.Content = JsonContent.Create(request,options:RmaFormalStoreV1.Json);
        try
        {
            using var response = await http.SendAsync(message,ct);
            if (!response.IsSuccessStatusCode) return Unknown(request,"OWNER_HTTP_"+(int)response.StatusCode);
            var observation = await response.Content.ReadFromJsonAsync<RmaCommercialObservationV1>(RmaFormalStoreV1.Json,ct)
                ?? throw Error("RMA_COMMERCIAL_OWNER_RESPONSE_INVALID",502);
            RmaCommercialBoundaryV1.Validate(request,observation);
            return observation;
        }
        catch (HttpRequestException) { return Unknown(request,"OWNER_TRANSPORT_UNKNOWN"); }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { return Unknown(request,"OWNER_TIMEOUT_UNKNOWN"); }
        catch (JsonException) { throw Error("RMA_COMMERCIAL_OWNER_RESPONSE_INVALID",502); }
    }
    private static RmaCommercialObservationV1 Unknown(RmaCommercialOwnerRequestV1 r,string reason)
        => new(r.Request.TenantId,r.RequestRef,"UNKNOWN",null,null,DateTimeOffset.UtcNow,[reason],"UNKNOWN");
}
