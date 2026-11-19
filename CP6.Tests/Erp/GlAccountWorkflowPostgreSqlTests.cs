using System.Net;
using System.Text.Json;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private const string GlOwnerRoute = "api/fin/account/owner-v1";
    private static readonly DateOnly GlBusinessDay = new(2026, 11, 19);

    [CurrentPermissionPostgresFact]
    public async Task Gl_owner_options_require_current_registered_book_without_assuming_a_scheme()
    {
        await using var h = await Seed();
        await GrantLegacyAuthorityAction(h, "fin-account", "view");
        var book = Guid.NewGuid();
        var environment = ScopeEnvironment.ToString("D");
        await h.Db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO cp6_fin_mfg_v1.ledgers(tenant_id,id,environment_id,mode,legal_entity_key,ledger_key,currency,business_date)
            VALUES ({h.Tenant},{book},{environment},'REAL','SIM-LE','SIM-GL','CNY',{GlBusinessDay});
            INSERT INTO cp6_fin_mfg_v1.members(tenant_id,ledger_id,subject_id) VALUES ({h.Tenant},{book},{h.User})
            """);
        await using var host = await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!,
            new Dictionary<string, string> { ["FinanceCosting__EnvironmentId"] = environment });
        var cookies = await Login(host, h);
        using var response = await host.Send(OperationsRequest(cookies, HttpMethod.Get, GlOwnerRoute + "/options"));
        var result = await GlJson(response, HttpStatusCode.OK);
        Assert.Empty(result.GetProperty("choices").EnumerateArray());
    }

    private static async Task<JsonElement> GlJson(HttpResponseMessage response, HttpStatusCode status)
    {
        Assert.True(response.StatusCode == status,
            $"预期 {(int)status}，实际 {(int)response.StatusCode}，{await OperationsFailure(response)}");
        using var body = JsonDocument.Parse(await response.Content.ReadAsByteArrayAsync());
        return body.RootElement.Clone();
    }
}
