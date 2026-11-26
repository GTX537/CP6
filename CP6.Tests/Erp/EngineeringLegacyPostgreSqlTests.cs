using System.Net;
using System.Text;
using System.Text.Json;
using CP6.Core.Services.Erp;
using CP6.Core.Services.Plm;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    [CurrentPermissionPostgresFact]
    public async Task EngineeringLegacy_HTTP_records_original_evidence_and_derives_only_a_new_draft()
    {
        await using var h = await Seed();
        await GrantEngineeringRevision(h);
        await GrantEngineering(h);
        await RevisionRule(h);
        await EngineeringAction(h, "plm-engineering-revision", "legacy-register");
        var product = await MakeEngineering(h);
        var create = await NewRevision(Revisions(h), product: product.Reference);
        await using var host = await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!);
        var cookies = await Login(host, h);
        var record = new { operationId = Guid.NewGuid(), product = product.Reference, sourceRepositorySha = new string('a', 40),
            sourceRecordRef = "ProductMaterial/Synthetic:00027", sourcePath = "archive/bom.json", sourceHash = new string('b', 64),
            evidenceGaps = new[] { "缺少原批准和原修订历史" }, reason = "登记批准查阅的合成档案" };
        const string path = "api/engineering-revisions/owner-v1";
        async Task<HttpResponseMessage> Post(string endpoint, object value)
        {
            var request = OperationsRequest(cookies, HttpMethod.Post, endpoint);
            request.Content = new StringContent(JsonSerializer.Serialize(value, new JsonSerializerOptions(JsonSerializerDefaults.Web)), Encoding.UTF8, "application/json");
            return await host.Send(request);
        }
        using var saved = await Post($"{path}/legacy-references", record);
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        var original = await saved.Content.ReadAsStringAsync();
        using var retry = await Post($"{path}/legacy-references", record);
        Assert.Equal(original, await retry.Content.ReadAsStringAsync());
        using var conflict = await Post($"{path}/legacy-references", record with { sourceHash = new string('c', 64) });
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        using var body = JsonDocument.Parse(original);
        var data = body.RootElement.GetProperty("data");
        Assert.Equal("EVIDENCE_INCOMPLETE", data.GetProperty("evidenceStatus").GetString());
        var reference = data.GetProperty("reference").Deserialize<EngineeringSourceRefV1>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        using var source = await host.Send(OperationsRequest(cookies, HttpMethod.Get, $"{path}/legacy-references/{reference.Id}"));
        Assert.Equal(HttpStatusCode.OK, source.StatusCode);
        Assert.Contains(record.sourceRecordRef, await source.Content.ReadAsStringAsync());
        using var context = await host.Send(OperationsRequest(cookies, HttpMethod.Get, $"{path}/creation-context"));
        Assert.Equal(HttpStatusCode.OK, context.StatusCode);
        using var contextBody = JsonDocument.Parse(await context.Content.ReadAsStringAsync());
        var authorityVersion = contextBody.RootElement.GetProperty("data").GetProperty("authorityVersion").GetString();
        using var draft = await Post($"{path}/commands", create with
        { Origin = new("LEGACY_REVIEW", LegacyRef: reference), ExpectedAuthorityVersion = authorityVersion });
        Assert.Equal(HttpStatusCode.OK, draft.StatusCode);
        var revision = await Revisions(h).ReadAsync(create.RevisionId, null);
        Assert.Equal(reference, revision.Origin.LegacyRef);
        Assert.Equal("DRAFT", revision.State);
        Assert.Equal("NOT_REQUESTED", revision.Freeze);
        Assert.Empty(revision.Draft.Components);
        using var operation = await host.Send(OperationsRequest(cookies, HttpMethod.Get, $"{path}/legacy-operations/{record.operationId}"));
        Assert.Equal(HttpStatusCode.OK, operation.StatusCode);
        using var recovered = JsonDocument.Parse(await operation.Content.ReadAsStringAsync());
        Assert.Equal("COMMITTED", recovered.RootElement.GetProperty("data").GetProperty("state").GetString());
        Assert.Equal(reference, recovered.RootElement.GetProperty("data").GetProperty("result").GetProperty("reference")
            .Deserialize<EngineeringSourceRefV1>(new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        var productMenu = await h.Db.Sys_Menus.Where(x => x.MenuKey == "plm-engineering-product").Select(x => x.MenuId).SingleAsync();
        await h.Db.Sys_RoleActions.Where(x => x.MenuId == productMenu && x.ActionCode == "query").ExecuteDeleteAsync();
        using var forbidden = await host.Send(OperationsRequest(cookies, HttpMethod.Get, $"{path}/legacy-operations/{record.operationId}"));
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
    }

    [CurrentPermissionPostgresFact]
    public async Task EngineeringLegacy_requires_an_exact_same_product_and_authorized_registration()
    {
        await using var h = await Seed();
        await GrantEngineeringRevision(h);
        await GrantEngineering(h);
        await RevisionRule(h);
        var product = await MakeEngineering(h);
        var otherProduct = await MakeEngineering(h);
        var owner = Revisions(h);
        var input = new EngineeringLegacyRegistrationV1(Guid.NewGuid(), product.Reference, new string('a', 40),
            "ProductProcess/合成:00002", null, new string('b', 64), ["变更历史待补"], "获准登记合成档案");
        Assert.Equal(403, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => owner.RegisterLegacyAsync(input))).Status);
        await EngineeringAction(h, "plm-engineering-revision", "legacy-register");
        Assert.Equal(400, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => owner.RegisterLegacyAsync(input with { EvidenceGaps = [] }))).Status);
        var legacy = await owner.RegisterLegacyAsync(input);
        Assert.Equal("ENGINEERING_LEGACY_REFERENCE_EXISTS", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() =>
            owner.RegisterLegacyAsync(input with { OperationId = Guid.NewGuid() }))).Message);
        var wrong = await NewRevision(owner, product: otherProduct.Reference);
        Assert.Equal("ENGINEERING_LEGACY_PRODUCT_MISMATCH", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() =>
            owner.CommandAsync(wrong with { Origin = new("LEGACY_REVIEW", LegacyRef: legacy.Reference) }))).Message);
        var exact = await NewRevision(owner, product: product.Reference);
        Assert.Equal(412, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => owner.CommandAsync(exact with
        {
            Origin = new("LEGACY_REVIEW", LegacyRef: legacy.Reference with { Digest = new string('c', 64) })
        }))).Status);
        Assert.Empty(await owner.ListAsync(null));

        await using var foreign = await Seed();
        await GrantEngineeringRevision(foreign);
        Assert.Equal(403, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => Revisions(foreign).LegacyReadAsync(legacy.Reference.Id))).Status);
        Assert.Equal("UNKNOWN", (await Revisions(foreign).LegacyOperationAsync(input.OperationId, false)).State);
        var unknown = input with { OperationId = Guid.NewGuid(), SourceRecordRef = "ProductProcess/closed" };
        Assert.Equal("CLOSED", (await owner.LegacyOperationAsync(unknown.OperationId, true)).State);
        Assert.Equal("ORIGINAL_OPERATION_CLOSED", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => owner.RegisterLegacyAsync(unknown))).Message);
    }

    [CurrentPermissionPostgresFact]
    public async Task EngineeringLegacy_forward_migration_preserves_existing_revision_original_journal_and_grants()
    {
        var name = await server.CreateIsolatedDatabaseAsync("20261126135000_ActFileNativeAuthorityV1");
        await using var h = await SeedForDatabase(name);
        await GrantEngineeringRevision(h);
        await GrantEngineering(h);
        await RevisionRule(h);
        var product = await MakeEngineering(h);
        var owner = Revisions(h);
        var command = await NewRevision(owner, product: product.Reference);
        var originalReceipt = await owner.CommandAsync(command);
        string original;
        await using (var transaction = await h.Db.Database.BeginTransactionAsync())
        {
            var store = new EngineeringRevisionStoreV1(h.Db);
            await store.Fence(false, default);
            original = (await store.Read(command.RevisionId, 1, default)).OriginalJson;
            await transaction.CommitAsync();
        }
        var grants = await h.Db.Sys_RoleActions.OrderBy(x => x.Id).Select(x => new { x.Id, x.MenuId, x.ActionCode }).ToArrayAsync();

        await h.Db.Database.MigrateAsync();
        Assert.Empty(await h.Db.Database.GetPendingMigrationsAsync());
        Assert.Equal(grants, await h.Db.Sys_RoleActions.OrderBy(x => x.Id).Select(x => new { x.Id, x.MenuId, x.ActionCode }).ToArrayAsync());
        Assert.Equal(original, (await owner.ReadAsync(command.RevisionId, "1")).OriginalJson);
        Assert.Equal(originalReceipt, await owner.CommandAsync(command));
        var menu = await h.Db.Sys_Menus.Where(x => x.MenuKey == "plm-engineering-revision").Select(x => x.MenuId).SingleAsync();
        Assert.True(await h.Db.Sys_MenuActions.AnyAsync(x => x.MenuId == menu && x.ActionCode == "legacy-register"));
        Assert.False(await h.Db.Sys_RoleActions.AnyAsync(x => x.MenuId == menu && x.ActionCode == "legacy-register"));
    }
}
