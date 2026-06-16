using CP6.Core.Services.Wf;

namespace CP6.Tests.Wf;

/// <summary>
/// OA 章06 §5 共享表达式求值器（B-1，OA2-D2）。ConditionEvaluator 的超集：
/// 算术 + 一元 + 内置函数 + Compute(返原值)；流程 condition 与表单 rules 后端复算共用此一份。
/// 安全铁律：白名单字段(=vars keys) + 绝不 eval 代码 + 任何错误安全失败(Evaluate→false / Compute→null)。
/// </summary>
public class ExpressionEvaluatorTests
{
    // ── 向后兼容：原 ConditionEvaluator 语义不变 ──
    [Fact]
    public void Compare_And_Logic_StillWork()
    {
        Assert.True(ExpressionEvaluator.Evaluate("days > 3", """{"days":5}"""));
        Assert.True(ExpressionEvaluator.Evaluate("type == 'annual' && days <= 3", """{"type":"annual","days":3}"""));
        Assert.True(ExpressionEvaluator.Evaluate("(days > 3 || type == 'x') && days < 10", """{"days":5,"type":"y"}"""));
        Assert.True(ExpressionEvaluator.Evaluate("", """{"days":5}"""));      // 空 = 无条件 true
    }

    // ── 算术 + 优先级 ──
    [Fact]
    public void Arithmetic_Precedence()
    {
        Assert.Equal(7d, ExpressionEvaluator.Compute("1 + 2 * 3", "{}"));
        Assert.Equal(9d, ExpressionEvaluator.Compute("(1 + 2) * 3", "{}"));
        Assert.Equal(2d, ExpressionEvaluator.Compute("10 / 5 % 3", "{}"));   // (10/5)%3 = 2
        Assert.Equal(1d, ExpressionEvaluator.Compute("7 % 3", "{}"));
    }

    [Fact]
    public void UnaryMinus_And_Not()
    {
        Assert.Equal(2d, ExpressionEvaluator.Compute("-3 + 5", "{}"));
        Assert.True(ExpressionEvaluator.Evaluate("days > -1", """{"days":0}"""));
        Assert.True(ExpressionEvaluator.Evaluate("!(days > 3)", """{"days":1}"""));
    }

    [Fact]
    public void Arithmetic_OverFields()
    {
        Assert.Equal(600d, ExpressionEvaluator.Compute("price * qty", """{"price":120,"qty":5}"""));
        Assert.Equal(90d, ExpressionEvaluator.Compute("total - discount", """{"total":100,"discount":10}"""));
    }

    // ── 内置函数 ──
    [Fact]
    public void DateDiff_WholeDays()
    {
        Assert.Equal(5d, ExpressionEvaluator.Compute("dateDiff('2026-06-15','2026-06-10')", "{}"));
        // 请假天数惯用法 dateDiff(end,start)+1（含首尾）
        Assert.Equal(6d, ExpressionEvaluator.Compute("dateDiff(end, start) + 1", """{"end":"2026-06-15","start":"2026-06-10"}"""));
        // 容忍 datetime 串（取日期部分）
        Assert.Equal(1d, ExpressionEvaluator.Compute("dateDiff('2026-06-15 23:00:00','2026-06-14 01:00:00')", "{}"));
    }

    [Fact]
    public void Sum_Min_Max_Abs_Round_Len()
    {
        Assert.Equal(6d, ExpressionEvaluator.Compute("sum(1, 2, 3)", "{}"));
        Assert.Equal(15d, ExpressionEvaluator.Compute("sum(a, b, c)", """{"a":3,"b":5,"c":7}"""));
        Assert.Equal(3d, ExpressionEvaluator.Compute("min(8, 3, 5)", "{}"));
        Assert.Equal(8d, ExpressionEvaluator.Compute("max(8, 3, 5)", "{}"));
        Assert.Equal(3d, ExpressionEvaluator.Compute("abs(-3)", "{}"));
        Assert.Equal(4d, ExpressionEvaluator.Compute("round(3.6)", "{}"));
        Assert.Equal(3.14d, ExpressionEvaluator.Compute("round(3.14159, 2)", "{}"));
        Assert.Equal(3d, ExpressionEvaluator.Compute("len('abc')", "{}"));
    }

    [Fact]
    public void If_Ternary_Function()
    {
        Assert.Equal("long", ExpressionEvaluator.Compute("if(days > 3, 'long', 'short')", """{"days":5}"""));
        Assert.Equal("short", ExpressionEvaluator.Compute("if(days > 3, 'long', 'short')", """{"days":2}"""));
    }

    [Fact]
    public void StringConcat_WithPlus()
    {
        Assert.Equal("annual-5", ExpressionEvaluator.Compute("type + '-' + days", """{"type":"annual","days":5}"""));
    }

    // ── Compute vs Evaluate ──
    [Fact]
    public void Evaluate_CoercesToBool_Compute_ReturnsRaw()
    {
        Assert.True(ExpressionEvaluator.Evaluate("sum(a, b)", """{"a":1,"b":2}"""));   // 3 → true
        Assert.False(ExpressionEvaluator.Evaluate("sum(a, b)", """{"a":0,"b":0}"""));  // 0 → false
        Assert.Equal(3d, ExpressionEvaluator.Compute("sum(a, b)", """{"a":1,"b":2}"""));
    }

    // ── 安全失败：白名单 + 不 eval + 非法 ──
    [Fact]
    public void UnknownField_SafeFail()
    {
        Assert.False(ExpressionEvaluator.Evaluate("missing > 3", """{"days":5}"""));
        Assert.Null(ExpressionEvaluator.Compute("missing + 1", """{"days":5}"""));
    }

    [Fact]
    public void UnknownFunction_SafeFail()
    {
        Assert.Null(ExpressionEvaluator.Compute("evil('x')", "{}"));
        Assert.False(ExpressionEvaluator.Evaluate("System.IO.File.Delete('x')", "{}"));
    }

    [Fact]
    public void FunctionWrongArity_SafeFail()
    {
        Assert.Null(ExpressionEvaluator.Compute("abs()", "{}"));
        Assert.Null(ExpressionEvaluator.Compute("if(true, 1)", "{}"));   // if 需 3 参
    }

    [Fact]
    public void IllegalOrNonNumericArithmetic_SafeFail()
    {
        Assert.Null(ExpressionEvaluator.Compute("days >> 3 ;; drop", """{"days":5}"""));
        Assert.Null(ExpressionEvaluator.Compute("days * ", """{"days":5}"""));
        Assert.Null(ExpressionEvaluator.Compute("'a' - 'b'", "{}"));   // 字符串做减法 → 安全失败
        Assert.Null(ExpressionEvaluator.Compute("dateDiff('not-a-date','2026-06-10')", "{}"));
    }

    [Fact]
    public void EmptyExpression_Compute_Null_Evaluate_True()
    {
        Assert.Null(ExpressionEvaluator.Compute("", "{}"));
        Assert.Null(ExpressionEvaluator.Compute(null, "{}"));
        Assert.True(ExpressionEvaluator.Evaluate("", "{}"));
    }
}
