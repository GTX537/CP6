using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CP6.Core.Services.Platform;
using CP6.Core.Services.Pub;
using CP6.Core.Services.Sys;
using CP6.Entity.DomainModels.Pub;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    [CurrentPermissionPostgresFact]
    public async Task PrivacyFile_current_global_hold_audit_and_legacy_archive_preserve_original_until_all_release()
    {
        await using var fileTenant = await Seed();
        await using var archiveTenant = await Seed();
        await PrivacyAuthority(archiveTenant);
        await EngineeringAction(archiveTenant, "privacy-disposition", "hold");
        await EngineeringAction(archiveTenant, "privacy-disposition", "read");
        await EngineeringAction(fileTenant, "file-content", "delete");

        var bytes = Encoding.UTF8.GetBytes("模拟技术原件与历史归档共用的字节");
        var upload = await ContentInput(fileTenant, bytes);
        var root = Path.Combine(Environment.GetEnvironmentVariable("CP6_PERMISSION_PG_ROOT")!, "privacy-shared-files", Guid.NewGuid().ToString("N"));
        var storage = new LocalFileStore(root);
        var files = ContentOwner(fileTenant, storage, new ContentInspector());
        await PublishHistoricalFiles(fileTenant, () => files.UploadAsync(upload, new MemoryStream(bytes)));
        var content = await files.ReadAsync(upload.FileId);
        var diskFile = Assert.Single(Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories));
        var storePath = Path.GetRelativePath(root, diskFile).Replace('\\', '/');

        var audit = new AuditRetentionV1(fileTenant.Db);
        Guid policyVersion;
        await using (var policyTransaction = await fileTenant.Db.Database.BeginTransactionAsync())
        {
            await audit.LockAsync(CancellationToken.None);
            var policy = await audit.PublishAsync(fileTenant.Tenant, fileTenant.User, "TECHNICAL_CONTENT_EVIDENCE",
                new(null, Guid.NewGuid(), Guid.NewGuid(), true, false, 1, null), CancellationToken.None);
            policyVersion = policy.Version;
            await policyTransaction.CommitAsync();
        }

        var settings = SetHostSettings();
        settings["Storage__Provider"] = "Local";
        settings["Storage__LocalRoot"] = root;
        settings["FileContent__Disposition__ExclusiveLocalRoot"] = root;
        await using var host = await AuthenticationApiProcess.Start(fileTenant.Db.Database.GetConnectionString()!, settings);
        var fileActor = await Login(host, fileTenant);
        var privacyActor = await Login(host, archiveTenant);
        const string dispositionRoute = "/api/file-content/owner-v1/dispositions";
        const string holdRoute = "/api/platform/privacy-dispositions-v1/holds";
        var command = new FileDispositionCommandV1(Guid.NewGuid(), content.ContentRef, content.StateVersion,
            content.Etag, policyVersion, "PHYSICAL", "模拟留存期届满后确认物理处置");
        var original = await Send(fileActor, HttpMethod.Post, dispositionRoute, command);
        Assert.Equal("DISPOSAL_PENDING", original.GetProperty("data").GetProperty("state").GetString());
        var originalRoute = $"{dispositionRoute}/{command.OperationId:D}";
        var completeRoute = originalRoute + "/complete";

        var hold = new PrivacyDispositionHoldCommandV1(Guid.NewGuid(), fileTenant.Tenant, fileTenant.User,
            null, true, Guid.NewGuid(), PrivacyReviewTime());
        var held = await Send(privacyActor, HttpMethod.Post, holdRoute, hold);
        var heldVersion = held.GetProperty("version").GetGuid();
        await AssertBlocked("FILE_RETENTION_HOLD_ACTIVE");

        var safeguard = Guid.NewGuid();
        var auditVersion = await ChangeAuditReference(true, null);
        await Send(privacyActor, HttpMethod.Post, holdRoute, hold with
        {
            OperationId = Guid.NewGuid(), ExpectedVersion = heldVersion, Active = false, BasisRef = Guid.NewGuid(), ReviewAt = null,
        });
        await AssertBlocked("FILE_RETENTION_HOLD_ACTIVE");

        // 模拟升级前已经存在的归档引用，另一个租户正常读取这些原字节。
        var archive = new Pub_Attachment
        {
            Id = Guid.NewGuid(), BizType = "technical-archive", BizId = "模拟原归档",
            FileName = "历史归档.txt", StorePath = storePath, StoreName = storePath, Size = bytes.Length,
            ContentType = "text/plain", FileHash = Convert.ToHexString(MD5.HashData(bytes)).ToLowerInvariant(), CreateDate = DateTime.Now,
        };
        archiveTenant.Db.Pub_Attachments.Add(archive);
        await archiveTenant.Db.SaveChangesAsync();
        archiveTenant.Db.ChangeTracker.Clear();
        var attachments = new AttachmentService(archiveTenant.Db, storage);
        var shared = await attachments.DownloadAsync(archive.Id);
        await using (var stream = shared.stream)
        {
            using var actual = new MemoryStream();
            await stream.CopyToAsync(actual);
            Assert.Equal(bytes, actual.ToArray());
        }

        await ChangeAuditReference(false, auditVersion);
        await AssertBlocked("FILE_DISPOSITION_REFERENCED");
        await attachments.DeleteAsync(archive.Id);
        Assert.Null(await attachments.FindAsync(archive.Id));
        Assert.True(File.Exists(diskFile));

        var completion = await Send(fileActor, HttpMethod.Post, completeRoute);
        Assert.Equal("DELETED", completion.GetProperty("data").GetProperty("state").GetString());
        Assert.False(File.Exists(diskFile));
        Assert.Equal(completion.GetRawText(), (await Send(fileActor, HttpMethod.Post, completeRoute)).GetRawText());
        Assert.Equal(original.GetRawText(), (await Send(fileActor, HttpMethod.Post, dispositionRoute, command)).GetRawText());
        var closed = await files.ReadAsync(upload.FileId);
        Assert.Equal(content.ContentRef, closed.ContentRef);
        Assert.Equal("DELETED", closed.Availability);
        var download = $"/api/file-content/owner-v1/{content.ContentRef.Id:D}/download?version=1&digest={content.ContentRef.Digest}";
        var unavailable = await Send(fileActor, HttpMethod.Get, download, status: HttpStatusCode.Conflict);
        Assert.Equal("FILE_CONTENT_NOT_AVAILABLE", unavailable.GetProperty("message").GetString());

        async Task<Guid> ChangeAuditReference(bool active, Guid? expected)
        {
            await using var transaction = await fileTenant.Db.Database.BeginTransactionAsync();
            await audit.LockAsync(CancellationToken.None);
            var changed = await audit.SafeguardAsync(fileTenant.Tenant, fileTenant.User, "TECHNICAL_CONTENT_EVIDENCE",
                new(safeguard, content.ContentRef.Id.ToString("D"), "REQUIRED_REFERENCE", active, expected), CancellationToken.None);
            await transaction.CommitAsync();
            return changed;
        }

        async Task AssertBlocked(string reason)
        {
            var blocked = await Send(fileActor, HttpMethod.Post, completeRoute, status: HttpStatusCode.Conflict);
            Assert.Equal(reason, blocked.GetProperty("message").GetString());
            Assert.True(File.Exists(diskFile));
            var pending = await Send(fileActor, HttpMethod.Get, originalRoute);
            Assert.Equal(original.GetRawText(), pending.GetRawText());
        }

        async Task<JsonElement> Send(Dictionary<string, string> actor, HttpMethod method, string route, object? input = null,
            HttpStatusCode status = HttpStatusCode.OK)
        {
            using var response = await host.Send(OperationsRequest(actor, method, route, input));
            var body = await response.Content.ReadAsStringAsync();
            Assert.True(response.StatusCode == status, $"{method} {route}: {(int)response.StatusCode}, {body}");
            using var json = JsonDocument.Parse(body);
            return json.RootElement.Clone();
        }
    }
}
