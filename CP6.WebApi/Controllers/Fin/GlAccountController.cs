using CP6.Core.Services.Fin;
using CP6.Entity.DomainModels.Fin;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CP6.WebApi.Controllers.Fin;

/// <summary>会计科目 REST —— 财务章01 §3。/api/fin/account。科目不删只停用；模板包一键导入。</summary>
[ApiController]
[Route("api/fin/account")]
[Authorize]
public class GlAccountController : ControllerBase
{
    private readonly IGlAccountService _svc;
    public GlAccountController(IGlAccountService svc) => _svc = svc;

    private string? CurrentUser => User?.Identity?.Name;
    private IActionResult Ok2(object? data = null) => Ok(new { code = 0, message = "OK", data });
    private IActionResult Err(InvalidOperationException e) => BadRequest(new { code = 400, message = e.Message });

    /// <summary>科目列表（可按模板方案过滤；includeInactive=true 含停用）。</summary>
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] string? scheme, [FromQuery] bool includeInactive = false)
        => Ok2(await _svc.ListAsync(scheme, includeInactive));

    [HttpGet("{id}")]
    public async Task<IActionResult> Get(Guid id)
    {
        var a = await _svc.GetAsync(id);
        return a == null ? Err(new InvalidOperationException("E-FIN-003")) : Ok2(a);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] GlAccount a)
    {
        try { return Ok2(new { id = await _svc.CreateAsync(a, CurrentUser) }); }
        catch (InvalidOperationException e) { return Err(e); }
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] GlAccount a)
    {
        try { await _svc.UpdateAsync(id, a, CurrentUser); return Ok2(); }
        catch (InvalidOperationException e) { return Err(e); }
    }

    /// <summary>停用科目（不删）。</summary>
    [HttpPost("{id}/deactivate")]
    public async Task<IActionResult> Deactivate(Guid id)
    {
        try { await _svc.DeactivateAsync(id, CurrentUser); return Ok2(); }
        catch (InvalidOperationException e) { return Err(e); }
    }

    /// <summary>导入科目表模板包（scheme=CN-GAAP / INTL）。</summary>
    [HttpPost("import")]
    public async Task<IActionResult> Import([FromQuery] string scheme)
    {
        try { return Ok2(new { count = await _svc.ImportTemplateAsync(scheme, CurrentUser) }); }
        catch (InvalidOperationException e) { return Err(e); }
    }
}
