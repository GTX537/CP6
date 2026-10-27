using System.Globalization;
using System.Numerics;
using System.Text.Json;
using CP6.Core.Services.Sys;
namespace CP6.Core.Services.Oa;

// OA-APP binding conditions: same bounded grammar as the existing authoring validator,
// exact rational arithmetic and explicit errors. A failed rule must never choose fallback.
internal static class ApprovalExpressionV2
{
    internal static bool Evaluate(string expression, JsonElement snapshot, string[] fields)
    {
        try
        {
            if(expression.Length>4000 || snapshot.ValueKind!=JsonValueKind.Object)throw new FormatException();
            var vars=new Dictionary<string,object?>(StringComparer.Ordinal);
            foreach(var field in fields)
            {
                var value=snapshot;
                foreach(var part in field.Split('.'))
                {
                    if(value.ValueKind!=JsonValueKind.Object||!value.TryGetProperty(part,out value)){value=default;break;}
                }
                if(value.ValueKind!=JsonValueKind.Undefined)vars.Add(field,value.ValueKind switch {
                    JsonValueKind.Number=>Number.Parse(value.GetRawText()),JsonValueKind.String=>value.GetString(),
                    JsonValueKind.True=>true,JsonValueKind.False=>false,JsonValueKind.Null=>null,_=>value});
            }
            var parser=new Parser(Tokenize(expression),vars);var result=parser.ParseExpr();parser.ExpectEnd();
            return result is bool b?b:throw new FormatException();
        }
        catch(Exception e)when(e is FormatException or KeyNotFoundException or ArgumentException or InvalidOperationException or ArithmeticException)
        {throw new ScopeAuthorizationExceptionV1("SNAPSHOT_SCHEMA_INVALID",422,e);}
    }
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
                        tokens.Add(new(TokType.Num, Number.Parse(s.Substring(start, i - start))));
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
        if(tokens.Count>512)throw new FormatException();
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
        private readonly bool _validateOnly;

        public Parser(List<Token> t, IReadOnlyDictionary<string, object?> vars, bool validateOnly = false) { _t = t; _vars = vars; _validateOnly = validateOnly; }

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
                return _validateOnly ? true : Compare(op.Type, left, right);
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
                left = _validateOnly ? Number.One : Arith(op.Type, left, right);
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
                left = _validateOnly ? Number.One : Arith(op.Type, left, right);
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
                    {
                        var args = ParseArgs();
                        if (!_validateOnly) return CallFunc(name, args);
                        var valid = name switch { "dateDiff" => args.Count == 2, "sum" or "min" or "max" => args.Count >= 1,
                            "abs" or "len" => args.Count == 1, "round" => args.Count is 1 or 2, "if" => args.Count == 3, _ => false };
                        if (!valid) throw new FormatException(); return Number.One;
                    }
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

