using CP6.Entity.DTOs.Space;

namespace CP6.Core.Services.Space;

/// <summary>模板服务契约（ch01 §F-1）</summary>
public interface ITemplateService
{
    /// <summary>列出当前租户所有模板</summary>
    Task<List<TemplateDto>> ListAsync();

    /// <summary>创建模板（编码租户内唯一 E-SPACE-001）</summary>
    Task<Guid> CreateAsync(TemplateDto d, string? user);

    /// <summary>更新模板（找不到→E-SPACE-001）</summary>
    Task UpdateAsync(Guid id, TemplateDto d, string? user);

    /// <summary>删除模板</summary>
    Task DeleteAsync(Guid id);

    /// <summary>克隆模板：新 Id + 新编码 {code}-COPY（撞则 -COPY2/-COPY3...）+ 同 Params/Type</summary>
    Task<Guid> CloneAsync(Guid id, string? user);
}
