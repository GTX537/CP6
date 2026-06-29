using CP6.Entity.DomainModels.Space;
using CP6.Entity.DTOs.Space;

namespace CP6.Core.Services.Space;

/// <summary>可配置库位编码引擎服务接口（ch03）。</summary>
public interface ICodeEngineService
{
    // ── CodeRule CRUD（ch03 §2.1，同作用域 IsDefault 互斥）──────────────

    /// <summary>列出全部编码规则（按作用域 + 名称排序）。</summary>
    Task<List<Space_CodeRule>> ListRulesAsync();

    /// <summary>新建编码规则。IsDefault=true 时自动把同作用域其他规则 IsDefault 置 false。</summary>
    Task<Guid> CreateRuleAsync(CodeRuleDto d, string? user);

    /// <summary>修改编码规则。IsDefault=true 时自动把同作用域其他规则 IsDefault 置 false。</summary>
    Task UpdateRuleAsync(Guid id, CodeRuleDto d, string? user);

    /// <summary>删除编码规则。</summary>
    Task DeleteRuleAsync(Guid id);

    // ── 生成（ch03 §4）──────────────────────────────────────────────────

    /// <summary>
    /// 批量生成库位编码（ch03 §4.2 主流程）。
    /// mode="fill-empty" 只填空码草稿；mode="rebuild" 两阶段重排全量重生成。
    /// scopeZoneId 不为 null 时仅处理指定库区。
    /// 返回本次生成的编码列表；批内重复或与既有码冲突时抛 E-SPACE-304。
    /// </summary>
    Task<List<string>> GenerateAsync(Guid floorId, string mode, Guid? scopeZoneId);

    // ── 预览（ch03 §8）──────────────────────────────────────────────────

    /// <summary>实时预览（ch03 §8）。合成虚拟层级数据返回两路样例，不写库。</summary>
    Task<CodePreviewResp> PreviewAsync(CodePreviewReq req);

    // ── 发布前预检（ch03 §9.2）─────────────────────────────────────────

    /// <summary>
    /// 发布前编码预检（ch03 §9.2，04 闸门入口）。
    /// 检查：空码草稿数 / 重复码组 / 规则静态错误 / 未落位草稿数。
    /// </summary>
    Task<CodePrecheckResp> PrecheckAsync(Guid floorId);

    // ── 单格生成（ch03 §10，可推迟）────────────────────────────────────

    /// <summary>
    /// 单格生成（ch03 §10）。对单个库位走同样的 Assemble 路径写回 LocationCode。
    /// 最小实现：rackSeq 简化为 1；后续补全参见计划 §10。
    /// </summary>
    Task<string> GenSingleAsync(Guid locationId);
}
