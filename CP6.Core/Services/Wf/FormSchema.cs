namespace CP6.Core.Services.Wf;

/// <summary>
/// 表单 schema（SchemaJson 反序列化目标，OA 章02）。仅含后端 required/类型/长度复核所需字段；
/// 完整布局/控件属性由前端 DynamicForm 消费，后端不关心。
/// </summary>
public class FormSchema
{
    public List<FormField> Fields { get; set; } = new();

    /// <summary>表单规则（章06 显隐/计算/联动/必填）。后端复算只关心 compute(计算回写) 与 require/optional/show/hide。</summary>
    public List<FormRule> Rules { get; set; } = new();
}

/// <summary>
/// 表单规则（OA 章06 §4）。<c>When</c> 表达式（<see cref="ExpressionEvaluator"/> 语义）求值为真时，
/// 依序应用 <c>Then</c> 各动作。前后端共用同一 schema + 同一求值器语义（前端体验、后端为准）。
/// </summary>
public class FormRule
{
    /// <summary>触发条件表达式（空=恒成立）</summary>
    public string When { get; set; } = string.Empty;

    /// <summary>命中后应用的动作列表</summary>
    public List<RuleEffect> Then { get; set; } = new();
}

/// <summary>
/// 规则动作（OA 章06 §4）。Action: show/hide（显隐）、require/optional（必填）、disable/enable（禁用，
/// 纯前端体验）、setOptions（联动选项，纯前端）、compute（计算回写，<c>Expr</c> 求值写入 <c>Target</c>）。
/// 后端复算只处理 compute / require / optional / show / hide（其余为前端表现，后端不关心）。
/// </summary>
public class RuleEffect
{
    /// <summary>动作类型</summary>
    public string Action { get; set; } = string.Empty;

    /// <summary>目标字段名</summary>
    public string Target { get; set; } = string.Empty;

    /// <summary>compute 的计算表达式（仅 compute 用）</summary>
    public string? Expr { get; set; }
}

public class FormField
{
    /// <summary>字段名（DataJson 的 key）</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>显示标签（报错文案用）</summary>
    public string? Label { get; set; }

    /// <summary>控件类型：input/textarea/number/select/radio/checkbox/date/datetime/user/dept/upload</summary>
    public string Type { get; set; } = "input";

    /// <summary>是否必填</summary>
    public bool Required { get; set; }

    /// <summary>字符串最大长度（仅文本类）</summary>
    public int? MaxLength { get; set; }

    /// <summary>正则校验（仅文本类；schema 由管理员配置，视为可信）</summary>
    public string? Pattern { get; set; }
}
