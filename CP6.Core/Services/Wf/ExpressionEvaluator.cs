using System.Globalization;
using System.Text.Json;

namespace CP6.Core.Services.Wf;

/// <summary>
/// 安全表达式求值器（OA 章06 §5 / OA2-D2 ★）。流程 condition（章03）与表单规则（章06 显隐/计算/联动）
/// 后端复算<b>共用此一份</b>，前端 <c>ruleEngine.ts</c> 同语义实现（语言克制到 C#/TS 两端可一致）。
/// 手写递归下降，支持：白名单字段（取自 vars，未知字段=安全失败）+ 比较 + 逻辑 + 算术(+ - * / %) +
/// 一元(- !) + 括号 + 内置函数(dateDiff/sum/min/max/abs/round/len/if) + 数字/字符串/布尔字面量。
/// <para>
/// 安全铁律：<b>绝不 eval 任意代码</b>（防 schema 注入）；任何词法/语法/求值错误一律安全失败——
/// <see cref="Evaluate"/> 返 false、<see cref="Compute"/> 返 null，绝不抛异常。
/// </para>
/// 运算符优先级（低→高）：<c>|| &amp;&amp; 比较 +/- *///% 一元 primary</c>。
/// </summary>
public static class ExpressionEvaluator
{
    /// <summary>布尔求值（流程条件边/表单 require 复核）。空表达式=true；任何错误→false。</summary>
    public static bool Evaluate(string? expression, string? varsJson)
        => Evaluate(expression, ParseVars(varsJson));

    public static bool Evaluate(string? expression, IReadOnlyDictionary<string, object?> vars)
    {
        if (string.IsNullOrWhiteSpace(expression)) return true;   // 无条件边
        var (ok, val) = TryEval(expression, vars);
        return ok && ToBool(val);
    }

    /// <summary>取值求值（表单 compute 计算回写）。空表达式/任何错误→null（不写回，不抛）。</summary>
    public static object? Compute(string? expression, string? varsJson)
        => Compute(expression, ParseVars(varsJson));

    public static object? Compute(string? expression, IReadOnlyDictionary<string, object?> vars)
    {
        if (string.IsNullOrWhiteSpace(expression)) return null;
        var (ok, val) = TryEval(expression, vars);
        return ok ? val : null;
    }

    private static (bool ok, object? val) TryEval(string expression, IReadOnlyDictionary<string, object?> vars)
    {
        try
        {
            var parser = new Parser(Tokenize(expression), vars);
            var result = parser.ParseExpr();
            parser.ExpectEnd();
            return (true, result);
        }
        catch { return (false, null); }   // 安全失败：不抛、不 eval
    }

    /// <summary>把流程变量 JSON 解析成 字段→值（double/string/bool/null）字典。解析失败→空字典。</summary>
    public static Dictionary<string, object?> ParseVars(string? varsJson)
    {
        var dict = new Dictionary<string, object?>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(varsJson)) return dict;
        try
        {
            using var doc = JsonDocument.Parse(varsJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return dict;
            foreach (var prop in doc.RootElement.EnumerateObject())
                dict[prop.Name] = prop.Value.ValueKind switch
                {
                    JsonValueKind.Number => prop.Value.GetDouble(),
                    JsonValueKind.String => prop.Value.GetString(),
                    JsonValueKind.True => true,
                    JsonValueKind.False => false,
                    _ => null,
                };
        }
        catch { /* 解析失败 → 空 vars（后续未知字段→安全失败）*/ }
        return dict;
    }

    // ───────────────────────── 词法 ─────────────────────────

    private enum TokType
    {
        Num, Str, Ident, Bool,
        And, Or, Not,
        Gt, Lt, Ge, Le, Eq, Ne,
        Plus, Minus, Mul, Div, Mod,
        LParen, RParen, Comma,
    }
    private readonly record struct Token(TokType Type, object? Value);

