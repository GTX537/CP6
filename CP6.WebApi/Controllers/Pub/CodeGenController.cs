using CP6.Core.Auth;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Pub;
using CP6.Entity.DomainModels.Pub;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CP6.WebApi.Controllers.Pub;

/// <summary>代码生成 REST —— PUB 章08。/api/pub/codegen</summary>
[ApiController]
[Route("api/pub/codegen")]
[Authorize]
public class CodeGenController : ControllerBase
{
    private readonly CP6Context _db;
    private readonly CodeGenService _gen;
    public CodeGenController(CP6Context db, CodeGenService gen) { _db = db; _gen = gen; }

    /// <summary>表元数据列表</summary>
    [HttpGet("tables")]
    public async Task<IActionResult> Tables() =>
        Ok(new { code = 0, data = await _db.Pub_GenTables.OrderBy(x => x.EntityName).ToListAsync() });

    /// <summary>保存表元数据 + 列（整体 upsert）</summary>
    [HttpPost("save")]
    [RequirePermission("pub-codegen", "save")]
    public async Task<IActionResult> Save([FromBody] GenSaveReq req)
    {
        var t = req.Table;
        var existing = await _db.Pub_GenTables.FirstOrDefaultAsync(x => x.EntityName == t.EntityName);
        if (existing == null)
        {
            t.Id = t.Id == Guid.Empty ? Guid.NewGuid() : t.Id;
            t.CreateDate = DateTime.Now;
            _db.Pub_GenTables.Add(t);
        }
        else
        {
            t.Id = existing.Id;
            _db.Entry(existing).CurrentValues.SetValues(t);
        }
        // 整体替换列
        var oldCols = await _db.Pub_GenColumns.Where(c => c.GenTableId == t.Id).ToListAsync();
        _db.Pub_GenColumns.RemoveRange(oldCols);
        foreach (var c in req.Columns)
        {
            c.Id = Guid.NewGuid();
            c.GenTableId = t.Id;
            _db.Pub_GenColumns.Add(c);
        }
        await _db.SaveChangesAsync();
        return Ok(new { code = 0, data = new { t.Id } });
    }

    /// <summary>预览生成产物（持久化元数据）</summary>
    [HttpGet("{id}/preview")]
    public async Task<IActionResult> Preview(Guid id)
    {
        var t = await _db.Pub_GenTables.FindAsync(id);
        if (t == null) return NotFound();
        var cols = await _db.Pub_GenColumns.Where(c => c.GenTableId == id).OrderBy(c => c.Sort).ToListAsync();
        return Ok(new { code = 0, data = _gen.Generate(t, cols) });
    }

    /// <summary>即时预览（不持久化，body 直接给元数据）</summary>
    [HttpPost("preview")]
    [RequirePermission("pub-codegen", "view")]
    public IActionResult PreviewInline([FromBody] GenSaveReq req) =>
        Ok(new { code = 0, data = _gen.Generate(req.Table, req.Columns) });

    public record GenSaveReq(GenTable Table, List<GenColumn> Columns);
}
