using CP6.Core.Services.Erp;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    [CurrentPermissionPostgresFact]
    public async Task ControlledDocument_binding_reuses_file_transaction_fence_and_rejects_unavailable_or_changed_bytes()
    {
        await using var h = await Seed();
        var store = new ContentStore();
        var input = await DocumentInput(h, store);
        var documents = Documents(h, store);
        var files = ContentOwner(h, store, new ContentInspector());
        var source = await files.ReadAsync(input.Body.Contents[0].File.Id);

        await files.CommandAsync(ContentCommand(source, "HOLD"));
        Assert.Equal("FILE_CONTENT_NOT_AVAILABLE", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(
            () => documents.CommandAsync(input))).Message);
        Assert.Empty(await documents.ListAsync());

        await files.CommandAsync(ContentCommand(await files.ReadAsync(source.ContentRef.Id), "RELEASE"));
        var path = store.Bytes.Single(x => x.Value.SequenceEqual(new byte[] { 1, 2, 3 })).Key;
        store.Bytes[path] = [9, 9, 9];
        Assert.Equal("FILE_CONTENT_INTEGRITY_UNAVAILABLE", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(
            () => documents.CommandAsync(input))).Message);
        Assert.Empty(await documents.ListAsync());
        Assert.Equal(0, await h.Db.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM cp6_controlled_document_v1.bindings WHERE tenant_id={h.Tenant}").SingleAsync());

        store.Bytes[path] = [1, 2, 3];
        var result = await documents.CommandAsync(input);
        Assert.Equal(result, await documents.CommandAsync(input));
        Assert.Equal(2, (await documents.ReadAsync(input.DocumentId)).Body.Contents.Length);
        Assert.Equal(new byte[] { 1, 2, 3 }, (await documents.DownloadAsync(input.DocumentId, "1", input.Body.Contents[0].OccurrenceId)).Bytes);
    }
}
