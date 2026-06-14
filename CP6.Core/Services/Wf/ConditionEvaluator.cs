using System.Globalization;
using System.Text.Json;

namespace CP6.Core.Services.Wf;

/// <summary>
/// 安全条件求值器（OA 章03 §6 / OA-D6）。手写递归下降，仅支持：
/// 白名单字段（取自 vars）+ 比较(<c>&gt; &lt; &gt;= &lt;= == !=</c>) + 逻辑(<c>&amp;&amp; ||</c>) + 括号 + 数字/字符串/布尔字面量。
/// **绝不 eval 任意代码**（防 schema 注入）；任何解析/求值错误一律安全失败返回 false，不抛异常。
/// 空表达式视为无条件（true），用于无条件流转边。
/// </summary>
public static class ConditionEvaluator
{
    /// <summary>对 varsJson（流程变量 JSON）求值。空表达式=true；任何错误→false。</summary>
    public static bool Evaluate(string? expression, string? varsJson)
        => Evaluate(expression, ParseVars(varsJson));

    public static bool Evaluate(string? expression, IReadOnlyDictionary<string, object?> vars)
    {
        if (string.IsNullOrWhiteSpace(expression)) return true;   // 无条件边
        try
        {
            var parser = new Parser(Tokenize(expression), vars);
            var result = parser.ParseExpr();
            parser.ExpectEnd();
            return ToBool(result);
        }
        catch { return false; }   // 安全失败：不抛、不 eval
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

    private enum TokType { Num, Str, Ident, Bool, And, Or, Gt, Lt, Ge, Le, Eq, Ne, LParen, RParen }
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
                    if (i + 1 < n && s[i + 1] == '=') { tokens.Add(new(TokType.Ne, null)); i += 2; } else throw new FormatException();
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
                    if (char.IsDigit(c) || (c == '-' && i + 1 < n && char.IsDigit(s[i + 1])))
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
    // expr := or ;  or := and ('||' and)* ;  and := comp ('&&' comp)* ;
    // comp := primary (cmpOp primary)? ;  primary := num | str | bool | ident | '(' or ')'

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
            var left = ParsePrimary();
            if (Peek is { } op && op.Type is TokType.Gt or TokType.Lt or TokType.Ge or TokType.Le or TokType.Eq or TokType.Ne)
            {
                _pos++;
                var right = ParsePrimary();
                return Compare(op.Type, left, right);
            }
            return left;
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
    }

    // ───────────────────────── 求值原语 ─────────────────────────

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

    private static bool ToBool(object? v) => v switch
    {
        bool b => b,
        double d => d != 0,
        string s => !string.IsNullOrEmpty(s),
        _ => false,
    };
}
