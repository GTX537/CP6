using CP6.Core.Auth;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Fin;
using CP6.Entity.DomainModels.Fin;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CP6.WebApi.Controllers.Fin;

/// <summary>资产分类 REST（A3 §7.1）。/api/fin/asset-category。CRUD + 删除守卫 FA012。</summary>
[ApiController]
[Route("api/fin/asset-category")]
[Authorize]
public class AssetCategoryController : ControllerBase
{
    private readonly CP6Context _db;
    public AssetCategoryController(CP6Context db) => _db = db;

    private IActionResult Ok2(object? data = null) => Ok(new { code = 0, message = "OK", data });
    private IActionResult Fin(FinResult r) => r.Ok ? Ok2() : BadRequest(new { code = 400, message = r.Code, args = r.Args });

    [HttpGet]
    [RequirePermission("fin-asset-category", "view")]
    public async Task<IActionResult> List() => Ok2(await _db.AssetCategories.OrderBy(c => c.Code).ToListAsync());

    [HttpGet("{id}")]
    [RequirePermission("fin-asset-category", "view")]
    public async Task<IActionResult> Get(Guid id) => Ok2(await _db.AssetCategories.FindAsync(id));

    [HttpPost]
    [RequirePermission("fin-asset-category", "add")]
    public async Task<IActionResult> Create([FromBody] AssetCategory c)
    {
        c.Id = Guid.NewGuid();
        _db.AssetCategories.Add(c);
        await _db.SaveChangesAsync();
        return Ok2(new { id = c.Id });
    }

    [HttpPut("{id}")]
    [RequirePermission("fin-asset-category", "edit")]
    public async Task<IActionResult> Update(Guid id, [FromBody] AssetCategory c)
    {
        var e = await _db.AssetCategories.FindAsync(id);
        if (e == null) return Fin(FinResult.Fail("FA006"));
        e.Name = c.Name; e.ParentId = c.ParentId; e.Level = c.Level;
        e.DefaultMethod = c.DefaultMethod;
        e.DefaultUsefulLifeMonths = c.DefaultUsefulLifeMonths;
        e.DefaultSalvageRate = c.DefaultSalvageRate;
        e.AssetAccountId = c.AssetAccountId;
        e.AccumDeprecAccountId = c.AccumDeprecAccountId;
        e.DeprecExpenseAccountId = c.DeprecExpenseAccountId;
        e.IsActive = c.IsActive;
        await _db.SaveChangesAsync();
        return Ok2();
    }

    [HttpDelete("{id}")]
    [RequirePermission("fin-asset-category", "delete")]
    public async Task<IActionResult> Delete(Guid id)
    {
        if (await _db.AssetCategories.AnyAsync(x => x.ParentId == id)
            || await _db.AssetCards.AnyAsync(x => x.CategoryId == id))
            return Fin(FinResult.Fail("FA012"));
        var e = await _db.AssetCategories.FindAsync(id);
        if (e != null) { _db.AssetCategories.Remove(e); await _db.SaveChangesAsync(); }
        return Ok2();
    }
}
