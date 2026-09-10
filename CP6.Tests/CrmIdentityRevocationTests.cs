using System.IdentityModel.Tokens.Jwt;
using System.Text.Json;
using CP6.Core.Services.CrmIdentity;
using CP6.Tests.Sys;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Primitives;
using Moq;

namespace CP6.Tests;

public class CrmIdentityRevocationTests
{
    [Fact]
    public async Task Issuance_records_the_exact_signed_jti_tenant_and_expiry_before_returning()
    {
        var records = new Mock<ICrmServiceTokenRecordStore>(MockBehavior.Strict);
        (string Issuer, string Client, Guid Tenant, string Jti, DateTimeOffset Expiry)? written = null;
        records.Setup(x => x.RecordAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string>(),
            It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, Guid, string, DateTimeOffset, CancellationToken>((issuer, client, tenant, jti, expiry, _) =>
                written = (issuer, client, tenant, jti, expiry)).Returns(Task.CompletedTask);
        using var f = new CrmOidcServiceTokenTests.Fixture(records: records.Object);
        f.ServiceForm();
        using var body = JsonDocument.Parse(JsonSerializer.Serialize(Assert.IsType<OkObjectResult>(await f.Controller.Token()).Value));
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(body.RootElement.GetProperty("access_token").GetString());
        Assert.NotNull(written);
        Assert.Equal(jwt.Issuer, written.Value.Issuer);
        Assert.Equal(jwt.Id, written.Value.Jti);
        Assert.Equal(jwt.Claims.Single(x => x.Type == "client_id").Value, written.Value.Client);
        Assert.Equal(Guid.Parse(jwt.Claims.Single(x => x.Type == "tenant_id").Value), written.Value.Tenant);
        Assert.Equal(jwt.ValidTo, written.Value.Expiry.UtcDateTime);
    }

    [Fact]
    public async Task Persistence_failure_returns_no_token()
    {
        var records = new Mock<ICrmServiceTokenRecordStore>();
        records.Setup(x => x.RecordAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string>(),
            It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>())).ThrowsAsync(new TimeoutException());
        using var f = new CrmOidcServiceTokenTests.Fixture(records: records.Object);
        f.ServiceForm();
        var result = Assert.IsType<ObjectResult>(await f.Controller.Token());
        Assert.Equal(503, result.StatusCode);
        Assert.DoesNotContain("access_token", JsonSerializer.Serialize(result.Value));
    }

    [Fact]
    public async Task Revocation_passes_only_authenticated_client_and_tenant_and_is_non_enumerating()
    {
        var records = new Mock<ICrmServiceTokenRecordStore>();
        using var f = new CrmOidcServiceTokenTests.Fixture(records: records.Object);
        f.ServiceForm();
        var jti = Guid.NewGuid().ToString("D");
        f.Request.Form = new FormCollection(new Dictionary<string, StringValues> { ["jti"] = jti });
        Assert.IsType<OkResult>(await f.Controller.RevokeServiceToken());
        Assert.IsType<OkResult>(await f.Controller.RevokeServiceToken());
        records.Verify(x => x.RevokeAsync(f.Options.Issuer, "crm-worker", f.TenantId, jti, It.IsAny<CancellationToken>()), Times.Exactly(2));
        Assert.Equal("no-store", f.Controller.Response.Headers.CacheControl);
    }

    [Theory]
    [InlineData("browser-client")]
    [InlineData("missing-basic")]
    [InlineData("tenant-override")]
    [InlineData("duplicate-jti")]
    public async Task Rejected_revocation_requests_never_reach_the_store(string invalid)
    {
        var records = new Mock<ICrmServiceTokenRecordStore>(MockBehavior.Strict);
        using var f = new CrmOidcServiceTokenTests.Fixture(records: records.Object);
        f.ServiceForm();
        var data = new Dictionary<string, StringValues> { ["jti"] = Guid.NewGuid().ToString("D") };
        if (invalid == "browser-client") f.Request.Headers.Authorization = f.Basic("CP6.Web", CrmOidcServiceTokenTests.Fixture.BrowserSecret);
        if (invalid == "missing-basic") f.Request.Headers.Remove("Authorization");
        if (invalid == "tenant-override") data["tenant_id"] = Guid.NewGuid().ToString();
        if (invalid == "duplicate-jti") data["jti"] = new StringValues([Guid.NewGuid().ToString(), Guid.NewGuid().ToString()]);
        f.Request.Form = new FormCollection(data);
        var result = Assert.IsType<ObjectResult>(await f.Controller.RevokeServiceToken());
        Assert.Equal(invalid is "browser-client" or "missing-basic" ? 401 : 400, result.StatusCode);
        records.VerifyNoOtherCalls();
    }
}
