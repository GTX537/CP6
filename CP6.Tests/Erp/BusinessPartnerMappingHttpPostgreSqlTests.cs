using System.Net;
using System.Text.Json;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    [CurrentPermissionPostgresFact]
    public async Task Business_partner_wp04_03_unadopted_account_source_is_unproven()
    {
        await using var database = await Seed();
        await ProvisionWorkspace(database);
        await GrantPartnerActions(database, "mapping", "mapping.read");
        await using var host = await AuthenticationApiProcess.Start(database.Db.Database.GetConnectionString()!);
        var cookies = await Login(host, database);
        using var request = OperationsRequest(cookies, HttpMethod.Post, FormalPartnerApi + "/account-probes",
            new { legalEntityKey = "JP-A", accountId = Guid.NewGuid() });
        using var response = await host.Send(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await FormalPartnerJson(response);
        Assert.Equal("UNPROVEN", body.GetProperty("availability").GetString());
        Assert.Equal(JsonValueKind.Null, body.GetProperty("observation").ValueKind);
    }
}
