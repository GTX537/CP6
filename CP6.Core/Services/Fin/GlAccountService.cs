using CP6.Core.EFDbContext;
using CP6.Entity.DomainModels.Fin;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Fin;

/// <summary>会计科目服务实现（章01 §3）。</summary>
public class GlAccountService : IGlAccountService
{
    private readonly CP6Context _db;
    public GlAccountService(CP6Context db) => _db = db;

    public async Task<List<GlAccount>> ListAsync(string? scheme = null, bool includeInactive = false)
    {
        var q = _db.GlAccounts.AsQueryable();
        if (!string.IsNullOrEmpty(scheme)) q = q.Where(x => x.StandardScheme == scheme);
        if (!includeInactive) q = q.Where(x => x.IsActive);
        return await q.OrderBy(x => x.Code).ToListAsync();
    }

    public Task<GlAccount?> GetAsync(Guid id) =>
        _db.GlAccounts.FirstOrDefaultAsync(x => x.Id == id);

    public Task<GlAccount?> GetByCodeAsync(string code) =>
        _db.GlAccounts.FirstOrDefaultAsync(x => x.Code == code);

    public Task<GlAccount?> GetByRoleAsync(string role, string? scheme = null)
    {
        var q = _db.GlAccounts.Where(x => x.Role == role && x.IsActive);
        if (!string.IsNullOrEmpty(scheme)) q = q.Where(x => x.StandardScheme == scheme);
        return q.FirstOrDefaultAsync();
    }

    public async Task<Guid> CreateAsync(GlAccount a, string? user)
    {
        if (string.IsNullOrWhiteSpace(a.Code))
            throw new InvalidOperationException("E-FIN-004");        // 科目编码必填
        if (await _db.GlAccounts.AnyAsync(x => x.Code == a.Code))
            throw new InvalidOperationException("E-FIN-001");        // 科目编码已存在

        a.Id = a.Id == Guid.Empty ? Guid.NewGuid() : a.Id;
        a.Creator = user;
        a.CreateDate = DateTime.Now;
        _db.GlAccounts.Add(a);
        await _db.SaveChangesAsync();
        return a.Id;
    }

    public async Task UpdateAsync(Guid id, GlAccount a, string? user)
    {
        var e = await _db.GlAccounts.FindAsync(id)
                ?? throw new InvalidOperationException("E-FIN-003");  // 科目不存在
        // Code / StandardScheme 只读（建后被凭证引用，不可改）
        e.Name = a.Name;
        e.Type = a.Type;
        e.NormalSide = a.NormalSide;
        e.ParentId = a.ParentId;
        e.Level = a.Level;
        e.IsLeaf = a.IsLeaf;
        e.IsControl = a.IsControl;
        e.SubLedgerType = a.SubLedgerType;
        e.RequirePartner = a.RequirePartner;
        e.Role = a.Role;
        e.IsActive = a.IsActive;
        e.CurrencyCd = a.CurrencyCd;
        e.Modifier = user;
        e.ModifyDate = DateTime.Now;
        await _db.SaveChangesAsync();
    }

    public async Task DeactivateAsync(Guid id, string? user)
    {
        var e = await _db.GlAccounts.FindAsync(id)
                ?? throw new InvalidOperationException("E-FIN-003");  // 科目不存在
        e.IsActive = false;
        e.Modifier = user;
        e.ModifyDate = DateTime.Now;
        await _db.SaveChangesAsync();
    }

    public async Task<int> ImportTemplateAsync(string scheme, string? user)
    {
        if (await _db.GlAccounts.AnyAsync(x => x.StandardScheme == scheme))
            throw new InvalidOperationException("E-FIN-002");        // 该模板包已导入

        var rows = FinCoaTemplate.Get(scheme);
        var now = DateTime.Now;

        // 先按 Code 分配 Id（供 ParentCode → ParentId 解析）
        var byCode = rows.ToDictionary(r => r.Code, _ => Guid.NewGuid());

        var entities = rows.Select(r => new GlAccount
        {
            Id = byCode[r.Code],
            Code = r.Code,
            Name = r.Name,
            Type = r.Type,
            NormalSide = r.NormalSide,
            Level = r.Level,
            IsLeaf = r.IsLeaf,
            IsControl = r.IsControl,
            SubLedgerType = r.SubLedgerType,
            RequirePartner = r.RequirePartner,
            Role = r.Role,
            StandardScheme = scheme,
            IsActive = true,
            ParentId = r.ParentCode != null && byCode.TryGetValue(r.ParentCode, out var pid) ? pid : null,
            Creator = user,
            CreateDate = now,
        }).ToList();

        _db.GlAccounts.AddRange(entities);
        await _db.SaveChangesAsync();
        return entities.Count;
    }
}
