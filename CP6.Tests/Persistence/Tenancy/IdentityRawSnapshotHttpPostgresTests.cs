using System.Net;
using System.Net.Http.Json;
using System.Text;
using CP6.WebApi.Controllers.Internal;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using CP6.Core.Services.CrmIdentity;
using CP6.Core.Services.CrmIdentity.Baselines;
using static CP6.Tests.Persistence.Tenancy.IdentityDirectoryPostgresFixture;

namespace CP6.Tests.Persistence.Tenancy;

[Collection("IdentityDirectoryConfiguredHttpPostgres")]
public sealed class IdentityRawSnapshotHttpPostgresTests(IdentityDirectoryConfiguredPostgresFixture postgres)
{
    private const string Root = "/internal/crm/identity/directory/v1/";

    [CurrentPermissionPostgresFact]
    public async Task Versioned_directory_exposes_complete_count_and_exact_raw_snapshot_through_ordinary_host()
    {
        await using var host = await IdentityDirectoryConfiguredHttpHost.Start(postgres);
        foreach (var tenant in new[] { A, B, A })
        {
            var token = await host.Token(tenant);
            using var response = await host.Get(Root + "versions?pageSize=1", token.Token);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode); Assert.True(response.Headers.CacheControl?.NoStore);
            var page = (await response.Content.ReadFromJsonAsync<IdentityBaselinePageV1>())!;
            Assert.Equal(tenant, page.Source.TenantId); Assert.Equal(IdentityBaselineVerifier.Contract, page.Contract);
            Assert.Equal(postgres.Identity.BaselineSha256, page.Source.StorageIdentitySha256);
            Assert.Equal(host.Runtime.Options.Issuer, page.Source.Issuer); Assert.Equal(host.Runtime.ContractBundleSha256, page.Source.ContractBundleSha256);
            Assert.Equal(2, page.TotalCount); Assert.Single(page.Items); Assert.NotNull(page.NextCursor);
            using var nextResponse = await host.Get(Root + "versions?pageSize=1&cursor=" + Uri.EscapeDataString(page.NextCursor!), token.Token);
            Assert.Equal(HttpStatusCode.OK, nextResponse.StatusCode);
            var last = (await nextResponse.Content.ReadFromJsonAsync<IdentityBaselinePageV1>())!;
            Assert.Equal(page.Source, last.Source); Assert.Equal(page.Boundary, last.Boundary);
            Assert.Equal(page.TotalCount, last.TotalCount); Assert.Single(last.Items); Assert.Null(last.NextCursor);
            foreach (var item in page.Items.Concat(last.Items))
            {
                using var rawResponse = await host.Get(Root + "snapshots/" + Uri.EscapeDataString(item.AggregateId), token.Token);
                Assert.Equal(HttpStatusCode.OK, rawResponse.StatusCode); Assert.True(rawResponse.Headers.CacheControl?.NoStore);
                var raw = (await rawResponse.Content.ReadFromJsonAsync<IdentityRawSnapshotV1>())!;
                Assert.Equal(page.Source, raw.Source); Assert.Equal(page.Boundary, raw.Boundary); Assert.Equal(item, raw.Item);
                Assert.Equal(item.PayloadSha256, IdentityEventContracts.Hash(Convert.FromBase64String(raw.PayloadBase64)));
            }
            using var foreign = await host.Get(Root + $"snapshots/tenant:{(tenant == A ? B : A):D}", token.Token);
            Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
        }
        Assert.Equal(0, host.OwnerQueries); Assert.Equal(0, host.Host.PoolSnapshot.Borrowers);
    }

    [CurrentPermissionPostgresFact]
    public async Task Original_whitespace_and_escapes_survive_PG_and_HTTP_but_corrupt_content_is_rejected()
    {
        await using var host = await IdentityDirectoryConfiguredHttpHost.Start(postgres); var token = await host.Token(A);
        await using var db = postgres.OwnerContext(A); var aggregate = "tenant:" + A.ToString("D");
        var original = await db.CrmIdentitySnapshots.AsNoTracking().SingleAsync(x => x.TenantId == A && x.AggregateId == aggregate);
        var exact = " \r\n" + original.PayloadJson.Replace("\"tenantId\"", "\"\\u0074enantId\"") + "\t  ";
        async Task Write(string payload, string hash) => await db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE crm_identity.\"Snapshot\" SET \"PayloadJson\"={payload},\"PayloadSha256\"={hash} WHERE \"TenantId\"={A} AND \"AggregateId\"={aggregate}");
        try
        {
            var bytes = Encoding.UTF8.GetBytes(exact); await Write(exact, IdentityEventContracts.Hash(bytes));
            using (var response = await host.Get(Root + "snapshots/" + aggregate, token.Token))
            {
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                var raw = (await response.Content.ReadFromJsonAsync<IdentityRawSnapshotV1>())!;
                Assert.Equal(bytes, Convert.FromBase64String(raw.PayloadBase64));
            }
            await Write(exact, new string('0', 64));
            using (var damaged = await host.Get(Root + "snapshots/" + aggregate, token.Token))
            {
                Assert.Equal(HttpStatusCode.ServiceUnavailable, damaged.StatusCode);
                Assert.Equal("{\"code\":\"C02_IDENTITY_STORE_UNAVAILABLE\"}", await damaged.Content.ReadAsStringAsync());
            }
            var foreign = exact.Replace(A.ToString("D"), B.ToString("D"));
            await Write(foreign, IdentityEventContracts.Hash(Encoding.UTF8.GetBytes(foreign)));
            using var mismatched = await host.Get(Root + "snapshots/" + aggregate, token.Token);
            Assert.Equal(HttpStatusCode.ServiceUnavailable, mismatched.StatusCode);
        }
        finally { await Write(original.PayloadJson, original.PayloadSha256); }
        using var recovered = await host.Get(Root + "snapshots/" + aggregate, token.Token); Assert.Equal(HttpStatusCode.OK, recovered.StatusCode);
        Assert.Equal(0, host.OwnerQueries); Assert.Equal(0, host.Host.PoolSnapshot.Borrowers);
    }

    [CurrentPermissionPostgresFact]
    public async Task New_protocol_binds_cursors_rejects_unready_data_and_preserves_legacy_routes()
    {
        await using var host = await IdentityDirectoryConfiguredHttpHost.Start(postgres); var a = await host.Token(A); var b = await host.Token(B);
        using var first = await host.Get(Root + "versions?pageSize=1", a.Token); Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var page = (await first.Content.ReadFromJsonAsync<IdentityBaselinePageV1>())!;
        var cursor = Uri.EscapeDataString(page.NextCursor!);
        using (var cross = await host.Get(Root + "versions?cursor=" + cursor, b.Token)) Assert.Equal(HttpStatusCode.BadRequest, cross.StatusCode);
        using (var mixed = await host.Get("/internal/crm/identity/versions?cursor=" + cursor, a.Token)) Assert.Equal(HttpStatusCode.BadRequest, mixed.StatusCode);
        foreach (var query in new[] { "?pageSize=201", "?pageSize=1&pageSize=2", "?tenantId=" + B, "?cursor=broken" })
        { using var bad = await host.Get(Root + "versions" + query, a.Token); Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode); }
        using (var anonymous = await host.Get(Root + "versions", null)) Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        using (var unrecorded = await host.Get(Root + "snapshots/tenant:" + A, (await host.Token(A, record: false)).Token)) Assert.Equal(HttpStatusCode.Forbidden, unrecorded.StatusCode);
        await using var db = postgres.OwnerContext(A);
        var state = await db.CrmIdentityBootstrapStates.AsNoTracking().SingleAsync(x => x.TenantId == A);
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE crm_identity.\"Bootstrap\" SET \"CompletedAtUtc\"=NULL WHERE \"TenantId\"={A}");
        try
        {
            foreach (var path in new[] { Root + "versions", Root + "snapshots/tenant:" + A })
            { using var unavailable = await host.Get(path, a.Token); Assert.Equal(HttpStatusCode.ServiceUnavailable, unavailable.StatusCode); }
        }
        finally { await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE crm_identity.\"Bootstrap\" SET \"CompletedAtUtc\"={state.CompletedAtUtc} WHERE \"TenantId\"={A}"); }
        foreach (var path in new[] { "/internal/crm/identity/versions", "/internal/crm/identity/snapshots/tenant:" + A })
        { using var legacy = await host.Get(path, a.Token); Assert.Equal(HttpStatusCode.OK, legacy.StatusCode); }
        Assert.Equal(0, host.OwnerQueries); Assert.Equal(0, host.Host.PoolSnapshot.Borrowers);
    }
}

public sealed class IdentityRawSnapshotHostRequirementTests
{
    [Fact]
    public async Task Missing_ordinary_host_never_uses_legacy_owner_path_for_new_routes()
    {
        var controller = new CrmIdentityController(null!, null!, null!)
        { ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() } };
        Assert.IsType<NotFoundResult>(await controller.BaselineVersions());
        Assert.IsType<NotFoundResult>(await controller.RawSnapshot("tenant:any"));
        Assert.Equal("no-store", controller.Response.Headers.CacheControl.ToString());
    }
}
