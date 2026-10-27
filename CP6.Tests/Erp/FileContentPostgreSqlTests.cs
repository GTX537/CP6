using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Common;
using CP6.Core.Services.Erp;
using CP6.Core.Services.Integration;
using CP6.Core.Services.Plm;
using CP6.Core.Services.Pub;
using CP6.Core.Services.Sys;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CP6.Tests.Erp;
public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private sealed class ContentStore : IFileStore
    {
        internal readonly System.Collections.Concurrent.ConcurrentDictionary<string, byte[]> Bytes = new();
        public async Task<string> SaveAsync(Stream content, string name) { using var copy = new MemoryStream(); await content.CopyToAsync(copy); Assert.True(Bytes.TryAdd(name, copy.ToArray())); return name; }
        public Task<Stream> OpenReadAsync(string name) => Task.FromResult<Stream>(new MemoryStream(Bytes[name], false));
        public Task<Stream> OpenReadBoundedAsync(string name, long size, CancellationToken ct) => OpenReadAsync(name);
        public Task DeleteAsync(string name) => throw new InvalidOperationException("Native content must not blindly delete staging");
        public bool Exists(string name) => Bytes.ContainsKey(name);
    }
    private sealed class ContentInspector : IFileContentInspectorV1
    {
        internal string Status = "CLEAN"; internal int Calls;
        public Task<FileInspectionV1> InspectAsync(byte[] bytes, CancellationToken ct)
        { Calls++; return Task.FromResult(new FileInspectionV1(Status, "EXPLICIT_TEST_DOUBLE", "test-engine", "test-signature", DateTimeOffset.UtcNow, FileContentBytesV1.Digest(bytes), "Controlled transaction test")); }
    }
    private static OperationsOwnerV1 ContentAuthority(Harness h, CP6Context? db = null)
    {
        var c = new AuthorizationCatalogStoreV1();
        foreach (var key in new[] { "file-content", "plm-engineering-revision", "plm-engineering-product" })
        { c.RegisterScope(key, key, [1, 2, 3, 4, 5], 5); c.RegisterFields(key, key == "file-content" ? [new FieldRegistry.Field("Content", "Content"), new FieldRegistry.Field("FileName", "Name")] : []); }
        c.Seal(); return new(db ?? h.Db, new OperationsHttp { HttpContext = h.Http }, _ => Task.FromResult(h.User), c.Capture);
    }
    private static async Task<FileUploadV1> ContentInput(Harness h, byte[] bytes)
    {
        var parent = await BomParent(h);
        foreach (var action in new[] { "query", "add", "edit" }) await EngineeringAction(h, "file-content", action);
        h.Db.Sys_RoleDataScopes.Add(new() { RoleId = 72, ResourceKey = "file-content", ScopeType = 5 });
        await h.Db.SaveChangesAsync(); h.Db.ChangeTracker.Clear();
        return new(Guid.NewGuid(), Guid.NewGuid(), parent.Reference, "技术原件.txt", bytes.Length, FileContentBytesV1.Digest(bytes), "Original technical content upload");
    }
    private static FileContentOwnerV1 ContentOwner(Harness h, IFileStore store, IFileContentInspectorV1 scanner, CP6Context? db = null)
        => new(db ?? h.Db, ContentAuthority(h, db), store, scanner);
    private static FileContentCommandV1 ContentCommand(FileContentSnapshotV1 value, string action) => new(Guid.NewGuid(), value.ContentRef.Id, action, value.StateVersion, value.Etag, "Explicit file state change");
    [CurrentPermissionPostgresFact]
    public async Task FileContent_exact_bytes_original_replay_and_immutable_history()
    {
        await using var h = await Seed(); var bytes = Encoding.UTF8.GetBytes("Technical original\r\n精确字节"); var input = await ContentInput(h, bytes);
        var store = new ContentStore(); var scan = new ContentInspector(); var owner = ContentOwner(h, store, scan);
        var result = await owner.UploadAsync(input, new MemoryStream(bytes)); Assert.Equal(result, await owner.UploadAsync(input, new MemoryStream(bytes)));
        Assert.Single(store.Bytes); Assert.Equal(1, scan.Calls); var saved = await owner.ReadAsync(input.FileId);
        Assert.Equal("SHA256", saved.ContentRef.HashAlgorithm); Assert.Equal(input.Digest, saved.ContentRef.Digest);
        Assert.Equal(bytes, (await owner.DownloadAsync(input.FileId, "1", input.Digest)).Bytes);
        Assert.DoesNotContain("storePath", JsonSerializer.Serialize(saved, new JsonSerializerOptions(JsonSerializerDefaults.Web)), StringComparison.OrdinalIgnoreCase);
        await owner.CommandAsync(ContentCommand(saved, "HOLD")); Assert.Equal("HELD", (await owner.ReadAsync(input.FileId)).Availability);
        Assert.Equal(saved, (await owner.ReadAsync(input.FileId, "1")) with { IsCurrent = true });
        Assert.Equal(409, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => owner.DownloadAsync(input.FileId, "1", input.Digest))).Status);
        Assert.Equal(result, (await owner.OperationAsync(input.OperationId, false)).Result);
        foreach (var sql in new[] { "UPDATE cp6_file_content_v1.contents SET digest=repeat('a',64)", "DELETE FROM cp6_file_content_v1.events", "TRUNCATE cp6_file_content_v1.contents CASCADE" })
            await Assert.ThrowsAsync<PostgresException>(() => h.Db.Database.ExecuteSqlRawAsync(sql));
    }
    [CurrentPermissionPostgresFact]
    public async Task FileContent_pending_and_rejected_scans_never_download_release_does_not_qualify()
    {
        await using var h = await Seed(); byte[] bytes = [1, 2, 3]; var input = await ContentInput(h, bytes); var scan = new ContentInspector { Status = "PENDING" }; var owner = ContentOwner(h, new ContentStore(), scan);
        await owner.UploadAsync(input, new MemoryStream(bytes)); var value = await owner.ReadAsync(input.FileId); Assert.Equal("PENDING_INSPECTION", value.Availability);
        Assert.Equal(409, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => owner.DownloadAsync(input.FileId, "1", input.Digest))).Status);
        await owner.CommandAsync(ContentCommand(value, "HOLD")); value = await owner.ReadAsync(input.FileId);
        scan.Status = "NOT_CLEAN"; await owner.CommandAsync(ContentCommand(value, "INSPECT")); value = await owner.ReadAsync(input.FileId); Assert.True(value.Held);
        await owner.CommandAsync(ContentCommand(value, "RELEASE")); value = await owner.ReadAsync(input.FileId); Assert.Equal("QUARANTINED", value.Availability);
        scan.Status = "CLEAN"; await owner.CommandAsync(ContentCommand(value, "INSPECT")); Assert.Equal(bytes, (await owner.DownloadAsync(input.FileId, "1", input.Digest)).Bytes);
    }
    [CurrentPermissionPostgresFact]
    public async Task FileContent_store_tampering_quarantines_and_never_falls_back()
    {
        await using var h = await Seed(); byte[] bytes = [1, 2, 3]; var input = await ContentInput(h, bytes); var store = new ContentStore(); var owner = ContentOwner(h, store, new ContentInspector());
        await owner.UploadAsync(input, new MemoryStream(bytes)); var path = Assert.Single(store.Bytes).Key; store.Bytes[path] = [4, 5, 6];
        Assert.Equal("FILE_CONTENT_INTEGRITY_UNAVAILABLE", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => owner.DownloadAsync(input.FileId, "1", input.Digest))).Message);
        Assert.Equal("QUARANTINED", (await owner.ReadAsync(input.FileId)).Availability); store.Bytes[path] = bytes;
        Assert.Equal(409, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => owner.DownloadAsync(input.FileId, "1", input.Digest))).Status);
        Assert.Equal("AVAILABLE", (await owner.ReadAsync(input.FileId, "1")).Availability);
        await owner.CommandAsync(ContentCommand(await owner.ReadAsync(input.FileId), "INSPECT")); Assert.Equal(bytes, (await owner.DownloadAsync(input.FileId, "1", input.Digest)).Bytes);
        Assert.Equal(412, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => owner.DownloadAsync(input.FileId, "1", new string('0', 64)))).Status);
    }
    [CurrentPermissionPostgresFact]
    public async Task FileContent_stream_length_hash_filename_and_duplicate_body_boundaries()
    {
        await using var h = await Seed(); byte[] bytes = [1, 2, 3]; var input = await ContentInput(h, bytes); var store = new ContentStore(); var owner = ContentOwner(h, store, new ContentInspector());
        foreach (var bad in new[] { input with { ByteLength = 20971521 }, input with { FileName = "../x" }, input with { FileName = "a\r\nb" }, input with { Parent = input.Parent with { Owner = "wrong" } } })
            Assert.Equal(400, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => owner.UploadAsync(bad, new MemoryStream(bytes)))).Status);
        foreach (var supplied in new byte[][] { [1, 2], [1, 2, 3, 4], [4, 5, 6] })
            Assert.Equal(400, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => owner.UploadAsync(input, new MemoryStream(supplied)))).Status);
        Assert.Empty(store.Bytes); await owner.UploadAsync(input, new MemoryStream(bytes));
        Assert.Equal(400, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => owner.UploadAsync(input, new MemoryStream(new byte[] { 4, 5, 6 })))).Status);
        Assert.Equal("ORIGINAL_OPERATION_INPUT_CONFLICT", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => owner.UploadAsync(input with { FileName = "changed" }, new MemoryStream(bytes)))).Message);
    }
    [CurrentPermissionPostgresFact]
    public async Task FileContent_current_parent_field_and_tenant_permissions_protect_history_and_receipts()
    {
        await using var h = await Seed(); byte[] bytes = [1, 2, 3]; var input = await ContentInput(h, bytes); var store = new ContentStore(); var scanner = new ContentInspector(); var owner = ContentOwner(h, store, scanner);
        await owner.UploadAsync(input, new MemoryStream(bytes));
        h.Db.Sys_RoleFieldPerms.Add(new() { RoleId = 72, ResourceKey = "file-content", FieldName = "Content", Access = 3 }); await h.Db.SaveChangesAsync(); h.Db.ChangeTracker.Clear();
        Assert.Equal(403, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => owner.ListAsync(input.Parent.Id))).Status);
        Assert.Equal(403, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => owner.OperationAsync(input.OperationId, false))).Status);
        await h.Db.Sys_RoleFieldPerms.Where(x => x.ResourceKey == "file-content").ExecuteDeleteAsync();
        var menu = await h.Db.Sys_Menus.Where(x => x.MenuKey == "plm-engineering-revision").Select(x => x.MenuId).SingleAsync();
        await h.Db.Sys_RoleActions.Where(x => x.MenuId == menu && x.ActionCode == "query").ExecuteDeleteAsync();
        Assert.Equal(403, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => owner.ReadAsync(input.FileId, "1"))).Status);
        await using var other = await Seed(); await ContentInput(other, bytes); Assert.Equal(403, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => ContentOwner(other, store, scanner).ReadAsync(input.FileId))).Status);
    }
    [TenantManagementPostgresTheory]
    [InlineData(2)] [InlineData(3)] [InlineData(4)]
    public async Task FileContent_department_parent_mapping_controls_creation_and_revocation(int type)
    {
        await using var h = await Seed(); byte[] bytes = [1, 2, 3]; var input = await ContentInput(h, bytes); var owner = ContentOwner(h, new ContentStore(), new ContentInspector()); var admin = await MappingAdmin(h);
        var parent = await BomRevisions(h).ReadAsync(input.Parent.Id, null);
        await StandaloneScope(h, admin, type); Assert.Equal(403, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => owner.UploadAsync(input, new MemoryStream(bytes)))).Status);
        await BindStandalone(h, admin, "plm-engineering-product", parent.Draft.ProductRef!.Id);
        await BindStandalone(h, admin, "plm-engineering-revision", input.Parent.Id); await owner.UploadAsync(input, new MemoryStream(bytes));
        await h.Db.Database.ExecuteSqlInterpolatedAsync($"UPDATE cp6_operations_owner_v1.department_mappings SET enabled=false WHERE tenant_id={h.Tenant} AND object_kind='plm-engineering-revision' AND object_id={input.Parent.Id}");
        Assert.Equal(403, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => owner.DownloadAsync(input.FileId, "1", input.Digest))).Status);
    }
    [CurrentPermissionPostgresFact]
    public async Task FileContent_parent_version_change_blocks_upload_but_preserves_history()
    {
        await using var h = await Seed(); byte[] bytes = [1, 2, 3]; var input = await ContentInput(h, bytes); var owner = ContentOwner(h, new ContentStore(), new ContentInspector());
        await owner.UploadAsync(input, new MemoryStream(bytes)); var parent = await BomRevisions(h).ReadAsync(input.Parent.Id, null);
        await BomRevisions(h).CommandAsync(PatchRevision(parent, new EngineeringDraftPatchV1 { Op = "SET_CONFIGURATION", Configuration = new([], "APPLIES", "Changed configuration") }));
        Assert.Equal(412, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => owner.UploadAsync(input with { OperationId = Guid.NewGuid(), FileId = Guid.NewGuid() }, new MemoryStream(bytes)))).Status);
        Assert.Equal(bytes, (await owner.DownloadAsync(input.FileId, "1", input.Digest)).Bytes);
    }
    [CurrentPermissionPostgresFact]
    public async Task FileContent_unknown_close_final_authority_rollback_and_trigger_readiness()
    {
        var hook = new CommandHook(); await using var h = await Seed(hook); byte[] bytes = [1, 2, 3]; var input = await ContentInput(h, bytes); var store = new ContentStore(); var owner = ContentOwner(h, store, new ContentInspector()); var reached = false;
        hook.Before = async command =>
        {
            if (!command.CommandText.Contains("WITH field_subject")) return;
            await using var probe = command.Connection!.CreateCommand(); probe.Transaction = command.Transaction;
            probe.CommandText = $"SELECT count(*) FROM cp6_file_content_v1.contents WHERE tenant_id='{h.Tenant}'";
            if (Convert.ToInt32(await probe.ExecuteScalarAsync()) > 0) { reached = true; throw new IOException("Current file authority unavailable"); }
        };
        try { Assert.Equal(503, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => owner.UploadAsync(input, new MemoryStream(bytes)))).Status); } finally { hook.Before = null; }
        Assert.True(reached); Assert.Single(store.Bytes); Assert.Empty(await owner.ListAsync(input.Parent.Id));
        Assert.Equal("UNKNOWN", (await owner.OperationAsync(input.OperationId, false)).State); Assert.Equal("CLOSED", (await owner.OperationAsync(input.OperationId, true)).State);
        Assert.Equal("ORIGINAL_OPERATION_CLOSED", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => owner.UploadAsync(input, new MemoryStream(bytes)))).Message);
        await h.Db.Database.ExecuteSqlRawAsync("ALTER TABLE cp6_file_content_v1.events DISABLE TRIGGER file_validate");
        try { Assert.Equal("FILE_STORE_UNAVAILABLE", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => owner.ReadAsync(input.FileId))).Message); }
        finally { await h.Db.Database.ExecuteSqlRawAsync("ALTER TABLE cp6_file_content_v1.events ENABLE TRIGGER file_validate"); }
    }
    [CurrentPermissionPostgresFact]
    public async Task FileContent_concurrent_upload_replay_and_state_compare_exchange()
    {
        await using var h = await Seed(); byte[] bytes = [1, 2, 3]; var input = await ContentInput(h, bytes); var store = new ContentStore(); var scanner = new ContentInspector(); var owner = ContentOwner(h, store, scanner);
        await using var peer = server.Context(new TenantContext { CurrentTenantId = h.Tenant }); var second = ContentOwner(h, store, scanner, peer);
        var results = await Task.WhenAll(owner.UploadAsync(input, new MemoryStream(bytes)), second.UploadAsync(input, new MemoryStream(bytes))); Assert.Equal(results[0], results[1]);
        Assert.Single(await owner.ListAsync(input.Parent.Id)); var saved = await owner.ReadAsync(input.FileId);
        async Task<bool> Hold(FileContentOwnerV1 writer) { try { await writer.CommandAsync(ContentCommand(saved, "HOLD")); return true; } catch (OrderPriceCorrectionException e) { Assert.Equal(412, e.Status); return false; } }
        Assert.Single(await Task.WhenAll(Hold(owner), Hold(second)), x => x);
    }
    [CurrentPermissionPostgresFact]
    public async Task FileContent_actual_HTTP_scan_exact_download_and_current_revocation()
    {
        await using var h = await Seed(); var bytes = Encoding.UTF8.GetBytes("CP6 benign technical content fixture\n原件字节校验\n"); var input = await ContentInput(h, bytes);
        var scanner = Environment.GetEnvironmentVariable("CP6_FILE_DEFENDER_EXE");
        var env = new Dictionary<string, string> { ["Storage__Provider"] = "Local", ["Storage__LocalRoot"] = Path.Combine(Environment.GetEnvironmentVariable("CP6_PERMISSION_PG_ROOT")!, "content-store") };
        if (!string.IsNullOrEmpty(scanner)) { env["FileContent__Defender__Enabled"] = "true"; env["FileContent__Defender__Executable"] = scanner; env["FileContent__Defender__TemporaryDirectory"] = Path.Combine(Environment.GetEnvironmentVariable("CP6_PERMISSION_PG_ROOT")!, "scan-temp"); }
        await using var host = await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!, env); var cookies = await Login(host, h);
        input = input with { Reason = new string('文', 4000) };
        var metadata = JsonSerializer.SerializeToUtf8Bytes(input, new JsonSerializerOptions(JsonSerializerDefaults.Web) { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
        var request = OperationsRequest(cookies, HttpMethod.Post, "/api/file-content/owner-v1/uploads"); request.Headers.Add("X-File-Command", Convert.ToBase64String(metadata)); request.Content = new ByteArrayContent(bytes);
        using var upload = await host.Send(request); Assert.Equal(HttpStatusCode.OK, upload.StatusCode);
        using var read = await host.Send(OperationsRequest(cookies, HttpMethod.Get, "/api/file-content/owner-v1/" + input.FileId)); Assert.Equal(HttpStatusCode.OK, read.StatusCode); Assert.True(read.Headers.CacheControl?.NoStore);
        using var json = JsonDocument.Parse(await read.Content.ReadAsStringAsync()); var value = json.RootElement.GetProperty("data");
        Assert.Equal(string.IsNullOrEmpty(scanner) ? "PENDING_INSPECTION" : "AVAILABLE", value.GetProperty("availability").GetString());
        if (!string.IsNullOrEmpty(scanner)) Assert.Equal("MicrosoftDefender", value.GetProperty("inspection").GetProperty("provider").GetString());
        var url = $"/api/file-content/owner-v1/{input.FileId}/download?version=1&digest={input.Digest}";
        using var download = await host.Send(OperationsRequest(cookies, HttpMethod.Get, url));
        Assert.Equal(string.IsNullOrEmpty(scanner) ? HttpStatusCode.Conflict : HttpStatusCode.OK, download.StatusCode);
        if (!string.IsNullOrEmpty(scanner)) { Assert.Equal(bytes, await download.Content.ReadAsByteArrayAsync()); Assert.Equal("attachment", download.Content.Headers.ContentDisposition?.DispositionType); Assert.Equal("nosniff", Assert.Single(download.Headers.GetValues("X-Content-Type-Options"))); }
        var menu = await h.Db.Sys_Menus.Where(x => x.MenuKey == "file-content").Select(x => x.MenuId).SingleAsync(); await h.Db.Sys_RoleActions.Where(x => x.MenuId == menu && x.ActionCode == "query").ExecuteDeleteAsync();
        using var denied = await host.Send(OperationsRequest(cookies, HttpMethod.Get, url)); Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
    }
}
