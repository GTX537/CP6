using CP6.Entity.DTOs.Space;

namespace CP6.Core.Services.Space;

/// <summary>
/// 编码规则静态预检（ch03 §6.1，租户全局唯一防线一）。
/// 仅分析规则段列表结构，无 DB 调用，返回错误码列表（空=通过）。
///
/// 检查项：
///   E-SPACE-303 — 无能区分到 Zone 的段（zone-code/zone-seq，或 site-code+floor-level 组合）。
///   E-SPACE-305 — 含巷道段（aisle-code/aisle-seq）但未标 optional（无巷道货架会产生空值撞码）。
///   E-SPACE-306 — 无 col/level/depth 库位粒度段（无法区分同一货架的不同格位）。
/// </summary>
public static class CodePrecheck
{
    private static readonly HashSet<string> LocSources   = new() { "col", "level", "depth" };
    private static readonly HashSet<string> ZoneSources  = new() { "zone-code", "zone-seq" };
    private static readonly HashSet<string> AisleSources = new() { "aisle-code", "aisle-seq" };

    /// <summary>
    /// 静态预检。返回错误码列表，空=通过。
    /// 可同时返回多条错误（如同时缺 Zone 段 + 缺库位粒度）。
    /// </summary>
    public static List<string> Validate(List<CodeSegmentDef> segs)
    {
        var errs = new List<string>();

        // ① Zone 区分段：含 zone-code/zone-seq，或同时含 site-code + floor-level（可精确到楼层）
        bool hasZone      = segs.Any(s => ZoneSources.Contains(s.Source));
        bool hasSiteFloor = segs.Any(s => s.Source == "site-code")
                         && segs.Any(s => s.Source == "floor-level");
        if (!hasZone && !hasSiteFloor)
            errs.Add("E-SPACE-303");

        // ② 巷道段必须标 optional（可选条件段）：
        //    无巷道货架挂该段时值为空，不标 optional 会导致无巷道路径产生带空值的码 → 撞码风险
        if (segs.Any(s => AisleSources.Contains(s.Source) && !s.Optional))
            errs.Add("E-SPACE-305");

        // ③ 至少含一个库位粒度段（col/level/depth），否则同一货架所有格位码相同
        if (!segs.Any(s => LocSources.Contains(s.Source)))
            errs.Add("E-SPACE-306");

        return errs;
    }
}
