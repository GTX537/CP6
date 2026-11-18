using CP6.Core.Auth;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Pub;
using CP6.Entity.DomainModels.Pub;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CP6.WebApi.Controllers.Pub;

/// <summary>富采番规则配置 REST —— PUB 章05。/api/pub/seq</summary>
[ApiController]
[Route("api/pub/seq")]
[Authorize]
public class SeqController : LocalizedControllerBase
{
    private readonly CP6Context _db;
    public SeqController(CP6Context db) => _db = db;

    private IQueryable<Guid> ManagedRuleIds() => _db.Database.SqlQuery<Guid>(
        $"SELECT rule_id AS \"Value\" FROM cp6_number_allocation_v1.scopes WHERE tenant_id={_db.CurrentTenantId}");

    [HttpGet("owner-v1")]
    [RequirePermission("pub-seq", "query")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> ManagedCatalog(int page = 1, int pageSize = 10, string? keyword = null)
    {
        if (!_db.Database.IsNpgsql()) return StatusCode(503, new { code = "NUMBER_POSTGRESQL_REQUIRED" });
        var offset = ((long)page - 1) * pageSize;
        if (page < 1 || pageSize < 1 || offset > int.MaxValue) return BadRequest(new { code = "NUMBER_PAGE_INVALID" });
        var registrations = ManagedRuleIds();
        var query = _db.Pub_DocSequences.AsNoTracking().Where(x => registrations.Contains(x.Id));
        if (!string.IsNullOrEmpty(keyword)) query = query.Where(x => x.BizKey.Contains(keyword));
        var total = await query.CountAsync();
        var rows = await query.OrderBy(x => x.BizKey).Skip((int)offset).Take(pageSize)
            .Select(x => new { x.Id, x.BizKey }).ToListAsync();
        return Ok(new { rows, total });
    }

    [HttpGet]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> GetList(int page = 1, int pageSize = 10, string? keyword = null, bool legacyOnly = false)
    {
        var q = _db.Pub_DocSequences.AsQueryable();
        if (legacyOnly && _db.Database.IsNpgsql())
        {
            var registrations = ManagedRuleIds();
            q = q.Where(x => !registrations.Contains(x.Id));
        }
        if (!string.IsNullOrEmpty(keyword))
            q = q.Where(x => x.BizKey.Contains(keyword) || x.Prefix.Contains(keyword));
        var total = await q.CountAsync();
        var rows = await q.OrderBy(x => x.BizKey)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();
        return Ok(new { rows, total });
    }

    [HttpPost]
    [RequirePermission("pub-seq", "add")]
    public async Task<IActionResult> Add([FromBody] Pub_DocSequence entity)
    {
        if (await _db.Pub_DocSequences.AnyAsync(x => x.BizKey == entity.BizKey))
            return BadRequest(new { message = Localizer["业务键已存在"] });
        entity.CreateDate = DateTime.Now;
        _db.Pub_DocSequences.Add(entity);
        await _db.SaveChangesAsync();
        return Ok(entity);
    }

    [HttpPut]
    [RequirePermission("pub-seq", "edit")]
    public async Task<IActionResult> Update([FromBody] Pub_DocSequence entity)
    {
        var cur = await _db.Pub_DocSequences.FindAsync(entity.Id);
        if (cur == null) return NotFound();
        if (await Managed([cur.Id])) return Conflict(new { code = "NUMBER_ALLOCATION_OWNER_REQUIRED" });
        cur.Prefix = entity.Prefix;
        cur.DateFormat = entity.DateFormat;
        cur.SeqLength = entity.SeqLength;
        cur.ResetCycle = entity.ResetCycle;
        cur.ModifyDate = DateTime.Now;
        await _db.SaveChangesAsync();
        return Ok(cur);
    }

    [HttpDelete]
    [RequirePermission("pub-seq", "delete")]
    public async Task<IActionResult> Delete([FromBody] Guid[] ids)
    {
        if (await Managed(ids)) return Conflict(new { code = "NUMBER_ALLOCATION_OWNER_REQUIRED" });
        var rows = await _db.Pub_DocSequences.Where(x => ids.Contains(x.Id)).ToListAsync();
        _db.Pub_DocSequences.RemoveRange(rows);
        var count = await _db.SaveChangesAsync();
        return Ok(new { count });
    }

    /// <summary>预览号码格式（不消费流水）。</summary>
    [HttpGet("preview/{bizKey}")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> Preview(string bizKey)
    {
        var s = await _db.Pub_DocSequences.FirstOrDefaultAsync(x => x.BizKey == bizKey);
        if (s == null) return NotFound();
        if (await Managed([s.Id])) return Conflict(new { code = "NUMBER_ALLOCATION_OWNER_REQUIRED" });
        var now = DateTime.Now;
        var periodKey = SeqService.PeriodKey(s.ResetCycle, now);
        var datePart = string.IsNullOrEmpty(s.DateFormat) ? "" : now.ToString(s.DateFormat);
        var sample = s.Prefix + datePart + ((long)s.CurrentValue + 1).ToString().PadLeft(s.SeqLength, '0');
        return Ok(new { sample, isReservation = false, periodKey, timeZone = TimeZoneInfo.Local.Id,
            wouldReset = s.CurrentPeriod != periodKey });
    }

    private Task<bool> Managed(Guid[] ids) => _db.Database.IsNpgsql()
        ? _db.Database.SqlQuery<bool>($"SELECT EXISTS(SELECT FROM cp6_number_allocation_v1.scopes WHERE tenant_id={_db.CurrentTenantId} AND rule_id=ANY({ids})) AS \"Value\"").SingleAsync()
        : Task.FromResult(false);
}
