using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using CP6.Entity.DomainModels.Pub;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace CP6.Core.Services.Pub;

public sealed record CodeGenPreviewOutput(string FileName, string Language, string Content);
public sealed record CodeGenPreviewResult(string SchemaVersion, string TemplateRef, string TemplateVersion,
    string InputDigest, bool Applied, IReadOnlyList<CodeGenPreviewOutput> Outputs);

/// <summary>Only fixed, bounded messages are exposed to preview callers.</summary>
public sealed class CodeGenPreviewValidationException(string message) : Exception(message);

/// <summary>Keep model-binding/annotation errors within the preview error contract.</summary>
public sealed class CodeGenPreviewRequestAttribute : ActionFilterAttribute
{
    public CodeGenPreviewRequestAttribute() => Order = -3000;
    public override void OnActionExecuting(ActionExecutingContext context)
    {
        if (!context.ModelState.IsValid)
            context.Result = new BadRequestObjectResult(new { code = 400, message = "预览请求格式或字段长度不正确。" });
    }
}

public partial class CodeGenService
{
    public const string PreviewTemplateRef = "cp6.codegen.crud.embedded";
    public const string PreviewTemplateVersion = "1.0.0";
    public const int PreviewMaxColumns = 512;
    public const int PreviewMaxRequestBytes = 1024 * 1024;

