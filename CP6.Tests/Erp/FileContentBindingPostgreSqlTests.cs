using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using CP6.Core.Services.Erp;
using CP6.Core.Services.Common;
using CP6.Core.Services.Plm;
using CP6.Core.Services.Pub;
using CP6.Entity.DomainModels.Sys;
using CP6.Tests.Platform;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    [CurrentPermissionPostgresFact]
    public async Task FileDraft_actual_http_binds_exact_set_and_recovers_original_receipt()
    {
        await using var h = await Seed();
        var bytes = Encoding.UTF8.GetBytes("受控草稿文件");
        var upload = await ContentInput(h, bytes);
        var path = Path.Combine(Environment.GetEnvironmentVariable("CP6_PERMISSION_PG_ROOT")!,
            "draft-files", Guid.NewGuid().ToString("N"));
        var owner = ContentOwner(h, new LocalFileStore(path), new ContentInspector());
        await owner.UploadAsync(upload, new MemoryStream(bytes));
        var file = (await owner.ReadAsync(upload.FileId)).ContentRef;
        await using var host = await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!,
            new Dictionary<string, string> { ["Storage__Provider"] = "Local", ["Storage__LocalRoot"] = path });
        var cookies = await Login(host, h);
        var draftId = Guid.NewGuid();
        var stage = new
        {
            OperationId = Guid.NewGuid(),
            DraftId = draftId,
            ExpectedDraftSetVersion = (string?)null,
            ContentRefs = new[] { file },
            Reason = "登记待绑定的准确原件",
        };
        const string route = "/api/file-content/owner-v1";
        var draft = await Post("/drafts", stage, HttpStatusCode.OK);
        Assert.Equal("DRAFT", draft.GetProperty("state").GetString());
        var draftVersion = draft.GetProperty("draftSetVersion").GetString();
        Assert.Equal(file.Id, draft.GetProperty("contentRefs")[0].GetProperty("id").GetGuid());

        var bind = new
        {
            OperationId = Guid.NewGuid(), DraftId = draftId, ExpectedDraftSetVersion = draftVersion,
            Target = upload.Parent, ContentRefs = new[] { file }, Reason = "提交准确草稿全集",
        };
        var result = await Post("/drafts/bind", bind, HttpStatusCode.OK);
        Assert.Equal("BOUND", result.GetProperty("state").GetString());
        Assert.Equal(upload.Parent.Id, result.GetProperty("target").GetProperty("id").GetGuid());
        Assert.Equal(file.Digest, result.GetProperty("contentRefs")[0].GetProperty("digest").GetString());
        Assert.Equal(result.GetRawText(), (await Post("/drafts/bind", bind, HttpStatusCode.OK)).GetRawText());
        Assert.Equal(result.GetRawText(), (await Get("/draft-operations/" + bind.OperationId)).GetRawText());
        await Post("/drafts/bind", bind with { Reason = "同一原键不能改输入" }, HttpStatusCode.Conflict);
        await Post("/drafts", stage with { OperationId = Guid.NewGuid(), ExpectedDraftSetVersion = draftVersion }, HttpStatusCode.Conflict);

        async Task<JsonElement> Post(string suffix, object body, HttpStatusCode expected)
        {
            using var request = OperationsRequest(cookies, HttpMethod.Post, route + suffix);
            request.Content = JsonContent.Create(body);
            using var response = await host.Send(request);
            Assert.Equal(expected, response.StatusCode);
            if (expected != HttpStatusCode.OK) return default;
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            return json.RootElement.GetProperty("data").Clone();
        }

        async Task<JsonElement> Get(string suffix)
        {
            using var response = await host.Send(OperationsRequest(cookies, HttpMethod.Get, route + suffix));
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            return json.RootElement.GetProperty("data").Clone();
        }
    }

    [CurrentPermissionPostgresFact]
    public async Task FileDraft_committed_result_survives_lost_response_and_current_write_revocation()
    {
        var fault = new CommitFault();
        await using var h = await Seed(fault);
        byte[] bytes = [1, 2, 3];
        var upload = await ContentInput(h, bytes);
        var owner = ContentOwner(h, new ContentStore(), new ContentInspector());
        await owner.UploadAsync(upload, new MemoryStream(bytes));
        var content = (await owner.ReadAsync(upload.FileId)).ContentRef;
        var draft = await owner.SaveDraftAsync(new(Guid.NewGuid(), Guid.NewGuid(), null, [content], "提交前保存草稿"));
        var bind = new FileDraftBindV1(Guid.NewGuid(), draft.DraftId!.Value, draft.DraftSetVersion!, upload.Parent, [content], "按原键绑定");
        fault.Armed = true;
        Assert.Equal("OPERATIONS_OUTCOME_UNKNOWN", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => owner.BindDraftAsync(bind))).Message);

        var fileMenu = await h.Db.Sys_Menus.Where(m => m.MenuKey == "file-content").Select(m => m.MenuId).SingleAsync();
        await h.Db.Sys_RoleActions.Where(a => a.MenuId == fileMenu && a.ActionCode == "edit").ExecuteDeleteAsync();
        var previous = await owner.ReadDraftOperationAsync(bind.OperationId);
        Assert.Equal("BOUND", previous.State);
        Assert.Equivalent(previous, await owner.BindDraftAsync(bind), strict: true);
        Assert.Equal(content, Assert.Single(await owner.ListBindingsAsync(upload.Parent)));
        Assert.Equal(403, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() =>
            owner.SaveDraftAsync(new(Guid.NewGuid(), Guid.NewGuid(), null, [content], "撤权后不可新增")))).Status);

        var other = new Sys_User { UserName = "另一个文件操作者", Password = "unused", RoleId = 72 };
        h.Db.Sys_Users.Add(other);
        await h.Db.SaveChangesAsync();
        await h.Db.Sys_RoleDataScopes.Where(s => s.RoleId == 72 && s.ResourceKey == "file-content")
            .ExecuteUpdateAsync(c => c.SetProperty(s => s.ScopeType, 1));
        var viewer = h with { User = other.Id, Http = new DefaultHttpContext { User = CurrentPermissionContextTests.Principal(other.Id, h.Tenant) } };
        var scoped = ContentOwner(viewer, new ContentStore(), new ContentInspector());
        Assert.Empty(await scoped.ListBindingsAsync(upload.Parent));
        Assert.Equal(403, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => scoped.ReadDraftAsync(bind.DraftId))).Status);
    }

    [CurrentPermissionPostgresFact]
    public async Task FileDraft_checks_whole_set_and_keeps_exact_prior_target_version()
    {
        await using var h = await Seed();
        byte[] bytes = [3, 2, 1];
        var upload = await ContentInput(h, bytes);
        var storage = new ContentStore();
        var scan = new ContentInspector();
        var owner = ContentOwner(h, storage, scan);
        await owner.UploadAsync(upload, new MemoryStream(bytes));
        var good = (await owner.ReadAsync(upload.FileId)).ContentRef;
        scan.Status = "PENDING";
        var pending = upload with { OperationId = Guid.NewGuid(), FileId = Guid.NewGuid() };
        await owner.UploadAsync(pending, new MemoryStream(bytes));
        var unavailable = (await owner.ReadAsync(pending.FileId)).ContentRef;
        var operation = Guid.NewGuid();
        var draftId = Guid.NewGuid();
        var stage = new FileDraftCommandV1(operation, draftId, null, [good, unavailable], "逐项核对草稿全集");
        Assert.Equal("FILE_CONTENT_NOT_AVAILABLE", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => owner.SaveDraftAsync(stage))).Message);
        Assert.Equal("UNKNOWN", (await owner.ReadDraftOperationAsync(operation)).State);

        var draft = await owner.SaveDraftAsync(stage with { ContentRefs = [good] });
        var bind = new FileDraftBindV1(Guid.NewGuid(), draftId, draft.DraftSetVersion!, upload.Parent, [good], "首次准确绑定");
        await using var peer = server.Context(new TenantContext { CurrentTenantId = h.Tenant });
        var peerOwner = ContentOwner(h, storage, scan, peer);
        var results = await Task.WhenAll(owner.BindDraftAsync(bind), peerOwner.BindDraftAsync(bind));
        Assert.All(results, result => Assert.Equal("BOUND", result.State));
        Assert.Equal(good, Assert.Single(await owner.ListBindingsAsync(upload.Parent)));

        var engineering = await BomRevisions(h).ReadAsync(upload.Parent.Id, null);
        await BomRevisions(h).CommandAsync(PatchRevision(engineering,
            new EngineeringDraftPatchV1 { Op = "SET_CONFIGURATION", Configuration = new([], "APPLIES", "修订后仍保留原附件集合") }));
        var current = await BomRevisions(h).ReadAsync(upload.Parent.Id, null);
        Assert.NotEqual(upload.Parent, current.Reference);
        Assert.Equal(good, Assert.Single(await owner.ListBindingsAsync(upload.Parent)));
        Assert.Empty(await owner.ListBindingsAsync(current.Reference));

        var next = await owner.SaveDraftAsync(stage with { OperationId = Guid.NewGuid(), DraftId = Guid.NewGuid(), ContentRefs = [good] });
        var second = bind with { OperationId = Guid.NewGuid(), DraftId = next.DraftId!.Value, ExpectedDraftSetVersion = next.DraftSetVersion! };
        Assert.Equal(412, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => owner.BindDraftAsync(second))).Status);
        Assert.Equal("BOUND", (await owner.BindDraftAsync(second with { Target = current.Reference })).State);
        Assert.Equal(good, Assert.Single(await owner.ListBindingsAsync(current.Reference)));
    }


    [CurrentPermissionPostgresFact]
    public async Task FileDisposition_actual_http_requires_approved_policy_and_confirms_isolated_physical_result()
    {
        await using var h = await Seed();
        var bytes = Encoding.UTF8.GetBytes("仅测试数据库保留的模拟原件");
        var upload = await ContentInput(h, bytes);
        await EngineeringAction(h, "file-content", "delete");
        var path = Path.Combine(Environment.GetEnvironmentVariable("CP6_PERMISSION_PG_ROOT")!,
            "disposition-files", Guid.NewGuid().ToString("N"));
        var store = new LocalFileStore(path);
        var owner = ContentOwner(h, store, new ContentInspector());
        await PublishHistoricalFiles(h, () => owner.UploadAsync(upload, new MemoryStream(bytes)));

        var content = await owner.ReadAsync(upload.FileId);
        var diskFile = Assert.Single(Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories));
        await using var host = await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!,
            new Dictionary<string, string>
            {
                ["Storage__Provider"] = "Local",
                ["Storage__LocalRoot"] = path,
                ["FileContent__Disposition__ExclusiveLocalRoot"] = path,
            });
        var cookies = await Login(host, h);
        const string route = "/api/file-content/owner-v1/dispositions";
        var command = new
        {
            OperationId = Guid.NewGuid(), ContentRef = content.ContentRef, ExpectedStateVersion = content.StateVersion,
            ExpectedEtag = content.Etag, PolicyVersion = Guid.NewGuid(), RequestedMode = "PHYSICAL", Reason = "模拟法定保留期已满",
        };
        await Post(route, command, HttpStatusCode.Conflict);
        Assert.True(File.Exists(diskFile));

        var audit = new CP6.Core.Services.Sys.AuditRetentionV1(h.Db);
        await using (var transaction = await h.Db.Database.BeginTransactionAsync())
        {
            await audit.LockAsync(CancellationToken.None);
            var policy = await audit.PublishAsync(h.Tenant, h.User, "TECHNICAL_CONTENT_EVIDENCE",
                new(null, Guid.NewGuid(), Guid.NewGuid(), true, false, 1, null), CancellationToken.None);
            command = command with { PolicyVersion = policy.Version };
            await transaction.CommitAsync();
        }

        var draft = await Post("/api/file-content/owner-v1/drafts", new
        {
            OperationId = Guid.NewGuid(), DraftId = Guid.NewGuid(), ExpectedDraftSetVersion = (string?)null,
            ContentRefs = new[] { content.ContentRef }, Reason = "尚有草稿引用",
        }, HttpStatusCode.OK);
        await Post(route, command, HttpStatusCode.Conflict);
        await Post("/api/file-content/owner-v1/drafts", new
        {
            OperationId = Guid.NewGuid(), DraftId = draft.GetProperty("draftId").GetGuid(),
            ExpectedDraftSetVersion = draft.GetProperty("draftSetVersion").GetString(),
            ContentRefs = Array.Empty<FileContentRefV1>(), Reason = "取消草稿引用",
        }, HttpStatusCode.OK);

        var pending = await Post(route, command, HttpStatusCode.OK);
        Assert.Equal("DISPOSAL_PENDING", pending.GetProperty("state").GetString());
        Assert.True(File.Exists(diskFile));
        var completeRoute = route + "/" + command.OperationId + "/complete";
        var marker = Path.Combine(path, ".cp6-file-disposition-id");
        var storageId = await File.ReadAllTextAsync(marker);
        await File.WriteAllTextAsync(marker, "无效的处置存储身份");
        try
        {
            await Post(completeRoute, null, HttpStatusCode.ServiceUnavailable, "FILE_DISPOSITION_STORAGE_UNAVAILABLE");
            Assert.True(File.Exists(diskFile));
        }
        finally { await File.WriteAllTextAsync(marker, storageId); }

        var reference = Guid.NewGuid();
        Guid guardVersion;
        await using (var hold = await h.Db.Database.BeginTransactionAsync())
        {
            await audit.LockAsync(CancellationToken.None);
            guardVersion = await audit.SafeguardAsync(h.Tenant, h.User, "TECHNICAL_CONTENT_EVIDENCE",
                new(reference, content.ContentRef.Id.ToString("D"), "UNKNOWN", true, null), CancellationToken.None);
            await hold.CommitAsync();
        }
        await Post(completeRoute, null, HttpStatusCode.Conflict);
        Assert.True(File.Exists(diskFile));
        await using (var release = await h.Db.Database.BeginTransactionAsync())
        {
            await audit.LockAsync(CancellationToken.None);
            await audit.SafeguardAsync(h.Tenant, h.User, "TECHNICAL_CONTENT_EVIDENCE",
                new(reference, content.ContentRef.Id.ToString("D"), "UNKNOWN", false, guardVersion), CancellationToken.None);
            await release.CommitAsync();
        }
        var recoveryFile = diskFile + ".fixture-recovery";
        File.Move(diskFile, recoveryFile);
        try { await Post(completeRoute, null, HttpStatusCode.ServiceUnavailable, "FILE_CONTENT_INTEGRITY_UNAVAILABLE"); }
        finally { File.Move(recoveryFile, diskFile); }
        using (var operation = await host.Send(OperationsRequest(cookies, HttpMethod.Get, route + "/" + command.OperationId)))
        {
            Assert.Equal(HttpStatusCode.OK, operation.StatusCode);
            using var json = JsonDocument.Parse(await operation.Content.ReadAsStringAsync());
            Assert.Equal("DISPOSAL_PENDING", json.RootElement.GetProperty("data").GetProperty("state").GetString());
        }
        var completed = await Post(completeRoute, null, HttpStatusCode.OK);
        Assert.Equal("DELETED", completed.GetProperty("state").GetString());
        Assert.False(File.Exists(diskFile));
        Assert.Equal(completed.GetRawText(), (await Post(completeRoute, null, HttpStatusCode.OK)).GetRawText());
        Assert.Equal(pending.GetRawText(), (await Post(route, command, HttpStatusCode.OK)).GetRawText());

        async Task<JsonElement> Post(string url, object? body, HttpStatusCode expected, string? error = null)
        {
            using var request = OperationsRequest(cookies, HttpMethod.Post, url);
            if (body is not null) request.Content = JsonContent.Create(body);
            using var response = await host.Send(request);
            Assert.Equal(expected, response.StatusCode);
            if (expected != HttpStatusCode.OK && error is null) return default;
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            if (error is not null)
            {
                Assert.Equal(error, json.RootElement.GetProperty("message").GetString());
                return default;
            }
            return json.RootElement.GetProperty("data").Clone();
        }
    }

    [CurrentPermissionPostgresFact]
    public async Task FileDisposition_holds_actual_document_and_rma_references()
    {
        await using var h = await Seed();
        var storage = new ContentStore();
        var document = await PublishHistoricalFiles(h, () => DocumentInput(h, storage));
        await Documents(h, storage).CommandAsync(document);
        var saved = await Documents(h, storage).ReadAsync(document.DocumentId);
        await VerifyReferencedFiles(h, storage, saved.Body.Contents.Select(member => member.File.Id));

        await using var logistics = await Seed();
        var evidence = await PublishHistoricalFiles(logistics, () => DeliveryEvidenceSetup(logistics));
        await evidence.Owner.RecordDeliveryEvidenceAsync(Guid.NewGuid(), evidence.Input);
        await VerifyReferencedFiles(logistics, storage, [Guid.Parse(Assert.Single(evidence.Input.FileRefs).Id)]);

        static async Task VerifyReferencedFiles(Harness ownerContext, IFileStore files, IEnumerable<Guid> ids)
        {
            await EngineeringAction(ownerContext, "file-content", "delete");
            var owner = ContentOwner(ownerContext, files, new ContentInspector());
            var audit = new CP6.Core.Services.Sys.AuditRetentionV1(ownerContext.Db);
            Guid version;
            await using (var transaction = await ownerContext.Db.Database.BeginTransactionAsync())
            {
                await audit.LockAsync(CancellationToken.None);
                var published = await audit.PublishAsync(ownerContext.Tenant, ownerContext.User, "TECHNICAL_CONTENT_EVIDENCE",
                    new(null, Guid.NewGuid(), Guid.NewGuid(), true, false, 1, null), CancellationToken.None);
                version = published.Version;
                await transaction.CommitAsync();
            }
            foreach (var id in ids)
            {
                var current = await owner.ReadAsync(id);
                var attempt = new FileDispositionCommandV1(Guid.NewGuid(), current.ContentRef, current.StateVersion,
                    current.Etag, version, "UNLINK_ONLY", "实际引用应当阻断处置");
                var error = await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => owner.RequestDispositionAsync(attempt));
                Assert.Equal("FILE_DISPOSITION_REFERENCED", error.Message);
                Assert.Equal("UNKNOWN", (await owner.ReadDispositionAsync(attempt.OperationId)).State);
                Assert.Equal("AVAILABLE", (await owner.ReadAsync(id)).Availability);
            }
        }
    }

    private static async Task<T> PublishHistoricalFiles<T>(Harness h, Func<Task<T>> create)
    {
        await h.Db.Database.ExecuteSqlRawAsync("""
            CREATE FUNCTION public.fixture_wp02_14b_old_file() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN NEW.recorded_at := clock_timestamp() - interval '3 days'; RETURN NEW; END $$;
            CREATE TRIGGER fixture_old_file BEFORE INSERT ON cp6_file_content_v1.events
              FOR EACH ROW EXECUTE FUNCTION public.fixture_wp02_14b_old_file();
            """);
        try { return await create(); }
        finally
        {
            await h.Db.Database.ExecuteSqlRawAsync("""
                DROP TRIGGER fixture_old_file ON cp6_file_content_v1.events;
                DROP FUNCTION public.fixture_wp02_14b_old_file();
                """);
        }
    }

}
