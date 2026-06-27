namespace CP6.Entity.DTOs.Space;

/// <summary>
/// 编码段定义（ch03 §3）。source 决定取值来源，Width/Pad 控制补零对齐。
/// 取值源：fixed/site-code/floor-level/zone-code/zone-seq/aisle-code/aisle-seq/rack-code/rack-seq/col/level/depth。
/// </summary>
public class CodeSegmentDef
{
    /// <summary>段 key（字母，同规则内唯一，如 "zone"/"rack"/"col"）</summary>
    public string Key { get; set; } = "";

    /// <summary>段显示名称</summary>
    public string Name { get; set; } = "";

    /// <summary>取值源标识符</summary>
    public string Source { get; set; } = "";

    /// <summary>补位宽度（0=不补；> 0 时左补 Pad 字符）</summary>
    public int Width { get; set; } = 0;

    /// <summary>补位字符（默认 "0"；取第一个字符）</summary>
    public string Pad { get; set; } = "0";

    /// <summary>序号起始值（序号源有效；默认 1）</summary>
    public int Start { get; set; } = 1;

    /// <summary>序号步长（序号源有效；默认 1）</summary>
    public int Step { get; set; } = 1;

    /// <summary>段后分隔符（默认 "-"；末段常设 ""）</summary>
    public string Sep { get; set; } = "-";

    /// <summary>是否大写（码源有效；source=fixed 时无效）</summary>
    public bool Upper { get; set; } = false;

    /// <summary>固定值（source="fixed" 时有效）</summary>
    public string FixedValue { get; set; } = "";

    /// <summary>是否可选条件段（巷道段须设 true；无巷道货架时跳过该段及其 Sep）</summary>
    public bool Optional { get; set; } = false;
}

/// <summary>编码规则 DTO（CRUD 用）。</summary>
public class CodeRuleDto
{
    public Guid? Id { get; set; }

    /// <summary>规则名称</summary>
    public string RuleName { get; set; } = "";

    /// <summary>作用域：0租户默认 / 1楼层 / 2库区</summary>
    public int ScopeType { get; set; }

    /// <summary>作用域对象 Id（1→FloorId，2→ZoneId，0→null）</summary>
    public Guid? ScopeId { get; set; }

    /// <summary>段定义列表（序列化为 JSON 存库）</summary>
    public List<CodeSegmentDef> Segments { get; set; } = new();

    /// <summary>是否该作用域默认规则（同作用域互斥）</summary>
    public bool IsDefault { get; set; }
}

/// <summary>实时预览请求（ch03 §8）。</summary>
public class CodePreviewReq
{
    public List<CodeSegmentDef> Segments { get; set; } = new();
    public int ScopeType { get; set; }
    public Guid? ScopeId { get; set; }
    public Guid? FloorId { get; set; }
}

/// <summary>实时预览响应（ch03 §8）。</summary>
public class CodePreviewResp
{
    /// <summary>段结构列表（Key/Name/Source/Optional）</summary>
    public List<object> Structure { get; set; } = new();

    /// <summary>样例编码列表（有/无巷道两路）</summary>
    public List<string> Samples { get; set; } = new();

    /// <summary>变长示例（有无巷道两路对比）</summary>
    public VariableLen VariableLen { get; set; } = new();

    /// <summary>静态预检结果</summary>
    public Precheck Precheck { get; set; } = new();
}

/// <summary>变长示例（ch03 §5.2，有/无巷道两路对比）。</summary>
public class VariableLen
{
    public string WithAisle { get; set; } = "";
    public string WithoutAisle { get; set; } = "";
}

/// <summary>静态预检结果（内嵌于预览响应）。</summary>
public class Precheck
{
    public bool Ok { get; set; } = true;
    public List<string> Errors { get; set; } = new();
}

/// <summary>
/// 发布前编码预检响应（ch03 §9.2，04 闸门入口）。
/// 全通过（EmptyCodeCount=0, DuplicateGroups 空, PrecheckErrors 空）才允许发布。
/// </summary>
public class CodePrecheckResp
{
    /// <summary>空码草稿数（还未生成编码的库位数量）</summary>
    public int EmptyCodeCount { get; set; }

    /// <summary>重复码组（每组含冲突 LocationId 列表）</summary>
    public List<List<Guid>> DuplicateGroups { get; set; } = new();

    /// <summary>规则静态预检错误码（E-SPACE-3xx，汇总各 Zone 结果，已去重）</summary>
    public List<string> PrecheckErrors { get; set; } = new();

    /// <summary>有码但未落位草稿数（Placed=false 的异常库位）</summary>
    public int UnplacedDraftCount { get; set; }
}