    private static object? CallFunc(string name,List<object?> args)=>name switch {
        "dateDiff" when args.Count==2=>new Number((ParseDate(args[0])-ParseDate(args[1])).Ticks,TimeSpan.TicksPerDay),
        "sum" when args.Count>=1=>args.Select(ToNum).Aggregate(Number.Zero,(a,b)=>a+b),
        "min" when args.Count>=1=>args.Select(ToNum).Min(),"max" when args.Count>=1=>args.Select(ToNum).Max(),
        "abs" when args.Count==1=>ToNum(args[0]).Abs(),
        "round" when args.Count is 1 or 2=>ToNum(args[0]).Round(args.Count==1?0:ToNum(args[1]).Integer()),
        "len" when args.Count==1=>new Number(ToStr(args[0]).Length,1),
        "if" when args.Count==3=>ToBool(args[0])?args[1]:args[2],_=>throw new FormatException()};
    private static DateTime ParseDate(object? value)
    {var s=ToStr(value);return DateTime.ParseExact(s.Length>=10?s[..10]:s,"yyyy-MM-dd",CultureInfo.InvariantCulture);}
    private static object Arith(TokType op,object? l,object? r)
    {
        if(op==TokType.Plus && !(l is Number&&r is Number))return ToStr(l)+ToStr(r);
        var a=ToNum(l);var b=ToNum(r);return op switch {
            TokType.Plus=>a+b,TokType.Minus=>a-b,TokType.Mul=>a*b,TokType.Div=>a/b,TokType.Mod=>a%b,_=>throw new FormatException()};
    }
    private static object Compare(TokType op,object? l,object? r)
    {
        if(op is TokType.Eq or TokType.Ne){var same=l switch{Number a when r is Number b=>a.CompareTo(b)==0,string a when r is string b=>a==b,bool a when r is bool b=>a==b,null=>r==null,_=>false};return op==TokType.Eq?same:!same;}
        // Registered business decimal strings stay lossless; an explicit numeric operand makes
        // numeric comparison intentional. Two arbitrary strings are not ordered as quantities.
        if(l is not Number && r is not Number)throw new FormatException();
        var cmp=ToNum(l).CompareTo(ToNum(r));return op switch{TokType.Gt=>cmp>0,TokType.Lt=>cmp<0,TokType.Ge=>cmp>=0,TokType.Le=>cmp<=0,_=>throw new FormatException()};
    }
    private static Number ToNum(object? v)=>v switch{Number n=>n,bool b=>b?Number.One:Number.Zero,string s=>Number.Parse(s),_=>throw new FormatException()};
    private static string ToStr(object? v)=>v switch{null=>"",string s=>s,Number n=>n.Text(),bool b=>b?"true":"false",_=>throw new FormatException()};
    private static bool ToBool(object? v)=>v switch{bool b=>b,Number n=>n.CompareTo(Number.Zero)!=0,string s=>s.Length!=0,_=>false};
    private readonly record struct Number : IComparable<Number>
    {
        private readonly BigInteger numerator,denominator;
        internal static Number Zero=>new(0,1);internal static Number One=>new(1,1);
        internal Number(BigInteger n,BigInteger d){if(d==0)throw new DivideByZeroException();if(d<0){n=-n;d=-d;}var gcd=BigInteger.GreatestCommonDivisor(n,d);numerator=n/gcd;denominator=d/gcd;}
        internal static Number Parse(string s)
        {
            var match=System.Text.RegularExpressions.Regex.Match(s,@"\A([+-]?)([0-9]+)(?:\.([0-9]+))?(?:[eE]([+-]?[0-9]+))?\z");
            if(!match.Success)throw new FormatException();
            var fraction=match.Groups[3].Value;var exponent=match.Groups[4].Success?int.Parse(match.Groups[4].Value,CultureInfo.InvariantCulture):0;
            if(Math.Abs((long)exponent)>10000)throw new FormatException();
            var n=BigInteger.Parse(match.Groups[2].Value+fraction,CultureInfo.InvariantCulture)*(match.Groups[1].Value=="-"?-1:1);
            var scale=fraction.Length-exponent;return scale>=0?new(n,BigInteger.Pow(10,scale)):new(n*BigInteger.Pow(10,-scale),1);
        }
        public int CompareTo(Number b)=>(numerator*b.denominator).CompareTo(b.numerator*denominator);
        public static Number operator +(Number a,Number b)=>new(a.numerator*b.denominator+b.numerator*a.denominator,a.denominator*b.denominator);
        public static Number operator -(Number a,Number b)=>a+-b;public static Number operator -(Number a)=>new(-a.numerator,a.denominator);
        public static Number operator *(Number a,Number b)=>new(a.numerator*b.numerator,a.denominator*b.denominator);
        public static Number operator /(Number a,Number b)=>new(a.numerator*b.denominator,a.denominator*b.numerator);
        public static Number operator %(Number a,Number b){var q=a/b;return a-new Number(q.numerator/q.denominator,1)*b;}
        internal Number Abs()=>new(BigInteger.Abs(numerator),denominator);
        internal int Integer()=>denominator==1&&numerator>=int.MinValue&&numerator<=int.MaxValue?(int)numerator:throw new FormatException();
        internal Number Round(int scale)
        {if(scale<0||scale>1000)throw new FormatException();var factor=BigInteger.Pow(10,scale);var n=numerator*factor;var q=BigInteger.DivRem(n,denominator,out var rem);if(BigInteger.Abs(rem)*2>=denominator)q+=n.Sign;return new(q,factor);}
        internal string Text()
        {
            if(denominator==1)return numerator.ToString(CultureInfo.InvariantCulture);
            // Concatenation cannot silently round a non-terminating rational.
            var d=denominator;var twos=0;var fives=0;while(d%2==0){d/=2;twos++;}while(d%5==0){d/=5;fives++;}if(d!=1)throw new FormatException();
            var scale=Math.Max(twos,fives);var n=BigInteger.Abs(numerator)*BigInteger.Pow(2,scale-twos)*BigInteger.Pow(5,scale-fives);
            var digits=n.ToString(CultureInfo.InvariantCulture).PadLeft(scale+1,'0');return (numerator.Sign<0?"-":"")+digits[..^scale]+"."+digits[^scale..];
        }
    }
}
