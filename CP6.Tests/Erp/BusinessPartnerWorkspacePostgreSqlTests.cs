using System.Net;
using System.Text.Json;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private const string FormalPartnerApi = "/api/main/v1/bp";

    [CurrentPermissionPostgresFact]
    public async Task Business_partner_wp04_01_legacy_without_legal_evidence_is_read_only()
    {
        await using var h = await Seed();
        await GrantBusinessPartnerIdentity(h);
        await GrantPartnerActions(h, "profile-query", "status", "command-result.read");
        var id = await SeedIdentityPartner(h);
        var before = await RetirementPartnerAsync(h, id);

        await using var host = await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!);
        var cookies = await Login(host, h);
        using var get = await host.Send(OperationsRequest(cookies, HttpMethod.Get, $"{FormalPartnerApi}/{id:D}"));
        Assert.Equal(HttpStatusCode.OK, get.StatusCode);
        var detail = await FormalPartnerJson(get);
        Assert.Equal("UNPROVEN", detail.GetProperty("state").GetProperty("legacyScopeState").GetString());
        Assert.Equal(JsonValueKind.Null, detail.GetProperty("bp").GetProperty("legalEntityKey").ValueKind);
        var etag = Assert.IsType<System.Net.Http.Headers.EntityTagHeaderValue>(get.Headers.ETag).ToString();

        using var command = OperationsRequest(cookies, HttpMethod.Post, $"{FormalPartnerApi}/retire",
            new { bpId = id, expectedRevision = "1", reason = "模拟缺法人依据" });
        command.Headers.TryAddWithoutValidation("Idempotency-Key", Guid.NewGuid().ToString("D"));
        command.Headers.TryAddWithoutValidation("If-Match", etag);
        using var denied = await host.Send(command);
        Assert.Equal(HttpStatusCode.Conflict, denied.StatusCode);
        Assert.Contains("BP_LEGAL_SCOPE_UNPROVEN", await denied.Content.ReadAsStringAsync());
        await AssertRetirementUnchanged(h, before);
        await DeviceEvidence("wp04-01-legacy-readonly.json", new
        {
            read = (int)get.StatusCode, retire = (int)denied.StatusCode,
            scope = detail.GetProperty("state").GetProperty("legacyScopeState").GetString(), unchanged = true
        });
    }

    private static async Task<JsonElement> FormalPartnerJson(HttpResponseMessage response)
    {
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.Clone();
    }
}
