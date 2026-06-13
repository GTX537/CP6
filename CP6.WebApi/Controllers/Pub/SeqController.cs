using CP6.Core.EFDbContext;
using CP6.Entity.DomainModels.Pub;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CP6.WebApi.Controllers.Pub;

/// <summary>富采番规则配置 REST —— PUB 章05。/api/pub/seq</summary>
[ApiController]
[Route("api/pub/seq")]
[Authorize]
public class SeqController : ControllerBase
{
    private readonly CP6Context _db;
    public SeqController(CP6Context db) => _db = db;

    [HttpGet]
    public async Task<IActionResult> GetList(int page = 1, int pageSize = 10, string? keyword = null)
    {
        var q = _db.Pub_DocSequences.AsQueryable();
        if (!string.IsNullOrEmpty(keyword))
            q = q.Where(x => x.BizKey.Contains(keyword) || x.Prefix.Contains(keyword));
        var total = await q.CountAsync();
        var rows = await q.OrderBy(x => x.BizKey)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();
        return Ok(new { rows, total });
    }

    [HttpPost]
    public async Task<IActionResult> Add([FromBody] Pub_DocSequence entity)
    {
        if (await _db.Pub_DocSequences.AnyAsync(x => x.BizKey == entity.BizKey))
            return BadRequest(new { message = "业务键已存在" });
        entity.CreateDate = DateTime.Now;
        _db.Pub_DocSequences.Add(entity);
        await _db.SaveChangesAsync();
        return Ok(entity);
    }

    [HttpPut]
    public async Task<IActionResult> Update([FromBody] Pub_DocSequence entity)
    {
        var cur = await _db.Pub_DocSequences.FindAsync(entity.Id);
        if (cur == null) return NotFound();
        cur.Prefix = entity.Prefix;
        cur.DateFormat = entity.DateFormat;
        cur.SeqLength = entity.SeqLength;
        cur.ResetCycle = entity.ResetCycle;
        cur.ModifyDate = DateTime.Now;
        await _db.SaveChangesAsync();
        return Ok(cur);
    }

    [HttpDelete]
    public async Task<IActionResult> Delete([FromBody] Guid[] ids)
    {
        var rows = await _db.Pub_DocSequences.Where(x => ids.Contains(x.Id)).ToListAsync();
        _db.Pub_DocSequences.RemoveRange(rows);
        var count = await _db.SaveChangesAsync();
        return Ok(new { count });
    }

    /// <summary>预览号码格式（不消费流水）。</summary>
    [HttpGet("preview/{bizKey}")]
    public async Task<IActionResult> Preview(string bizKey)
    {
        var s = await _db.Pub_DocSequences.FirstOrDefaultAsync(x => x.BizKey == bizKey);
        if (s == null) return NotFound();
        var datePart = string.IsNullOrEmpty(s.DateFormat) ? "" : DateTime.Now.ToString(s.DateFormat);
        var sample = s.Prefix + datePart + (s.CurrentValue + 1).ToString().PadLeft(s.SeqLength, '0');
        return Ok(new { sample });
    }
}
