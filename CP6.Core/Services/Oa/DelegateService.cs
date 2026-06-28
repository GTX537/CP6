using CP6.Core.EFDbContext;
using CP6.Entity.DomainModels.Wf;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Oa;

public class DelegateService : IDelegateService
{
    private readonly CP6Context _db;
    public DelegateService(CP6Context db) { _db = db; }

    private IQueryable<Wf_FlowDelegate> Active() => _db.Wf_FlowDelegates
        .Where(d => d.Enable && d.ValidFrom <= DateTime.Now && d.ValidTo >= DateTime.Now);

    public async Task<MyGrants> MyGrantsAsync(Guid userId)
    {
        var canActAsIds = await Active().Where(d => d.DelegateId == userId).Select(d => d.GrantorId).Distinct().ToListAsync();
        var actForMeIds = await Active().Where(d => d.GrantorId == userId).Select(d => d.DelegateId).Distinct().ToListAsync();
        var names = await OaUserNames.ResolveAsync(_db, canActAsIds.Concat(actForMeIds));
        GrantUser U(Guid id) => new(id, names.GetValueOrDefault(id, id.ToString()));
        return new MyGrants(canActAsIds.Select(U).ToList(), actForMeIds.Select(U).ToList());
    }

    public async Task AssertActiveGrantAsync(Guid delegateId, Guid grantorId)
    {
        var ok = await Active().AnyAsync(d => d.DelegateId == delegateId && d.GrantorId == grantorId);
        if (!ok) throw new InvalidOperationException("E-WF-001");
    }

    public async Task<IReadOnlyList<DelegateItem>> ListMyDelegatesAsync(Guid grantorId)
    {
        var rows = await _db.Wf_FlowDelegates.Where(d => d.GrantorId == grantorId)
            .OrderByDescending(d => d.CreateDate).ToListAsync();
        var names = await OaUserNames.ResolveAsync(_db, rows.Select(r => r.DelegateId));
        return rows.Select(d => new DelegateItem(d.Id, d.GrantorId, d.DelegateId,
            names.GetValueOrDefault(d.DelegateId, d.DelegateId.ToString()),
            d.ValidFrom, d.ValidTo, d.Enable, d.Scope, d.Remark)).ToList();
    }

    public async Task<Guid> AddDelegateAsync(Guid grantorId, Guid delegateId, DateTime from, DateTime to, string? scope, string? remark)
    {
        var d = new Wf_FlowDelegate { Id = Guid.NewGuid(), GrantorId = grantorId, DelegateId = delegateId,
            ValidFrom = from, ValidTo = to, Enable = true, Scope = scope, Remark = remark, Creator = grantorId.ToString() };
        _db.Wf_FlowDelegates.Add(d);
        await _db.SaveChangesAsync();
        return d.Id;
    }

    public async Task RemoveDelegateAsync(Guid grantorId, Guid id)
    {
        var d = await _db.Wf_FlowDelegates.FirstOrDefaultAsync(x => x.Id == id && x.GrantorId == grantorId);
        if (d is null) return;   // 幂等 / 仅能删自己授出的
        _db.Wf_FlowDelegates.Remove(d);
        await _db.SaveChangesAsync();
    }
}
