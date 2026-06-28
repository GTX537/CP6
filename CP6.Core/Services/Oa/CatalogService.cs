using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Oa;

public class CatalogService : ICatalogService
{
    private readonly CP6Context _db;
    private readonly IFavoriteService _fav;
    public CatalogService(CP6Context db, IFavoriteService fav) { _db = db; _fav = fav; }

    public async Task<IReadOnlyList<CatalogNode>> CatalogAsync(Guid userId)
    {
        var favs = (await _fav.ListAsync(userId)).ToHashSet();
        var defs = await _db.Wf_FormDefs.Where(d => d.Enable)
            .Select(d => new { d.FormKey, d.FormName, d.Category, d.SubCategory }).ToListAsync();
        return defs
            .GroupBy(d => d.Category ?? "未分类")
            .OrderBy(g => g.Key)
            .Select(catGrp => new CatalogNode(catGrp.Key,
                catGrp.GroupBy(d => d.SubCategory ?? "其他").OrderBy(s => s.Key)
                      .Select(subGrp => new CatalogSub(subGrp.Key,
                          subGrp.OrderBy(d => d.FormName)
                                .Select(d => new FormCard(d.FormKey, d.FormName, d.Category, d.SubCategory, favs.Contains(d.FormKey)))
                                .ToList()))
                      .ToList()))
            .ToList();
    }
}
