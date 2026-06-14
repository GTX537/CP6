using CP6.Core.Services.Wf;

namespace CP6.Tests;

/// <summary>OA 章03 §6 安全条件求值器（C-2）。比较/逻辑/括号 + 安全失败（未知字段/非法表达式/不 eval 代码）。</summary>
public class ConditionEvaluatorTests
{
    [Fact]
    public void Gt_NumericCompare()
    {
        Assert.True(ConditionEvaluator.Evaluate("days > 3", """{"days":5}"""));
        Assert.False(ConditionEvaluator.Evaluate("days > 3", """{"days":2}"""));
    }

    [Fact]
    public void Le_Boundary()
    {
        Assert.True(ConditionEvaluator.Evaluate("days <= 3", """{"days":3}"""));
        Assert.False(ConditionEvaluator.Evaluate("days <= 3", """{"days":4}"""));
    }

    [Fact]
    public void And_StringEq_With_Le()
    {
        Assert.True(ConditionEvaluator.Evaluate("type == 'annual' && days <= 3", """{"type":"annual","days":3}"""));
        Assert.False(ConditionEvaluator.Evaluate("type == 'annual' && days <= 3", """{"type":"sick","days":3}"""));
        Assert.False(ConditionEvaluator.Evaluate("type == 'annual' && days <= 3", """{"type":"annual","days":9}"""));
    }

    [Fact]
    public void Or_ShortPath()
        => Assert.True(ConditionEvaluator.Evaluate("days > 10 || type == 'vip'", """{"days":1,"type":"vip"}"""));

    [Fact]
    public void Neq_And_Bool()
    {
        Assert.True(ConditionEvaluator.Evaluate("type != 'annual'", """{"type":"sick"}"""));
        Assert.True(ConditionEvaluator.Evaluate("urgent == true", """{"urgent":true}"""));
        Assert.False(ConditionEvaluator.Evaluate("urgent == true", """{"urgent":false}"""));
    }

    [Fact]
    public void Parentheses_OverridePrecedence()
        => Assert.True(ConditionEvaluator.Evaluate("(days > 3 || type == 'x') && days < 10", """{"days":5,"type":"y"}"""));

    [Fact]
    public void EmptyExpression_IsUnconditionalTrue()
    {
        Assert.True(ConditionEvaluator.Evaluate("", """{"days":5}"""));
        Assert.True(ConditionEvaluator.Evaluate(null, """{"days":5}"""));
    }

    [Fact]
    public void UnknownField_SafeFalse()
        => Assert.False(ConditionEvaluator.Evaluate("missing > 3", """{"days":5}"""));

    [Fact]
    public void IllegalExpression_SafeFalse_NoThrow()
    {
        Assert.False(ConditionEvaluator.Evaluate("days >> 3 ;; drop", """{"days":5}"""));
        Assert.False(ConditionEvaluator.Evaluate("days > ", """{"days":5}"""));
        Assert.False(ConditionEvaluator.Evaluate("'unterminated", """{"days":5}"""));
    }

    [Fact]
    public void NoCodeEval_DottedIdentifierTreatedAsUnknownField()
        => Assert.False(ConditionEvaluator.Evaluate("System.IO.File.Delete('x')", """{}"""));   // 绝不执行代码 → 安全 false

    [Fact]
    public void NonNumericMagnitudeCompare_SafeFalse()
        => Assert.False(ConditionEvaluator.Evaluate("type > 3", """{"type":"annual"}"""));       // 字符串做大小比较 → 安全 false
}
