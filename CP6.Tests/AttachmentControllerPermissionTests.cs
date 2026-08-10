using System.Security.Claims;
using CP6.Core.Services.Pub;
using CP6.Core.Services.Sys;
using CP6.Entity.DomainModels.Pub;
using CP6.WebApi.Controllers.Pub;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;

namespace CP6.Tests;

public sealed class AttachmentControllerPermissionTests
{
    [Fact]
    public async Task Upload_DefaultsToFailClosedBusinessMenuCheck()
    {
        var service = new RecordingAttachmentService();
        var controller = CreateController(service, new RecordingPermissionService());

        var result = await controller.Upload(File(), "erp-order", "o-1", null);

        AssertForbidden(result);
        Assert.Equal(0, service.UploadCalls);
    }

    [Fact]
    public async Task Upload_WithHostMenuPermission_ReachesService()
    {
        var service = new RecordingAttachmentService();
        var permissions = new RecordingPermissionService("erp-order");
        var controller = CreateController(service, permissions);

        var result = await controller.Upload(File(), "erp-order", "o-1", null);

        Assert.IsType<OkObjectResult>(result);
        Assert.Equal(1, service.UploadCalls);
        Assert.Equal("alice", service.LastUploader);
    }

    [Fact]
    public async Task ExplicitCompatibilityOptOut_BypassesBusinessMenuCheck()
    {
        var service = new RecordingAttachmentService();
        var controller = CreateController(
            service,
            new RecordingPermissionService(),
            enforceBizPermission: false);

        var result = await controller.Upload(File(), "legacy-biz-type", "o-1", null);

        Assert.IsType<OkObjectResult>(result);
        Assert.Equal(1, service.UploadCalls);
    }

    [Fact]
    public async Task List_DeniesMetadataBeforeQueryingService()
    {
        var service = new RecordingAttachmentService();
        var controller = CreateController(service, new RecordingPermissionService());

        var result = await controller.List("erp-order", "o-1");

        AssertForbidden(result);
        Assert.Equal(0, service.ListCalls);
    }

    [Fact]
    public async Task Preview_DeniesBeforeOpeningPhysicalFile()
    {
        var service = new RecordingAttachmentService
        {
            FindResult = Attachment("erp-order", "alice"),
        };
        var controller = CreateController(service, new RecordingPermissionService());

        var result = await controller.Preview(service.FindResult.Id);

        AssertForbidden(result);
        Assert.Equal(0, service.DownloadCalls);
    }

    [Fact]
    public async Task Delete_UsesStoredBusinessTypeAndDeniesBeforeMutation()
    {
        var service = new RecordingAttachmentService
        {
            FindResult = Attachment("erp-order", "alice"),
        };
        var permissions = new RecordingPermissionService("pur-po");
        var controller = CreateController(service, permissions);

        var result = await controller.Delete(service.FindResult.Id);

        AssertForbidden(result);
        Assert.Equal(0, service.DeleteCalls);
        Assert.Contains("erp-order", permissions.CheckedMenus);
    }

    [Fact]
    public async Task Rebind_DeniesDraftOwnedByAnotherUser()
    {
        var service = new RecordingAttachmentService
        {
            DraftResults = [Attachment("erp-order", "mallory")],
        };
        var controller = CreateController(
            service,
            new RecordingPermissionService("erp-order"));

        var result = await controller.Rebind(new("draft-1", "o-1"));

        AssertForbidden(result);
        Assert.Equal(0, service.RebindCalls);
    }

    [Fact]
    public async Task Rebind_RequiresEveryDraftBusinessMenuBeforeMutation()
    {
        var service = new RecordingAttachmentService
        {
            DraftResults =
            [
                Attachment("erp-order", "alice"),
                Attachment("pur-po", "alice"),
            ],
        };
        var controller = CreateController(
            service,
            new RecordingPermissionService("erp-order"));

        var result = await controller.Rebind(new("draft-1", "o-1"));

        AssertForbidden(result);
        Assert.Equal(0, service.RebindCalls);
    }

