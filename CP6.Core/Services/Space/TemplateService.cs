using CP6.Core.EFDbContext;
using CP6.Entity.DomainModels.Space;
using CP6.Entity.DTOs.Space;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Space;

/// <summary>
/// 模板服务实现（ch01 §F-1，v1.1 多租户规则）。
/// 构造只注入 CP6Context；查询/盖章由全局过滤+SaveChanges 自动施加。
/// </summary>
public class TemplateService : ITemplateService
{
    private readonly CP6Context _db;

    public TemplateService(CP6Context db) => _db = db;

    /// <inheritdoc/>
    public async Task<List<TemplateDto>> ListAsync() =>
        await _db.Space_Templates.Select(t => new TemplateDto
        {
            Id = t.Id, TemplateCode = t.TemplateCode, TemplateName = t.TemplateName,
            TemplateType = t.TemplateType, Params = t.Params
        }).ToListAsync();

    /// <inheritdoc/>
    public async Task<Guid> CreateAsync(TemplateDto d, string? user)
    {
        if (await _db.Space_Templates.AnyAsync(t => t.TemplateCode == d.TemplateCode))
            throw new InvalidOperationException("E-SPACE-001");

        var e = new Space_Template
        {
            Id           = d.Id ?? Guid.NewGuid(),
            TemplateCode = d.TemplateCode,
            TemplateName = d.TemplateName,
            TemplateType = d.TemplateType,
            Params       = d.Params,
            Creator      = user,
            CreateDate   = DateTime.Now
        };
        _db.Space_Templates.Add(e);
        await _db.SaveChangesAsync();
        return e.Id;
    }

    /// <inheritdoc/>
    public async Task UpdateAsync(Guid id, TemplateDto d, string? user)
    {
        var e = await _db.Space_Templates.FirstOrDefaultAsync(t => t.Id == id)
                ?? throw new InvalidOperationException("E-SPACE-001");
        e.TemplateCode = d.TemplateCode;
        e.TemplateName = d.TemplateName;
        e.TemplateType = d.TemplateType;
        e.Params       = d.Params;
        e.Modifier     = user;
        e.ModifyDate   = DateTime.Now;
        await _db.SaveChangesAsync();
    }

    /// <inheritdoc/>
    public async Task DeleteAsync(Guid id)
    {
        var e = await _db.Space_Templates.FirstOrDefaultAsync(t => t.Id == id);
        if (e != null) { _db.Space_Templates.Remove(e); await _db.SaveChangesAsync(); }
    }

    /// <inheritdoc/>
    public async Task<Guid> CloneAsync(Guid id, string? user)
    {
        var src = await _db.Space_Templates.FirstOrDefaultAsync(t => t.Id == id)
                  ?? throw new InvalidOperationException("E-SPACE-001");

        // 生成不碰撞的克隆编码：{code}-COPY → -COPY2 → -COPY3 ...
        var candidate = src.TemplateCode + "-COPY";
        int n = 2;
        while (await _db.Space_Templates.AnyAsync(t => t.TemplateCode == candidate))
            candidate = src.TemplateCode + "-COPY" + n++;

        var clone = new Space_Template
        {
            Id           = Guid.NewGuid(),
            TemplateCode = candidate,
            TemplateName = src.TemplateName,
            TemplateType = src.TemplateType,
            Params       = src.Params,
            Creator      = user,
            CreateDate   = DateTime.Now
        };
        _db.Space_Templates.Add(clone);
        await _db.SaveChangesAsync();
        return clone.Id;
    }
}
