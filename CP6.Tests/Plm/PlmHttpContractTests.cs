using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using CP6.Core.Services.Common;
using CP6.Core.Services.Plm;
using CP6.Entity.DTOs.Plm;
using CP6.Tests.Infra;
using CP6.WebApi.Controllers.Plm;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.SqlClient;

namespace CP6.Tests.Plm;

[Collection(PlmSqlServerCollection.Name)]
public sealed class PlmHttpContractTests(PlmSqlServerFixture fixture)
{
    private async Task<(WebApplication App, HttpClient Client)> StartAsync()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Development" });
        builder.WebHost.UseKestrel().UseUrls("http://127.0.0.1:0");
        builder.Services.AddAuthentication("WP0Test").AddScheme<AuthenticationSchemeOptions, PlmTestAuthHandler>("WP0Test", _ => { });
        builder.Services.AddAuthorization();
        builder.Services.AddControllers().AddApplicationPart(typeof(PlmEngineeringController).Assembly);
        builder.Services.AddScoped<ITenantContext>(_ => new TenantContext { CurrentTenantId = fixture.TenantA });
        builder.Services.AddScoped<IPlmEngineeringService>(_ => fixture.Service(fixture.TenantA));
        var app = builder.Build();
        app.UseAuthentication(); app.UseAuthorization(); app.MapControllers();
        await app.StartAsync();
        var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        var client = new HttpClient { BaseAddress = new Uri(address) };
        client.DefaultRequestHeaders.Add("X-Test-Tenant", fixture.TenantA.ToString("D"));
        client.DefaultRequestHeaders.Add("X-Test-Actor", "wp0-http-test");
        return (app, client);
    }

    [SqlServerFact]
    public async Task TenantClaimBoundary()
    {
        var (app, client) = await StartAsync();
        await using (app)
        using (client)
        {
            const string path = "/api/plm/engineering/iteration-candidates";
            client.DefaultRequestHeaders.Remove("X-Test-Tenant");
            var missing = await client.PostAsJsonAsync(path, new CreateIterationCandidateCommand("ITER-MISSING", 1, null));
            Assert.Equal(HttpStatusCode.Forbidden, missing.StatusCode);
            Assert.Contains("PLM_TENANT_MISMATCH", await missing.Content.ReadAsStringAsync());
            client.DefaultRequestHeaders.Add("X-Test-Tenant", fixture.TenantB.ToString("D"));
            var wrong = await client.PostAsJsonAsync(path, new CreateIterationCandidateCommand("ITER-OTHER", 1, null));
            Assert.Equal(HttpStatusCode.Forbidden, wrong.StatusCode);
            client.DefaultRequestHeaders.Remove("X-Test-Tenant");
            client.DefaultRequestHeaders.Add("X-Test-Tenant", fixture.TenantA.ToString("D"));
            var good = await client.PostAsJsonAsync(path, new CreateIterationCandidateCommand($"ITER-{Guid.NewGuid():N}", 1, null));
            Assert.Equal(HttpStatusCode.Created, good.StatusCode);
        }
    }

    [SqlServerFact]
    public async Task ResolveSupersededBaseline()
    {
        var product = await fixture.SeedOwnerAsync(fixture.TenantA);
        var frozen = await fixture.FreezeAsync(fixture.TenantA, product);
        var service = fixture.Service(fixture.TenantA);
        var before = await service.GetEngineeringBaselineAsync(fixture.TenantA, frozen.BaselineId, CancellationToken.None);
        await service.SupersedeEngineeringBaselineAsync(fixture.TenantA, "test", frozen.BaselineId,
            new(Guid.NewGuid(), before.RowVersion, "new version"), CancellationToken.None);
        var (app, client) = await StartAsync();
        await using (app)
        using (client)
        {
            var get = await client.GetAsync($"/api/plm/engineering/baselines/{frozen.BaselineId}");
            Assert.Equal(HttpStatusCode.OK, get.StatusCode);
            var body = await get.Content.ReadFromJsonAsync<EngineeringBaselineResult>();
            Assert.Equal("SUPERSEDED", body!.Status);
            Assert.Equal(frozen.ManifestId, body.ManifestId);
            var manifestGet = await client.GetAsync($"/api/plm/engineering/manifests/{frozen.ManifestId}");
            Assert.Equal(HttpStatusCode.OK, manifestGet.StatusCode);
            var detail = await manifestGet.Content.ReadFromJsonAsync<TechnicalManifestDetailResult>();
            Assert.Equal("ITEM-1", detail!.Items.Single(x => x.ItemType == "PRODUCT").Content.GetProperty("itemCd").GetString());
            Assert.Equal("MAT-A", detail.Items.Single(x => x.ItemType == "BOM").Identity.GetProperty("materialCd").GetString());
            var resolved = await client.GetAsync($"/api/plm/engineering/baselines/resolve?candidateId={frozen.CandidateId}&candidateVersion=1");
            Assert.Equal(HttpStatusCode.OK, resolved.StatusCode);
            Assert.Equal(frozen.BaselineId, (await resolved.Content.ReadFromJsonAsync<EngineeringBaselineResult>())!.BaselineId);
        }
    }

    [SqlServerFact]
    public async Task FreezeDoesNotAssertVerPass()
    {
        var response = await FreezeOverHttpAsync();
        Assert.Equal("FROZEN", response.Result.Status);
        Assert.DoesNotContain("verPass", response.Json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("verificationVerdict", response.Json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(typeof(PlmContext).GetProperties(), x => x.Name.Contains("Verification", StringComparison.OrdinalIgnoreCase));
    }

    [SqlServerFact]
    public async Task FreezeDoesNotAssertProductionRelease()
    {
        var response = await FreezeOverHttpAsync();
        Assert.Equal("FROZEN", response.Result.Status);
        Assert.DoesNotContain("productionRelease", response.Json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("authorization", response.Json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(typeof(PlmContext).GetProperties(), x => x.Name.Contains("Release", StringComparison.OrdinalIgnoreCase));
    }

    [SqlServerFact]
    public async Task TamperedManifestFreezeReturnsIntegrityProblem()
    {
        var product = await fixture.SeedOwnerAsync(fixture.TenantA);
        var (_, candidateId, rowVersion) = await fixture.CreateCandidateAsync(fixture.TenantA, product);
        var service = fixture.Service(fixture.TenantA);
        var captured = await service.CaptureTechnicalManifestAsync(fixture.TenantA, "test", candidateId,
            new(Guid.NewGuid(), 1, rowVersion), CancellationToken.None);
        var validated = await service.ValidateTechnicalManifestAsync(fixture.TenantA, "test", candidateId, captured.Value.ManifestId,
            new(1, captured.Value.CandidateRowVersion, captured.Value.ManifestDigest), CancellationToken.None);
        await using (var connection = new SqlConnection(fixture.ConnectionString))
        {
            await connection.OpenAsync();
            await new SqlCommand("DISABLE TRIGGER [TR_PlmManifest_Immutable] ON [plm].[TechnicalManifest]", connection).ExecuteNonQueryAsync();
            try
            {
                await using var update = new SqlCommand("UPDATE [plm].[TechnicalManifest] SET [ManifestDigest]=@digest WHERE [Id]=@id", connection);
                update.Parameters.AddWithValue("@digest", new string('b', 64));
                update.Parameters.AddWithValue("@id", captured.Value.ManifestId);
                await update.ExecuteNonQueryAsync();
            }
            finally { await new SqlCommand("ENABLE TRIGGER [TR_PlmManifest_Immutable] ON [plm].[TechnicalManifest]", connection).ExecuteNonQueryAsync(); }
        }
        var (app, client) = await StartAsync();
        await using (app)
        using (client)
        {
            var response = await client.PostAsJsonAsync($"/api/plm/engineering/candidates/{candidateId}/baselines/freeze",
                new FreezeEngineeringBaselineCommand(Guid.NewGuid(), 1, validated.CandidateRowVersion, captured.Value.ManifestId, captured.Value.ManifestDigest));
            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            Assert.Contains("PLM_MANIFEST_INTEGRITY", await response.Content.ReadAsStringAsync());
        }
        await using var db = fixture.Plm(fixture.TenantA);
        Assert.False(await db.EngineeringBaselines.AnyAsync(x => x.EngineeringCandidateId == candidateId));
    }

    [SqlServerFact]
    public async Task CommandErrors()
    {
        var product = await fixture.SeedOwnerAsync(fixture.TenantA);
        var (_, candidateId, rowVersion) = await fixture.CreateCandidateAsync(fixture.TenantA, product);
        var (app, client) = await StartAsync();
        await using (app)
        using (client)
        {
            var malformed = await client.PostAsync("/api/plm/engineering/iteration-candidates",
                new StringContent("{", System.Text.Encoding.UTF8, "application/json"));
            Assert.Equal(HttpStatusCode.UnprocessableEntity, malformed.StatusCode);
            Assert.Contains("PLM_CANONICAL_SCHEMA", await malformed.Content.ReadAsStringAsync());
            var stale = await client.PostAsJsonAsync($"/api/plm/engineering/candidates/{candidateId}/manifests",
                new CaptureTechnicalManifestCommand(Guid.NewGuid(), 2, rowVersion));
            Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
            Assert.Contains("PLM_CANDIDATE_STALE", await stale.Content.ReadAsStringAsync());
            var absent = await client.GetAsync($"/api/plm/engineering/baselines/{Guid.NewGuid()}");
            Assert.Equal(HttpStatusCode.NotFound, absent.StatusCode);
            Assert.Contains("PLM_NOT_FOUND", await absent.Content.ReadAsStringAsync());
        }
    }

    private async Task<(EngineeringBaselineResult Result, string Json)> FreezeOverHttpAsync()
    {
        var product = await fixture.SeedOwnerAsync(fixture.TenantA);
        var (_, candidateId, rowVersion) = await fixture.CreateCandidateAsync(fixture.TenantA, product);
        var service = fixture.Service(fixture.TenantA);
        var captured = await service.CaptureTechnicalManifestAsync(fixture.TenantA, "test", candidateId,
            new(Guid.NewGuid(), 1, rowVersion), CancellationToken.None);
        var validated = await service.ValidateTechnicalManifestAsync(fixture.TenantA, "test", candidateId, captured.Value.ManifestId,
            new(1, captured.Value.CandidateRowVersion, captured.Value.ManifestDigest), CancellationToken.None);
        var (app, client) = await StartAsync();
        await using (app)
        using (client)
        {
            var reply = await client.PostAsJsonAsync($"/api/plm/engineering/candidates/{candidateId}/baselines/freeze",
                new FreezeEngineeringBaselineCommand(Guid.NewGuid(), 1, validated.CandidateRowVersion, captured.Value.ManifestId, captured.Value.ManifestDigest));
            Assert.Equal(HttpStatusCode.Created, reply.StatusCode);
            var json = await reply.Content.ReadAsStringAsync();
            var result = (await reply.Content.ReadFromJsonAsync<EngineeringBaselineResult>())!;
            await using var db = fixture.Plm(fixture.TenantA);
            Assert.Equal(1, await db.EngineeringBaselines.CountAsync(x => x.Id == result.BaselineId));
            return (result, json);
        }
    }
}

public sealed class PlmTestAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var claims = new List<Claim>();
        if (Request.Headers.TryGetValue("X-Test-Tenant", out var tenant)) claims.Add(new Claim("tenant_id", tenant.ToString()));
        if (Request.Headers.TryGetValue("X-Test-Actor", out var actor)) claims.Add(new Claim("sub", actor.ToString()));
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, Scheme.Name));
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name)));
    }
}