    [Fact]
    public async Task Rebind_OwnedAuthorizedDraft_ReachesService()
    {
        var service = new RecordingAttachmentService
        {
            DraftResults = [Attachment("erp-order", "alice")],
        };
        var controller = CreateController(
            service,
            new RecordingPermissionService("erp-order"));

        var result = await controller.Rebind(new("draft-1", "o-1"));

        Assert.IsType<OkObjectResult>(result);
        Assert.Equal(1, service.RebindCalls);
    }

    private static AttachmentController CreateController(
        RecordingAttachmentService service,
        RecordingPermissionService permissions,
        bool? enforceBizPermission = null,
        string userName = "alice")
    {
        var settings = new Dictionary<string, string?>();
        if (enforceBizPermission.HasValue)
            settings["Attachment:EnforceBizPermission"] = enforceBizPermission.Value.ToString();

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings)
            .Build();
        var identity = new ClaimsIdentity(
            [new Claim(ClaimTypes.Name, userName)],
            authenticationType: "test");

        return new AttachmentController(service, permissions, configuration)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(identity),
                },
            },
        };
    }

    private static IFormFile File() =>
        new FormFile(new MemoryStream([1]), 0, 1, "file", "evidence.txt")
        {
            Headers = new HeaderDictionary(),
            ContentType = "text/plain",
        };

    private static Pub_Attachment Attachment(string bizType, string uploader) =>
        new()
        {
            Id = Guid.NewGuid(),
            BizType = bizType,
            Uploader = uploader,
            FileName = "evidence.txt",
            StoreName = "evidence.txt",
            StorePath = "evidence.txt",
            FileHash = new string('a', 32),
        };

    private static void AssertForbidden(IActionResult result) =>
        Assert.Equal(StatusCodes.Status403Forbidden, Assert.IsType<ObjectResult>(result).StatusCode);

    private sealed class RecordingPermissionService(params string[] allowedMenus) : IPermissionService
    {
        private readonly HashSet<string> _allowedMenus = new(allowedMenus, StringComparer.Ordinal);
        public List<string> CheckedMenus { get; } = [];

        public Task<bool> HasActionAsync(string menu, string action) => Task.FromResult(false);

        public Task<bool> HasMenuAsync(string menu)
        {
            CheckedMenus.Add(menu);
            return Task.FromResult(_allowedMenus.Contains(menu));
        }
    }

    private sealed class RecordingAttachmentService : IAttachmentService
    {
        public Pub_Attachment? FindResult { get; init; }
        public List<Pub_Attachment> DraftResults { get; init; } = [];
        public int UploadCalls { get; private set; }
        public int ListCalls { get; private set; }
        public int DownloadCalls { get; private set; }
        public int DeleteCalls { get; private set; }
        public int RebindCalls { get; private set; }
        public string? LastUploader { get; private set; }

        public Task<Pub_Attachment> UploadAsync(
            Stream content,
            string fileName,
            string? contentType,
            string bizType,
            string? bizId,
            string? draftToken,
            string? uploader)
        {
            UploadCalls++;
            LastUploader = uploader;
            return Task.FromResult(Attachment(bizType, uploader ?? string.Empty));
        }

        public Task<List<Pub_Attachment>> ListAsync(string bizType, string bizId)
        {
            ListCalls++;
            return Task.FromResult(new List<Pub_Attachment>());
        }

        public Task<Pub_Attachment?> FindAsync(Guid id) => Task.FromResult(FindResult);

        public Task<List<Pub_Attachment>> ListDraftAsync(string draftToken) =>
            Task.FromResult(DraftResults);

        public Task<(Pub_Attachment att, Stream stream)> DownloadAsync(Guid id)
        {
            DownloadCalls++;
            return Task.FromResult((FindResult!, (Stream)new MemoryStream([1])));
        }

        public Task DeleteAsync(Guid id)
        {
            DeleteCalls++;
            return Task.CompletedTask;
        }

        public Task RebindAsync(string draftToken, string bizId)
        {
            RebindCalls++;
            return Task.CompletedTask;
        }
    }
}
