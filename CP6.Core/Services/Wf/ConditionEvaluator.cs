namespace CP6.Core.Services.Wf;

/// <summary>
/// 安全条件求值器（OA 章03 §6 / OA-D6）。<b>向后兼容门面</b>：实现已于阶段2（章06 §5）统一到
/// <see cref="ExpressionEvaluator"/>（条件流转与表单规则后端复算共用一份语义）。新代码请直接用
/// <see cref="ExpressionEvaluator"/>；本类保留供既有流程 condition 调用点与回归测试无缝沿用。
/// </summary>
public static class ConditionEvaluator
{
    /// <summary>对 varsJson 求布尔值。空表达式=true；任何错误→false。委托 <see cref="ExpressionEvaluator"/>。</summary>
    public static bool Evaluate(string? expression, string? varsJson)
        => ExpressionEvaluator.Evaluate(expression, varsJson);

    public static bool Evaluate(string? expression, IReadOnlyDictionary<string, object?> vars)
        => ExpressionEvaluator.Evaluate(expression, vars);

    /// <summary>把流程变量 JSON 解析成 字段→值 字典。</summary>
    public static Dictionary<string, object?> ParseVars(string? varsJson)
        => ExpressionEvaluator.ParseVars(varsJson);
}