    // Names are single Unicode identifiers. Formatting characters, escapes and paths are not identifiers.
    private static readonly Regex Identifier = new(@"\A[\p{L}\p{Nl}_][\p{L}\p{Nl}\p{Nd}\p{Mn}\p{Mc}\p{Pc}]*\z", RegexOptions.CultureInvariant);
    // Contextual C# words (e.g. value/async) and quoted TS property values are legal here.
    private static readonly HashSet<string> Keywords = new((
        "abstract as base bool break byte case catch char checked class const continue decimal default delegate do double else enum event explicit extern false finally fixed float for foreach goto if implicit in int interface internal is lock long namespace new null object operator out override params private protected public readonly ref return sbyte sealed short sizeof stackalloc static string struct switch this throw true try typeof uint ulong unchecked unsafe ushort using virtual void volatile while").Split(' '), StringComparer.Ordinal);
    private static readonly HashSet<string> TemplateNames = new((
        "BaseEntity IDataScoped Guid DateTime DateTimeOffset DateOnly TimeOnly TimeSpan Table TableAttribute CP6 System Microsoft " +
        "BaseCrudService CP6Context IDataScopeFilter IFieldPermService ICurrentPermissionContext ISeqService " +
        "BaseCrudController ApiController ApiControllerAttribute Route RouteAttribute Authorize AuthorizeAttribute " +
        "HttpGet HttpPost HttpPut HttpDelete FieldMask RequirePermission FromBody Task IActionResult").Split(' '), StringComparer.Ordinal);
    private static readonly HashSet<string> InheritedProperties = new(
        ["Id", "Creator", "CreateDate", "Modifier", "ModifyDate", "DeptId"], StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> ScalarTypes = new((
        "string string? bool byte sbyte short ushort int uint long ulong float double decimal char Guid DateTime DateTimeOffset DateOnly TimeOnly TimeSpan " +
        "bool? byte? sbyte? short? ushort? int? uint? long? ulong? float? double? decimal? char? Guid? DateTime? DateTimeOffset? DateOnly? TimeOnly? TimeSpan?").Split(' '), StringComparer.Ordinal);

    /// <summary>Pure preview: no storage, numbering, template loading or application of generated artifacts.</summary>
    public CodeGenPreviewResult GeneratePreview(GenTable t, List<GenColumn> cols)
    {
        var files = Generate(t, cols);
        // Hash exactly the rendering inputs (including stable ordering), not IDs, audit fields or request time.
        // A template change must bump TemplateVersion; its source hash also binds the embedded template bytes.
        var templateDigest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            string.Join("\n", EntityTpl, ServiceTpl, ControllerTpl, ColumnsTpl, ViewTpl)))).ToLowerInvariant();
        var input = JsonSerializer.SerializeToUtf8Bytes(new
        {
            SchemaVersion = "1.0", TemplateRef = PreviewTemplateRef, TemplateVersion = PreviewTemplateVersion, templateDigest,
            t.EntityName, t.Module, t.TableName, t.ResourceKey, t.SeqBizKey, t.CodeField, t.RoutePath,
            Columns = cols.OrderBy(c => c.Sort).Select(c => new { c.Name, c.ClrType, c.Label, c.Required, c.InList, c.InForm, c.Sort })
        });
        return new("1.0", PreviewTemplateRef, PreviewTemplateVersion,
            "sha256:" + Convert.ToHexString(SHA256.HashData(input)).ToLowerInvariant(), false,
            files.Select(f => new CodeGenPreviewOutput(f.Key,
                f.Key.EndsWith(".cs", StringComparison.Ordinal) ? "csharp" : f.Key.EndsWith(".ts", StringComparison.Ordinal) ? "typescript" : "vue", f.Value)).ToArray());
    }

    private static void Validate(GenTable? t, List<GenColumn>? cols)
    {
        if (t is null || cols is null) Fail("预览需要表元数据和列列表。");
        if (cols!.Count > PreviewMaxColumns) Fail("预览列数不能超过 512。");
        Name(t!.EntityName, 80, "实体名");
        Name(t.Module, 40, "模块名");
        if (TemplateNames.Contains(t.EntityName) || TemplateNames.Contains(t.Module)) Fail("实体名或模块名与模板类型冲突。");
        // Windows device names remain unsafe even when an extension is appended.
        if (Regex.IsMatch(t.EntityName, @"\A(CON|PRN|AUX|NUL|COM[1-9¹²³]|LPT[1-9¹²³])\z", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            Fail("实体名不能用作安全文件名。");
        Text(t.TableName, 120, "表名", required: true);
        Text(t.ResourceKey, 80, "权限资源键", required: true);
        Text(t.RoutePath, 160, "路由", required: true);
        Text(t.SeqBizKey, 50, "采番业务键");
        Text(t.MenuName, 80, "菜单名");
        if (!string.IsNullOrEmpty(t.CodeField)) Name(t.CodeField, 80, "编号字段");
        // Routes are relative literal segments, never filesystem paths or user-supplied route templates.
        if (t.RoutePath.StartsWith('/') || t.RoutePath.Split('/').Any(s => string.IsNullOrWhiteSpace(s) || s is "." or "..") ||
            t.RoutePath.IndexOfAny(['\\', '{', '}', '?', '#', '%', ':']) >= 0 || t.RoutePath.Any(char.IsControl))
            Fail("路由必须是安全的相对路径段。");
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var c in cols)
        {
            if (c is null) Fail("列元数据不能为空。");
            Name(c!.Name, 80, "列名");
            if (InheritedProperties.Contains(c.Name) || c.Name == t.EntityName || !names.Add(Camel(c.Name)))
                Fail("列名重复、大小写映射冲突或与模板成员冲突。");
            if (c.ClrType is null || !ScalarTypes.Contains(c.ClrType)) Fail("列类型必须是支持的内置标量类型。");
            Text(c.Label, 80, "列显示名");
        }
    }

    private static void Name(string? value, int limit, string field)
    {
        Text(value, limit, field, required: true);
        if (!Identifier.IsMatch(value!) || Keywords.Contains(value!)) Fail($"{field}必须是非保留字的有效标识符。");
    }

    private static void Text(string? value, int limit, string field, bool required = false)
    {
        if ((required && string.IsNullOrWhiteSpace(value)) || value?.Length > limit)
            Fail($"{field}为空或超过允许长度。");
        // Invalid UTF-16 cannot round-trip through the text preview contract.
        if (value is not null)
            for (var i = 0; i < value.Length; i++)
                if (char.IsSurrogate(value[i]) && !(char.IsHighSurrogate(value[i]) && i + 1 < value.Length && char.IsLowSurrogate(value[++i])))
                    Fail($"{field}包含无效字符。");
    }

    [System.Diagnostics.CodeAnalysis.DoesNotReturn]
    private static void Fail(string message) => throw new CodeGenPreviewValidationException(message);

    private static string CSharp(string value) => JsonSerializer.Serialize(value)[1..^1];

    private static string XmlDoc(string? value)
    {
        var text = new StringBuilder();
        foreach (var ch in value ?? "")
            text.Append(ch switch
            {
                '&' => "&amp;", '<' => "&lt;", '>' => "&gt;", '"' => "&quot;", '\'' => "&apos;",
                '\r' => "&#xD;", '\n' => "&#xA;", '\t' => "&#x9;",
                _ when char.IsControl(ch) || ch is '\u2028' or '\u2029' => $"\\u{(int)ch:x4}",
                _ => ch.ToString()
            });
        return text.ToString();
    }

    private static string TypeScript(string? value)
    {
        var text = new StringBuilder();
        foreach (var ch in value ?? "")
            text.Append(ch switch
            {
                '\\' => "\\\\", '\'' => "\\'", '\r' => "\\r", '\n' => "\\n", '\t' => "\\t",
                // Also safe if column text is later embedded in a script element.
                '<' => "\\u003c", '>' => "\\u003e", '&' => "\\u0026",
                _ when char.IsControl(ch) || ch is '\u2028' or '\u2029' => $"\\u{(int)ch:x4}",
                _ => ch.ToString()
            });
        return text.ToString();
    }
}
