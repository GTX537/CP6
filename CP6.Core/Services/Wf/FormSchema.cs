namespace CP6.Core.Services.Wf;

/// <summary>
/// 表单 schema（SchemaJson 反序列化目标，OA 章02）。仅含后端 required/类型/长度复核所需字段；
/// 完整布局/控件属性由前端 DynamicForm 消费，后端不关心。
/// </summary>
public class FormSchema
{
    public List<FormField> Fields { get; set; } = new();
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
