using System.Net;
using System.Text.Json;
using CP6.Core.Services.Wms;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;
namespace CP6.Tests.Erp;
public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    [CurrentPermissionPostgresFact]
    public async Task WarehouseClock_actual_Program_HTTP_policy_source_strict_input_CSRF_exact_time_and_original_recovery()
    {
        await using var h = await Seed(); var f = await ClockSetup(h);
        var settings = new Dictionary<string, string> { ["ScopeAuthorization__EnvironmentId"] = ScopeEnvironment.ToString(),
            ["WarehouseClock__Enabled"] = "true", ["WarehouseClock__Tenants__0"] = h.Tenant.ToString() };
        foreach (var prop in JsonSerializer.SerializeToElement(f.Options.Authority).EnumerateObject())
            settings["WarehouseClock__Authority__" + prop.Name] = prop.Value.ToString();
        await using var host = await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!, settings);
        var cookies = await Login(host, h); var root = $"api/wms/warehouses/{f.Warehouse}/clock-policy-v1";
        async Task<HttpResponseMessage> Save(object command, bool csrf = true)
        { using var request = OperationsRequest(cookies, HttpMethod.Post, root + "/commands", command); if (!csrf) request.Headers.Remove("X-CSRF-Token"); return await host.Send(request); }
        using (var response = await host.Send(OperationsRequest(cookies, HttpMethod.Get, root)))
        { var c = await DocumentHttpData<WarehouseClockContextV1>(response); Assert.NotNull(c.Source); Assert.False(c.AdmissionGrant); }
        var forged = JsonSerializer.SerializeToNode(f.Command, new JsonSerializerOptions(JsonSerializerDefaults.Web))!.AsObject(); forged["trustedNow"] = DateTimeOffset.UtcNow.ToString("O");
        using (var bad = await Save(forged)) Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
        using (var denied = await Save(f.Command, false)) Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        using (var saved = await Save(f.Command)) { Assert.True(saved.Headers.CacheControl?.NoStore); await DocumentHttpData<WarehouseClockResultV1>(saved); }
        var path = $"api/location-rule/owner-v1/locations/{f.Location}/proposals/{f.Candidate.Reference.Id}/time-evaluation";
        async Task<HttpResponseMessage> Evaluate(object input, string? etag)
        { using var req = OperationsRequest(cookies, HttpMethod.Post, path, input); if (etag != null) req.Headers.TryAddWithoutValidation("If-Match", etag); return await host.Send(req); }
        var command = new RuleTimeRequestV1(f.Candidate.Reference, "CURRENT", null);
        using (var missing = await Evaluate(command, null)) Assert.Equal((HttpStatusCode)428, missing.StatusCode);
        using (var stale = await Evaluate(command, "\"stale\"")) Assert.Equal(HttpStatusCode.PreconditionFailed, stale.StatusCode);
        using (var evaluated = await Evaluate(command, f.Candidate.Etag))
        { var result = await DocumentHttpData<RuleTimeAssessmentV1>(evaluated); Assert.Equal("TIME_SATISFIED", result.Outcome); Assert.False(result.AdmissionGrant); Assert.NotNull(result.Clock); }
        await RevokeTransferAction(h, "wms-warehouse", "clock-policy"); using (var recovery = await Save(f.Command)) Assert.Equal(HttpStatusCode.OK, recovery.StatusCode);
        using (var original = await host.Send(OperationsRequest(cookies, HttpMethod.Get, root + "/operations/" + f.Command.OperationId))) Assert.Equal("COMMITTED", (await DocumentHttpData<WarehouseClockOperationV1>(original)).State);
        await RevokeTransferAction(h, "wms-warehouse", "query"); using var hidden = await host.Send(OperationsRequest(cookies, HttpMethod.Get, root + "/operations/" + f.Command.OperationId)); Assert.Equal(HttpStatusCode.Forbidden, hidden.StatusCode);
    }
}
