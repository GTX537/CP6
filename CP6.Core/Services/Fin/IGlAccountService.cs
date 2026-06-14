using CP6.Entity.DomainModels.Fin;

namespace CP6.Core.Services.Fin;

/// <summary>
/// 会计科目服务（章01 §3）。科目 CRUD（不删只停用）+ 多国别模板包导入。
/// 自动凭证引擎（Plan 2）通过 <see cref="GetByRoleAsync"/> 按角色锚点拿科目，不认具体编码。
/// </summary>
public interface IGlAccountService
{
    /// <summary>列出科目（可按模板方案过滤；默认只列启用）。</summary>
    Task<List<GlAccount>> ListAsync(string? scheme = null, bool includeInactive = false);

    /// <summary>按 Id 取。</summary>
    Task<GlAccount?> GetAsync(Guid id);

    /// <summary>按编码取。</summary>
    Task<GlAccount?> GetByCodeAsync(string code);

    /// <summary>★按 Role 角色锚点取启用科目（自动凭证用，换模板包零改动）。</summary>
    Task<GlAccount?> GetByRoleAsync(string role, string? scheme = null);

    /// <summary>新建科目（Code 唯一，重复抛 E-FIN-001）。返回新 Id。</summary>
    Task<Guid> CreateAsync(GlAccount a, string? user);

    /// <summary>改科目（Code/StandardScheme 只读，建后不可改）。不存在抛 E-FIN-003。</summary>
    Task UpdateAsync(Guid id, GlAccount a, string? user);

    /// <summary>停用科目（财务里"删"是禁词，只 IsActive=false）。</summary>
    Task DeactivateAsync(Guid id, string? user);

    /// <summary>导入科目表模板包（按 scheme 选一套）。已导入过抛 E-FIN-002。返回导入科目数。</summary>
    Task<int> ImportTemplateAsync(string scheme, string? user);
}