    private static List<Token> Tokenize(string s)
    {
        var tokens = new List<Token>();
        int i = 0, n = s.Length;
        while (i < n)
        {
            char c = s[i];
            if (char.IsWhiteSpace(c)) { i++; continue; }
            switch (c)
            {
                case '(': tokens.Add(new(TokType.LParen, null)); i++; break;
                case ')': tokens.Add(new(TokType.RParen, null)); i++; break;
                case ',': tokens.Add(new(TokType.Comma, null)); i++; break;
                case '+': tokens.Add(new(TokType.Plus, null)); i++; break;
                case '-': tokens.Add(new(TokType.Minus, null)); i++; break;   // 一元/二元在语法层区分
                case '*': tokens.Add(new(TokType.Mul, null)); i++; break;
                case '/': tokens.Add(new(TokType.Div, null)); i++; break;
                case '%': tokens.Add(new(TokType.Mod, null)); i++; break;
                case '&':
                    if (i + 1 < n && s[i + 1] == '&') { tokens.Add(new(TokType.And, null)); i += 2; } else throw new FormatException();
                    break;
                case '|':
                    if (i + 1 < n && s[i + 1] == '|') { tokens.Add(new(TokType.Or, null)); i += 2; } else throw new FormatException();
                    break;
                case '>':
                    if (i + 1 < n && s[i + 1] == '=') { tokens.Add(new(TokType.Ge, null)); i += 2; } else { tokens.Add(new(TokType.Gt, null)); i++; }
                    break;
                case '<':
                    if (i + 1 < n && s[i + 1] == '=') { tokens.Add(new(TokType.Le, null)); i += 2; } else { tokens.Add(new(TokType.Lt, null)); i++; }
                    break;
                case '=':
                    if (i + 1 < n && s[i + 1] == '=') { tokens.Add(new(TokType.Eq, null)); i += 2; } else throw new FormatException();
                    break;
                case '!':
                    if (i + 1 < n && s[i + 1] == '=') { tokens.Add(new(TokType.Ne, null)); i += 2; } else { tokens.Add(new(TokType.Not, null)); i++; }
                    break;
                case '\'':
                case '"':
                {
                    char quote = c; i++; int start = i;
                    while (i < n && s[i] != quote) i++;
                    if (i >= n) throw new FormatException();   // 未闭合
                    tokens.Add(new(TokType.Str, s.Substring(start, i - start)));
                    i++;   // 跳过闭合引号
                    break;
                }
                default:
                    if (char.IsDigit(c))
                    {
                        int start = i; i++;
                        while (i < n && (char.IsDigit(s[i]) || s[i] == '.')) i++;
                        tokens.Add(new(TokType.Num, double.Parse(s.Substring(start, i - start), CultureInfo.InvariantCulture)));
                    }
                    else if (char.IsLetter(c) || c == '_')
                    {
                        int start = i; i++;
                        while (i < n && (char.IsLetterOrDigit(s[i]) || s[i] == '_' || s[i] == '.')) i++;
                        var ident = s.Substring(start, i - start);
                        tokens.Add(ident switch
                        {
                            "true" => new(TokType.Bool, true),
                            "false" => new(TokType.Bool, false),
                            _ => new(TokType.Ident, ident),
                        });
                    }
                    else throw new FormatException();   // 非法字符 → 安全失败
                    break;
            }
        }
        return tokens;
    }

    // ───────────────────────── 语法（递归下降） ─────────────────────────
    // expr := or
    // or   := and ('||' and)*           and := comp ('&&' comp)*
    // comp := add (cmpOp add)?           add := mul (('+'|'-') mul)*
    // mul  := unary (('*'|'/'|'%') unary)*   unary := ('-'|'!') unary | primary
    // primary := num | str | bool | func | ident | '(' or ')'
    // func := ident '(' (or (',' or)*)? ')'

    private sealed class Parser
    {
        private readonly List<Token> _t;
        private readonly IReadOnlyDictionary<string, object?> _vars;
        private int _pos;

        public Parser(List<Token> t, IReadOnlyDictionary<string, object?> vars) { _t = t; _vars = vars; }

        private Token? Peek => _pos < _t.Count ? _t[_pos] : null;
        public void ExpectEnd() { if (_pos != _t.Count) throw new FormatException(); }

        public object? ParseExpr() => ParseOr();

        private object? ParseOr()
        {
            var left = ParseAnd();
            while (Peek is { Type: TokType.Or }) { _pos++; var right = ParseAnd(); left = ToBool(left) || ToBool(right); }
            return left;
        }

        private object? ParseAnd()
        {
            var left = ParseComp();
            while (Peek is { Type: TokType.And }) { _pos++; var right = ParseComp(); left = ToBool(left) && ToBool(right); }
            return left;
        }

        private object? ParseComp()
        {
            var left = ParseAdd();
            if (Peek is { } op && op.Type is TokType.Gt or TokType.Lt or TokType.Ge or TokType.Le or TokType.Eq or TokType.Ne)
            {
                _pos++;
                var right = ParseAdd();
                return Compare(op.Type, left, right);
            }
            return left;
        }

        private object? ParseAdd()
        {
            var left = ParseMul();
            while (Peek is { } op && op.Type is TokType.Plus or TokType.Minus)
            {
                _pos++;
                var right = ParseMul();
                left = Arith(op.Type, left, right);
            }
            return left;
        }

        private object? ParseMul()
        {
            var left = ParseUnary();
            while (Peek is { } op && op.Type is TokType.Mul or TokType.Div or TokType.Mod)
            {
                _pos++;
                var right = ParseUnary();
                left = Arith(op.Type, left, right);
            }
            return left;
        }

        private object? ParseUnary()
        {
            if (Peek is { Type: TokType.Minus }) { _pos++; return -ToNum(ParseUnary()); }
            if (Peek is { Type: TokType.Not }) { _pos++; return !ToBool(ParseUnary()); }
            return ParsePrimary();
        }

