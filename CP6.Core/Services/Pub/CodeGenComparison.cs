using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using CP6.Entity.DomainModels.Pub;

namespace CP6.Core.Services.Pub;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CodeGenSourceFile(string TargetPath, string Content);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CodeGenComparisonRequest(GenTable Table, List<GenColumn> Columns,
    string TemplateRef, string TemplateVersion, IReadOnlyList<CodeGenSourceFile> ExistingFiles,
    IReadOnlyList<string> OverwritePaths);

// Lines include their original terminators. Replacing this one interval reconstructs the exact
// candidate, including CRLF, lone CR, and a final line without a newline. Linear time and memory.
public sealed record CodeGenDifference(int StartLine, IReadOnlyList<string> RemovedLines, IReadOnlyList<string> AddedLines);
public sealed record CodeGenComparisonOutput(string FileName, string TargetPath, string Language,
    string Status, string? Reason, int PreservedCustomBlocks, string? Content, CodeGenDifference? Difference);
public sealed record CodeGenComparisonResult(string SchemaVersion, string TemplateRef, string TemplateVersion,
    string InputDigest, string ComparisonDigest, bool Applied, IReadOnlyList<CodeGenComparisonOutput> Outputs);

public partial class CodeGenService
{
    /// <summary>Compare caller-supplied snapshots only. Paths are hints, never filesystem access.</summary>
    public CodeGenComparisonResult Compare(CodeGenComparisonRequest? request)
    {
        if (request is null || request.ExistingFiles is null || request.OverwritePaths is null)
            Fail("比较需要元数据、原文件列表和覆盖白名单。");
        if (request.TemplateRef != PreviewTemplateRef || request.TemplateVersion != PreviewTemplateVersion)
            Fail("模板或版本不可用，请重新生成预览。");
        if (request.ExistingFiles.Count > 5 || request.OverwritePaths.Count > 5)
            Fail("比较只接受当前模板的五种产物。");
        var preview = GeneratePreview(request.Table, request.Columns);
        var module = request.Table.Module;
        if (Regex.IsMatch(module, @"\A(CON|PRN|AUX|NUL|COM[1-9¹²³]|LPT[1-9¹²³])\z", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            Fail("模块名不能用作安全目录名。");
        // This mapping owns the fixed template's path policy. Callers cannot select a root or
        // append arbitrary segments; exact matching also excludes Windows/Unicode path aliases.
        var entity = request.Table.EntityName;
        var paths = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [$"{entity}.cs"] = $"CP6.Entity/DomainModels/{module}/{entity}.cs",
            [$"{entity}Service.cs"] = $"CP6.Core/Services/{module}/{entity}Service.cs",
            [$"{entity}Controller.cs"] = $"CP6.WebApi/Controllers/{module}/{entity}Controller.cs",
            [$"{Camel(entity)}.ts"] = $"cp6.web/src/types/{module}/{Camel(entity)}.ts",
            [$"{entity}View.vue"] = $"cp6.web/src/views/{module}/{entity}View.vue"
        };
        var allowedPaths = paths.Values.ToHashSet(StringComparer.Ordinal);
        var originals = new Dictionary<string, string>(StringComparer.Ordinal);
        var bytes = 0;
        foreach (var file in request.ExistingFiles)
        {
            if (file is null || file.TargetPath is null || !allowedPaths.Contains(file.TargetPath))
                Fail("原文件路径必须精确匹配当前模板的建议路径。");
            if (file.Content is null) Fail("原文件文本不能为空值；空文件请提供空文本。");
            Text(file.Content, PreviewMaxRequestBytes, "原文件文本");
            bytes += Encoding.UTF8.GetByteCount(file.Content);
            if (bytes > PreviewMaxRequestBytes) Fail("原文件文本总量不能超过 1 MiB。");
            if (!originals.TryAdd(file.TargetPath, file.Content)) Fail("同一路径不能提供多份原文件。");
        }
        var overwrite = new HashSet<string>(StringComparer.Ordinal);
        foreach (var path in request.OverwritePaths)
            if (path is null || !originals.ContainsKey(path) || !overwrite.Add(path))
                Fail("覆盖白名单必须唯一且对应已提供的原文件路径。");

        var outputs = new List<CodeGenComparisonOutput>();
        foreach (var file in preview.Outputs)
        {
            var path = paths[file.FileName];
            if (!originals.TryGetValue(path, out var old))
            {
                outputs.Add(new(file.FileName, path, file.Language, "NoBaseline", null, 0, file.Content, null));
                continue;
            }
            if (!TryPreserve(old, file.Content, out var candidate, out var count))
            {
                outputs.Add(new(file.FileName, path, file.Language, "CustomBlockConflict",
                    "自定义区标记损坏或数量不匹配，无法安全保留。", 0, null, null));
                continue;
            }
            var status = old == candidate ? "Unchanged" : overwrite.Contains(path) ? "OverwriteAllowed" : "OverwriteBlocked";
            outputs.Add(new(file.FileName, path, file.Language, status,
                status == "OverwriteBlocked" ? "此文件尚未加入覆盖白名单。" : null, count, candidate, Difference(old, candidate)));
        }
        // This digest additionally binds the external snapshots and explicit overwrite selection.
        // The prior preview input digest continues to describe metadata/template inputs only.
        var input = JsonSerializer.SerializeToUtf8Bytes(new
        {
            SchemaVersion = "codegen-comparison-v1", preview.InputDigest,
            ExistingFiles = originals.OrderBy(x => x.Key, StringComparer.Ordinal).Select(x => new { TargetPath = x.Key, Content = x.Value }),
            OverwritePaths = overwrite.OrderBy(x => x, StringComparer.Ordinal)
        });
        return new("codegen-comparison-v1", preview.TemplateRef, preview.TemplateVersion, preview.InputDigest,
            "sha256:" + Convert.ToHexString(SHA256.HashData(input)).ToLowerInvariant(), false, outputs);
    }

    private static bool TryPreserve(string old, string generated, out string candidate, out int count)
    {
        candidate = generated; count = 0;
        if (!TryBlocks(old, out var prior) || !TryBlocks(generated, out var next)) return false;
        if (prior.Count == 0) return true;
        if (prior.Count != next.Count) return false;
        var result = new StringBuilder(); var offset = 0;
        for (var i = 0; i < prior.Count; i++)
        {
            result.Append(generated, offset, next[i].Start - offset);
            result.Append(old, prior[i].Start, prior[i].End - prior[i].Start);
            offset = next[i].End;
            // A closing marker at the old EOF has no terminator. Keep it a complete comment
            // line when new framework text follows, rather than commenting out that text.
            if (offset < generated.Length && old[prior[i].End - 1] is not ('\r' or '\n')) result.Append('\n');
        }
        result.Append(generated, offset, generated.Length - offset);
        candidate = result.ToString(); count = prior.Count;
        return true;
    }

    private static bool TryBlocks(string text, out List<(int Start, int End)> blocks)
    {
        blocks = []; int offset = 0, start = -1;
        foreach (var line in Lines(text))
        {
            var marker = line.Trim();
            if (marker == "// <custom>")
            {
                if (start >= 0) return false;
                start = offset;
            }
            else if (marker == "// </custom>")
            {
                if (start < 0) return false;
                blocks.Add((start, offset + line.Length)); start = -1;
            }
            offset += line.Length;
        }
        return start < 0;
    }

    private static CodeGenDifference Difference(string old, string candidate)
    {
        var before = Lines(old); var after = Lines(candidate); var prefix = 0;
        while (prefix < before.Count && prefix < after.Count && before[prefix] == after[prefix]) prefix++;
        var oldEnd = before.Count; var newEnd = after.Count;
        while (oldEnd > prefix && newEnd > prefix && before[oldEnd - 1] == after[newEnd - 1]) { oldEnd--; newEnd--; }
        return new(prefix + 1, before.GetRange(prefix, oldEnd - prefix), after.GetRange(prefix, newEnd - prefix));
    }

    private static List<string> Lines(string text)
    {
        var lines = new List<string>(); var start = 0;
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] is not ('\r' or '\n')) continue;
            if (text[i] == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
            lines.Add(text[start..(i + 1)]); start = i + 1;
        }
        if (start < text.Length) lines.Add(text[start..]);
        return lines;
    }
}
