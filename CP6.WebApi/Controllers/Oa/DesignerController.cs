using CP6.Core.Services.Oa;
using CP6.Core.Services.Sys;
using CP6.Core.Services.Wf;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CP6.WebApi.Controllers.Oa;

/// <summary>
/// 流程设计器 REST（Phase C′）。/api/oa/designer — 列表/加载/保存/克隆。
/// 操作对象为流程定义 Wf_FlowDef；消费 IDesignerService（校验+upsert）+ IFlowDefService（读 SchemaJson）。
/// </summary>
[ApiController]
[Route("api/oa/designer")]
[Authorize]
public class DesignerController : LocalizedControllerBase
{
    private readonly IDesignerService _designer;
    private readonly IFlowDefService _flowDef;
    private readonly ICurrentPermissionContext _ctx;

    public DesignerController(IDesignerService designer, IFlowDefService flowDef, ICurrentPermissionContext ctx)
    {
        _designer = designer;
        _flowDef = flowDef;
        _ctx = ctx;
    }

    private IActionResult Ok2(object? data = null) => Ok(new { code = 0, message = "OK", data });
    private IActionResult Err(InvalidOperationException e) => BadRequest(new { code = 400, message = e.Message });

    [HttpGet("list")]
    public async Task<IActionResult> List([FromQuery] string? functionId)
        => Ok2(await _designer.ListAsync(functionId));

    [HttpGet("service-catalog")]
    public IActionResult ServiceCatalog()
        => Ok2(_designer.GetServiceCatalog());   // P1-6：按 Kind/VisibleInDesigner 过滤的回写动作 + 连接器

    [HttpGet("load/{flowKey}")]
    public async Task<IActionResult> Load(string flowKey)
    {
        var summary = await _designer.LoadAsync(flowKey);
        if (summary is null) return NotFound(new { code = 404, message = "E-WF-006" });
        var def = await _flowDef.GetDefAsync(flowKey);
        return Ok2(new { summary, schemaJson = def?.SchemaJson ?? "{}" });
    }

    public record SaveReq(string FlowKey, string FlowName, string FormKey, string? FunctionId, string? FlowCode, string SchemaJson);

    [HttpPost("save")]
    public async Task<IActionResult> Save([FromBody] SaveReq r)
    {
        try
        {
            var user = (await _ctx.GetAsync()).UserId.ToString();
            await _designer.SaveAsync(new SaveFlowRequest(r.FlowKey, r.FlowName, r.FormKey, r.FunctionId, r.FlowCode, r.SchemaJson), user);
            return Ok2(true);
        }
        catch (InvalidOperationException e) { return Err(e); }
    }

    public record CloneReq(string SourceFlowKey, string NewFlowKey, string NewFlowName);

    [HttpPost("clone")]
    public async Task<IActionResult> Clone([FromBody] CloneReq r)
    {
        try
        {
            var user = (await _ctx.GetAsync()).UserId.ToString();
            await _designer.CloneAsync(new CloneRequest(r.SourceFlowKey, r.NewFlowKey, r.NewFlowName), user);
            return Ok2(true);
        }
        catch (InvalidOperationException e) { return Err(e); }
    }
}
