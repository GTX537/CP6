using CP6.Core.EFDbContext;
using CP6.Entity.DomainModels.Wf;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Oa;

public class FavoriteService : IFavoriteService
{
    private readonly CP6Context _db;
    public FavoriteService(CP6Context db) { _db = db; }

    public async Task AddAsync(Guid userId, string formKey)
    {
        if (await _db.Wf_FormFavorites.AnyAsync(f => f.UserId == userId && f.FormKey == formKey)) return; // 幂等
        _db.Wf_FormFavorites.Add(new Wf_FormFavorite { Id = Guid.NewGuid(), UserId = userId, FormKey = formKey });
        await _db.SaveChangesAsync();
    }

    public async Task RemoveAsync(Guid userId, string formKey)
    {
        var f = await _db.Wf_FormFavorites.FirstOrDefaultAsync(x => x.UserId == userId && x.FormKey == formKey);
        if (f is null) return;
        _db.Wf_FormFavorites.Remove(f);
        await _db.SaveChangesAsync();
    }

    public async Task<IReadOnlyList<string>> ListAsync(Guid userId) =>
        await _db.Wf_FormFavorites.Where(f => f.UserId == userId).Select(f => f.FormKey).ToListAsync();
}
