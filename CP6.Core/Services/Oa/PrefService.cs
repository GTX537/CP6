using CP6.Core.EFDbContext;
using CP6.Entity.DomainModels.Wf;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Oa;

public class PrefService : IPrefService
{
    private readonly CP6Context _db;
    public PrefService(CP6Context db) { _db = db; }

    public async Task<string> GetAsync(Guid userId) =>
        (await _db.Wf_InboxPrefs.FirstOrDefaultAsync(p => p.UserId == userId))?.PrefsJson ?? "{}";

    public async Task SaveAsync(Guid userId, string prefsJson)
    {
        var p = await _db.Wf_InboxPrefs.FirstOrDefaultAsync(x => x.UserId == userId);
        if (p is null)
            _db.Wf_InboxPrefs.Add(new Wf_InboxPref { Id = Guid.NewGuid(), UserId = userId, PrefsJson = prefsJson ?? "{}" });
        else { p.PrefsJson = prefsJson ?? "{}"; p.ModifyDate = DateTime.Now; }
        await _db.SaveChangesAsync();
    }
}
