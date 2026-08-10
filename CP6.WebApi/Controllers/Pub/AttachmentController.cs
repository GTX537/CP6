using CP6.Core.Services.Pub;
using CP6.Core.Services.Sys;
using CP6.Entity.DomainModels.Pub;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CP6.WebApi.Controllers.Pub;

/// <summary>统一附件 REST —— PUB 章06。/api/pub/attachment</summary>
[ApiController]
[Route("api/pub/attachment")]
[Authorize]
public class AttachmentController : LocalizedControllerBase
{
    private readonly IAttachmentService _svc;
    private readonly IPermissionService _perm;
    private readonly bool _enforceBizPerm;

    public AttachmentController(IAttachmentService svc, IPermissionService perm, IConfiguration cfg)
    {
        _svc = svc;
        _perm = perm;
        _enforceBizPerm = cfg.GetValue<bool?>("Attachment:EnforceBizPermission") ?? true;
    }

    private string? CurrentUser => User?.Identity?.Name;

    private async Task<bool> CanAccessBizAsync(string? bizType) =>
        !_enforceBizPerm
        || (!string.IsNullOrWhiteSpace(bizType) && await _perm.HasMenuAsync(bizType));

    private ObjectResult BizPermissionDenied() =>
        StatusCode(403, new { code = 403, message = "E-PUB-063" });

    /// <summary>上传（multipart）。bizId 已知直传；草稿期传 draftToken。</summary>
    [HttpPost("upload")]
    [Consumes("multipart/form-data")]   // Swashbuckle 生成 multipart/IFormFile 操作所需
    public async Task<IActionResult> Upload(IFormFile file, [FromForm] string bizType,
        [FromForm] string? bizId, [FromForm] string? draftToken)
    {
        if (!await CanAccessBizAsync(bizType)) return BizPermissionDenied();
        if (file == null || file.Length == 0) return BadRequest(new { code = 400, message = Localizer["空文件"] });
        try
        {
            using var s = file.OpenReadStream();
            var att = await _svc.UploadAsync(s, file.FileName, file.ContentType, bizType, bizId, draftToken, CurrentUser);
            return Ok(new { code = 0, message = "OK", data = att });
        }
        catch (InvalidOperationException e) { return BadRequest(new { code = 400, message = e.Message }); }
    }

    /// <summary>按业务取附件列表</summary>
    [HttpGet("list")]
    public async Task<IActionResult> List(string bizType, string bizId)
    {
        if (!await CanAccessBizAsync(bizType)) return BizPermissionDenied();
        return Ok(new { code = 0, message = "OK", data = await _svc.ListAsync(bizType, bizId) });
    }

    /// <summary>下载：先按附件 BizType 回查宿主菜单，再打开物理文件。</summary>
    [HttpGet("{id}/download")]
    public async Task<IActionResult> Download(Guid id) => await Stream(id, asAttachment: true);

    /// <summary>预览（inline，供图片/PDF 内联）</summary>
    [HttpGet("{id}/preview")]
    public async Task<IActionResult> Preview(Guid id) => await Stream(id, asAttachment: false);

    private async Task<IActionResult> Stream(Guid id, bool asAttachment)
    {
        var att = await _svc.FindAsync(id);
        if (att == null) return NotFound();
        if (!await CanAccessBizAsync(att.BizType)) return BizPermissionDenied();

        Stream stream;
        try { (_, stream) = await _svc.DownloadAsync(id); }
        catch (InvalidOperationException) { return NotFound(); }
        catch (FileNotFoundException) { return NotFound(); }

        var contentType = att.ContentType ?? "application/octet-stream";
        return asAttachment
            ? File(stream, contentType, att.FileName)        // 带 Content-Disposition: attachment; filename
            : File(stream, contentType);                      // inline 预览
    }

    /// <summary>删除（引用计数后物理删由 service 决定）</summary>
    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var att = await _svc.FindAsync(id);
        if (att == null) return NotFound();
        if (!await CanAccessBizAsync(att.BizType)) return BizPermissionDenied();

        await _svc.DeleteAsync(id);
        return Ok(new { code = 0, message = "OK" });
    }

    /// <summary>草稿转正：把 draftToken 的附件回填 BizId（业务单据保存后调）</summary>
    [HttpPost("rebind")]
    public async Task<IActionResult> Rebind([FromBody] RebindReq r)
    {
        var attachments = await _svc.ListDraftAsync(r.DraftToken);
        if (_enforceBizPerm && attachments.Count > 0)
        {
            if (string.IsNullOrWhiteSpace(CurrentUser)
                || attachments.Any(a => !string.Equals(
                    a.Uploader, CurrentUser, StringComparison.OrdinalIgnoreCase)))
                return BizPermissionDenied();

            foreach (var bizType in attachments.Select(a => a.BizType).Distinct(StringComparer.Ordinal))
                if (!await CanAccessBizAsync(bizType))
                    return BizPermissionDenied();
        }

        await _svc.RebindAsync(r.DraftToken, r.BizId);
        return Ok(new { code = 0, message = "OK" });
    }

    public record RebindReq(string DraftToken, string BizId);
}
