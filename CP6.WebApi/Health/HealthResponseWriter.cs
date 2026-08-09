using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace CP6.WebApi.Health;

internal static class HealthResponseWriter
{
    public static Task WriteAsync(HttpContext context, HealthReport report)
    {
        context.Response.ContentType = "application/json; charset=utf-8";
        context.Response.Headers.CacheControl = "no-store";

        return context.Response.WriteAsJsonAsync(new
        {
            status = report.Status.ToString(),
            checks = report.Entries
                .OrderBy(entry => entry.Key, StringComparer.Ordinal)
                .Select(entry => new
                {
                    name = entry.Key,
                    status = entry.Value.Status.ToString()
                })
        });
    }
}
