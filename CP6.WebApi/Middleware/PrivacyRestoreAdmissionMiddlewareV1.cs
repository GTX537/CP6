using CP6.Core.EFDbContext;
using CP6.Core.Services.Platform;
using Microsoft.Extensions.Options;
using Npgsql;
namespace CP6.WebApi.Middleware;
public sealed class PrivacyRestoreAdmissionMiddlewareV1(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext http,CP6Context db,IOptions<PrivacyRestoreOptionsV1> options)
    {
        IAsyncDisposable? lease;
        try{lease=await PrivacyRestoreAdmissionV1.AcquireAsync(db,options.Value,http.RequestAborted);}
        catch(Exception e)when(e is PrivacyDispositionExceptionV1 or NpgsqlException or TimeoutException or ArgumentException)
        {
            http.Response.StatusCode=503;http.Response.Headers.CacheControl="no-store";
            await http.Response.WriteAsJsonAsync(new{code=e is PrivacyDispositionExceptionV1?e.Message:"PRIVACY_RESTORE_CONTROL_UNAVAILABLE"},http.RequestAborted);return;
        }
        // Keep the source authority fence until downstream authorization and response serialization complete.
        await using(lease)
        {
            // A verified candidate is still not the independent privacy authority; promotion has its own protocol.
            if(lease!=null&&http.Request.Path.StartsWithSegments("/api/platform/privacy-dispositions-v1"))
            {
                http.Response.StatusCode=409;http.Response.Headers.CacheControl="no-store";
                await http.Response.WriteAsJsonAsync(new{code="PRIVACY_RESTORE_USE_SOURCE_PRIVACY_AUTHORITY"},http.RequestAborted);return;
            }
            await next(http);
        }
    }
}
