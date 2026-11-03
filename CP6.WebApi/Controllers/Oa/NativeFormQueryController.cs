using System.Text.Json;
using CP6.Core.Services.Oa;
using CP6.Core.Services.Sys;
using CP6.Entity.Contracts.Wire;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CP6.WebApi.Controllers.Oa;
[ApiController, Authorize, Route("api/oa/v2")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class NativeFormQueryController(NativeInboxOwnerV2 owner) : ControllerBase
{
    [HttpGet("query/context")]
    public Task<IActionResult> Context(CancellationToken ct) => Run(async () => await owner.QueryContextAsync(ct));
    [HttpPost("query/search"), RequestSizeLimit(16384)]
    public Task<IActionResult> Search(CancellationToken ct) => Run(async () =>
    {
        using var bytes = new MemoryStream(); var buffer = new byte[4096]; int count;
        while ((count = await Request.Body.ReadAsync(buffer, ct)) != 0)
        { if (bytes.Length + count > 16384) throw new ScopeAuthorizationExceptionV1("QUERY_FILTER_INVALID", 422); await bytes.WriteAsync(buffer.AsMemory(0, count), ct); }
        NativeFormQueryV2 filter;
        try
        {
            using var json = WorkflowCommandJsonV1.Parse(bytes.ToArray());
            filter = json.RootElement.Deserialize<NativeFormQueryV2>(new JsonSerializerOptions(JsonSerializerDefaults.Web) { PropertyNameCaseInsensitive = false }) ?? throw new JsonException();
        }
        catch (JsonException e)
        { throw new ScopeAuthorizationExceptionV1("QUERY_FILTER_INVALID", 422, e); }
        return await owner.SearchAsync(filter, ActingAs(), ct);
    });
    [HttpGet("query/instances/{id:guid}")]
    public Task<IActionResult> Instance(Guid id, CancellationToken ct) => Run(async () => await owner.QueryInstanceAsync(id, ActingAs(), ct));
    [HttpGet("form-data/{id:guid}")]
    public Task<IActionResult> FormData(Guid id, CancellationToken ct) => Run(async () => await owner.QueryFormDataAsync(id, ActingAs(), ct));
    private Guid? ActingAs()
    {
        var value = Request.Headers["X-Acting-As"].ToString(); if (value.Length == 0) return null;
        return Guid.TryParseExact(value, "D", out var id) && id != Guid.Empty ? id : throw new ScopeAuthorizationExceptionV1("ACT_AS_INVALID", 422);
    }
    private async Task<IActionResult> Run(Func<Task<object>> action)
    {
        try { return Ok(new { code = 0, data = await action() }); }
        catch (ScopeAuthorizationExceptionV1 e) { return StatusCode(e.Status, new { code = e.Message, messageKey = e.Message }); }
        catch (Exception e) when (e is Npgsql.NpgsqlException or Microsoft.EntityFrameworkCore.DbUpdateException or CurrentPermissionUnavailableException)
        { return StatusCode(503, new { code = "QUERY_STORAGE_UNAVAILABLE" }); }
    }
}
