using CP6.Entity.DTOs.Space;

namespace CP6.Core.Services.Space;

/// <summary>
/// 段渲染输入。码源字段给 RawCode；序号源字段给 SeqIndex（1-based 位次）。
/// </summary>
public class SegInput
{
    public string? RawCode  { get; set; }
    public int?    SeqIndex { get; set; }
}

/// <summary>
/// 单段渲染纯逻辑（ch03 §3）。无 DB 依赖，便于独立测试。
///
/// 取值源分两类：
///   · 序号源（zone-seq/aisle-seq/rack-seq/col/level/depth）：用 SeqIndex 计算 Start + (n-1)*Step，Width/Pad 补零。
///   · 码源（site-code/floor-level/zone-code/aisle-code/rack-code）：用 RawCode；Upper=true 转大写。
///   · fixed：直接返回 FixedValue。
/// </summary>
public static class CodeSegment
{
    // 序号源集合（用于 IsSeq 判断 + Render 分支）
    private static readonly HashSet<string> SeqSources = new()
    {
        "zone-seq", "aisle-seq", "rack-seq", "col", "level", "depth"
    };

    /// <summary>判断 source 是否属于序号源（序号源用 SeqIndex 计算，否则用 RawCode）。</summary>
    public static bool IsSeq(string source) => SeqSources.Contains(source);

    /// <summary>
    /// 渲染单段，返回最终字符串（不含 Sep）。
    /// 步骤：① 按 source 取原始值 → ② Upper 转换 → ③ Width 补位。
    /// </summary>
    public static string Render(CodeSegmentDef seg, SegInput input)
    {
        // 1. 按 source 取原始字符串
        string raw;
        if (seg.Source == "fixed")
        {
            raw = seg.FixedValue;
        }
        else if (IsSeq(seg.Source))
        {
            // 序号源：value = Start + (SeqIndex - 1) × Step
            raw = (seg.Start + ((input.SeqIndex ?? 1) - 1) * seg.Step).ToString();
        }
        else
        {
            // 码源：直接取 RawCode
            raw = input.RawCode ?? "";
        }

        // 2. 大写转换（仅码源生效；序号源本就是数字，Upper 无实际影响）
        if (seg.Upper)
            raw = raw.ToUpperInvariant();

        // 3. 左补位对齐（Width=0 表示不补）
        if (seg.Width > 0)
            raw = raw.PadLeft(seg.Width, seg.Pad.Length > 0 ? seg.Pad[0] : '0');

        return raw;
    }
}