        private object? ParsePrimary()
        {
            var tok = Peek ?? throw new FormatException();
            switch (tok.Type)
            {
                case TokType.Num:
                case TokType.Str:
                case TokType.Bool:
                    _pos++; return tok.Value;
                case TokType.Ident:
                    _pos++;
                    var name = (string)tok.Value!;
                    if (Peek is { Type: TokType.LParen })   // 函数调用
                        return CallFunc(name, ParseArgs());
                    if (!_vars.TryGetValue(name, out var v)) throw new KeyNotFoundException(name);   // 未知字段 → 安全失败
                    return v;
                case TokType.LParen:
                    _pos++;
                    var inner = ParseOr();
                    if (Peek is not { Type: TokType.RParen }) throw new FormatException();
                    _pos++;
                    return inner;
                default: throw new FormatException();
            }
        }

        /// <summary>解析 '(' arg (',' arg)* ')'，调用前 Peek 已在 '('。</summary>
        private List<object?> ParseArgs()
        {
            _pos++;   // 吃 '('
            var args = new List<object?>();
            if (Peek is not { Type: TokType.RParen })
            {
                args.Add(ParseOr());
                while (Peek is { Type: TokType.Comma }) { _pos++; args.Add(ParseOr()); }
            }
            if (Peek is not { Type: TokType.RParen }) throw new FormatException();
            _pos++;   // 吃 ')'
            return args;
        }
    }

    // ───────────────────────── 内置函数（白名单，克制到 C#/TS 一致） ─────────────────────────

    private static object? CallFunc(string name, List<object?> args) => name switch
    {
        "dateDiff" when args.Count == 2 => (ParseDate(args[0]) - ParseDate(args[1])).TotalDays,
        "sum" when args.Count >= 1      => args.Sum(ToNum),
        "min" when args.Count >= 1      => args.Min(ToNum),
        "max" when args.Count >= 1      => args.Max(ToNum),
        "abs" when args.Count == 1      => Math.Abs(ToNum(args[0])),
        "round" when args.Count == 1    => Math.Round(ToNum(args[0]), MidpointRounding.AwayFromZero),
        "round" when args.Count == 2    => Math.Round(ToNum(args[0]), (int)ToNum(args[1]), MidpointRounding.AwayFromZero),
        "len" when args.Count == 1      => (double)ToStr(args[0]).Length,
        "if" when args.Count == 3       => ToBool(args[0]) ? args[1] : args[2],
        _ => throw new InvalidOperationException($"未知函数或参数数错误：{name}/{args.Count}"),   // 安全失败
    };

    /// <summary>解析日期串（'yyyy-MM-dd' 或带时间，取日期部分）。非法→抛（上层安全失败）。</summary>
    private static DateTime ParseDate(object? v)
    {
        var s = ToStr(v);
        if (s.Length >= 10) s = s.Substring(0, 10);
        return DateTime.ParseExact(s, "yyyy-MM-dd", CultureInfo.InvariantCulture);
    }

    // ───────────────────────── 求值原语 ─────────────────────────

    private static object Arith(TokType op, object? l, object? r)
    {
        if (op == TokType.Plus && !(l is double && r is double))
            return ToStr(l) + ToStr(r);   // 任一非数字 → 字符串拼接（与 JS '+' 一致）
        double a = ToNum(l), b = ToNum(r);
        return op switch
        {
            TokType.Plus => a + b,
            TokType.Minus => a - b,
            TokType.Mul => a * b,
            TokType.Div => b == 0 ? throw new DivideByZeroException() : a / b,
            TokType.Mod => b == 0 ? throw new DivideByZeroException() : a % b,
            _ => throw new InvalidOperationException(),
        };
    }

    private static object Compare(TokType op, object? l, object? r) => op switch
    {
        TokType.Eq => ValueEquals(l, r),
        TokType.Ne => !ValueEquals(l, r),
        _ when l is double dl && r is double dr => op switch
        {
            TokType.Gt => dl > dr,
            TokType.Lt => dl < dr,
            TokType.Ge => dl >= dr,
            TokType.Le => dl <= dr,
            _ => false,
        },
        _ => throw new InvalidOperationException("大小比较仅支持数字"),   // > < >= <= 非数字 → 安全失败
    };

    private static bool ValueEquals(object? l, object? r)
    {
        if (l is null || r is null) return l is null && r is null;
        if (l is double dl && r is double dr) return dl.Equals(dr);
        if (l is string sl && r is string sr) return string.Equals(sl, sr, StringComparison.Ordinal);
        if (l is bool bl && r is bool br) return bl == br;
        return false;   // 跨类型一律不等
    }

    private static double ToNum(object? v) => v switch
    {
        double d => d,
        bool b => b ? 1 : 0,
        string s => double.Parse(s, CultureInfo.InvariantCulture),   // 非数字串 → 抛 → 安全失败
        _ => throw new InvalidOperationException("非数值"),
    };

    private static string ToStr(object? v) => v switch
    {
        null => string.Empty,
        string s => s,
        double d => d.ToString(CultureInfo.InvariantCulture),
        bool b => b ? "true" : "false",
        _ => throw new InvalidOperationException("不可字符串化"),
    };

    private static bool ToBool(object? v) => v switch
    {
        bool b => b,
        double d => d != 0,
        string s => !string.IsNullOrEmpty(s),
        _ => false,
    };
}
