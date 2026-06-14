using CP6.Core.Services.Sys;
using CP6.Core.Services.Wf;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CP6.WebApi.Controllers.Wf;

/// <summary>表单 REST（OA 章02）。/api/wf —— 表单定义建取 + 数据提交（服务端 schema 复核）。</summary>
[ApiController]
[Route("api/wf")]
[Authorize]
public class FormController : LocalizedControllerBase
{
    private readonly IFormService _svc;
    public FormController(IFormService svc) => _svc = svc;

    private string? CurrentUser => User?.Identity?.Name;
    private IActionResult Ok2(object? data = null) => Ok(new { code = 0, message = "OK", data });
    private IActionResult Err(InvalidOperationException e) => BadRequest(new { code = 400, message = e.Message });

    [HttpPost("form/def")]
    public async Task<IActionResult> SaveDef([FromBody] FormDefReq r)
    {
        try { return Ok2(new { id = await _svc.SaveDefAsync(r.FormKey, r.FormName, r.SchemaJson, CurrentUser) }); }
        catch (InvalidOperationException e) { return Err(e); }
    }

    [HttpGet("form/def/{formKey}")]
    public async Task<IActionResult> GetDef(string formKey)
    {
        var def = await _svc.GetDefAsync(formKey);
        return def is null ? NotFound(new { code = 404, message = Localizer["表单定义不存在"] }) : Ok2(def);
    }

    [HttpPost("form/data")]
    public async Task<IActionResult> SubmitData([FromBody] FormDataReq r)
    {
        try { return Ok2(new { id = await _svc.SubmitDataAsync(r.FormKey, r.BizId, r.DataJson, CurrentUser) }); }
        catch (InvalidOperationException e) { return Err(e); }
    }

    public record FormDefReq(string FormKey, string FormName, string SchemaJson);
    public record FormDataReq(string FormKey, string? BizId, string DataJson);
}
